# PixelEngine 2.0 — MonoGame migration branch

PixelEngine 2.0 is an incremental modernization of the original Bloody Pixels source tree. The legacy game and editor remain in the repository; the new MonoGame runtime and custom WinForms Studio are developed alongside them so existing `.lic` projects can be migrated gradually.

## Requirements

- .NET 8 SDK
- NuGet access to restore MonoGame DesktopGL and MoonSharp
- Windows to run the WinForms Studio (`PixelEngine.Editor`)
- A graphics environment supported by MonoGame DesktopGL for the player/sandbox

## Build and validate

Run from the repository root on Windows:

```powershell
dotnet clean PixelEngine.sln
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln -c Release
dotnet run --project PixelEngine.SmokeTests -c Release
```

Open Studio:

```powershell
dotnet run --project PixelEngine.Editor -c Debug
```

The editor opens the default `Lic/demo.lic` project when launched from the repository root. Press **F5** inside the editor to launch the player in a separate window; `Shift+F5` stops its process tree. Startup diagnostics are written to `%LOCALAPPDATA%\PixelEngine\Logs\editor-startup.log` and `%LOCALAPPDATA%\PixelEngine\Logs\player-startup.log`.

Run the integrated Stage 10 showcase (animated sprite sheet, ambient darkness, point lights and collider shadows) in its own MonoGame window:

```powershell
.\Run-Stage10-Showcase.cmd
```

The earlier Stage 8 showcase is also retained:

```powershell
.\Run-Stage8-Showcase.cmd
```

Or launch it directly:

```powershell
dotnet run --project PixelEngine.Sandbox -- Lic/stage8-showcase.lic
```

## Studio features carried forward from Stage 5

### Script workspace

- Dark script editor with line-number and bookmark gutter, active-line highlight and status line/column.
- Lua/C# syntax coloring, matching-bracket highlighting, auto-pairing and pair deletion.
- Smart indentation, Tab/Shift+Tab, block indentation, reindent, line comment, duplicate/delete/move line.
- Contextual completion with `Ctrl+Space` and after `.`, including common LIC API, `self` properties, scene object names and WAV assets; completion list refreshes as the token is typed or erased.
- Completion descriptions, API reference (`F1`), function outline (`Ctrl+Shift+O`), conservative Lua/C# block reindent (`Ctrl+Shift+I`), bookmarks (`F2`, `Shift+F2`, `Ctrl+Shift+F2`), find/replace (`Ctrl+F` / `Ctrl+H`), go to line (`Ctrl+L`), zoom, snippets and editor context menu.
- Clicking a script diagnostic jumps to the reported line. Toolbar and menu Undo/Redo route to native script text undo when the Script workspace is active.
- Multiple open script tabs with close/close-others/close-all context actions.

This is a lightweight Godot-inspired editor built on WinForms `RichTextBox`, not a full Godot ScriptEditor clone. It does not include an LSP, semantic type analysis, code folding, breakpoint debugging, refactoring or safe C# runtime execution. Lua check validates syntax through MoonSharp; it is not a type checker.

### Prefabs and components

- Create, duplicate, rename, instantiate, update, apply to linked instances, revert and unpack prefabs.
- Editing a prefab template through the Hierarchy/Inspector/Components dock.
- Component metadata editors for SpriteRenderer, Rigidbody2D, BoxCollider2D, CircleCollider2D, AudioSource, Animator and Script.
- Add, duplicate, reorder, reset and remove component data.
- Unknown JSON extension fields are retained when records are copied or the project is saved.

Component data remains forward-compatible authoring metadata, but this stage now maps Rigidbody2D, BoxCollider2D, CircleCollider2D, AudioSource, Animator and SpriteRenderer-related fields into the MonoGame runtime. Physics body type/mass/gravity/damping/frozen axes, collider trigger/material/layer masks, sorting layer/order and WAV source volume/pitch/pan/loop/PlayOnAwake are active. C# Script components are still source metadata only; they are not executed.

### Asset manager

- Import PNG/WAV from a picker or by dragging files into the asset list.
- Filter by text/type and optionally show only referenced assets.
- Preview PNG dimensions, inspect WAV metadata and audition WAV files with Play/Stop controls (Windows SoundPlayer).
- Assign an asset to the selected object, export, copy the name, rename and delete.
- Renaming updates scene object, component and prefab references. Deleting warns about references and clears them to avoid dangling links.

