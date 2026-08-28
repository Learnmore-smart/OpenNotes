# tools/Test-OpenNotesCrossPageKeyboardSmoke.ps1
> 2026-08-28: physical text movement now starts from a safe point on the left border (away from resize handles), discovered through the TextAnnotationMoveBorder automation peer.
> Last updated: 2026-08-24 (Wave 4 review-follow-up verification) | Protection: STANDARD

## Purpose

Run a real, isolated desktop regression for the text-box keyboard and cross-page path: runtime page bounds, keyboard nudge, keyboard resize-handle routing, physical drag-handle transfer, cross-page Undo/Redo, PDF save, and reopen on the destination page.

## Constraints

- Use a generated two-page PDF and isolated `LOCALAPPDATA`, `APPDATA`, and `OPENNOTES_DATA_ROOT` values.
- Use only real screen pointer and OS keyboard input for the interaction phases; do not use `WM_MOUSE*`, direct model calls, or in-process event invocation.
- UI Automation is used only to discover stable controls, set/read text values, focus the keyboard target, and observe page/edit bounds.
- Require confirmed OpenNotes foreground ownership before every physical gesture/key sequence. A locked desktop or another foreground owner is an environment block, not a product pass.
- Close only the child process started by this script and remove only its exact temporary directory.
- Dot-sources `OpenNotesEditorAutomationIds.ps1`; toolbar, viewer, runtime page, drag-handle and bottom-right resize IDs use shared production aliases.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** close Task 43.4 coverage for real keyboard resize, text-box move and cross-page persistence.
- **Expected evidence:** `KEYBOARD_NUDGE_COMPLETED`, `KEYBOARD_RESIZE_COMPLETED`, `TEXT_CROSS_PAGE_COMPLETED`, `CROSS_PAGE_UNDO_REDO_COMPLETED`, `CROSS_PAGE_REOPEN_COMPLETED`, and `CROSS_PAGE_KEYBOARD_SMOKE_RESULT=PASS`.
- **Blockers / notes:** the current desktop may be owned by `LockApp`; the script must fail with `REAL_SCREEN_INPUT_UNAVAILABLE` rather than downgrade to a synthetic pointer path.
- **2026-08-23 run:** the isolated smoke stopped at `REAL_SCREEN_INPUT_UNAVAILABLE` during `open-library` (`foregroundHwnd=0`, `foregroundPid=0`) and reported `CROSS_PAGE_KEYBOARD_SMOKE_RESULT=FAIL` with `ISOLATED_ENV_CLEANED=True`; no cross-page pointer/keyboard PASS is claimed for this host run.
- **2026-08-24 run:** the isolated smoke again stopped at `REAL_SCREEN_INPUT_UNAVAILABLE` during `open-library` (`targetHwnd=12584604`, `foregroundHwnd=0`, `foregroundPid=0`) and reported `CROSS_PAGE_KEYBOARD_SMOKE_RESULT=FAIL` with `ISOLATED_ENV_CLEANED=True`; no cross-page pointer/keyboard PASS is claimed for this host run.

## Important Notes / NEVER Change

- Preserve the existing `SelectionCrossPageMoveAction` and text drag event path.
- Do not infer cross-page success from a changed rectangle alone: source-page absence, destination-page presence, undo/redo page reversal, and reopen destination are required.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-22 | Added the isolated real pointer/keyboard cross-page regression handoff. | Codex |
| 2026-08-22 | Implemented runtime page/drag-handle discovery, keyboard nudge/resize, physical cross-page transfer, Undo/Redo, save and destination-page reopen assertions. | Codex |
