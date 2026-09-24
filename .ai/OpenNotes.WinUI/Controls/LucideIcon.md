# OpenNotes.WinUI/Controls/LucideIcon.cs
> Last updated: 2026-09-23 (V6 Task 6 — editor toolbar icons) | Protection: STANDARD

## Purpose
`Caelum.Controls.LucideIcon : Path` — WinUI port of the WPF `Controls/LucideIcon.cs`
font-independent 24-unit outline icons (Lucide visual language) used across the
editor toolbar and chrome. The owning Button keeps the accessible name; the icon
is hit-test/tabbable-inert.

## What It Does
- Subclasses `Path` (WinUI has no `Shape.DefiningGeometry` override) and assigns
  `Path.Data` whenever `Kind` changes; WPF `OverrideMetadata` defaults move into
  the instance ctor (`Stretch=Uniform`, `StrokeThickness=1.8`, round caps/join).
- `Kind` accepts both the canonical names (`"Undo"`, `"PenLine"`, `"ChevronRight"`,
  …) and legacy Segoe glyph chars, mapped through `LegacyKinds` to the same table.
- `CreateIcons()` returns **markup strings**, not `Geometry` — a `Geometry`
  cannot be shared between two live `Path` elements (second assignment throws);
  each icon instance parses its own `PathGeometry` on assignment.
- `ParseIconGeometry` is a minimal SVG-path parser covering the command set the
  table uses (`M/L/H/V/A/C/Z`, relative+absolute). It is `internal` (Phase B —
  the free-form selection/shape preview paths reuse it for `Geometry.Parse`-
  style markup since WinUI has no `Geometry.Parse`). `C/c` emits
  `BezierSegment`s (control points resolve absolute/relative like WPF).
  `XamlReader.Load` was the first
  port attempt and **cannot be re-entered while the page's own XAML is loading**
  (the control is first materialized inside `InitializeComponent`) — the nested
  parse crashed icon creation with `Cannot create instance of type
  'Caelum.Controls.LucideIcon'`. Do not reintroduce it.

## Important Notes / NEVER Change
- NEVER cache/share a parsed `Geometry` across icon instances (mutation +
  cross-element share crashes — was the `Kind` assignment failure).
- Keep the parser scope-limited to the table's commands; extending the table
  beyond `M/L/H/V/A/C/Z` requires extending `ParseIconGeometry` first.

## Open Threads / Resume Context
- **Status:** GREEN — all toolbar icons instantiate; editor smoke passes.
