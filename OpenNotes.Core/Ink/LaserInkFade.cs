using System;

namespace Caelum.Ink;

/// <summary>
/// UI-free laser-pointer lifecycle math — the fade curve the WPF reference
/// applies with a DoubleAnimation, expressed as a pure function so both
/// heads share identical timing and the unit tests can assert the curve.
/// A completed laser stroke holds at opacity 1 for
/// <see cref="HoldSeconds"/>, then fades linearly to 0 over
/// <see cref="FadeSeconds"/>, then is removed.
/// </summary>
public static class LaserInkFade
{
    /// <summary>Seconds the stroke stays fully visible after pointer-up.</summary>
    public const double HoldSeconds = 0.15;

    /// <summary>Seconds of the linear fade after the hold.</summary>
    public const double FadeSeconds = 0.9;

    /// <summary>Hard cap on simultaneous laser visuals; oldest drop at once.</summary>
    public const int MaxLivePolylines = 60;

    /// <summary>Laser stroke colour — red, matching the WPF LaserColor.</summary>
    public const byte ColorR = 0xFF;
    public const byte ColorG = 0x3B;
    public const byte ColorB = 0x30;

    /// <summary>Laser polyline thickness in page DIPs.</summary>
    public const double StrokeThickness = 3.0;

    /// <summary>
    /// Opacity for a completed stroke <paramref name="elapsedSeconds"/> after
    /// pointer-up. When <paramref name="animate"/> is false (reduced motion)
    /// the stroke snaps to 0 immediately, mirroring WPF's zero-duration
    /// animation path.
    /// </summary>
    public static double GetOpacity(double elapsedSeconds, bool animate)
    {
        if (!animate)
            return 0.0;
        if (elapsedSeconds <= HoldSeconds)
            return 1.0;
        double fade = (elapsedSeconds - HoldSeconds) / FadeSeconds;
        return Math.Clamp(1.0 - fade, 0.0, 1.0);
    }

    /// <summary>True once the visual should be removed from the canvas.</summary>
    public static bool IsExpired(double elapsedSeconds, bool animate)
        => !animate || elapsedSeconds >= HoldSeconds + FadeSeconds;
}
