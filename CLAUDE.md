# StarMaster — Star Citizen helper app

**StarMaster is a Windows app the user is building up** as their personal Star Citizen toolkit — a single dependency-free program (needs only the .NET Framework Windows ships; **no NuGet/MSBuild/internet** to build). As of v6 the UI is **code-only WPF** (vector, auto-DPI, resizable) in an **Aurora dashboard** (cyan→violet on near-black). Three tools + a GitHub self-updater. **Current version: 11.**

**Repo:** `elliot-borst/StarMaster` — **public** (so the in-app updater reads Releases anonymously). Locked down: no collaborators, Issues/Projects/Discussions disabled. Local `C:\GitHub\StarMaster`. Run Claude Code **from this folder**. The user's Star Citizen control *bindings* are a SEPARATE repo — **StarBinding** — not here.

**Git identity:** commit as **Elliot Borst** `<61570912+elliot-borst@users.noreply.github.com>`. **No `Co-Authored-By: Claude` trailers** (user is the sole contributor).

**Distribution:** binaries ship as **GitHub Releases** tagged `vN` (integer scheme; not committed). Each release attaches `StarMaster.exe` (portable) + `StarMaster-Setup.exe` (installer). Cut a release: bump `MainWindow.Version` + `installer.iss` `MyAppVersion`, rebuild both, commit/push, `gh release create vN StarMaster.exe StarMaster-Setup.exe --title "StarMaster vN" --notes "..."`. (The installer is per-user, no UAC; the in-app updater downloads + runs it for installed copies.)

