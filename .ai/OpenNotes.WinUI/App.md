# OpenNotes.WinUI/App.xaml.cs
> Last updated: 2026-09-26 (T14-A — crash journaling hooks) | Protection: STANDARD

## Purpose
WinUI 3 application code-behind (`Caelum.App : Microsoft.UI.Xaml.Application`). `OnLaunched` creates and activates `MainWindow`. Process entry point is the XAML-generated `Main` (`Application.Start` → `new App()`); no custom `Program.cs` and no `DISABLE_XAML_GENERATED_MAIN` yet.

## Behaviour
- **Crash hooks (T14-A):** the ctor subscribes `Application.UnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` BEFORE `InitializeComponent` — even a XAML/xbf load failure leaves a managed stack. All three route to `Caelum.Services.CrashLogger` (`%LOCALAPPDATA%\Caelum\logs\crash-*.log`).
- **Log only, never swallow:** `e.Handled = false` on the XAML event, no `SetObserved()` on unobserved task exceptions — WER still sees the crash; we only gain the stack. Added after the v6.0.3 field crash (0xC000027B/E_INVALIDARG while inking) shipped with no managed stack.

## Open Threads / Resume Context
- **Status:** unchanged and correct as of the chrome/tab step apart from the T14-A hooks. No `Bootstrap.Initialize` call — `WindowsAppSDKSelfContained` makes launch work unpackaged (verified by repeated launch smokes).
- Theme/localization startup happens in `MainWindow`'s ctor (settings load → `WinUiThemeService.Apply` → `LocalizationService.ApplyLanguage`), mirroring where the WPF app does it; keep it out of `App` unless a second window type needs it earlier.
- Existing "no App.UnhandledException backstop" comments in MainWindow/EditorPage remain accurate — the handler logs but deliberately does NOT mark exceptions handled, so local try/catch discipline is still required.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-26 | T14-A: hooked the three unhandled-exception sources into `CrashLogger` (log-only, `Handled=false`, no `SetObserved`). | Devin |
