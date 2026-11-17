using System.Text;
using System.Text.Json;
using AxDeck.Core;

// A provider's translated lyric under the original one: the two are matched by timestamp, a translated line the other text has no
// line for is left empty, and the window carries the current line's translation only.
static class LyricsTranslationTests
{
    private const string Lrc = "[00:02.00]第一句\n[00:08.00]第二句\n[00:15.00]第三句";
    private const string Trans = "[00:02.00]first line\n[00:08.00]second line\n[00:15.00]third line";

    public static void Run(Action<string, bool> check)
    {
        Parsing(check);
        Alignment(check);
        Windows(check);
        Qq(check);
    }

    private static void Parsing(Action<string, bool> check)
    {
        var lyrics = Lyrics.Parse(Lrc, null, 200, "test", translation: Trans);
        check("Translation: each line carries the translation timed with it", lyrics.Lines.Count == 3
            && lyrics.Lines[0].Translation == "first line" && lyrics.Lines[2].Translation == "third line");
        // The original text is untouched; the translation is a second field of the same line.
        check("Translation: the original line stays as it was", lyrics.Lines[1].Seconds == 8 && lyrics.Lines[1].Text == "第二句");
        // Without a translated lyric the lines are exactly as before.
        check("Translation: a song without one has no translations", Lyrics.Parse(Lrc, null, 200, "test").Lines.All(line => line.Translation.Length == 0));
        // Plain lyrics are not timed, so a translation has no line to sit on.
        check("Translation: plain lyrics carry none", Lyrics.Parse(null, "一行\n二行", 200, "test", translation: Trans).Lines.All(line => line.Translation.Length == 0));
    }

    private static void Alignment(Action<string, bool> check)
    {
        // A line the translation has no text for stays empty rather than taking the next line's words.
        var missing = Lyrics.Parse(Lrc, null, 200, "test", translation: "[00:02.00]first line\n[00:15.00]third line");
        check("Translation: a line without a translation stays empty", missing.Lines[1].Translation.Length == 0
            && missing.Lines[2].Translation == "third line");
        // A timestamp that far from the line is another line's, so it is not used.
        var off = Lyrics.Parse(Lrc, null, 200, "test", translation: "[00:02.00]first line\n[00:09.50]late line\n[00:15.00]third line");
        check("Translation: a line timed apart from the text is not used", off.Lines[1].Translation.Length == 0);
        // The song's offset moves both texts alike, so the lines still meet.
        var shifted = Lyrics.Parse("[offset:-1000]\n" + Lrc, null, 200, "test", translation: "[offset:-1000]\n" + Trans);
        check("Translation: the song's offset moves the translation with the text", shifted.Lines[1].Seconds == 7 && shifted.Lines[1].Translation == "second line");
        // A translated lyric that is only whitespace leaves the text alone.
        check("Translation: an empty translated lyric changes nothing", Lyrics.Parse(Lrc, null, 200, "test", translation: "[00:02.00]\n[00:08.00] ").Lines.All(line => line.Translation.Length == 0));
    }

    private static void Windows(Action<string, bool> check)
    {
        var lyrics = Lyrics.Parse(Lrc, null, 200, "test", translation: Trans);
        check("Translation: the window carries the current line's translation", lyrics.Around(9, 1, 1) is { Current: "第二句", Translation: "second line" });
        // An instrumental gap has no words to translate.
        var gap = Lyrics.Parse("[00:02.00]第一句\n[00:08.00]\n[00:15.00]第三句", null, 200, "test", translation: "[00:02.00]first line\n[00:08.00]ignored\n[00:15.00]third line");
        check("Translation: a gap reports none", gap.Around(9, 1, 1) is { Current: "♪", Translation: "" });
        check("Translation: a song without a translated lyric reports none", Lyrics.Parse(Lrc, null, 200, "test").Around(9, 1, 1).Translation.Length == 0);
        check("Translation: an empty window has none", LyricWindow.Empty.Translation.Length == 0);
    }

    private static void Qq(Action<string, bool> check)
    {
        // QQ's GetPlayLyricInfo answers with a base64 lyric and, for a translated song, a base64 "trans".
        using var handler = new JsonHandler(JsonSerializer.Serialize(new
        {
            code = 0,
            lyric = new
            {
                code = 0,
                data = new
                {
                    lyric = Convert.ToBase64String(Encoding.UTF8.GetBytes(Lrc)),
                    trans = Convert.ToBase64String(Encoding.UTF8.GetBytes(Trans))
                }
            }
        }));
        using var http = new HttpClient(handler);
        using var service = new AxDeck.Core.LyricsService(http);
        var track = new MediaTrack { Source = "QQ 音乐", Title = "Song", Artist = "Artist", SongId = "qq:000000000000AA", End = TimeSpan.FromSeconds(200) };
        var lyrics = service.FindAsync(track, true, default).GetAwaiter().GetResult();
        check("Translation: QQ's translated lyric is decoded and timed with the original", lyrics.Lines.Count == 3
            && lyrics.Lines[1].Text == "第二句" && lyrics.Lines[1].Translation == "second line");
    }

    // Answers every request with one JSON body.
    private sealed class JsonHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}