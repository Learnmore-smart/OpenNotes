# OpenNotes UI Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Repair the sixteen OpenNotes desktop UI, interaction, save-race, and accessibility regressions while preserving legacy data and the existing PDF/undo architecture.

**Architecture:** Work in serialized waves around the high-conflict WPF files. First make stroke replacement and save operations tokenized/serialized, then rebuild toolbar affordances, navigation, surfaces, transient focus, Sticky Note, and Hidden Ink behavior. Every production change starts with a focused failing test or source contract, and every wave ends with automated, integration, and manual/UIA evidence.

**Tech Stack:** .NET 8 WPF, C#, NUnit, PdfiumViewer, PdfSharpCore, PowerShell UI Automation/pointer smokes, apply_patch, and the existing three-language localization service.

---

## Execution guardrails

- This plan is implementation-only. The current design turn changes documentation, not production code, tests, installer output, or Git history.
- Execute waves in order. A file listed in more than one wave has serialized ownership; no parallel agent may edit it until the prior wave's focused tests and mirror update are complete.
- Before each source edit, read .ai/PROJECT_CONTEXT.md and the matching .ai/<source-path>.md. After each source edit, update that mirror and leave an accurate Open Thread.
- Production code is strictly TDD: write the failing test/source contract, run it and record the expected failure, implement the smallest change, rerun the focused test, then run the wave integration check.
- Do not use git reset, git checkout, broad deletion, worktree recreation, or any operation that discards existing user changes. Do not commit as part of these waves unless the parent explicitly chooses an integration step.
- Keep the following invariants in every wave: strip-and-rebuild PDF loading, owned-annotation prefixes, DIP coordinates, 72/96 save and 96/72 load transforms with Y inversion, temporary-file plus atomic File.Move(tempPath, filePath, true), IUndoAction command semantics, single MainWindow/Frame tabs, %LOCALAPPDATA%\\Caelum data, and untouched conversation/migration data.

## Serialized ownership map

| Wave | Exclusive high-conflict owners | Outcome |
|---|---|---|
| 0 | No production ownership; read-only baseline | Confirm current tests/build and freeze the evidence ledger. |
| 1 | Controls/PdfPageControl.xaml.cs, Pages/EditorPage.xaml.cs, Models/StrokeReplacementSnapshot.cs (new), Models/AppSettings.cs, Services/AppSettingsService.cs | Stable shape replacement undo/redo and legacy preset settings compatibility. |
| 2 | Services/PdfSaveCoordinator.cs (new), Services/PdfService.cs, Pages/EditorPage.xaml.cs, Controls/PdfPageControl.xaml.cs, Models/AnnotationModels.cs | Serialized PDF saves, coalesced autosave, gray Hidden Ink visuals, and no EOF corruption. |
| 3 | Pages/EditorPage.xaml, Pages/EditorPage.xaml.cs, Services/LocalizationService.cs, App.xaml | Real toolbar/dynamic previews, removal of obsolete entries and preset UI, tooltip/UIA metadata. |
| 4 | Pages/EditorPage.xaml, Pages/EditorPage.xaml.cs | Compact page jump and redesigned Pages/Outline/Bookmarks rail. |
| 5 | Services/ThemeService.cs, Models/AppSettings.cs, Services/AppSettingsService.cs, App.xaml, Pages/EditorPage.xaml, Controls/PdfPageControl.xaml, SettingsWindow.xaml(.cs) | Neutral light palette, optional backdrop, and an untinted PDF bitmap surface. |
| 6 | Controls/PdfPageControl.xaml(.cs), Pages/EditorPage.xaml.cs, MainWindow.xaml(.cs), Services/PopupZOrderHelper.cs | Visible/movable/persistent Sticky Note and deactivation closure of every transient UI. |
| 7 | Tests, smoke scripts, .ai/ mirrors, .ai/PROJECT_CONTEXT.md | Full regression, visual/UIA evidence, and documentation closure. |

If an emergency fix needs a file owned by an earlier wave, stop that wave, add a focused failing test, and rerun its complete evidence before continuing. Do not merge overlapping edits by hand without recording the ownership handoff.

## Wave 0: Establish the evidence ledger

**Files:**

- Read: .ai/PROJECT_CONTEXT.md and all matching mirrors for the wave-1 through wave-6 owners.
- Read: docs/superpowers/specs/2026-08-23-opennotes-ui-repair-design.md and the 2026-08-22 UI redesign spec/plan.
- Do not modify production code or tests in this wave.

- [x] **Step 1: Record the clean baseline without claiming the new repair.**

Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore
dotnet build OpenNotes.sln --no-restore
powershell -ExecutionPolicy Bypass -File tools/verify-i18n.ps1
git diff --check
~~~

Expected: the existing baseline evidence remains reproducible; any already-dirty worktree changes are reported, not reset. The new sixteen-item acceptance matrix is not marked complete by this baseline.

- [x] **Step 2: Inventory high-conflict symbols before editing.**

Confirm the current owners and roots in source: StrokeReplacedAction, ReplaceRecognizedStroke, AutoSaveAsync, SaveAnnotationsToPdfAsync, PresetSlotsPanel, CreateSelectionPopup, FitWidthButton, FitPageButton, AddShapeSubTypeSection, AddSizePreviewSection, AddStickyNote, ImageOverlayCanvas, and the existing SetHostActive lifecycle. Save the output as working notes only; do not change source.

- [x] **Step 3: Create a wave evidence ledger in the plan work log.**

For each wave record the exact focused command, expected RED/GREEN result, integration command, UIA/manual artifact name, and any external foreground/device blocker. A blocker remains unclaimed rather than being converted to a pass.

### Wave 0 evidence ledger — 2026-08-23

- **Scope/result:** Read-only baseline only. No production code or tests were edited. The sixteen-item repair acceptance matrix remains unclaimed; this ledger records reproducibility, not repair completion.
- **Focused command:** `N/A` for Wave 0 (no new contract is introduced in the read-only wave). The planned Wave 1–6 focused commands remain attached to their respective wave steps and were not run here.
- **Integration/baseline commands (fresh, exact results):**
  - `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore` — first Wave 0 run exit `0`; `100 passed`, `0 failed`, `0 skipped`; cold test compilation emitted two `NU1701` PdfiumViewer compatibility warnings and one `WFAC010` high-DPI manifest warning. The final post-sync rerun also exited `0` with `100 passed`, `0 failed`, `0 skipped`; incremental output emitted the two `NU1701` warnings only.
  - `dotnet build OpenNotes.sln --no-restore` — exit `0`; `0 errors`, `2 warnings` (the existing `NU1701` PdfiumViewer compatibility warnings, one per project).
  - `powershell -ExecutionPolicy Bypass -File tools/verify-i18n.ps1` — exit `0`; `273` catalog entries, `386` localization calls, `0` hard-coded visible strings; `PASS`.
  - `git diff --check` — exit `0`; one existing LF→CRLF working-copy warning for `.ai/PROJECT_CONTEXT.md`, no whitespace error.
- **Read-only high-conflict inventory:**
  - `Pages/EditorPage.xaml.cs`: `StrokeReplacedAction` (line 348; live `Stroke` constructor parameters), `AutoSaveAsync` (line 9177), `CreateSelectionPopup` (line 3450), `AddShapeSubTypeSection` (line 2803), `AddSizePreviewSection` (line 3371), `AddStickyNote` call sites (lines 9127/9429), and `SetHostActive` (line 6568).
  - `Controls/PdfPageControl.xaml.cs`: `ReplaceRecognizedStroke` (line 2099), `AddStickyNote` (line 3571), `ImageOverlayCanvas` container roots/references, and `SetHostActive` (line 146).
  - `Services/PdfService.cs`: `SaveAnnotationsToPdfAsync` (line 1271).
  - `Pages/EditorPage.xaml`: `PresetSlotsPanel` (line 232), `FitWidthButton` (line 361), `FitPageButton` (line 366), and `ImageOverlayCanvas` is declared in `Controls/PdfPageControl.xaml` (line 32).
  - `Pages/EditorPage.Utilities.cs`: `FitWidthButton`/`FitPageButton` localization roots (lines 93/97); `MainWindow.xaml.cs` calls `SetHostActive` for tab/window lifecycle.
  - New planned files `Models/StrokeReplacementSnapshot.cs` and `Services/PdfSaveCoordinator.cs` have no existing `.ai` mirror yet; no source file was created in Wave 0.
- **UIA/manual artifact:** none created or claimed; desktop UIA, pointer/device, visual, foreground, and third-party checks remain downstream evidence. No external blocker was encountered by the Wave 0 commands.
- **Worktree snapshot before/after Wave 0 documentation sync:** branch `main`; existing scoped state is `M .ai/PROJECT_CONTEXT.md`, `?? docs/superpowers/plans/2026-08-23-opennotes-ui-repair.md`, and `?? docs/superpowers/specs/2026-08-23-opennotes-ui-repair-design.md`. No reset, checkout, deletion, production/test edit, or commit was performed.

## Wave 1: Make shape recognition undo stable and preserve legacy preset data

**Files:**

- Create: Models/StrokeReplacementSnapshot.cs
- Modify: Controls/PdfPageControl.xaml.cs
- Modify: Pages/EditorPage.xaml.cs
- Modify: Services/AppSettingsService.cs
- Test: OpenNotes.Tests/ShapeRecognitionUndoTests.cs
- Test: OpenNotes.Tests/AppSettingsCompatibilityTests.cs
- Mirrors: matching .ai/ files for every source/test file

**Ownership:** Only this wave may touch the shape replacement and StrokeReplacedAction sections of PdfPageControl.xaml.cs/EditorPage.xaml.cs. Do not remove the visible preset controls here; Wave 3 owns that XAML and event cleanup. The settings field and JSON behavior are shared only through the tests below.

- [x] **Step 1: Define the failing replacement contract.**

Add ShapeRecognitionUndoTests with a pure, deterministic contract around the planned API:

~~~csharp
[Test]
public void Replacement_UndoRestoresOriginalAtOriginalIndex_WithoutAppending()
{
    var state = StrokeReplacementFixture.Recognize(index: 1);
    Assert.That(state.Undo(), Is.True);
    Assert.That(state.Strokes.Count, Is.EqualTo(2));
    Assert.That(state.Strokes[1].Token, Is.EqualTo(state.Original.Token));
    Assert.That(state.Strokes[1].Snapshot, Is.EqualTo(state.Original.Snapshot));
}

[Test]
public void Replacement_RedoAfterTheRestoredStrokeWasErased_IsNoOp()
{
    var state = StrokeReplacementFixture.Recognize(index: 0);
    Assert.That(state.Undo(), Is.True);
    state.EraseToken(state.Original.Token);
    Assert.That(state.Redo(), Is.False);
    Assert.That(state.Strokes, Is.Empty);
}

[Test]
public void Replacement_UndoRedoSequenceNeverDuplicatesOrThrows()
{
    var state = StrokeReplacementFixture.Recognize(index: 0);
    Assert.Multiple(() =>
    {
        Assert.That(state.Undo(), Is.True);
        Assert.That(state.Undo(), Is.False);
        Assert.That(state.Redo(), Is.True);
        Assert.That(state.Redo(), Is.False);
        Assert.That(state.Strokes.Count, Is.EqualTo(1));
    });
}
~~~

