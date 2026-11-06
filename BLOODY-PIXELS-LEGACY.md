# Bloody Pixels — legacy game sources

The repository preserves the original **Bloody Pixels** game alongside PixelEngine 2.0. These projects are kept as source code and are included in the Windows preview archive so that the original game and editor are not lost during the MonoGame migration.

## Preserved projects

- `PixelFight/` — original Windows Forms game and legacy runtime.
- `PixelFight.Editor/` — original Scene/Game editor.
- `PixelFight.Player/` — standalone player for legacy `.lic` projects.
- `PixelFight.Droid/` — original .NET MAUI Android source.
- `PixelFight.Tests/` — regression runner for the legacy game/runtime.
- `avatar.png` — shared source artwork used by the original projects.

The source directory layout is intentionally preserved at the archive root. The old project files use relative references such as `..\PixelFight\PixelFight.csproj` and `..\avatar.png`; moving the folders into a nested `Legacy/` directory would break those references.

The preview ZIP also contains self-contained Windows builds in `BloodyPixels/Windows/Game/`, `BloodyPixels/Windows/Editor/` and `BloodyPixels/Windows/Player/`. They run without installing the .NET SDK. The separate source trees remain available for rebuilding and modifying the original projects.

## Run on Windows

The legacy source projects require the .NET 8 SDK. From the extracted archive root:

```powershell
dotnet run --project .\PixelFight\PixelFight.csproj -c Release
dotnet run --project .\PixelFight.Editor\PixelFight.Editor.csproj -c Release
dotnet run --project .\PixelFight.Player\PixelFight.Player.csproj -c Release -- ".\Lic\world.lic"
dotnet run --project .\PixelFight.Tests\PixelFight.Tests.csproj -c Release
```

The Android project additionally requires the .NET MAUI Android workload and Android SDK:

```powershell
dotnet workload install maui-android
dotnet build .\PixelFight.Droid\PixelFight.Droid.csproj -c Release
```

## Relationship to PixelEngine 2.0

PixelEngine 2.0 uses a separate MonoGame runtime and Studio editor. The old Bloody Pixels projects are preserved as the original game/source version; they are not silently converted into the new runtime. The new Player supports the current LIC compatibility subset, and full gameplay/physics/rendering parity with the original Bloody Pixels runtime is still work in progress.

The legacy Windows projects are built and regression-tested by CI, then published as self-contained Windows x64 applications in the release ZIP. The Android source is packaged but is not built by the Windows release workflow.
