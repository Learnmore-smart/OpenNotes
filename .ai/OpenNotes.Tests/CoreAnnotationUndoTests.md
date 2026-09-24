# OpenNotes.Tests/CoreAnnotationUndoTests.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Headless NUnit coverage for Task 8 Phase A — the UI-free annotation undo
ledger (`Caelum.Tests`, 20 tests). A `FakeHost` implementing
`IAnnotationContainerHost` stands in for the WinUI `PdfPageControl`, so
the Core actions are exercised exactly the way the editor drives them —
pinning one-action-per-gesture semantics, quiet-mutation ordering and the
cross-page transfer contract without any UI types.

## Coverage map

- **Text lifecycle** — `TextBoxAddedAction` (undo removes / redo re-adds),
  `TextBoxDeletedAction` (inverse), `TextEditSessionAction` (before/after
  text replay), `TextStyleChangedAction` (font-size + RGB snapshots),
  `TextFormatChangedAction` (bold/italic/family/alignment snapshot),
  `TextBoxMovedAction` (before/after positions),
  `TextBoxResizedAction` (bounds AND auto-width/auto-height flags restore
  with the undo).
- **Sticky notes** — `StickyNoteAddedAction`/`StickyNoteDeletedAction`
  marker round-trips, `StickyNoteMovedAction` clamped positions,
  `StickyNoteEditAction` text replay PLUS the model-payload fallback when
  the container is gone (a later re-add shows the reverted text).
- **Mixed selection transforms** — `AnnotationSelectionMoveAction`
  (−delta undo / +delta redo), `AnnotationSelectionResizeAction` (inverse
  scale about the anchor, degenerate-scale guard is the action's own),
  `AnnotationSelectionRotateAction` (±degrees about centre).
- **Combined add/remove** — `AnnotationItemsAddedAction` (undo clears the
  live selection FIRST then removes strokes descending-index and
  containers; redo restores placements ascending + containers),
  `AnnotationItemsRemovedAction` (undo restores placements ascending so
  z-order is preserved, redo removes both legs).
- **Cross-page move** — `AnnotationSelectionCrossPageMoveAction`:
  `ExecuteInitialTransfer` moves strokes (pre-adjusted by the
  container→page adjust — the action owns it) and transfers containers
  WITH their sticky payload (`GetOverlayData` → `SetOverlayData`), is
  idempotent, returns false when nothing exists to move; undo returns
  strokes to captured source placements + applies −(dx+adjust), redo
  mirrors; `LastOperationSucceeded=false` when the live stroke vanished
  (the editor keeps the action on the stack as a no-op — WPF contract).
- **Guarded container legs (quality pass, 2026-09-23)** —
  `CrossPageMove_InitialTransferSkipsUnhostedContainer` (ghost container
  skipped, hosted leg still moves, flag set),
  `CrossPageMove_UndoFlagsVanishedContainer` (container torn down on the
  target → flag, no phantom re-add),
  `ItemsAdded_UndoFlagsContainerTheHostDoesNotOwn` +
  `ItemsRemoved_UndoReaddsContainers_RedoRemoves` (the new
  `LastOperationSucceeded` surface on the batch actions). `FakeHost`
  gained `ContainsTextContainer` + `MarkText`; `SetTextContentQuiet`
  gates on the `_text` payload marker (real-host parity — payload
  presence, not canvas membership, survives detach).

## Constraints

- UI-free only — `FakeHost` records mutations against plain `object`
  containers; the real `PdfPageControl` binding is covered by
  `EditorTextStickySourceTests` (source contract) not runtime tests
  (WinUI types can't load under the WPF test host).
- Stroke-side expectations use real `InkStrokeStore`/`InkStrokeData` —
  placements and z-order are part of the pinned contract.
