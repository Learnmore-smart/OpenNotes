# OpenNotes.WinUI/App.xaml.cs
> Last updated: 2026-09-23 (V6 Task 4 Steps 2–5) | Protection: STANDARD

## Purpose
WinUI 3 application code-behind (`Caelum.App : Microsoft.UI.Xaml.Application`). `OnLaunched` creates and activates `MainWindow` — that is all. Process entry point is the XAML-generated `Main` (`Application.Start` → `new App()`); no custom `Program.cs` and no `DISABLE_XAML_GENERATED_MAIN` yet.

## Open Threads / Resume Context
- **Status:** unchanged and correct as of the chrome/tab step. No `Bootstrap.Initialize` call — `WindowsAppSDKSelfContained` makes launch work unpackaged (verified by repeated launch smokes).
- Theme/localization startup happens in `MainWindow`'s ctor (settings load → `WinUiThemeService.Apply` → `LocalizationService.ApplyLanguage`), mirroring where the WPF app does it; keep it out of `App` unless a second window type needs it earlier.
