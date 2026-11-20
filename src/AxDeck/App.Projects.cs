using System.Diagnostics;
using System.IO;
using System.Windows;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The projects the user registered by hand, and the actions that use them. A project is never discovered from a window,
// shell history or directory walk: projects.json holds only what the user chose. Git, the command line and process reads are
// left to the user's own terminal or AI tool; opening a project does not run any of them here.
public partial class App
{
    public ProjectStore ProjectsStore { get; private set; } = null!;
    public ProjectBook Projects { get; private set; } = new();
    private IReadOnlyList<ProjectServiceOption> _projectServices = [];

    internal IReadOnlyList<ProjectServiceOption> ProjectServices => _projectServices;

    // The Work page uses the same services.json list as the Services page. ServicesPanel owns the window-visible refresh;
    // this copy is only read when projects or the page are shown, and no probe or network request is involved.
    internal void RefreshProjectServices()
    {
        _projectServices = AxDeck.Core.ProjectServices.Options(ServicesConfig.Load(Store.DirectoryPath));
    }

    private void LoadProjects()
    {
        ProjectsStore = new ProjectStore(Store.DirectoryPath);
        Projects = ProjectsStore.Load();
        RefreshProjectServices();
    }

    // Saves the whole explicit list. A failed write keeps the in-memory edit for this run and is reported to the caller so the
    // settings page can say that it was not kept.
    internal bool SaveProjects()
    {
        Projects.Normalize();
        return ProjectsStore.Save(Projects);
    }

    internal ProjectEntry? AddProject(string rootPath, string? displayName = null)
    {
        try
        {
            var project = Projects.Add(rootPath, displayName);
            if (!SaveProjects()) { Projects.Remove(project.Id); return null; }
            WorkContextChanged?.Invoke();
            return project;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return null; }
    }

    internal bool UpdateProject(ProjectEntry edited)
    {
        var existing = Projects.Find(edited.Id);
        if (existing is null) return false;
        edited.Normalize();
        if (edited.RootPath.Length == 0) return false;
        if (Projects.Projects.Any(project => project.Id != edited.Id
            && string.Equals(project.RootPath, edited.RootPath, StringComparison.OrdinalIgnoreCase))) return false;
        var previous = existing.Copy();
        existing.DisplayName = edited.DisplayName;
        existing.RootPath = edited.RootPath;
        existing.WorkSource = edited.WorkSource;
        existing.DefaultFileGroupId = edited.DefaultFileGroupId;
        existing.LaunchAppPath = edited.LaunchAppPath;
        existing.ServiceHost = edited.ServiceHost;
        if (SaveProjects()) { WorkContextChanged?.Invoke(); return true; }
        existing.DisplayName = previous.DisplayName;
        existing.RootPath = previous.RootPath;
        existing.WorkSource = previous.WorkSource;
        existing.DefaultFileGroupId = previous.DefaultFileGroupId;
        existing.LaunchAppPath = previous.LaunchAppPath;
        existing.ServiceHost = previous.ServiceHost;
        return false;
    }

    internal bool RemoveProject(Guid id)
    {
        var removed = Projects.Projects.FirstOrDefault(project => project.Id == id);
        if (removed is null || !Projects.Remove(id)) return false;
        if (SaveProjects()) { WorkContextChanged?.Invoke(); return true; }
        Projects.Projects.Add(removed);
        Projects.Normalize();
        return false;
    }

    // The palette's work context. History, projects and shelf entries are already local records; this does not inspect a
    // project directory, a title, a process list or a conversation to fill them in.
    internal CommandPaletteContext PaletteContext()
    {
        var focus = Focus.History
            .OrderByDescending(session => session.EndedAt)
            .Select(session => new FocusResumeOption(session, Projects.Find(session.ProjectId)))
            .ToList();
        ProviderId? source = Projects.Find(Focus.ProjectId)?.WorkSource;
        if (source is not { } known || !SessionSources.Supports(known))
            source = Focus.WorkSources.Where(SessionSources.Supports).Cast<ProviderId?>().FirstOrDefault();
        return new CommandPaletteContext(focus, Projects.Projects.ToList(), Shelf.Data.Clips, Shelf.Data.FileGroups, source);
    }

    internal ProjectEntry? Project(Guid? id) => Projects.Find(id);

