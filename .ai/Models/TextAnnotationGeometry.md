# Models/TextAnnotationGeometry.cs
> Last updated: 2026-08-20 | Protection: STANDARD

## Purpose

Pure geometry helpers for resizable text annotations. It defines the eight resize handles, normalizes dimensions, and computes the new rectangle while keeping the opposite edge anchored.

## 2026-08-28 planned contract

- Add a framework-independent inner-border hit test used by textbox movement. Points inside the configured edge band count as border input; the center/content region does not.
- GREEN: IsMoveBorderHit validates finite bounds and applies an 8-DIP inner edge band, capped at half the smallest dimension.

## API

- `TextResizeHandle`: eight directions (`TopLeft`, `Top`, `TopRight`, `Left`, `Right`, `BottomLeft`, `Bottom`, `BottomRight`).
- `TextBoxBounds`: immutable `X`, `Y`, `Width`, `Height` value with derived `Right` and `Bottom` edges.
- `TextAnnotationGeometry.Normalize`: applies the minimum width/height constraints.
- `TextAnnotationGeometry.Resize`: applies a drag delta for one handle and preserves the opposite anchor when a minimum is reached.
- `TextAnnotationGeometry.ClampToPage`: clamps the normalized rectangle to a measured page surface while retaining the minimum dimensions; invalid/not-yet-measured page sizes leave the normalized rectangle usable.
- `TextAnnotationGeometry.GetResizeHandleAutomationId`: returns the stable `TextResizeHandle.{direction}` UI Automation ID used by code-created resize handles.

## Constraints

- Minimum size is 120 × 48 DIP; default size is 280 × 84 DIP.
- Non-finite dimensions are clamped to the minimum.
- Page bounds are applied by EditorPage during live resize; old annotations with zero width/height continue through the automatic-size path.
- The helper is UI-framework independent so geometry tests do not require a WPF desktop session.

## Verification

`OpenNotes.Tests/TextAnnotationTests.cs` covers legacy zero dimensions, top-left/opposite-edge behavior, minimum dimensions, bottom-right growth, page-boundary clamping, and all eight stable resize-handle IDs.

## Change History

- **2026-08-21:** Added stable resize-handle automation ID generation for UI Automation and the pointer smoke harness.
