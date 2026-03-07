using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GameAudioMixer.Core;
using GameAudioMixer.Hotkeys;
using GameAudioMixer.Profiles;
using GameAudioMixer.Services;
using GameAudioMixer.ViewModels;
using GameAudioMixer.Views;

namespace GameAudioMixer;

public partial class App : Application
{
    public static DeviceManager? DeviceManager { get; private set; }
    public static SessionManager? SessionManager { get; private set; }
    public static AudioManager? AudioManager { get; private set; }
    public static HotkeyManager? HotkeyManager { get; private set; }
    public static ProfileManager ProfileManager { get; private set; } = null!;
    public static ForegroundProcessService? ForegroundService { get; private set; }
    public static DiscordDetectionService? DiscordService { get; private set; }
    public static DuckingService? DuckingService { get; private set; }
    public static MainViewModel MainViewModel { get; private set; } = null!;

    private MainWindow? _mainWindow;
    private static void Log(string msg) => Debug.WriteLine($"[GameBridge] {msg}");

    public App()
    {
        Log("App constructor start");
        InitializeComponent();
        Log("InitializeComponent done");
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched start");

        try
        {
            Log("Creating ProfileManager...");
            ProfileManager = new ProfileManager();
            Log("ProfileManager created");
        }
        catch (Exception ex) { Log($"ProfileManager FAILED: {ex.Message}"); }

        try
        {
            Log("Creating DeviceManager...");
            DeviceManager = new DeviceManager();
            Log("DeviceManager created");
        }
        catch (Exception ex) { Log($"DeviceManager FAILED: {ex.Message}"); }

        if (DeviceManager != null)
        {
            try
            {
                Log("Creating SessionManager...");
                SessionManager = new SessionManager(DeviceManager);
                AudioManager = new AudioManager(DeviceManager, SessionManager);
                DiscordService = new DiscordDetectionService(SessionManager);
                DuckingService = new DuckingService();
                Log("Audio services created");
            }
            catch (Exception ex) { Log($"Audio services FAILED: {ex.Message}"); }
        }

        try
        {
            Log("Creating HotkeyManager...");
            HotkeyManager = new HotkeyManager();
            Log("HotkeyManager created");
        }
        catch (Exception ex) { Log($"HotkeyManager FAILED: {ex.Message}"); }

        Log("Creating ForegroundService...");
        ForegroundService = new ForegroundProcessService();
        Log("ForegroundService created");

        Log("Creating MainViewModel...");
        MainViewModel = new MainViewModel(
            AudioManager, DeviceManager, SessionManager,
            HotkeyManager, ProfileManager, ForegroundService,
            DiscordService, DuckingService);
        Log("MainViewModel created");

        try
        {
            Log("Creating MainWindow...");
            _mainWindow = new MainWindow();
            Log("MainWindow created");
        }
        catch (Exception ex)
        {
            Log($"MainWindow FAILED: {ex}");
            return;
        }

        // SystemBackdrop is set inside TrySetMicaBackdrop() in MainWindow

        // Hide before activation to prevent the brief "wrong size" flash.
        // WinUI 3 creates the Win32 window at OS default size; we resize in the
        // constructor, but the OS may still paint one frame at the wrong size.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
        GameAudioMixer.Core.Interop.Win32.ShowWindow(hwnd, 0); // SW_HIDE

        _mainWindow.Closed += OnMainWindowClosed;
        Log("Activating window...");
        _mainWindow.Activate();
        Log("Window activated!");

        // Show after the first layout pass so the window appears already at the
        // correct size with no visible resize. Apply real opacity here, not in the
        // constructor, to avoid DWM compositing artifacts while hidden.
        _mainWindow.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                _mainWindow.ApplyWindowOpacity(250); // ~98% opaque — set just before showing
                GameAudioMixer.Core.Interop.Win32.ShowWindow(hwnd, 5); // SW_SHOW
                _mainWindow.DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
                    () => _mainWindow.MoveFocusToSink());
            });
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        // Shutdown is handled in MainWindow.OnWindowClosed before Application.Current.Exit()
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log($"UNHANDLED EXCEPTION: {e.Exception}");
        e.Handled = true;
    }
}
