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
        timer.Resume(now.AddMinutes(6));
        check("Focus timer: resuming keeps the same work session", timer.Running && timer.StartedAt == now && timer.History.Count == 0);

        timer.Reset(now.AddMinutes(7));
        check("Focus timer: a reset fills the countdown again", !timer.Running && timer.RemainingSeconds == 1500 && !timer.Shown);
        check("Focus timer: a reset keeps one history entry", timer.History.Count == 1 && timer.History[0].PauseCount == 1);

        check("Focus timer: the figures are minutes and seconds", FocusTimer.Format(1500) == "25:00" && FocusTimer.Format(59) == "00:59" && FocusTimer.Format(0) == "00:00");
        check("Focus timer: an hour takes three figures", FocusTimer.Format(3600) == "1:00:00" && FocusTimer.Format(3661) == "1:01:01");
        check("Focus timer: a negative figure is nothing", FocusTimer.Format(-5) == "00:00");

        var ended = new FocusTimer();
        ended.Start(now, 1);
        ended.Finish(now.AddMinutes(1));
        var endedAt = ended.EndedAt;
        ended.Resume(now.AddMinutes(2));
        ended.Finish(now.AddMinutes(3));
        check("Focus timer: an ended session cannot be resumed or recorded twice",
            !ended.Running && ended.EndedAt == endedAt && ended.History.Count == 1);

        var timeout = new FocusTimer();
        timeout.Start(now, 1);
        timeout.RemainingSeconds = 0; timeout.Running = false; timeout.EndsAt = null;
        timeout.Finish(now.AddMinutes(1));
        check("Focus timer: a completed countdown is written to history", timeout.History.Count == 1);

        var labels = new FocusTimer();
        labels.Start(now, 25, "  fix\n  sign-in  ", [ProviderId.Codex, ProviderId.Claude, ProviderId.Codex, ProviderId.Antigravity]);
        check("Focus timer: sources keep the chosen order and drop unsupported values",
            labels.Label == "fix sign-in" && labels.WorkSources.SequenceEqual([ProviderId.Codex, ProviderId.Claude]));

        // The sources the AI usage page watches for local work activity.
        var workSettings = new DeckSettings();
        check("Work sources: Claude and Codex are watched by default",
            workSettings.WorkSources.SequenceEqual([ProviderId.Claude, ProviderId.Codex]));
        var copiedWork = workSettings.Copy();
        copiedWork.WorkSources.Clear();
        copiedWork.WorkSources.Add(ProviderId.Kimi);
        check("Work sources: a settings copy owns its own list", workSettings.WorkSources.SequenceEqual([ProviderId.Claude, ProviderId.Codex])
            && copiedWork.WorkSources.SequenceEqual([ProviderId.Kimi]));
        var orderedWork = new DeckSettings { WorkSources = [ProviderId.Pi, ProviderId.Codex, ProviderId.Pi, ProviderId.Claude] };
        orderedWork.Normalize();
        check("Work sources: normalization keeps the chosen order and removes repeats",
            orderedWork.WorkSources.SequenceEqual([ProviderId.Pi, ProviderId.Codex, ProviderId.Claude]));
        var unsupportedWork = new DeckSettings { WorkSources = [ProviderId.Antigravity, ProviderId.Kimi, (ProviderId)999] };
        unsupportedWork.Normalize();
        check("Work sources: normalization drops tools without watchable local records",
            unsupportedWork.WorkSources.SequenceEqual([ProviderId.Kimi]));
        var noWork = new DeckSettings { WorkSources = [] };
        noWork.Normalize();
        check("Work sources: watching nothing is kept as an explicit choice", noWork.WorkSources.Count == 0);

        var first = new SessionActivitySnapshot(ProviderId.Codex, SessionActivityState.Active, now, 4, 1);
        var second = new SessionActivitySnapshot(ProviderId.Claude, SessionActivityState.Quiet, now.AddMinutes(-1), 2, 0);
        check("Focus timer: a sample keeps only the selected sources",
            labels.Sample(now, [ProviderId.Claude], [first, second]) && labels.Samples[0].Activity.Select(item => item.Source).SequenceEqual([ProviderId.Claude]));
        check("Focus timer: identical activity is not sampled twice",
            !labels.Sample(now.AddSeconds(30), [ProviderId.Claude], [second]) && labels.Samples.Count == 1);
        for (int i = 0; i < FocusTimer.MaximumSamples + 5; i++)
            labels.Sample(now.AddMinutes(i + 2), [ProviderId.Claude],
                [second with { LastActivity = now.AddMinutes(i) }]);
        check("Focus timer: samples are bounded", labels.Samples.Count == FocusTimer.MaximumSamples);

        var history = new FocusTimer();
        for (int i = 0; i < FocusTimer.MaximumHistory + 5; i++)
        {
            history.Start(now.AddMinutes(i * 30), 1);
            history.Finish(now.AddMinutes(i * 30 + 1));
        }
        check("Focus timer: history is bounded", history.History.Count == FocusTimer.MaximumHistory);

        // A countdown that ran while the machine was off has run out: it is not resumed.
        var json = """{"totalSeconds":1500,"remainingSeconds":1200,"running":true,"endsAt":1767268800,"startedAt":1767267300}""";
        var late = FocusTimer.Parse(json, now.AddHours(3));
        check("Focus timer: a countdown that ran out is not resumed", !late.Running && late.EndsAt is null && late.RemainingSeconds == 0);
        check("Focus timer: an elapsed running session is written to history once", late.History.Count == 1);
        var early = FocusTimer.Parse(json, now.AddMinutes(-5));
        check("Focus timer: a countdown that has not run out keeps running", early.Running && early.EndsAt is not null);
        var legacy = FocusTimer.Parse("""{"totalSeconds":1500,"remainingSeconds":1200,"running":false}""", now);
        check("Focus timer: an old countdown keeps counting without inventing a session",
            legacy.Shown && legacy.StartedAt is null && legacy.History.Count == 0);
        legacy.Reset(now);
        check("Focus timer: resetting an old countdown does not write history", legacy.History.Count == 0);
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
