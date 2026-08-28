# OpenNotes.Tests/EditorToolbarVisualSourceTests.cs
> Last updated: 2026-08-23 (Wave 3 P2 theme-token TDD RED/GREEN source contract) | Protection: STANDARD

> 2026-08-26 RED/GREEN: Task 4 adds dedicated Laser/Hidden Ink semantics and compact toolbar-rhythm contracts; the focused 25-test class passes.

## Purpose

Source-level contracts for the Wave 3 editor toolbar affordance repair. These tests protect the visible toolbar boundary without requiring a WPF desktop session: obsolete Fit/preset/Ink Analysis entry points must be absent, laser and highlighter visuals must be vector-backed, shape/highlighter popup choices must expose real previews, and toolbar metadata must remain localizable and stable.

## Open Threads / Resume Context
- **2026-08-28:** Shared smoke aliases now track TextAnnotationMoveBorder instead of the removed dotted drag handle.
- **Status:** ready_for_next — Task 4 source contracts are green for dedicated Laser/Hidden Ink vectors, uniform compact action cells/separators, preserved action IDs/order and the compact page-jump source shape. The production change remains limited to `Controls/LucideIcon.cs` and `Pages/EditorPage.xaml`; Hidden Ink’s existing test expectation follows the intentional vector rename.

- **2026-08-24 GREEN result:** contracts require the accessible sidebar resize target and container right edge to render no vertical rail, require the custom themed/localized editor ToolTip path, reject doubled or per-tool-tinted Lucide toolbar glyphs, reject the shape checkmark overlay, and require the nine-choice 3×3 catalog. The focused editor contract passes 29/29 and the combined editor/navigation/localization slice passes 58/58.

- 2026-08-24 release 5.1.2 RED plan: scan the full visible WPF shell/home/editor/settings/template surface and runtime icon builders for Segoe MDL2/private-use glyphs; require named `LucideIcon` usage and keep the PenOnly toolbar action as `PenLine`.
- **Status:** 5.1.2 icon contract GREEN; visible WPF surfaces reject MDL2/XAML private-use glyph rendering, text stars and the text-only close symbol. Legacy runtime identifiers are accepted only through the central Lucide compatibility map.
- Screenshot-driven editor RED contract requires a named, font-independent Lucide vector renderer, previous/next page buttons around the editable one-based field, and a non-colliding three-column sidebar selector while preserving handlers and automation IDs.
- **Status:** Wave 3 P2 source-contract work complete; external visual/device/foreground checks remain separate.
- **Intent/result:** contracts cover highlighter size/opacity refresh (including the area-highlight main drag preview), localized alignment ItemsSource/selection preservation, live ruler/font/color/popup theme-token state colors, real Button/ToggleButton popup peers and keyboard paths, marker contrast, smoke ID drift, detachable/idempotent popup lifecycle, context/ComboBox duplicate-hook guards, and high-contrast pen visuals.
- **Coverage:** no visible preset slots/Fit/Ink Analysis entry points; explicit laser/highlighter vector paths; geometry/checked shape and highlighter popup choices (selection shape/filter and text-toolbar controls also receive stable metadata); localized tooltip/UIA helper path; no default PenPresets write.
- **Scope:** Wave 3 only. Sidebar, theme palette/backdrop, transient UI and Sticky Note lifecycle remain owned by later waves.
- A separate STA runtime-peer test validates constructed dynamic popups; it does not add Wave6 global popup dismissal or require foreground ownership.

## Important Notes / NEVER Change

- Keep `AppSettings.PenPresets` JSON compatibility; the visible slot controls are removed, not the settings field or round-trip behavior.
- Keep Hidden Ink's existing `HiddenInkToolButton` AutomationId and card/answer vector semantics.
- Keep the single-frame WPF editor and existing command handlers for supported zoom, selection, shape, highlighter and laser behavior.

## Agent Decisions / Thoughts

- **2026-08-23:** The RED contract intentionally reads the production XAML/code so it can reject text glyph or text-only popup regressions while remaining independent of desktop foreground ownership.
- **2026-08-23:** The dual-review continuation adds source contracts for explicit `PopupZOrderHelper.UnfixPopupTopmost` use, idempotent ContextMenu/ComboBox hooks, highlighter icon alpha sharing, and stable page/handle aliases; these protect lifecycle and smoke drift without requiring a foreground desktop.
- **2026-08-23:** The initial P2 source RED contracts were made green after the smallest production changes. The theme-token continuation added a RED scan for redundant hard-coded popup state initializers, then removed those initializers while retaining actual user color backgrounds. The final focused source/runtime filter passed `22/22`; full suite passed `189/189`, and the explicit-fixture Editor UIA smoke passed all required production IDs and nine tool toggles.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Added Wave 3 toolbar visual/accessibility TDD RED contract and verified it GREEN after implementation. | Codex |
| 2026-08-24 | Added a named Lucide renderer/page-navigator/sidebar-strip contract and updated live laser/highlighter expectations to the new vector control. | Codex |
| 2026-08-24 | Extended the contract across all visible application chrome and preserved PenOnly as a named PenLine action for 5.1.2. | Codex |
| 2026-08-24 | Added RED/GREEN contracts for the line-free sidebar edge, localized custom ToolTips, normalized Lucide toolbar, and checkmark-free nine-shape picker. | Codex |
