# Editor Toolbar and Shape Picker Polish

## Goal

Make the editor chrome visually coherent and self-explanatory: remove the visible sidebar resize rail, normalize the floating toolbar on the existing open-source Lucide vector system, show localized custom tooltips, remove the shape-picker checkmark, and add useful geometric shapes that use the existing ink pipeline.

## Design

- Keep `Controls/LucideIcon.cs` as the single font-independent icon source. Toolbar icons use one 18-DIP outline treatment and inherit the owning button foreground. Pen and highlighter colors remain visible only as small color bars, avoiding doubled or brightly tinted glyphs.
- Keep the 32-DIP sidebar resize hit target, range provider, keyboard behavior, and cursor, but render its template as transparent so no vertical divider appears.
- Add a page-local WPF `ToolTip` template with rounded themed surface, border, shadow, compact typography, and bottom placement. Existing `ApplyToolbarAccessibilityMetadata()` remains the source of EN/ZH/FR tooltip text, Automation Name, and HelpText.
- Replace the shape picker’s two-row layout with a 3×3 grid: Line, Rectangle, Ellipse, Arrow, Triangle, Diamond, Parallelogram, Pentagon, and Hexagon. Selected state uses the existing blue tile and underline; no overlaid checkmark is rendered.
- Generate every new polygon as a closed point list in `PdfPageControl.BuildShapeOutline`. Committed shapes continue to become ordinary ink strokes, preserving undo, selection, copy/paste, sidecar/PDF persistence, and export behavior.

## Compatibility and accessibility

- Preserve command handlers, stable AutomationIds, localization refresh, focus rings, 32-DIP targets, PDF coordinates, and annotation serialization.
- Add complete English, Simplified Chinese, and French shape names.
- `Shift` keeps the existing square/circle constraint and applies the same equal-bounds constraint to the new bounded polygons.

## Verification

- Source-level contracts cover the invisible resize thumb, custom localized tooltip path, normalized Lucide toolbar, 3×3 shape catalog, and missing visual checkmark.
- Behavioral tests call the production outline builder and verify closed, bounded geometry for every new polygon.
- Run focused tests first, then the full test suite, build, i18n verifier, and diff check. A desktop screenshot is attempted only if the existing isolated editor smoke can run safely.
