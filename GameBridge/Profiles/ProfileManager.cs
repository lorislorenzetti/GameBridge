using System.Diagnostics;
using GameAudioMixer.Helpers;
using GameAudioMixer.Models;
using GameAudioMixer.Hotkeys;

namespace GameAudioMixer.Profiles;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 0;
    public List<AudioPreset> Presets { get; set; } = AudioPreset.GetDefaults();
    public List<GameProfile> GameProfiles { get; set; } = [];
    public List<KeyBinding> Hotkeys { get; set; } = KeyBinding.GetDefaults();
    public string ChatProcessName { get; set; } = "Discord";
    public List<string> PreferredDeviceIds { get; set; } = [];
    public bool AutoDuckingEnabled { get; set; } = false;
    public float DuckingPercent { get; set; } = 50f;
    public float DuckingThreshold { get; set; } = 0.25f;
    public float DuckingAttackSpeed { get; set; } = 0.653f;
    public float DuckingReleaseSpeed { get; set; } = 0.653f;
    /// <summary>How many seconds the fade-down takes when chat starts. Range 0.1–1.0.</summary>
    public float DuckingAttackDuration { get; set; } = 0.20f;
    /// <summary>How many seconds the fade-back-to-normal takes after chat stops. Range 0.1–1.0.</summary>
    public float DuckingReleaseDuration { get; set; } = 0.20f;
    public bool AutoStartEnabled { get; set; }
    public bool AutoSaveProfiles { get; set; } = false;
    public int SelectedPresetIndex { get; set; }
    public int GlobalBalance { get; set; } = 100;
}

public sealed class ProfileManager
{
    private const string SettingsFile = "settings.json";
    public AppSettings Settings { get; private set; }

    public event Action? SettingsChanged;

    public ProfileManager()
    {
        Settings = JsonStorage.Load<AppSettings>(SettingsFile) ?? new AppSettings();
        MigrateBalanceRange();
        MergeMissingHotkeys();
    }

    // Adds any default hotkey actions not present in the saved settings.
    // This ensures new hotkeys added in future versions appear for existing users.
    private void MergeMissingHotkeys()
    {
        var existing = Settings.Hotkeys.Select(h => h.Action).ToHashSet();
        var defaults = KeyBinding.GetDefaults();
        bool changed = false;

        foreach (var def in defaults)
        {
            if (!existing.Contains(def.Action))
            {
                Settings.Hotkeys.Add(def);
                changed = true;
            }
        }

        if (changed)
            JsonStorage.Save(SettingsFile, Settings);
    }

    // Migrates old Balance (0-100, center=50) to new range (0-200, center=100).
    // Runs exactly once by checking SchemaVersion.
    private void MigrateBalanceRange()
    {
        if (Settings.SchemaVersion >= 2) return;

        foreach (var p in Settings.GameProfiles)
            p.Balance = Math.Clamp(p.Balance * 2, 0, 200);

        foreach (var p in Settings.Presets)
            p.Balance = Math.Clamp(p.Balance * 2, 0, 200);

        Settings.SchemaVersion = 2;
        JsonStorage.Save(SettingsFile, Settings);
    }

    public void Save()
    {
        JsonStorage.Save(SettingsFile, Settings);
        SettingsChanged?.Invoke();
    }

    public GameProfile? FindProfileForProcess(string processName)
    {
        return Settings.GameProfiles.FirstOrDefault(p =>
            p.ExeName.Equals(processName, StringComparison.OrdinalIgnoreCase));
    }

    public void SaveGameProfile(GameProfile profile)
    {
        var existing = Settings.GameProfiles.FindIndex(p =>
            p.ExeName.Equals(profile.ExeName, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
            Settings.GameProfiles[existing] = profile;
        else
            Settings.GameProfiles.Add(profile);

        Save();
    }

    public void DeleteGameProfile(string exeName)
    {
        Settings.GameProfiles.RemoveAll(p =>
            p.ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public AudioPreset? GetCurrentPreset()
    {
        if (Settings.Presets.Count == 0) return null;
        int idx = Settings.SelectedPresetIndex;
        if (idx >= 0 && idx < Settings.Presets.Count)
            return Settings.Presets[idx];
        return Settings.Presets.FirstOrDefault();
    }

    public AudioPreset? CyclePreset()
    {
        if (Settings.Presets.Count == 0) return null;
        Settings.SelectedPresetIndex = (Settings.SelectedPresetIndex + 1) % Settings.Presets.Count;
        Save();
        return GetCurrentPreset();
    }

    public void AddPreset(AudioPreset preset)
    {
        Settings.Presets.Add(preset);
        Save();
    }

    public void DeletePreset(int index)
    {
        if (index < 0 || index >= Settings.Presets.Count) return;
        Settings.Presets.RemoveAt(index);
        if (Settings.SelectedPresetIndex >= Settings.Presets.Count)
            Settings.SelectedPresetIndex = Math.Max(0, Settings.Presets.Count - 1);
        Save();
    }
}
