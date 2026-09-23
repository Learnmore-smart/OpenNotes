# OpenNotes.WinUI/Rendering/StrokeRenderer.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Converts `InkStrokeData` into WinUI `Path` fill geometry (`Caelum.Rendering`, Task 7 Phase A) — the visual half of the custom ink engine. The outline itself comes from `StrokeOutline.BuildFillOutline` (the same silhouette WPF's `System.Windows.Ink` renderer produces for the equivalent DrawingAttributes); the only decisions here are fill colour and figure assembly.

## API

- `CreateStrokePath(InkStrokeData)` → `Path` — `Fill = SolidColorBrush(ToColor(stroke))`, `Data = BuildGeometry(stroke)`, `IsHitTestVisible = false` (hit-testing lives in `StrokeGeometry` math, not XAML). The path is NOT parented — the caller adds it to the surface's `Children`.
- `UpdateStrokePath(Path, InkStrokeData)` — rebuilds `path.Data` in place; the live-stroke fast path (one `Path` per in-flight pointer, updated per move event).
- `BuildGeometry(InkStrokeData)` → `PathGeometry` — a single closed `PathFigure` (`IsClosed=true`, `IsFilled=true`) holding one `PolyLineSegment` over the outline polygon; empty/degenerate strokes produce an empty geometry.
- `ToColor(InkStrokeData)` → `Windows.UI.Color` straight from the stroke's RGBA channels.

## Behaviour notes

- Coordinates are already page DIP — no zoom/raster scaling here (the layer scales with the page control).
- Highlighter alpha lives in the stroke's `A` channel (WPF parity: the 140-alpha translucency is baked into the colour, NOT a separate opacity knob).
- Dashed shape strokes render solid here — a Phase-B concern.

## Constraints

- Pure projection: no state, no events. Stroke→Path identity lives in `InkSurface._strokeVisuals` (reference-keyed dictionary).
- Do NOT add selection adorners/shape previews here — Phase B adds separate overlays.
