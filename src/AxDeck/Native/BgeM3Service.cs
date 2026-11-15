using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace AxDeck.Native;

// The BGE-M3 embedding server, ported from the standalone "BGE-M3 控制面板" (a PyInstaller/Tkinter app)
// into native C#. The old panel launched the WinGet build of llama-server.exe with the local bge-m3 gguf
// model in --embeddings mode on port 8091, polled /health and /slots every two seconds and showed the slot
// occupancy, the request count and the processed prompt tokens. Everything here is plain C#: llama-server.exe
// is the only child process, started with no window and with its output written to a log file so a failed
// start can be read back. No Python and no panel .exe is involved.
internal sealed class BgeM3Service
{
    // ----------------------------------------------------------------------
    // Constants (mirrors the old panel's module level)
    // ----------------------------------------------------------------------

    public const string ModelPath = @"E:\llama-models\bge-m3.Q5_K_M.gguf";
    public const string Host = "0.0.0.0";
    public const int Port = 8091;
    public const string BaseUrl = "http://localhost:8091";
    public const string HealthPath = "/health";
    public const string SlotsPath = "/slots";
    public const string EmbeddingsApi = "http://localhost:8091/v1/embeddings";
    public const string ModelLabel = "Q5_K_M";
    public const int EmbeddingDimensions = 1024;
    public const string ServerFileName = "llama-server.exe";

    // The versioned WinGet folder the old panel hard-coded. The version suffix changes with every llama.cpp
    // release, so the candidates are also globbed below.
    private const string WingetPackage = "ggml.llamacpp_Microsoft.Winget.Source_8wekyb3d8bbwe";

    // llama-server's stdout/stderr land here instead of being discarded, so a failed start leaves a trace.
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AxDeck", "logs", "bge-m3.log");

    private static readonly HttpClient Client = CreateClient();
    private static readonly object LogLock = new();
    private static readonly Lazy<IReadOnlyList<string>> Candidates = new(BuildServerCandidates, isThreadSafe: true);

    private static HttpClient CreateClient()
    {
        // The machine runs an HTTP proxy for outbound traffic, and .NET picks it up from the environment. A server
        // on this machine must not be reached through it: the proxy forwards the absolute request line, llama.cpp
        // then sees a path it does not know and answers 404. Going straight to the server is also what the old
        // Python panel did, since urllib does not read the proxy environment.
        var handler = new HttpClientHandler { UseProxy = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
    }

    // When set, the HTTP reads delegate here instead of touching the network. Tests assign a single function.
    internal static Func<string, string?>? HttpGetOverride = null;

    private Process? _process;
    private int[]? _lastTasks;
    private long _totalTokens;
    private int _totalRequests;

    /// <summary>One poll of the server: the service state plus the slot statistics.</summary>
    public readonly record struct Snapshot(
        bool ServerFound,
        bool ModelFound,
        bool Running,
        bool Loading,
        int BusySlots,
        int IdleSlots,
        int TotalSlots,
        long TotalTokens,
        int TotalRequests,
        string Detail);

    /// <summary>The slot statistics parsed out of /slots.</summary>
    public readonly record struct Slots(int Total, int Busy, int Idle, long Tokens, int[] Tasks, int MaxTaskId);

    // ----------------------------------------------------------------------
    // Path discovery (mirrors the old panel's LLAMA_SERVER constant)
    // ----------------------------------------------------------------------

    /// <summary>Every place llama-server.exe is looked for, most specific first.</summary>
    public static IReadOnlyList<string> ServerCandidates() => Candidates.Value;

    /// <summary>The resolved llama-server.exe path, or null when llama.cpp is not installed.</summary>
    public static string? ResolveServerPath() => ServerCandidates().FirstOrDefault(File.Exists);

    private static IReadOnlyList<string> BuildServerCandidates()
    {
        var list = new List<string>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        list.Add(Path.Combine(local, "Microsoft", "WinGet", "Packages", WingetPackage, ServerFileName));
        list.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", ServerFileName));
        var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var dir in Directory.EnumerateDirectories(packages, "ggml.llamacpp_*").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                list.Add(Path.Combine(dir, ServerFileName));
        }
        list.Add(Path.Combine(programFiles, "llama.cpp", "bin", ServerFileName));
        list.Add(Path.Combine(programFiles, "llama.cpp", ServerFileName));
        return list;
    }

