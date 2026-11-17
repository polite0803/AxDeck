using System.IO;
using System.Windows.Threading;
using AxDeck.Core;

namespace AxDeck;

public partial class App
{
    // The quota readings kept for the curves: loaded once at start, added to as readings come, and written back at most once
    // every WriteEvery. Keeping it here rather than in the panel means the panels of several displays share one history, and a
    // restarted panel does not start its curve over.
    private Dictionary<string, List<UsageHistory.Sample>> _history = new(StringComparer.Ordinal);
    private DispatcherTimer? _historyWrite;
    private bool _historyLoaded;

    private string HistoryPath => Path.Combine(Store.DirectoryPath, UsageHistory.FileName);

    // Called with each quota reading the panel takes: the used share, not what is left, so a rising curve always means more
    // used. The readings are added whether or not any panel is showing, so a curve covers the time the panel was hidden too.
    internal void RecordHistory(IEnumerable<(string Key, double Used)> readings)
    {
        if (!_historyLoaded)
        {
            _history = UsageHistory.Load(HistoryPath, DateTimeOffset.Now);
            _historyLoaded = true;
        }
        if (!UsageHistory.Add(_history, readings, DateTimeOffset.Now)) return;
        if (_historyWrite is null)
        {
            _historyWrite = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = WriteEvery };
            _historyWrite.Tick += (_, _) => FlushHistory();
        }
        // Restarted on each change, so a run of readings becomes one write rather than one write each.
        _historyWrite.Stop(); _historyWrite.Start();
    }

    // A quarter of an hour between writes: the file holds samples a quarter of an hour apart at most, so nothing is lost by
    // waiting, and a hidden panel costs no disk access at all.
    private static readonly TimeSpan WriteEvery = TimeSpan.FromMinutes(15);

    private void FlushHistory()
    {
        _historyWrite?.Stop();
        if (_historyLoaded) UsageHistory.Save(HistoryPath, _history);
    }

    // The shares to draw for a quota, oldest first, within the last span; empty when nothing has been recorded yet.
    internal IReadOnlyList<double> History(string key, TimeSpan span)
        => _historyLoaded ? UsageHistory.Recent(_history, key, DateTimeOffset.Now, span) : [];
}