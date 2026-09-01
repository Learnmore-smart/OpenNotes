# Editable Dashed Shapes and Popup Design

**Goal:** Add a durable `ligne pointillée`, make logical shapes select/edit as one object, and keep tool popups compact and vertically scrollable.

## Shape representation

Shapes remain WPF Ink strokes so existing selection, move, resize, copy/paste, undo, thumbnails, and PDF annotation architecture stay intact. Each committed shape receives semantic metadata: group id, shape kind, part index, and dashed flag. A solid line/rectangle/ellipse is normally one part; arrows and dashed lines are multiple parts sharing one group.

`ligne pointillée` is a sequence of real short strokes along one straight segment. It is not a continuous stroke with decorative preview dashes. Clicking any part expands selection to every live part in the group, so existing selection transforms operate on the complete logical shape. Pixel erasing may split/touch individual visible parts while inheriting the group metadata; whole-shape deletion and selection commands act on the group.

The metadata is added compatibly to `StrokeAnnotation` and written to owned PDF `/Ink` annotations using private `/WNA*` keys. Old PDFs omit the fields and load as ordinary freehand strokes. Get/add, PDF load/save, clipboard copy/paste, duplicate, undo snapshots, and eraser fragments preserve the metadata.

## Shape editing

The Select popup gains a selected-drawing style section. When drawings are selected it exposes color and line width using the existing palette/size controls. Applying a style changes all selected/group-expanded strokes, retains the selection, marks the document dirty, invalidates the thumbnail, and creates one batch undo action with cloned before/after `DrawingAttributes`.

The existing eight selection handles remain the shape resize affordance. Group selection makes multi-part arrows and dashed lines resize together. No second shape-specific bounding box is introduced.

## Popup layout

The pen behavior controls become one compact three-column row with the existing localized labels and AutomationIds. Tool popup bodies sit inside a bounded vertical `ScrollViewer` with automatic scrollbar visibility; size, palette, preview, behavior, and smoothing sections remain in order. The nine-shape catalog keeps its readable three-column grid rather than overflowing horizontally.

## Invariants

- Legacy freehand strokes and PDFs remain valid and selectable.
- PDF coordinates, atomic save, and annotation stripping are unchanged.
- One logical style edit is one Undo/Redo entry.
- Dashed gaps are real gaps and therefore do not erase/hit as ink.
- Group ids never cause unrelated strokes to be selected or deleted.
- Existing localization and automation identities remain stable; add EN/ZH/FR for `ligne pointillée`.

## Verification

- Geometry tests for dash segmentation and group expansion.
- STA tests for group selection/move/resize/style/undo and selection retention.
- Model, clipboard, and PDF round-trip tests for metadata.
- Popup source/STA tests for one-row behavior toggles, bounded scrolling, localization, and AutomationIds.
# 2026-08-31 follow-up: dashed style and atomic completion

- The shape picker represents geometry only; dashed line is no longer a geometry tile.
- A separate session-only Solid/Dashed toggle applies to every geometry.
- Dashed visuals use real separated WPF ink segments with one logical group id.
- Pointer-up publishes the complete group as one history action, so Undo/Redo removes or restores the full arrow or dashed shape.