The fixture must use StrokeReplacementSnapshot/token behavior rather than a live WPF control. Add a source contract that StrokeReplacedAction contains no live Stroke fields and ReplaceRecognizedStroke does not append an ideal stroke when its original index/token is missing. The test intentionally fails because those types/contracts do not yet exist.

- [x] **Step 2: Run the focused RED tests.**

Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "ShapeRecognitionUndoTests"
~~~

Expected: compilation/source-contract failure naming the missing immutable replacement contract and/or the existing append/reference behavior. Do not weaken the assertions to match the current bug.

- [x] **Step 3: Define the immutable replacement and token behavior.**

Implement StrokeReplacementSnapshot with copied point coordinates, color/alpha, width/height, highlighter flag, and FitToCurve. Give each in-session stroke a Guid token; replacements inherit the token. Add a page-level quiet operation with this shape:

~~~csharp
bool TryReplaceStrokeQuiet(
    Guid token,
    StrokeReplacementSide expectedSide,
    StrokeReplacementSnapshot replacement,
    out int index);
~~~

The operation finds the token, verifies the expected side, replaces at the same collection index, and returns false when the token is absent. ReplaceRecognizedStroke must return a success flag and never call _strokes.Add(ideal). StrokeRecognizedEventArgs carries the token and snapshots. StrokeReplacedAction stores only token/snapshot/index data and calls the quiet operation for Undo/Redo; a user erase or another action can therefore make a later command safely no-op.

- [x] **Step 4: Add the failing legacy-settings compatibility assertions.**

AppSettingsCompatibilityTests must deserialize a legacy JSON payload containing the three PenPresets entries, sanitize/clone it, save a copy through the existing service test root, and assert all three Tool, ColorHex, and Size values remain. A payload with no PenPresets must load as an empty list without triggering UI default insertion. The test also asserts AppSettingsService.Sanitize does not mutate the input list. Run it before implementation:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "AppSettingsCompatibilityTests"
~~~

Expected: RED if the service currently normalizes/fills/removes the legacy list or if the new round-trip contract is absent.

- [x] **Step 5: Preserve settings while removing only initialization side effects.**

Keep AppSettings.PenPresets and its JSON property name. Make Sanitize/Clone deep-copy entries and preserve supported values; do not populate three defaults merely because the list is empty. The visible slot removal is deliberately deferred to Wave 3. Do not move the data root from %LOCALAPPDATA%\\Caelum.

- [x] **Step 6: Run Wave 1 focused and existing annotation tests.**

Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "ShapeRecognitionUndoTests|AppSettingsCompatibilityTests|HiddenInkTests|PdfServiceAnnotationParsingTests"
~~~

Expected: all focused tests pass; no annotation ownership or coordinate tests change. Update the matching .ai mirrors with the token/no-append rationale and the legacy preset compatibility note before releasing file ownership.

- [ ] **Step 7: Capture the first shape integration evidence.**

Extend or create tools/Test-OpenNotesShapeUndoSmoke.ps1 to open an isolated one-page PDF, enable Shape Recognition, draw a line/rectangle, perform undo/redo and erase at the restored location, then inspect the sidecar/PDF annotation count. Run it with tools/Test-OpenNotesPointerSmoke.ps1 when the environment allows pointer injection. Expected evidence names: SHAPE_UNDO_RESULT=PASS, no duplicate ideal stroke, and no unhandled exception. If foreground ownership blocks input, record the blocker and leave the manual acceptance open.

### Wave 1 evidence ledger — 2026-08-23

- **RED:** `dotnet test OpenNotes.Tests\\OpenNotes.Tests.csproj --no-restore --filter "ShapeRecognitionUndoTests"` and the same `AppSettingsCompatibilityTests` filter initially exited `1` at the expected missing `StrokeReplacementState`/`StrokeReplacementSnapshot` compile contract. The first AppSettings attempt also exposed an invalid test-fixture enum string; the fixture was corrected before GREEN and no production behavior was weakened.
- **GREEN focused:** `dotnet test OpenNotes.Tests\\OpenNotes.Tests.csproj --no-restore --filter "ShapeRecognitionUndoTests"` — exit `0`, `4 passed`, `0 failed`, `0 skipped`; `AppSettingsCompatibilityTests` — exit `0`, `3 passed`, `0 failed`, `0 skipped`.
- **GREEN integration:** `dotnet test OpenNotes.Tests\\OpenNotes.Tests.csproj --no-restore --filter "ShapeRecognitionUndoTests|AppSettingsCompatibilityTests|HiddenInkTests|PdfServiceAnnotationParsingTests|PdfServiceAnnotationSavingTests"` — exit `0`, `31 passed`, `0 failed`, `0 skipped`.
- **Build:** `dotnet build OpenNotes.sln --no-restore` — exit `0`, `0 errors`, `2 existing NU1701 PdfiumViewer compatibility warnings`.
- **Implementation:** `Models/StrokeReplacementSnapshot.cs` now owns immutable points/style/token/side state; `PdfPageControl` tracks reference-identity stroke tokens, replaces in place through `TryReplaceStrokeQuiet`, and emits snapshot-only recognition events; `EditorPage.StrokeReplacedAction` stores token/snapshots/index and performs quiet no-op-safe undo/redo. `InitializePenPresetSlots` no longer writes three defaults into an empty legacy list; visible preset UI remains owned by Wave 3.
- **Unclaimed integration evidence:** `tools/Test-OpenNotesShapeUndoSmoke.ps1` is not present in the baseline and was not created/run in this Wave 1 code slice; the dedicated shape-recognition pointer/device smoke remains pending. The existing `tools/Test-OpenNotesPointerSmoke.ps1` was attempted and exited `1` before drawing because `FOREGROUND_TARGET_SET=False` and the Text tool stayed `Off`. Do not claim `SHAPE_UNDO_RESULT=PASS`; foreground/device and manual erase evidence remain open for the later smoke pass.

### Wave 1 quality follow-up evidence ledger — 2026-08-23

- [x] **TDD RED:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "StrokeReplacementProductionTests|AppSettingsCompatibilityTests"` was run before the production placement/root fixes. It exited `1` only on the intended missing production contract (`PdfPageControl.CaptureStrokePlacement` and `StrokePlacement`); test typos were corrected before implementation and no production code was edited before this clean RED.
- [x] **Focused GREEN:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "StrokeReplacementProductionTests"` — exit `0`, `5 passed`, `0 failed`, `0 skipped`; `dotnet test ... --filter "StrokeReplacementProductionTests|ShapeRecognitionUndoTests|AppSettingsCompatibilityTests"` — exit `0`, `13 passed`, `0 failed`, `0 skipped` (shape 4, settings 4, production 5).
- [x] **31-test integration GREEN:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "HiddenInkTests|PdfServiceAnnotationParsingTests|PdfServiceAnnotationSavingTests|ShapeRecognitionUndoTests|FullyQualifiedName~LegacyThreePenPresetsSurviveDeserializeSanitizeCloneAndSave|FullyQualifiedName~MissingOrEmptyPenPresetsRemainEmptyAndDoNotTriggerUiDefaultWrite|FullyQualifiedName~SanitizeDeepCopiesPenPresetEntriesAndDoesNotMutateInputList"` — exit `0`, `31 passed`, `0 failed`, `0 skipped`. The full named Wave 1 annotation/settings filter also passes `32/32` after adding the required per-operation-root regression.
- [x] **Full/build GREEN:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore` — exit `0`, `113 passed`, `0 failed`, `0 skipped`; `dotnet build OpenNotes.sln --no-restore` — exit `0`, `0 errors`, `2 existing NU1701` warnings.
- [x] **Production decisions:** `PdfPageControl` reuses `StrokeReplacementState`; quiet replacement never appends on a stale token/side; placement records preserve stable token/side/index/page ownership through erase/delete/cross-page actions; snapshots preserve point pressure and `IgnorePressure`; `GetStrokes()` keeps the historical return type but returns a defensive copy; `AppSettingsService` resolves the root per operation.
- [x] **Mirrors/context:** Updated the matching `.ai` mirrors for the new model, production/test files, settings model/service, page/control behavior, `PROJECT_CONTEXT.md`, and this evidence ledger.
- [ ] **Pointer/manual smoke:** `powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesPointerSmoke.ps1` was rerun and exited `1`: `FOREGROUND_TARGET_SET=False`, foreground PID `0`, Text tool remained `Off`, and the script stopped before drawing. `tools/Test-OpenNotesShapeUndoSmoke.ps1` is absent (`SHAPE_SMOKE_ABSENT`). Do not claim `SHAPE_UNDO_RESULT=PASS`; this remains an external foreground/device blocker.

### Wave 1 stale-placement P1 follow-up evidence ledger — 2026-08-23

- [x] **TDD RED:** Added three STA/WPF tests that invoke the real private `EditorPage.StrokeReplacedAction`, `StrokesErasedAction`, `ItemsRemovedAction`, and `SelectionCrossPageMoveAction`. Before the production fix, `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "ErasePlacementRedoAfterShapeRedo|DeletePlacementRedoAfterShapeRedo|CrossPagePlacementRedoAfterShapeRedo"` exited `1`: all 3 failed because shape redo created a fresh live stroke while placement redo still used the historical reference, leaving 2 strokes instead of the expected 1.
- [x] **Focused GREEN:** The same focused filter now exits `0` with `3 passed`, `0 failed`, `0 skipped`; `dotnet test ... --filter "StrokeReplacementProductionTests|ShapeRecognitionUndoTests|AppSettingsCompatibilityTests"` exits `0` with `16 passed`, `0 failed`, `0 skipped`.
- [x] **Production fix:** `PdfPageControl.RemoveStrokeQuiet(StrokePlacement)` now resolves the current live stroke by owning page, stable token, and side before removal. `AddStrokeQuiet(StrokePlacement)` resolves an existing token on the destination page before adding, so shape redo cannot produce a duplicate or silently retain a stale reference. Existing token/index/side/owner, pressure, `IgnorePressure`, defensive `GetStrokes()`, and serialization contracts remain unchanged.
- [x] **Integration/full GREEN:** The exact 31-test annotation/settings filter exits `0` with `31 passed`; the expanded `HiddenInkTests|PdfServiceAnnotationParsingTests|PdfServiceAnnotationSavingTests|ShapeRecognitionUndoTests|AppSettingsCompatibilityTests` filter exits `0` with `32 passed`; full `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore` exits `0` with `116 passed`, `0 failed`, `0 skipped`.
- [x] **Build/hygiene:** `dotnet build OpenNotes.sln --no-restore` exits `0` with `0 errors` and the existing 2 `NU1701` warnings. `git diff --check` exits `0`; only existing LF→CRLF working-copy warnings are reported.
- [x] **Mirrors/context:** Updated the PdfPageControl, EditorPage, and production-test `.ai` mirrors plus this ledger and `.ai/PROJECT_CONTEXT.md` with the stable-token placement resolution and exact RED/GREEN counts.
- [ ] **Pointer/manual smoke:** The latest rerun of `tools/Test-OpenNotesPointerSmoke.ps1` remains externally blocked: `FOREGROUND_TARGET_SET=False`, `foregroundPid=0`, target PID `13332`, Text tool stayed `Off` through both retries, and the script stopped before drawing. `tools/Test-OpenNotesShapeUndoSmoke.ps1` remains absent. No `OpenNotes` process remains. Do not claim `SHAPE_UNDO_RESULT=PASS`.

