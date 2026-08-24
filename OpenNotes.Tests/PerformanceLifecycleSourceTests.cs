using System.IO;

namespace Caelum.Tests;

[TestFixture]
public sealed class PerformanceLifecycleSourceTests
{
    [Test]
    public void PdfPageControlSuspendsTransientRenderingWhenHostIsInactive()
    {
        var source = ReadProjectFile("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("public void SetHostActive(bool isActive)"));
            Assert.That(source, Does.Contain("StopSelectionDashAnimation();"));
            Assert.That(source, Does.Contain("if (HasSelection)"));
            Assert.That(source, Does.Contain("StartSelectionDashAnimation();"));
            Assert.That(source, Does.Contain("public void SetBitmapScalingMode(BitmapScalingMode scalingMode)"));
            Assert.That(source, Does.Contain("if (PdfImage.Source == null && PdfImageOverlay.Source == null)"));
        });
    }

    [Test]
    public void EditorKeepsOnlyAProfileBoundedBitmapWorkingSet()
    {
        var source = ReadProjectFile("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private const int ThumbnailCacheCapacity = 24;"));
            Assert.That(source, Does.Contain("private void TrimPageBitmapWorkingSet"));
            Assert.That(source, Does.Contain("public void SetHostActive(bool isActive)"));
            Assert.That(source, Does.Contain("public Task<bool> ReleaseResourcesAsync()"));
            Assert.That(source, Does.Contain("PdfRenderPolicy.CalculateRenderScale"));
            Assert.That(source, Does.Contain("PdfRenderPolicy.GetRetainedPageIndices"));
            Assert.That(source, Does.Contain("profile.PrefetchAdjacentPages"));
            Assert.That(source, Does.Contain("_scrollRenderDebounceTimer.Stop();"));
            Assert.That(source, Does.Contain("_scrollRenderDebounceTimer.Start();"));
            Assert.That(source, Does.Contain("_zoomRenderDebounceTimer.Stop();"));
            Assert.That(source, Does.Contain("_zoomRenderDebounceTimer.Start();"));
            Assert.That(source, Does.Contain("DispatcherPriority.Input"));
            Assert.That(source, Does.Contain("public void ResumeDocumentInteraction()"));
            Assert.That(source, Does.Contain("CommitStickyNoteEdit();"));
            Assert.That(source, Does.Contain("bool effectiveBlocked = blocked || !_releaseState.CanResumeInteraction;"));
            Assert.That(source, Does.Contain("_releaseState.MarkCleanupStarted();"));
            Assert.That(source, Does.Contain("if (!succeeded && _releaseState.CanResumeInteraction)"));
            Assert.That(source, Does.Contain("if (!_releaseState.CanResumeInteraction)"));
            Assert.That(source, Does.Contain("if (!Dispatcher.CheckAccess())"));
            Assert.That(source, Does.Contain("_documentSaveCoordinator.CancelCloseRequest();"));
        });
    }

    [Test]
    public void MainWindowCoordinatesEditorActivationAndFinalCleanup()
    {
        var source = ReadProjectFile("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("previousEditor.SetHostActive(false);"));
            Assert.That(source, Does.Contain("activeEditor.SetHostActive(WindowState != WindowState.Minimized);"));
            Assert.That(source, Does.Contain("activeEditor.ResumeDocumentInteraction();"));
            Assert.That(source, Does.Contain("navigatedEditor.SetHostActive(isActiveEditor);"));
            Assert.That(source, Does.Contain("navigatedEditor.ResumeDocumentInteraction();"));
            Assert.That(source, Does.Contain("_tabCloseWorkflows.Count > 0"));
            Assert.That(source, Does.Contain("_frameEditors"));
            Assert.That(source, Does.Contain("GetFrameEditors(tab.Frame)"));
            Assert.That(source, Does.Contain("Task<bool> releaseTask = editor.ReleaseResourcesAsync();"));
            Assert.That(source, Does.Contain("releaseTask.WaitAsync(cancellationToken)"));
        });
    }

    [Test]
    public void MainWindowCloseAndNavigationDoNotIgnoreSavePreparationFailure()
    {
        var source = ReadProjectFile("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("protected override void OnClosing(System.ComponentModel.CancelEventArgs e)"));
            Assert.That(source, Does.Not.Contain("protected override async void OnClosing"));
            Assert.That(source, Does.Contain("e.Cancel = true;"));
            Assert.That(source, Does.Contain("if (!await editor.PrepareForCloseAsync(timeout.Token))"));
            Assert.That(source, Does.Contain("if (!await editor.PrepareForNavigationAsync(timeout.Token))"));
            Assert.That(source, Does.Contain("Task<bool> releaseTask = editor.ReleaseResourcesAsync();"));
            Assert.That(source, Does.Contain("releaseTask.WaitAsync(cancellationToken)"));
            Assert.That(source, Does.Contain("await Dispatcher.InvokeAsync("));
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
