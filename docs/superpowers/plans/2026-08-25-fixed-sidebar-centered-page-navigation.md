# Fixed Sidebar and Centered Page Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove sidebar resizing and center the existing page navigator independently of toolbar action order.

**Architecture:** Keep the current 184-DIP sidebar and collapse state machine, but delete the resize input surface and its range-provider code. Convert the toolbar's single child into an overlay grid: the existing scrollable action row remains behind a centered page-navigation border, with a same-width transparent spacer reserving its footprint.

**Tech Stack:** .NET 8, WPF XAML/C#, NUnit source and STA tests, GitHub tag-driven Windows release workflow.

---

### Task 1: Add RED layout contracts

**Files:**
- Modify: `OpenNotes.Tests/EditorNavigationSourceTests.cs`
- Modify: `.ai/OpenNotes.Tests/EditorNavigationSourceTests.md`

- [ ] **Step 1: Replace resize expectations with fixed-sidebar and centered-overlay expectations**

Update the former resize STA test to assert `DocumentSidebar.Width == 184`, no element has `Editor.Sidebar.Resize`, collapse still produces 38 DIPs, and expand restores 184 DIPs. Add source assertions for `ToolbarOverlayGrid`, `PageJumpReservedSpace`, `HorizontalAlignment="Center"`, and absence of `SidebarResizeThumb`, `IRangeValueProvider`, and resize handlers.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj --no-restore --filter "FullyQualifiedName~EditorNavigationSourceTests" --verbosity quiet
```

Expected: failure because the current XAML/source still exposes the resize thumb/range provider and lacks the centered toolbar overlay markers.

### Task 2: Remove sidebar resizing and center the navigator

**Files:**
- Modify: `Pages/EditorPage.xaml`
- Modify: `Pages/EditorPage.xaml.cs`
- Modify: `.ai/Pages/EditorPage.md`

- [ ] **Step 1: Remove the resize UI and implementation**

Delete `SidebarResizeThumbStyle`, the `SidebarResizeThumb` instance, resize-only fields/constants, constructor subscription, drag/value/keyboard methods, resize metadata branches, and the `SidebarResizeThumb`/`SidebarResizeThumbAutomationPeer` types. Preserve `SidebarExpandedWidth = 184`, collapse/expand, and narrow auto-collapse.

- [ ] **Step 2: Center the page navigator**

Make the toolbar border child:

```xml
<Grid x:Name="ToolbarOverlayGrid">
    <ScrollViewer x:Name="ToolbarItemsScrollViewer">...</ScrollViewer>
    <Border x:Name="CenteredPageJumpHost"
            HorizontalAlignment="Center"
            VerticalAlignment="Center">...</Border>
</Grid>
```

Move the unchanged `PageJumpGroup` into `CenteredPageJumpHost`. Replace its old action-row location with:

```xml
<Border x:Name="PageJumpReservedSpace"
        Width="132"
        Height="36"
        Margin="2,0"
        Background="Transparent"
        IsHitTestVisible="False"/>
```

- [ ] **Step 3: Run focused tests and verify GREEN**

Run the Task 1 command. Expected: all `EditorNavigationSourceTests` pass.

### Task 3: Prepare and verify OpenNotes 5.2.2

**Files:**
- Modify: `OpenNotes.csproj`
- Modify: `Services/ProductInfo.cs`
- Modify: `Package.appxmanifest`
- Modify: `installer.iss`
- Modify: `OpenNotes.Tests/ProductInfoTests.cs`
- Modify matching `.ai/` mirrors and `.ai/PROJECT_CONTEXT.md`

- [ ] **Step 1: Advance release contracts to 5.2.2**

Update project/product/installer values to `5.2.2`, assembly/AppX values to `5.2.2.0`, and the visible-version test to `5.2.2`. Preserve all Caelum/WindowsNotesApp compatibility identifiers.

- [ ] **Step 2: Run complete verification**

```powershell
dotnet test OpenNotes.Tests\OpenNotes.Tests.csproj -c Release --no-restore --verbosity quiet
dotnet build OpenNotes.sln -c Release --no-restore --verbosity quiet
powershell -ExecutionPolicy Bypass -File .\tools\verify-i18n.ps1
git diff --check
```

Expected: zero failures/errors/hard-coded visible strings; only known Pdfium compatibility warnings.

- [ ] **Step 3: Commit, tag, and push**

```powershell
git add -- <the exact files above>
git commit -m "fix: center page navigation and lock sidebar width"
git push origin main
git tag -a v5.2.2 -m "OpenNotes 5.2.2"
git push origin v5.2.2
```

- [ ] **Step 4: Install and launch the published executable**

Wait for the Release workflow, download `OpenNotes-Setup-5.2.2.exe`, verify its hash/version, install it as the per-user upgrade, launch the installed `OpenNotes.exe`, and open the user's supplied textbook. Confirm the process remains alive and the fixed sidebar/centered page navigator are present.
