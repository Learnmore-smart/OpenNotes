# OpenNotes.Tests/ThemeReviewContractTests.cs
> Last updated: 2026-08-24（Wave5 review RED/GREEN） | Protection: STANDARD

## Purpose

Exercise the second-pass Wave5 acceptance contracts that source-only palette tests could miss: reduced-motion consumption, semantic alias use, compact Settings focus/disabled/UIA behavior, responsive French layout, runtime chrome resources, explicit/system HighContrast lifecycle, reduce-transparency consumers, and real WPF PDF display-layer composition.

## RED/GREEN evidence

- 2026-08-24 Settings-menu crash regression: `ManuallyOpenedMoreMenuSurvivesMissingPlacementTarget` opens a real WPF `ContextMenu` through the same programmatic `IsOpen` path as `MainWindow.MoreButton_Click`, leaves `PlacementTarget` null, and drains the deferred Render callback. RED reproduces the `ArgumentNullException` from `Window.GetWindow(null)` reported by the runtime event log.
- 2026-08-24 Light-background regression: `LightNeutralUsesWhiteWindowDeskAndWorkspace` requires the Light/Neutral window, desk, canvas and workspace surround to resolve to opaque white, matching the user-visible expectation without tinting PDF page pixels.
- 2026-08-24 startup-crash hotfix: `HomeTileHoverClonesFrozenTemplateScaleBeforeAnimating` uses the real private `HomePage.AnimateTileScale` path and a frozen template-style transform. RED reproduced the exact `InvalidOperationException` at line 100; GREEN proves hover replaces it with a mutable instance. Full suite passes 259/259.
- RED was captured before the review fixes for the missing production animation helper consumers, unused semantic aliases, Settings focus/disabled/responsive contracts, HighContrast refresh lifecycle, runtime chrome literals, and the missing page-level composite probe.
- GREEN `ThemeReviewContractTests` passes 13/13 on an STA fixture, and the full suite passes 261/261. The fixture calls `ThemeService.ResetForTests()` after every case so injected SystemEvents overrides and event subscriptions cannot leak across tests.
- The motion contract also scans Home/Editor XAML for fixed `Duration="0:0:*"` storyboard literals: Home hover scale uses the interruptible `AnimateTileScale` helper, and the Editor loading spinner is a code-behind animation gated by `ShouldAnimate`.
- The runtime-chrome scan also covers Editor/PdfPageControl text selection, resize, ruler and eraser visuals, rejecting the retired fixed `#0078D4`/alpha-blue expressions while preserving explicit user/annotation colors as the data-color allowlist.
- ReduceTransparency is checked beyond the XAML token declaration: App popup shadows use `ThemeShadowOpacity`, and code-created Home/Editor/PdfPageControl chrome reads `ThemeService.GetShadowOpacity()` when constructing effects.
- `SettingsControlsHaveKeyboardFocusPeersAndDisabledVisualsAtRuntime` builds the Settings resource dictionary, creates the real `SettingsWindow` in French at the 420 DIP minimum, checks the live focus style/UIA peers and disabled visual, and records that headless runners may not activate a keyboard focus ring even when its 2 DIP style is correctly installed.
- `FrenchSettingsAtMinimumSizeWrapWithoutHorizontalClip` verifies the named scroll viewport/extent and wrapped French labels rather than relying on a fixed desktop screenshot.
- `PdfPageControlCompositeKeepsPagePixelsStableAcrossWorkspaceBackdrops` uses a real STA `PdfPageControl`, known non-white `BitmapSource`, annotation rectangle, outer workspace Border, and `RenderTargetBitmap`; Neutral/Paper/Slate page crops remain identical while the outer surround is free to change.

## Important Notes / NEVER Change

- Keep `ThemeService.Shutdown`/`ResetForTests` lifecycle-safe: production App shutdown unhooks `SystemEvents.UserPreferenceChanged`, and tests must not retain static event roots or injected HC values.
- A workspace backdrop may decorate only the outer editor surround. Never apply its brush/effect/opacity/color matrix to `PdfImage`, `PdfImageOverlay`, page white surface, annotation colors, export, or render pixels.
- Keep RED contracts source/runtime/pixel based; a source string scan alone is insufficient for Settings focus or PDF composition.

## Verification

Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ThemeReviewContractTests"
~~~

Expected: 13 passed, 0 failed, 0 skipped. Desktop screenshot review remains an external/manual boundary and is not claimed without artifacts.
