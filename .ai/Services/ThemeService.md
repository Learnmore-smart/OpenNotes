# Services/ThemeService.cs
> Last updated: 2026-08-24（Wave5 review GREEN: motion/HC/composite lifecycle）| Protection: STANDARD

## Purpose
Owns the application-level chrome palette used by the MainWindow, editor, settings dialog and sidebar. It changes WPF resource tokens only; the rendered PDF bitmap is never tinted.

## What It Does

- `Apply(theme, reduceMotion, reduceTransparency, workspaceBackdrop)` normalizes `Light`, `Dark`, `System` and `HighContrast`, updates `CurrentTheme`, `IsDark` and `IsHighContrast`, then replaces the shared brush resources in `Application.Current.Resources`.
- Palette tokens cover window/surface/canvas/border/foreground, hover/pressed/selection/accent, scrollbar/menu and focus colors. Six material tokens—`ThemeDeskBrush`, `ThemePaperBrush`, `ThemePaperAltBrush`, `ThemeInkBrush`, `ThemeMarginBrush`, and `ThemeMarkBrush`—give the shell an independent paper/ink visual language in Light, Dark, and HighContrast modes. MainWindow runtime-created tab chrome and editor runtime-created controls use these resources instead of fixed light brushes; tool-popup headers/dividers, selection filters and text-formatting popups bind through `DynamicResource`/`SetResourceReference`.
- Accessibility tokens include `ThemeAnimationDuration` (zero when ReduceMotion is active), `ThemeSurfaceOpacity` (opaque when ReduceTransparency/HighContrast is active), `ThemeShadowOpacity`, `ThemePopupAnimation` and `ThemeFocusBrush`. `GetAnimationDuration` and `ShouldAnimate` are consumed by toast, Home/Editor smooth scroll, loading spinner, laser fade and selection-dash production paths; zero duration cancels/finishes the path immediately. Code-created popup/chrome effects use `GetShadowOpacity`, so ReduceTransparency also reaches Home/Editor/PdfPageControl shadows. With no explicit override, ReduceMotion follows the Windows client-area animation preference and HighContrast forces reduced motion/transparency.
- System preference handling is explicit and testable: `SystemEvents.UserPreferenceChanged` refreshes both `System` and explicit HighContrast palettes, `RefreshSystemPreferencesForTests` injects deterministic OS HighContrast/dark values, and `Shutdown`/`ResetForTests` unhooks the static event. `App.OnExit` calls `Shutdown`.
- Applying before startup localization and applying again during SettingsWindow preview are both supported and idempotent.

## Public API

| Member | Description |
|---|---|
| `Apply(string theme, bool? reduceMotion = null, bool? reduceTransparency = null, string workspaceBackdrop = null)` | Normalize theme/backdrop and update application resources |
| `CurrentTheme` | Effective Light/Dark/HighContrast theme name |
| `CurrentWorkspaceBackdrop` | Effective Neutral/Paper/Slate editor surround (HighContrast forces Neutral) |
| `NormalizeWorkspaceBackdrop(string)` | Defensive three-value backdrop normalization |
| `IsDark` / `IsHighContrast` | Effective palette flags |
| `ReduceMotion` / `ReduceTransparency` | Active accessibility preferences |
| `ShouldAnimate` / `GetAnimationDuration(TimeSpan)` | Shared interruptible animation gate/duration token consumer |
| `GetShadowOpacity()` | Live `ThemeShadowOpacity` value for code-created popup/chrome effects |
| `RefreshSystemPreferencesForTests(bool?, bool?)` | Deterministic System/HighContrast refresh hook |
| `Shutdown()` / `ResetForTests()` | Unhook process-wide preference event and reset static test state |

## Constraints

- Theme changes affect application chrome and canvas surfaces only; never recolor PDF page bitmaps or annotation data.
- Keep resource keys stable because XAML and runtime-created controls use `DynamicResource`/resource lookups.
- High contrast must retain visible focus and selection feedback.

## Open Threads / Resume Context

- **Status:** complete for the automated Wave5 review scope.
- **Result:** the approved neutral Light palette, semantic aliases, dynamic workspace backdrop resources, Dark/HighContrast behavior, and PDF image separation are implemented and covered by focused/runtime/source contracts.
- **Blockers / notes:** HighContrast ignores decorative backdrop choices; PDF image/page layers remain opaque and un-tinted. Desktop screenshot/foreground/device visual review remains external and unclaimed.

## Verification

`OpenNotes.Tests/ThemeServiceTests.cs` covers palette normalization, explicit accessibility overrides, all six material resources, source-level surface usage and HomePage tile token usage. `ThemeSurfaceSourceTests` adds semantic/backdrop/settings/PDF pixel contracts. `ThemeReviewContractTests` covers RED/GREEN motion helper consumers, semantic alias consumers, Settings runtime focus/disabled/French measure, injected HighContrast hook lifecycle, popup transparency consumers, runtime chrome literal allowlists and a real WPF `PdfPageControl` composite probe. The solution builds with 0 errors and the focused/full suite is expected to remain green after Wave5. UIA theme/backdrop preview plus save/restart persistence pass; full screenshot/foreground/high-contrast inspection remains a desktop manual check.

## Wave5 Decisions

- V5.1.1 makes the Light/Neutral window, desk, canvas and workspace surround opaque white in response to the shipped gray-background defect; alternate surfaces, borders and controls retain cool-neutral separation, and PDF pixels remain untouched.
- Light uses white primary surfaces with cool neutral alternates (`#FFFFFF`, `#F8F9FA`, `#D1D5DB`, `#1F2937`, `#2563EB`, `#D9A72E`, `#C2414B`); no cream/yellow desk defaults.
- Backdrop is a resource-level chrome choice (`Neutral`, `Paper`, `Slate`) and never an image effect, opacity tint, or overlay on `PdfImage`/`PdfImageOverlay`.
- Runtime tests render a known blank PDF before and after all three backdrop choices and verify identical PNG SHA-256/bytes; no screenshot or foreground/device visual PASS is claimed.
- When Windows High Contrast is active, core chrome plus scrollbars, slider, separators, focus and selection resolve from `SystemColors`; explicit HighContrast while the OS mode is off keeps the deterministic readable fallback palette for existing tests.
- 2026-08-24: workspace normalization and chrome resources now cover `Mist`, `Warm`, and `Midnight` in addition to the compatible `Neutral`, `Paper`, and `Slate` values; PDF page pixels remain isolated from all six.
