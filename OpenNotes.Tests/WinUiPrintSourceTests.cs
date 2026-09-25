using System;
using System.IO;
using System.Linq;

namespace Caelum.Tests;

/// <summary>
/// G1/G8 source contract — the WinUI print pipeline and the blank-menu
/// Refresh item. The GDI print path itself cannot run headless (no real
/// printer DC in CI), so these pin the wiring: the enabled context item +
/// Ctrl+P accelerator → lease-guarded <c>PrintPdfAsync</c> → Win32
/// PrintDlgEx/GDI spooler, the annotation-baked temp-copy render path, and
/// the full localized toast/dialog surface WPF uses. Behavioural coverage
/// of the pure math lives in <see cref="PrintPageGeometryTests"/>.
/// </summary>
[TestFixture]
public sealed class WinUiPrintSourceTests
{
    [Test]
    public void PrintMenuItemIsEnabledAndDrivesTheLeaseGuardedGdiPipeline()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Menu wiring (was: IsEnabled=false stub).
            Assert.That(editor, Does.Contain("Editor.ContextMenu.Print"));
            Assert.That(editor, Does.Not.Contain("IsEnabled = false // T9: print pipeline"),
                "the print stub must be gone — the item is live now");
            Assert.That(editor, Does.Contain("PrintMenuItem.Click += PrintMenuItem_Click"));
            Assert.That(editor, Does.Contain("private async void PrintMenuItem_Click("));

            // Ctrl+P keyboard parity (WPF EditorPage_KeyDown branch).
            Assert.That(editor, Does.Contain("e.Key == VirtualKey.P"));
            Assert.That(editor, Does.Contain("_ = PrintPdfAsync();"));

            // The pipeline: lease capture → Win32 sheet → overlay → bake →
            // rasterize → spool → toast/dialog.
            Assert.That(editor, Does.Contain("private async Task PrintPdfAsync("));
            Assert.That(editor, Does.Contain("Win32Print.TryShowPrintDialog("));
            Assert.That(editor, Does.Contain("Win32Print.PrintPages("));
            Assert.That(editor, Does.Contain("private async Task<IReadOnlyList<PrintablePageImage>> BuildPrintablePagesAsync("));
            Assert.That(editor, Does.Contain("private static IReadOnlyList<PrintablePageImage> RenderPrintablePages("));

