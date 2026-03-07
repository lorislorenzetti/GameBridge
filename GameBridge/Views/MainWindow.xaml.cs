using System.Text;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using GameAudioMixer.ViewModels;
using GameAudioMixer.Core.Interop;
using GameAudioMixer.Hotkeys;
using GameAudioMixer.Models;
using GameAudioMixer.Profiles;
using Microsoft.UI.Windowing;
using Windows.System;

namespace GameAudioMixer.Views;

public sealed partial class MainWindow : Window
{
    private MainViewModel ViewModel => App.MainViewModel;
    private bool _suppressEvents;
    private readonly DispatcherTimer _uiMeterTimer;
    private int _activePreset = -1;
    private Services.TrayIconService? _trayIcon;
    private IntPtr _hwnd;
    private bool _reallyClose;
    private bool _settingsVisible;
    private readonly List<ViewModels.DeviceViewModel> _displayedDevices = [];
    private string _lastNotifiedUpdateTag = "";
    private readonly DispatcherTimer _updateCheckTimer = new() { Interval = TimeSpan.FromHours(1) };

    private static readonly SolidColorBrush PresetActiveBg = new(ColorHelper.FromArgb(0x30, 0x60, 0xA5, 0xFA));
    private static readonly SolidColorBrush PresetActiveFg = new(ColorHelper.FromArgb(0xFF, 0x60, 0xA5, 0xFA));
    private static readonly SolidColorBrush PresetInactiveBg = new(ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush PresetInactiveFg = new(ColorHelper.FromArgb(0x70, 0xFF, 0xFF, 0xFF));

    private readonly DispatcherTimer _balanceAnimTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _animGameVol, _animChatVol, _animRatio;
    private double _targetGameVol, _targetChatVol, _targetRatio;
    private int _targetBalance;
    private double _cachedTrackWidth;
    private double _cachedMasterWidth;
    private const double AnimLerpSpeed = 0.18;

    // Home meter display values (0-1), persisted between ticks for smooth fade-out
    private double _gameMeterRatio;
    private double _chatMeterRatio;
    // Pre-created brushes to avoid per-tick GC allocations
    private readonly SolidColorBrush _peakActiveBrush  = new(ColorHelper.FromArgb(0x90, 0xB4, 0x4F, 0xFF));
    private readonly SolidColorBrush _peakIdleBrush    = new(ColorHelper.FromArgb(0x40, 0xB4, 0x4F, 0xFF));
    private readonly SolidColorBrush _badgeActiveBrush = new(ColorHelper.FromArgb(0x22, 0x00, 0xCF, 0xFF));
    private readonly SolidColorBrush _badgeIdleBrush   = new(ColorHelper.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
    private readonly SolidColorBrush _textActiveBrush  = new(ColorHelper.FromArgb(0xFF, 0x00, 0xCF, 0xFF));
    private readonly SolidColorBrush _textIdleBrush    = new(ColorHelper.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

    // Settings: hotkey recording
    private Button? _listeningButton;
    private int _listeningIndex = -1;

    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        [nameof(HotkeyAction.BalanceToGame)] = "Balance → Game",
        [nameof(HotkeyAction.BalanceToChat)] = "Balance → Chat",
        [nameof(HotkeyAction.TogglePreset)] = "Cycle Preset",
        [nameof(HotkeyAction.SwitchOutputDevice)] = "Switch Device",
        [nameof(HotkeyAction.ToggleMicMute)] = "Mute Microphone",
        [nameof(HotkeyAction.MuteGame)] = "Mute Game",
        [nameof(HotkeyAction.MuteChat)] = "Mute Chat",
        [nameof(HotkeyAction.ToggleDucking)] = "Toggle Auto-Ducking",
        [nameof(HotkeyAction.GameVolumeUp)] = "Volume Game +",
        [nameof(HotkeyAction.GameVolumeDown)] = "Volume Game -",
        [nameof(HotkeyAction.ChatVolumeUp)] = "Volume Chat +",
        [nameof(HotkeyAction.ChatVolumeDown)] = "Volume Chat -",
    };

    public MainWindow()
    {
        _balanceAnimTimer.Tick += OnBalanceAnimTick;

        InitializeComponent();
        Title = "GameBridge";

        TrySetMicaBackdrop();
        SetupCustomTitleBar();
        SetWindowSize(440, 660);
        ApplyWindowOpacity(0); // Start fully transparent — App.xaml.cs sets real alpha before SW_SHOW

        Closed += OnWindowClosed;

        _uiMeterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiMeterTimer.Tick += OnUiMeterTick;

        ViewModel.PropertyChanged += OnViewModelChanged;

        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ViewModel.Initialize(DispatcherQueue);
                BindUI();
                RefreshDeviceList();
                BuildPresetButtons();

                // Refresh the device list only when devices actually change, not on every session update
                ViewModel.OutputDevices.CollectionChanged += (_, _) => RefreshDeviceList();
                UpdateDuckToggleVisual();
                _uiMeterTimer.Start();

                ViewModel.PreCreateHud();
                StartTrayIcon();
                _ = CheckForUpdatesAsync();
                _updateCheckTimer.Tick += (_, _) => _ = CheckForUpdatesAsync();
                _updateCheckTimer.Start();

                MoveFocusToSink();

                BalanceGrid.SizeChanged += (_, _) =>
                {
                    _cachedTrackWidth = BalanceGrid.ActualWidth;
                    UpdateThumbPosition(_targetBalance);
                };

                MasterVolumeSlider.SizeChanged += (_, _) =>
                {
                    _cachedMasterWidth = MasterVolumeSlider.ActualWidth - 8;
                    UpdateMasterThumbPosition(ViewModel?.MasterVolume ?? 100);
                };

                // Update threshold marker whenever the track resizes.
                ChatPeakTrack.SizeChanged += (_, _) => UpdateThresholdMarker();
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Error: {ex.Message}";
            }
        });
    }

    // ══════════════════════════════════════════
    //  MAIN VIEW
    // ══════════════════════════════════════════

    private async Task CheckForUpdatesAsync()
    {
        var (isAvailable, latestTag) = await new Services.UpdateService().CheckAsync();
        if (!isAvailable) return;
        if (latestTag == _lastNotifiedUpdateTag) return;

        _lastNotifiedUpdateTag = latestTag;

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = $"Version {latestTag} is now available.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            Opacity = 0.9,
        });
        content.Children.Add(new TextBlock
        {
            Text = $"You are running v{Services.UpdateService.CurrentVersion}.",
            FontSize = 12,
            Opacity = 0.5,
        });

        var dialog = new ContentDialog
        {
            Title = "Update Available",
            Content = content,
            PrimaryButtonText = "Download",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(Services.UpdateService.ReleasesUrl));
    }

    private void SetWindowIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? "";
            Win32.ExtractIconEx(exePath, 0, out var hLarge, out var hSmall, 1);
            if (hLarge != IntPtr.Zero)
                Win32.SendMessage(_hwnd, Win32.WM_SETICON, new IntPtr(Win32.ICON_BIG), hLarge);
            if (hSmall != IntPtr.Zero)
                Win32.SendMessage(_hwnd, Win32.WM_SETICON, new IntPtr(Win32.ICON_SMALL), hSmall);
        }
        catch { }
    }

    private void StartTrayIcon()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowIcon();

        _trayIcon = new Services.TrayIconService();
        _trayIcon.ShowRequested += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Win32.ShowWindow(_hwnd, 9);
                Win32.SetForegroundWindow(_hwnd);
            });
        };
        _trayIcon.ExitRequested += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _reallyClose = true;
                Close();
            });
        };
        _trayIcon.Start();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_reallyClose)
        {
            args.Handled = true;
            Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
            return;
        }

        _trayIcon?.Dispose();
        ViewModel.Shutdown();
        // WinUI 3's Application.Start does not return automatically when windows close —
        // we must call Exit() explicitly so the mutex is released and the process terminates.
        Application.Current.Exit();
    }

    private void TrySetMicaBackdrop()
    {
        SystemBackdrop = null;
    }

    private void SetupCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void SetWindowSize(int width, int height)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

            if (appWindow.TitleBar != null)
            {
                appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                appWindow.TitleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0x15, 0xFF, 0xFF, 0xFF);
                appWindow.TitleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0x08, 0xFF, 0xFF, 0xFF);
                appWindow.TitleBar.ButtonForegroundColor = ColorHelper.FromArgb(0x80, 0xFF, 0xFF, 0xFF);
            }
        }
        catch { }
    }

    /// <summary>
    /// Sets the global Win32 window opacity (0 = invisible, 255 = fully opaque).
    /// Uses WS_EX_LAYERED + SetLayeredWindowAttributes so the entire window —
    /// including all content — is uniformly semi-transparent.
    /// </summary>
    internal void MoveFocusToSink() => FocusSink?.Focus(FocusState.Programmatic);

    internal void ApplyWindowOpacity(byte alpha)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, exStyle | Win32.WS_EX_LAYERED);
            Win32.SetLayeredWindowAttributes(hwnd, 0, alpha, 0x2 /* LWA_ALPHA */);
        }
        catch { }
    }

    private void BindUI()
    {
        _suppressEvents = true;

        BalanceSlider.Value = ViewModel.Balance;
        UpdateBalanceLabels(ViewModel.Balance);

        MicMuteToggle.IsOn = !ViewModel.IsMicMuted;
        UpdateMicVisuals();
        UpdateGameMuteVisuals(ViewModel.SelectedGameSession?.IsMuted ?? false);
        UpdateChatMuteVisuals(ViewModel.SelectedChatSession?.IsMuted ?? false);
        UpdateDetectedLabels();
        StatusBar.Text = ViewModel.StatusMessage;
        UpdateDuckToggleVisual();

        MasterVolumeSlider.Value = ViewModel.MasterVolume;
        MasterVolText.Text = $"{ViewModel.MasterVolume}%";
        UpdateMasterThumbPosition(ViewModel.MasterVolume);

        _suppressEvents = false;
    }

    // Shows only preferred devices (set in Settings). Falls back to the active device if none preferred.
    // Prevents the list from re-animating on every session update.
    private void RefreshDeviceList()
    {
        _suppressEvents = true;
        _displayedDevices.Clear();
        DeviceList.Items.Clear();

        var preferredIds = App.ProfileManager.Settings.PreferredDeviceIds;
        var all = ViewModel.OutputDevices;

        var toShow = preferredIds.Count > 0
            ? all.Where(d => preferredIds.Contains(d.Id)).ToList()
            : all.Where(d => d.IsDefault).Take(1).ToList();

        if (toShow.Count == 0)
            toShow = all.Take(1).ToList();

        foreach (var d in toShow)
        {
            _displayedDevices.Add(d);
            DeviceList.Items.Add($"{d.FriendlyName}{(d.IsDefault ? "  \u2713" : "")}");
        }

        DeviceCount.Text = preferredIds.Count > 0
            ? $"{toShow.Count} preferred"
            : $"{all.Count}";

        _suppressEvents = false;
    }

    private void UpdateDetectedLabels()
    {
        string gameName = ViewModel.DetectedGameName;
        string chatName = ViewModel.DetectedChatName;

        GameDetectedLabel.Text = string.IsNullOrEmpty(gameName) ? "Waiting..." : gameName;
        GameDetectedLabel.Opacity = string.IsNullOrEmpty(gameName) ? 0.4 : 0.9;

        ChatDetectedLabel.Text = string.IsNullOrEmpty(chatName) ? "Waiting..." : chatName;
        ChatDetectedLabel.Opacity = string.IsNullOrEmpty(chatName) ? 0.4 : 0.9;

        UpdateDiscordScreenShareBanner();
    }

    private void UpdateDiscordScreenShareBanner()
    {
        bool discordIsChat = ViewModel.SelectedChatSession != null &&
            ViewModel.SelectedChatSession.ProcessName.Contains("discord", StringComparison.OrdinalIgnoreCase);
        DiscordScreenShareBanner.Visibility = discordIsChat ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBalanceLabels(int balance)
    {
        _targetGameVol = MainViewModel.BalanceToGameVol(balance) * 100;
        _targetChatVol = MainViewModel.BalanceToChatVol(balance) * 100;
        _targetRatio = balance / 200.0;
        _targetBalance = balance;

        if (!_balanceAnimTimer.IsEnabled)
        {
            _animGameVol = _targetGameVol;
            _animChatVol = _targetChatVol;
            _animRatio = _targetRatio;
            ApplyBalanceVisuals();
        }

        _balanceAnimTimer.Start();
    }

    private void OnBalanceAnimTick(object? sender, object e)
    {
        _animGameVol += (_targetGameVol - _animGameVol) * AnimLerpSpeed;
        _animChatVol += (_targetChatVol - _animChatVol) * AnimLerpSpeed;
        _animRatio += (_targetRatio - _animRatio) * AnimLerpSpeed;

        ApplyBalanceVisuals();

        bool settled = Math.Abs(_animGameVol - _targetGameVol) < 0.5 &&
                       Math.Abs(_animChatVol - _targetChatVol) < 0.5;
        if (settled)
        {
            _animGameVol = _targetGameVol;
            _animChatVol = _targetChatVol;
            _animRatio = _targetRatio;
            ApplyBalanceVisuals();
            _balanceAnimTimer.Stop();
        }
    }

    private void ApplyBalanceVisuals()
    {
        GameVolText.Text = $"{(int)Math.Round(_animGameVol)}";
        ChatVolText.Text = $"{(int)Math.Round(_animChatVol)}";

        // Left column (game/green) grows with the thumb: split aligns with thumb position
        GameTrackCol.Width = new GridLength(Math.Max(0.01, _animRatio), GridUnitType.Star);
        ChatTrackCol.Width = new GridLength(Math.Max(0.01, 1.0 - _animRatio), GridUnitType.Star);

        UpdateThumbPosition(_targetBalance);
    }

    private void UpdateThumbPosition(int balance)
    {
        if (SliderThumb == null) return;
        if (_cachedTrackWidth <= 0)
            _cachedTrackWidth = BalanceGrid?.ActualWidth ?? 0;
        if (_cachedTrackWidth <= 0) return;
        // Subtract half thumb width (1.5) so thumb is centred on the computed position
        double offset = (balance / 200.0) * _cachedTrackWidth - 1.5;
        SliderThumb.Margin = new Thickness(offset, 0, 0, 0);
    }

    private void UpdateMicVisuals()
    {
        bool muted = ViewModel.IsMicMuted;
        MicStatusText.Text = muted ? "Muted" : "Active";
        // Active mic: neon green; muted mic: dim red
        MicStatusText.Foreground = new SolidColorBrush(muted
            ? ColorHelper.FromArgb(0x70, 0xFF, 0x44, 0x55)
            : ColorHelper.FromArgb(0x80, 0x00, 0xE8, 0x7A));
        MicIcon.Glyph = muted ? "\uF781" : "\uE720";
        MicIcon.Foreground = new SolidColorBrush(muted
            ? ColorHelper.FromArgb(0x50, 0xFF, 0x44, 0x55)
            : ColorHelper.FromArgb(0x65, 0x00, 0xE8, 0x7A));
        MicIconBg.Background = new SolidColorBrush(muted
            ? ColorHelper.FromArgb(0x0C, 0xFF, 0x44, 0x55)
            : ColorHelper.FromArgb(0x0C, 0x00, 0xE8, 0x7A));
    }

    private void UpdateDuckToggleVisual()
    {
        bool enabled = ViewModel.DuckingEnabled;
        // Enabled: cyan accent; disabled: very dim
        DuckToggleBtn.Background = new SolidColorBrush(enabled
            ? ColorHelper.FromArgb(0x14, 0x00, 0xCF, 0xFF)
            : ColorHelper.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
        DuckToggleBtn.BorderBrush = new SolidColorBrush(enabled
            ? ColorHelper.FromArgb(0x30, 0x00, 0xCF, 0xFF)
            : ColorHelper.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        DuckToggleIcon.Foreground = new SolidColorBrush(enabled
            ? ColorHelper.FromArgb(0xCC, 0x00, 0xCF, 0xFF)
            : ColorHelper.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
        DuckStatusText.Text = enabled ? "ON" : "OFF";
        DuckStatusBadge.Background = new SolidColorBrush(enabled
            ? ColorHelper.FromArgb(0x20, 0x00, 0xCF, 0xFF)
            : ColorHelper.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
        DuckStatusText.Foreground = new SolidColorBrush(enabled
            ? ColorHelper.FromArgb(0xCC, 0x00, 0xCF, 0xFF)
            : ColorHelper.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
    }

    private void OnUiMeterTick(object? sender, object e)
    {
        const double fadeStep = 0.04; // 4% per tick fade-out when session inactive

        // Home meters — Border with manual width, no WinUI animations
        _gameMeterRatio = ViewModel.SelectedGameSession != null
            ? ViewModel.SelectedGameSession.PeakValue
            : Math.Max(0, _gameMeterRatio - fadeStep);
        _chatMeterRatio = ViewModel.SelectedChatSession != null
            ? ViewModel.SelectedChatSession.PeakValue
            : Math.Max(0, _chatMeterRatio - fadeStep);

        double gw = GameMeterTrack.ActualWidth;
        if (gw > 0) GameMeterFill.Width = _gameMeterRatio * gw;
        double cw = ChatMeterTrack.ActualWidth;
        if (cw > 0) ChatMeterFill.Width = _chatMeterRatio * cw;

        // Ducking monitor — same tick, same source, always in sync
        if (_settingsVisible)
            UpdateDuckingMonitor();
    }

    private void UpdateDuckingMonitor()
    {
        var duck = App.DuckingService;
        if (duck == null) return;

        // Same source as home ChatMeter; settings are visible so ActualWidth is valid
        float peak = ViewModel.SelectedChatSession?.PeakValue ?? duck.LastChatPeak;
        double peakRatio = Math.Sqrt(Math.Min(peak, 1.0));

        double pw = ChatPeakTrack.ActualWidth;
        if (pw > 0) ChatPeakBar.Width = peakRatio * pw;

        bool active = duck.IsDucked;
        ChatPeakBar.Background = active ? _peakActiveBrush : _peakIdleBrush;

        DuckStateText.Text = active ? "DUCKING" : "IDLE";
        DuckStateBadge.Background = active ? _badgeActiveBrush : _badgeIdleBrush;
        DuckStateText.Foreground = active ? _textActiveBrush : _textIdleBrush;

        ChatPeakValue.Text = $"peak: {peak:F3}";
        ThresholdValue.Text = $"threshold: {FormatThreshold(duck.ActivationThreshold)}";

        float baseVol = MainViewModel.BalanceToGameVol(App.MainViewModel.Balance);
        float gameVol = baseVol * duck.DuckFactor;
        double gw = GameVolTrack.ActualWidth;
        if (gw > 0) GameVolBar.Width = duck.DuckFactor * gw;
        GameVolValue.Text = $"{(int)(gameVol * 100)}%  (×{duck.DuckFactor:F2})";
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.StatusMessage):
                    StatusBar.Text = ViewModel.StatusMessage;
                    break;
                case nameof(MainViewModel.ForegroundProcess):
                    ForegroundText.Text = ViewModel.ForegroundProcess;
                    break;
                case nameof(MainViewModel.DetectedGameName):
                    UpdateDetectedLabels();
                    break;
                case nameof(MainViewModel.DetectedChatName):
                    UpdateDetectedLabels();
                    break;
                case nameof(MainViewModel.Balance):
                    _suppressEvents = true;
                    BalanceSlider.Value = ViewModel.Balance;
                    UpdateBalanceLabels(ViewModel.Balance);
                    _suppressEvents = false;
                    break;
                case nameof(MainViewModel.IsMicMuted):
                    _suppressEvents = true;
                    MicMuteToggle.IsOn = !ViewModel.IsMicMuted;
                    UpdateMicVisuals();
                    _suppressEvents = false;
                    break;
                case nameof(MainViewModel.IsDucking):
                    DuckBadge.Visibility = ViewModel.IsDucking ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case nameof(MainViewModel.DuckingEnabled):
                    UpdateDuckToggleVisual();
                    break;
                case nameof(MainViewModel.CurrentPresetName):
                    HighlightPreset(App.ProfileManager.Settings.SelectedPresetIndex);
                    break;
                case nameof(MainViewModel.SelectedGameSession):
                case nameof(MainViewModel.SelectedChatSession):
                    BindUI();
                    break;
                case nameof(MainViewModel.MasterVolume):
                    _suppressEvents = true;
                    MasterVolumeSlider.Value = ViewModel.MasterVolume;
                    MasterVolText.Text = $"{ViewModel.MasterVolume}%";
                    _suppressEvents = false;
                    UpdateMasterThumbPosition(ViewModel.MasterVolume);
                    break;
            }
        });
    }

    private void OnBalanceChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ViewModel == null) return;
        int balance = (int)e.NewValue;
        ViewModel.Balance = balance;
        SetBalanceImmediate(balance);
    }

    private void OnMixSliderDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel == null) return;
        _suppressEvents = true;
        ViewModel.Balance = 100;
        BalanceSlider.Value = 100;
        _suppressEvents = false;
        SetBalanceImmediate(100);
    }

    private void SetBalanceImmediate(int balance)
    {
        double gv = MainViewModel.BalanceToGameVol(balance) * 100;
        double cv = MainViewModel.BalanceToChatVol(balance) * 100;
        double ratio = balance / 200.0;

        _animGameVol = _targetGameVol = gv;
        _animChatVol = _targetChatVol = cv;
        _animRatio = _targetRatio = ratio;
        _targetBalance = balance;

        _balanceAnimTimer.Stop();
        ApplyBalanceVisuals();
    }

    private static FontIcon CheckIcon(bool visible) =>
        new() { Glyph = visible ? "\uE73E" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11 };

    private void OnGameOverrideClick(object sender, RoutedEventArgs e)
    {
        GameFlyout.Items.Clear();

        bool isAuto = ViewModel.SelectedGameSession == null;
        var autoItem = new MenuFlyoutItem { Text = "Auto-detect", Icon = CheckIcon(isAuto) };
        autoItem.Click += (_, _) => ViewModel.ManualSelectGame(null);
        GameFlyout.Items.Add(autoItem);
        GameFlyout.Items.Add(new MenuFlyoutSeparator());

        foreach (var s in ViewModel.Sessions)
        {
            bool selected = s == ViewModel.SelectedGameSession;
            var item = new MenuFlyoutItem { Text = s.DisplayName, Tag = s, Icon = CheckIcon(selected) };
            item.Click += (_, _) =>
            {
                if (item.Tag is AudioSessionViewModel session)
                    ViewModel.ManualSelectGame(session);
            };
            GameFlyout.Items.Add(item);
        }
    }

    private void OnChatOverrideClick(object sender, RoutedEventArgs e)
    {
        ChatFlyout.Items.Clear();

        bool isAuto = ViewModel.SelectedChatSession == null;
        var autoItem = new MenuFlyoutItem { Text = "Auto-detect", Icon = CheckIcon(isAuto) };
        autoItem.Click += (_, _) => ViewModel.ManualSelectChat(null);
        ChatFlyout.Items.Add(autoItem);
        ChatFlyout.Items.Add(new MenuFlyoutSeparator());

        foreach (var s in ViewModel.Sessions)
        {
            bool selected = s == ViewModel.SelectedChatSession;
            var item = new MenuFlyoutItem { Text = s.DisplayName, Tag = s, Icon = CheckIcon(selected) };
            item.Click += (_, _) =>
            {
                if (item.Tag is AudioSessionViewModel session)
                    ViewModel.ManualSelectChat(session);
            };
            ChatFlyout.Items.Add(item);
        }
    }

    private void OnGameMuteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGameSession == null) return;
        bool muted = ViewModel.ToggleGameMute();
        UpdateGameMuteVisuals(muted);
    }

    private void UpdateGameMuteVisuals(bool muted)
    {
        GameMuteIcon.Glyph = muted ? "\uE74F" : "\uE767";
        GameMuteText.Text  = muted ? "UNMUTE GAME" : "MUTE GAME";

        if (muted)
        {
            GameMuteButton.Background   = new SolidColorBrush(ColorHelper.FromArgb(0x18, 0xFF, 0x44, 0x55));
            GameMuteButton.BorderBrush  = new SolidColorBrush(ColorHelper.FromArgb(0x44, 0xFF, 0x44, 0x55));
            GameMuteIcon.Foreground     = new SolidColorBrush(ColorHelper.FromArgb(0xEE, 0xFF, 0x44, 0x55));
            GameMuteText.Foreground     = new SolidColorBrush(ColorHelper.FromArgb(0xEE, 0xFF, 0x44, 0x55));
        }
        else
        {
            GameMuteButton.Background   = new SolidColorBrush(ColorHelper.FromArgb(0x0C, 0x00, 0xCF, 0xFF));
            GameMuteButton.BorderBrush  = new SolidColorBrush(ColorHelper.FromArgb(0x22, 0x00, 0xCF, 0xFF));
            GameMuteIcon.Foreground     = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0x00, 0xCF, 0xFF));
            GameMuteText.Foreground     = new SolidColorBrush(ColorHelper.FromArgb(0xDD, 0x00, 0xCF, 0xFF));
        }
    }

    private void OnChatMuteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedChatSession == null) return;
        bool muted = ViewModel.ToggleChatMute();
        UpdateChatMuteVisuals(muted);
    }

    private void UpdateChatMuteVisuals(bool muted)
    {
        ChatMuteIcon.Glyph = muted ? "\uE74F" : "\uE767";
        ChatMuteText.Text  = muted ? "UNMUTE CHAT" : "MUTE CHAT";

        if (muted)
        {
            ChatMuteButton.Background   = new SolidColorBrush(ColorHelper.FromArgb(0x18, 0xFF, 0x44, 0x55));
            ChatMuteButton.BorderBrush  = new SolidColorBrush(ColorHelper.FromArgb(0x44, 0xFF, 0x44, 0x55));
            ChatMuteIcon.Foreground     = new SolidColorBrush(ColorHelper.FromArgb(0xEE, 0xFF, 0x44, 0x55));
            ChatMuteText.Foreground     = new SolidColorBrush(ColorHelper.FromArgb(0xEE, 0xFF, 0x44, 0x55));
        }
        else
        {
            ChatMuteButton.Background   = new SolidColorBrush(ColorHelper.FromArgb(0x0C, 0xB4, 0x4F, 0xFF));
            ChatMuteButton.BorderBrush  = new SolidColorBrush(ColorHelper.FromArgb(0x22, 0xB4, 0x4F, 0xFF));
            ChatMuteIcon.Foreground     = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0xB4, 0x4F, 0xFF));
            ChatMuteText.Foreground     = new SolidColorBrush(ColorHelper.FromArgb(0xDD, 0xB4, 0x4F, 0xFF));
        }
    }

    private void OnDuckToggleClick(object sender, RoutedEventArgs e)
    {
        ViewModel.DuckingEnabled = !ViewModel.DuckingEnabled;
        ViewModel.StatusMessage = ViewModel.DuckingEnabled ? "Auto-Ducking enabled" : "Auto-Ducking disabled";
    }

    private void OnMicMuteToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ViewModel.ToggleMicCommand.Execute(null);
    }

    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        int idx = DeviceList.SelectedIndex;
        if (idx >= 0 && idx < _displayedDevices.Count)
        {
            ViewModel.SetOutputDeviceCommand.Execute(_displayedDevices[idx]);
            RefreshDeviceList();
            BindUI();
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshAllCommand.Execute(null);
        RefreshDeviceList();
        BindUI();
    }



    // ══════════════════════════════════════════
    //  PRESETS
    // ══════════════════════════════════════════

    private void BuildPresetButtons()
    {
        PresetButtonsPanel.Children.Clear();
        var presets = App.ProfileManager.Settings.Presets;
        _activePreset = App.ProfileManager.Settings.SelectedPresetIndex;

        for (int i = 0; i < presets.Count; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Content = presets[i].Name,
                Tag = idx,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Padding = new Thickness(14, 0, 14, 0),
                BorderThickness = new Thickness(0),
                Background = i == _activePreset ? PresetActiveBg : PresetInactiveBg,
                Foreground = i == _activePreset ? PresetActiveFg : PresetInactiveFg,
            };
            btn.Click += OnPresetClick;
            btn.RightTapped += OnPresetRightTapped;
            PresetButtonsPanel.Children.Add(btn);
        }

    }

    private void HighlightPreset(int index)
    {
        _activePreset = index;
        for (int i = 0; i < PresetButtonsPanel.Children.Count; i++)
        {
            if (PresetButtonsPanel.Children[i] is Button btn)
            {
                btn.Background = i == index ? PresetActiveBg : PresetInactiveBg;
                btn.Foreground = i == index ? PresetActiveFg : PresetInactiveFg;
            }
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int index)
        {
            var settings = App.ProfileManager.Settings;
            if (index >= 0 && index < settings.Presets.Count)
            {
                settings.SelectedPresetIndex = index;
                var preset = settings.Presets[index];
                ViewModel.CurrentPresetName = preset.Name;
                HighlightPreset(index);

                _suppressEvents = true;
                ViewModel.Balance = preset.Balance;
                BalanceSlider.Value = preset.Balance;
                UpdateBalanceLabels(preset.Balance);
                ViewModel.DuckingEnabled = preset.DuckingEnabled;
                _suppressEvents = false;

                ViewModel.StatusMessage = $"Preset: {preset.Name}";
            }
        }
    }

    private async void OnAddPresetClick(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "Preset name...",
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
        };

        var dialog = new ContentDialog
        {
            Title = "New Preset",
            Content = nameBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        string name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var preset = new AudioPreset
        {
            Name = name,
            Balance = ViewModel.Balance,
            DuckingEnabled = ViewModel.DuckingEnabled,
            DuckingPercent = App.ProfileManager.Settings.DuckingPercent,
        };

        App.ProfileManager.AddPreset(preset);
        BuildPresetButtons();
        int newIndex = App.ProfileManager.Settings.Presets.Count - 1;
        App.ProfileManager.Settings.SelectedPresetIndex = newIndex;
        HighlightPreset(newIndex);
        ViewModel.CurrentPresetName = name;
        ViewModel.StatusMessage = $"Preset created: {name}";
    }

    private async void OnPresetRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int index) return;
        var settings = App.ProfileManager.Settings;
        if (index < 0 || index >= settings.Presets.Count) return;

        string presetName = settings.Presets[index].Name;

        var dialog = new ContentDialog
        {
            Title = "Delete Preset",
            Content = $"Delete preset \"{presetName}\"?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        App.ProfileManager.DeletePreset(index);
        BuildPresetButtons();
        ViewModel.StatusMessage = $"Preset deleted: {presetName}";
    }

    // ══════════════════════════════════════════
    //  SETTINGS VIEW
    // ══════════════════════════════════════════

    private void OnDuckSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!_settingsVisible)
        {
            _settingsVisible = true;
            MainContent.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
            SettingsIcon.Glyph = "\uE72B";
            TitleText.Text = "Settings";
            LoadSettings();
        }
        // Scroll to ducking section (it's the first card, so scroll to top)
        SettingsPanel.ScrollToVerticalOffset(0);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        _settingsVisible = !_settingsVisible;

        MainContent.Visibility = _settingsVisible ? Visibility.Collapsed : Visibility.Visible;
        SettingsPanel.Visibility = _settingsVisible ? Visibility.Visible : Visibility.Collapsed;
        SettingsIcon.Glyph = _settingsVisible ? "\uE72B" : "\uE713";
        TitleText.Text = _settingsVisible ? "Settings" : "GameBridge";

        if (_settingsVisible)
            LoadSettings();
    }

    private void LoadSettings()
    {
        _suppressEvents = true;
        var settings = App.ProfileManager.Settings;

        // Set slider ranges here (not in XAML) — WinUI 3 XBF reorders attributes causing
        // Minimum/Value assignment failures when Minimum > 0 or Value > default Maximum (1.0).
        SettDuckPercentSlider.Minimum = 10; SettDuckPercentSlider.Maximum = 100;
        SettDuckThresholdSlider.Minimum = 0.001; SettDuckThresholdSlider.Maximum = 0.5;
        // Attack duration: 0.1 s → 1.0 s
        SettFadeInSlider.Minimum = 0.1; SettFadeInSlider.Maximum = 1.0;
        // Release duration: 0.1 s → 1.0 s
        SettAttackSlider.Minimum = 0.1; SettAttackSlider.Maximum = 1.0;

        SettDuckToggle.IsOn = ViewModel.DuckingEnabled;
        SettDuckPercentSlider.Value = settings.DuckingPercent;
        SettDuckThresholdSlider.Value = settings.DuckingThreshold;
        SettDuckPercentLabel.Text = $"{(int)settings.DuckingPercent}%";
        SettDuckThresholdLabel.Text = FormatThreshold(settings.DuckingThreshold);
        float attackDur = Math.Clamp(settings.DuckingAttackDuration, 0.1f, 1.0f);
        SettFadeInSlider.Value = attackDur;
        SettFadeInLabel.Text = FormatDuration(attackDur);
        float releaseDur = Math.Clamp(settings.DuckingReleaseDuration, 0.1f, 1.0f);
        SettAttackSlider.Value = releaseDur;
        SettAttackLabel.Text = FormatDuration(releaseDur);
        SettAutoStartToggle.IsOn = IsAutoStartRegistered();

        BuildSettingsHotkeyUI(settings.Hotkeys);

        var devices = App.DeviceManager?.GetOutputDevices() ?? [];
        SettDeviceList.ItemsSource = devices;
        for (int i = 0; i < devices.Count; i++)
        {
            if (settings.PreferredDeviceIds.Contains(devices[i].Id))
                SettDeviceList.SelectedItems.Add(devices[i]);
        }

        _suppressEvents = false;
        UpdateThresholdMarker();

        // Thumbs need ActualWidth, which is only valid after layout. Use Loaded/SizeChanged once.
        void RefreshSettThumbs()
        {
            UpdateSettSliderThumb(SettDuckPercentSlider, SettDuckPercentThumb, SettDuckPercentFill);
            UpdateSettSliderThumb(SettDuckThresholdSlider, SettDuckThresholdThumb, SettDuckThresholdFill);
            UpdateSettSliderThumb(SettFadeInSlider, SettFadeInThumb, SettFadeInFill);
            UpdateSettSliderThumb(SettAttackSlider, SettAttackThumb, SettAttackFill);
        }

        if (SettDuckPercentSlider.ActualWidth > 0)
        {
            RefreshSettThumbs();
        }
        else
        {
            void OnLoaded(object s, RoutedEventArgs _) { RefreshSettThumbs(); SettDuckPercentSlider.Loaded -= OnLoaded; }
            SettDuckPercentSlider.Loaded += OnLoaded;
        }

        SettDuckPercentSlider.SizeChanged += (_, _) =>
            UpdateSettSliderThumb(SettDuckPercentSlider, SettDuckPercentThumb, SettDuckPercentFill);
        SettDuckThresholdSlider.SizeChanged += (_, _) =>
            UpdateSettSliderThumb(SettDuckThresholdSlider, SettDuckThresholdThumb, SettDuckThresholdFill);
        SettFadeInSlider.SizeChanged += (_, _) =>
            UpdateSettSliderThumb(SettFadeInSlider, SettFadeInThumb, SettFadeInFill);
        SettAttackSlider.SizeChanged += (_, _) =>
            UpdateSettSliderThumb(SettAttackSlider, SettAttackThumb, SettAttackFill);

    }

    // Slider values are directly in seconds — display as e.g. "0.3s"
    private static string FormatDuration(double seconds) => $"{seconds:0.00}s";

    private static string FormatThreshold(double threshold) =>
        ((int)Math.Round(threshold * 100)).ToString();

    private void OnMasterVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;
        int pct = (int)e.NewValue;
        MasterVolText.Text = $"{pct}%";
        UpdateMasterThumbPosition(pct);
        ViewModel?.ApplyMasterVolume(pct);
    }

    private void UpdateMasterThumbPosition(int pct)
    {
        if (MasterThumb == null) return;
        if (_cachedMasterWidth <= 0)
            _cachedMasterWidth = (MasterVolumeSlider?.ActualWidth ?? 0) - 8;
        if (_cachedMasterWidth <= 0) return;
        double ratio = pct / 100.0;
        double offset = Math.Clamp(ratio * _cachedMasterWidth, 0, _cachedMasterWidth);
        MasterThumb.Margin = new Thickness(offset - 1.5, 0, 0, 0);
        if (MasterFillTrack != null)
            MasterFillTrack.Width = offset;
    }

    private static void UpdateSettSliderThumb(Slider slider, Border thumb, Border fill)
    {
        // Slider has Margin="-4,0" so ActualWidth is 8px wider than the visible track.
        double trackWidth = slider.ActualWidth - 8;
        if (trackWidth <= 0) return;
        double min = slider.Minimum, max = slider.Maximum;
        if (max <= min) return;
        double ratio = (slider.Value - min) / (max - min);
        double offset = Math.Clamp(ratio * trackWidth, 0, trackWidth);
        thumb.Margin = new Thickness(offset - 1.5, 0, 0, 0);
        fill.Width = offset;
    }

    private void OnSettDuckPercentChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (SettDuckPercentLabel != null)
            SettDuckPercentLabel.Text = $"{(int)e.NewValue}%";
        UpdateSettSliderThumb(SettDuckPercentSlider, SettDuckPercentThumb, SettDuckPercentFill);
        ApplyAndSaveSettings();
    }

    private void OnSettDuckThresholdChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (SettDuckThresholdLabel != null)
            SettDuckThresholdLabel.Text = FormatThreshold(e.NewValue);
        UpdateSettSliderThumb(SettDuckThresholdSlider, SettDuckThresholdThumb, SettDuckThresholdFill);
        UpdateThresholdMarker();
        ApplyAndSaveSettings();
    }

    private void OnSettFadeInChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (SettFadeInLabel != null)
            SettFadeInLabel.Text = FormatDuration(e.NewValue);
        UpdateSettSliderThumb(SettFadeInSlider, SettFadeInThumb, SettFadeInFill);
        ApplyAndSaveSettings();
    }

    private void OnSettAttackChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (SettAttackLabel != null)
            SettAttackLabel.Text = FormatDuration(e.NewValue);
        UpdateSettSliderThumb(SettAttackSlider, SettAttackThumb, SettAttackFill);
        ApplyAndSaveSettings();
    }

    // Fired by SettDuckToggle and SettAutoStartToggle
    private void OnSettingsControlChanged(object sender, RoutedEventArgs e)
    {
        ApplyAndSaveSettings();
    }

    // Fired by SettDeviceList selection changes
    private void OnSettDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        ApplyAndSaveSettings();
        RefreshDeviceList();
    }

    private void UpdateThresholdMarker()
    {
        double w = ChatPeakTrack?.ActualWidth ?? 0;
        if (w <= 0) return;
        if (ThresholdMarker == null) return;
        double threshold = SettDuckThresholdSlider?.Value ?? 0.01;
        // Same sqrt scale as the peak ProgressBar (0–1 range) so the marker aligns correctly
        double ratio = Math.Sqrt(Math.Min(threshold, 1.0));
        ThresholdMarker.Margin = new Thickness(ratio * w, 0, 0, 0);
    }

    private void BuildSettingsHotkeyUI(List<KeyBinding> hotkeys)
    {
        SettHotkeyPanel.Children.Clear();

        var conflicts = new HashSet<string>(
            App.HotkeyManager?.RegistrationErrors
                .Select(e => e.Split("->").FirstOrDefault()?.Trim().Replace("FAILED: ", "") ?? "")
            ?? []);

        for (int i = 0; i < hotkeys.Count; i++)
        {
            var binding = hotkeys[i];
            if (binding.Action == HotkeyAction.TogglePreset) continue;
            string label = ActionLabels.TryGetValue(binding.Action.ToString(), out var l)
                ? l : binding.Action.ToString();

            bool isConflict = conflicts.Contains(binding.DisplayName);

            var row = new Grid
            {
                ColumnSpacing = 10,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(isConflict
                    ? ColorHelper.FromArgb(0x10, 0xFF, 0x44, 0x55)
                    : ColorHelper.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(4),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center };
            labelStack.Children.Add(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Opacity = 0.55,
            });
            if (isConflict)
            {
                labelStack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(ColorHelper.FromArgb(0x22, 0xFF, 0x44, 0x55)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "CONFLICT",
                        FontSize = 8,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0xFF, 0x66, 0x66)),
                        CharacterSpacing = 80,
                    }
                });
            }
            Grid.SetColumn(labelStack, 0);

            var keyBtn = new Button
            {
                Content = binding.DisplayName,
                Tag = i,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 10,
                MinWidth = 140,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(isConflict
                    ? ColorHelper.FromArgb(0x18, 0xFF, 0x44, 0x55)
                    : ColorHelper.FromArgb(0x10, 0x00, 0xCF, 0xFF)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x20, 0x00, 0xCF, 0xFF)),
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(0x80, 0x00, 0xCF, 0xFF)),
            };
            keyBtn.Click += OnHotkeyButtonClick;
            keyBtn.KeyDown += OnHotkeyKeyDown;
            Grid.SetColumn(keyBtn, 1);

            row.Children.Add(labelStack);
            row.Children.Add(keyBtn);
            SettHotkeyPanel.Children.Add(row);
        }
    }

    private void OnHotkeyButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (_listeningButton != null && _listeningButton != btn)
        {
            int prevIdx = (int)_listeningButton.Tag;
            _listeningButton.Content = App.ProfileManager.Settings.Hotkeys[prevIdx].DisplayName;
            _listeningButton.Background = null;
        }

        _listeningButton = btn;
        _listeningIndex = (int)btn.Tag;
        btn.Content = "Press keys...";
        btn.Background = new SolidColorBrush(ColorHelper.FromArgb(0x30, 0x00, 0xCF, 0xFF));
        btn.Focus(FocusState.Programmatic);
    }

    private void OnHotkeyKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_listeningButton == null || _listeningIndex < 0) return;

        var key = e.Key;

        if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return;
        }

        if (key == VirtualKey.Escape)
        {
            var binding = App.ProfileManager.Settings.Hotkeys[_listeningIndex];
            _listeningButton.Content = binding.DisplayName;
            _listeningButton.Background = null;
            _listeningButton = null;
            _listeningIndex = -1;
            e.Handled = true;
            return;
        }

        uint mods = 0;
        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if (ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            mods |= Win32.MOD_CONTROL;
        var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            mods |= Win32.MOD_SHIFT;
        var altState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        if (altState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            mods |= Win32.MOD_ALT;

        if (mods == 0) { e.Handled = true; return; }

        uint vk = (uint)key;

        var settings = App.ProfileManager.Settings;
        if (_listeningIndex < settings.Hotkeys.Count)
        {
            settings.Hotkeys[_listeningIndex].Modifiers = mods;
            settings.Hotkeys[_listeningIndex].VirtualKey = vk;
            _listeningButton.Content = settings.Hotkeys[_listeningIndex].DisplayName;
        }

        _listeningButton.Background = null;
        _listeningButton = null;
        _listeningIndex = -1;
        e.Handled = true;
    }

    private void OnResetHotkeysClick(object sender, RoutedEventArgs e)
    {
        App.ProfileManager.Settings.Hotkeys = KeyBinding.GetDefaults();
        BuildSettingsHotkeyUI(App.ProfileManager.Settings.Hotkeys);
    }


    private void ApplyAndSaveSettings()
    {
        if (_suppressEvents) return;
        var settings = App.ProfileManager.Settings;

        settings.AutoDuckingEnabled = SettDuckToggle.IsOn;
        settings.DuckingPercent = (float)SettDuckPercentSlider.Value;
        settings.DuckingThreshold = (float)SettDuckThresholdSlider.Value;
        settings.DuckingAttackDuration = (float)SettFadeInSlider.Value;
        settings.DuckingReleaseDuration = (float)SettAttackSlider.Value;
        settings.AutoStartEnabled = SettAutoStartToggle.IsOn;

        settings.PreferredDeviceIds.Clear();
        foreach (var item in SettDeviceList.SelectedItems)
        {
            if (item is Core.AudioDeviceInfo dev)
                settings.PreferredDeviceIds.Add(dev.Id);
        }

        App.ProfileManager.Save();

        ViewModel.DuckingEnabled = settings.AutoDuckingEnabled;
        if (App.DuckingService != null)
        {
            App.DuckingService.DuckPercent = settings.DuckingPercent;
            App.DuckingService.ActivationThreshold = settings.DuckingThreshold;
            App.DuckingService.AttackDurationMs = settings.DuckingAttackDuration * 1000f;
            App.DuckingService.ReleaseDurationMs = settings.DuckingReleaseDuration * 1000f;
        }

        App.HotkeyManager?.RegisterBindings(settings.Hotkeys);
        SetAutoStart(settings.AutoStartEnabled);
    }

    private static bool IsAutoStartRegistered()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("GameBridge") != null;
    }

    private static void SetAutoStart(bool enabled)
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "GameBridge";

        int result = Win32.RegOpenKeyEx(Win32.HKEY_CURRENT_USER, runKey, 0, Win32.KEY_SET_VALUE, out var hKey);
        if (result != 0) return;

        try
        {
            if (enabled)
            {
                string exePath = Environment.ProcessPath ?? "";
                byte[] data = Encoding.Unicode.GetBytes(exePath + "\0");
                Win32.RegSetValueEx(hKey, appName, 0, Win32.REG_SZ, data, (uint)data.Length);
            }
            else
            {
                Win32.RegDeleteValue(hKey, appName);
            }
        }
        finally
        {
            Win32.RegCloseKey(hKey);
        }
    }
}
