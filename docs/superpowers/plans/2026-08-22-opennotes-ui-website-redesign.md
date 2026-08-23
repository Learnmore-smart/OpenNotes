# OpenNotes UI and Website Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a coherent paper/ink desktop theme and a fully re-authored, interactive, responsive OpenNotes landing page without regressing PDF, localization, or persistence behavior.

**Architecture:** Extend the existing WPF runtime resource palette with stable semantic material tokens, then consume those tokens in the existing shell/pages without changing navigation or annotation architecture. Recompose the existing dependency-free static website around its live demo, retaining the current localization and pointer-event modules while adding a dedicated text drag interaction and stronger automated contracts.

**Tech Stack:** .NET 8 WPF, NUnit, HTML5, CSS custom properties, vanilla JavaScript Pointer Events, PowerShell static verification, Playwright-compatible browser tests.

---

### Task 1: Lock the material theme contract

**Files:**
- Modify: `OpenNotes.Tests/ThemeServiceTests.cs`
- Modify: `.ai/OpenNotes.Tests/ThemeServiceTests.md`
- Modify: `Services/ThemeService.cs`
- Modify: `.ai/Services/ThemeService.md`

- [x] Add expected light/dark values for `ThemeDeskBrush`, `ThemePaperBrush`, `ThemePaperAltBrush`, `ThemeInkBrush`, `ThemeMarginBrush`, and `ThemeMarkBrush`; assert the complete resource count.
- [x] Run `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter ThemeServiceTests` and confirm the missing-token failure.
- [x] Publish the six frozen brushes in every palette and align the generic chrome palette with the approved warm-paper/blue-black colors.
- [x] Rerun the filtered tests and confirm all theme tests pass.

### Task 2: Apply the desktop visual hierarchy

**Files:**
- Modify: `OpenNotes.Tests/ThemeServiceTests.cs`
- Modify: `App.xaml`
- Modify: `MainWindow.xaml`
- Modify: `Pages/HomePage.xaml`
- Modify: `Pages/EditorPage.xaml`
- Modify: `SettingsWindow.xaml`
- Modify: `PageTemplatePickerWindow.xaml`
- Modify mirrors under `.ai/` for every changed source file.

- [x] Add source contracts requiring semantic paper/desk/ink/margin/mark tokens in the shell, home surface, editor toolbar, settings sheet, and template previews.
- [x] Run the filtered tests and confirm the source contracts fail against the old XAML.
- [x] Replace fixed chrome colors, add the title/home margin orientation rail, and map tool/support colors to semantic resources while preserving names, event handlers, bindings, focus, and automation IDs.
- [x] Rerun the filtered tests, then build `OpenNotes.sln --no-restore`.

### Task 3: Lock the redesigned website contract

**Files:**
- Modify: `tools/check-website.ps1`
- Modify: `.ai/tools/check-website.md`
- Modify: `website/index.html`
- Modify: `website/content.js`
- Modify: `website/demo.js`
- Modify: `website/theme.css`
- Modify the corresponding `.ai/website/*.md` mirrors.

- [x] Extend the static checker to require the exact hero thesis, semantic live-folio structure, text drag grip, eight resize handles, theme/locale controls, relative URLs, and identical en/zh/fr key sets.
- [x] Run `powershell -ExecutionPolicy Bypass -File tools/check-website.ps1 -WorkflowPath .github/workflows/pages.yml` and confirm it fails on the new requirements.
- [x] Recompose the HTML into Header → Live folio → Method → Workspace anatomy → Evidence drawer → Principles → Download, retaining all required artwork filenames.
- [x] Rewrite CSS from the approved token system with a full-bleed first viewport, cardless sections, mobile layouts, visible focus, reduced motion/transparency, and high contrast.
- [x] Update all three locale catalogs and metadata with matched placeholders and the exact localized control labels.

### Task 4: Add direct text movement and preserve drawing behavior

**Files:**
- Modify: `website/demo.js`
- Modify: `website/index.html`
- Modify: `website/content.js`
- Modify: `website/theme.css`
- Test: `tools/check-website.ps1` and browser interaction script used by the repository.

- [x] Add a failing static/browser contract for a localized `data-demo-drag` grip that moves the text box with pointer capture and stays inside the paper.
- [x] Implement pointer-down/move/up/cancel drag state with grab-offset preservation and no input lockout.
- [x] Keep content editing separate from dragging, and retain all existing resize, keyboard, undo, clear, locale, theme, and optional-artwork behavior.
- [x] Run `node --check website/content.js` and `node --check website/demo.js` plus the static checker.

### Task 5: Browser and desktop regression pass

**Files:**
- Modify only files implicated by failing evidence.

- [x] Capture and inspect full-page screenshots at 375, 768, and 1440 px in dark and light themes.
- [x] Exercise keyboard navigation, language changes, theme changes, drawing, erasing, undo, clear, text editing, text dragging, all pointer handles, and keyboard resize.
- [x] Verify reduced-motion behavior and absence of horizontal overflow or console errors.
- [x] Run the i18n verifier, website checker, full build, full test suite, and `git diff --check`; fix every in-scope failure.

### Task 6: Documentation closure

**Files:**
- Modify: `.ai/PROJECT_CONTEXT.md`
- Modify: `docs/checklist.md`
- Modify: `docs/spec.md`
- Modify: `docs/tasks.md`
- Modify: `.trae/specs/**` only where a task status is demonstrably stale.

- [x] Record final visual decisions and verified commands in each changed file mirror.
- [x] Reconcile tasks 0–48 against fresh evidence without marking external/manual checks complete unless they actually ran.
- [x] Review the final diff for unrelated edits, placeholders, broken internal links, and accidental compatibility changes.
