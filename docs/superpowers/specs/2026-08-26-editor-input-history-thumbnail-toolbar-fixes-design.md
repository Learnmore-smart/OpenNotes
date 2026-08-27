# Editor Input, History, Thumbnail, and Toolbar Fixes Design

**Goal:** Repair the six reported editor regressions while preserving PDF compatibility, one-based navigation, and existing automation identities.

## Design

Selection remains page-local. Opening a tool popup must not permanently cost the first selection gesture. Stroke hit testing will accept visible freehand drawings through a bounded fallback while retaining precise path hits, and Ctrl-click will toggle multiple items on the same page without clearing the existing set. Cross-page accumulation is intentionally unchanged.

When a pen-family popup closes from a canvas pointer gesture, the editor records a pending dismissal gesture. A stationary click is treated as dismissal-only and cannot create an ink/undo entry; movement beyond the system drag threshold remains an intentional stroke and proceeds through the normal pipeline. This guard is limited to ink-producing tools so it does not redefine eraser behavior.

Shape recognition is one user gesture and therefore one history entry. A newly drawn stroke that is smoothed and recognized is recorded as an added ideal stroke: Undo removes it and Redo restores the ideal result. The historical intermediate freehand/smoothed geometry is not exposed as a separate undo step. Existing immutable placement/token safety remains intact.

Sidebar thumbnails keep using the clean Pdfium base image, then composite the current page's ordinary ink at thumbnail scale. Ink mutations invalidate only that page, and revision/session checks prevent an older asynchronous render from overwriting a newer thumbnail.

Toolbar semantics are repaired without changing handlers or AutomationIds: Laser receives a beam/dot vector, Hidden Ink receives a card/reveal vector, tool widths and separator rhythm become consistent, and the page navigator becomes a compact symmetric group while remaining centered and editable.

## Verification

- RED/GREEN STA tests for same-page selection, Ctrl-toggle, broad-stroke hit testing, popup click suppression versus drag-through, and one-step recognized-stroke undo/redo.
- Pixel/compositor and invalidation tests for live ink in thumbnails.
- Source/STA layout contracts for semantic icons, compact navigator geometry, centering, localization, and stable AutomationIds.
- Focused filters, full test suite in a clean process, Release build, i18n, diff check, installer build, isolated launch smoke, then v5.2.3 GitHub release asset verification.

