using System.Diagnostics;
using AxDeck.Core;

namespace AxDeck.Native;

// A short, one-off sample of AxDeck's own cost. It runs on the same background task as the services refresh and is never put
// on a timer: two snapshots of the process's cumulative CPU time over a small window give a share of the whole machine, as Task
// Manager shows it. A failed read is a Reading without a value, so the page can say why instead of drawing zero.
internal static class ProcessProbe
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(250);

    public static Reading<ProcessResourceUsage> Sample(TimeSpan? window = null)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var started = Stopwatch.GetTimestamp();
            var firstCpu = process.TotalProcessorTime;
            Thread.Sleep(window ?? DefaultWindow);
            process.Refresh();
            var elapsed = Stopwatch.GetElapsedTime(started);
            var cpu = process.TotalProcessorTime - firstCpu;
            int processors = Math.Max(Environment.ProcessorCount, 1);
            // TotalProcessorTime covers all logical processors. Dividing by processors turns it into the machine-wide share
            // used by the monitor and Task Manager rather than a share of one core.
            double percent = elapsed > TimeSpan.Zero
                ? Math.Clamp(cpu.TotalMilliseconds / (elapsed.TotalMilliseconds * processors) * 100, 0, 100)
                : 0;
            return new ProcessResourceUsage(
                percent, elapsed, checked((ulong)Math.Max(process.WorkingSet64, 0)), checked((ulong)Math.Max(process.PrivateMemorySize64, 0)),
                process.HandleCount, process.Threads.Count, processors);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Reading<ProcessResourceUsage>.Unavailable(Loc.T("无法读取本进程开销：", "Could not read this process's cost: ") + ex.Message);
        }
    }
}
