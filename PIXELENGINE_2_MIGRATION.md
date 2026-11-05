# PixelEngine 2.0 — incremental migration plan

## Architecture decisions

- Runtime: MonoGame DesktopGL on .NET 8. It owns gameplay state and rendering.
- Editor: separate Windows Forms process, initially editing the preserved LIC v1 data model and launching the MonoGame sandbox as a child process. The editor contains no gameplay simulation.
- Compatibility: the old Bloody Pixels Windows Forms runtime, editor and Android MAUI port remain available while feature parity is incomplete.
- Scripting: run Lua through MoonSharp `Preset_SoftSandbox`; imported C# Roslyn scripts remain source-only and are never automatically executed.

## Solution layout

- `PixelEngine.Core`: MonoGame host, fixed-step loop, component/scene model, renderer, asset store, LIC reader/writer, compatibility layer and Lua script API.
- `PixelEngine.Sandbox`: playable/preview host for imported `.lic` projects.
- `PixelEngine.SmokeTests`: LIC fixture import checks and Lua source syntax checks.
- `PixelEngine.Editor`: custom dark editor shell with Hierarchy, Inspector, Scene View, script editor, Assets, Console and Play/Stop.
- `PixelFight`: original Bloody Pixels game and old LIC runtime; retained as fallback.
- `PixelFight.Editor`: original editor; retained until editor feature parity.
- `PixelFight.Droid`: existing MAUI Android port.
- `PixelFight.Tests`: current regression suite for the original runtime.

## LIC v1 compatibility contract

LIC is a JSON document containing `app`, `version`, `name`, `startScene`, `scenes`, `scripts` and `assets`. Existing lower-case field names and scene/object/script/asset ordering are preserved. `LicProjectReader` reads and normalizes the model; `LicProjectWriter` serializes the same field schema for editor saves. PNG assets embedded as Base64 are decoded lazily into the MonoGame texture cache.

Supported render kinds in this slice: `rect`, `circle`, `sprite`, `poly` outline, `text`, and a basic `button` rectangle. Special old kinds such as knight/castle/tower are currently represented as simple bounds. Physics supports gravity, floor landing and one-way platform tops. It is not yet a full replacement for the old runtime's physics or entity-specific simulation.

## Lua compatibility

`LicLuaBehaviour` creates one MoonSharp soft-sandboxed state per scripted object. The runner calls optional `start()` once and `update(dt)` every frame. Exposed globals:

- `self`: name, x/y, vx/vy, w/h, ground, dead, num/maxnum, attack, face, color, text, Get/Set, hit and destroy.
- `api`: log, key, time, rand, find, spawn, destroy, purge, cam, bg, save and load.

`api.mousex` and `api.mousey` now return world coordinates, `api.click` reports a left-click edge, `api.pick` performs basic object bounds/polygon picking, and `api.play` loads embedded WAV assets through MonoGame. Picking does not yet model polygon holes exactly. `api.save`/`api.load` currently use a session-local in-memory store and are not persisted. The `.lic` sample Lua scripts use the common API subset. Lua errors are logged and disable the faulty component rather than crashing the game loop.

C# script code is imported and retained but not executed. Roslyn execution in the legacy runtime can invoke arbitrary .NET APIs; a trustworthy in-process security sandbox is not assumed. A future C# plugin feature must use an explicit trusted-project confirmation and preferably process isolation with a narrow IPC contract.

## Editor slice

The custom editor currently provides:

- Hierarchy tree for scenes, objects, scripts and assets.
- PropertyGrid for selected project/scene/object/script/asset records.
- 2D Scene View with grid, object shapes, labels and selection outline.
- Multiline script source editor; edits update the in-memory LIC model.
- Lua syntax validation command.
- LIC Save / Save As.
- Console and asset list.
- Play / Stop that launches the MonoGame sandbox as a separate process.

Not yet implemented: drag/drop object creation, scene gizmos, snapping, embedded game preview, undo/redo, prefab system, asset import UI, docking layout persistence and build profile management. Scene editing operations are schema-preserving, but runtime scene transitions from Lua are still pending.

## Migration milestones

1. [x] Create MonoGame host, fixed simulation step and core component lifecycle.
2. [x] Add scene/component/transform primitives, camera and basic renderer.
3. [x] Add LIC v1 read-only import model and basic compatibility scene factory.
4. [x] Add first-pass render, texture loading and platformer physics subset.
5. [x] Add smoke-test console project for bundled LIC projects.
6. [x] Add MoonSharp Lua component lifecycle and compatibility API subset.
7. [x] Add LIC writer for safe round-trip editing.
8. [x] Add first custom editor shell with hierarchy, inspector, script editor, scene view and Play process launch.
9. [x] Add basic multi-scene authoring: add, duplicate, delete and select startup scene while preserving LIC extension data.
10. [ ] Improve compatibility: special legacy entity rendering, collision parity, polygon fills, exact world-map picking and robust audio mixing.
11. [ ] Add editor gizmos, drag/drop, undo/redo, project/asset importer and embedded Game view.
12. [ ] Add exporter/player packaging and project round-trip regression tests.
13. [ ] Port Bloody Pixels gameplay into the new runtime after automated parity tests.
14. [ ] Evaluate Android player path after the shared runtime stabilizes.

## Validation

The environment used to author this source update has no `dotnet` executable. Code and project files were reviewed and the LIC fixtures can be structurally inspected, but package restore, C# compilation, SmokeTests and live GUI/MonoGame testing are not claimed as passing. MoonSharp is a reduced-global sandbox, not a hardened security boundary or instruction-budgeted sandbox; do not run untrusted scripts where denial-of-service matters. On a .NET 8 machine run:

```powershell
dotnet restore PixelEngine.sln
dotnet run --project PixelEngine.SmokeTests/PixelEngine.SmokeTests.csproj
dotnet build PixelEngine.sln
dotnet run --project PixelEngine.Editor/PixelEngine.Editor.csproj
```

## Non-negotiable compatibility rules

- Do not rename or remove LIC v1 fields.
- Preserve original project and gameplay files until the new player has parity.
- Never silently run imported C# code.
- Keep rendering/resource ownership in MonoGame; keep editor UI out of gameplay runtime.
- Any incomplete feature must be reported as incomplete rather than simulated as fully supported.


## Stage 5 Studio polish

- [x] Godot-inspired script editor reindent, outline shortcut, completion refresh and API help
- [x] Script-aware Undo/Redo routing
- [x] WAV preview in Asset Manager
- [x] Prefab and component keyboard workflows
- [x] Scene/prefab reference-safe asset rename/removal checks
- [ ] Full LSP, code folding, semantic debugger and native AudioSource runtime remain future work