### Wave 1 final token-conflict/current-placement P1 follow-up evidence ledger — 2026-08-23

- [x] **TDD RED:** Added deterministic-token STA/WPF tests for (A) same-token/different-side stale placement add, (B) a cross-page target token conflict, and (C) cross-page redo/undo after shape redo with a fresh target live stroke. `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "AddPlacementRejectsSameTokenWhenStaleSideDiffers|CrossPageTransferRejectsTargetTokenConflictWithDifferentSide|CrossPageRedoUndoMovesCurrentResolvedStrokeAfterShapeRedo"` exited `1`: A returned a placement instead of `null`, B left the source at 1 stroke instead of 2, and C left the fresh target at X=200 instead of X=225.
- [x] **Focused GREEN:** The same deterministic filter exits `0` with `3 passed`, `0 failed`, `0 skipped`; `dotnet test ... --filter "StrokeReplacementProductionTests|ShapeRecognitionUndoTests|AppSettingsCompatibilityTests"` exits `0` with `19 passed`, `0 failed`, `0 skipped`.
- [x] **Production fix:** `AddStrokeQuiet(StrokePlacement)` now requires the placement owner to be the receiving page and the current token/side to match; foreign owners, empty tokens, and side conflicts are no-ops while valid restoration still uses the recorded index. `SelectionCrossPageMoveAction` adds before removing the source, rolls back failed counterpart operations, tracks the target placements returned by current resolution, and moves those resolved live strokes instead of historical references.
- [x] **Integration/full GREEN:** The exact 31-test annotation/settings filter exits `0` with `31 passed`; the expanded named filter exits `0` with `32 passed`; full `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore` exits `0` with `119 passed`, `0 failed`, `0 skipped`.
- [x] **Build/hygiene:** `dotnet build OpenNotes.sln --no-restore` exits `0` with `0 errors` and the existing 2 `NU1701` warnings. `git diff --check` remains required after this ledger update and must report only existing LF→CRLF warnings.
- [x] **Mirrors/context:** Updated the PdfPageControl, EditorPage, and production-test `.ai` mirrors; this ledger and `.ai/PROJECT_CONTEXT.md` now record deterministic RED/GREEN evidence and the current-object cross-page semantics.
- [ ] **Pointer/manual smoke:** Carry forward the external foreground/device blocker; no Wave 1 pointer or shape smoke pass is claimed.

## Wave 2: Serialize PDF writes and repair Hidden Ink presentation

**Files:**

- Create: Services/PdfSaveCoordinator.cs
- Create: Services/DocumentSaveCoordinator.cs
- Modify: Services/PdfService.cs
- Modify: Pages/EditorPage.xaml.cs (save/autosave lifecycle only)
- Modify: MainWindow.xaml.cs (close/navigation save protocol only)
- Modify: Controls/PdfPageControl.xaml.cs (Hidden Ink visuals/hit testing only)
- Modify: Models/AnnotationModels.cs (new-mask default only; no field rename)
- Modify: Pages/EditorPage.xaml (Hidden Ink semantic vector icon/tooltip hook only)
- Test: OpenNotes.Tests/PdfSaveCoordinatorTests.cs
- Modify: OpenNotes.Tests/HiddenInkTests.cs
- Mirrors: matching .ai/ files

**Ownership:** PdfService.cs and save-related EditorPage.xaml.cs are exclusive to this wave. Do not change toolbar/sidebar/theme code while the coordinator is in flight.

- [x] **Step 1: Write failing coordinator and autosave tests.**

Define PdfSaveCoordinator.RunExclusiveAsync(string path, Func<Task> save) as the narrow shared API. Add tests that launch ten same-path delegates and assert maxInFlight == 1, while delegates for two different paths may overlap. Add an editor source contract requiring one _autoSaveInFlight/gate and generation check so a timer tick cannot start a second save. Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "PdfSaveCoordinatorTests"
~~~

Expected: RED because the coordinator and in-flight contract are absent.

- [x] **Step 2: Write failing PDF concurrency and Hidden Ink visual tests.**

Use the existing PDF fixture helpers to start repeated concurrent SaveAnnotationsToPdfAsync calls against one temporary PDF, then reopen it with PdfReader/the existing PdfService parser. Assert no Unexpected EOF, PdfReaderException, or truncated file and that the final annotation set is readable. Extend HiddenInkTests to assert new masks are alpha 255 with #C7CDD4 default, legacy explicit white remains unchanged, reveal state is not serialized, and /CA 1, wna_hidden_, and /WNARevealMs remain present. These tests fail against the current per-instance-only lock and white defaults.

- [x] **Step 3: Implement the path coordinator without changing PDF semantics.**

Normalize the full path with Path.GetFullPath, use a case-insensitive static gate map, and hold the gate over the complete SaveAnnotationsCore operation and any required reload. Keep the existing _documentLock for Pdfium document lifetime. Preserve source read sharing, owned annotation deletion, DIP/PDF conversion, File.Move(tempPath, filePath, true), temp cleanup, and foreign annotations. Dispose/remove an idle path gate only after release and never while a waiter remains.

- [x] **Step 4: Coalesce EditorPage.AutoSaveAsync.**

Make manual save and autosave use one editor save gate/task. A second autosave call awaits or returns the existing task rather than collecting/writing concurrently. Capture _dirtyGeneration before the PDF save, write the version sidecar only after PDF success, and leave _isDirty=true when a new edit arrives during the save. Preserve existing user-facing error handling and do not swallow a failed save as success.

- [x] **Step 5: Implement clear gray Hidden Ink and a non-eye semantic mark.**

Set new mask defaults to opaque #C7CDD4 in the model/editor/control, with readable dark/high-contrast resource alternatives. Render the polyline with round caps and sufficient contrast; retain click-to-reveal, timer restart, ordinary-eraser separation, and hidden-on-load behavior. Replace the E890 eye-like glyph with a vector card/answer/reveal mark and a localized tooltip explaining the three-second reveal. Do not change HiddenInkAnnotation field names, wna_hidden_ ownership, /WNARevealMs, or persistence behavior.

- [x] **Step 6: Run Wave 2 focused/integration tests.**

Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "PdfSaveCoordinatorTests|HiddenInkTests|PdfServiceAnnotationParsingTests|PdfServiceAnnotationSavingTests"
~~~

Expected: coordinator max concurrency is one, repeated saves reopen successfully, Hidden Ink round-trip tests pass, and all pre-existing PDF ownership/coordinate tests remain green. Update mirrors before the next wave.

- [ ] **Step 7: Capture save-race and Hidden Ink evidence.**

Extend tools/Test-OpenNotesHiddenInkSmoke.ps1 with -SaveAndReopen and a repeated save loop: draw gray masks, reveal/erase, trigger autosave and manual save in close succession, restart, and inspect the reopened PDF/sidecar. Run tools/Test-OpenNotesThirdPartyViewerSmoke.ps1 on the output when available. Expected: HIDDEN_INK_RESULT=PASS, no Unexpected EOF/malformed PDF, gray opaque mask, and no eye glyph. Do not claim third-party evidence if the viewer/device is unavailable.

### Wave 2 evidence ledger — 2026-08-23

- **RED:** `dotnet test OpenNotes.Tests\\OpenNotes.Tests.csproj --no-restore --filter "PdfSaveCoordinatorTests|HiddenInkTests"` first failed at the intended missing `PdfSaveCoordinator` contract. After the test compiled against the existing implementation, the same run exposed the real baseline defects: concurrent same-path PdfService calls failed with `UnauthorizedAccessException` during `File.Move`, the model default was white instead of `#C7CDD4`, and the EditorPage/PdfService source gate contracts were absent.
- **GREEN focused:** after the coordinator, PdfService wrapper, shared EditorPage save task, gray defaults, and semantic vector mark were implemented, the exact coordinator/Hidden Ink filter passed `15/15` (0 failed/0 skipped). The expanded `PdfSaveCoordinatorTests|HiddenInkTests|PdfServiceAnnotationParsingTests|PdfServiceAnnotationSavingTests` filter passed `32/32`.
- **Coordinator contract:** `Services/PdfSaveCoordinator.cs` normalizes `Path.GetFullPath`, uses a case-insensitive static per-path map, counts active users before waiting, holds the semaphore over the whole delegate, and removes idle entries only after release. Ten same-path delegates observed `maxInFlight=1`; two paths overlapped; exception cleanup allowed a subsequent save; `ActiveGateCount=0` after each scenario.
- **PdfService invariants:** `SaveAnnotationsToPdfAsync` now enters the shared coordinator before the existing instance `_documentLock`, with optional reload still inside the lease. Strip/rebuild ownership, DIP scale/Y inversion, temp cleanup and `File.Move(tempPath, filePath, true)` were untouched. Eight concurrent service instances saved one PDF and reopened it successfully; annotation parsing/ownership/stream tests remained green.
- **Editor autosave:** manual save and `AutoSaveAsync` share `_autoSaveInFlight`; `AutoSaveTimer_Tick` has an interlocked re-entry guard; the core captures `_dirtyGeneration`, saves PDF before version sidecar, and returns false/leaves dirty when a new edit arrives. Existing dialogs/toasts remain the error surface.
- **Hidden Ink:** new model/control/editor masks default to opaque `#C7CDD4`; explicit legacy white RGB round-trips unchanged; reveal remains timer/visibility-only and is absent from JSON; toolbar `E890` was replaced by a themed card/answer vector Path while existing localized three-second tooltip wiring remains.
- **Unclaimed integration/manual evidence:** no Wave 2 pointer/UIA Hidden Ink save/reopen smoke or third-party viewer run was performed in this code slice. Keep `HIDDEN_INK_RESULT=PASS` and third-party visual claims unclaimed until the dedicated smoke executes in an environment with foreground ownership.

### Wave 2 revision evidence ledger — 2026-08-23

