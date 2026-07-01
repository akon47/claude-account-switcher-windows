# Claude Account Switcher

> Switch and concurrently run multiple **Claude Code (CLI)** accounts on Windows — from the system tray.

*[한국어 README](README.ko.md)*

**Claude Account Switcher** lives in the Windows system tray and manages multiple Claude Code (CLI)
login accounts. Each account is kept as an isolated *profile*; switch the active account with one
click, or run several accounts side by side in parallel.

[![Release](https://img.shields.io/github/v/release/akon47/claude-account-switcher-windows?logo=github)](https://github.com/akon47/claude-account-switcher-windows/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/akon47/claude-account-switcher-windows/total?color=brightgreen)](https://github.com/akon47/claude-account-switcher-windows/releases)
![.NET](https://img.shields.io/badge/.NET%209-WPF-512BD4)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

<p align="center">
  <img src="docs/images/main-window.png" alt="Account manager — switch between Claude Code accounts with one click" width="720">
</p>

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
- **Session usage at a glance** — each account shows its remaining 5-hour session limit with a
  reset countdown. When the **weekly limit** is used up it shows **0%** with the time until the
  weekly reset (since a full 5-hour window can't be used while the weekly cap is exhausted).
- **Keep session alive** (per-account toggle) — the moment an account's 5-hour window resets, the app
  sends a tiny headless message so a fresh window starts right away. Runs while the tray app is resident.
- **Resume sessions across accounts** — browse any account's past Claude Code conversations and
  continue one under a *different* account (opens a copy, so the original stays put).
- **Account status line** — when running an account concurrently, claude shows a bottom status line
  with the account email · plan · name · live session %.
- **Run options**: choose **PowerShell or cmd**, and decide whether to pass
  `--dangerously-skip-permissions` (with an optional *don't ask again* that remembers your choice —
  reset it any time in **Settings**). Applies to Explorer right-click launches too.
- Optional Explorer right-click **"Run with Claude"** submenu (per account).
- Start with Windows (autostart).
- **Automatic updates** — checks GitHub Releases on startup (and on demand from the tray menu) and
  offers to download & install a newer version.
- **14 UI languages**, auto-detected from your Windows display language (English, 한국어, 日本語,
  简体中文, 繁體中文, Español, Français, Deutsch, Português, Русский, Italiano, Türkçe, Tiếng Việt,
  Bahasa Indonesia). Each is a self-describing `Localization/<culture>.json` discovered at runtime —
  adding a language is just dropping in a new JSON file.

## Screenshots

Right-click any folder in Explorer → **Run with Claude** → pick an account. It opens a terminal
in that folder running `claude` as the chosen account (no switching needed):

![Explorer right-click "Run with Claude" submenu](docs/images/explorer-context-menu.png)

<p align="center">
  <img src="docs/images/tray-menu.png" alt="System-tray menu with quick account switch" height="340">
  &nbsp;&nbsp;
  <img src="docs/images/settings.png" alt="Settings window" height="340">
</p>

<p align="center"><sub>Tray menu (quick switch &amp; toggles) · Settings</sub></p>

## Install

### winget

```powershell
winget install akon47.ClaudeAccountSwitcher
```
*A submission to [winget-pkgs](https://github.com/microsoft/winget-pkgs) is in review — this works once it's merged.*

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
