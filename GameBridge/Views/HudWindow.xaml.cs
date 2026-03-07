using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using GameAudioMixer.Core.Interop;
using GameAudioMixer.ViewModels;

namespace GameAudioMixer.Views;

public sealed partial class HudWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _topmostTimer;
    private IntPtr _hwnd;

    private const int LWA_ALPHA = 0x2;

    // Sizes in logical pixels
    private const int W = 265;
    private const int H_BALANCE = 120;
    private const int H_STATUS = 90;

    public HudWindow()
    {
        InitializeComponent();
        Title = "";

        try { SystemBackdrop = new DesktopAcrylicBackdrop(); } catch { }

        // Re-assert topmost every 50 ms so fullscreen-borderless games can't push us down
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _topmostTimer.Tick += (_, _) => ForceTopmost();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _topmostTimer.Stop();
            HideNoActivate();
        };
    }

    public void ConfigureAsOverlay()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Activate() may have shown the window — hide again before making style changes
        // so DWM never composites a visible frame during reconfiguration.
        Win32.ShowWindow(_hwnd, 0); // SW_HIDE

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new Windows.Graphics.SizeInt32(W, H_STATUS));

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        ExtendsContentIntoTitleBar = true;

        int style = Win32.WS_POPUP;
        Win32.SetWindowLong(_hwnd, Win32.GWL_STYLE, style);

        int exStyle = Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW
                    | Win32.WS_EX_LAYERED | Win32.WS_EX_NOACTIVATE
                    | Win32.WS_EX_TRANSPARENT;
        Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, exStyle);

        // Start fully transparent (alpha=0) — opacity is restored in ShowNoActivate()
        // so even if Windows/DWM briefly renders the window it remains invisible.
        Win32.SetLayeredWindowAttributes(_hwnd, 0, 0, LWA_ALPHA);

        PlaceWindow(W, H_STATUS);
    }

    // ── Win32 positioning ────────────────────────────────────────────────────

    private void PlaceWindow(int w, int h)
    {
        if (_hwnd == IntPtr.Zero) return;
        // No SWP_SHOWWINDOW here — window is shown explicitly via ShowNoActivate()
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST,
            28, 28, w, h,
            Win32.SWP_NOACTIVATE | Win32.SWP_HIDEWINDOW);
    }

    private void ForceTopmost()
    {
        if (_hwnd == IntPtr.Zero) return;
        // Two-step: HWND_TOP first, then HWND_TOPMOST — helps with borderless-fullscreen games
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOP,
            0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST,
            0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    public void ShowNoActivate()
    {
        if (_hwnd == IntPtr.Zero) return;
        Win32.SetLayeredWindowAttributes(_hwnd, 0, 210, LWA_ALPHA); // Restore visible opacity
        Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE);
        ForceTopmost();
    }

    public void HideNoActivate()
    {
        if (_hwnd == IntPtr.Zero) return;
        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        Win32.SetLayeredWindowAttributes(_hwnd, 0, 0, LWA_ALPHA); // Reset to transparent
    }

    private void ResetTimer()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
        if (!_topmostTimer.IsEnabled)
            _topmostTimer.Start();
        ShowNoActivate();
    }

    // ── Resize helper ────────────────────────────────────────────────────────

    private void ResizeTo(int h)
    {
        if (_hwnd == IntPtr.Zero) return;
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        AppWindow.GetFromWindowId(windowId).Resize(new Windows.Graphics.SizeInt32(W, h));
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST,
            28, 28, W, h, Win32.SWP_NOACTIVATE);
    }

    // ── Accent / border colour helpers ──────────────────────────────────────

    private void SetAccent(byte r, byte g, byte b)
    {
        AccentBar.Background = new SolidColorBrush(ColorHelper.FromArgb(0xCC, r, g, b));
        CardBorder.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x35, r, g, b));
    }

    private void SetCyanAccent()   => SetAccent(0x00, 0xCF, 0xFF);
    private void SetVioletAccent() => SetAccent(0xB4, 0x4F, 0xFF);
    private void SetRedAccent()    => SetAccent(0xFF, 0x44, 0x55);
    private void SetGreenAccent()  => SetAccent(0x00, 0xE8, 0x7A);
    private void SetAmberAccent()  => SetAccent(0xFB, 0xBF, 0x24);

    // ── Balance thumb positioning ────────────────────────────────────────────
    // Uses the same 3-column star trick as MainWindow:
    //   ThumbLeft  = balance * star
    //   ThumbRight = (200 - balance) * star
    // balance=0   → thumb all the way left  (full game)
    // balance=100 → thumb centre            (equal mix)
    // balance=200 → thumb all the way right (full chat)

    private void ApplyBalanceThumb(int balance)
    {
        double left  = Math.Max(0.01, balance);
        double right = Math.Max(0.01, 200 - balance);
        ThumbLeft.Width  = new GridLength(left,  GridUnitType.Star);
        ThumbRight.Width = new GridLength(right, GridUnitType.Star);

        // Track colouring mirrors main window: left cyan grows with balance ratio
        double ratio = balance / 200.0;
        GameTrackCol.Width = new GridLength(Math.Max(0.01, ratio),       GridUnitType.Star);
        ChatTrackCol.Width = new GridLength(Math.Max(0.01, 1.0 - ratio), GridUnitType.Star);
    }

    // ── Public show methods ──────────────────────────────────────────────────

    public void ShowBalance(int balance, string? gameName, string? chatName)
    {
        SetCyanAccent();
        ActionIcon.Glyph = "\uE9E9";
        ActionIcon.Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0x00, 0xCF, 0xFF));
        ActionTitle.Text = "MIX";
        ActionSubtitle.Text = "";

        int gameVol = (int)Math.Round(MainViewModel.BalanceToGameVol(balance) * 100);
        int chatVol = (int)Math.Round(MainViewModel.BalanceToChatVol(balance) * 100);
        GamePercent.Text = $"{gameVol}";
        ChatPercent.Text = $"{chatVol}";

        ApplyBalanceThumb(balance);

        BalancePanel.Visibility = Visibility.Visible;
        LargeStatus.Visibility  = Visibility.Collapsed;
        ResizeTo(H_BALANCE);
        ResetTimer();
    }

    public void ShowPreset(string presetName, int balance)
    {
        SetAmberAccent();
        ActionIcon.Glyph = "\uE7F6";
        ActionIcon.Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFB, 0xBF, 0x24));
        ActionTitle.Text = "PRESET";
        ActionSubtitle.Text = presetName;

        int gameVol = (int)Math.Round(MainViewModel.BalanceToGameVol(balance) * 100);
        int chatVol = (int)Math.Round(MainViewModel.BalanceToChatVol(balance) * 100);
        GamePercent.Text = $"{gameVol}";
        ChatPercent.Text = $"{chatVol}";

        ApplyBalanceThumb(balance);

        BalancePanel.Visibility = Visibility.Visible;
        LargeStatus.Visibility  = Visibility.Collapsed;
        ResizeTo(H_BALANCE);
        ResetTimer();
    }

    public void ShowMic(bool muted)
    {
        if (muted) SetRedAccent(); else SetGreenAccent();
        var color = muted
            ? ColorHelper.FromArgb(0xFF, 0xFF, 0x44, 0x55)
            : ColorHelper.FromArgb(0xFF, 0x00, 0xE8, 0x7A);
        ActionIcon.Glyph = muted ? "\uF781" : "\uE720";
        ActionIcon.Foreground = new SolidColorBrush(color);
        ActionTitle.Text = "MICROPHONE";
        ActionSubtitle.Text = "";

        LargeStatus.Text       = muted ? "MUTED" : "ACTIVE";
        LargeStatus.Foreground = new SolidColorBrush(color);
        LargeStatus.FontSize   = 18;
        LargeStatus.Visibility  = Visibility.Visible;
        BalancePanel.Visibility = Visibility.Collapsed;
        ResizeTo(H_STATUS);
        ResetTimer();
    }

    public void ShowMuteGame(bool muted, string? gameName)
    {
        if (muted) SetRedAccent(); else SetCyanAccent();
        var color = muted
            ? ColorHelper.FromArgb(0xFF, 0xFF, 0x44, 0x55)
            : ColorHelper.FromArgb(0xCC, 0x00, 0xCF, 0xFF);
        ActionIcon.Glyph = muted ? "\uE74F" : "\uE767";
        ActionIcon.Foreground = new SolidColorBrush(color);
        ActionTitle.Text = "GAME";
        ActionSubtitle.Text = gameName ?? "";

        LargeStatus.Text       = muted ? "MUTED" : "ACTIVE";
        LargeStatus.Foreground = new SolidColorBrush(color);
        LargeStatus.FontSize   = 18;
        LargeStatus.Visibility  = Visibility.Visible;
        BalancePanel.Visibility = Visibility.Collapsed;
        ResizeTo(H_STATUS);
        ResetTimer();
    }

    public void ShowMuteChat(bool muted, string? chatName)
    {
        if (muted) SetRedAccent(); else SetVioletAccent();
        var color = muted
            ? ColorHelper.FromArgb(0xFF, 0xFF, 0x44, 0x55)
            : ColorHelper.FromArgb(0xCC, 0xB4, 0x4F, 0xFF);
        ActionIcon.Glyph = muted ? "\uE74F" : "\uE767";
        ActionIcon.Foreground = new SolidColorBrush(color);
        ActionTitle.Text = "CHAT";
        ActionSubtitle.Text = chatName ?? "";

        LargeStatus.Text       = muted ? "MUTED" : "ACTIVE";
        LargeStatus.Foreground = new SolidColorBrush(color);
        LargeStatus.FontSize   = 18;
        LargeStatus.Visibility  = Visibility.Visible;
        BalancePanel.Visibility = Visibility.Collapsed;
        ResizeTo(H_STATUS);
        ResetTimer();
    }

    public void ShowDucking(bool enabled)
    {
        if (enabled) SetCyanAccent(); else SetRedAccent();
        var color = enabled
            ? ColorHelper.FromArgb(0xFF, 0x00, 0xCF, 0xFF)
            : ColorHelper.FromArgb(0xFF, 0xFF, 0x44, 0x55);
        ActionIcon.Glyph = "\uE994"; // volume duck icon
        ActionIcon.Foreground = new SolidColorBrush(color);
        ActionTitle.Text = "AUTO-DUCKING";
        ActionSubtitle.Text = "";

        LargeStatus.Text       = enabled ? "ON" : "OFF";
        LargeStatus.Foreground = new SolidColorBrush(color);
        LargeStatus.FontSize   = 18;
        LargeStatus.Visibility  = Visibility.Visible;
        BalancePanel.Visibility = Visibility.Collapsed;
        ResizeTo(H_STATUS);
        ResetTimer();
    }

    public void ShowDevice(string deviceName)
    {
        SetAmberAccent();
        ActionIcon.Glyph = "\uE7F5";
        ActionIcon.Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFB, 0xBF, 0x24));
        ActionTitle.Text = "OUTPUT";
        ActionSubtitle.Text = "";

        LargeStatus.Text       = deviceName;
        LargeStatus.Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xEE, 0xFF, 0xFF, 0xFF));
        LargeStatus.FontSize   = 13;
        LargeStatus.Visibility  = Visibility.Visible;
        BalancePanel.Visibility = Visibility.Collapsed;
        ResizeTo(H_STATUS);
        ResetTimer();
    }
}
