using AxDeck.Native;

// The Android emulator manager (the old "AxTV 模拟器管理器"), checked without an emulator: the command lines it
// builds, the output of the tools it reads, and the geometry of the windows it places are all decided by pure
// functions, so they can be checked on their own.
internal static class AxTvChecks
{
    public static void Run(Action<string, bool> check)
    {
        // -------------------------------------------------------------- the command lines

        var arguments = AxTvEmulator.BuildArguments("axtv_tv");
        check("The emulator starts cold, without a snapshot and with the picture left to the machine",
            arguments.SequenceEqual(["-avd", "axtv_tv", "-no-snapshot-load", "-gpu", "auto", "-no-boot-anim"]));

        check("The AVD's own name goes into the command line",
            AxTvEmulator.BuildArguments("axtv_portrait")[1] == "axtv_portrait");

        check("An argument with a space stays one argument",
            AxTvEmulator.Quote(@"C:\Program Files\emulator.exe") == "\"C:\\Program Files\\emulator.exe\"");
        check("An argument without a space is left alone",
            AxTvEmulator.Quote("auto") == "auto");

        // -------------------------------------------------------------- reading the tools' output

        var devices = AxTvEmulator.ParseDevices("List of devices attached\nemulator-5554\tdevice\nemulator-5556\toffline\n");
        check("The attached devices are read, with their state",
            devices.Count == 2 && devices[0].Serial == "emulator-5554" && devices[0].State == "device"
            && devices[1].Serial == "emulator-5556" && devices[1].State == "offline");

        check("The heading of the device list is not a device",
            AxTvEmulator.ParseDevices("List of devices attached\n\n").Count == 0);
        check("The daemon's start message is not a device",
            AxTvEmulator.ParseDevices("* daemon not running; starting now at tcp:5037\n").Count == 0);

        var size = AxTvEmulator.ParseWmSize("Physical size: 1920x1080\nOverride size: 1080x1920\n");
        check("The physical size and the override are told apart",
            size.Physical == "1920x1080" && size.Override == "1080x1920");
        check("A screen without an override reads as none",
            AxTvEmulator.ParseWmSize("Physical size: 1920x1080\n").Override is null);

        // -------------------------------------------------------------- installing

        check("A finished installation is a success",
            AxTvEmulator.ClassifyInstall("Performing Streamed Install\nSuccess\n").Outcome == AxTvEmulator.InstallOutcome.Success);
        check("A different signature is told apart from a plain failure",
            AxTvEmulator.ClassifyInstall("Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Existing package signatures do not match]")
                .Outcome == AxTvEmulator.InstallOutcome.SignatureMismatch);
        check("A package for another architecture is told apart too",
            AxTvEmulator.ClassifyInstall("Failure [INSTALL_FAILED_NO_MATCHING_ABIS: Failed to extract native libraries]")
                .Outcome == AxTvEmulator.InstallOutcome.AbiMismatch);
        check("Anything else is a failure",
            AxTvEmulator.ClassifyInstall("adb: failed to install").Outcome == AxTvEmulator.InstallOutcome.Failed);

        // -------------------------------------------------------------- the picture

        check("A blank picture is not a screen that shows something",
            !AxTvEmulator.IsHealthyFrame(4000) && !AxTvEmulator.IsHealthyFrame(AxTvEmulator.HealthyFrameBytes));
        check("A picture with a screen in it is",
            AxTvEmulator.IsHealthyFrame(AxTvEmulator.HealthyFrameBytes + 1) && AxTvEmulator.IsHealthyFrame(500000));

        // -------------------------------------------------------------- the AVDs

        // A name no machine has, so what is read is the fallback and not the machine's own configuration.
        var landscape = AxTvEmulator.ProfileFor("no-such-avd-at-all");
        check("An AVD nobody has falls back to the wide panel",
            !landscape.Portrait && Math.Abs(landscape.Aspect - 16.0 / 9.0) < 0.001);
        var portrait = AxTvEmulator.ProfileFor("no-such-portrait-avd");
        check("An AVD whose name says portrait falls back to the tall panel",
            portrait.Portrait && Math.Abs(portrait.Aspect - 9.0 / 16.0) < 0.001);

        // An AVD the machine has: the panel comes from its own configuration, so the shape follows the panel.
        var real = AxTvEmulator.ListAvds().FirstOrDefault();
        if (real is not null)
        {
            var profile = AxTvEmulator.ProfileFor(real);
            check("The panel of an AVD on the machine comes from its own configuration",
                profile.Portrait == profile.Aspect < 1 && Math.Abs(profile.Aspect - Math.Round(profile.Aspect, 4)) < 0.0001);
        }

        var avds = AxTvEmulator.ListAvds();
        check("The list of AVDs never holds an empty name",
            avds.All(name => !string.IsNullOrWhiteSpace(name)));
        check("The AVD that is used is one the machine has, or the default name",
            AxTvEmulator.ResolveAvd("no-such-avd") is { Length: > 0 });

        // -------------------------------------------------------------- the window geometry

        var screen = new EmulatorWindow.Rect(0, 0, 1920, 1080);
        var small = EmulatorWindow.SmallWindowRect(screen, 16.0 / 9.0);
        check("The small window takes 70 percent of the screen and keeps the shape",
            small.Width == 1344 && small.Height == 796 && small.Left == 288 && small.Top == 142);

        var stable = EmulatorWindow.StableRect(screen, 16.0 / 9.0);
        check("The plain window fills the screen when the shape fits",
            stable.Width == 1920 && stable.Height == 1080 && stable.Left == 0 && stable.Top == 0);

        var tooTall = EmulatorWindow.StableRect(new EmulatorWindow.Rect(0, 0, 1920, 1080), 9.0 / 16.0);
        check("A window that does not fit the screen is made smaller, not cut off",
            tooTall.Height == 1080 && tooTall.Width == 608);

        var tallScreen = new EmulatorWindow.Rect(5376, 0, 864, 1536);
        var full = EmulatorWindow.FullscreenRect(tallScreen, 9.0 / 16.0, portrait: true);
        check("A portrait AVD on a tall screen fills it, so the picture has no bars",
            full.Width == 864 && full.Height == 1536 && full.Left == 5376);
        var fullWide = EmulatorWindow.FullscreenRect(tallScreen, 16.0 / 9.0, portrait: false);
        check("A wide AVD keeps its shape on a tall screen and does not pretend to fill it",
            fullWide.Width == 864 && fullWide.Height == 486);

        check("A window that sits on the screen is not off it",
            !EmulatorWindow.MostlyOffscreen(new EmulatorWindow.Rect(0, 0, 1920, 1080), screen));
        check("A window far away from the screen is off it",
            EmulatorWindow.MostlyOffscreen(new EmulatorWindow.Rect(3500, 0, 1920, 1080), screen));
        // A window that is deliberately wider than the screen, so that the bars fall off it, is still on the screen.
        var narrow = new EmulatorWindow.Rect(0, 0, 864, 1536);
        check("A window that is deliberately wider than the screen is not taken for one that fell off",
            !EmulatorWindow.MostlyOffscreen(new EmulatorWindow.Rect(-900, 0, 2343, 1318), narrow));
        check("A window shrunk to a sliver is off the screen",
            EmulatorWindow.MostlyOffscreen(new EmulatorWindow.Rect(0, 0, 100, 80), screen));

        // -------------------------------------------------------------- the tools it runs

        var calls = new List<IReadOnlyList<string>>();
        // The device list is the first call every command makes: it is how the running emulator is found.
        AxTvEmulator.RunnerOverride = (args, _) =>
        {
            calls.Add(args);
            return args.Any(a => a.Contains("devices", StringComparison.Ordinal))
                ? new AxTvEmulator.RunResult(0, "List of devices attached\nemulator-5554\tdevice\n", "")
                : new AxTvEmulator.RunResult(0, "", "");
        };
        try
        {
            AxTvEmulator.Adb(["shell", "wm", "size"]);
            var shell = calls[^1];
            check("The device the commands go to is the emulator, not another device",
                shell.Contains("-s") && shell.Contains("emulator-5554")
                && shell.Contains("shell") && shell.Contains("wm") && shell.Contains("size"));

            calls.Clear();
            AxTvEmulator.Adb(["emu", "kill"]);
            var kill = calls[^1];
            check("The emulator is closed through adb",
                kill.Contains("emu") && kill.Contains("kill") && kill.Contains("emulator-5554"));
        }
        finally { AxTvEmulator.RunnerOverride = null; }
    }
}
