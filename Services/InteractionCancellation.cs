using System;
using System.Collections.Generic;

namespace Caelum.Services;

/// <summary>
/// Common boundary for a live pointer/stylus gesture.  Implementations must
/// restore their start snapshot and release capture without emitting a
/// completion event, undo action, or dirty notification.
/// </summary>
public interface IInteractionCancellation
{
    bool HasActiveInteraction { get; }

    void CancelInteraction(string reason = null);
}

/// <summary>
/// Small, deterministic fan-out used by editor lifecycle boundaries.  Owners
/// are copied before iteration because cancellation can detach a page or
/// unregister an interaction while the sweep is running.
/// </summary>
public static class InteractionCancellation
{
    public static void CancelAll(
        IEnumerable<IInteractionCancellation> owners,
        string reason = null)
    {
        if (owners == null)
            return;

        foreach (var owner in new List<IInteractionCancellation>(owners))
            owner?.CancelInteraction(reason);
    }
}
