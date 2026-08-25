# DialogService

> V5.1.2 replaces the runtime close font glyph with the shared Lucide X vector.

> Last updated: 2026-08-21（runtime localization synchronization / sealed template regression） | Protection: STANDARD

## Purpose

Creates the application's small asynchronous information/error dialog without introducing a second dialog framework.

## What It Does

- Builds a borderless owner-modal WPF window at runtime.
- Renders a title, scrollable message, close button, and optional cancel/confirm actions.
- Reuses the application button styles and localization service.
- Builds the close-button `ControlTemplate` completely, including its keyboard-focus trigger, before assigning it to the live Button. This avoids WPF sealing the template before `Triggers.Add` runs.

## Important Notes / NEVER Change

- Keep `ShowInfoAsync`, `ShowErrorAsync`, and `ShowDialogAsync` signatures compatible with existing callers.
- Keep owner-modal behavior and the nullable result semantics for the close button.
- Dialog colors must come from the shared `ThemeService` resource tokens; do not tint PDF content.

## Open Threads / Resume Context

- **Status:** in_progress
- Runtime dialogs retain their theme resource bindings. The current pass also refreshes known catalog-backed title/content/button values while a dialog is open and unsubscribes on close; arbitrary dynamic message text remains unchanged. Catalog entries are selected directly by the active language so the static verifier can prove the catalog itself is the source of truth.
- A regression test exercises the close-button template factory on an STA WPF thread so error dialogs remain usable when a save or other operation fails.

## Agent Decisions / Thoughts

- **2026-08-20 Codex:** The runtime dialog uses `SetResourceReference` for its surface, border, opacity, title, message, and close-icon brushes so live settings previews and high-contrast mode are respected. Existing localized button styles and nullable close-result behavior remain unchanged.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Added mirror before the scoped theme-surface pass. | Codex |
| 2026-08-20 | Replaced fixed dialog colors with dynamic theme resources and added a keyboard-focus ring to the close action. | Codex |
| 2026-08-21 | Added lifecycle-bound refresh for catalog-backed runtime dialog strings. | Codex |
| 2026-08-21 | Added a template-factory regression contract after the save smoke exposed `TriggerCollection` mutation on a sealed template. | Codex |
