# Build fix — Stage 10 — Scene namespace/type collision

## Symptom

`Physics2DWorld.cs(23,22): error CS0118: "Scene" является пространство имен, но используется как тип.`

## Cause and fix

The project has both the namespace `PixelEngine.Scene` and the class `PixelEngine.Scene.Scene`. In `PixelEngine.Physics`, the unqualified parameter type `Scene` can resolve to the namespace. The physics world now declares `using SceneType = PixelEngine.Scene.Scene;` and uses `SceneType` in `Step(...)`. This is only a compile-time qualification; it does not change runtime behavior or public object semantics.

The Stage 10 verification script also contains a static guard for this alias so the ambiguity is caught before the build step.

## Verify on Windows

```powershell
dotnet build .\PixelEngine.sln -c Debug
dotnet run --project .\PixelEngine.SmokeTests -c Debug
.\Run-Stage10-Showcase.cmd
```

The authoring environment for this fix does not contain the .NET SDK, so a successful C# build is not claimed here.
