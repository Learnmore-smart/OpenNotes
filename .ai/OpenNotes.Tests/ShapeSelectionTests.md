# OpenNotes.Tests/ShapeSelectionTests.cs

## v5.2.4 routed selection follow-up (2026-08-27) — IN PROGRESS

- Add realized WPF coverage for the real ScrollViewer → page overlay route. Existing tests manually call `InvokeSelectionMouse*Core` and therefore cannot detect duplicate or swallowed production routing.

> Last updated: 2026-08-26 | Protection: STANDARD

## Purpose

Protect page-local drawing selection geometry and Ctrl-toggle behavior through real WPF/production seams.

## Open Threads / Resume Context

- **Status:** complete
- **Intent:** protect the first click after Select popup dismissal, broad/open-stroke hits, and same-page Ctrl add/remove/empty-click behavior through real WPF/production seams.
- **Constraint:** do not enable cross-page selection accumulation.
- **Evidence:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeSelectionTests"` was RED at 3 failed / 4 passed before the first fix; the added routed first-post-dismissal gesture test was independently RED (2 failed / 8) with popup consumption restored; final focused GREEN is 8 passed / 0 failed. The new test invokes `EditorPage_PreviewMouseDown` and then a real `PdfPageControl` selection gesture, asserting the first outside click selects the open stroke.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-26 | Documented regression-test scope before implementation. | Codex |
| 2026-08-26 | Added popup, broad/open hit, and same-page Ctrl selection regressions; focused RED/GREEN closed 7/7. | Codex |
| 2026-08-26 | Added the routed first-post-dismissal gesture assertion; focused GREEN closed 8/8. | Codex |
| 2026-08-28 | Added delegated-state cancellation and stylus-capture contracts; focused selection suite is GREEN 10/10. | Codex |
