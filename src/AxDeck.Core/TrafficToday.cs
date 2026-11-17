using System.Text.Json;

namespace AxDeck.Core;

// The day's traffic as the page shows it: the bytes read from and written to the disks, and the bytes received over and sent
// through the network, since the day began.
public sealed record TrafficTotals(long DiskRead, long DiskWritten, long NetworkReceived, long NetworkSent);

// The day's traffic, kept on disk so it survives a restart: the bytes read from and written to the disks, and the bytes received
// over and sent through the network, since the day began (TrafficToday). The counters the system gives are the ones since the
// machine started, so the day's total is kept here: the counter's reading at the day began is remembered, and everything the
// counter grew by since is added to the day's total. The totals are written to disk so they survive a restart, and the counter's
// reading at the last reading is written too: a machine that was off overnight still counts what it downloaded before it was shut
// down, and one that was off while the day rolled over starts the new day from zero.
//
// Nothing leaves this PC, and nothing but the four byte counts is stored: no address, no host name, no packet.
public sealed class TrafficToday
{
    // The file the day's totals are kept in.
    public const string FileName = "traffic-today.json";

    // The day the totals belong to, as the date it started (local time). A new day starts the totals over.
    public DateOnly Day { get; set; }
    // The bytes read from the disks and written to them today.
    public long DiskRead { get; set; }
    public long DiskWritten { get; set; }
    // The bytes received over the network and sent today.
    public long NetworkReceived { get; set; }
    public long NetworkSent { get; set; }
    // The counters' readings when the totals were last brought up to date, so the growth since then can be added. Each is null
    // when the counter was not read yet (the totals then start from the counter's first reading).
    public long? DiskReadCounter { get; set; }
    public long? DiskWrittenCounter { get; set; }
    public long? NetworkReceivedCounter { get; set; }
    public long? NetworkSentCounter { get; set; }

    // The counters' readings as the system gives them. A counter that went backwards (the machine rebooted, the adapter was
    // replaced) starts the day's total over from it, as the old count belonged to a run that no longer exists.
    public sealed record Counters(long DiskRead, long DiskWritten, long NetworkReceived, long NetworkSent);

    // Brings the totals up to date with the counters' readings, and starts a new day when the day has turned. Returns whether
    // anything changed, so the file is written only when there is something new to write.
    public bool Add(Counters counters, DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.LocalDateTime);
        bool changed = false;
        if (Day != day)
        {
            // A new day: the totals start over, but the counters keep their readings, so the growth since the last reading of
            // yesterday counts towards today instead of being lost.
            Day = day;
            DiskRead = DiskWritten = NetworkReceived = NetworkSent = 0;
            DiskReadCounter ??= counters.DiskRead; DiskWrittenCounter ??= counters.DiskWritten;
            NetworkReceivedCounter ??= counters.NetworkReceived; NetworkSentCounter ??= counters.NetworkSent;
            return true;
        }
        changed |= Grow(DiskRead, DiskReadCounter, counters.DiskRead, out var read, out var readCounter);        DiskRead = read; DiskReadCounter = readCounter;
        changed |= Grow(DiskWritten, DiskWrittenCounter, counters.DiskWritten, out var written, out var writtenCounter);
        DiskWritten = written; DiskWrittenCounter = writtenCounter;
        changed |= Grow(NetworkReceived, NetworkReceivedCounter, counters.NetworkReceived, out var received, out var receivedCounter);
        NetworkReceived = received; NetworkReceivedCounter = receivedCounter;
        changed |= Grow(NetworkSent, NetworkSentCounter, counters.NetworkSent, out var sent, out var sentCounter);
        NetworkSent = sent; NetworkSentCounter = sentCounter;
        return changed;
    }

    // Adds what the counter grew by to the day's total, and remembers the counter's reading. A counter that went backwards starts
    // the total over from it: the growth it gave belongs to a run that is gone.
    private static bool Grow(long total, long? counter, long current, out long newTotal, out long? newCounter)
    {
        newTotal = total; newCounter = counter;
        if (counter is not { } before)
        {
            newCounter = current;
            return true;
        }
        if (current < before)
        {
            newCounter = current;
            newTotal = 0;
            return true;
        }
        long grown = current - before;
        if (grown == 0) return false;
        newCounter = current;
        newTotal = total + grown;
        return true;
    }

    // The day's totals as they are shown.
    public TrafficTotals Totals => new(DiskRead, DiskWritten, NetworkReceived, NetworkSent);

    // The day's totals as they are shown, with a fallback for a file that could not be read.
    public static TrafficToday Empty(DateTimeOffset now) => new() { Day = DateOnly.FromDateTime(now.LocalDateTime) };

    public static TrafficToday Parse(string json, DateTimeOffset now)
    {
        var empty = Empty(now);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return empty;
            var root = doc.RootElement;
            var day = root.TryGetProperty("day", out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTime(out var date)
                ? DateOnly.FromDateTime(date) : empty.Day;
            var traffic = new TrafficToday { Day = day };
            traffic.DiskRead = Number(root, "diskRead"); traffic.DiskWritten = Number(root, "diskWritten");
            traffic.NetworkReceived = Number(root, "networkReceived"); traffic.NetworkSent = Number(root, "networkSent");
            traffic.DiskReadCounter = Counter(root, "diskReadCounter"); traffic.DiskWrittenCounter = Counter(root, "diskWrittenCounter");
            traffic.NetworkReceivedCounter = Counter(root, "networkReceivedCounter"); traffic.NetworkSentCounter = Counter(root, "networkSentCounter");
            // A file from before the day turned keeps the counters' readings, so the growth since then counts towards the new
            // day; its totals belong to another day and are dropped.
            if (traffic.Day != empty.Day)
            {
                traffic.Day = empty.Day;
                traffic.DiskRead = traffic.DiskWritten = traffic.NetworkReceived = traffic.NetworkSent = 0;
            }
            return traffic;
        }
        catch (JsonException) { return empty; }
    }

    private static long Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) return 0;
        return value.TryGetInt64(out var number) ? Math.Max(0, number) : 0;
    }

    private static long? Counter(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) return null;
        return value.TryGetInt64(out var number) && number >= 0 ? number : null;
    }

    public static string Serialize(TrafficToday traffic) => JsonSerializer.Serialize(new
    {
        day = traffic.Day.ToString("yyyy-MM-dd"),
        diskRead = traffic.DiskRead,
        diskWritten = traffic.DiskWritten,
        networkReceived = traffic.NetworkReceived,
        networkSent = traffic.NetworkSent,
        diskReadCounter = traffic.DiskReadCounter,
        diskWrittenCounter = traffic.DiskWrittenCounter,
        networkReceivedCounter = traffic.NetworkReceivedCounter,
        networkSentCounter = traffic.NetworkSentCounter
    });

    public static TrafficToday Load(string path, DateTimeOffset now)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path), now) : Empty(now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Empty(now); }
    }

    // Written whole and then moved into place, so a write cut short by an exit never leaves half a file. A failed write keeps the
    // previous file; the totals stay in memory for this run, so the next write carries them.
    public static void Save(string path, TrafficToday traffic)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, Serialize(traffic));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
