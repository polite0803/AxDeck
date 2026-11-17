using AxDeck.Core;

// The day's traffic: the totals grow with what the counters grew by, a new day starts them over, and the file they are
// written to survives a restart without counting the same growth twice.
static class TrafficTodayTests
{
    private static readonly DateTimeOffset Day = new(2026, 3, 14, 9, 0, 0, TimeSpan.FromHours(8));

    public static void Run(Action<string, bool> check)
    {
        SameDayAccumulates(check);
        NewDayStartsOver(check);
        CounterGoingBackStartsOver(check);
        FileRoundTrip(check);
        BrokenFileIsIgnored(check);
        UnknownMembersAreIgnored(check);
    }

    private static void SameDayAccumulates(Action<string, bool> check)
    {
        var traffic = TrafficToday.Empty(Day);
        check("TrafficToday: the first reading sets the baseline", traffic.Add(new(100, 50, 10, 5), Day));
        check("TrafficToday: the first reading counts no bytes", traffic.DiskRead == 0);
        check("TrafficToday: a growth is a change", traffic.Add(new(300, 150, 30, 15), Day));
        check("TrafficToday: the disk read grew by 200", traffic.DiskRead == 200);
        check("TrafficToday: the disk written grew by 100", traffic.DiskWritten == 100);
        check("TrafficToday: the network received grew by 20", traffic.NetworkReceived == 20);
        check("TrafficToday: the network sent grew by 10", traffic.NetworkSent == 10);
        check("TrafficToday: the same reading changes nothing", !traffic.Add(new(300, 150, 30, 15), Day));
    }

    private static void NewDayStartsOver(Action<string, bool> check)
    {
        var traffic = TrafficToday.Empty(Day);
        traffic.Add(new(100, 100, 100, 100), Day);
        traffic.Add(new(200, 200, 200, 200), Day);
        var next = Day.AddDays(1);
        check("TrafficToday: a new day is a change", traffic.Add(new(200, 200, 200, 200), next));
        check("TrafficToday: the new day starts at zero", traffic.DiskRead == 0);
        check("TrafficToday: the day is the new one", traffic.Day == DateOnly.FromDateTime(next.LocalDateTime));
        // The counters kept their readings, so what they grow by from here on counts towards the new day.
        check("TrafficToday: the growth after the turn counts", traffic.Add(new(350, 350, 350, 350), next));
        check("TrafficToday: 150 bytes counted for the new day", traffic.DiskRead == 150);
    }

    private static void CounterGoingBackStartsOver(Action<string, bool> check)
    {
        var traffic = TrafficToday.Empty(Day);
        traffic.Add(new(1000, 1000, 1000, 1000), Day);
        traffic.Add(new(2000, 2000, 2000, 2000), Day);
        check("TrafficToday: a thousand counted", traffic.DiskRead == 1000);
        // The counter went backwards: it was read again after a restart, and its growth belongs to a run that is gone.
        check("TrafficToday: a counter that went backwards is a change", traffic.Add(new(500, 2000, 2000, 2000), Day));
        check("TrafficToday: the total starts over", traffic.DiskRead == 0);
        check("TrafficToday: the other totals stay", traffic.DiskWritten == 1000);
    }

    private static void FileRoundTrip(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AxDeck.Tests.Traffic." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, TrafficToday.FileName);
            var traffic = TrafficToday.Empty(Day);
            traffic.Add(new(100, 100, 100, 100), Day);
            traffic.Add(new(400, 400, 400, 400), Day);
            TrafficToday.Save(path, traffic);

            var read = TrafficToday.Load(path, Day);
            check("TrafficToday: the totals come back", read.DiskRead == 300);
            check("TrafficToday: the baseline comes back", read.DiskReadCounter == 400);
            // After the restart the counters are read again: what they grew by since is counted, and the growth before the
            // restart is not counted twice.
            check("TrafficToday: the growth after the restart counts", read.Add(new(700, 700, 700, 700), Day));
            check("TrafficToday: 600 bytes in all", read.DiskRead == 600);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void BrokenFileIsIgnored(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AxDeck.Tests.Traffic." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, TrafficToday.FileName);
            File.WriteAllText(path, "not json at all");
            var read = TrafficToday.Load(path, Day);
            check("TrafficToday: a broken file starts from zero", read.DiskRead == 0);
            check("TrafficToday: a broken file counts for today", read.Day == DateOnly.FromDateTime(Day.LocalDateTime));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void UnknownMembersAreIgnored(Action<string, bool> check)
    {
        var read = TrafficToday.Parse("{\"day\":\"2026-03-14\",\"diskRead\":123,\"somethingNew\":{\"a\":1}}", Day);
        check("TrafficToday: the known members are read", read.DiskRead == 123);
        check("TrafficToday: the missing members take their default", read.DiskWritten == 0);
    }
}
