PIXELENGINE 2.0.0-PREVIEW.1 — WINDOWS X64

INSTALL
1. Extract the entire ZIP archive into its own folder.
2. Keep the Lic folder beside both executables.
3. Run PixelEngine.Editor.exe.
4. The editor opens Lic\demo.lic automatically.

PLAY
- F5: launch the selected project in a separate Player window.
- Shift+F5: stop the Player.

PACKAGE
- PixelEngine.Editor.exe — Windows Studio editor.
- PixelEngine.Sandbox.exe — self-contained MonoGame Player.
- Lic\ — sample projects and LIC fixtures.
- README.md — project overview, source-build instructions and current limitations.
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
- Startup logs: %LOCALAPPDATA%\PixelEngine\Logs\

Official repository: https://github.com/fanxcz/pixelengine
Releases: https://github.com/fanxcz/pixelengine/releases
