# Controls/TextAnnotationDragHandleBorder.cs

## 2026-08-28 role

- This peer-bearing legacy control now backs the text annotation's visible chrome border only, preserving a Thumb automation surface without rendering the retired dotted side handle. User input is routed by the parent annotation container's bounded border gesture handlers.
> Last updated: 2026-08-22 | Protection: STANDARD

## Purpose

Provide a dedicated UI Automation peer for the code-created text-annotation move handle. A plain WPF `Border` has no reliable desktop UIA peer, so cross-page regression tooling and assistive technologies cannot otherwise distinguish the move target from the text editor.

## Contract

- `TextAnnotationDragHandleBorder` remains a `Border`, preserving the existing mouse/stylus event route and visual chrome.
- Its peer reports `AutomationControlType.Thumb` and returns `AutomationProperties.Name`.
- `EditorPage.CreateTextBox` assigns `TextAnnotationDragHandle` as the stable AutomationId and uses the localized `Editor.MoveTextBox` name.
- The handle does not expose or modify document content; it only improves discovery of the existing drag interaction.

## Open Threads / Resume Context

- **Status:** ready_for_next
- The cross-page keyboard smoke uses this peer together with `PdfPageControl.{i}` page bounds.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-22 | Added a dedicated UIA peer for the runtime text annotation move handle. | Codex |
