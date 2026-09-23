# tools/winui-editor-smoke.ps1
> Last updated: 2026-09-23 (V6 Task 6 — editor shell smoke) | Protection: STANDARD

## Purpose
UIA-driven smoke for the WinUI `EditorPage` shell: seeds a throwaway library +
PDF under `OPENNOTES_DATA_ROOT`, launches the debug exe, drives Home → file
tile → editor, and asserts 60 checks — toolbar/sidebar AutomationIds, the
228/32 DIP margin contract, narrow auto-collapse, page navigation, zoom label,
search, context menu, and tab-close survival.

## What It Does
- Seeds `%TEMP%\opennotes_editorsmoke_*` data root (`recent_files.json` +
  generated multi-page PDF containing the text `BRAVO` on page 2).
- Verifies every WPF-parity AutomationId (toolbar, sidebar tabs/rail, thumbnails,
  outline tree, bookmark list/toggle, search panel + text box + results + status).
- Margin contract via `PagesContainer` DEBUG `HelpText` (`pages-margin-left=N`):
  228 expanded, 32 collapsed, 32 narrow (`Editor.DebugSidebarNarrow` toggle),
  228 restored.
- Navigation: page-jump box starts at 1, next/prev buttons, commit jump to page 3.
- Zoom: `Editor.ZoomInButton` → `Editor.ZoomLabel` accessible name = `110%`.
- Search: opens `PdfSearchPanel`, `SetValue("BRAVO")` → ≥1 result + `1*` status.
- Context menu: `Editor.ContextMenu.RotateCurrentPage` /
  `ExportCurrentPagePng1x` items present; tab close returns Home, process alive.

## Important Notes / NEVER Change
- **OS input does not reach the window in this automation session** — physical
  clicks land on the desktop site bridge and `SendKeys` goes to the wrong
  foreground window. Input-driven paths run through DEBUG invoke seams
  (`Editor.DebugCommitJump`/`DebugOpenSearch`/`DebugOpenContextMenu`) that call
  the SAME handlers real input uses; `SendKeys`/mouse remains as fallback.
- WinUI TextBox UIA `Value`/`TextPattern` can stay pinned after a `SetValue` +
  programmatic rewrite — verify the landed page through the
  `Editor.PageJumpGroup` `current-page=N` HelpText probe, not the box value.
- `$script:results` is the check-result list — never reuse `$results` as a
  local name (it clobbers the list and crashes `Check`).

## Open Threads / Resume Context
- **Status:** GREEN — 60/60 checks.
