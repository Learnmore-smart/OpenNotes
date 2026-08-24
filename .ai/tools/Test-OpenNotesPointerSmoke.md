# tools/Test-OpenNotesPointerSmoke.ps1
> Last updated: 2026-08-23 (Wave 3 production ID migration) | Protection: STANDARD

## Purpose

Run a real isolated WPF session and send Windows pointer input through the actual editor surface. The smoke covers pen drawing, whole-stroke erasing, text-box creation, discovery of all eight resize handles, a real BottomRight drag, Undo/Redo geometry restoration, and text save/reopen; device/third-party checks remain separate.

## Constraints

- Use an explicitly generated temporary PDF and isolated `LOCALAPPDATA`, `APPDATA`, and `OPENNOTES_DATA_ROOT` values.
- Never scan or modify the user's normal data directories.
- Do not claim stylus/device behavior from mouse input; the script reports pointer coverage separately.
- Close only the child OpenNotes process started by the script.
- Dot-sources `OpenNotesEditorAutomationIds.ps1`; toolbar, viewer, page and text resize-handle lookups use the shared production aliases, with no Fit Page entry and no direct legacy toolbar strings.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** Preserve the real pointer-input smoke path for pen/whole-stroke eraser, text-box creation, eight-handle UI Automation discovery, resize, Undo/Redo geometry restoration and text save/reopen.
- **Next steps:** 1) extend the same session to third-party viewer checks where the environment permits; 2) keep stylus/device behavior as a separate hardware check; 3) retain the remaining full-flow gaps explicitly in the main checklist.
- **Blockers / notes:** The script first tries physical cursor input; if the host rejects `SetCursorPos`, it uses an explicitly labelled `WM_MOUSE*` window-message fallback. The pen phase logs exact screen drag coordinates and checks Undo/Save immediately after the drag so an input-delivery failure is not misdiagnosed as a PDF writer failure. The escalated interactive run now accepts physical input and creates the pen stroke, removes it with Whole-Stroke Eraser, creates the text box and discovers all eight UIA handles; the un-escalated fallback still cannot claim WPF hit-testing. A later retry found the Windows foreground owner was `LockApp`, so no physical pointer claim is made for that retry; the earlier `POINTER_SMOKE_RESULT=PASS` remains the valid product evidence. Codex project/session migration is already complete; the remaining external checks are separate.
- The save/reopen branch logs any secondary error-dialog title and text before failing, so a PDF write problem cannot be hidden behind a generic hash timeout.
- Wave 3 parse/source verification is green. A fresh run after the alias migration reached the isolated editor but was blocked at the first physical Text-tool click (`FOREGROUND_TARGET_SET=False`, `foregroundPid=0`) and cleaned its exact temporary environment; no pointer PASS is claimed for that run. Earlier separately recorded escalated pointer evidence remains unchanged.

## Agent Decisions / Thoughts

- **2026-08-21:** Keep this as a separate smoke script instead of changing the existing tool-control smoke: control discovery and pointer geometry have different failure modes, and a green control-toggle test must not imply drawing or resize behavior.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-22 | Added pen-phase drag coordinates plus immediate Undo/Save state diagnostics to distinguish desktop input delivery from PDF save failures. | Codex |
| 2026-08-22 | Added isolated real pen draw and Whole-Stroke Eraser save evidence by counting PDF `/Ink` annotations before and after the pointer gesture. | Codex |
| 2026-08-22 | Recorded the later desktop retry limitation: `LockApp` owned the foreground, so the failed pointer activation was classified as a host-input block rather than a product regression. | Codex |
| 2026-08-21 | Planned isolated real-pointer text-box/resize-handle smoke coverage. | Codex |
| 2026-08-21 | Added pointer injection guard and stable-handle discovery assertions; recorded the host input limitation. | Codex |
| 2026-08-21 | Added real BottomRight drag plus Undo/Redo geometry assertions after the custom resize-handle automation peer made all eight handles discoverable. | Codex |
| 2026-08-21 | Added text entry, atomic PDF save hash change, process restart, recent-file reopen and persisted text-value assertions. | Codex |
| 2026-08-21 | Corrected all PDF fixture newline literals and added bounded toolbar retry; the full pointer/save/reopen smoke passed with `POINTER_SMOKE_RESULT=PASS`. | Codex |
