# OpenNotes.Tests/WinUiThumbnailReorderAndImmersiveSourceTests.cs
> Last updated: 2026-09-26 | Protection: STANDARD

## Purpose

Source-contract fixture pinning the G5/G6 port (`a9088e1`):

- `ThumbnailDragPayload` stamp (`Caelum.ThumbnailDragPayload` format id,
  SourceIndex + load-session + normalized path + item ref), `handledEventsToo`
  wiring (ListViewItem marks presses handled), `StartDragAsync` (no
  `CanReorderItems` — the collection must not move before the lease pipeline
  validates).
- Drop reads `e.DataView.GetDataAsync` — the package, not the swept gesture
  field (a landed drop can't go stale).
- `MovePageAsync` runs the full structural-op pipeline: payload-bound lease →
  `TryBeginDocumentEdit` → `BeginStructuralOperation` → dirty flush →
  `ReorderPagesAsync` → `ReloadDocumentForOperationAsync` →
  `TryRollbackStructuralOperationAsync` on post-write failure →
  `JumpToPage(finalIndex)` → `PageBookmarkService.ApplyPageMove` →
  `DocumentSnapshotAction` undo → `Editor.PageReorderFailed` toast.
- F11 ordering in `EditorPage_PreviewKeyDown`: immersive-Escape BEFORE the
  generic Escape; F11 gated on `!IsEditableTextInputFocused()`;
  `SetHostActive(false)` exits immersive (window-global presenter).

## Notes

Verified 2026-09-26 with `dotnet test` — fixture green. Headless contracts
only; the drop UX and FullScreen presenter swap are `[manual]` checklist rows.