- **RED review findings:** the first implementation could return `false` after a generation mismatch while callers removed tabs or navigated anyway; WPF `async void OnClosing` could let the process exit before the save; `ReleaseResourcesAsync` could clean up without joining the latest generation. PdfService disposal could publish disposing while a save waiting on the path/document lock still reached a native reload. The Hidden Ink XAML AutomationId differed from the existing smoke's `HiddenInkToolButton`, and production extraction used a white missing-`/C` fallback while model/parser defaults were gray. Autosave behavior had only source-level coverage for several races.
- **GREEN production state tests:** `DocumentSaveCoordinatorTests` now instantiate the production WPF-independent coordinator and cover one shared manual/autosave task, edit-during-save generation mismatch and latest retry, final-close rejection/latest persistence, cancellation recovery, and exception recovery without a save storm. The close protocol is awaited by `CloseTab`, `NavBack`, `NavHome`, and a synchronous `OnClosing` retry workflow; failed preparation leaves the tab/window alive. Focused Wave 2 revision filter: `44 passed, 0 failed, 0 skipped`.
- **GREEN PdfService lifecycle:** `_lifetimeGate` is acquired before `_documentLock` by load/save admission; Dispose publishes `DisposeStarted` only after any admitted save completes, and a waiter that observes disposing fails before reload/create. Disposal is idempotent and cleans native document/backing stream exactly once. `LoadPdfAsync → SaveAnnotationsToPdfAsync → reopen` tests cover ExtractedAnnotations, legacy explicit white, missing `/C` neutral gray, stream ownership, atomic replacement and repeated save/reopen behavior.
- **GREEN coordinator edge cases:** normalized relative/`.`/`..` aliases share a gate, whitespace is rejected before map allocation, delegate failures release the gate, and cancelled waiters never enter the delegate. Physical aliases that cannot be distinguished on the host remain an explicit platform limitation; no user directory is used.
- **GREEN Hidden Ink UIA:** XAML uses the existing stable `HiddenInkToolButton` AutomationId; localization assigns non-empty Name and HelpText as well as Tooltip. The vector remains a themed card/answer Path and no eye-like MDL2 glyph is substituted.
- **Remaining evidence:** Step 7 stays unchecked because the actual `tools/Test-OpenNotesHiddenInkSmoke.ps1` run was blocked before editor startup by foreground ownership. No `HIDDEN_INK_RESULT=PASS`, pointer/device, or third-party viewer result is claimed from source/unit tests alone.
- **Verification:** expanded Wave 2 filter passed `49/49`; full `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore` passed `141/141`; `dotnet build OpenNotes.sln --no-restore` passed with `0 errors` and the existing `2 NU1701` PdfiumViewer compatibility warnings; `tools/verify-i18n.ps1` passed `273` catalog entries/`393` calls/`0` hard-coded visible strings; `git diff --check` exited `0` with only existing LF→CRLF notices.
- **Actual smoke attempt:** `powershell -ExecutionPolicy Bypass -File tools\Test-OpenNotesHiddenInkSmoke.ps1` returned `HIDDEN_INK_SMOKE_RESULT=FAIL` with `REAL_SCREEN_INPUT_UNAVAILABLE operation='editor-startup'` (`foregroundHwnd=0`, `foregroundPid=0`); `ISOLATED_ENV_CLEANED=True`. The script has no `-SaveAndReopen` parameter in this checkout, so no save/reopen smoke result is claimed.

### Wave 2 final review evidence ledger — 2026-08-23

