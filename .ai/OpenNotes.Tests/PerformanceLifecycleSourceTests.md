# OpenNotes.Tests/PerformanceLifecycleSourceTests.cs

> Last updated: 2026-08-23（Wave 2 final review） | Protection: STANDARD

## Purpose

Regression contracts for WPF performance lifecycle wiring that cannot be exercised reliably in headless unit tests.

## Open Threads / Resume Context

- **Status:** green (4 contracts).
- Verifies page suspension/direct first render/scaling-mode API, editor working-set/LRU/profile/restartable-timer wiring, whole-editor `IsEnabled` admission plus inline/Sticky Note Popup commit, shell activation/navigation/minimize coordination (including active-frame/window restore re-entry), navigation-frame admission re-entry, queued-input Dispatcher barrier, tracked Frame-journal editor cleanup, awaited cleanup on both tab and window close, bounded/retryable WPF close/navigation preparation, post-cleanup release boundary and non-resumable retry failure, tab-close busy guards, synchronous `OnClosing`, timeout handoff that retains guards until release settlement, and Dispatcher-marshaled retry snapshot collection/coordinator reopening. Executable generation/admission/close semantics live in `DocumentSaveCoordinatorTests` and `EditorTextSessionTests`; this source guard only protects event wiring. Runtime WPF coverage is supplemented by the isolated smoke when foreground ownership allows it.
