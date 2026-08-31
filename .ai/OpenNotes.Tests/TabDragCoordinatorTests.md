# TabDragCoordinatorTests

> Last updated: 2026-08-30 | Protection: STANDARD

## Purpose

Focused RED/GREEN coverage for process-wide tab drag payload ownership, destination acknowledgement, cancellation, and source contracts.

## Open Threads / Resume Context

- **Status:** ready_for_next
- **Intent:** Prove drag sessions carry the live tab and distinguish accepted docks from outside/cancelled drops before production wiring.
- **Next steps:** Add desktop UI automation coverage if a foreground-capable WPF session becomes available.

## Important Notes / NEVER Change

- Tests must not instantiate a second app process or release a real editor document.
- Every test must end its drag session so static process-wide state cannot leak across fixtures.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-30 | Added mirror for detachable-tab coordinator tests. | Codex |
| 2026-08-30 | Added six focused payload, cancellation, source-contract, title, and taskbar tests. | Codex |