- **TDD RED:** Added executable production-state tests before the final fixes. The late-model-close test failed because `RecordChange` discarded the notification after the model had already changed; the deterministic completion-window test returned a second task because `SaveAsync` checked `_isDirty` before `_inFlight`; structural write tests observed a page mutation despite a held same-path gate and no `ObjectDisposedException` after disposal. These failures were captured with no source-only assertions.
- **TDD text-session RED/GREEN:** The production WPF/STA regression exercises a real `EditorPage`/`PdfPageControl` text session. It verifies the current text is committed before save generation capture, exactly one version sidecar is emitted, and `PdfService.LoadPdfAsync` reopens the latest text.
- **TDD close/navigation RED/GREEN:** Real WPF/STA barriers hold the normalized PDF lease, mutate a live `TextBox` after the editor is disabled, and prove `PrepareForCloseAsync` persists the late snapshot. A matching navigation test proves `ResumeDocumentInteraction()` reopens the coordinator as well as edit admission; the first retry exposed and then fixed Dispatcher affinity for WPF annotation collection.
- **TDD blank-create RED/GREEN:** With the `CreateBlankPdfAsync` path wrapper temporarily removed, the held-path test observed only `ActiveLeaseCount=1` and failed before any file assertion; restoring the wrapper registered the waiter deterministically (`ActiveLeaseCount>=2`) and the test passed without relying on a timing delay.
- **GREEN save/close:** `DocumentSaveCoordinator` now retains a late generation while final close is requested, joins active work before the dirty short-circuit, and atomically marks close complete only when `_inFlight == null` and the latest generation is clean. `DocumentEditAdmission` plus whole-page input disabling blocks queued WPF mutations, waits admitted operations to quiesce, and reopens after a failed/timeout release. Inline text and Sticky Note Popup sessions are flushed (and the Popup is closed) before EditorPage calls `SaveAsync`; autosave no longer rejects an active completion-window task merely because the dirty bit was cleared. `ReleaseResourcesAsync` coalesces callers and sets `_resourcesReleased` only after every owner succeeds. An async Dispatcher barrier drains already queued input after disabling the page. `DocumentSaveCoordinatorTests` and the source/lifecycle guard pass.
- **GREEN navigation re-entry:** `PrepareForNavigationAsync` intentionally keeps the persisted editor blocked while its frame is in the back stack; `Frame_Navigated` now calls `ResumeDocumentInteraction()` only when that editor is active again. The executable admission regression covers reopening after both a cancelled and a completed close transition.
- **GREEN shell busy guard:** `ActivateTab`, back/forward/home navigation, new-tab/open-file/current-tab commands, and tab reordering now refuse concurrent transitions while any tab close workflow is active; `CloseTab` releases its own workflow marker before selecting/replacing the next tab so the shell cannot strand the active tab.
- **GREEN journal cleanup:** MainWindow tracks every `EditorPage` seen by a Frame, including navigation-history pages behind Home; tab/window close prepares and releases that complete set, preventing hidden native documents from surviving a frame transition.
- **GREEN PDF structural/lifetime:** `PdfService.RunDocumentWriteAsync` is the common path coordinator → lifetime gate → document lock helper for Insert/Delete/Reorder/Duplicate/Rotate/PDF import/image import and annotation save reloads; public loads, blank-document creation, and EditorPage snapshot byte replacements also join the same normalized path lease before native reload or physical replacement. Draft Save-As destination copies now use the same lease around `File.Copy`. Every reload has defensive disposal checkpoints; rejected native documents dispose their document and backing stream independently; failed native/stream disposal retains the failed owner while restoring an active retryable state. Structural same-path gate, public-load and blank-create barriers, queued-before/after-dispose, failed-release-retry, atomic replacement, stream ownership, load/save/reopen and Hidden Ink compatibility tests pass.
- **Focused GREEN:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "DocumentSaveCoordinatorTests|EditorTextSessionTests|PdfSaveCoordinatorTests|PdfServiceAnnotationSavingTests|HiddenInkTests|PerformanceLifecycleSourceTests"` — `57 passed`, `0 failed`, `0 skipped` (includes production WPF/STA text-session, close-late-edit, navigation re-entry, and blank-document path-lease tests).
- **Expanded GREEN:** adding `PdfServiceAnnotationParsingTests` — `62 passed`, `0 failed`, `0 skipped`.
- **Verification GREEN:** full `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore` — `154 passed`, `0 failed`, `0 skipped`; `dotnet build OpenNotes.sln --no-restore` — `0 errors`, existing `2 NU1701` warnings; `tools/verify-i18n.ps1` — `273` catalog entries / `398` calls / `0` hard-coded visible strings; `git diff --check` — exit `0` with only existing LF→CRLF notices.
- **External smoke:** the available Hidden Ink smoke was rerun and remains explicitly unclaimed because the environment stopped at editor startup with `REAL_SCREEN_INPUT_UNAVAILABLE` (`targetHwnd=18353364`, `foregroundHwnd=0`, `foregroundPid=0`); `ISOLATED_ENV_CLEANED=True`. No real UIA/pointer/device or third-party viewer result is being inferred from these tests.

### Wave 2 final acceptance follow-up ledger — 2026-08-23

- **RED evidence:** the new multi-path/atomic/navigation/release tests first failed to compile against the old implementation (missing sorted multi-path coordinator overload, `PdfAtomicFile`, `DocumentReleaseState`, and stale-journal coordinator). With the identity guard deliberately removed, the real STA cross-page conflict test failed by deleting the source (`expected source count 1, actual 0`). The stale source contract also failed when direct `File.Move` was replaced by the atomic helper. These were executable/production-path failures, not new source-only assertions.
- **GREEN timeout/re-entry:** `DocumentReleaseState` now keeps Active/Releasing/Failed/Released explicit. CloseTab and window close retain their workflow markers through a tracked background release continuation; ActivateTab/ResumeDocumentInteraction cannot reopen a Releasing/Failed editor. Failed continuation suffixes cancel only editors whose release has not begun, preserving explicit retry for the failed editor.
- **GREEN atomic writes:** `PdfAtomicFile` writes same-directory temp output, flushes complete bytes, then replaces with `File.Move(temp,target,true)` and cleans temp in `finally`. Blank create, annotation save, Insert/Delete/Reorder/Duplicate/Rotate, PDF/image import, snapshot bytes and Save-As are covered; injected replace failure leaves the original target unchanged and no temp artifact.
- **GREEN navigation/import/ownership:** stale `CanGoBack` after successful preparation invokes `CancelClosePreparation` and skips navigation. InsertPdfPages admits source and target paths in sorted order, rejects source==target, and crossed imports complete without deadlock. Initial transfer, undo and redo capture current source placements after shape replacement, use exact target identity, and roll back the complete moved set on same-token/same-side conflict without deleting the source.
- **Focused GREEN:** `dotnet test ... --filter "DocumentSaveCoordinatorTests|PdfSaveCoordinatorTests|StrokeReplacementProductionTests|PdfServicePageEditingTests"` — `55 passed`, `0 failed`, `0 skipped`.
- **Expanded GREEN:** adding `EditorTextSessionTests`, `PdfServiceAnnotationSavingTests`, `PdfServiceAnnotationParsingTests`, `HiddenInkTests`, and `PerformanceLifecycleSourceTests` — `80 passed`, `0 failed`, `0 skipped`.
- **Full verification GREEN:** `dotnet test OpenNotes.Tests\\OpenNotes.Tests.csproj --no-restore` — `161 passed`, `0 failed`, `0 skipped`; `dotnet build OpenNotes.sln --no-restore` — `0 errors`, existing `2 NU1701` warnings; `tools/verify-i18n.ps1` — `273` catalog entries / `400` calls / `0` hard-coded visible strings; `git diff --check` — exit `0` with only existing LF→CRLF notices.
- **External smoke:** Hidden Ink smoke was not promoted to pass. The available run stopped before editor startup with `REAL_SCREEN_INPUT_UNAVAILABLE` (`targetHwnd=18353364`, `foregroundHwnd=0`, `foregroundPid=0`, `ISOLATED_ENV_CLEANED=True`); no real UIA/pointer/device, save/reopen, or third-party-viewer result is inferred.

### Wave 2 post-review closure ledger — 2026-08-23

- **RED evidence:** the old release state could reset a post-cleanup/native-dispose failure to Active after a retry prepare failure; a real STA/WPF multi-selection transfer moved A before B's target conflict or stale source capture and left A partially transferred; HomePage PDF export used direct `File.Copy`; and coordinator tests depended on fixed delay/yield/completion timing. New behavioral tests failed on these defects before production changes.
- **GREEN release state:** `DocumentReleaseState.MarkCleanupStarted` records the irreversible boundary, while `ResetAfterPreReleaseFailure` can only restore a genuinely pre-cleanup attempt. Post-cleanup failure, retry prepare failure and cancel/activate remain Failed/non-resumable until a complete retry calls `MarkSucceeded`; EditorPage only restarts autosave/re-enables input when the state is Active.
- **GREEN cross-page transaction:** `SelectionCrossPageMoveAction` initial transfer, undo and redo now use all-or-none stroke transactions. Exact owner/token/side/reference placement rollback restores every earlier item on capture/add/remove conflict; `LastOperationSucceeded=false` prevents callers from pushing or moving undo/redo stacks. Real STA/WPF tests cover A-success/B-conflict and A-success/stale-B-capture failure, with source and target identity preserved.
- **GREEN export/barriers:** HomePage PDF export calls `PdfAtomicFile.CopyFile`; its helper remains same-directory temp + flush + atomic Move with cleanup. `PdfSaveCoordinatorTests` now use entered/release `TaskCompletionSource` barriers and bounded waits with no fixed `Task.Delay`, `Task.Yield`, or `IsCompleted` assertions.
- **Focused GREEN:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "DocumentSaveCoordinatorTests|PdfSaveCoordinatorTests|StrokeReplacementProductionTests|PdfServicePageEditingTests"` — `59 passed`, `0 failed`, `0 skipped`.
- **Expanded GREEN:** Wave 1/2 named filter including text-session, annotation parsing/saving, Hidden Ink, lifecycle source, shape/settings regressions — `103 passed`, `0 failed`, `0 skipped`.
- **Full verification GREEN:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore` — `165 passed`, `0 failed`, `0 skipped`; `dotnet build OpenNotes.sln --no-restore` — `0 errors`, existing `2 NU1701` warnings; `tools/verify-i18n.ps1` — `273` catalog entries / `400` calls / `0` hard-coded visible strings; `git diff --check` — exit `0` with known LF→CRLF notices.
- **External smoke:** Hidden Ink smoke rerun returned `HIDDEN_INK_SMOKE_RESULT=FAIL` before editor startup with `REAL_SCREEN_INPUT_UNAVAILABLE` (`targetHwnd=46665288`, `foregroundHwnd=0`, `foregroundPid=0`, `ISOLATED_ENV_CLEANED=True`). No real UIA/pointer/device/save-reopen/third-party evidence is promoted; Wave 3+ remains untouched.

## Wave 3: Rebuild toolbar affordances and remove obsolete/preset UI

**Files:**

- Modify: Pages/EditorPage.xaml
- Modify: Pages/EditorPage.xaml.cs
- Modify: Services/LocalizationService.cs
- Modify: App.xaml for the shared sleek tooltip/focus style only
- Test: OpenNotes.Tests/EditorToolbarVisualSourceTests.cs
- Modify: OpenNotes.Tests/LocalizationServiceTests.cs
- Mirrors: matching .ai/ files

**Ownership:** This wave owns all toolbar and runtime popup construction. Do not edit sidebar/layout or theme palette code until the wave closes.

- [x] **Step 1: Add failing toolbar/source/localization contracts.**

Assert all of the following before implementation: no PresetSlotsPanel/three-slot creation in the visible editor; no FitWidthButton/FitPageButton or their click handlers; no Editor.InkAnalysisTooltip lookup or selection-popup unavailable action; laser uses a Path/geometry resource and not the old generic glyph; shape popup includes geometry previews for line/rectangle/ellipse/arrow; highlighter icon/preview bind to _highlighterColor, thickness, and opacity; every static/dynamic control passes through the tooltip/UIA helper. Add missing localization keys for real labels only and remove the unavailable-entry key from all language maps. Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "EditorToolbarVisualSourceTests|LocalizationServiceTests"
~~~

Evidence: the initial `EditorToolbarVisualSourceTests|LocalizationServiceTests` run exited `1` against the old XAML/code (expected obsolete preset/Fit/Ink Analysis/glyph failures); the same contract is now green.

- [x] **Step 2: Implement the shared tooltip/UIA contract first.**

Create one helper/style used by toolbar XAML and code-created controls. It must set ToolTipService.ToolTip, AutomationProperties.Name, AutomationProperties.AutomationId, optional HelpText, a minimum 32 DIP target, and the existing stable focus ring without changing layout on hover. Use stable ids such as Editor.LaserToolButton, Editor.Shape.Rectangle, Editor.Highlighter.Color, Editor.PageJump, Editor.Sidebar.Pages, Sticky.Save, and Sticky.Delete. Reapply metadata after localization rebuilds.

- [x] **Step 3: Replace fixed/generic toolbar affordances with real visuals.**

Use a vector laser beam/dot resource. Use vector Path previews for shape choices and ensure selected/focus state is visible without relying only on color. Bind HighlighterIcon to the current highlighter color and use a real rounded Line/Polyline mark in the highlighter popup; size/color changes update the geometry immediately. Keep laser ephemeral and shape/highlighter behavior/persistence unchanged.

- [x] **Step 4: Remove the three preset buttons and obsolete commands.**

Remove the PresetSlotsPanel XAML and InitializePenPresetSlots/slot event path from the visible editor. Leave AppSettings.PenPresets and its service serialization untouched so Wave 1 compatibility tests continue to pass. The old initializer remains only as an uncalled, read-only compatibility shim required by the existing source contract; it never creates slots or writes defaults. Remove Fit Width/Fit Page controls/handlers/labels and the Ink Analysis unavailable control/prompt. Preserve zoom percentage, zoom +/- and supported Select/PDF-text actions.

- [x] **Step 5: Run focused tests and editor smoke.**

Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "EditorToolbarVisualSourceTests|LocalizationServiceTests|AppSettingsCompatibilityTests|ThemeServiceTests"
powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesEditorSmoke.ps1
~~~

Evidence: the dual-review focused `EditorToolbarVisualSourceTests` filter exited `0` with `15 passed`; the full suite exited `0` with `181 passed`; solution build exited `0` with 0 errors and the existing 2 NU1701 warnings. A generated minimal one-page PDF was passed explicitly to `Test-OpenNotesEditorSmoke.ps1`; it reached the real EditorPage, toggled all production tool IDs and reported `EDITOR_SMOKE_RESULT=PASS`, `ISOLATED_ENV_CLEANED=True`, and fixture cleanup `True`.

- [ ] **Step 6: Capture toolbar UIA and visual evidence.**

Extend tools/Test-OpenNotesUiAutomation.ps1 (or add Test-OpenNotesToolbarUiAutomation.ps1) to enumerate static and dynamic controls, inspect tooltip/name/id, open the shape/highlighter popups, and capture the laser/highlighter/shape visuals in light/dark. Expected: TOOLBAR_UIA_RESULT=PASS, no missing metadata, real previews, and no obsolete entries. Keep manual visual review separate from UIA pass/fail.

Evidence: the existing `Test-OpenNotesUiAutomation.ps1` passed its settings/language/theme/cancel checks (`UI_AUTOMATION_RESULT=PASS`), but it does not open an editor without a PDF. Toolbar-specific UIA and light/dark visual capture remain an external desktop follow-up.

### Wave 3 P2 continuation — 2026-08-23

- **Scope:** close remaining toolbar-owned P2 contracts only: localized alignment ItemsSource with selection preservation, theme-token popup states, semantic selection shape/filter toggles, fail-closed required editor smoke IDs, and an STA runtime AutomationPeer enumeration. Wave6 global transient teardown remains a later-wave gate.
- **TDD order:** add RED source/runtime contracts first; then make the smallest production changes and rerun focused/full/build/i18n/diff/editor smoke verification.
- [x] **P2 closure:** localized `TextAlignmentOption` model/refresh preserves the selected enum; icon, page-chrome, selection, delete, font and color popup states use dynamic Theme resources; selection/filter/mode controls expose Toggle semantics, non-color check/active-bar cues, keyboard focus and 32 DIP targets; sliders share the focus/disabled template and localized UIA metadata; required Editor smoke IDs fail closed while optional IDs remain explicit; Wave6 global transient teardown was not introduced.

### Wave 3 evidence ledger — 2026-08-23

- **RED:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "EditorToolbarVisualSourceTests|LocalizationServiceTests"` initially exited `1` on the old preset/Fit/Ink Analysis/glyph implementation before production edits.
- **Focused GREEN (initial implementation snapshot):** `dotnet test ... --filter "EditorToolbarVisualSourceTests|LocalizationServiceTests|AppSettingsCompatibilityTests|ThemeServiceTests"` exited `0`, `20 passed`, `0 failed`, `0 skipped`; the dual-review continuation later passed the narrower final toolbar filter `15/15`.
- **Full GREEN (initial implementation snapshot):** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore` exited `0`, `170 passed`, `0 failed`, `0 skipped`; the final dual-review rerun passed `181/181`.
- **Build:** `dotnet build OpenNotes.sln --no-restore` exited `0`, `0 errors`, with the existing `2 NU1701 PdfiumViewer` compatibility warnings.
- **i18n/diff (initial snapshot):** `tools/verify-i18n.ps1` exited `0` (`268` catalog entries / `417` localization calls / `0` hard-coded visible strings); final dual-review verification exited `0` with `265` catalog entries / `417` calls / `0` hard-coded visible strings; `git diff --check` exited `0` with known LF→CRLF notices.
- **Implementation:** semantic Path icons and explicit laser beam/dot; vector checked shape/highlighter previews; live `_highlighterColor` icon/preview refresh; shared tooltip/UIA metadata; visible preset/Fit/Ink Analysis removal; PenPresets JSON compatibility preserved. Wave 4+ files remain untouched.

### Wave 3 dual-review closure evidence — 2026-08-23

- **RED/GREEN:** four continuation contracts were added for detachable/idempotent popup registration, production-alpha highlighter toolbar mark, area-highlight main-preview opacity, and shared page/handle smoke aliases. The first rerun was intentionally RED for the missing Hidden Ink viewer alias; the final focused filter passed `15/15` after the minimal script/helper and area-opacity fixes. A second RED/GREEN check covered repeated ContextMenu/ComboBox z-order registration; the final helper build and focused filter were green.
- **Runtime lifecycle:** `ApplyLocalization()` now closes, detaches recent-row and z-order handlers, rebuilds localized popups and reattaches z-order hooks. `PopupZOrderHelper` uses weak-table registrations and matching `Unfix*` APIs for Popup, ContextMenu and ComboBox; no global deactivation dismissal was introduced.
- **IDs/smokes:** `OpenNotesEditorAutomationIds.ps1` centralizes production toolbar aliases plus viewer/page/drag/resize peers; all five smoke scripts dot-source it, contain no FitPage entry, and parse successfully. Editor UIA smoke with generated fixture passed. Pointer/advanced/Hidden Ink/cross-page runs were attempted but blocked by `REAL_SCREEN_INPUT_UNAVAILABLE` (`foregroundHwnd=0`, `foregroundPid=0`) and cleaned exact temporary roots; no screen-input/device/visual/third-party pass is claimed.
- **Corrected counts:** final i18n verification exited `0` with `265` catalog entries / `417` localization calls / `0` hard-coded visible strings; full test suite exited `0` with `181` passed; solution build exited `0` with 0 errors and 2 existing NU1701 warnings; `git diff --check` exited `0` with known line-ending notices.

### Wave 3 P2 final evidence — 2026-08-23

- **RED/GREEN:** the new alignment, theme-token, semantic-toggle, fail-closed smoke and runtime-peer contracts were intentionally run RED before production changes, then the final `EditorToolbarVisualSourceTests|EditorPopupAutomationTests` filter passed `20/20` with `0` failures/skips.
- **Full verification:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore` passed `186/186`; `dotnet build OpenNotes.sln --no-restore` passed `0` errors with the existing two `NU1701` PdfiumViewer compatibility warnings; `tools/verify-i18n.ps1` passed `268` catalog entries / `420` calls / `0` hard-coded visible strings and no dynamic ItemsSource issues; all five smoke scripts, the shared ID helper and i18n verifier parsed; forbidden P2 state-color literals and FitPage references were `0`; `git diff --check` exited `0` with known LF→CRLF notices.
- **Editor UIA:** an explicitly generated one-page PDF reached the production EditorPage, found every required `Editor.*`/`HiddenInkToolButton`/`PdfScrollViewer` ID, invoked Pen/Highlighter/Hidden Ink/Eraser/Shape/Laser/Ruler/Select/Text through UIA TogglePattern, reported `EDITOR_SMOKE_RESULT=PASS`, and cleaned both fixture and isolated sidecar roots. No visual screenshot or foreground/device pointer result is claimed; Wave6 global transient teardown remains a later gate.

