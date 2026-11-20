using System.Text.Json;

namespace AxDeck.Core;

// The focus timer: a countdown the user starts, pauses and resets, with an optional local work-session record. The state
// lives on this machine, in its own file, so the countdown survives the panel and application closing. Nothing is sent
// anywhere.
public sealed class FocusTimer
{
    public const string FileName = "focus-timer.json";
    public const int MaximumHistory = 50;
    // The lengths the panel offers, in minutes.
    public static readonly int[] Presets = [15, 25, 45, 60];

    // How long the countdown runs, in seconds.
    public int TotalSeconds { get; set; } = 25 * 60;
    // How much is left, in seconds, while the timer is not running.
    public int RemainingSeconds { get; set; } = 25 * 60;
    // Whether the countdown is running.
    public bool Running { get; set; }
    // When the countdown ends, while it runs. The remaining time is worked out from it, so a machine that slept does not
    // lose the time it slept for.
    public DateTimeOffset? EndsAt { get; set; }
    // The work session this countdown belongs to. StartedAt is the first start of the session, while a pause and resume keep
    // the same session. EndedAt is set when it runs out, is reset, or a new session replaces it.
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int TargetSeconds { get; set; }
    public int PauseCount { get; set; }
    public List<ProviderId> WorkSources { get; set; } = [];
    public string Label { get; set; } = "";
    // The project this session belongs to, when the user chose one. It is an explicit local record, not a guess from the
    // session folder or a window title.
    public Guid? ProjectId { get; set; }
    // A metadata-only summary, taken at a low frequency while the session is being worked on. It never contains the
    // contents of a session record.
    public List<WorkSessionSample> Samples { get; set; } = [];
    // Completed or abandoned sessions, kept on this machine only. History is bounded so the file cannot grow forever.
    public List<FocusSession> History { get; set; } = [];

    // How much is left, in seconds, right now.
    public int Remaining(DateTimeOffset now)
    {
        if (!Running) return Math.Max(0, RemainingSeconds);
        if (EndsAt is not { } end) return Math.Max(0, RemainingSeconds);
        return (int)Math.Max(0, Math.Ceiling((end - now).TotalSeconds));
    }

    // True when the countdown has run out.
    public bool Finished(DateTimeOffset now) => Running && Remaining(now) <= 0;

    // Whether there is a countdown to show: one that runs, one that stands paused, or one that has run out. A timer that was
    // never started, or was put back to the start, shows nothing.
    public bool Shown => Running || RemainingSeconds < TotalSeconds;

    // Starts the countdown for the given number of minutes.
    public void Start(DateTimeOffset now, int minutes, string? label = null, IEnumerable<ProviderId>? sources = null, Guid? projectId = null)
        => StartSeconds(now, minutes * 60, label, sources, projectId);

    // Starts from seconds for restoring a saved session whose target is not a whole number of minutes.
    public void StartSeconds(DateTimeOffset now, int secondsValue, string? label = null, IEnumerable<ProviderId>? sources = null, Guid? projectId = null)
    {
        int seconds = Math.Max(60, secondsValue);
        if (StartedAt is not null) Finish(now);
        TotalSeconds = seconds; RemainingSeconds = seconds; Running = true; EndsAt = now.AddSeconds(seconds);
        StartedAt = now; EndedAt = null; TargetSeconds = seconds; PauseCount = 0;
        WorkSources = [.. (sources ?? []).Where(SessionSources.Supports).Distinct()];
        Label = CleanLabel(label);
        ProjectId = projectId == Guid.Empty ? null : projectId;
        Samples = [];
    }

    // Pauses the countdown where it stands.
    public void Pause(DateTimeOffset now)
    {
        if (!Running) return;
        RemainingSeconds = Remaining(now); Running = false; EndsAt = null; PauseCount++;
    }

    // Resumes a paused countdown without making it a new work session. A countdown that has already run out stays finished:
    // resuming it would start another run of the same work session, and ending that run again would write a duplicate history entry.
    public void Resume(DateTimeOffset now)
    {
        if (Running || StartedAt is null) return;
        int seconds = RemainingSeconds;
        if (EndedAt is not null || seconds <= 0) return;
        RemainingSeconds = seconds; Running = true; EndsAt = now.AddSeconds(seconds);
    }

    // Stops the countdown and fills it up again.
    public void Reset(DateTimeOffset now)
    {
        if (StartedAt is not null && EndedAt is null) Finish(now);
        Running = false; EndsAt = null; RemainingSeconds = TotalSeconds;
    }

    // Ends the current work session and keeps a bounded local account of it. Calling this again for an already ended
    // session is harmless.
    public void Finish(DateTimeOffset now)
    {
        if (StartedAt is null || EndedAt is not null) return;
        EndedAt = now; Running = false; EndsAt = null; RemainingSeconds = 0;
        History.Add(new FocusSession
        {
            StartedAt = StartedAt.Value,
            EndedAt = now,
            TargetSeconds = Math.Max(TotalSeconds, TargetSeconds),
            PauseCount = PauseCount,
            WorkSources = [.. WorkSources],
            Label = Label,
            ProjectId = ProjectId,
            Samples = [.. Samples],
        });
        if (History.Count > MaximumHistory) History = [.. History.TakeLast(MaximumHistory)];
    }

