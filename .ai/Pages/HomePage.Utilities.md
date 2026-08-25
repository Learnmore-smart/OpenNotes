# Pages/HomePage.Utilities.cs

> V5.1.2 selection and export notifications use the shared Lucide toast renderer.
> Last updated: 2026-08-23 | Protection: STANDARD

## Purpose

Utility and context-menu workflows for the HomePage, including user PDF export.

## Wave 2 follow-up plan

- PDF export targets are real PDF files. Route the source-to-target copy through `PdfAtomicFile` so a failed export cannot truncate the existing destination or leave a half-written file.
- Keep the existing SaveFileDialog, localization, success toast and error dialog behavior unchanged.

## Evidence

- `HomePagePdfExportUsesAtomicTargetWriteContract` verifies the production export path calls `PdfAtomicFile.CopyFile`; the helper's same-directory temp/flush/atomic-move and failure cleanup are covered by `PdfSaveCoordinatorTests.AtomicReplacementFailureLeavesOriginalAndCleansTemp`.

## Constraints

- Do not write a PDF target directly with `File.Copy`.
- Preserve the HomePage single-window navigation and user-selected output path.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Created mirror before Wave 2 export contract change. | Codex |
