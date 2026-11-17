using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The output device: the panel's button on the music page lists the machine's playback devices, and picking one puts it in
// charge of the sound. The devices are read off the UI thread, because the audio service can take a moment to answer.
public partial class MainWindow
{
    private bool _audioOutputReading;

    private void InitializeAudioOutput()
    {
        AudioOutputButton.ContextMenu = null;
    }

    private void AudioOutput_Click(object sender, RoutedEventArgs e)
    {
        // The button opens its own list; the same button closes it again.
        if (AudioOutputButton.ContextMenu is { IsOpen: true } open) { open.IsOpen = false; return; }
        if (_audioOutputReading) return;
        _audioOutputReading = true;
        var generation = ++_audioOutputGeneration;
        // Reading the devices may wait for the audio service, so it happens off the UI thread.
        Task.Run(() => Native.AudioOutputs.List()).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _audioOutputReading = false;
                if (generation != _audioOutputGeneration || !IsVisible || _app.Exiting) return;
                if (task.IsFaulted || task.Result.Count == 0) { ShowAudioOutputEmpty(); return; }
                ShowAudioOutputMenu(task.Result);
            });
        }, TaskScheduler.Default);
    }

    private int _audioOutputGeneration;

    // The list of the devices, with the one in use marked.
    private void ShowAudioOutputMenu(IReadOnlyList<AudioOutputDevice> devices)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("MediaSourceMenu") };
        foreach (var device in devices)
        {
            var label = new TextBlock { Text = device.Label, FontSize = 12.5, Foreground = UI.Brush(UI.Primary),
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var item = new MenuItem { Style = (Style)FindResource("AudioOutputMenuItem"), Header = label, IsCheckable = true,
                IsChecked = device.IsDefault, IsEnabled = device.Ready };
            AutomationProperties.SetName(item, device.Label);
            item.Click += (_, _) => SwitchAudioOutput(device);
            menu.Items.Add(item);
        }
        UI.Scale(menu, _scale); menu.VerticalOffset = 6 * _scale;
        menu.Width = Math.Min(280, Math.Max(150, ContentCanvas.Width - 40));
        AudioOutputButton.ContextMenu = menu; menu.PlacementTarget = AudioOutputButton; menu.Placement = PlacementMode.Bottom;
        menu.Closed += (_, _) => { if (ReferenceEquals(AudioOutputButton.ContextMenu, menu)) AudioOutputButton.ContextMenu = null; };
        menu.IsOpen = true;
    }

    // No device to choose from: the audio service is not there, or the machine has no playback device at all.
    private void ShowAudioOutputEmpty()
    {
        var label = new TextBlock { Text = Loc.T("没有找到音频输出设备", "No playback device found"), FontSize = 12.5,
            Foreground = UI.Brush(UI.Primary), TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        var menu = new ContextMenu { Style = (Style)FindResource("MediaSourceMenu") };
        var item = new MenuItem { Style = (Style)FindResource("AudioOutputMenuItem"), Header = label, IsEnabled = false };
        AutomationProperties.SetName(item, label.Text);
        menu.Items.Add(item);
        UI.Scale(menu, _scale); menu.VerticalOffset = 6 * _scale;
        menu.Width = Math.Min(280, Math.Max(150, ContentCanvas.Width - 40));
        AudioOutputButton.ContextMenu = menu; menu.PlacementTarget = AudioOutputButton; menu.Placement = PlacementMode.Bottom;
        menu.Closed += (_, _) => { if (ReferenceEquals(AudioOutputButton.ContextMenu, menu)) AudioOutputButton.ContextMenu = null; };
        menu.IsOpen = true;
    }

    // Puts another device in charge. The switch may take a moment, so it is made off the UI thread; the list is read again
    // afterwards, so the device in use is the one marked.
    private void SwitchAudioOutput(AudioOutputDevice device)
    {
        Task.Run(() => Native.AudioOutputs.Use(device.Id)).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsVisible || _app.Exiting) return;
                // The list is rebuilt with the device that is in use now.
                AudioOutput_Click(this, new RoutedEventArgs());
            });
        }, TaskScheduler.Default);
    }
}