    // Records a metadata-only sample if it differs from the previous one. Sampling belongs to the running session only.
    public bool Sample(DateTimeOffset now, IEnumerable<ProviderId> sources, IEnumerable<SessionActivitySnapshot> activity)
    {
        if (!Running || StartedAt is null) return false;
        var selected = sources.Where(SessionSources.Supports).ToHashSet();
        var list = activity.Where(item => selected.Contains(item.Source)).DistinctBy(item => item.Source).OrderBy(item => item.Source).ToList();
        var sample = new WorkSessionSample(now, list);
        if (Samples.Count > 0 && Samples[^1].SameReadings(sample)) return false;
        Samples.Add(sample);
        if (Samples.Count > MaximumSamples) Samples.RemoveRange(0, Samples.Count - MaximumSamples);
        return true;
    }

    public const int MaximumSamples = 120;

    public static string CleanLabel(string? label)
    {
        var cleaned = string.Join(" ", (label ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }

    // The countdown as it is shown: "25:00", or "1:05:00" when it is longer than an hour.
    public static string Format(int seconds)
    {
        seconds = Math.Max(0, seconds);
        int hours = seconds / 3600, minutes = seconds % 3600 / 60, rest = seconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{rest:00}" : $"{minutes:00}:{rest:00}";
    }

    // The countdown as it is shown now.
    public string Format(DateTimeOffset now) => Format(Remaining(now));

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // The state as it is written to the file.
    public string Serialize() => JsonSerializer.Serialize(new
    {
        totalSeconds = TotalSeconds,
        remainingSeconds = RemainingSeconds,
        running = Running,
        endsAt = EndsAt?.ToUnixTimeSeconds(),
        startedAt = StartedAt?.ToUnixTimeSeconds(),
        endedAt = EndedAt?.ToUnixTimeSeconds(),
        targetSeconds = TargetSeconds,
        pauseCount = PauseCount,
        workSources = WorkSources.Select(ProviderCatalog.Key),
        label = Label,
        projectId = ProjectId,
        samples = Samples.Select(sample => new
        {
            at = sample.At.ToUnixTimeSeconds(),
            activity = sample.Activity.Select(item => new
            {
                source = ProviderCatalog.Key(item.Source), state = item.State.ToString(), lastActivity = item.LastActivity?.ToUnixTimeSeconds(),
                recordCount = item.RecordCount, activeRecordCount = item.ActiveRecordCount
            })
        }),
        history = History.Select(session => new
        {
            startedAt = session.StartedAt.ToUnixTimeSeconds(), endedAt = session.EndedAt.ToUnixTimeSeconds(),
            targetSeconds = session.TargetSeconds, pauseCount = session.PauseCount,
            workSources = session.WorkSources.Select(ProviderCatalog.Key), label = session.Label,
            projectId = session.ProjectId,
            samples = session.Samples.Select(sample => new
            {
                at = sample.At.ToUnixTimeSeconds(),
                activity = sample.Activity.Select(item => new
                {
                    source = ProviderCatalog.Key(item.Source), state = item.State.ToString(), lastActivity = item.LastActivity?.ToUnixTimeSeconds(),
                    recordCount = item.RecordCount, activeRecordCount = item.ActiveRecordCount
                })
            })
        }),
    }, Options);

    // Reads the state back. A missing or broken file reads as a timer that was never started.
    public static FocusTimer Parse(string? json, DateTimeOffset now)
    {
        var timer = new FocusTimer();
        if (string.IsNullOrWhiteSpace(json)) return timer;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            int total = Number(root, "totalSeconds"), remaining = Number(root, "remainingSeconds");
            if (total > 0) timer.TotalSeconds = total;
            if (remaining >= 0) timer.RemainingSeconds = remaining;
            timer.Running = root.TryGetProperty("running", out var running) && running.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("endsAt", out var ends) && ends.ValueKind == JsonValueKind.Number && ends.TryGetInt64(out var seconds))
                timer.EndsAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (root.TryGetProperty("startedAt", out var started) && started.ValueKind == JsonValueKind.Number && started.TryGetInt64(out var start))
                timer.StartedAt = DateTimeOffset.FromUnixTimeSeconds(start);
            if (root.TryGetProperty("endedAt", out var ended) && ended.ValueKind == JsonValueKind.Number && ended.TryGetInt64(out var end))
                timer.EndedAt = DateTimeOffset.FromUnixTimeSeconds(end);
            timer.TargetSeconds = Math.Max(0, Number(root, "targetSeconds"));
            timer.PauseCount = Math.Max(0, Number(root, "pauseCount"));
            if (root.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String) timer.Label = CleanLabel(label.GetString());
            timer.ProjectId = ReadGuid(root, "projectId");
            timer.WorkSources = ReadSources(root, "workSources");
            timer.Samples = ReadSamples(root, "samples");
            timer.History = ReadHistory(root, "history");
            // Files from before the work-session record have no start time. They keep their countdown, but do not become a
            // session whose start and samples are invented, so they never write a work-session history entry.
            if (timer.StartedAt is null)
            {
                timer.EndedAt = null; timer.TargetSeconds = 0; timer.PauseCount = 0;
                timer.WorkSources = []; timer.Label = ""; timer.Samples = [];
                timer.ProjectId = null;
            }
            // A timer that ran while the machine was off has run out: it is not resumed.
            if (timer.Running && timer.EndsAt is { } endAt && endAt <= now)
            {
                timer.Running = false; timer.EndsAt = null; timer.RemainingSeconds = 0;
                if (timer.StartedAt is not null && timer.EndedAt is null) timer.Finish(endAt);
            }
            return timer;
        }
        catch (JsonException) { return new(); }
    }

