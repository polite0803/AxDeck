using AxDeck.Core;

// A row's own warning and critical levels: unchosen means the panel's global ones, a chosen pair is kept the right way up,
// and the level a share sits at follows the row rather than the global constants.
static class AppLevelTests
{
    public static void Run(Action<string, bool> check)
    {
        Defaults(check);
        Chosen(check);
        KeepingOrder(check);
        Normalize(check);
    }

    private static void Defaults(Action<string, bool> check)
    {
        var row = new AppEntry();
        check("AppLevel: an unchanged row uses the global levels",
            row.WarningAt == AppPresets.WarningPercent && row.CriticalAt == AppPresets.CriticalPercent);
        check("AppLevel: a share below the warning level is level 0", row.At(69.9) == 0);
        check("AppLevel: a share at the warning level is level 1", row.At(70) == 1);
        check("AppLevel: a share at the critical level is level 2", row.At(90) == 2);
    }

    private static void Chosen(Action<string, bool> check)
    {
        var row = new AppEntry { WarningLevel = 40, CriticalLevel = 60 };
        check("AppLevel: a chosen pair is read back", row.WarningAt == 40 && row.CriticalAt == 60);
        check("AppLevel: a share below a chosen warning level is level 0", row.At(39.9) == 0);
        check("AppLevel: a share at a chosen warning level is level 1", row.At(40) == 1);
        check("AppLevel: a share at a chosen critical level is level 2", row.At(60) == 2);
        // A share past the global critical level still reads by the row's own levels, not the global ones.
        check("AppLevel: the global levels do not apply to a row that chose its own", row.At(85) == 2);
        // Only one of the two chosen leaves the other global.
        var half = new AppEntry { CriticalLevel = 50 };
        check("AppLevel: an unchosen warning level stays global", half.WarningAt == AppPresets.WarningPercent && half.CriticalAt == 50);
    }

    private static void KeepingOrder(Action<string, bool> check)
    {
        // A warning level at or above the critical one is pulled below it, so a share is never both at once.
        var crossed = new AppEntry { WarningLevel = 95, CriticalLevel = 50 };
        crossed.Normalize();
        check("AppLevel: a warning level above the critical one is pulled below it", crossed.WarningLevel == 49 && crossed.CriticalLevel == 50);
        var equal = new AppEntry { WarningLevel = 50, CriticalLevel = 50 };
        equal.Normalize();
        check("AppLevel: equal levels are pulled apart", equal.WarningAt < equal.CriticalAt && equal.CriticalLevel == 50);
        // The warning level alone at the default critical level still stays below it.
        var aboveDefault = new AppEntry { WarningLevel = 95 };
        aboveDefault.Normalize();
        check("AppLevel: a warning level above the default critical level is pulled below it", aboveDefault.WarningAt == 89 && aboveDefault.CriticalAt == 90);
    }

    private static void Normalize(Action<string, bool> check)
    {
        // A level outside 1..100 is not a level: the row goes back to the global one instead of being clamped into a false one.
        var wrong = new AppEntry { WarningLevel = 0.5, CriticalLevel = 150 };
        wrong.Normalize();
        check("AppLevel: a level outside 1 to 100 goes back to the global one", wrong.WarningLevel == 0 && wrong.CriticalLevel == 0);
        var fractional = new AppEntry { WarningLevel = 62.34 };
        fractional.Normalize();
        check("AppLevel: a level is kept to one decimal", fractional.WarningLevel == 62.3);
        // A copy carries the levels with it, so an edit that is cancelled leaves the saved row alone.
        var source = new AppEntry { WarningLevel = 40, CriticalLevel = 60 };
        check("AppLevel: a copy carries the levels", source.Copy() is { } copy && copy.WarningLevel == 40 && copy.CriticalLevel == 60);
    }
}