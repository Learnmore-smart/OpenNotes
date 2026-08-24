using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
public sealed class DocumentSaveCoordinatorTests
{
    [Test]
    public async Task ManualAndAutosaveCallsShareOneInFlightTask()
    {
        var coordinator = new DocumentSaveCoordinator();
        coordinator.MarkDirty();
        var entered = NewSignal();
        var release = NewSignal();
        int saves = 0;

        Task<DocumentSaveResult> first = coordinator.SaveAsync(async _ =>
        {
            Interlocked.Increment(ref saves);
            entered.TrySetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<DocumentSaveResult> second = coordinator.SaveAsync(_ => Task.CompletedTask);

        Assert.That(second, Is.SameAs(first));
        release.SetResult(true);
        await Task.WhenAll(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(coordinator.IsDirty, Is.False);
        });
    }

    [Test]
    public async Task EditDuringSaveLeavesDirtyAndNextAttemptPersistsLatestGeneration()
    {
        var coordinator = new DocumentSaveCoordinator();
        coordinator.MarkDirty();
        var entered = NewSignal();
        var release = NewSignal();
        var persisted = new ConcurrentQueue<long>();

        Task<DocumentSaveResult> first = coordinator.SaveAsync(async generation =>
        {
            persisted.Enqueue(generation);
            entered.SetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.MarkDirty();
        release.SetResult(true);

        DocumentSaveResult stale = await first;
        Assert.Multiple(() =>
        {
            Assert.That(stale.GenerationIsCurrent, Is.False);
            Assert.That(coordinator.IsDirty, Is.True);
        });

        DocumentSaveResult latest = await coordinator.SaveAsync(generation =>
        {
            persisted.Enqueue(generation);
            return Task.CompletedTask;
        });

        Assert.Multiple(() =>
        {
            Assert.That(latest.GenerationIsCurrent, Is.True);
            Assert.That(coordinator.IsDirty, Is.False);
            Assert.That(persisted, Is.EqualTo(new[] { 1L, 2L }));
        });
    }

    [Test]
    public async Task FinalCloseWaitsForLatestSnapshotBeforeReturning()
    {
        var coordinator = new DocumentSaveCoordinator();
        coordinator.MarkDirty();
        var firstEntered = NewSignal();
        var firstRelease = NewSignal();
        var persisted = new ConcurrentQueue<long>();

        Task<DocumentSaveResult> close = coordinator.SaveUntilCleanAsync(async generation =>
        {
            persisted.Enqueue(generation);
            firstEntered.TrySetResult(true);
            await firstRelease.Task;
        }, finalClose: true);

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(coordinator.CloseRequested, Is.True);
        Assert.That(coordinator.MarkDirty(), Is.False, "final close blocks a late edit atomically");
        firstRelease.SetResult(true);
        await close.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.IsDirty, Is.False);
            Assert.That(persisted, Is.EqualTo(new[] { 1L, 2L }));
            Assert.That(coordinator.CloseRequested, Is.True);
        });
    }

