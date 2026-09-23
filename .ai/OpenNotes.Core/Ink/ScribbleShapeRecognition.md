# OpenNotes.Core/Ink/ScribbleShapeRecognition.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

The recognition block of the WPF `InkCanvas_StrokeCollected` pipeline, ported UI-free (`Caelum.Ink`) so the WinUI `InkSurface.CompleteStroke` path and headless tests run the identical replace — wired by the spec-review fix (recognition was previously dead code: `TryRecognizeShape`/`InkStrokeReplacedAction`/`AppSettings.ShapeRecognition` existed but nothing invoked them).

## API surface

- `ScribbleShapeRecognition.TryReplaceWithRecognizedStroke(store, stroke, out RecognizedStrokeReplacement)` — gates on `!stroke.IsHighlighter` and `Points ≥ StrokeGeometry.MinRecognizedShapePoints`, runs `StrokeGeometry.TryRecognizeShape` on the spine, and on a hit: `AddStrokeQuiet(stroke)` (token + slot must exist first — WPF's InkCanvas inserts before `StrokeCollected` fires), captures the Original snapshot, builds the ideal stroke (same colour/size/highlighter, `FitToCurve=false`, `IgnorePressure=true`, outline points at uniform 0.5 pressure — the WPF `StylusPoint` default), then `TryReplaceStrokeQuiet(token, Original → idealSnapshot)` in place. A pathological replace failure re-removes the added stroke so the store looks untouched. Returns false without touching the store on any gate/classifier miss.
- `RecognizedStrokeReplacement` — `Token`/`OriginalIndex`/`OriginalSnapshot`/`IdealSnapshot`; exactly the payload `InkStrokeReplacedAction` needs.

## Constraints / NEVER Change

- In-place swap under the SAME token, Original→Ideal side flip — never append; a missing/stale token is a safe no-op via `TryReplaceStrokeQuiet`.
- The recognizer emits exactly ONE outline stroke — multi-part `ShapeGroupId`/`ShapePartIndex` strokes are the shape tool's commit format (Phase B), not scribble recognition.
- Caller order is load-bearing (WPF `InkCanvas_StrokeCollected` parity): Shift-straighten → smoothing → **recognition** → ink simulation — a recognized stroke is uniform-width, so simulating the raw stroke first would be wasted.
- The enabled flag lives on the host (`InkSurface.ShapeRecognitionEnabled` ← `AppSettings.ShapeRecognition`); the helper itself is unconditional once invoked.

## Verification

`CoreInkEngineTests` — `Recognize_Enabled_ReplacesStroke_UndoRestoresOriginal`, `Recognize_Disabled_KeepsRawStroke`, `Recognize_Gates_SkipHighlighterShortAndUnrecognizable`.
