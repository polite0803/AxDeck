using System.Text;

namespace AxDeck.Core;

// The QR codes of the clipboard page: a piece of text made into a code that a phone can read, and a code read back out of a
// picture. What is worth a code, and what a code means once it is read, are decided here, so they can be tested without a
// camera or a screen.
public static class QrCodes
{
    // The longest text a code carries well. The code grows with the text, and a long one becomes too dense to be read by a
    // phone from a screen, so the page says so rather than making a code nobody can read.
    public const int MaximumLength = 512;

    // Whether the text is worth a code: not empty, and not so long that the code would be unreadable.
    public static bool Worthy(string? text) => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= MaximumLength;

    // What is wrong with the text, in the words the page shows. Null when nothing is.
    public static string? Refusal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Loc.T("没有内容可以生成二维码", "There is nothing to make a code from");
        if (text.Trim().Length > MaximumLength)
            return Loc.T($"内容太长（{text.Trim().Length} 字，二维码最多 {MaximumLength} 字）",
                $"The text is too long ({text.Trim().Length} characters, a code holds {MaximumLength})");
        return null;
    }

    // What a code holds, read as the page shows it: a link as a link, anything else as the text itself.
    public static string Describe(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return Loc.T("空内容", "Empty");
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return trimmed;
        return trimmed;
    }

    // Whether the text reads as a link, so the page can offer to open it.
    public static bool IsLink(string text)
    {
        string trimmed = text.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    // The text a code carries: the same text, without the ends the user does not need.
    public static string Payload(string text) => text.Trim();
}
