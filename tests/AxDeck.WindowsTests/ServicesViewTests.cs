using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The services & tools page, built for real: the view is filled in twice, the way it is when the state is read
// every few seconds, and the page is looked at afterwards. The buttons only call the controller, so the stubs
// here stand in for it and record that nothing was asked of them.
public partial class MainWindow
{
    internal static int ServicesDiagnosticCalls { get; set; }
    internal static int ServicesResumeCalls { get; set; }
    internal void ServicesRefresh(bool probe) { }
    internal void ServicesSshTest(string host) { }
    internal void ServicesContainer(string action) { }
    internal void ServicesEditConfig() { }
    internal void ServicesOpenConfig() { }
    internal void ServicesOpenSite(CloudflareTunnel.Site site) { }
    internal void ServicesToggleForwarder(CloudflareTunnel.Site site) { }
    internal void ServicesCopyCommand(CloudflareTunnel.Site site) { }
    internal void ServicesCopyDiagnostics() { }
    internal DiagnosticAction? ServicesDiagnosticAction(DiagnosticFact fact) => AppDiagnostics.ActionFor(fact.Subject, fact.State);
    internal void ServicesDiagnostic(DiagnosticAction action) => ServicesDiagnosticCalls++;
    internal void ServicesResumeReading(SensorStop stop) => ServicesResumeCalls++;
    internal void ServicesStartBge() { }
    internal void ServicesStopBge() { }
    internal void ServicesCopyBgeApi() { }
    internal void ServicesOpenBgeLog() { }
    internal void ServicesShowBgeLog() { }
    internal void ServicesShowAxTvLog() { }
    internal bool ServicesBgeAutostartEnabled() => false;
    internal void ServicesSetBgeAutostart(bool enabled) { }
    internal void ServicesBrowseApk() { }
    internal void ServicesInstallApk() { }
    internal void ServicesAxTvStart() { }
    internal void ServicesAxTvStop() { }
    internal void ServicesAxTvForceKill() { }
    internal void ServicesAxTvRestart() { }
    internal void ServicesAxTvSwitchAvd() { }
    internal void ServicesAxTvFullscreen() { }
    internal void ServicesAxTvSmall() { }
    internal void ServicesAxTvResetWindow() { }
    internal void ServicesAxTvToggleOrientation() { }
    internal void ServicesAxTvLaunchApp() { }
    internal void ServicesAxTvStopApp() { }
    internal void ServicesAxTvUninstallApp() { }
    internal void ServicesAxTvScreenshot() { }
    internal void ServicesAxTvOpenLog() { }
}

internal static class ServicesViewTests
{
    public static void Run(Action<string, bool> check)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // The page's buttons take the app's own button style; the test app has none, so a plain one stands in.
        if (app.Resources["SegmentButton"] is null) app.Resources["SegmentButton"] = new Style(typeof(Button));

