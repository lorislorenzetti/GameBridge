using System.Diagnostics;
using System.Runtime.InteropServices;
using GameAudioMixer.Core.Interop;

namespace GameAudioMixer.Core;

public sealed class AudioDeviceInfo
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public required string Description { get; init; }
    public bool IsDefault { get; set; }
}

public sealed class DeviceManager : IMMNotificationClient, IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly object _lock = new();
    private bool _disposed;

    public event Action? DevicesChanged;
    public event Action<string>? DefaultDeviceChanged;

    public DeviceManager()
    {
        var type = Type.GetTypeFromCLSID(CLSID.MMDeviceEnumerator)
            ?? throw new InvalidOperationException("MMDeviceEnumerator CLSID not found");
        _enumerator = (IMMDeviceEnumerator)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Failed to create MMDeviceEnumerator"));

        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public List<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        int hr = _enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE.ACTIVE, out var collection);
        if (hr != 0) return devices;

        string? defaultId = null;
        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defaultDev) == 0)
            {
                defaultDev.GetId(out defaultId);
                Marshal.ReleaseComObject(defaultDev);
            }
        }
        catch { /* No default device available */ }

        collection.GetCount(out uint count);
        for (uint i = 0; i < count; i++)
        {
            try
            {
                collection.Item(i, out var device);
                device.GetId(out string id);

                string friendlyName = GetDeviceProperty(device, PKEY.DeviceFriendlyName) ?? "Unknown";
                string description = GetDeviceProperty(device, PKEY.DeviceDescription) ?? "";

                devices.Add(new AudioDeviceInfo
                {
                    Id = id,
                    FriendlyName = friendlyName,
                    Description = description,
                    IsDefault = id == defaultId
                });

                Marshal.ReleaseComObject(device);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeviceManager: Error enumerating device {i}: {ex.Message}");
            }
        }

        Marshal.ReleaseComObject(collection);
        return devices;
    }

    public List<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        int hr = _enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DEVICE_STATE.ACTIVE, out var collection);
        if (hr != 0) return devices;

        string? defaultId = null;
        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out var defaultDev) == 0)
            {
                defaultDev.GetId(out defaultId);
                Marshal.ReleaseComObject(defaultDev);
            }
        }
        catch { }

        collection.GetCount(out uint count);
        for (uint i = 0; i < count; i++)
        {
            try
            {
                collection.Item(i, out var device);
                device.GetId(out string id);

                string friendlyName = GetDeviceProperty(device, PKEY.DeviceFriendlyName) ?? "Unknown";
                string description = GetDeviceProperty(device, PKEY.DeviceDescription) ?? "";

                devices.Add(new AudioDeviceInfo
                {
                    Id = id,
                    FriendlyName = friendlyName,
                    Description = description,
                    IsDefault = id == defaultId
                });

                Marshal.ReleaseComObject(device);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeviceManager: Error enumerating capture device {i}: {ex.Message}");
            }
        }

        Marshal.ReleaseComObject(collection);
        return devices;
    }

    public IMMDevice? GetDevice(string deviceId)
    {
        int hr = _enumerator.GetDevice(deviceId, out var device);
        return hr == 0 ? device : null;
    }

    public IMMDevice? GetDefaultOutputDevice()
    {
        int hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
        return hr == 0 ? device : null;
    }

    public IMMDevice? GetDefaultCaptureDevice()
    {
        // Try communications role first (preferred for headsets), fall back to console
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out var device) == 0)
            return device;
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out device) == 0)
            return device;
        return null;
    }

    public bool SetDefaultDevice(string deviceId)
    {
        try
        {
            var policyConfig = PolicyConfigHelper.CreatePolicyConfig();
            int hr1 = policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
            int hr2 = policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            int hr3 = policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            Marshal.ReleaseComObject(policyConfig);
            return hr1 == 0 && hr2 == 0 && hr3 == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DeviceManager: SetDefaultDevice failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Cycles through preferred devices in order. Returns the newly selected device ID, or null on failure.
    /// </summary>
    public string? CycleDevice(List<string> preferredDeviceIds)
    {
        if (preferredDeviceIds.Count == 0) return null;

        var current = GetOutputDevices().FirstOrDefault(d => d.IsDefault);
        string? currentId = current?.Id;

        int currentIndex = currentId != null ? preferredDeviceIds.IndexOf(currentId) : -1;
        int nextIndex = (currentIndex + 1) % preferredDeviceIds.Count;

        string nextId = preferredDeviceIds[nextIndex];
        return SetDefaultDevice(nextId) ? nextId : null;
    }

    private static string? GetDeviceProperty(IMMDevice device, PROPERTYKEY key)
    {
        try
        {
            device.OpenPropertyStore(STGM.STGM_READ, out var store);
            store.GetValue(ref key, out var propVariant);
            string? result = propVariant.AsString();
            Marshal.ReleaseComObject(store);
            return result;
        }
        catch
        {
            return null;
        }
    }

    #region IMMNotificationClient

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DEVICE_STATE newState)
    {
        DevicesChanged?.Invoke();
    }

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId)
    {
        DevicesChanged?.Invoke();
    }

    void IMMNotificationClient.OnDeviceRemoved(string deviceId)
    {
        DevicesChanged?.Invoke();
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
    {
        if (flow == EDataFlow.eRender && role == ERole.eMultimedia)
        {
            DefaultDeviceChanged?.Invoke(defaultDeviceId);
        }
        DevicesChanged?.Invoke();
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PROPERTYKEY key) { }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
            Marshal.ReleaseComObject(_enumerator);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DeviceManager: Dispose error: {ex.Message}");
        }
    }
}
