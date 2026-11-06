PIXELENGINE 2.0.0-PREVIEW.6 — WINDOWS X64

INSTALL
1. Extract the entire ZIP archive into its own folder.
2. Keep Lic, Games, and the complete Player folder beside PixelEngine.Editor.exe.
3. Keep the PixelFight* legacy source folders and root avatar.png in place; their project files use relative paths.
4. Do not move PixelEngine.Sandbox.exe out of Player; native SDL and .NET runtime files are stored beside it.
5. Run PixelEngine.Editor.exe. Lic\demo.lic opens by default.

PLAY — PIXELENGINE 2
- F5: launch the selected LIC project in a separate Player window.
- Shift+F5: stop the Player.

PLAY — ORIGINAL BLOODY PIXELS
- PixelFight\PixelFight.csproj — original Windows Forms game.
- PixelFight.Editor\PixelFight.Editor.csproj — original editor.
- PixelFight.Player\PixelFight.Player.csproj — legacy LIC Player.
- PixelFight.Droid\ — original MAUI Android source (requires MAUI Android workload).
- PixelFight.Tests\ — legacy runtime regression suite.

INCLUDED PROJECTS
- Lic\ — all built-in LIC samples and showcase projects.
- Games\Ashfall\ — Ashfall.lic, Lua source, assets, preview and documentation.
- PixelFight*\ — original Bloody Pixels game/editor/player/Android source and tests, with original relative paths preserved.
- BloodyPixels\Windows\Game\PixelFight.exe — ready-to-run original Windows game.
- BloodyPixels\Windows\Editor\PixelFightEditor.exe — ready-to-run original editor.
- BloodyPixels\Windows\Player\PixelPlayer.exe — ready-to-run legacy LIC Player; game.lic opens by default.

PACKAGE
- PixelEngine.Editor.exe — Windows Studio editor.
- Player\PixelEngine.Sandbox.exe — self-contained MonoGame Player.
- Player\ — complete .NET publish output, including SDL2.dll and native runtime dependencies.
- Lic\ and Games\ — sample projects and complete game sources/assets.
- PixelFight\, PixelFight.Editor\, PixelFight.Player\, PixelFight.Droid\, PixelFight.Tests\ — preserved legacy source trees. The folder layout is intentional because project references are relative.
- Website\ — static project website (HTML, CSS, favicon, sitemap and robots.txt).
- README.md, BLOODY-PIXELS-LEGACY.md, LICENSE and RELEASE-NOTES.md.
- SHA-256 checksum beside the ZIP.

REQUIREMENTS
- PixelEngine 2.0 release: Windows 10/11 x64. No .NET SDK required for the self-contained editor/player package.
- Legacy Windows source projects: .NET 8 SDK. Packaged legacy Windows apps are self-contained.
- Legacy Android source: .NET MAUI Android workload and Android SDK.
- MonoGame Player: compatible graphics driver/OpenGL.

NOTES
- Preview release.
- Current PixelEngine runtime is 2D; a full 3D workspace is not implemented.
- Lua scripts run through MoonSharp SoftSandbox.
- C# scripts are retained as editable source but are not executed by the current PixelEngine Player.
- The original Bloody Pixels game is preserved as a separate legacy implementation; full gameplay parity has not yet been ported to PixelEngine 2.0.
- Project source is MIT-licensed; third-party dependencies and assets may retain separate terms.
- Startup logs: %LOCALAPPDATA%\PixelEngine\Logs\

Official repository: https://github.com/fanxcz/pixelengine
Release: https://github.com/fanxcz/pixelengine/releases/tag/v2.0.0-preview.6
