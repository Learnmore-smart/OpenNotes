using System;

namespace Caelum.Models;

/// <summary>
/// Pure timing rules for Hidden Ink. Keeping this separate from WPF's timer
/// makes the boundary behavior deterministic and unit-testable.
/// </summary>
public static class HiddenInkRevealState
{
    public const int DefaultRevealDurationMs = 3000;

    public static DateTimeOffset GetRevealUntil(
        DateTimeOffset revealedAt,
        TimeSpan? duration = null)
    {
        var effectiveDuration = duration.GetValueOrDefault(
            TimeSpan.FromMilliseconds(DefaultRevealDurationMs));
        if (effectiveDuration <= TimeSpan.Zero)
            effectiveDuration = TimeSpan.FromMilliseconds(DefaultRevealDurationMs);

        return revealedAt + effectiveDuration;
    }

    public static bool IsRevealed(DateTimeOffset now, DateTimeOffset? revealedUntil)
    {
        return revealedUntil.HasValue && now < revealedUntil.Value;
    }
}