The LIC asset store is still a flat embedded list; folders, waveform editing and streaming import are not implemented. Runtime `api.play('file.wav')` uses the MonoGame audio service. AudioSource now creates controllable WAV instances and applies PlayOnAwake, loop, volume, pitch, pan and the shared master/effects/music mixer; there is not yet an editor waveform or mixer panel.

### Undo / redo

- `Ctrl+Z`, `Ctrl+Y` / `Ctrl+Shift+Z`, toolbar buttons and Edit menu.
- Command Palette (`Ctrl+Shift+P`) includes editor commands.
- Project edits use bounded LIC JSON snapshots (up to 60 entries and an approximate 24-million-character cap per stack).
- Text editing uses the RichTextBox native text undo; debounced script edits are committed to project history.

### Multi-scene and scene editing

- Scene selector, create, duplicate, delete and set startup scene.
- Hierarchy for scenes, objects, scripts, assets and prefabs.
- Scene View selection and drag-to-move, grid, labels, selection outline and wheel zoom.
- Save/Save As in the existing LIC v1 JSON format.
- Play/Stop launches the MonoGame sandbox as a separate process.

## Runtime and compatibility

- `PixelEngine.Core/` — MonoGame host, fixed-step loop, component/scene model, camera, texture cache, LIC reader/writer, compatibility renderer/physics adapter and Lua script host.
- `PixelEngine.Sandbox/` — MonoGame preview/player for `.lic` projects.
- `PixelEngine.SmokeTests/` — import, schema round-trip, Lua syntax, scene, prefab, component and asset operation checks.
- `PixelEngine.Editor/` — custom WinForms Studio.
- `PixelEngine.sln` — solution containing new PixelEngine projects; original Bloody Pixels projects remain alongside.

LIC Lua scripts run through MoonSharp `Preset_SoftSandbox` with the compatible API surface documented in `PixelEngine.Core/README.md`. The original Roslyn C# scripts are retained and editable but are not run because unrestricted C# is not safely sandboxed. The MonoGame branch remains a compatibility slice rather than full gameplay parity with every legacy mode. A new 2D solver now supports oriented boxes/circles, broadphase, triggers, material response and contact callbacks; legacy objects without modern Rigidbody2D still use the legacy platformer compatibility controller. Transform animation, radial additive lighting, CPU particles, palette TileMaps, persistent key/value saves, input action maps and runtime profiling are working first implementations with explicitly scoped limitations. Legacy C# scripts remain source-only and are not executed.

## Stage notes

- `PIXELENGINE_2_MIGRATION.md` — architecture and migration strategy.
- `PIXELENGINE_2_STAGE_2.md` — LIC importer and first MonoGame preview slice.
- `PIXELENGINE_2_STAGE_3.md` — multi-scene authoring.
- `PIXELENGINE_2_STAGE_4.md` — script workspace, prefab/component, asset and undo/redo details.
- `PIXELENGINE_2_STAGE_5.md` — script-workflow polish, WAV preview and editor shortcut routing.


## Stage 8 Balanced — diagnostics and runtime systems

Stage 8 adds an optional `pixelEngine` JSON extension under each `.lic` project. Legacy LIC fields and unknown extension properties remain forward-compatible. The extension stores transform animation clips, 2D radial lights, bounded CPU particle emitters and palette TileMaps.

