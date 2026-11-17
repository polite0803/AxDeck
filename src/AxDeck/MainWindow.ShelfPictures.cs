using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The clipboard page's pictures: the code a picture holds, the text in it, and the code a text makes. The reading happens off
// the UI thread, because the recognizer takes its time and the panel must not stall while it works.
public partial class MainWindow
{
    private bool _pictureReading;

    // The picture's code and text, shown in a window of their own.
    private void ReadPicture(ClipEntry clip)
    {
        if (_pictureReading) return;
        if (_app.Shelf.Picture(clip.Id) is not { } picture) { Flash(clip.Id, Loc.T("图片已不在", "The picture is gone")); return; }
        _pictureReading = true;
        var generation = ++_pictureGeneration;
        Task.Run(() => QrCodeImage.Decode(picture)).ContinueWith(codeTask =>
        {
            Native.Ocr.ReadAsync(picture).ContinueWith(ocrTask =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _pictureReading = false;
                    if (generation != _pictureGeneration || _app.Exiting) return;
                    var code = codeTask.IsFaulted ? null : codeTask.Result;
                    var lines = ocrTask.IsFaulted ? [] : ocrTask.Result.Lines;
                    var supported = !ocrTask.IsFaulted && ocrTask.Result.Supported;
                    var window = new PictureReaderWindow(Settings.Theme, picture, code, lines, supported) { Owner = this };
                    window.ShowDialog();
                });
            }, TaskScheduler.Default);
        }, TaskScheduler.Default);
    }

    private int _pictureGeneration;

    // The code a text makes, shown in a window of its own, so a phone can read it off the screen.
    private void ShowQrCode(ClipEntry clip)
    {
        if (QrCodes.Refusal(clip.Text) is { } refusal) { Flash(clip.Id, refusal); return; }
        if (QrCodeImage.Encode(clip.Text, 360) is not { } code) { Flash(clip.Id, Loc.T("无法生成二维码", "Could not make a code")); return; }
        var window = new QrCodeWindow(Settings.Theme, code, clip.Text) { Owner = this };
        window.ShowDialog();
    }
}
