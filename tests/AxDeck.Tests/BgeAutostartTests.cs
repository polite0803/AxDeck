using AxDeck.Core;

// The BGE-M3 server's startup entry and the log window's tail: both are pure, so they are checked here.
static class BgeAutostartTests
{
    public static void Run(Action<string, bool> check)
    {
        string command = BgeAutostart.Command(@"C:\llama\llama-server.exe", @"C:\models\bge-m3.gguf");
        check("BgeAutostart: the command quotes the server", command.StartsWith("\"C:\\llama\\llama-server.exe\"", StringComparison.Ordinal));
        check("BgeAutostart: the command holds the model", command.Contains("\"C:\\models\\bge-m3.gguf\"", StringComparison.Ordinal));
        check("BgeAutostart: the command asks for embeddings", command.Contains("--embeddings", StringComparison.Ordinal));
        check("BgeAutostart: the command holds the port", command.Contains("--port 8091", StringComparison.Ordinal));
        check("BgeAutostart: a missing server is refused", BgeAutostart.Refusal(null, @"C:\m.gguf") is not null);
        check("BgeAutostart: a missing model is refused", BgeAutostart.Refusal(@"C:\s.exe", null) is not null);
        check("BgeAutostart: an empty model is refused", BgeAutostart.Refusal(@"C:\s.exe", "  ") is not null);
        check("BgeAutostart: a good pair is not refused", BgeAutostart.Refusal(@"C:\s.exe", @"C:\m.gguf") is null);
        check("BgeAutostart: the value name is its own", BgeAutostart.ValueName == "AxDeckBgeM3");
    }
}

static class ServiceLogTests
{
    public static void Run(Action<string, bool> check)
    {
        var lines = Enumerable.Range(0, 10).Select(i => "line " + i).ToArray();
        var tail = ServiceLog.Tail(lines, 3);
        check("ServiceLog: the tail keeps the last lines", tail.Count == 3 && tail[0] == "line 7" && tail[2] == "line 9");
        check("ServiceLog: a short log is returned whole", ServiceLog.Tail(lines, 100).Count == 10);
        check("ServiceLog: nothing asked for means nothing shown", ServiceLog.Tail(lines, 0).Count == 0);
        check("ServiceLog: the text keeps the line order", ServiceLog.Text(lines, 2) == "line 8\nline 9");
        check("ServiceLog: the empty note is not empty", ServiceLog.Empty.Length > 0);
    }
}
