# OpenNotes.Core/Services/PrintPageGeometry.cs
> Last updated: 2026-09-26 (G1 print pipeline port) | Protection: STANDARD

## Purpose

Pure page-fit / raster-planning math for the print pipelines (`Caelum.Services`,
UI-free so `OpenNotes.Tests` covers it headlessly). Ports the WPF
`CreatePrintDocument` fit (uniform aspect-preserving scale into the printable
area, centered) into printer device pixels, plus a DPI resolver that trades
resolution for a bounded per-job raster budget.

## What it does

- `PrintPageGeometry.ResolvePrintRenderDpi(printerDpi, totalPageAreaPoints)` —
  clamps to `[MinPrintDpi=96, MaxPrintDpi=600]` (non-positive → floor), then
  reduces DPI when `area·(dpi/72)²` would exceed `MaxPrintRasterPixels`
  (250 MP ≈ 1 GB of BGRA). WPF buffered every page at a fixed 220 DPI — the
  budget only binds on documents WPF already handled worse.
- `PrintPageGeometry.FitPageToPrintableArea(pageWpt, pageHpt, areaPxW, areaPxH)`
  → `PrintPageRect` — `min(areaW/pageW, areaH/pageH)` scale, rounded, clamped
  to the area (the WPF FixedPage relied on clipping a ±1 px rounding
  overflow), centered via `max(0, (area−size)/2)`. Degenerate page or area →
  empty rect (WPF produced a zero-size image).
- `PrintPageRect` — readonly int X/Y/Width/Height device-pixel rect.

## Constraints / NEVER Change

- No UI or GDI dependency — device pixels and PDF points only; the WinUI
  caller supplies printer caps, the WPF caller supplied 96-DIP equivalents.
- Keep the centered min-scale semantics identical to WPF `CreatePrintDocument`.

## Dependencies

- Consumed by `OpenNotes.WinUI/Services/Win32Print.cs` (destination rect per
  page) and `Pages/EditorPage.xaml.cs` `RenderPrintablePages` (raster DPI).

## Open Threads / Resume Context

- **Status:** complete. `PrintPageGeometryTests` pins clamps, budget shrink,
  portrait/landscape/upscale fits, degenerate guards.
