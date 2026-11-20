using System.IO;
using System.Windows.Threading;
using AxDeck.Core;

namespace AxDeck;

// The focus timer: a countdown that keeps running while the panel is closed, and while AxDeck itself is not open. The state
// lives in its own file in the data folder, so a restart does not lose it. The countdown is only read once a second, and
// only while it runs: a panel that sits idle costs nothing.
public partial class App
{
    // The timer itself, shared by every panel.
    internal FocusTimer Focus { get; private set; } = new();
    // The panel shows the countdown; it is told when the time changes.
    internal event Action? FocusChanged;

    private PromptTimer? _focusTick;
    private bool _focusFinished;
    private DateTimeOffset _focusSampled;
    private bool _focusSampling;
    private int _focusSampleGeneration;

    // Reads the timer from its file and starts the countdown if it was running when AxDeck was last closed.
    internal void StartFocus()
    {
        Focus = FocusTimer.Load(Store.DirectoryPath);
        if (Focus.Running)
        {
            _focusSampled = DateTimeOffset.MinValue;
            // A countdown that ran out while AxDeck was closed is not resumed: it is shown as finished.
            _focusFinished = Focus.Finished(DateTimeOffset.Now);
            if (_focusFinished)
                SystemNotice(Loc.T("专注计时结束", "The focus timer is up"), Loc.T("计时结束，休息一下吧。", "The countdown is over. Take a break."));
            FocusTick();
        }
    }

    // The countdown's tick. It runs on a prompt timer, so the time on screen is right even while the mouse moves.
    private void FocusTick()
    {
        var now = DateTimeOffset.Now;
        if (Focus.Running && Focus.Finished(now))
        {
            // The countdown is over: it stops, and the panel is told once.
            Focus.Running = false; Focus.EndsAt = null; Focus.RemainingSeconds = 0;
            Focus.Finish(now);
            _focusFinished = true;
            SaveFocus();
            FocusChanged?.Invoke();
            SystemNotice(Loc.T("专注计时结束", "The focus timer is up"), Loc.T("计时结束，休息一下吧。", "The countdown is over. Take a break."));
            return;
        }
        SampleFocus(now);
        FocusChanged?.Invoke();
    }

    // The tick's own timer: it exists only while the countdown runs.
    private void EnsureFocusTimer()
    {
        if (_focusTick is null)
        {
            _focusTick = new PromptTimer(Dispatcher, FocusTick) { Interval = TimeSpan.FromSeconds(1) };
        }
        if (Focus.Running && !_focusTick.IsEnabled) _focusTick.Start();
        else if (!Focus.Running && _focusTick.IsEnabled) _focusTick.Stop();
    }

    // Starts, pauses or resets the countdown, and tells the panels.
    internal void FocusStart(int minutes, string? label = null, IEnumerable<ProviderId>? sources = null, Guid? projectId = null)
    {
        Focus.Start(DateTimeOffset.Now, minutes, label, sources ?? Settings.WorkSources, projectId);
        FocusStarted();
    }

    // Starts a countdown from a registered project's own label and work source. An unknown id falls back to an ordinary
    // countdown, so a stale palette or address never starts work under a project that no longer exists.
    internal void FocusStartProject(Guid id, int minutes = 25)
    {
        var project = Projects.Find(id);
        var sources = project is not null && SessionSources.Supports(project.WorkSource)
            ? new List<ProviderId> { project.WorkSource }
            : Settings.WorkSources.ToList();
        FocusStart(minutes, project?.DisplayName, sources, project?.Id);
    }

    // A saved session is a new run of the same work, not an edit of the old record: its target, label, sources and project
    // are copied into a fresh countdown, while the old history entry stays exactly as it was.
    internal void FocusResume(long startedAtUnix)
    {
        var session = Focus.History.FirstOrDefault(item => item.StartedAt.ToUnixTimeSeconds() == startedAtUnix);
        if (session is null) return;
        int seconds = session.TargetSeconds > 0 ? session.TargetSeconds : FocusTimer.Presets[1] * 60;
        Focus.StartSeconds(DateTimeOffset.Now, seconds, session.Label, session.WorkSources, session.ProjectId);
        FocusStarted();
    }

    private void FocusStarted()
    {
        _focusFinished = false;
        _focusSampled = DateTimeOffset.MinValue;
        if (!_focusSampling) SampleFocus(DateTimeOffset.Now);
        SaveFocus();
        EnsureFocusTimer();
        FocusChanged?.Invoke();
    }

    internal void FocusPause()
    {
        if (!Focus.Running) return;
        Focus.Pause(DateTimeOffset.Now);
        SaveFocus();
        EnsureFocusTimer();
        FocusChanged?.Invoke();
    }

    internal void FocusResume()
    {
        if (Focus.Running || !Focus.Shown) return;
        Focus.Resume(DateTimeOffset.Now);
        _focusFinished = false;
        if (!_focusSampling) SampleFocus(DateTimeOffset.Now);
        SaveFocus();
        EnsureFocusTimer();
        FocusChanged?.Invoke();
    }

    internal void FocusReset()
    {
        Focus.Reset(DateTimeOffset.Now);
        _focusFinished = false;
        SaveFocus();
        EnsureFocusTimer();
        FocusChanged?.Invoke();
    }

    // The running session asks for local session metadata every 30 seconds. Only timestamps and counts are recorded; a
    // pause or the end of the countdown stops this low-frequency demand.
    private void SampleFocus(DateTimeOffset now)
    {
        if (!Focus.Running || _focusSampling || now - _focusSampled < TimeSpan.FromSeconds(30)) return;
        _focusSampled = now;
        var sources = Focus.WorkSources.Distinct().Where(SessionSources.Supports).ToList();
        if (sources.Count == 0) return;
        _focusSampling = true;
        int generation = ++_focusSampleGeneration;
        var locations = Usage.Locations;
        _ = Task.Run(() =>
        {
            var snapshots = new List<SessionActivitySnapshot>();
            foreach (var source in sources)
            {
                try { snapshots.Add(SessionSources.Read(source, locations, now)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            return snapshots;
        }).ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (generation != _focusSampleGeneration) return;
            _focusSampling = false;
            if (!task.IsCompletedSuccessfully) return;
            if (Focus.Sample(DateTimeOffset.Now, sources, task.Result))
            {
                SaveFocus();
                FocusChanged?.Invoke();
            }
        })), TaskScheduler.Default);
    }

    // True when the countdown has run out and was not restarted yet.
    internal bool FocusFinished => _focusFinished;

    // Writes the countdown to its file. The file is the timer's own; nothing else is written and nothing is sent anywhere.
    private void SaveFocus()
    {
        Focus.Save(Store.DirectoryPath);
        EnsureFocusTimer();
    }
}
