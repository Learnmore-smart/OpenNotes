# OpenNotes.WinUI/App.xaml.cs
> Last updated: 2026-09-22 (V6 Task 4 Step 1) | Protection: STANDARD

## Purpose
WinUI 3 application code-behind (`Caelum.App : Microsoft.UI.Xaml.Application`). `OnLaunched` creates and activates `MainWindow` — that is all. Process entry point is the XAML-generated `Main` (`Application.Start` → `new App()`); no custom `Program.cs` and no `DISABLE_XAML_GENERATED_MAIN` yet.

## Open Threads / Resume Context
- **Status:** complete for the empty-window step. No `Bootstrap.Initialize` call — `WindowsAppSDKSelfContained` makes launch work unpackaged (verified by 8s launch smoke).
- Later steps add theme, tab shell (`_tabs`/`AppTab` model) and page `Frame` navigation here.
