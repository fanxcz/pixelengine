# PixelEngine 2.0 — Stage 3: Scene Management

This milestone adds safe multi-scene editing to the custom Windows Forms editor while keeping the legacy `.lic` v1 JSON schema unchanged.

## Editor features

- Scene selector in the top toolbar.
- New Scene (`Ctrl+Shift+N`).
- Duplicate Active Scene (copies scene settings, objects, polygon points, and unknown JSON extension fields).
- Delete Active Scene (asks for confirmation and prevents deleting the final scene).
- Set Active as Startup Scene.
- Startup scene is marked with `★` in Hierarchy and `★ Startup` in the selector.
- Changing the active scene updates Hierarchy, Scene View, Inspector and object creation target.
- The editor preserves the current active scene when refreshing the project tree. Opening a different project still starts from its configured startup scene.

## Core API

`PixelEngine.Serialization.LicProjectOperations` provides:

- `AddScene(project, requestedName?)`
- `DuplicateScene(project, sceneIndex)`
- `RemoveScene(project, sceneIndex)`
- `SetStartScene(project, sceneIndex)`

The operations protect the invariant that a project always has at least one scene, maintain `startScene` indexes, create independent object copies and preserve unrecognized JSON fields.

## Verification

`PixelEngine.SmokeTests` now exercises scene add/duplicate/delete/startup selection, deep-copy behavior, extension-data preservation and serialization round-trip in addition to the existing fixture imports.

Build and verify on Windows with .NET 8:

```powershell
dotnet clean PixelEngine.sln
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln
dotnet run --project PixelEngine.SmokeTests
```

This environment does not have the .NET SDK, so the updated solution has not been compiled here. The changes are source-level and must be validated with the commands above on the Windows development machine.
