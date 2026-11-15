using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AxDeck.Native;

// The AxTV emulator manager, ported from the standalone Python/Tkinter app (axtv_manager.py) into native C#.
// The old app drove the Android SDK's emulator.exe and adb.exe: it started the emulator with a hidden console,
// closed it gracefully through "adb emu kill", installed the AxTV package, took screenshots and put the emulator's
// window into a small or a borderless full screen shape. Everything here is plain C#: the Android SDK's own
// executables are the only child processes, and the window is driven through Win32 (see EmulatorWindow).
//
// Two things are done differently from the old app, both because it had been written against a fixed setup:
// the AVDs are read from the machine instead of being two hard coded names, and the physical panel of the
// selected AVD is read from its own config.ini, so an AVD that is not one of the two known ones still gets the
// right window shape.
internal static class AxTvEmulator
{
    // ----------------------------------------------------------------------
    // Constants (mirrors the old app's module level)
    // ----------------------------------------------------------------------

    /// <summary>The package the AxTV app is published under.</summary>
    public const string Package = "com.github.axtv.dv";

    /// <summary>The activity that shows the app.</summary>
    public const string Activity = Package + "/.MainActivity";

    /// <summary>The guest screen size the portrait mode uses.</summary>
    public const string PortraitSize = "1080x1920";

    /// <summary>The default AVD, used when the machine has none that the user picked.</summary>
    public const string DefaultAvd = "axtv_tv";

    /// <summary>A picture smaller than this is treated as a blank screen.</summary>
    public const long HealthyFrameBytes = 20000;

    private const int StartfUseShowWindow = 0x00000001;
    private const int StartfUseStdHandles = 0x00000100;
    private const uint CreateNewConsole = 0x00000010;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const int SwHide = 0;

    /// <summary>The emulator's own log, written where AxDeck keeps its other logs.</summary>
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AxDeck", "logs", "axtv-emulator.log");

    /// <summary>Where the screenshots are saved.</summary>
    public static readonly string ShotDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AxDeck", "AxTV");

    /// <summary>The Android SDK the emulator and adb come from.</summary>
    public static string SdkRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");

    /// <summary>adb.exe, the bridge to the running device.</summary>
    public static string AdbPath => Path.Combine(SdkRoot, "platform-tools", "adb.exe");

    /// <summary>emulator.exe, the virtual device itself.</summary>
    public static string EmulatorPath => Path.Combine(SdkRoot, "emulator", "emulator.exe");

    /// <summary>The folder the AVD definitions live in.</summary>
    public static string AvdHome => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");

    // ----------------------------------------------------------------------
    // Types
    // ----------------------------------------------------------------------

    /// <summary>The result of running one of the SDK's command line tools.</summary>
    public readonly record struct RunResult(int ReturnCode, string Stdout, string Stderr)
    {
        /// <summary>Everything the tool printed, in the order the old app read it.</summary>
        public string Combined => (Stdout + "\n" + Stderr).Trim();
    }

    /// <summary>An AVD and the shape of its physical panel.</summary>
    public readonly record struct AvdProfile(string Name, string Label, string Panel, bool Portrait, double Aspect);

    /// <summary>How an installation ended.</summary>
    public enum InstallOutcome { Success, SignatureMismatch, AbiMismatch, Failed }

    /// <summary>What an installation did and what the tool said about it.</summary>
    public readonly record struct InstallResult(InstallOutcome Outcome, string Detail);

    /// <summary>What the emulator is doing right now.</summary>
    public readonly record struct StatusSnapshot(
        bool SdkFound,
        bool AdbFound,
        bool EmulatorFound,
        bool DeviceOnline,
        bool ProcessAlive,
        string AvdName,
        string AvdLabel,
        string Panel,
        bool Portrait,
        string WindowMode,
        string WindowRect,
        string Orientation,
        string AppVersion,
        bool BootCompleted,
        string Detail);

    // The AVDs the old app knew about, with the panel it described. The panel of an AVD that is not one of
    // these is read from the AVD's own configuration.
    private static readonly (string Name, string Label)[] KnownAvds = [("axtv_tv", "横屏 TV"), ("axtv_portrait", "竖屏 TV")];

