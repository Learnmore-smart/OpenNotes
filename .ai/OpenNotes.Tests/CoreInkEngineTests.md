# OpenNotes.Tests/CoreInkEngineTests.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Headless coverage for the Task 7 Phase A Core ink stack — `StrokeOutline` tessellation, `InkStrokeData`⇄`StrokeAnnotation` `[x,y,p]` converters, `InkStrokeStore` placement/token semantics and `InkUndoActions` — 22 tests, all UI-free (no STA needed).

## Coverage

- `StrokeOutline.BuildFillOutline`: outline bounds grow with pressure, `IgnorePressure` uniform width, caps extend past endpoints by rendered radius, FitToCurve resampling, single-point disc.
- Persistence: `[x,y,p]` round-trip (incl. JSON serialize/deserialize), legacy `[x,y]` → pressure 0.5, malformed rows skipped, `IgnorePressure` never serialized.
- `InkStrokeStore`: `EnsureStrokeToken` stability, `CaptureStrokePlacement` caching, quiet add/remove at index, `TryCaptureCurrentStrokePlacement` token/side resolution after replacement, `TryReplaceStrokeQuiet` safe no-op on stale token.
- Undo actions: `InkStrokeAddedAction` undo/redo at captured index; `InkStrokesErasedAction` fragment-desc/original-asc ordering + rollback on mid-sequence failure; `InkStrokeReplacedAction` original↔ideal swap + no-op after erase.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-23 | Task 7A: 22 tests for outline/store/undo/persistence. | Devin |
