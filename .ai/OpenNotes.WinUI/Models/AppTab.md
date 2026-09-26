# OpenNotes.WinUI/Models/AppTab.cs
> Last updated: 2026-09-26 (T12-A — Fluent pill chrome: hover-reveal close + accent bar) | Protection: STANDARD

## Purpose
One tab in the WinUI shell — the counterpart of the WPF `Models/AppTab.cs`. Owns its live `Microsoft.UI.Xaml.Controls.Frame` (the navigation journal host), `Title`, `Icon` (Lucide name), `FilePath`, `IsHome`, `IsActive`.

## Design Decisions
- **Lives in OpenNotes.WinUI, not Core.** The plan's "AppTab moves to Core minus Frame" was dropped: a Frame-free AppTab carries almost nothing (the Frame IS the payload) and would force a second synced type. Documented in the type's XML doc.
- `Id` is stable for the tab's lifetime (drag-payload fallback identity, same contract as WPF).
- `IsHome` derives from `FilePath`, never from frame content — the `FilePath` setter raises `PropertyChanged` for BOTH itself and `IsHome` (derived property must notify or bindings go stale).
- **Computed chrome properties** (`TabBackground`, `TabBorderBrush`, `TabForeground`, `TitleFontWeight`, `CloseButtonOpacity`, `CloseButtonHitTestVisible`, `CloseButtonVisibility`, `AccentBarVisibility`, `DisplayTitle`, `CloseTooltip`) resolve theme brushes at get-time; the tab template binds them so active/inactive visuals stay declarative (replaces WPF's code-built tab chrome + `ApplyTabChrome`). `RefreshVisualState()` re-raises them after each palette swap (driven by `WinUiThemeService.ThemeApplied`).
- T12-A Fluent pill: `TabBackground` = `ThemeSurfaceBrush` when active (elevated card over the Mica band), transparent inactive; `AccentBarVisibility` = accent underline on the active pill only; `IsPointerOver` (set by the template's PointerEntered/Exited, ALSO cleared by the window's deactivate sweep — PointerExited may never fire mid-deactivate) drives `CloseButtonOpacity` 0→1 on inactive pills (active pill always shows close). `CloseButtonHitTestVisible` = `_isCloseButtonVisible && (_isActive || _isPointerOver)` — REQUIRED and bound to BOTH `IsHitTestVisible` and `IsTabStop`: an Opacity-0 element still hit-tests AND stays keyboard-focusable/UIA-invokable; without the gates the invisible close button could swallow a select-click or take an Enter/Space close on an invisible target.
- `Icon` is a Lucide kind name (`Home`, `FileText`) bound straight into `LucideIcon.Kind` by the tab template — the interim `IconGlyph` Segoe-glyph mapper was removed when the icon port landed (T13-B).
- `IsCloseButtonVisible` hides the close button while only one tab exists (WPF parity).
- `DisplayTitle` truncates >20 chars to 17+"..." (WPF parity); template also trims at 132 DIP.

## Important Notes / NEVER Change
- `Frame` is the live navigation journal; future cross-window tab moves must detach/attach owner event handlers exactly once.
- Brushes resolved here come from `Application.Current.Resources`; they are only correct after `WinUiThemeService.Apply` has run.

## Open Threads / Resume Context
- **Status:** drives the `TabStrip` ListView in `MainWindow.xaml`. Editor-tab semantics (`FileText` icon, dirty state) arrive with the editor port.
