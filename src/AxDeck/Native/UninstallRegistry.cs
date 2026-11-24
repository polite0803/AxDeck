using System.IO;
using AxDeck.Core;
using Microsoft.Win32;

namespace AxDeck.Native;

// 从卸载注册表枚举已安装应用。只读，不改任何键。
// 哪些条目算「用户装的应用」（补丁、系统组件、嵌套项）由 InstalledApps.Create 判断，这里只负责取值。
internal static class UninstallRegistry
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    // 每用户、32 位、64 位的三份卸载键；同一个应用可能只出现在其中一处。
    private static readonly (RegistryHive Hive, RegistryView View)[] Locations =
    [
        (RegistryHive.CurrentUser, RegistryView.Default),
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.LocalMachine, RegistryView.Registry64),
    ];

    // 全机已安装应用，已按名称去重并按名称排序。
    // 键打不开或某个子键读不到都只是少几条，不能让整页失败——权限不足是常态。
    public static IReadOnlyList<InstalledApp> Enumerate()
    {
        var apps = new List<InstalledApp>();
        foreach (var (hive, view) in Locations)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallKey);
                if (uninstall is null) continue;

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var entry = uninstall.OpenSubKey(name);
                        if (entry is not null && Read(entry) is { } app) apps.Add(app);
                    }
                    catch (Exception ex) when (IsUnreadable(ex)) { }
                }
            }
            catch (Exception ex) when (IsUnreadable(ex)) { }
        }
        return InstalledApps.Distinct(apps);
    }

    private static InstalledApp? Read(RegistryKey entry) => InstalledApps.Create(
        Text(entry, "DisplayName"),
        Text(entry, "DisplayVersion"),
        Text(entry, "Publisher"),
        Text(entry, "DisplayIcon"),
        Text(entry, "InstallLocation"),
        systemComponent: Integer(entry.GetValue("SystemComponent")) == 1,
        releaseType: Text(entry, "ReleaseType"),
        parentKeyName: Text(entry, "ParentKeyName"));

    private static string? Text(RegistryKey entry, string name) => entry.GetValue(name) as string;

    // SystemComponent 一般是 DWORD，个别安装包写成字符串。
    private static int Integer(object? value) => value switch
    {
        int number => number,
        long number => (int)number,
        string text => int.TryParse(text, out var parsed) ? parsed : 0,
        _ => 0,
    };

    private static bool IsUnreadable(Exception ex) =>
        ex is System.Security.SecurityException or UnauthorizedAccessException or IOException;
}
