# Exact Ink Erasing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace bounds/sample-based erasing with exact WPF ink-path geometry and preserve repeatable undo/redo identity.

**Architecture:** `PdfPageControl` will construct one rectangular `StylusShape` per gesture and use `Stroke.HitTest` for whole-stroke mode plus `Stroke.GetEraseResult` for pixel mode. Existing placement-backed erase history remains the mutation boundary, with tests driving the public page-control behavior through reflection only where the production method is private.

**Tech Stack:** C# 12, .NET 8 WPF Ink, NUnit STA tests.

---

### Task 1: Exact eraser geometry regressions

**Files:**
- Create: `OpenNotes.Tests/StrokeEraserGeometryTests.cs`
- Create: `.ai/OpenNotes.Tests/StrokeEraserGeometryTests.md`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test, Apartment(ApartmentState.STA)]
public void WholeStroke_DoesNotRemoveDiagonalWhoseBoundsOnlyOverlapEraser()
{
    var page = CreatePage(wholeStroke: true, Stroke((0, 0), (100, 100)));
    Erase(page, (10, 90));
    Assert.That(page.GetStrokes().Count, Is.EqualTo(1));
}

[Test, Apartment(ApartmentState.STA)]
public void PixelEraser_SplitsSparseStraightLineAtCrossing()
{
    var page = CreatePage(wholeStroke: false, Stroke((0, 50), (100, 50)));
    Erase(page, (50, 50));
    Assert.That(page.GetStrokes().Count, Is.EqualTo(2));
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~StrokeEraserGeometryTests"`

Expected: the bounds-only whole-stroke case removes the diagonal and the sparse pixel line remains unsplit.

- [ ] **Step 3: Add identity/history regressions**

```csharp
[Test, Apartment(ApartmentState.STA)]
public void UndoThenEraseAgain_CreatesASecondValidEraseGesture()
{
    var (editor, page, original) = CreateEditorWithSparseLine();
    EraseGesture(page, (50, 50));
    InvokeUndo(editor);
    EraseGesture(page, (50, 50));
    Assert.That(page.GetStrokes(), Has.None.SameAs(original));
    Assert.That(UndoDepth(editor), Is.EqualTo(1));
}
```

- [ ] **Step 4: Run the focused tests and keep the failure output**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~StrokeEraserGeometryTests|FullyQualifiedName~StrokeReplacementProductionTests"`

Expected: new eraser regressions fail; existing replacement tests pass.

- [ ] **Step 5: Commit the RED coverage**

```powershell
git add OpenNotes.Tests/StrokeEraserGeometryTests.cs .ai/OpenNotes.Tests/StrokeEraserGeometryTests.md docs/superpowers
git commit -m "test: cover exact repeatable ink erasing"
```

### Task 2: Path-aware whole-stroke and pixel erasing

**Files:**
- Modify: `Controls/PdfPageControl.xaml.cs:3359-3679`
- Modify: `.ai/Controls/PdfPageControl.md`

- [ ] **Step 1: Construct the production eraser geometry once per gesture**

```csharp
var eraserPath = points.Select(point => new Point(point.X, point.Y)).ToList();
var eraserShape = new RectangleStylusShape(_eraserSize, _eraserSize);
```

- [ ] **Step 2: Replace the whole-stroke bounds decision with a visible-path hit**

```csharp
if (WholeStrokeEraser)
{
    if (!stroke.HitTest(eraserPath, eraserShape))
        continue;
    ApplyErasedStroke(stroke, new List<Stroke>());
    mutated = true;
    continue;
}
```

- [ ] **Step 3: Replace point-sample clipping with WPF retained fragments**

```csharp
var fragments = stroke.GetEraseResult(eraserPath, eraserShape).Cast<Stroke>().ToList();
if (fragments.Count == 1 && ReferenceEquals(fragments[0], stroke))
    continue;
ApplyErasedStroke(stroke, fragments);
mutated = true;
```

- [ ] **Step 4: Run the exact geometry tests**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~StrokeEraserGeometryTests"`

Expected: PASS with zero failures.

- [ ] **Step 5: Commit the geometry fix**

```powershell
git add Controls/PdfPageControl.xaml.cs .ai/Controls/PdfPageControl.md
git commit -m "fix: erase only touched ink geometry"
```

### Task 3: Placement-backed repeat erasing

**Files:**
- Modify: `Controls/PdfPageControl.xaml.cs:3529-3557,4282-4371`
- Modify: `Pages/EditorPage.xaml.cs:493-541`
- Modify: `.ai/Pages/EditorPage.md`

- [ ] **Step 1: Verify erase events carry the placements captured at mutation time**

```csharp
StrokesErased?.Invoke(this,
    new StrokesErasedEventArgs(removed, added, removedPlacements, addedPlacements));
```

- [ ] **Step 2: Make Undo/Redo resolve the current token-side placement and fail atomically on conflicts**

```csharp
foreach (var fragment in _addedFragments.OrderByDescending(p => p.Index))
    Require(_page.RemoveStrokeQuiet(fragment));
foreach (var original in _removedOriginals.OrderBy(p => p.Index))
    Require(_page.AddStrokeQuiet(original.ForOwner(_page, original.Index)) != null);
```

- [ ] **Step 3: Run history and repeat-erase coverage**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~StrokeEraserGeometryTests|FullyQualifiedName~StrokeReplacementProductionTests|FullyQualifiedName~ShapeRecognitionUndoTests"`

Expected: PASS with zero failures.

- [ ] **Step 4: Commit the history fix**

```powershell
git add Controls/PdfPageControl.xaml.cs Pages/EditorPage.xaml.cs .ai/Controls/PdfPageControl.md .ai/Pages/EditorPage.md
git commit -m "fix: preserve erase identity across undo redo"
```

### Task 4: Verification

**Files:**
- Modify: `.ai/PROJECT_CONTEXT.md`

- [ ] **Step 1: Run focused tests**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~StrokeEraserGeometryTests|FullyQualifiedName~StrokeReplacementProductionTests"`

Expected: PASS with zero failures.

- [ ] **Step 2: Run the full suite**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj -c Debug --no-restore`

Expected: all tests pass.

- [ ] **Step 3: Build Release and validate the diff**

Run: `dotnet build OpenNotes.csproj -c Release --no-restore`

Expected: zero errors.

Run: `git diff --check`

Expected: exit code 0.

- [ ] **Step 4: Record final evidence and commit**

```powershell
git add .ai/PROJECT_CONTEXT.md
git commit -m "docs: record exact eraser verification"
```
