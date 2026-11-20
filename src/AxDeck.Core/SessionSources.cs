namespace AxDeck.Core;

// Where each AI coding tool keeps the session records the panel already reads for usage figures, for the activity dot on its
// column: only the folders and the file extension, so the dot knows where to look and nothing about what is inside.
public static class SessionSources
{
    // The tools whose local session records can be watched for activity. It is a narrower set than the usage sources: a tool
    // read over an API may still report usage without keeping a file the panel can watch.
    public static readonly ProviderId[] Watchable = [ProviderId.Claude, ProviderId.Codex, ProviderId.Dsh, ProviderId.Kimi, ProviderId.Pi];
    public static bool Supports(ProviderId source) => Watchable.Contains(source);

    // The folders holding this source's session records, and the extension of those records (without the dot). Empty when the
    // source keeps no records on this PC, which is the case for the ones read over an API instead.
    public static (IReadOnlyList<string> Folders, string Extension) Of(ProviderId source, DataLocations locations)
    {
        return source switch
        {
            // Claude Code writes one .jsonl per conversation under projects/, in a folder per working directory.
            ProviderId.Claude => ([Path.Combine(locations.ClaudeHome, "projects")], "jsonl"),
            // Codex writes a .jsonl per run under sessions/.
            ProviderId.Codex => ([Path.Combine(locations.CodexHome, "sessions")], "jsonl"),
            // DeepSeek Harness, Kimi Code and Pi each keep their sessions under their own root.
            ProviderId.Dsh => ([DshSessions.Folder(locations.DshHome)], "jsonl"),
            ProviderId.Kimi => ([KimiSessions.Folder(locations.KimiHome)], "jsonl"),
            ProviderId.Pi => ([locations.PiSessions], "jsonl"),
            // The rest keep their records in a database, or none at all: nothing is appended to as a conversation runs.
            _ => ([], "")
        };
    }

    // Whether the tool is writing its records right now. False when it keeps none on this PC, or wrote none within the active
    // span: either way there is nothing to show.
    public static bool IsWorking(ProviderId source, DataLocations locations, DateTimeOffset now, TimeSpan? active = null)
    {
        return Read(source, locations, now, active).IsWorking;
    }

    // The latest write time for this source, or unavailable when it keeps no local records or its folder is absent.
    public static SessionActivitySnapshot Read(ProviderId source, DataLocations locations, DateTimeOffset now, TimeSpan? active = null)
    {
        var (folders, extension) = Of(source, locations);
        if (extension.Length == 0) return SessionActivitySnapshot.Unavailable(source);
        var (state, latest, records, activeRecords) = SessionActivity.Read(folders, extension, now, active);
        return new SessionActivitySnapshot(source, state, latest, records, activeRecords);
    }
}
