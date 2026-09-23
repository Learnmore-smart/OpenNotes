# OpenNotes.Core/Pdf/IPdfRasterizer.cs

> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

UI-agnostic contract for a loaded, renderable PDF document — the only
raster/text surface the Core `Caelum.Pdf.PdfService` needs. Member semantics
deliberately mirror `PdfiumViewer.PdfDocument` so the split preserves behavior.

## What It Does

- `IPdfRasterizer` (implements `IDisposable`):
  - `PageCount` / `PageSizes` — page sizes in PDF points (float), cached at
    load like `PdfDocument.PageSizes`.
  - `RenderPageBgra(pageIndex, pixelWidth, pixelHeight, renderAnnotations = true)`
    → `PdfPageBitmap` (top-down B,G,R,A buffer, `Stride = width*4`,
    `DpiX/DpiY` stamped by the caller side so adapters can preserve bitmap
    metadata). `renderAnnotations` maps to `PdfRenderFlags.Annotations`
    (FFLDraw) vs `(PdfRenderFlags)0` (base pass only).
  - `GetPageText(pageIndex)` — full page text (mirrors `GetPdfText(int)`).
  - `GetTextBounds(pageIndex, offset, length)` → merged `PdfRectF` list in
    unrotated PDF page coordinates, including the horizontal-run merge
    heuristic.
  - `RectangleFromPdf(pageIndex, PdfRectF)` → `PdfRectI` in top-left device
    space at natural point size.
- `IPdfRasterizerFactory` — `LoadFromStream(Stream)` (rasterizer owns the
  stream for the document lifetime, same contract as PdfiumViewer) and
  `LoadFromFile(string)`.
- Value types: `PdfPageSize` (float w/h), `PdfRectF` (float PDF-space rect,
  `IsValid` matches `PdfRectangle.IsValid`), `PdfRectI` (int device rect),
  `PdfPageBitmap` (Width/Height/Stride/Bgra/DpiX/DpiY).

## Dependencies

- `System.IO` only — no `System.Drawing`, no WPF types on the surface.
- Implemented by `PdfiumRasterizer` in the same folder; consumed by
  `Caelum.Pdf.PdfService` and injected via `IPdfRasterizerFactory` (test seam:
  WPF facade exposes `internal PdfService(IPdfRasterizerFactory)`).

## Important Notes / NEVER Change

- **NEVER** add `System.Drawing` or WPF types to this file — it is the Core
  public boundary for the future WinUI host.
- Keep semantics aligned with `PdfiumViewer.PdfDocument` — callers (facade,
  tests, parity harness) depend on identical answers.

## Change History

- 2026-09-22: Created for Task 3 (PDF rasterization abstraction) — defines
  the BGRA-oriented surface chosen over PNG bytes so WPF can keep its fast
  `BitmapSource` path and byte-identical GDI+ PNG encoding.
