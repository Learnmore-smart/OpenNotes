# website/index.html
> Last updated: 2026-08-22（live-folio website redesign completion） | Protection: STANDARD

## Purpose

Static GitHub Pages editorial landing page for OpenNotes with a live annotation preview.

## Important Notes

- Use relative URLs for local assets because the site is served under the project page path `/OpenNotes/`; GitHub links must use the canonical `Learnmore-smart/OpenNotes` repository.
- Keep the page content-first and notebook-specific; do not introduce generic SaaS pricing/testimonial sections.
- The first viewport is anchored by the exact thesis “Open a PDF. Leave a trace.” and a live folio. The preview includes pen/highlighter/eraser canvas input, undo/clear controls, an editable text note, a dedicated pointer/keyboard drag grip, eight pointer-resizable handles, direction-aware keyboard arrow resizing, language switching and theme switching.
- The header theme control switches the full page between the ink-desk dark palette and a warm paper-light palette, while the language controls remain independent.
- Interactive controls expose localized labels/ARIA state; the status region is polite/live and artwork slots show designed placeholders until optional local files are added.
- The canvas is keyboard focusable and the editable note exposes `aria-multiline`; `.nojekyll` is part of the checked static publish set.

## Open Threads / Resume Context

- **Status:** complete
- The page is recomposed into hero/live folio, method, workspace anatomy, evidence drawer, principles and download sections. Relative links, all six optional artwork filenames, three locales and every annotation control are preserved.
- **Brand asset pass:** in progress; replace the hand-authored header glyph with the supplied optimized OpenNotes 96px favicon PNG and add full relative SVG/ICO/manifest metadata without changing layout or interaction behavior.

## Verification

The static contract passes. Playwright passes at 375, 768 and 1440px with no horizontal overflow and covers locale/theme switching, drawing/undo, keyboard text movement, keyboard resize, all eight handles, reduced motion and 404.
