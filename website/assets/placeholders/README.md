# Optional website artwork

The landing page is intentionally self-contained: its notebook, paper texture, and annotation preview are built with CSS, inline SVG, and a small canvas demo so GitHub Pages does not depend on a CDN or an external image host.

If the project later adds approved product photography or screenshots, keep them in this directory and use these exact names. The landing page probes each file at runtime; when a file is absent, its filename, recommended size, and purpose remain visible as a designed placeholder.

| File | Suggested use | Suggested size | Alt text |
| --- | --- | --- | --- |
| `hero-editor.webp` | Full desktop workspace detail | 1600 × 1000 | OpenNotes PDF workspace with handwritten annotations |
| `annotation-ink.webp` | Close-up of pen and highlighter tools | 1200 × 800 | OpenNotes ink and selection tools |
| `textbox-resize.webp` | Text annotation resize interaction | 1200 × 800 | OpenNotes resizable text box |
| `dark-theme.webp` | Dark theme application chrome | 1200 × 800 | OpenNotes dark theme |
| `page-templates.webp` | Page template picker | 1200 × 800 | OpenNotes page templates |
| `opennotes-mark.svg` | Product mark / app identity | 512 × 512 | OpenNotes logo mark |

The page should continue to work when these optional files are absent. Do not add remote image URLs or fonts here.
