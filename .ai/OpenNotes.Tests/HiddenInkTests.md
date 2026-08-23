# OpenNotes.Tests/HiddenInkTests.cs
> Last updated: 2026-08-20 | Protection: STANDARD

## Purpose

Verify Hidden Ink model persistence, reveal timing, geometry and ownership rules without requiring pointer hardware.

## Open Threads / Resume Context

- **Status:** ready_for_next
- Model/PDF coverage is complete; real pointer, timer, eraser, save/reopen UI and third-party viewer checks remain manual.

## Important Notes / NEVER Change

- Hidden Ink remains a separate collection and must not be treated as ordinary strokes.
- Temporary reveal state is never serialized.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Documented Hidden Ink regression tests. | Codex |
