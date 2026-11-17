using AxDeck.Core;

// The activity dot's reading of a tool's session records: only a record written to within the active span counts, a record from
// the future says nothing, a missing folder is not an error, and each source is looked for where its tool keeps its records.
static class SessionActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public static void Run(Action<string, bool> check)
    {
        Waits(check);
        Reading(check);
        Sources(check);
    }

    // Every suite uses its own folder under the temporary path; nothing under the user's own data is touched.
    private static string Folder(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "AxDeck-Tests-activity-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Write(string file, DateTimeOffset time)
    {
        File.WriteAllText(file, "{}");
        File.SetLastWriteTimeUtc(file, time.UtcDateTime);
    }

    private static void Waits(Action<string, bool> check)
    {
        check("SessionActivity: the active span is short enough to mean now", SessionActivity.Active <= TimeSpan.FromMinutes(5) && SessionActivity.Active >= TimeSpan.FromMinutes(1));
        // No folder, an empty folder, and a folder of another kind of file: nothing is running, and none of it is an error.
        check("SessionActivity: a folder that does not exist is quiet", !SessionActivity.IsWorking([Path.Combine(Path.GetTempPath(), "AxDeck-Tests-absent-" + Guid.NewGuid().ToString("N"))], "jsonl", Now));
        check("SessionActivity: an empty folder is quiet", !SessionActivity.IsWorking([Folder("empty")], "jsonl", Now));
        var other = Folder("other");
        Write(Path.Combine(other, "notes.txt"), Now.AddMinutes(-1));
        check("SessionActivity: another kind of file is left alone", !SessionActivity.IsWorking([other], "jsonl", Now));
        check("SessionActivity: an empty folder path is skipped", !SessionActivity.IsWorking(["", Folder("blank")], "jsonl", Now));
    }

    private static void Reading(Action<string, bool> check)
    {
        var folder = Folder("reading");
        Write(Path.Combine(folder, "old.jsonl"), Now.AddMinutes(-30));
        // An old record says nothing about now, even when nothing newer is there.
        check("SessionActivity: an old record is quiet", !SessionActivity.IsWorking([folder], "jsonl", Now));
        Write(Path.Combine(folder, "fresh.jsonl"), Now.AddMinutes(-1));
        check("SessionActivity: a record written a minute ago is working", SessionActivity.IsWorking([folder], "jsonl", Now));
        // The walk goes below the folder the tool keeps its sessions in.
        var deeper = Folder("deep");
        Directory.CreateDirectory(Path.Combine(deeper, "a", "b"));
        Write(Path.Combine(deeper, "a", "b", "run.jsonl"), Now.AddSeconds(-30));
        check("SessionActivity: a record in a subfolder counts", SessionActivity.IsWorking([deeper], "jsonl", Now));
        // A record from the future, from a clock set back since, does not keep the dot on forever.
        var future = Folder("future");
        Write(Path.Combine(future, "run.jsonl"), Now.AddHours(2));
        check("SessionActivity: a record from the future is quiet", !SessionActivity.IsWorking([future], "jsonl", Now));
        // A span chosen by the caller: the same record is quiet when asked about a shorter one.
        check("SessionActivity: a shorter span is honoured", !SessionActivity.IsWorking([folder], "jsonl", Now, TimeSpan.FromSeconds(10)));
    }

    private static void Sources(Action<string, bool> check)
    {
        var home = Folder("home");
        var locations = DataLocations.Resolve(home, home, home, null, null);
        // Claude Code's records live under projects/, so a record elsewhere in its home is not one.
        var projects = Path.Combine(locations.ClaudeHome, "projects", "a");
        Directory.CreateDirectory(projects);
        Write(Path.Combine(projects, "session.jsonl"), Now.AddMinutes(-1));
        check("SessionActivity: Claude's records are found under projects", SessionSources.IsWorking(ProviderId.Claude, locations, Now));
        // A source read over an API keeps no records to watch, so it is never shown as working.
        check("SessionActivity: a source without records is never working", !SessionSources.IsWorking(ProviderId.OpenRouter, locations, Now));
        check("SessionActivity: a source without records has no folder", SessionSources.Of(ProviderId.OpenRouter, locations).Extension.Length == 0);
        var codex = Path.Combine(locations.CodexHome, "sessions", "2026");
        Directory.CreateDirectory(codex);
        Write(Path.Combine(codex, "rollout.jsonl"), Now.AddMinutes(-1));
        check("SessionActivity: Codex's records are found under sessions", SessionSources.IsWorking(ProviderId.Codex, locations, Now));
        // A tool with no record on this PC is quiet rather than an error.
        check("SessionActivity: an installed tool that is not running is quiet", !SessionSources.IsWorking(ProviderId.Kimi, locations, Now));
    }
}