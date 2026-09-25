# OpenNotes.Tests/WinUiLocalizationCoverageTests.cs
> Last updated: 2026-09-25 (V6 Task 9 Phase B — WinUI i18n coverage) | Protection: STANDARD

## Purpose
Headless localization coverage for the WinUI shell: scans every
`OpenNotes.WinUI/**/*.cs` file (excluding `bin`/`obj`) for literal
`LocalizationService.Get("Key")`/`Format("Key")`/`GetForLanguage("Key")`
call sites, then asserts each referenced key exists in
`LocalizationService.GetCatalog()` and resolves non-empty in English,
Chinese and French. This is the complement to `LocalizationCoverageTests`
(which proves every catalog ENTRY carries three translations) — this test
proves no WinUI call site names a MISSING key.

Catches drift like the pre-existing `Editor.ColorTooltip` fallback key that
was referenced by `EditorPage` but absent from the catalog until T9-B.

## Change History
- 2026-09-25 Task 9 Phase B: initial test. | Devin
