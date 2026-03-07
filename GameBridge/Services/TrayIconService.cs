using System.Runtime.InteropServices;
using GameAudioMixer.Core.Interop;

namespace GameAudioMixer.Services;

/// <summary>
/// Manages a Win32 system tray icon with a hidden message window.
/// Double-click restores the app, right-click shows a context menu.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private IntPtr _hwnd;
    private Win32.NOTIFYICONDATA _nid;
    private Thread? _thread;
    private bool _disposed;
    private Win32.WndProc? _wndProcDelegate;

    public event Action? ShowRequested;
    public event Action? ExitRequested;

    public void Start()
    {
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "TrayIconThread" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void MessageLoop()
    {
        _wndProcDelegate = WndProc;

        var wc = new Win32.WNDCLASS
        {
            lpfnWndProc = _wndProcDelegate,
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = "GameAudioMixer_Tray"
        };
        Win32.RegisterClass(ref wc);

        _hwnd = Win32.CreateWindowEx(0, wc.lpszClassName, "", 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        IntPtr hIcon = Win32.LoadIcon(IntPtr.Zero, Win32.IDI_APPLICATION);
        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                IntPtr extracted = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (extracted != IntPtr.Zero) hIcon = extracted;
            }
        }
        catch { }

        _nid = new Win32.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
            uCallbackMessage = Win32.WM_TRAYICON,
            hIcon = hIcon,
            szTip = "GameBridge",
        };
        Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref _nid);

        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }

        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref _nid);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_TRAYICON)
        {
            uint mouseMsg = (uint)lParam.ToInt64();
            if (mouseMsg == Win32.WM_LBUTTONDBLCLK)
            {
                ShowRequested?.Invoke();
            }
            else if (mouseMsg == Win32.WM_RBUTTONUP)
            {
                ShowContextMenu();
            }
            return IntPtr.Zero;
        }

        if (msg == Win32.WM_COMMAND)
        {
            uint cmd = (uint)(wParam.ToInt64() & 0xFFFF);
            if (cmd == 1) ShowRequested?.Invoke();
            if (cmd == 2) ExitRequested?.Invoke();
            return IntPtr.Zero;
        }

        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr hMenu = Win32.CreatePopupMenu();
        Win32.AppendMenu(hMenu, Win32.MF_STRING, 1, "Open GameBridge");
        Win32.AppendMenu(hMenu, Win32.MF_SEPARATOR, 0, "");
        Win32.AppendMenu(hMenu, Win32.MF_STRING, 2, "Exit");

        Win32.GetCursorPos(out var pt);
        Win32.SetForegroundWindow(_hwnd);
        Win32.TrackPopupMenu(hMenu, Win32.TPM_RIGHTBUTTON, pt.x, pt.y, 0, _hwnd, IntPtr.Zero);
        Win32.DestroyMenu(hMenu);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref _nid);
            Win32.PostMessage(_hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, uint nIconIndex);
}
