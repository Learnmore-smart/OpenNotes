using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caelum.Models;
using Caelum.Services;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
public sealed class PdfSaveCoordinatorTests
{
    [Test]
    public async Task SameNormalizedPathRunsOnlyOneDelegateAtATimeAndRemovesIdleGate()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"), "Document.pdf");
        int inFlight = 0;
        int maxInFlight = 0;
        int completed = 0;

        var saves = Enumerable.Range(0, 10)
            .Select(_ => PdfSaveCoordinator.RunExclusiveAsync(
                path.ToLowerInvariant(),
                () =>
                {
                    int current = Interlocked.Increment(ref inFlight);
                    while (true)
                    {
                        int observed = Volatile.Read(ref maxInFlight);
                        if (current <= observed || Interlocked.CompareExchange(ref maxInFlight, current, observed) == observed)
                            break;
                    }

                    Interlocked.Decrement(ref inFlight);
                    Interlocked.Increment(ref completed);
                    return Task.CompletedTask;
                }))
            .ToArray();

        await Task.WhenAll(saves);

        Assert.Multiple(() =>
        {
            Assert.That(maxInFlight, Is.EqualTo(1));
            Assert.That(completed, Is.EqualTo(10));
            Assert.That(PdfSaveCoordinator.ActiveGateCount, Is.Zero);
        });
    }

    [Test]
    public async Task DifferentNormalizedPathsCanRunInParallel()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        string firstPath = Path.Combine(root, "first.pdf");
        string secondPath = Path.Combine(root, "second.pdf");
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task first = PdfSaveCoordinator.RunExclusiveAsync(firstPath, async () =>
        {
            firstEntered.SetResult(true);
            await release.Task;
        });
        Task second = PdfSaveCoordinator.RunExclusiveAsync(secondPath, async () =>
        {
            secondEntered.SetResult(true);
            await release.Task;
        });

        await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult(true);
        await Task.WhenAll(first, second);

        Assert.That(PdfSaveCoordinator.ActiveGateCount, Is.Zero);
    }

    [Test]
    public async Task MultiPathLeaseUsesDeterministicOrderAndAvoidsCrossedDeadlock()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        string firstPath = Path.Combine(root, "a.pdf");
        string secondPath = Path.Combine(root, "b.pdf");
        var firstEntered = NewSignal();
        var secondEntered = NewSignal();
        var release = NewSignal();

        Task first = PdfSaveCoordinator.RunExclusiveAsync(
            new[] { firstPath, secondPath },
            async () =>
            {
                firstEntered.SetResult(true);
                await release.Task;
            });
        Task second = PdfSaveCoordinator.RunExclusiveAsync(
            new[] { secondPath.ToUpperInvariant(), firstPath.ToUpperInvariant() },
            async () =>
            {
                secondEntered.SetResult(true);
                await release.Task;
            });

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult(true);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(PdfSaveCoordinator.ActiveGateCount, Is.Zero);
    }

    [Test]
    public void AtomicReplacementFailureLeavesOriginalAndCleansTemp()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string target = Path.Combine(root, "atomic.pdf");
        File.WriteAllText(target, "original");
        string temp = PdfAtomicFile.CreateTempPath(target);
        File.WriteAllText(temp, "new");

        try
        {
            Assert.That(
                () => PdfAtomicFile.Replace(temp, target, (_, _) => throw new IOException("injected replace failure")),
                Throws.TypeOf<IOException>());

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(target), Is.EqualTo("original"));
                Assert.That(File.Exists(temp), Is.False,
                    "the atomic writer must clean a failed same-directory temp file");
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task RelativeAndDotSegmentAliasesShareOneNormalizedGate()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        string absolutePath = Path.Combine(root, "nested", "..", "alias.pdf");
        string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, absolutePath);
        var entered = NewSignal();
        var release = NewSignal();
        int inFlight = 0;
        int maxInFlight = 0;

        async Task Save()
        {
            int current = Interlocked.Increment(ref inFlight);
            int previous;
            do
            {
                previous = Volatile.Read(ref maxInFlight);
            } while (current > previous && Interlocked.CompareExchange(ref maxInFlight, current, previous) != previous);

            entered.TrySetResult(true);
            await release.Task;
            Interlocked.Decrement(ref inFlight);
        }

        Task first = PdfSaveCoordinator.RunExclusiveAsync(relativePath, Save);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task second = PdfSaveCoordinator.RunExclusiveAsync(absolutePath, Save);
        release.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.Multiple(() =>
        {
            Assert.That(maxInFlight, Is.EqualTo(1));
            Assert.That(PdfSaveCoordinator.ActiveGateCount, Is.Zero);
        });
    }

    [Test]
    public async Task CancelledWaiterDoesNotEnterDelegateOrLeakGateUser()
    {
        string path = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"), "cancel.pdf");
        var entered = NewSignal();
        var release = NewSignal();
        Task first = PdfSaveCoordinator.RunExclusiveAsync(path, async () =>
        {
            entered.SetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool delegateRan = false;
        Assert.That(
            async () => await PdfSaveCoordinator.RunExclusiveAsync(
                Path.GetFullPath(path),
                () =>
                {
                    delegateRan = true;
                    return Task.CompletedTask;
                },
                cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(delegateRan, Is.False);
        release.SetResult(true);
        await first;
        Assert.That(PdfSaveCoordinator.ActiveGateCount, Is.Zero);
    }

    [Test]
    public void WhitespacePathIsRejectedBeforeCreatingAGate()
    {
        Assert.That(
            () => PdfSaveCoordinator.RunExclusiveAsync("  ", () => Task.CompletedTask),
            Throws.TypeOf<ArgumentException>());
        Assert.That(PdfSaveCoordinator.ActiveGateCount, Is.Zero);
    }

    [Test]
    public async Task DelegateFailureReleasesGateForTheNextSave()
    {
        string path = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"), "failure.pdf");

        Assert.That(
            async () => await PdfSaveCoordinator.RunExclusiveAsync(
                path,
                () => Task.FromException(new InvalidOperationException("expected save failure"))),
            Throws.TypeOf<InvalidOperationException>());

        bool ranAfterFailure = false;
        await PdfSaveCoordinator.RunExclusiveAsync(path.ToUpperInvariant(), () =>
        {
            ranAfterFailure = true;
            return Task.CompletedTask;
        });

        Assert.Multiple(() =>
        {
            Assert.That(ranAfterFailure, Is.True);
            Assert.That(PdfSaveCoordinator.ActiveGateCount, Is.Zero);
        });
    }

    [Test]
    public async Task ConcurrentPdfServiceSavesRemainReadableAndPreserveAtomicWritePath()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "concurrent.pdf");
        CreateTestPdf(path);

        var services = Enumerable.Range(0, 8).Select(_ => new PdfService()).ToList();
        try
        {
            await Task.WhenAll(services.Select((service, index) =>
                service.SaveAnnotationsToPdfAsync(
                    path,
                    new Dictionary<int, PageAnnotation>
                    {
                        [0] = new PageAnnotation
                        {
                            Texts = new List<TextAnnotation>
                            {
                                new() { Text = $"save-{index}", X = 80 + index, Y = 100 }
                            }
                        }
                    })));

            using var document = PdfReader.Open(path, PdfDocumentOpenMode.ReadOnly);
            Assert.That(document.PageCount, Is.EqualTo(1));
            Assert.That(document.Pages[0].Elements.GetArray("/Annots"), Is.Not.Null);
        }
        finally
        {
            foreach (var service in services)
                await service.DisposeAsync();
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task SaveWaitingForPathGateObservesDisposeAndDoesNotReloadAfterDisposal()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "dispose-waiting.pdf");
        CreateTestPdf(path);
        var entered = NewSignal();
        var release = NewSignal();
        Task heldGate = PdfSaveCoordinator.RunExclusiveAsync(path, async () =>
        {
            entered.SetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var service = new PdfService();
        Task save = service.SaveAnnotationsToPdfAsync(path, new Dictionary<int, PageAnnotation>());
        Task dispose = service.DisposeAsync().AsTask();
        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult(true);
        await heldGate;

        Assert.That(async () => await save, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(
            async () => await service.SaveAnnotationsToPdfAsync(path, new Dictionary<int, PageAnnotation>()),
            Throws.TypeOf<ObjectDisposedException>());
        await service.DisposeAsync();
        Directory.Delete(root, true);
    }

    [Test]
    public async Task ActiveSaveAndDisposeJoinWithoutNativeResourceLeak()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "dispose-active.pdf");
        CreateTestPdf(path);
        var service = new PdfService();
        Task save = service.SaveAnnotationsToPdfAsync(
            path,
            new Dictionary<int, PageAnnotation>
            {
                [0] = new PageAnnotation
                {
                    Texts = new List<TextAnnotation> { new() { Text = "dispose-race", X = 36, Y = 36 } }
                }
            });
        Task dispose = service.DisposeAsync().AsTask();

        try
        {
            await save;
        }
        catch (ObjectDisposedException)
        {
            // Dispose may win before the save acquires the document lock;
            // the state-machine contract makes that outcome recoverable and
            // prevents a post-dispose native reload.
        }

        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        await service.DisposeAsync();
        Assert.That(
            async () => await service.SaveAnnotationsToPdfAsync(path, new Dictionary<int, PageAnnotation>()),
            Throws.TypeOf<ObjectDisposedException>());
        Directory.Delete(root, true);
    }

    [Test]
    public async Task StructuralWriteJoinsTheSamePathGateAsAnnotationSave()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "structural-gate.pdf");
        CreateTestPdf(path);
        var entered = NewSignal();
        var release = NewSignal();
        Task heldGate = PdfSaveCoordinator.RunExclusiveAsync(path, async () =>
        {
            entered.SetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await using var service = new PdfService();
        Task structural = service.InsertPageAsync(path, 1, PageInsertTemplate.Blank);
        release.SetResult(true);
        await Task.WhenAll(heldGate, structural).WaitAsync(TimeSpan.FromSeconds(2));
        using (var afterRelease = PdfReader.Open(path, PdfDocumentOpenMode.ReadOnly))
            Assert.That(afterRelease.PageCount, Is.EqualTo(2));
        Directory.Delete(root, true);
    }

    [Test]
    public async Task StructuralWriteQueuedBeforeDisposeFailsBeforeReloadingNativeDocument()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "structural-dispose.pdf");
        CreateTestPdf(path);
        var entered = NewSignal();
        var release = NewSignal();
        Task heldGate = PdfSaveCoordinator.RunExclusiveAsync(path, async () =>
        {
            entered.SetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await using var service = new PdfService();
        Task structural = service.InsertPageAsync(path, 1, PageInsertTemplate.Blank);
        Task dispose = service.DisposeAsync().AsTask();
        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult(true);
        await heldGate;

        Assert.That(async () => await structural, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(
            async () => await service.InsertPageAsync(path, 1, PageInsertTemplate.Blank),
            Throws.TypeOf<ObjectDisposedException>());
        Directory.Delete(root, true);
    }

    [Test]
    public async Task PublicLoadJoinsTheSamePathLeaseAsStructuralWrites()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "load-gate.pdf");
        CreateTestPdf(path);
        var entered = NewSignal();
        var release = NewSignal();
        Task heldGate = PdfSaveCoordinator.RunExclusiveAsync(path, async () =>
        {
            entered.SetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await using var service = new PdfService();
        Task load = service.LoadPdfAsync(path);
        release.SetResult(true);
        await Task.WhenAll(heldGate, load).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(service.PageCount, Is.EqualTo(1));
        Directory.Delete(root, true);
    }

    [Test]
    public async Task BlankPdfCreationJoinsTheSamePathLeaseAsOtherWrites()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "blank-gate.pdf");
        var entered = NewSignal();
        var release = NewSignal();
        Task heldGate = PdfSaveCoordinator.RunExclusiveAsync(path, async () =>
        {
            entered.SetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task create = PdfService.CreateBlankPdfAsync(path);
        release.SetResult(true);
        await Task.WhenAll(heldGate, create).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(File.Exists(path), Is.True);
        Directory.Delete(root, true);
    }

    [Test]
    public async Task InsertPdfPagesWaitsForSourceReadLeaseAndThenCompletes()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string targetPath = Path.Combine(root, "target.pdf");
        string sourcePath = Path.Combine(root, "source.pdf");
        CreateTestPdf(targetPath);
        CreateTestPdf(sourcePath);
        var entered = NewSignal();
        var release = NewSignal();
        Task heldSource = PdfSaveCoordinator.RunExclusiveAsync(sourcePath, async () =>
        {
            entered.SetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await using var service = new PdfService();
        Task import = service.InsertPdfPagesAsync(targetPath, sourcePath, 1, 0, 0);
        release.SetResult(true);
        await Task.WhenAll(heldSource, import).WaitAsync(TimeSpan.FromSeconds(2));

        using var result = PdfReader.Open(targetPath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(result.PageCount, Is.EqualTo(2));
        Directory.Delete(root, true);
    }

    [Test]
    public async Task InsertPdfPagesCrossedPairsSerializeWithoutDeadlockAndRejectSamePath()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string firstPath = Path.Combine(root, "first.pdf");
        string secondPath = Path.Combine(root, "second.pdf");
        CreateTestPdf(firstPath);
        CreateTestPdf(secondPath);

        await using var firstService = new PdfService();
        await using var secondService = new PdfService();
        Task first = firstService.InsertPdfPagesAsync(firstPath, secondPath, 1, 0, 0);
        Task second = secondService.InsertPdfPagesAsync(secondPath, firstPath, 1, 0, 0);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(
            async () => await firstService.InsertPdfPagesAsync(firstPath, firstPath, 0, 0, 0),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(PdfSaveCoordinator.ActiveGateCount, Is.Zero);
        Directory.Delete(root, true);
    }

    [Test]
    public async Task FailedNativeStreamReleaseLeavesPdfServiceRetryable()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenNotesWave2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "dispose-retry.pdf");
        CreateTestPdf(path);
        await using var service = new PdfService();
        await service.LoadPdfAsync(path);

        var backingField = typeof(PdfService).GetField(
            "_pdfBackingStream",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(backingField, Is.Not.Null);
        (backingField.GetValue(service) as Stream)?.Dispose();
        backingField.SetValue(service, new ThrowingDisposeStream());

        Assert.That(
            async () => await service.DisposeAsync().AsTask(),
            Throws.TypeOf<InvalidOperationException>());

        // The first failure did not poison the service's disposal state; a
        // second attempt can finish after the bad owner has been cleared.
        await service.DisposeAsync();
        Assert.That(
            async () => await service.SaveAnnotationsToPdfAsync(path, new Dictionary<int, PageAnnotation>()),
            Throws.TypeOf<ObjectDisposedException>());
        Directory.Delete(root, true);
    }

    [Test]
    public void SaveAndAutosaveSourceRequiresSharedGenerationAwareInFlightGate()
    {
        string editorSource = ReadProjectFile("Pages", "EditorPage.xaml.cs");
        string pdfSource = ReadProjectFile("Services", "PdfService.cs");

        Assert.Multiple(() =>
        {
            Assert.That(editorSource, Does.Contain("_autoSaveInFlight"));
            Assert.That(editorSource, Does.Contain("_dirtyGeneration"));
            Assert.That(editorSource, Does.Contain("DocumentSaveCoordinator"));
            Assert.That(editorSource, Does.Contain("SaveUntilCleanAsync"));
            Assert.That(editorSource, Does.Contain("PrepareForCloseAsync"));
            Assert.That(editorSource, Does.Contain("SaveAnnotationsToPdfAsync(_currentPdfPath, annotations)"));
            Assert.That(pdfSource, Does.Contain("PdfSaveCoordinator.RunExclusiveAsync"));
            Assert.That(pdfSource, Does.Contain("PdfAtomicFile.Replace(tempPath, filePath)"));
        });
    }

    [Test]
    public void HomePagePdfExportUsesAtomicTargetWriteContract()
    {
        string homeUtilitiesSource = ReadProjectFile("Pages", "HomePage.Utilities.cs");

        Assert.Multiple(() =>
        {
            Assert.That(homeUtilitiesSource, Does.Contain("PdfAtomicFile.CopyFile"));
            Assert.That(homeUtilitiesSource, Does.Not.Contain("File.Copy(tile.Path"));
        });
    }

    private static void CreateTestPdf(string filePath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = 612;
        page.Height = 792;
        document.Save(filePath);
    }

    private static string ReadProjectFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()), Encoding.UTF8);
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ThrowingDisposeStream : MemoryStream
    {
        private int _failed;

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _failed, 1) == 0)
                throw new InvalidOperationException("expected release failure");
            base.Dispose(disposing);
        }
    }
}
