# OpenNotes.Tests/MainWindowTabChromeSourceTests.cs

> Last updated: 2026-09-08 | Protection: STANDARD

## Purpose

Source contracts for tab chrome hit-testing: inactive-tab close must not depend on a rebuilt Click handler, and close must still work after inking when WPF stops promoting stylus to mouse.

## Open Threads / Resume Context

- **Status:** complete
- **Intent:** Tab X handles PreviewStylusDown, disables press-and-hold, CloseTab releases pointer captures, and PrepareForClose is WaitAsync-bounded.

## Important Notes / NEVER Change

- Keep `PreviewMouseLeftButtonDown` on the close button; stylus handling is additive.