            // Lease discipline: read-only op → operation lease (never the
            // structural latch), validation at every await boundary.
            Assert.That(editor, Does.Contain("operationLease ??= CaptureDocumentOperationLease(_pdfService)"));
        });

        // The structural latch must not appear INSIDE PrintPdfAsync's body.
        int print = editor.IndexOf("private async Task PrintPdfAsync(", StringComparison.Ordinal);
        int showFail = editor.IndexOf("private async Task ShowPrintFailureAsync(", StringComparison.Ordinal);
        Assert.That(print, Is.GreaterThanOrEqualTo(0));
        Assert.That(showFail, Is.GreaterThan(print));
        Assert.That(editor.Substring(print, showFail - print),
            Does.Not.Contain("_structuralOperationInFlight"));
    }

    [Test]
    public void PrintFlowKeepsTheWpfUxSurfaceAndTempCopyDiscipline()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Same localized keys the WPF path uses.
            Assert.That(editor, Does.Contain("LocalizationService.Get(\"Editor.NoDocumentLoaded\")"));
            Assert.That(editor, Does.Contain("LocalizationService.Get(\"Editor.PreparingPrint\")"));
            Assert.That(editor, Does.Contain("LocalizationService.Get(\"Editor.NoPagesToPrint\")"));
            Assert.That(editor, Does.Contain("LocalizationService.Get(\"Editor.PrintSent\")"));
            Assert.That(editor, Does.Contain("LocalizationService.Format(\"Editor.PrintFailed\""));
            Assert.That(editor, Does.Contain("LocalizationService.Get(\"Common.Error\")"));

            // Overlay + error surface (WinUI LoadingOverlay / dialog service).
            Assert.That(editor, Does.Contain("LoadingOverlay.Visibility = Visibility.Visible"));
            Assert.That(editor, Does.Contain("WinUiDialogService.ShowErrorAsync("));

            // Bake-in: atomic temp copy → SaveAnnotationsToPdfAsync → temp
            // cleanup — the exact WPF BuildPrintablePagesAsync recipe.
            Assert.That(editor, Does.Contain("Path.GetTempPath(), \"Caelum\", \"Print\""));
            Assert.That(editor, Does.Contain("PdfAtomicFile.CopyFile(filePath, tempPrintPath)"));
            Assert.That(editor, Does.Contain("_pdfService.SaveAnnotationsToPdfAsync(\n                        tempPrintPath, CollectAnnotations())"));
            Assert.That(editor, Does.Contain("try { File.Delete(tempPrintPath); } catch { }"));

            // Print-DPI rasterization through the shared resolver.
            Assert.That(editor, Does.Contain("PrintPageGeometry.ResolvePrintRenderDpi("));
            Assert.That(editor, Does.Contain("PdfiumRasterizerFactory.Shared.LoadFromFile(filePath)"));
        });
    }

    [Test]
    public void Win32PrintSpoolsBgraPagesThroughPrintDlgExAndGdi()
    {
        string svc = Read("Services", "Win32Print.cs");

        Assert.Multiple(() =>
        {
            // Classic print sheet (printer choice + page ranges + copies).
            Assert.That(svc, Does.Contain("PrintDlgEx"));
            Assert.That(svc, Does.Contain("PRINTDLGEX"));
            Assert.That(svc, Does.Contain("PD_USEDEVMODECOPIESANDCOLLATE"));
            Assert.That(svc, Does.Contain("PD_PAGENUMS"));
            Assert.That(svc, Does.Contain("lpPageRanges"));

            // Devmode copies/collate lifted into the job, DC self-created so
            // the driver cannot double-replicate.
            Assert.That(svc, Does.Contain("DmCopiesOffset"));
            Assert.That(svc, Does.Contain("DmCollateOffset"));
            Assert.That(svc, Does.Contain("CreateDCW"));
            Assert.That(svc, Does.Contain("GlobalFree"));

            // Spool loop: StartDoc → per-page fit + StretchDIBits → EndDoc.
            Assert.That(svc, Does.Contain("GetDeviceCaps"));
            Assert.That(svc, Does.Contain("LOGPIXELSX"));
            Assert.That(svc, Does.Contain("HORZRES"));
            Assert.That(svc, Does.Contain("StartDocW"));
            Assert.That(svc, Does.Contain("StartPage"));
            Assert.That(svc, Does.Contain("StretchDIBits"));
            Assert.That(svc, Does.Contain("EndPage"));
            Assert.That(svc, Does.Contain("EndDoc"));
            Assert.That(svc, Does.Contain("AbortDoc"));
            Assert.That(svc, Does.Contain("DeleteDC"));
            Assert.That(svc, Does.Contain("biHeight = -page.PixelHeight"),
                "pdfium BGRA is top-down — negative DIB height is required");

            // Fit math shared with Core + page-range expansion.
            Assert.That(svc, Does.Contain("PrintPageGeometry.FitPageToPrintableArea("));
            Assert.That(svc, Does.Contain("OrderedPageIndexes"));

            // Win32-only surface is platform-guarded.
            Assert.That(svc, Does.Contain("SupportedOSPlatform"));
        });
    }

    [Test]
    public void BlankContextMenuCarriesRefreshPageBetweenSelectAllAndDelete()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        int menuStart = editor.IndexOf("private void ShowBlankContextMenu()", StringComparison.Ordinal);
        int menuEnd = editor.IndexOf("flyout.ShowAt(PdfScrollViewer);", menuStart, StringComparison.Ordinal);
        Assert.That(menuStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(menuEnd, Is.GreaterThan(menuStart));
        string menu = editor.Substring(menuStart, menuEnd - menuStart);

        Assert.Multiple(() =>
        {
            Assert.That(menu, Does.Contain("Editor.Action.RefreshPage"));
            Assert.That(menu, Does.Contain("RefreshCurrentDocumentPreservingEditsAsync"));

            // WPF EnsureBlankContextMenu order: Copy, Paste, SelectAll,
            // RefreshPage, Delete.
            int selectAll = menu.IndexOf("Editor.Action.SelectAll", StringComparison.Ordinal);
            int refresh = menu.IndexOf("Editor.Action.RefreshPage", StringComparison.Ordinal);
            int delete = menu.IndexOf("Editor.Action.Delete", StringComparison.Ordinal);
            Assert.That(selectAll, Is.GreaterThanOrEqualTo(0));
            Assert.That(refresh, Is.GreaterThan(selectAll));
            Assert.That(delete, Is.GreaterThan(refresh));
        });

        // The reload path: save first, then reload under a fresh-session
        // lease (ReloadDocumentForOperationAsync retires the old one).
        int refreshBody = editor.IndexOf(
            "private async Task RefreshCurrentDocumentPreservingEditsAsync()", StringComparison.Ordinal);
        Assert.That(refreshBody, Is.GreaterThanOrEqualTo(0));
        string body = editor.Substring(refreshBody, 1800);
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("await AutoSaveAsync(operationLease)"));
            Assert.That(body, Does.Contain("await ReloadDocumentForOperationAsync("));
            Assert.That(body, Does.Contain("ValidateDocumentOperationLease(operationLease)"));
        });
    }

    [Test]
    public void DevNamesOffsetsAreCharacterBasedNotByteBased()
    {
        // DEVNAMES w*Offsets are in WCHARs — byte offset = offset * 2.
        // Using the raw char offset lands halfway into the string table and
        // CreateDCW fails on every print (spec-review catch).
        string source = Read("Services", "Win32Print.cs");
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Marshal.PtrToStringUni(ptr + (int)offset * 2)"));
            Assert.That(source, Does.Not.Contain("PtrToStringUni(ptr + offset)"));
            Assert.That(source, Does.Contain("GlobalLock(hDevNames)"));
            Assert.That(source, Does.Contain("returned no DEVMODE"));
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
