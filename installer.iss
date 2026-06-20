; StarMaster - Inno Setup installer script.
; Produces StarMaster-Setup.exe (a real setup wizard with Start-menu shortcut,
; optional "run on Windows startup", and an uninstaller).
;
; HOW TO BUILD THE INSTALLER:
;   1. Install Inno Setup (free): https://jrsoftware.org/isdl.php
;   2. Right-click this file -> "Compile" (or open in Inno Setup and press F9).
;   3. Out pops StarMaster-Setup.exe in this folder - that's the installer you can run/share.

#define MyAppName "StarMaster"
#define MyAppVersion "8"
#define MyAppExe "StarMaster.exe"
#define MyAppPublisher "Elliot Borst"

[Setup]
AppId={{8F3A1C2B-5D6E-4A7F-9B0C-1D2E3F4A5B6C}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/elliot-borst/StarMaster
DefaultDirName={localappdata}\StarMaster
DefaultGroupName=StarMaster
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=StarMaster-Setup
Compression=lzma
SolidCompression=yes
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
SetupIconFile=StarMaster.ico
; In-app updater downloads this installer and runs it while StarMaster is open;
; offer to close the running copy so files can be replaced (no forced reboot).
CloseApplications=yes
RestartApplications=no

[Files]
Source: "StarMaster.exe"; DestDir: "{app}"; Flags: ignoreversion
; config.txt is intentionally NOT bundled - the app creates it on first run,
; so your settings survive a reinstall/upgrade.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startupicon"; Description: "Run automatically when Windows starts (recommended for keep-alive)"; GroupDescription: "Startup:"

[Icons]
Name: "{group}\StarMaster"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\Uninstall StarMaster"; Filename: "{uninstallexe}"
Name: "{userdesktop}\StarMaster"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon
Name: "{userstartup}\StarMaster"; Filename: "{app}\{#MyAppExe}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch StarMaster now"; Flags: nowait postinstall skipifsilent
