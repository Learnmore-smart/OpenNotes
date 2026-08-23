# PageTemplatePickerWindow

## Purpose

Modal picker used by page insertion and notebook creation. It returns a `PageInsertTemplate` value and, in notebook mode, a destination folder.

## Constraints

- Keep the picker usable for both insertion and notebook creation.
- Template cards must map one-to-one to `PageInsertTemplate` values.
- PDF drawing remains in `PdfService`; this window only selects a value.

## Open Threads

- **Status: complete for the paper/ink redesign.** The picker code-behind remains unchanged; global `ThemeService` resources drive the XAML surface.

## Agent Decisions / Thoughts

- **2026-08-20 Codex:** Paper previews intentionally use the existing semantic theme tokens rather than fixed white/blue/red values: surface and border preserve the paper structure, while accent/selection tokens keep notebook and grid landmarks visible in dark and high-contrast palettes.
- **2026-08-20 Codex:** Card, icon, and close-button templates expose `ThemeFocusBrush` with a two-pixel keyboard focus ring; hover and pressed states use the control hover/pressed tokens.
- **2026-08-22 Codex:** The window root and previews now use semantic Paper/PaperAlt/Ink/Margin resources with tighter physical-paper corners; template mapping and selection behavior are unchanged.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Converted picker surfaces, paper previews, and interaction states to dynamic theme resources with focus contrast. | Codex |
| 2026-08-22 | Aligned page previews with the new Desk/Paper/Ink/Margin material system. | Codex |

## V5 Completion Status

- Dotted, Music and Cornell cards map to the corresponding `PageInsertTemplate` values and are available to both page insertion and notebook creation flows.
