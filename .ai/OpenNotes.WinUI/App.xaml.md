# OpenNotes.WinUI/App.xaml
> Last updated: 2026-09-23 (V6 Task 4 Step 3 — theme token port) | Protection: STANDARD

## Purpose
WinUI 3 `Microsoft.UI.Xaml.Application` markup — merges `XamlControlsResources` for Fluent control themes and carries the ported WPF chrome palette.

## What It Contains
- `AppGlass*` overlay brushes at root level (theme-independent — WPF defines them identically and never switches them).
- All `Theme*Brush` keys at root level with Light-palette values (baseline + the semantic aliases + `ThemeDangerBrush`), plus `ThemeAnimationDuration` (`Duration`), `ThemeSurfaceOpacity`/`ThemeShadowOpacity` (`x:Double`) — `WinUiThemeService.Apply` replaces these in place, exactly like the WPF `ThemeService` does.
- `ResourceDictionary.ThemeDictionaries` with `Light`, `Dark`, and `HighContrast` dictionaries holding the full three palettes from `ThemeService.cs` (including resolved aliases + HC's forced zero-duration/opaque tokens) so a bare `RequestedTheme` switch already resolves sensible colors before the service writes overrides.

## Important Notes / NEVER Change
- Keep `x:Class="Caelum.App"` (RootNamespace compatibility).
- Keep the key NAMES identical to WPF — ported XAML binds `{ThemeResource Theme*Brush}` and must not be edited.
- `ThemePopupAnimation` is intentionally absent (WPF-only enum token).

## Open Threads / Resume Context
- **Status:** complete for the theme-token port; shared control styles (ToolbarButton etc.) port alongside their consumers in later tasks.
