# Services/InteractionCancellation.cs
> Last updated: 2026-08-24 (Wave6 dual-review GREEN closure) | Protection: STANDARD

## Purpose

Shared cancellation boundary for pointer/stylus interactions. It lets editor/page owners
rollback an in-flight gesture on capture loss, Escape, deactivation, navigation, unload or
document replacement without emitting a phantom undo/dirty operation.

## Open Threads / Resume Context

- **Status:** complete for the approved Wave6 automated scope
- **Intent:** use one idempotent cancellation boundary for page gestures and editor-owned
  text drag/resize transactions. Capture loss, Escape, deactivation, navigation, reload and
  `SetHostActive(false)` must restore the opening snapshot before capture is released; only
  normal pointer/stylus up may publish one undo/dirty completion.
- **Result:** `EditorPage` and `PdfPageControl` both implement the shared
  `IInteractionCancellation` boundary. Text drag/resize, Sticky drag and page
  selection/PDF-text capture rollback on LostCapture, Escape/CloseTransientUi,
  deactivation, navigation/reload, unload and `SetHostActive(false)` before capture
  release; normal pointer/stylus-up is the sole path that emits one undo/dirty action.
  Focused deterministic STA/source/PDF contracts pass `20/20`; the full suite passes
  `241/241`. External foreground/Alt-Tab/device evidence remains unclaimed.

## Important Notes / NEVER Change

- Cancellation restores the gesture start snapshot and never pushes undo or marks dirty.
- Normal pointer/stylus release remains the only completion path for a user gesture.
- The helper must not close ordinary text editing or save dialogs.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-24 | Planned shared Wave6 interaction cancellation contract after dual review. | Codex |
| 2026-08-24 | Resumed Wave6 dual-review closure: shared page/editor cancellation and lifecycle barriers are the active scope; no Wave7+ work. | Codex |
| 2026-08-24 | GREEN: shared EditorPage/PdfPageControl cancellation restores snapshots without phantom undo/dirty; focused 20/20 and full 241/241 pass. | Codex |
