namespace GameAudioMixer.Models;

public sealed class AudioPreset
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Balance 0-200: 0 = full game, 100 = equal, 200 = full chat.
    /// Center (100) means both at max; moving away reduces the opposite side.
    /// </summary>
    public int Balance { get; set; } = 100;

    public float DuckingPercent { get; set; } = 30f;
    public bool DuckingEnabled { get; set; } = true;

    public float GameVolume => Balance <= 100 ? 1.0f : Math.Max(0f, 1.0f - (Balance - 100) / 100f);
    public float ChatVolume => Balance >= 100 ? 1.0f : Math.Max(0f, Balance / 100f);

    public static List<AudioPreset> GetDefaults() => [];
}
