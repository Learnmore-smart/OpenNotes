# Pages/EditorPage.xaml
> Last updated: 2026-08-24（Wave4 review follow-up + Wave5 theme/backdrop/runtime chrome GREEN）| Protection: STANDARD

## Purpose

Toolbar and editor-surface markup for the single OpenNotes PDF editor page.

## What It Does

- Keeps the existing Hidden Ink tool command/binding and localized tooltip hook.
- Uses a themed card/answer vector `Path` instead of the old eye-like `E890` glyph so the tool communicates masking/reveal semantics without eye semantics.
- The Hidden Ink button exposes the stable `HiddenInkToolButton` AutomationId used by the existing UIA smoke script; `EditorPage.Utilities.cs` supplies a non-empty localized Automation Name and HelpText alongside the tooltip. Target sizing and toolbar layout remain unchanged.
- Wave 3 replaces the remaining toolbar glyph placeholders with semantic vector geometry, removes visible preset slots and Fit Width/Fit Page controls, and keeps the compact zoom/page-jump controls. Highlighter and shape popup choices are vector previews with stable localized UIA metadata.

## Open Threads / Resume Context
- **Status:** in_progress for screenshot-driven editor chrome repair. Replace mixed toolbar/sidebar glyphs with a consistent scalable Lucide-style vector renderer, give page navigation an unambiguous previous/current/total/next structure, and prevent sidebar mode icons/labels from colliding. Preserve all handlers, AutomationIds, localization, 32-DIP targets and PDF geometry.
- **Status:** Wave4 review follow-up remains in_progress. The implementation and deterministic STA/UIA contracts are green; the remaining status is intentionally open for the foreground/device visual boundary and the parent wave's final review bookkeeping.
- The page indicator is an always-visible compact editable TextBox (`Editor.PageJump`) with keyboard Enter/Escape/LostFocus validation, localized invalid/range feedback, and one-based clamping. The document rail uses custom command buttons and a state host instead of native TabControl/TabItem styling. Overlay placement is unchanged so PDF scroll/zoom geometry is unchanged.
- **Intent/result:** preserve the runtime popup lifecycle and production-ID contract while keeping Wave 5+/Wave6 lifecycle work untouched. The explicit-fixture Editor UIA smoke found every required production ID and successfully toggled all nine tool controls.
- **Lifecycle:** `ApplyLocalization()` closes, detaches and rebuilds dynamic popups; tool/ContextMenu/ComboBox z-order registrations are idempotent, and old tool-popup handlers are explicitly removed.
- Remove only visible preset/fit/Ink Analysis entry points; preserve the settings JSON field, zoom core and existing toolbar command handlers that remain supported. The legacy preset initializer remains a read-only compatibility shim and is not invoked.
- Wave5: use a backdrop-aware DynamicResource on the editor shell/scroll surround only; `PdfPageControl` owns an opaque, separate PDF paper surface.
- Wave5 review: the page root/toolbar/sidebar consume semantic `ThemeWindowBrush`/`ThemeToolbarBrush`/`ThemeSidebarBrush` aliases, while PDF scroll surround remains `ThemeWorkspaceBackdropBrush`. Runtime insert/delete and text-selection chrome are resource-bound in the code-behind; PDF bitmap/annotation data colors remain separate.
- P2 keeps dynamic popup labels/theme brushes localized and peer-visible, gives selection/filter controls real Toggle semantics and non-color cues, uses shared semantic theme tokens for ruler/font/color popup state (including dark/high-contrast updates), and does not add Wave6 global transient teardown.
- Sidebar page/outline/bookmark controls retain their existing names and handlers; Wave4 adds stable automation metadata and custom list/tree item styling only. Wave6 global transient teardown remains out of scope.
- The sidebar rail exposes Pages, Outline and Bookmarks commands, a 154–320 DIP resize range, a collapse affordance, selected/focus cues, and dynamic page/bookmark/outline UIA metadata. Existing thumbnail, outline and bookmark controls remain functional.
- Review follow-up implementation: ListBox/TreeView item foregrounds and selected states use live DynamicResource expressions; fallback outline rows are real selection/invoke items; thumbnails/context menus are deferred behind recycling ItemsSource templates; outline refreshes are guarded by cancellation/session/path tokens; resize exposes a 32 DIP keyboard/range peer; bookmark state is a localized Toggle; narrow widths auto-collapse without covering the PDF; and programmatic thumbnail synchronization cannot jump the page.
- Final P2 follow-up: the one-based XAML `Text="1"` is guarded during construction so the initial `Editor.PageJump` Value is `1` and `_isPageJumpEditing` stays false; empty documents retain a safe `1 / 0` field. Localization now finishes with a state-aware metadata pass, preserving Expand/Collapse and Bookmark/Unbookmark names, help text and tooltips across EN/ZH/FR. Each fallback outline row keeps TreeView selection semantics and adds a localized, keyboard/UIA-invokable 32 DIP `.Invoke` button that uses the same `JumpToPage` route.
- Recycled sidebar follow-up plan: realized ListBoxItem menus must be treated as bindings to the current page/bookmark model, not as permanent container state. `Unloaded` and `DataContextChanged` will detach popup hooks/handlers and clear the old menu; every Opening will rebuild from the current model identity. The deterministic STA contract covers both Pages and Bookmarks and explicitly exercises an old menu after page1/bookmarkA → page2/bookmarkB recycling.
- Evidence on 2026-08-24: the focused navigation class passed 14/14 (including initial one-based UIA state, three-language state metadata and fallback Selection/Invoke parity); the full suite passed 203/203; build passed with 0 errors; i18n passed 274 catalog entries / 454 calls / 0 hard-coded visible strings; a fresh three-page PDF editor smoke reported initial PageJump `1`, outline `.Invoke` page `2`, outline SelectionItem page `2`, `EDITOR_SMOKE_RESULT=PASS` and isolated cleanup. The real-screen cross-page smoke remains blocked by `REAL_SCREEN_INPUT_UNAVAILABLE` (`foregroundHwnd=0`, `foregroundPid=0`); no foreground/device visual pass is claimed.
- Recycled-row GREEN: `ListBoxItem` `Unloaded`/`DataContextChanged` now unbind old page/bookmark ContextMenus (including `PopupZOrderHelper` and named Click handlers), and each Opening creates a fresh current-model binding; localization refresh may safely clear realized menus for the next Opening. This is limited to existing Pages/Bookmarks rows and does not alter PDF canvas or MainWindow lifecycle.
- Wave5 evidence: editor shell/scroll surround uses `DynamicResource ThemeWorkspaceBackdropBrush`; the known PDF fixture's PNG SHA-256/bytes remain identical across Neutral/Paper/Slate, while PageGrid/PdfImage/PdfImageOverlay stay independent.
- Wave5 review follow-up: `PagesContainer` uses the live `ThemePaperBrush` as the page-surround/paper alias without touching `PdfPageControl` bitmap layers; the loading spinner no longer owns a fixed XAML storyboard duration and is controlled by the code-behind motion helper. Toolbar/sidebar/page-chrome focus and disabled states remain DynamicResource-bound.
- Tool chrome cleanup removes fixed blue eraser/selection literals from Editor XAML and code; ruler and text resize visuals are created with `SetResourceReference`, while user-selected pen/text/annotation colors remain data-owned.
- Final recycled-menu evidence on 2026-08-24: navigation tests passed 15/15, full suite 204/204, build 0 errors, i18n 274 catalog entries / 454 calls / 0 hard-coded visible strings, and the generated three-page Editor smoke remained PASS with isolated cleanup.

