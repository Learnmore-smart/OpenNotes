# OpenNotes.Tests/TextAnnotationTests.cs

## 2026-08-28 RED plan

- Add boundary coverage for the textbox inner border move band so border drags cannot consume interior text-edit gestures.
- GREEN: seven edge/interior cases pass with the 8-DIP inner band.
> Last updated: 2026-08-21 | Protection: STANDARD

## Purpose

Pure model/geometry regression coverage for backward-compatible text annotation rectangles.

## Coverage

- Legacy zero-width/zero-height text annotations retain automatic sizing.
- Geometry tests cover minimum-size clamping, opposite-anchor preservation and page-boundary constraints.
- All eight `TextResizeHandle` values map to stable `TextResizeHandle.<Direction>` identifiers.
- The code-created resize-handle control must expose a `Thumb` UI Automation peer so live desktop automation can discover and exercise it.
- The STA UIA control test supplies the process-local `WINDIR` alias from `SystemRoot` when the test host omits it, matching the application's WPF startup guard without changing machine state.

## Open Threads / Resume Context

- **Status:** in_progress
- The stable identifier contract is green. The next regression contract covers the custom UI Automation peer used by the code-created resize-handle control; then rerun real pointer discovery/drag/undo smoke.
