using System.Text.Json;

namespace AxDeck.Core;

// The used share of each quota over time, kept on disk so the panel can show where a quota has been rather than only where it
// is now. One sample per quota per interval, kept for a rolling month, and written only when a reading brought a new share:
// the file's growth is bounded by the quotas the user watches, not by how long the app runs.
// Nothing leaves this PC, and nothing but the share is stored: no token, no account id, no message content.
public static class UsageHistory
{
    public const string FileName = "usage-history.json";
    // Two samples closer together than this are one: the panel reads its quotas far more often than a curve needs.
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    // Samples older than this are dropped when the file is written.
    public static readonly TimeSpan Keep = TimeSpan.FromDays(30);
    // Enough for a month at the interval above, and a hard cap on a damaged or hand-edited file.
    public const int MaximumSamples = 6000;

    public sealed record Sample(DateTimeOffset Time, double Used);

    // Keys are "application id:configuration:quota id", the same key the panel uses for its levels, so a row's quota keeps
    // its curve across restarts and across a change of display name.
    public static Dictionary<string, List<Sample>> Parse(string json, DateTimeOffset now)
    {
        var history = new Dictionary<string, List<Sample>>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return history;
            foreach (var pair in doc.RootElement.EnumerateObject())
            {
                if (pair.Value.ValueKind != JsonValueKind.Array) continue;
                var samples = new List<Sample>();
                foreach (var item in pair.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2) continue;
                    var time = item[0].Date();
                    if (time is null) continue;
                    if (item[1].Number() is not { } used) continue;
                    // A reading from the future (a clock set back since) or past the kept span says nothing about now.
                    if (time > now.AddMinutes(5) || now - time > Keep + Interval) continue;
                    samples.Add(new Sample(time.Value, Math.Clamp(used, 0, 100)));
                }
                if (samples.Count > 0) history[pair.Name] = [.. samples.OrderBy(sample => sample.Time).TakeLast(MaximumSamples)];
            }
        }
        catch (JsonException) { return new(StringComparer.Ordinal); }
        return history;
    }

    // Adds the readings that came in, keeping at most one every Interval and dropping what is past Keep. Returns whether
    // anything changed, so the file is not written when there is nothing new to write.
    public static bool Add(Dictionary<string, List<Sample>> history, IEnumerable<(string Key, double Used)> readings, DateTimeOffset now)
    {
        bool changed = false;
        foreach (var (key, used) in readings)
        {
            if (!history.TryGetValue(key, out var samples)) history[key] = samples = [];
            var share = Math.Clamp(used, 0, 100);
            // A share that fell means the quota reset: the older samples describe a period that has ended, so the curve starts
            // again here rather than showing a cliff down that never happened as usage.
            if (samples.Count > 0 && (share < samples[^1].Used || now < samples[^1].Time)) samples.Clear();
            if (samples.Count > 0 && now - samples[^1].Time < Interval) continue;
            // An unchanged share adds nothing to a curve and would only grow the file.
            if (samples.Count > 0 && Math.Abs(samples[^1].Used - share) < 0.05) { samples[^1] = samples[^1] with { Time = now }; changed = true; continue; }
            samples.Add(new Sample(now, share));
            changed = true;
        }
        foreach (var (key, samples) in history.ToList())
        {
            int dropped = samples.RemoveAll(sample => now - sample.Time > Keep);
            if (samples.Count > MaximumSamples) { samples.RemoveRange(0, samples.Count - MaximumSamples); dropped++; }
            if (samples.Count == 0) { history.Remove(key); dropped++; }
            if (dropped > 0) changed = true;
        }
        return changed;
    }

    // The shares to draw, oldest first, within the last span: what a sparkline needs, and nothing more.
    public static IReadOnlyList<double> Recent(IReadOnlyDictionary<string, List<Sample>> history, string key, DateTimeOffset now, TimeSpan span)
        => history.TryGetValue(key, out var samples)
            ? [.. samples.Where(sample => now - sample.Time <= span).Select(sample => sample.Used)]
            : [];

    public static string Serialize(Dictionary<string, List<Sample>> history)
    {
        var root = new Dictionary<string, List<object[]>>(StringComparer.Ordinal);
        foreach (var (key, samples) in history.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            root[key] = [.. samples.Select(sample => new object[] { sample.Time.ToString("O"), Math.Round(sample.Used, 1) })];
        return JsonSerializer.Serialize(root);
    }

    public static Dictionary<string, List<Sample>> Load(string path, DateTimeOffset now)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path), now) : new(StringComparer.Ordinal); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(StringComparer.Ordinal); }
    }

    // Written whole and then moved into place, so a write cut short by an exit never leaves half a file. A failed write keeps
    // the previous file; the readings stay in memory for this run, so the next write carries them.
    public static void Save(string path, Dictionary<string, List<Sample>> history)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, Serialize(history));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}