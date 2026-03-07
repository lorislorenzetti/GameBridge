using System.Diagnostics;
using GameAudioMixer.Core.Interop;
using Microsoft.UI.Xaml;

namespace GameAudioMixer.Services;

/// <summary>
/// Polls the foreground window at a 1-second interval to detect when the user
/// switches to a different application (game). Uses GetForegroundWindow +
/// GetWindowThreadProcessId — a single lightweight Win32 call.
/// </summary>
public sealed class ForegroundProcessService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private uint _currentPid;
    private string _currentProcessName = "";

    public string CurrentProcessName => _currentProcessName;
    public uint CurrentProcessId => _currentPid;

    public event Action<string, uint>? ForegroundChanged;

    public ForegroundProcessService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, object e)
    {
        try
        {
            IntPtr hwnd = Win32.GetForegroundWindow();
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);

            if (pid != _currentPid && pid != 0)
            {
                _currentPid = pid;
                try
                {
                    var process = Process.GetProcessById((int)pid);
                    _currentProcessName = process.ProcessName;
                }
                catch
                {
                    _currentProcessName = $"PID {pid}";
                }

                ForegroundChanged?.Invoke(_currentProcessName, pid);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ForegroundProcessService: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
