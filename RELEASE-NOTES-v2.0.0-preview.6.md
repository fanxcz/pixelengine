# PixelEngine 2.0.0-preview.6

Windows x64 preview release.

## Changes

- Keeps the minimal, accessible project website and lists the original Bloody Pixels project separately from the new PixelEngine 2.0 runtime.
- Adds the preserved Bloody Pixels source trees to the release archive in their original layout: `PixelFight/`, `PixelFight.Editor/`, `PixelFight.Player/`, `PixelFight.Droid/`, `PixelFight.Tests/` and `avatar.png`.
- Preserves the original relative project references by keeping the legacy folders at the archive root rather than nesting or renaming them.
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
- `PixelFight/`, `PixelFight.Editor/`, `PixelFight.Player/`, `PixelFight.Droid/`, `PixelFight.Tests/` — original Bloody Pixels sources and tests. Their root-level layout is intentional because project references are relative.
- `avatar.png`, `BLOODY-PIXELS-LEGACY.md`, `PIXELENGINE_2_MIGRATION.md` — legacy artwork and development notes.
- `Website/` — static project website.
- `README.md`, `README.txt`, `LICENSE` and these release notes.
- SHA-256 checksum beside the ZIP.

## Verification

The release workflow restores and builds the PixelEngine solution, runs runtime smoke tests and Editor headless tests, restores/builds the original Bloody Pixels Windows projects, runs their regression suite, publishes Studio and Player, checks native SDL, validates the website and sample projects, verifies the legacy folders are present, builds the ZIP and generates SHA-256.

The Android MAUI source is included but is not built by the Windows release workflow. CI does not replace a real interactive Windows run with graphics and input devices. PixelEngine 2.0 remains a 2D runtime; its current Player does not execute imported C# scripts, and full gameplay parity with original Bloody Pixels is still in progress.

This is a preview release intended for testing and feedback.
