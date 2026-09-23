# OpenNotes.WinUI/Services/WinUiThemeService.cs
> Last updated: 2026-09-23 (V6 Task 4 Step 3 — theme port) | Protection: STANDARD

## Purpose
WinUI 3 counterpart of the WPF `Services/ThemeService.cs` — owns the runtime-swappable application chrome palette (`Theme*Brush` keys) for the shell, future editor/settings surfaces. The rendered PDF bitmap is never tinted.

## What It Does
- `Apply(theme, reduceMotion, reduceTransparency, workspaceBackdrop)` normalizes `Light`/`Dark`/`System`/`HighContrast`, sets `CurrentTheme`/`IsDark`/`IsHighContrast`/`CurrentWorkspaceBackdrop`/`ReduceMotion`/`ReduceTransparency`, then rewrites every palette brush inside `Application.Current.Resources` — same key names as WPF so ported XAML binds unchanged.
- The three palettes are copied 1:1 from WPF `ThemeService` (`LightPalette`/`DarkPalette`/`HighContrastPalette`), plus the semantic aliases (`ThemeWindowBrush`, `ThemeWorkspaceBrush`, `ThemeSidebarBrush`, `ThemeToolbarBrush`, `ThemeControlBrush`, `ThemeTextBrush`, `ThemeSubtleTextBrush`, `ThemeDangerBrush`, `ThemeWindowOutlineBrush`), the six material tokens, `ThemeFocusBrush`, `ThemeAnimationDuration`, `ThemeSurfaceOpacity`, `ThemeShadowOpacity`, and the six `WorkspaceBackdrop` colors.
- Fluent theme switching: WinUI has no runtime app-level `RequestedTheme`, so `RegisterWindow(window)` tracks windows and `Apply` sets `RequestedTheme` on each window's content root (`ElementTheme.Dark` for Dark AND for explicit HighContrast — `ElementTheme` cannot express HC; real OS high contrast is handled by Fluent itself).
- XAML consumes keys via `{ThemeResource}`, which re-resolves both on theme changes AND on in-place resource replacement — the WinUI equivalent of WPF `DynamicResource`/`SetResourceReference`.
- System inputs use `UISettings` (`GetColorValue(Background)` dark probe, `AnimationsEnabled`, `AdvancedEffectsEnabled`, `ColorValuesChanged`) and `AccessibilitySettings` (`HighContrast`, `HighContrastChanged`), with the WPF HKCU `AppsUseLightTheme` registry probe as dark fallback. OS-HC resource repainting maps to `UIColorType` (Background/Foreground/Accent/Complement) — the closest public surface to WPF `SystemColors`.
- `ThemeApplied` event fires after each apply so chrome holding computed brushes (`AppTab` tab pills) re-resolves.
- `RefreshSystemPreferences()` (public), `Shutdown()`, `ResetForTests()`, `NormalizeWorkspaceBackdrop`, `GetAnimationDuration`, `GetShadowOpacity`, `ShouldAnimate` mirror the WPF API surface.

## Important Notes / NEVER Change
- Keep resource key NAMES identical to WPF — later ported XAML binds via `{ThemeResource}` and must not be touched.
- `ThemePopupAnimation` was deliberately NOT ported (WPF-only `PopupAnimation` enum; WinUI flyouts own their motion).
- Callbacks may arrive off-thread; refresh marshals through a registered window's `DispatcherQueue`.

## Open Threads / Resume Context
- **Status:** applied at startup in `MainWindow` ctor from `AppSettingsService.Load()` (theme + workspaceBackdrop + language).
- Test hooks `SystemHighContrastOverrideForTests`/`SystemDarkThemeOverrideForTests` were dropped — no WinUI test project exists yet; re-add when tests port over.
