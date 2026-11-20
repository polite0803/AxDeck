using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AxDeck.Core;
using Microsoft.Win32;

namespace AxDeck;

// The named file groups used by projects and the command palette. Their rows edit the group's paths in place; nothing here
// opens, copies or expands a path, so editing a group stays an inexpensive settings operation.
public partial class SettingsWindow
{
    private Guid? _openFileGroup;
    private Guid? _namingFileGroup;

    private void FileGroupsEditor()
    {
        var groups = _app.Shelf.Data.FileGroups;
        var list = new StackPanel();
        if (groups.Count == 0)
        {
            var empty = TextLine(Loc.T("还没有文件组。", "No file groups yet."), 12.5, TextTertiary);
            empty.Margin = new Thickness(0, 13, 0, 13);
            list.Children.Add(empty);
        }
        foreach (var group in groups)
        {
            if (list.Children.Count > 0) list.Children.Add(new Border { Height = 1, Background = UI.Brush(_palette.RowDivider) });
            list.Children.Add(FileGroupRow(group, list));
        }
        Target.Children.Add(new Border { Child = list, BorderBrush = UI.Brush(Divider), BorderThickness = new Thickness(0, 1, 0, 0) });
        var add = AddLink(Loc.T("新建文件组", "New file group"), AddFileGroup);
        add.Margin = new Thickness(0, 6, 0, 24);
        add.IsEnabled = groups.Count < ShelfFileGroup.MaximumGroups;
        if (!add.IsEnabled)
        {
            add.ToolTip = Loc.T($"最多 {ShelfFileGroup.MaximumGroups} 个文件组。", $"Up to {ShelfFileGroup.MaximumGroups} file groups.");
            ToolTipService.SetShowOnDisabled(add, true);
        }
        Target.Children.Add(add);
    }

