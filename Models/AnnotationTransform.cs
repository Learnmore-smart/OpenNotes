using System;
using System.Windows;

namespace Caelum.Models
{
    internal static class AnnotationTransform
    {
        public static Point RotatePoint(Point point, Point center, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;
            return new Point(
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
}
