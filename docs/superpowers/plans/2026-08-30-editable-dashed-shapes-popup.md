# Editable Dashed Shapes and Popup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persistent grouped dashed-line shape, batch style editing for selected drawings, and compact scrollable tool popups.

**Architecture:** Extend ordinary stroke annotations with optional shape metadata and keep dashed lines as grouped short WPF strokes. Reuse the current selection transform pipeline by expanding a hit part to its group, then add one batch style action and wrap popup content in a bounded scroll viewer.

**Tech Stack:** C# 12, .NET 8 WPF Ink, PdfSharpCore, NUnit, JSON-compatible annotation models.

---

### Task 1: Shape metadata and dash geometry

**Files:**
- Modify: `Models/AnnotationModels.cs:35-48`
- Create: `Models/ShapeStrokeMetadata.cs`
- Modify: `Controls/PdfPageControl.xaml.cs:25-37,694-718,2638-2837`
- Modify: `OpenNotes.Tests/ShapeToolTests.cs`
- Create: `.ai/Models/ShapeStrokeMetadata.md`

- [ ] **Step 1: Add failing dash/group tests**

```csharp
[Test]
public void DashedLineSegments_AlternateInkAndRealGaps()
{
    var parts = ShapeStrokeMetadata.BuildDashedLine(new Point(0, 0), new Point(100, 0), 12, 8);
    Assert.That(parts.Count, Is.GreaterThan(1));
    Assert.That(parts.Zip(parts.Skip(1), (a, b) => b[0].X - a[^1].X), Has.All.GreaterThan(0));
}
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeToolTests"`

Expected: compile failure for missing metadata/geometry.

- [ ] **Step 3: Add compatible metadata and dash builder**

```csharp
public string ShapeGroupId { get; set; }
public string ShapeKind { get; set; }
public int ShapePartIndex { get; set; }
public bool IsDashedShape { get; set; }
```

- [ ] **Step 4: Commit shapes with one group id and real dash parts**

```csharp
string groupId = Guid.NewGuid().ToString("N");
foreach (var (points, partIndex) in shapeParts.Select((points, index) => (points, index)))
    AddShapeStroke(points, groupId, CurrentShape.ToString(), partIndex, CurrentShape == ShapeKind.DashedLine);
```

- [ ] **Step 5: Run and verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeToolTests"`

Expected: PASS.

### Task 2: Persist and transfer shape metadata

**Files:**
- Modify: `Controls/PdfPageControl.xaml.cs:3529-3557,3975-4036`
- Modify: `Pages/EditorPage.xaml.cs:4934-5164,5500-5530`
- Modify: `Services/PdfService.cs:1080-1122,1711-1790`
- Modify: `OpenNotes.Tests/PdfAnnotationTests.cs`
- Modify: `.ai/Models/AnnotationModels.md`
- Modify: `.ai/Services/PdfService.md`

- [ ] **Step 1: Add failing model/PDF/copy round-trip tests**

```csharp
Assert.Multiple(() =>
{
    Assert.That(reloaded.ShapeGroupId, Is.EqualTo(groupId));
    Assert.That(reloaded.ShapeKind, Is.EqualTo("DashedLine"));
    Assert.That(reloaded.ShapePartIndex, Is.EqualTo(2));
    Assert.That(reloaded.IsDashedShape, Is.True);
});
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~PdfAnnotationTests|FullyQualifiedName~ShapeToolTests"`

Expected: metadata is lost.

- [ ] **Step 3: Write/read private PDF keys and preserve metadata through every stroke clone**

```csharp
dict.Elements.SetString("/WNShapeGroup", stroke.ShapeGroupId);
dict.Elements.SetString("/WNShapeKind", stroke.ShapeKind);
dict.Elements.SetInteger("/WNShapePart", stroke.ShapePartIndex);
dict.Elements.SetInteger("/WNShapeDashed", stroke.IsDashedShape ? 1 : 0);
```

- [ ] **Step 4: Run round-trip tests and verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~PdfAnnotationTests|FullyQualifiedName~ShapeToolTests"`

Expected: PASS.

### Task 3: Logical group selection and transforms

**Files:**
- Modify: `Controls/PdfPageControl.xaml.cs:5334-5569,5920-6010,6200-6320`
- Modify: `OpenNotes.Tests/ShapeSelectionTests.cs`
- Modify: `.ai/OpenNotes.Tests/ShapeSelectionTests.md`

- [ ] **Step 1: Add failing arrow/dashed group selection tests**

