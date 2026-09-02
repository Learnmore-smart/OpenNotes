# OpenNotes.Tests/PdfServiceAnnotationSavingTests.cs
> Last updated: 2026-08-23（Wave 2 load/save/reopen and disposal integration coverage）| Protection: STANDARD

## Purpose

Exercise the production PdfService strip/rebuild path against isolated temporary PDFs. The tests preserve annotation ownership, DIP/PDF coordinate conversion, atomic replacement, stream ownership and readability while covering Hidden Ink compatibility and the save/dispose lifecycle.

## Wave 2 revision coverage

- `LoadPdfAsync` → repeated `SaveAnnotationsToPdfAsync`/dispose → fresh `LoadPdfAsync` cycles verify `ExtractedAnnotations`, legacy explicit white Hidden Ink round-trip, and that the owned backing stream is released before the PDF is deleted.
- A production PDF with a hidden `/Ink` annotation that omits `/C` resolves to the neutral gray `#C7CDD4` default and the default reveal duration.
- Existing tests continue to verify repeated annotation writes, owned/foreign annotation handling, printable appearance streams, CJK text, DIP geometry, and atomic temporary-file replacement.
- Disposal-race and path-coordinator tests live in `PdfSaveCoordinatorTests`; they use temporary roots only and never read/write a real user data directory.

## Open Threads / Resume Context

- **Edge compatibility regression (2026-09-02, GREEN):** saving a normal PDF whose CropBox is absent does not materialize `/CropBox [0 0 0 0]`; saving an input already containing that malformed box removes it; and a valid explicit CropBox is preserved. The two defect regressions fail with the mutating `PdfPage.CropBox` geometry read and pass with the guarded raw-element lookup plus pre-save sanitizer. This class passes 20/20, page-editing passes 18/18, and the complete suite passes 381/381 with normal Windows permissions.
- v5.2.6 adds real PDF save/reload coverage for `FitToCurve=false` plus a legacy owned-ink rectangle recovery case. Both are GREEN and guard against straight-edged shapes being re-rendered as curves after any document reload.
- **Status:** ready_for_next — focused/expanded/full automated tests are green; real desktop save/reopen smoke remains blocked by foreground ownership in this environment.
- Keep helper-generated PDFs aligned with production missing-`/C` behavior; do not migrate explicit legacy white values.

## Important Notes / NEVER Change

- Preserve strip-and-rebuild loading, 96/72 DIP conversion and Y inversion.
- Preserve owned-annotation deletion only, foreign annotation retention, backing-stream disposal, and `File.Move(tempPath, filePath, true)` atomic replacement.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-30 | Added GREEN PDF `FitToCurve` metadata round-trip and legacy rectangle recovery regressions. | Codex |
| 2026-08-23 | Added Wave 2 PDF save/readability, Hidden Ink legacy/default, stream ownership and disposal-race integration contracts. | Codex |
