using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace MidiMixer;

record OutputDevice(string Id, string Name)
{
    public override string ToString() => Name;
}
static class Headphones
{
    public static OutputDevice[] List()
    {
        using var e = new MMDeviceEnumerator();
        var result = new List<OutputDevice>();
        foreach (var device in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device) result.Add(new(device.ID, device.FriendlyName));
        return result.ToArray();
    }
    public static void SetDefault(string id)
    {
        using var e = new MMDeviceEnumerator();
        using var device = e.GetDevice(id);
        if (device.State != DeviceState.Active) throw new IOException("Der Kopfhörer ist nicht verbunden.");
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"))!)!;
        try
        {
            var policy = (IPolicyConfig)instance;
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, 0));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, 1));
        }
        finally { Marshal.ReleaseComObject(instance); }
    }
    // IPolicyConfig is an undocumented Windows interface; the UI offers Windows settings as fallback.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        void GetPropertyValue(); void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, int role);
        void SetEndpointVisibility();
    }
}
