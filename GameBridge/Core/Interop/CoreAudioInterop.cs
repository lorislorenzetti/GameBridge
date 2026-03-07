using System.Runtime.InteropServices;

namespace GameAudioMixer.Core.Interop;

#region Enums

public enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2
}

public enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

[Flags]
public enum DEVICE_STATE : uint
{
    ACTIVE = 0x00000001,
    DISABLED = 0x00000002,
    NOTPRESENT = 0x00000004,
    UNPLUGGED = 0x00000008,
    MASK_ALL = 0x0000000F
}

public enum AudioSessionState
{
    AudioSessionStateInactive = 0,
    AudioSessionStateActive = 1,
    AudioSessionStateExpired = 2
}

public enum AudioSessionDisconnectReason
{
    DisconnectReasonDeviceRemoval = 0,
    DisconnectReasonServerShutdown = 1,
    DisconnectReasonFormatChanged = 2,
    DisconnectReasonSessionLogoff = 3,
    DisconnectReasonSessionDisconnected = 4,
    DisconnectReasonExclusiveModeOverride = 5
}

public enum STGM : uint
{
    STGM_READ = 0x00000000
}

#endregion

#region Property Key

[StructLayout(LayoutKind.Sequential)]
public struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

public static class PKEY
{
    public static readonly PROPERTYKEY DeviceDescription = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 2
    };

    public static readonly PROPERTYKEY DeviceFriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14
    };

    public static readonly PROPERTYKEY DeviceInterfaceFriendlyName = new()
    {
        fmtid = new Guid("026e516e-b814-414b-8384-93c4b1cf812d"),
        pid = 2
    };
}

#endregion

#region PROPVARIANT

[StructLayout(LayoutKind.Sequential)]
public struct PROPVARIANT
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr data1;
    public IntPtr data2;

    public string? AsString()
    {
        // VT_LPWSTR = 31
        if (vt == 31 && data1 != IntPtr.Zero)
            return Marshal.PtrToStringUni(data1);
        return null;
    }
}

#endregion

#region COM Class IDs

public static class CLSID
{
    public static readonly Guid MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
}

#endregion

#region IMMDeviceEnumerator

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, DEVICE_STATE dwStateMask, out IMMDeviceCollection ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
}

#endregion

#region IMMDeviceCollection

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint pcDevices);

    [PreserveSig]
    int Item(uint nDevice, out IMMDevice ppDevice);
}

#endregion

#region IMMDevice

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    [PreserveSig]
    int OpenPropertyStore(STGM stgmAccess, out IPropertyStore ppProperties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

    [PreserveSig]
    int GetState(out DEVICE_STATE pdwState);
}

#endregion

#region IPropertyStore

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint cProps);

    [PreserveSig]
    int GetAt(uint iProp, out PROPERTYKEY pkey);

    [PreserveSig]
    int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);

    [PreserveSig]
    int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);

    [PreserveSig]
    int Commit();
}

#endregion

#region IMMNotificationClient

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, DEVICE_STATE newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PROPERTYKEY key);
}

#endregion

#region IAudioSessionManager2

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionManager2
{
    // IAudioSessionManager methods
    [PreserveSig]
    int GetAudioSessionControl(IntPtr AudioSessionGuid, uint StreamFlags, out IAudioSessionControl SessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr AudioSessionGuid, uint StreamFlags, out ISimpleAudioVolume AudioVolume);

    // IAudioSessionManager2 methods
    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);

    [PreserveSig]
    int RegisterSessionNotification(IAudioSessionNotification SessionNotification);

    [PreserveSig]
    int UnregisterSessionNotification(IAudioSessionNotification SessionNotification);

    [PreserveSig]
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, IntPtr duckNotification);

    [PreserveSig]
    int UnregisterDuckNotification(IntPtr duckNotification);
}

#endregion

#region IAudioSessionEnumerator

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int SessionCount);

    [PreserveSig]
    int GetSession(int SessionCount, out IAudioSessionControl Session);
}

#endregion

#region IAudioSessionControl

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionControl
{
    [PreserveSig]
    int GetState(out AudioSessionState pRetVal);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid pRetVal);

    [PreserveSig]
    int SetGroupingParam(ref Guid Override, ref Guid EventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IAudioSessionEvents NewNotifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IAudioSessionEvents NewNotifications);
}

#endregion

#region IAudioSessionControl2

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionControl2
{
    // IAudioSessionControl methods
    [PreserveSig]
    int GetState(out AudioSessionState pRetVal);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid pRetVal);

    [PreserveSig]
    int SetGroupingParam(ref Guid Override, ref Guid EventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IAudioSessionEvents NewNotifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IAudioSessionEvents NewNotifications);

    // IAudioSessionControl2 methods
    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int GetProcessId(out uint pRetVal);

    [PreserveSig]
    int IsSystemSoundsSession();

    [PreserveSig]
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

#endregion

#region IAudioSessionEvents

[ComImport]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionEvents
{
    void OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string NewDisplayName, ref Guid EventContext);
    void OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string NewIconPath, ref Guid EventContext);
    void OnSimpleVolumeChanged(float NewVolume, [MarshalAs(UnmanagedType.Bool)] bool NewMute, ref Guid EventContext);
    void OnChannelVolumeChanged(uint ChannelCount, IntPtr NewChannelVolumeArray, uint ChangedChannel, ref Guid EventContext);
    void OnGroupingParamChanged(ref Guid NewGroupingParam, ref Guid EventContext);
    void OnStateChanged(AudioSessionState NewState);
    void OnSessionDisconnected(AudioSessionDisconnectReason DisconnectReason);
}

#endregion

#region IAudioSessionNotification

[ComImport]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionNotification
{
    void OnSessionCreated(IAudioSessionControl NewSession);
}

#endregion

#region ISimpleAudioVolume

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float fLevel, ref Guid EventContext);

    [PreserveSig]
    int GetMasterVolume(out float pfLevel);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid EventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

#endregion

#region IAudioMeterInformation

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float pfPeak);

    [PreserveSig]
    int GetMeteringChannelCount(out uint pnChannelCount);

    [PreserveSig]
    int GetChannelsPeakValues(uint u32ChannelCount, [Out] float[] afPeakValues);

    [PreserveSig]
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);
}

#endregion

#region IAudioEndpointVolume

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int GetChannelCount(out uint pnChannelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float pfLevelDB);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float pfLevel);

    [PreserveSig]
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);

    [PreserveSig]
    int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);

    [PreserveSig]
    int VolumeStepUp(ref Guid pguidEventContext);

    [PreserveSig]
    int VolumeStepDown(ref Guid pguidEventContext);

    [PreserveSig]
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

#endregion

#region GUIDs for Activate

public static class AudioInterfaceGuids
{
    public static Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    public static Guid IID_ISimpleAudioVolume = new("87CE5498-68D6-44E5-9215-6DA47EF883D8");
}

#endregion
