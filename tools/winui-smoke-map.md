# WinUI smoke scripts — WPF test-script mapping (Task 10)

> Maps each WPF `tools/Test-OpenNotes*.ps1` driver to the WinUI equivalent.
> WinUI scripts launch `OpenNotes.WinUI.exe` (Debug `bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\`)
> against a throwaway `OPENNOTES_DATA_ROOT` library — same isolation model as the WPF drivers.
> **Constraint reminder:** these scripts launch the real app; they are documentation targets here,
> not executed by headless agents.

## Mapping table

| WPF driver | Scope | WinUI equivalent | Status |
|---|---|---|---|
| `Test-OpenNotesUiAutomation.ps1` | Launch, More menu, Settings window, preview+save theme/language, `-SaveAndReopen` persistence | `winui-uia-smoke.ps1` | ✅ equivalent (chrome + tab UIA); settings-dialog leg now covered by `winui-home-smoke.ps1` More flyout + `WinUiDialogsServicesSourceTests` source contracts. A dedicated settings save/reopen leg is a smoke gap |
| `Test-OpenNotesEditorSmoke.ps1` | Seeded PDF → editor load, tool toggles, SavePdfButton, sidecar check | `winui-editor-smoke.ps1` (60 checks) | ✅ equivalent + sidebar/zoom/search/context-menu coverage |
| `Test-OpenNotesPointerSmoke.ps1` | Real pointer: strokes, whole-stroke eraser, textbox create/resize, undo, save, reopen | `winui-uia-pointer-smoke.ps1` | 🟡 partial — real SendInput clicks vs chrome; **ink-stroke + text-edit legs not yet ported** (see limitations) |
| `Test-OpenNotesAdvancedPointerSmoke.ps1` | Extended pointer flows | — | ❌ no equivalent yet |
| `Test-OpenNotesCrossPageKeyboardSmoke.ps1` | Cross-page drag via keyboard/UIA | — | ❌ no equivalent yet (cross-page move is unit-covered by Core undo tests) |
| `Test-OpenNotesHiddenInkSmoke.ps1` | Hidden-ink draw/reveal/erase/undo/save-reopen, real mouse | — | ❌ no equivalent yet (Core behavior covered by `HiddenInkTests`/`CoreInkPhaseBTests`) |
| `Test-OpenNotesThirdPartyViewerSmoke.ps1` | Poppler `pdfinfo`/`pdftoppm` + Edge headless on an OpenNotes-written PDF | — (tool, not shell-bound) | ➖ reusable as-is against a V6-saved PDF — it inspects the artifact, not the app; scheduling it in CI/manual release verification is a follow-up |
| `winui-home-smoke.ps1` | — | (V6-only) | 27 checks: tiles, folders, breadcrumb, search, selection bar, context menu, editor nav |
| `winui-uia-smoke.ps1` | — | (V6-only) | window + chrome + tab strip via UIA invoke |
| `winui-uia-pointer-smoke.ps1` | — | (V6-only) | real pointer clicks at UIA bounding rects (caption passthrough) |

Shared helper: `OpenNotesEditorAutomationIds.ps1` (WPF id contract). WinUI preserves the
`Editor.*` AutomationIds, so the contract rows remain valid where the ids exist;
container ids (`PagesContainer`, `DocumentSidebar`, `PdfSearchPanel`, `Editor.PageJumpGroup`)
need the `UiaPanels.cs` hosts because WinUI panels emit no AutomationPeer.

## Known environment limitation (pointer smoke)

`winui-uia-pointer-smoke.ps1` uses `SendInput`/`SetCursorPos` for **real OS-level input**.
In sandboxed/agent sessions input injection does not reach the window (the desktop belongs
to another session / `LockApp`), so pointer-driven legs are env-limited no-ops — this mirrors
the WPF pointer smoke's documented `WM_MOUSE*` fallback note. The WinUI editor exposes
DEBUG-only invoke seams (`Editor.DebugSidebarNarrow`, `Editor.DebugCommitJump`,
`Editor.DebugOpenSearch`, `Editor.DebugOpenContextMenu`, plus `pages-margin-left` /
`current-page` HelpText probes) that call the identical handlers — that is how
`winui-editor-smoke.ps1` verifies input-driven paths headlessly. Drag-reorder remains a
documented env-limited no-op even on desktop.
