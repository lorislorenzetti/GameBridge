using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameAudioMixer.Services;

public class UpdateService
{
    public const string CurrentVersion = "1.0.0";

    private const string ApiUrl = "https://api.github.com/repos/lorislorenzetti/GameBridge/releases/latest";
    public const string ReleasesUrl = "https://github.com/lorislorenzetti/GameBridge/releases/latest";

    private static readonly HttpClient _http = new();

    static UpdateService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "GameBridge-UpdateCheck");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Checks GitHub for a newer release.
    /// Returns (true, "vX.Y.Z") if an update is available, (false, "") otherwise.
    /// Never throws — network errors are silently ignored.
    /// </summary>
    public async Task<(bool IsAvailable, string LatestTag)> CheckAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
            var latestStr = tag.TrimStart('v');

            if (Version.TryParse(latestStr, out var latest) &&
                Version.TryParse(CurrentVersion, out var current) &&
                latest > current)
            {
                return (true, tag);
            }
        }
        catch { }

        return (false, string.Empty);
    }
}
