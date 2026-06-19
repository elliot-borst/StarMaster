# StarMaster — Star Citizen helper app

**StarMaster is a Windows app the user is building up** as their personal Star Citizen toolkit — a single dependency-free C# WinForms program (needs only the .NET Framework Windows ships). **Three tools** (keep-alive, backup, StarStrings) in one window, plus a GitHub self-updater and a dark "HUD" UI. **Current version: 3.**

**Repo:** `elliot-borst/StarMaster` — **public** (so the in-app updater can read Releases anonymously). Locked down: no collaborators, Issues/Projects/Discussions disabled. Local `C:\GitHub\StarMaster`. Run Claude Code **from this folder**. The user's Star Citizen control *bindings* are a SEPARATE repo/project — **StarBinding** — not here.

**Git identity:** commit as **Elliot Borst** `<61570912+elliot-borst@users.noreply.github.com>`. **Do NOT add `Co-Authored-By: Claude` trailers** — the user wants to be the sole contributor.

**Distribution:** binaries ship as **GitHub Releases** tagged `vN` (integer scheme; not committed). Each release attaches `StarMaster.exe` (portable) + `StarMaster-Setup.exe` (installer). To cut a release: bump `MainForm.Version` + `installer.iss` `MyAppVersion`, rebuild both, commit/push, then `gh release create vN StarMaster.exe StarMaster-Setup.exe --title "StarMaster vN" --notes "..."`.

