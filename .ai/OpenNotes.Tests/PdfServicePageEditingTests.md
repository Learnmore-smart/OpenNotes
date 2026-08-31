# PdfServicePageEditingTests

## Purpose

Regression coverage for blank-page insertion/deletion and the V5 structural page operations: reorder, duplicate, rotate, and vector template creation.

## Constraints

- Tests use isolated temporary PDFs and never touch user documents.
- Assertions inspect page count, dimensions, rotation metadata, and non-empty content streams.

## Open Threads

- Checklist and TwoColumn are included in the vector-template creation contract; UI-only visual export remains manual.

## Rotation regression (2026-08-31) — GREEN

- `RotatePageAsync_SwapsDisplayAspectAndKeepsOwnedDrawingAttachedToPage` covers a real PDF save/rotate/load cycle, verifies the 90-degree landscape dimensions, ordinary/grouped-shape ink and Hidden Ink coordinates, then saves and reloads again to catch double transforms or drift.
- `RotatePageAsync_MapsDrawingThroughEveryQuarterTurnWithoutDrift` covers 90/180/270 display dimensions and coordinate mappings, including a second save/reload for every rotation.
- The pre-fix RED evidence was negative Y coordinates after 90 degrees because PdfSharpCore's rotation-aware `Height` was incorrectly mixed with raw `/InkList` coordinates.

## Completion Status

- Reorder covers backward, forward, and final-slot moves using distinct page dimensions as durable page identity; duplicate, rotate, inclusive PDF insertion and Dotted/Music/Cornell/Checklist/TwoColumn vector content are also covered.
