using System.Text.Json;

namespace AxDeck.Core;

// The focus timer: a countdown the user starts, pauses and resets. The state lives on this machine, in its own file, so
// the countdown survives the panel being closed and the application being restarted. Nothing is sent anywhere.
public sealed class FocusTimer
{
    public const string FileName = "focus-timer.json";
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
    public void Start(DateTimeOffset now, int minutes)
    {
        int seconds = Math.Max(60, minutes * 60);
        TotalSeconds = seconds; RemainingSeconds = seconds; Running = true; EndsAt = now.AddSeconds(seconds);
    }

    // Pauses the countdown where it stands.
    public void Pause(DateTimeOffset now)
    {
        RemainingSeconds = Remaining(now); Running = false; EndsAt = null;
    }

    // Stops the countdown and fills it up again.
    public void Reset()
    {
        Running = false; EndsAt = null; RemainingSeconds = TotalSeconds;
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
            // A timer that ran while the machine was off has run out: it is not resumed.
            if (timer.Running && timer.EndsAt is { } end && end <= now) { timer.Running = false; timer.EndsAt = null; timer.RemainingSeconds = 0; }
            return timer;
        }
        catch (JsonException) { return new(); }
    }

    private static int Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }

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
