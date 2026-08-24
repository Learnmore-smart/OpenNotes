# OpenNotes.Tests/DocumentOperationSessionTests.cs

> Last updated: 2026-08-24 (Wave6 async stale-operation P2, audit continuation) | Protection: STANDARD

## Purpose

Deterministic, WPF-free TaskCompletionSource coverage for the shared document
operation lease. The tests model the real Version History, sidebar/page context,
and Undo/Redo await→reload interleavings and prove a stale continuation cannot
mutate state, while a same-session continuation completes exactly once.

## Planned cases

- Version History await resumes after a new load: silent no-op.
- Sidebar/page context await resumes after a new load: silent no-op.
- Async Undo/Redo crosses reload: old action is not moved between stacks or
  applied; same-session action applies once.
- Normalized path and model identity are part of validation.
- History/sidebar stale-error and dirty/undo publication are source-level
  continuation contracts; deterministic lease behavior remains covered by the
  TCS cases above.

## Verification

All five deterministic lease tests are green. The TCS cases cover Version
History await→reload, sidebar/page context await→reload, async Undo/Redo across
reload, same-session exactly-once success, and cancellation/model-identity
rejection. Companion source contracts cover final dirty/toast publication,
session-scoped thumbnail cleanup, stale PDF-search exceptions, and deferred
context-menu admission. No commit or Wave7+ change was made.
