using System;
using System.IO;
using System.Linq;

namespace Caelum.Tests;

/// <summary>
/// G5/G6 parity contracts — the WinUI sidebar thumbnail drag-reorder
/// (WPF <c>ThumbnailListBox</c> PreviewMouseLeftButtonDown/PreviewMouseMove/
/// DragOver/Drop chain via <c>StartDragAsync</c> + a manual payload drag,
/// NOT the built-in CanReorderItems path) and the F11 immersive fullscreen
/// (WPF <c>ToggleImmersiveMode</c> opacity hit-test chrome hide +
/// <c>AppWindowPresenterKind.FullScreen</c> presenter swap) are pinned
/// as source contracts. The slot math lives in Core
/// (<c>ThumbnailDropPlacement.ResolveFinalIndex</c>, covered by
/// <c>ThumbnailDropPlacementTests</c>); runtime verification is
/// tools/winui-*.ps1 smoke territory.
/// </summary>
[TestFixture]
public sealed class WinUiThumbnailReorderAndImmersiveSourceTests
{
    [Test]
    public void ThumbnailListBoxExposesTheManualDragSurface()
    {
        string xaml = Read("Pages", "EditorPage.xaml");

        Assert.Multiple(() =>
        {
            // The rail stays a MANUAL drag surface: the built-in reorder
            // path must remain off because it would mutate SidebarPageItems
            // before the document lease pipeline validates the move.
            Assert.That(xaml, Does.Contain("x:Name=\"ThumbnailListBox\""));
            Assert.That(xaml, Does.Contain("AllowDrop=\"True\""));
            Assert.That(xaml, Does.Contain("CanDragItems=\"False\""));
            Assert.That(xaml, Does.Contain("CanReorderItems=\"False\""));
            // The WPF drop indicator element stays wired (accent 2-DIP bar).
            Assert.That(xaml, Does.Contain("x:Name=\"ThumbnailDropIndicator\""));
        });
    }

