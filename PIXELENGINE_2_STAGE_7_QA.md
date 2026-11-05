# PixelEngine 2.0 Stage 7 — Package QA

## Checks completed in the authoring environment

- ZIP container integrity: tested after packaging.
- All 10 `.csproj` files parse as XML.
- All 4 project references listed in `PixelEngine.sln` point to files present in the package.
- All 8 bundled `.lic` files parse as JSON.
- All embedded Base64 blobs decode; embedded PNG resources have valid PNG signatures.
- Scene and prefab object sprite references, component texture references and script references resolve to resources included in each project.
- `Lic/demo.lic` contains 54 named objects, four embedded PNG resources and the `hero.lua`, `enemy.lua` and `bolt.lua` scripts.
- The demo scripts no longer log `pew` for every shot.
- Source review confirms the editor launches `PixelEngine.Sandbox` as a separate `dotnet run` process and stops the process tree. The editor no longer includes native HWND reparenting code.
- Inspector and Component tabs are configured to fill their dock areas.
- Stage 7 uses `%LOCALAPPDATA%/PixelEngine/EditorLayout.v2.json` so stale Stage 6 splitter measurements do not override the new default layout.
- The Stage 7 document contains a numbered 30-item editor feature checklist.

## Manual visual review

A static reference raster was generated from `Lic/demo.lic` scene coordinates and its embedded sprite PNGs, then inspected. It shows the Crystal Valley sample with a layered blue background, parallax-like hills, ground, platforms, foliage, sign, crystals, coins, hero and enemy. This is a data-derived preview image, **not** a screenshot from a running MonoGame process.

## Not run here

The authoring container does not have `dotnet`, `msbuild`, `csc`, `mono` or the .NET SDK. Therefore these actions have not been performed in this environment:

- `dotnet restore` / `dotnet build`.
- Actual MoonSharp runtime execution of the sample scripts.
- WinForms layout and dock interaction on Windows.
- Actual separate-window MonoGame launch, focus, resize or shutdown.

On Windows with .NET 8 installed, run:

```powershell
dotnet clean PixelEngine.sln
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln
dotnet run --project PixelEngine.SmokeTests
dotnet run --project PixelEngine.Editor
```

Press **F5** in the editor to launch the project in a separate game window. Press **Shift+F5** to stop it. Press **Ctrl+Shift+L** to reset the dock layout if you have manually resized panels before.
