# PixelEngine 2.0 — Stage 13: Studio UI overhaul, real terminal and persisted preferences

Date: 2026-11-05
Branch: `arena/e14bace7-pixelengine`
Scope: in-place upgrade of the existing PixelEngine 2.0 Stage 12.2 tree. No project was recreated, no
feature was removed, and the four-solution layout (`PixelEngine.Core`, `PixelEngine.Editor`,
`PixelEngine.Sandbox`, `PixelEngine.SmokeTests`) is unchanged — one headless test project
(`PixelEngine.Editor.Tests`) was added.

## What changed and why

### 1. Studio shell: a real dark Windows design instead of stock WinForms

Everything visual now flows from one token file, `PixelEngine.Editor/Presentation/Theme.cs`:

| Token | Value | Used for |
| --- | --- | --- |
| `Window` / `Background` | `#0B0D12` | form and window background |
| `Panel` | `#141821` | dock panels, cards |
| `Toolbar` / `PanelAlt` | `#191E29` / `#1B202B` | toolbars, raised strips |
| `Viewport` / `Field` | `#0F1219` | scene canvas, list/field backgrounds |
| `Terminal` | `#080A0F` | terminal transcript |
| `Accent` | `#6E8BFF` | focus, selection, active tab underline |
| `Violet` | `#9B7CFF` | secondary accent (gradients, badges) |
| `Border` / `BorderSoft` | `#262D3A` / `#1C222D` | hairlines and grid lines |

* All colours, paddings, radii and widths go through `Theme.Px()`/`Theme.Pt()`, so the shell is
  DPI-aware (96–384 DPI) instead of fixed-pixel.
* `Presentation/StudioToolStripStyle.cs` — custom `ToolStripRenderer` + professional colour table:
  flat dark toolbars, hover/pressed states, accent checked state, no gradient artefacts.
* `Presentation/StudioTabControl.cs` — owner-drawn tabs with fixed DIP width, accent underline on the
  active tab, overflow indicator, no white flicker.
* `Presentation/StudioButton.cs` — `StudioButton` (primary/secondary/ghost/danger kinds), cards and
  section headers used by the dock panels.
* `Presentation/StudioControlStyler.cs` — recursive dark-mode application (TreeView, ListView,
  DataGridView, PropertyGrid, SplitContainer, ComboBox, ScrollBar) with a custom TreeView drawer and
  `.NET 8`-safe dark-mode probing (the .NET 9 `Application.SetColorMode` API is deliberately absent).

### 2. Responsive layout (no clipping at any window size)

* `MainForm.ApplyResponsiveLayout()` collapses/restores the Inspector (<1240 DIP), Explorer dock
  (<1080 DIP) and Bottom Dock (<640 DIP, restores at ≥700 DIP), and re-applies splitter proportions
  in DIPs, so no element is pushed off-screen when the window is resized.
* `MinimumSize` is 1000×620 DIP; the layout is re-evaluated on `Resize`, on `DpiChanged` and after a
  dock reset, with DPI-scaled splitter distances.
* Scene/Asset/Prefab/Component/Script panels and the Studio Tools window were converted from
  hard-coded pixels to `Theme.Px`, and their splitters now defer `SplitterDistance` until the handle
  exists (the old code assigned it against the 1000×600 design size, which clamped and clipped
  panels on high-DPI displays).
* `Stage8ToolsForm` uses the shared palette and `AutoScaleMode.Dpi` instead of ad-hoc colours.

### 3. Real terminal (PowerShell + CMD, no simulation)

`Presentation/TerminalSession.cs` + `Presentation/EditorTerminalControl.cs`:

* One persistent child process per shell kind, started with the appropriate
  `pwsh`/`powershell`/`cmd.exe` arguments; commands are written to stdin, stdout **and** stderr are
  read asynchronously, and every command reports its real exit code through an out-of-band sentinel
  (`__PIXELENGINE__EXIT:<token>:$LASTEXITCODE` / `%ERRORLEVEL%`), so the UI never guesses.
* Batched repaint (40 ms) with repeat collapsing and in-place transcript trimming (configurable
  scrollback, default 6000 lines trimmed to 4500) — no per-line full control rebuild.
* Never blocks the UI thread: all IO runs on the process' reader threads, UI touch points go through
  `PostToUi`/`BeginInvoke`.
* Shell selection (PowerShell / Command Prompt), working-directory awareness, `Ctrl+L` clear,
  and build diagnostics extracted from real compiler output into the Problems list, with MSBuild
  duplicate suppression (`×N` counts) and stderr fallback.

