using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using AxDeck.Core;

namespace AxDeck.Native;

// The machine's playback devices, and the switch to another one. NAudio reads the devices (their names, their states); the
// switch to another one is done through the system's own policy interface, which is what the sound settings use.
internal static class AudioOutputs
{
    // The devices as they stand, and the one in use. An empty list when the audio service cannot be reached.
    public static IReadOnlyList<AudioOutputDevice> List()
    {
        var devices = new List<AudioOutputDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? current = null;
            try { current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)?.ID; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
            // The active ones and those that are plugged out or disabled: the last two are shown, but not ready to take sound.
            var mask = NAudio.CoreAudioApi.DeviceState.Active | NAudio.CoreAudioApi.DeviceState.Unplugged | NAudio.CoreAudioApi.DeviceState.Disabled;
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, mask))
            {
                try
                {
                    bool ready = device.State == NAudio.CoreAudioApi.DeviceState.Active;
                    devices.Add(new AudioOutputDevice(device.ID, device.FriendlyName, device.ID == current, ready));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        return Core.AudioOutputs.Ordered(devices);
    }

    // Puts another device in charge of the sound. True when it took.
    public static bool Use(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            var policy = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidPolicy)!)!;
            try
            {
                // The console and the communications each have their own choice; the panel's switch changes the one the music
                // plays through, and the other two along with it, so the sound does not stay where it was.
                policy.SetDefaultEndpoint(id, ERole.Multimedia);
                policy.SetDefaultEndpoint(id, ERole.Console);
                policy.SetDefaultEndpoint(id, ERole.Communications);
                return true;
            }
            finally { Marshal.ReleaseComObject(policy); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }
    }

    private static readonly Guid ClsidEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid ClsidPolicy = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int bDefault, IntPtr role, out IntPtr format);
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr role);
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int bDefault, out IntPtr defaultPeriod, out IntPtr minimumPeriod);
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref Guid key, out IntPtr value);
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref Guid key, IntPtr value);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
