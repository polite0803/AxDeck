using AxDeck.Core;

// The low-battery notices' picking: a device crossing into the getting-low or low level notifies, once per crossing, and
// only once it has been seen before; charging counts as normal, and devices that are gone are tracked from their return.
static class DeviceNoticesTests
{
    public static void Run(Action<string, bool> check)
    {
        Crossings(check);
        Charging(check);
        Parts(check);
        Gone(check);
    }

    private static DeviceBattery Battery(int? percent, bool charging = false)
        => new("k", "鼠标", DeviceKind.Mouse, DeviceSource.Bluetooth, percent) { Charging = charging };

    private static void Crossings(Action<string, bool> check)
    {
        var levels = new Dictionary<string, int>();
        // The first reading only starts tracking, whatever the charge.
        check("DeviceNotices: the first reading tracks without notifying", DeviceNotices.Pick([Battery(80)], levels, 25, 15, seeded: false).Count == 0 && levels["k"] == 0);
        // A rise into the warning and then the critical level notifies, each once; staying and recovering stay quiet.
        var first = DeviceNotices.Pick([Battery(24)], levels, 25, 15, seeded: true);
        var deeper = DeviceNotices.Pick([Battery(20)], levels, 25, 15, seeded: true);
        var critical = DeviceNotices.Pick([Battery(15)], levels, 25, 15, seeded: true);
        var recover = DeviceNotices.Pick([Battery(50)], levels, 25, 15, seeded: true);
        check("DeviceNotices: crossing into the warning level notifies", first.Count == 1 && first[0].Level == MemoryLevel.Elevated && first[0].Percent == 24);
        check("DeviceNotices: deeper into the same level notifies nothing", deeper.Count == 0);
        check("DeviceNotices: crossing into the critical level notifies", critical.Count == 1 && critical[0].Level == MemoryLevel.Critical);
        check("DeviceNotices: a fall back to normal notifies nothing", recover.Count == 0 && levels["k"] == 0);
        // A device seen for the first time never notifies, seeded or not: it has crossed nothing.
        var newcomer = new Dictionary<string, int> { ["other"] = 0 };
        check("DeviceNotices: a device seen for the first time does not notify", DeviceNotices.Pick([Battery(10)], newcomer, 25, 15, seeded: true).Count == 0);
    }

    private static void Charging(Action<string, bool> check)
    {
        var levels = new Dictionary<string, int>();
        DeviceNotices.Pick([Battery(50)], levels, 25, 15, seeded: true);
        // Charging keeps the level normal even at 5 %: plugging in silences a device until it is unplugged.
        check("DeviceNotices: charging counts as normal", DeviceNotices.Pick([Battery(5, charging: true)], levels, 25, 15, seeded: true).Count == 0 && levels["k"] == 0);
        // Unplugged while still low: the level rises from normal, so it notifies once.
        check("DeviceNotices: unplugged and still low, it notifies once", DeviceNotices.Pick([Battery(10)], levels, 25, 15, seeded: true).Count == 1);
    }

    private static void Parts(Action<string, bool> check)
    {
        // The case is not counted: the buds' lowest reading decides, as the readings shown do.
        var buds = new DeviceBattery("k", "耳机", DeviceKind.Earbuds, DeviceSource.Gatt, 90)
        {
            Parts = [new PartBattery(BatteryPart.Left, 20, false), new PartBattery(BatteryPart.Right, 24, false), new PartBattery(BatteryPart.Case, 100, false)]
        };
        var levels = new Dictionary<string, int>();
        DeviceNotices.Pick([buds], levels, 25, 15, seeded: true);
        var low = DeviceNotices.Pick([buds with { Parts = [new PartBattery(BatteryPart.Left, 20, false), new PartBattery(BatteryPart.Right, 14, false), new PartBattery(BatteryPart.Case, 100, false)] }], levels, 25, 15, seeded: true);
        check("DeviceNotices: the case is left out, the buds decide", low.Count == 1 && low[0].Percent == 14 && low[0].Level == MemoryLevel.Critical);
    }

    private static void Gone(Action<string, bool> check)
    {
        var levels = new Dictionary<string, int>();
        DeviceNotices.Pick([Battery(80)], levels, 25, 15, seeded: true);
        check("DeviceNotices: crossing into the warning level notifies", DeviceNotices.Pick([Battery(20)], levels, 25, 15, seeded: true).Count == 1);
        // Gone: the stale level leaves, so a device that returns is tracked from its return.
        check("DeviceNotices: a device that is gone leaves the levels", DeviceNotices.Pick([], levels, 25, 15, seeded: true).Count == 0 && levels.Count == 0);
        check("DeviceNotices: returning counts as seen for the first time", DeviceNotices.Pick([Battery(20)], levels, 25, 15, seeded: true).Count == 0 && DeviceNotices.Pick([Battery(10)], levels, 25, 15, seeded: true).Count == 1);
    }
}
