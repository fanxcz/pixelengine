# PixelEngine 2.0 — Stage 12: Studio Workspace & Runtime Hardening

## Editor

- Dock-first Godot-inspired workspace: Scene/Scripts/Prefabs/Search dock and a persistent FileSystem browser on the left; central 2D Scene/Game/Script workspaces; Inspector/Node/Signals on the right; Output/Problems/Terminal below.
- Integrated task terminal with command history, asynchronous stdout/stderr, stdin forwarding, bounded output and process-tree Stop.
- MSBuild compiler errors/warnings are captured into the Problems panel with source path and line/column; double-click opens the source with the system editor.
- Problems panel with severity, diagnostic code, location and double-click navigation.
- Quick Open / project search (`Ctrl+P`) across scripts, scenes, objects, assets and prefabs.
- Command Palette (`Ctrl+Shift+P`) now includes build, editor build, smoke tests, search, terminal and diagnostics.
- Script tabs support middle-click close and `Ctrl+W`; right-click retains Close / Close Others / Close All.
- Fixed conflicting `Ctrl+Shift+F` shortcuts; Fit Scene uses `Ctrl+Alt+F`.
- Build Solution, Build Editor and Run Smoke Tests are available from the Tools menu or integrated terminal.
- Dark Studio color palette and custom ToolStrip renderer for menus, workspace modes, contextual scene tools, status strip and context menus; direct bottom Output / Problems / Terminal dock.

## Runtime hardening

- Camera2D sanitizes position, bounds, zoom and rotation at assignment boundaries.
- Screen/world conversion returns finite coordinates for invalid input.
- EngineTime has a monotonic total-time sanitizer; EngineGame uses it when reading the MonoGame clock.
- A failed component enable/start rolls back its Enabled state and invokes best-effort OnDisabled teardown.
- Rigidbody2D ignores force accumulation that would overflow to non-finite values.

## Regression tests

Smoke tests cover Camera2D finite values and coordinate conversion, EngineTime total-time fallback, and component activation rollback.

## Developer workspace

- `.vscode/tasks.json`: solution build, editor build, SmokeTests, Editor launch and ASHFALL launch.
- `.vscode/launch.json`: debugger profiles for PixelEngine Editor and ASHFALL.
- `.vscode/settings.json` and recommended extensions for C# / EditorConfig.
- `Run-Editor.cmd` launches the Studio editor from the project root.
- Root `.editorconfig` and `verify-stage12.ps1` for repeatable local verification.

## Stage 12.2 — Godot-inspired dock layout

- Top chrome is ordered deterministically as menu, workspace modes (2D / 3D / Script / Game), and global project/run toolbar.
- Left dock is split vertically: Scene / Scripts / Prefabs / Search on top and FileSystem assets below. The Scene tree contains scenes and scene objects only.
- Scene dock toolbar provides Add, Expand, Collapse and Find Node (Enter jumps through matching nodes).
- Center document tabs follow the active scene name plus Game and Script; the top workspace buttons switch the same modes.
- 3D is intentionally disabled because PixelEngine's current runtime is 2D-only.
- Right dock exposes Inspector / Node / Signals. Signals lists the existing C# collision and trigger callbacks and states that visual/Lua signal wiring is not implemented yet.
- FileSystem uses a compact two-row toolbar; the asset preview automatically appears only when the dock is wide enough.
- Layout persistence moved to `EditorLayout.v4.json`, avoiding old VS Code-style pane sizes.

## Shortcuts

- `Ctrl+Shift+P` — Command Palette
- `Ctrl+P` — Quick Open / Project Search
- `Ctrl+B` — Explorer sidebar
- `Ctrl+Shift+F` — Search panel
- `Ctrl+`` — Integrated terminal
- `Ctrl+Shift+B` — Build solution
- `Ctrl+W` — Close active script tab
- `Ctrl+Alt+F` — Fit Scene
- `Ctrl+Shift+E` — Toggle FileSystem dock
- `Ctrl+Alt+S` — Show project scripts

## Verification status

The archive is structurally checked (project XML, JSON, LIC fixtures, source delimiters and patch consistency). A real .NET build and WinForms rendering check must still be executed on Windows with the .NET 8 SDK; this environment does not contain `dotnet`, `csc`, `msbuild` or Mono, so no successful compilation or visual runtime claim is made here.


## Stage 12.1 — Workspace layout refinement

The editor chrome has been reorganized after visual review of the 1920×1080 workspace:

- Global toolbar is limited to project, edit/history, run, active-scene, and build actions.
- Transform, snapping, grid, labels, collider overlays, and Fit Scene controls now live in a dedicated Scene toolbar above the viewport.
- The script toolbar is grouped into Navigate, Symbols, Code, and Format/View instead of exposing every command in one long row.
- Historical Stage 12.1 used compact Explorer tabs (`Tree`, `Assets`, `Prefabs`, `Search`) and a VS Code-style Activity Bar. This layout was superseded by Stage 12.2 below.
- Dock layout storage in Stage 12.1 used `EditorLayout.v3.json`; Stage 12.2 now uses `EditorLayout.v4.json`.
- Minimum explorer and inspector widths were increased to keep labels and controls legible.

The updated layout is source-level validated. A Windows build is still required to confirm WinForms rendering and interaction on the target machine.
