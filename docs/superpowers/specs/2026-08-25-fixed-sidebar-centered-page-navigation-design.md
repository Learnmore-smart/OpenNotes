# Fixed Sidebar and Centered Page Navigation Design

## Goal

Remove the document-sidebar resize affordance and keep the editable page navigator visually centered in the floating toolbar at every normal window width.

## Design

- The expanded sidebar keeps its existing 184-DIP width. Collapse/expand and the existing 38-DIP narrow-window collapsed rail remain unchanged.
- Remove the resize thumb, cursor, tooltip/UIA range provider, drag/keyboard handlers, and dead resize-only state. There is no invisible resize hit target at the sidebar edge.
- Wrap the toolbar content in an overlay `Grid`. The action row remains the existing horizontally scrollable `StackPanel`. A transparent, non-interactive 150-DIP spacer reserves the page navigator's footprint after Ruler and before Select, which is the action row's measured natural midpoint.
- Move the existing page navigator border out of the scrolling action row and render it as a centered overlay sibling. Its previous/next buttons, editable one-based field, page count, localization, keyboard behavior, and AutomationIds are unchanged.
- Narrow-window toolbar overflow remains available. Centering is relative to the floating toolbar rather than to whichever actions happen to precede the navigator.

## Verification

- Source/STA navigation tests reject every sidebar resize symbol and assert the fixed expanded width, collapse behavior, centered overlay structure, reserved footprint, and unchanged page-jump AutomationId.
- Run focused navigation tests, the full Release suite, Release build, i18n check, and diff check.
- Build 5.2.2, publish the tag-driven installer, install it, launch it, and open the user's 1,353-page textbook.

## Scope Guard

No PDF numbering semantics, printed-page labels, render policy, annotations, save format, zoom math, or sidebar collapse behavior changes.
