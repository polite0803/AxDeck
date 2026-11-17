using System.IO;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The day's traffic, counted in the background and kept on disk: the bytes read from and written to the disks, and the bytes
// received over and sent through the network, since the day began (TrafficToday). The counters are read on a thread of their own
// every few seconds, so the panel's cost is the same whether it shows the traffic or not, and the counts keep up while the panel
// is closed.
public partial class App
{
    // How often the counters are read. The disks' query costs tens of milliseconds, the network's a few; a few seconds is often
    // enough for a day's total and costs nothing.
    private static readonly TimeSpan TrafficEvery = TimeSpan.FromSeconds(10);
    // How often the totals are written: the counts change slowly, and the file is small.
    private static readonly TimeSpan TrafficWriteEvery = TimeSpan.FromMinutes(5);

    private TrafficCounters? _traffic;
    private TrafficToday? _trafficToday;
    private System.Threading.Timer? _trafficTimer;
    private int _trafficWorking;

    private string TrafficPath => Path.Combine(Store.DirectoryPath, TrafficToday.FileName);

    // The day's traffic as it stands, or null while it is not known yet.
    internal TrafficToday? Today => _trafficToday;

    // Starts counting the day's traffic. Called once, when the app starts.
    internal void StartTraffic()
    {
        if (_traffic is not null) return;
        _traffic = new TrafficCounters();
        _trafficToday = TrafficToday.Load(TrafficPath, DateTimeOffset.Now);
        // The first reading of the disks' counters costs about half a second (WMI starts its providers), so it is made on the
        // thread of its own and not on the thread that starts the app.
        _trafficTimer = new System.Threading.Timer(_ => CountTraffic(), null, TimeSpan.Zero, TrafficEvery);
    }

    // Reads the counters and adds what they grew by to the day's totals.
    private void CountTraffic()
    {
        if (_traffic is not { } counters || Exiting) return;
        // One reading at a time: a slow WMI query must not be started again before the last one ended.
        if (System.Threading.Interlocked.Exchange(ref _trafficWorking, 1) == 1) return;
        try
        {
            counters.ReadNetwork();
            var now = StopwatchTimestamp();
            if (counters.DiskDue(now)) counters.ReadDisk(now);
            var readings = counters.Counters();
            if (readings is not { } values) return;
            if (_trafficToday is not { } today) return;
            if (!today.Add(values, DateTimeOffset.Now)) return;
            SaveTraffic();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        finally { System.Threading.Interlocked.Exchange(ref _trafficWorking, 0); }
    }

    private static double StopwatchTimestamp() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    // The totals written out, so they survive a restart. Written at most every TrafficWriteEvery, and on the way out.
    private double _trafficWrittenAt = double.NegativeInfinity;
    private void SaveTraffic()
    {
        if (_trafficToday is not { } today) return;
        double now = StopwatchTimestamp();
        if (now - _trafficWrittenAt < TrafficWriteEvery.TotalSeconds) return;
        _trafficWrittenAt = now;
        TrafficToday.Save(TrafficPath, today);
    }

    // The day's totals written out before the process ends, so the last minutes are not lost.
    private void FlushTraffic()
    {
        _trafficTimer?.Dispose(); _trafficTimer = null;
        if (_trafficToday is { } today) TrafficToday.Save(TrafficPath, today);
    }

    // The snapshot with the day's traffic on it, as it stands. The traffic is counted apart from the other readings, so it is
    // added here rather than in the sampler.
    internal MonitorSnapshot WithTraffic(MonitorSnapshot snapshot)
        => _trafficToday is { } today ? snapshot with { Traffic = today.Totals } : snapshot;
}
