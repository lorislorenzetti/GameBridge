using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GameAudioMixer.Core.Interop;

namespace GameAudioMixer.Core;

public sealed class AudioSessionInfo
{
    public required uint ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string DisplayName { get; init; }
    public required IAudioSessionControl2 SessionControl { get; init; }
    public required ISimpleAudioVolume VolumeControl { get; init; }
    public IAudioMeterInformation? MeterInfo { get; init; }

    // Extra sub-sessions grouped under this one (same process name, e.g. multiple Discord sessions)
    private List<ISimpleAudioVolume>? _extraVolume;
    private List<IAudioMeterInformation>? _extraMeters;

    public void AddGroupedSession(ISimpleAudioVolume vol, IAudioMeterInformation? meter)
    {
        (_extraVolume ??= []).Add(vol);
        if (meter != null) (_extraMeters ??= []).Add(meter);
    }

    public float Volume
    {
        get
        {
            try { VolumeControl.GetMasterVolume(out float vol); return vol; }
            catch { return 0f; }
        }
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            try { var g = Guid.Empty; VolumeControl.SetMasterVolume(clamped, ref g); } catch { }
            if (_extraVolume == null) return;
            foreach (var v in _extraVolume)
                try { var g = Guid.Empty; v.SetMasterVolume(clamped, ref g); } catch { }
        }
    }

    public bool IsMuted
    {
        get
        {
            try { VolumeControl.GetMute(out bool mute); return mute; }
            catch { return false; }
        }
        set
        {
            try { var g = Guid.Empty; VolumeControl.SetMute(value, ref g); } catch { }
            if (_extraVolume == null) return;
            foreach (var v in _extraVolume)
                try { var g = Guid.Empty; v.SetMute(value, ref g); } catch { }
        }
    }

    public float PeakValue
    {
        get
        {
            float peak = 0f;
            try { MeterInfo?.GetPeakValue(out peak); } catch { }
            if (_extraMeters == null) return peak;
            foreach (var m in _extraMeters)
            {
                try { m.GetPeakValue(out float p); if (p > peak) peak = p; } catch { }
            }
            return peak;
        }
    }

}

public sealed class SessionManager : IAudioSessionNotification, IDisposable
{
    private readonly DeviceManager _deviceManager;
    private IAudioSessionManager2? _sessionManager;
    private readonly object _lock = new();
    private bool _disposed;
    private readonly List<SessionEventListener> _eventListeners = [];
    private Timer? _refreshTimer;
    private int _lastSessionCount;


    public ObservableCollection<AudioSessionInfo> Sessions { get; } = [];
    public event Action? SessionsChanged;

    public SessionManager(DeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
        _deviceManager.DefaultDeviceChanged += OnDefaultDeviceChanged;
        InitializeSessionManager();

        // Periodic refresh every 3 seconds to catch new sessions (e.g. Discord joining a call)
        _refreshTimer = new Timer(_ => PeriodicRefresh(), null, 3000, 3000);
    }

    private void PeriodicRefresh()
    {
        if (_disposed) return;
        try
        {
            int countBefore;
            lock (_lock) { countBefore = Sessions.Count; }

            RefreshSessions();

            int countAfter;
            lock (_lock) { countAfter = Sessions.Count; }

            // Only fire event if count changed (avoid unnecessary UI updates)
            if (countBefore != countAfter)
                SessionsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SessionManager: PeriodicRefresh error: {ex.Message}");
        }
    }

    private void InitializeSessionManager()
    {
        lock (_lock)
        {
            CleanupCurrentManager();

            var device = _deviceManager.GetDefaultOutputDevice();
            if (device == null) return;

            var iid = AudioInterfaceGuids.IID_IAudioSessionManager2;
            int hr = device.Activate(ref iid, 0x17, IntPtr.Zero, out var obj);
            Marshal.ReleaseComObject(device);

            if (hr != 0 || obj == null) return;

            _sessionManager = (IAudioSessionManager2)obj;

            try
            {
                _sessionManager.RegisterSessionNotification(this);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SessionManager: Failed to register notification: {ex.Message}");
            }

            RefreshSessions();
        }
    }

    public void RefreshSessions()
    {
        lock (_lock)
        {
            foreach (var listener in _eventListeners)
                listener.Detach();
            _eventListeners.Clear();
            Sessions.Clear();

            if (_sessionManager == null) return;

            int hr = _sessionManager.GetSessionEnumerator(out var enumerator);
            if (hr != 0) return;

            enumerator.GetCount(out int count);

            var grouped = new Dictionary<string, AudioSessionInfo>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                try
                {
                    enumerator.GetSession(i, out var sessionControl);
                    var session2 = (IAudioSessionControl2)sessionControl;

                    session2.GetProcessId(out uint pid);
                    if (pid == 0) continue;

                    session2.GetState(out var state);
                    if (state == AudioSessionState.AudioSessionStateExpired) continue;

                    string processName = GetProcessName(pid);
                    if (IsHiddenProcess(processName)) continue;

                    session2.GetDisplayName(out string? displayName);
                    if (string.IsNullOrEmpty(displayName)) displayName = processName;

                    var volumeControl = (ISimpleAudioVolume)sessionControl;
                    IAudioMeterInformation? meterInfo = null;
                    try { meterInfo = (IAudioMeterInformation)sessionControl; } catch { }

                    var info = new AudioSessionInfo
                    {
                        ProcessId = pid,
                        ProcessName = processName,
                        DisplayName = displayName!,
                        SessionControl = session2,
                        VolumeControl = volumeControl,
                        MeterInfo = meterInfo
                    };

                    if (grouped.TryGetValue(processName, out var primary))
                    {
                        primary.AddGroupedSession(volumeControl, meterInfo);
                    }
                    else
                    {
                        grouped[processName] = info;
                        Sessions.Add(info);

                        var listener = new SessionEventListener(info, this);
                        sessionControl.RegisterAudioSessionNotification(listener);
                        _eventListeners.Add(listener);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SessionManager: Error reading session {i}: {ex.Message}");
                }
            }

            Marshal.ReleaseComObject(enumerator);

            _lastSessionCount = Sessions.Count;
        }

        SessionsChanged?.Invoke();
    }

