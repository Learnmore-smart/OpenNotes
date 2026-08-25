# Controls/LucideIcon.cs

> V5.1.2 expands the shared geometry catalog and maps historical icon identifiers to Lucide vectors.

> Last updated: 2026-08-24 | Protection: STANDARD

## Purpose

Provides one theme-aware WPF renderer for the consistent open-source Lucide-style vector icons used by editor chrome.

## Open Threads / Resume Context

- **Status:** implemented
- **Intent:** replace mixed font glyphs and one-off toolbar geometry with named 24-unit vector icons while preserving existing button handlers, IDs, tooltips and target sizes.
- **Constraints:** icons are decorative; accessible names remain on their owning controls. Geometry must scale without layout shifts and inherit the owning control foreground.

## Important Notes / NEVER Change

- Do not use an icon font or depend on a particular installed font.
- Keep icon rendering independent from PDF/annotation colors.

## Implementation

- `LucideIcon : Shape` resolves named, frozen 24-unit geometries and inherits the owner control's dynamic theme stroke. The editor uses it for tools, zoom/history, sidebar navigation, collapse state, and page navigation without an installed icon font.
- The Sticky editor header uses the named `GripVertical` geometry as a decorative drag affordance; accessible text remains on the owning header surface.