    private static int Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }

    private static List<ProviderId> ReadSources(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return [];
        var list = new List<ProviderId>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !Enum.TryParse<ProviderId>(item.GetString(), true, out var source)
                || !SessionSources.Supports(source)) continue;
            if (!list.Contains(source)) list.Add(source);
        }
        return list;
    }

    private static List<WorkSessionSample> ReadSamples(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return [];
        var list = new List<WorkSessionSample>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("at", out var at) || !at.TryGetInt64(out var seconds)) continue;
            var readings = new List<SessionActivitySnapshot>();
            if (item.TryGetProperty("activity", out var activity) && activity.ValueKind == JsonValueKind.Array)
                foreach (var reading in activity.EnumerateArray())
                {
                    if (reading.ValueKind != JsonValueKind.Object || !reading.TryGetProperty("source", out var source)
                        || !Enum.TryParse<ProviderId>(source.GetString(), true, out var provider) || !SessionSources.Supports(provider)) continue;
                    var state = reading.TryGetProperty("state", out var stateNode) && Enum.TryParse<SessionActivityState>(stateNode.GetString(), true, out var parsed)
                        ? parsed : SessionActivityState.Unavailable;
                    var last = reading.TryGetProperty("lastActivity", out var lastNode) && lastNode.TryGetInt64(out var lastSeconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(lastSeconds) : (DateTimeOffset?)null;
                    readings.Add(new SessionActivitySnapshot(provider, state, last,
                        Math.Max(0, JsonInt(reading, "recordCount")), Math.Max(0, JsonInt(reading, "activeRecordCount"))));
                }
            list.Add(new WorkSessionSample(DateTimeOffset.FromUnixTimeSeconds(seconds), readings));
        }
        return list.TakeLast(MaximumSamples).ToList();
    }

    private static List<FocusSession> ReadHistory(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return [];
        var list = new List<FocusSession>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("startedAt", out var started) || !started.TryGetInt64(out var start)
                || !item.TryGetProperty("endedAt", out var ended) || !ended.TryGetInt64(out var end)) continue;
            var session = new FocusSession
            {
                StartedAt = DateTimeOffset.FromUnixTimeSeconds(start), EndedAt = DateTimeOffset.FromUnixTimeSeconds(end),
                TargetSeconds = Math.Max(0, JsonInt(item, "targetSeconds")), PauseCount = Math.Max(0, JsonInt(item, "pauseCount")),
                WorkSources = ReadSources(item, "workSources"),
                Label = item.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String ? CleanLabel(label.GetString()) : "",
                ProjectId = ReadGuid(item, "projectId"),
                Samples = ReadSamples(item, "samples"),
            };
            list.Add(session);
        }
        return list.TakeLast(MaximumHistory).ToList();
    }

    private static int JsonInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static Guid? ReadGuid(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var id) && id != Guid.Empty ? id : null;

    // Reads the timer from its file in the data folder.
    public static FocusTimer Load(string directory) => Parse(TryRead(Path.Combine(directory, FileName)), DateTimeOffset.Now);

    // Writes the timer to its file in the data folder. A file that cannot be written is not an error worth stopping for:
    // the countdown keeps running in memory.
    public void Save(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileName);
            File.WriteAllText(path + ".tmp", Serialize());
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string? TryRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}

public sealed record WorkSessionSample(DateTimeOffset At, IReadOnlyList<SessionActivitySnapshot> Activity)
{
    // Comparing metadata only, not timestamps of the sample itself: an unchanged folder walk should not fill the file with
    // identical samples.
    public bool SameReadings(WorkSessionSample other) =>
        Activity.Count == other.Activity.Count && Activity.Zip(other.Activity).All(pair =>
            pair.First.Source == pair.Second.Source && pair.First.State == pair.Second.State &&
            pair.First.LastActivity == pair.Second.LastActivity && pair.First.RecordCount == pair.Second.RecordCount &&
            pair.First.ActiveRecordCount == pair.Second.ActiveRecordCount);
}

public sealed class FocusSession
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public int TargetSeconds { get; set; }
    public int PauseCount { get; set; }
    public List<ProviderId> WorkSources { get; set; } = [];
    public string Label { get; set; } = "";
    public Guid? ProjectId { get; set; }
    public List<WorkSessionSample> Samples { get; set; } = [];
}
