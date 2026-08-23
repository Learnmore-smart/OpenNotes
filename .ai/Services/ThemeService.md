# Services/ThemeService.cs
> Last updated: 2026-08-22（paper/ink material system completion）| Protection: STANDARD

## Purpose
Owns the application-level chrome palette used by the MainWindow, editor, settings dialog and sidebar. It changes WPF resource tokens only; the rendered PDF bitmap is never tinted.

## What It Does

- `Apply(theme, reduceMotion, reduceTransparency)` normalizes `Light`, `Dark`, `System` and `HighContrast`, updates `CurrentTheme`, `IsDark` and `IsHighContrast`, then replaces the shared brush resources in `Application.Current.Resources`.
- Palette tokens cover window/surface/canvas/border/foreground, hover/pressed/selection/accent, scrollbar/menu and focus colors. Six material tokens—`ThemeDeskBrush`, `ThemePaperBrush`, `ThemePaperAltBrush`, `ThemeInkBrush`, `ThemeMarginBrush`, and `ThemeMarkBrush`—give the shell an independent paper/ink visual language in Light, Dark, and HighContrast modes. MainWindow runtime-created tab chrome and editor runtime-created controls use these resources instead of fixed light brushes; tool-popup headers/dividers, selection filters and text-formatting popups bind through `DynamicResource`/`SetResourceReference`.
- Accessibility tokens include `ThemeAnimationDuration` (zero when ReduceMotion is requested), `ThemeSurfaceOpacity` (opaque when ReduceTransparency is requested) and `ThemeFocusBrush`. With no explicit override, ReduceMotion follows the Windows client-area animation preference and HighContrast defaults to reduced motion/transparency.
- Applying before startup localization and applying again during SettingsWindow preview are both supported and idempotent.

## Public API

| Member | Description |
|---|---|
| `Apply(string theme, bool? reduceMotion = null, bool? reduceTransparency = null)` | Normalize theme and update application resources |
| `CurrentTheme` | Effective Light/Dark/HighContrast theme name |
| `IsDark` / `IsHighContrast` | Effective palette flags |
| `ReduceMotion` / `ReduceTransparency` | Active accessibility preferences |

## Constraints

- Theme changes affect application chrome and canvas surfaces only; never recolor PDF page bitmaps or annotation data.
- Keep resource keys stable because XAML and runtime-created controls use `DynamicResource`/resource lookups.
- High contrast must retain visible focus and selection feedback.

## Open Threads / Resume Context

- **Status:** complete
- The semantic material palette and matching generic chrome values are implemented across light, dark and high-contrast modes. Existing resource keys and PDF bitmap rendering remain unchanged.

## Verification

`OpenNotes.Tests/ThemeServiceTests.cs` covers palette normalization, explicit accessibility overrides, all six material resources, source-level surface usage and HomePage tile token usage. The solution builds with 0 errors and the full suite passes 100/100. UIA theme preview plus save/restart persistence pass; full pixel-level WPF/high-contrast inspection remains a desktop manual check.
