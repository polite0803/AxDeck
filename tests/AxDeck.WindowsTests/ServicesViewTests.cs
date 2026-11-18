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
    internal void ServicesRefresh(bool probe) { }
    internal void ServicesSshTest(string host) { }
    internal void ServicesContainer(string action) { }
    internal void ServicesEditConfig() { }
    internal void ServicesOpenConfig() { }
    internal void ServicesOpenSite(CloudflareTunnel.Site site) { }
    internal void ServicesToggleForwarder(CloudflareTunnel.Site site) { }
    internal void ServicesCopyCommand(CloudflareTunnel.Site site) { }
    internal void ServicesCopyDiagnostics() { }
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
}
