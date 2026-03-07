using System.Text;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using GameAudioMixer.Core.Interop;
using GameAudioMixer.Hotkeys;
using GameAudioMixer.Models;
using Windows.System;

namespace GameAudioMixer.Views;

public sealed partial class SettingsWindow : Window
{
    private Button? _listeningButton;
    private int _listeningIndex = -1;
    private readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private double _monitorWidth;

    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        [nameof(HotkeyAction.BalanceToGame)] = "Balance → Game",
        [nameof(HotkeyAction.BalanceToChat)] = "Balance → Chat",
        [nameof(HotkeyAction.TogglePreset)] = "Cycle Preset",
        [nameof(HotkeyAction.SwitchOutputDevice)] = "Switch Device",
        [nameof(HotkeyAction.ToggleMicMute)] = "Mute Microphone",
        [nameof(HotkeyAction.MuteGame)] = "Mute Game",
        [nameof(HotkeyAction.MuteChat)] = "Mute Chat",
        [nameof(HotkeyAction.GameVolumeUp)] = "Volume Game +",
        [nameof(HotkeyAction.GameVolumeDown)] = "Volume Game -",
        [nameof(HotkeyAction.ChatVolumeUp)] = "Volume Chat +",
        [nameof(HotkeyAction.ChatVolumeDown)] = "Volume Chat -",
    };

    public SettingsWindow()
    {
        _monitorTimer.Tick += OnMonitorTick;

        InitializeComponent();
        Title = "Settings";

        TrySetMicaBackdrop();
        SetupTitleBar();
        SetWindowSize(520, 850);
        LoadSettings();

        ChatPeakBar.SizeChanged += (_, _) => { };
        ChatPeakBar.Loaded += (_, _) =>
        {
            if (ChatPeakBar.Parent is Grid g)
            {
                _monitorWidth = g.ActualWidth;
                g.SizeChanged += (_, _) => _monitorWidth = g.ActualWidth;
            }
            UpdateThresholdMarker();
        };

        _monitorTimer.Start();
        Closed += (_, _) => _monitorTimer.Stop();
    }

    private void TrySetMicaBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            else if (DesktopAcrylicController.IsSupported())
                SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch { }
    }

    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(SettingsTitleBar);
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

    private void LoadSettings()
    {
        var settings = App.ProfileManager.Settings;

        DuckingToggle.IsOn = settings.AutoDuckingEnabled;
        DuckPercentSlider.Value = settings.DuckingPercent;
        DuckThresholdSlider.Value = settings.DuckingThreshold;
        DuckPercentLabel.Text = $"{(int)settings.DuckingPercent}%";
        DuckThresholdLabel.Text = $"{settings.DuckingThreshold:F3}";
        AutoStartToggle.IsOn = IsAutoStartRegistered();

        BuildHotkeyUI(settings.Hotkeys);
        ProfileList.ItemsSource = settings.GameProfiles;

        var devices = App.DeviceManager?.GetOutputDevices() ?? [];
        PreferredDeviceList.ItemsSource = devices;

        for (int i = 0; i < devices.Count; i++)
        {
            if (settings.PreferredDeviceIds.Contains(devices[i].Id))
                PreferredDeviceList.SelectedItems.Add(devices[i]);
        }
    }

    private void BuildHotkeyUI(List<KeyBinding> hotkeys)
    {
        HotkeyPanel.Children.Clear();

        for (int i = 0; i < hotkeys.Count; i++)
        {
            var binding = hotkeys[i];
            string label = ActionLabels.TryGetValue(binding.Action.ToString(), out var l)
                ? l : binding.Action.ToString();

            var row = new Grid
            {
                ColumnSpacing = 10,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(ColorHelper.FromArgb(0x06, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(10),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var actionText = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Opacity = 0.7,
            };
            Grid.SetColumn(actionText, 0);

            var keyBtn = new Button
            {
                Content = binding.DisplayName,
                Tag = i,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 11,
                MinWidth = 140,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5, 10, 5),
                Background = new SolidColorBrush(ColorHelper.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            };
            keyBtn.Click += OnHotkeyButtonClick;
            keyBtn.KeyDown += OnHotkeyKeyDown;
            Grid.SetColumn(keyBtn, 1);

            row.Children.Add(actionText);
            row.Children.Add(keyBtn);
            HotkeyPanel.Children.Add(row);
        }
    }

    private void OnDuckPercentChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (DuckPercentLabel != null)
            DuckPercentLabel.Text = $"{(int)e.NewValue}%";
    }

    private void OnDuckThresholdChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (DuckThresholdLabel != null)
            DuckThresholdLabel.Text = $"{e.NewValue:F3}";
        UpdateThresholdMarker();
    }

    private void OnMonitorTick(object? sender, object e)
    {
        var duck = App.DuckingService;
        if (duck == null || _monitorWidth <= 0) return;

        float peak = duck.LastChatPeak;
        float clampedPeak = Math.Min(peak, 0.15f);
        double peakRatio = clampedPeak / 0.15;
        ChatPeakBar.Width = Math.Max(0, peakRatio * _monitorWidth);

        bool active = duck.IsDucked;
        ChatPeakBar.Background = new SolidColorBrush(active
            ? ColorHelper.FromArgb(0x90, 0x8B, 0x5C, 0xF6)
            : ColorHelper.FromArgb(0x40, 0x8B, 0x5C, 0xF6));

        DuckStateText.Text = active ? "DUCKING" : "IDLE";
        DuckStateBadge.Background = new SolidColorBrush(active
            ? ColorHelper.FromArgb(0x20, 0xFB, 0xBF, 0x24)
            : ColorHelper.FromArgb(0x15, 0xEF, 0x44, 0x44));
        DuckStateText.Foreground = new SolidColorBrush(active
            ? ColorHelper.FromArgb(0xFF, 0xFB, 0xBF, 0x24)
            : ColorHelper.FromArgb(0xFF, 0xEF, 0x44, 0x44));

        ChatPeakValue.Text = $"peak: {peak:F3}";
        ThresholdValue.Text = $"threshold: {duck.ActivationThreshold:F3}";

        float baseVol = ViewModels.MainViewModel.BalanceToGameVol(App.MainViewModel.Balance);
        float gameVol = baseVol * duck.DuckFactor;
        GameVolBar.Width = Math.Max(0, duck.DuckFactor * _monitorWidth);
        GameVolValue.Text = $"{(int)(gameVol * 100)}%";
    }

    private void UpdateThresholdMarker()
    {
        if (ThresholdMarker == null || _monitorWidth <= 0) return;
        double threshold = DuckThresholdSlider?.Value ?? 0.01;
        double ratio = Math.Min(threshold / 0.15, 1.0);
        ThresholdMarker.Margin = new Thickness(ratio * _monitorWidth, 0, 0, 0);
    }

    private void OnHotkeyButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        // Cancel previous listening
        if (_listeningButton != null && _listeningButton != btn)
        {
            int prevIdx = (int)_listeningButton.Tag;
            _listeningButton.Content = App.ProfileManager.Settings.Hotkeys[prevIdx].DisplayName;
            _listeningButton.Background = null;
        }

        _listeningButton = btn;
        _listeningIndex = (int)btn.Tag;
        btn.Content = "Press keys...";
        btn.Background = new SolidColorBrush(ColorHelper.FromArgb(0x30, 0x60, 0xA5, 0xFA));
        btn.Focus(FocusState.Programmatic);
    }

    private void OnHotkeyKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_listeningButton == null || _listeningIndex < 0) return;

        var key = e.Key;

        // Ignore modifier-only presses
        if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return;
        }

        // Escape cancels
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

        // Build modifiers from CoreWindow state
        uint mods = 0;
        var coreWin = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if (coreWin.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            mods |= Win32.MOD_CONTROL;
        var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            mods |= Win32.MOD_SHIFT;
        var altState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        if (altState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            mods |= Win32.MOD_ALT;

        // Require at least one modifier
        if (mods == 0)
        {
            e.Handled = true;
            return;
        }

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
        BuildHotkeyUI(App.ProfileManager.Settings.Hotkeys);
    }

    private void OnPreferredDevicesChanged(object sender, SelectionChangedEventArgs e) { }
    private void OnAutoStartToggled(object sender, RoutedEventArgs e) { }

    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string exeName)
        {
            App.ProfileManager.DeleteGameProfile(exeName);
            ProfileList.ItemsSource = null;
            ProfileList.ItemsSource = App.ProfileManager.Settings.GameProfiles;
        }
    }

    private void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        var settings = App.ProfileManager.Settings;

        settings.AutoDuckingEnabled = DuckingToggle.IsOn;
        settings.DuckingPercent = (float)DuckPercentSlider.Value;
        settings.DuckingThreshold = (float)DuckThresholdSlider.Value;
        settings.AutoStartEnabled = AutoStartToggle.IsOn;

        settings.PreferredDeviceIds.Clear();
        foreach (var item in PreferredDeviceList.SelectedItems)
        {
            if (item is Core.AudioDeviceInfo dev)
                settings.PreferredDeviceIds.Add(dev.Id);
        }

        App.ProfileManager.Save();

        if (App.DuckingService != null)
        {
            App.DuckingService.Enabled = settings.AutoDuckingEnabled;
            App.DuckingService.DuckPercent = settings.DuckingPercent;
            App.DuckingService.ActivationThreshold = settings.DuckingThreshold;
        }

        App.HotkeyManager?.RegisterBindings(settings.Hotkeys);

        SetAutoStart(settings.AutoStartEnabled);

        App.MainViewModel.StatusMessage = "Settings saved";
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
