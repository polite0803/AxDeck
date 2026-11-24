using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AxDeck.Core;

namespace AxDeck;

// 「软件更新」页的设置：自动检查的开关，和一次立即检查。尺寸由「展开外观」里这一页的行给出，开关页
// （SoftwarePage）也在那里。
// 这里只读面板已经拿到的状态，不自己跑 winget：同一个检查只有一个入口，结果也只有一份（software-updates.json）。
public partial class SettingsWindow
{
    private const int SoftwareSettingsPage = 18;

    private void SoftwareSettings()
    {
        var status = TextLine(StatusText(_app.Deck.SoftwareState), 11.5, TextTertiary);
        status.TextWrapping = TextWrapping.Wrap;
        Button check = null!;
        check = ActionButton(Loc.T("检查", "Check"), () => CheckSoftware(check, status));
        AutomationProperties.SetName(check, Loc.T("立即检查更新", "Check for updates now"));

        SectionTitle(Loc.T("检查更新", "Checking for updates"));
        Group(
            Toggle(Loc.T("自动检查更新", "Check for updates automatically"), S.SoftwareAutoCheck,
                value => Change(s => s.SoftwareAutoCheck = value),
                help: Loc.T("打开后，AxDeck 启动约 90 秒后检查一次，之后每 6 小时一次；检查失败时按 15 分钟、1 小时、4 小时重试。每次检查要跑一次 WinGet，首次约需 1～2 分钟，全程在后台。",
                    "When on, a check runs about 90 seconds after AxDeck starts, then every 6 hours; a failed check is retried after 15 minutes, an hour, then four. Each check runs WinGet once, about 1–2 minutes the first time, in the background.")),
            Row(Loc.T("立即检查", "Check now"), check, status));

        // 这一页读了什么、发出哪些请求，写在这里：面板上没有别的地方能说明。
        SectionTitle(Loc.T("数据来源", "Where the list comes from"));
        var sources = new StackPanel();
        foreach (var line in new[]
        {
            Loc.T("WinGet：已安装应用里 WinGet 认得出、且有新版的，给出可更新版本，可以在这里点一次升级。",
                "WinGet: the installed applications WinGet recognises and has a newer version of, with the new version and a one-click upgrade."),
            Loc.T("厂商渠道：WinGet 认不出的常见应用（如 VS Code、Firefox、Obsidian），向应用自己的公开版本接口问一次最新版本号。问到的只用来比较，不能静默安装，按钮会打开下载页。",
                "Vendor channel: for common applications WinGet does not recognise (VS Code, Firefox, Obsidian, ...), the application's own public version endpoint is asked for its latest version number. It is only compared, never installed silently; the button opens the download page."),
            Loc.T("已安装应用清单：只在本机读注册表与快捷方式，用来知道装了什么，不上传。",
                "Installed list: read only on this machine, from the registry and shortcuts, to know what is installed. Nothing is uploaded."),
            Loc.T("出站请求只有上面这些公开的版本接口，不带任何标识，并照常走系统代理。",
                "The only outgoing requests are those public version endpoints. They carry no identifier and use the system proxy as usual."),
        })
        {
            var text = TextLine(line, 12, TextSecondary);
            text.TextWrapping = TextWrapping.Wrap;
            text.Margin = new Thickness(0, 0, 0, 8);
            sources.Children.Add(text);
        }
        Target.Children.Add(sources);
    }

    // 上一次检查的结论，四句话分得开：没装 winget、失败、有结果、还没查过，各自不能读成别的。
    private string StatusText(SoftwareUpdateState state) => state switch
    {
        { MissingTool: true } => Loc.T("未检测到 WinGet。安装「应用安装程序」后即可检查。", "WinGet was not found. Install App Installer to check."),
        { Failure: { Length: > 0 } failure } => Loc.T($"上次检查失败：{failure}", $"The last check failed: {failure}"),
        { CheckedAt: { } at } => Loc.T($"上次检查：{at:yyyy-MM-dd HH:mm}，共 {state.Updates.Count} 项可更新。", $"Last checked {at:yyyy-MM-dd HH:mm}: {state.Updates.Count} updates available."),
        _ => Loc.T("还没有检查过。", "No check has run yet."),
    };

    // async void 在事件处理里可以接受：CheckSoftwareNow 自己收住所有异常，这里只负责按钮与那一行字的来回。
    private async void CheckSoftware(Button check, TextBlock status)
    {
        check.IsEnabled = false;
        check.Content = Loc.T("检查中…", "Checking…");
        status.Text = Loc.T("正在检查，首次约需 1～2 分钟…", "Checking; the first time takes 1–2 minutes…");
        var state = await _app.Deck.CheckSoftwareNow();
        if (!IsLoaded) return;
        check.IsEnabled = true;
        check.Content = Loc.T("检查", "Check");
        status.Text = StatusText(state);
    }

    private void SoftwareSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) }; dimensions.ColumnDefinitions.Add(new()); dimensions.ColumnDefinitions.Add(new());
        var width = Dimension(Loc.T("宽度", "Width"), S.SoftwareWidth, 440, 1200, value => Resize(s => s.SoftwareWidth = value, DeckPage.Software)); width.Margin = new Thickness(0, 0, 20, 0); dimensions.Children.Add(width);
        var height = Dimension(Loc.T("高度", "Height"), S.SoftwareHeight, 140, 400, value => Resize(s => s.SoftwareHeight = value, DeckPage.Software)); height.Margin = new Thickness(20, 0, 0, 0); Grid.SetColumn(height, 1); dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }
}
