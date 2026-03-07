using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAudioMixer.Core;
using GameAudioMixer.Hotkeys;
using GameAudioMixer.Models;
using GameAudioMixer.Profiles;
using GameAudioMixer.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GameAudioMixer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AudioManager? _audioManager;
    private readonly DeviceManager? _deviceManager;
    private readonly SessionManager? _sessionManager;
    private readonly HotkeyManager? _hotkeyManager;
    private readonly ProfileManager _profileManager;
    private readonly ForegroundProcessService? _foregroundService;
    private readonly DiscordDetectionService? _discordService;
    private readonly DuckingService? _duckingService;
    private readonly DispatcherTimer _meterTimer;
    private DispatcherQueue? _dispatcher;
    private HudService? _hud;
    /// <summary>Tracks the last game name assigned, survives session refreshes.</summary>
    private string _lastAssignedGameName = "";
    private int _micPollCounter;

    // Auto-switch chat: counts consecutive silent ticks on current chat session.
    // When the current chat has been silent for ~5s AND another chat app has audio, we switch.
    private int _chatSilentTicks;
    private const int ChatSilentTicksThreshold = 150; // ~5s at 33ms/tick

    // True when the user manually picked a chat session (bypasses the known-chat-process guard).
    private bool _chatManuallySelected;

    // Global mute state per slot — persists across session switches.
    private bool _gameMuted;
    private bool _chatMuted;

    private const int BalanceStep = 10;

    /// <summary>
    /// Known system/shell processes that are never a "game".
    /// </summary>
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
        "SystemSettings", "ApplicationFrameHost", "TextInputHost", "LockApp",
        "GameAudioMixer", "GameBridge", "svchost", "RuntimeBroker", "taskhostw",
        "dwm", "csrss", "conhost", "sihost", "ctfmon",
        "Cursor", "Code", "devenv", "WindowsTerminal", "cmd", "powershell", "pwsh",
        "Taskmgr", "mmc", "notepad", "regedit", "mspaint", "SnippingTool",
        "msedge", "chrome", "firefox", "opera", "brave",
        "Spotify", "SpotifyWebHelper",
        "steam", "steamwebhelper", "steam_monitor",
        "EpicGamesLauncher", "EpicWebHelper", "EpicOnlineServicesUserHelper",
        "EOSOverlayRenderer-Win64-Shipping",
        "RiotClientServices", "RiotClientUx", "RiotClientCrashHandler",
        "Battle.net", "Agent",
        "EADesktop", "EABackgroundService", "EAConnect_microsoft",
        "UbisoftConnect", "UbisoftGameLauncher", "upc",
        "GalaxyClient", "GalaxyClientHelper",
        "nvcontainer", "lghub_agent", "lghub_updater",
    };

    public ObservableCollection<AudioSessionViewModel> Sessions { get; } = [];
    public ObservableCollection<DeviceViewModel> OutputDevices { get; } = [];
    public ObservableCollection<DeviceViewModel> InputDevices { get; } = [];

    [ObservableProperty] private string _foregroundProcess = "";
    [ObservableProperty] private string _currentPresetName = "";
    [ObservableProperty] private bool _isMicMuted;
    [ObservableProperty] private bool _isDucking;
    [ObservableProperty] private bool _duckingEnabled = true;
    [ObservableProperty] private string _chatProcessName = "Discord";
    [ObservableProperty] private AudioSessionViewModel? _selectedGameSession;
    [ObservableProperty] private AudioSessionViewModel? _selectedChatSession;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private int _balance = 50;
    [ObservableProperty] private string _detectedGameName = "";
    [ObservableProperty] private string _detectedChatName = "";
    [ObservableProperty] private int _masterVolume = 100;

    public AppSettings Settings => _profileManager.Settings;

    public MainViewModel(
        AudioManager? audioManager,
        DeviceManager? deviceManager,
        SessionManager? sessionManager,
        HotkeyManager? hotkeyManager,
        ProfileManager profileManager,
        ForegroundProcessService? foregroundService,
        DiscordDetectionService? discordService,
        DuckingService? duckingService)
    {
        _audioManager = audioManager;
        _deviceManager = deviceManager;
        _sessionManager = sessionManager;
        _hotkeyManager = hotkeyManager;
        _profileManager = profileManager;
        _foregroundService = foregroundService;
        _discordService = discordService;
        _duckingService = duckingService;

        _chatProcessName = profileManager.Settings.ChatProcessName;
        if (_discordService != null)
            _discordService.ChatProcessName = _chatProcessName;
        var currentPreset = profileManager.GetCurrentPreset();
        _currentPresetName = currentPreset?.Name ?? "";
        _balance = profileManager.Settings.GlobalBalance;

        _duckingEnabled = profileManager.Settings.AutoDuckingEnabled;
        if (_duckingService != null)
        {
            _duckingService.Enabled = _duckingEnabled;
            _duckingService.DuckPercent = profileManager.Settings.DuckingPercent;
            _duckingService.ActivationThreshold = profileManager.Settings.DuckingThreshold;
            _duckingService.AttackDurationMs = profileManager.Settings.DuckingAttackDuration * 1000f;
            _duckingService.ReleaseDurationMs = profileManager.Settings.DuckingReleaseDuration * 1000f;
            _duckingService.DuckingStateChanged += ducked =>
            {
                _dispatcher?.TryEnqueue(() => IsDucking = ducked);
            };
        }

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += OnMeterTick;
    }

    public static float BalanceToGameVol(int balance) =>
        balance <= 100 ? 1.0f : Math.Max(0f, 1.0f - (balance - 100) / 100f);

    public static float BalanceToChatVol(int balance) =>
        balance >= 100 ? 1.0f : Math.Max(0f, balance / 100f);

    partial void OnBalanceChanged(int value)
    {
        ApplyBalance(value);
        _profileManager.Settings.GlobalBalance = value;
        _profileManager.Save();
    }

    partial void OnDuckingEnabledChanged(bool value)
    {
        if (_duckingService != null)
            _duckingService.Enabled = value;
        _profileManager.Settings.AutoDuckingEnabled = value;
        _profileManager.Save();
    }

    private void ApplyBalance(int balance)
    {
        float gameVol = BalanceToGameVol(balance);
        float chatVol = BalanceToChatVol(balance);

        if (SelectedGameSession != null)
        {
            float duckFactor = _duckingService?.DuckFactor ?? 1f;
            SelectedGameSession.Volume = gameVol * duckFactor;
        }
        if (SelectedChatSession != null)
            SelectedChatSession.Volume = chatVol;
    }

    public void PreCreateHud()
    {
        _hud?.PreCreateWindow();
    }

    public void Initialize(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _hud = new HudService(dispatcher);

        try { RefreshDevices(); } catch { }
        try { RefreshSessions(); } catch { }

        if (_sessionManager != null)
            _sessionManager.SessionsChanged += () =>
                _dispatcher?.TryEnqueue(() =>
                {
                    RefreshSessions();
                    TryAutoAssign();
                });

        if (_deviceManager != null)
            _deviceManager.DevicesChanged += () =>
                _dispatcher?.TryEnqueue(RefreshDevices);

        if (_foregroundService != null)
        {
            _foregroundService.ForegroundChanged += (name, pid) =>
                _dispatcher?.TryEnqueue(() => OnForegroundChanged(name, pid));
            _foregroundService.Start();
        }

        if (_hotkeyManager != null)
        {
            _hotkeyManager.HotkeyPressed += action =>
                _dispatcher?.TryEnqueue(() => HandleHotkey(action));
            _hotkeyManager.RegisterBindings(_profileManager.Settings.Hotkeys);
        }

        _meterTimer.Start();


        try { IsMicMuted = _audioManager?.GetMicrophoneMuteState() ?? false; }
        catch { IsMicMuted = false; }

        TryAutoAssign();
    }

    /// <summary>
    /// Core auto-detection logic. Called on every session refresh and foreground change.
    /// </summary>
    private void TryAutoAssign()
    {
        TryAutoAssignChat();
        TryAutoAssignGame();
    }

    private void TryAutoAssignChat()
    {
        if (SelectedChatSession != null)
        {
            // If session process is gone, clear and re-detect.
            var stillAlive = Sessions.Any(s => s.ProcessId == SelectedChatSession.ProcessId);
            if (!stillAlive)
            {
                SetChatSession(null);
                DetectedChatName = "";
                _chatSilentTicks = 0;
            }
            else
            {
                // Check if another known chat app has audio while this one is silent.
                // Only attempt switch when current app has been quiet for ~5s.
                bool currentHasAudio = SelectedChatSession.PeakValue > 0.005f;
                if (currentHasAudio)
                {
                    _chatSilentTicks = 0;
                    return;
                }

                _chatSilentTicks++;
                if (_chatSilentTicks < ChatSilentTicksThreshold) return;

                // Current chat is silent for 5s — check if another chat app is active.
                var activeOther = FindActiveChatSession(excludePid: SelectedChatSession.ProcessId);
                if (activeOther == null) return;

                // Switch to the active one.
                _chatSilentTicks = 0;
                AssignChatSession(activeOther);
                return;
            }
        }

        // No session yet — pick the first chat app with audio, or fall back to any known chat app.
        var activeVm = FindActiveChatSession(excludePid: 0);
        if (activeVm != null)
        {
            AssignChatSession(activeVm);
            return;
        }

        var fallback = _discordService?.GetChatSession();
        if (fallback == null) return;
        var vm = Sessions.FirstOrDefault(s => s.ProcessId == fallback.ProcessId);
        if (vm == null || vm == SelectedGameSession) return;
        AssignChatSession(vm);
    }

    /// <summary>Returns the known chat session with the highest peak, excluding the given PID.</summary>
    private AudioSessionViewModel? FindActiveChatSession(uint excludePid)
    {
        AudioSessionViewModel? best = null;
        float bestPeak = 0.005f; // minimum threshold to be considered "active"
        foreach (var s in Sessions)
        {
            if (s.ProcessId == excludePid) continue;
            if (s == SelectedGameSession) continue;
            if (!DiscordDetectionService.IsChatProcess(s.ProcessName)) continue;
            if (s.PeakValue > bestPeak)
            {
                bestPeak = s.PeakValue;
                best = s;
            }
        }
        return best;
    }

    private void AssignChatSession(AudioSessionViewModel vm)
    {
        _chatManuallySelected = false;
        SetChatSession(vm);
        DetectedChatName = vm.ProcessName;

        if (SelectedGameSession != null)
        {
            var profile = _profileManager.FindProfileForProcess(SelectedGameSession.ProcessName);
            if (profile != null)
            {
                ApplyGameProfile(profile);
                StatusMessage = $"Profile: {profile.DisplayName}";
                return;
            }
        }

        StatusMessage = $"Chat detected: {vm.ProcessName}";
    }

    private void AssignChatSession(AudioSessionInfo info)
    {
        var vm = Sessions.FirstOrDefault(s => s.ProcessId == info.ProcessId);
        if (vm != null && vm != SelectedGameSession)
            AssignChatSession(vm);
    }

    private const int DefaultBalance = 100;

    private void TryAutoAssignGame()
    {
        if (SelectedGameSession != null)
        {
            var stillAlive = Sessions.Any(s => s.ProcessId == SelectedGameSession.ProcessId);
            if (stillAlive)
            {
                // Foreground switched to a different potential game whose session now exists:
                // re-assign instead of staying locked on the old process (e.g. launcher).
                if (!string.IsNullOrEmpty(ForegroundProcess) &&
                    IsPotentialGame(ForegroundProcess) &&
                    !ForegroundProcess.Equals(SelectedGameSession.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    var fgSession = Sessions.FirstOrDefault(s =>
                        s.ProcessName.Equals(ForegroundProcess, StringComparison.OrdinalIgnoreCase));
                    if (fgSession != null)
                        AssignGameSession(fgSession);
                }
                return;
            }
            SetGameSession(null);
            DetectedGameName = "";
            // Do NOT clear _lastAssignedGameName here: audio sessions can disappear briefly
            // during a refresh even while the game is still running. Keeping the name ensures
            // auto-save targets the right profile and alreadyActive stays valid.
            RestoreGlobalDuckingParams();
        }

        // Try to match the current foreground process to an audio session
        if (!string.IsNullOrEmpty(ForegroundProcess) && IsPotentialGame(ForegroundProcess))
        {
            var gameVm = Sessions.FirstOrDefault(s =>
                s.ProcessName.Equals(ForegroundProcess, StringComparison.OrdinalIgnoreCase));
            if (gameVm != null)
            {
                AssignGameSession(gameVm);
                return;
            }
        }

        // Fallback: pick the session with the most audio activity that isn't the chat
        var candidate = Sessions
            .Where(s => s != SelectedChatSession &&
                        !SystemProcesses.Contains(s.ProcessName) &&
                        !DiscordDetectionService.IsChatProcess(s.ProcessName))
            .OrderByDescending(s => s.PeakValue)
            .FirstOrDefault();

        if (candidate != null)
        {
            AssignGameSession(candidate);
            return;
        }

        // No game at all — the game truly closed
        _lastAssignedGameName = "";
        if (Balance != DefaultBalance)
        {
            Balance = DefaultBalance;
            StatusMessage = "Game closed — balance reset";
        }
    }

    /// <summary>
    /// Assigns a game session and applies saved profile settings if available.
    /// </summary>
    private void AssignGameSession(AudioSessionViewModel session)
    {
        SetGameSession(session);
        DetectedGameName = session.ProcessName;
        _lastAssignedGameName = session.ProcessName;
        StatusMessage = $"Game detected: {session.ProcessName}";
    }

    /// <summary>
    /// Determines if a process name could be a game (not a known system/shell/chat process).
    /// </summary>
    private static bool IsPotentialGame(string processName)
    {
        if (SystemProcesses.Contains(processName)) return false;
        if (DiscordDetectionService.IsChatProcess(processName)) return false;
        return true;
    }

    private void OnMeterTick(object? sender, object e)
    {
        // Check every tick for chat auto-switch (silent-app detection uses per-tick counter).
        TryAutoAssignChat();

        // Read chat peak FIRST — Windows IAudioMeterInformation.GetPeakValue() resets on each read.
        // Use ONLY SelectedChatSession for ducking decisions; never fall back to a process-name scan.
        // A fallback scan using partial process-name matching can accidentally pick up a game helper
        // process whose name contains a chat-app keyword (e.g. "SignalR", "TeamSpeak" plugin),
        // causing the game to duck itself whenever its own audio is loud.
        var chatVm = SelectedChatSession;
        // Safety guard 1: refuse to use the game session as the chat source under ANY circumstance.
        if (chatVm != null && SelectedGameSession != null &&
            (chatVm == SelectedGameSession ||
             chatVm.ProcessId == SelectedGameSession.ProcessId ||
             chatVm.ProcessName.Equals(SelectedGameSession.ProcessName, StringComparison.OrdinalIgnoreCase)))
            chatVm = null;
        // Safety guard 2: refuse to use any non-chat process as the chat source.
        // Skipped when the user has manually selected a session (they know what they're doing).
        if (!_chatManuallySelected &&
            chatVm != null &&
            !DiscordDetectionService.IsChatProcess(chatVm.ProcessName) &&
            !chatVm.ProcessName.Equals(ChatProcessName, StringComparison.OrdinalIgnoreCase))
            chatVm = null;
        float chatPeak = 0f;
        try { chatPeak = chatVm?.UpdatePeak() ?? 0f; } catch { }
        // When chat is muted from the app, treat peak as 0 so ducking never activates
        if (chatVm?.IsMuted == true) chatPeak = 0f;

        // Update all other session peaks.
        // Track which sessions were read so we never call UpdatePeak() twice on the same
        // session — Windows resets the COM peak meter on every read, so a second call
        // on the same session returns ~0, corrupting PeakValue and the visual meters.
        var snapshot = Sessions.ToArray();
        bool gameReadInLoop = false;
        foreach (var session in snapshot)
        {
            if (session == chatVm) continue;
            try { session.UpdatePeak(); } catch { }
            if (session == SelectedGameSession) gameReadInLoop = true;
        }
        if (!gameReadInLoop)
            try { SelectedGameSession?.UpdatePeak(); } catch { }

        // Remove Discord's screen-share audio bleed from the chat peak.
        // When sharing screen with audio, Discord normalises the captured game audio and
        // plays it back locally at a fixed absolute level (~4-8%), regardless of game volume.
        // Subtracting a fixed 10% constant eliminates this bleed completely.
        // Only applied to Discord — other chat apps don't exhibit this behaviour.

        // Apply ducking with the chat peak we already read this frame
        bool wasDucked = _duckingService?.IsDucked ?? false;

        if (_duckingService != null && SelectedGameSession != null)
        {
            float duckFactor = _duckingService.Tick(chatPeak);
            float targetVol = BalanceToGameVol(Balance) * duckFactor;

            // Only write to COM if the value actually changed (avoids spurious OnSimpleVolumeChanged)
            if (Math.Abs(SelectedGameSession.Volume - targetVol) > 0.001f)
                SelectedGameSession.Volume = targetVol;
        }
        else
        {
            _duckingService?.Tick(chatPeak);
        }

        // Poll system master volume (cheap scalar read, every ~80ms is fine)
        if (_audioManager != null)
        {
            try
            {
                int mv = (int)MathF.Round(_audioManager.GetSystemMasterVolume() * 100f);
                if (mv != MasterVolume) MasterVolume = mv;
            }
            catch { }
        }

        // Poll mic mute state every ~500ms to catch external changes (headset button, other apps)
        if (++_micPollCounter >= 15)
        {
            _micPollCounter = 0;
            var micMuted = _audioManager?.GetMicrophoneMuteState();
            if (micMuted.HasValue && micMuted.Value != IsMicMuted)
                IsMicMuted = micMuted.Value;
        }
    }

    private void RefreshSessions()
    {
        // Remember the process IDs of the currently selected sessions before clearing
        var prevGamePid = SelectedGameSession?.ProcessId;
        var prevChatPid = SelectedChatSession?.ProcessId;

        Sessions.Clear();
        if (_sessionManager == null) return;
        foreach (var session in _sessionManager.GetSessionSnapshot())
        {
            var vm = new AudioSessionViewModel(session);
            Sessions.Add(vm);
        }

        // Restore selected sessions to the new VMs that match the same process,
        // so the meter always has a live VM to read PeakValue from.
        if (prevGamePid.HasValue)
        {
            var newVm = Sessions.FirstOrDefault(s => s.ProcessId == prevGamePid.Value);
            // Use direct assignment here (not SetGameSession) to avoid restoring/reapplying
            // volume on a simple VM pointer refresh — the session didn't actually change.
            SelectedGameSession = newVm;
            if (newVm == null) DetectedGameName = "";
        }

        if (prevChatPid.HasValue)
        {
            var newVm = Sessions.FirstOrDefault(s => s.ProcessId == prevChatPid.Value);
            SelectedChatSession = newVm;
            if (newVm == null) DetectedChatName = "";
        }
    }

    private void RefreshDevices()
    {
        OutputDevices.Clear();
        InputDevices.Clear();
        if (_deviceManager == null) return;

        foreach (var device in _deviceManager.GetOutputDevices())
            OutputDevices.Add(new DeviceViewModel(device));

        foreach (var device in _deviceManager.GetInputDevices())
            InputDevices.Add(new DeviceViewModel(device));
    }

    private void OnForegroundChanged(string processName, uint pid)
    {
        ForegroundProcess = processName;

        if (IsPotentialGame(processName))
        {
            var gameSession = Sessions.FirstOrDefault(s =>
                s.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            if (gameSession != null)
            {
                AssignGameSession(gameSession);
                return;
            }

            // Session doesn't exist yet — nothing to pre-apply, Balance is already global.
        }
    }

    /// <summary>Restores the ducking service to the global AppSettings values.</summary>
    public void RestoreGlobalDuckingParams()
    {
        if (_duckingService == null) return;
        var s = _profileManager.Settings;
        _duckingService.DuckPercent = s.DuckingPercent;
        _duckingService.ActivationThreshold = s.DuckingThreshold;
        _duckingService.AttackSpeed = s.DuckingAttackSpeed;
        _duckingService.ReleaseSpeed = s.DuckingReleaseSpeed;
    }

    private void ApplyGameProfile(GameProfile profile)
    {
        Balance = profile.Balance;
        DuckingEnabled = profile.DuckingEnabled;

        if (_duckingService != null)
        {
            if (profile.UseCustomDucking)
            {
                _duckingService.DuckPercent = profile.CustomDuckingPercent;
                _duckingService.ActivationThreshold = profile.CustomDuckingThreshold;
                _duckingService.AttackSpeed = profile.CustomDuckingAttackSpeed;
                _duckingService.ReleaseSpeed = profile.CustomDuckingAttackSpeed;
            }
            else
            {
                _duckingService.DuckPercent = profile.DuckingPercent;
                RestoreGlobalDuckingParams();
            }
        }

        try
        {
            if (profile.PreferredOutputDeviceId != null)
                _deviceManager?.SetDefaultDevice(profile.PreferredOutputDeviceId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ApplyGameProfile: SetDefaultDevice failed: {ex.Message}");
        }

        StatusMessage = $"Profile: {profile.DisplayName}";
        _hud?.ShowPreset(profile.DisplayName, profile.Balance);
    }

    private void HandleHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.BalanceToGame:
            {
                // Snap to nearest lower multiple of 10, then step if already aligned
                int snapped = Balance % BalanceStep == 0
                    ? Balance - BalanceStep
                    : (Balance / BalanceStep) * BalanceStep;
                Balance = Math.Max(0, snapped);
                StatusMessage = $"Balance: Game {(int)(BalanceToGameVol(Balance)*100)}% / Chat {(int)(BalanceToChatVol(Balance)*100)}%";
                _hud?.ShowBalance(Balance, DetectedGameName, DetectedChatName);
                break;
            }

            case HotkeyAction.BalanceToChat:
            {
                // Snap to nearest upper multiple of 10, then step if already aligned
                int snapped = Balance % BalanceStep == 0
                    ? Balance + BalanceStep
                    : (int)Math.Ceiling(Balance / (double)BalanceStep) * BalanceStep;
                Balance = Math.Min(200, snapped);
                StatusMessage = $"Balance: Game {(int)(BalanceToGameVol(Balance)*100)}% / Chat {(int)(BalanceToChatVol(Balance)*100)}%";
                _hud?.ShowBalance(Balance, DetectedGameName, DetectedChatName);
                break;
            }

            case HotkeyAction.GameVolumeUp:
            case HotkeyAction.GameVolumeDown:
            case HotkeyAction.ChatVolumeUp:
            case HotkeyAction.ChatVolumeDown:
                break;

            case HotkeyAction.TogglePreset:
                var preset = _profileManager.CyclePreset();
                if (preset != null)
                {
                    CurrentPresetName = preset.Name;
                    Balance = preset.Balance;
                    StatusMessage = $"Preset: {preset.Name}";
                    _hud?.ShowPreset(preset.Name, preset.Balance);
                }
                break;

            case HotkeyAction.SwitchOutputDevice:
                var newId = _deviceManager?.CycleDevice(_profileManager.Settings.PreferredDeviceIds);
                if (newId != null)
                {
                    RefreshDevices();
                    var dev = OutputDevices.FirstOrDefault(d => d.Id == newId);
                    StatusMessage = $"Output: {dev?.FriendlyName ?? newId}";
                    _hud?.ShowDevice(dev?.FriendlyName ?? newId);
                }
                break;

            case HotkeyAction.ToggleMicMute:
                ToggleMic();
                _hud?.ShowMic(IsMicMuted);
                break;

            case HotkeyAction.MuteGame:
                _gameMuted = !_gameMuted;
                if (SelectedGameSession != null)
                    SelectedGameSession.IsMuted = _gameMuted;
                StatusMessage = _gameMuted ? "Game: Muted" : "Game: Active";
                _hud?.ShowMuteGame(_gameMuted, DetectedGameName);
                break;

            case HotkeyAction.MuteChat:
                _chatMuted = !_chatMuted;
                if (SelectedChatSession != null)
                    SelectedChatSession.IsMuted = _chatMuted;
                StatusMessage = _chatMuted ? "Chat: Muted" : "Chat: Active";
                _hud?.ShowMuteChat(_chatMuted, DetectedChatName);
                break;

            case HotkeyAction.ToggleDucking:
                DuckingEnabled = !DuckingEnabled;
                StatusMessage = DuckingEnabled ? "Auto-Ducking: ON" : "Auto-Ducking: OFF";
                _hud?.ShowDucking(DuckingEnabled);
                break;
        }
    }

    public bool ToggleGameMute()
    {
        _gameMuted = !_gameMuted;
        if (SelectedGameSession != null)
            SelectedGameSession.IsMuted = _gameMuted;
        return _gameMuted;
    }

    public bool ToggleChatMute()
    {
        _chatMuted = !_chatMuted;
        if (SelectedChatSession != null)
            SelectedChatSession.IsMuted = _chatMuted;
        return _chatMuted;
    }

    [RelayCommand]
    private void ToggleMic()
    {
        var result = _audioManager?.ToggleMicrophoneMute();
        if (result.HasValue)
        {
            IsMicMuted = result.Value;
            StatusMessage = result.Value ? "Microphone: Muted" : "Microphone: Active";
        }
    }

    [RelayCommand]
    private void SetOutputDevice(DeviceViewModel? device)
    {
        if (device == null) return;
        if (_deviceManager != null && _deviceManager.SetDefaultDevice(device.Id))
        {
            RefreshDevices();
            StatusMessage = $"Output: {device.FriendlyName}";
        }
    }

    [RelayCommand]
    private void CyclePreset()
    {
        HandleHotkey(HotkeyAction.TogglePreset);
    }

    [RelayCommand]
    private void SaveCurrentProfile()
    {
        // Fallback to _lastAssignedGameName in case DetectedGameName was briefly cleared.
        string gameName = !string.IsNullOrEmpty(DetectedGameName)
            ? DetectedGameName
            : _lastAssignedGameName;
        if (string.IsNullOrEmpty(gameName)) return;

        // Update the existing profile to preserve custom ducking settings.
        // Only create a new one if no profile exists yet for this game.
        var profile = _profileManager.FindProfileForProcess(gameName) ?? new GameProfile
        {
            ExeName = gameName,
            DisplayName = gameName,
        };

        profile.Balance = Balance;
        profile.DuckingEnabled = _duckingService?.Enabled ?? true;
        profile.DuckingPercent = _duckingService?.DuckPercent ?? 30f;

        var preferredDevice = OutputDevices.FirstOrDefault(d => d.IsDefault);
        if (preferredDevice != null)
            profile.PreferredOutputDeviceId = preferredDevice.Id;

        _profileManager.SaveGameProfile(profile);
        StatusMessage = $"Profile saved: {gameName}";
    }

    /// <summary>
    /// Central method to change the game session.
    /// Restores previous session to 100% volume and applies current balance + mute state to new one.
    /// </summary>
    private void SetGameSession(AudioSessionViewModel? session)
    {
        // Restore previous session to 100% and unmute it
        if (SelectedGameSession != null && SelectedGameSession != session)
        {
            SelectedGameSession.Volume = 1f;
            if (SelectedGameSession.IsMuted)
                SelectedGameSession.IsMuted = false;
        }

        SelectedGameSession = session;

        if (session != null)
        {
            session.IsMuted = _gameMuted;
            ApplyBalance(Balance);
        }
    }

    /// <summary>
    /// Central method to change the chat session.
    /// Restores previous session to 100% volume and applies current balance + mute state to new one.
    /// </summary>
    private void SetChatSession(AudioSessionViewModel? session)
    {
        // Restore previous session to 100% and unmute it
        if (SelectedChatSession != null && SelectedChatSession != session)
        {
            SelectedChatSession.Volume = 1f;
            if (SelectedChatSession.IsMuted)
                SelectedChatSession.IsMuted = false;
        }

        SelectedChatSession = session;

        if (session != null)
        {
            session.IsMuted = _chatMuted;
            ApplyBalance(Balance);
        }
    }

    /// <summary>
    /// Manually override game session selection. Passing null re-enables auto-detection.
    /// </summary>
    public void ManualSelectGame(AudioSessionViewModel? session)
    {
        SetGameSession(session);
        DetectedGameName = session?.ProcessName ?? "";
    }

    /// <summary>
    /// Manually override chat session selection. Passing null re-enables auto-detection.
    /// </summary>
    public void ManualSelectChat(AudioSessionViewModel? session)
    {
        _chatSilentTicks = 0;
        _chatManuallySelected = session != null;
        SetChatSession(session);
        DetectedChatName = session?.ProcessName ?? "";
    }

    [RelayCommand]
    private void RefreshAll()
    {
        _sessionManager?.RefreshSessions();
        RefreshDevices();
        TryAutoAssign();
        StatusMessage = "Refreshed";
    }

    public void ApplyMasterVolume(int percent)
    {
        _audioManager?.SetSystemMasterVolume(percent / 100f);
    }

    public void Shutdown()
    {
        _meterTimer.Stop();
        _foregroundService?.Stop();
        _profileManager.Save();
        _hotkeyManager?.Dispose();
        _duckingService?.Dispose();
        _audioManager?.Dispose();
    }
}
