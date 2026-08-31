# TabDragCoordinator

> Last updated: 2026-08-30 | Protection: STANDARD

## Purpose

Coordinates process-wide in-process tab drag sessions so any registered `MainWindow` can dock a live tab or the source can detach it into a new taskbar window.

## What It Does

- Registers and unregisters open `MainWindow` instances.
- Publishes an in-process drag payload containing the source window, stable tab ID, and the live `AppTab` reference.
- Records whether a drop was accepted by a destination window; an uncancelled drop with no destination is eligible for outside detach.
- Keeps all state process-local; no second application process or serialized editor/document copy is created.

## Important Notes / NEVER Change

- The payload must carry the existing `AppTab`/`Frame` reference.
- A target must acknowledge a successful dock before the source removes its tab.
- The source is responsible for moving its `_frameEditors` entry exactly once alongside the frame event subscription.
- Escape/cancel must never detach a tab.

## Open Threads / Resume Context

- **Status:** ready_for_next
- **Intent:** Add cross-window docking and outside-detach coordination while preserving same-window reorder semantics.
- **Next steps:** Validate real foreground drag/drop smoke when a desktop session is available.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-30 | Added process-wide drag-session design and transfer invariants. | Codex |
| 2026-08-30 | Implemented process-local payload sessions, docking acknowledgement, cancellation, and outside-detach signaling. | Codex |
