# tools/OpenNotesEditorAutomationIds.ps1
> 2026-08-28: replaced the retired TextAnnotationDragHandle alias with TextAnnotationMoveBorder; resize/page/navigation aliases remain unchanged.
> Last updated: 2026-08-24 (Wave4 review follow-up in progress) | Protection: STANDARD

> 2026-08-25 GREEN: removed `Editor.Sidebar.Resize` from the alias map because the sidebar is fixed-width; all page-jump, sidebar navigation/collapse, viewer and dynamic-row IDs remain stable.

## Purpose

Single source of truth for production editor toolbar AutomationId aliases used by the editor, pointer, advanced-pointer, Hidden Ink and cross-page smoke scripts.

## Important Notes / NEVER Change

- Toolbar and navigation ids must match the `Editor.*` ids declared by `Pages/EditorPage.xaml` and refreshed by `ApplyToolbarAccessibilityMetadata()`.
- Preserve `HiddenInkToolButton` as the compatibility id used by existing Hidden Ink smoke and UIA integrations.
- Do not reintroduce Fit Width/Fit Page ids; zoom behavior is exercised through `Editor.ZoomOutButton`/`Editor.ZoomInButton`.

## Open Threads / Resume Context

- **Status:** baseline aliases, the three-page UIA smoke and the separately reported resize range, dynamic theme/HC and narrow/collapsed STA evidence are green; final parent review remains in progress. Foreground/device interaction remains an external smoke concern.
- **Intent/result:** smoke scripts dot-source this map for toolbar IDs, `Editor.PageJump`, the Pages/Outline/Bookmarks/collapse/resize rail commands, PDF viewer, page surfaces and text drag/resize peers; `HiddenInkToolButton` remains the only legacy toolbar compatibility alias. `SidebarPagePrefix`, `SidebarBookmarkPrefix` and `SidebarOutlinePrefix` describe dynamic item IDs. `Get-EditorPageAutomationId` and `Get-EditorTextResizeHandleAutomationId` reject invalid inputs and produce the runtime peer IDs.
- **2026-08-24 evidence:** the isolated three-page editor smoke exposed initial `Editor.PageJump` value `1`, committed page 2, invoked the dynamic `Editor.Sidebar.Outline.Page.2.Invoke` affordance to page 2, and selected `Editor.Sidebar.Outline.Page.2` to page 2; all required aliases were present and exact fixture/sidecar cleanup succeeded. The `.Invoke` suffix is derived from each dynamic outline item's stable AutomationId and is not a replacement for the parent TreeView SelectionItem id.
- **Verification:** helper invocation returns PdfPageControl.0, TextResizeHandle.BottomRight, TextAnnotationMoveBorder and PdfScrollViewer; parser and source contracts are green.
