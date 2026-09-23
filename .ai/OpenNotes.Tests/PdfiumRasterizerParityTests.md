# OpenNotes.Tests/PdfiumRasterizerParityTests.cs

> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

Task 3 parity gate: `Caelum.Pdf.PdfiumRasterizer` (pure P/Invoke) must produce
byte-identical pixels and identical text/geometry answers as the legacy
`PdfiumViewer.PdfDocument` path it replaces.

## What It Does

- `BuildFixturePdfBytes` — 3-page PdfSharpCore fixture: vector shapes via
  `XGraphics` plus text written as a **raw standard-14 `/Helv` content
  stream** (`AddStandardFontText`) — deliberately no `XFont`, because once any
  `PdfService` static initializer installs the app's CJK-only font resolver,
  arbitrary family names can no longer resolve (test-ordering hazard).
- `RenderWithPdfiumViewer` — renders through the old path
  (`PdfDocument.Render(..., PdfRenderFlags.Annotations)` → GDI+ bitmap →
  `LockBits Format32bppArgb` → row copy) as the reference byte source.
- `RenderPageBgra_IsByteIdenticalToPdfiumViewerRender` — 3 pages × 96/144/192
  DPI: exact width/height/stride and `Is.EqualTo(expected)` byte equality on
  the BGRA buffers (SHA-256 in the failure message for triage).
- `DocumentMetadata_MatchesPdfiumViewer` — `PageCount` + `PageSizes` floats.
- `PageTextAndBounds_MatchPdfiumViewer` — `GetPdfText` vs `GetPageText`,
  `GetTextBounds` merged rects (±0.001f), and `RectangleFromPdf` device
  coords (exact ints).
- `Dispose_IsIdempotent` — double dispose + post-dispose render throws
  `ObjectDisposedException`.

## Dependencies

- References both `PdfiumViewer` and `OpenNotes.Core` — the test project is
  the only place the two backends coexist (they share one pdfium monitor via
  the interned lock string, so concurrent entry is impossible anyway).

## Important Notes / NEVER Change

- Keep the fixture resolver-free — do not reintroduce `XFont`/`DrawString`.
- Byte equality is the acceptance bar; tolerances apply only to float text
  bounds (0.001) — pixels must match exactly.

## Change History

- 2026-09-22: Created with the rasterizer; green 4/4 before and after wiring
  the real `FPDF_FORMFILLINFO` callbacks.
