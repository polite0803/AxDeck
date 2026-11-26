using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The services page has two places that show readings: the panel's tab and the actions page in settings. The controller
// reads each snapshot once and broadcasts it, so neither place starts its own sampler. Views attach only while visible.
internal interface IServicesDisplay
{
    void SetStatus(string text, bool warning = false);
    void Update(CloudflareTunnel.StatusSnapshot status, IReadOnlyList<CloudflareTunnel.SiteProbe>? probes);
    void UpdateDiagnostics(AppDiagnostics diagnostics);
    void UpdateBge(BgeM3Service.Snapshot snapshot);
    void UpdateAxTv(AxTvEmulator.StatusSnapshot status, string avd);
}

internal sealed class ServicesDisplays
{
    private readonly List<IServicesDisplay> _views = [];

    public void Attach(IServicesDisplay view) { if (!_views.Contains(view)) _views.Add(view); }
    public void Detach(IServicesDisplay view) => _views.Remove(view);

    // A snapshot of the list: a button handler is allowed to detach a view while the broadcast is running.
    public void SetStatus(string text, bool warning = false)
    { foreach (var view in _views.ToArray()) view.SetStatus(text, warning); }

    public void Update(CloudflareTunnel.StatusSnapshot status, IReadOnlyList<CloudflareTunnel.SiteProbe>? probes)
    { foreach (var view in _views.ToArray()) view.Update(status, probes); }

    public void UpdateDiagnostics(AppDiagnostics diagnostics)
    { foreach (var view in _views.ToArray()) view.UpdateDiagnostics(diagnostics); }

    public void UpdateBge(BgeM3Service.Snapshot snapshot)
    { foreach (var view in _views.ToArray()) view.UpdateBge(snapshot); }

    public void UpdateAxTv(AxTvEmulator.StatusSnapshot status, string avd)
    { foreach (var view in _views.ToArray()) view.UpdateAxTv(status, avd); }
}
