# OpenNotes.Core/Geometry/StrokeOutline.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Tessellates an `InkStrokeData` spine into a closed fill outline — the pressure-variable hull the WinUI `StrokeRenderer` turns into `PathGeometry` (Task 7 Phase A). Per-point half-width follows `StrokeGeometry.GetRenderedStrokeHalfWidth` (the empirical WPF pressure law), so the silhouette matches what WPF's `System.Windows.Ink` renderer draws for the same DrawingAttributes.

## API

- `BuildFillOutline(spine, size, ignorePressure, fitToCurve)` / `BuildFillOutline(stroke)` → `List<PointD>` closed polygon (first point not repeated); empty list for degenerate input.
- `ResampleCatmullRom(spine)` — public for tests; endpoint-duplicated uniform Catmull-Rom, 4 subdivisions/segment, pressure lerped (clamped 0–1).

## Behaviour

- Offsets each spine vertex by `±n_i·w_i` (n = perpendicular of the averaged adjacent edge directions) → left chain + end-cap arc + reversed right chain + start-cap arc.
- Round caps: 7-segment half-disc arcs, outward direction verified against spine direction (a −90° rotation of the vertex normal).
- Single-point stroke → 12-gon disc of the rendered radius (WPF draws a tap stamp; we keep the point truthful rather than synthesizing a micro-segment).
- `fitToCurve=true` resamples through Catmull-Rom first — same role as WPF's bezier curve-fit: smooths the path AND densifies sparse spines so corner facets stay small.
- Joins are simple per-vertex offsets (no bevel/miter clipping): sharp hairpins can self-intersect briefly — visually acceptable, noted as a fidelity limit.

## Constraints

- UI-free: returns `PointD` lists; the WinUI layer converts to `Path`/`PathGeometry`.
- Outline direction convention: screen-space Y-down, caps sweep through the outward direction — changing the cap orientation folds the cap inward (regression-tested via end-cap extent assertions).
