# winget packaging

These are the [winget](https://learn.microsoft.com/windows/package-manager/) manifests for
**Claude Account Switcher**. They are submitted to the community repo
[`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs); the copies here are kept in
sync so maintainers can review/update them in this repo.

Once published, users install with:

```powershell
winget install akon47.ClaudeAccountSwitcher
```

## Release checklist (each new version)

1. **Build the installer**

   ```powershell
   powershell Installer\build-installer.ps1
   # -> dist\Claude-Account-Switcher-Setup_vX.Y.Z.exe  (NSIS, per-user, supports /S silent)
   ```

2. **Create a GitHub Release** tagged `vX.Y.Z` and upload `Claude-Account-Switcher-Setup_vX.Y.Z.exe` as an asset.
   The download URL must be the stable release-asset URL:
   `https://github.com/akon47/claude-account-switcher-windows/releases/download/vX.Y.Z/Claude-Account-Switcher-Setup_vX.Y.Z.exe`

3. **Compute the installer hash** and put it in `*.installer.yaml` → `InstallerSha256`:

   ```powershell
   (Get-FileHash dist\Claude-Account-Switcher-Setup_vX.Y.Z.exe -Algorithm SHA256).Hash
   ```

4. **Bump versions** in all three manifests (`PackageVersion`), the `InstallerUrl` tag, and `ReleaseDate`.

5. **Validate**, then submit a PR to `winget-pkgs`. Easiest path is
   [`wingetcreate`](https://github.com/microsoft/winget-create):

   ```powershell
   winget install Microsoft.WingetCreate
   wingetcreate update akon47.ClaudeAccountSwitcher --version X.Y.Z `
     --urls https://github.com/akon47/claude-account-switcher-windows/releases/download/vX.Y.Z/Claude-Account-Switcher-Setup_vX.Y.Z.exe `
     --submit
   # or validate the local manifests:
   winget validate --manifest winget
   ```

## Notes

- `InstallerType: nullsoft` (NSIS). Silent install via `/S` is supported, so winget's
  `silent` / `silentWithProgress` modes work.
- `Scope: user` — installs to `%LOCALAPPDATA%\Programs`, no admin required (matches the installer).
- The first submission to `winget-pkgs` also creates the `akon47` publisher folder and goes through
  community moderation; later versions are simple updates.
