# PixelEngine 2.0 — Stage 5: Studio workflow polish

Stage 5 builds on the Stage 4 authoring features and improves the daily editing workflow. The LIC v1 document schema is unchanged.

## Script Editor improvements

- Script tabs remain open while switching between scene, assets and prefabs.
- `Ctrl+Space` opens context-filtered completions. Typing after `api.` / `self.` and backspacing refreshes the list; Escape closes it; Enter/Tab inserts the selection.
- Completion descriptions explain the PixelEngine API. `F1` opens the searchable API reference.
- Auto-paired brackets and quotes, smart newline indentation, matching-bracket highlight and line-number gutter remain enabled.
- `Ctrl+Shift+I` applies conservative block reindentation (Lua `function` / `if` / `do` / `repeat` blocks or C# braces). This is an indentation helper, not a syntax formatter; it does not rewrite tokens or statements.
- `Ctrl+Shift+O` opens the function Outline selector; `Ctrl+F` opens Find/Replace; `Ctrl+L` goes to a line; `F2` / `Shift+F2` manage and navigate bookmarks.
- The Editor dock's toolbar/menu Undo/Redo now operate on the active script's native text history while the Script workspace is active. Outside the Script workspace they operate on bounded project snapshots.
- Diagnostic status is clickable and navigates to the first reported line.

The editor remains a Godot-inspired WinForms editor based on `RichTextBox`, not a full language-server editor. No LSP, code folding, semantic refactoring, breakpoints or integrated debugger are provided. Lua is syntax-checked through MoonSharp; C# source is retained and colored but not executed.

## Prefabs and components

- Prefabs can be created, duplicated, renamed, instantiated, edited as templates, updated from scene objects and propagated to linked instances.
- Removing a prefab safely unpacks linked instances. The Prefab dock supports F2 rename, Ctrl+D duplicate, Enter instantiate and Delete remove.
- Component Inspector supports type-specific property sets for SpriteRenderer, Rigidbody2D, BoxCollider2D, CircleCollider2D, AudioSource, Animator and Script.
- Components can be added, copied, reordered, reset and removed. The Components list supports Ctrl+D copy, Delete remove and Alt+Up/Down reorder.
- Unknown component extension fields are copied and retained by LIC serialization.
- Component properties are authoring metadata. Only fields explicitly mapped in runtime code affect the running simulation/rendering.

## Assets

- Import PNG/WAV by file picker or drag-and-drop; filter by name/type and optionally hide unused resources.
- PNG preview shows dimensions. WAV preview reports format/duration and now has Play/Stop preview controls using Windows `SoundPlayer`.
- Assign, export, rename and remove actions are available. Rename updates sprite/texture/clip references in scenes and prefab templates. Remove clears references before deleting the embedded asset.
- Runtime Lua `api.play('sound.wav')` uses the MonoGame audio service. The AudioSource component's PlayOnAwake/loop/volume settings are authoring metadata and are not yet automatically driven by the runtime.
- Asset storage is still a flat embedded list inside `.lic`; folders, streaming import and waveform editing are not implemented.

## Undo / Redo

- `Ctrl+Z` / `Ctrl+Y` (or `Ctrl+Shift+Z`) route to native script text undo/redo while the Script workspace is active and text history is available.
- The toolbar and Edit menu use the same routing, so clicking Undo while editing code undoes the text before it reverts a project-wide operation.
- For scene, prefab, component and asset authoring, history stores complete LIC JSON snapshots (maximum 60 entries and ~24M characters per stack). Script changes are debounced into project snapshots after a brief pause.

## Validation

Smoke tests cover LIC v1 round-trips, unknown JSON fields, scene add/duplicate/delete/startup, prefab lifecycle and safe unlinking, component metadata cloning, PNG import/rename/removal across scene and prefab references, Base64 validity, and Lua syntax of the bundled fixtures.

The development environment used to create this archive does not include the .NET SDK. The archive can be checked for ZIP integrity, project XML and fixture JSON/Base64, but `dotnet restore`, `dotnet build`, SmokeTests and Windows GUI/audio playback must be run on a Windows/.NET 8 machine.

### Static checks performed while packaging

- All 4 project references in `PixelEngine.sln` resolve to existing `.csproj` files.
- All project XML files parse successfully.
- All 6 bundled `.lic` fixtures parse as JSON and all embedded asset Base64 payloads decode.
- The edited editor/serialization source files passed a delimiter/token-structure scan.
- ZIP integrity is checked after packaging.

These checks are not a substitute for `dotnet build` or running the WinForms and MonoGame applications. The .NET SDK is not installed in the packaging environment.
