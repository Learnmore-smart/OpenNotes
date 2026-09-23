namespace Caelum.Services;

/// <summary>
/// Tracks the lifetime of an editor's native-resource release independently
/// from the UI workflow timeout.  A timed-out caller may stop waiting, but it
/// cannot make the document interactive again while the underlying release is
/// still running (or after a partial release failed).
/// </summary>
public sealed class DocumentReleaseState
{
    private enum Status
    {
        Active,
        Releasing,
        Failed,
        Released
    }

    private readonly object _gate = new();
    private Status _status = Status.Active;
    private bool _cleanupStarted;
    private bool _postCleanupFailure;

    public bool IsReleaseInFlight
    {
        get
        {
            lock (_gate)
                return _status == Status.Releasing;
        }
    }

    public bool HasFailed
    {
        get
        {
            lock (_gate)
                return _status == Status.Failed;
        }
    }

    public bool IsReleased
    {
        get
        {
            lock (_gate)
                return _status == Status.Released;
        }
    }

    /// <summary>
    /// True once this lifetime has crossed the point where event detachment or
    /// native disposal may have changed ownership.  This flag survives a
    /// failed retry so a later prepare failure cannot reopen a partial release.
    /// </summary>
    public bool HasPostCleanupFailure
    {
        get
        {
            lock (_gate)
                return _postCleanupFailure;
        }
    }

    /// <summary>
    /// Only an editor that has never entered release, or whose previous
    /// release settled with a retryable failure, may be resumed.  Releasing
    /// and released states intentionally remain non-interactive.
    /// </summary>
    public bool CanResumeInteraction
    {
        get
        {
            lock (_gate)
                return _status == Status.Active;
        }
    }

    /// <summary>
    /// Starts one release attempt.  A second close caller receives false and
    /// must join the already-running task rather than starting another native
    /// disposal.
    /// </summary>
    public bool TryBeginRelease()
    {
        lock (_gate)
        {
            if (_status == Status.Releasing || _status == Status.Released)
                return false;

            _status = Status.Releasing;
            return true;
        }
    }

    /// <summary>
    /// Records the irreversible boundary for the current release attempt.
    /// Call immediately before detaching UI/native owners.
    /// </summary>
    public void MarkCleanupStarted()
    {
        lock (_gate)
        {
            if (_status == Status.Releasing)
                _cleanupStarted = true;
        }
    }

    public void MarkSucceeded()
    {
        lock (_gate)
            _status = Status.Released;
    }

    public void MarkFailed()
    {
        lock (_gate)
        {
            if (_status != Status.Released)
            {
                if (_cleanupStarted)
                    _postCleanupFailure = true;
                _status = Status.Failed;
            }
        }
    }

    /// <summary>
    /// Used only when close preparation never started native cleanup (for
    /// example a save failure).  This is an explicit safe recovery point; a
    /// failure after cleanup has begun remains Failed until a retry succeeds.
    /// </summary>
    public void ResetAfterPreReleaseFailure()
    {
        lock (_gate)
        {
            if (_status == Status.Releasing && !_cleanupStarted && !_postCleanupFailure)
                _status = Status.Active;
            else if (_status == Status.Releasing && (_cleanupStarted || _postCleanupFailure))
                _status = Status.Failed;
        }
    }
}
