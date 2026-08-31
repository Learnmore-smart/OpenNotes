# OpenNotes.Tests/EditorPopupAutomationTests.cs
> Last updated: 2026-08-23（Wave 3 P2 STA AutomationPeer contract complete） | Protection: STANDARD

## Purpose

Exercise the production `EditorPage` popup construction on an STA dispatcher without a foreground desktop session. The test inspects AutomationPeers for the highlighter slider/modes and selection shape/filter controls, including localized metadata, semantic toggle state, 32-DIP targets, and Invoke/Toggle activation.

The descendant walker follows realized visual children and falls back to a content control's logical content when a bounded popup `ScrollViewer` has not yet been visually realized offscreen.

## Open Threads / Resume Context

- **Status:** complete; the RED runtime-peer and STA theme-expression contracts were added before the popup implementation/cleanup and now pass in the full suite.
- **Constraints:** use the existing `EditorPage` constructor and minimal test `Application` resources; do not open a global transient-dismissal hook or alter Wave6 ownership.

## Important Notes / NEVER Change

- Keep `HiddenInkToolButton` compatibility ID and all production `Editor.*` IDs.
- The test must restore `LocalizationService` language and shut down/leave the test dispatcher cleanly.
- The test complements, rather than replaces, source contracts and foreground smoke evidence. It passed offscreen on STA and validated highlighter slider/modes plus selection shape/filter IDs, localized Name/HelpText, 32 DIP targets, `TogglePattern` state/activation, shared focus metadata, and dynamic Theme resource expressions for ruler, text font-group and color-indicator state. It does not add Wave6 global popup dismissal.

## Verification

- Focused `EditorToolbarVisualSourceTests|EditorPopupAutomationTests`: `22/22` passed.
- Full `OpenNotes.Tests`: `189/189` passed.
- 2026-08-24: the live ruler color contract now targets the shared `Shape` abstraction so the named `LucideIcon` renderer is covered without weakening dynamic-resource assertions.