    private FrameworkElement FileGroupRow(ShelfFileGroup group, StackPanel list)
    {
        bool open = _openFileGroup == group.Id;
        Button? arrow = null;
        var paths = new StackPanel { Margin = new Thickness(28, 2, 0, 8), Visibility = open ? Visibility.Visible : Visibility.Collapsed };
        var header = new Grid { MinHeight = 42, Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        arrow = ChevronButton(Loc.T($"展开 {group.ShownName}", $"Show {group.ShownName}"), () =>
        {
            _openFileGroup = open ? null : group.Id;
            ShowPage(ProjectsSettingsPage);
        });
        arrow.Margin = new Thickness(0);
        header.Children.Add(arrow);

        var name = new TextBox
        {
            Style = (Style)FindResource("SettingsField"), Height = 30, Padding = new Thickness(9, 0, 9, 0),
            FontSize = 12.5, MaxLength = 48, Text = group.Name, VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(name, Loc.T("文件组名称", "File group name"));
        void CommitName()
        {
            string value = ProjectEntry.CleanText(name.Text, 48);
            if (value.Length == 0)
            {
                name.Text = group.Name;
                return;
            }
            if (value == group.Name) return;
            group.Name = value;
            ShelfEdited();
        }
        name.LostKeyboardFocus += (_, _) => CommitName();
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitName(); Keyboard.ClearFocus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { name.Text = group.Name; Keyboard.ClearFocus(); e.Handled = true; }
        };
        if (_namingFileGroup == group.Id)
        {
            _namingFileGroup = null;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => { name.Focus(); name.SelectAll(); name.BringIntoView(); });
        }
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 2, 12, 2) };
        labels.Children.Add(name);
        var count = TextLine(group.Paths.Count == 0 ? Loc.T("没有路径", "No paths")
            : Loc.T($"{group.Paths.Count} 个路径", $"{Loc.Count(group.Paths.Count, "path", "paths")}"), 11.5, TextTertiary);
        count.Margin = new Thickness(9, 2, 0, 0);
        labels.Children.Add(count);
        Grid.SetColumn(labels, 1);
        header.Children.Add(labels);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var add = ActionButton(Loc.T("添加路径", "Add paths"), () => AddGroupFiles(group));
        add.FontSize = 11.5;
        add.IsEnabled = group.Paths.Count < ShelfFileGroup.MaximumPaths;
        controls.Children.Add(add);
        var remove = GlyphButton("M 0,0 L 8,8 M 8,0 L 0,8", 8, 8, Loc.T($"删除 {group.ShownName}", $"Delete {group.ShownName}"), () => DeleteFileGroup(group));
        remove.Margin = new Thickness(8, 0, 0, 0);
        remove.Width = 26;
        remove.Height = 26;
        controls.Children.Add(remove);
        Grid.SetColumn(controls, 2);
        header.Children.Add(controls);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsWithinControl(source)) return;
            _openFileGroup = open ? null : group.Id;
            ShowPage(ProjectsSettingsPage);
            e.Handled = true;
        };

        if (open)
        {
            for (int i = 0; i < group.Paths.Count; i++) paths.Children.Add(GroupPathRow(group, i));
            if (group.Paths.Count == 0)
            {
                var empty = TextLine(Loc.T("这个文件组还没有路径。", "This file group has no paths yet."), 12, TextTertiary);
                empty.Margin = new Thickness(0, 6, 0, 8);
                paths.Children.Add(empty);
            }
            else if (group.Paths.Count >= ShelfFileGroup.MaximumPaths)
            {
                var limit = TextLine(Loc.T($"已达到 {ShelfFileGroup.MaximumPaths} 个路径的上限。",
                    $"The limit of {ShelfFileGroup.MaximumPaths} paths has been reached."), 11.5, TextTertiary);
                limit.Margin = new Thickness(0, 6, 0, 4);
                paths.Children.Add(limit);
            }
        }

        var row = new StackPanel();
        row.Children.Add(header);
        row.Children.Add(paths);
        return row;
    }

    private FrameworkElement GroupPathRow(ShelfFileGroup group, int index)
    {
        var row = new Grid { MinHeight = 38, Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var path = new TextBox
        {
            Style = (Style)FindResource("SettingsField"), Height = 30, Padding = new Thickness(9, 0, 9, 0),
            FontSize = 11.5, MaxLength = 2048, Text = group.Paths[index], VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(path, Loc.T("文件组路径", "File group path"));
        void CommitPath()
        {
            string value = ProjectEntry.CleanPath(path.Text);
            if (value.Length == 0 || group.Paths.Where((_, at) => at != index).Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                path.Text = group.Paths[index];
                return;
            }
            if (value == group.Paths[index]) return;
            group.Paths[index] = value;
            ShelfEdited();
        }
        path.LostKeyboardFocus += (_, _) => CommitPath();
        path.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitPath(); Keyboard.ClearFocus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { path.Text = group.Paths[index]; Keyboard.ClearFocus(); e.Handled = true; }
        };
        row.Children.Add(path);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var up = GlyphButton("M 2,8 L 6,4 L 10,8", 10, 10, Loc.T("上移", "Move up"), () => MoveGroupPath(group, index, -1));
        up.Width = 24; up.Height = 26; up.IsEnabled = index > 0;
        up.Margin = new Thickness(7, 0, 0, 0);
        controls.Children.Add(up);
        var down = GlyphButton("M 2,4 L 6,8 L 10,4", 10, 10, Loc.T("下移", "Move down"), () => MoveGroupPath(group, index, 1));
        down.Width = 24; down.Height = 26; down.IsEnabled = index < group.Paths.Count - 1;
        controls.Children.Add(down);
        var remove = GlyphButton("M 0,0 L 8,8 M 8,0 L 0,8", 8, 8, Loc.T("移除路径", "Remove path"), () =>
        {
            group.Paths.RemoveAt(index);
            ShelfEdited();
            ShowPage(ProjectsSettingsPage);
        });
        remove.Width = 24; remove.Height = 26;
        controls.Children.Add(remove);
        Grid.SetColumn(controls, 1);
        row.Children.Add(controls);
        return row;
    }

    private void AddFileGroup()
    {
        if (_app.Shelf.Data.FileGroups.Count >= ShelfFileGroup.MaximumGroups) return;
        var group = Shelf.AddFileGroup(_app.Shelf.Data, Loc.T("新文件组", "New file group"));
        _openFileGroup = group.Id;
        _namingFileGroup = group.Id;
        ShelfEdited();
        ShowPage(ProjectsSettingsPage);
    }

    private void AddGroupFiles(ShelfFileGroup group)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T($"向“{group.ShownName}”添加路径", $"Add paths to {group.ShownName}"),
            Filter = Loc.T("所有文件 (*.*)|*.*", "All files (*.*)|*.*"),
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        int added = Shelf.AddGroupPaths(group, dialog.FileNames);
        if (added == 0)
        {
            SaveStatus(Loc.T("没有添加路径：它们已经在文件组里，或已经达到上限。",
                "No paths were added: they are already in the group, or its limit was reached."), true);
            return;
        }
        SaveStatus("");
        _openFileGroup = group.Id;
        ShelfEdited();
        ShowPage(ProjectsSettingsPage);
    }

    private void MoveGroupPath(ShelfFileGroup group, int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= group.Paths.Count) return;
        (group.Paths[index], group.Paths[target]) = (group.Paths[target], group.Paths[index]);
        ShelfEdited();
        ShowPage(ProjectsSettingsPage);
    }

    private void DeleteFileGroup(ShelfFileGroup group)
    {
        if (!Confirm(Loc.T("删除文件组", "Delete file group"),
            Loc.T($"将删除“{group.ShownName}”和它保存的路径。原文件不会受影响；使用它作为默认文件组的项目会改为不指定。",
                $"This deletes \"{group.ShownName}\" and its saved paths. Original files are not changed; projects using it as their default will no longer name a file group."),
            Loc.T("删除", "Delete"))) return;

        _app.Shelf.Data.FileGroups.RemoveAll(item => item.Id == group.Id);
        if (_app.Projects.ClearFileGroup(group.Id) > 0) _app.SaveProjects();
        if (_openFileGroup == group.Id) _openFileGroup = null;
        ShelfEdited();
        ShowPage(ProjectsSettingsPage);
    }

    private static bool IsWithinControl(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = Ancestor(current))
            if (current is ButtonBase or TextBox or ComboBox) return true;
        return false;
    }

    private static DependencyObject? Ancestor(DependencyObject source)
        => source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(source)
            : LogicalTreeHelper.GetParent(source);
}
