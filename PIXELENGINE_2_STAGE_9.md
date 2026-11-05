# PixelEngine 2.0 — Stage 9: Runtime foundations and playable 2D workflow

Stage 9 focuses on connecting authoring data to runtime behavior and making existing systems safer to compose. The source remains compatible with the original `.lic` project shape; new authoring fields are additive and unknown JSON extension fields continue to round-trip.

## Implemented in this source drop

### Core and lifecycle
- Explicit `OnAdded`, one-time `Start`, `OnEnabled`, `OnDisabled`, `OnRemoved` hooks.
- Safe late component addition, component removal/reuse and GameObject detach/reattach.
- Snapshot invalidation when objects/components are mutated during update iteration.
- Attachment rollback and best-effort cleanup when lifecycle callbacks throw.

### Physics 2D
- Static, kinematic and dynamic `Rigidbody2DComponent` bodies.
- Gravity scale, force, impulse, damping, frozen position axes and `MovePosition`.
- Oriented box and circle colliders, offsets, trigger state, 32 layer indices and bit masks.
- Uniform-grid broadphase; oriented box/box SAT, box/circle and circle/circle narrowphase.
- Normal impulses, positional correction, friction/restitution and enter/stay/exit callbacks.
- Optional TileMap collision baking merges adjacent non-empty cells into static box colliders.

### Rendering, camera and input
- Named `Background`, `Default`, `Foreground` and `UI` sorting layers plus per-layer order.
- Camera zoom limits, viewport/world conversion, world bounds and smoothed target following with dead zone.
- Keyboard, mouse and first-gamepad input snapshots.
- Rebindable keyboard/gamepad button actions; Lua `api.action`, `api.action_pressed`, `api.bind_key` and analog `api.axis` (`move_x`, `move_y`, `look_x`, `look_y`, `left_trigger`, `right_trigger`).

### Audio, saves and game workflow
- Controllable embedded-WAV AudioSource instances with Play/Pause/Stop, loop, pitch, pan and volume.
- Shared Master, SoundEffects and Music mixer buses.
- Bounded project-name save files with atomic writes, corrupt-file quarantine and in-memory fallback.
- Runtime FPS/frame/update/draw/object/component/memory telemetry written once per second for the Studio Profiler tab.
- Desktop self-contained packaging script for Windows/Linux/macOS x64 and ARM64 (`Publish-Game.ps1`).

### TileMap tools
- Brush painting, rectangle painting, fill and flood fill.
- Four-neighbour adjacency masks and seeded cellular-automata cave generation.
- Optional generated static collision geometry; the bake merges matching horizontal spans across rows and caps output at 8,192 colliders.

## Build and run

Windows with .NET 8 SDK and NuGet access:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-stage9.ps1
```

Run the Studio directly:

```powershell
dotnet run --project .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug
```

Package a `.lic` game for Windows x64:

```powershell
.\Publish-Game.ps1 -ProjectFile .\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj -LicFile .\Lic\demo.lic -GameName CrystalValley -RuntimeIdentifier win-x64
```

The publish script requires an empty destination directory. DesktopGL native graphics/audio dependencies may still be required by the target operating system.

## Deliberate limitations

This is a functional foundation, not feature parity with Godot, Unity or Box2D. The physics solver does not yet include continuous collision detection, angular velocity/torque, joints, convex polygon shapes or a stable iterative stacking solver. Fast bodies can tunnel through thin shapes; rotation changes box geometry but contacts do not impart angular motion.

Stage 10 supersedes the Stage 9 rendering limitations with frame-based sprite-sheet playback, ambient light/darkness, colored point lights with hard collider shadows, and a runtime sprite-atlas packer. Particles still use a bounded CPU pool without particle/collider response or GPU simulation. Skeletal rigs, IK, blend trees and animation events are not included. TileMaps still use palette-color cells rather than texture atlases, and do not yet stream chunks asynchronously or implement rule-based atlas autotiling.

C# scripts remain editable/importable source metadata and are intentionally not executed. Enabling arbitrary in-process C# would not be a safe sandbox; a separate worker/compiler architecture is required. Lua uses MoonSharp SoftSandbox, which is not a hardened security boundary and has no strict per-script instruction/time budget; do not run untrusted scripts. The Studio still launches the player in a separate window and does not yet provide an integrated Game View frame-step debugger, breakpoint debugger, visual scripting or hot reload. The publisher currently targets Windows/Linux/macOS; Android packaging is not wired into the new runtime.

## Validation status

`PixelEngine.SmokeTests` now includes lifecycle, physics contacts/triggers/rotated boxes, input maps, sorting/camera/audio foundations, persistent storage recovery, TileMap editing/cave generation/collision baking, profiler telemetry, and the existing LIC fixtures. `verify-stage9.ps1` runs source-level guards followed by restore, Release/Debug builds and smoke tests.

The source archive was statically reviewed and its ZIP/XML/JSON contents can be checked by the packager; this authoring environment does not have the .NET SDK or Windows Forms runtime. Therefore no successful C# build, smoke-test execution, interactive Studio session, GPU/audio test or cross-platform publish is claimed here. Run `verify-stage9.ps1` on the Windows development machine and address any compiler diagnostics it reports.
