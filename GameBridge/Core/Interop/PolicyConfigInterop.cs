using System.Runtime.InteropServices;

namespace GameAudioMixer.Core.Interop;

/// <summary>
/// Undocumented COM interface used to change the default audio endpoint.
/// Stable across Windows 10 and 11. Falls back gracefully if unavailable.
/// </summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        IntPtr ppFormat);

    [PreserveSig]
    int GetDeviceFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        [MarshalAs(UnmanagedType.Bool)] bool bDefault,
        IntPtr ppFormat);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);

    [PreserveSig]
    int SetDeviceFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        IntPtr pEndpointFormat,
        IntPtr mixFormat);

    [PreserveSig]
    int GetProcessingPeriod(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        [MarshalAs(UnmanagedType.Bool)] bool bDefault,
        IntPtr pmftDefaultPeriod,
        IntPtr pmftMinimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        IntPtr pmftPeriod);

    [PreserveSig]
    int GetShareMode(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        IntPtr pMode);

    [PreserveSig]
    int SetShareMode(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        IntPtr mode);

    [PreserveSig]
    int GetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        [MarshalAs(UnmanagedType.Bool)] bool bFxStore,
        IntPtr key,
        IntPtr pv);

    [PreserveSig]
    int SetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        [MarshalAs(UnmanagedType.Bool)] bool bFxStore,
        IntPtr key,
        IntPtr pv);

    [PreserveSig]
    int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId,
        ERole eRole);

    [PreserveSig]
    int SetEndpointVisibility(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
        [MarshalAs(UnmanagedType.Bool)] bool bVisible);
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
public class CPolicyConfigClient { }

public static class PolicyConfigHelper
{
    public static IPolicyConfig CreatePolicyConfig()
    {
        return (IPolicyConfig)new CPolicyConfigClient();
    }
}
