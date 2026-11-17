using System.IO;
using AxDeck.Core;

namespace AxDeck;

public partial class App
{
    // How long the answer is kept before the session folders are walked again. Half a minute keeps the dot current while a panel
    // is open without walking the folders on every rebuild.
    private static readonly TimeSpan ActivityHold = TimeSpan.FromSeconds(30);
    private DateTimeOffset _activityAsked;
    private bool _activityAsking;
    private IReadOnlySet<ProviderId> _activityWorking = new HashSet<ProviderId>();

    // The tools writing their session records right now, for the dot on their column (SessionActivity). Only the records' last
    // write times are read; the answer of the last walk, never asked for on the drawing thread.
    internal IReadOnlySet<ProviderId> Working => _activityWorking;

    // Asks again in the background when the answer has rested long enough, and draws the columns once when it changes. Walking
    // another tool's session folders takes as long as it takes, so it never runs on the UI thread: the dot shows the last answer
    // until a new one arrives.
    internal void AskActivity()
    {
        var now = DateTimeOffset.Now;
        if (_activityAsking || now < _activityAsked || now - _activityAsked < ActivityHold) return;
        _activityAsked = now; _activityAsking = true;
        var sources = Settings.EnabledApps.Select(entry => entry.QuotaSource).Distinct().ToList();
        var locations = Usage.Locations;
        _ = Task.Run(() =>
        {
            var working = new HashSet<ProviderId>();
            foreach (var source in sources)
            {
                try { if (SessionSources.IsWorking(source, locations, now)) working.Add(source); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            return working;
        }).ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
        {
            _activityAsking = false;
            if (!task.IsCompletedSuccessfully || task.Result.SetEquals(_activityWorking)) return;
            _activityWorking = task.Result;
            // Only a panel that shows the columns is built again: the dot goes with them, and a collapsed panel draws nothing.
            foreach (var deck in Decks) if (deck.IsExpanded && deck.IsUsagePage) deck.RenderUsage();
        })), TaskScheduler.Default);
    }
}