    // ----------------------------------------------------------------------
    // Pure helpers (unit tested)
    // ----------------------------------------------------------------------

    /// <summary>The llama-server command line: model, bind address, port and embeddings mode.</summary>
    public static IReadOnlyList<string> BuildArguments(string modelPath, string host, int port) =>
        ["-m", modelPath, "--host", host, "--port", port.ToString(CultureInfo.InvariantCulture), "--embeddings"];

    /// <summary>True when a /health body reports the server as ready.</summary>
    public static bool ParseHealth(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    /// <summary>The slot statistics of a /slots body: busy slots, idle slots, prompt tokens and task ids.</summary>
    public static Slots ParseSlots(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return new(0, 0, 0, 0, [], -1);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return new(0, 0, 0, 0, [], -1);
        int busy = 0, total = 0, maxTaskId = -1;
        long tokens = 0;
        var tasks = new List<int>();
        foreach (var slot in doc.RootElement.EnumerateArray())
        {
            total++;
            if (Truthy(slot, "is_processing")) busy++;
            tokens += Number(slot, "n_prompt_tokens_processed");
            tasks.Add((int)Number(slot, "id_task"));
            // llama.cpp only writes the task id on the slots that ran a task, so the highest one seen is the
            // number of tasks the server has served (the first task is number 0).
            if (slot.TryGetProperty("id_task", out var task) && task.ValueKind == JsonValueKind.Number && task.TryGetInt32(out var id))
                maxTaskId = Math.Max(maxTaskId, id);
        }
        return new(total, busy, total - busy, tokens, [.. tasks], maxTaskId);
    }

    /// <summary>True when the server answers that it is still loading the model.</summary>
    public static bool IsLoading(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && message.GetString() is { } text
                && text.Contains("Loading model", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    private static bool Truthy(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetDouble(out var n) && n != 0,
            JsonValueKind.String => value.GetString() is { Length: > 0 } text && text != "0" && !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static long Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out var n) ? n : 0,
            JsonValueKind.String => long.TryParse(value.GetString(), out var n) ? n : 0,
            _ => 0,
        };
    }

    /// <summary>The token counter as the old panel showed it: plain below a thousand, "1.2k" above.</summary>
    public static string FormatTokens(long tokens) =>
        tokens >= 1000 ? ((double)tokens / 1000).ToString("0.0", CultureInfo.InvariantCulture) + "k" : tokens.ToString(CultureInfo.InvariantCulture);

    // ----------------------------------------------------------------------
    // Process control (mirrors start / stop)
    // ----------------------------------------------------------------------

    /// <summary>Starts llama-server with the BGE-M3 model and waits for it to report healthy.</summary>
    public (bool Ok, string Detail) Start()
    {
        var server = ResolveServerPath();
        if (server is null) return (false, "llama-server.exe not found");
        if (!File.Exists(ModelPath)) return (false, $"model missing: {ModelPath}");
        if (IsHealthy(out _)) return (false, "already running");

        var start = new ProcessStartInfo
        {
            FileName = server,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in BuildArguments(ModelPath, Host, Port)) start.ArgumentList.Add(arg);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var proc = new Process { StartInfo = start, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => AppendLog(e.Data);
            proc.ErrorDataReceived += (_, e) => AppendLog(e.Data);
            if (!proc.Start()) return (false, "llama-server.exe did not start");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            _process = proc;
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }

        // The model is ~450 MiB: loading it takes a moment, so poll the health endpoint instead of sleeping blindly.
        for (int i = 0; i < 60; i++)
        {
            if (_process.HasExited) return (false, $"llama-server exited with code {_process.ExitCode}");
            if (IsHealthy(out _)) return (true, "started");
            Thread.Sleep(500);
        }
        // The model did not come up in time: the server is left stopped rather than half running.
        Terminate(_process);
        return (false, "timed out waiting for /health");
    }

    /// <summary>Stops the server: the process AxDeck started, or the one listening on the port.</summary>
    public (bool Ok, string Detail) Stop()
    {
        if (_process is { HasExited: false })
        {
            return (Terminate(_process), "stopped");
        }
        // Started outside AxDeck: fall back to the llama-server processes that come from the same install.
        var server = ResolveServerPath();
        if (server is null) return (false, "llama-server.exe not found");
        var killed = 0;
        foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ServerFileName)))
        {
            try
            {
                if (!string.Equals(proc.MainModule?.FileName, server, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsHealthy(out _)) continue;
                proc.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception) { }
            finally { proc.Dispose(); }
        }
        _process = null;
        return killed > 0 ? (true, $"stopped {killed} process(es)") : (false, "no running server found");
    }

    private static bool Terminate(Process proc)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(5000);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>True when a llama-server process this class started is still alive.</summary>
    public bool OwnsRunningProcess => _process is { HasExited: false };

    // ----------------------------------------------------------------------
    // Status polling (mirrors the old monitor thread)
    // ----------------------------------------------------------------------

    /// <summary>Polls /health and then /slots once, the way the old panel's monitor loop did.</summary>
    public Snapshot Poll()
    {
        bool serverFound = ResolveServerPath() is not null;
        bool modelFound = File.Exists(ModelPath);
        if (!IsHealthy(out var detail))
        {
            bool loading = detail == "loading";
            return new(serverFound, modelFound, false, loading, 0, 0, 0, _totalTokens, _totalRequests, detail);
        }
        int busy = 0, idle = 0, total = 0;
        detail = "ok";
        if (TryRead(SlotsPath, out var body))
        {
            var slots = ParseSlots(body);
            // The old panel ignored an empty slot list, keeping the previous counters.
            if (slots.Total > 0)
            {
                busy = slots.Busy; idle = slots.Idle; total = slots.Total;
                // The task id rises with every request, so the highest one is the number of requests served. The old
                // panel counted a change of the task id vector instead, which misses the first request.
                if (slots.MaxTaskId >= 0) _totalRequests = Math.Max(_totalRequests, slots.MaxTaskId + 1);
                else if (_lastTasks is not null && !_lastTasks.SequenceEqual(slots.Tasks)) _totalRequests++;
                _lastTasks = slots.Tasks;
                if (slots.Tokens > _totalTokens) _totalTokens = slots.Tokens;
                detail = busy > 0 ? "busy" : "idle";
            }
        }
        return new(serverFound, modelFound, true, false, busy, idle, total, _totalTokens, _totalRequests, detail);
    }

    /// <summary>True when the server answers /health with status ok.</summary>
    public static bool IsHealthy(out string detail)
    {
        if (!TryRead(HealthPath, out var body))
        {
            detail = "no response";
            return false;
        }
        if (ParseHealth(body))
        {
            detail = "ok";
            return true;
        }
        detail = IsLoading(body) ? "loading" : "not ready";
        return false;
    }

    /// <summary>The raw body of one of the server's endpoints, for diagnostics.</summary>
    public static string? ReadRaw(string path) => TryRead(path, out var body) ? body : null;

    private static bool TryRead(string path, out string body)
    {
        body = "";
        try
        {
            var get = HttpGetOverride ?? (url =>
            {
                using var response = Client.GetAsync(url).GetAwaiter().GetResult();
                // The body is kept even for an error status: while the model loads, /health answers with a 503
                // and a "Loading model" message, which is worth telling apart from a server that is not there.
                var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            });
            var text = get(BaseUrl + path);
            if (text is null) return false;
            body = text;
            return true;
        }
        catch (Exception) { return false; }
    }

    // ----------------------------------------------------------------------
    // Log
    // ----------------------------------------------------------------------

    private static void AppendLog(string? line)
    {
        if (line is null) return;
        try
        {
            lock (LogLock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (Exception) { }
    }

    /// <summary>The last lines of the server log, for the diagnostics view.</summary>
    public static IReadOnlyList<string> ReadLogTail(int lines = 20)
    {
        try
        {
            if (!File.Exists(LogPath)) return [];
            return File.ReadAllLines(LogPath).TakeLast(lines).ToArray();
        }
        catch (Exception) { return []; }
    }
}
