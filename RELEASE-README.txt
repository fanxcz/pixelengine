PIXELENGINE 2.0.0-PREVIEW.4 — WINDOWS X64

INSTALL
1. Extract the entire ZIP archive into its own folder.
2. Keep the Lic folder and the complete Player folder beside PixelEngine.Editor.exe.
3. Do not move PixelEngine.Sandbox.exe out of Player; its native SDL runtime and .NET files are stored beside it.
4. Run PixelEngine.Editor.exe.
5. The editor opens Lic\demo.lic automatically.

PLAY
- F5: launch the selected project in a separate Player window.
- Shift+F5: stop the Player.

PACKAGE
- PixelEngine.Editor.exe — Windows Studio editor.
- Player\PixelEngine.Sandbox.exe — self-contained MonoGame Player.
- Player\ — complete .NET publish output, including SDL2.dll and native runtime dependencies. Keep this folder intact.
- Lic\ — sample projects and LIC fixtures.
- Website\ — complete static site (HTML, CSS, favicon, sitemap and robots.txt).
- README.md — project overview, source-build instructions and current limitations.
- LICENSE — MIT license for the project.
- RELEASE-NOTES.md — changes and known limitations for this preview.

REQUIREMENTS
- Windows 10/11 x64.
- A graphics driver/OpenGL environment supported by MonoGame for Player rendering.
- No .NET SDK is required for this self-contained release package.

NOTES
- This is a preview release.
- The current runtime is 2D. The 3D workspace is not implemented.
- Lua scripts run through MoonSharp SoftSandbox.
- C# scripts are retained as editable source and are not executed by the current Player.
- Source code is MIT-licensed; third-party dependencies and assets may retain separate terms.
- Startup logs: %LOCALAPPDATA%\PixelEngine\Logs\

Official repository: https://github.com/fanxcz/pixelengine
Release: https://github.com/fanxcz/pixelengine/releases/tag/v2.0.0-preview.4
