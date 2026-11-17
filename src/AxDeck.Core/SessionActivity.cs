namespace AxDeck.Core;

// Whether an AI coding tool is working right now, from whether its session records are still being appended to. The panel
// already reads those records for usage figures; this looks only at when each one last changed, never at what it says, so
// nothing about a conversation is parsed or shown.
// It is a guess and says only that much: a record still being written means the tool is probably working, and a quiet one means
// it is probably not. Thinking between two short writes looks the same as being finished, so the two are not told apart.
public static class SessionActivity
{
    // A file changed within this long counts as still being written: a tool writes its record a little after each message, and a
    // model can think for a minute with nothing written in between.
    public static readonly TimeSpan Active = TimeSpan.FromMinutes(2);

    // Folders that cannot be read are skipped inside the walk (IgnoreInaccessible), and links are not followed: a session folder
    // that links to itself would otherwise be walked forever.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
    };

    // Whether any record under the given folders was written to within the active span. Folders that do not exist, cannot be
    // read, or hold nothing are skipped: no tool installed is the ordinary case, not an error. Only files with the given
    // extension (without the dot) are looked at, so a tool's other files are left alone. Nothing is opened or parsed, and the
    // walk stops at the first record written to within the span rather than timing every file.
    public static bool IsWorking(IEnumerable<string> folders, string extension, DateTimeOffset now, TimeSpan? active = null)
    {
        var span = active ?? Active;
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (folder.Length == 0) continue;
            try
            {
                if (!Directory.Exists(folder)) continue;
                // The listing already carries each file's time, so no file is asked for it again.
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*." + extension, Options))
                {
                    // A file from the future (a clock set back since) says nothing about now.
                    if (file.LastWriteTimeUtc > now.UtcDateTime.AddMinutes(5)) continue;
                    if (now.UtcDateTime - file.LastWriteTimeUtc <= span) return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return false;
    }
}