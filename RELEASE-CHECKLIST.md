# PixelEngine 2.0.0-preview.1 — release readiness

## Automated release gate

This revision also fixes all download links to target the actual preview release instead of GitHub's stable-only `releases/latest` endpoint.

The GitHub Actions release workflow runs on Windows x64 and must complete all of these steps before publishing the GitHub Release:

- Restore the .NET 8 solution.
- Build the complete solution in Release.
- Run runtime smoke tests.
- Run editor headless regression tests.
- Publish self-contained Windows x64 Studio and Player executables.
- Verify the demo LIC and static website files exist.
- Package the Studio, Player, LIC samples, README, release notes and website into one ZIP.
- Generate and upload a SHA-256 checksum.

## Manual checks still required

These are intentionally not represented as CI-passed:

- On a real Windows 10/11 x64 machine, launch `PixelEngine.Editor.exe` from the extracted ZIP without the source repository or .NET SDK.
- Open the bundled `Lic/demo.lic`, press F5, confirm the separate Player window renders, then stop it with Shift+F5.
- Inspect resize, minimum window size, DPI scaling, keyboard/mouse interaction and CMD/PowerShell terminal behavior.
- Enable GitHub Pages in **Settings → Pages → Build and deployment → Source → GitHub Actions**. The first deploy attempt failed because Pages was not enabled in repository settings.
- Choose and add a `LICENSE` file before describing the repository as open source or granting redistribution rights. No license file is currently present.

## Release scope

This is a **preview** for Windows x64, not a stable or feature-complete release. The runtime is 2D. Lua scripts run through MoonSharp SoftSandbox; C# scripts remain editable source and are not executed by the current Player. The integrated terminal is not a full VT100/ANSI implementation.

## Preview 2 link validation

- The repository README and website CTAs point directly to `/releases/tag/v2.0.0-preview.2`.
- The release ZIP contains the same README and website files, plus the standalone Windows Studio and Player executables.
- The release pipeline publishes a SHA-256 checksum alongside the ZIP.
