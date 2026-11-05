# PixelEngine 2.0 — Stage 6: Adaptive Scene Studio

## What's new

### Adaptive editor layout
- The editor now uses nested resizable split containers for Hierarchy, Workspace, Inspector and Bottom Dock.
- Default dock proportions adapt to the actual client size.
- Dock widths/heights and collapsed panel visibility are restored from `%LOCALAPPDATA%\PixelEngine\EditorLayout.json`.
- `Ctrl+1` toggles Hierarchy, `Ctrl+2` toggles Inspector, `Ctrl+3` toggles Bottom Dock.
- `Ctrl+Shift+L` restores the default layout.
- `Ctrl+4`, `Ctrl+5`, `Ctrl+6` switch to Scene, Game and Script views.

### Script and debugging workflow
- The existing Godot-inspired script editor remains integrated with the Script workspace: line numbers, Lua/C# syntax colors, API completion, automatic pairs/indentation, bracket matching, bookmarks, function outline, find/replace and go-to-line.
- `Ctrl+Space` opens completion, `F2`/`Shift+F2` navigates bookmarks, `Ctrl+Shift+F2` toggles a bookmark, `Ctrl+F` searches, `Ctrl+L` jumps to a line, and `F1` opens the script API reference.
- MonoGame preview stdout/stderr is forwarded into the Output dock, so build/startup errors are visible inside the editor instead of getting lost in a separate console.

### Scene View
- Select (`Q`), Move (`W`), Rotate (`E`) and Scale (`R`) tools.
- Move tool supports center, X and Y handles.
- Rotate tool supports free rotation; with Snap enabled, rotation snaps to 15-degree increments.
- Scale tool has corner and axis handles; the local transform axes follow the object's rotation.
- Snap uses a 16-world-unit grid for move/scale.
- Middle or right mouse drag pans the view.
- Mouse-wheel zoom is anchored at the cursor.
- Fit frames all objects and considers rotation when calculating bounds.
- Collider overlay draws BoxCollider2D, CircleCollider2D, trigger and legacy `solid` outlines.
- Gizmo handles use hover cursor feedback.
- A selected object is moved only with the Move tool, not with Select.

### Transform serialization and runtime
- Object rotation is stored in LIC as `rotation` only when it is non-zero, preserving compatibility with old files.
- MonoGame Runtime rendering applies object rotation to supported sprites, rects, circles, text and polygons.
- The editor collider overlay is a visualization of authoring metadata. This stage does not implement a complete rigid-body collision solver for rotated bodies.

### Game View
- F5 launches the MonoGame Sandbox for the active `.lic` file.
- On Windows, the editor attempts to host the sandbox window in the Game View using native HWND parenting.
- If the native window cannot be detected or hosted (for example, due to a platform/driver limitation), the editor clearly reports the fallback and the game may remain in a separate window.
- Shift+F5 stops the preview.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| Q / W / E / R | Select / Move / Rotate / Scale (when Scene View has focus) |
| Middle or right drag | Pan Scene View |
| Mouse wheel | Zoom at cursor |
| Ctrl+Shift+F | Fit Scene |
| Ctrl+1 / Ctrl+2 / Ctrl+3 | Toggle Hierarchy / Inspector / Bottom Dock |
| Ctrl+4 / Ctrl+5 / Ctrl+6 | Scene / Game / Script view |
| Ctrl+Shift+L | Reset dock layout |
| F5 / Shift+F5 | Run / Stop preview |

## Build and run

```powershell
dotnet clean PixelEngine.sln
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln
dotnet run --project PixelEngine.SmokeTests
dotnet run --project PixelEngine.Editor
```

## Validation status

Static checks performed for this delivery:
- all six LIC fixtures parse as JSON;
- Base64 payloads found in LIC assets decode successfully;
- all project files parse as XML;
- solution project paths resolve;
- delimiter balance checked for the changed editor and native-window helper source;
- rotation and scene-operation smoke-test cases are present.

The current build container does not have the .NET SDK, so `dotnet build`, MonoGame window embedding and interactive UI behavior have not been executed here. Please run the commands above on Windows and share the first build/runtime error if one appears.
