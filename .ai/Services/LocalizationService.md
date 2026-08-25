# Services/LocalizationService.cs
> Last updated: 2026-08-24（Wave4 review-follow-up + Wave5 backdrop labels verified; parent Wave4 bookkeeping remains in progress） | Protection: STANDARD

> 2026-08-25 GREEN: removed `Editor.SidebarResize` with the retired resize affordance; i18n passes 293 catalog entries / 480 calls / 0 hard-coded visible strings.

## Purpose

Central catalog for English, Simplified Chinese, and French UI strings and culture switching.

## Open Threads / Resume Context
- **Complete:** Triangle, Diamond, Parallelogram, Pentagon, and Hexagon have EN/ZH/FR names in the editor shape picker. Literal lookup calls preserve the static i18n audit; live popup rebuild behavior remains unchanged.
- Checklist and Two-column cards have complete placeholder-free EN/ZH/FR title and hint keys.
- **Wave4 dependency:** baseline keys are present, but review follow-up remains open for localized resize Range/HelpText, collapsed Expand/Collapse labels and bookmark Toggle names/status across all three catalog languages with placeholder parity preserved.
- **Status:** Wave 4 catalog follow-up is in_progress; model/JSON compatibility remains preserved.
- **Intent/result:** removed only localization keys belonging to removed visible preset/Fit/Ink Analysis UI while retaining `PenPresets` settings data and serialization compatibility. Added localized alignment labels for the runtime `TextAlignmentOption` model and verified live refresh preserves the enum selection.

- **Status:** performance settings and Wave 3 toolbar localization complete.
- `LanguageChanged`, read-only catalog access, three-language catalog, placeholder parity, and complete keys for dynamic WPF UI text are implemented.
- The performance label, Battery saver/Balanced/Best quality choices, and the text-box drag-handle accessible name are present in English, Simplified Chinese and French with identical key and placeholder sets.
- `tools/verify-i18n.ps1` reports 279 catalog entries, 459 localization calls, and 0 hard-coded visible strings; its dynamic `ItemsSource` audit rejects the former literal Left/Center/Right alignment array and missing keys still fail loudly.

## Important Notes / NEVER Change

- Keep `AppLanguage` and the existing culture mappings.
- Preserve placeholder parity across all three translations.
- Product name `OpenNotes` is a proper name; internal compatibility references to `Caelum` remain intentional.

## Verification status

- `OpenNotes.Tests` includes catalog completeness, placeholder parity, language-change notification, and missing-key failure tests.
- Website static verification reports 113/113 keys for each of en/zh/fr; the app catalog includes the immersive-mode, Hidden Ink, PenOnly and demo undo labels used by the new UI.
- MainWindow, SettingsWindow, PageTemplatePickerWindow, HomePage and EditorPage refresh through `LanguageChanged`; page subscriptions are lifecycle-bound to avoid retaining unloaded tabs. The current synchronization pass also removes duplicate same-language notifications and covers already-created dynamic controls. Visual text rendering remains a desktop smoke check.
- `Editor.MoveTextBox` is used as the localized UI Automation name for the runtime text drag handle; its stable `TextAnnotationDragHandle` id is separate from the visible text box content.
- Wave 3 removes the obsolete `Editor.InkAnalysisUnavailable`, `Editor.InkAnalysisTooltip`, `Editor.FitWidthTooltip` and `Editor.FitPageTooltip` catalog entries together with their UI entry points. Existing `PenPresets` copy remains compatibility data only and is not reset. Shape/highlighter popup labels remain three-language localized.
- P2 adds three localized `Editor.Alignment*` labels for the runtime ComboBox model; the stored `TextAlignment` enum remains the value, so language refresh cannot change the current selection.
- Wave 4 adds localized `Editor.PageJumpInvalid`, `Editor.PageJumpOutOfRange`, `Editor.SidebarNoBookmarks`, `Editor.SidebarSelected` and `Editor.SidebarResize` strings for keyboard validation, empty states and rail accessibility metadata.
- Wave4 review follow-up also localizes toolbar horizontal-scroll overflow, sidebar resize/collapse Expand/Collapse labels and bookmark Add/Remove status. `ApplyLocalization()` reapplies these labels to realized PageJump/sidebar controls after EN/ZH/FR changes; range/validation HelpText stays localized. The catalog verifier remains green at 279/459/0 after Wave5 backdrop labels.
- Wave5 adds complete EN/ZH/FR labels and hint text for `Settings.WorkspaceBackdrop` plus Neutral/Paper/Slate choices; all three share the same key set and are refreshed with SettingsWindow language preview.
- 2026-08-24 UI refresh adds EN/ZH/FR Previous/Next page labels and expands workspace labels to White/Paper/Mist/Warm/Slate/Midnight; missing keys still fail catalog verification.
- 2026-08-24 Sticky editor refresh adds the EN/ZH/FR `Editor.MoveStickyNoteEditor` drag instruction; the visible header continues to reuse the localized Sticky Note tool label.
