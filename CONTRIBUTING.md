# Contributing

## Build & run

```powershell
dotnet build Claude-Account-Switcher.csproj -c Debug     # build
dotnet run --project Claude-Account-Switcher.csproj      # run (tray)
powershell Installer\build-installer.ps1                 # build installer
```

Stop a running `Claude-Account-Switcher.exe` before rebuilding (the exe is locked):
`Get-Process Claude-Account-Switcher | Stop-Process -Force`.

To eyeball UI changes without a human, render a window to PNG:
`Claude-Account-Switcher.exe --screenshot out.png [--page main|settings|sessions]`.

## Code style

- Formatting, naming, and analyzer severities are defined in **`.editorconfig`** — Visual Studio,
  Rider, and VS Code (C# Dev Kit) apply it automatically. Key points: 4-space indent, CRLF, file-scoped
  namespaces, `_camelCase` private fields, PascalCase types/members, `var` only when the type is obvious.
- **XAML** is formatted with [XAML Styler](https://github.com/Xavalon/XamlStyler) using
  **`Settings.XamlStyler`** (one attribute per line, attribute reordering, format-on-save).
- Strict analyzer enforcement (**StyleCop + Roslynator**) is configured in `.editorconfig` /
  `stylecop.json` but **opt-in** — see the commented block in `Directory.Build.props`. Enabling it
  surfaces ~300 formatting warnings against the current terse one-liner style, so align the code first.

## Architecture — MVVM, no code-behind

This is an MVVM app (`Microsoft.Extensions.DependencyInjection` + `CommunityToolkit.Mvvm`). Keep
**Views free of code-behind logic**:

- User actions → **commands** (`[RelayCommand]` on the ViewModel, bound via `Command="{Binding ...}"`).
- Anything event-shaped → a **custom attached behavior** under `Behaviors/<TargetType>/` (an attached
  `DependencyProperty` that subscribes to the event and invokes a bound `ICommand`), not a `*.xaml.cs`
  handler.
- Keyboard shortcuts → native `InputBindings`/`KeyBinding` bound to commands.
- Modern C# (`LangVersion latest`): primary constructors for DI, collection expressions (`[]`),
  target-typed `new()`, pattern matching, `sealed` by default, `[GeneratedRegex]`, raw string literals.
- Value conversions → prefer `DataTrigger` styles or the built-in `BooleanToVisibilityConverter`
  (declared per view). There is no custom converter library — add one only when a real need appears.

See the WPF resource note in `CLAUDE.md`: in Views reference theme resources with `DynamicResource`;
each `Theme/Controls/*.xaml` merges its own dependency brushes (StaticResource closure caveat).

## Commits & releases

- Commit normally (descriptive message + `Co-Authored-By` trailer); don't squash/force-push. Author for
  this repo is `Kim, Hwan <akon47@naver.com>` (local git config).
- Release: bump `<Version>` and push a `vX.Y.Z` tag (or run the **bump-version** GitHub Action). The
  **release** workflow builds the self-contained installer and publishes the GitHub Release; the
  **winget** job then opens an update PR (needs the `WINGET_TOKEN` secret + `PUBLISH_WINGET=true` var).
- Don't put external project/brand names in code, comments, identifiers, or paths.
