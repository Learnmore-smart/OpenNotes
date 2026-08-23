# OpenNotes.Tests/PerformanceLifecycleSourceTests.cs

> Last updated: 2026-08-21 | Protection: STANDARD

## Purpose

Regression contracts for WPF performance lifecycle wiring that cannot be exercised reliably in headless unit tests.

## Open Threads / Resume Context

- **Status:** green (3 contracts).
- Verifies page suspension/direct first render/scaling-mode API, editor working-set/LRU/profile/restartable-timer wiring, shell activation/navigation/minimize coordination, and awaited cleanup on both tab and window close. Runtime WPF coverage is supplemented by the isolated Release UI automation smoke.
