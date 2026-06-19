# StarMaster

A personal **Star Citizen helper app** for Windows — dependency-free (needs only the .NET Framework that already ships with Windows). Dark "HUD" interface with a built-in update checker.

**Tools:**
- **Keep-Alive** — anti-idle: sends a harmless, configurable keystroke (default *Wipe Visor* = **Left Alt + X**) on a timer, **only while Star Citizen is the active window**, so you don't get idle-logged-out.
- **Backup / Restore** — copies the config a patch or channel-switch wipes (`user\` settings + key bindings, the StarStrings `data\Localization`, and `user.cfg`) between channels (LIVE / HOTFIX) or from a saved snapshot. Overwrites only (never deletes), touches only those 3 paths, asks first, and warns if Star Citizen has files locked.
- **StarStrings** — keeps [MrKraken's StarStrings](https://github.com/MrKraken/StarStrings) community localization mod current: shows your installed build vs. the latest available, and installs/updates it in one click (downloads the release and copies it into your Star Citizen folder, safely merging `user.cfg`).

## Download & install
Grab the latest from the **[Releases page](https://github.com/elliot-borst/StarMaster/releases/latest)**:

| Download | What it is |
|----------|------------|
| **`StarMaster-Setup.exe`** | Installer — Start-menu shortcut, optional "run on Windows startup", and an uninstaller. **Recommended.** |
| **`StarMaster.exe`** | Portable — just run it, nothing installed. |

> Windows SmartScreen may warn about an "unknown publisher" (the app isn't code-signed yet) — click **More info → Run anyway**. On first launch the app creates its own `config.txt` next to itself; for run-on-boot, tick *Auto-start* (the installer can also add a startup shortcut).

StarMaster checks for newer versions on launch and offers to update.

## Build from source
Only needs the C# compiler that ships with Windows — no Node / NuGet / internet:
```
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /win32icon:StarMaster.ico /win32manifest:app.manifest /out:StarMaster.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll StarMaster.cs BackupForm.cs
```
The installer is built by compiling `installer.iss` with [Inno Setup](https://jrsoftware.org/isdl.php). See `CLAUDE.md` for full details.

## Files
| File | Purpose |
|------|---------|
| `StarMaster.cs`, `BackupForm.cs` | App source (C# WinForms) |
| `app.manifest` | DPI-awareness manifest (embedded at build for crisp high-DPI rendering) |
| `StarMaster.ps1`, `StarMaster-Startup.vbs` | Older PowerShell version + hidden launcher |
| `Make-Icon.ps1` | Regenerates `StarMaster.ico` (GDI+, offline) |
| `installer.iss` | Inno Setup script → `StarMaster-Setup.exe` |
| `CLAUDE.md` | Project context / handoff doc |

Runtime config (`config.txt`) stays out of git. Backups are saved to `Documents\StarMaster\Backups\`.
