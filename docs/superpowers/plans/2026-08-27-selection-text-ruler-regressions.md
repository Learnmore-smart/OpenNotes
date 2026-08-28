# Selection, Text Formatting, and Ruler Regression Fix Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore reliable page-local selection, allow font-family/alignment ComboBox choices to commit, and make the on-screen ruler constrain strokes instead of letting ink pass through its body.

**Architecture:** Selection keeps its working page/mouse route while pen gestures capture the stylus route and every cancellation clears delegated state. Text ComboBox dropdown descendants will be recognized as editor-owned transient UI so preview dismissal cannot close them before `SelectionChanged`. Ruler geometry will expose both long edges and its body; collected ink will snap to the nearest edge when drawn alongside it or be clipped at first body entry when drawn toward it.

**Tech Stack:** .NET 8, WPF, NUnit, WPF routed input, `System.Windows.Ink`.

---

### Task 1: Restore the production selection route

**Files:**
- Modify: `OpenNotes.Tests/ShapeSelectionTests.cs`
- Modify: `Pages/EditorPage.xaml.cs`

- [x] Add regressions that verify cancellation clears delegated state and stylus selection captures/releases the stylus route.
- [x] Run `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ShapeSelectionTests` and confirm the new route assertion fails.
- [x] Capture the originating input kind in `PdfPageControl` and clear editor delegation state from `CancelInteraction`; preserve the working mouse and Ctrl-click route.
- [x] Rerun the focused selection tests and require all cases to pass.

### Task 2: Let text ComboBox item clicks commit

**Files:**
- Modify: `OpenNotes.Tests/EditorPopupDismissalTests.cs`
- Modify: `Pages/EditorPage.xaml.cs`

- [x] Add an STA regression that opens each text ComboBox, supplies a generated `ComboBoxItem` as the pointer source, and asserts `ShouldClosePopupOnPointerDown` returns false.
- [x] Run the focused popup test and confirm it fails because registered ComboBox dropdown content is currently classified as outside UI.
- [x] Add a narrow owner lookup using `ItemsControl.ItemsControlFromItemContainer`; exempt only `_textFontFamilyCombo` and `_textAlignmentCombo` dropdown descendants before outside-click dismissal.
- [x] Verify selecting `Arial` and centered alignment invokes the existing `ApplySelectedTextFormat` history/dirty path once.

### Task 3: Make the ruler block and guide ink

**Files:**
- Create: `OpenNotes.Tests/RulerInteractionTests.cs`
- Modify: `Pages/EditorPage.xaml.cs`
- Modify: `Controls/PdfPageControl.xaml.cs`

- [x] Add RED geometry tests: a perpendicular stroke stops at the near ruler edge, a stroke starting inside the body is rejected, and an outside parallel stroke snaps to the nearer long edge.
- [x] Replace the single-edge provider with a four-corner ruler geometry provider translated into page coordinates at collection time.
- [x] In `InkCanvas_StrokeCollected`, constrain before Shift/smoothing: clip at the first body intersection, discard a stroke beginning inside the ruler, otherwise project an entirely near-edge stroke onto the closest long edge.
- [x] Run `RulerInteractionTests` and the existing popup/selection/shape/history tests.

### Task 4: Integrated verification and v5.2.4 release

**Files:**
- Modify: version surfaces and their `.ai` mirrors
- Modify: `.ai/PROJECT_CONTEXT.md`

- [x] Run the complete Release test suite, Release build, i18n verifier, and `git diff --check`.
- [ ] Publish and launch the self-contained executable with isolated data, then build and silently install/launch/uninstall the installer.
- [ ] Commit, fast-forward `main`, push, tag `v5.2.4`, wait for GitHub Actions, verify both public asset hashes, and launch the GitHub-downloaded executable.
