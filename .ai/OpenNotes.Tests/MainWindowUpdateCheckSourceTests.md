# OpenNotes.Tests/MainWindowUpdateCheckSourceTests.cs

> Last updated: 2026-09-01 | Protection: STANDARD

## Purpose

Source-level regression contract for More-menu ordering, update click wiring, localization refresh, busy-state restoration, close cancellation, and trusted browser launch.

## Important Notes / NEVER Change

- Preserve the stable MoreButton AutomationId and existing Settings/About handlers.
- Require a finally-based menu restoration and a second trusted-URI check at the process-launch boundary.
- Do not require live GitHub or browser interaction in automated tests.

## Open Threads / Resume Context

- **Status:** complete
- **Coverage:** command ordering/wiring, live localization, busy disable/restore, close cancellation, trusted URI revalidation, and shell execution.
- The source contract also keeps the busy-header conditional outside literal catalog lookups, matching the static i18n verifier.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-01 | Added the RED/GREEN MainWindow source contract; focused localization/UI run passes 11/11. | Codex |
| 2026-09-01 | Created before the MainWindow update-check RED contract. | Codex |
