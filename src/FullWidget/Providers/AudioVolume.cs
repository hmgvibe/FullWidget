using System.Runtime.InteropServices;
using FullWidget.Bridge;
using FullWidget.Core;
using FullWidget.Windows;

namespace FullWidget.Providers;

/// The speakers' volume and mute (Windows Core Audio), shared by every widget that asks.
/// Windows tells us about changes (volume keys, the taskbar slider, switching to headphones),
/// so subscribed widgets update at once without polling.
sealed class AudioVolume : IDisposable
{
    readonly App app;
    readonly HashSet<WidgetWindow> subs = new();
    IMMDeviceEnumerator? enumerator;
    IAudioEndpointVolume? endpoint;
    string device = "";
    VolumeCallback? volumeCallback;
    DeviceCallback? deviceCallback;
    static Guid context = Guid.NewGuid(); // marks changes we make, so they aren't echoed back twice

    public AudioVolume(App app) => this.app = app;

    /// { volume: 0–100, muted, device } or { available: false } without an output device.
    public object State()
    {
        if (!Ensure()) return new { available = false, volume = 0, muted = false, device = "" };
        endpoint!.GetMasterVolumeLevelScalar(out float level);
        endpoint.GetMute(out bool muted);
        return new { available = true, volume = (int)Math.Round(level * 100), muted, device };
    }

    public object Set(double? volume, bool? muted)
    {
        if (!Ensure()) throw new BridgeException("no audio output device");
        if (volume is double v) endpoint!.SetMasterVolumeLevelScalar((float)Math.Clamp(v / 100, 0, 1), ref context);
        if (muted is bool m) endpoint!.SetMute(m, ref context);
        var s = State();
        Push(s);
        return s;
    }

    public object Subscribe(WidgetWindow w)
    {
        subs.Add(w);
        return State();
    }

    public void Unsubscribe(WidgetWindow w) => subs.Remove(w);

    void Push(object state)
    {
        foreach (var w in subs.ToList()) w.Send("audio", state);
    }

    // Windows calls these on its own threads.
    void Changed() => app.Post(() => Push(State()));
    void DeviceChanged() => app.Post(() => { Release(); Push(State()); });

    bool Ensure()
    {
        if (endpoint != null) return true;
        try
        {
            enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (deviceCallback == null)
            {
                deviceCallback = new DeviceCallback(DeviceChanged);
                enumerator.RegisterEndpointNotificationCallback(deviceCallback);
            }
            if (enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var dev) != 0 || dev == null) return false;
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj));
            endpoint = (IAudioEndpointVolume)obj;
            device = FriendlyName(dev);
            volumeCallback = new VolumeCallback(Changed);
            endpoint.RegisterControlChangeNotify(volumeCallback);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn($"Audio: {ex.Message}");
            Release();
            return false;
        }
    }

    static string FriendlyName(IMMDevice dev)
    {
        try
        {
            if (dev.OpenPropertyStore(0, out var store) != 0) return "";
            var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 }; // PKEY_Device_FriendlyName
            if (store.GetValue(ref key, out var pv) != 0) return "";
            try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) ?? "" : ""; } // VT_LPWSTR
            finally { PropVariantClear(ref pv); }
        }
        catch (COMException) { return ""; }
    }

    void Release()
    {
        if (endpoint != null && volumeCallback != null)
        {
            try { endpoint.UnregisterControlChangeNotify(volumeCallback); } catch (COMException) { }
        }
        endpoint = null;
        volumeCallback = null;
        device = "";
    }

    public void Dispose()
    {
        Release();
        if (enumerator != null && deviceCallback != null)
        {
            try { enumerator.UnregisterEndpointNotificationCallback(deviceCallback); } catch (COMException) { }
        }
        enumerator = null;
    }

    // ── Core Audio, declared by hand (only the parts used) ──

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Sequential)] struct PropVariant { public ushort vt, r1, r2, r3; public IntPtr p, p2; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }

    [ComVisible(true)]
    sealed class VolumeCallback(Action changed) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data) { changed(); return 0; }
    }

    [ComVisible(true)]
    sealed class DeviceCallback(Action changed) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string id, int state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            if (flow == 0 && role == 1) changed(); // the default speakers changed
            return 0;
        }
        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
    }
}
