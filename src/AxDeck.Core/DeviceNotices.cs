namespace AxDeck.Core;

// Picks the devices whose charge crossed into the getting-low or low level since the last reading, and so deserve a
// Windows notification. Pure logic, so the tests can cover it; the caller reads the devices and shows the notices.
public static class DeviceNotices
{
    // A device to notify about: the key identifies it in the settings, the name and percent go into the text.
    public sealed record Notice(string Key, string Name, int Percent, MemoryLevel Level);

    // readings are what is connected now; levels is the caller's dictionary, updated in place with every device's
    // level this call. The first call (seeded false) only starts tracking: a device that was already low when the
    // reading began has not crossed anything, so it stays quiet, and so does one seen for the first time. Devices no
    // longer connected leave the dictionary, so one that returns is tracked from its return rather than from long
    // ago. Charging counts as normal (Devices.Level), so a charge level regained after unplugging notifies once more.
    public static IReadOnlyList<Notice> Pick(IReadOnlyList<DeviceBattery> readings, Dictionary<string, int> levels, int warning, int low, bool seeded)
    {
        List<Notice> notices = [];
        foreach (var battery in readings)
        {
            var level = (int)Devices.Level(battery.Lowest, battery.Charging, warning, low);
            bool tracked = levels.TryGetValue(battery.Key, out var was);
            levels[battery.Key] = level;
            if (seeded && tracked && level > was && level > 0)
                notices.Add(new Notice(battery.Key, battery.Name, Devices.Clamp(battery.Lowest ?? 0), (MemoryLevel)level));
        }
        // Devices that are gone leave no stale level behind: seen again, they are treated as first seen.
        var gone = new HashSet<string>(levels.Keys);
        foreach (var battery in readings) gone.Remove(battery.Key);
        foreach (var key in gone) levels.Remove(key);
        return notices;
    }
}
