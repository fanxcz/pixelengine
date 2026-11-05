# PixelEngine 2.0 — Stage 8 Balanced

Stage 8 adds the first integrated versions of Diagnostics, Animation, Lighting, Particles, TileMap and Runtime Profiler while preserving the legacy `.lic` v1 schema.

## Compatibility and storage

All new authoring data is written under the optional root property `pixelEngine`:

```json
{
  "pixelEngine": {
    "animations": [],
    "lights": [],
    "particleEmitters": [],
    "tileMaps": []
  }
}
```

Projects without `pixelEngine` remain readable. Unknown JSON extension fields continue to round-trip. The original root fields (`scenes`, `objects`, `scripts`, `assets`, `prefabs`) are not renamed.

## Editor access

Open a project and choose **Tools → Diagnostics & Stage 8 Tools…** (`Ctrl+F8`). The window contains:

- **Diagnostics** — static checks for references, duplicate names, invalid transforms, animation data, light/particle parameters and tilemap dimensions/cells. `Fix safe issues` only changes deterministic invalid values; missing references and duplicate names remain manual work.
- **Animation** — create/remove clips, edit clip properties and keyframe transform channels (time, X/Y, rotation, scale X/Y). The horizontal timeline shows key markers and a clickable playhead; `Add key` inserts at the playhead. Add/remove/edit key rows in the grid. An object's `Animator` authoring component refers to a clip by name.
- **Lighting** — add/remove and edit light positions, radius, color, intensity, scene and enabled state.
- **Particles** — add/remove and edit CPU particle emitters, including rate, lifetime, speed, spread, size, gravity and pool cap.
- **TileMap** — create/remove maps and edit palette-index cells (0 means empty). The editor grid shows at most 128×128 cells to avoid allocating hundreds of thousands of WinForms controls; the runtime data model supports up to 512×512.
- **Profiler** — editor process metrics plus the latest live MonoGame Player sample when a game is running.

The tools window saves the project when closed after edits. You can also use the normal Save command from the main editor.

## Runtime systems

### Animation

`AnimationEvaluator` interpolates transform keyframes, wraps/clamps time and uses shortest-path angle interpolation. `AnimationPlayerComponent` is attached by an enabled `Animator` component on a legacy LIC object. Unkeyed transform channels retain their live values, so a scale-only clip does not reset position controlled by physics/input. Scale affects legacy object render/collision bounds.

Current scope: transform channels only, with a keyframe timeline and scrub/playhead editing. Timeline playback preview, curves/easing, blend trees, root motion and skeletal animation are not included yet.

### Lighting

`Light2DComponent` submits radial glow commands. `LightingRenderer` renders them as an additive pass after the normal scene. This is a glow/light overlay, not a shadow-map, occlusion or normal-map lighting solution. Values and positions are bounded/validated to prevent invalid GPU transforms.

### Particles

`ParticleEmitterComponent` uses a capped CPU particle pool and reuses the shared one-pixel texture. Spawn rate, lifetime, speed, spread, size and gravity are bounded. This initial system supports primitive particles; texture sheets, collision, sub-emitters and GPU simulation are future work.

### TileMap

`TileMapComponent` renders palette-indexed cells; index 0 is empty. It culls cells outside the camera's visible world bounds and clamps untrusted dimensions before rendering. Tile collision/autotiling, tileset atlases, painting brushes and chunk streaming are not included yet.

### Profiler

`EngineProfiler` keeps a 120-frame rolling window for FPS, frame/update/draw times, managed memory, object count and component count. Managed heap size is sampled every 30 frames by default to avoid calling `GC.GetTotalMemory` every frame. The player writes a per-process JSON snapshot under `%LOCALAPPDATA%\PixelEngine\Logs`; the editor reads the newest fresh snapshot while the Profiler tab is open. This is a lightweight runtime profiler, not a CPU/GPU sampling profiler.

## Known compatibility limits

- Legacy C# scripts are imported but intentionally not executed. Lua scripts continue to run through MoonSharp's sandbox.
- The compatibility physics layer is still the existing platformer subset; TileMap cells are visual and are not solid colliders. No tile collision/autotiling or chunk streaming is implied by the TileMap editor.
- The Stage 8 systems are additive features; they do not replace the legacy combat/game-specific code in Bloody Pixels.

## Build and regression checks

From the repository root on Windows with the .NET 8 SDK:

```powershell
powershell -ExecutionPolicy Bypass -File .\verify-stage8.ps1
```

The script restores and builds Release, then runs `PixelEngine.SmokeTests`. To open the editor in Debug (console diagnostics enabled):

```powershell
dotnet run --project .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug
```

To run the integrated sample game in its separate MonoGame window:

```powershell
dotnet run --project .\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj -- .\Lic\stage8-showcase.lic
```

`Lic/stage8-showcase.lic` demonstrates a scale-only animation, two radial lights, two particle emitters and a palette TileMap. Player input: A/D or arrows to move, W/Up/Space to jump, Esc to exit.

> The source bundle was statically checked in the packaging environment. That environment did not have the .NET SDK or Windows Forms runtime, so a real Windows compilation and interactive GPU/editor test must be run with `verify-stage8.ps1` on the target Windows machine.
