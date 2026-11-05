# PixelEngine 2.0 Stage 8 — compiler fixes (2026-11-04)

## Fixed

1. `PixelEngine.Core/Animation/AnimationEvaluator.cs`
   - Changed the nearest-keyframe distance accumulator from `float` to `double`.
   - The distance is already computed as `double`; retaining that type avoids CS0266 and correctly handles the distance between two finite `float` timestamps whose difference exceeds `float.MaxValue`.

2. `PixelEngine.Core/Diagnostics/ProjectDiagnostics.cs`
   - Removed the extra selector argument from `ToDictionarySafe`.
   - The existing helper already reads `LicScriptData.Name`, skips blank names, and resolves duplicate names deterministically (first entry wins, case-insensitive).

3. `PixelEngine.SmokeTests/Program.cs`
   - Added a regression assertion that nearest-keyframe selection still chooses the exact matching key when the other finite timestamp is `-float.MaxValue`.

## Verification

- Verified that all 6 `ProjectReference` paths in the 10 project files resolve inside the archive.
- Verified the `.csproj` files are well-formed XML.
- Verified the changed source lines and the new regression assertion are present.
- Verified the generated ZIP archive integrity.

A live `dotnet build` and smoke-test run could not be performed in this environment because the .NET SDK (`dotnet`) is not installed. On Windows, run from the extracted project root:

```powershell
dotnet clean PixelEngine.sln
dotnet restore PixelEngine.Editor/PixelEngine.Editor.csproj
dotnet build PixelEngine.Editor/PixelEngine.Editor.csproj -c Debug
dotnet run --project PixelEngine.SmokeTests -c Debug
dotnet run --project PixelEngine.Editor -c Debug
```
