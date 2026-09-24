# OpenNotes.Core/Ink/InkUndoActions.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

UI-free undo primitives for ink mutations (`Caelum.Ink`), ported from the WPF `EditorPage` private `IUndoAction` implementations — Task 7 Phase A.

## API surface

- `IUndoAction` — `Description`, `LeavesDocumentDirty`, `UndoAsync()`, `RedoAsync()` (async shape retained for parity; current actions complete synchronously).
- `InkStrokeAddedAction(store, stroke|placement)` — undo removes via token-resolved `RemoveStrokeQuiet(placement)`; redo re-inserts at the captured index.
- `InkStrokesErasedAction(store, removedOriginals, addedFragments)` — one erase gesture = one action. Undo removes fragments (descending index) then restores originals (ascending); redo is the mirror. Every removal resolves the live stroke by token/side first; a mid-sequence failure rolls back the half-applied gesture and leaves `LastOperationSucceeded=false` (the editor keeps the action on its stack as a no-op — WPF contract).
- `InkStrokeReplacedAction(store, token, originalIndex, originalSnapshot, idealSnapshot)` — replacement undo/redo through `TryReplaceStrokeQuiet`; absent tokens no-op (e.g. after an erase).
- `InkStrokesAddedAction(store, placements)` — Phase B: multi-stroke add (shape commit, Delete-selection redo); undo removes descending index, redo re-inserts ascending at captured placements.
- `InkStrokesRemovedAction(store, placements)` — Delete-key batch removal (WPF ItemsRemovedAction); undo restores all at placements ascending, redo removes descending.
- `InkSelectionMoveAction(store, strokes, dx, dy)` — undo −delta / redo +delta via `TranslateSpinePoints` + `NotifyGeometryChanged`; strokes no longer in the store are skipped (post-erase undo is a safe no-op).
- `InkSelectionResizeAction(store, strokes, totalScale, anchor)` — uniform scale about the opposite-corner anchor; points AND `stroke.Size` scale (WPF ScaleItemsDirectly), undo applies 1/totalScale.
- `InkSelectionRotateAction(store, strokes, totalDegrees, center)` — rotation about selection centre; undo counter-rotates.
- `InkSelectionCrossPageMoveAction(source, target, strokes, dx, dy, adjustX, adjustY, sourcePlacements)` — selection dragged onto another page: `ExecuteInitialTransfer()` moves strokes between stores once (idempotent, returns false → skip push when nothing moved); undo re-inserts at captured source placements and applies −(delta+adjust), redo mirrors.
- `HiddenInkAddedAction` / `HiddenInkRemovedAction(store, item, index)` / `HiddenInksRemovedAction(store, entries)` — mask add/remove/batch-remove against `HiddenInkStore` (NEVER `InkStrokeStore`); undo re-inserts at captured indices by Id identity.
- `InkStrokesStyleChangedAction(store, before, after)` — selection colour/size apply as one unit; per-stroke before/after tuples (not one shared value) so undo restores each stroke's own appearance + `NotifyGeometryChanged`.

## Constraints / NEVER Change

- Erase ordering (desc fragments → asc originals on undo; mirrored on redo) is load-bearing for z-order — do not "simplify".
- These operate purely on `InkStrokeStore`; they never touch UI, never raise user events, and never push themselves onto a stack (the editor owns stacks).