## Tools / features
1. **Keep-Alive (anti-idle).** A 1 s timer sends a configurable keystroke (default *Wipe Visor* = **Left Alt + X**, every **600 s**) **only while Star Citizen is the active window** (focus guard, fail-closed), so the user isn't idle-logged-out. Replaces a VoiceAttack scheduled command.
2. **Backup / Restore.** Copies what a patch/channel-switch wipes — `user\` (settings + bindings), `data\Localization\`, `user.cfg` — between SC channels (LIVE/HOTFIX) or saved snapshots. Overwrites but never deletes; only those 3 sub-paths; skips locked files with a warning; confirms first. Snapshots → `Documents\StarMaster\Backups\<channel>-<timestamp>\`.
3. **StarStrings.** Keeps [MrKraken's StarStrings](https://github.com/MrKraken/StarStrings) community localization mod current. Shows installed build vs. latest, one-click download+install. It's a **rolling `latest` release** (`StarStrings.zip`) re-published every SC patch — versioned by build name (date+commit), not a clean `vN`. Install = download zip → copy its `Data\` folder into `<scRoot>\<channel>\data` + ensure `user.cfg` has `g_language = english` (append, never overwrite). Installed build tracked in `config.txt` (`starstrings_build`).
- **Auto-update (the app itself).** On launch + the header "Check for updates", reads GitHub's public `releases/latest` (anonymous, TLS 1.2, background thread); amber banner if a newer `vN` exists. An **installed** copy downloads `StarMaster-Setup.exe` and runs it; a **portable** copy opens the Releases page. `Updater` class.

## Architecture
- `StarMaster.cs` — the app, namespace `StarMaster`. Classes:
  - `Native` — P/Invoke. **`Press` sends HARDWARE SCAN CODES** (`keybd_event` + `KEYEVENTF_SCANCODE` + `MapVirtualKey`). This is essential: Star Citizen uses raw/DirectInput and **ignores vk-only synthetic keys** (the pre-v3 path did nothing in-game).
  - `Vk` — key-name → virtual-key map.
  - `Cmd` — one scheduled keystroke (default interval 600 s).
  - `Theme` — dark HUD palette/fonts + styling, recursive `Apply`, `DpiFactor`/`ScaleControls`.
  - `TimedWebClient` — `WebClient` with a timeout.
  - `Updater` — app self-update (GitHub `releases/latest`, multi-part `ParseVer`/`Compare`).
  - `StarStrings` — `CheckLatest` (MrKraken rolling release) + `Install` (download zip, copy `Data\`, merge `user.cfg`).
  - `MainForm` — **single fixed window**, header on top + a **2-column section layout** (no scrolling, no popups): Keep-Alive top-left, Backup/Restore top-right (an embedded `BackupControl`), StarStrings full-width underneath. `ScaleToDpi()` does manual high-DPI scaling. Owns config + keep-alive timer + both updaters' UI.
  - `Main()` — `EnableVisualStyles`, temp-installer cleanup, run.
- `BackupForm.cs` — defines **`BackupControl : UserControl`** (was a popup `Form` pre-v3; now embedded as a section). `CopyTree` skips locked files and counts them.
- `app.manifest` — embedded via `/win32manifest`; declares **DPI awareness** (crisp on scaled displays; required for `ScaleToDpi` to read the true DPI).
- `StarMaster.ps1` + `StarMaster-Startup.vbs` — older PowerShell version + hidden launcher. **The C# `.exe` is primary**; only touch the `.ps1` if asked.
- `Make-Icon.ps1` — regenerates `StarMaster.ico` (GDI+, offline; amber HUD pulse).
- `installer.iss` — Inno Setup → `StarMaster-Setup.exe`. Installs to `%localappdata%\StarMaster`; `AppPublisher`="Elliot Borst", `UninstallDisplayName`="StarMaster" (so Add/Remove shows "StarMaster", not "StarMaster version N"); `CloseApplications=yes`.

## Build (no Node / NuGet / internet)
```
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /win32icon:StarMaster.ico /win32manifest:app.manifest /out:StarMaster.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll StarMaster.cs BackupForm.cs
```
- **`/win32manifest:app.manifest` required** (DPI). **`System.IO.Compression*` refs required** (StarStrings unzips via `ZipFile`).
- **Close any running `StarMaster.exe` first** or the compiler can't overwrite it (`CS0016`).
- Source is **ASCII-only** (no `/codepage` needed).
- The built `.exe` is gitignored (ships via Releases). Re-run `Make-Icon.ps1` only when the icon changes.
- Installer: `ISCC.exe` (Inno Setup 6 via winget at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`) → `& "<that path>" installer.iss` → `StarMaster-Setup.exe` (build `StarMaster.exe` first; it's embedded).

## Conventions / facts
- **Config** = `config.txt` next to the exe (NOT committed). `key=value` (`autostart`, `focusguard`, `wintitle`, `starstrings_build/root/channel`) + command rows `Label|Shift|Ctrl|Alt|Key|Interval|Enabled`. Interval clamped 1–3600 s. App seeds defaults on first run — **Wipe Visor** (Alt+X, 600 s, enabled) and **Auto Accept** (`[`, 1 s, disabled) — so an end user only needs the exe. `Vk.Map` keys: A-Z, 0-9, F1-F12, Space/Enter/Tab/Esc, `[` `]`.
- **Focus guard fails CLOSED** — blank title → sends nothing. Case-insensitive contains; default "Star Citizen" (SC's actual window title is `"Star Citizen "`, which matches).
- **Game input needs SCAN CODES** — see `Native.Press`. vk-only does not register in SC.
- **Version** = `MainForm.Version` const (`"3"`); shown in title/header, **must match the Release tag** (`vN`) and `installer.iss` `MyAppVersion`.
- **High-DPI:** crispness = `app.manifest`; sizing = `ScaleToDpi()`. `AutoScaleMode = None`. The fixed window is ~1100×902 logical (fits the user's 4K @ 200% → ~1920×1032 logical desktop).
- Don't commit `config.txt`, `config.json`, `Backups/`, `StarMaster.exe`, or `StarMaster-Setup.exe`.
- **SC environment:** install root `C:\Program Files\Roberts Space Industries\StarCitizen`; channels `LIVE` / `HOTFIX`.

## Review history
- 2026-06-18: focus-guard fail-open → fail-closed; NumericUpDown clamp.
- 2026-06-19: backup locked-file mid-copy fix (`CopyTree` skips + counts locked files).
- 2026-06-19: **v2** — dark HUD GUI, GitHub self-updater, public + locked-down repo. Adversarial review fixed 7 issues. Then fixed blurry GUI (DPI manifest + manual `ScaleToDpi`). Repo history re-authored solely to Elliot Borst (Claude co-author trailers removed).
- 2026-06-19: **v3** — third tool **StarStrings**; consolidated into a single fixed **2-column window** (Backup is now an embedded `BackupControl`, no popup, no scroll); **scan-code keyboard input** (fixed keep-alive not registering in-game — SC reads raw input); default Wipe Visor interval → 600 s; installer publisher "Elliot Borst" + Add/Remove name "StarMaster". *Note: StarStrings install path is new — live-test before relying on it.*

## Backlog / ideas
System-tray minimize; multiple per-window keystroke profiles; back up the VoiceAttack profile; **sign `StarMaster-Setup.exe`** (kills SmartScreen warning); per-monitor-V2 DPI (currently system-DPI-aware). *Done: v2 auto-updater + modern UI + high-DPI; v3 StarStrings + single-window layout + scan-code input.*

> This file is the app's handoff doc — a fresh Claude Code session opened in this folder has everything it needs to continue.
