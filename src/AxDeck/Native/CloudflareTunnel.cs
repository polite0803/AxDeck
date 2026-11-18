using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using AxDeck.Core;

namespace AxDeck.Native;

// Manages the Cloudflare tunnel infrastructure configured on this machine without Python: the local
// cloudflared.exe, the SSH config and its ProxyCommand, the tunnel entries, and the remote docker
// container "cloudflared". The logic mirrors the standalone Python tool it replaces; every child
// process has a timeout and runs with no window, and no credentials or tokens are stored here.
// Nothing here names this machine's own hosts or ports: they come from ServicesConfig (services.json in
// the data folder), so the repository and the published build carry none of them.
// All side effects (launching cloudflared / ssh / tasklist, probing the edge) go through small seams
// (RunnerOverride, HttpGetOverride) so the pure decisions can be unit-tested without a live network.
internal static class CloudflareTunnel
{
    public const string AppName = "Cloudflare Tunnel Manager";
    public const string AppVersion = "2.2.1";

    public static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static readonly string SshDir = Path.Combine(Home, ".ssh");
    public static readonly string SshConfig = Path.Combine(SshDir, "config");
    public static readonly string ProxyBat = Path.Combine(SshDir, "cloudflared-ssh.bat");
    public static readonly string Cloudflared = @"C:\Windows\System32\cloudflared.exe";
    // The docker container the remote actions address. Not installed by AxDeck; it only names a container the user runs.
    public static readonly string RemoteContainer = "cloudflared";
    public static readonly string AccessLogDir = Path.Combine(Path.GetTempPath(), "ctm-access");

    public static readonly string SshExe = FindSsh();

    // The config the page works from. ServicesPanel assigns it whenever it reloads services.json.
    public static ServicesConfig Config { get; set; } = new();

    public static string TunnelHostname => Config.Tunnel.Hostname;
    public static string DefaultSshHost => Config.Tunnel.DefaultSshHost.Length > 0
        ? Config.Tunnel.DefaultSshHost
        : Config.Tunnel.SshHosts.FirstOrDefault() ?? "";
    public static IReadOnlyList<string> SshHosts => Config.Tunnel.SshHosts;
    public static IReadOnlyList<int> LocalPorts => Config.Tunnel.Ports;

    public enum SiteKind { Http, Tcp, Rdp, Ssh }

    // One tunnel entry. kind: http=浏览器直开；tcp/rdp=需 cloudflared access 本地转发；ssh=走 ssh config 的 ProxyCommand。
    public readonly record struct Site(string Host, SiteKind Kind, string Label, string Backend, string? Path, string? Proto, int? Port, string? Client);

    // The configured entries, in file order. A kind this build does not know was already dropped by ServicesConfig.Normalized.
    public static IReadOnlyList<Site> Sites => [.. Config.Tunnel.Sites.Select(site => new Site(
        site.Host, KindOf(site.Kind), site.Label, site.Backend, site.Path, site.Proto, site.Port, site.Client))];

    public static SiteKind KindOf(string kind) => kind switch
    {
        TunnelSiteKind.Tcp => SiteKind.Tcp,
        TunnelSiteKind.Rdp => SiteKind.Rdp,
        TunnelSiteKind.Ssh => SiteKind.Ssh,
        _ => SiteKind.Http,
    };

    // ----------------------------------------------------------------------
    // Path discovery (mirrors find_ssh / find_cloudflared)
    // ----------------------------------------------------------------------

