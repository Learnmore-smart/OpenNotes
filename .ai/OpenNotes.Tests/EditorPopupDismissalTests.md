# EditorPopupDismissalTests

## 2026-08-28 RED plan

- Add runtime coverage that the selected Center alignment option renders/stringifies as its localized label and never exposes the backing `Caelum` type name.
- GREEN: the runtime Center option now returns its localized label through the shared selected-item presenter fallback.

## v5.2.4 text ComboBox follow-up (2026-08-27) — IN PROGRESS

- Add STA coverage proving generated font-family and alignment dropdown items are editor-owned transient content and cannot be closed by `ShouldClosePopupOnPointerDown` before their selection commits.

> Last updated: 2026-08-26 | Protection: STANDARD

## Purpose

Production-path regression coverage for outside Pen/Highlighter popup gestures.

## What It Does

Exercises `EditorPage_PreviewMouseDown`, the native `InkCanvas_StrokeCollected` pipeline, and the editor's real stroke/dirty event forwarding. A stationary outside click must dismiss without ink/history; a path beyond the system drag thresholds must remain a normal drawing.

## Open Threads / Resume Context

- **Status:** complete
- **Intent:** finish issue 3 with RED/GREEN coverage for stationary dismissal, drag-through behavior, stale pending-state cleanup after a no-collection pointer-up, interactive Hidden Ink overlay clicks, and unrelated transient-surface dismissal.
- **Evidence:** the lifecycle regression was RED at `1 failure / 2 passes` before the pointer-up fix. The Hidden Ink overlay regression was RED at `1 failure / 3 passes` before the native-target gate. Focused popup coverage is now GREEN at `4/4`; `HiddenInkTests` are GREEN at `10/10`; relevant `PenOnlyInputTests` are GREEN at `1/1`. Expected Pdfium NU1701 and WPF high-DPI WFAC010 warnings remain.
- **Evidence:** the unrelated-popup regression was RED at `1 failure / 4 passes` before active-popup-only arming. Focused popup coverage is GREEN at `5/5`; `HiddenInkTests` are GREEN at `10/10`; `PenOnlyInputTests` are GREEN at `1/1`; `ShapeSelectionTests` are GREEN at `8/8`.
- **Next steps:** none; the complete suite and Release build are GREEN.

## Important Notes / NEVER Change

- Keep eraser behavior outside the guard.
- Do not replace the native InkCanvas collection pipeline or alter PDF coordinate/persistence semantics.

## Change History

| Date | Change | Author |
|------|--------|--------|
| 2026-08-26 | Added issue-3 test plan before production edits. | Codex |
| 2026-08-26 | Added stationary-tap, drag-threshold, and PenOnly no-collection pointer-up coverage; the first lifecycle run was RED at 1/3. | Codex |
| 2026-08-26 | Cleared stale pending dismissal state on normal mouse-up and stylus-up; popup filter 3/3 and PenOnly filter 1/1 are GREEN. | Codex |
| 2026-08-26 | Added interactive Hidden Ink overlay coverage, gated arming to the actual native InkCanvas target, and verified popup 4/4, HiddenInk 10/10, and PenOnly 1/1. | Codex |
| 2026-08-26 | Added unrelated-popup dismissal coverage, gated arming to the active Pen/Highlighter popup closure, and verified popup 5/5 plus selection 8/8. | Codex |
| 2026-08-28 | Added detached font/alignment item ownership plus real SelectionChanged format/history/dirty regressions; preserved non-activating popup HWND behavior after review. | Codex |
