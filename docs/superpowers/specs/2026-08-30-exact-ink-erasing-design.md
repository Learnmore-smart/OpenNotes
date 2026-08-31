# Exact Ink Erasing Design

**Goal:** Make pixel and whole-stroke erasing operate on the visible stroke path so sparse straight lines and shapes erase correctly, unrelated drawings never disappear from a bounding-box false positive, and an undone erase can be erased again.

## Design

The current eraser uses two approximations: whole-stroke mode accepts a stroke when its rectangular bounds overlap the eraser, and pixel mode only checks whether an existing stylus sample lies inside a stamped rectangle. A diagonal shape can therefore be removed even when its visible path is far from the pointer, while a two-point straight line can pass through the eraser without either endpoint entering it.

Both modes will use WPF Ink's path-aware `StylusShape` geometry. The gesture supplies its ordered ink-canvas points and a rectangular stylus shape matching the visible eraser size. Whole-stroke mode removes a stroke only when `Stroke.HitTest(points, shape)` reports a path hit. Pixel mode obtains retained fragments from `Stroke.GetEraseResult(points, shape)` and only applies an undo mutation when the returned geometry differs from the original.

The existing `StrokePlacement` token/side ledger remains authoritative. Each erase gesture records original placements and fragment placements once; Undo removes current fragments and restores originals, Redo performs the inverse. Re-erasing a restored original starts a fresh gesture and captures its current placement rather than reusing stale live-stroke references.

## Invariants

- Eraser points are in `InkCanvas` coordinates; no page/screen coordinate mixing is introduced.
- Stroke thickness, pressure, color, and highlighter attributes survive retained fragments.
- A path whose bounds overlap the eraser but whose visible geometry does not is unchanged.
- A sparse two-point line crossing the eraser is split in pixel mode and removed in whole-stroke mode.
- One pointer gesture remains one undo action, including fragments re-clipped during that gesture.
- Hidden Ink keeps its existing separate annotation eraser path.

## Verification

- RED/GREEN STA tests for sparse line intersection, bounding-box false positives, pixel fragment preservation, whole-stroke exact hits, Undo, Redo, and Undo-then-erase-again.
- Focused eraser and stroke-replacement tests, full test suite, Release build, i18n, and diff check.
