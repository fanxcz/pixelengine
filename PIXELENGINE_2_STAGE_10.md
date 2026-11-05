# PixelEngine 2.0 — Stage 10: sprite sheets, lighting and sprite atlases

Stage 10 extends the Stage 9 runtime with practical 2D rendering features while keeping legacy `.lic` projects readable. New fields are additive and unknown JSON extension properties continue to round-trip.

## Implemented

### Frame-based sprite animation

- Added `SpriteSheetAnimationComponent` with frame width/height, frame count, optional column count, first-frame offset, frames-per-second and ping-pong playback.
- `Loop`, `Pause()` and `Play(restart)` control playback; the frame sequence calculation is exposed as a pure helper for tests.
- The component supports both legacy `LicObjectComponent` sprites and native `SpriteRenderer` components.
- LIC `Animator` metadata accepts `spriteFrameWidth`, `spriteFrameHeight`, `spriteFrameCount`, `spriteFramesPerSecond`, `spriteSheetColumns`, `spriteFirstFrame`, `spriteLoop` and `spritePingPong`.
- If columns are zero, the runtime derives them from the loaded sprite texture width. Invalid frame rectangles are skipped instead of drawing the entire sheet accidentally.

### Ambient light, point lights and hard shadows

- The LIC project stores a global `AmbientLight` value (0–1), copied to the loaded scene at runtime. The Studio Lighting tab exposes an Ambient Light control; older projects default to 1 and therefore keep their original brightness.
- `Light2DComponent` supports color, radius, intensity, falloff and deterministic multi-wave flicker.
- Box and circle colliders can set `castsShadows: true`. Rotated box edges are transformed to world space; circles are approximated with 20 line segments.
- The renderer draws an ambient-darkness overlay and a colored additive light map. Shadow tests use the ray from each map sample to the light against candidate world-space segments.
- CPU work is bounded: the LIC scene factory and render context accept at most 16 lights per scene; the quarter-resolution light map is capped at 384×384 and each light tests at most 64 candidate shadow edges. The render map is generated in screen space and does not require a platform-specific shader.
- Diagnostics validate ambient values, sprite sheet parameters and point-light falloff/flicker settings; safe fixes clamp deterministic numeric values.

### Sprite atlases

- Added deterministic shelf-packing `SpriteAtlasBuilder` for 1–512 already-loaded textures, up to 4096×4096.
- Adds configurable transparent gutters with edge-texel extrusion to reduce linear-sampling seams.
- `AssetStore.BuildSpriteAtlas(atlasName, namedTextureKeys, ...)` builds/replaces a cached atlas from loaded texture cache keys and owns its GPU resource until store disposal.
- `SpriteRenderer.SetAtlasRegion(atlas, regionName)` binds a named source rectangle.
- `SpriteAtlas` exposes named regions and is independently disposable; repeated disposal is safe.

### Studio and sample project

- Component Inspector exposes sprite-sheet controls and collider shadow flags.
- Lighting tools expose per-project ambient light and editable point-light properties.
- Added `Lic/stage10-showcase.lic`, containing a generated 4-frame pixel character sheet, a point light with flicker/falloff and a wall collider marked to cast a shadow.
- Added `Run-Stage10-Showcase.cmd` and `verify-stage10.ps1`.

## Run and verify

On Windows with the .NET 8 SDK and NuGet access:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-stage10.ps1
.\Run-Stage10-Showcase.cmd
```

Or run the sample directly:

```powershell
dotnet run --project .\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj -c Debug -- .\Lic\stage10-showcase.lic
```

The verification script checks required source markers and `.lic`/embedded PNG structure, restores the solution, builds Release and Debug, and runs the smoke tests.

## Deliberate limitations

This is a rendering milestone, not full parity with Unity/Godot.

- Shadows are hard-edged 2D segment occlusion. There are no soft penumbra, normal maps, height maps, volumetric lights or GPU shadow maps. Only colliders explicitly marked `castsShadows` occlude lights; generated TileMap colliders are not automatically marked as shadow casters.
- The CPU light map is intentionally capped. Many large-radius lights or dense shadow geometry can still be expensive; use a small number of local lights and mark only meaningful blockers.
- The sprite atlas is a runtime packing API, not yet a visual import/packing workspace in the Studio. It packs textures that have already been loaded into `AssetStore`; it does not rewrite source `.lic` files.
- Sprite-sheet playback is frame based. Skeletal rigs, IK, blend trees, animation events and root motion are not implemented.
- Particles remain CPU simulated. GPU particles, particle collision, sub-emitters and particle texture sheets remain future work.
- The Studio still launches the player in a separate process. An integrated Game View, breakpoints, frame-step debugger, visual scripting, safe isolated C# execution and hot reload remain separate milestones.
- Android publishing is not wired into the current MonoGame DesktopGL project.

## Validation status

Static checks and archive integrity can be run in this environment. The authoring environment used for this source drop does not have the .NET SDK, so no successful C# compilation, actual SmokeTests execution, GPU rendering session or Windows Forms session is claimed. Run `verify-stage10.ps1` on the target Windows development machine; any resulting compiler diagnostic should be addressed before using the runtime in a game project.
