using System;
using System.IO;
using System.Linq;

namespace Caelum.Tests;

/// <summary>
/// Task 10 parity audit — WinUI-side source contracts for the surfaces that
/// previously had only WPF <c>*SourceTests</c> coverage: the sidebar geometry
/// + three-tab contract, the page-jump/anchored-zoom entry points, the
/// MainWindow tab shell + window close protocol, the theme service port, the
/// update-check wiring, the HomePage library/import contract, the editor
/// render lifecycle, and the unpackaged self-contained V6 packaging/version
/// contract. The behavioural halves of shared logic live in the Core test
/// fixtures; runtime verification lives in tools/winui-*.ps1 (see
/// docs/winui3-parity-checklist.md for the full audit + gap list).
/// </summary>
[TestFixture]
public sealed class WinUiParitySourceTests
{
    [Test]
    public void EditorPageKeepsTheSidebarGeometryAndThreeTabContract()
    {
        string xaml = Read("Pages", "EditorPage.xaml");
        string source = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // 5.2.15 content-offset contract: expanded sidebar reserves
            // 12+184 DIP on the left of PagesContainer (228 total), collapsed
            // restores the default 32 DIP margin; ≤375 DIP auto-collapses.
            Assert.That(source, Does.Contain("private const double SidebarExpandedWidth = 184.0"));
            Assert.That(source, Does.Contain("private const double SidebarCollapsedWidth = 38.0"));
            Assert.That(source, Does.Contain("private const double SidebarNarrowAutoCollapseWidth = 375.0"));
            Assert.That(source, Does.Contain("PagesContainerSidebarMargin"));
            Assert.That(source, Does.Contain("new(32 + 12 + SidebarExpandedWidth, 20, 32, 32)"));
            Assert.That(source, Does.Contain("UpdatePagesContainerMarginForSidebar"));
            Assert.That(source, Does.Contain("SetSidebarCollapsed("));
            Assert.That(source, Does.Contain("AutoCollapseSidebarForNarrowLayout"));
            Assert.That(source, Does.Contain("SetSidebarTab("));

            // Sidebar host + the three WPF tabs with their AutomationIds.
            Assert.That(xaml, Does.Contain("x:Name=\"DocumentSidebar\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"DocumentSidebar\""));
            Assert.That(xaml, Does.Contain("x:Name=\"SidebarPagesButton\""));
            Assert.That(xaml, Does.Contain("x:Name=\"SidebarOutlineButton\""));
            Assert.That(xaml, Does.Contain("x:Name=\"SidebarBookmarksButton\""));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.Pages"));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.Outline"));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.Bookmarks"));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.Collapse"));

            // Per-tab content surfaces (WinUI ListView/TreeView types — the
            // WPF ListBox/TreeView equivalents).
            Assert.That(xaml, Does.Contain("x:Name=\"ThumbnailListBox\""));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.Thumbnails"));
            Assert.That(xaml, Does.Contain("x:Name=\"OutlineTreeView\""));
            Assert.That(xaml, Does.Contain("x:Name=\"BookmarksListBox\""));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.Bookmarks.List"));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.BookmarkToggle"));
        });
    }

    [Test]
    public void EditorPageKeepsThePageJumpAndAnchoredZoomContract()
    {
        string xaml = Read("Pages", "EditorPage.xaml");
        string source = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Page-jump group: UIA-discoverable host, editable TextBox,
            // prev/next buttons — WPF Editor.PageJump* parity.
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"Editor.PageJumpGroup\""));
            Assert.That(xaml, Does.Contain("x:Name=\"PageNumberTextBox\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"Editor.PageJump\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"Editor.PreviousPageButton\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"Editor.NextPageButton\""));
            Assert.That(xaml, Does.Contain("KeyDown=\"PageNumberTextBox_KeyDown\""));
            Assert.That(xaml, Does.Contain("LostFocus=\"PageNumberTextBox_LostFocus\""));
            Assert.That(source, Does.Contain("EndPageJumpEdit"));

            // Zoom: label + editable textbox + the WPF step/range contract,
            // anchored ZoomAroundPoint → single ChangeView commit.
            Assert.That(xaml, Does.Contain("x:Name=\"ZoomLabel\""));
            Assert.That(xaml, Does.Contain("x:Name=\"ZoomTextBox\""));
            Assert.That(xaml, Does.Contain("Tapped=\"ZoomLabel_Tapped\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"Editor.ZoomInButton\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"Editor.ZoomOutButton\""));
            Assert.That(source, Does.Contain("private const double ZoomMin = 0.25"));
            Assert.That(source, Does.Contain("private const double ZoomMax = 8.0"));
            Assert.That(source, Does.Contain("private const double ZoomStep = 0.1"));
            Assert.That(source, Does.Contain("private void ApplyZoomFromTextBox()"));
            Assert.That(source, Does.Contain("ZoomTextBox.Text.Trim().TrimEnd('%')"));
            Assert.That(source, Does.Contain("private void ZoomAroundPoint(double newZoom, Point viewportPoint)"));
            Assert.That(source, Does.Contain("PdfScrollViewer.ChangeView("));
            Assert.That(source, Does.Contain("PagesContainer_PointerWheelChanged"));
        });
    }

    [Test]
    public void MainWindowKeepsTheTabShellAndWindowCloseProtocol()
    {
        string xaml = Read("MainWindow.xaml");
        string source = Read("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            // Tab model + strip: ObservableCollection<AppTab>, built-in
            // ListView reorder, programmatic MoveTab index math (WPF parity).
            Assert.That(source, Does.Contain("private readonly ObservableCollection<AppTab> _tabs"));
            Assert.That(source, Does.Contain("public void AddNewHomeTab("));
            Assert.That(source, Does.Contain("private bool MoveTab(AppTab draggedTab, AppTab targetTab, bool insertAfter)"));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"AppTab\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"TabCloseButton\""));

            // Close protocol: AppWindow.Closing is always cancelled first,
            // a 30 s workflow guard covers release, per-tab close workflows
            // are tracked, and editors shut down deterministically.
            Assert.That(source, Does.Contain("_appWindow.Closing += AppWindow_Closing"));
            Assert.That(source, Does.Contain("private bool _allowWindowClose"));
            Assert.That(source, Does.Contain("private bool _windowCloseWorkflowActive"));
            Assert.That(source, Does.Contain("TimeSpan.FromSeconds(30)"));
            Assert.That(source, Does.Contain("_tabCloseWorkflows"));
            Assert.That(source, Does.Contain("NavigationCloseCoordinator.TryNavigateBackAsync"));

            // Window-level accelerators: Ctrl+T new tab, Ctrl+W close,
            // Ctrl+F editor search, Ctrl+Tab cycle.
            Assert.That(source, Does.Contain("e.Key == VirtualKey.T"));
            Assert.That(source, Does.Contain("e.Key == VirtualKey.W"));
            Assert.That(source, Does.Contain("e.Key == VirtualKey.F"));
            Assert.That(source, Does.Contain("e.Key == VirtualKey.Tab"));

            // Custom chrome: the unpackaged window owns its title bar and
            // nav buttons (WPF WindowStyle=None parity).
            Assert.That(source, Does.Contain("ExtendsContentIntoTitleBar"));
            Assert.That(xaml, Does.Contain("x:Name=\"NavBackButton\""));
            Assert.That(xaml, Does.Contain("x:Name=\"NavForwardButton\""));
            Assert.That(xaml, Does.Contain("x:Name=\"NavHomeButton\""));
        });
    }

    [Test]
    public void WinUiThemeServiceKeepsTheWpfThemeContract()
    {
        string service = Read("Services", "WinUiThemeService.cs");
        string appXaml = Read("App.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(service, Does.Contain("public static class WinUiThemeService"));
            // WinUI has no app-level runtime RequestedTheme — the service
            // rewrites Application.Resources brushes and flips each
            // registered window's root element instead.
            Assert.That(service, Does.Contain("ApplyRequestedThemeTo(window)"));
            Assert.That(service, Does.Contain("public static event EventHandler ThemeApplied"));
            // System inputs: UISettings/AccessibilitySettings + HKCU fallback
            // replace WPF SystemEvents.
            Assert.That(service, Does.Contain("UISettings"));
            Assert.That(service, Does.Contain("AccessibilitySettings"));

            // The WPF Theme*Brush key names survive verbatim so ported XAML
            // keeps binding via {ThemeResource}; keys live at the root level
            // (never ThemeDictionaries — root entries always win lookup).
            Assert.That(appXaml, Does.Contain("XamlControlsResources"));
            Assert.That(appXaml, Does.Contain("x:Key=\"ThemeWindowBrush\""));
            Assert.That(appXaml, Does.Contain("x:Key=\"ThemePaperBrush\""));
            Assert.That(appXaml, Does.Contain("x:Key=\"ThemeInkBrush\""));
            Assert.That(appXaml, Does.Contain("x:Key=\"ThemeAccentBrush\""));
            Assert.That(appXaml, Does.Contain("x:Key=\"ThemeSidebarBrush\""));
            Assert.That(appXaml, Does.Contain("x:Key=\"ThemeToolbarBrush\""));
            Assert.That(appXaml, Does.Contain("x:Key=\"AppGlassPanelBrush\""));
            Assert.That(appXaml, Does.Not.Contain("<ResourceDictionary.ThemeDictionaries>"));
        });
    }

    [Test]
    public void UpdateCheckPrefersTheRunningAssemblyVersion()
    {
        string main = Read("MainWindow.xaml.cs");
        var coreService = File.ReadAllText(Path.Combine(
            ProjectRoot(), "OpenNotes.Core", "Services", "UpdateCheckService.cs"));

        Assert.Multiple(() =>
        {
            // The WinUI check reads its own assembly version (6.0.0) first;
            // ProductInfo.Version is only the fallback when no entry
            // assembly reports one.
            Assert.That(main, Does.Contain("typeof(App).Assembly.GetName().Version"));
            Assert.That(main, Does.Contain("Version.Parse(ProductInfo.Version)"));
            Assert.That(main, Does.Contain("_updateCheckService.CheckAsync(installedVersion"));
            Assert.That(main, Does.Contain("CheckForUpdatesMenuItem"));
            Assert.That(main, Does.Contain("FormatDisplayVersion"));

            // The shared User-Agent reflects the caller's installed version,
            // so WPF 5.x and WinUI 6.x announce their real channel.
            Assert.That(coreService, Does.Contain("OpenNotes/{installedVersion}"));
        });
    }

    [Test]
    public void HomePageKeepsTheLibraryAndImportContract()
    {
        string home = Read("Pages", "HomePage.xaml.cs");
        string utilities = Read("Pages", "HomePage.Utilities.cs");

        Assert.Multiple(() =>
        {
            // Library model parity: folders, colors, rename, move, remove all
            // flow through the shared Core RecentFilesService.
            Assert.That(home, Does.Contain("RecentFilesService.GetLibraryEntries("));
            Assert.That(home, Does.Contain("RecentFilesService.MoveToLibraryRoot("));
            Assert.That(home, Does.Contain("RecentFilesService.SetFolderColor("));
            Assert.That(home, Does.Contain("RecentFilesService.RemoveFolder("));
            Assert.That(home, Does.Contain("RecentFilesService.RenameFolder("));
            Assert.That(home, Does.Contain("RecentFilesService.CreateFolder("));
            Assert.That(home, Does.Contain("RecentFilesService.AddOrPromote("));
            Assert.That(home, Does.Contain("RecentFilesService.Remove("));
            Assert.That(home, Does.Contain("RecentFilesService.UpdatePath("));

            // Drag-out stays Copy-only (a Move would let Explorer relocate
            // the library file and dangle the entry — WPF invariant).
            Assert.That(home, Does.Contain("e.AllowedOperations = DataPackageOperation.Copy"));
            Assert.That(home, Does.Contain("e.Data.RequestedOperation = DataPackageOperation.Copy"));

            // Deleting goes through the recycle bin, never hard delete.
            Assert.That(utilities, Does.Contain("RecycleBinService.TrySendToRecycleBin("));

            // Word → PDF import path shared with WPF.
            Assert.That(home, Does.Contain("WordDocumentImport.IsPdfPath("));
            Assert.That(home, Does.Contain("WordDocumentImport.IsWordPath("));
            Assert.That(home, Does.Contain("WordToPdfConverter.Default.ImportAsync("));
        });
    }

    [Test]
    public void EditorPageKeepsTheRenderLifecycleContract()
    {
        string source = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // BGRA pipeline: rasterizer → SoftwareBitmapSource, DPI-aware
            // baseline render, policy-driven render scale + retention.
            Assert.That(source, Does.Contain("_pdfService.RenderPageBgraAsync("));
            Assert.That(source, Does.Contain("SoftwareBitmapSource"));
            Assert.That(source, Does.Contain("XamlRoot?.RasterizationScale"));
            Assert.That(source, Does.Contain("PdfRenderPolicy.CalculateRenderScale("));
            Assert.That(source, Does.Contain("PdfRenderPolicy.GetRetainedPageIndices("));
            // Working-set trim reclaims off-screen page bitmaps.
            Assert.That(source, Does.Contain("page.PageSource = null"));

            // Debounced re-render on zoom/scroll via DispatcherQueueTimers +
            // cancel-only CTSes.
            Assert.That(source, Does.Contain("_zoomRenderDebounceTimer"));
            Assert.That(source, Does.Contain("_scrollRenderDebounceTimer"));
            Assert.That(source, Does.Contain("_reRenderCts"));
            Assert.That(source, Does.Contain("_scrollReRenderCts"));

            // Deterministic teardown — a collapsed Frame may never raise
            // Unloaded, so CloseTab calls ShutdownEditor synchronously.
            Assert.That(source, Does.Contain("public void ShutdownEditor() => ReleaseResources();"));
            Assert.That(source, Does.Contain("ReleaseResourcesAsync()"));
            Assert.That(source, Does.Contain("DeferredTeardownAsync"));
        });
    }

    [Test]
    public void ToolbarKeepsTheWpfAutomationIdSet()
    {
        string xaml = Read("Pages", "EditorPage.xaml");

        Assert.Multiple(() =>
        {
            // The full WPF toolbar id surface — the smoke scripts and any
            // UIA tooling key on these names.
            string[] ids =
            {
                "Editor.UndoButton", "Editor.RedoButton",
                "Editor.PenToolButton", "Editor.HighlighterToolButton",
                "HiddenInkToolButton", "Editor.StickyNoteToolButton",
                "Editor.EraserToolButton", "Editor.ShapeToolButton",
                "Editor.LaserToolButton", "Editor.RulerToolButton",
                "Editor.SelectToolButton", "Editor.TextToolButton",
                "Editor.SavePdfButton", "Editor.VersionHistoryButton",
                "Editor.PenOnlyButton",
                "Editor.ZoomOutButton", "Editor.ZoomLabel", "Editor.ZoomInput",
                "Editor.ZoomInButton", "Editor.RotatePageButton",
            };
            foreach (string id in ids)
            {
                Assert.That(xaml, Does.Contain($"AutomationProperties.AutomationId=\"{id}\""),
                    $"Toolbar AutomationId {id} missing from WinUI EditorPage.xaml");
            }

            // Page context menu ids (print stays disabled until the print
            // pipeline lands — see docs/winui3-parity-checklist.md).
            string source = Read("Pages", "EditorPage.xaml.cs");
            Assert.That(source, Does.Contain("Editor.ContextMenu.Print"));
            Assert.That(source, Does.Contain("Editor.ContextMenu.ExportCurrentPagePng1x"));
            Assert.That(source, Does.Contain("Editor.ContextMenu.ExportAllPagesPng2x"));
            Assert.That(source, Does.Contain("Editor.ContextMenu.InsertPdfPage"));
            Assert.That(source, Does.Contain("Editor.ContextMenu.InsertImagePage"));
            Assert.That(source, Does.Contain("Editor.ContextMenu.RotateCurrentPage"));
        });
    }

    [Test]
    public void WinUIPackageStaysUnpackagedSelfContainedAndReportsV6()
    {
        string csproj = File.ReadAllText(Path.Combine(
            ProjectRoot(), "OpenNotes.WinUI", "OpenNotes.WinUI.csproj"));

        Assert.Multiple(() =>
        {
            // V6 packaging contract: unpackaged, WASDK self-contained, x64 —
            // installer.iss packages this publish output verbatim.
            Assert.That(csproj, Does.Contain("<WindowsPackageType>None</WindowsPackageType>"));
            Assert.That(csproj, Does.Contain("<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>"));
            Assert.That(csproj, Does.Contain("<RuntimeIdentifier>win-x64</RuntimeIdentifier>"));
            Assert.That(csproj, Does.Contain("<AssemblyName>OpenNotes.WinUI</AssemblyName>"));

            // Version: the V6 line stays on a 6.x channel while the WPF project
            // keeps its own 5.2.x channel version. Major-only match — patch
            // bumps (6.0.1 etc.) must not break this contract.
            Assert.That(csproj, Does.Match("<Version>6\\.\\d+\\.\\d+</Version>"));
            Assert.That(csproj, Does.Match("<AssemblyVersion>6\\.\\d+\\.\\d+\\.\\d+</AssemblyVersion>"));
            Assert.That(csproj, Does.Match("<FileVersion>6\\.\\d+\\.\\d+\\.\\d+</FileVersion>"));
        });
    }

    private static string Read(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(
            new[] { Path.Combine(ProjectRoot(), "OpenNotes.WinUI") }.Concat(segments).ToArray()));
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.WinUI", "OpenNotes.WinUI.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not locate the solution root containing OpenNotes.WinUI.");
    }
}
