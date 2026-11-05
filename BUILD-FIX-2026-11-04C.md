# PixelEngine 2.0 Stage 7 — Startup shortcut crash fix

## Root cause

The Node menu assigned `Keys.F` (numeric value 70) directly to `ToolStripMenuItem.ShortcutKeys` for “Focus Selected”. WinForms does not accept a bare printable letter as a ToolStrip menu shortcut and threw `InvalidEnumArgumentException` during `BuildMenu()`, before the main form could appear.

## Changes

- Changed the menu item to have no ToolStrip shortcut; its label now says `Focus Selected (F in Scene View)`.
- Retained the existing `F` key handler in the Scene View (`ProcessCmdKey`) so the intended viewport shortcut still works when the Scene View has focus.
- Hardened the `MenuItem` helper: bare A–Z and 0–9 keys are not assigned to `ShortcutKeys`. Modifier combinations and special/function keys remain available.

## Verification

- Static source checks confirm the invalid `Keys.F` menu assignment is gone.
- Static source checks confirm the Scene View `F` handler and `Ctrl+Shift+F` Fit Scene shortcut remain.
- MainForm delimiter balance check passed.
- The previous user-provided build log confirms the solution compiled with 0 warnings and 0 errors before this runtime-only change.
- A new .NET build and Windows Forms launch could not be run in this Linux environment because the .NET SDK / Windows desktop runtime are unavailable.
