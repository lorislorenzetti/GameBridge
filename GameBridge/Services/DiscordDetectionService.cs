using System.Diagnostics;
using GameAudioMixer.Core;

namespace GameAudioMixer.Services;

/// <summary>
/// Detects chat application audio sessions by matching against a comprehensive list
/// of known VoIP / chat process names. Uses exact matching only (no partial/Contains)
/// to avoid false positives with game helper processes.
/// </summary>
public sealed class DiscordDetectionService
{
    private readonly SessionManager _sessionManager;

    public string ChatProcessName { get; set; } = "Discord";

    private static readonly string[] KnownChatProcesses =
    [
        "Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment",
        "Teams", "ms-teams",
        "Skype", "SkypeApp", "SkypeBridge",
        "ts3client_win64", "ts3client_win32", "TeamSpeak3", "TeamSpeak",
        "mumble",
        "Zoom", "ZoomUS",
        "Telegram", "Viber",
        "Slack",
        "Signal",
        "Ventrilo",
        "RaidCall",
        "Guilded",
    ];

    public DiscordDetectionService(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public AudioSessionInfo? GetChatSession()
    {
        // Priority 1: user-configured process name
        var session = _sessionManager.FindSessionByProcessName(ChatProcessName);
        if (session != null) return session;

        // Priority 2: known chat process names (exact match)
        foreach (var name in KnownChatProcesses)
        {
            session = _sessionManager.FindSessionByProcessName(name);
            if (session != null) return session;
        }

        return null;
    }

    /// <summary>
    /// Checks if a process name is a known chat application (exact match only).
    /// Partial / Contains matching is intentionally avoided to prevent game helper processes
    /// whose names happen to contain a chat-app keyword from being misidentified as chat.
    /// </summary>
    public static bool IsChatProcess(string processName)
    {
        foreach (var name in KnownChatProcesses)
        {
            if (processName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a chat app process is running (regardless of audio session existence).
    /// Returns the process name if found, null otherwise.
    /// </summary>
    public static string? FindRunningChatProcess()
    {
        foreach (var name in KnownChatProcesses)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                if (procs.Length > 0)
                    return name;
            }
            catch { }
        }
        return null;
    }
}
