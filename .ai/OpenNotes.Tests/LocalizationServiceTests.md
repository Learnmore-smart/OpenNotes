# OpenNotes.Tests/LocalizationServiceTests.cs
> Last updated: 2026-08-27 (cross-platform source-contract normalization) | Protection: STANDARD

## Purpose

Test translation catalog completeness, placeholder parity, and language-change notifications without requiring a WPF window.

## Open Threads / Resume Context

- 2026-08-31: exact EN/ZH/FR coverage now targets line-style, Solid, and Dashed after the dashed geometry tile was removed.
- **Status:** complete for Wave 3 source/test scope — catalog remains three-language complete after obsolete Fit/Ink Analysis keys are removed.
- Add tests before changing catalog behavior; all three languages must have non-empty values for every key.
- Performance-mode tests require all four catalog keys and verify the settings UI exposes and preserves the new field.
- The page subscription regression contract checks that HomePage and EditorPage wire `LanguageChanged` through their Loaded/Unloaded lifecycle and keep `ApplyLocalization()` as the refresh path; it passes with the full suite.
- Multiline source contracts normalize CRLF to LF before matching so a Windows checkout does not produce false failures.
- Wave 3 focused tests also verify that removed visible commands have no catalog/source references and that refreshed toolbar metadata remains localized.
- `ToolbarObsoleteEntriesAreNotExposedByAnyLanguageCatalog` asserts the removed Fit Width/Fit Page and Ink Analysis-unavailable keys stay absent from the shared catalog.
