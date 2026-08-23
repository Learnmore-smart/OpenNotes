# OpenNotes.Tests/PenOnlyInputTests.cs
> Last updated: 2026-08-21（Hidden Ink pen-only boundary） | Protection: STANDARD

## Purpose

Verify the input-mode boundary used by `PdfPageControl` for PenOnly filtering. Hidden Ink must retain mouse and pen input, while ordinary freehand and shape creation remain pen-only when that setting is enabled.

## Open Threads / Resume Context

- **Status:** ready_for_next
- The rule is covered without requiring pointer hardware; actual mouse, pen and touch capture remains a desktop/manual check.

## Important Notes / NEVER Change

- Do not route Hidden Ink through the PenOnly block: its reveal/cover workflow supports mouse and pen drawing.
- Keep eraser, selection, text and PDF-text-selection modes outside the ink-creation filter.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-21 | Added the regression contract for Hidden Ink and PenOnly mode boundaries. | Codex |
