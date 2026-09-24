# OpenNotes.Core/Models/InkStrokeData.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

UI-free in-memory stroke payload mirroring `StrokeAnnotation` semantics 1:1 for the V6 WinUI 3 migration (Task 2). The WinUI `InkSurface` builds this type directly; the WPF layer converts live `System.Windows.Ink.Stroke` objects through `Controls/WpfStrokeAdapter.cs`.

## API

- `Points`: `List<InkPointData>` spine points in page DIP coordinates (origin top-left).
- `R`/`G`/`B`/`A`: RGBA colour channels (`A` defaults to 255), same as `StrokeAnnotation`.
- `Size`: uniform stroke width/height in DIPs, default 2.0.
- `IsHighlighter`, `FitToCurve` (default `true`, same default as `StrokeAnnotation`).
- `IgnorePressure` (Task 7A): session-only rendering choice, mirrors WPF `DrawingAttributes.IgnorePressure` — uniform-width rendering/erasing at effective pressure 0.5. **Never serialized.**
- Shape metadata: `ShapeGroupId`, `ShapeKind`, `ShapePartIndex`, `IsDashedShape` — empty group/kind means ordinary legacy ink.
- `GetShapeIdentity()` / `ApplyShapeIdentity(ShapeStrokeIdentity)`: read or copy the logical-shape identity in one step.
- `Clone()`: deep copy (new point list).
- `ToAnnotation()` / `FromAnnotation(StrokeAnnotation)`: sidecar conversion.
- `CaptureSnapshot(token, side)` / `FromSnapshot(StrokeReplacementSnapshot)`: replacement-ledger conversion — carries the shape identity too (`FromSnapshot` restores via `ApplyShapeIdentity`).

## Pressure format decision (Task 7 Phase A)

`StrokeAnnotation.Points` entries serialize as **`[x, y, pressure]`** (3-element) for new writes:

- Reading: `pt.Length >= 3` → `p = Clamp(pt[2], 0, 1)`; legacy `[x,y]` keeps the historical **0.5** default — the same value `StylusPoint` defaults to and `IgnorePressure` renders at, so old sidecars render identically.
- Writing: always `[x,y,p]`.
- Compatibility: the WPF loader reads `pt[0]`/`pt[1]` only, so `[x,y,p]` files remain forward-compatible with the shipping WPF app (it silently keeps pressure=0.5 on load, and drops `p` on copy/paste — accepted deviation, documented). WPF's own save path still writes `[x,y]`.

## Constraints

- Shape metadata semantics must stay identical to the WPF extended-property keys (`ShapeStrokeMetadataKeys`).
- `StrokeReplacementSnapshot` carries the four shape fields (added 2026-09-23 — previously a round-trip silently dropped shape identity and re-saved it as plain ink). `InkSurface.AddStroke` also re-derives `IgnorePressure=true` for any stroke with a non-empty `ShapeGroupId` (WPF `PdfPageControl.AddStroke` parity — the flag never serializes).

## Verification

`OpenNotes.Tests/CoreInkEngineTests.cs` — `[x,y,p]` round-trips (incl. JSON), legacy `[x,y]` default, malformed-point skipping, snapshot conversion.
