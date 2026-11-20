namespace AxDeck.Core;

public enum SessionActivityState { Unavailable, Quiet, Active }

// What one tool's session records say without opening them: how many there are, when the newest was written, and whether that
// is recent enough to suggest the tool is still working. Quiet never means finished; it only means no record has been written
// lately. A record count is metadata, not a count of conversations: a tool may keep several records for one conversation.
public sealed record SessionActivitySnapshot(
    ProviderId Source, SessionActivityState State, DateTimeOffset? LastActivity, int RecordCount = 0, int ActiveRecordCount = 0)
{
    public bool IsWorking => State == SessionActivityState.Active;
    public static SessionActivitySnapshot Unavailable(ProviderId source) => new(source, SessionActivityState.Unavailable, null);
}

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

    // Metadata under the given folders: record counts, newest write time, and whether any record is recent. A missing folder means
    // there is nothing to report; an existing empty folder is quiet. Folders that cannot be read are skipped. Only files with the
    // given extension (without the dot) are looked at, so a tool's other files are left alone. Nothing is opened or parsed.
    public static (SessionActivityState State, DateTimeOffset? LastActivity, int RecordCount, int ActiveRecordCount) Read(
        IEnumerable<string> folders, string extension, DateTimeOffset now, TimeSpan? active = null)
    {
        var span = active ?? Active;
        bool present = false;
        DateTimeOffset? latest = null;
        int records = 0;
        int activeRecords = 0;
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (folder.Length == 0) continue;
            try
            {
                if (!Directory.Exists(folder)) continue;
                present = true;
                // The listing already carries each file's time, so no file is asked for it again.
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*." + extension, Options))
                {
                    // A file from the future (a clock set back since) says nothing about now.
                    if (file.LastWriteTimeUtc > now.UtcDateTime.AddMinutes(5)) continue;
                    records++;
                    bool recent = now.UtcDateTime - file.LastWriteTimeUtc <= span;
                    if (recent) activeRecords++;
                    var written = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
                    if (latest is null || written > latest) latest = written;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (!present) return (SessionActivityState.Unavailable, null, 0, 0);
        if (latest is not { } last) return (SessionActivityState.Quiet, null, records, activeRecords);
        return (activeRecords > 0 ? SessionActivityState.Active : SessionActivityState.Quiet, last, records, activeRecords);
    }

    // Whether any record under the given folders was written to within the active span. Kept for callers that need only the
    // boolean; the snapshot API above also yields the latest time and the difference between no records and a quiet tool.
    public static bool IsWorking(IEnumerable<string> folders, string extension, DateTimeOffset now, TimeSpan? active = null)
    {
        var (state, _, _, _) = Read(folders, extension, now, active);
        return state == SessionActivityState.Active;
    }
}
