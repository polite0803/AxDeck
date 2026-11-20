using System.Text.Json;
using System.Text.RegularExpressions;

namespace AxDeck.Core;

// A project the user explicitly registered. A project is not discovered from window titles, shells or disk walks: the
// root, the work source and the optional launcher are all choices made in the settings. The default file group is a shelf
// group the project may put on the clipboard when it is restored.
public sealed class ProjectEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "";
    public string RootPath { get; set; } = "";
    public ProviderId WorkSource { get; set; } = ProviderId.Codex;
    public Guid? DefaultFileGroupId { get; set; }
    public string LaunchAppPath { get; set; } = "";
    // An explicit choice from services.json. AxDeck never matches this to the project path, name, process or git remote;
    // an empty value means the project has no site and the Work page offers no service action.
    public string ServiceHost { get; set; } = "";
    // Set only when the user opens or restores this project from AxDeck. It is a local recency marker, not an inference from
    // windows, processes or folder timestamps.
    public DateTimeOffset? LastOpenedAt { get; set; }

    public ProjectEntry Copy() => (ProjectEntry)MemberwiseClone();

    public void Normalize()
    {
        DisplayName = CleanText(DisplayName, 64);
        RootPath = CleanPath(RootPath);
        LaunchAppPath = CleanPath(LaunchAppPath);
        ServiceHost = CleanHost(ServiceHost);
        if (DisplayName.Length == 0)
        {
            string trimmed = RootPath.TrimEnd('\\', '/');
            DisplayName = Path.GetFileName(trimmed) is { Length: > 0 } name ? CleanText(name, 64) : "Project";
        }
        WorkSource = SessionSources.Supports(WorkSource) ? WorkSource : ProviderId.Codex;
        if (DefaultFileGroupId == Guid.Empty) DefaultFileGroupId = null;
        if (LastOpenedAt is { } opened && opened == default) LastOpenedAt = null;
    }

    public static string CleanText(string? text, int maximum)
    {
        string cleaned = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        return cleaned.Length > maximum ? cleaned[..maximum] : cleaned;
    }

    public static string CleanPath(string? path)
    {
        string cleaned = (path ?? "").Trim().Trim('"');
        if (cleaned.Length > 2048 || cleaned.Any(char.IsControl)) return "";
        return cleaned;
    }

    public static string CleanHost(string? host)
    {
        string cleaned = (host ?? "").Trim();
        return cleaned.Length <= 512 && !cleaned.Any(char.IsControl) ? cleaned : "";
    }
}

// One site a project may explicitly point at. The label and host are copied from services.json for display; the command is
// prepared only when the user asks for it. No status is probed and no host is guessed here.
public sealed record ProjectServiceOption(string Host, string Label, string Kind, string Command, string Tip);

public static class ProjectServices
{
    // Keeps the site list's own order and drops malformed entries the same way ServicesConfig.Normalized does. A project
    // keeps a saved host even if services.json no longer lists it, so editing that file does not silently rewrite projects.
    public static IReadOnlyList<ProjectServiceOption> Options(ServicesConfig? config)
    {
        if (config?.Tunnel?.Sites is not { } sites) return [];
        return [.. sites.Where(site => !string.IsNullOrWhiteSpace(site.Host) && TunnelSiteKind.IsKnown(site.Kind))
            .Select(site => new ProjectServiceOption(
                site.Host.Trim(),
                string.IsNullOrWhiteSpace(site.Label) ? site.Host.Trim() : site.Label.Trim(),
                site.Kind,
                Command(site),
                CommandTip(site)))];
    }

    public static ProjectServiceOption? Find(IEnumerable<ProjectServiceOption>? options, string? host)
    {
        string cleaned = ProjectEntry.CleanHost(host);
        if (cleaned.Length == 0) return null;
        return options?.FirstOrDefault(option => string.Equals(option.Host, cleaned, StringComparison.OrdinalIgnoreCase));
    }

    private static string Command(TunnelSite site) => site.Kind switch
    {
        TunnelSiteKind.Tcp or TunnelSiteKind.Rdp => $"cloudflared access tcp --hostname {site.Host}",
        TunnelSiteKind.Ssh => $"ssh {site.Host}",
        _ => $"start {site.Host}",
    };

    private static string CommandTip(TunnelSite site) => site.Kind switch
    {
        TunnelSiteKind.Tcp or TunnelSiteKind.Rdp =>
            "cloudflared access tcp --hostname <host> " + Loc.T("（先在 services.json 配好 Port；这里只复制命令，不启动转发）",
                "(set Port in services.json first; this only copies the command, it does not start a forwarder)"),
        TunnelSiteKind.Ssh => "ssh <host> " + Loc.T("（只复制命令，不启动连接）", "(only copies the command; no connection is started)"),
        _ => "start <host> " + Loc.T("（只复制命令，不打开浏览器）", "(only copies the command; no browser is opened)"),
    };
}

