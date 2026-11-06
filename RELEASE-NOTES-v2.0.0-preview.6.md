# PixelEngine 2.0.0-preview.6

Windows x64 preview release.

## Changes

- Keeps the minimal, accessible project website and lists the original Bloody Pixels project separately from the new PixelEngine 2.0 runtime.
- Adds the preserved Bloody Pixels source trees to the release archive in their original layout: `PixelFight/`, `PixelFight.Editor/`, `PixelFight.Player/`, `PixelFight.Droid/`, `PixelFight.Tests/` and `avatar.png`.
- Preserves the original relative project references by keeping the legacy folders at the archive root rather than nesting or renaming them.
- Publishes ready-to-run self-contained Windows x64 builds of the original Bloody Pixels game, editor and Player into `BloodyPixels/Windows/`.
- Repairs the legacy game packaging script: it now uses repository-relative paths rather than a hard-coded developer path, creates six game-specific `.cmd` launchers and shares one self-contained Player instead of duplicating the .NET runtime six times.
- Fixes the legacy editor publish conflict: its own `avatar.png` and `world.lic` are copied once, while the referenced game project suppresses only duplicate transitive content. The editor keeps the required local assets and publishes without `NETSDK1152`.
- Adds a legacy guide describing how to run the original Windows game, editor, old Player, Android source and test runner.
- CI now restores and builds the legacy Windows projects and runs the Bloody Pixels regression suite in addition to PixelEngine 2.0 tests.
- Continues to include all `Lic/` samples and the full `Games/Ashfall/` project with Lua source and assets.
- Release workflow validates both the new runtime samples and the legacy source layout before publishing.
- Includes the minimal website, generalized release workflow, Lua editor viewport protection and Player startup error-code fix.

## Package contents

- `PixelEngine.Editor.exe` — PixelEngine 2.0 Windows Studio.
- `Player/` — self-contained MonoGame Player and native dependencies, including SDL.
- `Lic/` — current LIC sample and showcase projects.
- `Games/` — current game projects and assets, including Ashfall.
- `BloodyPixels/Windows/Game/`, `BloodyPixels/Windows/Editor/`, `BloodyPixels/Windows/Player/` — ready-to-run self-contained original Bloody Pixels Windows apps.
- `PixelFight/`, `PixelFight.Editor/`, `PixelFight.Player/`, `PixelFight.Droid/`, `PixelFight.Tests/` — original Bloody Pixels sources and tests. Their root-level layout is intentional because project references are relative.
- `Publish/Games/` — six legacy LIC game packages with `game.lic`, README and launch script.
- `Publish/PlayerSC/PixelPlayer.exe` — shared self-contained Player used by all six legacy game launchers.
- `avatar.png`, `BLOODY-PIXELS-LEGACY.md`, `PIXELENGINE_2_MIGRATION.md` — legacy artwork and development notes.
- `Website/` — static project website.
- `README.md`, `README.txt`, `LICENSE` and these release notes.
- SHA-256 checksum beside the ZIP.

## Verification

The release workflow restores and builds the PixelEngine solution, runs runtime smoke tests and Editor headless tests, restores/builds the original Bloody Pixels Windows projects, runs their regression suite, publishes all three legacy Windows apps, publishes Studio and the new Player, checks native SDL, validates the website and sample projects, verifies both legacy source trees, legacy Windows executables and all six launchers plus their shared runtime are present, builds the ZIP and generates SHA-256.

The Android MAUI source is included but is not built by the Windows release workflow; install the .NET MAUI Android workload and Android SDK to build it. CI does not replace a real interactive Windows run with graphics and input devices. PixelEngine 2.0 remains a 2D runtime; its current Player does not execute imported C# scripts, and full gameplay parity with original Bloody Pixels is still in progress.

This is a preview release intended for testing and feedback.
