using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AxDeck.Core;

namespace AxDeck;

// The production view's controller calls are routed to this test double, so no project folder, service host, clipboard or
// setting is touched. The production MainWindow methods themselves stay linked from src/AxDeck/MainWindow.Work.cs.
internal sealed class WorkViewHost : IWorkViewHost
{
    internal int ProjectOpens { get; private set; }
    internal int ProjectRestores { get; private set; }
    internal int ServiceCommands { get; private set; }
    internal int FocusStarts { get; private set; }
    internal string? LastPastedCommand { get; private set; }
    public IReadOnlyList<ProjectServiceOption> WorkServices { get; set; } = [];

    public void WorkOpenProject(Guid id) => ProjectOpens++;
    public void WorkRestoreProject(Guid id) => ProjectRestores++;
    public void WorkFocusStart(Guid? projectId = null) => FocusStarts++;
    public void WorkFocusReset() { }
    public ProjectEntry? WorkProject(Guid? id) => null;
    public string? WorkServiceCommand(Guid id) => "cloudflared access tcp --hostname db.example.com";
    public void WorkPasteServiceCommand(string command)
    {
        LastPastedCommand = command;
        ServiceCommands++;
    }
    public void WorkOpenSettings() { }
    public void WorkPasteClip(Guid id) { }
    public void WorkPasteGroup(Guid id) { }
    public void FocusOpen() { }
}

internal static class WorkViewTests
{
    public static void Run(Action<string, bool> check)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (app.Resources["SegmentButton"] is null) app.Resources["SegmentButton"] = new Style(typeof(Button));

        var window = new MainWindow { Width = 640, Height = 720, Left = -20000, Top = -20000, ShowActivated = false, WindowStyle = WindowStyle.None };
        try
        {
            var project = new ProjectEntry
            {
                Id = Guid.NewGuid(), DisplayName = "AxDeck", RootPath = @"D:\work\AxDeck",
                WorkSource = ProviderId.Codex, ServiceHost = "db.example.com",
            };
            var options = ProjectServices.Options(new ServicesConfig
            {
                Tunnel = new TunnelConfig { Sites = [new TunnelSite { Host = "db.example.com", Kind = "tcp", Label = "Database", Port = 15432 }] },
            });
            var host = new WorkViewHost { WorkServices = options };

            var now = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(8));
            var timer = new FocusTimer();
            timer.Start(now.AddHours(-1), 25, "Morning", [ProviderId.Codex], project.Id);
            timer.Sample(now.AddMinutes(-50), [ProviderId.Codex],
                [new SessionActivitySnapshot(ProviderId.Codex, SessionActivityState.Active, now.AddMinutes(-50), 4, 1)]);
            timer.Finish(now.AddMinutes(-35));
            var overview = WorkOverviewBuilder.Build(timer, [project], now);
            var view = new WorkView(host);
            view.Update(timer, overview, [project],
                [new SessionActivitySnapshot(ProviderId.Codex, SessionActivityState.Active, now.AddMinutes(-5), 4, 1)],
                [new ClipEntry { Text = "check the tunnel", Copied = now.AddMinutes(-3) }], []);
            window.Content = view;
            window.Show();
            Pump();

            var texts = Texts(view);
            check("the work page shows today's local focus account",
                texts.Any(text => text.Contains("今日工作态势", StringComparison.Ordinal))
                && texts.Any(text => text.Contains("1 次专注", StringComparison.Ordinal))
                && texts.Any(text => text.Contains("目标 25 分钟", StringComparison.Ordinal)));
            check("the work page names the source with real session activity",
                texts.Any(text => text.Contains("Codex", StringComparison.Ordinal)));
            check("the work page names the explicitly linked service site",
                texts.Any(text => text.Contains("Database", StringComparison.Ordinal)));

            var firstRows = VisualTreeOf<Grid>(view).Count(grid => grid.ColumnDefinitions.Count == 2 && grid.Margin == new Thickness(0, 1, 0, 1));
            view.Update(timer, overview, [project],
                [new SessionActivitySnapshot(ProviderId.Codex, SessionActivityState.Active, now.AddMinutes(-4), 5, 2)],
                [new ClipEntry { Text = "check the tunnel", Copied = now.AddMinutes(-3) }], []);
            Pump();
            check("the work page refreshes its rows in place",
                VisualTreeOf<Grid>(view).Count(grid => grid.ColumnDefinitions.Count == 2 && grid.Margin == new Thickness(0, 1, 0, 1)) == firstRows && texts.Count > 0);

            var buttons = VisualTreeOf<Button>(view)
                .Where(button => button.Visibility == Visibility.Visible && System.Windows.Automation.AutomationProperties.GetName(button) is { Length: > 0 })
                .ToList();
            var open = buttons.FirstOrDefault(button => System.Windows.Automation.AutomationProperties.GetName(button)!.Contains("打开项目", StringComparison.Ordinal));
            var restore = buttons.FirstOrDefault(button => System.Windows.Automation.AutomationProperties.GetName(button)!.Contains("恢复工作", StringComparison.Ordinal));
            var service = buttons.FirstOrDefault(button => System.Windows.Automation.AutomationProperties.GetName(button)!.Contains("复制 Database", StringComparison.Ordinal));
            check("project actions have screen-reader names", open is not null && restore is not null && service is not null);
            if (open is not null) Click(open);
            if (restore is not null) Click(restore);
            if (service is not null) Click(service);
            check("project actions reach their controllers without launching anything",
                host.ProjectOpens == 1 && host.ProjectRestores == 1
                && host.ServiceCommands == 1 && host.LastPastedCommand == "cloudflared access tcp --hostname db.example.com");
        }
        finally { window.Close(); }
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private static List<string> Texts(DependencyObject root)
    {
        var result = new List<string>();
        foreach (var block in VisualTreeOf<TextBlock>(root))
            if (!string.IsNullOrWhiteSpace(block.Text)) result.Add(block.Text);
        return result;
    }

    private static IEnumerable<T> VisualTreeOf<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in VisualTreeOf<T>(child)) yield return nested;
        }
    }
}
