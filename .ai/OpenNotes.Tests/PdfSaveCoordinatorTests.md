# OpenNotes.Tests/PdfSaveCoordinatorTests.cs
> Last updated: 2026-08-23（Wave 2 final review GREEN: coordinator/PDF/autosave/structural contracts）| Protection: STANDARD

## Purpose

Prove same-path PDF save delegates never overlap, independent paths can overlap, and gate entries are released after both success and exceptions. The source contract also guards EditorPage's shared autosave/manual save gate.

## Open Threads / Resume Context

- **Status:** ready_for_next — 21 executable coordinator/PDF tests pass in the focused scope.
- RED was observed first: the coordinator contract was missing, concurrent PDF writes failed during `File.Move`, and structural writes bypassed the path gate/disposal state. The blank-document lease regression also failed deterministically at `ActiveLeaseCount=1` when its production wrapper was temporarily removed, then passed after the wrapper was restored. GREEN coverage now includes path aliases, cancellation, exception cleanup, concurrent PdfService save/reopen, active/waiting DisposeAsync races, structural writes queued behind a held path gate, public load and blank-document creation queued behind a held path lease, queued structural writes failing before post-dispose reload, and a fail-once stream owner proving disposal retains failed ownership for retry. Test paths are temporary/isolated and never use the user's data directory.

## Important Notes / NEVER Change

- Same-path assertions must include case/relative normalization.
- Failure tests must prove a later save can acquire the same path.
- PDF behavior tests must retain strip/rebuild, atomic replacement, ownership and stream disposal assertions.
- Queueing tests use entered/release `TaskCompletionSource` barriers and bounded `WaitAsync`; no fixed `Task.Delay`, `Task.Yield`, or scheduler-sensitive `IsCompleted` assertion remains in this fixture.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Added Wave 2 RED coordinator and autosave source contracts. | Codex |
| 2026-08-23 | Verified same-path serialization, independent-path overlap, failure recovery, concurrent PdfService readability and source contracts. | Codex |
| 2026-08-23 | Added relative/`.`/`..`/whitespace/cancel tests plus PdfService load/save/disposal/reopen coverage; PdfService lifetime admission prevents post-dispose reload/create races. | Codex |
| 2026-08-23 | Added structural write gate/dispose, public-load and blank-document path-lease tests, failed-stream retry coverage, and refactored all PdfService write/reload entrances through the common path/lifetime/document helper; 15/15 coordinator tests pass. | Codex |
| 2026-08-23 | Added deterministic multi-path sorted admission, source-read import barriers, crossed import no-deadlock, and atomic replacement failure/temp cleanup tests; Save-As source+target admission is covered by production code. | Codex |
| 2026-08-23 | Replaced scheduler-sensitive waits with deterministic barriers, added HomePage PDF export atomic-target contract, and retained full PDFService/stream/structural coverage; 21/21 pass. | Codex |
| 2026-08-23 | Planned replacement of scheduler-sensitive `Task.Delay`/`Task.Yield`/`IsCompleted` assertions with entered/release task-completion barriers and bounded waits. HomePage export audit confirms blank PDF creation already routes through `PdfService.CreateBlankPdfAsync` and its atomic target-write contract. | Codex |