    // When set, the command line calls delegate here instead of starting a process. Tests assign a single function.
    internal static Func<IReadOnlyList<string>, int, RunResult>? RunnerOverride = null;

    // ----------------------------------------------------------------------
    // Running the SDK's tools
    // ----------------------------------------------------------------------

    /// <summary>Runs one of the SDK's tools and collects what it printed.</summary>
    public static RunResult Run(IReadOnlyList<string> args, int timeoutSeconds = 120)
        => RunnerOverride is { } runner ? runner(args, timeoutSeconds) : RunReal(args, timeoutSeconds);

    private static RunResult RunReal(IReadOnlyList<string> args, int timeoutSeconds)
    {
        if (args.Count == 0) return new(-2, "", "empty command line");
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
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            if (!proc.WaitForExit(timeoutSeconds * 1000))
            {
                try { proc.Kill(entireProcessTree: true); } catch (Exception) { }
                return new(-1, stdout.ToString(), stderr + "\ntimed out");
            }
            proc.WaitForExit();
            return new(proc.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            return new(-3, stdout.ToString(), $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Runs adb with the given arguments, aiming them at the running emulator when it is known.</summary>
    public static RunResult Adb(IReadOnlyList<string> args, int timeoutSeconds = 120)
    {
        var list = new List<string> { AdbPath };
        string? serial = EmulatorSerial();
        if (serial is not null) { list.Add("-s"); list.Add(serial); }
        list.AddRange(args);
        return Run(list, timeoutSeconds);
    }

    // ----------------------------------------------------------------------
    // The AVDs on this machine
    // ----------------------------------------------------------------------

    /// <summary>The names of the AVDs the machine has, read from the AVD folder.</summary>
    public static IReadOnlyList<string> ListAvds()
    {
        try
        {
            if (!Directory.Exists(AvdHome)) return [];
            return Directory.EnumerateFiles(AvdHome, "*.ini")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception) { return []; }
    }

    /// <summary>
    /// The AVD to use: the one that was picked when it exists, otherwise the first one the machine has, otherwise
    /// the default name so the message can name what is missing.
    /// </summary>
    public static string ResolveAvd(string? preferred)
    {
        var avds = ListAvds();
        if (preferred is { Length: > 0 } && avds.Contains(preferred, StringComparer.OrdinalIgnoreCase)) return preferred;
        return avds.FirstOrDefault() ?? DefaultAvd;
    }

    /// <summary>
    /// The shape of an AVD's physical panel. The panel is read from the AVD's own configuration, because the
    /// window's proportions are fixed by the panel the emulator was created with and not by the name it has.
    /// </summary>
    public static AvdProfile ProfileFor(string avdName)
    {
        string label = KnownAvds.FirstOrDefault(pair => string.Equals(pair.Name, avdName, StringComparison.OrdinalIgnoreCase)).Label
            ?? avdName;
        int width = 0, height = 0;
        try
        {
            string config = Path.Combine(AvdHome, avdName + ".avd", "config.ini");
            if (File.Exists(config))
            {
                foreach (var line in File.ReadLines(config))
                {
                    if (line.StartsWith("hw.lcd.width=", StringComparison.OrdinalIgnoreCase)) width = ParseInt(line, 16);
                    else if (line.StartsWith("hw.lcd.height=", StringComparison.OrdinalIgnoreCase)) height = ParseInt(line, 16);
                }
            }
        }
        catch (Exception) { }
        if (width <= 0 || height <= 0)
        {
            // The configuration is gone or unreadable: fall back to what the old app knew about its two AVDs.
            return avdName.Contains("portrait", StringComparison.OrdinalIgnoreCase)
                ? new(avdName, label, "1080×1920", true, 9.0 / 16.0)
                : new(avdName, label, "1920×1080", false, 16.0 / 9.0);
        }
        bool portrait = height > width;
        return new(avdName, label, $"{width}×{height}", portrait, (double)width / height);
    }

    private static int ParseInt(string line, int from)
    {
        int cut = line.IndexOf('=', from);
        return cut >= 0 && int.TryParse(line[(cut + 1)..].Trim(), out int value) ? value : 0;
    }

    /// <summary>The emulator's command line for an AVD: a cold start, with the picture left to the machine.</summary>
    public static IReadOnlyList<string> BuildArguments(string avdName) =>
        ["-avd", avdName, "-no-snapshot-load", "-gpu", "auto", "-no-boot-anim"];

    // ----------------------------------------------------------------------
    // The device
    // ----------------------------------------------------------------------

    /// <summary>The serial of the running emulator, or null when there is none.</summary>
    public static string? EmulatorSerial()
    {
        var result = Run([AdbPath, "devices"], 20);
        foreach (var device in ParseDevices(result.Stdout))
            if (device.Serial.StartsWith("emulator-", StringComparison.Ordinal)) return device.Serial;
        return null;
    }

    /// <summary>The devices adb reports, as serial and state.</summary>
    public static IReadOnlyList<(string Serial, string State)> ParseDevices(string output)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(output)) return list;
        foreach (var line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("*", StringComparison.Ordinal)) continue;      // the daemon's start message
            var parts = trimmed.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) list.Add((parts[0], parts[1]));
        }
        return list;
    }

    /// <summary>
    /// True when the emulator is reachable. The adb daemon needs a moment to start the first time, so an empty
    /// answer is retried before it is taken for an answer.
    /// </summary>
    public static bool DeviceOnline()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var result = Run([AdbPath, "devices"], 20);
            foreach (var device in ParseDevices(result.Stdout))
                if (device.Serial.StartsWith("emulator-", StringComparison.Ordinal) && device.State == "device") return true;
            if (attempt < 2) Thread.Sleep(400);
        }
        return false;
    }

    /// <summary>True when an emulator process is running, whatever started it.</summary>
    public static bool EmulatorProcessAlive()
    {
        try
        {
            var result = Run(["tasklist", "/FI", "IMAGENAME eq emulator.exe", "/NH"], 20);
            return result.Stdout.Contains("emulator.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    // ----------------------------------------------------------------------
    // Starting and stopping
    // ----------------------------------------------------------------------

    /// <summary>
    /// Starts the emulator with a console of its own that is hidden from the first moment. The emulator is a
    /// console program and the virtual machine it starts has to sit on a console: taking the parent's console
    /// away with a hidden window makes the virtual machine open a visible terminal of its own, and closing that
    /// terminal would stop the emulator. The output goes to the log file, so nothing depends on the console.
    /// </summary>
    public static (bool Ok, string Detail) Start(string avdName)
    {
        if (!File.Exists(EmulatorPath)) return (false, $"emulator.exe not found: {EmulatorPath}");
        if (DeviceOnline()) return (false, "the emulator is already running");
        if (!ListAvds().Contains(avdName, StringComparer.OrdinalIgnoreCase)) return (false, $"no such AVD: {avdName}");

        var (ok, detail, _) = SpawnHidden(EmulatorPath, BuildArguments(avdName), LogPath);
        return (ok, ok ? $"started {avdName}" : detail);
    }

    /// <summary>
    /// Starts a console program with a console of its own that is hidden from the first moment, and writes what
    /// it prints to a log file. The program keeps the console, so what it starts later stays on it too, and the
    /// window is hidden as it is created rather than hidden afterwards.
    /// </summary>
    public static (bool Ok, string Detail, int ProcessId) SpawnHidden(string executable, IReadOnlyList<string> arguments, string logPath)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); }
        catch (Exception ex) { return (false, ex.Message, 0); }

        string command = Quote(executable) + " " + string.Join(" ", arguments.Select(Quote));
        IntPtr nul = IntPtr.Zero, logFile = IntPtr.Zero, commandPtr = IntPtr.Zero, appPtr = IntPtr.Zero, attributesPtr = IntPtr.Zero;
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                Descriptor = IntPtr.Zero,
                InheritHandle = true,
            };
            attributesPtr = Marshal.AllocHGlobal(attributes.Length);
            Marshal.StructureToPtr(attributes, attributesPtr, false);

            nul = CreateFileW("NUL", 0x80000000, 0x00000003, attributesPtr, 3, 0x00000080, IntPtr.Zero);
            logFile = CreateFileW(logPath, 0x40000000, 0x00000003, attributesPtr, 2, 0x00000080, IntPtr.Zero);
            if (logFile == new IntPtr(-1)) return (false, "could not open the log file", 0);

            var info = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
            info.Flags = StartfUseStdHandles | StartfUseShowWindow;
            info.ShowWindow = (short)SwHide;
            info.StdInput = nul;
            info.StdOutput = logFile;
            info.StdError = logFile;

            commandPtr = Marshal.StringToHGlobalUni(command);
            appPtr = Marshal.StringToHGlobalUni(executable);
            string? directory = Path.GetDirectoryName(executable);
            IntPtr dirPtr = directory is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(directory);
            try
            {
                if (!CreateProcessW(appPtr, commandPtr, IntPtr.Zero, IntPtr.Zero, true,
                        CreateNewConsole | CreateNewProcessGroup, IntPtr.Zero, dirPtr, ref info, out var process))
                    return (false, $"the program did not start: {new Win32Exception(Marshal.GetLastWin32Error()).Message}", 0);
                CloseHandle(process.Thread);
                CloseHandle(process.Process);
                return (true, "started", process.ProcessId);
            }
            finally { if (dirPtr != IntPtr.Zero) Marshal.FreeHGlobal(dirPtr); }
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}", 0);
        }
        finally
        {
            if (nul != IntPtr.Zero) CloseHandle(nul);
            if (logFile != IntPtr.Zero && logFile != new IntPtr(-1)) CloseHandle(logFile);
            if (commandPtr != IntPtr.Zero) Marshal.FreeHGlobal(commandPtr);
            if (appPtr != IntPtr.Zero) Marshal.FreeHGlobal(appPtr);
            if (attributesPtr != IntPtr.Zero) Marshal.FreeHGlobal(attributesPtr);
        }
    }

    /// <summary>
    /// Closes the emulator the way the emulator itself wants to be closed, so it can save its state. The process
    /// can outlive the connection by a few seconds while it writes the snapshot, so the wait is generous.
    /// </summary>
    public static (bool Ok, string Detail) Stop(int waitSeconds = 30)
    {
        if (!DeviceOnline() && !EmulatorProcessAlive()) return (false, "the emulator is not running");
        var result = Adb(["emu", "kill"], 30);
        for (int i = 0; i < waitSeconds; i++)
        {
            Thread.Sleep(1000);
            if (!DeviceOnline() && !EmulatorProcessAlive()) return (true, result.Combined.Length > 0 ? result.Combined : "closed");
        }
        return (false, $"the emulator did not close within {waitSeconds} seconds");
    }

    /// <summary>Ends the emulator processes outright, the way the old app's "force end" button did.</summary>
    public static (bool Ok, string Detail) ForceKill()
    {
        var lines = new List<string>();
        foreach (string name in new[] { "emulator.exe", "qemu-system-x86_64.exe" })
        {
            var result = Run(["taskkill", "/F", "/IM", name, "/T"], 25);
            if (result.Combined.Length > 0) lines.Add(result.Combined);
        }
        Thread.Sleep(1000);
        bool alive = EmulatorProcessAlive();
        return (!alive, alive ? "processes are still running" : string.Join("\n", lines));
    }

    // ----------------------------------------------------------------------
    // The app on the device
    // ----------------------------------------------------------------------

    /// <summary>What an installation ended with, read from what adb printed.</summary>
    public static InstallResult ClassifyInstall(string output)
    {
        if (output.Contains("Success", StringComparison.OrdinalIgnoreCase)) return new(InstallOutcome.Success, output);
        if (output.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase)
            || output.Contains("signatures do not match", StringComparison.OrdinalIgnoreCase))
            return new(InstallOutcome.SignatureMismatch, output);
        if (output.Contains("INSTALL_FAILED_NO_MATCHING_ABIS", StringComparison.OrdinalIgnoreCase))
            return new(InstallOutcome.AbiMismatch, output);
        return new(InstallOutcome.Failed, output);
    }

    /// <summary>
    /// Installs the package. A package that is signed differently cannot be put over the one that is installed,
    /// so it is uninstalled first when the user allows it; the uninstall clears the app's own data.
    /// </summary>
    public static InstallResult InstallApk(string apkPath, bool autoReinstall)
    {
        if (apkPath.Length == 0) return new(InstallOutcome.Failed, "no file was chosen");
        if (!File.Exists(apkPath)) return new(InstallOutcome.Failed, $"the file does not exist: {apkPath}");
        var result = Adb(["install", "-r", apkPath], 300);
        var outcome = ClassifyInstall(result.Combined);
        if (outcome.Outcome != InstallOutcome.SignatureMismatch) return outcome;
        if (!autoReinstall) return outcome;
        var uninstall = Adb(["uninstall", Package], 60);
        var again = Adb(["install", apkPath], 300);
        var retry = ClassifyInstall(again.Combined);
        if (retry.Outcome == InstallOutcome.Success)
            return new(InstallOutcome.Success, uninstall.Combined + "\n" + retry.Detail);
        return retry;
    }

    /// <summary>Starts the AxTV app.</summary>
    public static (bool Ok, string Detail) LaunchApp()
    {
        if (!DeviceOnline()) return (false, "the emulator is not running");
        var result = Adb(["shell", "am", "start", "-n", Activity], 60);
        return (!result.Combined.Contains("Error", StringComparison.OrdinalIgnoreCase), result.Combined);
    }

    /// <summary>Stops the AxTV app.</summary>
    public static (bool Ok, string Detail) StopApp()
    {
        var result = Adb(["shell", "am", "force-stop", Package], 60);
        return (true, result.Combined.Length > 0 ? result.Combined : "stopped");
    }

    /// <summary>Removes the AxTV app from the device.</summary>
    public static (bool Ok, string Detail) UninstallApp()
    {
        var result = Adb(["uninstall", Package], 60);
        return (result.Combined.Contains("Success", StringComparison.OrdinalIgnoreCase), result.Combined);
    }

    /// <summary>The installed app's version, empty when it is not installed.</summary>
    public static string AppVersion()
    {
        var result = Adb(["shell", "dumpsys", "package", Package], 30);
        if (result.ReturnCode != 0) return "";
        var match = Regex.Match(result.Stdout, @"versionName=([^\s|]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }

    /// <summary>True when the device has finished booting.</summary>
    public static bool BootCompleted()
    {
        var result = Adb(["shell", "getprop", "sys.boot_completed"], 30);
        return result.Stdout.Trim() == "1";
    }

    // ----------------------------------------------------------------------
    // The screen
    // ----------------------------------------------------------------------

    /// <summary>The screen size the device reports, both the physical one and the override.</summary>
    public static (string? Physical, string? Override) ParseWmSize(string output)
    {
        string? physical = null, over = null;
        var physicalMatch = Regex.Match(output, @"Physical size:\s*(\d+x\d+)", RegexOptions.IgnoreCase);
        if (physicalMatch.Success) physical = physicalMatch.Groups[1].Value;
        var overrideMatch = Regex.Match(output, @"Override size:\s*(\d+x\d+)", RegexOptions.IgnoreCase);
        if (overrideMatch.Success) over = overrideMatch.Groups[1].Value;
        return (physical, over);
    }

    /// <summary>The size the guest screen is overridden to, or null when it is at its native size.</summary>
    public static string? WmOverride()
    {
        var result = Adb(["shell", "wm", "size"], 30);
        return ParseWmSize(result.Stdout).Override;
    }

    /// <summary>Sets the guest screen size, or puts it back to the native one when the size is null.</summary>
    public static string SetWmOverride(string? size)
    {
        var result = Adb(["shell", "wm", "size", size ?? "reset"], 40);
        return result.Combined;
    }

    /// <summary>True when a picture of that many bytes shows a screen and not a black one.</summary>
    public static bool IsHealthyFrame(long bytes) => bytes > HealthyFrameBytes;

    /// <summary>
    /// Takes a picture of the device and tells whether it shows something. A blank picture is small, and a real
    /// one is much bigger, so the size of the file is enough to tell them apart.
    /// </summary>
    public static (bool Ok, long Bytes) DisplayHealth()
    {
        const string remote = "/sdcard/_axtv_health.png";
        try
        {
            Adb(["shell", "screencap", "-p", remote], 40);
            var listed = Adb(["shell", "ls", "-l", remote], 25);
            var match = Regex.Match(listed.Stdout, @"\s(\d+)\s+\d{4}-\d{2}-\d{2}");
            long size = match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : 0;
            Adb(["shell", "rm", "-f", remote], 20);
            return (IsHealthyFrame(size), size);
        }
        catch (Exception) { return (false, 0); }
    }

    /// <summary>Takes a screenshot and saves it in the pictures folder.</summary>
    public static (bool Ok, string Detail) Screenshot()
    {
        if (!DeviceOnline()) return (false, "the emulator is not running");
        string name = "axtv_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".png";
        string remote = "/sdcard/" + name;
        try { Directory.CreateDirectory(ShotDir); }
        catch (Exception ex) { return (false, ex.Message); }
        string local = Path.Combine(ShotDir, name);
        Adb(["shell", "screencap", "-p", remote], 60);
        var result = Adb(["pull", remote, local], 120);
        Adb(["shell", "rm", "-f", remote], 30);
        try
        {
            if (File.Exists(local) && new FileInfo(local).Length > 1000)
                return (true, $"{local} ({new FileInfo(local).Length / 1024} KB)");
        }
        catch (Exception) { }
        return (false, result.Combined);
    }

    /// <summary>
    /// Restarts the app so it lays itself out again after the screen changed. The app is only started again when
    /// it was the one in front, so a change in the background does not pull the screen away from the user.
    /// </summary>
    public static bool RelayoutApp()
    {
        try
        {
            var focus = Adb(["shell", "dumpsys", "window"], 30);
            bool wasFront = focus.Stdout.Contains(Package, StringComparison.Ordinal);
            Adb(["shell", "am", "force-stop", Package], 30);
            if (wasFront) Adb(["shell", "am", "start", "-n", Activity], 40);
            return wasFront;
        }
        catch (Exception) { return false; }
    }

    // ----------------------------------------------------------------------
    // Status
    // ----------------------------------------------------------------------

    /// <summary>Reads what the emulator is doing, for the panel.</summary>
    /// <param name="avdName">The AVD the panel is set to.</param>
    /// <param name="quick">True to leave out the readings that are slow, for a poll that runs every few seconds.</param>
    public static StatusSnapshot CollectStatus(string avdName, bool quick = false)
    {
        bool adbFound = File.Exists(AdbPath);
        bool emulatorFound = File.Exists(EmulatorPath);
        bool online = adbFound && DeviceOnline();
        bool alive = EmulatorProcessAlive();
        var profile = ProfileFor(avdName);

        string windowMode = "missing", windowRect = "";
        IntPtr window = EmulatorWindow.FindEmulatorWindow();
        if (window != IntPtr.Zero)
        {
            windowMode = EmulatorWindow.IsFullscreen(window) ? "fullscreen" : "small";
            windowRect = EmulatorWindow.WindowRect(window).ToString();
        }

        string orientation;
        if (profile.Portrait) orientation = "portrait_avd";
        else
        {
            string? over = online ? WmOverride() : null;
            orientation = over is null ? "landscape" : over == PortraitSize ? "portrait" : "custom";
        }

        // Reading the installed package is the slowest of the readings, so the quick poll leaves it out.
        string version = online && !quick ? AppVersion() : "";
        bool booted = online && BootCompleted();
        string detail = online ? (booted ? "ready" : "booting") : alive ? "starting" : "stopped";
        return new(adbFound && emulatorFound, adbFound, emulatorFound, online, alive,
            profile.Name, profile.Label, profile.Panel, profile.Portrait,
            windowMode, windowRect, orientation, version, booted, detail);
    }

    // ----------------------------------------------------------------------
    // The hidden console
    // ----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    // The two handles the child is to use for its input and its output. They are only passed on when they are
    // marked as inheritable, which is what this structure says: a handle opened without it stays in the
    // process that opened it, and the child would print to the console instead of to the log.
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool CreateProcessW(IntPtr applicationName, IntPtr commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, IntPtr currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Puts an argument between quotes so a path with spaces stays one argument.</summary>
    internal static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(char.IsWhiteSpace) ? argument : "\"" + argument + "\"";

    // ----------------------------------------------------------------------
    // Log
    // ----------------------------------------------------------------------

    /// <summary>The last lines of the emulator's log, for the panel.</summary>
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