// projects.json holds only the entries the user chose. It lives beside settings.json instead of inside it because projects
// have their own lifetime and can be edited without writing the whole settings file.
public sealed class ProjectBook
{
    public const int MaximumProjects = 64;
    public List<ProjectEntry> Projects { get; set; } = [];

    public ProjectEntry? Find(Guid? id) => id is { } known ? Projects.FirstOrDefault(project => project.Id == known) : null;
    public ProjectEntry? Find(string? rootPath) => Projects.FirstOrDefault(project =>
        string.Equals(project.RootPath, ProjectEntry.CleanPath(rootPath), StringComparison.OrdinalIgnoreCase));

    public ProjectEntry Add(string rootPath, string? displayName = null)
    {
        string cleanedRoot = ProjectEntry.CleanPath(rootPath);
        if (cleanedRoot.Length == 0)
            throw new ArgumentException("A project needs a root path.", nameof(rootPath));

        Normalize();
        if (Find(cleanedRoot) is { } existing) return existing;
        if (Projects.Count >= MaximumProjects)
            throw new InvalidOperationException($"At most {MaximumProjects} projects can be registered.");

        var project = new ProjectEntry { RootPath = rootPath, DisplayName = displayName ?? "" };
        project.Normalize();
        Projects.Add(project);
        Normalize();
        return Find(project.Id) ?? throw new InvalidOperationException("The project could not be normalized.");
    }

    public bool Remove(Guid id) => Projects.RemoveAll(project => project.Id == id) > 0;

    public ProjectEntry? MostRecent => Projects
        .Where(project => project.LastOpenedAt is not null)
        .OrderByDescending(project => project.LastOpenedAt)
        .FirstOrDefault();

    // A file group can be removed while projects still name it as their default. Clearing those references keeps the project
    // list honest; a stale id is otherwise harmless and is reported only when that project is explicitly restored.
    public int ClearFileGroup(Guid id)
    {
        int changed = 0;
        foreach (var project in Projects.Where(project => project.DefaultFileGroupId == id))
        {
            project.DefaultFileGroupId = null;
            changed++;
        }
        return changed;
    }

    public void Normalize()
    {
        var ids = new HashSet<Guid>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<ProjectEntry>(MaximumProjects);
        foreach (var project in (Projects ?? []).OfType<ProjectEntry>())
        {
            project.Normalize();
            if (project.RootPath.Length == 0 || project.DisplayName.Length == 0 || !roots.Add(project.RootPath)) continue;
            if (project.Id == Guid.Empty || !ids.Add(project.Id))
            {
                do { project.Id = Guid.NewGuid(); } while (!ids.Add(project.Id));
            }
            kept.Add(project);
            if (kept.Count == MaximumProjects) break;
        }
        Projects = kept;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public byte[] Serialize()
    {
        Normalize();
        return JsonSerializer.SerializeToUtf8Bytes(this, Options);
    }

    public static ProjectBook Parse(byte[]? bytes)
        => TryParse(bytes, out var book) ? book : new();

    // The store needs to tell an empty list from a file it could not read, since it leaves the damaged file alone.
    public static bool TryParse(byte[]? bytes, out ProjectBook book)
    {
        book = new();
        if (bytes is null || bytes.Length == 0) return true;
        try
        {
            book = JsonSerializer.Deserialize<ProjectBook>(bytes, Options) ?? new();
            book.Normalize();
            return true;
        }
        catch (JsonException) { book = new(); return false; }
        catch (NotSupportedException) { book = new(); return false; }
    }
}

// The file store reports a corrupted file instead of overwriting it. A project path is checked only when an action uses it,
// so opening settings never turns into a directory walk.
public sealed class ProjectStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "projects.json");
    public string? LoadWarning { get; private set; }

    public ProjectBook Load()
    {
        LoadWarning = null;
        try
        {
            if (!File.Exists(FilePath)) return new();
            byte[] bytes = File.ReadAllBytes(FilePath);
            if (ProjectBook.TryParse(bytes, out var book)) return book;
            LoadWarning = Loc.T("项目列表文件已损坏，已暂时按空列表打开；原文件不会被覆盖。",
                "The project list is damaged and is open as empty for now; the original file will not be overwritten.");
            return new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadWarning = Loc.T("项目列表无法读取，已暂时按空列表打开。", "The project list could not be read and is open as empty for now.");
            return new();
        }
    }

    public bool Save(ProjectBook book)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string temporary = FilePath + ".tmp";
            File.WriteAllBytes(temporary, book.Serialize());
            File.Move(temporary, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
