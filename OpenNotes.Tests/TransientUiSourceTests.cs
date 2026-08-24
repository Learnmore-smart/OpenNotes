using System;
using System.IO;
using System.Linq;

namespace Caelum.Tests;

[TestFixture]
public sealed class TransientUiSourceTests
{
    [Test]
    public void EditorExposesOneIdempotentTransientCloseSweep()
    {
        var source = ReadProjectFile("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("public void CloseTransientUi(string reason = null)"));
            Assert.That(source, Does.Contain("_transientUiRegistry"));
            Assert.That(source, Does.Contain("CancelInteraction(reason)"));
            Assert.That(source, Does.Contain("_pdfSearchCts?.Cancel()"));
            Assert.That(source, Does.Contain("ContextMenu"));
            Assert.That(source, Does.Contain("IsDropDownOpen = false"));
            Assert.That(source, Does.Contain("CancelStickyNoteEdit();"));
            Assert.That(source, Does.Contain("CloseTransientUi(\""));
        });
    }

    [Test]
    public void EditorRegistryCoversEveryPopupFamilyAndReleasesOnUnload()
    {
        var source = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        var xaml = ReadProjectFile("Pages", "EditorPage.xaml");

        foreach (var expected in new[]
        {
            "_penPopup", "_highlighterPopup", "_eraserPopup", "_shapePopup",
            "_selectionPopup", "_textColorPopup", "_stickyNotePopup",
            "_textFontFamilyCombo", "_textAlignmentCombo", "PdfViewerContextMenu",
            "VersionHistory"
        })
            Assert.That(source + Environment.NewLine + xaml, Does.Contain(expected), expected);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("EditorPage_Unloaded"));
            Assert.That(source, Does.Contain("CloseTransientUi(\"unloaded\")"));
            Assert.That(source, Does.Contain("CloseTransientUi(\"navigation\")"));
            Assert.That(source, Does.Contain("CloseTransientUi(\"release\")"));
            Assert.That(source, Does.Contain("UnfixPopupTopmost(_textColorPopup)"));
            Assert.That(source, Does.Contain("UnfixComboBoxPopupTopmost(_textFontFamilyCombo)"));
            Assert.That(source, Does.Contain("UnfixComboBoxPopupTopmost(_textAlignmentCombo)"));
            Assert.That(source, Does.Contain("UnfixContextMenuTopmost(PdfViewerContextMenu)"));
            Assert.That(source, Does.Contain("_stickyNoteEditingSessionId"));
            Assert.That(source, Does.Contain("SetStickyNoteTextQuiet"));
            Assert.That(source, Does.Contain("RemoveTextContainerQuiet"));
        });
    }

    [Test]
    public void MainWindowDeactivatedClosesAllRetainedEditors()
    {
        var source = ReadProjectFile("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Deactivated += MainWindow_Deactivated"));
            Assert.That(source, Does.Contain("private void MainWindow_Deactivated"));
            Assert.That(source, Does.Contain("CloseTransientUi(\"window deactivated\")"));
            Assert.That(source, Does.Contain("_frameEditors"));
        });
    }

    [Test]
    public void SharedCancellationCoversPageGesturesTextGesturesAndLoadBoundary()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        var page = ReadProjectFile("Controls", "PdfPageControl.xaml.cs");
        var mainWindow = ReadProjectFile("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(editor, Does.Contain("IInteractionCancellation"));
            Assert.That(editor, Does.Contain("public void CancelInteraction(string reason = null)"));
            Assert.That(editor, Does.Contain("InteractionCancellation.CancelAll(_pageControls, reason)"));
            Assert.That(editor, Does.Contain("CancelTextBoxDrag(restoreBounds: true)"));
            Assert.That(editor, Does.Contain("CancelTextResize(restoreBounds: true)"));
            Assert.That(editor, Does.Contain("DragHandle_LostMouseCapture"));
            Assert.That(editor, Does.Contain("DragHandle_LostStylusCapture"));
            Assert.That(editor, Does.Contain("TextResizeHandle_LostMouseCapture"));
            Assert.That(editor, Does.Contain("TextResizeHandle_LostStylusCapture"));
            Assert.That(page, Does.Contain("SelectionOverlayCanvas_LostMouseCapture"));
            Assert.That(page, Does.Contain("SelectionOverlayCanvas_LostStylusCapture"));
            Assert.That(mainWindow, Does.Contain("editor.CancelInteraction(\"window deactivated\")"));
            Assert.That(mainWindow.IndexOf("editor.CancelInteraction(\"window deactivated\")"), Is.LessThan(
                mainWindow.IndexOf("editor.CloseTransientUi(\"window deactivated\")")));
        });

        int close = editor.IndexOf("CloseTransientUi(\"load\")", StringComparison.Ordinal);
        int clear = editor.IndexOf("PagesContainer.Children.Clear()", StringComparison.Ordinal);
        Assert.That(close, Is.GreaterThanOrEqualTo(0));
        Assert.That(clear, Is.GreaterThan(close));
    }

    [Test]
    public void TransientSweepUnfixesEveryDetachedOwnerAndReopensWithFreshHooks()
    {
        var source = ReadProjectFile("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("UnfixTransientUiHooks"));
            Assert.That(source, Does.Contain("UnfixPopupTopmost(_textColorPopup)"));
            Assert.That(source, Does.Contain("UnfixComboBoxPopupTopmost(_textFontFamilyCombo)"));
            Assert.That(source, Does.Contain("UnfixComboBoxPopupTopmost(_textAlignmentCombo)"));
            Assert.That(source, Does.Contain("UnfixContextMenuTopmost(PdfViewerContextMenu)"));
            Assert.That(source, Does.Contain("EnsureTransientUiHooks"));
            Assert.That(source, Does.Contain("EnsureTransientUiHooks();"));
        });
    }

    [Test]
    public void PopupZOrderHooksRemainIdempotentAndDetachable()
    {
        var source = ReadProjectFile("Services", "PopupZOrderHelper.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ConditionalWeakTable<Popup"));
            Assert.That(source, Does.Contain("UnfixPopupTopmost"));
            Assert.That(source, Does.Contain("UnfixContextMenuTopmost"));
            Assert.That(source, Does.Contain("UnfixComboBoxPopupTopmost"));
            Assert.That(source, Does.Contain("HWND_NOTOPMOST"));
        });
    }

    [Test]
    public void AsyncDocumentOperationsShareOneSessionLeaseBoundary()
    {
        var session = ReadProjectFile("Services", "DocumentOperationSession.cs");
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(session, Does.Contain("public void Begin("));
            Assert.That(session, Does.Contain("public DocumentOperationLease Capture("));
            Assert.That(session, Does.Contain("public bool Validate("));
            Assert.That(session, Does.Contain("public void Cancel()"));
            Assert.That(session, Does.Contain("NormalizePath"));
            Assert.That(editor, Does.Contain("_documentOperationSession.Begin(sessionId, filePath, _pdfService)"));
            Assert.That(editor, Does.Contain("_documentOperationSession.Cancel()"));
            Assert.That(editor, Does.Contain("private bool ValidateDocumentOperationLease("));
            Assert.That(editor, Does.Contain("ReloadDocumentForOperationAsync"));
            Assert.That(editor, Does.Contain("using var operationLease = CaptureDocumentOperationLease("));
            Assert.That(editor, Does.Contain("await Services.VersionControlService.LoadVersionAsync(vFile, operationLease.Token)"));
            Assert.That(editor, Does.Contain("await RefreshOutlineCoreAsync(cancellationToken, sessionId, filePath, operationLease)"));
            Assert.That(editor, Does.Contain("await action.UndoAsync()"));
            Assert.That(editor, Does.Contain("await action.RedoAsync()"));
        });
    }

    [Test]
    public void StaleMenuAndStructuralCallbacksFailClosedBeforeErrorOrUndo()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(editor, Does.Contain("if (!ValidateDocumentOperationLease(operationLease))\n                                return;"));
            Assert.That(editor, Does.Contain("if (!ValidateDocumentOperationLease(currentLease))\n                        return;"));
            Assert.That(editor, Does.Contain("await InsertExternalDocumentAsync("));
            Assert.That(editor, Does.Contain("operationLease);"));
            Assert.That(editor, Does.Contain("catch (OperationCanceledException)"));
            Assert.That(editor, Does.Contain("if (!ValidateDocumentOperationLease(operationLease))\n                            return;\n            GetMainWindow()?.ShowToast(LocalizationService.Get(\"Editor.VersionLoadFailed\"))"));
            Assert.That(editor, Does.Contain("PushUndoAction(new DocumentSnapshotAction"));
            Assert.That(editor, Does.Contain("RefreshBookmarks(_loadSessionId, filePath, currentLease)"));
        });
    }

    [Test]
    public void VersionHistoryAndThumbnailCallbacksStaySilentAtFinalPublishBoundaries()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");

        int versionLoad = editor.IndexOf(
            "await LoadAnnotationsFromPdfServiceAsync(operationLease);",
            StringComparison.Ordinal);
        int versionToast = editor.IndexOf(
            "GetMainWindow()?.ShowToast(LocalizationService.Format(\"Editor.RestoredVersion\"",
            versionLoad,
            StringComparison.Ordinal);
        int versionDirty = editor.IndexOf("MarkDirty();", versionToast, StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(versionLoad, Is.GreaterThanOrEqualTo(0));
            Assert.That(versionToast, Is.GreaterThan(versionLoad));
            Assert.That(versionDirty, Is.GreaterThan(versionToast));
        });

        // A reload can happen while the restored snapshot is being injected.
        // The final toast and dirty publication must each have a live lease
        // boundary; checking only before the toast leaves MarkDirty exposed.
        string versionPublishWindow = editor.Substring(versionToast, versionDirty - versionToast);
        Assert.That(versionPublishWindow, Does.Contain("ValidateDocumentOperationLease(operationLease)"));

        int thumbnailMethod = editor.IndexOf(
            "private async void ThumbnailListBoxItem_Loaded",
            StringComparison.Ordinal);
        int thumbnailCatch = editor.IndexOf(
            "catch (Exception ex)",
            thumbnailMethod,
            StringComparison.Ordinal);
        Assert.That(thumbnailMethod, Is.GreaterThanOrEqualTo(0));
        Assert.That(thumbnailCatch, Is.GreaterThanOrEqualTo(0));
        string thumbnailCatchBody = editor.Substring(thumbnailCatch, Math.Min(420, editor.Length - thumbnailCatch));
        Assert.That(thumbnailCatchBody, Does.Contain("ValidateDocumentOperationLease(operationLease, model)"));
    }

    [Test]
    public void SidebarSessionAndThumbnailContinuationUseNormalizedLiveIdentity()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        int gateStart = editor.IndexOf("private sealed class SidebarLoadSessionGate", StringComparison.Ordinal);
        int gateEnd = editor.IndexOf("private sealed class ContextMenuOperationBinding", gateStart, StringComparison.Ordinal);
        Assert.That(gateStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(gateEnd, Is.GreaterThan(gateStart));

        string gate = editor.Substring(gateStart, gateEnd - gateStart);
        Assert.That(gate, Does.Contain("DocumentOperationSession.NormalizePath(filePath)"));

        int thumbnailMethod = editor.IndexOf(
            "private async void ThumbnailListBoxItem_Loaded",
            StringComparison.Ordinal);
        int cache = editor.IndexOf("CacheThumbnail(model.PageIndex, bitmap);", StringComparison.Ordinal);
        int liveModelGuard = editor.IndexOf(
            "ReferenceEquals(item.DataContext, model)",
            thumbnailMethod,
            StringComparison.Ordinal);
        Assert.That(cache, Is.GreaterThanOrEqualTo(0));
        Assert.That(liveModelGuard, Is.GreaterThanOrEqualTo(thumbnailMethod));
        Assert.That(liveModelGuard, Is.LessThan(cache));
    }

    [Test]
    public void OutlineReadErrorsAreSilentAfterSessionExpiry()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        int outlineMethod = editor.IndexOf("private async Task RefreshOutlineCoreAsync", StringComparison.Ordinal);
        int outlineCatch = editor.IndexOf(
            "System.Diagnostics.Debug.WriteLine($\"[Outline] Failed to read outline",
            outlineMethod,
            StringComparison.Ordinal);
        int outlineAfterCatch = editor.IndexOf(
            "if (!IsSidebarLoadCurrent(sessionId, filePath)",
            outlineCatch,
            StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(outlineMethod, Is.GreaterThanOrEqualTo(0));
            Assert.That(outlineCatch, Is.GreaterThan(outlineMethod));
            Assert.That(outlineAfterCatch, Is.GreaterThan(outlineCatch));
        });

        int outlineCatchStart = editor.LastIndexOf("catch (Exception ex)", outlineCatch, StringComparison.Ordinal);
        Assert.That(outlineCatchStart, Is.GreaterThan(outlineMethod));
        string outlineErrorPath = editor.Substring(outlineCatchStart, outlineAfterCatch - outlineCatchStart);
        Assert.That(outlineErrorPath, Does.Contain("ValidateDocumentOperationLease(operationLease)"));
    }

    [Test]
    public void NavigationAndClosePersistenceCallbacksCarryTheDocumentLease()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        foreach (string method in new[]
        {
            "private async Task<bool> PrepareForNavigationCoreAsync",
            "private async Task<bool> PrepareForCloseCoreAsync"
        })
        {
            int start = editor.IndexOf(method, StringComparison.Ordinal);
            int next = editor.IndexOf("private async Task", start + method.Length, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), method);
            Assert.That(next, Is.GreaterThan(start), method);
            string body = editor.Substring(start, next - start);
            Assert.That(body, Does.Contain("using var operationLease = CaptureDocumentOperationLease(_pdfService);"), method);
            Assert.That(body, Does.Contain("ValidateDocumentOperationLease(operationLease)"), method);
            Assert.That(body, Does.Contain("SaveCurrentDocumentCoreAsync(generation, operationLease)"), method);
        }
    }

    [Test]
    public void AnnotationLoadFinallyCannotClearAReplacementLoadState()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        int method = editor.IndexOf("private async Task LoadAnnotationsFromPdfServiceAsync", StringComparison.Ordinal);
        int finallyStart = editor.IndexOf("finally", method, StringComparison.Ordinal);
        int finallyEnd = editor.IndexOf("await Task.CompletedTask;", finallyStart, StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(method, Is.GreaterThanOrEqualTo(0));
            Assert.That(finallyStart, Is.GreaterThan(method));
            Assert.That(finallyEnd, Is.GreaterThan(finallyStart));
        });

        string finallyBody = editor.Substring(finallyStart, finallyEnd - finallyStart);
        Assert.That(finallyBody, Does.Contain("ValidateDocumentOperationLease(operationLease)"));
    }

    [Test]
    public void PdfTransientAsyncCallbacksSilenceStaleExceptions()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        foreach (string method in new[]
        {
            "private async void PageControl_PdfTextSelectionPointerPressed",
            "private async void PdfSearchTextBox_TextChanged"
        })
        {
            int start = editor.IndexOf(method, StringComparison.Ordinal);
            int next = editor.IndexOf("private ", start + method.Length, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), method);
            Assert.That(next, Is.GreaterThan(start), method);
            string body = editor.Substring(start, next - start);
            Assert.That(body, Does.Contain("catch (Exception ex)"), method);
            Assert.That(body, Does.Contain("ValidateDocumentOperationLease(operationLease"), method);
        }

        int jumpStart = editor.IndexOf(
            "private async Task JumpToPdfSearchResultAsync",
            StringComparison.Ordinal);
        int jumpEnd = editor.IndexOf(
            "private async Task MovePdfSearchSelectionAsync",
            jumpStart,
            StringComparison.Ordinal);
        Assert.That(jumpStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(jumpEnd, Is.GreaterThan(jumpStart));
        string jumpBody = editor.Substring(jumpStart, jumpEnd - jumpStart);
        Assert.That(jumpBody, Does.Contain("catch (Exception ex)"));
        Assert.That(jumpBody, Does.Contain("ValidateDocumentOperationLease(operationLease, result)"));
    }

    [Test]
    public void ThumbnailLoadingMarkersRemainSessionScopedAfterReload()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(editor, Does.Contain("_thumbnailPageLoadSessions"));
            Assert.That(editor, Does.Contain("_thumbnailPageLoadSessions[model.PageIndex] = sessionId"));
            Assert.That(editor, Does.Contain("_thumbnailPageLoadSessions.TryGetValue(model.PageIndex"));
            Assert.That(editor, Does.Contain("loadingSessionId == sessionId"));
        });
    }

    [Test]
    public void AutosaveAndPrintCallbacksReleaseAndPublishOnlyLiveLeases()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        int autosave = editor.IndexOf("public async Task<bool> AutoSaveAsync", StringComparison.Ordinal);
        int autosaveCatch = editor.IndexOf("catch (Exception ex)", autosave, StringComparison.Ordinal);
        int autosaveEnd = editor.IndexOf("finally", autosaveCatch, StringComparison.Ordinal);
        int diagnostic = editor.IndexOf("[AutoSave] Failed", autosaveCatch, StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(autosave, Is.GreaterThanOrEqualTo(0));
            Assert.That(autosaveCatch, Is.GreaterThan(autosave));
            Assert.That(autosaveEnd, Is.GreaterThan(autosaveCatch));
            Assert.That(diagnostic, Is.GreaterThan(autosaveCatch));
            Assert.That(editor.Substring(autosaveCatch, diagnostic - autosaveCatch),
                Does.Contain("ValidateDocumentOperationLease(operationLease)"));
        });

        int printClick = editor.IndexOf("private async void PrintPdf_Click", StringComparison.Ordinal);
        int printEnd = editor.IndexOf("private void PdfScrollViewer_PreviewMouseRightButtonUp", printClick, StringComparison.Ordinal);
        Assert.That(printClick, Is.GreaterThanOrEqualTo(0));
        Assert.That(printEnd, Is.GreaterThan(printClick));
        string printBody = editor.Substring(printClick, printEnd - printClick);
        Assert.That(printBody, Does.Contain("using var operationLease = CaptureDocumentOperationLease(_pdfService);"));
        Assert.That(printBody, Does.Contain("await PrintPdfAsync(operationLease);"));
    }

    [Test]
    public void DeferredContextCallbacksRespectTheInteractionBarrier()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        int version = editor.IndexOf("item.Click += async (s, args) =>", StringComparison.Ordinal);
        int versionEnd = editor.IndexOf("menu.Items.Add(item);", version, StringComparison.Ordinal);
        Assert.That(version, Is.GreaterThanOrEqualTo(0));
        Assert.That(versionEnd, Is.GreaterThan(version));
        string versionBody = editor.Substring(version, versionEnd - version);
        Assert.That(versionBody, Does.Contain("TryBeginDocumentEdit(out var editLease)"));

        int pdfCapture = editor.IndexOf("private bool TryCapturePdfContextMenuLease", StringComparison.Ordinal);
        int pdfCaptureEnd = editor.IndexOf("private async void ContextMenu_PrintClick", pdfCapture, StringComparison.Ordinal);
        Assert.That(pdfCapture, Is.GreaterThanOrEqualTo(0));
        Assert.That(pdfCaptureEnd, Is.GreaterThan(pdfCapture));
        string pdfCaptureBody = editor.Substring(pdfCapture, pdfCaptureEnd - pdfCapture);
        Assert.Multiple(() =>
        {
            Assert.That(pdfCaptureBody, Does.Contain("_documentInteractionBlocked"));
            Assert.That(pdfCaptureBody, Does.Contain("_resourcesReleased"));
            Assert.That(pdfCaptureBody, Does.Contain("_isHostActive"));
        });
    }

    [Test]
    public void ContextMenusBindAtOpenAndCaptureTheBoundDocumentLease()
    {
        var editor = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        var xaml = ReadProjectFile("Pages", "EditorPage.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(editor, Does.Contain("private sealed class ContextMenuOperationBinding"));
            Assert.That(editor, Does.Contain("PdfViewerContextMenu_Opened"));
            Assert.That(editor, Does.Contain("private bool TryCapturePdfContextMenuLease("));
            Assert.That(editor, Does.Contain("binding.SessionId"));
            Assert.That(editor, Does.Contain("binding.FilePath"));
            Assert.That(editor, Does.Contain("if (sender is MenuItem)"));
            Assert.That(editor, Does.Contain("e.Handled = true;"));
            Assert.That(editor, Does.Contain("_thumbnailDragSessionId"));
            Assert.That(editor, Does.Contain("dragSessionId"));
            Assert.That(xaml, Does.Contain("Opened=\"PdfViewerContextMenu_Opened\""));
        });
    }

    private static string ReadProjectFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
    }
}