```csharp
SelectAt(page, dashedPartMidpoint);
Assert.That(page.SelectedStrokes.Select(GetShapeGroupId), Has.All.EqualTo(groupId));
Assert.That(page.SelectedStrokes, Has.Count.EqualTo(dashPartCount));
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeSelectionTests"`

Expected: only one part is selected.

- [ ] **Step 3: Expand a hit/toggled stroke to its live group**

```csharp
private IEnumerable<Stroke> GetLogicalShapeStrokes(Stroke stroke)
{
    string groupId = GetShapeGroupId(stroke);
    return string.IsNullOrWhiteSpace(groupId)
        ? new[] { stroke }
        : _strokes.Where(candidate => GetShapeGroupId(candidate) == groupId);
}
```

- [ ] **Step 4: Run selection/move/resize/delete tests**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeSelectionTests"`

Expected: PASS.

### Task 4: Selected drawing color/width with one undo action

**Files:**
- Modify: `Controls/PdfPageControl.xaml.cs`
- Modify: `Pages/EditorPage.xaml.cs:4638-4825`
- Modify: `OpenNotes.Tests/ShapeSelectionTests.cs`
- Modify: `.ai/Pages/EditorPage.md`

- [ ] **Step 1: Add failing batch style/undo tests**

```csharp
ApplySelectedDrawingStyle(editor, Colors.Red, 6);
Assert.That(page.SelectedStrokes, Has.All.Matches<Stroke>(s => s.DrawingAttributes.Color == Colors.Red && s.DrawingAttributes.Width == 6));
Undo(editor);
AssertOriginalStyles(page.SelectedStrokes);
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeSelectionTests"`

Expected: selected-style API/action is absent.

- [ ] **Step 3: Add a cloned before/after batch style snapshot and action**

```csharp
var before = strokes.ToDictionary(stroke => stroke, stroke => stroke.DrawingAttributes.Clone());
foreach (var stroke in strokes)
{
    stroke.DrawingAttributes.Color = color;
    stroke.DrawingAttributes.Width = width;
    stroke.DrawingAttributes.Height = width;
}
```

- [ ] **Step 4: Keep selection active, refresh overlays, dirty state, and thumbnail once**

```csharp
page.RefreshSelectionVisualsAfterStyleChange();
PushUndoAction(new StrokeStyleChangedAction(page, before, after));
MarkDirty();
```

- [ ] **Step 5: Run and verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeSelectionTests"`

Expected: PASS.

### Task 5: Localization and compact scrollable popup

**Files:**
- Modify: `Pages/EditorPage.xaml.cs:3745-4320,4638-4825,5718-5958`
- Modify: `Services/LocalizationService.cs`
- Modify: `OpenNotes.Tests/ShapeToolTests.cs`
- Modify: `OpenNotes.Tests/EditorPopupDismissalTests.cs`
- Modify: `.ai/Services/LocalizationService.md`

- [ ] **Step 1: Add failing popup/localization contracts**

```csharp
Assert.That(LocalizationService.Get("Editor.Shape.DashedLine", "fr-FR"), Is.EqualTo("Ligne pointillée"));
Assert.That(FindDescendant<ScrollViewer>(penPopup.Child), Is.Not.Null);
Assert.That(Grid.GetColumn(pressureToggle), Is.EqualTo(0));
Assert.That(Grid.GetColumn(inkToggle), Is.EqualTo(1));
Assert.That(Grid.GetColumn(recognitionToggle), Is.EqualTo(2));
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeToolTests|FullyQualifiedName~EditorPopupDismissalTests"`

Expected: dashed localization and scroll/row contracts fail.

- [ ] **Step 3: Wrap tool content and compact the behavior toggles**

```csharp
var scroller = new ScrollViewer
{
    Content = panel,
    MaxHeight = 680,
    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
};
```

- [ ] **Step 4: Run popup, shape, localization, and i18n checks**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeToolTests|FullyQualifiedName~EditorPopupDismissalTests|FullyQualifiedName~Localization"`

Expected: PASS.

### Task 6: Verification

**Files:**
- Modify: `.ai/PROJECT_CONTEXT.md`

- [ ] **Step 1: Run focused shape/editor suites**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ShapeToolTests|FullyQualifiedName~ShapeSelectionTests|FullyQualifiedName~PdfAnnotationTests|FullyQualifiedName~EditorPopupDismissalTests|FullyQualifiedName~StrokeEraserGeometryTests"`

Expected: PASS.

- [ ] **Step 2: Run full suite, Release build, i18n, and diff check**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj -c Debug --no-restore`

Expected: all tests pass.

Run: `dotnet build OpenNotes.csproj -c Release --no-restore`

Expected: zero errors.

Run: `git diff --check`

Expected: exit code 0.
