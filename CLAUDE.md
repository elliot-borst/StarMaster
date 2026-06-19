# SC Keep-Alive — project guide

A tiny Windows utility for Star Citizen, written to run with **zero dependencies** (just the .NET Framework Windows already ships). Two features:

1. **Keep-alive (anti-idle).** Sends a harmless, configurable keystroke (default *Wipe Visor* = **Left Alt + X**) on a timer, **only while Star Citizen is the active window** (focus guard, fail-closed), to avoid the idle logout.
2. **Backup / Restore.** Copies the config a SC patch or channel-switch wipes — `user\` (in-game settings + key bindings), `data\Localization\` (the StarStrings text mod, ~10 MB `global.ini`), and `user.cfg` (StarStrings `g_language`) — between channels or from a saved snapshot.

## Files
- `SC-KeepAlive.cs` — main app (C# WinForms): keep-alive form, keystroke sender (`keybd_event` P/Invoke), VK map, config (`config.txt`), `Main()`.
- `BackupForm.cs` — the "Backup / Restore..." dialog (opened from the main form).
- `SC-KeepAlive.ps1` + `SC-KeepAlive-Startup.vbs` — a PowerShell mirror of the keep-alive (older; uses `config.json`) + a hidden launcher. **The C# `.exe` is the primary**; only keep the `.ps1` in sync if asked.
- `Make-Icon.ps1` — regenerates `SC-KeepAlive.ico` (GDI+, offline). Design = amber HUD heartbeat/pulse on a dark panel.
- `installer.iss` — Inno Setup script → `SC-KeepAlive-Setup.exe` (Start-menu shortcut, run-on-startup task, uninstaller).

## Build
Compiles with the in-box compiler (no Node / NuGet / internet):
```
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /win32icon:SC-KeepAlive.ico /out:SC-KeepAlive.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll SC-KeepAlive.cs BackupForm.cs
```
Rebuild after editing any `.cs`. Re-run `Make-Icon.ps1` only when the icon changes.

## Key decisions / facts
- **Wipe-visor key = Left Alt + X** (SC action `visor_wipe`; it's a keyboard action, unbound on the joystick).
- **Focus guard fails CLOSED** — blank window-title box → sends nothing (never blast keystrokes into the wrong app). Title match is case-insensitive; default "Star Citizen".
- **Keep-alive config** = `config.txt` (NOT committed). Interval clamped 5–3600 s.
- **Backup** touches only three sub-paths (`user\`, `data\Localization\`, `user.cfg`) of a channel; **overwrites but never deletes**; guards source==target; skips locked files (warns to close SC); confirms before writing. Snapshots go to `Documents\SC-KeepAlive\Backups\<channel>-<timestamp>\`.
- **SC environment**: install root `C:\Program Files\Roberts Space Industries\StarCitizen`; channels are subfolders (`LIVE`, `HOTFIX`) — each has its OWN `user\`, and a patch overwrites `data\`.

## Review history
- Keep-alive (2026-06-18): adversarial review fixed 2 bugs — focus-guard fail-open → fail-closed; NumericUpDown out-of-range crash → clamp + sanitize.
- Backup (2026-06-19): adversarial review fixed 1 bug — a locked file mid-copy left a partial set and was mis-reported as "nothing copied" → `CopyTree` now skips locked files, counts real files, and warns.

## Conventions
- Don't commit `config.txt`, `config.json`, or `Backups/` (see `.gitignore`).
- This repo is the app's home. **Deep Star Citizen control-binding + StarStrings work is a SEPARATE project** rooted at the SC install directory (`C:\Program Files\Roberts Space Industries`) — do that work there, not here.
