using System.IO;
using Microsoft.Win32;
using AxDeck.Core;

namespace AxDeck.Native;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key is not null && IsEnabled(key);
    }
    internal static bool IsEnabled(RegistryKey key) => key.GetValue("AxDeck") is string value && value.Length > 0;
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        SetEnabled(key, enabled, Environment.ProcessPath);
    }
    internal static void SetEnabled(RegistryKey key, bool enabled, string? executable)
    {
        if (!enabled) { key.DeleteValue("AxDeck", false); return; }
        if (executable is null) throw new IOException(Loc.T("无法确定应用位置。", "The application location could not be determined."));
        if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "AxDeck", StringComparison.OrdinalIgnoreCase))
            throw new IOException(Loc.T("请从 AxDeck.exe 启动应用后设置自启动。", "Start the app from AxDeck.exe before setting it to start with Windows."));
        var command = '"' + executable + '"';
        if (command.Length > 260) throw new IOException(Loc.T("应用路径过长，无法注册自启动。", "The application path is too long to register for startup."));
        key.SetValue("AxDeck", command, RegistryValueKind.String);
    }
}
