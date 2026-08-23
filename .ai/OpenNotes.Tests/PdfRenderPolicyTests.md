# OpenNotes.Tests/PdfRenderPolicyTests.cs

> Last updated: 2026-08-21 | Protection: STANDARD

## Purpose

Unit tests for deterministic render-mode normalization, page retention, thumbnail DPI, and per-bitmap memory budgets.

## Open Threads / Resume Context

- **Status:** green (11 tests).
- Covers document boundaries, invalid modes/scales, standard page ceilings, oversized page byte caps, true thumbnail DPI, and the PdfService async-disposal contract without launching WPF or Pdfium.
