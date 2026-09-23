using System;
using Caelum.InkGeometry;

namespace Caelum.Models;

/// <summary>
/// UI-free annotation rotation math, moved from the WPF
/// <c>Caelum.Models.AnnotationTransform</c> (now a thin Point adapter).
/// <see cref="NormalizeDegrees"/> semantics are load-bearing: the saved
/// /WNARotation metadata must match the legacy normalization exactly.
/// </summary>
internal static class AnnotationRotation
{
    public static PointD RotatePoint(PointD point, PointD center, double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double dx = point.X - center.X;
        double dy = point.Y - center.Y;
        return new PointD(
            center.X + (dx * cos) - (dy * sin),
            center.Y + (dx * sin) + (dy * cos));
    }

    public static double NormalizeDegrees(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
            return 0;

        degrees %= 360.0;
        if (degrees > 180.0)
            degrees -= 360.0;
        if (degrees <= -180.0)
            degrees += 360.0;
        return degrees;
    }
}
