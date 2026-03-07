using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GameAudioMixer;

public static class Program
{
    private const string MutexName = "GameBridge_SingleInstance_Mutex";
    private const string WindowClass = "WinUIDesktopWin32WindowClass";
    private const string WindowTitle = "GameBridge";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    [STAThread]
    static void Main(string[] args)
    {
        // Single-instance guard
        bool isFirstInstance;
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(true, MutexName, out isFirstInstance);
        }
        catch (AbandonedMutexException ex)
        {
            // Previous instance was killed without releasing the mutex.
            // We acquired it — treat as first instance.
            mutex = ex.Mutex;
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            // Another instance is running — bring its window to the foreground.
            // SW_RESTORE (9) works for both minimized and hidden windows.
            IntPtr hWnd = FindWindow(WindowClass, WindowTitle);
            if (hWnd == IntPtr.Zero)
                hWnd = FindWindow(null, WindowTitle);
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
            return;
        }

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FATAL: {ex}");
        }
        finally
        {
            mutex?.ReleaseMutex();
            // Force process exit so background threads don't keep it alive
            // and the mutex is guaranteed to be fully released before exit.
            Environment.Exit(0);
        }
    }
}
