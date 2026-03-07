using CommunityToolkit.Mvvm.ComponentModel;
using GameAudioMixer.Core;

namespace GameAudioMixer.ViewModels;

public partial class DeviceViewModel : ObservableObject
{
    public string Id { get; }
    public string FriendlyName { get; }
    public string Description { get; }

    [ObservableProperty]
    private bool _isDefault;

    public DeviceViewModel(AudioDeviceInfo info)
    {
        Id = info.Id;
        FriendlyName = info.FriendlyName;
        Description = info.Description;
        _isDefault = info.IsDefault;
    }
}
