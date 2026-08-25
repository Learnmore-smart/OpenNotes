# OpenNotes.Tests/ProductInfoTests.cs
> Last updated: 2026-08-24（5.0.0 release metadata coverage） | Protection: STANDARD

## Purpose

Verify the visible OpenNotes brand while protecting the Caelum data-directory and WindowsNotesApp identity compatibility values.

## Open Threads / Resume Context

- **Status:** complete (5.2.0 release)
- The visible-version assertion is `5.2.0`; every OpenNotes/Caelum/WindowsNotesApp compatibility contract remains intact.
- **Status:** complete (5.1.0 release)
- The initial full run failed only because the visible-version contract still expected 5.0.0. The assertion now expects 5.1.0 while all OpenNotes/Caelum/WindowsNotesApp compatibility checks remain; the rerun passes 259/259.
- **Status:** ready_for_next
- Product branding coverage now asserts the visible `5.0.0` version alongside the OpenNotes/Caelum compatibility values; installer/AppX and live UI checks remain static/manual integration checks. `DataDirectoryUsesLegacyPathByDefaultAndOptInOverrideForIsolatedRuns` covers the opt-in `OPENNOTES_DATA_ROOT` resolver while asserting the default compatibility path.

## Important Notes / NEVER Change

- Do not assert a namespace or data-directory rename; those are deliberate compatibility exceptions.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-24 | Updated the visible-version assertion to 5.2.0 without changing compatibility contracts. | Codex |
| 2026-08-24 | Updated the visible-version assertion to 5.1.0 without changing compatibility contracts. | Codex |
| 2026-08-24 | Added the 5.0.0 visible-version assertion for the release. | Codex |
| 2026-08-20 | Documented branding compatibility tests. | Codex |
| 2026-08-21 | Added default-path and isolated-root resolver coverage. | Codex |
