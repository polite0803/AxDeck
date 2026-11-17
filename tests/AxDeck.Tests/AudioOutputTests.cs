using AxDeck.Core;

// The output devices as they are listed: the ones that can take sound first, the device in use next, and the rest by name.
static class AudioOutputTests
{
    public static void Run(Action<string, bool> check)
    {
        var devices = new[]
        {
            new AudioOutputDevice("a", "Zulu"),
            new AudioOutputDevice("b", "Alpha", IsDefault: true),
            new AudioOutputDevice("c", "Mike", Ready: false),
            new AudioOutputDevice("d", "Bravo"),
        };
        var ordered = AxDeck.Core.AudioOutputs.Ordered(devices);
        check("AudioOutput: the device in use comes first among the ready ones", ordered[0].Id == "b");
        check("AudioOutput: the ready devices come before the ones that are not", ordered[3].Id == "c");
        check("AudioOutput: the ready devices are in name order", ordered[1].Name == "Bravo" && ordered[2].Name == "Zulu");
        check("AudioOutput: a device without a name still has one to show",
            new AudioOutputDevice("x", "").Label.Length > 0);
    }
}
