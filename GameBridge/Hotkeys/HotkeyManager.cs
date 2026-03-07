using System.Collections.Concurrent;
using System.Diagnostics;
using GameAudioMixer.Core.Interop;
using GameAudioMixer.Models;

namespace GameAudioMixer.Hotkeys;

/// <summary>
/// Manages global hotkeys via a hidden Win32 message-only window on a dedicated STA thread.
/// All RegisterHotKey/UnregisterHotKey calls are marshalled to the window-owning thread
/// because the Win32 API requires thread affinity for the hidden window.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly Dictionary<int, KeyBinding> _bindings = [];
    private IntPtr _hwnd;
    private Thread? _messageThread;
    private bool _disposed;
    private bool _ready;
    private readonly ManualResetEventSlim _readyEvent = new();
    private Win32.WndProc? _wndProc;
    private readonly ConcurrentQueue<Action> _pendingActions = new();
    private const uint WM_PROCESS_QUEUE = Win32.WM_USER + 1;

    public event Action<HotkeyAction>? HotkeyPressed;
    public List<KeyBinding> Bindings { get; private set; } = [];
    public int RegisteredCount { get; private set; }
    public List<string> RegistrationErrors { get; } = [];

    private static void Log(string msg) => Debug.WriteLine($"[Hotkey] {msg}");

    public HotkeyManager()
    {
        _messageThread = new Thread(MessageLoop) { IsBackground = true, Name = "HotkeyMessageLoop" };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();
        _readyEvent.Wait(TimeSpan.FromSeconds(5));
        Log($"HotkeyManager ready={_ready}, hwnd={_hwnd}");
    }

    public void RegisterBindings(List<KeyBinding> bindings)
    {
        if (!_ready) { Log("RegisterBindings: not ready"); return; }

        _pendingActions.Enqueue(() => DoRegisterBindings(bindings));
        Win32.PostMessage(_hwnd, WM_PROCESS_QUEUE, IntPtr.Zero, IntPtr.Zero);
    }

    private void DoRegisterBindings(List<KeyBinding> bindings)
    {
        DoUnregisterAll();
        Bindings = bindings;
        RegisteredCount = 0;
        RegistrationErrors.Clear();

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            uint mods = binding.Modifiers | Win32.MOD_NOREPEAT;
            bool success = Win32.RegisterHotKey(_hwnd, i, mods, binding.VirtualKey);
            if (success)
            {
                _bindings[i] = binding;
                RegisteredCount++;
                Log($"Registered: {binding.DisplayName} -> {binding.Action}");
            }
            else
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                string msg = $"FAILED: {binding.DisplayName} -> {binding.Action} (error={err})";
                RegistrationErrors.Add(msg);
                Log(msg);
            }
        }
        Log($"Registration complete: {RegisteredCount}/{bindings.Count} hotkeys active");
    }

    private void DoUnregisterAll()
    {
        foreach (var id in _bindings.Keys)
            Win32.UnregisterHotKey(_hwnd, id);
        _bindings.Clear();
    }

    private void ProcessPendingActions()
    {
        while (_pendingActions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Log($"Action error: {ex.Message}"); }
        }
    }

    private void MessageLoop()
    {
        _wndProc = WndProc;

        var wc = new Win32.WNDCLASS
        {
            lpfnWndProc = _wndProc,
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = "GameAudioMixerHotkeyWindow_" + Environment.ProcessId
        };

        Win32.RegisterClass(ref wc);

        _hwnd = Win32.CreateWindowEx(
            0, wc.lpszClassName, "",
            0, 0, 0, 0, 0,
            new IntPtr(-3), // HWND_MESSAGE: message-only window
            IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        _ready = _hwnd != IntPtr.Zero;
        _readyEvent.Set();

        if (!_ready)
        {
            Log("FATAL: Failed to create message-only window");
            return;
        }

        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_bindings.TryGetValue(id, out var binding))
            {
                Log($"Hotkey fired: {binding.Action}");
                HotkeyPressed?.Invoke(binding.Action);
            }
            return IntPtr.Zero;
        }

        if (msg == WM_PROCESS_QUEUE)
        {
            ProcessPendingActions();
            return IntPtr.Zero;
        }

        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            _pendingActions.Enqueue(DoUnregisterAll);
            Win32.PostMessage(_hwnd, WM_PROCESS_QUEUE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(100);
            Win32.PostMessage(_hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        _readyEvent.Dispose();
    }
}
