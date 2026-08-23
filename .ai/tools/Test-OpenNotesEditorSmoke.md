# tools/Test-OpenNotesEditorSmoke.ps1
> Last updated: 2026-08-21（isolated real-PDF editor smoke）| Protection: STANDARD

## Purpose

Run a reproducible real WPF editor-load check without writing the user's `%LOCALAPPDATA%\Caelum` data. The script launches the built OpenNotes executable with unique temporary `LOCALAPPDATA`, `APPDATA`, and `OPENNOTES_DATA_ROOT` values, pre-seeds a real PDF as a temporary library entry, opens it through the real library tile flow, and verifies that editor toolbar controls become visible.

## Scope

- Removes only `WINDIR` from the child process while preserving `SystemRoot`, matching the WPF startup diagnostic.
- Uses UI Automation for the OpenNotes library tile; the temporary `recent_files.json` is only a test fixture and is removed with the rest of the isolated root.
- Does not draw, save, modify the source PDF, change settings, or touch Codex/AppData state.
- Cleans the unique temporary environment directory and reports whether cleanup succeeded.
- A PASS proves startup, real library navigation, PDF load, and toolbar exposure; it does not prove mouse/stylus geometry, PDF export fidelity, third-party viewers, or Codex migration.

## Evidence

The script is intended to be run after rebuilding with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Test-OpenNotesEditorSmoke.ps1 `
  -PdfPath 'D:\Noah\文档\School\MP(Cegep)\Semester 2\MariHacks2026_overnight_consent.pdf'
```

The corrected run reached the real `EditorPage`, exposed `TextToolButton`, all primary drawing/tool controls, `SavePdfButton` and `PdfScrollViewer`, then successfully toggled Pen, Highlighter, Hidden Ink, Eraser, Shape, Laser, Ruler, Select and Text through UIA `TogglePattern`. It reported `EDITOR_SMOKE_RESULT=PASS` with two isolated sidecar files and `ISOLATED_ENV_CLEANED=True`. The first run had exposed a WPF `RemoveHandler` delegate-type mismatch in `InstallScrollbarTrackJump`; the explicit `MouseButtonEventHandler` fix is included in the verified build.
