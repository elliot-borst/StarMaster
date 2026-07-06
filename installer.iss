; StarMaster - Inno Setup installer script.
; Produces dist\StarMaster-Setup.exe. Built by build-installer.ps1, which reads the
; whole-number version from MainWindow.Version in StarMaster.cs and passes it via
; /DMyAppVersion (a manual IDE compile falls back to "0" - build via the script).
;
; Zero-question wizard: no dir/group/ready/finished pages. NB an INTERACTIVE run still
; shows at least one confirmation page no matter what - only the silent flags
; (/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /FORCECLOSEAPPLICATIONS, used by the
; in-app auto-updater) remove all UI.

#ifndef MyAppVersion
  #define MyAppVersion "0"
#endif
#define MyAppName "StarMaster"
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
; Zero-question install. No desktop-shortcut or startup [Tasks] either - silent installs
; never show task pages, so start-with-Windows is managed in-app (HKCU Run key toggle).
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=StarMaster-Setup
Compression=lzma
SolidCompression=yes
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
SetupIconFile=StarMaster.ico
; The in-app updater runs this installer silently while StarMaster is open; force-close
; the running copy so files can be replaced (no forced reboot).
CloseApplications=force
RestartApplications=no

[Files]
Source: "StarMaster.exe"; DestDir: "{app}"; Flags: ignoreversion
; config.txt is intentionally NOT bundled - the app creates it on first run,
; so your settings survive a reinstall/upgrade.

[Icons]
Name: "{group}\StarMaster"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\Uninstall StarMaster"; Filename: "{uninstallexe}"

[Run]
; Always relaunch, ALSO on silent auto-updates - a plain nowait entry, NOT
; postinstall/skipifsilent (those never fire on /VERYSILENT installs, so the app
; would not come back after an update). --minimized keeps updates invisible in the
; tray; the app shows its window anyway on a true first run (no config.txt yet).
Filename: "{app}\{#MyAppExe}"; Parameters: "--minimized"; Flags: nowait