- **Diagnostics:** checks missing and wrongly typed resource references, PNG/WAV signatures, prefab template references, C# scripts that the current player does not execute, invalid component values, animation bindings/channels, light/particle values and TileMap dimensions/cell indices. `Fix safe issues` only repairs deterministic numeric/buffer problems; it does not rewrite scripts or guess broken references.
- **Animation:** transform keyframes for X/Y/rotation/scale, per-channel interpolation, loop/reverse playback and a scrub/keyframe editor. Stage 10 adds frame-based Sprite Sheet playback (loop/ping-pong) for LIC sprites and native `SpriteRenderer` components. Skeletal animation, curves, blend trees, animation events and root motion are not included.
- **Lighting:** Stage 10 adds ambient darkness, colored point lights, adjustable falloff/flicker and ray/segment shadows from Box/Circle colliders marked `castsShadows`. The current renderer uses a bounded quarter-resolution CPU light map, not normal-map lighting or soft GPU shadow maps.
- **Particles:** CPU simulation with gravity, lifetime/fade and a bounded reusable particle pool. No GPU simulation, collisions, sprite sheets or sub-emitters.
- **TileMap:** palette-indexed grid with cell editing, rectangle brush, fill/flood fill, four-neighbour masks, serialization and camera-range culling. Optional collision baking merges solid cells into static box colliders. Rule-based auto-tile painting, texture atlas slicing, animated tiles and streaming chunks are not included yet.
- **Sprite atlases:** `SpriteAtlasBuilder` packs loaded textures with named regions and edge-extruded gutters; `AssetStore` owns atlas GPU resources and `SpriteRenderer.SetAtlasRegion` binds a region. Visual atlas editing/import remains future work.
- **Profiler:** rolling frame/FPS/update/draw/memory/object/component metrics, player JSON telemetry and an editor Profiler tab. No GPU timings or allocation call stacks.
- **Frame-loop quality:** scene/object and component snapshots are cached and invalidated on mutation; the MonoGame host reuses its render context. The legacy entity cleanup path detaches objects even when a behaviour already marked them destroyed. These behaviors have dedicated regression tests.
- **TileMap import diagnostics:** malformed dimensions and cell-buffer lengths remain visible to Diagnostics during import; safe repairs clamp bounds while preserving overlapping authored cells.
- **Animation authoring:** the editor includes a horizontal keyframe ruler, key markers and a clickable playhead; the `Add key` command inserts at the current playhead.

