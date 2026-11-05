# PixelEngine 2.0 — Stage 11: Core Stability & QA

Stage 11 hardens the Stage 10 runtime and adds a repeatable Windows build gate. It intentionally prioritizes reliability and compatibility over new gameplay features.

## Changes

### Scene graph ownership

- `Scene.Objects` and `GameObject.Components` now expose read-only live collection views. Mutations must use `Scene.Add/Remove` and `GameObject.AddComponent/RemoveComponent`, preserving ownership, snapshots and lifecycle callbacks.
- Failed component attachment now invokes `OnRemoved` as best-effort cleanup before detaching the component. The original `OnAdded`/`Start` exception remains the reported error.

### Numeric and timing safety

- `Transform` normalizes non-finite position, scale, rotation and origin values at assignment boundaries.
- `EngineTime.SanitizeDeltaTime` rejects negative/non-finite elapsed times and clamps long stalls to 250 ms. `EngineGame` uses this shared helper.

### Bounded project and asset I/O

- `.lic` reads use bounded streaming and reject documents above 256 MiB. In-memory parsing and serialization enforce the same UTF-8 byte limit.
- PNG/WAV asset import validates the configured byte limit and reads through a bounded stream, including protection against a file growing after its initial metadata check.
- Save-file cleanup after a failed atomic write is best-effort and does not hide the primary write exception.

### Profiler telemetry hardening

- Runtime profiler snapshots are capped at 1 MiB.
- Writers reject non-finite or negative metrics.
- Readers skip oversized, corrupt, stale or invalid snapshots and continue looking for another valid process snapshot.
- Process ID arguments are validated.

### Automated verification

- Added scene graph collection immutability, Transform finite-value, delta-time boundary, failed attachment cleanup, bounded asset import and profiler telemetry regressions to `PixelEngine.SmokeTests`.
- Added `verify-stage11.ps1`, which validates solution/project references, LIC fixtures and embedded payload signatures, then restores, builds Debug and Release, and runs smoke tests.
- Added `.github/workflows/ci.yml` to run that full verification on Windows for pushes, pull requests and manual dispatches.

## Verify on Windows

From the repository root in PowerShell:

```powershell
.\verify-stage11.ps1
```

Static checks only:

```powershell
.\verify-stage11.ps1 -StaticOnly
```

Run individual steps:

```powershell
dotnet restore .\PixelEngine.sln
dotnet build .\PixelEngine.sln -c Debug
dotnet build .\PixelEngine.sln -c Release
dotnet run --project .\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj -c Release -- .
dotnet run --project .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug
```

## Verification status for this archive

The source, project XML, script structure, fixture JSON/Base64 signatures and ZIP integrity were checked in the packaging environment. A C# compile and the executable smoke suite were not run here because the environment does not contain the .NET SDK or Windows GUI runtime. The new CI workflow is intended to provide that missing authoritative build/test result when pushed to GitHub or run locally on Windows.

## Not included in Stage 11

This stage does not claim to implement CCD/advanced rigid-body solving, skeletal animation/IK, GPU particles, C# sandbox execution, an embedded debugger, visual scripting, or Android publishing. Those remain separate feature stages after the current solution has a green build/test baseline.
