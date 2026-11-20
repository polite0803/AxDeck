using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// The development work page. It gathers the local records the app already keeps: projects the user registered, the focus
// countdown, workspace metadata from the selected AI tools, recent clipboard text and named file groups. The view is built
// once and filled on each render; nothing here polls while the panel is closed, and none of the actions steals focus.
public partial class MainWindow : IWorkViewHost
{
    private WorkView? _workView;
    private string _workKey = "";

    private void InitializeWork()
    {
        _app.WorkContextChanged += WorkContextChanged;
        _app.FocusChanged += WorkFocusChanged;
        _app.Shelf.Changed += WorkShelfChanged;
        Closed += (_, _) =>
        {
            _app.WorkContextChanged -= WorkContextChanged;
            _app.FocusChanged -= WorkFocusChanged;
            _app.Shelf.Changed -= WorkShelfChanged;
        };
    }

    private void WorkContextChanged() => WorkRenderIfVisible();
    private void WorkFocusChanged() => WorkRenderIfVisible();
    private void WorkShelfChanged() => WorkRenderIfVisible();

    private void WorkRenderIfVisible()
    {
        if (IsWorkPage && _expanded && !_preview) RenderUsage();
    }

    internal void RenderWork()
    {
        RefreshStatus();
        _app.RefreshProjectServices();
        ShowPageContent(WorkContent);
        ModelToolbar.Visibility = Visibility.Collapsed;
        ApplicationPages.Visibility = Visibility.Collapsed;
        ShowClock(true);
        double width = ContentCanvas.Width, height = ContentCanvas.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(header);
        WorkContent.Margin = new Thickness(_header.Side, 0, _header.Side, 10);
        if (IsWorkPage && _expanded && !_preview) _app.AskActivity();

        var focus = _app.Focus;
        var overview = WorkOverviewBuilder.Build(focus, _app.Projects.Projects);
        var projects = _app.Projects.Projects
            .OrderByDescending(project => project.LastOpenedAt ?? DateTimeOffset.MinValue)
            .ThenBy(project => project.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(6)
            .ToList();
        var activity = Settings.WorkSources.Distinct().Where(SessionSources.Supports)
            .OrderBy(source => source)
            .Select(source => _app.Activity.TryGetValue(source, out var snapshot) ? snapshot : SessionActivitySnapshot.Unavailable(source))
            .ToList();
        var clips = _app.Shelf.Data.Clips.Where(clip => clip.Kind != ClipKind.Image)
            .OrderByDescending(clip => clip.Copied)
            .Take(4)
            .ToList();
        var groups = _app.Shelf.Data.FileGroups.Take(4).ToList();
        string key = string.Join("|",
            projects.Select(project => $"{project.Id:N}:{project.LastOpenedAt?.UtcDateTime.Ticks}:{project.DisplayName}:{project.RootPath}:{project.WorkSource}"),
            $"services:{string.Join(",", _app.ProjectServices.Select(option => option.Host))}:{string.Join(",", projects.Select(project => project.ServiceHost))}",
            $"focus:{focus.Running}:{focus.Shown}:{focus.Remaining(DateTimeOffset.Now) / 5}:{focus.Label}:{focus.ProjectId:N}:{WorkSessionState(focus)}",
            $"overview:{overview.FocusCount}:{overview.FocusMinutes}:{overview.ActiveSourceCount}:{string.Join(",", overview.ActiveSources)}:{string.Join(",", overview.Projects.Select(row => $"{row.Project.Id:N}:{row.FocusCount}:{row.FocusMinutes}"))}",
            activity.Select(item => $"{item.Source}:{item.State}:{item.LastActivity?.UtcDateTime.Ticks}:{item.RecordCount}:{item.ActiveRecordCount}"),
            clips.Select(clip => $"{clip.Id:N}:{clip.Copied.UtcDateTime.Ticks}:{clip.Text.Length}:{Shelf.When(clip.Copied, DateTimeOffset.Now)}"),
            groups.Select(group => $"{group.Id:N}:{group.ShownName}:{group.Paths.Count}"));
        bool fade = _workKey.Length == 0;
        if (_workView is not null && key == _workKey && WorkContent.Children.Contains(_workView)) return;
        _workKey = key;
        var old = WorkContent.IsVisible ? _workView : null;
        _workView = new WorkView(this);
        _workView.Update(focus, overview, projects, activity, clips, groups);
        if (old is not null) MonitorCrossfade.Swap(WorkContent, _workView, old, Soft.Enabled);
        else
        {
            WorkContent.Children.Clear();
            WorkContent.Children.Add(_workView);
            if (fade && Soft.Enabled) _workView.BeginAnimation(OpacityProperty, Soft.Fade(.15, 1, Soft.Short));
        }
    }

    private static string WorkSessionState(FocusTimer focus)
        => $"{focus.StartedAt?.UtcDateTime.Ticks}:{focus.EndedAt?.UtcDateTime.Ticks}:{focus.WorkSources.Count}";

    internal void WorkOpenProject(Guid id) => _app.OpenProject(id);

    internal void WorkRestoreProject(Guid id) => _app.RestoreProject(id, sent => _app.SystemNotice(
        sent ? Loc.T("工作已准备", "Work is ready") : Loc.T("项目已恢复", "Project restored"),
        sent
            ? Loc.T("项目已恢复，默认文件组已投递到打开面板前的窗口。", "The project is restored and its default file group was sent to the window that was active before the panel.")
            : Loc.T("项目已恢复，默认文件组已放到剪贴板。", "The project is restored and its default file group is on the clipboard."),
        sent ? 0 : 1));

    internal void WorkFocusStart(Guid? projectId = null)
    {
        if (projectId is { } id) _app.FocusStartProject(id);
        else _app.FocusStart(FocusTimer.Presets[1]);
    }

    internal void WorkFocusToggle()
    {
        if (_app.Focus.Running) _app.FocusPause();
        else _app.FocusResume();
    }

    internal void WorkFocusReset() => _app.FocusReset();

    internal ProjectEntry? WorkProject(Guid? id) => _app.Project(id);

    internal void WorkPasteClip(Guid id)
    {
        var clip = _app.Shelf.Data.Clips.FirstOrDefault(entry => entry.Id == id);
        if (clip is null) return;
        _app.Shelf.Paste(_app.Shelf.ClipData(clip), Pasted);
    }

    internal void WorkPasteGroup(Guid id)
    {
        var group = _app.Shelf.Data.FileGroups.FirstOrDefault(entry => entry.Id == id);
        if (group is not null) PasteGroup(group);
    }

    internal void WorkOpenSettings() => _app.OpenSettingsPage(SettingsWindow.ProjectsSettingsPage);

    IReadOnlyList<ProjectServiceOption> IWorkViewHost.WorkServices => _app.ProjectServices;
    void IWorkViewHost.WorkOpenProject(Guid id) => WorkOpenProject(id);
    void IWorkViewHost.WorkRestoreProject(Guid id) => WorkRestoreProject(id);
    void IWorkViewHost.WorkFocusStart(Guid? projectId) => WorkFocusStart(projectId);
    void IWorkViewHost.WorkFocusReset() => WorkFocusReset();
    ProjectEntry? IWorkViewHost.WorkProject(Guid? id) => WorkProject(id);
    string? IWorkViewHost.WorkServiceCommand(Guid id) => WorkServiceCommand(id);
    void IWorkViewHost.WorkPasteServiceCommand(string command) => WorkPasteServiceCommand(command);
    void IWorkViewHost.WorkPasteClip(Guid id) => WorkPasteClip(id);
    void IWorkViewHost.WorkPasteGroup(Guid id) => WorkPasteGroup(id);
    void IWorkViewHost.WorkOpenSettings() => WorkOpenSettings();
    void IWorkViewHost.FocusOpen() => FocusOpen();

    internal string? WorkServiceCommand(Guid id)
        => _app.Project(id) is { ServiceHost.Length: > 0 } project
            ? AxDeck.Core.ProjectServices.Find(_app.ProjectServices, project.ServiceHost)?.Command : null;

    internal void WorkPasteServiceCommand(string command)
        => _app.Shelf.Paste(ShelfService.TextData(command), sent => _app.SystemNotice(
            sent ? Loc.T("命令已粘贴", "Command pasted") : Loc.T("命令已复制", "Command copied"),
            command, 0));
}