    [Test]
    public void ThumbnailDragGestureAndPayloadContractAreWired()
    {
        string source = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // WPF drag-arm + threshold + immutable payload (in-app format
            // marker replaces the typeof(ThumbnailDragPayload) DataObject
            // format).
            Assert.That(source, Does.Contain("ThumbnailListBox_PointerPressed"));
            Assert.That(source, Does.Contain("ThumbnailListBox_PointerMoved"));
            Assert.That(source, Does.Contain("ThumbnailListBox_DragStarting"));
            Assert.That(source, Does.Contain("ThumbnailListBox_DragOver"));
            Assert.That(source, Does.Contain("ThumbnailListBox_DragLeave"));
            Assert.That(source, Does.Contain("ThumbnailListBox_Drop"));
            Assert.That(source, Does.Contain("ThumbnailDragFormatId"));
            Assert.That(source, Does.Contain("private sealed record ThumbnailDragPayload("));
            Assert.That(source, Does.Contain("SidebarPageItem Source)"));
            Assert.That(source, Does.Contain("private const double ThumbnailDragThreshold"));

            // StartDragAsync is the WinUI DragDrop.DoDragDrop stand-in; the
            // pointer hooks need handledEventsToo because ListViewItem marks
            // PointerPressed handled for selection.
            Assert.That(source, Does.Contain("await ThumbnailListBox.StartDragAsync(point)"));
            Assert.That(source, Does.Contain("handledEventsToo: true"));
            Assert.That(source, Does.Contain("e.Data.SetData(ThumbnailDragFormatId, payload)"));
            Assert.That(source, Does.Contain("e.AllowedOperations = DataPackageOperation.Move"));
            // The Drop commit reads the payload from the package (WPF e.Data
            // parity), not the armed gesture field.
            Assert.That(source, Does.Contain("await e.DataView.GetDataAsync(ThumbnailDragFormatId) as ThumbnailDragPayload"));

            // Payload revalidation (WPF IsCurrentThumbnailDragPayload) —
            // host-active + session + path + identity checks all present.
            Assert.That(source, Does.Contain("IsCurrentThumbnailDragPayload"));
            Assert.That(source, Does.Contain("payload.SessionId == _loadSessionId"));
            Assert.That(source, Does.Contain("ReferenceEquals(SidebarPageItems[payload.SourceIndex], payload.Source)"));

            // Slot math + indicator (WPF TryResolveThumbnailDropSlot /
            // ShowThumbnailDropIndicator / ClearThumbnailDropIndicator).
            Assert.That(source, Does.Contain("TryResolveThumbnailDropSlot"));
            Assert.That(source, Does.Contain("ShowThumbnailDropIndicator"));
            Assert.That(source, Does.Contain("ClearThumbnailDropIndicator"));
            Assert.That(source, Does.Contain("ResetThumbnailDragState"));
            Assert.That(source, Does.Contain("origin.Y + (height / 2.0)"));
            Assert.That(source, Does.Contain("ContainerFromIndex(index)"));
        });
    }

    [Test]
    public void ThumbnailDropRunsTheStructuralOpPipeline()
    {
        string source = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // MovePageAsync = the WPF Drop commit half under the WinUI
            // structural-op invariants (admission lease + latch + rollback).
            Assert.That(source, Does.Contain("private async Task MovePageAsync("));
            Assert.That(source, Does.Contain("CaptureDocumentOperationLease(\n                payload.SessionId, payload.FilePath, payload.Source)"));
            Assert.That(source, Does.Contain("ThumbnailDropPlacement.ResolveFinalIndex"));
            Assert.That(source, Does.Contain("_pdfService.ReorderPagesAsync"));
            Assert.That(source, Does.Contain("BeginStructuralOperation"));
            Assert.That(source, Does.Contain("TryBeginDocumentEdit"));
            Assert.That(source, Does.Contain("TryRollbackStructuralOperationAsync"));
            Assert.That(source, Does.Contain("ReloadDocumentForOperationAsync"));
            Assert.That(source, Does.Contain("PageBookmarkService.ApplyPageMove"));
            Assert.That(source, Does.Contain("DocumentSnapshotAction"));
            Assert.That(source, Does.Contain("\"Editor.PageReorderFailed\""));
            Assert.That(source, Does.Contain("Editor.DocumentReloadFailed"));
        });

        // The transient sweep is a drag boundary (WPF CancelInteraction).
        int sweep = source.IndexOf("private void CloseTransientUi", StringComparison.Ordinal);
        int sweepEnd = source.IndexOf("public void SetHostActive", sweep, StringComparison.Ordinal);
        Assert.That(sweep, Is.GreaterThanOrEqualTo(0));
        Assert.That(sweepEnd, Is.GreaterThan(sweep));
        string sweepBody = source.Substring(sweep, sweepEnd - sweep);
        Assert.Multiple(() =>
        {
            Assert.That(sweepBody, Does.Contain("ResetThumbnailDragState()"));
            Assert.That(sweepBody, Does.Contain("ClearThumbnailDropIndicator()"));
        });

        // LoadPdfAsync head carries the WPF LoadPdf drag-state sweep too.
        int load = source.IndexOf("private async Task LoadPdfAsync(", StringComparison.Ordinal);
        Assert.That(load, Is.GreaterThanOrEqualTo(0));
        string loadHead = source.Substring(load, 700);
        Assert.Multiple(() =>
        {
            Assert.That(loadHead, Does.Contain("ClearThumbnailDropIndicator()"));
            Assert.That(loadHead, Does.Contain("ResetThumbnailDragState()"));
        });
    }

    [Test]
    public void ImmersiveModeKeepsTheWpfChromeHideAndPresenterContract()
    {
        string source = Read("Pages", "EditorPage.xaml.cs");
        string window = File.ReadAllText(Path.Combine(
            ProjectRoot(), "OpenNotes.WinUI", "MainWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            // Recorded pre-immersive chrome state (WPF :102-108).
            Assert.That(source, Does.Contain("private bool _isImmersiveMode"));
            Assert.That(source, Does.Contain("private double _preImmersiveToolbarOpacity"));
            Assert.That(source, Does.Contain("private bool _preImmersiveToolbarHitTestVisible"));
            Assert.That(source, Does.Contain("private double _preImmersiveSidebarOpacity"));
            Assert.That(source, Does.Contain("private bool _preImmersiveSidebarHitTestVisible"));
            Assert.That(source, Does.Contain("private double _preImmersiveSearchOpacity"));
            Assert.That(source, Does.Contain("private bool _preImmersiveSearchHitTestVisible"));

            // The visual hide is opacity + hit-test, not Visibility — no
            // reflow (WPF ToggleImmersiveMode parity).
            Assert.That(source, Does.Contain("private void ToggleImmersiveMode()"));
            Assert.That(source, Does.Contain("CloseToolFlyouts()"));
            Assert.That(source, Does.Contain("ToolbarBorder.Opacity = 0"));
            Assert.That(source, Does.Contain("ToolbarBorder.IsHitTestVisible = false"));
            Assert.That(source, Does.Contain("DocumentSidebar.Opacity = 0"));
            Assert.That(source, Does.Contain("PdfSearchPanel.Opacity = 0"));
            // T12-B review: margin writes route through the probe-aware
            // helper — a direct "PagesContainer.Margin = ..." write on the
            // immersive path would skip the DEBUG pages-margin-left
            // HelpText refresh.
            Assert.That(source, Does.Not.Contain("PagesContainer.Margin = PagesContainerDefaultMargin"));
            Assert.That(source, Does.Contain("UpdatePagesContainerMarginForSidebar()"));

            // F11 toggles only when a TextBox isn't being edited; Escape
            // leaves immersive before the generic Escape branches.
            Assert.That(source, Does.Contain("e.Key == VirtualKey.F11 && !textInputFocused"));
            Assert.That(source, Does.Contain("e.Key == VirtualKey.Escape && _isImmersiveMode"));

            // Tab deactivation exits immersive (window-global presenter).
            Assert.That(source, Does.Contain("if (_isImmersiveMode)\n                    ToggleImmersiveMode();"));

            // Window side: the FullScreen/Default presenter swap.
            Assert.That(source, Does.Contain("GetMainWindow()?.SetImmersiveFullscreen(_isImmersiveMode)"));
            Assert.That(window, Does.Contain("internal void SetImmersiveFullscreen(bool immersive)"));
            Assert.That(window, Does.Contain("AppWindowPresenterKind.FullScreen"));
            Assert.That(window, Does.Contain("AppWindowPresenterKind.Default"));
        });

        // Ordering pin — the immersive Escape gate must run BEFORE the
        // generic Escape sweep or it can never fire.
        int f11 = source.IndexOf("e.Key == VirtualKey.F11 && !textInputFocused", StringComparison.Ordinal);
        int immersiveEsc = source.IndexOf("e.Key == VirtualKey.Escape && _isImmersiveMode", StringComparison.Ordinal);
        int resizeEsc = source.IndexOf("e.Key == VirtualKey.Escape && _resizingTextContainer != null", StringComparison.Ordinal);
        int genericEsc = source.IndexOf("if (e.Key == VirtualKey.Escape)\n            {\n                CloseTransientUi(\"escape\")", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(f11, Is.GreaterThanOrEqualTo(0), "F11 gate missing");
            Assert.That(immersiveEsc, Is.GreaterThanOrEqualTo(0), "immersive Escape gate missing");
            Assert.That(resizeEsc, Is.GreaterThanOrEqualTo(0), "resize Escape gate missing");
            Assert.That(genericEsc, Is.GreaterThanOrEqualTo(0), "generic Escape branch missing");
            Assert.That(f11, Is.LessThan(immersiveEsc), "WPF order: F11 ahead of the immersive-Escape gate");
            Assert.That(immersiveEsc, Is.LessThan(resizeEsc), "immersive-Escape must run before resize-Escape (WPF)");
            Assert.That(immersiveEsc, Is.LessThan(genericEsc), "immersive-Escape must run before the generic Escape sweep");
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
