# website/theme.css
> Last updated: 2026-08-22（annotated paper archive redesign） | Protection: STANDARD

## Purpose

Own the landing page visual language: dark desk, paper sheet, blue ink, red margin rail, editorial typography, responsive layout, and accessibility states.

## Important Notes

- No external fonts or CDN dependencies.
- Include visible focus, reduced motion, reduced transparency, and high contrast support.
- Keep anchor targets clear of the sticky header with `scroll-margin-top`.
- Keep all eight text-box resize handles visibly focusable and preserve readable focus/contrast states for the live demo.
- Keep the `data-theme="light"` palette intentional and legible; the dark ink-desk palette remains the default.
- Avoid a hard body minimum width; long placeholder statuses wrap inside their captions so 320–375px viewports do not overflow horizontally.

## Open Threads / Resume Context

- **Status:** complete
- The live-folio hierarchy uses Desk `#0C141D`, Paper `#FFFDF7`, Ink `#1C5D99`, Margin `#B94B52`, and Mark `#D9A72E` as its source of truth. Focus, reduced-motion/transparency, high-contrast and 375px narrow-viewport rules remain explicit; headings balance, pointer controls use `touch-action: manipulation`, and numerical metadata uses tabular figures.