### 4. Persisted preferences (new)

`Presentation/EditorSettings.cs` — JSON at `%LocalAppData%\PixelEngine\EditorSettings.json`,
written atomically (temp file + replace), versioned, with `Normalize()` clamping every value:

* General: check scripts after open, save before launching, confirm before discarding, group repeated
  log lines, max output entries.
* Terminal: default shell, font size, scrollback.
* Script: font size, word wrap.
* Scene: grid size, snapping, grid/label/collider visibility.
* Recent projects: newest first, case-insensitive de-duplication, bounded to 12.

`Presentation/EditorPreferencesDialog.cs` — real preferences UI (General / Terminal / Script / Scene
sections, Restore defaults, Cancel keeps the previous values). Wired into `Tools → Preferences…`
(Ctrl+,). `File → Recent Projects` lists existing projects, shows a disabled hint when empty and can
clear the list. Scene-toolbar toggles now start from the saved state and are written back on exit.

### 5. Editor behaviour fixes

* `Open .lic` and recent-project activation confirm unsaved changes **before** the current project is
  replaced (previously the document could be swapped first and the confirmation came too late).
* `Run Preview` respects "Save before launching" and explains what to do when the project has never
  been saved, instead of silently failing or overwriting the file.
* `CheckScripts` has a `silentWhenClean` mode used on open (`AutoCheckScripts`), so opening a clean
  project does not spam Output; errors still surface and jump to the Problems tab.
* Stage 8 authoring tools now ask whether to save modified animation/light/particle/tilemap data and
  mark the document dirty instead of trying to save behind the user's back.
* Startup log, status bar and window title stay in sync with the real dirty state.

### 6. Core/runtime robustness

* `AssetStore.Dispose` no longer swallows failures silently: each atlas/texture/sound/instance
  disposal is guarded and reports the reason to stderr, so a broken audio device cannot take down
  shutdown and cannot hide a leak.
* `PersistentSaveStore` reports why a temporary file could not be deleted instead of an empty `catch`.

### 7. Tests

* New `PixelEngine.Editor.Tests` (headless, `net8.0-windows`, no test framework dependency) covering
  settings round-trip, hostile-value clamping, corrupt-file recovery, recent-project ordering,
  dock-layout serialization, MSBuild diagnostic parsing (both line shapes, including rejected
  summaries and truncated records) and loading every bundled `.lic` fixture through the editor's
  project reader.
* `BuildDiagnosticLineParser` was extracted from `MainForm` so those rules are testable.
* CI (`.github/workflows/ci.yml`) builds Debug **and** Release, runs the existing smoke tests in both
  configurations and now runs the editor tests, annotating each failure in the GitHub UI.

## Verification performed

| Check | Result |
| --- | --- |
| Windows CI: restore + Build Debug + Build Release | ✅ success |
| CI: `PixelEngine.SmokeTests` (Release and Debug) | ✅ success (34+ groups) |
| CI: `PixelEngine.Editor.Tests` | ✅ success (11 checks) |
| Structural C# syntax check over all projects | ✅ 0 files with syntax errors |

**Honest limitation:** this container has no .NET SDK and no Windows GUI, so no local `dotnet build`,
`dotnet run`, terminal session or window-resize inspection was executed here. The evidence above is
the Windows GitHub Actions pipeline (real compiler, real WinForms/MonoGame assembly build) plus
headless logic tests. Manual UI checks — dragging splitters, resizing to the minimum size, switching
DPI, typing commands in the terminal — still need a Windows machine with the .NET 8 SDK:

```powershell
dotnet build PixelEngine.sln -c Release
dotnet run --project .\PixelEngine.SmokeTests -c Release
dotnet run --project .\PixelEngine.Editor.Tests -c Release
dotnet run --project .\PixelEngine.Editor -c Debug
```

## Known issues / remaining work

1. MonoGame DesktopGL still needs a graphics device; the Sandbox and the F5 player cannot run
   headless, so gameplay rendering is verified only structurally.
2. The terminal runs commands interactively but is not a full VT100 emulator: it does not render
   ANSI cursor movement, progress bars redraw as new lines, and full-screen console apps (e.g. `vim`)
   are not supported. Use the shell's own window for those.
3. The terminal's `cd` tracking is textual (parsed from the command line), so a directory change made
   by a script inside the shell is only picked up when the shell prints a new prompt.
4. Preferences for the script editor and terminal font sizes apply to whole controls; per-scenario
   fonts are not implemented.
5. C# scripts remain authoring-only (no hot reload / dynamic compilation) — that was true before this
   pass and is unchanged.
