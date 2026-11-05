# PixelEngine.Core

MonoGame-based 2D runtime foundation for PixelEngine 2.0. The Windows Studio remains a WinForms authoring shell; the new runtime loads the original `.lic` format and keeps unknown JSON extension fields intact.

## Runtime features

- Fixed 60 Hz simulation step with bounded catch-up and cached Scene/GameObject/Component snapshots.
- Explicit component lifecycle: `OnAdded`, one-time `Start`, `OnEnabled`/`OnDisabled`, `OnRemoved`; safe late addition, removal and reattachment.
- 2D rigid bodies: static, kinematic and dynamic; gravity scale, force/impulse, damping, freeze axes and movement API.
- 2D colliders: oriented boxes and circles, triggers, layer/mask filtering, friction/restitution, spatial-grid broadphase and collision enter/stay/exit callbacks.
- Camera2D with zoom limits, screen/world conversion and optional bounds; `CameraFollowComponent` with exponential smoothing, dead-zone, zero-speed snap and finite-value guards.
- Named render sorting layers and per-layer order; cached PNG textures, sprites, shapes, text and legacy LIC rendering.
- Keyboard, mouse and first-gamepad input snapshots plus bounded rebindable `InputActionMap` bindings.
- AudioSource playback for embedded WAV assets, looping/pitch/pan/volume, SoundEffects/Music buses and a master mixer.
- Project-scoped persistent `api.save`/`api.load` key-value storage with atomic writes, size limits, corrupt-file quarantine and in-memory fallback when OS storage is unavailable.
- LIC reader/writer, scenes, objects, prefabs, runtime spawning and the backwards-compatible Lua script API.
- Transform animation and frame-based sprite sheets (loop/ping-pong), ambient darkness, colored point lights with falloff/flicker and Box/Circle collider shadows, runtime SpriteAtlas packing, pooled CPU particles, palette TileMaps with viewport culling, tile painting/fill/flood fill, deterministic cave generation and optional merged static TileMap colliders.
- Runtime profiler snapshots (FPS/frame/update/draw time, objects/components and managed memory) published once per second for the separate Studio Profiler tab, plus diagnostic validation.

## Physics scope and known limitations

The new solver supports oriented-box/box, circle/circle and box/circle overlap; impulses, positional correction, friction and restitution; triggers; 32 collision layers with bit masks; and a uniform-grid broadphase. It is an intentionally small 2D solver, not a replacement for Box2D: it does not yet provide continuous collision detection, angular velocity/torque, joints, compound convex polygons, a full stable stacking solver or a dedicated physics debug renderer. Very fast bodies can tunnel through thin geometry. Rotation affects box shapes but does not currently produce rotational motion from collisions.

LIC objects without a modern `Rigidbody2D` continue to use the legacy platformer compatibility controller. Add a Rigidbody2D and Collider2D to use the new solver. A TileMap's `collisionEnabled` option creates merged static box colliders for non-empty cells; legacy entities without rigid bodies do not automatically collide with these TileMap colliders.

## Rendering, animation, lighting and particles

- Sorting layers and camera bounds/follow are implemented.
- Animation supports transform clips plus frame-based sprite-sheet playback with loop and ping-pong modes for both LIC sprites and native `SpriteRenderer` components; skeletal/bone animation, IK, blend trees and animation events are not included yet.
- Lighting supports ambient darkness, colored point lights, falloff, flicker and ray/segment occlusion from Box/Circle colliders marked `castsShadows`. It uses a quarter-resolution CPU light map capped at 384x384, 16 lights per scene and 64 shadow edges per light; normal maps, soft penumbra shadows and GPU light accumulation are not included yet.
- `SpriteAtlasBuilder` deterministically packs readable runtime textures with named source rectangles and edge-extruded gutters. `AssetStore.BuildSpriteAtlas` owns and caches atlases, and `SpriteRenderer.SetAtlasRegion` binds a named region; the editor does not yet provide a visual atlas import/packing workspace.
- Particles use a bounded CPU pool; GPU simulation, particle collision, texture sheets and sub-emitters are not included yet.
- TileMaps support grid cells, palette painting, rectangle painting, flood fill, four-neighbour adjacency masks, deterministic cave generation, viewport culling and optional static collision baking. Texture atlas slicing, rule-based auto-tile painting, asynchronous chunk streaming and per-tile animated materials remain future work.

## Scripts and safety

- Lua runs through MoonSharp `Preset_SoftSandbox`; common calls include `api.key`, `api.action`, `api.action_pressed`, `api.axis`, `api.bind_key`, `api.find`, `api.spawn`, `api.destroy`, `api.save`, `api.load` and `api.play`.
- The default action map includes `move_left`, `move_right`, `move_up`, `move_down`, `jump` and `interact`; Lua can rebind with `api.bind_key`/`api.bind_button`, remove bindings with `api.unbind_key`/`api.unbind_button`, and query `api.action`/`api.action_pressed`. Action names are case-insensitive and trimmed; the map limits projects to 64 actions and 16 bindings per device/action. `api.axis` exposes `move_x`, `move_y`, `look_x`, `look_y`, `left_trigger` and `right_trigger` from keyboard/gamepad snapshots.
- SoftSandbox removes file/process/OS modules, but is not a hardened security boundary and this runtime does not yet enforce a strict instruction/time budget. Do not run untrusted scripts.
- Legacy C# scripts are imported and retained but are deliberately not executed. Unrestricted in-process C# is not a safe sandbox. A future isolated worker process is required before enabling user C# execution.

## Audio and packaging

AudioSource currently supports embedded WAV assets. It pauses/resumes when disabled, releases its instance when removed, and responds to source and mixer volume/pitch/pan/loop settings. One-shot `api.play` sounds use the shared master/effects mixer. It does not stream long music tracks or provide an editor waveform/mixer UI; editor-wide bus controls remain future work.

Use `Publish-Game.ps1` to create a self-contained package for Windows x64/ARM64, Linux x64/ARM64 or macOS x64/ARM64. It publishes `PixelEngine.Sandbox`, copies one `.lic` project, and creates launch scripts. DesktopGL still depends on native platform graphics/audio libraries supplied by the target OS/runtime. Android is not wired to PixelEngine.Core in this milestone.

## Build and validation

```powershell
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln -c Debug
dotnet run --project PixelEngine.SmokeTests -c Debug
dotnet run --project PixelEngine.Editor -c Debug
```

Example package:

```powershell
.\Publish-Game.ps1 -ProjectFile .\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj -LicFile .\Lic\demo.lic -GameName CrystalValley -RuntimeIdentifier win-x64
```

## Known incomplete work

The current stage is a practical 2D foundation, not feature parity with Godot/Unity. Full editor-integrated Game View/step-debugger/hot reload, full C# isolation, visual scripting, skeletal animation, GPU particles, normal-map/soft-shadow lighting, tile atlas/autotile/chunk streaming, full prefab override workflows in runtime, advanced profiler traces and Android publishing still need dedicated design and implementation. Do not infer that every inspector field or imported C# script is executable merely because the authoring schema can preserve it.
