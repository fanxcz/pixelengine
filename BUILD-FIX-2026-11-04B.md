# Stage 7 startup and prefab-test repair — 2026-11-04

## Reported symptoms
- SmokeTests failed only the prefab propagation check.
- `dotnet run --project PixelEngine.Editor` returned without visible diagnostics and the user reported no editor/player window.

## Changes
1. The prefab smoke test now assigns an explicit per-instance position (21, 22) and rotation (12 degrees) before applying template updates. The test now actually exercises the intended instance-override behavior instead of assuming `InstantiatePrefab` would produce 21, 22 from a source object whose serialized x/y default to zero.
2. Editor startup now records diagnostics to `%LOCALAPPDATA%\PixelEngine\Logs\editor-startup.log`, resolves the repository root and default `Lic\demo.lic` deterministically, and displays a message box if startup throws.
3. Unhandled WinForms UI exceptions are logged and displayed. AppDomain/background task exceptions are recorded.
4. The MonoGame Sandbox writes startup/failure diagnostics to `%LOCALAPPDATA%\PixelEngine\Logs\player-startup.log`; fatal player errors are shown in a Windows message box so the WinExe player cannot fail silently.

## Run
From the repository root:

```powershell
dotnet clean PixelEngine.sln
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln
dotnet run --project PixelEngine.SmokeTests
dotnet run --project PixelEngine.Editor
```

The editor opens the project. To start the game preview press **F5** in the editor. To run the player directly, use:

```powershell
dotnet run --project PixelEngine.Sandbox -- Lic/demo.lic
```

## Diagnostics
- Editor: `%LOCALAPPDATA%\PixelEngine\Logs\editor-startup.log`
- Player: `%LOCALAPPDATA%\PixelEngine\Logs\player-startup.log`

## Verification limit
The source and archive have been structurally checked in this environment. A Windows/.NET SDK build and GUI launch were not available here, so no claim of a successful live build is made.
