# SC Keep-Alive

A tiny Windows utility for Star Citizen that does two jobs:

- **Keep-alive (anti-idle).** Sends a harmless, configurable keystroke (default *Wipe Visor* = `Left Alt + X`) on a timer — but **only while Star Citizen is the active window** — so you don't get idle-logged-out during long sessions.
- **Backup / Restore.** Copies the things a patch or channel-switch wipes — your `user\` folder (in-game settings + key bindings), the StarStrings `data\Localization` text mod, and `user.cfg` — between channels (LIVE / HOTFIX) or from a saved snapshot. It only ever overwrites (never deletes), only touches those three sub-paths, and asks before writing.

## Run

Double-click **`SC-KeepAlive.exe`** — standalone, needs only the .NET Framework that Windows already ships. Tick *Auto-start* and drop a shortcut in `shell:startup` to run it on boot. (A PowerShell version, `SC-KeepAlive.ps1` launched via `SC-KeepAlive-Startup.vbs`, is also included.)

## Build from source

```bat
csc /target:winexe /win32icon:SC-KeepAlive.ico /out:SC-KeepAlive.exe ^
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
    SC-KeepAlive.cs BackupForm.cs
```

`csc.exe` ships with Windows at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`. No NuGet / internet needed.

## What's in here

| File | Purpose |
|------|---------|
| `SC-KeepAlive.cs`, `BackupForm.cs` | App source (C# WinForms) |
| `SC-KeepAlive.ps1`, `SC-KeepAlive-Startup.vbs` | PowerShell version + hidden launcher |
| `Make-Icon.ps1` | Regenerates `SC-KeepAlive.ico` (GDI+, offline) |
| `installer.iss` | Inno Setup script → `SC-KeepAlive-Setup.exe` |

## Config

Settings are saved to `config.txt` on first run (kept out of git). Edit commands, intervals, and the focus-guard window title in the app's UI.
