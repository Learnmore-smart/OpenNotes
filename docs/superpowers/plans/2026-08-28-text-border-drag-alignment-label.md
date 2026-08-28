# Text Border Drag and Alignment Label Fix

## Goal

Remove the separate dotted text-move affordance, move selected text annotations by dragging their visible border without stealing interior text-edit gestures or resize-handle input, and ensure the alignment selector renders its localized label instead of the backing `Caelum` type name.

## TDD sequence

1. Add geometry coverage for an inner border hit band and runtime coverage that alignment options stringify to their localized labels.
2. Verify the focused tests fail against the current implementation.
3. Route mouse/stylus border gestures on the annotation container through the existing drag, cross-page transfer, dirty-state, and undo paths; remove the separate drag-handle visual.
4. Make alignment-option presentation deterministic in the shared ComboBox selection presenter.
5. Update UI Automation/source contracts and File Guardian mirrors, then run focused tests, the full suite, and a Release build.

## Invariants

- Interior clicks continue to place the text caret and edit text.
- Eight resize handles keep priority over border movement.
- Cross-page text movement and same-page undo/redo retain the existing action implementations.
- The legacy `Caelum` namespace and storage/package compatibility identities remain unchanged and must not leak into visible alignment text.
