namespace AxDeck.Core;

// One of the machine's playback devices, as the panel lists them: the system's own id, the name it shows, whether it is the
// one in use now, and whether it is ready to take sound (plugged in and working).
public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault = false, bool Ready = true)
{
    // The name shown when the system gives none.
    public string Label => string.IsNullOrWhiteSpace(Name) ? Loc.T("未命名的设备", "Unnamed device") : Name;
}

// The playback devices as they are listed: the ones that are ready first, and among them the default one, then the rest by
// name, so the list does not jump about as devices come and go.
public static class AudioOutputs
{
    public static IReadOnlyList<AudioOutputDevice> Ordered(IEnumerable<AudioOutputDevice> devices)
    {
        return devices.OrderBy(d => d.Ready ? 0 : 1)
            .ThenBy(d => d.IsDefault ? 0 : 1)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
