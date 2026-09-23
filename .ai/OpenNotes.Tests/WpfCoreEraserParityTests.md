# OpenNotes.Tests/WpfCoreEraserParityTests.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

STA fixture cross-validating the Core square-stamp eraser against the live WPF `System.Windows.Ink` implementation — the Task-7 review gate ("cross-validate `SplitStrokeAtEraser` against `Stroke.GetEraseResult` on a random stroke/eraser-path corpus").

## How it works

- Builds real `System.Windows.Ink.Stroke`s (`DrawingAttributes` Width=Height=size, `FitToCurve`, `IgnorePressure`, real `StylusPoint.PressureFactor`s), runs `Stroke.GetEraseResult` with a `RectangleStylusShape(eraserSize, eraserSize)` stylus shape along the same eraser path, and compares fragment count + per-fragment X extents against `StrokeGeometry.SplitStrokeAtEraser`.
- Corpus: straight/curved/diagonal/vertical spines, single-point stamps, swept multi-point paths, pressure ramps/tapers/step-pressure, `IgnorePressure`, dots, endpoint grazes, full erase, clean miss.
- Boundary ladder: `HitTest`-style threshold checks pin `eraser/2 + renderedHalfWidth` as the decision distance.

## Important Notes / NEVER Change

- `[RequiresSTA]`/`[Apartment(ApartmentState.STA)]` — WPF `Stroke` requires STA; do not move to a parallel-friendly fixture.
- Corner-graze cases tolerate ~0.4·strokeWidth divergence: WPF clips the tapered quad patch, Core clips the node-disc union — documented model difference, not a defect.
- Expectations were calibrated empirically (vertical spine swept horizontally with 20-DIP stamp + width-10 stroke cuts at x≈38/62, not 40/60).

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-23 | Task 7A: parity corpus for the square-stamp eraser replacing the scratch probe (`ZScratchProbe.cs`, deleted). | Devin |
