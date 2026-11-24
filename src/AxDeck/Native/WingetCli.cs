using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using AxDeck.Core;

namespace AxDeck.Native;

// winget.exe 的进程封装。只做「怎么调」，不判断输出内容——表格解析与版本比较都在 AxDeck.Core。
// 这里全部是实测过的调用形式，改参数前先看 SoftwareUpdates 的测试与文档：带 --source 会 exit=22，
// 带 --accept-source-agreements 会因 msstore 源不可达整条失败（exit=226）。
internal static class WingetCli
{
    // 一次调用的结果。TimedOut 单独标出来，避免把「超时被杀」当成普通失败去解析输出。
    public readonly record struct RunResult(int ExitCode, string Stdout, string Stderr, bool TimedOut)
    {
        public string Combined => (Stdout + "\n" + Stderr).Trim();
    }

    // 测试缝：WindowsTests 注入假实现，避免真的启动 winget。
    internal static Func<IReadOnlyList<string>, int, RunResult>? RunnerOverride = null;

    // 首次查询要拉源，实测 1～2 分钟，240 秒留足余量。
    public const int QueryTimeoutSeconds = 240;

    // 升级要下载并运行安装包，给得更宽。
    private const int UpgradeTimeoutSeconds = 900;

    // 唯一可用的查询形式：不带 accept 参数、不带 --source、不要 --output json（不存在这个参数）。
    private static readonly string[] QueryArguments = ["upgrade", "--include-unknown", "--disable-interactivity"];

    // 定位顺序：显式环境变量 → PATH → WindowsApps 下的固定位置（PATH 里常常没有它）。
    public static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable("AXDECK_WINGET");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        try
        {
            foreach (var folder in PathFolders())
            {
                var candidate = Path.Combine(folder, "winget.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (ArgumentException)
        {
            // PATH 里有非法字符。继续走固定位置。
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
        {
            var candidate = Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // 全机可用更新的表格（原始文本，未解析）。
    public static RunResult Query() => Run(QueryArguments, QueryTimeoutSeconds);

    // 一次完整查询，转成 Core 能读的结论。没装 winget 与命令失败是两种空态：前者提示装 App Installer，
    // 后者如实给出原因，都不假装「没有更新」。
    public static WingetProbe Probe()
    {
        if (Locate() is null) return new WingetProbe(null, MissingTool: true);
        var result = Query();
        var scan = WingetTable.Parse(result.Stdout);
        if (result.ExitCode == 0) return new WingetProbe(scan);
        string reason = result.TimedOut
            ? Loc.T("检查超时，可重试。", "The check timed out. Try again.")
            : FirstLine(result.Stderr) ?? FirstLine(result.Stdout) ?? Loc.T("winget 未能完成检查。", "winget could not finish the check.");
        return new WingetProbe(scan, Failure: reason);
    }

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return null;
    }

    // 升级一个包。winget 自己有锁，调用方必须逐个串行。
    // 首选不带任何 accept 参数；只有 winget 明确要求接受包协议时才补一个参数重试一次。
    public static RunResult Upgrade(string id)
    {
        var arguments = new List<string>
        {
            "upgrade", "--id", id, "--exact", "--silent", "--disable-interactivity",
        };

        var result = Run([.. arguments], UpgradeTimeoutSeconds);
        if (!NeedsAgreement(result)) return result;

        arguments.Add("--accept-package-agreements");
        return Run([.. arguments], UpgradeTimeoutSeconds);
    }

    // winget 要求接受协议时输出里会出现这些字样。源协议绝不代接受（msstore 不可达会让整条命令失败）。
    private static bool NeedsAgreement(RunResult result)
    {
        if (result.TimedOut || result.ExitCode == 0) return false;
        var text = result.Combined;
        return text.Contains("协议", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("agreement", StringComparison.OrdinalIgnoreCase);
    }

    private static RunResult Run(IReadOnlyList<string> arguments, int timeoutSeconds)
    {
        if (RunnerOverride is { } runner) return runner(arguments, timeoutSeconds);

        if (Locate() is not { } executable) return new RunResult(-2, "", "winget.exe not found", false);

        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            // winget 输出 UTF-8。不显式指定会按控制台代码页解码，中文表头直接变乱码。
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        try
        {
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            if (!process.Start()) return new RunResult(-3, "", "winget could not be started", false);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
                return new RunResult(-1, stdout.ToString(), stderr.ToString(), true);
            }

            // 带超时的 WaitForExit 不保证异步读取已排空，再等一次无参版本。
            process.WaitForExit();
            return new RunResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new RunResult(-3, stdout.ToString(), $"{ex.GetType().Name}: {ex.Message}", false);
        }
    }

    private static string[] PathFolders()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return [];
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
