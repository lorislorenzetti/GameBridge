# GameBridge

**GameBridge** is a lightweight Windows audio mixer designed for gamers who use voice chat (Discord, TeamSpeak, Mumble, etc.) while playing. It sits in the system tray and lets you control the balance between game audio and chat audio in real time — with global hotkeys, so you never have to alt-tab.

---

## The Problem

When you play online with friends, you're constantly fighting two audio sources:

- The **game** — sound effects, music, spatial audio cues
- The **chat** — your teammates' voices

Windows has no built-in way to balance these two independently in real time. You end up either cranking the game volume and missing callouts, or lowering it so much you lose immersion. Every game has different default volumes. Every session is different. The only options are to either dig into the Volume Mixer (which you can't open while in a fullscreen game) or just live with a bad balance.

**GameBridge solves this** by automatically detecting which process is your game and which is your chat app, then giving you a single slider — and a hotkey — to shift the balance between them instantly, from anywhere, without ever leaving your game.

---

## Features

### Automatic Audio Detection
GameBridge monitors the foreground window and all active audio sessions. When it detects a game and a chat application running at the same time, it automatically assigns them as the Game and Chat sources. No manual configuration needed for standard setups. Supports Discord, TeamSpeak, Mumble, Skype, and other common VoIP apps out of the box.

### Game/Chat Balance Slider
A single slider controls the ratio between game and chat volume. The center position (50%) means equal volume. Moving left boosts chat, moving right boosts game. The actual volumes are displayed live as percentages. Double-clicking the slider snaps it back to center.

### Auto-Ducking
When a teammate speaks, GameBridge can automatically reduce the game volume so their voice comes through clearly, then smoothly restore it when they stop. All parameters are fully configurable:

- **Duck amount** — how much to reduce game volume when chat is active (10–100%)
- **Activation threshold** — how loud chat needs to be before ducking triggers
- **Attack duration** — how fast the game volume fades down when chat starts (0.1–1.0 s)
- **Release duration** — how long the game volume takes to fade back after chat goes silent (0.1–1.0 s)

The ducking uses a real-time envelope follower with timed fades. When ducking is active, a cyan indicator appears on the main window.

### HUD Overlay
A minimal on-screen display appears briefly whenever you use a hotkey to change something. It works over fullscreen games (including exclusive fullscreen) and shows the current balance, preset name, output device, or mic status. The overlay is non-interactive and disappears automatically after ~1.8 seconds.

### Global Hotkeys
All major actions are available via configurable global keyboard shortcuts that work in any application, including fullscreen games. Default bindings:

| Action | Default |
|---|---|
| Balance → Game | `Ctrl+Alt+Right` |
| Balance → Chat | `Ctrl+Alt+Left` |
| Switch Output Device | `Ctrl+Alt+D` |
| Mute Microphone | `Ctrl+Alt+N` |
| Mute Game | `Ctrl+Alt+G` |
| Mute Chat | `Ctrl+Alt+C` |
| Toggle Auto-Ducking | `Ctrl+Alt+U` |

All hotkeys are fully rebindable from the Settings panel.

### Output Device Switcher
Switch between audio output devices directly from the app or via hotkey without opening Windows settings. The device list in the main window can be filtered to show only your preferred devices (configured in Settings).

### Master Volume
Control the Windows system master volume directly from the GameBridge interface.

### Microphone Mute
Toggle your system microphone mute with one click or a global hotkey. The current mic state is always visible in the main window with a color indicator (green = active, red = muted).

### System Tray & Background Operation
GameBridge runs in the background after launch, accessible from the system tray. Closing the window hides it rather than quitting — the app keeps working and hotkeys remain active. Right-click the tray icon to exit completely.

### Auto-Start with Windows
Optional setting to launch GameBridge automatically when Windows starts, so it's always ready when you sit down to play.

### Automatic Update Check
At startup, GameBridge silently checks GitHub for a newer version. If one is available, a notification appears with a direct link to download it.

---

## Installation

1. Download `GameBridgeSetup-1.0.0.exe` from the [Releases](https://github.com/lorislorenzetti/GameBridge/releases/latest) page.
2. Run the installer and follow the prompts.
3. GameBridge will launch automatically after installation.

**No .NET runtime installation required.** The app is fully self-contained.

---

## System Requirements

- Windows 10 (version 19045 / 22H2) or later, or Windows 11
- x64 processor
- ~160 MB disk space

---

## Usage

### First Launch
GameBridge starts in the background and shows a compact window. Start your game and open your chat app. GameBridge will detect both within a few seconds and display their names in the interface.

### Adjusting Balance
Drag the balance slider left (more chat) or right (more game). The percentage labels update in real time. Use `Ctrl+Alt+Left/Right` to adjust in steps without leaving your game.

### Using Hotkeys
All hotkeys work globally while in-game. Each action triggers a brief HUD overlay confirming the change. Hotkeys can be reconfigured in the Settings panel by clicking the key button and pressing the new combination.

### Manual Session Override
If GameBridge picks the wrong audio source, click the label next to "Game" or "Chat" to open a flyout with all active audio sessions. Select the correct one manually. The next time that game is detected in the foreground, it will be reassigned automatically.

### Muting Game or Chat
The MUTE GAME and MUTE CHAT buttons (and corresponding hotkeys) mute the selected session's audio entirely, independently of the balance slider.

### Configuring Auto-Ducking
Click the duck icon (or the Settings gear) to access the Settings panel. Adjust the threshold so ducking only triggers on actual voice, not background noise. Typical values: threshold 3–8, duck amount 40–60%, attack 0.1–0.3 s, release 0.3–0.6 s. The live monitor graph shows the chat peak level and ducking state in real time.

---

## Build from Source

**Requirements:** .NET 8 SDK, Visual Studio 2022 with WinUI 3 workload (or `dotnet` CLI), Inno Setup 6 (for the installer).

```powershell
# Clone
git clone https://github.com/lorislorenzetti/GameBridge.git
cd GameBridge

# Build and publish
dotnet publish GameBridge/GameBridge.csproj -c Release -r win-x64 --self-contained true

# Build installer (optional)
& "C:\InnoSetup6\ISCC.exe" installer\GameBridge.iss
```

The published output goes to `GameBridge\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\`.

---

## License

This project is released for personal use. All rights reserved.
