# tools/Test-OpenNotesThirdPartyViewerSmoke.ps1
> Last updated: 2026-08-22 | Protection: STANDARD

## Purpose

Validate a PDF produced by OpenNotes with independent PDF tooling: Poppler metadata/rendering and the installed Microsoft Edge headless PDF viewer path. This runner never edits the input PDF.

## Constraints

- Require an explicit `-PdfPath`; the caller is responsible for choosing an OpenNotes-saved artifact.
- Write only to an exact temporary output directory under the system temp path.
- Use bundled Poppler (`pdfinfo.exe`/`pdftoppm.exe`) and the installed Edge executable when available.
- Do not claim stylus, manual Acrobat UI, or visual equivalence from process startup alone. The output distinguishes Poppler render evidence from Edge headless evidence.
- Never modify or delete the input PDF.
- Track the Edge PID set before launch and, during cleanup, terminate only new Edge processes created by this runner; never use a broad process-name kill.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** close the independent viewer part of Tasks 48.3 and 49.7 for a freshly saved Hidden Ink PDF.
- **Expected evidence:** `POPPLER_RENDER_RESULT=PASS`, `EDGE_HEADLESS_RESULT=PASS`, and `THIRD_PARTY_VIEWER_SMOKE_RESULT=PASS`.
- **Blockers / notes:** if the installed viewer or bundled renderer is absent, fail with an explicit availability error rather than treating source inspection as viewer validation.
- The first host run showed that Windows PowerShell exposes no initialized `ProcessStartInfo.ArgumentList`; the runner now uses explicit Windows command-line quoting through `ProcessStartInfo.Arguments` so paths with spaces remain one argument.

## Important Notes / NEVER Change

- Preserve the input artifact and report its SHA-256 hash so the caller can compare it before/after.
- Require a non-empty rendered page and a valid page count. A successful process exit with no output is not a pass.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-22 | Created the independent Poppler/Edge PDF viewer regression handoff. | Codex |
| 2026-08-22 | Replaced the unavailable `ArgumentList` path with Windows-safe quoted arguments after the first host run stopped before launching Poppler. | Codex |
| 2026-08-22 | Added exact Edge PID-set cleanup for headless multi-process children without touching pre-existing Edge processes. | Codex |
