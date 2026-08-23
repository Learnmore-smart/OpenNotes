# Models/HiddenInkRevealState.cs
> Last updated: 2026-08-20 | Protection: STANDARD

## Purpose

Pure, WPF-free timing rules for Hidden Ink reveal windows. Keeping the deadline logic separate from `DispatcherTimer` makes the boundary behavior deterministic and unit-testable.

## API

- `DefaultRevealDurationMs`: 3000 milliseconds.
- `GetRevealUntil(revealedAt, duration)`: adds a positive custom duration, or falls back to the three-second default for missing/non-positive values.
- `IsRevealed(now, revealedUntil)`: returns true only before the deadline; at the exact deadline the mask is hidden again.

## Constraints

- This class does not store document state and does not persist temporary reveal state.
- `PdfPageControl` owns the visual timer; this class owns only deterministic timing rules.

## Verification

`OpenNotes.Tests/HiddenInkTests.cs` covers the default duration, custom duration, missing/non-positive fallback, and exact-deadline behavior.
