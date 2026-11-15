using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// 分组需要问用户的事：一个新名字、一个可见度、一次删除确认。都做成小窗口，样子跟设置窗口一样。
internal static class BoxDialogs
{
    // 给分组起一个新名字。名字只是窗口上写着的那几个字，不动真实文件夹的名字。
    public static void Rename(Window owner, BoxService boxes, DesktopBox box, Action rebuild)
    {
        var dialog = new BoxNameDialog(Loc.T("重命名分组", "Rename the box"), Loc.T("新名字", "New name"), box.Title);
        if (!dialog.Accepted) return;
        var name = dialog.NewName;
        if (name.Length == 0) return;
        box.Title = name;
        boxes.Save(box);
        rebuild();
    }

    // 鼠标不在分组上时它有多淡。0 是完全看不见，鼠标回到它身上时又全显出来。
    public static void Opacity(Window owner, BoxService boxes, DesktopBox box, Action rebuild)
    {
        var dialog = new BoxDialog(Loc.T("闲置时可见度", "Opacity when idle"),
            Loc.T("鼠标离开分组后它剩下的不透明度。0 表示完全看不见，鼠标回到它身上时又会全部显出来。",
                "How much of the box stays when the mouse is elsewhere. 0 hides it completely; it comes back in full when the mouse returns."));
        var slider = dialog.AddSlider(box.TitleOpacity, 0, 100);
        var readout = dialog.AddReadout(box.TitleOpacity);
        slider.ValueChanged += (_, _) => readout.Text = ((int)Math.Round(slider.Value)) + "%";
        dialog.AddConfirm(Loc.T("保存", "Save"));
        dialog.Show(owner);
        if (!dialog.Accepted) return;
        box.TitleOpacity = (int)Math.Round(slider.Value);
        boxes.Save(box);
        rebuild();
    }

    // 删掉一个分组之前先说清楚它的真实文件夹会怎么样，用户自己决定。
    public static void Delete(Window owner, BoxService boxes, DesktopBox box)
    {
        var policy = boxes.Settings.Delete;
        var what = policy switch
        {
            BoxDeletePolicy.MoveContentsToDesktop => Loc.T("分组里的文件会移回桌面。", "The files in the box will move back to the desktop."),
            BoxDeletePolicy.KeepFolder => Loc.T("分组的真实文件夹会留在原处，不会删除。", "The box's real folder will be kept where it is; nothing is deleted."),
            _ => Loc.T("分组的真实文件夹会进回收站。", "The box's real folder will go to the recycle bin.")
        };
        var message = Loc.T($"确定删除分组“{box.Title}”？", $"Delete the box \"{box.Title}\"?") + "\n" + what;
        if (!BoxReport.Confirm(owner, Loc.T("删除分组", "Delete the box"), message, Loc.T("删除", "Delete"))) return;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        boxes.Delete(box, desktop);
        BoxWindows.Close(box.Id);
    }
}

// 小窗口的骨架：一个标题、一段说明、若干内容，最后是取消和确认。
internal class BoxDialog : Window
{
    private readonly SettingsPalette _palette;
    private readonly StackPanel _body = new();
    private readonly StackPanel _buttons;
    public bool Accepted { get; private set; }

    public BoxDialog(string title, string subtitle)
    {
        _palette = new SettingsPalette(SettingsPalette.IsDark(SettingsTheme.System));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AxDeck;component/SettingsResources.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AxDeck;component/NeteaseControlResources.xaml", UriKind.Relative) });
        _palette.Apply(Resources);
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = UI.PanelFont;
        Background = UI.Brush(_palette.Surface);
        Foreground = UI.Brush(_palette.Primary);
        Native.SettingsChrome.Apply(this);
        SourceInitialized += (_, _) => Native.SettingsChrome.SetTheme(this, _palette.Dark);

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = UI.Brush(_palette.Primary),
            TextWrapping = TextWrapping.Wrap
        };
        heading.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(heading);
        if (subtitle.Length > 0)
        {
            var note = new TextBlock
            {
                Text = subtitle,
                FontSize = 12.5,
                Foreground = UI.Brush(_palette.Secondary),
                TextWrapping = TextWrapping.Wrap
            };
            note.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(note);
        }
        panel.Children.Add(_body);
        _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = Loc.T("取消", "Cancel"), Style = (Style)FindResource("SettingsOutlineButton"), IsCancel = true };
        AutomationProperties.SetName(cancel, Loc.T("取消", "Cancel"));
        cancel.Click += (_, _) => { Accepted = false; Close(); };
        _buttons.Children.Add(cancel);
        panel.Children.Add(_buttons);

        var frame = new Border { BorderThickness = new Thickness(1), Child = panel };
        frame.SetResourceReference(Border.BorderBrushProperty, "SettingsBorder");
        frame.SetResourceReference(Border.BackgroundProperty, "SettingsSurface");
        Content = frame;
        Loaded += (_, _) => cancel.Focus();
    }

    protected SettingsPalette Palette => _palette;
    protected StackPanel Body => _body;

    // 一个输入框。
    public TextBox AddField(string value)
    {
        var box = new TextBox
        {
            Text = value,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13,
            FontFamily = UI.PanelFont,
            Foreground = UI.Brush(_palette.Primary),
            CaretBrush = UI.Brush(_palette.Primary),
            Background = UI.Brush(_palette.Input)
        };
        box.SetResourceReference(Control.BorderBrushProperty, "SettingsInputBorder");
        _body.Children.Add(box);
        return box;
    }

    // 一根滑块。
    public Slider AddSlider(double value, double min, double max)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            TickFrequency = 5,
            Width = 372,
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = UI.Brush(_palette.Accent)
        };
        _body.Children.Add(slider);
        return slider;
    }

    // 滑块右边那个跟着走的数字。
    public TextBlock AddReadout(int value)
    {
        var readout = new TextBlock
        {
            Text = value + "%",
            FontSize = 12.5,
            Foreground = UI.Brush(_palette.Secondary),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _body.Children.Add(readout);
        return readout;
    }

    // 确认按钮：按下它，这扇窗就带着 Accepted = true 关上。
    public Button AddConfirm(string label)
    {
        var confirm = new Button { Content = label, Style = (Style)FindResource("SettingsPrimaryButton"), Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(confirm, label);
        confirm.Click += (_, _) => { Accepted = true; Close(); };
        _buttons.Children.Add(confirm);
        return confirm;
    }

    // 窗口在它自己的主人面前打开；没有主人时开在屏幕中间。
    public void Show(Window? owner)
    {
        if (owner is not null) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowDialog();
    }
}

// 一个问名字的小窗口：名字对不对，当场就能改。
internal sealed class BoxNameDialog : BoxDialog
{
    private readonly TextBox _input;

    public BoxNameDialog(string title, string subtitle, string value) : base(title, subtitle)
    {
        _input = AddField(value);
        _input.SelectAll();
        var confirm = AddConfirm(Loc.T("保存", "Save"));
        confirm.IsEnabled = value.Trim().Length > 0;
        _input.TextChanged += (_, _) => confirm.IsEnabled = _input.Text.Trim().Length > 0;
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || !confirm.IsEnabled) return;
            confirm.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        };
    }

    // 用户给的名字。
    public string NewName => _input.Text.Trim();
}
