using System.IO;
using AxDeck.Core;

namespace AxDeck;

public partial class App
{
    // How long the answer is kept before the session folders are walked again. Half a minute keeps the status current while a panel
    // is open without walking the folders on every rebuild.
    private static readonly TimeSpan ActivityHold = TimeSpan.FromSeconds(30);
    private DateTimeOffset _activityAsked;
    private bool _activityAsking;
    private int _activityGeneration;
    private IReadOnlyDictionary<ProviderId, SessionActivitySnapshot> _activity = new Dictionary<ProviderId, SessionActivitySnapshot>();

    // The selected tools' latest local activity. Only the records' metadata is read; the last answer is what the drawing
    // thread sees.
    internal IReadOnlyDictionary<ProviderId, SessionActivitySnapshot> Activity => _activity;
    internal bool ActivityReadInProgress => _activityAsking;

    // Asks again in the background when the answer has rested long enough, and draws the usage page once when it changes.
    // The source list is independent of quota columns: a tool can be watched while its quota is not on the dashboard.
    internal void AskActivity()
    {
        var now = DateTimeOffset.Now;
        if (_activityAsking || now < _activityAsked || now - _activityAsked < ActivityHold) return;
        var sources = Settings.WorkSources.Distinct().Where(SessionSources.Supports).ToList();
        if (sources.Count == 0)
        {
            _activityAsked = now;
            if (_activity.Count > 0)
            {
                _activity = new Dictionary<ProviderId, SessionActivitySnapshot>();
                RedrawActivity();
            }
            return;
        }
        _activityAsked = now;
        _activityAsking = true;
        int generation = ++_activityGeneration;
        var locations = Usage.Locations;
        _ = Task.Run(() =>
        {
            var snapshots = new Dictionary<ProviderId, SessionActivitySnapshot>();
            foreach (var source in sources)
            {
                try { snapshots[source] = SessionSources.Read(source, locations, now); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            return snapshots;
        }).ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
        {
            // A settings change may have started a newer walk, or cleared the selection, while this one was running.
            if (generation != _activityGeneration) return;
            _activityAsking = false;
            if (!task.IsCompletedSuccessfully || SameActivity(task.Result, _activity)) return;
            _activity = task.Result;
            RedrawActivity();
        })), TaskScheduler.Default);
    }

    // Settings changed the watched tools: forget the old answer and its pending walk immediately, then read the new selection
    // without waiting out the cache. The panel can redraw with the empty answer before the new one lands.
    internal void WorkSourcesChanged()
    {
        _activityGeneration++;
        _activityAsking = false;
        _activityAsked = DateTimeOffset.MinValue;
        _activity = new Dictionary<ProviderId, SessionActivitySnapshot>();
        RedrawActivity();
        AskActivity();
    }

    private void RedrawActivity()
    {
        foreach (var deck in Decks.ToList()) if (deck.IsExpanded && (deck.IsUsagePage || deck.IsWorkPage)) deck.RenderUsage();
    }

    private static bool SameActivity(
        IReadOnlyDictionary<ProviderId, SessionActivitySnapshot> left,
        IReadOnlyDictionary<ProviderId, SessionActivitySnapshot> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var other) && pair.Value == other);
}
