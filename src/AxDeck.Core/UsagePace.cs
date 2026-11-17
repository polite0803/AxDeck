namespace AxDeck.Core;

// Estimates from the quota readings seen so far when a quota will reach a share, from how fast it has been rising. A sliding
// window in memory only: nothing is written, so the estimate is forgotten when the app closes. It is only offered once the
// readings show a steady rise over enough time; with too little data, or a quota that is not moving, nothing is said — the
// panel never invents a figure.
public sealed class UsagePace
{
    // Readings older than this are dropped: an estimate over hours of history says little about now.
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);
    // The least time between the first and the last reading in the window before an estimate is offered at all.
    private static readonly TimeSpan LeastSpan = TimeSpan.FromMinutes(6);
    // Below this many percentage points per hour the rise is rounding noise, not a pace worth naming.
    private const double LeastRisePerHour = 0.5;

    private readonly record struct Sample(DateTimeOffset At, double Used);
    private readonly Dictionary<string, List<Sample>> _samples = [];

    // A quota's guess at what is coming: the share now, the rise per hour, and the moment the target share is reached.
    public readonly record struct Pace(double Used, double PerHour, DateTimeOffset ReachAt);

    // Records one reading of a quota and returns the pace, or null when the readings so far do not support one.
    // used is the used share (0 to 100), never what is left, so a rising number always means more used. target is the share
    // the estimate counts towards (the row's critical level), and resetAt cuts it off: a quota that resets before the target
    // is reached is never reported as running out. While the row is inside an off-peak period nothing is recorded, as that
    // time follows its own prices and says nothing about the pace afterwards.
    public Pace? Record(string key, double used, DateTimeOffset at, double target, DateTimeOffset? resetAt, bool offPeakNow)
    {
        if (!_samples.TryGetValue(key, out var samples)) _samples[key] = samples = [];
        // A share that went down means the quota was reset or the reading was corrected: what came before says nothing about
        // the period that is starting now, and keeping it would turn a fresh rise into a shrinking one.
        if (samples.Count > 0 && (used < samples[^1].Used || at < samples[^1].At)) samples.Clear();
        if (offPeakNow) return null;
        if (samples.Count > 0 && samples[^1].At == at) samples[^1] = new Sample(at, used);
        else samples.Add(new Sample(at, used));
        while (samples.Count > 1 && samples[1].At <= at - Window) samples.RemoveAt(0);
        return Estimate(samples, target, resetAt, at);
    }

    // The same estimate as Record found, without recording anything: for drawing, where no new reading has arrived.
    public Pace? Peek(string key, double target, DateTimeOffset? resetAt, DateTimeOffset now)
        => _samples.TryGetValue(key, out var samples) ? Estimate(samples, target, resetAt, now) : null;

    private static Pace? Estimate(List<Sample> samples, double target, DateTimeOffset? resetAt, DateTimeOffset now)
    {
        if (samples.Count < 2) return null;
        var first = samples[0];
        var last = samples[^1];
        var span = last.At - first.At;
        if (span < LeastSpan) return null;
        double perHour = (last.Used - first.Used) / span.TotalHours;
        if (perHour < LeastRisePerHour || last.Used >= target) return null;
        var reach = last.At + TimeSpan.FromHours((target - last.Used) / perHour);
        // A time already past is a stale reading, not a pace; and a quota that resets before the target is reached empties
        // first, so there is nothing to warn about yet.
        if (reach <= now) return null;
        if (resetAt is { } cut && reach > cut) return null;
        return new Pace(last.Used, perHour, reach);
    }

    // Forgets the quotas that have left the panel, so their readings do not linger.
    public void Forget(IEnumerable<string> live)
    {
        var keep = new HashSet<string>(live);
        foreach (var key in _samples.Keys.Where(key => !keep.Contains(key)).ToList()) _samples.Remove(key);
    }

    public void Clear() => _samples.Clear();
}