# tools/Test-OpenNotesAdvancedPointerSmoke.ps1
> Last updated: 2026-08-22 | Protection: STANDARD

## Purpose

Provide an independent, isolated desktop smoke for editor paths that are not covered by `Test-OpenNotesPointerSmoke.ps1`: real shape dragging, Hidden Ink persistence/reveal where the current UI path is automatable, and selection/copy/paste where the editor exposes a reliable pointer/keyboard route.

## Constraints

- This file is validation-only. Do not change WPF product source, models, or runtime behavior to make the smoke pass.
- Use a generated temporary PDF and isolated `LOCALAPPDATA`, `APPDATA`, and `OPENNOTES_DATA_ROOT` values.
- Use real screen pointer and keyboard input through the child OpenNotes window. Do not replace editor interaction with direct model calls or synthetic in-process events.
- Do not access or modify Codex AppData, authentication, logs, user documents, or the normal Caelum data root.
- Do not delete user files. Cleanup is limited to exact temporary paths created by this smoke under the system temp directory and the child process it started.
- Report unsupported or unreliable interaction paths as unverified; never emit a PASS from source inspection alone.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** Add a real-pointer/keyboard regression smoke for shape, Hidden Ink, and selection/clipboard behavior without changing WPF product behavior.
- **Next steps:** 1) keep all screen-input phases foreground-verified; 2) use page-image bounds for pixel probes; 3) require a fresh clipboard payload and shape geometry; 4) run PowerShell parsing and an isolated desktop smoke; 5) leave any hardware, cross-page, or third-party-viewer gaps explicitly unclaimed.
- **Blockers / notes:** The current host may reject physical cursor injection or clipboard ownership. The script must distinguish physical input from any fallback and fail/mark unverified if the requested path cannot be observed reliably. `window-message` is intentionally rejected for real-pointer claims. Hidden Ink uses a three-phase base/mask/reveal/restore pixel comparison and a page-surface bound, rather than independent brightness thresholds. The first live run exposed and fixed a PowerShell pipeline-capture bug in the toolbar logging helper; the returned pointer mode is now kept separate from its diagnostic line.

## Agent Decisions / Thoughts

- **2026-08-22:** Keep this separate from the existing pointer smoke so a failure in shape/Hidden Ink/clipboard behavior cannot be hidden by the already-green pen/text coverage. Prefer saved-PDF markers/counts and observable UI state over private model inspection.
- **2026-08-22:** Toolbar pointer diagnostics use `Write-Host` so callers receive only the mode string; mixing log text with a return value caused a false script-side failure on the first live run.
- **2026-08-22:** Strengthened the evidence boundary after read-only review: physical input now requires confirmed foreground ownership; all drawing, selection and paste coordinates use the runtime `PdfPageControl.{i}` bounds (with a rendered-image fallback, never the outer viewer alone); popup-owning tool buttons receive a harmless dismissal tap before drag phases; the Hidden Ink probe checks phase-to-phase colour distance; Ctrl+C clears the old clipboard first and validates a two-point line-shape payload before paste.

## Important Notes / NEVER Change

- Preserve the strip-and-rebuild PDF architecture, existing test data-root override, and the existing pointer smoke's cleanup boundaries.
- Do not claim stylus behavior from mouse input, and do not claim full selection semantics if only a source-level route can be inspected.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-22 | Separated toolbar diagnostics from the returned pointer mode after the first real run found pipeline capture ambiguity. | Codex |
| 2026-08-22 | Added foreground, page-boundary, Hidden Ink phase, fresh-clipboard and shape-geometry assertions after the reliability audit. | Codex |
| 2026-08-22 | Created the validation-only handoff before implementing the advanced desktop smoke. | Codex |
