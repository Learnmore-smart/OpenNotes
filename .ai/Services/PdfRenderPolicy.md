# Services/PdfRenderPolicy.cs

> Last updated: 2026-08-21 | Protection: STANDARD

## Purpose

Pure, WPF-independent policy for performance-mode normalization, retained PDF page windows, render DPI/scale limits, and bitmap-byte estimates.

## Public API

- `NormalizeMode` and `GetProfile` map settings to BatterySaver, Balanced, or BestQuality limits.
- `GetRetainedPageIndices` returns a bounded visible-page working set.
- `NormalizeRequestedScale`, `CalculateRenderDpi`, `CalculateRenderScale`, and `EstimateBitmapBytes` keep thumbnails accurate and main-page renders inside mode budgets.

## Open Threads / Resume Context

- **Status:** complete and covered by deterministic unit tests.
- Approved profiles: BatterySaver = visible only/no prefetch/1.35x/32 MiB; Balanced = ±1/prefetch/2x/64 MiB; BestQuality = ±1/prefetch/3x/128 MiB. The type remains free of WPF/Pdfium dependencies.

## Important Notes / NEVER Change

- Balanced is the backward-compatible default.
- Display policy must not affect saved PDF or annotation fidelity.