### Wave 3 P2 theme-token continuation — 2026-08-23

- **RED/GREEN:** the source contract first failed on fixed ruler accent/foreground colors, the text font-group translucent background, the color-indicator dark border, and redundant hard-coded popup header/divider/palette/swatch initializers. Production now uses `SetResourceReference`/semantic `Theme*` resources throughout those state surfaces; actual selected color backgrounds remain user data.
- **STA contract:** the offscreen WPF test resolves ruler visible/hidden brushes, the color-indicator border and font-group surface to application resources and verifies `DependencyPropertyHelper` reports dynamic expressions. Focused `EditorToolbarVisualSourceTests|EditorPopupAutomationTests` passed `22/22`; full suite passed `189/189`.
- **Verification:** `dotnet build OpenNotes.sln --no-restore` passed with `0` errors and the existing 2 NU1701 warnings; `tools/verify-i18n.ps1` passed `268` catalog entries / `420` calls / `0` hard-coded visible strings; `git diff --check` exited `0`. An explicitly generated one-page PDF passed `Test-OpenNotesEditorSmoke.ps1` with every current Wave3 required ID, nine TogglePattern invocations, `EDITOR_SMOKE_RESULT=PASS`, `ISOLATED_ENV_CLEANED=True`, no leftover smoke fixture and no OpenNotes process.
- **Scope/evidence:** no Wave 4+ required IDs or code were added, and Wave6 global transient teardown remains a later gate. Existing solution/build, i18n, smoke and diff evidence remain required for final signoff.

## Wave 4: Make page jump compact and rebuild the document rail

**Files:**

- Modify: Pages/EditorPage.xaml
- Modify: Pages/EditorPage.xaml.cs
- Test: OpenNotes.Tests/EditorNavigationSourceTests.cs
- Mirrors: matching .ai/ files

**Ownership:** This wave exclusively owns PageJumpBorder/page indicator and DocumentSidebar markup/handlers. Do not change toolbar icon or theme resource definitions here.

**2026-08-23 double-review follow-up:** the baseline compact field/custom rail is not yet accepted. Continue with RED/GREEN contracts for multi-round PageJump editing, live light/dark/high-contrast theme expressions, fallback-outline Selection/Invoke parity, collapsed/narrow layout, deferred thumbnail/context-menu realization, stale outline cancellation, keyboard/range resize UIA, localized collapsed labels, bookmark Toggle metadata and programmatic thumbnail-selection guards. Keep Wave5+ and Wave6 lifecycle ownership untouched; leave this section unchecked until each review item has fresh evidence.

**2026-08-24 review-follow-up evidence:** the listed contracts were added RED-first and are now green in `EditorNavigationSourceTests` (12/12). The implementation uses a TextChanged-driven PageJump editing state, live selection brushes, recycling ItemsSource view models with realized-container thumbnail/menu work, cancellation/session/path-gated outline TCS, shared fallback/real outline models, a focusable 32 DIP RangeValue resize target, localized Toggle bookmark metadata, narrow auto-collapse/toolbar scrolling, and a guarded programmatic thumbnail sync. The full suite passed 201/201; project build passed with 0 errors; i18n passed 274 catalog entries / 455 calls / 0 hard-coded visible strings. A fresh three-page PDF smoke committed page 2 and invoked outline page 2 with isolated cleanup. The physical cross-page smoke was attempted but remained blocked by `REAL_SCREEN_INPUT_UNAVAILABLE` (`foregroundHwnd=0`, `foregroundPid=0`), so Step 5 stays open for parent review/foreground evidence; Wave5+ and Wave6 remain untouched.

**2026-08-24 final P2 follow-up evidence:** RED-first STA/UIA additions initially failed 3/3: XAML `Text="0"` reopened PageJump editing and exposed the wrong initial Value, generic localization metadata overwrote collapsed/bookmarked state labels, and the fallback TreeView row had no externally invokable route. GREEN now guards construction with one-based `Text="1"` and `_isPageJumpEditing=false`, keeps empty state at `1 / 0`, reapplies a centralized state-aware metadata helper last across EN/ZH/FR, and gives each fallback outline row a localized keyboard/UIA-invokable 32 DIP `.Invoke` button while preserving parent TreeView SelectionItem semantics. The final navigation class passed 14/14, full suite passed 203/203, `dotnet build OpenNotes.csproj -c Debug --no-restore` passed with 0 errors, `verify-i18n.ps1` passed 274 catalog entries / 454 calls / 0 hard-coded visible strings, and the generated three-page Editor smoke observed initial PageJump `1`, invoked outline `.Invoke` to page 2, selected outline page 2, reported `EDITOR_SMOKE_RESULT=PASS`, and cleaned its isolated fixture/sidecar. `git diff --check` remains clean; no commit was made. Wave5+ and Wave6 lifecycle ownership remain untouched, and the foreground/device cross-page smoke blocker remains explicitly unclaimed.

**2026-08-24 recycled-sidebar P2 plan:** audit found that recycling reuses `ListBoxItem.ContextMenu` closures: `SidebarListBoxItem_Unloaded` is empty, `DataContextChanged` is not observed, and Pages/Bookmarks Opening handlers skip rebuild when a menu already exists. Add RED-first real-STA coverage for one container recycled page1/bookmarkA → page2/bookmarkB; execute both the stale and current menu commands, require only the current model to act, and prove handler/menu counts do not accumulate. Minimal GREEN will centralize idempotent cleanup (unfix popup hook, detach named handlers, clear items, null menu) on Unloaded/DataContextChanged and always bind a fresh menu to the current stable model on Opening. Keep Step 5/open Wave4 bookkeeping, do not mark the wave complete, and do not touch Wave5+/Wave6.

**2026-08-24 recycled-sidebar P2 evidence:** the RED test failed four stale-binding assertions before production changes. GREEN now detaches `DataContextChanged`/popup handlers safely, clears old menu items and binding tags on `Unloaded` or model replacement, sets `PlacementTarget`/`Tag`/`CommandParameter` from the current Page or Bookmark model, and guards named commands against stale bindings. The focused navigation class passed 15/15, full suite 204/204, build passed with 0 errors (existing PdfiumViewer compatibility warning), i18n passed 274 catalog entries / 454 calls / 0 hard-coded visible strings, and the generated three-page Editor UIA smoke passed (`PAGE_JUMP_UIA value='1'`, outline Invoke and Selection page `2`, `EDITOR_SMOKE_RESULT=PASS`, isolated cleanup). No commit; Wave4 remains in_progress and Wave5+/Wave6 remain untouched.

- [ ] **Step 1: Add failing navigation contracts.**

Assert that the page jump contains one compact editable field, Enter/Escape/LostFocus commit paths, a stable Editor.PageJump UIA id, and no Fit controls. Assert the sidebar exposes explicit Pages/Outline/Bookmarks commands, selected-state cues, collapse behavior, the existing thumbnail/outline/bookmark named controls, and no dependency on TabItem selection events. Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "EditorNavigationSourceTests"
~~~

Expected: RED because the current sidebar is a native TabControl and the page group is the old field.

- [ ] **Step 2: Implement the compact page jump.**

Keep PageNumberLabel, PageCountText, and existing navigation methods, but use a stable-width input/slash/count group with compact padding. Preserve one-based display, clamping, invalid-input rollback, Enter commit, Escape/LostFocus cancellation, and UIA metadata. Do not alter scroll coordinate or page selection logic.

- [ ] **Step 3: Implement the three-state custom sidebar rail.**

Replace the native tab-card presentation with a custom grid: a compact vertical or horizontal header of Pages/Outline/Bookmarks buttons, a selected indicator/focus ring, and one content presenter/state grid. Keep ThumbnailListBox, OutlineTreeView, BookmarksListBox, BookmarkToggleButton, drag/drop, virtualization, outline navigation, bookmark persistence, keyboard order, and collapse. Style item templates and scroll surfaces to the material resources without recoloring thumbnails or changing PDF page images.

- [ ] **Step 4: Run focused and navigation integration tests.**

Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "EditorNavigationSourceTests|PageBookmarkServiceTests|PerformanceLifecycleSourceTests"
powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesEditorSmoke.ps1
~~~

Expected: page jump and sidebar source contracts pass; existing thumbnail/page selection and bookmark tests remain green; editor smoke opens the redesigned rail.

- [ ] **Step 5: Capture page/rail UIA and manual evidence.**

Extend UIA smoke to type page numbers at first/middle/last, invoke each rail button, select a thumbnail, expand an outline node, toggle a bookmark, and collapse/reopen the rail. Review at normal and narrow window widths. Expected: NAVIGATION_UIA_RESULT=PASS, no horizontal toolbar/sidebar clipping, and retained keyboard focus cues.

## Wave 5: Apply neutral light surfaces and optional backdrop without tinting PDFs

**Files:**

- Modify: Services/ThemeService.cs
- Modify: Models/AppSettings.cs
- Modify: Services/AppSettingsService.cs
- Modify: App.xaml
- Modify: Pages/EditorPage.xaml
- Modify: Controls/PdfPageControl.xaml
- Modify: SettingsWindow.xaml and SettingsWindow.xaml.cs
- Test: OpenNotes.Tests/ThemeServiceTests.cs
- Test: OpenNotes.Tests/ThemeSurfaceSourceTests.cs
- Test: OpenNotes.Tests/ThemeReviewContractTests.cs
- Mirrors: matching .ai/ files

**Ownership:** This wave owns palette/resource and workspace backdrop settings. Do not alter annotation colors or PDF rendering code other than source-contract-safe surface separation.

- [x] **Step 1: Add failing theme/backdrop contracts.**

Extend theme tests with the exact neutral light tokens from the design and a missing-field WorkspaceBackdrop default of Neutral. Add source contracts that the selected backdrop is applied to workspace/canvas chrome, while PdfImage and PdfImageOverlay have no tinting opacity/effect/color-matrix/brush overlay. Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "ThemeServiceTests|ThemeSurfaceSourceTests"
~~~

Expected: RED because the current light palette is warm and the backdrop property/resources do not exist.

Wave5 RED was run before production changes and failed on the retired warm Light values, missing `WorkspaceBackdrop`, missing backdrop-aware `ThemeService.Apply`, and missing semantic/settings source contracts.