## Important Notes / NEVER Change

- Keep the Hidden Ink button name and click handler stable.
- Keep the localized three-second reveal tooltip, Automation Name, and HelpText supplied by `EditorPage.Utilities.cs`.
- Do not tint `PdfImage`/`PdfImageOverlay` or change the page layer architecture.
- Keep icon-only toolbar targets at least 32 DIP with stable IDs and a visible focus ring; hover/checked visuals must not alter layout.

## Agent Decisions / Thoughts

- **2026-08-23:** A simple card with answer lines was chosen over substituting another MDL2 glyph; the vector geometry is explicit, theme-bound, and does not imply viewing/eye behavior.
- **2026-08-23:** Toolbar icon-only controls now use semantic `Path` geometry, a shared `ToolbarFocusVisualStyle`, stable UIA ids and localized tooltip/name/help metadata. Shape/highlighter popup choices expose real checked vector previews; highlighter previews follow the selected color.
- **2026-08-23:** Dual-review continuation makes popup z-order hooks explicitly detachable/idempotent so localization rebuilds do not retain anonymous `Opened` handlers; the highlighter toolbar mark shares the production freehand alpha while retaining the selected color.
- **2026-08-23:** P2 closes the alignment localization model, shared code-created popup styles, slider focus/disabled template, semantic selection toggles, required-ID smoke fail-closed contract, and the ruler/font/color theme-token contract. The STA AutomationPeer/theme-expression tests passed alongside the explicit-fixture Editor UIA smoke; no visual/device/foreground claim is implied.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Replaced Hidden Ink eye-like glyph with themed card/answer vector mark and stable `HiddenInkToolButton` UIA metadata (localized Name/HelpText). | Codex |
| 2026-08-23 | Completed Wave 3 toolbar vector/UIA pass; removed visible preset/Fit/Ink Analysis entry points while preserving PenPresets JSON compatibility. | Codex |
| 2026-08-24 | Wave4 review follow-up implementation and deterministic STA/UIA coverage are green; three-page smoke is green, while the foreground/device boundary remains explicitly unclaimed. | Codex |
| 2026-08-24 | Final P2 follow-up guards the one-based initial PageJump value, reapplies state-aware collapsed/bookmarked metadata last, and adds the localized 32 DIP fallback-outline Invoke affordance beside TreeView SelectionItem semantics. | Codex |
