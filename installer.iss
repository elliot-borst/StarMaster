; SC Keep-Alive - Inno Setup installer script.
; Produces SC-KeepAlive-Setup.exe (a real setup wizard with Start-menu shortcut,
; optional "run on Windows startup", and an uninstaller).
;
; HOW TO BUILD THE INSTALLER:
;   1. Install Inno Setup (free): https://jrsoftware.org/isdl.php
;   2. Right-click this file -> "Compile" (or open in Inno Setup and press F9).
;   3. Out pops SC-KeepAlive-Setup.exe in this folder - that's the installer you can run/share.

#define MyAppName "SC Keep-Alive"
#define MyAppVersion "1.0.0"
#define MyAppExe "SC-KeepAlive.exe"
#define MyAppPublisher "Egbor"

[Setup]
AppId={{8F3A1C2B-5D6E-4A7F-9B0C-1D2E3F4A5B6C}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\SC Keep-Alive
DefaultGroupName=SC Keep-Alive
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=SC-KeepAlive-Setup
Compression=lzma
SolidCompression=yes
UninstallDisplayIcon={app}\{#MyAppExe}
WizardStyle=modern
SetupIconFile=SC-KeepAlive.ico

[Files]
Source: "SC-KeepAlive.exe"; DestDir: "{app}"; Flags: ignoreversion
; config.txt is intentionally NOT bundled - the app creates it on first run,
; so your settings survive a reinstall/upgrade.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startupicon"; Description: "Run automatically when Windows starts (recommended for keep-alive)"; GroupDescription: "Startup:"

[Icons]
Name: "{group}\SC Keep-Alive"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\Uninstall SC Keep-Alive"; Filename: "{uninstallexe}"
Name: "{userdesktop}\SC Keep-Alive"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon
Name: "{userstartup}\SC Keep-Alive"; Filename: "{app}\{#MyAppExe}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch SC Keep-Alive now"; Flags: nowait postinstall skipifsilent
