# PixelEngine 2.0 — Stage 8 follow-up

This follow-up strengthens the Stage 8 Balanced source package without changing the legacy LIC v1 root schema.

## Runtime and memory

- Scene and GameObject update/draw passes reuse cached array snapshots; adding/removing objects or components invalidates the appropriate snapshot.
- The active MonoGame host reuses a single RenderContext and clears per-frame command lists instead of allocating a new context each draw.
- Lighting commands are traversed by index to avoid interface-enumerator boxing in the draw path.
- Legacy entity purging always removes its GameObject from the Scene, even if a compatibility behaviour already set `IsDestroyed`. The GameObject is set inactive first because a cached Scene snapshot can still contain it until the next pass.
- Profiler heap sampling remains throttled to every 30 frames by default.

## Import and authoring

- The LIC reader preserves malformed TileMap dimensions and cell-buffer length long enough for Diagnostics to report them.
- Safe TileMap repairs clamp supported dimensions and preserve overlapping authored cells using the original row stride.
- The Animation tools have a horizontal keyframe ruler, visible key markers, a clickable playhead and an `Add key` action at the playhead.

## Additional regression

The smoke tests include a compatibility purge regression that constructs a legacy component, pre-marks it destroyed, runs `PurgeDestroyed()`, then verifies that the entity is removed from the list, absent from the active Scene, inactive for any stale snapshot, and safe under repeated purge.

## Verification status

Static checks performed in the packaging environment:

- 74 C# source files: delimiter/string/comment scan passed.
- 10 project XML files: parsed successfully.
- 7 LIC fixtures: JSON parsed successfully.
- 8 embedded Base64 assets: decoded; PNG/WAV signatures validated.
- Solution project paths: all resolved.
- Smoke-test markers and verification-script guards: present.

The packaging environment does not contain the .NET SDK or Windows Forms/MonoGame runtime. A real build and smoke-test execution must still be run on Windows with:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-stage8.ps1
```