    [Test]
    public async Task FinalCloseRetriesEditThatArrivedBeforeCloseRequest()
    {
        var coordinator = new DocumentSaveCoordinator();
        coordinator.MarkDirty();
        var firstEntered = NewSignal();
        var firstRelease = NewSignal();
        var secondEntered = NewSignal();
        var secondRelease = NewSignal();
        var persisted = new ConcurrentQueue<long>();

        async Task Persist(long generation)
        {
            persisted.Enqueue(generation);
            if (generation == 1)
            {
                firstEntered.SetResult(true);
                await firstRelease.Task;
            }
            else
            {
                secondEntered.SetResult(true);
                await secondRelease.Task;
            }
        }

        Task<DocumentSaveResult> first = coordinator.SaveAsync(Persist);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.MarkDirty();

        Task<DocumentSaveResult> close = coordinator.SaveUntilCleanAsync(Persist, finalClose: true);

        firstRelease.SetResult(true);
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(persisted, Is.EqualTo(new[] { 1L, 2L }));
        secondRelease.SetResult(true);
        await Task.WhenAll(first, close);

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.IsDirty, Is.False);
            Assert.That(coordinator.CloseRequested, Is.True);
        });
    }

    [Test]
    public async Task FailureLeavesStateRecoverableAndDoesNotCreateSaveStorm()
    {
        var coordinator = new DocumentSaveCoordinator();
        coordinator.MarkDirty();
        int attempts = 0;
        Task<DocumentSaveResult> failed = coordinator.SaveAsync(_ =>
        {
            Interlocked.Increment(ref attempts);
            return Task.FromException(new IOException("expected persistence failure"));
        });

        Assert.That(async () => await failed, Throws.TypeOf<IOException>());
        Assert.That(coordinator.IsDirty, Is.True);

        DocumentSaveResult recovered = await coordinator.SaveAsync(_ =>
        {
            Interlocked.Increment(ref attempts);
            return Task.CompletedTask;
        });

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Succeeded, Is.True);
            Assert.That(coordinator.IsDirty, Is.False);
            Assert.That(attempts, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task CancelledFinalCloseReleasesCloseRequestAndAllowsRecovery()
    {
        var coordinator = new DocumentSaveCoordinator();
        coordinator.MarkDirty();
        var entered = NewSignal();
        var release = NewSignal();
        using var cancellation = new CancellationTokenSource();

        Task<DocumentSaveResult> close = coordinator.SaveUntilCleanAsync(async _ =>
        {
            entered.SetResult(true);
            await release.Task;
        }, finalClose: true, cancellation.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        Assert.That(async () => await close, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(coordinator.CloseRequested, Is.False);

        release.SetResult(true);
        await coordinator.SaveAsync(_ => Task.CompletedTask);
        Assert.That(coordinator.MarkDirty(), Is.True);
    }

    [Test]
    public async Task FinalClosePersistsARealModelEditThatArrivesDuringTheSaveBarrier()
    {
        var coordinator = new DocumentSaveCoordinator();
        coordinator.MarkDirty();
        var model = new TestDocumentModel("before-close");
        var entered = NewSignal();
        var release = NewSignal();
        var persisted = new ConcurrentQueue<string>();

        Task<DocumentSaveResult> close = coordinator.SaveUntilCleanAsync(async _ =>
        {
            persisted.Enqueue(model.Text);
            entered.TrySetResult(true);
            await release.Task;
        }, finalClose: true);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // The model mutation happens before its dirty notification, as it does
        // for WPF ink/text events.  A rejected notification must not make the
        // already-applied model change disappear at close.
        model.Apply("late-edit");
        Assert.That(coordinator.RecordChange(leavesDocumentDirty: true), Is.False);

        release.SetResult(true);
        await close.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(persisted, Is.EqualTo(new[] { "before-close", "late-edit" }));
    }

    [Test]
    public async Task SaveAsyncJoinsAnActiveSaveEvenWhenCompletionHasAlreadyClearedDirtyState()
    {
        var coordinator = new DocumentSaveCoordinator();
        coordinator.MarkDirty();
        var entered = NewSignal();
        var release = NewSignal();

        Task<DocumentSaveResult> first = coordinator.SaveAsync(async _ =>
        {
            entered.SetResult(true);
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // This is the deterministic completion window: RunSaveAsync has
        // published a clean generation before it has cleared _inFlight.
        // Reflecting the state here keeps the test focused on the public
        // SaveAsync ordering without relying on timing or sleeps.
        var dirtyField = typeof(DocumentSaveCoordinator).GetField(
            "_isDirty",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(dirtyField, Is.Not.Null);
        dirtyField.SetValue(coordinator, false);

        Task<DocumentSaveResult> joined = coordinator.SaveAsync(_ => Task.CompletedTask);
        Assert.That(joined, Is.SameAs(first));

        release.SetResult(true);
        await joined.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public void EditAdmissionBlocksQueuedMutationsUntilAFailedCloseIsCancelled()
    {
        var admission = new DocumentEditAdmission();
        using (admission.TryEnter())
            Assert.That(admission.ActiveEditCount, Is.EqualTo(1));

        admission.BeginClose();
        Assert.Multiple(() =>
        {
            Assert.That(admission.TryEnter(), Is.Null);
            Assert.That(admission.IsClosing, Is.True);
            Assert.That(admission.ActiveEditCount, Is.Zero);
        });

        admission.CancelClose();
        using var retry = admission.TryEnter();
        Assert.That(retry, Is.Not.Null);
    }

    [Test]
    public async Task CompletedNavigationAdmissionCanBeResumedForTheActiveEditor()
    {
        var admission = new DocumentEditAdmission();
        var coordinator = new DocumentSaveCoordinator();

        admission.BeginClose();
        Assert.That(admission.TryEnter(), Is.Null);

        coordinator.MarkDirty();
        await coordinator.SaveUntilCleanAsync(_ => Task.CompletedTask, finalClose: true);
        Assert.That(coordinator.CloseRequested, Is.True);

        // Returning to a frame uses the same production CancelClose path as
        // EditorPage.ResumeDocumentInteraction for both admission state
        // machines.
        admission.CancelClose();
        coordinator.CancelCloseRequest();
        using (var resumedLease = admission.TryEnter())
            Assert.That(resumedLease, Is.Not.Null);
        Assert.That(coordinator.MarkDirty(), Is.True);

        admission.BeginClose();
        admission.CompleteClose();
        Assert.That(admission.TryEnter(), Is.Null);
        admission.CancelClose();
        coordinator.CancelCloseRequest();
        using (var resumedAfterCompletedClose = admission.TryEnter())
            Assert.That(resumedAfterCompletedClose, Is.Not.Null);
        Assert.That(coordinator.MarkDirty(), Is.True);
    }

    [Test]
    public async Task NavigationAdmissionWaitsForAnAlreadyAdmittedMutationToQuiesce()
    {
        var admission = new DocumentEditAdmission();
        Assert.That(admission.TryEnter(out var activeLease), Is.True);

        admission.BeginClose();
        Task quiesced = admission.WaitForQuiescenceAsync();

        activeLease.Dispose();
        await quiesced.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(admission.ActiveEditCount, Is.Zero);
    }

    [Test]
    public void ReleaseTimeoutKeepsInteractionBlockedUntilReleaseFinishesAndAllowsOnlyAJoin()
    {
        var release = new DocumentReleaseState();

        Assert.That(release.TryBeginRelease(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(release.IsReleaseInFlight, Is.True);
            Assert.That(release.CanResumeInteraction, Is.False);
            Assert.That(release.TryBeginRelease(), Is.False,
                "a re-close must join the existing release rather than dispose concurrently");
        });

        release.MarkFailed();
        Assert.Multiple(() =>
        {
            Assert.That(release.HasFailed, Is.True);
            Assert.That(release.CanResumeInteraction, Is.False,
                "ActivateTab/late input cannot reopen a partially released document");
            Assert.That(release.TryBeginRelease(), Is.True,
                "a later explicit close may retry after the failed release task settled");
        });

        release.MarkSucceeded();
        Assert.Multiple(() =>
        {
            Assert.That(release.IsReleased, Is.True);
            Assert.That(release.CanResumeInteraction, Is.False);
            Assert.That(release.TryBeginRelease(), Is.False);
        });
    }

    [Test]
    public void PostCleanupFailureCannotBeResetAfterRetryPrepareFailureOrCancelActivation()
    {
        var release = new DocumentReleaseState();

        Assert.That(release.TryBeginRelease(), Is.True);
        // A native cleanup/dispose failure is terminal for this release
        // attempt.  A later prepare failure must not turn a partially
        // detached editor back into an Active editor.
        release.MarkCleanupStarted();
        release.MarkFailed();
        Assert.That(release.TryBeginRelease(), Is.True);

        release.ResetAfterPreReleaseFailure();

        Assert.Multiple(() =>
        {
            Assert.That(release.HasFailed, Is.True);
            Assert.That(release.CanResumeInteraction, Is.False);
            Assert.That(release.IsReleased, Is.False);
        });

        // Only a complete retry may terminate the blocked state.
        release.MarkSucceeded();
        Assert.That(release.IsReleased, Is.True);
    }

    [Test]
    public async Task NavigationPrepareSuccessButStaleJournalCancelsPreparationWithoutNavigating()
    {
        bool cancelled = false;
        bool navigated = false;

        bool result = await NavigationCloseCoordinator.TryNavigateBackAsync(
            () => Task.FromResult(true),
            () => false,
            () => cancelled = true,
            () =>
            {
                navigated = true;
                return Task.CompletedTask;
            });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(cancelled, Is.True);
            Assert.That(navigated, Is.False);
        });
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestDocumentModel
    {
        public TestDocumentModel(string text) => Text = text;

        public string Text { get; private set; }

        public void Apply(string text) => Text = text;
    }
}
