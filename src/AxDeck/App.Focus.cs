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

    // Reads the timer from its file and starts the countdown if it was running when AxDeck was last closed.
    internal void StartFocus()
    {
        Focus = FocusTimer.Load(Store.DirectoryPath);
        if (Focus.Running)
        {
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
            _focusFinished = true;
            SaveFocus();
            FocusChanged?.Invoke();
            SystemNotice(Loc.T("专注计时结束", "The focus timer is up"), Loc.T("计时结束，休息一下吧。", "The countdown is over. Take a break."));
            return;
        }
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
    internal void FocusStart(int minutes)
    {
        Focus.Start(DateTimeOffset.Now, minutes);
        _focusFinished = false;
        SaveFocus();
        EnsureFocusTimer();
        FocusChanged?.Invoke();
    }

    internal void FocusPause()
    {
        Focus.Pause(DateTimeOffset.Now);
        SaveFocus();
        EnsureFocusTimer();
        FocusChanged?.Invoke();
    }

    internal void FocusReset()
    {
        Focus.Reset();
        _focusFinished = false;
        SaveFocus();
        EnsureFocusTimer();
        FocusChanged?.Invoke();
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
