# OpenNotes.Core/Pdf/PdfService.cs

> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

UI-free home of the PDF document service (namespace `Caelum.Pdf`) — moved
verbatim-in-spirit from `Services/PdfService.cs` (Task 3 split). Owns
load/strip-and-redraw of owned annotations, page edits, outline, text info,
BGRA rendering through `IPdfRasterizer`, and atomic saves. The WPF
`Caelum.Services.PdfService` now derives from this class and only adapts
render/text/outline types.

## What It Does

- Same behavior as the pre-split service — see `.ai/Services/PdfService.md`
  for the full mechanism notes (strip-and-rebuild ownership, DIP↔PDF scale
  96/72 + Y flip on load / 72/96 + Y flip on save, `wna_hidden_` ink prefix,
  CJK FreeText appearance + `/WNAutoWidth`/`/WNAutoHeight`, `/WNARotation`,
  `PdfPageDisplayGeometry`, Edge CropBox repair, `PdfSaveCoordinator` +
  `PdfAtomicFile`, `_lifetimeGate` → `_documentLock` ordering, retryable
  disposal). None of that changed in the move.
- `_pdfDocument` (`PdfiumViewer.PdfDocument`) became `_rasterizer`
  (`IPdfRasterizer`) + `_pdfBackingStream` (the stripped stream it owns).
  `PdfiumPdfDocument.Load` calls became `IPdfRasterizerFactory.LoadFromStream`
  / `LoadFromFile`; default factory is `PdfiumRasterizerFactory.Shared`
  (internal ctor takes a factory for test seams).
- Rendering: `RenderPageAsync`/`RenderPagePngBytesAsync`/
  `RenderPageBitmapSourceAsync` moved to the WPF facade; Core exposes
  `RenderPageBgraAsync(pageIndex, dpiScale, ct)` → `PdfPageBitmap` with
  `DpiX/DpiY = PdfRenderPolicy.CalculateRenderDpi(dpiScale)` — identical
  pixel dimensions and DPI metadata as the old paths.
- Text/outline view models (`PdfTextCharacterInfo`, `PdfPageTextInfo`,
  `PdfOutlineEntry`) keep their shapes but `System.Windows.Rect` became Core
  `RectD`; the WPF facade re-exposes `Rect` twins via `new` members.
- Geometry: `System.Windows.Point/Rect` uses became
  `Caelum.InkGeometry.PointD/RectD`; `AnnotationTransform` rotation
  persistence now calls `Models.AnnotationRotation.NormalizeDegrees`.
- No `using System.Windows`, no `using System.Drawing` — grep-clean Core.

## Dependencies

- `OpenNotes.Core` internals: `Caelum.InkGeometry` (`PointD`/`RectD`),
  `Caelum.Models` (annotation models + `AnnotationRotation`),
  `Caelum.Services` (`PdfRenderPolicy`, `PdfSaveCoordinator`,
  `PdfAtomicFile`, `DocumentReleaseState`), `Caelum.Pdf` (`IPdfRasterizer`).
- External: `PdfSharpCore` only. No pdfium managed wrapper — native calls go
  through `IPdfRasterizer`.

## Important Notes / NEVER Change

- All NEVER-change invariants from `.ai/Services/PdfService.md` apply here
  unchanged (strip/rebuild ownership, coordinate transforms, atomic
  replacement, gate ordering, Unicode `/Contents`, indirect-object
  registration, temp+move atomicity).
- **NEVER** reintroduce WPF/System.Drawing types into this file or its
  public API — it is the shared WinUI surface.
- Keep the `IPdfRasterizer` indirection: tests and the WPF facade depend on
  the factory seam; `InternalsVisibleTo` covers `OpenNotes`,
  `OpenNotes.Tests`, `OpenNotes.WinUI`.

## Change History

- 2026-09-22: Created by the Task 3 split (full ~3,600-line service moved to
  Core). Public API preserved for WPF consumers through the facade.
