# OpenNotes.Core/Models/ShapeStrokeMetadata.cs
> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

UI-free logical-shape identity primitives for the V6 WinUI 3 migration (Task 2): the `ShapeStrokeIdentity` value type plus the stable property-key GUIDs shared by WPF extended properties and serialized `StrokeAnnotation` shape fields.

## API

- `readonly record struct ShapeStrokeIdentity(string GroupId, string Kind, int PartIndex, bool IsDashed)` — the logical identity carried by every generated shape part.
- `internal static class ShapeStrokeMetadataKeys` — the four stable GUIDs (`GroupId`, `Kind`, `PartIndex`, `IsDashed`) used as WPF `Stroke` extended-property keys; exposed to `OpenNotes`/`OpenNotes.Tests` via `InternalsVisibleTo`.

## Invariants

- Empty group/kind metadata means ordinary legacy ink.
- All parts of one arrow or dashed line share one group id and have distinct part indices.
- Dashed gaps contain no ink and therefore do not hit or erase.
- Property keys are stable GUIDs across releases and copied into `StrokeAnnotation` for persistence — NEVER regenerate them.

## Constraints

- Dash-path math (`BuildDashedLine`/`BuildDashedPolyline`) lives in `OpenNotes.Core/Geometry/StrokeGeometry.cs`, not here; the WPF `Models/ShapeStrokeMetadata.cs` facade re-exposes it on `System.Windows.Point`.

## Verification

`OpenNotes.Tests/ShapeStrokeMetadataTests.cs` exercises Apply/Read identity round-trips and dash-phase behaviour through the WPF facade.
