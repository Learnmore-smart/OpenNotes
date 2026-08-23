# tools/verify-i18n.ps1
> Last updated: 2026-08-20 | Protection: STANDARD

## Purpose

Fail-closed static verification for application catalog completeness, localization call keys, placeholder parity, and visible-string migration.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** Keep the three app catalogs and all visible WPF strings synchronized.
- **Next steps:** Run after every localization or XAML copy change; runtime refresh of already-open windows remains a desktop check.

## Important Notes / NEVER Change

- Missing catalog keys and placeholder mismatches must fail the script; never render a key name as a fallback.
- `OpenNotes` is a proper visible brand; intentional `Caelum` compatibility identifiers must remain allow-listed.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Documented the i18n verification contract. | Codex |
