# OpenNotes.Tests/ProductInfoTests.cs
> Last updated: 2026-08-21（data-root override coverage） | Protection: STANDARD

## Purpose

Verify the visible OpenNotes brand while protecting the Caelum data-directory and WindowsNotesApp identity compatibility values.

## Open Threads / Resume Context

- **Status:** ready_for_next
- Product branding coverage is complete at the pure-value level; installer/AppX and live UI checks remain static/manual integration checks. `DataDirectoryUsesLegacyPathByDefaultAndOptInOverrideForIsolatedRuns` now covers the opt-in `OPENNOTES_DATA_ROOT` resolver while asserting the default compatibility path.

## Important Notes / NEVER Change

- Do not assert a namespace or data-directory rename; those are deliberate compatibility exceptions.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Documented branding compatibility tests. | Codex |
| 2026-08-21 | Added default-path and isolated-root resolver coverage. | Codex |
