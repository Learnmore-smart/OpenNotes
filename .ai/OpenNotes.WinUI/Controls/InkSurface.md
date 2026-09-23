# OpenNotes.WinUI/Controls/InkSurface.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

The custom pointer-ink pipeline for the WinUI editor (`Caelum.Controls`, Task 7 Phase A) — replaces WPF `InkCanvas`/`StylusPlugIn`/`DynamicRenderer` wholesale. A `Canvas` hosted inside each `PdfPageControl` between `ImageOverlayCanvas` and `ShapePreviewCanvas`.

## What it does

- **Input arbitration**: `DefaultTool` (`InkToolKind.Pen`/`Highlighter`/`Eraser`/`Select`/`None`; None+Select ignore strokes), `PenOnlyMode` (rejects non-pen pointers — `PointerPointProperties.IsLeftButtonPressed` carves out the pen-as-touch edge case), `TouchEnabled` hard kill, `IsInputEnabled`, `PressureEnabled` (drives `IgnorePressure`).
- **Erase tool routing** (`ResolveTool`): `IsEraser`/`IsInverted` → eraser, `IsBarrelButtonPressed` + `BarrelEraseEnabled` → eraser, else DefaultTool. `ApplyBarrelOverride`/`ClearBarrelOverride` hold the override across already-captured strokes (mid-stroke button).
- **Gesture state machine**: `OnPointerPressed` (tool resolve → erase-whole instant kill → capture + erase-stamp state OR stroke start) → `OnPointerMoved` (per-`PointerPoint` in the intermediate-points list, throttled by `MinMoveDeltaDips=0.5` — `DispatcherTimer`-erased points excluded) → `OnPointerReleased`/`Cancel`/`CaptureLost` → `FinishStroke`/`FinishErase`.
- **Stroke build**: pointer → page-DIP points via `GetCurrentPoint` (XAML already normalizes raster→DIP); pressure = `PointerPointProperties.Pressure` clamped 0–1; stroke color/size/alpha/highlighter/FitToCurve/IgnorePressure snapshot at press. Completed stroke → `Store.AddStrokeQuiet` + `StrokeCollected` (the editor pushes `InkStrokeAddedAction` from that event).
- **Eraser**: `EraseWholeStroke` (simple hit via `EraserHitsStroke`) vs point-erase: swept square stamp (`SplitStrokeAtEraser`, `EraserSize` DIP, `WholeStrokeEraseEnabled` still honoured for backwards compat) producing `EraserPath`/`EraserStamps`/`EraserSnapshotPath` + `RemovedPlacements`/`AddedFragments`; `EraserGestureEnded` carries the `InkStrokesErasedAction` payload. `EraserCanvas`/`EraserIndicator` visuals live on `PdfPageControl`.
- **Rendering**: `_strokeLayer` Canvas children (via `StrokeRenderer.CreateStrokeElement` / `CreateLiveStrokeElement`); `RenderStroke`/`UnrenderStroke`/`RefreshVisuals` (display-index keys; `RefreshVisuals` only when placements actually shifted); `LiveStrokeElement` swap on finish.
- **Stylus debug**: `GetStylusPointDebugInfo` — channel property flags + `IsBarrelButtonPressed`/`IsEraser`/`IsInverted`/`IsInRange`/`Pressure` dump per point (DEBUG-gated log through `StylusDebug`).

## Lifecycle / constraints

- `Dismiss()` (PdfPageControl `CancelInteraction`): clears capture via `InputManager`, kills pending erase state + live visual — survives ScrollViewer pawns. `Release()` detaches handlers and clears the store.
- `CapturePointer` must run on the UI element that owns the pointer event chain — `TryCapture` guards against the already-captured case.
- `StrokesChanged`/thumbnail invalidation is EditorPage's job via `Store.Mutated` + `StrokeCollected`/`StrokesErased`/`StrokeShapeReplaced` — the surface itself only mutates the store quietly.
- `Store` is public read-only — EditorPage needs it for undo placements and shape replacement (`ReplaceRecognizedStroke` via `TryReplaceStrokeQuiet` + `ReplaceVisual`).
- Shape/selection/hidden-ink/laser/ruler are Phase B — the surface deliberately has no lasso/shape-preview state.

## Open Threads / Resume Context

- **Status:** GREEN — Phase A complete; Phase B (selection/shapes/hidden ink/laser/ruler) will extend this file.
