# Services/LocalizationService.cs
> Last updated: 2026-08-21（full catalog and runtime-refresh audit） | Protection: STANDARD

## Purpose

Central catalog for English, Simplified Chinese, and French UI strings and culture switching.

## Open Threads / Resume Context

- **Status:** performance settings localization complete.
- `LanguageChanged`, read-only catalog access, three-language catalog, placeholder parity, and complete keys for dynamic WPF UI text are implemented.
- The performance label, Battery saver/Balanced/Best quality choices, and the text-box drag-handle accessible name are present in English, Simplified Chinese and French with identical key and placeholder sets.
- `tools/verify-i18n.ps1` reports 272 catalog entries, 385 localization calls, and 0 hard-coded visible strings; missing keys still fail loudly.

## Important Notes / NEVER Change

- Keep `AppLanguage` and the existing culture mappings.
- Preserve placeholder parity across all three translations.
- Product name `OpenNotes` is a proper name; internal compatibility references to `Caelum` remain intentional.

## Verification status

- `OpenNotes.Tests` includes catalog completeness, placeholder parity, language-change notification, and missing-key failure tests.
- Website static verification reports 113/113 keys for each of en/zh/fr; the app catalog includes the immersive-mode, Hidden Ink, PenOnly and demo undo labels used by the new UI.
- MainWindow, SettingsWindow, PageTemplatePickerWindow, HomePage and EditorPage refresh through `LanguageChanged`; page subscriptions are lifecycle-bound to avoid retaining unloaded tabs. The current synchronization pass also removes duplicate same-language notifications and covers already-created dynamic controls. Visual text rendering remains a desktop smoke check.
- `Editor.MoveTextBox` is used as the localized UI Automation name for the runtime text drag handle; its stable `TextAnnotationDragHandle` id is separate from the visible text box content.
