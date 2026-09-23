# OpenNotes.WinUI/Controls/InkSurface.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

The custom pointer-ink pipeline for the WinUI editor (`Caelum.Controls`, Task 7 Phase A) — replaces WPF `InkCanvas`/`StylusPlugIn`/`DynamicRenderer` wholesale. `InkSurface : Canvas` is hosted inside each `PdfPageControl` between `ImageOverlayCanvas` and `ShapePreviewCanvas`; it fills the page grid 1:1, so element coordinates ARE page-DIP coordinates at any zoom.

## What it does

- **Configuration surface** (EditorPage pushes these via `ApplyToolToAllPages`): `Tool` (`InkSurfaceTool.None`/`Pen`/`Highlighter`/`Eraser` — Phase-B tools map to `None`), `PenColor`, `HighlighterColor`, `PenSize`, `HighlighterSize`, `EraserSize`, `PenOnlyMode` (blocks non-pen ink CREATION only — erasing is not an ink-creation mode, WPF `IsInkCreationModeActive` parity), `EnablePressure` (drives `InkStrokeData.IgnorePressure`), `WholeStrokeEraser`, `InkSimulationEnabled`, `ShapeRecognitionEnabled`, `StrokeSmoothingLevel` (0=Off/raw, 1-3 moving-average window), `InputEnabled` (host gate — modal dialogs/inactive document), `EraserIndicator` (host-owned `Ellipse` in `EraserCanvas` the surface moves/sizes), `Store` (`InkStrokeStore`, public get-only).
- **Input arbitration**: routed events only — `PointerPressed`/`PointerMoved`/`PointerReleased`/`PointerCaptureLost`/`PointerExited` handlers (`InkSurface_Pointer*`; WinUI 3 exposes no `OnPointer*` overrides). Pen + mouse are ink input; touch never is (it pans the ScrollViewer). The draw-vs-erase decision is sampled ONCE at pointer-down (`Tool==Eraser` OR pen `IsEraser` OR `IsBarrelButtonPressed`) and held for the whole gesture; `_activePointerId` + `CapturePointer` lock the gesture to one pointer. The WPF double-barrel press-to-toggle is not ported.
- **Stroke build**: `BeginStroke` snapshots colour/size/highlighter/`IgnorePressure` at pointer-down; `PointerMoved` appends every `GetIntermediatePoints` packet plus the current point past the 0.0001 dedup, pressure via `EffectivePacketPressure` (`Pressure` clamped 0–1, 0.5 fallback); `StrokeRenderer.UpdateStrokePath` refreshes the transient live `Path` (`_livePath`).
- **Commit (`CompleteStroke(point, shiftHeld)`)** — WPF `InkCanvas_StrokeCollected` order: the release packet is appended (same dedup — the up-point IS part of the stroke) → Shift sampled at stylus-UP straightens to first→last (`TryGetStraightEndpoints`, `FitToCurve=false`) → `SmoothPoints` (level 0 keeps raw + `FitToCurve=false`) → **shape recognition** → `SimulateInkFlow` (pen-only, ≥3 pts) → `Store.AddStrokeQuiet` + `StrokeCollected` (`EventHandler<InkStrokeData>` — the editor pushes `InkStrokeAddedAction`).
- **Shape recognition**: `ShapeRecognitionEnabled && !IsHighlighter && Points ≥ StrokeGeometry.MinRecognizedShapePoints` → `ScribbleShapeRecognition.TryReplaceWithRecognizedStroke(Store, stroke, out replacement)` adds the stroke and swaps it in place for the ideal outline (same token, `StrokeReplacementSide.Ideal`, `FitToCurve=false`/`IgnorePressure=true`) → raises `StrokeRecognized` (`InkStrokeRecognizedEventArgs`: `Token`/`OriginalIndex`/`OriginalSnapshot`/`IdealSnapshot`) INSTEAD of `StrokeCollected`; the editor pushes `InkStrokeReplacedAction` so undo restores the user's raw scribble (deliberate spec change — WPF pushed StrokeAddedAction on fresh strokes, dropping the whole gesture on undo). Recognition emits exactly one outline stroke; multi-part `ShapeGroupId`/`ShapePartIndex` groups are the shape tool's commit format (Phase B), not the recognizer's.
- **Eraser**: incremental visible-during-drag — `EraseAtPoint` builds the swept square-stamp path (`_lastErasePoint` + current), prefilters candidates by spine bounds, then `EraserHitsStroke` (WholeStrokeEraser) or `SplitStrokeAtEraser`; `ApplyErasedStroke` mutates the live store while accumulating net `_eraseRemovedPlacements`/`_eraseAddedPlacements` (a fragment re-clipped in the same gesture cancels out). `EndEraseGesture` → `StrokesErased` (`InkStrokesErasedEventArgs`) = one `InkStrokesErasedAction` payload per gesture. `CancelEraseGesture` rolls back (fragments out descending index, originals restored ascending).
- **Visuals**: `Store.Mutated` → `AddVisual`/`RemoveVisual`/`RebuildAllVisuals` — one `StrokeRenderer.CreateStrokePath` `Path` child per store stroke in draw order plus the transient live path; `InkMutated` fires on every visible change (hosts invalidate thumbnails).

## Lifecycle / constraints

- `CancelInteraction()` discards a live stroke / rolls back a pending erase and releases capture — called on tool switch, input-gate close and page teardown.
- `SetPenService(PenService)` applies `service.PressureEnabled` → `EnablePressure` and lets the surface feed it pen packets (`ProbePointer`/`NoteBarrelButton`) for capability probing — WPF `SetPenService` parity.
- `AddStroke(StrokeAnnotation)` is the quiet sidecar-load path — no `StrokeCollected`, no undo.
- Undo/dirty/thumbnail policy lives in `EditorPage` — the surface only mutates the store quietly and raises `StrokeCollected`/`StrokeRecognized`/`StrokesErased`/`InkMutated`.
- Selection/shape tools/hidden ink/laser/ruler are Phase B — the surface deliberately has no lasso/shape-preview state.

## Open Threads / Resume Context

- **Status:** GREEN — Phase A complete (pen/highlighter/eraser, pressure, recognition-on-collect); Phase B extends this file.
