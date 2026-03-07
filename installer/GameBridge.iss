; ============================================================
;  GameBridge – Inno Setup Script
;  To build: open with Inno Setup Compiler and press Ctrl+F9
;  or run from command line:
;    "C:\InnoSetup6\ISCC.exe" GameBridge.iss
; ============================================================

#define MyAppName      "GameBridge"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "Loris Lorenzetti"
#define MyAppExeName   "GameBridge.exe"
#define MyAppURL       ""
#define SourceDir      "..\GameBridge\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish"

[Setup]
AppId={{A3F2C1D4-88B7-4E2A-9F3C-2D5E8A1B7C4F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Output
OutputDir=Output
OutputBaseFilename=GameBridgeSetup-{#MyAppVersion}
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
; Appearance
WizardStyle=modern
SetupIconFile=..\GameBridge\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; Minimum Windows 10 (required by WinUI 3)
MinVersion=10.0.19041
; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Misc
DisableProgramGroupPage=yes
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Launch GameBridge with Windows"; GroupDescription: "Auto-start"; Flags: unchecked

[Files]
; All published files – recursive, preserving subdirectories
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Desktop (optional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Auto-start with Windows (optional task)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startupicon

[Run]
; Offer to launch the app after installation
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill any running instance before uninstall
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; \
  Flags: runhidden; RunOnceId: "KillGameBridge"

[Code]

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Kill any running instance before install/upgrade (prevents file-lock errors)
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
