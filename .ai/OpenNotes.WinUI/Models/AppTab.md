# OpenNotes.WinUI/Models/AppTab.cs
> Last updated: 2026-09-23 (V6 Task 4 Step 4 — tab model) | Protection: STANDARD

## Purpose
One tab in the WinUI shell — the counterpart of the WPF `Models/AppTab.cs`. Owns its live `Microsoft.UI.Xaml.Controls.Frame` (the navigation journal host), `Title`, `Icon` (Lucide name), `FilePath`, `IsHome`, `IsActive`.

## Design Decisions
- **Lives in OpenNotes.WinUI, not Core.** The plan's "AppTab moves to Core minus Frame" was dropped: a Frame-free AppTab carries almost nothing (the Frame IS the payload) and would force a second synced type. Documented in the type's XML doc.
- `Id` is stable for the tab's lifetime (drag-payload fallback identity, same contract as WPF).
- `IsHome` derives from `FilePath`, never from frame content.
- **Computed chrome properties** (`TabBackground`, `TabBorderBrush`, `TabForeground`, `TitleFontWeight`, `CloseButtonOpacity`, `CloseButtonVisibility`, `DisplayTitle`, `IconGlyph`, `CloseTooltip`) resolve theme brushes at get-time; the tab template binds them so active/inactive visuals stay declarative (replaces WPF's code-built tab chrome + `ApplyTabChrome`). `RefreshVisualState()` re-raises them after each palette swap (driven by `WinUiThemeService.ThemeApplied`).
- `IconGlyph` maps the Lucide name to a Segoe Fluent/MDL2 glyph — interim until the icon port lands (`Home`→E80F, `FileText`/`File`→E8A5).
- `IsCloseButtonVisible` hides the close button while only one tab exists (WPF parity).
- `DisplayTitle` truncates >20 chars to 17+"..." (WPF parity); template also trims at 132 DIP.

## Important Notes / NEVER Change
- `Frame` is the live navigation journal; future cross-window tab moves must detach/attach owner event handlers exactly once.
- Brushes resolved here come from `Application.Current.Resources`; they are only correct after `WinUiThemeService.Apply` has run.

## Open Threads / Resume Context
- **Status:** drives the `TabStrip` ListView in `MainWindow.xaml`. Editor-tab semantics (`FileText` icon, dirty state) arrive with the editor port.
