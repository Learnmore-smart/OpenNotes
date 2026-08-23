# SettingsWindow
> Last updated: 2026-08-22（paper-led settings layout completion）| Protection: STANDARD

## Purpose
Modal editor for persisted application settings. It previews language/theme and utility changes, then returns a complete `AppSettings` snapshot to MainWindow.

## What It Does

- Initializes language, auto-save interval, pressure, PenOnly, smoothing, default pen color/size, performance mode and Light/Dark/System/HighContrast theme controls from the incoming settings.
- `ApplyLocalization()` refreshes labels, checkbox content, combo-box item text and title without losing the current selections.
- `GetSelectedSettings()` starts from a deep clone of the original snapshot, then applies current control values; unrelated fields such as presets and recent colors are preserved.
- Language and utility changes preview immediately through MainWindow; Cancel/close restores the original snapshot, while Save returns the selected snapshot.
- A language selection publishes one change notification only; SettingsWindow refreshes itself from that notification and MainWindow owns the outer-window refresh, avoiding a second explicit refresh of the same controls.
- All five ComboBoxes (`LanguageComboBox`, `AutoSaveIntervalComboBox`, `SmoothingComboBox`, `PerformanceModeComboBox`, `ThemeComboBox`) register `PopupZOrderHelper.FixComboBoxPopupTopmost` after `InitializeComponent`, before item sources are populated.

## Constraints

- Preserve every `AppSettings` field when saving; do not construct a language-only object.
- Preview changes must be reversible on cancel.
- ComboBox popups use `PopupZOrderHelper`; do not duplicate Win32 interop here.
- Visible labels and checkbox content come from `LocalizationService`.

## Open Threads / Resume Context

- **Status:** complete.
- The localized performance-mode ComboBox follows the utility-row, preview/cancel, complete-clone, and PopupZOrderHelper patterns. Balanced is default/recommended; Battery saver and Best quality are explicit opt-ins.
- The settings surface uses `ThemePaperBrush` with a margin-red rail and quieter rule/spacing groups instead of nested card chrome. Control bindings, localization, preview/cancel semantics and persistence are unchanged; UIA save/reopen verification passes.

## V5 Completion Status

- The dialog exposes auto-save interval, pressure, PenOnly, smoothing, default pen color/size, performance mode and Light/Dark/System/HighContrast theme controls. Live preview, cancel restoration and complete snapshot saving are wired.
