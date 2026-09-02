# OpenNotes.Tests/ProductInfoTests.cs
> 2026-09-02 GREEN: the 5.2.9 visible-version contract failed against stale 5.2.8 production metadata, then passed 2/2 after alignment; compatibility assertions remain unchanged.
> 2026-08-31 GREEN: the 5.2.8 visible-version contract failed against stale 5.2.7 production metadata, then passed 2/2 after alignment; compatibility assertions remain unchanged.
> 2026-08-31 GREEN: the 5.2.7 visible-version contract failed against stale 5.2.6 production metadata, then passed 2/2 after alignment; compatibility assertions remain unchanged.
> 2026-08-30 GREEN: the visible product-version expectation is 5.2.6; all legacy compatibility assertions remain unchanged.
> 2026-08-28 GREEN: visible-version assertion is 5.2.4 for the selection/text/ruler regression release; the stale 5.2.3 value failed first and compatibility assertions remain unchanged.
> Last updated: 2026-08-24（5.0.0 release metadata coverage） | Protection: STANDARD

## Purpose

Verify the visible OpenNotes brand while protecting the Caelum data-directory and WindowsNotesApp identity compatibility values.

## Open Threads / Resume Context

- **Status:** GREEN (5.2.3 editor regression-fix release)
- The focused ProductInfoTests run was RED against ProductInfo.Version 5.2.2 after changing the expectation, then GREEN after production metadata advanced to 5.2.3; all OpenNotes/Caelum/WindowsNotesApp compatibility assertions remain unchanged.
- **Status:** GREEN (5.2.2 navigation layout patch)
- The visible-version assertion expects `5.2.2`; all compatibility assertions remain unchanged.
- **Status:** complete (5.2.1 hotfix release)
- Advance only the visible-version contract to `5.2.1`; keep every compatibility assertion.
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
| 2026-09-02 | Advanced the visible-version contract to 5.2.9 with observed RED/GREEN coverage and unchanged compatibility assertions. | Codex |
| 2026-08-31 | Advanced the visible-version contract to 5.2.8 with observed RED/GREEN coverage and unchanged compatibility assertions. | Codex |
| 2026-08-31 | Advanced the visible-version contract to 5.2.7 with observed RED/GREEN coverage and unchanged compatibility assertions. | Codex |
| 2026-08-26 | Advanced the visible-version contract to 5.2.3 with verified RED/GREEN coverage and unchanged compatibility assertions. | Codex |
| 2026-08-25 | Advanced the RED visible-version assertion to 5.2.2 without changing compatibility contracts. | Codex |
| 2026-08-25 | Updated the visible-version assertion to 5.2.1 for the large-PDF crash hotfix. | Codex |
| 2026-08-24 | Updated the visible-version assertion to 5.2.0 without changing compatibility contracts. | Codex |
| 2026-08-24 | Updated the visible-version assertion to 5.1.0 without changing compatibility contracts. | Codex |
| 2026-08-24 | Added the 5.0.0 visible-version assertion for the release. | Codex |
| 2026-08-20 | Documented branding compatibility tests. | Codex |
| 2026-08-21 | Added default-path and isolated-root resolver coverage. | Codex |
