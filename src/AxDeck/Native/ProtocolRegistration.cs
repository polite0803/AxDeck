using System.IO;
using Microsoft.Win32;

namespace AxDeck.Native;

// The axdeck:// addresses of the app, registered for the current user (HKCU\Software\Classes) so the addresses reach the
// app without administrator rights. The entry stays after AxDeck ends: a link has to be able to start it.
internal static class ProtocolRegistration
{
    private const string KeyPath = @"Software\Classes\axdeck";

    // Whether the addresses are registered right now.
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("URL Protocol") is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    // Whether the addresses are registered for this executable. A moved or reinstalled app is registered again.
    public static bool PointsAt(string executable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command");
            return key?.GetValue("") is string command && command.Contains(executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    // Registers the addresses, pointing at the running executable. A program that cannot be located is not registered.
    public static void Register()
    {
        var executable = Environment.ProcessPath;
        if (executable is null) return;
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        key.SetValue("", "URL:AxDeck", RegistryValueKind.String);
        key.SetValue("URL Protocol", "", RegistryValueKind.String);
        using var command = key.CreateSubKey(@"shell\open\command", true);
        command.SetValue("", $"\"{executable}\" \"%1\"", RegistryValueKind.String);
    }
}
