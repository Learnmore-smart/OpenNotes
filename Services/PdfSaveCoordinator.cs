using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Caelum.Services;

/// <summary>
/// Coordinates PDF writes across all PdfService instances in this process.
/// The key is the normalized full path, so unrelated documents do not share a
/// global write lock while concurrent callers for one document are serialized.
/// </summary>
public static class PdfSaveCoordinator
{
    private sealed class GateEntry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }

    private static readonly object GateMapLock = new();
    private static readonly Dictionary<string, GateEntry> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Exposed to deterministic tests only; an idle coordinator has no map
    /// entries. The semaphores themselves are intentionally not disposed when
    /// an entry is removed because a caller may still hold a just-released
    /// reference between the lease and map locks.
    /// </summary>
    internal static int ActiveGateCount
    {
        get
        {
            lock (GateMapLock)
                return Gates.Count;
        }
    }

    /// <summary>Deterministic test diagnostic for callers queued on a gate.</summary>
    internal static int ActiveLeaseCount
    {
        get
        {
            lock (GateMapLock)
            {
                int count = 0;
                foreach (var entry in Gates.Values)
                    count += entry.Users;
                return count;
            }
        }
    }

    /// <summary>
    /// Runs one complete PDF save under the gate for <paramref name="path"/>.
    /// Exceptions from <paramref name="save"/> are propagated after the gate
    /// is released, allowing a later save to recover normally.
    /// </summary>
    public static Task RunExclusiveAsync(string path, Func<Task> save)
        => RunExclusiveAsync(path, save, CancellationToken.None);

    /// <summary>
    /// Runs one operation while holding every distinct path lease in a
    /// deterministic normalized order.  This is used by operations such as
    /// PDF page import which read a source and replace a target.  Acquiring
    /// multiple path semaphores in sorted order prevents A&lt;-B and B&lt;-A
    /// requests from deadlocking, while still allowing unrelated paths to run
    /// in parallel.
    /// </summary>
    public static Task RunExclusiveAsync(IReadOnlyCollection<string> paths, Func<Task> save)
        => RunExclusiveAsync(paths, save, CancellationToken.None);

    public static Task RunExclusiveAsync(
        IReadOnlyCollection<string> paths,
        Func<Task> save,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
            throw new ArgumentException("At least one PDF path is required.", nameof(paths));
        ArgumentNullException.ThrowIfNull(save);

        string[] normalizedPaths = paths
            .Select(path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("A PDF path is required.", nameof(paths));
                return NormalizePath(path);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return RunExclusiveCoreAsync(normalizedPaths, save, cancellationToken);
    }

    /// <summary>
    /// Runs one save with cancellation while waiting for the path gate. A
    /// cancelled waiter is removed from the user count and never enters the
    /// delegate; an already-active delegate is never cancelled by this token.
    /// </summary>
    public static Task RunExclusiveAsync(
        string path,
        Func<Task> save,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A PDF path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(save);

        string normalizedPath = NormalizePath(path);
        return RunExclusiveCoreAsync(new[] { normalizedPath }, save, cancellationToken);
    }

    internal static string NormalizePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (fullPath.Length > 1)
        {
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.Length == 2 && fullPath[1] == Path.VolumeSeparatorChar)
                fullPath += Path.DirectorySeparatorChar;
        }

        return fullPath;
    }

    private static async Task RunExclusiveCoreAsync(
        IReadOnlyList<string> normalizedPaths,
        Func<Task> save,
        CancellationToken cancellationToken)
    {
        GateEntry[] entries = Acquire(normalizedPaths);
        int acquiredCount = 0;
        try
        {
            for (int i = 0; i < entries.Length; i++)
            {
                await entries[i].Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquiredCount++;
            }

            await save().ConfigureAwait(false);
        }
        finally
        {
            for (int i = acquiredCount - 1; i >= 0; i--)
                entries[i].Semaphore.Release();

            Release(normalizedPaths, entries);
        }
    }

    private static GateEntry[] Acquire(IReadOnlyList<string> normalizedPaths)
    {
        lock (GateMapLock)
        {
            var entries = new GateEntry[normalizedPaths.Count];
            for (int i = 0; i < normalizedPaths.Count; i++)
            {
                string normalizedPath = normalizedPaths[i];
                if (!Gates.TryGetValue(normalizedPath, out var entry))
                {
                    entry = new GateEntry();
                    Gates.Add(normalizedPath, entry);
                }

                entry.Users++;
                entries[i] = entry;
            }

            return entries;
        }
    }

    private static void Release(IReadOnlyList<string> normalizedPaths, IReadOnlyList<GateEntry> entries)
    {
        lock (GateMapLock)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                GateEntry entry = entries[i];
                if (entry.Users > 0)
                    entry.Users--;

                string normalizedPath = normalizedPaths[i];
                if (entry.Users == 0
                    && Gates.TryGetValue(normalizedPath, out var current)
                    && ReferenceEquals(current, entry))
                {
                    Gates.Remove(normalizedPath);
                }
            }
        }
    }
}
