# tools/verify-i18n.ps1
> Last updated: 2026-08-23（Wave 3 P2 dynamic ItemsSource audit complete） | Protection: STANDARD

## Purpose

Fail-closed static verification for application catalog completeness, localization call keys, placeholder parity, and visible-string migration.

## Open Threads / Resume Context

- **Status:** Wave 3 P2 follow-up complete for the static verifier.
- **Intent/result:** Keep the three app catalogs and all visible WPF strings synchronized. The verifier now rejects the former literal alignment `ItemsSource` array and the production model resolves labels through `LocalizationService`; runtime refresh of an already-open ComboBox is covered by the STA/UIA contract.
- **Evidence:** `tools/verify-i18n.ps1` passed with `268` catalog entries, `420` localization calls, `0` hard-coded visible strings, and no dynamic ItemsSource issues.

## Important Notes / NEVER Change

- Missing catalog keys and placeholder mismatches must fail the script; never render a key name as a fallback.
- `OpenNotes` is a proper visible brand; intentional `Caelum` compatibility identifiers must remain allow-listed.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Documented the i18n verification contract. | Codex |
