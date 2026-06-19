# StarMaster — Star Citizen Master Tools

A small, personal collection of dependency-free Windows tools for Star Citizen (needs only the .NET Framework that Windows already ships).

## Tools

### 🫀 Keep-Alive — `SC-KeepAlive.exe`
- **Anti-idle.** Sends a harmless, configurable keystroke (default *Wipe Visor* = **Left Alt + X**) on a timer, **only while Star Citizen is the active window**, so you don't get idle-logged-out.
- **Backup / Restore.** Copies the config a patch or channel-switch wipes — `user\` (settings + key bindings), `data\Localization\` (the StarStrings text mod), and `user.cfg` — between channels (LIVE / HOTFIX) or from a saved snapshot. Only overwrites (never deletes), only touches those three sub-paths, asks before writing, and warns if Star Citizen has files open.

Run by double-clicking `SC-KeepAlive.exe`.

## Build
```
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /win32icon:SC-KeepAlive.ico /out:SC-KeepAlive.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll SC-KeepAlive.cs BackupForm.cs
```
`csc.exe` ships with Windows — no Node / NuGet / internet required.

## Files
| File | Purpose |
|------|---------|
| `SC-KeepAlive.cs`, `BackupForm.cs` | Keep-Alive app source (C# WinForms) |
| `SC-KeepAlive.ps1`, `SC-KeepAlive-Startup.vbs` | PowerShell version + hidden launcher |
| `Make-Icon.ps1` | Regenerates `SC-KeepAlive.ico` (GDI+, offline) |
| `installer.iss` | Inno Setup script → `SC-KeepAlive-Setup.exe` |
| `CLAUDE.md` | Project guide / context for Claude Code |

Runtime config (`config.txt` / `config.json`) and backups are kept out of git.
