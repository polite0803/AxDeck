namespace AxDeck.Core;

// Where each AI coding tool keeps the session records the panel already reads for usage figures, for the activity dot on its
// column: only the folders and the file extension, so the dot knows where to look and nothing about what is inside.
public static class SessionSources
{
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
        var (folders, extension) = Of(source, locations);
        return extension.Length > 0 && SessionActivity.IsWorking(folders, extension, now, active);
    }
}