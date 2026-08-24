# tools/Test-OpenNotesEditorSmoke.ps1
> Last updated: 2026-08-24（Wave4 review follow-up in progress）| Protection: STANDARD

## Purpose

Run a reproducible real WPF editor-load check without writing the user's `%LOCALAPPDATA%\Caelum` data. The script launches the built OpenNotes executable with unique temporary `LOCALAPPDATA`, `APPDATA`, and `OPENNOTES_DATA_ROOT` values, pre-seeds a real PDF as a temporary library entry, opens it through the real library tile flow, and verifies that editor toolbar controls become visible.

## Scope

- Removes only `WINDIR` from the child process while preserving `SystemRoot`, matching the WPF startup diagnostic.
- Uses UI Automation for the OpenNotes library tile; the temporary `recent_files.json` is only a test fixture and is removed with the rest of the isolated root.
- Dot-sources `OpenNotesEditorAutomationIds.ps1`; toolbar/tool invocations use the production `Editor.*` aliases while `HiddenInkToolButton` remains the compatibility id. Surface and text-handle IDs use the same map.
- Does not draw, save, modify the source PDF, change settings, or touch Codex/AppData state.
- Cleans the unique temporary environment directory and reports whether cleanup succeeded.
- A PASS proves startup, real library navigation, PDF load, toolbar exposure, initial compact page-jump UIA Value `1`, a multi-page page-2 commit, fallback-outline page-2 SelectionItem, the separate localized 32 DIP fallback row `.Invoke` button reaching page 2, and sidebar command invocation; it does not prove mouse/stylus geometry, PDF export fidelity, third-party viewers, or Codex migration.
- Baseline Wave4 required IDs include `Editor.PageJump`, `Editor.Sidebar.Pages`, `Editor.Sidebar.Outline`, `Editor.Sidebar.Bookmarks`, `Editor.Sidebar.Collapse`, `Editor.Sidebar.Resize`, `PdfScrollViewer` and dynamic page/outline items. Review follow-up also covers thumbnail selection guards, keyboard resize/range metadata, theme/HC refresh and narrow/collapsed layout; those remain separately evidenced by STA tests.
- Required production IDs now fail closed: a missing control throws and the script exits non-zero. Any future optional surface must be listed separately and may only emit an informational `OPTIONAL_CONTROL_MISSING` line.

## Evidence

The script is intended to be run after rebuilding with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Test-OpenNotesEditorSmoke.ps1 `
  -PdfPath 'D:\Noah\文档\School\MP(Cegep)\Semester 2\MariHacks2026_overnight_consent.pdf'
```

The final 2026-08-24 Wave4 follow-up run generated a fresh three-page PDF through `PdfService`, reached the real `EditorPage`, observed initial `Editor.PageJump` ValuePattern value `1`, committed it to page 2, discovered and invoked `Editor.Sidebar.Outline.Page.2.Invoke` to page 2, then discovered and selected `Editor.Sidebar.Outline.Page.2` to page 2. It also exposed the production toolbar/navigation IDs plus `HiddenInkToolButton`, `SavePdfButton` and `PdfScrollViewer`, invoked Pages/Bookmarks/Outline/Collapse, and toggled the existing tool controls through UIA `TogglePattern`. It reported `EDITOR_SMOKE_RESULT=PASS`, `ISOLATED_ENV_CLEANED=True`; the explicit fixture was then removed and verified absent (`FIXTURE_EXISTS=False`). The separate cross-page physical-input smoke remained blocked by `REAL_SCREEN_INPUT_UNAVAILABLE` (`foregroundHwnd=0`, `foregroundPid=0`); no physical-input pass is claimed.