Run the Windows validation script (restore, Release build, Debug build, smoke tests):

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-stage8.ps1
```

See `PIXELENGINE_2_STAGE_8_BALANCED.md` for the detailed change list, `PIXELENGINE_2_STAGE_8_QA.md` for regression coverage and verification commands, and the known limits of this milestone.

## Validation status

The authoring container used for this update does not have the .NET SDK. ZIP integrity, project XML, solution project paths, bundled LIC JSON and embedded Base64 data can be checked here, but NuGet restore, C# compilation, MoonSharp runtime tests and live WinForms/MonoGame interaction have **not** been run in this environment. Run the commands above on a machine with .NET 8 before treating this bundle as a verified executable build.


## Stage 6 — Adaptive Scene Studio

The editor now has resizable nested docks, automatic default proportions and saved dock positions under `%LOCALAPPDATA%\PixelEngine\EditorLayout.v2.json`. Use `Ctrl+Shift+L` to reset the layout, `Ctrl+1/2/3` to toggle Hierarchy/Inspector/Bottom Dock, and `Ctrl+4/5/6` to switch Scene/Game/Script views.

The Script workspace includes line numbers, Lua/C# syntax colors, API completion (`Ctrl+Space`), automatic indentation and bracket pairs, bracket matching, bookmarks (`F2` / `Shift+F2`), function outline, find/replace, go-to-line (`Ctrl+L`) and API help (`F1`). Preview process output is streamed into the Output dock.

The Scene View includes Q/W/E/R Select/Move/Rotate/Scale tools, 16-unit position/scale snapping, 15-degree rotation snapping, middle/right-drag panning, cursor-anchored wheel zoom, Fit Scene bounds that account for rotation, and visual BoxCollider2D/CircleCollider2D/trigger overlays. Rotation is saved in LIC as an optional `rotation` property and is applied by the MonoGame renderer to supported 2D shapes and sprites.

Historical Stage 6 behavior: F5 attempted to embed the Sandbox HWND into Game View. This was replaced in Stage 7 after review of the initial screenshot. Current behavior: F5 launches a normal, separate, resizable MonoGame player window; Shift+F5 stops the complete player process tree.

**Limitations:** collider overlays are authoring visualizations, not a complete rotated-body physics solver. Current Stage 7 preview no longer uses Windows HWND reparenting. This source delivery has not been built in the authoring environment because the .NET SDK is unavailable.

## PixelEngine 2.0 — Stage 7 Studio Quality Pass

See `PIXELENGINE_2_STAGE_7.md` for the editor improvements, 30-feature checklist, compatibility notes and validation scope. The default `Lic/demo.lic` is now the pixel-art **Crystal Valley** sample with embedded hero, goblin, bolt and crystal sprites. The MonoGame preview opens in a separate Windows window (F5 / Shift+F5); it is no longer embedded into the editor.

Build and run on Windows:

```powershell
dotnet clean PixelEngine.sln
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln
dotnet run --project PixelEngine.SmokeTests
dotnet run --project PixelEngine.Editor
```

## Stage 7 follow-up: prefab rotation and editor startup

This follow-up preserves object rotation when cloning scenes/prefabs, keeps per-instance X/Y/Rotation overrides during prefab update/revert, and adds more precise prefab smoke-test diagnostics. Debug editor builds use a console so startup progress/errors are visible; Release stays a regular GUI application. Run `powershell -ExecutionPolicy Bypass -File .\run-editor.ps1` for a build-and-launch diagnostic.


## Stage 9 — Runtime foundations

Stage 9 connects several previously authoring-only component settings to the runtime and hardens scene mutation. The component lifecycle now starts once per scene attachment, supports late enabled component additions, and dispatches removal hooks. The physics world uses an AABB uniform-grid broadphase and oriented-box/circle overlap tests, with static/kinematic/dynamic bodies, force and impulse, damping, frozen axes, layers/masks, friction/restitution and collision/trigger callbacks. TileMaps can optionally bake their non-empty cells into merged static boxes. AudioSource uses independent WAV instances with a master/effects/music mixer. InputActionMap supports keyboard/gamepad button bindings and Lua exposes `api.action`, `api.action_pressed`, `api.axis` and `api.bind_key`; axes include left-stick/digital movement and right-stick look input. TileMap authoring includes seeded cellular-automata cave generation. `api.save`/`api.load` now use bounded project-specific persistent JSON files with atomic writes and recovery.

`Publish-Game.ps1` packages a `.lic` project with the MonoGame Sandbox for Windows/Linux/macOS x64/ARM64. The player now publishes rolling runtime telemetry once per second so the separate Studio Profiler tab can display live FPS and frame/update/draw timings. This publisher and the Stage 9 source changes have not been exercised by a local .NET build in the authoring environment. The new solver still lacks continuous collision detection, joints, torque/angular dynamics, and a full stacking solver; TileMap collision baking is for modern Rigidbody2D bodies. Stage 10 now supplies hard collider shadows for 2D point lights. Normal-map lighting and soft GPU shadows, skeletal animation, GPU particles, visual scripting, a fully integrated Game View debugger/C# hot-reload and Android packaging remain future stages.


## Stage 10 — lighting, sprite sheets and atlases

See `PIXELENGINE_2_STAGE_10.md` for implementation details, the Stage 10 showcase, performance limits and remaining renderer/editor work. Run `verify-stage10.ps1` on Windows with the .NET 8 SDK to perform static checks, Release/Debug builds and smoke tests.

## Stage 13 — Studio UI overhaul, real terminal, persisted preferences

Stage 13 rebuilds the Studio look on a single design-token file (`PixelEngine.Editor/Presentation/Theme.cs`,
`#0B0D12` background, `#141821` panels, `#6E8BFF`/`#9B7CFF` accents), adds a responsive dock layout that
collapses the Inspector/Explorer/Bottom Dock instead of clipping, converts every panel and the Studio
Tools window to DPI-aware sizing, and ships a **real** PowerShell/CMD terminal that executes commands,
reads stdout and stderr and reports true exit codes without blocking the UI thread. Preferences
(general, terminal, script font, scene grid, recent projects) are now persisted to
`%LOCALAPPDATA%\PixelEngine\EditorSettings.json` and editable through `Tools → Preferences…` (Ctrl+,).
A headless `PixelEngine.Editor.Tests` project covers settings, layout serialization and MSBuild
diagnostic parsing and runs in CI. Full details and the honest verification scope are in
[`PIXELENGINE_2_STAGE_13.md`](PIXELENGINE_2_STAGE_13.md).

## Stage 11 — Core Stability & QA

The current Stage 11 reliability pass adds read-only runtime scene graph views, finite transform/time guards, bounded LIC/asset I/O, hardened profiler telemetry and a Windows CI/build gate. See [`PIXELENGINE_2_STAGE_11.md`](PIXELENGINE_2_STAGE_11.md) and run `verify-stage11.ps1` from PowerShell to restore, build Debug/Release and execute smoke tests.
