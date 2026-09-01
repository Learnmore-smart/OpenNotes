# MainWindow.xaml

> Last updated: 2026-08-24 | Protection: STANDARD

## Purpose

Defines the custom OpenNotes window chrome, tab strip, global commands, frame host and toast surface.

## Open Threads / Resume Context

- **2026-09-01 complete:** `CheckForUpdatesMenuItem` sits between Settings and About without changing `MoreButton` AutomationId or popup ownership.
- **2026-09-01:** Insert the localized `CheckForUpdatesMenuItem` between Settings and About without changing `MoreButton` AutomationId or popup ownership.
- **Status:** complete (5.1.2)
- Every visible shell glyph now uses `controls:LucideIcon`; dynamic tab and toast identifiers are resolved through the same Lucide geometry library.
- **2026-08-30:** tab-strip drop routing supports process-wide docking; `ShowInTaskbar` is explicit so detached windows remain independently taskbar-visible, while the stable `OpenNotes` title remains unchanged.

## Important Notes / NEVER Change

- Preserve custom window drag/maximize/minimize/close handlers and tab bindings.
- Icons are decorative; accessible names remain on owning controls.
