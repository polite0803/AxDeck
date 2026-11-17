using AxDeck.Core;

// The QR codes of the clipboard page: what is worth a code, and how a code is described once it is read.
static class QrCodeTests
{
    public static void Run(Action<string, bool> check)
    {
        check("QrCode: a text is worth a code", QrCodes.Worthy("hello"));
        check("QrCode: an empty text is not", !QrCodes.Worthy("") && !QrCodes.Worthy("   ") && !QrCodes.Worthy(null));
        check("QrCode: a very long text is not", !QrCodes.Worthy(new string('x', QrCodes.MaximumLength + 1)));
        check("QrCode: a text of the limit is", QrCodes.Worthy(new string('x', QrCodes.MaximumLength)));
        check("QrCode: an empty text is refused with a reason", QrCodes.Refusal("") is not null);
        check("QrCode: a long text is refused with a reason", QrCodes.Refusal(new string('x', QrCodes.MaximumLength + 1)) is not null);
        check("QrCode: a good text is not refused", QrCodes.Refusal("hello") is null);
        check("QrCode: a link reads as a link", QrCodes.IsLink("https://example.com/a"));
        check("QrCode: plain text is not a link", !QrCodes.IsLink("just some words"));
        check("QrCode: a link is described as itself", QrCodes.Describe("https://example.com") == "https://example.com");
        check("QrCode: the ends of the text are dropped", QrCodes.Payload("  hello  ") == "hello");
    }
}
