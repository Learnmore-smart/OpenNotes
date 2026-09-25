using System.IO;
using System.Linq;

namespace Caelum.Tests;

/// <summary>
/// Task 9 Phase A source contract — pins the WinUI save/load pipeline and
/// close/dirty protocol so a later refactor cannot silently drop pieces the
/// WPF editor relies on: the shared save task, autosave timer gating, edit
/// admission + release state, the prepare/save/release close protocol in
/// MainWindow, and the dirty flush before document-mutating operations.
/// The behavioural half lives in <see cref="DocumentSaveCoordinatorTests"/>
/// (the coordinator/admission/release/navigation types are Core types shared
/// by both shells).
/// </summary>
[TestFixture]
public sealed class WinUiSavePipelineSourceTests
{
    [Test]
    public void EditorPageExposesTheSharedSaveAndAutosavePipeline()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Coordinator stack (Core types, WPF-identical semantics).
            Assert.That(editor, Does.Contain("private readonly DocumentSaveCoordinator _documentSaveCoordinator = new()"));
            Assert.That(editor, Does.Contain("private readonly DocumentEditAdmission _editAdmission = new()"));
            Assert.That(editor, Does.Contain("private readonly DocumentReleaseState _releaseState = new()"));
            Assert.That(editor, Does.Contain("private readonly object _lifecycleGate = new()"));
            Assert.That(editor, Does.Contain("private readonly object _saveGate = new()"));
            Assert.That(editor, Does.Contain("private Task<DocumentSaveResult> _autoSaveInFlight"));

            // Autosave timer: repeating DispatcherQueueTimer armed at ctor +
            // Loaded, interlocked re-entry guard, host-active gate.
            Assert.That(editor, Does.Contain("private Microsoft.UI.Dispatching.DispatcherQueueTimer _autoSaveTimer"));
            Assert.That(editor, Does.Contain("private void EnsureAutoSaveTimer()"));
            Assert.That(editor, Does.Contain("AutoSaveTimer_Tick"));
            Assert.That(editor, Does.Contain("_autoSaveTimer.IsRepeating = true"));
            Assert.That(editor, Does.Contain("Interlocked.Exchange(ref _autoSaveTimerRunning, 1)"));
            Assert.That(editor, Does.Contain("Volatile.Write(ref _autoSaveTimerRunning, 0)"));
            Assert.That(editor, Does.Contain("Math.Max(15, _applicationSettings?.AutoSaveIntervalSeconds ?? 60)"));
            Assert.That(editor, Does.Contain("public void ApplySettings()"));

            // Shared save task + atomic save path + version sidecar.
            Assert.That(editor, Does.Contain("public async Task<bool> AutoSaveAsync("));
            Assert.That(editor, Does.Contain("private async Task<bool> SaveCurrentDocumentWithLeaseAsync("));
            Assert.That(editor, Does.Contain("private async Task SaveCurrentDocumentCoreAsync("));
            Assert.That(editor, Does.Contain("_documentSaveCoordinator.SaveAsync("));
            Assert.That(editor, Does.Contain("_pdfService.SaveAnnotationsToPdfAsync(_currentPdfPath, annotations)"));
            Assert.That(editor, Does.Contain("VersionControlService.SaveVersionAsync(filePath, annotations"));
            Assert.That(editor, Does.Contain("var annotations = CollectAnnotations();"));

            // Manual save entry points (toolbar + Ctrl+S).
            Assert.That(editor, Does.Contain("private async void SavePdf_Click("));
            Assert.That(editor, Does.Contain("private async Task SaveAnnotationsToPdfAsync()"));
            Assert.That(editor, Does.Contain("e.Key == VirtualKey.S"));

            // Toasts + failure dialog (localized WPF keys).
            Assert.That(editor, Does.Contain("Editor.AutoSaved"));
            Assert.That(editor, Does.Contain("Editor.AutoSaveFailed"));
            Assert.That(editor, Does.Contain("Editor.SavedSuccessfully"));
            Assert.That(editor, Does.Contain("Editor.SaveFailed"));
            Assert.That(editor, Does.Contain("Editor.NoDocumentLoaded"));

