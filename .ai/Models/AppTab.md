# AppTab

> Last updated: 2026-08-30 | Protection: STANDARD

## Purpose

Represents one tab and owns its live WPF `Frame`, title/icon metadata, and optional document path.

## Open Threads / Resume Context

- **Status:** ready_for_next
- **Intent:** Allow one `AppTab`/`Frame` to move between `MainWindow` instances without rebuilding the editor, while retaining same-file tabs as separate live tabs.
- **Next steps:** None for the model; future window changes must preserve the identity/event-map transfer contract.

## Agent Decisions / Thoughts

- **2026-08-30 Codex:** A tab transfer must move the existing `Frame` object, not create a new `EditorPage`; this preserves scroll, selection, undo, and in-flight document state.

## Important Notes / NEVER Change

- `Id` is stable for the lifetime of the tab and is the fallback identity in drag payloads.
- `Frame` is the live navigation journal; callers must detach/attach its owner event handlers exactly once when moving windows.
- `IsHome` is derived from `FilePath`; do not infer it from the current frame content in the model.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-30 | Added transfer invariants for cross-window tab ownership. | Codex |
| 2026-08-30 | MainWindow now transfers the live tab/frame without changing AppTab identity. | Codex |
