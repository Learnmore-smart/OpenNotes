# OpenNotes UI and Website Redesign

## Status

Approved for direct implementation by the user on 2026-08-22. This specification narrows the existing tasks 41–48 to the remaining UI/UX and website redesign work; it does not reopen completed data migration, annotation persistence, or PDF architecture work.

## Product thesis

OpenNotes is a quiet Windows workbench for people who think on documents. The interface should feel like a working folio open on a blue-black desk: every surface has a material role, every accent resembles a mark the user could make, and the PDF remains visually dominant.

The website has one job: let a visitor understand and feel the annotation workflow before downloading. Its hero statement is **“Open a PDF. Leave a trace.”** The live page—not a screenshot card—is the first-viewport visual anchor.

## Visual system

### Palette

| Token | Role | Light | Dark |
|---|---|---:|---:|
| Desk | application/background depth | `#E6E1D8` | `#0C141D` |
| Paper | primary working surface | `#FFFDF7` | `#17212C` |
| Paper alt | secondary surface | `#F3EFE6` | `#1D2A37` |
| Ink | primary action/selection | `#1C5D99` | `#6EACEA` |
| Margin | orientation and destructive emphasis | `#B94B52` | `#ED7A80` |
| Mark | highlight/support | `#D9A72E` | `#F2C75C` |
| Graphite | main text | `#1E2933` | `#EEF2F4` |

High contrast continues to use system-legible black, white, yellow, and cyan. The desktop app publishes material tokens in addition to its stable generic `Theme*` resource keys. PDF bitmaps are never tinted.

### Typography

- Desktop UI: Segoe UI Variable/Segoe UI for native Windows clarity, with compact utility labels and no ornamental display face.
- Website display: Palatino Linotype/Iowan Old Style/Georgia fallbacks, used only for large editorial statements.
- Website body and controls: Segoe UI/system-ui. No external font or CDN dependency.

### Shape and depth

- Software chrome uses 8–14 px radii for controls and panels; paper previews may use 2–6 px corners.
- Shadows are reserved for floating toolbars, popovers, sheets, and the live folio. Routine content uses spacing and rules instead of cards.
- A thin red margin rail is an orientation device, not decoration. Blue remains the primary action color.

## Desktop application

### Shell

- Preserve the single `MainWindow` + Frame/tab architecture.
- Strengthen the distinction between title bar, tab rail, and working canvas through the new desk/paper tokens.
- Give keyboard focus a stable two-pixel ink ring. Pressed and selected states must not rely on color alone.
- Keep title-bar and navigation targets at least 32 DIP and avoid hover-induced geometry changes.

### Home surface

- Present the title block as a document index with a short margin rail.
- Replace remaining fixed light-only chrome values with semantic theme resources.
- Keep PDF/folder artwork content-specific and do not recolor thumbnails.
- Preserve selection, drag/drop, file/folder navigation, and current bindings.

### Editor

- The PDF remains the dominant, untinted paper object on the desk canvas.
- The floating toolbar uses the paper surface and ink focus states; fixed tool colors map to semantic Ink, Mark, Margin, and subtle-foreground resources.
- Sidebar/search/loading chrome follows the shared tokens. Existing undo, persistence, pointer, text resize, and UI Automation behavior is unchanged.

### Settings and template picker

- Settings becomes a single calm settings sheet: one outer paper surface, internal sections separated by rules and spacing rather than stacked dashboard cards.
- Template previews remain visibly paper-like in all modes and use the margin/ink/mark tokens for notebook structure.
- Existing localization, preview/cancel semantics, ComboBox z-order handling, and template mappings remain unchanged.

## Website information architecture

1. **Header:** compact brand, Method/Workspace/Download navigation, language and theme controls.
2. **Hero / live folio:** exact statement “Open a PDF. Leave a trace.”, download/repository actions, interactive annotation paper, and a three-part Open/Mark/Keep index.
3. **Method:** one editorial sequence explaining direct ink, page navigation, and portable files without a card grid.
4. **Workspace anatomy:** a paper-led composition connecting actual controls to outcomes.
5. **Evidence drawer:** the six optional artwork slots, compact and clearly documented when assets are absent.
6. **Principles:** Paper logic, direct hands, honest files.
7. **Download folio:** decisive Windows download action and repository link.

The page must not use a generic SaaS card wall, pricing, testimonials, logo clouds, decorative gradients, or scroll hijacking.

## Website interaction

- Pen, highlighter, and eraser track the pointer one-to-one with `setPointerCapture`.
- Undo and clear operate on a bounded in-memory history.
- The text note is editable, draggable by a dedicated localized grip, and resizable from eight handles.
- Every resize handle is a real button and supports direction-aware arrow-key resizing; Shift increases the step.
- Theme and locale changes update visible copy, ARIA labels, status messages, and metadata without reload.
- Motion is limited to one hero arrival, one scroll-aware header/material change, and local control feedback. All motion has a reduced-motion equivalent; transparency has a solid fallback.

## Responsive and accessibility requirements

- No horizontal overflow at 320/375/768/1024/1440 CSS pixels.
- Header + hero fit the first viewport at common desktop and mobile sizes without hiding the primary action.
- Keyboard order follows visual order; skip link, visible focus, semantic landmarks, polite status announcements, and descriptive image alternatives are required.
- Minimum normal-text contrast is 4.5:1. Color is never the sole state indicator.
- Pointer capture must be released on pointer up/cancel. Animations remain interruptible because no input is disabled during transitions.

## Verification

- Theme unit/source contracts first fail, then pass with the new material tokens and XAML usage.
- Website static checker first fails, then passes with the new hero, drag grip, required controls, locale parity, and deployment paths.
- `node --check` for both website scripts.
- Browser tests at 375, 768, and 1440 px for theme, locale, drawing, undo/clear, text drag, eight-way pointer resize, keyboard resize, focus, reduced motion, and 404.
- `dotnet build OpenNotes.sln --no-restore` and `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore`.
- `tools/verify-i18n.ps1`, `tools/check-website.ps1`, and `git diff --check`.

## Non-goals and invariants

- Do not change the Caelum namespace/data folder/AppX compatibility identifiers.
- Do not alter annotation coordinates, strip-and-rebuild PDF loading, atomic save replacement, or the `IUndoAction` stack.
- Do not reset the dirty working tree, remove the historical migration backup, change repository identity, or touch conversation bodies/authentication/logs.
- Do not introduce a framework, package manager, CDN, analytics, or network dependency for the static website.
