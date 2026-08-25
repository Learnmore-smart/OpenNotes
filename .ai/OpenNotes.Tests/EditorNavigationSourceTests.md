# OpenNotes.Tests/EditorNavigationSourceTests.cs
> Last updated: 2026-08-24 | Protection: STANDARD

> 2026-08-25 RED/GREEN: new contracts require a fixed 184-DIP expanded sidebar, 38-DIP collapse/auto-collapse, no resize surface/provider/handlers, and a centered `PageJumpGroup` toolbar overlay with a reserved action-row footprint. The focused slice failed against the pre-change implementation; the live STA layout now also proves the page-jump center matches the floating toolbar center within one DIP.

## Purpose

Wave4 source and WPF contract tests for the compact page jump and custom document navigation rail.

## Open Threads / Resume Context

- **Status:** review_followup_in_progress.
- **Intent/result:** page navigation is keyboard-first and the native TabControl presentation is rejected while PDF scrolling, zoom, thumbnail rendering, outline navigation and bookmark persistence remain protected.
- **RED/GREEN baseline:** the initial source contracts failed against the old `PageJumpBorder`/native tab markup; after the baseline implementation the source checks and real STA AutomationPeer checks passed 3/3. The review-follow-up additions were written RED-first and the first expanded class passed 12/12. The final P2 RED added three deterministic failures for the zero-based initial field, state metadata being overwritten after localization, and fallback external Invoke reachability; after the guard, state-aware metadata tail pass and row invoke affordance, the class passed 14/14. The recycled-menu RED then caught four stale-binding assertions; after centralized cleanup and current-model rebind, the complete navigation class passes 15/15. The full suite passes 204/204.
- **Follow-up coverage:** deterministic multi-round PageJump Enter/Escape/invalid/out-of-range/LostFocus re-entry plus one-based initial UIA Value and non-editing state; live light/dark/high-contrast expressions and selection text contrast; fallback outline SelectionItem and Invoke parity to page 2 (including the external `.Invoke` button); recycled Pages/Bookmarks ListBoxItem menu cleanup and command identity; recycling/deferred thumbnail realization; stale outline cancellation/session TCS; resize RangeValue keyboard semantics; localized bookmark Toggle metadata across EN/ZH/FR; narrow/collapsed layout; and a programmatic thumbnail-selection guard.
- **Runtime fixtures:** tests use real STA `EditorPage`, `AutomationPeer`, `ISelectionItemProvider`, `IRangeValueProvider`, and high-contrast resource switching. Window-backed tests detach the production WindowsPen loaded hook to avoid an unrelated HwndSubclass teardown on the headless test dispatcher. The explicit three-page editor smoke separately commits PageJump page 2 and invokes outline page 2.
- **Blockers:** foreground/device visual evidence is external; `Test-OpenNotesCrossPageKeyboardSmoke.ps1` reports `REAL_SCREEN_INPUT_UNAVAILABLE` with no foreground window. Wave6 transient dismissal and Sticky Note lifecycle are explicitly out of scope.
- **Pending recycled-menu bug fix:** the existing realized-row contract does not yet prove that `ListBoxItem.ContextMenu` is invalidated when a recycling container changes from an old page/bookmark model to a new one. The next RED test will execute old and current menu commands on one recycled container and require no stale action or handler accumulation for both Pages and Bookmarks.

## Important Notes / NEVER Change

- Page jump must retain one-based display and clamp/restore behavior while exposing `Editor.PageJump` on the editable TextBox.
- Sidebar tests must reject native `TabControl`/`TabItem` presentation and preserve `ThumbnailListBox`, `OutlineTreeView`, and `BookmarksListBox` functionality.
- Do not assert or require changes to PDF bitmap dimensions, scroll coordinates, toolbar controls, or MainWindow deactivation.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Added Wave4 RED/GREEN contracts for compact navigation and custom rail semantics. | Codex |
| 2026-08-23 | Promoted the Wave4 source/STA contracts to green after the compact page field, custom rail and dynamic metadata were implemented. | Codex |
| 2026-08-24 | Added review-follow-up RED/GREEN contracts for repeat editing, themes/HC, fallback outline, deferred virtualization, stale loads, resize RangeValue, bookmark Toggle, narrow collapse and selection guards; 12/12 navigation tests and 201/201 full suite passed. | Codex |
| 2026-08-24 | Added final P2 RED/GREEN contracts for one-based PageJump construction, state-preserving EN/ZH/FR metadata and fallback SelectionItem+Invoke parity; 14/14 navigation tests and 203/203 full suite passed. | Codex |
| 2026-08-24 | Documented the recycled ListBoxItem ContextMenu binding bug and RED-first plan: clear old menu/handlers on Unloaded/DataContextChanged and rebuild from current page/bookmark model identity on every Opening. | Codex |
| 2026-08-24 | Added and passed the recycled Pages/Bookmarks menu regression: one container page1/bookmarkA → page2/bookmarkB, old commands no-op after cleanup, current model identity rebinds; 15/15 navigation and 204/204 full suite passed. | Codex |
