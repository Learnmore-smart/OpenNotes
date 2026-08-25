# MainWindow.xaml

> Last updated: 2026-08-24 | Protection: STANDARD

## Purpose

Defines the custom OpenNotes window chrome, tab strip, global commands, frame host and toast surface.

## Open Threads / Resume Context

- **Status:** complete (5.1.2)
- Every visible shell glyph now uses `controls:LucideIcon`; dynamic tab and toast identifiers are resolved through the same Lucide geometry library.

## Important Notes / NEVER Change

- Preserve custom window drag/maximize/minimize/close handlers and tab bindings.
- Icons are decorative; accessible names remain on owning controls.
