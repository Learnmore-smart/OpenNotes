# tools/Test-OpenNotesHiddenInkSmoke.ps1
> Last updated: 2026-08-23 (Wave 3 production ID migration) | Protection: STANDARD

## Purpose

Run an isolated real-desktop regression for the Hidden Ink path: physical mouse drawing, opaque mask visibility, reveal and timed restoration, eraser removal, PDF persistence, process restart/reopen and fresh mask visibility after loading.

## Constraints

- Use a generated one-page PDF and isolated `LOCALAPPDATA`, `APPDATA` and `OPENNOTES_DATA_ROOT` values.
- Use real screen pointer input for toolbar, drawing, reveal and eraser gestures. UI Automation is limited to stable control discovery, state/value reads and save-button bounds.
- Require confirmed OpenNotes foreground ownership before every physical gesture. A locked desktop or another foreground owner is an environment block, not a product pass.
- Close only the child process started by this script and remove only its exact temporary directory.
- Do not inspect or modify Codex state, authentication, logs, user documents or the normal Caelum data root.
- Do not claim stylus coverage from mouse input; this runner reports mouse coverage only.
- `-KeepArtifacts` is an explicit handoff mode for the independent viewer runner; it leaves only the exact generated temporary directory and reports its path.
- Dot-sources `OpenNotesEditorAutomationIds.ps1`; toolbar, `PdfScrollViewer`, runtime page and save/undo controls use production aliases while `HiddenInkToolButton` remains compatible.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** close the desktop portion of Task 49.7 that is observable with a real mouse while retaining a strict evidence boundary for stylus and third-party viewers.
- **Expected evidence:** `HIDDEN_INK_DRAW_COMPLETED`, `HIDDEN_INK_REVEAL_COMPLETED`, `HIDDEN_INK_ERASE_COMPLETED`, `HIDDEN_INK_REOPEN_COMPLETED` and `HIDDEN_INK_SMOKE_RESULT=PASS`.
- **Blockers / notes:** the current desktop may be owned by `LockApp`; the runner must fail with `REAL_SCREEN_INPUT_UNAVAILABLE` rather than downgrade to a window-message or UIA path.
- The first live run reached the real isolated home window but could not identify the file tile because the initial lookup required the exact PDF filename in the UIA name. The lookup now follows the existing editor/pointer smoke pattern: prefer any named PDF tile, then fall back to the large file-card shape.

## Important Notes / NEVER Change

- Preserve the strip-and-rebuild PDF path and `wna_hidden_` ownership marker.
- Require both PDF marker/count evidence and screen phase evidence. A changed PDF alone does not prove that the live mask was visible, revealed or restored.
- After restart, require the mask to be visible before attempting a second reveal; this protects the session-only reveal contract.
- **2026-08-23 run:** the script parsed successfully and stopped at `REAL_SCREEN_INPUT_UNAVAILABLE` during `editor-startup` (`foregroundHwnd=0`, `foregroundPid=0`); it reported `HIDDEN_INK_SMOKE_RESULT=FAIL` with `ISOLATED_ENV_CLEANED=True`. No Hidden Ink pointer PASS is claimed for this host run.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-22 | Created the isolated Hidden Ink mouse/persistence regression handoff. | Codex |
| 2026-08-22 | Implemented strict foreground-verified draw/reveal/timer/erase/undo/save/reopen checks with PDF marker assertions. | Codex |
| 2026-08-22 | Matched the working smoke runners' resilient PDF-card discovery after the first live run exposed a UIA display-name mismatch. | Codex |
| 2026-08-22 | Kept the save helper's diagnostic output single-line after the passing run exposed an unused hash return value. | Codex |
| 2026-08-22 | Added opt-in artifact retention so the freshly saved PDF can be handed to the independent Poppler/Edge viewer runner. | Codex |
