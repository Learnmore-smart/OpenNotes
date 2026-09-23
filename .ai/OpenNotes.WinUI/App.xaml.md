# OpenNotes.WinUI/App.xaml
> Last updated: 2026-09-23 (V6 Task 4 — theme token port; dead ThemeDictionaries removed) | Protection: STANDARD

## Purpose
WinUI 3 `Microsoft.UI.Xaml.Application` markup — merges `XamlControlsResources` for Fluent control themes and carries the ported WPF chrome palette.

## What It Contains
- `AppGlass*` overlay brushes at root level (theme-independent — WPF defines them identically and never switches them).
- All `Theme*Brush` keys at root level with **ThemeService LightPalette** values (pure-white #FFFFFF window/surface — NOT the #F3F4F6/#E5E7EB pre-theme placeholder the WPF App.xaml root carries; at runtime WPF overwrites those placeholders with the same LightPalette values used here), plus semantic aliases + `ThemeDangerBrush`, `ThemeAnimationDuration` (`Duration`), `ThemeSurfaceOpacity`/`ThemeShadowOpacity` (`x:Double`). `WinUiThemeService.Apply` replaces these in place, exactly like the WPF `ThemeService` does — that in-place rewrite is the ONLY palette-switch mechanism.

## Important Notes / NEVER Change
- **NO `ResourceDictionary.ThemeDictionaries` for these keys, deliberately.** Root-level keys always win `{ThemeResource}` lookup (Current → Merged → Theme), so dictionaries holding the same keys were unreachable dead code — a `RequestedTheme` flip could never resolve them. Do not re-add theme dictionaries for `Theme*` keys; `WinUiThemeService.Apply` writing root resources is the sole switch path (Fluent control defaults still theme-switch via `XamlControlsResources`'s own dictionaries — those stay).
- Keep `x:Class="Caelum.App"` (RootNamespace compatibility).
- Keep the key NAMES identical to WPF — ported XAML binds `{ThemeResource Theme*Brush}` and must not be edited.
- `ThemePopupAnimation` is intentionally absent (WPF-only enum token).

## Open Threads / Resume Context
- **Status:** complete for the theme-token port; shared control styles (ToolbarButton etc.) port alongside their consumers in later tasks.
