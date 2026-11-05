# PixelEngine 2.0 — Stage 8 QA and regression notes

This document describes source-level checks added to Stage 8 Balanced. It does not claim that a Windows build or interactive GPU test has run in the packaging environment.

## Regression coverage

`PixelEngine.SmokeTests` covers:

- Legacy LIC JSON extension-data round-trip and object rotation persistence.
- Stage 8 `pixelEngine` metadata round-trip and limits for excessive authoring records.
- Scene/object and GameObject/component snapshot invalidation when items are added during an update pass.
- Legacy compatibility entity purge when a behaviour has already marked an entity as destroyed; the GameObject is made inactive and detached from the live scene.
- Animation interpolation, angle wrap, sparse channels, pre-first-key live baselines, duplicate timestamps, unsorted/invalid keys, reverse playback and long-running loops.
- TileMap cell editing, out-of-range rejection, resize preservation, malformed dimensions/buffers, runtime bounds and palette indices.
- Profiler rolling-window wrap, invalid sample sanitization and telemetry JSON round-trip.
- Radial light falloff, premultiplied tint and invalid intensity handling.
- Particle capacity bounds, pool reuse, deterministic seeded behavior and premultiplied tint.
- Project Diagnostics for missing references, wrong asset formats/signatures, script warnings, animation bindings, numeric component values, safe repairs and idempotence.
- Prefab propagation across scenes while retaining per-instance position and rotation.
- Showcase scene wiring and all legacy LIC fixtures (`demo`, `knights`, `merchant`, `strategy`, `tower`, `world`).

## Allocation and frame-loop guards

- `GameObject` caches its component snapshot and invalidates it on component addition.
- `Scene` caches its object snapshot and invalidates it on add/remove.
- New objects/components do not participate in the currently executing snapshot; they become visible on the next pass.
- `EngineGame` reuses one `RenderContext` and calls `ResetFrame()` to clear the command list while retaining capacity.
- The legacy compatibility physics and entity cleanup paths avoid repeated array materialization.
- Lighting draw commands use indexed iteration over the read-only list.
- Managed heap sampling is throttled to every 30 frames unless the caller supplies a measurement.

## Import safety

The LIC reader intentionally does not normalize TileMap dimensions or cell-buffer length before Diagnostics. This lets Diagnostics report malformed authoring data. `Fix safe issues` clamps supported dimensions and attempts to preserve the overlapping top-left portion of the authored map. Runtime TileMap construction clamps dimensions, tile size and palette limits before drawing.

## Windows verification

With .NET 8 installed, run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-stage8.ps1
```

The script checks the presence of feature modules, key source-level invariants and test markers, then runs restore, Release/Debug builds and smoke tests. The packaging environment for this source bundle had no .NET SDK or Windows Forms runtime; those commands and live GPU/editor interactions remain to be executed on Windows.
