# PixelEngine 2.0 — Stage 7 follow-up

This update addresses two issues reported from the first Windows run of the Stage 7 quality-pass build.

## Prefab/component smoke test

- Object cloning now copies the `rotation` field, fixing rotation loss in scene duplication, prefab creation, prefab duplication and instantiation.
- Template data copying now includes rotation.
- Propagation and Revert explicitly preserve per-instance `X`, `Y` and `Rotation` overrides.
- The smoke test now verifies template rotation, instantiated rotation, preservation of instance transform overrides and provides separate diagnostics for each propagation check.

## Editor startup diagnostics

- Debug configuration builds the WinForms editor as a console-subsystem executable (`Exe`), so `dotnet run --project PixelEngine.Editor` prints startup progress and fatal exceptions.
- Release configuration remains a GUI-subsystem executable (`WinExe`), so published/Release users do not get a console window.
- Startup details continue to be appended to `%LOCALAPPDATA%\PixelEngine\Logs\editor-startup.log`.
- `run-editor.ps1` builds Debug, starts the actual editor executable, checks whether it exited immediately and displays the startup log if it did.

## Run

```powershell
dotnet clean PixelEngine.sln
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln
dotnet run --project PixelEngine.SmokeTests
dotnet run --project PixelEngine.Editor
```

Alternative diagnostic launcher:

```powershell
powershell -ExecutionPolicy Bypass -File .\run-editor.ps1
```

The archive was checked for ZIP integrity, project XML validity, LIC JSON validity, embedded Base64 validity and edited-source delimiter balance. The packaging environment does not contain the .NET SDK, so no local `dotnet build` or Windows GUI launch is claimed.
