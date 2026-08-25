# MainWindow

> V5.1.2 renders programmatic tabs, window state and toasts with the shared Lucide vector control.

## Wave6 transient lifecycle (2026-08-24)

- **Status:** green for focused automated scope. `MainWindow_Deactivated` first calls the
  shared `EditorPage.CancelInteraction("window deactivated")` boundary, then closes the
  Sort/More menus and calls `CloseTransientUi("window deactivated")` on every retained editor
  (including hidden Frame journal instances). `ActivateTab` closes only the editor becoming
  inactive before changing host state, preserving multi-tab isolation.

## Wave6 dual-review follow-up (2026-08-24) — GREEN closure

- `MainWindow.Deactivated` cancels each retained editor's in-flight page/editor captures
  before sweeping transient UI, while save/modal dialogs remain protected. The same
  idempotent EditorPage boundary is used by tab/navigation/reload; `_tabs` and Frame journal
  ownership are unchanged. Focused source/STA coverage is green; real foreground Alt-Tab
  focus-loss remains unavailable in this environment.

## Wave6 async stale-operation P2 (2026-08-24) — RED/in_progress

Tab activation/deactivation and editor release must cancel the shared
`DocumentOperationSession` lease before a retained editor can resume elsewhere.
Old Version History/sidebar/PDF context and Undo/Redo continuations must not
touch a replacement tab/document or surface stale errors; Frame/tab ownership and
the existing save/close admission protocol remain unchanged.

## Purpose

Owns the single-window Frame/tab shell, application chrome, startup theme and global tab keyboard navigation.

## V5 Changes

- V5.1.1 anchors programmatically opened Sort/More context menus to their owning button before setting `IsOpen`, preserving popup ownership and preventing the Settings-menu crash.
- Applies the persisted `ThemeService` palette before localization at startup.
- Applies the persisted `WorkspaceBackdrop` to editor workspace chrome at startup; the PDF page surface remains an independent opaque layer.
- `Ctrl+Tab` and `Ctrl+Shift+Tab` cycle the existing `_tabs` list without replacing the active Frame.
- Settings preview propagates the complete `AppSettings` snapshot to open editor tabs and restores it on cancel.
- Title-bar, search, tab-strip and chrome surfaces consume the runtime theme brush resources. The shell now reads as a paper archive: desk root, alternate-paper title/tab areas, an ink search surface and a margin-red brand rail.
- Wave5 review keeps the outer shell root on the live `ThemeDeskBrush` alias (updated by `ThemeService` for backdrop/theme changes); inner workspace/title/search surfaces remain dynamic and PDF page pixels are outside this shell resource.
- The More/settings command retains the stable `MoreButton` AutomationId used by the isolated Settings UIA smoke.

## Constraints

- Preserve the single MainWindow + Frame-tab architecture.
- Do not tint PDF page bitmaps when changing application theme.

## Open Threads

- **Status:** performance/save lifecycle complete. `ActivateTab` suspends the previous editor and activates the selected editor; `Frame_Navigated` applies the same rule to back/forward/new-content navigation, and navigation-away paths suspend before leaving. Minimizing/restoring the window uses the same host-active lifecycle. CloseTab, NavBack, NavHome, and window close now await the editor's generation-aware close/navigation protocol before releasing resources or changing content; failures keep the editor/tab alive and surface a toast. Re-entry guards serialize tab close/navigation/frame changes, including tab activation, new-tab, open-file, and current-tab navigation commands while a close workflow is active; each workflow has a bounded 30-second retryable timeout. A timeout hands the still-running release task to a tracked background continuation while retaining the tab/window guard; only settled success removes the tab or requests `Close()`, and a failed release leaves the editor non-interactive for explicit retry. `_frameEditors` retains every editor in a Frame journal, so closing a tab/window also prepares and releases editors hidden behind a HomePage. `OnClosing` remains synchronous to WPF and requests Close again only after all editors finish. The Frame/tab shell and save ordering are unchanged.
- Active tab/window restoration also calls `ResumeDocumentInteraction` after `SetHostActive`, covering an editor whose navigation completed while the window was minimized or its frame was inactive.

## Agent Decisions / Thoughts

- **2026-08-20 Codex:** Runtime-created tab controls use `SetResourceReference` so an in-place `ThemeService.Apply` swaps their brushes without requiring a new window. Inactive tabs remain transparent; active tabs use the theme's alternate surface and border tokens.
- **2026-08-20 Codex:** Tab borders are keyboard focusable and activate on Enter/Space. Focus uses `ThemeFocusBrush` and does not alter the existing drag/reorder or single-window Frame architecture.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-24 | Passed the persisted WorkspaceBackdrop through startup/settings preview into ThemeService without changing Frame/tab or PDF rendering behavior. | Codex |
| 2026-08-20 | Replaced fixed runtime tab colors with dynamic theme resources and added keyboard focus/activation feedback. | Codex |
| 2026-08-20 | Migrated title-bar, navigation, selection, search, and toast chrome to existing runtime `Theme*Brush` resources without changing layout or behavior. | Codex |
| 2026-08-21 | Wired editor activation/navigation/minimize suspension and awaited native-resource cleanup on tab/window close. | Codex |
| 2026-08-22 | Applied the Desk/Paper/PaperAlt/Ink/Margin material tokens to the existing shell without changing Frame/tab bindings or runtime behavior. | Codex |
| 2026-08-23 | Added close-safe generation retry/false handling for tab, navigation, and synchronous WPF `OnClosing` orchestration. | Codex |
| 2026-08-23 | Added tab/navigation/window busy guards, bounded cancellation/retry workflow, and release-result checks so failed cleanup never removes a tab. | Codex |
| 2026-08-23 | Navigation re-entry now resumes an editor's blocked admission only when its frame becomes active again, preventing both late edits during save and a permanently disabled returned editor. | Codex |
| 2026-08-23 | Timeout handoff now retains tab/window busy guards until the underlying release task settles; failed suffix preparation is cancellable without re-enabling a partially disposed editor, and stale NavBack journals cancel preparation before returning. | Codex |
| 2026-08-24 | Replaced the hard-coded default toast icon parameter with the named `ToastIconKind.Check` fallback, preserving the Lucide icon while keeping visible-string verification clean. | Codex |
