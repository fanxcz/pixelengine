# PixelEngine 2.0.0-preview.5

Windows x64 preview release.

## Changes

- Replaced the project website with a minimal, accessible project page using a restrained dark palette, plain typography, direct links and no decorative editor mockup or gradient-heavy cards.
- Added Ashfall and the full `Games/` directory to the release package. This includes `Ashfall.lic`, editable Lua source, assets, preview image, documentation and validation notes.
- Updated README and release instructions with the complete sample-project inventory and actual runtime limitations.
- Expanded Editor headless checks to load every `.lic` project from both `Lic/` and `Games/`, and to verify Ashfall's embedded script and assets.
- Corrected release workflow filtering: any push to `main` whose commit message starts with `release: v` can build and publish a version derived from that message. Manual workflow dispatch now produces a verification artifact without creating a public release.
- Added release-time checks for the website's local assets, all bundled game projects and the Ashfall source/assets.
- Includes the Lua editor viewport/scroll event protection and Player startup exit-code fixes from the preceding commits.

## Package contents

- `PixelEngine.Editor.exe` — Windows Studio.
- `Player/` — self-contained MonoGame Player and its native dependencies, including SDL runtime files.
- `Lic/` — all built-in LIC samples and showcase projects.
- `Games/` — complete bundled game projects and assets, including Ashfall.
- `Website/` — project website HTML, CSS, favicon, sitemap and robots file.
- `README.md`, `README.txt`, `LICENSE` and these release notes.
- SHA-256 checksum beside the ZIP.

## Verification

The release workflow restores and builds Release, runs runtime smoke tests and Editor headless tests, publishes Studio and Player, checks the native SDL runtime, validates local website assets, verifies that the game samples are packaged, builds the ZIP and generates SHA-256.

Automated tests do not replace a real graphical launch test on the target Windows computer. MonoGame DesktopGL still requires a compatible OpenGL driver. Lua executes through MoonSharp SoftSandbox; C# script files remain editable source and are not executed by the current Player.

This is a preview release intended for testing and feedback.
