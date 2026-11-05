# PixelEngine 2.0 — Stage 4: Studio authoring workflow

Stage 4 improves the editor workflow while preserving the legacy LIC v1 JSON schema and the original Bloody Pixels projects.

## Script Workspace (Godot-inspired)

The WinForms script editor now includes a searchable API reference (F1 / toolbar), contextual completion (Ctrl+Space and after `.`), project-aware `api.find("Object")` and `api.play("sound.wav")` suggestions, completion descriptions, auto-paired brackets/quotes, pair deletion, smart indentation, line numbers, active-line gutter highlight, syntax colors, matching-bracket highlight, function outline, bookmarks (F2 / Shift+F2 / Ctrl+Shift+F2), find/replace (Ctrl+F), go-to-line (Ctrl+L), line comment (Ctrl+/), duplicate line (Ctrl+D), move line (Alt+Up/Down), snippets, zoom and a script context menu.

This is a lightweight Godot-inspired editor, not a full Godot ScriptEditor clone: it uses WinForms RichTextBox, local completion dictionaries and regex-based highlighting. It does not provide LSP/semantic analysis, code folding, breakpoint debugging, refactoring or C# runtime execution. Lua validation is syntax validation through MoonSharp; it is not a type checker.

## Prefab workflow

- Create a prefab from the selected scene object.
- Duplicate, rename, instantiate, update from a selected scene object and delete prefabs.
- Selecting a prefab in the Scene Tree opens its template object in the Inspector and Component dock for editing.
- `Apply to instances` propagates template values to linked instances while preserving each instance name and transform.
- `Revert Selected Prefab Instance` restores template values while preserving the instance transform.
- `Unpack Selected Prefab Instance` removes the link without changing the object's data.
- Removing a prefab automatically unpacks linked scene objects rather than leaving dangling prefab names.

## Component Inspector

- Component-specific property sets are shown for SpriteRenderer, Rigidbody2D, BoxCollider2D, CircleCollider2D, AudioSource, Animator and Script.
- Add, duplicate, reorder, reset and remove component data.
- Unknown component JSON fields are retained when components are copied or the project is saved.

These components are authoring metadata and only a subset is mapped into the current runtime. The Inspector does not imply that every property already affects simulation or rendering.

## Asset Manager

- Import PNG/WAV by file picker or drag-and-drop.
- Filter by name and type; show only referenced assets.
- Preview PNG dimensions and inspect WAV format/duration metadata.
- Assign a selected asset to the selected object, export it, copy its asset name and rename it.
- Renaming updates references in scene objects, components and prefab templates.
- Removing an asset warns about references and clears them to prevent dangling links.
- The asset store remains a flat embedded list in the legacy `.lic` document. Folder hierarchy and WAV waveform/audio audition are not implemented.

## Undo / Redo

Project-level authoring changes can be undone/redone with Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z), toolbar buttons, Edit menu or Command Palette (Ctrl+Shift+P). History stores complete LIC JSON snapshots, capped at 60 entries and approximately 24 million characters per stack. Text editing retains the RichTextBox's native text undo while the script debounce commits project snapshots.

## LIC compatibility and tests

The file schema is unchanged. Scene, prefab, component and asset edits are serialized through the existing `LicProjectWriter`; unknown JSON extension fields remain supported. Smoke tests include safe prefab removal/unpacking and asset deletion/reference cleanup in addition to scene, prefab, import and round-trip tests.

## Build and run on Windows

```powershell
dotnet clean PixelEngine.sln
dotnet restore PixelEngine.sln
dotnet build PixelEngine.sln
dotnet run --project PixelEngine.SmokeTests
dotnet run --project PixelEngine.Editor
```

Open another project with:

```powershell
dotnet run --project PixelEngine.Editor -- Lic/knights.lic
```

## Validation status

This authoring environment does not contain the .NET SDK. ZIP integrity, project XML, LIC JSON/Base64 fixtures and source-structure checks can be verified here, but NuGet restore, C# compilation, MoonSharp smoke tests and live WinForms/MonoGame interaction have not been run in this environment. Run the commands above on Windows/.NET 8 and send the complete build output if a compiler error remains.
