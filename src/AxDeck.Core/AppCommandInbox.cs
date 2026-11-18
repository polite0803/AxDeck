namespace AxDeck.Core;

// The command another instance of AxDeck leaves for the running one. A second copy of the app (a link, a command line) writes
// the command into a small file in the data folder and rings a bell; the running app reads the file and does what it says.
//
// The file is plain text: the kind on the first line, the argument on the second. It is read once and then removed.
public static class AppCommandInbox
{
    public const string FileName = "command.txt";

    // Writes the command for the running app. A command without a kind is not written at all.
    public static void Write(string directory, AppCommandKind kind, string? argument = null)
    {
        if (kind == AppCommandKind.None) return;
        Directory.CreateDirectory(directory);
        string text = AppCommand.Name(kind) + "\n" + (argument ?? "") + "\n";
        string path = Path.Combine(directory, FileName);
        string temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, true);
    }

    // Reads the command that is waiting, and takes it away. Nothing when there is none, or when the file cannot be read.
    public static (AppCommandKind Kind, string? Argument)? Read(string directory)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return null;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        var kind = AppCommand.Kind(lines.FirstOrDefault()?.Trim());
        if (kind == AppCommandKind.None) return null;
        string argument = lines.Length > 1 ? lines[1].Trim() : "";
        return (kind, argument.Length == 0 ? null : argument);
    }
}