    // Opens the registered folder only. It never registers a path, launches an app or reads the directory.
    internal bool OpenProject(Guid id)
    {
        if (Projects.Find(id) is not { } project)
        {
            SystemNotice(Loc.T("找不到项目", "Project not found"), Loc.T("项目可能已经从设置中移除。", "The project may have been removed from the settings."), 1);
            return false;
        }
        if (!Directory.Exists(project.RootPath))
        {
            SystemNotice(Loc.T("项目目录不存在", "Project folder not found"),
                Loc.T($"无法打开“{project.DisplayName}”：{project.RootPath}", $"Could not open {project.DisplayName}: {project.RootPath}"), 1);
            return false;
        }
        try
        {
            BoxShell.Open(project.RootPath);
            MarkProjectOpened(project);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SystemNotice(Loc.T("无法打开项目", "Could not open the project"), ex.Message, 1);
            return false;
        }
    }

    // Opens a registered project's launcher, or its folder when no launcher is chosen. A launcher already running is brought
    // forward; a new one is started in the project root. The default file group is only prepared when it is explicitly set.
    internal bool RestoreProject(Guid id, Action<bool>? pasted = null)
    {
        if (Projects.Find(id) is not { } project)
        {
            SystemNotice(Loc.T("找不到项目", "Project not found"), Loc.T("项目可能已经从设置中移除。", "The project may have been removed from the settings."), 1);
            return false;
        }
        if (!Directory.Exists(project.RootPath))
        {
            SystemNotice(Loc.T("项目目录不存在", "Project folder not found"),
                Loc.T($"无法恢复“{project.DisplayName}”：{project.RootPath}", $"Could not restore {project.DisplayName}: {project.RootPath}"), 1);
            return false;
        }

        bool launched = true;
        string? note = null;
        if (project.LaunchAppPath.Length == 0)
        {
            try { BoxShell.Open(project.RootPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                SystemNotice(Loc.T("无法打开项目目录", "Could not open the project folder"), ex.Message, 1);
                launched = false;
            }
        }
        else if (!File.Exists(project.LaunchAppPath))
        {
            SystemNotice(Loc.T("启动程序不存在", "Launcher not found"),
                Loc.T($"“{project.DisplayName}”的启动程序不存在：{project.LaunchAppPath}", $"The launcher for {project.DisplayName} does not exist: {project.LaunchAppPath}"), 1);
            launched = false;
        }
        else
        {
            string processName = Path.GetFileName(project.LaunchAppPath);
            nint existing = ProcessWindows.Find(processName);
            if (existing != 0)
            {
                if (!ProcessWindows.Activate(existing)) note = Loc.T("已找到启动程序，但无法将它切到前台。", "The launcher is running, but it could not be brought to the foreground.");
            }
            else
            {
                try
                {
                    Process.Start(new ProcessStartInfo(project.LaunchAppPath)
                    {
                        WorkingDirectory = project.RootPath,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    SystemNotice(Loc.T("无法启动项目", "Could not launch the project"), ex.Message, 1);
                    launched = false;
                }
            }
        }

        if (project.DefaultFileGroupId is { } groupId)
        {
            var group = Shelf.Data.FileGroups.FirstOrDefault(item => item.Id == groupId);
            if (group is null)
                SystemNotice(Loc.T("默认文件组不存在", "Default file group not found"),
                    Loc.T($"“{project.DisplayName}”没有可用的默认文件组。", $"The default file group for {project.DisplayName} is no longer available."), 1);
            else
            {
                var present = group.Paths.Where(ShelfFiles.Exists).ToList();
                int missing = group.Paths.Count - present.Count;
                if (present.Count > 0)
                    Shelf.Paste(ShelfService.FileData(present), done => pasted?.Invoke(done));
                if (missing > 0)
                    SystemNotice(Loc.T("部分文件不存在", "Some files are missing"),
                        Loc.T($"“{project.DisplayName}”的默认文件组有 {missing} 个路径不存在，只准备了现有的 {present.Count} 个。",
                            $"{missing} paths in {project.DisplayName}'s default file group are missing; only {present.Count} existing paths were prepared."), 1);
                else if (present.Count == 0)
                    SystemNotice(Loc.T("默认文件组为空", "Default file group is empty"),
                        Loc.T($"“{project.DisplayName}”的默认文件组没有可用的路径。", $"The default file group for {project.DisplayName} has no available paths."), 1);
            }
        }
        if (note is not null) SystemNotice(Loc.T("项目已恢复", "Project restored"), note, 1);
        if (launched) MarkProjectOpened(project);
        return launched;
    }

    internal event Action? WorkContextChanged;

    // A project becomes recent only after an action it triggered succeeded. This is a user-attributed event, not a guess from
    // another application's foreground window.
    private void MarkProjectOpened(ProjectEntry project)
    {
        project.LastOpenedAt = DateTimeOffset.Now;
        SaveProjects();
        WorkContextChanged?.Invoke();
    }
}
