namespace GameAudioMixer.Profiles;

public sealed class GameProfile
{
    public string ExeName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PresetName { get; set; } = "Casual";
    public int Balance { get; set; } = 100;
    public bool DuckingEnabled { get; set; } = false;
    public float DuckingPercent { get; set; } = 50f;
    public bool UseCustomDucking { get; set; } = false;
    public float CustomDuckingPercent { get; set; } = 50f;
    public float CustomDuckingThreshold { get; set; } = 0.100f;
    public float CustomDuckingAttackSpeed { get; set; } = 0.653f;
    public string? PreferredOutputDeviceId { get; set; }

    public float GameVolume => Balance <= 100 ? 1.0f : Math.Max(0f, 1.0f - (Balance - 100) / 100f);
    public float ChatVolume => Balance >= 100 ? 1.0f : Math.Max(0f, Balance / 100f);
}
