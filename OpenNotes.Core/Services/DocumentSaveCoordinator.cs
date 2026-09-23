using System;
using System.Threading;
using System.Threading.Tasks;

namespace Caelum.Services;

/// <summary>
/// WPF-independent state machine for one editor's manual/autosave lifecycle.
/// It is deliberately separate from <see cref="PdfSaveCoordinator"/>: this
/// class coalesces callers and tracks document generations, while the PDF
/// coordinator serializes the actual file replacement across service instances.
/// </summary>
public readonly record struct DocumentSaveResult(
    bool Attempted,
    bool Succeeded,
    bool GenerationIsCurrent,
    long SavedGeneration)
{
    public static DocumentSaveResult NotNeeded(long generation) =>
        new(false, true, true, generation);
}

public sealed class DocumentSaveCoordinator
{
    private readonly object _gate = new();
    private Task<DocumentSaveResult> _inFlight;
    private long _dirtyGeneration;
    private bool _isDirty;
    private bool _closeRequested;
    private bool _closeCompleted;

    public bool IsDirty
    {
        get
        {
            lock (_gate)
                return _isDirty;
        }
    }

    public long DirtyGeneration
    {
        get
        {
            lock (_gate)
                return _dirtyGeneration;
        }
    }

    public bool CloseRequested
    {
        get
        {
            lock (_gate)
                return _closeRequested;
        }
    }

    /// <summary>
    /// Records a normal edit.  A notification which races final close is still
    /// retained as a dirty generation: the caller may have already changed its
    /// model before it could report that change.  The false return only tells
    /// the caller that the edit admission was closing; it must not discard the
    /// generation or the save loop would release stale data.
    /// </summary>
    public bool MarkDirty() => RecordChange(leavesDocumentDirty: true);

    /// <summary>
    /// Records an undo/redo state transition. A clean transition that happens
    /// while an older save is active still requires one latest-generation write.
    /// </summary>
    public bool RecordChange(bool leavesDocumentDirty)
    {
        lock (_gate)
        {
            if (_closeCompleted)
                return false;

            _dirtyGeneration++;
            _isDirty = leavesDocumentDirty || _inFlight != null || _closeRequested;
            return !_closeRequested;
        }
    }

    /// <summary>
    /// Runs one persistence callback for the current generation. Calls made
    /// while that callback is active receive the exact same task, result and
    /// exception; no second snapshot is collected or written concurrently.
    /// </summary>
    public Task<DocumentSaveResult> SaveAsync(Func<long, Task> persistAsync)
    {
        ArgumentNullException.ThrowIfNull(persistAsync);

        lock (_gate)
        {
            if (_inFlight != null)
                return _inFlight;

            if (!_isDirty)
                return Task.FromResult(DocumentSaveResult.NotNeeded(_dirtyGeneration));

            long generation = _dirtyGeneration;
            var completion = new TaskCompletionSource<DocumentSaveResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<DocumentSaveResult> task = completion.Task;
            _inFlight = task;

            // The task reference is installed before invoking user code so a
            // synchronous callback/re-entry cannot create a second save.
            _ = RunSaveAsync(generation, persistAsync, completion, task);
            return task;
        }
    }

    /// <summary>
    /// Joins any active save and keeps retrying until the newest generation is
    /// persisted. In final-close mode new edits are rejected atomically with
    /// the final clean check, so resource release cannot race a late mutation.
    /// </summary>
    public async Task<DocumentSaveResult> SaveUntilCleanAsync(
        Func<long, Task> persistAsync,
        bool finalClose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persistAsync);

        if (finalClose)
        {
            lock (_gate)
                _closeRequested = true;
        }

        try
        {
            DocumentSaveResult last = DocumentSaveResult.NotNeeded(DirtyGeneration);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A successful persistence callback clears _isDirty before
                // its completion task is published/observed. Close and
                // navigation must still join that task before declaring the
                // document safe to release.
                Task<DocumentSaveResult> active = GetInFlight();
                if (active != null)
                {
                    last = await active.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (!IsDirty)
                {
                    if (!finalClose)
                        return last;

                    lock (_gate)
                    {
                        // Complete close under the same lock as the clean
                        // check.  A late RecordChange can therefore either be
                        // included in another iteration or be rejected after
                        // the final snapshot is known to be current.
                        if (_inFlight == null && !_isDirty)
                        {
                            _closeCompleted = true;
                            return last;
                        }
                    }

                    continue;
                }

                last = await SaveAsync(persistAsync)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (last.GenerationIsCurrent && !IsDirty)
                {
                    if (!finalClose)
                        return last;

                    lock (_gate)
                    {
                        if (_inFlight == null && !_isDirty)
                        {
                            _closeCompleted = true;
                            return last;
                        }
                    }
                }
            }
        }
        catch
        {
            if (finalClose)
                CancelCloseRequest();
            throw;
        }
    }

    /// <summary>Allows a failed navigation/close attempt to resume editing.</summary>
    public void CancelCloseRequest()
    {
        lock (_gate)
        {
            _closeRequested = false;
            _closeCompleted = false;
        }
    }

    /// <summary>Resets a newly loaded document; active persistence is a bug.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (_inFlight != null)
                throw new InvalidOperationException("Cannot reset while a document save is active.");

            _dirtyGeneration = 0;
            _isDirty = false;
            _closeRequested = false;
            _closeCompleted = false;
        }
    }

    private Task<DocumentSaveResult> GetInFlight()
    {
        lock (_gate)
            return _inFlight;
    }

    private async Task RunSaveAsync(
        long generation,
        Func<long, Task> persistAsync,
        TaskCompletionSource<DocumentSaveResult> completion,
        Task<DocumentSaveResult> task)
    {
        try
        {
            await persistAsync(generation).ConfigureAwait(false);

            DocumentSaveResult result;
            lock (_gate)
            {
                bool current = _dirtyGeneration == generation;
                // A newer edit (including a clean undo while an older save is
                // active) must force one latest-generation retry.
                _isDirty = !current;
                result = new DocumentSaveResult(true, true, current, generation);
            }

            completion.TrySetResult(result);
        }
        catch (Exception ex)
        {
            // Keep _isDirty true so the next timer/manual/close attempt can
            // recover. The original exception remains observable to callers.
            completion.TrySetException(ex);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_inFlight, task))
                    _inFlight = null;
            }
        }
    }
}
