using System;
using System.Windows;
using Caelum.InkGeometry;

namespace Caelum.Models
{
    /// <summary>
    /// WPF <see cref="Point"/> adapter over the UI-free math in Core
    /// (<see cref="AnnotationRotation"/>). Signatures are unchanged so the
    /// PdfPageControl call sites and tests keep working.
    /// </summary>
    internal static class AnnotationTransform
    {
        public static Point RotatePoint(Point point, Point center, double degrees)
        {
            var rotated = AnnotationRotation.RotatePoint(
                new PointD(point.X, point.Y),
                new PointD(center.X, center.Y),
                degrees);
            return new Point(rotated.X, rotated.Y);
        }

        public static double NormalizeDegrees(double degrees)
            => AnnotationRotation.NormalizeDegrees(degrees);
    }
}
