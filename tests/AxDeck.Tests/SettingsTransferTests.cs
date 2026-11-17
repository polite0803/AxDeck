using System.Text.Json;
using System.Text.Json.Nodes;
using AxDeck.Core;
using NodeValue = System.Text.Json.Nodes.JsonValue;

// The settings file carried to another PC: what it holds, what it leaves behind, and what an import gives this PC back.
static class SettingsTransferTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Version Running = new(1, 4, 0);

    public static void Run(Action<string, bool> check)
    {
        Export(check);
        Import(check);
        Local(check);
    }

    private static DeckSettings Sample()
    {
        var settings = new DeckSettings
        {
            Width = 780, Height = 420, Theme = SettingsTheme.Dark, Language = Loc.Chinese,
            Display = DisplayPlacement.Chosen, LaunchAtStartup = true, MonitorTemperatures = true,
            MonitorGpu = @"PCI\VEN_10DE", MonitorGpuName = "GeForce RTX 4070"
        };
        settings.DisplayMonitors = [new ChosenDisplay(@"\\.\DISPLAY2", "DELL U2720Q")];
        settings.DeviceChoices = [new DeviceChoice { Key = "hid-a", Name = "Mouse", Label = "鼠标" }];
        var app = settings.Apps.First(entry => entry.QuotaSource == ProviderId.Claude);
        app.ThemeColor = "#D97757"; app.LocalDataPath = @"C:\Users\someone\appdata"; app.SecretRevision = Guid.NewGuid();
        settings.Apps.First(entry => entry.QuotaSource == ProviderId.Codex).Enabled = true;
        settings.Apps.First(entry => entry.QuotaSource == ProviderId.Cursor).Enabled = true;
        return settings;
    }

    private static JsonObject File(DeckSettings settings) => SettingsMigrations.Parse(SettingsTransfer.Export(settings, "1.4.0", At));

    private static void Export(Action<string, bool> check)
    {
        var file = File(Sample());
        check("SettingsTransfer: the file says what it is and when it was written",
            (int?)file[SettingsTransfer.FormatKey] == SettingsTransfer.Format && (string?)file["AppVersion"] == "1.4.0"
            && (string?)file["ExportedAt"] == At.ToString("O") && file["Settings"] is JsonObject);
        var settings = (JsonObject)file["Settings"]!;
        // The choices that name this PC's hardware stay behind: on another PC they point at nothing.
        check("SettingsTransfer: hardware choices are not exported",
            !settings.ContainsKey("DisplayMonitors") && !settings.ContainsKey("MonitorGpu")
            && !settings.ContainsKey("MonitorGpuName") && !settings.ContainsKey("DeviceChoices"));
        // A panel's settings, by contrast, are exactly what is being carried.
        check("SettingsTransfer: the panel's own settings are exported",
            (double?)settings["Width"] == 780 && (int?)settings["Theme"] == (int)SettingsTheme.Dark
            && (string?)settings["Language"] == Loc.Chinese);
        var apps = (JsonArray)settings["Apps"]!;
        var claude = apps.OfType<JsonObject>().First(row => (string?)row["QuotaSource"] == ProviderCatalog.Key(ProviderId.Claude));
        check("SettingsTransfer: an application row keeps its colors but not this PC's paths or key mark",
            (string?)claude["ThemeColor"] == "#D97757" && !claude.ContainsKey("LocalDataPath") && !claude.ContainsKey("SecretRevision"));
        check("SettingsTransfer: every application row is exported once", apps.Count == Sample().Apps.Count);
        // settings.json's snapshots of other versions belong to the machine that wrote it, not to the file being carried.
        check("SettingsTransfer: snapshots of other versions stay behind", !settings.ContainsKey("Snapshots"));
        // A file leaves with the version that wrote it, not with whatever AppVersion the settings happened to hold.
        check("SettingsTransfer: the file names the exporting version", (string?)settings["AppVersion"] == "1.4.0");
        // What the export writes is what the panel would have used: a value it would never honour is normalized away.
        var odd = Sample(); odd.ClockCustomColor = "12ab3c"; odd.Width = 10;
        var normalized = (JsonObject)File(odd)["Settings"]!;
        check("SettingsTransfer: the exported settings are normalized first",
            (string?)normalized["ClockCustomColor"] == "#12AB3C" && (double?)normalized["Width"] != 10);
    }

    private static void Import(Action<string, bool> check)
    {
        var text = SettingsTransfer.Export(Sample(), "1.4.0", At);
        var (read, version) = SettingsTransfer.Import(text, Running);
        check("SettingsTransfer: a file reads back as the settings it carried",
            read.Width == 780 && read.Theme == SettingsTheme.Dark && read.Language == Loc.Chinese && version == new Version(1, 4, 0));
        // The settings are read the way settings.json is, so a file from an older AxDeck arrives already upgraded.
        const string older = """{"AxDeckSettings":1,"AppVersion":"0.0.0","ExportedAt":"2025-01-01T00:00:00.0000000+00:00","Settings":{"CompactSummary":false,"Width":900}}""";
        var (upgraded, upgradedFrom) = SettingsTransfer.Import(older, Running);
        check("SettingsTransfer: an older file is migrated on the way in", upgradedFrom == new Version(0, 0, 0)
            && upgraded.Width == 900 && !upgraded.NotchSummary && !upgraded.CapsuleSummary);
        // A file from a version that introduced a format change runs the steps newer than it.
        const string fromOneDotTwo = """{"AxDeckSettings":1,"AppVersion":"1.2.0","ExportedAt":"2025-01-01T00:00:00.0000000+00:00","Settings":{"AppVersion":"1.2.0","NotchMusic":false}}""";
        var (migrated, _) = SettingsTransfer.Import(fromOneDotTwo, Running);
        check("SettingsTransfer: a file from a version with a format change is upgraded", !migrated.NotchEqualizer);
        // A file this build does not know is refused rather than half read.
        check("SettingsTransfer: a file written by a newer AxDeck is refused",
            Throws<JsonException>(() => SettingsTransfer.Import("""{"AxDeckSettings":2,"Settings":{}}""", Running)));
        check("SettingsTransfer: another program's JSON is refused",
            Throws<JsonException>(() => SettingsTransfer.Import("""{"sessions":[]}""", Running)));
        check("SettingsTransfer: a file without settings is refused",
            Throws<JsonException>(() => SettingsTransfer.Import("""{"AxDeckSettings":1}""", Running)));
        check("SettingsTransfer: text that is not JSON is refused",
            Throws<JsonException>(() => SettingsTransfer.Import("settings", Running)));
    }

    private static void Local(Action<string, bool> check)
    {
        var current = Sample();
        // What the importer brings from the other PC, untouched by this one's hardware.
        var imported = new DeckSettings { Width = 640, Theme = SettingsTheme.Light, MonitorTemperatures = false, LaunchAtStartup = false };
        imported.Display = DisplayPlacement.Primary;
        imported.DisplayMonitors = [new ChosenDisplay(@"\\.\DISPLAY1", "Other panel")];
        imported.MonitorGpu = @"PCI\VEN_1002"; imported.MonitorGpuName = "Radeon";
        imported.DeviceChoices = [new DeviceChoice { Key = "hid-b", Name = "Keyboard" }];
        var claude = imported.Apps.First(entry => entry.QuotaSource == ProviderId.Claude);
        claude.InstanceId = current.Apps.First(entry => entry.QuotaSource == ProviderId.Claude).InstanceId;
        claude.LocalDataPath = ""; claude.SecretRevision = Guid.Empty;
        var other = imported.Apps.First(entry => entry.QuotaSource == ProviderId.Codex);
        other.InstanceId = Guid.NewGuid(); other.LocalDataPath = "";

        var kept = SettingsTransfer.KeepLocal(imported, current);
        // What the file chose is kept; only what names this PC is given back.
        check("SettingsTransfer: an import takes the file's own choices", kept.Width == 640 && kept.Theme == SettingsTheme.Light);
        check("SettingsTransfer: the display choices stay this PC's",
            kept.Display == DisplayPlacement.Chosen && kept.DisplayMonitors.Count == 1 && kept.DisplayMonitors[0].Name == "DELL U2720Q"
            && kept.MonitorGpu == @"PCI\VEN_10DE" && kept.MonitorGpuName == "GeForce RTX 4070");
        check("SettingsTransfer: the wireless devices stay this PC's", kept.DeviceChoices.Count == 1 && kept.DeviceChoices[0].Key == "hid-a");
        check("SettingsTransfer: the startup entry and sensors stay this PC's", kept.LaunchAtStartup && kept.MonitorTemperatures);
        // A row this PC already has takes back its own data file and key mark; a row it does not have keeps neither.
        var keptClaude = kept.Apps.First(entry => entry.QuotaSource == ProviderId.Claude);
        var keptCodex = kept.Apps.First(entry => entry.QuotaSource == ProviderId.Codex);
        check("SettingsTransfer: a known row takes back its own data file and key mark",
            keptClaude.LocalDataPath == @"C:\Users\someone\appdata" && keptClaude.SecretRevision != Guid.Empty);
        check("SettingsTransfer: a row this PC does not have keeps no local path", keptCodex.LocalDataPath.Length == 0 && keptCodex.SecretRevision == Guid.Empty);
        // The copy keeps the current settings as they were.
        check("SettingsTransfer: giving back local choices does not edit either side",
            current.Width == 780 && current.DisplayMonitors[0].Name == "DELL U2720Q" && imported.Width == 640 && imported.DisplayMonitors[0].Name == "Other panel");
        // A row whose instance id matches but whose source changed is another row, and takes nothing.
        var moved = new DeckSettings();
        var clash = moved.Apps.First(entry => entry.QuotaSource == ProviderId.Codex);
        clash.InstanceId = keptClaude.InstanceId; clash.QuotaSource = ProviderId.Cursor; clash.SecretRevision = Guid.Empty;
        var swapped = SettingsTransfer.KeepLocal(moved, current);
        check("SettingsTransfer: a row with another source takes no key mark",
            swapped.Apps.First(entry => entry.InstanceId == keptClaude.InstanceId).SecretRevision == Guid.Empty);
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
        catch { return false; }
    }
}