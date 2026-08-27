using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Caelum.Models;

namespace Caelum.Pages;

/// <summary>
/// Composes the page-owned ordinary ink over the clean Pdfium thumbnail.
/// The PDF service intentionally strips OpenNotes annotations before rendering;
/// this display-only layer restores the current in-memory pen/highlighter view.
/// </summary>
internal static class ThumbnailCompositor
{
    public static BitmapSource Composite(
        BitmapSource baseBitmap,
        IReadOnlyList<StrokeAnnotation> strokes,
        double pageWidthDip,
        double pageHeightDip)
    {
        if (baseBitmap == null || baseBitmap.PixelWidth <= 0 || baseBitmap.PixelHeight <= 0)
            return baseBitmap;

        double widthDip = NormalizePageDimension(pageWidthDip, baseBitmap.PixelWidth, baseBitmap.DpiX);
        double heightDip = NormalizePageDimension(pageHeightDip, baseBitmap.PixelHeight, baseBitmap.DpiY);
        double dpiX = NormalizeDpi(baseBitmap.DpiX);
        double dpiY = NormalizeDpi(baseBitmap.DpiY);

        var visual = new DrawingVisual();
        using (DrawingContext drawingContext = visual.RenderOpen())
        {
            drawingContext.DrawImage(baseBitmap, new Rect(0, 0, widthDip, heightDip));
            DrawStrokes(drawingContext, strokes, widthDip, heightDip);
        }

        var result = new RenderTargetBitmap(
            baseBitmap.PixelWidth,
            baseBitmap.PixelHeight,
            dpiX,
            dpiY,
            PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static void DrawStrokes(
        DrawingContext drawingContext,
        IReadOnlyList<StrokeAnnotation> strokes,
        double pageWidthDip,
        double pageHeightDip)
    {
        if (strokes == null || strokes.Count == 0)
            return;

        foreach (var annotation in strokes)
        {
            if (annotation?.Points == null || annotation.Points.Count == 0)
                continue;

            var points = new StylusPointCollection();
            foreach (var point in annotation.Points)
            {
                if (point == null || point.Length < 2
                    || !double.IsFinite(point[0]) || !double.IsFinite(point[1]))
                    continue;

                points.Add(new StylusPoint(
                    Math.Clamp(point[0], 0, pageWidthDip),
                    Math.Clamp(point[1], 0, pageHeightDip)));
            }

            if (points.Count == 0)
                continue;
            if (points.Count == 1)
                points.Add(new StylusPoint(points[0].X + 0.1, points[0].Y));

            var attributes = new DrawingAttributes
            {
                Color = Color.FromArgb(annotation.A, annotation.R, annotation.G, annotation.B),
                Width = NormalizeStrokeSize(annotation.Size),
                Height = NormalizeStrokeSize(annotation.Size),
                IsHighlighter = annotation.IsHighlighter,
                FitToCurve = annotation.FitToCurve,
                // StrokeAnnotation intentionally stores geometry and visual
                // attributes, not per-point pressure. Keep thumbnail width
                // stable instead of inventing pressure from reconstructed data.
                IgnorePressure = true
            };

            new Stroke(points, attributes).Draw(drawingContext);
        }
    }

    private static double NormalizePageDimension(double value, int pixels, double dpi)
    {
        if (double.IsFinite(value) && value > 0)
            return value;

        double safeDpi = NormalizeDpi(dpi);
        return Math.Max(1.0, pixels * 96.0 / safeDpi);
    }

    private static double NormalizeDpi(double dpi)
        => double.IsFinite(dpi) && dpi > 0 ? dpi : 96.0;

    private static double NormalizeStrokeSize(double size)
        => double.IsFinite(size) && size > 0 ? size : 2.0;
}

/// <summary>
/// Page-local revision/session state for thumbnail render continuations.
/// </summary>
internal sealed class ThumbnailRevisionGate
{
    private readonly Dictionary<int, int> _revisions = new();
    private int _sessionId;

    public void BeginSession(int sessionId)
    {
        _sessionId = sessionId;
        _revisions.Clear();
    }

    public int CaptureRevision(int pageIndex)
        => _revisions.TryGetValue(pageIndex, out int revision) ? revision : 0;

    public int InvalidatePage(int pageIndex)
    {
        int revision = CaptureRevision(pageIndex) + 1;
        _revisions[pageIndex] = revision;
        return revision;
    }

    public bool IsCurrent(int pageIndex, int sessionId, int revision)
    {
        return _sessionId == sessionId && CaptureRevision(pageIndex) == revision;
    }
}
