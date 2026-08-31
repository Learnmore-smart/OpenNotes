# OpenNotes.Tests/StrokeEraserGeometryTests.cs

## Exact eraser regression coverage (2026-08-30)

- **2026-08-31 stylus crash regression:** reproduces the shipped 5.2.7 crash with an
  eraser packet whose `StylusPointDescription` includes a device property beyond
  X/Y/pressure. It was RED with the same incompatible-description exception,
  then GREEN after production normalized the path to coordinates; whole-stroke
  erase semantics are asserted in the same test.

- **Scope:** STA production tests invoke the page control's private erase gesture seam and assert visible `InkCanvas` stroke results without synthesizing device events.
- **Whole-stroke safety:** a bounds-overlapping diagonal that is not touched by the rectangular eraser path remains intact.
- **Pixel geometry:** a sparse two-point line is split when the eraser crosses its segment, proving erasure is path-aware rather than sample-point-only.
- **Pixel safety:** a diagonal whose broad bounds overlap the eraser rectangle but whose visible path is elsewhere remains untouched and produces no erase payload.
- **Fast pointer moves:** two erase updates whose endpoints miss a stroke still erase a crossing segment because the previous pointer position is joined to the current update path.
- **Cancellation:** an in-flight mouse-like erase with no mutation loses its active flag, capture state, erase points, and previous pointer anchor when `CancelInteraction` runs.
- **History:** erase → undo → erase again is covered through the same placement-backed `StrokesErasedAction` used by `EditorPage`, and the second gesture must remove the restored live stroke.
- **Atomic history conflicts:** an invalid token/side fragment placement makes erase undo report failure and rolls back the already-removed fragment, leaving the pre-undo collection unchanged.
- **Mode propagation:** the real eraser popup callback is covered to ensure changing whole-stroke/pixel mode updates every page even when the editor's cached settings snapshot is stale.
- The popup regression accepts both the original direct panel and the bounded scroll wrapper used by the production tool popup.
- A real hosted WPF mouse-capture regression verifies mouse-up commits the erase transaction before releasing capture, so `LostMouseCapture` cannot roll the gesture back.
- **Test runtime:** NUnit tests run in an STA apartment because WPF `InkCanvas`, `Stroke`, and `StylusShape` require the WPF dispatcher/threading model.

> Keep this mirror synchronized with `OpenNotes.Tests/StrokeEraserGeometryTests.cs` when the test seam or assertions change.
