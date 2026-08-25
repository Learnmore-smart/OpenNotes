# PdfServicePageEditingTests

## Purpose

Regression coverage for blank-page insertion/deletion and the V5 structural page operations: reorder, duplicate, rotate, and vector template creation.

## Constraints

- Tests use isolated temporary PDFs and never touch user documents.
- Assertions inspect page count, dimensions, rotation metadata, and non-empty content streams.

## Open Threads

- Checklist and TwoColumn are included in the vector-template creation contract; UI-only visual export remains manual.

## Completion Status

- Reorder, duplicate, rotate, inclusive PDF insertion and Dotted/Music/Cornell/Checklist/TwoColumn vector content are covered.
