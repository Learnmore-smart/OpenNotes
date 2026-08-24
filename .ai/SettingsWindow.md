# SettingsWindow
> Last updated: 2026-08-24（Wave5 review focus/disabled/FR measure GREEN）| Protection: STANDARD

## Purpose
Modal editor for persisted application settings. It previews language/theme and utility changes, then returns a complete `AppSettings` snapshot to MainWindow.

## What It Does

- Initializes language, auto-save interval, pressure, PenOnly, smoothing, default pen color/size, performance mode, Light/Dark/System/HighContrast theme, and Neutral/Paper/Slate workspace backdrop controls from the incoming settings.
- `ApplyLocalization()` refreshes labels, checkbox content, combo-box item text and title without losing the current selections.
- `GetSelectedSettings()` starts from a deep clone of the original snapshot, then applies current control values; unrelated fields such as presets and recent colors are preserved.
- Language and utility changes preview immediately through MainWindow; Cancel/close restores the original snapshot, while Save returns the selected snapshot.
- A language selection publishes one change notification only; SettingsWindow refreshes itself from that notification and MainWindow owns the outer-window refresh, avoiding a second explicit refresh of the same controls.
- All six ComboBoxes (including `WorkspaceBackdropComboBox`) register `PopupZOrderHelper.FixComboBoxPopupTopmost` after `InitializeComponent`, before item sources are populated.
- The Wave5 dialog is compact and resizable with bounded min/max dimensions and an auto vertical scrollbar so narrow/small windows do not clip the form. The utility grid uses a responsive star/auto pair and wrapped labels; French 420-DIP measure has no horizontal extent. Backdrop previews update the chrome immediately and Cancel restores the complete original snapshot.
- ModernComboBox, DialogPrimary/DialogSecondary and CloseButton expose an explicit DynamicResource two-DIP focus ring, keyboard tab stop/UIA peers and themed disabled opacity/text. A WPF runtime contract exercises focus eligibility, disabled visuals and French layout; HWND activation may be unavailable on a headless test desktop, so the contract also verifies live focus templates and keyboard properties.

## Constraints

- Preserve every `AppSettings` field when saving; do not construct a language-only object.
- Preview changes must be reversible on cancel.
- ComboBox popups use `PopupZOrderHelper`; do not duplicate Win32 interop here.
- Visible labels and checkbox content come from `LocalizationService`.

## Open Threads / Resume Context

- **Status:** complete for the automated Wave5 review scope.
- The localized performance-mode and workspace-backdrop ComboBoxes follow the utility-row, preview/cancel, complete-clone, and PopupZOrderHelper patterns. Balanced and Neutral are defaults; Battery saver/Best quality and Paper/Slate are explicit opt-ins. UIA save/reopen and Cancel rollback pass in the isolated smoke.
- The settings surface uses `ThemePaperBrush` with a margin-red rail and quieter rule/spacing groups instead of nested card chrome. Control bindings, localization, preview/cancel semantics and persistence are unchanged; UIA save/reopen verification passes.

## V5 Completion Status

- The dialog exposes auto-save interval, pressure, PenOnly, smoothing, default pen color/size, performance mode and Light/Dark/System/HighContrast theme controls. Live preview, cancel restoration and complete snapshot saving are wired.
- Wave5 also exposes localized Neutral/Paper/Slate workspace backdrop choices in a bounded resizable/scrollable form; backdrop preview is chrome-only and Cancel restores the original setting snapshot.
