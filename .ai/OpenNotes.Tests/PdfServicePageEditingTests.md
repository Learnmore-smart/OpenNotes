# PdfServicePageEditingTests

## Purpose

Regression coverage for blank-page insertion/deletion and the V5 structural page operations: reorder, duplicate, rotate, and vector template creation.

## Constraints

- Tests use isolated temporary PDFs and never touch user documents.
- Assertions inspect page count, dimensions, rotation metadata, and non-empty content streams.

## Open Threads

- UI-only drag/drop and visual export checks remain manual verification items.

## Completion Status

- Reorder, duplicate, rotate, inclusive PDF insertion and Dotted/Music/Cornell vector content are covered; the full suite reached 29/29 passing tests.
