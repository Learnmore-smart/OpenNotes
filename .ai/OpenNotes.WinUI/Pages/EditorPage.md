# OpenNotes.WinUI/Pages/EditorPage.xaml(.cs)
> Last updated: 2026-09-23 (V6 Task 5 — editor navigation stub) | Protection: STANDARD

## Purpose
`Caelum.Pages.EditorPage : Page` — Task 5 stub standing at the end of the HomePage → editor navigation path (WPF `NavigateActiveTabToFile` parity). Proves tab retitle/repath and shell toolbar switching end to end; the real editor surface lands with the Task 6 port.

## What It Does
- `OnNavigatedTo` accepts a `string` path (or `NavigationEventArgs.Parameter`-equivalent), stores `CurrentPdfPath`, and renders `EditorTitleTextBlock` (file name) + `EditorPathTextBlock` (full path) on `ThemeWorkspaceBrush` with a document glyph.
- The stub notice `EditorStubNoticeTextBlock` is localized via the `Editor.StubNotice` catalog key (EN/ZH/FR) set in the ctor — no literal UI strings in the XAML (spec-review fix).
- `UpdateCurrentPdfPath(newPath)` retitles in place — called by `MainWindow.HandleFilePathChanged` when a library rename moves an open file.
- AutomationIds `EditorPageTitle`/`EditorPagePath` let `tools/winui-home-smoke.ps1` verify navigation + path display.

## Open Threads / Resume Context
- **Status:** stub by design — do NOT port editor content here before Task 6; the mirror for the real port lives in that task's scope.
- `MainWindow` treats any non-`HomePage` frame content as "editor" for toolbar visibility — keep `EditorPage` the only file-page type for now.
