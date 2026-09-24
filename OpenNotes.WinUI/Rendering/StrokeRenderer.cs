using System.Collections.Generic;
using Caelum.InkGeometry;
using Caelum.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Caelum.Rendering;

/// <summary>
/// Turns <see cref="InkStrokeData"/> into WinUI fill geometry. Each stroke
/// renders as a filled <see cref="Path"/> whose outline is the
/// pressure-variable hull produced by
/// <see cref="StrokeOutline.BuildFillOutline"/> — the same silhouette WPF's
/// <c>System.Windows.Ink</c> renderer produces for the equivalent
/// DrawingAttributes. Highlighter strokes differ only in their RGBA alpha
/// (the caller stores the 140-alpha colour on the stroke), matching WPF
/// where IsHighlighter changes blend mode but the payload is the colour.
/// Dashed shape strokes (<see cref="InkStrokeData.IsDashedShape"/>) render as
/// real dashes: the spine is re-segmented by
/// <see cref="StrokeGeometry.BuildDashedPolyline"/> under the persisted
/// shape-tool pattern and each dash becomes its own figure — the same look
/// WPF produces by storing one stroke per dash (re-dashing a stored dash is
/// idempotent, so WPF-loaded documents keep their exact outlines).
/// </summary>
public static class StrokeRenderer
{
    /// <summary>
    /// Creates a filled <see cref="Path"/> for a stroke. The path is not
    /// parented — callers add it to the ink canvas.
    /// </summary>
    public static Path CreateStrokePath(InkStrokeData stroke)
    {
        var path = new Path
        {
            Fill = new SolidColorBrush(ToColor(stroke)),
            Data = BuildGeometry(stroke),
            IsHitTestVisible = false, // hit-testing lives in StrokeGeometry math, not XAML
        };
        return path;
    }

    /// <summary>
    /// Rebuilds an existing path's geometry in place — the live-stroke fast
    /// path: one Path element per in-flight pointer, updated per move event.
    /// </summary>
    public static void UpdateStrokePath(Path path, InkStrokeData stroke)
    {
        if (path == null)
            return;
        path.Data = BuildGeometry(stroke);
    }

    /// <summary>
    /// The fill geometry for a stroke — a single closed figure over the
    /// outline polygon, or one figure per dash segment for
    /// <see cref="InkStrokeData.IsDashedShape"/> strokes. Empty/degenerate
    /// strokes produce an empty geometry.
    /// </summary>
    public static PathGeometry BuildGeometry(InkStrokeData stroke)
    {
        var geometry = new PathGeometry();
        if (stroke == null)
            return geometry;

        if (stroke.IsDashedShape)
        {
            var dashOutlines = StrokeOutline.BuildDashedFillOutlines(stroke);
            foreach (var outline in dashOutlines)
                AddOutlineFigure(geometry, outline);
            // A degenerate dashed spine (e.g. a lone tap) still draws its dot.
            if (geometry.Figures.Count == 0)
                AddOutlineFigure(geometry, StrokeOutline.BuildFillOutline(
                    stroke.Points, stroke.Size, stroke.IgnorePressure, stroke.FitToCurve));
            return geometry;
        }

        AddOutlineFigure(geometry, StrokeOutline.BuildFillOutline(
            stroke.Points, stroke.Size, stroke.IgnorePressure, stroke.FitToCurve));
        return geometry;
    }

    /// <summary>Adds one closed polygon figure; sub-triangle outlines are skipped.</summary>
    private static void AddOutlineFigure(PathGeometry geometry, IReadOnlyList<PointD> outline)
    {
        if (outline == null || outline.Count < 3)
            return;

        var figure = new PathFigure
        {
            StartPoint = new Point(outline[0].X, outline[0].Y),
            IsClosed = true,
            IsFilled = true,
        };
        var segment = new PolyLineSegment();
        for (int i = 1; i < outline.Count; i++)
            segment.Points.Add(new Point(outline[i].X, outline[i].Y));
        figure.Segments.Add(segment);
        geometry.Figures.Add(figure);
    }

    /// <summary>Stroke's RGBA payload → WinUI colour.</summary>
    public static Color ToColor(InkStrokeData stroke)
        => Color.FromArgb(stroke.A, stroke.R, stroke.G, stroke.B);
}
