# OpenNotes.Core/Pdf/PdfTextSelectionGeometry.cs
> Last updated: 2026-09-24 | Protection: STANDARD

## Purpose

UI-free port of the WPF editor's PDF-text-selection math — the pieces that
used to live inside WPF `EditorPage.FindNearestTextOffset` /
`BuildPdfTextSelectionRects` / `BuildTextMarkupAnnotation` and
`PdfPageControl.NormalizeAreaHighlightRect`, plus the `AddImage` 40%-of-page
fit rule. Everything runs on page-DIP coordinates over
`PdfService.PdfPageTextInfo`/`RectD`/`PointD`, so the WinUI page control and
headless tests drive the identical logic (`Caelum.Pdf`, Task 8 Phase B).

## API surface

- `RectContainsWithPadding(rect, point, padding)` — padded containment; a
  press within `ContainmentPaddingDips` (3 DIP) of a glyph box snaps to it.
- `DistanceToRect(point, rect)` — edge distance, 0 inside.
- `FindNearestTextOffset(textInfo, point, maxDistance)` — padded hit wins
  immediately, else the closest `UnionBounds` under `maxDistance`;
  `double.PositiveInfinity` = always answer (the drag path follows the
  pointer anywhere on the page). Boundless characters never match;
  null/empty → -1.
- `ShouldMergeSelectionRects(current, next)` — same visual line (vertical
  overlap ≥ 35% of the shorter height) AND horizontal gap ≤ 8 DIP.
- `BuildSelectionRects(textInfo, startOffset, endOffset)` — merged selection
  quads over the clamped, order-independent offset range; every bound a
  character carries contributes (pdfium can split one glyph across rects).
- `BuildTextMarkupAnnotation(absoluteRects, kind, r, g, b)` — union bounds
  become the `TextMarkupAnnotation.X/Y` origin; each rect relativizes to it;
  empty input returns a rect-less model at (0,0). `Kind` serializes as the
  enum name (`Underline`/`StrikeOut`/`Squiggly`).
- `NormalizeAreaHighlightRect(anchor, current)` — axis-aligned drag box.
- `ComputeImagePlacementSize(pageW, pageH, pixelW, pixelH, explicitW?,
  explicitH?)` — explicit dims win verbatim; otherwise scale inside 40% of
  the page keeping aspect; degenerate input → (1,1).

## Constraints / NEVER Change

- Stays UI-free — no `Microsoft.UI.Xaml`/`System.Windows` types; both the
  WinUI host and the headless tests must call the same code.
- The constants are WPF-parity numbers (3-DIP pad, 35%/8-DIP merge) — do not
  retune without matching the WPF reference.

## Open Threads / Resume Context

- **Status:** GREEN — exercised by `OpenNotes.Tests/Task8PhaseBTests.cs`.
