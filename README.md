# StarMaster

A personal **Star Citizen helper app** for Windows — dependency-free (needs only the .NET Framework Windows already ships). Built to grow; two features so far:

- **Keep-alive (anti-idle)** — sends a harmless, configurable keystroke (default *Wipe Visor* = **Left Alt + X**) on a timer, **only while Star Citizen is the active window**, so you don't get idle-logged-out.
- **Backup / Restore** — copies the config a patch or channel-switch wipes (`user\` settings + key bindings, the StarStrings `data\Localization`, and `user.cfg`) between channels (LIVE / HOTFIX) or from a saved snapshot. Overwrites only (never deletes), touches only those 3 paths, asks first, and warns if Star Citizen has files locked.

## Run
Double-click **`StarMaster.exe`**. Tick *Auto-start* and drop a shortcut in `shell:startup` for run-on-boot.

## Build
```
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /win32icon:StarMaster.ico /out:StarMaster.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll StarMaster.cs BackupForm.cs
```
`csc.exe` ships with Windows — no Node / NuGet / internet required.

## Files
| File | Purpose |
|------|---------|
| `StarMaster.cs`, `BackupForm.cs` | App source (C# WinForms) |
| `StarMaster.ps1`, `StarMaster-Startup.vbs` | PowerShell version + hidden launcher |
| `Make-Icon.ps1` | Regenerates `StarMaster.ico` (GDI+, offline) |
| `installer.iss` | Inno Setup script → `StarMaster-Setup.exe` |
| `CLAUDE.md` | Project context / handoff doc |

Runtime config (`config.txt` / `config.json`) stays out of git. Backups are saved to `Documents\StarMaster\Backups\`.