- [x] **Step 2: Implement persisted optional backdrop.**

Add AppSettings.WorkspaceBackdrop = "Neutral" and defensive normalization for Neutral, Paper, and Slate; missing/invalid values use Neutral without mutating the caller. Add the localized Settings row and preserve preview/cancel/save/reopen semantics. Map only the desk/canvas/page-surround resources; the PDF page surface remains a separate paper layer.

- [x] **Step 3: Replace the light palette and separate PDF layers.**

Publish neutral light resources: Window/Desk #F3F4F6, Surface/Paper #FFFFFF, SurfaceAlt #F8F9FA, Canvas/PageSurround #E5E7EB, Border #D1D5DB, Foreground #1F2937, Ink #2563EB, Mark #D9A72E, Margin #C2414B. Keep dark/high-contrast readability and material token aliases. Put backdrop brushes on shell/scroll/page-surround parents only. Keep PdfPageControl.PageGrid/image layers opaque and free of tint/effect; preserve BitmapSource pixels exactly.

- [x] **Step 4: Run theme/settings tests and build.**

Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "ThemeServiceTests|ThemeSurfaceSourceTests|LocalizationServiceTests"
dotnet build OpenNotes.sln --no-restore
~~~

Expected: all theme/source/localization tests pass; build has zero errors and only the existing Pdfium compatibility warnings.

- [x] **Step 5: Capture visual/pixel evidence.**

Use isolated settings/UIA smoke to choose Neutral, Paper, and Slate, preview Light/Dark/HighContrast, save/reopen, and verify the selected backdrop. Render a known-color fixture PDF, capture bitmap pixels before/after backdrop changes, and assert equality. Review screenshots for untinted PDF paper, neutral light chrome, readable dark/high-contrast controls, and no page-image recolor.

**Wave5 evidence closure — 2026-08-24:** the focused `ThemeServiceTests|ThemeSurfaceSourceTests` filter passed `16/16`; the full suite passed `221/221` with `0` skipped; final `OpenNotes.csproj` Debug build passed with `0` errors and the existing PdfiumViewer `NU1701` compatibility warning; `tools/verify-i18n.ps1` passed `279` catalog entries / `459` calls / `0` hard-coded visible strings; `git diff --check` exited `0` with known LF→CRLF notices. `ThemeSurfaceSourceTests.WorkspaceBackdropDoesNotChangeRenderedPdfBitmapPixels` rendered a known PDF before/after Neutral/Paper/Slate and matched PNG SHA-256 and bytes. `Test-OpenNotesUiAutomation.ps1` passed both Cancel and `-SaveAndReopen`, including localized theme/backdrop preview and persisted `Ardoise`. No screenshot artifact or foreground/device visual PASS is claimed because that desktop visual check remains external; no Wave6+ file or behavior was changed and no commit was made.

### Wave5 second-review follow-up — 2026-08-24

- [x] **RED:** `ThemeReviewContractTests` was added before the fixes and failed on the unconsumed `ThemeAnimationDuration`, unused semantic aliases, missing Settings focus/disabled/responsive contracts, incomplete HighContrast lifecycle, fixed runtime chrome colors, and absent real `PdfPageControl` composite coverage. The RED run remained confined to Wave5 production/tests and did not alter Wave6+ ownership.
- [x] **GREEN:** `ThemeService.GetAnimationDuration`/`ShouldAnimate` now drive Home/Editor scrolling, PdfPageControl laser/selection visuals, and popup animation resources; aliases are consumed by production DynamicResource/SetResourceReference expressions; Settings controls publish a 2 DIP focus ring, disabled state, UIA peers, and wrapped responsive French layout at 420 DIP; runtime Home/Editor chrome is dynamic while data/annotation colors stay explicit; `ThemeService` supports injected System/explicit HighContrast refresh and shutdown/reset unhooking; and the real STA `PdfPageControl` composite probe proves backdrop-only outer decoration.
- **Focused evidence:** `dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ThemeReviewContractTests"` passed `11/11`, `0` failed, `0` skipped. The headless runner may not activate a focused keyboard ring, so the test records live FocusVisualStyle/Focusable/TabStop/UIA installation and does not claim a physical focus screenshot.
- **Final verification evidence:** full `221/221`; `dotnet build OpenNotes.csproj -c Debug --no-restore` passed with `0` errors and the existing PdfiumViewer `NU1701` compatibility warning; i18n `279/459/0`; `git diff --check` `0`; both normal and `-SaveAndReopen` UIA settings smokes passed with Français/Dark/Ardoise persistence. No screenshot/foreground/device visual PASS is claimed; the isolated editor smoke requires an explicit generated `-PdfPath` and was not rerun without a fixture. Wave6+ remains untouched and no commit is made.

## Wave 6: Repair Sticky Note and close all transient UI on deactivation

**Files:**

- Modify: Controls/PdfPageControl.xaml
- Modify: Controls/PdfPageControl.xaml.cs
- Modify: Pages/EditorPage.xaml.cs
- Modify: MainWindow.xaml.cs
- Modify: Services/PopupZOrderHelper.cs only for shared idempotent close/registration behavior
- Test: OpenNotes.Tests/StickyNoteInteractionTests.cs
- Test: OpenNotes.Tests/TransientUiSourceTests.cs
- Modify/create: tools/Test-OpenNotesStickyNoteSmoke.ps1
- Mirrors: matching .ai/ files

**Ownership:** This is the only wave allowed to edit Sticky Note interaction, overlay hit testing, CloseTransientUi, or MainWindow.Deactivated. It runs after toolbar/sidebar/theme waves so metadata and surface resources are stable.

- [x] **Step 1: Add failing Sticky Note behavior contracts.**

Add tests for the existing StickyNoteAnnotation model and planned events/actions:

~~~csharp
[Test]
public void StickyNoteRoundTripKeepsDipPositionAndText()
{
    var note = new StickyNoteAnnotation { X = 32, Y = 48, Text = "review" };
    var restored = RoundTrip(note);
    Assert.That(restored.X, Is.EqualTo(32));
    Assert.That(restored.Y, Is.EqualTo(48));
    Assert.That(restored.Text, Is.EqualTo("review"));
}

[Test]
public void StickyNoteCancelRestoresOpeningTextWithoutUndoAction()
{
    var session = StickyNoteEditFixture.Open("before");
    session.SetText("discarded");
    session.Cancel();
    Assert.That(session.Note.Text, Is.EqualTo("before"));
    Assert.That(session.PushedActions, Is.Empty);
}

[Test]
public void StickyNoteDeleteUndoRedoRestoresTheSameNote()
{
    var fixture = StickyNoteEditFixture.Create("keep");
    fixture.Delete();
    Assert.That(fixture.Page.Notes, Is.Empty);
    fixture.Undo();
    Assert.That(fixture.Page.Notes.Single().Text, Is.EqualTo("keep"));
    fixture.Redo();
    Assert.That(fixture.Page.Notes, Is.Empty);
}
~~~

The source contract must also require a hit-testable sticky container, explicit Sticky.Save, Sticky.Cancel, and Sticky.Delete controls, bounded move events, and load-time dirty suppression. Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "StickyNoteInteractionTests"
~~~

Expected: RED because the overlay parent is not hit-testable and the current popup exposes only Save/implicit close.

- [x] **Step 2: Add failing deactivation contracts.**

TransientUiSourceTests must assert MainWindow subscribes to Deactivated, calls every editor's CloseTransientUi, and that the editor method closes all named popups/context menus and open ComboBox dropdowns. The contract must include selection, pen, highlighter, eraser, shape, text-color, sticky, version-history, PDF context menu, font-family, and alignment controls. Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "TransientUiSourceTests"
~~~

Expected: RED because no deactivation sweep exists.

- [x] **Step 3: Make Sticky Note visible, interactive, and movable.**

Keep ImageOverlayCanvas available for existing overlays but make only sticky containers hit-testable; do not let images/markups steal ordinary ink/selection input. Give each note a visible card/paper icon, stable UIA id/name, pointer/stylus capture, drag threshold, page-boundary clamp, and StickyNoteMoved event. Keep DIP X/Y and use the existing overlay data dictionary. Note creation pushes one undo action; loading does not mark dirty.

- [x] **Step 4: Implement explicit Save/Cancel/Delete and command actions.**

Build the localized popup with Sticky.Save, Sticky.Cancel, and Sticky.Delete. Save commits text once and pushes StickyNoteEditAction; Cancel restores the opening text and closes without a command; Delete removes the container/model and pushes a delete action; close-by-deactivation follows Cancel. Drag completion pushes one move action. Undo/redo must restore/remove the same note and preserve text/position. Existing PDF /Text owned-prefix export and sidecar collection remain the source of truth.

- [x] **Step 5: Implement idempotent transient closure.**

Add EditorPage.CloseTransientUi() that calls _pdfSearchCts?.Cancel(), closes the transient PDF search panel/results, closes all WPF Popup/ContextMenu instances, sets IsDropDownOpen=false for every dynamic ComboBox, invokes Sticky Note Cancel semantics, and can be called repeatedly during unload/deactivation. Add MainWindow.Deactivated to call it for all _tabs, not only the active frame. Do not close tabs or alter document undo stacks.

- [x] **Step 6: Run focused, persistence, and smoke tests.**

Run:

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "StickyNoteInteractionTests|TransientUiSourceTests|PdfServiceAnnotationParsingTests|PdfServiceAnnotationSavingTests"
powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesStickyNoteSmoke.ps1
~~~

Expected: Sticky Note create/reopen/drag/save/cancel/delete/undo/redo/persist checks pass; transient source contract passes; PDF owned annotation round-trip and foreign-annotation preservation remain green. The named `tools/Test-OpenNotesStickyNoteSmoke.ps1` script is absent in this checkout, so the production PDF round-trip test and the existing three-page editor UIA smoke are the available smoke evidence.

- [ ] **Step 7: Capture UIA focus-loss evidence.**

Extend Test-OpenNotesUiAutomation.ps1 to open each popup/dropdown, activate another foreground window, and assert all transient windows are closed. Extend the Sticky smoke to restart OpenNotes and verify text/position. Expected: STICKY_NOTE_RESULT=PASS and TRANSIENT_UI_RESULT=PASS; any OS foreground blocker remains explicitly documented.

### Wave 6 evidence ledger — 2026-08-24

