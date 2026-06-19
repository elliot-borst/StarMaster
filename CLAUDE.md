# StarMaster — Star Citizen helper app

**StarMaster is a Windows app the user is building up** as their personal Star Citizen toolkit — a single dependency-free C# WinForms program (needs only the .NET Framework Windows ships). Two features today; more planned.

**Repo:** `elliot-borst/StarMaster` (private) · local `C:\GitHub\StarMaster`. Run Claude Code **from this folder** to work on it. The user's Star Citizen control *bindings* are a SEPARATE repo/project — **StarBinding** — not here.

## Features today
1. **Keep-alive (anti-idle).** A timer sends a harmless, configurable keystroke (default *Wipe Visor* = **Left Alt + X**) **only while Star Citizen is the active window** (focus guard, fail-closed), so the user isn't idle-logged-out. Replaces a VoiceAttack scheduled command.
2. **Backup / Restore** ("Backup / Restore..." button → `BackupForm`). Copies what a patch/channel-switch wipes — `user\` (settings + bindings), `data\Localization\` (StarStrings mod), `user.cfg` — between SC channels (LIVE/HOTFIX) or saved snapshots. Overwrites but never deletes; only those 3 sub-paths; skips locked files with a warning; confirms first. Snapshots → `Documents\StarMaster\Backups\<channel>-<timestamp>\`.

## Architecture
- `StarMaster.cs` — the app. Classes: `Native` (keybd_event + foreground-window P/Invoke), `Vk` (key-name → virtual-key map), `Cmd` (one scheduled keystroke), `MainForm` (UI + 1 s timer + config load/save), `Main()`. Namespace `StarMaster`.
- `BackupForm.cs` — the Backup/Restore dialog (same namespace). `CopyTree` is resilient — skips locked files and counts them rather than aborting.
- `StarMaster.ps1` + `StarMaster-Startup.vbs` — an older PowerShell version + a hidden (no-console) launcher pointing at `C:\GitHub\StarMaster\StarMaster.ps1`. **The C# `.exe` is the primary**; only touch the `.ps1` if asked.
- `Make-Icon.ps1` — regenerates `StarMaster.ico` (GDI+, offline; amber HUD pulse on a dark panel).
- `installer.iss` — Inno Setup → `StarMaster-Setup.exe` (Start-menu + optional run-on-startup + uninstaller).

## Build (no Node / NuGet / internet)
```
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /win32icon:StarMaster.ico /out:StarMaster.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll StarMaster.cs BackupForm.cs
```
Rebuild after editing any `.cs`. Re-run `Make-Icon.ps1` only when the icon changes.

## Conventions / facts
- **Config** = `config.txt` next to the exe (NOT committed). `key=value` lines (`autostart`, `focusguard`, `wintitle`) + command rows `Label|Shift|Ctrl|Alt|Key|Interval|Enabled`. Interval clamped 5–3600 s.
- **Focus guard fails CLOSED** — blank title box → sends nothing (never blast keys into the wrong app). Case-insensitive match; default "Star Citizen".
- **Wipe-visor key = Left Alt + X** (SC action `visor_wipe`).
- Don't commit `config.txt`, `config.json`, `Backups/`, or `StarMaster-Setup.exe` (see `.gitignore`).
- **SC environment:** install root `C:\Program Files\Roberts Space Industries\StarCitizen`; channels `LIVE` / `HOTFIX` (each has its own `user\`; a patch overwrites `data\`).

## Review history
- 2026-06-18: focus-guard fail-open → fail-closed; NumericUpDown out-of-range crash → clamp + sanitize.
- 2026-06-19: backup locked-file mid-copy left a partial set and mis-reported "nothing copied" → `CopyTree` now skips locked files, counts them, and warns.

## Backlog / ideas
System-tray minimize; multiple per-window keystroke profiles; option to back up the VoiceAttack profile too; produce a signed `StarMaster-Setup.exe` via `installer.iss`.

> This file is the app's handoff doc — a fresh Claude Code session opened in this folder has everything it needs to continue.
