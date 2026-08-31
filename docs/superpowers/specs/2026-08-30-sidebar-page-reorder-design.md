# Sidebar Page Reorder Design

**Goal:** Let users drag page thumbnails vertically to persistently reorder PDF pages with an insertion cue, synchronized navigation/bookmarks/thumbnails, and one-step Undo/Redo.

## Design

The drag operation carries an immutable payload containing source page index, load session, normalized document path, and source model identity. This avoids the current failure where mutable drag fields are cleared before WPF's synchronous `DoDragDrop` enters `Drop`.

`DragOver` resolves a visual insertion slot: the upper half of a row means before it, the lower half means after it, and empty space below the rows means the final slot. The slot is converted to the PDF service's final index after source removal. One accent-colored, non-hit-testable line overlays the list and is cleared on leave, drop, cancellation, reload, or host deactivation.

A valid drop autosaves dirty annotations, snapshots PDF bytes and bookmarks, performs the atomic page reorder, reloads the document using the actual single session increment, selects the moved page, remaps bookmarks with the same final index, refreshes thumbnails/navigation, and pushes one `DocumentSnapshotAction`. A stale payload performs no write.

## Invariants

- PDF page order and bookmark order use the same final-index convention.
- Page data, size, rotation, annotations, and thumbnail ink remain attached to their moved page.
- Same-position drops are no-ops and add no history entry.
- Undo/Redo restores exact document bytes and bookmark snapshots.
- Existing one-based navigator labels and AutomationIds remain unchanged.

## Verification

- Pure slot/final-index tests for upward, downward, before, after, end, and no-op cases.
- Source/STA tests for immutable payload retention, indicator lifecycle, stale-session rejection, selected page, and history.
- PDF service forward/backward/end reorder tests plus focused sidebar/bookmark/thumbnail suites.
