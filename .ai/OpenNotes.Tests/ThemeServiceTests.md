# OpenNotes.Tests/ThemeServiceTests.cs
> Last updated: 2026-08-24（Wave5 neutral palette/backdrop/review GREEN） | Protection: STANDARD

## Purpose

Verify theme normalization and application chrome resource tokens without requiring a WPF window, including HomePage file-tile states and editor ComboBox/runtime-popup contracts that must follow the shared palette.

## Open Threads / Resume Context

- **Status:** complete
- V5.1.1 updates the exact Light palette contract so window, canvas and desk resolve to `#FFFFFF`; alternate controls and borders retain their neutral contrast.
- Red-first contracts now cover the six material theme resources and their deliberate use by the shell, home, editor, settings and template picker.
- Resource-level coverage is complete; visual high-contrast, popup, and restart checks remain desktop/manual.
- The HomePage audit reads the source XAML and requires hover, selection, foreground and subtle-foreground states to use existing ThemeService resources rather than fixed light-only literals.
- The editor audit reads App.xaml and EditorPage sources and requires explicit ComboBox item styling, compact formatting ComboBoxes, theme-bound popup surfaces/foregrounds, popup z-order registration, a preview-key guard that leaves arrow keys to text resize handles, and stable runtime page AutomationIds for desktop regression tools.
- The review contract additionally checks that `ThemeService.GetAnimationDuration`/`ShouldAnimate` have production consumers, every declared semantic alias has a production DynamicResource/SetResourceReference consumer, Settings focus/disabled visuals are explicit, and system/explicit HighContrast refresh can be injected and unhooked deterministically.

## Important Notes / NEVER Change

- Theme application must not mutate PDF page bitmaps or annotation data.
- Wave5 tests must assert exact neutral Light values, backdrop normalization/roundtrip, HighContrast decoration override, semantic token coverage, and dynamic runtime resource expressions.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Documented theme resource tests. | Codex |
| 2026-08-21 | Added a regression contract for HomePage file-tile theme tokens. | Codex |
| 2026-08-22 | Added regression contracts for editor ComboBox and runtime-popup theme resources. | Codex |
| 2026-08-22 | Added a red regression contract for the EditorPage preview-key resize-handle routing bug. | Codex |
| 2026-08-22 | Added a red regression contract for stable runtime PdfPageControl AutomationIds used by real desktop smokes. | Codex |
| 2026-08-22 | Added a red regression contract for the text drag handle's stable AutomationId and localized accessible name. | Codex |
| 2026-08-22 | Added and satisfied the six paper/ink material palette assertions and primary desktop-surface source contract; full suite now passes 100/100. | Codex |
| 2026-08-24 | Wave5 RED/GREEN covers the approved neutral Light values, backdrop runtime choices, HighContrast fallback, semantic aliases and dynamic surface separation; focused theme/surface coverage is 16/16 and the full suite is 210/210. | Codex |
| 2026-08-24 | Review follow-up added `ThemeReviewContractTests`: RED-first motion/alias/Settings runtime/HC/PDF-composite contracts now pass 11/11; the test fixture resets ThemeService event hooks between STA cases. | Codex |
