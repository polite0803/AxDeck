using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AxDeck.Core;
using Microsoft.Win32;

namespace AxDeck;

// Projects are entered here, never found on disk. A row is the complete user-visible work context: the folder, the local AI
// tool that may write session records for it, and the optional launcher and file group used by Restore.
public partial class SettingsWindow
{
    internal const int ProjectsSettingsPage = 17;

    private Guid? _projectEdit;

    private void ProjectsSettings()
    {
        SectionTitle(Loc.T("项目", "Projects"), help: () => Loc.T(
            "只列出你在这里登记的项目。AxDeck 不扫描磁盘，也不根据窗口、进程或文件夹时间猜测项目。打开与恢复都只使用这一行里的路径和选择。",
            "Only projects you register here are listed. AxDeck does not scan the disk or infer a project from windows, processes or folder times. Open and Restore use only the paths and choices on this row."));
        var list = new StackPanel();
        var projects = _app.Projects.Projects.OrderBy(project => project.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (projects.Count == 0)
        {
            var empty = TextLine(Loc.T("还没有登记项目。添加一个目录后，就可以从面板或命令面板打开它。",
                "No projects yet. Add a folder to open it from the panel or command palette."), 12.5, TextTertiary);
            empty.Margin = new Thickness(0, 13, 0, 13);
            empty.TextWrapping = TextWrapping.Wrap;
            list.Children.Add(empty);
        }
        foreach (var project in projects)
        {
            if (list.Children.Count > 0) list.Children.Add(new Border { Height = 1, Background = UI.Brush(_palette.RowDivider) });
            list.Children.Add(_projectEdit == project.Id ? ProjectEditor(project) : ProjectRow(project));
        }
        Target.Children.Add(new Border { Child = list, BorderBrush = UI.Brush(Divider), BorderThickness = new Thickness(0, 1, 0, 0) });
        var add = AddLink(Loc.T("添加项目", "Add project"), AddProject);
        add.Margin = new Thickness(0, 6, 0, 24);
        add.IsEnabled = _projectEdit is null && projects.Count < ProjectBook.MaximumProjects;
        if (!add.IsEnabled && projects.Count >= ProjectBook.MaximumProjects)
        {
            add.ToolTip = Loc.T($"最多登记 {ProjectBook.MaximumProjects} 个项目。", $"Up to {ProjectBook.MaximumProjects} projects.");
            ToolTipService.SetShowOnDisabled(add, true);
        }
        Target.Children.Add(add);

        SectionTitle(Loc.T("文件组", "File groups"), help: () => Loc.T(
            "文件组只保存你挑选的有序路径，不复制文件，也不展开文件夹。恢复项目时可以把它里的文件准备到剪贴板，或从命令面板随时粘贴。",
            "A file group stores only the ordered paths you choose. It copies nothing and expands no folders. Restoring a project can prepare its files, or paste them from the command palette at any time."));
        FileGroupsEditor();
    }

    private FrameworkElement ProjectRow(ProjectEntry project)
    {
        var row = new Grid { MinHeight = 60, Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 7, 12, 7) };
        var name = TextLine(project.DisplayName, 13, TextPrimary, FontWeights.Medium);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(name);
        var detail = TextLine(project.RootPath, 11.5, TextTertiary);
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.ToolTip = project.RootPath;
        text.Children.Add(detail);
        var meta = new List<string> { ProviderCatalog.Name(project.WorkSource) };
        var service = AxDeck.Core.ProjectServices.Find(_app.ProjectServices, project.ServiceHost);
        if (service is not null) meta.Add(service.Label);
        else if (project.ServiceHost.Length > 0) meta.Add(Loc.T($"站点已配置：{project.ServiceHost}", $"site set: {project.ServiceHost}"));
        if (project.LaunchAppPath.Length > 0) meta.Add(Loc.T("有启动程序", "launcher set"));
        if (_projectGroup(project.DefaultFileGroupId) is { } group) meta.Add(group.ShownName);
        var line = TextLine(string.Join(" · ", meta), 11.5, TextTertiary);
        line.Margin = new Thickness(0, 3, 0, 0);
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(line);
        row.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Add(Button button, double margin = 4)
        {
            button.Margin = new Thickness(margin, 0, 0, 0);
            button.MinWidth = 0;
            actions.Children.Add(button);
        }
        Add(ActionButton(Loc.T("打开", "Open"), () => _app.OpenProject(project.Id)));
        Add(ActionButton(Loc.T("恢复", "Restore"), () => _app.RestoreProject(project.Id)), 6);
        Add(GlyphButton("M 10.5,2.5 L 13.5,5.5 L 6,13 H 3 V 10 Z", 12, 12, Loc.T("编辑项目", "Edit project"), () =>
        {
            _projectEdit = project.Id;
            ShowPage(ProjectsSettingsPage);
        }), 8);
        Add(GlyphButton("M 0,0 L 8,8 M 8,0 L 0,8", 8, 8, Loc.T("删除项目", "Delete project"), () => RemoveProject(project)), 4);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private FrameworkElement ProjectEditor(ProjectEntry project)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 12) };
        var name = Field(project.DisplayName, Loc.T("项目名称", "Project name"), 64);
        var root = Field(project.RootPath, Loc.T("项目目录", "Project folder"), 2048);
        var launch = Field(project.LaunchAppPath, Loc.T("启动程序（可选）", "Launcher (optional)"), 2048);
        var browseFolder = ActionButton(Loc.T("选择…", "Choose…"), () =>
        {
            var dialog = new OpenFolderDialog { Title = Loc.T("选择项目目录", "Choose a project folder"), Multiselect = false };
            if (dialog.ShowDialog(this) == true) root.Text = dialog.FolderName;
        });
        var browseLaunch = ActionButton(Loc.T("选择…", "Choose…"), () =>
        {
            if (BrowseExecutable(Loc.T("选择项目启动程序", "Choose a project launcher")) is { } path) launch.Text = path;
        });
        var clearLaunch = ActionButton(Loc.T("清除", "Clear"), () => launch.Text = "");
        panel.Children.Add(EditorLine(Loc.T("名称", "Name"), name));
        panel.Children.Add(EditorLine(Loc.T("目录", "Folder"), root, browseFolder));
        panel.Children.Add(EditorLine(Loc.T("启动程序", "Launcher"), launch, browseLaunch, clearLaunch));

        var sources = SessionSources.Watchable;
        var sourceBox = new ComboBox
        {
            Width = 220, Style = (Style)FindResource("SettingsCombo"),
            ItemsSource = sources.Select(ProviderCatalog.Name).ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(sources, project.WorkSource))
        };
        AutomationProperties.SetName(sourceBox, Loc.T("工作来源", "Work source"));
        PopupMotion.Attach(sourceBox);
        panel.Children.Add(EditorLine(Loc.T("工作来源", "Work source"), sourceBox));

        var groups = _app.Shelf.Data.FileGroups;
        var groupBox = new ComboBox
        {
            Width = 220, Style = (Style)FindResource("SettingsCombo"),
            ItemsSource = new[] { Loc.T("不指定", "None") }.Concat(groups.Select(group => group.ShownName)).ToList(),
            SelectedIndex = project.DefaultFileGroupId is { } selected
                ? Math.Max(0, groups.FindIndex(group => group.Id == selected) + 1) : 0
        };
        AutomationProperties.SetName(groupBox, Loc.T("默认文件组", "Default file group"));
        PopupMotion.Attach(groupBox);
        panel.Children.Add(EditorLine(Loc.T("默认文件组", "Default file group"), groupBox));

        var serviceOptions = _app.ProjectServices.ToList();
        var serviceBox = new ComboBox
        {
            Width = 220, Style = (Style)FindResource("SettingsCombo"),
            ItemsSource = new[] { Loc.T("不指定", "None") }.Concat(serviceOptions.Select(option => option.Label)).ToList(),
            SelectedIndex = AxDeck.Core.ProjectServices.Find(serviceOptions, project.ServiceHost) is { } selectedService
                ? serviceOptions.IndexOf(selectedService) + 1 : 0
        };
        AutomationProperties.SetName(serviceBox, Loc.T("关联站点", "Linked site"));
        PopupMotion.Attach(serviceBox);
        panel.Children.Add(EditorLine(Loc.T("关联站点", "Linked site"), serviceBox));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        void Close() { _projectEdit = null; ShowPage(ProjectsSettingsPage); }
        var cancel = ActionButton(Loc.T("取消", "Cancel"), Close);
        cancel.Style = (Style)FindResource("SettingsOutlineButton");
        var save = ActionButton(Loc.T("保存", "Save"), () =>
        {
            var edited = project.Copy();
            edited.DisplayName = name.Text;
            edited.RootPath = root.Text;
            edited.LaunchAppPath = launch.Text;
            if (sourceBox.SelectedIndex >= 0) edited.WorkSource = sources[sourceBox.SelectedIndex];
            edited.DefaultFileGroupId = groupBox.SelectedIndex > 0 && groupBox.SelectedIndex - 1 < groups.Count
                ? groups[groupBox.SelectedIndex - 1].Id : null;
            edited.ServiceHost = serviceBox.SelectedIndex > 0 && serviceBox.SelectedIndex - 1 < serviceOptions.Count
                ? serviceOptions[serviceBox.SelectedIndex - 1].Host : "";
            if (!_app.UpdateProject(edited))
            {
                SaveStatus(Loc.T("项目没有保存：目录不能为空，也不能和另一个项目重复。",
                    "The project was not saved: its folder cannot be empty or the same as another project's."), true);
                return;
            }
            SaveStatus("");
            Close();
        }, primary: true);
        save.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);

        panel.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        };
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => name.Focus());
        return panel;
    }

    private FrameworkElement EditorLine(string label, Control control, params Button[] actions)
    {
        var row = new Grid { MinHeight = 42, Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = TextLine(label, 12.5, TextSecondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions)
        {
            action.Margin = new Thickness(buttons.Children.Count == 0 ? 8 : 6, 0, 0, 0);
            buttons.Children.Add(action);
        }
        Grid.SetColumn(buttons, 2);
        row.Children.Add(buttons);
        return row;
    }

    private TextBox Field(string text, string name, int maximum) => new()
    {
        Style = (Style)FindResource("SettingsField"), Height = 32, Padding = new Thickness(10, 0, 10, 0),
        FontSize = 12.5, MaxLength = maximum, Text = text, VerticalContentAlignment = VerticalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Tag = name, ToolTip = name
    };

    private void AddProject()
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("添加项目", "Add project"), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var project = _app.AddProject(dialog.FolderName);
        if (project is null)
        {
            SaveStatus(Loc.T("项目没有添加：路径无效、已经登记，或项目数量已达上限。",
                "The project was not added: the path is invalid, already registered, or the project limit was reached."), true);
            return;
        }
        SaveStatus("");
        _projectEdit = project.Id;
        ShowPage(ProjectsSettingsPage);
    }

    private void RemoveProject(ProjectEntry project)
    {
        if (!Confirm(Loc.T("删除项目", "Delete project"),
            Loc.T($"将删除“{project.DisplayName}”的项目登记。目录里的文件不会受影响。",
                $"This deletes the registration for \"{project.DisplayName}\". Files in the folder are not changed."),
            Loc.T("删除", "Delete"))) return;
        if (!_app.RemoveProject(project.Id))
        {
            SaveStatus(Loc.T("项目没有删除，项目列表可能无法写入。", "The project was not deleted; the project list may not be writable."), true);
            return;
        }
        if (_projectEdit == project.Id) _projectEdit = null;
        SaveStatus("");
        ShowPage(ProjectsSettingsPage);
    }

    private ShelfFileGroup? _projectGroup(Guid? id) => id is { } known
        ? _app.Shelf.Data.FileGroups.FirstOrDefault(group => group.Id == known) : null;
}
