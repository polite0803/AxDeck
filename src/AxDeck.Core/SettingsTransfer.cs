using System.Text.Json;
using System.Text.Json.Nodes;
using NodeValue = System.Text.Json.Nodes.JsonValue;

namespace AxDeck.Core;

// A settings file to carry to another PC, and the reading of one back. What is carried is what a person chose: colors, pages,
// shortcuts, applications, price lists and the rest. What stays behind is what names something only the machine that exported it
// has — the monitors, the graphics adapter, the wireless devices, a local application's data file — because on another PC those
// names point at nothing and the panel would show a choice it cannot honour. Keys are not here either (they live in secrets.dat),
// so an imported row keeps its key only where this PC already has one for it.
public static class SettingsTransfer
{
    // The envelope's name and version, so a file that is not one of these, or one written by a newer AxDeck, is refused instead
    // of being half read.
    public const string FormatKey = "AxDeckSettings";
    public const int Format = 1;

    // Settings members that name hardware on the PC that wrote the file.
    private static readonly string[] MachineKeys = ["DisplayMonitors", "MonitorGpu", "MonitorGpuName", "DeviceChoices"];

    // The file's text, and the version whose settings it carries. The settings are normalized first, as saving does, so an export
    // never writes a value the panel would not have used.
    public static string Export(DeckSettings settings, string appVersion, DateTimeOffset at)
    {
        var copy = settings.Copy();
        copy.AppVersion = appVersion;
        copy.Normalize();
        var node = JsonSerializer.SerializeToNode(copy)!.AsObject();
        foreach (var key in MachineKeys) node.Remove(key);
        // An application row keeps its identity, its source and its colors, but not where this PC keeps that source's data, and
        // not the mark that says a key was saved: the key itself stays in secrets.dat, so the mark would claim a key on a PC that
        // has none. KeepLocal gives the mark back for a row this PC already has.
        var apps = new JsonArray();
        foreach (var app in copy.Apps)
        {
            if (JsonSerializer.SerializeToNode(app) is not JsonObject row) continue;
            row.Remove("LocalDataPath");
            row.Remove("SecretRevision");
            apps.Add(row);
        }
        node["Apps"] = apps;
        // A file meant to be carried says what it holds; settings.json's snapshots of other versions stay behind.
        node.Remove("Snapshots");
        return new JsonObject
        {
            [FormatKey] = Format,
            ["AppVersion"] = appVersion,
            ["ExportedAt"] = at.ToString("O"),
            ["Settings"] = node
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // Reads a file back, or throws JsonException when it is not a settings file this build can read. The settings are migrated as
    // settings.json is, so a file exported by an older AxDeck reads as its settings would after an upgrade.
    public static (DeckSettings Settings, Version Version) Import(string json, Version running)
    {
        var root = SettingsMigrations.Parse(json);
        if (root[FormatKey] is not NodeValue format || !format.TryGetValue(out int version) || version < 1)
            throw new JsonException("The file is not an AxDeck settings file.");
        if (version > Format) throw new JsonException("The settings file was written by a newer AxDeck.");
        if (root["Settings"] is not JsonObject node) throw new JsonException("The settings file holds no settings.");
        return (SettingsMigrations.Read(node, running, out var saved), saved);
    }

    // Gives an imported set this PC's own choices back: what the export left out, and what only this machine can honour. Without
    // it, importing a file on another PC would quietly move the panel to the primary monitor and forget which devices were picked.
    // A row's local data path comes back too, but only for a row this PC already has under the same instance id.
    public static DeckSettings KeepLocal(DeckSettings imported, DeckSettings current)
    {
        var kept = imported.Copy();
        kept.Display = current.Display;
        kept.DisplayMonitors = [.. current.DisplayMonitors ?? []];
        kept.MonitorGpu = current.MonitorGpu;
        kept.MonitorGpuName = current.MonitorGpuName;
        kept.DeviceChoices = [.. (current.DeviceChoices ?? []).Select(device => device.Copy())];
        // A startup entry is registered with Windows, not with the settings file, and the sensors are installed on this PC.
        kept.LaunchAtStartup = current.LaunchAtStartup;
        kept.MonitorTemperatures = current.MonitorTemperatures;
        // A row's own local data file and key mark come back only from this PC's row with the same instance id and source: another
        // source has another key, and a row this PC does not have has neither.
        foreach (var app in kept.Apps)
        {
            if (current.Apps.FirstOrDefault(own => own.InstanceId == app.InstanceId && own.QuotaSource == app.QuotaSource) is not { } own) continue;
            app.LocalDataPath = own.LocalDataPath;
            app.SecretRevision = own.SecretRevision;
        }
        return kept;
    }
}