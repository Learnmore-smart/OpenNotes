# OpenNotes.Tests/LocalizationServiceTests.cs
> Last updated: 2026-08-21（open-page i18n subscription regression） | Protection: STANDARD

## Purpose

Test translation catalog completeness, placeholder parity, and language-change notifications without requiring a WPF window.

## Open Threads / Resume Context

- **Status:** performance settings localization tests complete.
- Add tests before changing catalog behavior; all three languages must have non-empty values for every key.
- Performance-mode tests require all four catalog keys and verify the settings UI exposes and preserves the new field.
- The page subscription regression contract checks that HomePage and EditorPage wire `LanguageChanged` through their Loaded/Unloaded lifecycle and keep `ApplyLocalization()` as the refresh path; it passes with the full suite.
