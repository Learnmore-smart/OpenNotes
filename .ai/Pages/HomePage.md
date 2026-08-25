# HomePage

> V5.1.2 runtime menu and notification icons use the shared Lucide renderer.
> Tile hover resolves a directly assigned named content element before walking the visual tree, preserving the frozen-transform clone contract under custom button templates.

> Last updated: 2026-08-22（paper archive home-surface completion）| Protection: STANDARD

## Purpose

Displays the document/folder home surface inside the MainWindow tab shell.

## V5 Changes

- The page background, navigation labels, title/subtitle, selection summary and drag/drop panel use the application theme resources.
- Folder/file tile hover, drop-target, selection, checkbox, filename and metadata states use the shared `ThemeService` brush tokens, so the home surface follows light, dark and high-contrast palettes.
- PDF/file preview artwork remains content-specific; changing the theme does not recolor document thumbnails.
- The fallback notebook directory uses `ProductInfo.GetDataDirectory()`; production remains `%LOCALAPPDATA%\Caelum\Notebooks`, while an explicit `OPENNOTES_DATA_ROOT` isolates diagnostic/test runs.
- Wave5 review routes dynamically created add/file/folder context menus through `ThemeSurfaceBrush`, `ThemeBorderBrush`, `ThemeSurfaceOpacity`, `ThemeTextBrush` and `ThemeDangerBrush`; the rename prompt now uses live surface/control/text/border resources and follows runtime theme preview.
- Home smooth scrolling consumes `ThemeService.GetAnimationDuration`; ReduceMotion cancels the Rendering subscription and jumps to the target offset.
- Add-tile, folder, and file hover scale effects now run through the code-behind `AnimateTileScale` helper and `ThemeService.GetAnimationDuration`; the retired fixed `0.2/0.3` second storyboards cannot bypass ReduceMotion.

## Constraints

- Preserve existing HomeTile bindings and file/folder navigation.
- Keep the page safe inside the single-window Frame-tab architecture.

## Open Threads

- **Status:** complete (2026-08-24 startup crash hotfix)
- `AnimateTileScale` now replaces only a frozen template `ScaleTransform` with `CloneCurrentValue()` on its owning tile before clearing/starting animations. ReduceMotion behavior, target scale values, lookup and layout are unchanged. The real STA regression reproduced the event-log exception before the fix and passes afterward; full suite is 259/259, and an installed 5.0.1 startup remained alive with zero new Windows crash events.
- **Status:** complete for localization, paper/ink surface and Wave5 review work
- Every live HomePage subscribes to `LocalizationService.LanguageChanged` while loaded, unsubscribes while unloaded, and refreshes once when it is loaded again. The current pass keeps the page refresh idempotent while MainWindow updates tab chrome.
- All three dynamically created HomePage menus register the helper immediately before opening. Their refresh path resolves each catalog key at the call site, keeping the static i18n verifier able to prove every key while preserving already-open menu updates.
- The root uses `ThemeWorkspaceBrush`, the selection panel uses `ThemeSidebarBrush`, the breadcrumb/header uses `ThemePaperAltBrush`, the leading archive rail uses `ThemeMarginBrush`, and drag/drop feedback uses semantic ink/selection tokens. Manual Alt-Tab/context-menu verification remains a desktop check; the solution builds with 0 errors and two documented NU1701 warnings.

## Agent Decisions / Thoughts

- **2026-08-24:** Windows `.NET Runtime` event 1026 identifies the launch crash at `HomePage.AnimateTileScale` line 100: `BeginAnimation` is called on a frozen `ScaleTransform`. The fix must replace the frozen Freezable at its owning element rather than catch/suppress the exception.
- **2026-08-20:** The helper is called immediately before `IsOpen = true`, matching the existing EditorPage dynamic-menu pattern and avoiding any changes to menu construction.
- **2026-08-21:** Replaced HomePage tile chrome literals with `DynamicResource` theme tokens while preserving content-specific folder/file artwork colors.
- **2026-08-21:** Added the lifecycle-bound language-change subscription plan for open HomePage instances.
- **2026-08-21:** Added the lifecycle-bound language-change subscription and refresh path for open HomePage instances.
- **2026-08-21:** Audited duplicate language refreshes and navigation-history return behavior; loaded pages now need an explicit refresh on re-entry while MainWindow skips already-loaded page instances.
- **2026-08-21:** Routed the fallback notebook directory through `ProductInfo.GetDataDirectory()` without changing the production compatibility path.
- **2026-08-22:** Restructured the header as an annotated-paper archive with semantic desk/paper/margin resources while preserving HomeTile bindings, navigation and PDF thumbnail rendering.

## Change History

- **2026-08-24:** Fixed the OpenNotes 5.0 home hover startup crash caused by animating a frozen WPF template transform.
- **2026-08-20:** Added z-order registration for add-tile, file, and folder context menus.
