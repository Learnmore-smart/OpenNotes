# OpenNotes.Tests/RulerInteractionTests.cs

> Last updated: 2026-08-27 | Protection: STANDARD

## Purpose

RED-first production geometry regressions for the session-only on-screen ruler.

## Open Threads / Resume Context

- **Status:** complete
- Cover perpendicular body collision, inside-body rejection, and nearest-long-edge snapping through the real `PdfPageControl` ruler constraint seam.
- Preserve ordinary stroke attributes/pressure and the single placement-backed undo boundary.
- RED failed 3/3 because the four-corner constraint seam did not exist; focused GREEN covers body rejection, crossing clipping, nearest-edge snapping, and the real collection pipeline publishing one ordinary undoable stroke.
- Final review added an exact-long-edge start regression so the ruler's primary along-edge gesture is not mistaken for an inside-body start.
