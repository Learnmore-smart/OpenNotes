# website/demo.js
> Last updated: 2026-08-22（direct text movement and interaction verification） | Protection: STANDARD

## Purpose

Provide a safe static preview of pen/highlighter/eraser input, one-step undo/clear controls, and an editable, eight-direction pointer-resizable text box using Pointer Events and pointer capture.

## Important Notes

- The demo never reads local files or modifies the desktop app.
- Gesture feedback begins on pointer-down and remains direct during movement.
- Each resize direction is a real button with localized ARIA text and direction-aware keyboard Arrow resizing; Shift increases the step size.
- The status region uses polite live announcements, and the demo refreshes its messages when the locale changes.
- The theme button persists a dark/light choice in local storage and exposes its pressed state for keyboard and assistive-technology users.
- User drawing, erasing, and clear actions push a snapshot onto a bounded undo stack; undo restores the previous marks without reading or writing local files.
- Undo and clear controls expose disabled state and localized status messages, including the empty-history case.
- Optional artwork probes are memoized per slot; changing language updates the localized status without issuing duplicate requests.
- Optional artwork is checked with a handled `fetch`; missing drop-in files keep the placeholder and are expected to produce development-only 404 network entries until the documented assets are added.

## Open Threads / Resume Context

- **Status:** complete
- The localized `data-demo-drag` grip preserves pointer grab offset, uses pointer capture, clamps the note inside the paper, releases on up/cancel and supports Arrow-key movement with a larger Shift step. Content editing and all eight pointer/keyboard resize paths remain intact.

## Verification

Browser automation passes drawing/undo, pointer and keyboard text movement, pointer/keyboard resize, theme/locale switching, reduced-motion and error-free 404 navigation.
