# OpenNotes.Core/Ink/InkStrokeStore.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Ordered, tokenized stroke collection for one page — the UI-free port of the WPF page's `InkCanvas.Strokes` + placement bookkeeping (`Caelum.Ink` namespace, Task 7 Phase A). One instance per `InkSurface`/page.

## API surface

- `InkStrokeStore` — `Strokes`/`Count`/`IndexOf`; `EnsureStrokeToken(stroke)` (assigns a stable `Guid` + `StrokeReplacementSide.Original` on first sight); `CaptureStrokePlacement(stroke)` (cached per instance via `_placementHistory`); `AddStrokeQuiet(stroke|placement)` (placement re-inserts at clamped index and re-attaches snapshot identity); `RemoveStrokeQuiet(stroke|placement)` / `RemoveStrokeQuietExact(placement)`; `TryCaptureCurrentStrokePlacement(token|placement, side, out current)` (fresh-index resolution by token/side); `TryReplaceStrokeQuiet(token, expectedSide, snapshot, out index)` (in-place swap, fails safely without appending); `GetStrokeSide(token)`; `Clear()`.
- `InkStrokePlacement` — `Owner`/`Stroke`/`Snapshot`/`Token`/`Side`/`Index`; `ForOwner(owner, index)` rebinds for restore.
- `InkStoreMutationEventArgs`/`InkStoreMutationKind` (`Added`/`Removed`/`Replaced`/`Cleared`/`GeometryChanged`, `Quiet` flag always true) — store mutations never create undo actions; the ink surface/editor decides what enters history.
- `NotifyGeometryChanged(strokes)` — Phase B: raises one `Mutated(GeometryChanged)` per stroke AFTER an in-place spine/style mutation (selection move/rotate/scale, style apply) so surfaces rebuild the affected `Path`s — add/remove notifications don't fire for in-place edits.

## Constraints / NEVER Change

- Token identity is the contract: undo resolves the CURRENT live stroke by token+side (`TryCaptureCurrentStrokePlacement`) because replacement can swap the captured reference; a failed lookup is a no-op, never an append.
- Placement index is captured-once (history-cached) and may go stale — restore clamps, matching WPF.
- All mutators raise `Mutated` with `Quiet=true` — hosts use it for thumbnail/visual sync only; `StrokeCollected`-style user events live on `InkSurface`.
