# OpenNotes.Core/Ink/InkUndoActions.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

UI-free undo primitives for ink mutations (`Caelum.Ink`), ported from the WPF `EditorPage` private `IUndoAction` implementations — Task 7 Phase A.

## API surface

- `IUndoAction` — `Description`, `LeavesDocumentDirty`, `UndoAsync()`, `RedoAsync()` (async shape retained for parity; current actions complete synchronously).
- `InkStrokeAddedAction(store, stroke|placement)` — undo removes via token-resolved `RemoveStrokeQuiet(placement)`; redo re-inserts at the captured index.
- `InkStrokesErasedAction(store, removedOriginals, addedFragments)` — one erase gesture = one action. Undo removes fragments (descending index) then restores originals (ascending); redo is the mirror. Every removal resolves the live stroke by token/side first; a mid-sequence failure rolls back the half-applied gesture and leaves `LastOperationSucceeded=false` (the editor keeps the action on its stack as a no-op — WPF contract).
- `InkStrokeReplacedAction(store, token, originalIndex, originalSnapshot, idealSnapshot)` — replacement undo/redo through `TryReplaceStrokeQuiet`; absent tokens no-op (e.g. after an erase).

## Constraints / NEVER Change

- Erase ordering (desc fragments → asc originals on undo; mirrored on redo) is load-bearing for z-order — do not "simplify".
- These operate purely on `InkStrokeStore`; they never touch UI, never raise user events, and never push themselves onto a stack (the editor owns stacks).
