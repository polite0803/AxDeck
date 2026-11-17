using AxDeck.Core;

// The focus timer: the countdown, its figures, and its file. All of it is pure, so it is checked here.
static class FocusTimerTests
{
    public static void Run(Action<string, bool> check)
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timer = new FocusTimer();
        check("Focus timer: a new timer shows nothing", !timer.Shown);

        timer.Start(now, 25);
        check("Focus timer: starting fills the countdown", timer.Running && timer.TotalSeconds == 1500 && timer.Remaining(now) == 1500 && timer.Shown);
        check("Focus timer: a minute passes", timer.Remaining(now.AddMinutes(1)) == 1440);
        check("Focus timer: the countdown runs out", timer.Finished(now.AddMinutes(25)) && !timer.Finished(now.AddMinutes(24)));

        timer.Pause(now.AddMinutes(5));
        check("Focus timer: pausing keeps the time that is left", !timer.Running && timer.RemainingSeconds == 1200 && timer.Shown);
        check("Focus timer: a paused countdown does not run out", !timer.Finished(now.AddHours(1)));

        timer.Reset();
        check("Focus timer: a reset fills the countdown again", !timer.Running && timer.RemainingSeconds == 1500 && !timer.Shown);

        check("Focus timer: the figures are minutes and seconds", FocusTimer.Format(1500) == "25:00" && FocusTimer.Format(59) == "00:59" && FocusTimer.Format(0) == "00:00");
        check("Focus timer: an hour takes three figures", FocusTimer.Format(3600) == "1:00:00" && FocusTimer.Format(3661) == "1:01:01");
        check("Focus timer: a negative figure is nothing", FocusTimer.Format(-5) == "00:00");

        // A countdown that ran while the machine was off has run out: it is not resumed.
        var json = """{"totalSeconds":1500,"remainingSeconds":1200,"running":true,"endsAt":1767268800}""";
        var late = FocusTimer.Parse(json, now.AddHours(3));
        check("Focus timer: a countdown that ran out is not resumed", !late.Running && late.EndsAt is null && late.RemainingSeconds == 0);
        var early = FocusTimer.Parse(json, now.AddMinutes(-5));
        check("Focus timer: a countdown that has not run out keeps running", early.Running && early.EndsAt is not null);
        check("Focus timer: a broken file reads as a timer that was never started", !FocusTimer.Parse("{oops", now).Running);
        check("Focus timer: no file at all reads the same", !FocusTimer.Parse(null, now).Running);

        // The file the countdown is kept in.
        var directory = Path.Combine(Path.GetTempPath(), "axdeck-focus-" + Guid.NewGuid().ToString("N"));
        try
        {
            var kept = new FocusTimer();
            kept.Start(DateTimeOffset.Now, 45);
            kept.Save(directory);
            var read = FocusTimer.Load(directory);
            check("Focus timer: the countdown survives the file", read.Running && read.TotalSeconds == 2700 && read.EndsAt is not null);
            check("Focus timer: the file is its own", File.Exists(Path.Combine(directory, FocusTimer.FileName)));
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
