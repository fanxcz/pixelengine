# PixelEngine 2.0 — Stage 8 Balanced (quality pass)

This stage adds the first integrated versions of Diagnostics, Animation, radial 2D Lighting, CPU Particles, palette TileMaps and a lightweight Runtime Profiler. Existing LIC v1 root fields remain unchanged; authoring metadata is stored under the optional `pixelEngine` root member.

## Changes in this quality pass

- Animation channels now evaluate independently. Blank or partial keyframes do not pull unrelated transform channels toward the live Transform. Duplicate timestamps deterministically select the last serialized key. Looping playheads are wrapped to the clip duration so they do not accumulate indefinitely.
- Prefab update/revert preserve per-instance position and rotation while propagating template data. Regression coverage spans multiple scenes.
- Menu shortcut creation remains centralized. Bare printable keys are not assigned as WinForms menu shortcuts, preventing the previously reported `InvalidEnumArgumentException` for `Keys.F`.
- Profiler sanitizes non-finite timings, negative counts and negative memory samples before publishing snapshots.
- Particle emitters use a bounded reusable pool; lighting uses premultiplied radial falloff for additive blend; TileMap bounds and palette indices are validated.
- `verify-stage8.ps1` checks required feature files, the menu-shortcut guard, cached scene/component snapshots, the reusable render context, allocation-sensitive compatibility loops, TileMap malformed-input preservation and key regression-test markers before building Debug/Release and running smoke tests.
- Scene and component snapshots are invalidated on mutation; new elements are not processed in the same in-progress update pass, but are visible on the next pass. The render context is reused across frames, and compatibility script purge removes entities already flagged as destroyed.
- LIC loading deliberately retains malformed TileMap dimensions and cell-buffer length for Diagnostics. Safe repairs clamp dimensions and preserve the overlapping authored cells.

## Features and current scope

| Feature | Current implementation | Not included yet |
|---|---|---|
| Diagnostics | Static LIC validation, references, invalid transforms and Stage 8 metadata; safe value fixes; text report | No automatic edits to user scripts or ambiguous references |
| Animation | Transform keyframes for position, rotation and scale; linear interpolation, shortest angle path, loop/reverse; editable timeline ruler with scrub/playhead and key markers | No skeletal animation, curves, blend trees, root motion or timeline playback preview |
| Lighting | Additive radial 2D glow in the Stage 8 snapshot | Stage 10 later adds ambient darkness and hard collider shadows; normal maps and soft shadows remain out of scope |
| Particles | CPU particles, gravity, color fade and capped object pool | No GPU simulation, collision, texturesheets or sub-emitters |
| TileMap | Palette-indexed cells, editor cell grid and camera-range culling | No tile collision/autotile, atlas slicing, brush tools or streaming chunks |
| Profiler | Rolling 120-frame FPS/frame/update/draw/memory/object/component metrics; player JSON telemetry; editor viewer | No GPU timings, allocation call stacks or sampling profiler |

## Later rendering milestone

The Stage 8 implementation shown above is historical. Stage 10 adds a bounded CPU light map with ambient darkness and hard collider shadows, sprite-sheet animation and runtime sprite atlases; see `PIXELENGINE_2_STAGE_10.md`.

## Run the sample

On Windows with the .NET 8 SDK:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-stage8.ps1
```

Open Studio:

```powershell
dotnet run --project .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug
```

Run the integrated showcase in its own MonoGame window:

```powershell
.\Run-Stage8-Showcase.cmd
```

Or directly:

```powershell
dotnet run --project .\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj -- .\Lic\stage8-showcase.lic
```

## Compatibility and safety

- Unknown LIC JSON fields are retained through the reader/writer.
- The `pixelEngine` extension is optional; old projects continue to load without it.
- Legacy C# scripts are imported but deliberately not executed. Lua scripts use MoonSharp SoftSandbox; do not run untrusted scripts because the sandbox is not a hardened security boundary and no instruction budget is enforced.
- The old game-specific PixelFight code is preserved. The MonoGame runtime is still a compatibility slice, not full gameplay parity with every old game mode.
