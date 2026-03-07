using GameAudioMixer.Models;

namespace GameAudioMixer.Hotkeys;

public sealed class KeyBinding
{
    public HotkeyAction Action { get; set; }
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }
    public string DisplayName => FormatKeyCombo();

    private string FormatKeyCombo()
    {
        var parts = new List<string>();
        if ((Modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((Modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((Modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((Modifiers & 0x0008) != 0) parts.Add("Win");

        string keyName = VirtualKey switch
        {
            >= 0x30 and <= 0x39 => ((char)VirtualKey).ToString(),
            >= 0x41 and <= 0x5A => ((char)VirtualKey).ToString(),
            >= 0x70 and <= 0x87 => $"F{VirtualKey - 0x6F}",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0xBB => "=",
            0xBD => "-",
            0x21 => "PgUp",
            0x22 => "PgDn",
            _ => $"0x{VirtualKey:X2}"
        };
        parts.Add(keyName);
        return string.Join(" + ", parts);
    }

    public static List<KeyBinding> GetDefaults() =>
    [
        new() { Action = HotkeyAction.BalanceToGame,      Modifiers = 0x0002 | 0x0001, VirtualKey = 0x25 }, // Ctrl+Alt+Left
        new() { Action = HotkeyAction.BalanceToChat,       Modifiers = 0x0002 | 0x0001, VirtualKey = 0x27 }, // Ctrl+Alt+Right
        new() { Action = HotkeyAction.TogglePreset,        Modifiers = 0x0002 | 0x0001, VirtualKey = 0x50 }, // Ctrl+Alt+P
        new() { Action = HotkeyAction.SwitchOutputDevice,  Modifiers = 0x0002 | 0x0001, VirtualKey = 0x44 }, // Ctrl+Alt+D
        new() { Action = HotkeyAction.ToggleMicMute,       Modifiers = 0x0002 | 0x0001, VirtualKey = 0x4E }, // Ctrl+Alt+N
        new() { Action = HotkeyAction.MuteGame,            Modifiers = 0x0002 | 0x0001, VirtualKey = 0x47 }, // Ctrl+Alt+G
        new() { Action = HotkeyAction.MuteChat,            Modifiers = 0x0002 | 0x0001, VirtualKey = 0x43 }, // Ctrl+Alt+C
        new() { Action = HotkeyAction.ToggleDucking,       Modifiers = 0x0002 | 0x0001, VirtualKey = 0x55 }, // Ctrl+Alt+U
    ];
}
