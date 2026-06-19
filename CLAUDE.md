# StarMaster — Star Citizen helper app

**StarMaster is a Windows app the user is building up** as their personal Star Citizen toolkit — a single dependency-free C# WinForms program (needs only the .NET Framework Windows ships). Two tools today (keep-alive, backup) plus a GitHub self-updater and a dark "HUD" UI; more planned. **Current version: 2.**

**Repo:** `elliot-borst/StarMaster` — **public** (so the in-app updater can read Releases anonymously). Locked down: no collaborators, Issues/Projects/Discussions disabled. (Forking can't be disabled on a personal public repo, but with no collaborators nobody can push/merge.) Local `C:\GitHub\StarMaster`. Run Claude Code **from this folder** to work on it. The user's Star Citizen control *bindings* are a SEPARATE repo/project — **StarBinding** — not here.

**Distribution:** binaries ship as **GitHub Releases** tagged `vN` (not committed). Each release attaches `StarMaster.exe` (portable) and ideally `StarMaster-Setup.exe` (installer). To cut a release: bump `MainForm.Version`, rebuild, commit/push, then `gh release create vN StarMaster.exe [StarMaster-Setup.exe] --title "StarMaster vN" --notes "..."`.

## Features today
1. **Keep-alive (anti-idle).** A timer sends a harmless, configurable keystroke (default *Wipe Visor* = **Left Alt + X**) **only while Star Citizen is the active window** (focus guard, fail-closed), so the user isn't idle-logged-out. Replaces a VoiceAttack scheduled command.
2. **Backup / Restore** ("Backup / Restore..." button → `BackupForm`). Copies what a patch/channel-switch wipes — `user\` (settings + bindings), `data\Localization\` (StarStrings mod), `user.cfg` — between SC channels (LIVE/HOTFIX) or saved snapshots. Overwrites but never deletes; only those 3 sub-paths; skips locked files with a warning; confirms first. Snapshots → `Documents\StarMaster\Backups\<channel>-<timestamp>\`.
3. **Auto-update (notify + 1-click install).** On launch and via the header "Check for updates" button, the app reads GitHub's public `releases/latest` (anonymous, TLS 1.2, on a background thread) and shows an amber banner if a newer `vN` tag exists. An **installed** copy downloads `StarMaster-Setup.exe` and runs it; a **portable** copy (or a release with no setup asset) just opens the Releases page. `Updater` class.

## Architecture
- `StarMaster.cs` — the app, namespace `StarMaster`. Classes:
  - `Native` — `keybd_event` + foreground-window P/Invoke.
  - `Vk` — key-name → virtual-key map.
  - `Cmd` — one scheduled keystroke.
  - `Theme` — dark HUD palette/fonts + button/input/checkbox styling, recursive `Apply`, and `DpiFactor`/`ScaleControls` helpers.
  - `TimedWebClient` — `WebClient` with a real timeout (stalled downloads fail fast).
  - `Updater` — checks/parses GitHub `releases/latest`, multi-part version compare (`ParseVer`/`Compare`), downloads the installer.
  - `MainForm` — built from docked `header`/`banner`/`content` panels, an owner-drawn command list, a hidden update banner, and `ScaleToDpi()` (manual high-DPI layout scaling). Keep-alive 1 s timer + config load/save are unchanged from v1.
  - `Main()` — `EnableVisualStyles`, temp-installer cleanup, run.
- `BackupForm.cs` — the Backup/Restore dialog (same namespace), themed via `Theme.Apply` + `ScaleControls`. `CopyTree` is resilient — skips locked files and counts them rather than aborting.
- `app.manifest` — embedded via `/win32manifest`; declares **DPI awareness** so Windows renders the UI crisp instead of bitmap-stretching it on scaled displays. Without it the manual scaling can't read the true DPI.
- `StarMaster.ps1` + `StarMaster-Startup.vbs` — an older PowerShell version + a hidden (no-console) launcher pointing at `C:\GitHub\StarMaster\StarMaster.ps1`. **The C# `.exe` is the primary**; only touch the `.ps1` if asked.
- `Make-Icon.ps1` — regenerates `StarMaster.ico` (GDI+, offline; amber HUD pulse on a dark panel).
- `installer.iss` — Inno Setup → `StarMaster-Setup.exe` (Start-menu + optional run-on-startup + uninstaller). Installs to `%localappdata%\StarMaster`; `CloseApplications=yes` so an in-app update can replace a running copy.

## Build (no Node / NuGet / internet)
```
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /win32icon:StarMaster.ico /win32manifest:app.manifest /out:StarMaster.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll StarMaster.cs BackupForm.cs
```
- **`/win32manifest:app.manifest` is required** (DPI awareness) — drop it and the GUI goes blurry on scaled displays.
- **Close any running `StarMaster.exe` first**, or the compiler can't overwrite it (`CS0016 ... being used by another process`).
- Source is **ASCII-only** (no `/codepage` needed) — keep it that way.
- The built `.exe` is gitignored (distributed via Releases). Re-run `Make-Icon.ps1` only when the icon changes.
- Installer: `ISCC.exe` (Inno Setup 6, installed via winget at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`) → `& "<that path>" installer.iss` produces `StarMaster-Setup.exe`, then `gh release upload vN StarMaster-Setup.exe`. (`StarMaster.exe` must be built first; it gets embedded.)

