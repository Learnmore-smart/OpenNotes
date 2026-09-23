using System;
using System.Threading;
using System.Threading.Tasks;

namespace Caelum.Services;

/// <summary>
/// Synchronous admission boundary for document mutations.  WPF input and
/// command handlers acquire a short lease before changing the model; close or
/// navigation flips the boundary first, so a queued event cannot mutate a
/// document after the final snapshot has been accepted.
/// </summary>
public sealed class DocumentEditAdmission
{
    private readonly object _gate = new();
    private int _activeEdits;
    private bool _closing;
    private bool _closed;
    private TaskCompletionSource<bool> _quiescence = CompletedSignal();

    public bool IsClosing
    {
        get
        {
            lock (_gate)
                return _closing;
        }
    }

    public bool IsClosed
    {
        get
        {
            lock (_gate)
                return _closed;
        }
    }

    public int ActiveEditCount
    {
        get
        {
            lock (_gate)
                return _activeEdits;
        }
    }

    public bool TryEnter(out IDisposable lease)
    {
        lock (_gate)
        {
            if (_closing || _closed)
            {
                lease = null;
                return false;
            }

            _activeEdits++;
            if (_activeEdits == 1)
                _quiescence = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lease = new EditLease(this);
            return true;
        }
    }

    public IDisposable TryEnter()
    {
        return TryEnter(out var lease) ? lease : null;
    }

    public void BeginClose()
    {
        lock (_gate)
            _closing = true;
    }

    public Task WaitForQuiescenceAsync(CancellationToken cancellationToken = default)
    {
        Task wait;
        lock (_gate)
            wait = _quiescence.Task;
        return wait.WaitAsync(cancellationToken);
    }

    public void CancelClose()
    {
        lock (_gate)
        {
            // A failed resource release must be retryable even when the save
            // loop had already completed its final snapshot and marked this
            // admission closed.
            _closing = false;
            _closed = false;
        }
    }

    public void CompleteClose()
    {
        lock (_gate)
        {
            _closing = true;
            _closed = true;
        }
    }

    private void Exit()
    {
        lock (_gate)
        {
            if (_activeEdits > 0)
                _activeEdits--;
            if (_activeEdits == 0)
                _quiescence.TrySetResult(true);
        }
    }

    private static TaskCompletionSource<bool> CompletedSignal()
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult(true);
        return signal;
    }

    private sealed class EditLease : IDisposable
    {
        private DocumentEditAdmission _owner;

        public EditLease(DocumentEditAdmission owner) => _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Exit();
        }
    }
}
