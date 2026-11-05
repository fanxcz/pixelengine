# PixelEngine 2.0 — Stage 7: Studio Quality Pass

Stage 7 focuses on the problems visible in the Stage 6 screenshot: the preview was embedded in the editor, the Inspector did not occupy the available dock area, repeated runtime messages flooded Output, and the sample project looked like untextured prototype geometry.

## Scene and Game workspace

- MonoGame preview launches as a separate, resizable Windows window.
- F5 launches/restarts the player; Shift+F5 stops it.
- Stop terminates the full `dotnet run` process tree, not just the CLI parent.
- The Game Preview tab is a status/launch panel; it does not reparent or embed a native game window.
- The Scene View has a grid, origin axes, world-space mouse coordinates, zoom readout and adaptive line spacing.
- Grid visibility, grid size, object labels and collider overlays can be toggled independently.
- Sprite assets are shown in the Scene View using a per-project decoded bitmap cache.
- Scene object drawing respects SpriteRenderer sorting order.
- Q/W/E/R switch Select/Move/Rotate/Scale tools.
- Move has X/Y handles; Rotate has an arc handle; Scale has edge/corner handles.
- Snap-to-grid applies to position and size; rotation snaps in 15-degree increments.
- Middle mouse pans; wheel zooms around the pointer; Fit frames the scene.
- F focuses the selected object.
- F2 renames the selected object and updates exact `api.find("name")` references in project scripts.
- Ctrl+D duplicates; Delete removes the selection.
- Right-click context menu creates objects at the mouse world position and offers focus/rename/duplicate/delete/align operations.
- Toolbar Create adds at the current scene view center rather than at the last pointer location.

## Layout and Inspector

- Hierarchy, Workspace, Inspector and Bottom Dock are nested, resizable SplitContainers.
- Inspector PropertyGrid is docked to Fill and uses categorized properties and the standard help panel.
- Component Manager fills its tab and exposes editable component metadata.
- Default dock proportions prioritize the Scene View while retaining readable Hierarchy and Inspector widths.
- Splitter positions and dock visibility are saved to `%LOCALAPPDATA%/PixelEngine/EditorLayout.v2.json`.
- Reset Dock Layout restores defaults; Ctrl+1/2/3 toggles main docks; Ctrl+4/5/6 switches Scene/Game/Script tabs.
- The form uses DPI-aware WinForms scaling and a maximized startup window.

## Output and script workflow

- Output has All/Errors/Warnings/Player/Editor category filtering.
- Output supports text filtering, clear, copy and export to TXT/LOG.
- Identical worker messages emitted in a short burst are grouped into one line with a repeat count.
- Crystal Valley no longer logs `pew` for every shot.
- Script tabs support multiple open scripts and close/close-others/close-all context actions.
- Code editor includes line numbers, Lua/C# syntax coloring, API completion, API reference, code outline, diagnostics, auto-indent, pair insertion, bracket matching, search/replace, line comments, duplicate/delete/move line, bookmarks, reindent and font zoom.
- Completion can be invoked with Ctrl+Space and is refreshed as the user types a token or member access.

## Sample game: Crystal Valley

`Lic/demo.lic` has been replaced with a varied 2D sample rather than plain placeholder blocks:

- 54 named scene objects with layered hills, clouds, trees, grass, platforms, a sign, portal, crystals and coins.
- Four embedded pixel-art PNG assets: hero, goblin, bolt and crystal.
- Player and enemy use SpriteRenderer, Rigidbody2D and BoxCollider2D authoring data.
- A/D or arrow keys move; W/Up/Space jumps; F fires projectiles.
- The enemy patrols and approaches the player; projectiles have a lifetime and remove enemy hit points.
- All sample scripts are Lua; there is no retained C# enemy script that would only generate a warning.
- Repeated shot logging has been removed.

The compatibility runtime still intentionally does not execute imported C# scripts. The 2D physics solver is a compatibility slice (gravity, ground and platform tops), not a complete Box2D implementation; component metadata is authoring data unless a runtime system explicitly consumes it.

## 30 editor improvements available in this build

1. Separate game window.
2. Process-tree stop.
3. F5 run/restart.
4. Shift+F5 stop.
5. Inspector fills its tab.
6. Categorized Inspector properties.
7. Resizable Hierarchy dock.
8. Resizable Inspector dock.
9. Resizable Bottom Dock.
10. Persistent dock layout.
11. Reset layout.
12. DPI-aware scaling.
13. Output category filter.
14. Output text search.
15. Output clear.
16. Output copy.
17. Output export.
18. Repeated worker-log grouping.
19. Adaptive scene grid.
20. Grid-size selector.
21. Grid visibility toggle.
22. World axes and origin marker.
23. World coordinate/zoom readout.
24. Object-label toggle.
25. Collider overlay toggle.
26. PNG sprite preview in Scene View.
27. Sprite sorting-order preview.
28. Move/Rotate/Scale gizmos.
29. Snap-to-grid and 15-degree rotation snapping.
30. Context menu plus focus/rename/duplicate/delete/align scene operations.

## Validation performed for this package

- Parsed all `.lic` files as JSON.
- Decoded all embedded Base64 assets and verified PNG signatures.
- Checked all object sprite and SpriteRenderer texture references against embedded asset names.
- Checked script references against project scripts.
- Parsed all project `.csproj` files as XML.
- Checked solution project paths.
- Reviewed the separate-window preview path and confirmed there are no HWND reparenting/NativeWindowHost references in the editor source.
- Generated and visually inspected a raster preview of Crystal Valley from the scene data and embedded sprites.

## Not verified in this environment

The environment used to prepare this ZIP has no .NET SDK, so `dotnet build`, MoonSharp runtime tests, WinForms dock interactions and the actual MonoGame window could not be executed here. Run the commands below on Windows and report any compiler output before treating this build as a verified release.

## Startup / Prefab test follow-up (2026-11-04)

- Prefab propagation smoke test now explicitly sets an instance-level position and rotation before applying a template update. This corrects a flawed test assumption: `InstantiatePrefab` offsets a source object's actual position by 16 units, and the fixture's source coordinates defaulted to 0,0; it did not initially place the object at 21,22.
- Editor startup exceptions are now written to `%LOCALAPPDATA%\PixelEngine\Logs\editor-startup.log` and shown in a dialog instead of failing silently.
- Player startup/runtime exceptions are written to `%LOCALAPPDATA%\PixelEngine\Logs\player-startup.log` and displayed in a Windows error dialog.
- `Run-Editor.cmd` and `Run-Demo-Game.cmd` are provided as double-click launchers.
- Start the editor with `Run-Editor.cmd`; to start the game from the editor, press F5. The editor itself does not automatically start the game.
