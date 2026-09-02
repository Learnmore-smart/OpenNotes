# OpenNotes.Tests/UpdateCheckServiceTests.cs

> Last updated: 2026-09-01 | Protection: STANDARD

## Purpose

Deterministically verify update-check requests, strict payload/URL validation, normalized version comparison, timeout, cancellation, and failure categories with an in-memory HTTP handler.

## Important Notes / NEVER Change

- Do not call live GitHub from automated tests.
- Assert behavior and outgoing request metadata rather than private implementation details.
- Cover both fail-closed response handling and caller-cancellation preservation.

## Open Threads / Resume Context

- **Status:** complete
- **Coverage:** 22 deterministic cases cover request metadata, version normalization/comparison, invalid payloads and tags, trusted URI boundaries, HTTP/transport/timeout failures, and caller cancellation.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-01 | Added the RED/GREEN service suite; focused run passes 22/22. | Codex |
| 2026-09-01 | Created before the update-check RED tests. | Codex |
