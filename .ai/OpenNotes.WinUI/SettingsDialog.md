# OpenNotes.WinUI/SettingsDialog.cs
> Last updated: 2026-09-25 (V6 Task 9 Phase B — settings dialog port) | Protection: STANDARD

## Purpose
`public sealed class SettingsDialog : ContentDialog` (namespace `Caelum`) — the WinUI port of the WPF `SettingsWindow` borderless window. A borderless WPF window has no WinUI counterpart, so it is a `ContentDialog`; callers set `XamlRoot` and show it through `WinUiDialogService.RunUnderDialogGateAsync` (MainWindow `OpenSettingsDialogAsync`).

## Contract
- Ctor takes the CURRENT `AppSettings`; `_originalSettings = CloneSettings(current)` is the revert snapshot.
- `SelectedSettings` — populated only on the primary ("Save") button; `null` on Cancel/Esc/light-dismiss. The caller persists via `AppSettingsService.Save` (`MainWindow.ApplySettings`) and toasts `Main.SettingsSaved`.
- **Live preview:** every control change calls `MainWindow.Current?.PreviewSettings(GetSelectedSettings())` — the WPF owner-call parity. Language changes additionally run `LocalizationService.ApplyLanguage` so the whole shell re-localizes live.
- **Revert:** `Closed` handler calls `PreviewSettings(_originalSettings)` when `!_confirmed` — covers Cancel, the X button, Esc and light-dismiss (WPF could not be light-dismissed; the revert point is Closed because ContentDialogs can close without a button).
- **Re-localize/repaint while open:** `LocalizationService.LanguageChanged` → `ApplyLocalization()` (combo indices field-backed and re-stamped); `WinUiThemeService.ThemeApplied` → `_themeBindings` re-resolve (code-built UI can't use `{ThemeResource}`, so brushes are baked at build time and rebound via the `ThemeBinding` ledger). Both subscriptions detach in `Closed`.

## Controls staged
`LanguageComboBox` (AppLanguage options via `GetLanguageOptions`), `AutoSaveIntervalComboBox` (15/30/60/120), `PressureCheckBox`, `PenOnlyCheckBox`, `SmoothingComboBox` (0–3), `ThemeComboBox` (Light/Dark/System/HighContrast), `PerformanceModeComboBox` (BatterySaver/Balanced/BestQuality via `PdfRenderPolicy`), `WorkspaceBackdropComboBox` (six backdrops + literal preview swatch — WinUI's selection box can't host per-item swatches, so the swatch tracks the current pick). AutomationIds mirror the WPF `x:Name`s.

## Important Notes / NEVER Change
- `CloneSettings` deep-copies the recent-color lists and `PenPresets` (new `PenPreset` instances) and carries every field the dialog doesn't expose (`WholeStrokeEraser`, `InkSimulation`, `ShapeRecognition`, `DefaultPenColorHex`, `DefaultPenSize`, …) — a staged edit must never leak into or drop parts of the persisted model. Pinned by `WinUiDialogsServicesSourceTests`.
- `_isApplyingLocalization` guards SelectionChanged/Toggled during bulk rebind so rebuilding combo ItemsSources doesn't fire a phantom preview.
- `_opened` gates previews until `Opened` fires — initial `RestoreControlState` must not live-preview (WPF loaded the window with values already set).
- `GetSelectedSettings` sanitizes each control (`Math.Clamp`-style index guards, `AppLanguage.English` fallback) — never trust combo state.

## Open Threads / Resume Context
- **Status:** GREEN — builds 0 err/0 warn; source-pinned by `WinUiDialogsServicesSourceTests.SettingsDialogStagesPreviewsAndRevertsOnDismiss`.

## Change History
- 2026-09-25 Task 9 Phase B: initial WinUI port (WPF `SettingsWindow.xaml(.cs)` parity). | Devin
