# OpenNotes.Tests/ThemeSurfaceSourceTests.cs
> Last updated: 2026-08-24 (Wave5 RED/GREEN source, runtime, and PDF pixel contracts) | Protection: STANDARD

## Purpose

Guard the visual boundary between the editor workspace/backdrop and the rendered PDF page. The source contract is intentionally conservative: shell/scroll/page-surround surfaces use dynamic semantic resources, while `PdfImage` and `PdfImageOverlay` remain opaque bitmap hosts with no tint/effect/color-matrix/overlay brush.

## Open Threads / Resume Context

- **Status:** complete for Wave5 automated scope.
- V5.1.1 keeps Neutral as the persisted/default backdrop identifier but maps its Light surround to white, while Paper/Slate remain explicit alternate decorations and the PDF render-byte contract remains unchanged.
- **Intent/result:** RED-first coverage now verifies neutral palette declarations, semantic token coverage, Settings sizing/scroll/backdrop controls, runtime DynamicResource usage, and a known PDF render hash/byte sequence across Neutral/Paper/Slate.
- The source verifier also rejects the retired warm Light literals in production chrome so a future palette edit cannot silently restore the screenshot defect.
- Desktop screenshot/foreground/device visual review remains an external manual boundary; no screenshot PASS is claimed here.

## Important Notes / NEVER Change

- Do not add a brush, opacity tint, effect, or color matrix to PDF image/page layers.
- Tests are source contracts, not a substitute for desktop pixel/UIA evidence.

## Verification

- Focused `ThemeServiceTests|ThemeSurfaceSourceTests` passed 16/16 after the render-hash regression was added.
- The known blank-PDF page is rendered before and after all three backdrop choices; SHA-256 and PNG bytes remain identical.
- The source contract also requires the actual-system HighContrast branch to publish `SystemColors` window/highlight brushes while explicit non-system HighContrast retains the deterministic readable fallback palette used by existing navigation tests.
- The review follow-up complements the service hash with a real WPF `PdfPageControl` composition: a known dark bitmap pixel and crimson annotation are rendered inside an outer workspace Border for Neutral/Paper/Slate, and only the page crop is compared. `PdfImage`/`PdfImageOverlay` opacity/effect/source stay unchanged; HC decoration is intentionally outside this page contract.
