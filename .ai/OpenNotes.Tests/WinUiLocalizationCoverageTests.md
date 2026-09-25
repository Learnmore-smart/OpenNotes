# OpenNotes.Tests/WinUiLocalizationCoverageTests.cs
> Last updated: 2026-09-25 (V6 Task 9 Phase B — WinUI i18n coverage) | Protection: STANDARD

## Purpose
Headless localization coverage for the WinUI shell: scans every
`OpenNotes.WinUI/**/*.cs` file (excluding `bin`/`obj`) for literal
`LocalizationService.Get("Key")`/`Format("Key")`/`GetForLanguage("Key")`
call sites, then asserts each referenced key exists in
`LocalizationService.GetCatalog()` and resolves non-empty in English,
Chinese and French. This is the complement to `LocalizationCoverageTests`
(which proves every catalog ENTRY carries three translations) — this test
proves no WinUI call site names a MISSING key.

Three passes (all literal-only; key shape `[A-Za-z0-9_.]+`):
1. `LiteralKeyCall` — key is the first argument literal.
2. `CallSiteStart` + `ArgumentSpan` — balanced-paren scan of each call's
   argument expression for DOTTED literals (ternary operands like the
   picker's `Get(mode ? "Home.X" : "Editor.Y")`, invisible to pass 1).
   The span walker skips string contents so a `)` inside a literal or a
   nested call can't truncate it; a dotted non-key literal would
   false-positive (none exist today).
3. `TemplateOptionTuple` — key-table indirection no call-site regex can
   see (`PageTemplatePickerDialog.TemplateOptions` stores title/hint keys
   in tuple literals consumed later via `Get(titleKey)`).

Catches drift like the pre-existing `Editor.ColorTooltip` fallback key that
was referenced by `EditorPage` but absent from the catalog until T9-B.

## Change History
- 2026-09-25 Task 9 Phase B quality pass: passes 2+3 added (computed-arg
  ternary keys via balanced-paren `ArgumentSpan` — the naive `;`-sliced
  version false-positived nested literals like AutomationIds — and the
  `TemplateOptions` tuple scan); would have caught the
  `Editor.PageTemplateTitle`/`Subtitle` crash keys. | Devin
- 2026-09-25 Task 9 Phase B: initial test. | Devin
