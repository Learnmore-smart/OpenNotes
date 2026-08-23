using System;
using System.Collections.Generic;
using System.Linq;

namespace Caelum.Services
{
    public readonly record struct PdfRenderProfile(
        int RetainedPagePadding,
        bool PrefetchAdjacentPages,
        double MaxRenderScale,
        long MaxBitmapBytes);

    /// <summary>
    /// Pure display policy for bounding PDF bitmap memory and render work.
    /// These limits never affect annotations or saved-document fidelity.
    /// </summary>
    public static class PdfRenderPolicy
    {
        public const string BatterySaver = "BatterySaver";
        public const string Balanced = "Balanced";
        public const string BestQuality = "BestQuality";

        private const double PixelsPerDipAtBaseRender = 2.0;
        private const int BytesPerPixel = 4;

        public static string NormalizeMode(string value)
        {
            if (string.Equals(value?.Trim(), BatterySaver, StringComparison.OrdinalIgnoreCase))
                return BatterySaver;
            if (string.Equals(value?.Trim(), BestQuality, StringComparison.OrdinalIgnoreCase))
                return BestQuality;
            return Balanced;
        }

        public static PdfRenderProfile GetProfile(string mode)
        {
            return NormalizeMode(mode) switch
            {
                BatterySaver => new PdfRenderProfile(0, false, 1.35, 32L * 1024 * 1024),
                BestQuality => new PdfRenderProfile(1, true, 3.0, 128L * 1024 * 1024),
                _ => new PdfRenderProfile(1, true, 2.0, 64L * 1024 * 1024)
            };
        }

        public static IReadOnlyList<int> GetRetainedPageIndices(
            int firstVisible,
            int lastVisible,
            int pageCount,
            string mode)
        {
            if (pageCount <= 0 || firstVisible < 0 || lastVisible < firstVisible)
                return Array.Empty<int>();

            int padding = GetProfile(mode).RetainedPagePadding;
            int first = Math.Max(0, firstVisible - padding);
            int last = Math.Min(pageCount - 1, lastVisible + padding);
            return Enumerable.Range(first, last - first + 1).ToArray();
        }

        public static double NormalizeRequestedScale(double value)
            => double.IsFinite(value) ? Math.Clamp(value, 0.1, 3.0) : 1.0;

        public static int CalculateRenderDpi(double requestedScale)
        {
            double dpi = 192.0 * NormalizeRequestedScale(requestedScale);
            return Math.Max(1, (int)Math.Round(dpi, MidpointRounding.AwayFromZero));
        }

        public static long EstimateBitmapBytes(double widthDips, double heightDips, double scale)
        {
            if (!double.IsFinite(widthDips)
                || !double.IsFinite(heightDips)
                || widthDips <= 0
                || heightDips <= 0)
            {
                return 0;
            }

            double normalizedScale = NormalizeRequestedScale(scale);
            double bytes = widthDips * PixelsPerDipAtBaseRender * normalizedScale
                * heightDips * PixelsPerDipAtBaseRender * normalizedScale
                * BytesPerPixel;
            return bytes >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(bytes);
        }

        public static double CalculateRenderScale(
            string mode,
            double widthDips,
            double heightDips,
            double requestedScale)
        {
            var profile = GetProfile(mode);
            double requested = Math.Min(
                Math.Max(NormalizeRequestedScale(requestedScale), 0.25),
                profile.MaxRenderScale);
            long baseBytes = EstimateBitmapBytes(widthDips, heightDips, 1.0);
            if (baseBytes <= 0)
                return Math.Min(1.0, requested);

            double budgetScale = Math.Sqrt((double)profile.MaxBitmapBytes / baseBytes);
            return Math.Clamp(
                Math.Min(requested, budgetScale),
                0.25,
                profile.MaxRenderScale);
        }
    }
}