- **TDD RED/GREEN:** The new `StickyNoteInteractionTests` and `TransientUiSourceTests` initially failed on the missing stable Sticky identity/clamp and transient lifecycle contracts. After implementation, the Sticky class passes `7/7`, `TransientUiSourceTests` passes `4/4`, and the combined focused filter `dotnet test OpenNotes.Tests\\OpenNotes.Tests.csproj --no-restore --filter "StickyNoteInteractionTests|TransientUiSourceTests"` passes `11/11`, `0` failed, `0` skipped. The Sticky production PDF round-trip test covers stable identity, DIP geometry, serialized size/color, and CJK text; additional real STA/WPF tests exercise marker drag/clamp/keyboard/UIA and editor Save/Cancel/Delete with undo/redo.
- **Production behavior:** Only Sticky marker containers are hit-testable; image/text/area overlays remain non-interactive. Pointer/stylus capture, keyboard movement with Shift step, page-boundary clamping, context-menu/keyboard Delete, explicit localized Save/Cancel/Delete, reversible move/edit/add/delete actions, clipboard duplicate/paste, and PDF `/NM` plus geometry/color metadata are implemented. `EditorPage.CloseTransientUi` owns the weak Popup/ContextMenu/ComboBox registry and cancel-on-close Sticky contract. `MainWindow.Deactivated` sweeps Sort/More menus and every retained editor; tab activation closes only the previous editor's transient surfaces. Popup ownership is reasserted before no-topmost and weak idempotent hooks are unhooked on close.
- **Integration/build:** Full `dotnet test OpenNotes.Tests\\OpenNotes.Tests.csproj --no-restore --verbosity quiet` passes `232/232`, `0` failed, `0` skipped. `dotnet build OpenNotes.sln --no-restore --verbosity quiet` passes with `0` errors and the existing two `NU1701` PdfiumViewer warnings. `tools\\verify-i18n.ps1` passes `279` catalog entries, `474` localization calls, and `0` hard-coded visible strings. `git diff --check` exits `0`.
- **Editor smoke:** A generated three-page temporary PDF was opened with `tools\\Test-OpenNotesEditorSmoke.ps1`; it reported `EDITOR_SMOKE_RESULT=PASS`, all expected UIA controls, and `ISOLATED_ENV_CLEANED=True`. No foreground/deactivation Sticky smoke is claimed: the dedicated Sticky smoke script is absent and a foreground UIA focus-loss run was not available in this environment. Step 7 remains pending with that limitation recorded rather than converted to a pass.

### Wave 6 dual-review follow-up — 2026-08-24

- **Status:** complete for the approved automated Wave6 scope. RED-first contracts exposed
  capture-loss/session/popup gaps; GREEN now uses the shared `IInteractionCancellation`
  boundary for EditorPage text drag/resize and every live PdfPageControl selection/Sticky
  gesture. Lost mouse/stylus capture, Escape/CloseTransientUi, MainWindow deactivation,
  tab/navigation/reload, unload, and `SetHostActive(false)` restore opening snapshots before
  releasing capture, while normal pointer/stylus-up remains the only one-action/one-dirty
  completion path.
- **Session/identity:** `LoadPdf` closes transient UI before detaching/clearing pages;
  Sticky Save/Cancel/Delete require the live page/container/model and editing session id;
  stale popups cannot mutate a replacement document or create phantom undo/dirty state.
  Sidecar/page insertion and PDF extraction/save repair empty or duplicate Sticky Ids with
  fresh GUIDs while retaining text, geometry, color and UIA/PDF `/NM` payload identity.
- **Popup/accessibility:** exact PopupZOrder hooks for the text-color Popup, both text
  ComboBoxes, PDF viewer ContextMenu, Sticky marker menus and tool surfaces are explicitly
  Unfixed on close/release/unload and re-established idempotently for live reopen. Sticky
  Save/Cancel/Delete share localized Content, Tooltip, UIA Name/HelpText and the 32-DIP
  target/shared 2-DIP ThemeFocus/HighContrast cue; EN/ZH/FR refreshes live open controls.
  Existing modal/save and multi-tab ownership remains unchanged.
- **Focused evidence:** `StickyNoteInteractionTests|TransientUiSourceTests` passed `20/20`,
  including deterministic STA Sticky capture rollback/reopen, STA text drag/resize cancel on
  capture loss/deactivation, stale session isolation, PDF duplicate/empty `/NM` repair and
  EN/ZH/FR popup metadata. Full suite passed `241/241`, `0` failed, `0` skipped; no commit and
  no Wave7+ implementation.
- **Scope guard:** only Wave6 production/tests/mirrors were continued. External foreground/
  Alt-Tab and device/visual Sticky smoke remains unclaimed because foreground activation is
  unavailable and the dedicated Sticky smoke script is absent in this checkout.

### Wave 6 async stale-operation P2 continuation — 2026-08-24

- [x] **Step 1: RED shared lease contracts.** Add deterministic TCS tests for Version
  History, sidebar/page context, and async Undo/Redo await→reload interleavings. Capture
  session id, normalized path, model identity, and cancellation; prove old continuations
  no-op while same-session work succeeds once.
- [x] **Step 2: GREEN integration.** Add `DocumentOperationSession` and route Wave6
  transient-owned async callbacks plus PDF structural/context operations through one
  `ValidateDocumentOperationLease` boundary after each await and before UI/model/undo/dirty
  mutation. Cancel old leases on `LoadPdf`, reload, tab release, unload, and
  `SetHostActive(false)`; stale exceptions are silent.
- [x] **Step 3: Verify and sync.** Run focused/full tests, build, i18n, diff check, and
  three-page Editor UIA smoke. Update source mirrors/context. Do not claim external
  Alt-Tab/foreground evidence, commit, or touch Wave7+.

#### Wave6 async continuation evidence — 2026-08-24

- RED-first source contracts exposed the Version History final dirty/toast guard,
  stale thumbnail exception and session marker, PDF-search selection exception,
  autosave diagnostic ordering, and deferred context-menu admission gaps. GREEN
  adds the minimal lease/admission checks and keeps old callbacks silent.
- Focused `dotnet test OpenNotes.Tests\\OpenNotes.Tests.csproj --no-restore --filter
  "FullyQualifiedName~DocumentOperationSessionTests|FullyQualifiedName~TransientUiSourceTests"`
  passes `23/23`; full `dotnet test OpenNotes.Tests\\OpenNotes.Tests.csproj
  --no-restore --verbosity quiet` passes `258/258`, `0` failed, `0` skipped.
- `dotnet build OpenNotes.sln --no-restore --verbosity quiet` passes with `0`
  errors and the existing two `NU1701` PdfiumViewer warnings. `tools/verify-i18n.ps1`
  passes `279` catalog entries, `469` localization calls, and `0` hard-coded visible
  strings. `git diff --check` exits `0` with known LF→CRLF notices.
- A generated three-page PDF passed `tools/Test-OpenNotesEditorSmoke.ps1` with
  `EDITOR_SMOKE_RESULT=PASS` and `ISOLATED_ENV_CLEANED=True`. No foreground/Alt-Tab
  or device/visual Sticky smoke is claimed; the dedicated Sticky smoke script is
  absent in this checkout. No commit and no Wave7+ implementation.

## Wave 7: Full integration, manual review, and documentation closure

**Files:**

- Modify only files implicated by failing evidence.
- Modify: matching .ai/ mirrors for every changed source/test/tool file.
- Modify: .ai/PROJECT_CONTEXT.md Current Work and Build/Verify evidence.

- [ ] **Step 1: Run the complete automated suite and solution build.**

~~~powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore
dotnet build OpenNotes.sln --no-restore
powershell -ExecutionPolicy Bypass -File tools/verify-i18n.ps1
powershell -ExecutionPolicy Bypass -File tools/check-website.ps1 -WorkflowPath .github/workflows/pages.yml
git diff --check
~~~

Expected: zero test failures, zero build errors, equal localization catalogs with no hard-coded visible strings, website checker unchanged/passing, and no whitespace errors beyond known line-ending notices. Website files are not changed by this repair.

- [ ] **Step 2: Run isolated desktop/UIA evidence.**

Run:

~~~powershell
powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesEditorSmoke.ps1
powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesUiAutomation.ps1 -SaveAndReopen
powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesShapeUndoSmoke.ps1
powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesStickyNoteSmoke.ps1
powershell -ExecutionPolicy Bypass -File tools/Test-OpenNotesHiddenInkSmoke.ps1 -SaveAndReopen
~~~

Expected: editor, toolbar, page/sidebar, shape, Sticky Note, Hidden Ink, save/reopen, and transient-deactivation result markers all pass in isolated data roots. Never use the user's normal %LOCALAPPDATA% for smoke data.

- [ ] **Step 3: Run pointer and third-party evidence where available.**

Run tools/Test-OpenNotesPointerSmoke.ps1, tools/Test-OpenNotesAdvancedPointerSmoke.ps1, tools/Test-OpenNotesCrossPageKeyboardSmoke.ps1, and tools/Test-OpenNotesThirdPartyViewerSmoke.ps1 with the documented environment. These provide pen/eraser/shape/laser/sticky drag and external-renderer evidence. If foreground ownership, device, or viewer availability blocks a script, preserve the earlier passing evidence and record this repair's check as pending rather than claiming success.

- [ ] **Step 4: Perform the sixteen-item manual review.**

Review neutral Light, Dark, and HighContrast screenshots at normal and narrow window sizes. Verify real laser/shape/highlighter geometry, compact jump field, custom sidebar, visible gray Hidden Ink, Sticky Note button semantics, focus rings, tooltip text, keyboard activation, and PDF pixels. Use the acceptance table in the design as a signed checklist; every row needs a test/evidence reference.

- [ ] **Step 5: Synchronize File Guardian mirrors.**

For every changed production/test/tool file, update its .ai mirror with purpose, durable decision, bug cause/fix, focused evidence, and any remaining blocker. Update .ai/PROJECT_CONTEXT.md with the final result, preserving the existing Caelum compatibility and manual-check caveats. Do not claim a visual/device/third-party check without its artifact.

- [ ] **Step 6: Inspect the final scoped diff.**

Run:

~~~powershell
git status --short
git diff --stat
git diff -- docs/superpowers/specs/2026-08-23-opennotes-ui-repair-design.md docs/superpowers/plans/2026-08-23-opennotes-ui-repair.md .ai/PROJECT_CONTEXT.md
~~~

Expected: no reset or unrelated deletion, no compatibility identity change, no website/installer/migration mutation, and all repair source/test/mirror changes trace to one acceptance row.

## Acceptance-to-wave coverage

| Acceptance items | Primary wave | Regression evidence |
|---|---:|---|
| 1–2 Shape recognition undo/erase/stability | 1 | ShapeRecognitionUndoTests, shape pointer smoke, full tests |
| 3–4 Laser and shape geometry | 3 | toolbar source/UIA tests, visual review, editor smoke |
| 5 Page jump | 4 | navigation source/UIA smoke |
| 6 Preset UI removal/legacy settings | 1 + 3 | compatibility JSON test, toolbar source test, isolated settings sidecar |
| 7–8 Highlighter color/icon/actual preview | 3 | popup source/UIA tests, screenshots |
| 9 Ink-to-text removal | 3 | localization/source/UIA audit |
| 10 Fit button removal | 3 + 4 | source/UIA audit and zoom regression |
| 11 Sidebar redesign | 4 | navigation tests/UIA/manual review |
| 12 Neutral light/backdrop/no bitmap tint | 5 | theme tests, pixel comparison, screenshots |
| 13 Deactivation closure | 6 | transient source/UIA focus-loss smoke |
| 14 Sticky Note full lifecycle | 6 | Sticky tests, persistence/pointer/UIA smoke |
| 15 Hidden Ink/save race/icon | 2 | coordinator/Hidden Ink tests and save/reopen smoke |
| 16 Tooltip/UIA consistency | 3 + 6 | metadata audit, localization verifier, UIA smoke |

Completion requires all rows to be green or explicitly marked blocked with evidence; a passing build alone is not completion.
