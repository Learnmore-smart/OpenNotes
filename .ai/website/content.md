# website/content.js
> Last updated: 2026-08-22（redesigned three-locale catalog completion） | Protection: STANDARD

## Purpose

Hold English, Simplified Chinese, and French landing-page copy and update `data-i18n`, `data-i18n-html`, `data-i18n-content` and localized ARIA-label nodes.

## Verification

The static checker reports 116 matching keys in each locale and validates meta content, visible copy, undo/clear/status messages, language/theme controls, the drag grip, eight resize-direction labels and accessibility labels.

Locale controls are selected with `button[data-locale]` so the root `<html data-locale>` state marker is never mistaken for a language button.

## Open Threads / Resume Context

- **Status:** complete
- English, Simplified Chinese and French catalogs now match the new thesis and section order; drag-grip accessible/status strings are present with exact key parity. Browser-language detection considers `navigator.languages` before falling back to English.