    public List<AudioSessionInfo> GetSessionSnapshot()
    {
        lock (_lock)
        {
            return Sessions.ToList();
        }
    }

    public AudioSessionInfo? FindSessionByProcessName(string name)
    {
        lock (_lock)
        {
            return Sessions.FirstOrDefault(s =>
                s.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Finds session by partial process name match (case insensitive).
    /// </summary>
    public AudioSessionInfo? FindSessionContaining(string partialName)
    {
        lock (_lock)
        {
            return Sessions.FirstOrDefault(s =>
                s.ProcessName.Contains(partialName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public List<AudioSessionInfo> FindSessionsByProcessName(string name)
    {
        lock (_lock)
        {
            return Sessions.Where(s =>
                s.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private static readonly HashSet<string> HiddenProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "GameBridge", "GameAudioMixer", "Slider",
        // Background service helpers that never produce useful audio
        "steamwebhelper", "steam_monitor",
        "nvcontainer", "nvsphelper64", "NVDisplay.Container", "nvspcaps64",
        "lghub_agent", "lghub_updater", "lghub_system_tray",
        "CorsairGamingAudioCfgSvc64",
        "EOSOverlayRenderer-Win64-Shipping", "EpicWebHelper",
        "EpicOnlineServicesUserHelper",
        "audiodg", "RAVBg64", "RAVCpl64", "RtkAudUService64",
        "WavesSvc64", "NahimicSvc64", "NahimicSvc32",
        "CrashReportClient", "GoogleUpdate", "MicrosoftEdgeUpdate",
    };

    private static bool IsHiddenProcess(string processName) =>
        HiddenProcesses.Contains(processName) ||
        processName.StartsWith("PID ", StringComparison.Ordinal);

    private static string GetProcessName(uint pid)
    {
        try
        {
            var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return $"PID {pid}";
        }
    }

    private void OnDefaultDeviceChanged(string deviceId)
    {
        Task.Run(InitializeSessionManager);
    }

    private void CleanupCurrentManager()
    {
        foreach (var listener in _eventListeners)
            listener.Detach();
        _eventListeners.Clear();
        Sessions.Clear();

        if (_sessionManager != null)
        {
            try
            {
                _sessionManager.UnregisterSessionNotification(this);
                Marshal.ReleaseComObject(_sessionManager);
            }
            catch { }
            _sessionManager = null;
        }
    }

    internal void OnSessionDisconnected(AudioSessionInfo session)
    {
        lock (_lock)
        {
            Sessions.Remove(session);
        }
        SessionsChanged?.Invoke();
    }

    #region IAudioSessionNotification

    void IAudioSessionNotification.OnSessionCreated(IAudioSessionControl newSession)
    {
        Task.Run(async () =>
        {
            await Task.Delay(800);
            RefreshSessions();
        });
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Dispose();
        _deviceManager.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        CleanupCurrentManager();
    }

    private sealed class SessionEventListener : IAudioSessionEvents
    {
        private readonly AudioSessionInfo _session;
        private readonly SessionManager _manager;
        private IAudioSessionControl? _control;

        public SessionEventListener(AudioSessionInfo session, SessionManager manager)
        {
            _session = session;
            _manager = manager;
            _control = (IAudioSessionControl)session.SessionControl;
        }

        public void Detach()
        {
            if (_control != null)
            {
                try { _control.UnregisterAudioSessionNotification(this); }
                catch { }
                _control = null;
            }
        }

        void IAudioSessionEvents.OnDisplayNameChanged(string NewDisplayName, ref Guid EventContext) { }
        void IAudioSessionEvents.OnIconPathChanged(string NewIconPath, ref Guid EventContext) { }
        void IAudioSessionEvents.OnSimpleVolumeChanged(float NewVolume, bool NewMute, ref Guid EventContext)
        {
            // Volume changes don't require re-enumerating sessions.
            // Firing SessionsChanged here caused cascading RefreshSessions every 33ms
            // whenever DuckingService wrote to the game session volume.
        }
        void IAudioSessionEvents.OnChannelVolumeChanged(uint ChannelCount, IntPtr NewChannelVolumeArray, uint ChangedChannel, ref Guid EventContext) { }
        void IAudioSessionEvents.OnGroupingParamChanged(ref Guid NewGroupingParam, ref Guid EventContext) { }
        void IAudioSessionEvents.OnStateChanged(AudioSessionState NewState)
        {
            if (NewState == AudioSessionState.AudioSessionStateExpired)
                _manager.OnSessionDisconnected(_session);
        }
        void IAudioSessionEvents.OnSessionDisconnected(AudioSessionDisconnectReason DisconnectReason)
        {
            _manager.OnSessionDisconnected(_session);
        }
    }
}
