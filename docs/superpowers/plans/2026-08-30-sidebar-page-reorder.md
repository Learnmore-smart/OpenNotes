# Sidebar Page Reorder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver persistent drag-to-reorder page thumbnails with clear placement feedback and safe history/state synchronization.

**Architecture:** Add a small pure placement helper and immutable drag payload, then route WPF DragOver/Drop through the existing `PdfService.ReorderPagesAsync`, document lease, bookmark remap, reload, and snapshot action. The indicator is a single overlay line and never participates in hit testing.

**Tech Stack:** C# 12, .NET 8 WPF drag/drop, PdfSharpCore, NUnit.

---

### Task 1: Placement and payload RED coverage

**Files:**
- Create: `Models/ThumbnailDropPlacement.cs`
- Create: `OpenNotes.Tests/ThumbnailDropPlacementTests.cs`
- Create: `.ai/Models/ThumbnailDropPlacement.md`
- Create: `.ai/OpenNotes.Tests/ThumbnailDropPlacementTests.md`

- [ ] **Step 1: Write table-driven failing tests**

```csharp
[TestCase(0, 3, true, 2)]
[TestCase(0, 3, false, 3)]
[TestCase(3, 1, true, 1)]
public void ResolveFinalIndex_AccountsForSourceRemoval(
    int source, int targetRow, bool before, int expected)
{
    Assert.That(ThumbnailDropPlacement.ResolveFinalIndex(source, targetRow, before, 4), Is.EqualTo(expected));
}
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ThumbnailDropPlacementTests"`

Expected: compile failure because the helper is absent.

- [ ] **Step 3: Implement the pure slot conversion**

```csharp
public static int ResolveFinalIndex(int source, int slot, int pageCount)
{
    int clampedSlot = Math.Clamp(slot, 0, pageCount);
    int finalIndex = clampedSlot > source ? clampedSlot - 1 : clampedSlot;
    return Math.Clamp(finalIndex, 0, pageCount - 1);
}
```

- [ ] **Step 4: Run and verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ThumbnailDropPlacementTests"`

Expected: PASS.

### Task 2: WPF insertion indicator and immutable drag payload

**Files:**
- Modify: `Pages/EditorPage.xaml:834-855`
- Modify: `Pages/EditorPage.xaml.cs:7337-7445`
- Modify: `OpenNotes.Tests/EditorNavigationSourceTests.cs`
- Modify: `.ai/Pages/EditorPage.md`

- [ ] **Step 1: Add failing source/STA contracts**

```csharp
Assert.That(xaml, Does.Contain("DragOver=\"ThumbnailListBox_DragOver\""));
Assert.That(xaml, Does.Contain("DragLeave=\"ThumbnailListBox_DragLeave\""));
Assert.That(xaml, Does.Contain("x:Name=\"ThumbnailDropIndicator\""));
Assert.That(source, Does.Not.Contain("_thumbnailDragSessionId = -1;\r\n            DragDrop.DoDragDrop"));
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~EditorNavigationSourceTests"`

Expected: indicator/handler contracts fail.

- [ ] **Step 3: Add payload, DragOver, DragLeave, and indicator lifecycle**

```csharp
private sealed record ThumbnailDragPayload(
    int SourceIndex, int SessionId, string FilePath, SidebarPageItem Source);

private void ThumbnailListBox_DragLeave(object sender, DragEventArgs e)
    => ClearThumbnailDropIndicator();
```

- [ ] **Step 4: Run source/STA tests and verify GREEN**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~EditorNavigationSourceTests|FullyQualifiedName~ThumbnailDropPlacementTests"`

Expected: PASS.

### Task 3: Persistent reorder, reload, bookmark, and history synchronization

**Files:**
- Modify: `Pages/EditorPage.xaml.cs:6806-6821,7365-7445`
- Modify: `Services/PdfService.cs:569-597`
- Modify: `OpenNotes.Tests/PdfServicePageEditingTests.cs`
- Modify: `OpenNotes.Tests/PageBookmarkServiceTests.cs`

- [ ] **Step 1: Add forward/backward/final-slot service regressions**

```csharp
await service.ReorderPagesAsync(path, 0, 2);
AssertPageMarkers(path, "page-2", "page-3", "page-1");
```

- [ ] **Step 2: Run and verify current gaps**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~PdfServicePageEditingTests|FullyQualifiedName~PageBookmarkServiceTests"`

Expected: new ordering/session integration assertions fail before production wiring.

- [ ] **Step 3: Use one resolved final index across the transaction**

```csharp
await _pdfService.ReorderPagesAsync(filePath, payload.SourceIndex, finalIndex);
currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
JumpToPage(finalIndex);
var afterBookmarks = PageBookmarkService.ApplyPageMove(filePath, payload.SourceIndex, finalIndex).ToList();
PushUndoAction(new DocumentSnapshotAction(this, before, after, focusBefore, finalIndex, beforeBookmarks, afterBookmarks));
```

- [ ] **Step 4: Correct reload validation to the actual session returned by `LoadPdf`**

```csharp
int previousSessionId = operationLease.SessionId;
await LoadPdf(filePath);
if (_loadSessionId != previousSessionId + 1)
    return null;
```

- [ ] **Step 5: Run focused sidebar/service/history tests**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~ThumbnailDropPlacementTests|FullyQualifiedName~EditorNavigationSourceTests|FullyQualifiedName~PdfServicePageEditingTests|FullyQualifiedName~PageBookmarkServiceTests|FullyQualifiedName~ThumbnailCompositorTests"`

Expected: PASS.

### Task 4: Verification

**Files:**
- Modify: `.ai/PROJECT_CONTEXT.md`

- [ ] **Step 1: Run the full suite**

Run: `dotnet test OpenNotes.Tests/OpenNotes.Tests.csproj -c Debug --no-restore`

Expected: all tests pass.

- [ ] **Step 2: Build Release and check the diff**

Run: `dotnet build OpenNotes.csproj -c Release --no-restore`

Expected: zero errors.

Run: `git diff --check`

Expected: exit code 0.

- [ ] **Step 3: Record verification**

```powershell
git add .ai/PROJECT_CONTEXT.md
git commit -m "docs: record sidebar reorder verification"
```
