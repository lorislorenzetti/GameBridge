using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAudioMixer.Core;

namespace GameAudioMixer.ViewModels;

public partial class AudioSessionViewModel : ObservableObject
{
    private readonly AudioSessionInfo _session;

    public uint ProcessId => _session.ProcessId;
    public string ProcessName => _session.ProcessName;
    public string DisplayName => _session.DisplayName;

    [ObservableProperty]
    private float _volume;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private float _peakValue;

    public AudioSessionViewModel(AudioSessionInfo session)
    {
        _session = session;
        _volume = session.Volume;
        _isMuted = session.IsMuted;
    }

    partial void OnVolumeChanged(float value)
    {
        _session.Volume = value;
    }

    partial void OnIsMutedChanged(bool value)
    {
        _session.IsMuted = value;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    /// <summary>
    /// Reads the peak from the COM session, updates PeakValue, and returns the value.
    /// Call this once per tick — GetPeakValue() resets the Windows peak meter on each read.
    /// </summary>
    public float UpdatePeak()
    {
        PeakValue = _session.PeakValue;
        return PeakValue;
    }

    public void SyncFromSession()
    {
        Volume = _session.Volume;
        IsMuted = _session.IsMuted;
    }
}
