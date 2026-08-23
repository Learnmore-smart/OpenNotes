# OpenNotes.Tests/AreaHighlightTests.cs

> Last updated: 2026-08-19 | Protection: STANDARD

## Purpose

Pure geometry regression tests for the Task 27 area-highlight drag path.

## Open Threads / Resume Context

- **Status:** complete
- **Intent:** lock the expected normalized rectangle behavior and keep the WPF drag implementation deterministic.
- **Next steps:** none required for V5.

## Important Notes / NEVER Change

- Tests must remain independent of a running WPF window; only deterministic geometry is asserted here.
