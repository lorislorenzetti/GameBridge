using GameAudioMixer.Core.Interop;
using GameAudioMixer.Views;
using Microsoft.UI.Dispatching;

namespace GameAudioMixer.Services;

public sealed class HudService
{
    private HudWindow? _hud;
    private readonly DispatcherQueue _dispatcher;
    private bool _initialized;

    public HudService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Call once at startup to pre-create the HUD window invisibly.
    /// We hide at Win32 level before Activate() to prevent the brief flash.
    /// </summary>
    public void PreCreateWindow()
    {
        if (_initialized) return;
        _initialized = true;

        _dispatcher.TryEnqueue(() =>
        {
            _hud = new HudWindow();
            // Hide at Win32 level before WinUI shows it via Activate()
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_hud);
            Win32.ShowWindow(hwnd, 0); // SW_HIDE
            _hud.Activate();
            _hud.ConfigureAsOverlay();
            _hud.HideNoActivate();
        });
    }

    private HudWindow? GetWindow()
    {
        if (_hud == null && !_initialized)
        {
            _hud = new HudWindow();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_hud);
            Win32.ShowWindow(hwnd, 0); // SW_HIDE
            _hud.Activate();
            _hud.ConfigureAsOverlay();
            _initialized = true;
        }
        return _hud;
    }

    public void ShowBalance(int balance, string? gameName, string? chatName)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var hud = GetWindow();
            if (hud == null) return;
            hud.ShowBalance(balance, gameName, chatName);
            hud.ShowNoActivate();
        });
    }

    public void ShowPreset(string presetName, int balance)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var hud = GetWindow();
            if (hud == null) return;
            hud.ShowPreset(presetName, balance);
            hud.ShowNoActivate();
        });
    }

    public void ShowMic(bool muted)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var hud = GetWindow();
            if (hud == null) return;
            hud.ShowMic(muted);
            hud.ShowNoActivate();
        });
    }

    public void ShowMuteGame(bool muted, string? gameName)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var hud = GetWindow();
            if (hud == null) return;
            hud.ShowMuteGame(muted, gameName);
            hud.ShowNoActivate();
        });
    }

    public void ShowMuteChat(bool muted, string? chatName)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var hud = GetWindow();
            if (hud == null) return;
            hud.ShowMuteChat(muted, chatName);
            hud.ShowNoActivate();
        });
    }

    public void ShowDevice(string deviceName)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var hud = GetWindow();
            if (hud == null) return;
            hud.ShowDevice(deviceName);
            hud.ShowNoActivate();
        });
    }

    public void ShowDucking(bool enabled)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var hud = GetWindow();
            if (hud == null) return;
            hud.ShowDucking(enabled);
            hud.ShowNoActivate();
        });
    }
}
