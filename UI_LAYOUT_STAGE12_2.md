# PixelEngine Studio — Stage 12.2 UI Layout

This pass moves the editor from a VS Code-style activity rail to a dock-first layout modeled on the workflow of a 2D game editor.

## Layout regions

1. **Menu row** — File, Edit, Node, Scene, View, Window, Project, Tools, Help.
2. **Workspace row** — 2D, 3D (disabled until a 3D renderer/runtime exists), Script, Game, and FileSystem shortcut.
3. **Global toolbar** — project actions, undo/redo, run/stop, active scene, object creation, script checks and build.
4. **Left dock** — Scene / Scripts / Prefabs / Search tabs above a persistent FileSystem asset browser.
5. **Center** — active scene document, Game preview and Script editor tabs; Scene toolbar contains transforms, snapping and overlays.
6. **Right dock** — Inspector / Node / Signals.
7. **Bottom dock** — Output / Problems / Terminal.
8. **Status row** — engine, active project and current status.

## Behavioral changes

- Asset selection in search opens the FileSystem dock. Prefab hits open the Prefabs tab. Script hits open the Script workspace.
- Scene hierarchy no longer mixes embedded files, scripts and prefabs into the object tree.
- Scene toolbar supports creating a node and expanding/collapsing or finding nodes.
- The active scene name is used as the center document tab title.
- FileSystem preview is hidden in a narrow dock and enabled automatically when the dock is widened.
- Dock persistence is versioned as `EditorLayout.v4.json`; old dimensions are not carried forward.

## Deliberate scope limits

- PixelEngine remains a 2D engine; the 3D workspace button is disabled.
- The Signals dock documents C# collision/trigger callbacks. Visual signal connections and Lua event binding are not available yet and are not presented as functional.
- Game preview still launches as a separate window; an embedded render surface is a later editor milestone.

## Verification

`verify-stage12.ps1` restores the solution, builds Debug and Release, and runs SmokeTests on Windows with .NET 8. This environment has no .NET SDK or WinForms runtime, so only source/configuration/archive checks can be performed here.
