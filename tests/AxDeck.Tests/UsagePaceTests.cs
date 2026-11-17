using AxDeck.Core;

// The pace estimate: it is offered only once the readings show a steady rise over enough time, it is cut off by a reset or
// by the target already being reached, and a drop in the share starts the readings over.
static class UsagePaceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public static void Run(Action<string, bool> check)
    {
        TooFewReadings(check);
        SteadyRise(check);
        Stops(check);
        Resets(check);
        OffPeak(check);
        Forgetting(check);
    }

    private static void TooFewReadings(Action<string, bool> check)
    {
        var pace = new UsagePace();
        check("UsagePace: one reading says nothing", pace.Record("k", 20, Start, 90, null, false) is null);
        check("UsagePace: two readings a minute apart say nothing", pace.Record("k", 21, Start.AddMinutes(1), 90, null, false) is null);
        // Six minutes is the least span, and a rise under half a point an hour is noise.
        check("UsagePace: a span under six minutes says nothing", pace.Record("k", 40, Start.AddMinutes(5), 90, null, false) is null);
        var flat = new UsagePace();
        for (int i = 0; i <= 6; i++) flat.Record("k", 20, Start.AddMinutes(i * 10), 90, null, false);
        check("UsagePace: a quota that is not moving says nothing", flat.Peek("k", 90, null, Start.AddHours(1)) is null);
    }

    private static void SteadyRise(Action<string, bool> check)
    {
        var pace = new UsagePace();
        // 10 points an hour: 20% at 9:00, 30% at 10:00, so 90% is reached at 16:00.
        pace.Record("k", 20, Start, 90, null, false);
        var found = pace.Record("k", 30, Start.AddHours(1), 90, null, false);
        check("UsagePace: a steady rise estimates when the target is reached",
            found is { } p && Math.Abs(p.PerHour - 10) < 0.01 && Math.Abs((p.ReachAt - Start.AddHours(7)).TotalMinutes) < 1);
        // The same estimate is read without recording a new reading.
        var peeked = pace.Peek("k", 90, null, Start.AddHours(1));
        check("UsagePace: the estimate can be read without a new reading", peeked is { } q && Math.Abs((q.ReachAt - Start.AddHours(7)).TotalMinutes) < 1);
    }

    private static void Stops(Action<string, bool> check)
    {
        var pace = new UsagePace();
        pace.Record("k", 20, Start, 90, null, false);
        // Already at or past the target: nothing left to warn about.
        check("UsagePace: a quota already at the target says nothing", pace.Record("k", 90, Start.AddHours(1), 90, null, false) is null);
        var past = new UsagePace();
        past.Record("k", 20, Start, 90, null, false);
        // An estimate that has already run out is a stale reading, not a pace.
        check("UsagePace: an estimate already past is dropped", past.Peek("k", 90, null, Start.AddDays(1)) is null);
    }

    private static void Resets(Action<string, bool> check)
    {
        var pace = new UsagePace();
        pace.Record("k", 20, Start, 90, null, false);
        // The quota resets in ten minutes, long before 90% would be reached: nothing to say yet.
        check("UsagePace: a reset before the target cuts the estimate", pace.Record("k", 30, Start.AddHours(1), 90, Start.AddMinutes(70), false) is null);
        // A share that falls starts the readings over, so a stale high reading cannot prop up a falling pace.
        var drop = new UsagePace();
        drop.Record("k", 50, Start, 90, null, false);
        drop.Record("k", 10, Start.AddMinutes(10), 90, null, false);
        check("UsagePace: a fall starts the readings over", drop.Peek("k", 90, null, Start.AddMinutes(20)) is null);
    }

    private static void OffPeak(Action<string, bool> check)
    {
        var pace = new UsagePace();
        pace.Record("k", 20, Start, 90, null, false);
        check("UsagePace: an off-peak reading is left out", pace.Record("k", 40, Start.AddHours(1), 90, null, true) is null);
        // Nothing was recorded during the off-peak hour, so the pace still rests on the single early reading.
        check("UsagePace: an off-peak reading does not feed the estimate", pace.Peek("k", 90, null, Start.AddHours(1)) is null);
    }

    private static void Forgetting(Action<string, bool> check)
    {
        var pace = new UsagePace();
        pace.Record("k", 20, Start, 90, null, false);
        pace.Record("k", 30, Start.AddHours(1), 90, null, false);
        pace.Forget([]);
        check("UsagePace: a quota that left the panel is forgotten", pace.Peek("k", 90, null, Start.AddHours(1)) is null);
    }
}