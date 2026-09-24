# OpenNotes.Core/Ink/HiddenInkStore.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

UI-free ordered store for `HiddenInkAnnotation` masks (`Caelum.Ink`), Task 7
Phase B. Hidden ink is deliberately NOT in `InkStrokeStore` — masks are study
mode covers, not ink: the lasso/eraser/undo stroke ledger must never see them.
One `HiddenInkStore` per `PdfPageControl`; visuals rebuild off `Changed`.

## API surface

- `Items` (`IReadOnlyList`), `Count`, `Contains` — read views.
- `IndexOf(item)` — identity is the annotation `Id` string, NOT instance
  equality (a deserialized clone resolves the same slot — load-bearing for
  undo-by-id).
- `AddQuiet(item)` / `InsertQuiet(index, item)` — append or clamped insert;
  both `Sanitize` then raise `Changed`. `InsertQuiet` is the undo-restore
  path (index captured pre-removal).
- `RemoveQuiet(item)` — removes the mask carrying `item.Id`; returns false
  when absent. `Clear()` empties quietly.
- `Changed` event — raised after every mutation (visual rebuild trigger).

## Constraints / NEVER Change

- `Sanitize` mirrors WPF `AddHiddenInk` defaults: empty `Id` → fresh
  `Guid.NewGuid().ToString("N")`; duplicate `Id` → regenerated until unique
  (the page's visual dictionary keys on Id); `Size <= 0` → 28.0; `A == 0`
  → 255 (masks are opaque covers); `RevealDurationMs <= 0` →
  `HiddenInkRevealState.DefaultRevealDurationMs`.
- No undo pushes, no UI — `HiddenInkAddedAction`/`HiddenInkRemovedAction`/
  `HiddenInksRemovedAction` (in `InkUndoActions.cs`) drive it; the editor
  owns the stack.
- Persistence flows through `PageAnnotation.HiddenInks` ↔
  `PdfService` strip/write (`/NM` ownership prefix + `/WNARevealMs`);
  the store itself never serializes.