## Tools / features
1. **Keep-Alive** — a 1 s DispatcherTimer sends a configurable keystroke **only while Star Citizen is the active window** (focus guard, fail-closed). Defaults: *Wipe Visor* (Left Alt + X, 600 s, ON) and *Auto Accept* (`[`, 1 s, OFF). Add/edit/remove via `AddKeyDialog`. Keystrokes are sent as **hardware scan codes** (essential — SC uses raw input; vk-only does nothing). The actual send is queued **off the UI thread** (Press sleeps 40 ms).
2. **Backup / Restore** — copies `user\`, `data\Localization\`, `user.cfg` between channels (LIVE/HOTFIX) or saved snapshots; overwrites, never deletes; guards against same-source==target. `BackupOps`.
3. **StarStrings** — installs/updates [MrKraken's StarStrings](https://github.com/MrKraken/StarStrings) (rolling `latest` release; build = date+commit). Downloads the zip, copies `Data\`→`<channel>\data`, merges `user.cfg`. Installed build tracked in `config.txt`.
- **Self-update** — on launch + header "Check for updates": reads GitHub `releases/latest`; if newer, a prompt offers to download+run the installer (installed copies) or open the Releases page (portable).
- **Close-to-tray** — X hides to a `NotifyIcon` (keep-alive keeps running); restore/quit via the tray menu. Window is resizable/maximizable (native WPF).

## Architecture
- `StarMaster.cs` — namespace `StarMaster`. **Logic (UI-agnostic, reused since the WinForms era):** `Native` (scan-code `Press` + foreground-window P/Invoke), `Vk` (key map incl. `[` `]`), `Cmd`, `TimedWebClient`, `Updater` (self-update + `ParseVer`/`Compare`), `StarStrings` (check/install). **WPF UI:** `Ui` (Aurora brushes/fonts + `AccentGrad`), widget helpers (`Btn`, `Toggle`, `Check`, `TextField`, `Dropdown` custom dark combo), `AddKeyDialog`, `MainWindow` (the dashboard — built as a Grid of cards; config load/save; timer; tray; updater — split across two `partial` blocks). `App.Main` (`[STAThread]`, temp-installer cleanup, `Application.Run`).
- `BackupForm.cs` — now **`BackupOps`** (plain static backup file-ops; `CopyTree` skips locked files). *(File kept its name; class is no longer a Form.)*
- `app.manifest` — embedded via `/win32manifest`; DPI-aware (WPF renders crisp).
- `Make-Icon.ps1` — regenerates `StarMaster.ico`: an **Aurora sparkle-star** (4-point ✦, cyan→violet gradient + glow) on a dark tile. The in-app header logo (`MainWindow.Header`) is the matching WPF `Polygon` star.
- `StarMaster.ps1` + `StarMaster-Startup.vbs` — old PowerShell version + launcher. **The C# `.exe` is primary**; only touch the `.ps1` if asked.
- `installer.iss` — Inno Setup → `StarMaster-Setup.exe`; installs to `%localappdata%\StarMaster`; `AppPublisher`="Elliot Borst", `UninstallDisplayName`="StarMaster", `CloseApplications=yes`.

## Build (no Node / NuGet / MSBuild / internet)
```
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /win32icon:StarMaster.ico /win32manifest:app.manifest /out:StarMaster.exe /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll StarMaster.cs BackupForm.cs
```
- **It's code-only WPF** — the WPF refs (full framework paths; the 3 `WPF\` assemblies + `System.Xaml`) are required. WinForms/Drawing are for the tray `NotifyIcon`; Compression for the StarStrings zip.
- **Close any running `StarMaster.exe` first** (or csc can't overwrite it). When iterating, close only the repo instance (`Where-Object { $_.Path -like 'C:\GitHub\StarMaster\*' }`) so the user's installed copy is left alone.
- Source is **ASCII-only** except a few UI glyph literals (✦/↻/▾ etc.) which the Roslyn csc handles fine without `/codepage`.
- C# language version is **5** (this csc) — no string interpolation / `?.` / expression-bodied members.
- The built `.exe` is gitignored (ships via Releases). Re-run `Make-Icon.ps1` only when the icon changes.
- Installer: `ISCC.exe` (Inno Setup 6 via winget at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`) → `& "<that path>" installer.iss` → `StarMaster-Setup.exe` (build the exe first; it's embedded).

## Conventions / facts
- **Config** = `config.txt` next to the exe (NOT committed). `key=value` (`autostart`, `focusguard`, `wintitle`, `starstrings_build/root/channel`) + command rows `Label|Shift|Ctrl|Alt|Key|Interval|Enabled`. Interval clamped 1–3600 s. App seeds defaults on first run.
- **Focus guard fails CLOSED** — blank title → sends nothing. SC's window title is `"Star Citizen "` (matches the default contains-check).
- **Game input needs SCAN CODES** (`Native.Press`).
- **Version** = `MainWindow.Version` const (`"11"`); must match the Release tag (`vN`) and `installer.iss` `MyAppVersion`. Bump `MainWindow.VersionDate` (shown in the header dashboard) at the same time.
- **WPF UI thread:** worker (ThreadPool) callbacks touch UI only via `Dispatcher.BeginInvoke`. Keep-alive sends are queued off-thread.
- Don't commit `config.txt`, `config.json`, `Backups/`, `StarMaster.exe`, or `StarMaster-Setup.exe`.
- **SC environment:** root `C:\Program Files\Roberts Space Industries\StarCitizen`; channels `LIVE`/`HOTFIX`.

## Review history
- 2026-06-18/19: v1–v2 (focus-guard fail-closed, backup locked-file handling, dark HUD GUI, self-updater, public repo). v2 fixed blurry GUI (DPI manifest + manual scaling).
- 2026-06-19: **v3** StarStrings tool + single-window 2-col layout + scan-code input; **v4** Auto Accept default + close-to-tray; **v5** resizable window. Repo history re-authored solely to Elliot Borst.
- 2026-06-19: **v6** — full **WPF rewrite** (code-only, csc-built): Aurora dashboard-cards UI, vector/auto-DPI, new sparkle-star icon. Adversarial review fixed 11 issues (tray-icon-null → unreachable fallback; restored same-source==target copy guard; keep-alive send moved off the UI thread; ported installer-temp cleanup; StarStrings channel detection; preserve LastFire on edit; "Open backups folder"; Dropdown value validation; docs build command).
- 2026-06-20: **v7** — "Start minimised to tray" toggle (Keep-Alive card; launches hidden to tray, `startminimized` in config).
- 2026-06-20: **v8** — update-check result now shows as inline status text left of the header "Check for updates" button (auto-clears after 5 s) instead of a MessageBox; status badges ("Running" / "up to date") moved to the top-right of their cards.
- 2026-06-20: **v9** — "Start minimised" toggle moved from the Keep-Alive card to the top bar (it's an app-wide setting, not keep-alive-specific).
- 2026-06-20: **v10** — in-app "update available" banner (under the header, Download/Later buttons) replaces the Yes/No update popup.
- 2026-06-20: **v11** — header redesign: left-aligned dashboard section (version + `VersionDate` + Start-minimised toggle); the "Check for updates" button label now doubles as the status ("Up to date" / "Update available" / "Check failed"), replacing the inline flash message; removed the title version pill. StarStrings card now credits MrKraken with a clickable link to his repo.

## Backlog / ideas
Multiple per-window keystroke profiles; back up the VoiceAttack profile; **sign `StarMaster-Setup.exe`** (kills SmartScreen warning); per-monitor-V2 DPI; theme the self-update prompt as an in-app banner (currently a MessageBox). *Done: v2 updater + dark UI + high-DPI; v3 StarStrings + single window + scan codes; v4 Auto Accept + tray; v5 resizable; v6 WPF Aurora dashboard rewrite.*

> This file is the app's handoff doc — a fresh Claude Code session opened in this folder has everything it needs to continue.