            // A fresh document resets the generation space.
            Assert.That(editor, Does.Contain("_documentSaveCoordinator.Reset();"));
            Assert.That(editor, Does.Contain("_documentSaveCoordinator.DirtyGeneration"));
        });
    }

    [Test]
    public void EditorPageImplementsTheCloseProtocol()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Lifecycle protocol surface (WPF PrepareFor*/ReleaseResourcesAsync).
            Assert.That(editor, Does.Contain("public Task<bool> PrepareForNavigationAsync("));
            Assert.That(editor, Does.Contain("public Task<bool> PrepareForCloseAsync("));
            Assert.That(editor, Does.Contain("public Task<bool> ReleaseResourcesAsync()"));
            Assert.That(editor, Does.Contain("public void CancelClosePreparation()"));
            Assert.That(editor, Does.Contain("public void ResumeDocumentInteraction()"));
            Assert.That(editor, Does.Contain("private async Task BeginDocumentInteractionBlockAsync("));
            Assert.That(editor, Does.Contain("private bool TryBeginDocumentEdit(out IDisposable lease)"));
            Assert.That(editor, Does.Contain("private void SetDocumentInteractionBlocked(bool blocked)"));

            // Admission close → quiescence → dispatcher barrier →
            // SaveUntilClean(finalClose) → CompleteClose ordering.
            Assert.That(editor, Does.Contain("_editAdmission.BeginClose();"));
            Assert.That(editor, Does.Contain("_editAdmission.WaitForQuiescenceAsync(cancellationToken)"));
            Assert.That(editor, Does.Contain("DispatcherQueueBarrierAsync"));
            Assert.That(editor, Does.Contain("_documentSaveCoordinator.SaveUntilCleanAsync("));
            Assert.That(editor, Does.Contain("finalClose: true"));
            Assert.That(editor, Does.Contain("_editAdmission.CompleteClose();"));
            Assert.That(editor, Does.Contain("_editAdmission.CancelClose();"));
            Assert.That(editor, Does.Contain("_documentSaveCoordinator.CancelCloseRequest();"));

            // Release state machine usage + shared in-flight release task.
            Assert.That(editor, Does.Contain("_releaseState.TryBeginRelease()"));
            Assert.That(editor, Does.Contain("_releaseState.MarkCleanupStarted()"));
            Assert.That(editor, Does.Contain("_releaseState.MarkSucceeded()"));
            Assert.That(editor, Does.Contain("_releaseState.MarkFailed()"));
            Assert.That(editor, Does.Contain("_releaseState.ResetAfterPreReleaseFailure()"));
            Assert.That(editor, Does.Contain("_releaseState.CanResumeInteraction"));
            Assert.That(editor, Does.Contain("_releaseState.IsReleaseInFlight"));
            Assert.That(editor, Does.Contain("_releaseResourcesInFlight"));
            Assert.That(editor, Does.Contain("_navigationPreparationInFlight"));
            Assert.That(editor, Does.Contain("_closePreparationInFlight"));

            // The teardown awaits the PdfService dispose (native owner) and
            // cancels the document-operation session.
            Assert.That(editor, Does.Contain("await _pdfService.DisposeAsync().AsTask()"));
            Assert.That(editor, Does.Contain("_documentOperationSession.Cancel();"));

            // A forced unload mid-protocol defers to the tracked tasks
            // instead of wiping the collectors under a pending save.
            Assert.That(editor, Does.Contain("DeferredTeardownAsync"));
        });
    }

    [Test]
    public void EditorPageGuardsDocumentMutatingOperations()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        // Undo/redo and the undo ledger push hold edit leases (quiescence).
        Assert.Multiple(() =>
        {
            int push = editor.IndexOf("private void PushUndoAction(", StringComparison.Ordinal);
            int undo = editor.IndexOf("private async Task PerformUndoAsync()", StringComparison.Ordinal);
            int redo = editor.IndexOf("private async Task PerformRedoAsync()", StringComparison.Ordinal);
            int insert = editor.IndexOf("private async Task InsertExternalDocumentAsync(", StringComparison.Ordinal);
            int rotate = editor.IndexOf("private async void RotateCurrentPage_Click(", StringComparison.Ordinal);
            Assert.That(push, Is.GreaterThanOrEqualTo(0));
            Assert.That(undo, Is.GreaterThanOrEqualTo(0));
            Assert.That(redo, Is.GreaterThanOrEqualTo(0));
            Assert.That(insert, Is.GreaterThanOrEqualTo(0));
            Assert.That(rotate, Is.GreaterThanOrEqualTo(0));

            Assert.That(editor.Substring(push, 1200), Does.Contain("TryBeginDocumentEdit(out var editLease)"),
                "PushUndoAction must hold an admission lease");
            Assert.That(editor.Substring(undo, 700), Does.Contain("TryBeginDocumentEdit(out var editLease)"),
                "PerformUndoAsync must hold an admission lease");
            Assert.That(editor.Substring(redo, 700), Does.Contain("TryBeginDocumentEdit(out var editLease)"),
                "PerformRedoAsync must hold an admission lease");
            Assert.That(editor.Substring(insert, 900), Does.Contain("TryBeginDocumentEdit(out var editLease)"),
                "InsertExternalDocumentAsync must hold an admission lease");
            Assert.That(editor.Substring(rotate, 1100), Does.Contain("TryBeginDocumentEdit(out var editLease)"),
                "RotateCurrentPage_Click must hold an admission lease");

            // Dirty flush before the binary PDF is rewritten.
            Assert.That(editor.Substring(insert, 2200), Does.Contain("_documentSaveCoordinator.IsDirty"),
                "Insert must flush a dirty document first");
            Assert.That(editor.Substring(rotate, 1600), Does.Contain("_documentSaveCoordinator.IsDirty"),
                "Rotate must flush a dirty document first");
        });
    }

    [Test]
    public void PushUndoActionRecordsDirtyStateOnBothPaths()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        int push = editor.IndexOf("private void PushUndoAction(", StringComparison.Ordinal);
        Assert.That(push, Is.GreaterThanOrEqualTo(0), "PushUndoAction missing");
        int end = editor.IndexOf("\n        }", push, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(push));
        string body = editor.Substring(push, end - push);

        Assert.Multiple(() =>
        {
            // Blocked path: the edit may already have mutated the model, so
            // the generation must be retained even when admission fails.
            Assert.That(body, Does.Contain("_documentSaveCoordinator.RecordChange(action.LeavesDocumentDirty);"),
                "blocked PushUndoAction must record the action's dirty flag");
            // Success path funnels through the WPF-parity helper.
            Assert.That(body, Does.Contain("ApplyDirtyStateForAction(action);"),
                "admitted PushUndoAction must record dirty state via ApplyDirtyStateForAction");
            Assert.That(body, Does.Contain("SyncDirtyStateMirror();"),
                "blocked PushUndoAction must sync the dirty mirror");

            int helper = editor.IndexOf("private void ApplyDirtyStateForAction(IUndoAction action)", StringComparison.Ordinal);
            Assert.That(helper, Is.GreaterThanOrEqualTo(0), "ApplyDirtyStateForAction missing");
            string helperBody = editor.Substring(helper, editor.IndexOf("\n        }", helper, StringComparison.Ordinal) - helper);
            Assert.That(helperBody, Does.Contain("_documentSaveCoordinator.RecordChange(action.LeavesDocumentDirty);"));
            Assert.That(helperBody, Does.Contain("SyncDirtyStateMirror();"));
        });
    }

    [Test]
    public void InkMutatedMarksDirtyOutsideAnnotationLoad()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        int handler = editor.IndexOf("private void PageControl_InkMutated(", StringComparison.Ordinal);
        Assert.That(handler, Is.GreaterThanOrEqualTo(0), "PageControl_InkMutated missing");
        int end = editor.IndexOf("\n        }", handler, StringComparison.Ordinal);
        string body = editor.Substring(handler, end - handler);

        Assert.Multiple(() =>
        {
            // WinUI quiet mutators raise InkMutated during the annotation
            // load sweep too — the dirty mark must stay behind the
            // _isLoadingAnnotations guard while thumbnail invalidation
            // remains unconditional (WPF marks dirty outright because its
            // quiet path raises a different event).
            int guard = body.IndexOf("if (!_isLoadingAnnotations)", StringComparison.Ordinal);
            int dirty = body.IndexOf("MarkDirty();", StringComparison.Ordinal);
            int thumbnail = body.IndexOf("InvalidateThumbnail(page.PageIndex);", StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThanOrEqualTo(0), "InkMutated must guard MarkDirty on _isLoadingAnnotations");
            Assert.That(dirty, Is.GreaterThan(guard), "MarkDirty must sit inside the !_isLoadingAnnotations guard");
            Assert.That(thumbnail, Is.GreaterThan(dirty), "thumbnail invalidation must remain unconditional");
        });
    }

    [Test]
    public void StructuralOperationsPushDocumentSnapshotUndo()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // The snapshot action + its helpers (WPF DocumentSnapshotAction,
            // ApplyDocumentSnapshotAsync, WriteDocumentBytesAsync,
            // ReloadDocumentForOperationAsync).
            Assert.That(editor, Does.Contain("private sealed class DocumentSnapshotAction : IUndoAction"));
            Assert.That(editor, Does.Contain("public bool LeavesDocumentDirty => false;"),
                "snapshot restores write the PDF itself — the transition is clean");
            Assert.That(editor, Does.Contain("public void SetOperationLease(DocumentOperationLease operationLease)"));
            Assert.That(editor, Does.Contain("public DocumentOperationLease CompletedOperationLease"));
            Assert.That(editor, Does.Contain("private async Task<DocumentOperationLease> ApplyDocumentSnapshotAsync("));
            Assert.That(editor, Does.Contain("private static Task WriteDocumentBytesAsync("));
            Assert.That(editor, Does.Contain("private async Task<DocumentOperationLease> ReloadDocumentForOperationAsync("));
            Assert.That(editor, Does.Contain("RecentFilesService.UpdateMetadata(filePath, _pageControls.Count"),
                "snapshot apply must refresh recent-file metadata for the reloaded document");
            Assert.That(editor, Does.Contain("PageBookmarkService.Replace(_owner._currentPdfPath, bookmarks);"),
                "snapshot apply must restore the persisted bookmark list");

            // Insert: before/after byte snapshots, bookmark snapshots, push,
            // and the failure rollback (bytes + sidecar + reload).
            int insert = editor.IndexOf("private async Task InsertExternalDocumentAsync(", StringComparison.Ordinal);
            Assert.That(insert, Is.GreaterThanOrEqualTo(0));
            int insertEnd = editor.IndexOf("        private async void RotateCurrentPage_Click(", insert, StringComparison.Ordinal);
            string insertBody = editor.Substring(insert, insertEnd - insert);
            Assert.That(insertBody, Does.Contain("before = await File.ReadAllBytesAsync(filePath, currentLease.Token);"));
            Assert.That(insertBody, Does.Contain("byte[] after = await File.ReadAllBytesAsync(filePath, currentLease.Token);"));
            Assert.That(insertBody, Does.Contain("beforeBookmarks = PageBookmarkService.Load(filePath).ToList();"));
            Assert.That(insertBody, Does.Contain("PageBookmarkService.ApplyPageInsert("));
            Assert.That(insertBody, Does.Contain("PushUndoAction(new DocumentSnapshotAction("));
            Assert.That(insertBody, Does.Contain("await WriteDocumentBytesAsync(filePath, before, currentLease.Token);"),
                "insert failure must roll the document bytes back");
            Assert.That(insertBody, Does.Contain("PageBookmarkService.Replace(filePath, beforeBookmarks ?? new List<PageBookmark>());"),
                "insert failure must restore the bookmark sidecar");

            // Rotate: before/after byte snapshots + snapshot push.
            int rotate = editor.IndexOf("private async void RotateCurrentPage_Click(", StringComparison.Ordinal);
            Assert.That(rotate, Is.GreaterThanOrEqualTo(0));
            int rotateEnd = editor.IndexOf("\n        }", rotate, StringComparison.Ordinal);
            rotateEnd = editor.IndexOf("\n        }", rotateEnd + 1, StringComparison.Ordinal);
            string rotateBody = editor.Substring(rotate, rotateEnd - rotate);
            Assert.That(rotateBody, Does.Contain("byte[] before = await File.ReadAllBytesAsync(filePath, operationLease.Token);"));
            Assert.That(rotateBody, Does.Contain("byte[] after = await File.ReadAllBytesAsync(filePath, operationLease.Token);"));
            Assert.That(rotateBody, Does.Contain("PushUndoAction(new DocumentSnapshotAction(this, before, after, pageIndex, pageIndex));"));

            // Undo/redo hand their lease to a snapshot action and validate
            // the lease of the RELOADED session it publishes.
            int undo = editor.IndexOf("private async Task PerformUndoAsync()", StringComparison.Ordinal);
            int redo = editor.IndexOf("private async Task PerformRedoAsync()", StringComparison.Ordinal);
            int update = editor.IndexOf("private void UpdateUndoRedoButtons()", StringComparison.Ordinal);
            Assert.That(undo, Is.GreaterThanOrEqualTo(0));
            Assert.That(redo, Is.GreaterThan(undo));
            Assert.That(update, Is.GreaterThan(redo));
            string undoBody = editor.Substring(undo, redo - undo);
            string redoBody = editor.Substring(redo, update - redo);
            foreach (var (name, body) in new[] { ("undo", undoBody), ("redo", redoBody) })
            {
                Assert.That(body, Does.Contain("snapshotAction.SetOperationLease(operationLease);"),
                    $"{name} must hand its lease to a DocumentSnapshotAction");
                Assert.That(body, Does.Contain("!ValidateDocumentOperationLease(snapshot.CompletedOperationLease)"),
                    $"{name} must validate the snapshot's post-reload lease");
                Assert.That(body, Does.Contain("completedSnapshotAction.SetOperationLease(null);"),
                    $"{name} must release the snapshot lease in finally");
            }
        });
    }

    [Test]
    public void MainWindowRunsTheTabAndWindowCloseProtocol()
    {
        string window = Read("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            // Workflow markers + bounded UI wait.
            Assert.That(window, Does.Contain("_windowCloseWorkflowActive"));
            Assert.That(window, Does.Contain("_allowWindowClose"));
            Assert.That(window, Does.Contain("_navigationWorkflowActive"));
            Assert.That(window, Does.Contain("_tabCloseWorkflows"));
            Assert.That(window, Does.Contain("CloseWorkflowTimeout"));
            Assert.That(window, Does.Contain("_windowCloseCts"));

            // App-close interception via AppWindow.Closing.
            Assert.That(window, Does.Contain("_appWindow.Closing += AppWindow_Closing"));
            Assert.That(window, Does.Contain("args.Cancel = true"));
            Assert.That(window, Does.Contain("CompleteWindowCloseAsync"));
            Assert.That(window, Does.Contain("ContinueTimedOutWindowCloseAsync"));

            // Tab close: prepare → FileAutoSaved toast → release → remove.
            Assert.That(window, Does.Contain("private async void CloseTab(AppTab tab)"));
            Assert.That(window, Does.Contain("PrepareForCloseAsync(timeout.Token).WaitAsync(timeout.Token)"));
            Assert.That(window, Does.Contain("ReleaseResourcesAsync()"));
            Assert.That(window, Does.Contain("RemoveTabAfterResourcesReleased(tab)"));
            Assert.That(window, Does.Contain("ContinueTimedOutTabCloseAsync("));
            Assert.That(window, Does.Contain("CancelClosePreparation()"));
            Assert.That(window, Does.Contain("Main.FileAutoSaved"));
            Assert.That(window, Does.Contain("Editor.SaveFailed"));

            // Navigation workflows run the same prepare barrier; Back uses
            // the Core journal coordinator so a stale queued click cancels.
            Assert.That(window, Does.Contain("NavigationCloseCoordinator.TryNavigateBackAsync"));
            Assert.That(window, Does.Contain("PrepareForNavigationAsync(timeout.Token)"));
            // Re-activation reopens admission on a nav-prepared editor.
            Assert.That(window, Does.Contain("ResumeDocumentInteraction()"));
            Assert.That(window, Does.Contain("private async void NavBack_Click("));
            Assert.That(window, Does.Contain("private async void NavForward_Click("));
            Assert.That(window, Does.Contain("private async void NavHome_Click("));
            Assert.That(window, Does.Contain("public async void NavigateActiveTabToFile("));
        });
    }

    [Test]
    public void CollectAnnotationsCoversAllEightCollections()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        int collect = editor.IndexOf("internal Dictionary<int, PageAnnotation> CollectAnnotations()",
            StringComparison.Ordinal);
        Assert.That(collect, Is.GreaterThanOrEqualTo(0), "CollectAnnotations missing");
        int end = editor.IndexOf("\n        }", collect, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(collect));
        string body = editor.Substring(collect, end - collect);

        Assert.Multiple(() =>
        {
            // All eight persisted PageAnnotation collections are collected
            // (hidden ink included — GetHiddenInkData writes the sidecar
            // annotation payload the Core writer persists).
            Assert.That(body, Does.Contain("pa.Strokes"));
            Assert.That(body, Does.Contain("pa.HiddenInks"));
            Assert.That(body, Does.Contain("pa.Texts"));
            Assert.That(body, Does.Contain("pa.StickyNotes"));
            Assert.That(body, Does.Contain("pa.Highlights"));
            Assert.That(body, Does.Contain("pa.Images.Add("));
            Assert.That(body, Does.Contain("pa.TextMarkups.Add("));
            Assert.That(body, Does.Contain("pa.AreaHighlights.Add("));
            Assert.That(body, Does.Contain("pa.HiddenInks.Count > 0"));
        });
    }

    [Test]
    public void SaveButtonIsWiredInXaml()
    {
        string xaml = Read("Pages", "EditorPage.xaml");
        int save = xaml.IndexOf("Editor.SavePdfButton", StringComparison.Ordinal);
        Assert.That(save, Is.GreaterThanOrEqualTo(0));
        int buttonStart = xaml.LastIndexOf("<Button", save, StringComparison.Ordinal);
        Assert.That(buttonStart, Is.GreaterThanOrEqualTo(0));
        string tag = xaml.Substring(buttonStart, save - buttonStart);
        Assert.That(tag, Does.Contain("Click=\"SavePdf_Click\""));
        Assert.That(tag, Does.Not.Contain("IsEnabled=\"False\""));
    }

    [Test]
    public void NavigationCloseCoordinatorLivesInCore()
    {
        var core = Read(Path.Combine("..", "OpenNotes.Core", "Services", "NavigationCloseCoordinator.cs"));
        Assert.That(core, Does.Contain("public static class NavigationCloseCoordinator"));
        Assert.That(core, Does.Contain("TryNavigateBackAsync"));
        // The WPF facade no longer carries its own copy — both shells share
        // the Core type.
        var wpfPath = Path.Combine(ProjectRoot(), "Services", "NavigationCloseCoordinator.cs");
        Assert.That(File.Exists(wpfPath), Is.False,
            "WPF should consume NavigationCloseCoordinator from Core");
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
