using AxDeck.Core;

// The day's work view joins only records AxDeck already keeps on this PC. No project directory, process, window title or
// conversation is read; a missing session or an old record simply does not appear in today's account.
static class WorkOverviewTests
{
    public static void Run(Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(8));
        var midnight = new DateTimeOffset(now.Date, now.Offset);
        var project = new ProjectEntry { Id = Guid.NewGuid(), DisplayName = "AxDeck", RootPath = @"D:\work\AxDeck", WorkSource = ProviderId.Codex };
        var other = new ProjectEntry { Id = Guid.NewGuid(), DisplayName = "Other", RootPath = @"D:\work\Other" };
        var timer = new FocusTimer();
        timer.Start(midnight.AddHours(14), 45, "Afternoon", [ProviderId.Codex], other.Id);
        timer.Finish(midnight.AddHours(14).AddMinutes(45));
        timer.Start(now.AddDays(-1), 15, "Yesterday", [ProviderId.Claude]);
        timer.Finish(now.AddDays(-1).AddMinutes(15));
        timer.Start(midnight.AddHours(9), 25, "Morning", [ProviderId.Codex, ProviderId.Claude], project.Id);
        timer.Sample(midnight.AddHours(9).AddMinutes(10), [ProviderId.Codex, ProviderId.Claude],
            [new SessionActivitySnapshot(ProviderId.Codex, SessionActivityState.Active, midnight.AddHours(9).AddMinutes(10), 3, 1)]);
        timer.Finish(midnight.AddHours(9).AddMinutes(25));

        var view = WorkOverviewBuilder.Build(timer, [project, other], now);
        check("Work overview: only sessions that ended today are counted",
            view.FocusCount == 2 && view.FocusMinutes == 70 && view.Projects.Count == 2);
        check("Work overview: a session's own metadata names the source that was working",
            view.ActiveSourceCount == 1 && view.ActiveSources.SequenceEqual([ProviderId.Codex]));
        check("Work overview: project rows carry their own counts and newest time",
            view.Projects.Single(row => row.Project.Id == project.Id) is { FocusCount: 1, FocusMinutes: 25 }
            && view.Projects.Single(row => row.Project.Id == other.Id) is { FocusCount: 1, FocusMinutes: 45 }
            && view.Projects.All(row => row.LastFocusedAt >= midnight));

        var quiet = WorkOverviewBuilder.Build(timer, [project, other], now.AddDays(-1));
        check("Work overview: another day starts empty instead of inventing activity",
            quiet.FocusCount == 0 && quiet.FocusMinutes == 0 && quiet.ActiveSourceCount == 0 && quiet.Projects.Count == 0);
        var stale = WorkOverviewBuilder.Build(timer, [], now);
        check("Work overview: a session naming a removed project is still reported honestly",
            stale.Projects.Count == 2 && stale.Projects.All(row => row.Project.DisplayName == "未登记的项目"));

        var localNow = new DateTimeOffset(2026, 11, 20, 23, 30, 0, TimeSpan.FromHours(-5));
        var localTimer = new FocusTimer();
        localTimer.Start(localNow.AddDays(-1), 25, "Local", [ProviderId.Codex]);
        localTimer.Finish(localNow.AddDays(-1).AddMinutes(25));
        var localDay = WorkOverviewBuilder.Build(localTimer, [], localNow);
        check("Work overview: another offset's local midnight does not pull in yesterday",
            localDay.FocusCount == 0 && localDay.Projects.Count == 0);
    }
}