    // Fixed on Windows: Git Bash's ssh rewrites the ProxyCommand's backslashes and then fails to find the executable.
    private static string FindSsh()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var candidate = @"C:\Windows\System32\OpenSSH\ssh.exe";
            if (File.Exists(candidate)) return candidate;
        }
        return Which("ssh") ?? "ssh";
    }

    public static string? FindCloudflared()
    {
        if (File.Exists(Cloudflared)) return Cloudflared;
        return Which("cloudflared");
    }

    // shutil.which: the first directory on PATH holding the executable (with a Windows extension if any).
    private static string? Which(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var exts = windows ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.COM;.BAT;.CMD").Split(';') : [""];
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    // ----------------------------------------------------------------------
    // Process execution (mirrors run_cmd). Side effects go through RunnerOverride
    // so tests can inject results without launching anything.
    // ----------------------------------------------------------------------

    public readonly record struct RunResult(int ReturnCode, string Stdout, string Stderr);

    // When set, RunCommand delegates here instead of starting a process. Tests assign a single function.
    internal static Func<IReadOnlyList<string>, double, RunResult>? RunnerOverride = null;

    public static RunResult RunCommand(IReadOnlyList<string> args, double timeoutSeconds = 15)
        => RunnerOverride is { } runner ? runner(args, timeoutSeconds) : RunCommandReal(args, timeoutSeconds);

    private static RunResult RunCommandReal(IReadOnlyList<string> args, double timeoutSeconds)
    {
        if (args.Count == 0) return new(-2, "", "空的命令行");
        var start = new ProcessStartInfo
        {
            FileName = args[0],
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        for (int i = 1; i < args.Count; i++) start.ArgumentList.Add(args[i]);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        try
        {
            using var proc = new Process { StartInfo = start };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            if (!proc.Start()) return new(-2, "", $"无法启动：{args[0]}");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            if (!proc.WaitForExit((int)(timeoutSeconds * 1000)))
            {
                try { proc.Kill(); } catch { }
                try { proc.WaitForExit(); } catch { }
                return new(-1, stdout.ToString().Trim(), $"命令超时（{timeoutSeconds:0}s）");
            }
            return new(proc.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return new(-2, "", $"找不到可执行文件：{args[0]}");
        }
        catch (Exception ex)
        {
            return new(-3, "", $"{ex.GetType().Name}：{ex.Message}");
        }
    }

    // ----------------------------------------------------------------------
    // Cloudflared presence / version / local processes
    // ----------------------------------------------------------------------

    public static string CloudflaredVersion()
    {
        var exe = FindCloudflared();
        if (exe is null) return "未找到 cloudflared.exe";
        var (rc, output, err) = RunCommand([exe, "--version"], 8);
        var text = string.IsNullOrEmpty(output) ? err : output;
        var first = text.Split('\n').FirstOrDefault()?.Trim() ?? "";
        return rc == 0 && first.Length > 0 ? first : $"读取失败（rc={rc}）{(err.Length > 80 ? err[..80] : err)}";
    }

    public static int LocalCloudflaredProcesses()
    {
        var (rc, output, _) = RunCommand(["tasklist", "/FI", "IMAGENAME eq cloudflared.exe", "/FO", "CSV", "/NH"], 8);
        if (rc != 0 || string.IsNullOrWhiteSpace(output)) return 0;
        int count = 0;
        foreach (var line in output.Split('\n'))
            if (line.ToLowerInvariant().Contains("cloudflared.exe")) count++;
        return count;
    }

    // ----------------------------------------------------------------------
    // Port / reachability probing (mirrors port_open / public_tcp_reachable)
    // ----------------------------------------------------------------------

    public static bool PortOpen(int port, string host = "127.0.0.1", double timeoutSeconds = 0.35)
    {
        using var client = new TcpClient();
        try
        {
            var ar = client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds))) return false;
            client.EndConnect(ar);
            return true;
        }
        catch (Exception) { return false; }
    }

    public static bool PublicTcpReachable(double timeoutSeconds = 5)
    {
        // No tunnel host configured: there is nothing to reach, and connecting to an empty name would just throw.
        if (string.IsNullOrWhiteSpace(TunnelHostname)) return false;
        using var client = new TcpClient();
        try
        {
            var ar = client.BeginConnect(TunnelHostname, 443, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds))) return false;
            client.EndConnect(ar);
            return true;
        }
        catch (Exception) { return false; }
    }

    // ----------------------------------------------------------------------
    // SSH config inspection (mirrors ssh_config_hosts / proxy_command_ok)
    // ----------------------------------------------------------------------

    public static List<string> SshConfigHosts()
    {
        try
        {
            var text = File.ReadAllText(SshConfig, Encoding.UTF8);
            var hosts = new List<string>();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("host ", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1) hosts.Add(parts[1]);
            }
            return hosts;
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public static bool ProxyCommandOk()
    {
        string text;
        try { text = File.ReadAllText(SshConfig, Encoding.UTF8); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        var has = text.Contains("cloudflared-ssh.bat") || text.Contains("cloudflared.exe");
        return has && File.Exists(ProxyBat);
    }

    // ----------------------------------------------------------------------
    // Cloudflare edge probe (mirrors edge_status). Side effects go through
    // HttpGetOverride so the 1033 classification can be tested offline.
    // ----------------------------------------------------------------------

    public readonly record struct EdgeResult(string Detail, bool Online);
    public readonly record struct EdgeHttpResponse(string? Body, int? StatusCode, bool IsHttpError, string? ExceptionType, string? ExceptionMessage);
    public delegate EdgeHttpResponse EdgeHttpGet(string url);

    internal static Func<string, EdgeHttpResponse>? HttpGetOverride = null;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    // Pure per-attempt decision: null Online means "retry on transport error", otherwise a firm verdict.
    internal static (string? Detail, bool? Online) DecideEdge(EdgeHttpResponse resp)
    {
        if (resp.ExceptionMessage is null)
        {
            if (resp.Body != null && resp.Body.Contains("1033"))
                return ("error 1033：远端 cloudflared 连接器离线", false);
            if (resp.IsHttpError)
            {
                if ((resp.Body != null && resp.Body.Contains("1033")) || resp.StatusCode == 530)
                    return ("error 1033：远端 cloudflared 连接器离线", false);
                return ($"在线 HTTP {resp.StatusCode}（非 1033）", true);
            }
            return ($"在线 HTTP {resp.StatusCode}", true);
        }
        return (null, null);
    }

    public static EdgeResult EdgeStatus(string? hostname = null, int attempts = 2)
    {
        hostname ??= TunnelHostname;
        var get = HttpGetOverride ?? EdgeHttpGetReal;
        string last = "";
        for (int i = 0; i < Math.Max(1, attempts); i++)
        {
            var resp = get("https://" + hostname + "/");
            var decision = DecideEdge(resp);
            if (decision.Online is { } online) return new(decision.Detail!, online);
            var reason = resp.ExceptionMessage!;
            if (reason.Contains("handshake", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("TLS", StringComparison.OrdinalIgnoreCase))
                reason = "TLS 握手超时（边缘偶发）";
            last = $"{resp.ExceptionType}：{reason}";
        }
        return new($"探测失败：{last}", false);
    }

    private static EdgeHttpResponse EdgeHttpGetReal(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("curl/8.0");
            using var response = Client.Send(request);
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Length > 512) body = body[..512];
            return new(body, (int)response.StatusCode, !response.IsSuccessStatusCode, null, null);
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode is { } code) return new(null, (int)code, true, null, null);
            return new(null, null, false, ex.GetType().Name, ex.Message);
        }
        catch (TaskCanceledException) { return new(null, null, false, "TaskCanceledException", "连接超时"); }
        catch (Exception ex) { return new(null, null, false, ex.GetType().Name, ex.Message); }
    }

    // Turns Windows OpenSSH's misleading errors into an actionable conclusion (mirrors explain_failure).
    public static string ExplainFailure(string host, string detail)
    {
        var d = detail.ToLowerInvariant();
        if (d.Contains("unknown port -1") || d.Contains("getpeername") || d.Contains("kex_exchange_identification"))
        {
            // A host other than the configured default is reached through a local forwarder, which must be running first.
            if (DefaultSshHost.Length > 0 && !string.Equals(host, DefaultSshHost, StringComparison.OrdinalIgnoreCase))
            {
                // The port comes from services.json: the site entry for that host if it has one, and otherwise the last
                // port listed there, which is where the hand-written forwarders listen.
                int port = Sites.Where(site => string.Equals(site.Host, host, StringComparison.OrdinalIgnoreCase))
                    .Select(site => site.Port ?? 0).FirstOrDefault(value => value > 0);
                if (port == 0 && LocalPorts.Count > 0) port = LocalPorts[^1];
                var where = port > 0 ? $"本地 127.0.0.1:{port}" : "本地转发器";
                return $"{where} 没有 cloudflared access tcp 转发器监听；Windows OpenSSH 会伪装成 banner exchange / UNKNOWN port -1。"
                    + $"改用 Host「{DefaultSshHost}」即可（ProxyCommand 直连）。";
            }
            return "底层连接被重置，检查目标端口是否真的有服务在监听。";
        }
        if (d.Contains("websocket: bad handshake") || d.Contains("1033"))
            return "Cloudflare 边缘拒绝：远端 cloudflared 连接器离线（error 1033）。";
        if (d.Contains("permission denied"))
            return "公钥认证失败，检查 ~/.ssh/config 为该 Host 指定的 IdentityFile 与远端的 authorized_keys。";
        if (d.Contains("timed out") || d.Contains("timeout"))
            return "连接超时，检查网络与隧道连接器状态。";
        return "";
    }

    // ----------------------------------------------------------------------
    // SSH login test and remote docker container ops (mirrors ssh_login_test /
    // remote_container_status|logs|restart). All go through RunCommand.
    // ----------------------------------------------------------------------

    public static (bool Ok, string Detail, int ReturnCode) SshLoginTest(string host, double timeoutSeconds = 18)
    {
        var (rc, output, err) = RunCommand(
            [SshExe, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=5", host, "echo TUNNEL_OK"],
            timeoutSeconds);
        var ok = rc == 0 && output.Contains("TUNNEL_OK");
        return (ok, output.Length > 0 ? output : (err.Length > 0 ? err : "(无输出)"), rc);
    }

    public static RunResult RemoteContainerStatus(string host)
        => RunCommand([SshExe, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host,
            $"docker ps -a --filter name={RemoteContainer} --format '{{{{.Names}}}} | {{{{.Status}}}} | {{{{.Image}}}}'"], 25);

    public static RunResult RemoteContainerLogs(string host, int lines = 25)
        => RunCommand([SshExe, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host,
            $"docker logs --tail {lines} {RemoteContainer} 2>&1"], 25);

    public static RunResult RemoteContainerRestart(string host)
        => RunCommand([SshExe, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host,
            $"docker restart {RemoteContainer}"], 40);

    // ----------------------------------------------------------------------
    // Site helpers (mirrors site_by_host / client_command / site_table)
    // ----------------------------------------------------------------------

    public static Site? SiteByHost(string host)
    {
        foreach (var site in Sites) if (site.Host == host) return site;
        return null;
    }

    // How a client connects. TCP hostnames are two-step: start the local forwarder, then connect to 127.0.0.1.
    public static string ClientCommand(Site site)
    {
        if (site.Kind == SiteKind.Http) return "https://" + site.Host + (site.Path ?? "/");
        if (site.Kind == SiteKind.Ssh) return "ssh " + site.Host;
        // A tcp/rdp entry without a port in services.json has nowhere to listen; say that instead of printing a bare colon.
        if (site.Port is not { } port) return $"# services.json 里 {site.Host} 缺 port 与 proto，无法生成本地转发命令";
        return $"cloudflared access {site.Proto ?? "tcp"} --hostname {site.Host} --url 127.0.0.1:{port}\n# 然后：{site.Client}";
    }

    public readonly record struct SiteInfo(string Host, SiteKind Kind, string Label, string Backend, int? Port);

    public static List<SiteInfo> SiteTable()
        => Sites.Select(s => new SiteInfo(s.Host, s.Kind, s.Label, s.Backend, s.Port)).ToList();

    // ----------------------------------------------------------------------
    // Local TCP access forwarders (mirrors start/stop_forwarder). Each runs as
    // a detached, windowless cloudflared process; the panel tracks them here.
    // ----------------------------------------------------------------------

    private sealed record ForwarderEntry(Process Process, FileStream? Log);
    private static readonly ConcurrentDictionary<string, ForwarderEntry> Forwarders = new();

    public static bool ForwarderRunning(string host)
        => Forwarders.TryGetValue(host, out var entry) && !entry.Process.HasExited;

    public static (bool Ok, string Detail) StartForwarder(Site site)
    {
        var exe = FindCloudflared();
        if (exe is null) return (false, "未找到 cloudflared.exe");
        var host = site.Host;
        if (ForwarderRunning(host)) return (true, $"已在运行（127.0.0.1:{site.Port}）");
        if (site.Port is null || site.Proto is null) return (false, "该站点不是 TCP/RDP 转发类型");
        try
        {
            Directory.CreateDirectory(AccessLogDir);
            var logPath = Path.Combine(AccessLogDir, host + ".log");
            var log = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            var start = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            start.ArgumentList.Add("access");
            start.ArgumentList.Add(site.Proto);
            start.ArgumentList.Add("--hostname");
            start.ArgumentList.Add(host);
            start.ArgumentList.Add("--url");
            start.ArgumentList.Add($"127.0.0.1:{site.Port}");
            var proc = new Process { StartInfo = start };
            proc.Start();
            Forwarders[host] = new(proc, log);
            return (true, $"已启动 pid={proc.Id} → 127.0.0.1:{site.Port}，日志 {logPath}");
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}：{ex.Message}");
        }
    }

    public static (bool Ok, string Detail) StopForwarder(string host)
    {
        if (!Forwarders.TryRemove(host, out var entry))
            return (false, $"没有本程序启动的 {host} 转发进程");
        try
        {
            if (!entry.Process.HasExited)
            {
                entry.Process.Kill();
                entry.Process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}：{ex.Message}");
        }
        finally
        {
            try { entry.Log?.Dispose(); } catch { }
            try { entry.Process.Dispose(); } catch { }
        }
        return (true, $"已停止 pid={entry.Process.Id}");
    }

    public static List<(string Host, bool Ok, string Detail)> StopAllForwarders()
    {
        var results = new List<(string, bool, string)>();
        foreach (var host in Forwarders.Keys.ToList())
        {
            var (ok, detail) = StopForwarder(host);
            results.Add((host, ok, detail));
        }
        return results;
    }

    // ----------------------------------------------------------------------
    // Site probe and full status (mirrors probe_sites / collect_status)
    // ----------------------------------------------------------------------

    public readonly record struct SiteProbe(string Host, SiteKind Kind, string Label, string Backend, bool Ok, string Level, string Detail);

    public static List<SiteProbe> ProbeSites()
    {
        var outp = new List<SiteProbe>();
        foreach (var site in Sites)
        {
            string detail; bool ok; string level;
            if (site.Kind == SiteKind.Http)
            {
                var edge = EdgeStatus(site.Host);
                ok = edge.Online; detail = edge.Detail; level = ok ? "ok" : "bad";
            }
            else if (site.Kind == SiteKind.Ssh)
            {
                ok = ProxyCommandOk();
                detail = ok ? "ProxyCommand 正常 ✓" : $"ProxyCommand 异常 ✗（检查 {ProxyBat}）";
                level = ok ? "ok" : "bad";
            }
            else if (site.Port is not { } localPort) // tcp / rdp without a port in services.json
            {
                ok = false; level = "bad";
                detail = "services.json 里这一项缺 port，无法本地转发";
            }
            else
            {
                var up = PortOpen(localPort);
                var edge = EdgeStatus(site.Host, attempts: 1);
                var running = ForwarderRunning(site.Host);
                if (!up)
                {
                    ok = false; level = "warn";
                    detail = $"127.0.0.1:{localPort} 未监听（转发进程{(running ? "在跑" : "未启动")}）；边缘 {edge.Detail}";
                }
                else
                {
                    ok = edge.Online; level = ok ? "ok" : "bad";
                    detail = $"127.0.0.1:{localPort} 监听中；边缘 {edge.Detail}";
                }
            }
            // Only the forwarded kinds (tcp / rdp) can be "warn"; http and ssh always get a firm verdict.
            if (site.Kind is not (SiteKind.Tcp or SiteKind.Rdp)) level = ok ? "ok" : "bad";
            outp.Add(new(site.Host, site.Kind, site.Label, site.Backend, ok, level, detail));
        }
        return outp;
    }

    public readonly record struct StatusSnapshot(
        string? CloudflaredPath, string CloudflaredVersion, int LocalProcesses,
        bool PublicTcp443, EdgeResult Edge, IReadOnlyDictionary<string, bool> Ports,
        string SshConfig, bool SshConfigExists, List<string> SshHosts,
        bool ProxyCommandOk, string ProxyBat, bool ProxyBatExists, List<SiteInfo> Sites);

    // Everything cheap and side-effect free: run on open and on Refresh.
    public static StatusSnapshot CollectStatus()
    {
        var exe = FindCloudflared();
        var ports = new Dictionary<string, bool>();
        foreach (var port in LocalPorts) ports[port.ToString()] = PortOpen(port);
        return new(
            exe, CloudflaredVersion(), LocalCloudflaredProcesses(),
            PublicTcpReachable(), EdgeStatus(), ports,
            SshConfig, File.Exists(SshConfig), SshConfigHosts(),
            ProxyCommandOk(), ProxyBat, File.Exists(ProxyBat), SiteTable());
    }

    // The "is the basic infra present" verdict, for the page header and diagnostics.
    public static bool CoreOk(StatusSnapshot snapshot)
        => snapshot.CloudflaredPath != null && snapshot.SshConfigExists && snapshot.ProxyBatExists && snapshot.ProxyCommandOk;
}
