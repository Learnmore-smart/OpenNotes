# OpenNotes.Tests/LocalizationServiceTests.cs
> Last updated: 2026-09-25 (file/class name alignment — class moved out of UnitTest1.cs) | Protection: STANDARD

## Purpose

Focused `LocalizationService` behavior checks (originally `UnitTest1.cs`):
`ApplyLanguage` updates the current culture and French strings;
`Format` renders parameterized values (Chinese page label). A `[TearDown]`
restores English so the fixture is order-independent.

## Open Threads / Resume Context

- Renamed from `UnitTest1.cs` in the v6 review-residuals pass so the file
  name matches the hosted class; the catalog-completeness fixture that used
  to live in `LocalizationServiceTests.cs` is `LocalizationCoverageTests`
  in `LocalizationCoverageTests.cs`.
