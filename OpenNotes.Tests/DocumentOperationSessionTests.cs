using System;
using System.Threading;
using System.Threading.Tasks;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
public sealed class DocumentOperationSessionTests
{
    [Test]
    public async Task VersionHistoryAwaitThenReloadSilentlySkipsMutation()
    {
        var session = new DocumentOperationSession();
        var oldModel = new object();
        session.Begin(17, @"C:\Docs\history.pdf", oldModel);
        using DocumentOperationLease lease = session.Capture(
            17,
            @"c:\docs\.\history.pdf",
            oldModel);
        var resume = NewSignal();
        int mutations = 0;

        Task<bool> continuation = ResumeAfterAwaitAsync(
            session,
            lease,
            17,
            @"C:\Docs\history.pdf",
            oldModel,
            resume.Task,
            () => Interlocked.Increment(ref mutations));

        session.Begin(18, @"C:\Docs\replacement.pdf", new object());
        resume.SetResult(true);

        Assert.That(await continuation, Is.False);
        Assert.That(mutations, Is.EqualTo(0));
    }

    [Test]
    public async Task SidebarPageContextAwaitThenReloadSilentlySkipsMutation()
    {
        var session = new DocumentOperationSession();
        var page = new object();
        session.Begin(3, @"C:\Docs\sidebar.pdf", page);
        using DocumentOperationLease lease = session.Capture(3, @"C:\Docs\sidebar.pdf", page);
        var resume = NewSignal();
        int mutations = 0;

        Task<bool> continuation = ResumeAfterAwaitAsync(
            session,
            lease,
            3,
            @"C:\Docs\sidebar.pdf",
            page,
            resume.Task,
            () => Interlocked.Increment(ref mutations));

        session.Begin(4, @"C:\Docs\new-sidebar.pdf", new object());
        resume.SetResult(true);

        Assert.That(await continuation, Is.False);
        Assert.That(mutations, Is.EqualTo(0));
    }

    [Test]
    public async Task AsyncUndoRedoCrossingReloadDoesNotMoveStacks()
    {
        var session = new DocumentOperationSession();
        var action = new object();
        session.Begin(9, @"C:\Docs\undo.pdf", action);
        using DocumentOperationLease lease = session.Capture(9, @"C:\Docs\undo.pdf", action);
        var resume = NewSignal();
        int stackTransitions = 0;

        Task<bool> continuation = ResumeAfterAwaitAsync(
            session,
            lease,
            9,
            @"C:\Docs\undo.pdf",
            action,
            resume.Task,
            () => Interlocked.Increment(ref stackTransitions));

        session.Cancel();
        session.Begin(10, @"C:\Docs\redo.pdf", new object());
        resume.SetResult(true);

        Assert.That(await continuation, Is.False);
        Assert.That(stackTransitions, Is.EqualTo(0));
    }

    [Test]
    public async Task SameSessionContinuationCommitsExactlyOnce()
    {
        var session = new DocumentOperationSession();
        var model = new object();
        const string path = @"C:\Docs\live.pdf";
        session.Begin(22, path, model);
        using DocumentOperationLease lease = session.Capture(22, path, model);
        var resume = NewSignal();
        int mutations = 0;

        Task<bool> continuation = ResumeAfterAwaitAsync(
            session,
            lease,
            22,
            @"c:\docs\live.pdf",
            model,
            resume.Task,
            () => Interlocked.Increment(ref mutations));

        resume.SetResult(true);

        Assert.That(await continuation, Is.True);
        Assert.That(mutations, Is.EqualTo(1));
    }

    [Test]
    public void LeaseRequiresMatchingModelIdentityAndCancellation()
    {
        var session = new DocumentOperationSession();
        var model = new object();
        const string path = @"C:\Docs\identity.pdf";
        session.Begin(31, path, model);
        using DocumentOperationLease lease = session.Capture(31, path, model);

        Assert.Multiple(() =>
        {
            Assert.That(session.Validate(lease, 31, @"c:\docs\.\identity.pdf", model), Is.True);
            Assert.That(session.Validate(lease, 31, path, new object()), Is.False);
            Assert.That(session.Validate(lease, 30, path, model), Is.False);
        });

        session.Cancel();
        Assert.That(session.Validate(lease, 31, path, model), Is.False);
    }

    private static async Task<bool> ResumeAfterAwaitAsync(
        DocumentOperationSession session,
        DocumentOperationLease lease,
        int sessionId,
        string path,
        object model,
        Task signal,
        Action mutate)
    {
        await signal.ConfigureAwait(false);
        if (!session.Validate(lease, sessionId, path, model))
            return false;

        mutate();
        return true;
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
