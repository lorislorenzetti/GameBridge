using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameAudioMixer.Helpers;

public static class JsonStorage
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameBridge");

    // One-time migration: copy settings from the old "GameAudioMixer" folder if present
    static JsonStorage()
    {
        try
        {
            string oldFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameAudioMixer");
            if (Directory.Exists(oldFolder) && !Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
                foreach (var file in Directory.GetFiles(oldFolder))
                    File.Copy(file, Path.Combine(AppDataFolder, Path.GetFileName(file)), overwrite: false);
            }
        }
        catch { }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T? Load<T>(string fileName) where T : class
    {
        try
        {
            string path = Path.Combine(AppDataFolder, fileName);
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"JsonStorage: Load<{typeof(T).Name}> failed: {ex.Message}");
            return null;
        }
    }

    public static void Save<T>(string fileName, T data)
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder);
            string path = Path.Combine(AppDataFolder, fileName);
            string json = JsonSerializer.Serialize(data, Options);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"JsonStorage: Save<{typeof(T).Name}> failed: {ex.Message}");
        }
    }

    public static string GetDataFolder() => AppDataFolder;
}
