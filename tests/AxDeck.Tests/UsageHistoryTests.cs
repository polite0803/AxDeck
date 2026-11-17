using AxDeck.Core;

// The kept usage curves: at most one sample every ten minutes, a month kept, a fall starting the curve over, and a file that
// survives a round trip through JSON.
static class UsageHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public static void Run(Action<string, bool> check)
    {
        Thinning(check);
        Falls(check);
        Pruning(check);
        RoundTrip(check);
        Damaged(check);
    }

    private static void Thinning(Action<string, bool> check)
    {
        var history = new Dictionary<string, List<UsageHistory.Sample>>();
        check("UsageHistory: a first reading is kept", UsageHistory.Add(history, [("k", 20)], Start));
        // Inside the interval: no second sample, and nothing to write.
        check("UsageHistory: a reading inside the interval is not a new sample", !UsageHistory.Add(history, [("k", 25)], Start.AddMinutes(5)));
        check("UsageHistory: the kept sample is the first one", history["k"] is { Count: 1 } samples && samples[0].Used == 20);
        check("UsageHistory: a reading past the interval is kept", UsageHistory.Add(history, [("k", 30)], Start.AddMinutes(10)) && history["k"].Count == 2);
        // An unchanged share only moves the last sample's time, so a curve is not made of one flat value.
        check("UsageHistory: an unchanged share moves the last sample", UsageHistory.Add(history, [("k", 30)], Start.AddMinutes(20)) && history["k"].Count == 2
            && history["k"][^1].Time == Start.AddMinutes(20));
    }

    private static void Falls(Action<string, bool> check)
    {
        var history = new Dictionary<string, List<UsageHistory.Sample>>();
        UsageHistory.Add(history, [("k", 50)], Start);
        UsageHistory.Add(history, [("k", 60)], Start.AddMinutes(10));
        // A share that fell: the quota reset, so the older samples describe a period that has ended.
        UsageHistory.Add(history, [("k", 5)], Start.AddMinutes(20));
        check("UsageHistory: a fall starts the curve over", history["k"] is { Count: 1 } samples && samples[0].Used == 5);
    }

    private static void Pruning(Action<string, bool> check)
    {
        var history = new Dictionary<string, List<UsageHistory.Sample>>();
        UsageHistory.Add(history, [("old", 10)], Start);
        // Past the kept month: the sample, and with it the quota, is dropped.
        UsageHistory.Add(history, [], Start + UsageHistory.Keep + TimeSpan.FromDays(1));
        check("UsageHistory: a sample past the kept month is dropped", !history.ContainsKey("old"));
    }

    private static void RoundTrip(Action<string, bool> check)
    {
        var history = new Dictionary<string, List<UsageHistory.Sample>>();
        UsageHistory.Add(history, [("a:b:c", 12.5)], Start);
        UsageHistory.Add(history, [("a:b:c", 40)], Start.AddMinutes(10));
        var read = UsageHistory.Parse(UsageHistory.Serialize(history), Start.AddMinutes(20));
        check("UsageHistory: a file reads back as it was written", read.TryGetValue("a:b:c", out var samples)
            && samples.Count == 2 && samples[0].Used == 12.5 && samples[1].Used == 40);
        check("UsageHistory: the times survive the round trip", read["a:b:c"][1].Time == Start.AddMinutes(10));
        // Recent gives what a curve draws, within its span, oldest first.
        var recent = UsageHistory.Recent(read, "a:b:c", Start.AddMinutes(20), TimeSpan.FromMinutes(15));
        check("UsageHistory: recent keeps only the samples inside its span", recent.Count == 1 && recent[0] == 40);
    }

    private static void Damaged(Action<string, bool> check)
    {
        check("UsageHistory: a damaged file holds no curves", UsageHistory.Parse("{not json", Start).Count == 0);
        check("UsageHistory: a file of another shape holds no curves", UsageHistory.Parse("[]", Start).Count == 0);
        // A reading from the future, from a clock set back since, is not kept: it would sit at the right edge forever.
        var future = UsageHistory.Parse("{\"k\":[[\\\"" + Start.AddDays(2).ToString("O") + "\\\",40]]}", Start);
        check("UsageHistory: a reading from the future is dropped", !future.ContainsKey("k"));
        var bad = UsageHistory.Parse("{\"k\":[[\\\"nonsense\\\",40],[\"2026-01-01T09:00:00+00:00\",\"bad\"]]}", Start.AddHours(1));
        check("UsageHistory: a malformed sample is skipped", !bad.ContainsKey("k"));
    }
}