# OpenNotes.Tests/ShapeToolTests.cs

> Last updated: 2026-08-24 | Protection: STANDARD

## Purpose

Behavioral regression coverage for the editor's production shape outline generator.

## Open Threads / Resume Context

- **Status:** GREEN.
- **Result:** reflection-based production tests verify Triangle, Diamond, Parallelogram, Pentagon, and Hexagon are closed, bounded outlines produced by `PdfPageControl.BuildShapeOutline`, not menu-only placeholders. Triangle apex and parallelogram slant geometry receive additional assertions.
- **Evidence:** the fixture passes 6/6; the focused toolbar/shape filter passes 29/29 and the full suite passes 277/277.

## Important Notes / NEVER Change

- Exercise the production helper rather than duplicating polygon math in the test.
- Shape commits must remain ordinary ink strokes so existing undo and persistence behavior is inherited.
