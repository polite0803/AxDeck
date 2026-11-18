using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AxDeck.Core;

namespace AxDeck;

// The focus timer's countdown in the collapsed row (CompactPart.Focus). The badge shows the time that is left, and the row
// keeps its width: the badge reserves the room of the longest figure the countdown can show, so the seconds passing do not
// move the row. Clicking the badge opens the timer.
public partial class MainWindow
{
    private FocusBadge? _compactFocus;
    // Whether the countdown was on show the last time the row was built; a change of that rebuilds the row, a second passing
    // only changes the text.
    private bool _focusShown;

    // The panel listens for the countdown's changes; the badge follows them.
    private void InitializeFocus()
    {
        _focusShown = Settings.ShowsFocus(EffectiveStyle) && _app.Focus.Shown;
        _app.FocusChanged += FocusRefresh;
        Closed += (_, _) => _app.FocusChanged -= FocusRefresh;
    }

    // The countdown's badge, or nothing when the timer was never started or the part is switched off.
    private FocusBadge? FocusBadgeElement(double scale)
    {
        _compactFocus = null;
        if (!Settings.ShowsFocus(EffectiveStyle) || !_app.Focus.Shown) return null;
        double s = CompactMusicLayout.SafeScale(scale);
        double font = CompactMusicLayout.Half(11.5 * s);
        double width = FocusBadge.ReservedWidth(font);
        var badge = new FocusBadge(width, CompactMusicLayout.Whole(16 * s), font, UI.Primary);
        badge.Set(_app.Focus.Format(DateTimeOffset.Now));
        string tip = Loc.T("专注计时", "Focus timer");
        badge.ToolTip = tip;
        AutomationProperties.SetName(badge, tip);
        // The click opens the timer; the row would otherwise open the panel.
        badge.MouseLeftButtonUp += (_, e) => { e.Handled = true; FocusOpen(); };
        _compactFocus = badge;
        return badge;
    }

    // The timer changes: the badge shows the new time, and the row is built again when the countdown appears or goes away.
    internal void FocusRefresh()
    {
        bool shown = Settings.ShowsFocus(EffectiveStyle) && _app.Focus.Shown;
        if (shown != _focusShown)
        {
            _focusShown = shown;
            RenderCompact();
            return;
        }
        if (_compactFocus is not null) _compactFocus.Set(_app.Focus.Format(DateTimeOffset.Now));
    }

    // The window the timer is set in.
    internal void FocusOpen()
    {
        var window = new FocusTimerWindow(_app) { Owner = this };
        window.Show();
    }
}
