# Claude Account Switcher

> Switch and concurrently run multiple **Claude Code (CLI)** accounts on Windows — from the system tray.

*[한국어 README](README.ko.md)*

**Claude Account Switcher** lives in the Windows system tray and manages multiple Claude Code (CLI)
login accounts. Each account is kept as an isolated *profile*; switch the active account with one
click, or run several accounts side by side in parallel.

![status](https://img.shields.io/badge/status-WIP-orange) ![.NET](https://img.shields.io/badge/.NET%209-WPF-512BD4)

## What it does

- **Switch** — make any saved account the active one with a single click. Your usual `claude`
  command then runs as that account.
- **Concurrent** — give each account its own isolated config folder and run different accounts in
  different terminals at the same time, with no conflicts.

## How it works

Claude Code stores its login in `~/.claude/.credentials.json` (OAuth token) and the account info
(email, plan…) in `~/.claude.json` (`oauthAccount`). This app keeps these per profile:

- **Switch** copies the selected profile's `.credentials.json` into `~/.claude/` and patches the
  `oauthAccount` in `~/.claude.json` so `claude /status` shows the right account. (The current
  credentials are backed up first, and the account you switch away from keeps its refreshed token.)
- **Concurrent** launches a new terminal with `CLAUDE_CONFIG_DIR=<profile folder>`, so it never
  touches `~/.claude` and accounts stay isolated.

Profile data lives in `%APPDATA%\ClaudeAccountSwitcher\` (not committed to the repo).

## Features

- System-tray resident, dark themed UI, custom window chrome.
- Capture the currently logged-in account, add a new account (isolated login), switch, rename, delete.
- Launch a profile in a new terminal in a chosen folder.
- **Run options**: choose **PowerShell or cmd**, and decide whether to pass
  `--dangerously-skip-permissions` (with an optional *don't ask again* that remembers your choice —
  reset it any time in **Settings**).
- Optional Explorer right-click **"Run with Claude"** submenu (per account).
- Start with Windows (autostart).
- **Automatic updates** — checks GitHub Releases on startup (and on demand from the tray menu) and
  offers to download & install a newer version.
- **14 UI languages**, auto-detected from your Windows display language (English, 한국어, 日本語,
  简体中文, 繁體中文, Español, Français, Deutsch, Português, Русский, Italiano, Türkçe, Tiếng Việt,
  Bahasa Indonesia). Each is a self-describing `Localization/<culture>.json` discovered at runtime —
  adding a language is just dropping in a new JSON file.

## Install

### winget (once published)

```powershell
winget install akon47.ClaudeAccountSwitcher
```

### Installer

Download `Claude-Account-Switcher-Setup_vX.Y.Z.exe` from the
[Releases](https://github.com/akon47/claude-account-switcher-windows/releases) page and run it.
It's a self-contained, per-user installer (`%LOCALAPPDATA%\Programs`, no admin, ~54 MB).

## Build from source

Requires the **.NET 9 SDK**. NSIS (`makensis`) is needed only to build the installer.

```powershell
dotnet build Claude-Account-Switcher.csproj -c Debug     # build
dotnet run --project Claude-Account-Switcher.csproj      # run (tray)
powershell Installer\build-installer.ps1                 # build installer -> dist\Claude-Account-Switcher-Setup.exe
```

## Releasing (maintainers)

Releases are automated with GitHub Actions:

- **`build`** — compiles on every push / PR to `main`.
- **`bump-version`** (manual) — bumps `<Version>` in the csproj, commits, and pushes a `vX.Y.Z` tag.
- **`release`** — on a `vX.Y.Z` tag, builds the self-contained installer and publishes a GitHub
  Release with `Claude-Account-Switcher-Setup.exe`.
- **`winget`** — on a published release, opens an update PR to `microsoft/winget-pkgs`
  (requires a `WINGET_TOKEN` repo secret; see [`winget/README.md`](winget/README.md)).

To cut a release: run **bump-version** with the new version (or push a `vX.Y.Z` tag yourself).

## Tech

- .NET 9 / WPF (Windows only), MVVM with `Microsoft.Extensions.DependencyInjection` +
  `CommunityToolkit.Mvvm`.
- Self-designed dark theme; system-tray via `H.NotifyIcon.Wpf`.

## Security note

Claude Code itself stores `.credentials.json` in plaintext on Windows. This app keeps per-account
credentials at the same level, under your user profile folder (`%APPDATA%`). DPAPI-based encryption
at rest is on the roadmap.

## License

[MIT](LICENSE)
