# OpenNotes.Tests/ThemeServiceTests.cs
> Last updated: 2026-08-22（paper/ink material and surface contracts） | Protection: STANDARD

## Purpose

Verify theme normalization and application chrome resource tokens without requiring a WPF window, including HomePage file-tile states and editor ComboBox/runtime-popup contracts that must follow the shared palette.

## Open Threads / Resume Context

- **Status:** complete
- Red-first contracts now cover the six material theme resources and their deliberate use by the shell, home, editor, settings and template picker.
- Resource-level coverage is complete; visual high-contrast, popup, and restart checks remain desktop/manual.
- The HomePage audit reads the source XAML and requires hover, selection, foreground and subtle-foreground states to use existing ThemeService resources rather than fixed light-only literals.
- The editor audit reads App.xaml and EditorPage sources and requires explicit ComboBox item styling, compact formatting ComboBoxes, theme-bound popup surfaces/foregrounds, popup z-order registration, a preview-key guard that leaves arrow keys to text resize handles, and stable runtime page AutomationIds for desktop regression tools.

## Important Notes / NEVER Change

- Theme application must not mutate PDF page bitmaps or annotation data.

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
