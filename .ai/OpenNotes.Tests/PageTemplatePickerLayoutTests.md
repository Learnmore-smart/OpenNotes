# PageTemplatePickerLayoutTests.cs

> Last updated: 2026-08-24 | Protection: STANDARD

## Purpose

Source-level regression contract for the notebook/page template picker layout and complete template mapping.

## Open Threads / Resume Context

- **Status:** GREEN.
- Requires a wider resizable window, fixed header/footer, an independently two-axis scrollable gallery, a 3×3 card grid, and nine enum-backed cards including Checklist and TwoColumn.

## Important Notes / NEVER Change

- Do not test pixels or localized copy here; catalog completeness and PDF output have their own tests.
- Preserve all existing card names and click handlers.