        var window = new MainWindow { Width = 600, Height = 900, Left = -20000, Top = -20000, ShowActivated = false, WindowStyle = WindowStyle.None };
        try
        {
            MainWindow.ServicesDiagnosticCalls = 0;
            MainWindow.ServicesResumeCalls = 0;
            var view = new ServicesView(window);
            window.Content = view;
            window.Show();
            Pump();

            var status = new AxTvEmulator.StatusSnapshot(
                SdkFound: true, AdbFound: true, EmulatorFound: true, DeviceOnline: true, ProcessAlive: true,
                AvdName: "axtv_tv", AvdLabel: "横屏 TV", Panel: "1920×1080", Portrait: false,
                WindowMode: "small", WindowRect: "1344x796@(288,142)", Orientation: "landscape",
                AppVersion: "0.1.8", BootCompleted: true, Detail: "ready");

            bool filled = false, filledAgain = false;
            try
            {
                view.UpdateAxTv(status, "axtv_tv");
                filled = true;
                // The state is read every few seconds, so the block is filled in again. Nothing may be taken apart
                // and put back together: the path the user typed has to stay where it is.
                view.UpdateAxTv(status, "axtv_tv");
                filledAgain = true;
            }
            catch (Exception ex) { check($"the page is filled in again without complaint ({ex.GetType().Name}: {ex.Message})", false); }

            check("the page is filled in", filled);
            check("the page is filled in again without complaint", filledAgain);

            var texts = Texts(view);
            check("the emulator's state is shown", texts.Any(t => t.Contains("axtv_tv", StringComparison.Ordinal)));
            check("the window's shape is shown", texts.Any(t => t.Contains("1344x796", StringComparison.Ordinal)));
            check("the app's version is shown", texts.Any(t => t.Contains("0.1.8", StringComparison.Ordinal)));
            check("the buttons of the emulator are there",
                texts.Any(t => t.Contains("切换 AVD", StringComparison.Ordinal)) && texts.Any(t => t.Contains("截图", StringComparison.Ordinal)));
            check("the runtime diagnostics section is shown", texts.Any(t => t.Contains("运行开销与能力", StringComparison.Ordinal)));

            var diagnostics = AppDiagnostics.Build(
                Reading<ProcessResourceUsage>.Of(new ProcessResourceUsage(0.8, TimeSpan.FromMilliseconds(250), 96UL << 20, 64UL << 20, 210, 17, 16)),
                null, new MemorySample(32UL << 30, 16UL << 30, 0, 0), 16, SensorStates.Unknown, [], DateTimeOffset.Now);
            view.UpdateDiagnostics(diagnostics);
            var firstDiagnostics = Texts(view);
            check("the app's own runtime cost is shown", firstDiagnostics.Any(t => t.Contains("96M", StringComparison.Ordinal)));
            check("an unsampled capability stays visibly unsampled",
                firstDiagnostics.Any(t => t.Contains("尚未读取", StringComparison.Ordinal)));
            check("the page names the number of automatically stopped readings",
                firstDiagnostics.Any(t => t.Contains("0 项自动停用的读数", StringComparison.Ordinal)));
            check("all eleven capability facts get their own labelled rows",
                diagnosticSubjects(view).SequenceEqual(diagnostics.Facts.Select(fact => fact.Subject)));
            check("the monitor snapshot fact is shown separately from stopped readings",
                firstDiagnostics.Any(t => t.StartsWith("尚未读取 · 系统监控当前未运行", StringComparison.Ordinal)));
            check("a stopped reading is labelled as a stopped reading",
                firstDiagnostics.Any(t => t.StartsWith("0 项自动停用的读数", StringComparison.Ordinal)));

            // Refreshing the readings only changes text. The rows stay in the same place, which keeps the page stable while
            // the Services controller updates it and avoids rebuilding a whole visual tree on every refresh.
            var diagnosticRows = DiagnosticsRows(view);
            view.UpdateDiagnostics(diagnostics with { At = diagnostics.At.AddSeconds(1) });
            check("the diagnostics are refreshed without rebuilding their rows",
                DiagnosticsRows(view).SequenceEqual(diagnosticRows));

            var facts = diagnostics.Facts;
            var confirmed = AppDiagnostics.Build(
                Reading<ProcessResourceUsage>.Unavailable("test"), null, null, 16,
                new SensorStates(SensorState.None, SensorState.None, SensorState.None, SensorState.None)
                {
                    Power = SensorState.None,
                    Charge = SensorState.None,
                    Frames = SensorState.None,
                }, [], DateTimeOffset.Now);
            view.UpdateDiagnostics(confirmed);
            var confirmedFacts = confirmed.Facts;
            var cpuTemperature = confirmedFacts.Single(fact => fact.Subject == DiagnosticSubject.CpuTemperature);
            var gpuTemperature = confirmedFacts.Single(fact => fact.Subject == DiagnosticSubject.GpuTemperature);
            var gpuTemperatureRow = DiagnosticRow(view, gpuTemperature);
            var cpuTemperatureRow = DiagnosticRow(view, cpuTemperature);
            var gpuAction = DiagnosticActionButton(gpuTemperatureRow);
            var cpuAction = DiagnosticActionButton(cpuTemperatureRow);
            check("only facts with an existing repair show an action button",
                gpuAction is { Visibility: Visibility.Visible } && cpuAction is { Visibility: Visibility.Visible }
                && DiagnosticActionButton(DiagnosticRow(view, confirmedFacts.Single(fact => fact.Subject == DiagnosticSubject.Battery))) is null);
            check("diagnostic actions have a useful keyboard and screen-reader name",
                gpuAction is not null && System.Windows.Automation.AutomationProperties.GetName(gpuAction).Contains("GPU 温度", StringComparison.Ordinal)
                && cpuAction is not null && System.Windows.Automation.AutomationProperties.GetName(cpuAction).Contains("CPU 温度", StringComparison.Ordinal));
            check("diagnostic action buttons can take keyboard focus",
                gpuAction is { Focusable: true } && cpuAction is { Focusable: true });
            if (gpuAction is not null) Click(gpuAction);
            if (cpuAction is not null) Click(cpuAction);
            check("diagnostic action buttons call the controller", MainWindow.ServicesDiagnosticCalls == 2);

            var stop = new SensorStop(GuardedReading.CpuTemperature, "CPU", "CPU", 1, DateTimeOffset.Now);
            view.UpdateDiagnostics(AppDiagnostics.Build(
                Reading<ProcessResourceUsage>.Unavailable("test"), null, null, 16, SensorStates.Unknown, [stop], DateTimeOffset.Now));
            var resume = VisualTreeOf<Button>(view)
                .FirstOrDefault(button => System.Windows.Automation.AutomationProperties.GetName(button).StartsWith("重新开启：", StringComparison.Ordinal));
            check("a stopped reading keeps a direct resume action", resume is { Visibility: Visibility.Visible });
            if (resume is not null) Click(resume);
            check("the resume action calls the controller", MainWindow.ServicesResumeCalls == 1);

            // The path the user put in survives the next reading.
            view.SetApkPath(@"C:\Users\someone\Downloads\AxTV-0.1.8-x86_64.apk");
            view.UpdateAxTv(status, "axtv_tv");
            check("the path of the package stays where the user put it",
                view.ApkPath == @"C:\Users\someone\Downloads\AxTV-0.1.8-x86_64.apk");
        }
        finally { window.Close(); }
    }

    // Lets the layout run, so the page is measured before it is looked at.
    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    // Every piece of text on the page.
    private static IReadOnlyList<string> Texts(DependencyObject root)
    {
        var list = new List<string>();
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock block) list.Add(block.Text);
            list.AddRange(Texts(child));
        }
        return list;
    }

    // The diagnostics rows are the only Grids in the StackPanel under the "Runtime cost and capabilities" heading. Keeping
    // references to the rows lets the test prove that a text refresh did not dismantle the visual tree.
    private static IReadOnlyList<object> DiagnosticsRows(DependencyObject root)
        => VisualTreeOf<Grid>(root).Where(grid => grid.Tag is "DiagnosticFact").Cast<object>().ToList();

    private static IReadOnlyList<DiagnosticSubject> diagnosticSubjects(DependencyObject root)
        => VisualTreeOf<Grid>(root).Where(grid => grid.Tag is "DiagnosticFact")
            .Select(grid => grid.Children.OfType<TextBlock>().First().Text)
            .Select(label => Enum.GetValues<DiagnosticSubject>().Single(subject => AppDiagnostics.SubjectName(subject) == label))
            .ToList();

    private static Grid DiagnosticRow(DependencyObject root, DiagnosticFact fact)
        => VisualTreeOf<Grid>(root).First(grid => grid.Children.OfType<TextBlock>()
            .Any(block => block.Text == AppDiagnostics.SubjectName(fact.Subject)));

    private static Button? DiagnosticActionButton(DependencyObject root)
        => VisualTreeOf<Button>(root).FirstOrDefault(button => button.Visibility != Visibility.Collapsed);

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
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
