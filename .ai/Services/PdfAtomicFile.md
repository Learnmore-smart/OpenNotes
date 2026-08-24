# Services/PdfAtomicFile.cs
> Last updated: 2026-08-23（Wave 2 atomic-write contract GREEN）| Protection: CRITICAL

## Purpose

Shared same-directory PDF replacement primitives. Complete temp output is flushed before `File.Move(temp,target,true)`; failures delete only the temp artifact and preserve the existing target.

## Contract

- `CreateTempPath(target)` keeps the temp file beside the target, including relative paths.
- `SaveDocument(document,temp)` writes through an exclusive `FileStream` and calls `Flush(true)`.
- `CopyFile(source,target)` reads the source into a same-directory temp stream, flushes it, then replaces the target atomically.
- `Replace(temp,target,move?)` always attempts temp cleanup in `finally`; the optional delegate is a deterministic failure-injection seam for tests.

## Consumers

PdfService blank creation, annotation save, Insert/Delete/Reorder/Duplicate/Rotate/PDF/image import; EditorPage snapshot bytes, print copy and draft Save-As. Save-As admits both source and destination paths through the sorted `PdfSaveCoordinator` multi-path lease.

## Evidence

`PdfSaveCoordinatorTests.AtomicReplacementFailureLeavesOriginalAndCleansTemp` passes; no structural path retains direct target overwrite. Focused coordinator/PDF tests pass with isolated temporary paths only.
