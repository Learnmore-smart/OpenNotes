# OpenNotes.Tests/AppSettingsCompatibilityTests.cs
> Last updated: 2026-08-23 (Wave 1 test-root quality follow-up GREEN) | Protection: STANDARD

## Purpose

Verify legacy `PenPresets` JSON survives deserialization, sanitization, clone/save, and reload while empty/missing lists remain empty until UI code explicitly chooses otherwise.

## Open Threads / Resume Context

- **Status:** complete for automated Wave 1 scope
- **Intent:** Lock compatibility for the three legacy preset entries and prove sanitization does not mutate the caller's list.
- **Next steps:** Preserve the per-operation data-root lookup in later settings changes; the external pointer/device smoke remains separate.

## Important Notes / NEVER Change

- Preserve the `AppSettings.PenPresets` JSON property and the `%LOCALAPPDATA%\\Caelum` default data root.
- Service sanitization/clone must deep-copy entries and must not insert UI defaults for an empty list.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Added the Wave 1 RED compatibility contract for legacy preset data. | Codex |
| 2026-08-23 | Production compatibility remained intact: 3 focused tests and the full 107-test suite passed; empty lists do not trigger UI writes. | Codex |
| 2026-08-23 | Added a per-operation `OPENNOTES_DATA_ROOT` switch regression; focused settings tests pass 4/4 and the full suite passes 113/113. | Codex |
