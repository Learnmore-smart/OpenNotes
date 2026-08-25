# Editor Toolbar and Shape Picker Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a coherent Lucide-based editor toolbar, localized custom tooltips, an invisible sidebar resize rail, and a nine-shape picker with real drawable polygon support.

**Architecture:** Retain the existing WPF controls and command/data paths. Restrict visual changes to `EditorPage` resources/markup and shape-picker construction; extend `PdfPageControl.BuildShapeOutline` so new menu choices commit through the existing ink pipeline. Localization remains catalog-driven.

**Tech Stack:** .NET 8, WPF/XAML, NUnit, the repository's font-independent Lucide vector renderer.

---

### Task 1: Lock the visual regressions

**Files:**
- Modify: `OpenNotes.Tests/EditorToolbarVisualSourceTests.cs`
- Create: `OpenNotes.Tests/ShapeToolTests.cs`

- [x] Add source contracts that require a custom `ToolTip` template, reject the resize-thumb line and shape checkmark, require all nine localized shape IDs, and reject the doubled pen icon treatment.
- [x] Add reflection-based tests for `PdfPageControl.BuildShapeOutline` asserting Triangle, Diamond, Parallelogram, Pentagon, and Hexagon return closed point lists inside the drag bounds.
- [x] Run `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~EditorToolbarVisualSourceTests|FullyQualifiedName~ShapeToolTests" --verbosity quiet` and confirm the new assertions fail for the missing behavior.

### Task 2: Repair sidebar, toolbar, and tooltips

**Files:**
- Modify: `Pages/EditorPage.xaml`
- Modify: `Pages/EditorPage.xaml.cs`

- [x] Add the themed page-local `ToolTip` template and timing/placement setters; keep localized text supplied by `SetToolbarMetadata`.
- [x] Change `SidebarResizeThumbStyle` to a transparent hit target with no rendered divider.
- [x] Normalize toolbar `LucideIcon` instances to owner-foreground strokes, replace the doubled/tinted pen and highlighter glyphs with one icon plus a compact color bar, and update `UpdateToolIconColors` to color only those bars.
- [x] Run the focused test command and confirm only the still-unimplemented shape assertions remain red.

### Task 3: Expand the shape picker and drawing engine

**Files:**
- Modify: `Pages/EditorPage.xaml.cs`
- Modify: `Controls/PdfPageControl.xaml.cs`
- Modify: `Services/LocalizationService.cs`

- [x] Extend `ShapeKind` with Triangle, Diamond, Parallelogram, Pentagon, and Hexagon.
- [x] Build a 3×3 localized `UniformGrid` of vector toggles and remove the checkmark `Path`; keep `IsChecked`, border/background, active bar, keyboard behavior, ToolTip, Name, HelpText, and AutomationId.
- [x] Add preview geometries and production outline generation for the five polygons. Constrain their bounds under Shift and leave the ink commit/persistence pipeline unchanged.
- [x] Add EN/ZH/FR catalog entries for the five new names.
- [x] Run the focused test command and confirm it passes.

### Task 4: Verify and document

**Files:**
- Modify: `.ai/Pages/EditorPage.xaml.md`
- Modify: `.ai/Pages/EditorPage.md`
- Modify: `.ai/Controls/PdfPageControl.md`
- Modify: `.ai/Services/LocalizationService.md`
- Modify: `.ai/OpenNotes.Tests/EditorToolbarVisualSourceTests.md`
- Create: `.ai/OpenNotes.Tests/ShapeToolTests.md`
- Modify: `.ai/PROJECT_CONTEXT.md`

- [x] Run the full NUnit suite, Debug build, `tools/verify-i18n.ps1`, and `git diff --check`.
- [x] Inspect the final diff for unrelated or overwritten user changes.
- [x] Synchronize the file mirrors with the root causes, durable decisions, test evidence, and any remaining visual-smoke boundary.
