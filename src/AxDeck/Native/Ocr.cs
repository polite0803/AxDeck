using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AxDeck.Native;

// The text in a picture, read with the system's own recognizer (Windows.Media.Ocr). It works offline, on the machine, and
// nothing is sent anywhere. The recognizer understands the languages the system has installed; where there is none, the
// page says so rather than guessing.
internal static class Ocr
{
    // The text a picture holds, as the lines the recognizer found. Empty when the system has no recognizer for the picture's
    // language, or when the picture holds no text.
    public static async Task<(IReadOnlyList<string> Lines, bool Supported)> ReadAsync(BitmapSource picture)
    {
        var lines = new List<string>();
        try
        {
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null) return (lines, false);
            var software = await GetSoftwareBitmapAsync(picture);
            if (software is null) return (lines, false);
            using (software)
            {
                var result = await engine.RecognizeAsync(software);
                foreach (var line in result.Lines)
                {
                    string text = line.Text;
                    if (!string.IsNullOrWhiteSpace(text)) lines.Add(text);
                }
            }
            return (lines, true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return (lines, false); }
    }

    // The picture as the recognizer takes it: the picture is written out as a picture and read back by the system's decoder,
    // which gives the recognizer the bitmap it wants.
    private static async Task<Windows.Graphics.Imaging.SoftwareBitmap?> GetSoftwareBitmapAsync(BitmapSource picture)
    {
        byte[] png;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using (var buffer = new MemoryStream())
        {
            encoder.Save(buffer);
            png = buffer.ToArray();
        }
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync();
    }
}