## Conventions / facts
- **Config** = `config.txt` next to the exe (NOT committed). `key=value` lines (`autostart`, `focusguard`, `wintitle`) + command rows `Label|Shift|Ctrl|Alt|Key|Interval|Enabled`. Interval clamped 5–3600 s. App writes its own defaults (Wipe Visor) on first run, so an end user only needs the exe.
- **Focus guard fails CLOSED** — blank title box → sends nothing (never blast keys into the wrong app). Case-insensitive match; default "Star Citizen".
- **Wipe-visor key = Left Alt + X** (SC action `visor_wipe`).
- **Version** = `MainForm.Version` const (currently `"2"`); shown in title/header and **must match the GitHub Release tag** (`vN`). Bump per release.
- **High-DPI:** crispness = `app.manifest` (DPI-aware); correct sizing = `ScaleToDpi()` scaling every control's bounds by the DPI factor (point fonts auto-scale). `AutoScaleMode = None` on both forms — WinForms `AutoScaleMode.Dpi` did **not** scale the hand-coded layout.
- Don't commit `config.txt`, `config.json`, `Backups/`, `StarMaster.exe`, or `StarMaster-Setup.exe` (see `.gitignore`).
- **SC environment:** install root `C:\Program Files\Roberts Space Industries\StarCitizen`; channels `LIVE` / `HOTFIX` (each has its own `user\`; a patch overwrites `data\`).

## Review history
- 2026-06-18: focus-guard fail-open → fail-closed; NumericUpDown out-of-range crash → clamp + sanitize.
- 2026-06-19: backup locked-file mid-copy left a partial set and mis-reported "nothing copied" → `CopyTree` now skips locked files, counts them, and warns.
- 2026-06-19: **v2** — dark HUD GUI revamp, GitHub self-updater, installer version sync, repo made public + locked down. Adversarial review caught & fixed 7 issues (multi-part version compare; only `*setup*.exe` counts as an installer; download timeout via `TimedWebClient`; ASCII-only source; portable-copy update opens the page instead of diverging; temp-installer cleanup; icon-handle dispose). Then fixed a blurry GUI on scaled displays — embedded a DPI-awareness manifest + manual `ScaleToDpi()` (the in-code `SetProcessDPIAware()` ran too late and `AutoScaleMode.Dpi` didn't scale the hand-coded layout).

## Backlog / ideas
System-tray minimize; multiple per-window keystroke profiles; option to back up the VoiceAttack profile too; **sign `StarMaster-Setup.exe`** (kills the SmartScreen warning on download); per-monitor-V2 DPI (currently system-DPI-aware only). *Done in v2: GitHub auto-updater, modern dark UI, high-DPI fix.*

> This file is the app's handoff doc — a fresh Claude Code session opened in this folder has everything it needs to continue.
