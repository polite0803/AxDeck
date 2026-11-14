using System.IO;
using System.Net.Http;
using AxDeck.Core;
using Microsoft.Win32;

namespace AxDeck.Updates;

internal static class GitHubUpdates
{
    public const string RepositoryUrl = "https://github.com/polite0803/AxDeck";
    // Must match AppId in installer/AxDeck.iss. Inno Setup registers the uninstall entry as "<AppId>_is1".
    public const string InstallerAppId = "{CD0C54FB-5EF0-4444-B123-B72D58D4B749}";
    public static string BuildVersion => ApplicationVersion.Current;
    private static readonly HttpClient Http = CreateClient();

    // /releases/latest skips drafts and prereleases. The release workflow copies each beta's update.json to the beta-channel
    // release. Packages are fetched from their own tag so a release published between the check and the download cannot swap the file.
    public static InstallerUpdateBackend Create(string dataDirectory) => Create(new Uri(RepositoryUrl + "/releases/latest/download/" + UpdateManifest.FileName),
        (manifest, package) => new Uri($"{RepositoryUrl}/releases/download/v{manifest.Version}/{package.File}"), Http, InstallerAppId, dataDirectory,
        new Uri(RepositoryUrl + "/releases/download/beta-channel/" + UpdateManifest.FileName));

    internal static InstallerUpdateBackend Create(Uri manifest, Func<UpdateManifest, UpdatePackage, Uri> package, HttpClient http, string appId, string dataDirectory,
        Uri? betaManifest = null) =>
        new(http, manifest, package, BuildVersion, Path.Combine(dataDirectory, "updates"), Mode(appId, AppContext.BaseDirectory), betaManifest: betaManifest);

    // A copy the installer registered updates through the installer; a portable copy replaces its own files.
    internal static UpdateMode Mode(string appId, string directory) =>
        IsInstalled(appId, directory) ? UpdateMode.Installer : PortableUpdate.IsPortable(directory) ? UpdateMode.Portable : UpdateMode.None;

    // In-app updates are offered only to the copy the installer registered; a build output or copied folder is left alone.
    internal static bool IsInstalled(string appId, string directory)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + appId + "_is1");
                if (key?.GetValue("InstallLocation") is string location && location.Length > 0 &&
                    string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
        }
        return false;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AxDeck/" + BuildVersion);
        return client;
    }
}
