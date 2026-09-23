# OpenNotes.WinUI/Rendering/StrokeRenderer.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Converts `InkStrokeData` into WinUI `Path` elements (`Caelum.Rendering`, Task 7 Phase A) — the visual half of the custom ink engine; geometry comes from `StrokeOutline`/`StrokeGeometry`, so the only rendering decisions here are fill colour and pressure width.

## API

- `CreateStrokeElement(stroke, liveIndex?)` → `Path` (`Fill`, `IsHitTestVisible=false`, `Opacity=1`, tag = stroke ref).
- `CreateLiveStrokeElement(spine, color, size, isHighlighter, fitToCurve, ignorePressure)` — same pipeline for the in-progress stroke (swapped for the completed element at `StrokeCollected`).
- `BuildStrokeOutline` → `StrokeOutline.BuildFillOutline`.
- `GetStrokeColor(stroke)` — `Windows.UI.Color` from RGBA channels.

## Behaviour notes

- Geometry: `PathGeometry` + `PathFigure` + `PolyLineSegment` (a single closed figure; `IsClosed=true`, `IsFilled=true`).
- Colour: `SolidColorBrush` straight from RGBA — highlighter alpha lives in `A` (WPF parity: `HighlighterAlpha=140` is baked into the stroke, NOT a separate opacity knob).
- Coordinates are already page DIP — no zoom/raster scaling here (the layer scales with the page control).

## Constraints

- Pure projection: no state, no events. Stroke identity lives on the `InkSurface` visual dictionary (`Tag` keeps the data ref for hit tests).
- Do NOT add selection adorners/shape previews here — Phase B adds a separate overlay.
