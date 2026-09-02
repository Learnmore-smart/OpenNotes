# Services/PdfAtomicFile.cs
> Last updated: 2026-09-02（Edge page-box compatibility fix GREEN）| Protection: CRITICAL

## Purpose

Shared same-directory PDF replacement primitives. Complete temp output is flushed before `File.Move(temp,target,true)`; failures delete only the temp artifact and preserve the existing target.

## Contract

- `CreateTempPath(target)` keeps the temp file beside the target, including relative paths.
- `SaveDocument(document,temp)` writes through an exclusive `FileStream` and calls `Flush(true)`.
- `CopyFile(source,target)` reads the source into a same-directory temp stream, flushes it, then replaces the target atomically.
- `Replace(temp,target,move?)` always attempts temp cleanup in `finally`; the optional delegate is a deterministic failure-injection seam for tests.
- Before serializing, `SaveDocument` removes only explicit zero-area `/CropBox` rectangles. A missing/invalid CropBox must fall back to MediaBox; valid explicit CropBoxes and every other page box remain unchanged. Direct PdfSharpCore save paths must invoke the same sanitizer.

## Consumers

PdfService blank creation, annotation save, Insert/Delete/Reorder/Duplicate/Rotate/PDF/image import; EditorPage snapshot bytes, print copy and draft Save-As. Save-As admits both source and destination paths through the sorted `PdfSaveCoordinator` multi-path lease.

## Evidence

`PdfSaveCoordinatorTests.AtomicReplacementFailureLeavesOriginalAndCleansTemp` passes; no structural path retains direct target overwrite. Focused coordinator/PDF tests pass with isolated temporary paths only.

## Open Threads / Resume Context

- **Status:** ready_for_next — RED/GREEN coverage proves ordinary saves do not materialize a zero CropBox, malformed zero-area boxes are removed, and valid explicit CropBoxes are preserved. Annotation/page-editing tests pass 20/20 and 18/18, the complete suite passes 381/381 with normal Windows permissions, Release build has 0 errors, and a generated ink PDF visibly renders in desktop Microsoft Edge 152.
