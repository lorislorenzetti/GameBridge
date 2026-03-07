using System.Diagnostics;
using System.Runtime.InteropServices;
using GameAudioMixer.Core.Interop;

namespace GameAudioMixer.Core;

/// <summary>
/// High-level facade over DeviceManager and SessionManager.
/// Provides simplified access to common audio operations.
/// </summary>
public sealed class AudioManager : IDisposable
{
    private readonly DeviceManager _deviceManager;
    private readonly SessionManager _sessionManager;
    private bool _disposed;

    public DeviceManager Devices => _deviceManager;
    public SessionManager Sessions => _sessionManager;

    public AudioManager(DeviceManager deviceManager, SessionManager sessionManager)
    {
        _deviceManager = deviceManager;
        _sessionManager = sessionManager;
    }

    public void SetProcessVolume(string processName, float volume)
    {
        var sessions = _sessionManager.FindSessionsByProcessName(processName);
        foreach (var session in sessions)
            session.Volume = volume;
    }

    public void AdjustProcessVolume(string processName, float delta)
    {
        var sessions = _sessionManager.FindSessionsByProcessName(processName);
        foreach (var session in sessions)
            session.Volume = Math.Clamp(session.Volume + delta, 0f, 1f);
    }

    public void SetProcessMute(string processName, bool mute)
    {
        var sessions = _sessionManager.FindSessionsByProcessName(processName);
        foreach (var session in sessions)
            session.IsMuted = mute;
    }

    public void ToggleProcessMute(string processName)
    {
        var sessions = _sessionManager.FindSessionsByProcessName(processName);
        foreach (var session in sessions)
            session.IsMuted = !session.IsMuted;
    }

    /// <summary>
    /// Toggles the default capture device mute state.
    /// Returns the new mute state, or null if the device is unavailable.
    /// </summary>
    public bool? ToggleMicrophoneMute()
    {
        var device = _deviceManager.GetDefaultCaptureDevice();
        if (device == null) return null;

        try
        {
            var iid = AudioInterfaceGuids.IID_IAudioEndpointVolume;
            int hr = device.Activate(ref iid, 0x17, IntPtr.Zero, out var obj);
            if (hr != 0) return null;

            var endpointVolume = (IAudioEndpointVolume)obj;
            endpointVolume.GetMute(out bool currentMute);
            var guid = Guid.Empty;
            endpointVolume.SetMute(!currentMute, ref guid);
            Marshal.ReleaseComObject(endpointVolume);
            return !currentMute;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AudioManager: ToggleMicrophoneMute failed: {ex.Message}");
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    public bool? GetMicrophoneMuteState()
    {
        var device = _deviceManager.GetDefaultCaptureDevice();
        if (device == null) return null;

        try
        {
            var iid = AudioInterfaceGuids.IID_IAudioEndpointVolume;
            int hr = device.Activate(ref iid, 0x17, IntPtr.Zero, out var obj);
            if (hr != 0) return null;

            var endpointVolume = (IAudioEndpointVolume)obj;
            endpointVolume.GetMute(out bool muted);
            Marshal.ReleaseComObject(endpointVolume);
            return muted;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    public float GetSystemMasterVolume()
    {
        var device = _deviceManager.GetDefaultOutputDevice();
        if (device == null) return 1f;
        try
        {
            var iid = AudioInterfaceGuids.IID_IAudioEndpointVolume;
            if (_deviceManager.GetDefaultOutputDevice() == null) return 1f;
            int hr = device.Activate(ref iid, 0x17, IntPtr.Zero, out var obj);
            if (hr != 0) return 1f;
            var ep = (IAudioEndpointVolume)obj;
            ep.GetMasterVolumeLevelScalar(out float level);
            Marshal.ReleaseComObject(ep);
            return level;
        }
        catch { return 1f; }
        finally { Marshal.ReleaseComObject(device); }
    }

    public void SetSystemMasterVolume(float level)
    {
        var device = _deviceManager.GetDefaultOutputDevice();
        if (device == null) return;
        try
        {
            var iid = AudioInterfaceGuids.IID_IAudioEndpointVolume;
            int hr = device.Activate(ref iid, 0x17, IntPtr.Zero, out var obj);
            if (hr != 0) return;
            var ep = (IAudioEndpointVolume)obj;
            var guid = Guid.Empty;
            ep.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref guid);
            Marshal.ReleaseComObject(ep);
        }
        catch { }
        finally { Marshal.ReleaseComObject(device); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionManager.Dispose();
        _deviceManager.Dispose();
    }
}
