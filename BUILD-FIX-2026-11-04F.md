# Build fix — 2026-11-04 (SortingLayerRegistry and Color overload)

Fixed two compiler errors reported when building `PixelEngine.Editor`: 

1. `Compatibility/LicObjectComponent.cs`: imported `PixelEngine.Graphics`, where `SortingLayerRegistry` is declared.
2. `Graphics/LightingRenderer.cs`: explicitly cast the RGB zero values to `byte` so the `Color(byte, byte, byte, byte)` constructor is selected unambiguously.

No rendering or gameplay behavior is otherwise changed.

Run from the repository root:

```powershell
dotnet build .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug
dotnet run --project PixelEngine.Editor -c Debug
```

The source changes and archive were inspected in this environment, but a .NET build could not be executed because the .NET SDK is not installed here.
