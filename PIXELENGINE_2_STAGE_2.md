# PixelEngine 2.0 — Stage 2 delivery notes

## Delivered in source

1. **Lua runtime**: MoonSharp package integrated into `PixelEngine.Core`; one sandboxed Lua VM per scripted object; optional `start()` and per-frame `update(dt)`; failures are logged and disable only that behaviour.
2. **Compatibility facade**: common legacy calls `api.key/log/time/rand/find/spawn/destroy/purge/cam/bg/save/load/mousex/mousey/click/pick/play` plus `self` object properties and helper methods.
3. **WAV playback**: embedded LIC WAV Base64 assets are decoded into cached MonoGame `SoundEffect` instances.
4. **Mouse mapping**: window coordinates are transformed into world coordinates through the active camera; picking checks topmost rectangles, circles and polygon rings.
5. **LIC writer**: editor save uses the same v1 field names and captures unknown JSON fields through `JsonExtensionData` instead of dropping them.
6. **Custom editor**: Windows Forms studio with dark layout, scene/object/script/asset hierarchy, PropertyGrid, grid-based Scene View, script text editor, Lua checks, object add/delete, Assets and Console tabs, Save/Save As, Build and separate-process Play/Stop.
7. **Smoke tests**: fixtures are checked for parse/serialize counts, Base64 validity, missing script references, and Lua syntax through MoonSharp when run under .NET.

## Important limitations

- C# Roslyn scripts are not executed. They remain editable source data only.
- MoonSharp SoftSandbox removes dangerous standard libraries but has no instruction/time budget here; treat imported scripts as trusted code.
- `api.save/load` is in-memory per runtime session, not disk-persistent yet.
- Polygon hit tests do not handle holes as exact boolean geometry.
- The editor Scene View is a drawing/selection view, not a complete gizmo editor. Play opens the sandbox as a separate process.
- Special legacy types (knight/castle/tower) still use primitive placeholders; gameplay parity is not achieved.

## Validation performed while authoring

- All 6 packaged LIC fixtures parsed as JSON.
- All embedded Base64 assets decoded.
- All object-to-script references in the fixtures resolved.
- All 6 bundled Lua scripts parsed using the system Lua 5.4 shared library. This is a syntax sanity check, not a substitute for running the MoonSharp smoke tests.
- Project XML files parsed and every project path in `PixelEngine.sln` exists.
- The environment has no `dotnet` executable, so NuGet restore, .NET compilation, MoonSharp execution, WinForms UI and live MonoGame rendering remain unverified.
