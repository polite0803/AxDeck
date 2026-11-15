using System.Diagnostics;
using System.IO;
using AxDeck.Native;

// The live check for the Android emulator manager. It runs against this machine's own Android SDK: the tools are
// found, adb is asked for the devices, and a console program is started through the same hidden console the
// emulator is started with. It is asked for by name (--only axtv-live) and does not run with the other checks,
// because it starts processes and reads this machine's Android SDK.
internal static class AxTvLive
{
    public static void Run(Action<string, bool> check)
    {
        // -------------------------------------------------------------- the tools on this machine

        bool sdk = File.Exists(AxTvEmulator.AdbPath);
        bool emulator = File.Exists(AxTvEmulator.EmulatorPath);
        check($"the Android SDK is found ({AxTvEmulator.SdkRoot})", sdk);
        check("adb.exe is found", sdk);
        check("emulator.exe is found", emulator);

        var status = AxTvEmulator.CollectStatus("axtv_tv");
        check("the status is read without throwing", status.SdkFound == sdk);
        check("the AVDs of the machine are listed", AxTvEmulator.ListAvds() is not null);

        // -------------------------------------------------------------- adb

        var devices = AxTvEmulator.ParseDevices(AxTvEmulator.Run([AxTvEmulator.AdbPath, "devices"], 30).Stdout);
        check("adb answers with a device list", devices is not null);
        check("no emulator is running, or the one that is is found",
            !AxTvEmulator.DeviceOnline() || AxTvEmulator.EmulatorSerial() is not null);

        // -------------------------------------------------------------- the hidden console

        // The emulator is a console program whose virtual machine has to sit on a console. A console that is taken
        // away leaves the virtual machine with a visible terminal of its own, which is what this checks for: the
        // program runs, its output lands in the log, and no console window shows up while it runs.
        string log = Path.Combine(Path.GetTempPath(), "axtv-live-console.log");
        try { File.Delete(log); } catch (Exception) { }
        var (started, detail, pid) = AxTvEmulator.SpawnHidden(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/c", "echo axdeck-hidden-console & ping -n 30 127.0.0.1 > nul"],
            log);
        check($"a console program starts with a hidden console ({detail})", started && pid > 0);

        Thread.Sleep(1500);
        string text = ReadShared(log);
        check("what the program prints lands in the log", text.Contains("axdeck-hidden-console", StringComparison.Ordinal));

        bool consoleShown = false;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle.Contains("cmd", StringComparison.OrdinalIgnoreCase))
                {
                    consoleShown = true;
                    break;
                }
            }
            catch (Exception) { }
            finally { process.Dispose(); }
        }
        check("no console window showed up while it ran", !consoleShown);

        try { Process.GetProcessById(pid).Kill(); } catch (Exception) { }
        try { File.Delete(log); } catch (Exception) { }

        // -------------------------------------------------------------- the emulator's own tool

        // The same path with the emulator itself: it starts, writes what it says to the log and leaves nothing
        // on the screen.
        string emuLog = Path.Combine(Path.GetTempPath(), "axtv-live-emulator.log");
        try { File.Delete(emuLog); } catch (Exception) { }
        if (emulator)
        {
            // The version is asked for, not the list of AVDs: the list is empty on a machine without AVDs and
            // then the emulator has nothing to say, while the version is always there.
            var (emuStarted, emuDetail, _) = AxTvEmulator.SpawnHidden(AxTvEmulator.EmulatorPath, ["-version"], emuLog);
            check($"emulator.exe starts the same way ({emuDetail})", emuStarted);
            Thread.Sleep(2500);
            string emuText = ReadShared(emuLog);
            check("emulator.exe writes to the log it was given", emuText.Contains("emulator version", StringComparison.OrdinalIgnoreCase));
        }
        try { File.Delete(emuLog); } catch (Exception) { }
    }

    // The log is held open by the program that writes to it, so it is read with the writing allowed too.
    private static string ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception) { return ""; }
    }
}
