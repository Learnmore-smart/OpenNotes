# OpenNotes.Tests/ShapeSelectionTests.cs

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
