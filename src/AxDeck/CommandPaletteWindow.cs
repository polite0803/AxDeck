using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// The command palette: a small window with a text field in it, where a few words are enough to reach any page or action of
// the panel. The commands themselves are data (CommandPalette in AxDeck.Core); this window shows them and runs the one that is
// chosen.
//
// The window stands on its own, above the panel: the panel keeps its own keyboard, and the palette takes the keyboard while
// it is open. It closes on Esc and when a command has been run.
internal sealed class CommandPaletteWindow : Window
{
    private readonly App _app;
    private readonly MainWindow _deck;
    private readonly SettingsPalette _palette;
    private readonly TextBox _box;
    private readonly TextBlock _hint;
    private readonly StackPanel _list;
    private readonly List<PaletteCommand> _commands;
    private List<PaletteCommand> _results = [];
    private int _selected;

    public CommandPaletteWindow(App app, MainWindow deck, string? query = null)
    {
        _app = app;
        _deck = deck;
        _commands = CommandPalette.Build(app.Settings, app.PaletteContext());
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AxDeck;component/SettingsResources.xaml", UriKind.Relative) });
        _palette = new SettingsPalette(SettingsPalette.IsDark(app.Settings.Theme));
        _palette.Apply(Resources);
        Style = (Style)FindResource(typeof(Window));
        Title = Loc.T("命令面板", "Command palette");
        Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
        FontFamily = UI.PanelFont; Background = UI.Brush(_palette.Surface); Foreground = UI.Brush(_palette.Primary);
        Native.SettingsChrome.Apply(this);
        SourceInitialized += (_, _) => Native.SettingsChrome.SetTheme(this, _palette.Dark);

        var body = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };

        _box = new TextBox { Style = (Style)FindResource("SettingsField"), Height = 38, FontSize = 14 };
        AutomationProperties.SetName(_box, Loc.T("搜索命令", "Search commands"));
        if (!string.IsNullOrEmpty(query)) _box.Text = query;
        _box.TextChanged += (_, _) => { _selected = 0; Refresh(); };
        _box.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down: Step(1); e.Handled = true; break;
                case Key.Up: Step(-1); e.Handled = true; break;
                case Key.Enter: Run(); e.Handled = true; break;
                case Key.Escape: Close(); e.Handled = true; break;
            }
        };
        body.Children.Add(_box);

        _hint = UI.Line(Loc.T("输入命令或页面的名字", "Type a command or a page name"), 11.5, _palette.Tertiary);
        _hint.Margin = new Thickness(2, 8, 0, 0);
        body.Children.Add(_hint);

        _list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(_list);

        Content = new Border { Background = UI.Brush(_palette.Surface), Child = body };
        Refresh();
        Loaded += (_, _) => { Activate(); _box.Focus(); Keyboard.Focus(_box); };
    }

    // The commands that answer the query, with the chosen one marked.
    private void Refresh()
    {
        _hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _results = CommandPalette.Search(_commands, _box.Text);
        _list.Children.Clear();
        if (_results.Count == 0)
        {
            var none = UI.Line(Loc.T("没有找到匹配的命令", "No matching command"), 12.5, _palette.Tertiary);
            none.Margin = new Thickness(2, 8, 0, 8);
            _list.Children.Add(none);
            return;
        }
        for (int i = 0; i < _results.Count; i++)
        {
            var command = _results[i];
            bool chosen = i == _selected;
            var row = new Grid();
            var mark = UI.Line(chosen ? "›" : "", 13, _palette.Accent);
            mark.Margin = new Thickness(4, 0, 6, 0);
            mark.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(mark);
            var title = UI.Line(command.Title, 13, chosen ? _palette.Primary : _palette.Secondary);
            title.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(title);
            var hit = new Border { Background = chosen ? UI.Brush(_palette.Dark ? "#25252B" : "#EFF0F4") : Brushes.Transparent, Padding = new Thickness(2, 7, 2, 7), CornerRadius = new CornerRadius(6), Child = row };
            var button = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Content = hit, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => { _selected = i; Run(); };
            AutomationProperties.SetName(button, command.Title);
            _list.Children.Add(button);
        }
    }

    // The chosen command moves down or up, and stays within the list.
    private void Step(int step)
    {
        if (_results.Count == 0) return;
        _selected = (_selected + step + _results.Count) % _results.Count;
        Refresh();
        _box.Focus();
    }

    // Runs the chosen command and closes the window.
    private void Run()
    {
        if (_selected < 0 || _selected >= _results.Count) return;
        var command = _results[_selected];
        Close();
        _deck.PaletteRun(command);
    }
}
