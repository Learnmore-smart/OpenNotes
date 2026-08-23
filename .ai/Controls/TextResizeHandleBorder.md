# Controls/TextResizeHandleBorder.cs
> Last updated: 2026-08-21 | Protection: STANDARD

## Purpose

The code-created text annotation resize handle keeps its existing `Border` visuals and input events while supplying a real WPF `Thumb` UI Automation peer. Plain `Border` elements have no default automation peer, so `AutomationProperties.AutomationId` alone is not enough for live UIA discovery.

## Contract

- `TextResizeHandleBorder` derives from `Border`, preserving the existing visual and pointer/stylus behavior.
- `OnCreateAutomationPeer()` returns `TextResizeHandleAutomationPeer`.
- The peer reports `AutomationControlType.Thumb` and reads its name/automation ID from the element's `AutomationProperties`.
- The editor assigns `TextAnnotationGeometry.GetResizeHandleAutomationId(...)` to each instance; the eight direction identifiers remain stable for smoke tests and assistive technology.

## Open Threads / Resume Context

- Add this control only to the eight text resize handles; do not change unrelated decorative `Border` elements.
- Real desktop pointer smoke must still verify the peer is visible, a handle drag changes the text rectangle, and undo restores it.
