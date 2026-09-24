# OpenNotes.Core/Ink/LaserInkFade.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Pure fade-timing math for the ephemeral laser tool (`Caelum.Ink`), Task 7
Phase B — ported from the WPF laser polyline animation. Laser ink is
presentation-only: it lives on `LaserInkCanvas`, never enters
`InkStrokeStore`, is never persisted and never pushes an undo action.

## API surface (all static)

- Constants — `HoldSeconds = 0.15` (full-opacity hold after pointer-up),
  `FadeSeconds = 0.9` (linear fade duration), `MaxLivePolylines = 60`
  (oldest polylines dropped past the cap), `ColorR/G/B` = `FF/3B/30`
  (WPF laser red), `StrokeThickness = 3.0`.
- `GetOpacity(elapsedSeconds, animate)` → `1.0` during the hold, then
  `1 - (elapsed - hold) / fade` clamped to [0,1]; `animate=false`
  (reduced motion) returns `0.0` immediately — WPF's zero-duration path.
- `IsExpired(elapsedSeconds, animate)` → `true` at/after
  `hold + fade`, or immediately when `animate=false`.

## Constraints / NEVER Change

- The timings are WPF animation-parity numbers — do not retune without
  re-measuring the WPF `DoubleAnimation` storyboard.
- No timers here — the host's `DispatcherQueueTimer` (~30 ms) polls
  `GetOpacity`/`IsExpired` per live polyline and removes expired
  children. The math is UI-free so tests can pin the curve.
- `animate=false` must hard-expire, not freeze at full opacity.
