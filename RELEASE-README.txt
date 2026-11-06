PIXELENGINE 2.0.0-PREVIEW.5 — WINDOWS X64

INSTALL
1. Extract the entire ZIP archive into its own folder.
2. Keep Lic, Games, and the complete Player folder beside PixelEngine.Editor.exe.
3. Do not move PixelEngine.Sandbox.exe out of Player; native SDL and .NET runtime files are stored beside it.
4. Run PixelEngine.Editor.exe. Lic\demo.lic opens by default.

PLAY
- F5: launch the selected project in a separate Player window.
- Shift+F5: stop the Player.

INCLUDED PROJECTS
- Lic\ — all built-in LIC samples and showcase projects.
- Games\Ashfall\ — Ashfall.lic, Lua source, assets, preview and documentation.

PACKAGE
- PixelEngine.Editor.exe — Windows Studio editor.
- Player\PixelEngine.Sandbox.exe — self-contained MonoGame Player.
- Player\ — complete .NET publish output, including SDL2.dll and native runtime dependencies. Keep this folder intact.
- Lic\ — sample projects and LIC fixtures.
- Games\ — complete game projects, sources and assets.
- Website\ — static project website (HTML, CSS, favicon, sitemap and robots.txt).
- README.md, LICENSE and RELEASE-NOTES.md.
- SHA-256 checksum beside the ZIP.

REQUIREMENTS
- Windows 10/11 x64.
- A graphics driver/OpenGL environment supported by MonoGame for Player rendering.
- No .NET SDK is required for this self-contained release package.

NOTES
- Preview release.
- Current runtime is 2D; a full 3D workspace is not implemented.
- Lua scripts run through MoonSharp SoftSandbox.
- C# scripts are retained as editable source but are not executed by the current Player.
- Project source is MIT-licensed; third-party dependencies and assets may retain separate terms.
- Startup logs: %LOCALAPPDATA%\PixelEngine\Logs\

Official repository: https://github.com/fanxcz/pixelengine
Release: https://github.com/fanxcz/pixelengine/releases/tag/v2.0.0-preview.5
