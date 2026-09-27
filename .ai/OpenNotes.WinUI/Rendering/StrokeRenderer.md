# OpenNotes.WinUI/Rendering/StrokeRenderer.cs
> Last updated: 2026-09-26 | Protection: STANDARD

## Purpose

Converts `InkStrokeData` into WinUI `Path` fill geometry (`Caelum.Rendering`, Task 7 Phase A) — the visual half of the custom ink engine. The outline itself comes from `StrokeOutline.BuildFillOutline` (the same silhouette WPF's `System.Windows.Ink` renderer produces for the equivalent DrawingAttributes); the only decisions here are fill colour and figure assembly.

## API

- `CreateStrokePath(InkStrokeData)` → `Path` — `IsHitTestVisible = false` (hit-testing lives in `StrokeGeometry` math, not XAML), then delegates to `UpdateStrokePath` so the create path rides the same guarded rebuild. The path is NOT parented — the caller adds it to the surface's `Children`.
- `UpdateStrokePath(Path, InkStrokeData)` — rebuilds `path.Data` in place; the live-stroke fast path (one `Path` per in-flight pointer, updated per move event). Phase B: also re-resolves `Fill` so selection style changes (colour/size) refresh through the same `GeometryChanged` notification. **T14-A: wrapped in try/catch** — a throw inside PointerMoved is a fatal stowed 0xC000027B, so a fault logs via `LogFault` and keeps the last good geometry.
- `BuildGeometry(InkStrokeData)` → `PathGeometry` — normally a single closed `PathFigure` (`IsClosed=true`, `IsFilled=true`) holding one `PolyLineSegment` over the outline polygon; empty/degenerate strokes produce an empty geometry. **T14-A: `FillRule = FillRule.Nonzero`** — the EvenOdd default punched holes where a stroke's outline crosses itself.
- `ToColor(InkStrokeData)` → `Windows.UI.Color` straight from the stroke's RGBA channels.

## Behaviour notes

- Coordinates are already page DIP — no zoom/raster scaling here (the layer scales with the page control).
- Highlighter alpha lives in the stroke's `A` channel (WPF parity: the 140-alpha translucency is baked into the colour, NOT a separate opacity knob).
- **Dashed shape strokes render as real dashes** (fixed 2026-09-23): when `stroke.IsDashedShape`, `BuildGeometry` re-segments the spine via `StrokeOutline.BuildDashedFillOutlines` (Core `BuildDashedPolyline` under `GetShapeDashPattern` = the WPF commit-time `dash = max(size·4,10)` / `gap = max(size·2.5,6)` formula) and emits one `PathFigure` per dash. If the dash split yields nothing (a lone tap), the geometry falls back to the solid outline so the dot still draws.
- **T14-A non-finite guard:** `AddOutlineFigure` scans the outline once before touching XAML — any NaN/Infinity vertex skips the figure entirely. A non-finite coordinate in `PathFigure.StartPoint`/`PolyLineSegment.Points` throws `ArgumentException` (E_INVALIDARG) on the UI thread → the stowed 0xC000027B field crash seen in v6.0.3. Sources of non-finite values: a bad pointer packet propagating through `ResampleCatmullRom`, or `ex*ex` overflow in `AveragedNormal` on extreme deltas.
- **`LogFault`** (private): `Debug.WriteLine` always + `CrashLogger.Log` capped at 3 writes per process — the renderer runs per pointer-move, so an uncapped fault would spam the log directory.

## Constraints

- Pure projection except the `_faultsLogged` write-cap counter: no stroke state, no events. Stroke→Path identity lives in `InkSurface._strokeVisuals` (reference-keyed dictionary).
- Do NOT add selection adorners/shape previews here — Phase B adds separate overlays.
- Contract pinned by `OpenNotes.Tests/WinUiInkRenderingSourceTests.cs` (Nonzero fill rule, finite scan before figure construction, guarded `UpdateStrokePath`).

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-26 | T14-A: `FillRule.Nonzero` (self-crossing holes fix), non-finite vertex guard in `AddOutlineFigure`, try/catch + capped crash-logging in `UpdateStrokePath`, `CreateStrokePath` delegates to it. | Devin |
