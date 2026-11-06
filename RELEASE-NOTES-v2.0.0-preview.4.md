# PixelEngine 2.0.0-preview.4

Preview release for Windows x64.

## Fixes

- Fixed a script-editor responsiveness problem: syntax highlighting now formats only complete lines in the visible viewport instead of applying thousands of native RichTextBox selection changes across the entire file.
- Script outline parsing is debounced; scrolling schedules a small visible-range highlight pass.
- The Windows Player is now published as a normal self-contained folder rather than a single-file executable. The complete Player directory is copied into the release ZIP so native MonoGame dependencies travel with it.
- The release workflow verifies that SDL2.dll exists both in the publish output and in the final package. It refuses to publish an archive with the native SDL runtime missing.
- Studio now discovers Player/PixelEngine.Sandbox.exe while remaining compatible with older preview packages that had the Player executable beside Studio.
- Player starts with its own dependency directory as the working directory.
- Player startup dialog now shows the innermost exception message, and the startup log records process architecture and application base directory.
- Added an automated test for viewport-range calculation in the script editor.

## Package contents

- PixelEngine.Editor.exe — Windows Studio.
- Player/PixelEngine.Sandbox.exe — MonoGame Player.
- Player/ — complete self-contained .NET publish output, including SDL2.dll and native/runtime dependencies. Do not move the executable out of this directory.
- Lic/ — demo and test projects.
- Website/ — static HTML, CSS, favicon, sitemap and robots file.
- README.md, README.txt, LICENSE, RELEASE-NOTES.md.
- SHA-256 checksum beside the ZIP.

## Verification

The CI workflow runs Debug and Release builds, runtime smoke tests and editor headless tests. The release workflow runs Release build and tests, publishes Studio and Player, checks for native SDL, assembles the package and creates a SHA-256 checksum.

The CI checks confirm the code and package structure; they do not replace a real graphical launch test on the target Windows PC. MonoGame DesktopGL still needs a compatible OpenGL driver.

## Current limitations

- Preview for Windows x64; Studio uses WinForms.
- Runtime is 2D. The 3D workspace is not implemented.
- Lua runs through MoonSharp SoftSandbox. C# scripts remain editable source and are not executed by the current Player.
- The terminal is not a full VT100/ANSI implementation.
- Third-party libraries and assets may have separate license terms; review their notices before redistribution.

This preview includes fixes for the reported MonoGame SDL startup failure and script-editor freezing/scroll jumps on larger Lua files. It is intended for testing and feedback; verify the Player launch on the target Windows machine.
