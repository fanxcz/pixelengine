# PixelEngine 2.0 — Stage 9 QA notes

## Automated smoke-test coverage added or expanded

- Component lifecycle: `Start` idempotence, late component additions, enable-after-start, `OnRemoved`, component reuse and GameObject reattachment.
- Physics: dynamic circle against static box, impulse response, collision enter/stay/exit, trigger entry and false-positive rejection for rotated OBB AABBs.
- Input/render/camera/audio foundations: key/gamepad action bindings, unbinding, layer/order depth, camera viewport bounds and mixer volume composition.
- Persistent saves: cross-instance persistence, key removal, corrupt-file quarantine and in-memory fallback when storage is unavailable.
- TileMaps: cell bounds, resize, brush rectangles, flood fill, adjacency masks, runtime limits, seeded cave determinism, preserved solid borders and merged static collider geometry.
- Existing project import, JSON round-trip, animation, particles, lighting, profiler and diagnostics coverage from earlier stages remains in the smoke-test project.

## Static checks

`verify-stage9.ps1` checks that the required source modules and integration points exist and that representative regression markers remain in `PixelEngine.SmokeTests`. It then runs `dotnet restore`, Release and Debug solution builds, and the smoke-test executable.

## Environment limitation

The archive authoring environment does not provide `dotnet`, `csc`, MSBuild or WinForms. Static XML/JSON/source checks are possible here, but they are not a substitute for compiling the C# projects or running the MonoGame/WinForms applications. The verification script must be run on a Windows machine with .NET 8 SDK and the required NuGet packages available.
