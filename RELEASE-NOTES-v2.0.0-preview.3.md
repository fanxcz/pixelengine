# PixelEngine 2.0.0-preview.3

Preview release for Windows x64.

## Changes

- Added the MIT license and included `LICENSE` in the distributable ZIP.
- Updated the project README and website to describe forking, editing and redistribution under MIT.
- Changed the website download buttons to point directly to the Windows x64 ZIP.
- Reworked the Pages workflow: it now validates the static site and performs a real deployment instead of succeeding while silently skipping because Pages is disabled.
- Made FileSystem action buttons wrap inside narrow docks.
- Owner-drawn ListView rows, report headers and ComboBox drop-down items now use the Studio dark palette with readable text.
- Applied dark title-bar styling when the form is shown.
- Reduced the minimum Studio window dimensions and adjusted automatic dock collapsing for smaller displays.
- Applied the Studio styling pass to the Stage 8 tools window.

## Package contents

- `PixelEngine.Editor.exe` — Windows Studio.
- `PixelEngine.Sandbox.exe` — MonoGame Player.
- `Lic/` — demo and test projects.
- `Website/` — static HTML, CSS, favicon, sitemap and robots file.
- `README.md`, `README.txt`, `LICENSE`, `RELEASE-NOTES.md`.
- SHA-256 checksum beside the ZIP.

## Verification

The release workflow runs restore, Release build, runtime smoke tests and editor headless tests before publishing self-contained Windows x64 binaries. It validates required package files and generates a SHA-256 checksum.

Automated tests do not replace manual verification of the WinForms interface and MonoGame rendering on a real Windows machine.

## Current limitations

- Preview for Windows x64; Studio uses WinForms.
- Runtime is 2D. The 3D workspace is not implemented.
- Lua runs through MoonSharp SoftSandbox. C# scripts remain editable source and are not executed by the current Player.
- The terminal is not a full VT100/ANSI implementation.
- GitHub Pages must be enabled once in repository Settings → Pages → Source → GitHub Actions before the site URL becomes available.
- Third-party libraries and assets may have separate license terms; review their notices before redistribution.

This is a preview release intended for testing and feedback.
