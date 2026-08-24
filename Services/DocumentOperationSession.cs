using System;
using System.IO;
using System.Threading;

namespace Caelum.Services;

/// <summary>
/// Owns the lifetime of asynchronous work associated with one loaded editor
/// document.  A lease is deliberately smaller than an editor/page object: it
/// records the load session, normalized path, optional live model identity and
/// a token that is cancelled when the document or host changes.
/// </summary>
public sealed class DocumentOperationSession : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource _sessionCancellation = new();
    private int _sessionId;
    private string _normalizedPath = string.Empty;
    private object _modelIdentity;
    private bool _active;
    private bool _disposed;

    /// <summary>
    /// Starts the lease boundary for a newly loading document. Existing leases
    /// are cancelled before the new identity becomes visible.
    /// </summary>
    public void Begin(int sessionId, string path, object modelIdentity = null)
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            ThrowIfDisposed();
            previous = _sessionCancellation;
            _sessionCancellation = new CancellationTokenSource();
            _sessionId = sessionId;
            _normalizedPath = NormalizePath(path);
            _modelIdentity = modelIdentity;
            _active = true;
        }

        CancelAndDispose(previous);
    }

    /// <summary>
    /// Captures the current document lease. The requested session/path are
    /// retained in the lease even if the caller is racing a load transition;
    /// validation below is the authority at each continuation boundary.
    /// </summary>
    public DocumentOperationLease Capture(
        int sessionId,
        string path,
        object modelIdentity = null,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            CancellationToken sessionToken = _sessionCancellation.Token;
            CancellationTokenSource linked = null;
            CancellationToken token = sessionToken;
            if (cancellationToken.CanBeCanceled)
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(sessionToken, cancellationToken);
                token = linked.Token;
            }

            return new DocumentOperationLease(
                sessionId,
                NormalizePath(path),
                modelIdentity,
                token,
                linked);
        }
    }

    /// <summary>
    /// Shared validation boundary used immediately after awaits and before a
    /// continuation mutates UI, document model, undo/redo or dirty state.
    /// </summary>
    public bool Validate(
        DocumentOperationLease lease,
        int sessionId,
        string path,
        object modelIdentity = null)
    {
        if (lease == null || lease.IsDisposed || lease.Token.IsCancellationRequested)
            return false;

        string normalizedPath = NormalizePath(path);
        lock (_gate)
        {
            if (_disposed || !_active || lease.IsDisposed || lease.Token.IsCancellationRequested)
                return false;

            if (sessionId != _sessionId || lease.SessionId != sessionId)
                return false;

            if (!string.Equals(normalizedPath, _normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(lease.NormalizedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                return false;

            if (lease.ModelIdentity != null)
            {
                object currentIdentity = modelIdentity ?? _modelIdentity;
                if (!ReferenceEquals(lease.ModelIdentity, currentIdentity))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Cancels every lease captured for the current document. A following
    /// <see cref="Begin"/> creates a fresh token; without Begin, the editor is
    /// intentionally inactive and old continuations remain invalid.
    /// </summary>
    public void Cancel()
    {
        CancellationTokenSource current;
        lock (_gate)
        {
            if (_disposed)
                return;

            _active = false;
            current = _sessionCancellation;
        }

        CancelAndDispose(current);
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            string fullPath = Path.GetFullPath(path);
            return Path.TrimEndingDirectorySeparator(fullPath);
        }
        catch (ArgumentException)
        {
            return path.Trim();
        }
        catch (NotSupportedException)
        {
            return path.Trim();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource current;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _active = false;
            current = _sessionCancellation;
        }

        CancelAndDispose(current);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DocumentOperationSession));
    }

    private static void CancelAndDispose(CancellationTokenSource source)
    {
        if (source == null)
            return;

        try
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Another lifecycle boundary may have won the race and
                // already disposed this source. It is already cancelled for
                // every lease that captured it, so the boundary is complete.
            }
        }
        finally
        {
            try
            {
                source.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Idempotent release is required for unload/deactivation and
                // a concurrent Begin/Cancel pair.
            }
        }
    }
}

/// <summary>One immutable capture of a document operation identity.</summary>
public sealed class DocumentOperationLease : IDisposable
{
    private readonly CancellationTokenSource _linkedCancellation;
    private int _disposed;

    internal DocumentOperationLease(
        int sessionId,
        string normalizedPath,
        object modelIdentity,
        CancellationToken token,
        CancellationTokenSource linkedCancellation)
    {
        SessionId = sessionId;
        NormalizedPath = normalizedPath ?? string.Empty;
        ModelIdentity = modelIdentity;
        Token = token;
        _linkedCancellation = linkedCancellation;
    }

    public int SessionId { get; }
    public string NormalizedPath { get; }
    public object ModelIdentity { get; }
    public CancellationToken Token { get; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _linkedCancellation?.Dispose();
    }
}
