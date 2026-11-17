using System.Windows.Media;
using System.Windows.Media.Imaging;
using AxDeck.Core;
using ZXing;
using ZXing.Common;

namespace AxDeck;

// The QR codes of the clipboard page: a picture turned into a code, and a code read back out of a picture. The drawing is
// done here, on the panel's own side; what is worth a code, and how a code is described, is decided in Core (QrCodes).
internal static class QrCodeImage
{
    // The code of a text, as a picture. Null when the text cannot make one.
    public static BitmapSource? Encode(string text, int size)
    {
        if (!QrCodes.Worthy(text)) return null;
        try
        {
            var writer = new BarcodeWriterGeneric
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions { Width = size, Height = size, Margin = 1, PureBarcode = true }
            };
            var matrix = writer.Encode(QrCodes.Payload(text));
            int width = matrix.Width, height = matrix.Height;
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // A dark module is black; the rest is white and opaque, so the picture reads as a code and not as ink on
                    // a transparent ground.
                    byte ink = matrix[x, y] ? (byte)0 : (byte)255;
                    int at = (y * width + x) * 4;
                    pixels[at] = ink; pixels[at + 1] = ink; pixels[at + 2] = ink; pixels[at + 3] = 255;
                }
                var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                bitmap.Freeze();
                return bitmap;
            }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    // The code a picture holds, read out of it. Null when the picture holds none.
    public static string? Decode(BitmapSource picture)
    {
        try
        {
            var (pixels, width, height) = Luminance(picture);
            if (width <= 0 || height <= 0) return null;
            var source = new RGBLuminanceSource(pixels, width, height);
            var reader = new BarcodeReaderGeneric { Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] } };
            var result = reader.Decode(source);
            return result?.Text;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    // The picture's grey levels, as the reader takes them.
    private static (byte[] Pixels, int Width, int Height) Luminance(BitmapSource picture)
    {
        var converted = picture.Format == PixelFormats.Bgra32 ? picture : new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth; var height = converted.PixelHeight;
        var stride = width * 4;
        var raw = new byte[stride * height];
        converted.CopyPixels(raw, stride, 0);
        var grey = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = y * stride + x * 4;
                // The green channel stands in for the grey level: it is the least noisy of the three.
                grey[y * width + x] = raw[at + 1];
            }
        return (grey, width, height);
    }
}
