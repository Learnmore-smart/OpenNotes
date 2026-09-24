using System;
using System.Collections.Generic;
using System.Linq;
using Caelum.InkGeometry;
using Caelum.Models;

namespace Caelum.Ink;

/// <summary>
/// UI-free shape-commit pipeline — the Core half of the WPF page's
/// <c>CommitShape</c>. Given the constrained drag endpoints it produces the
/// final ordered <see cref="InkStrokeData"/> set: arrow shaft+head as two
/// parts, dashed shapes split into baked dash segments, every part stamped
/// with a shared <see cref="ShapeStrokeIdentity"/> (group id, kind, part
/// index, dashed flag) so lasso selection, persistence and undo treat the
/// whole shape as one logical unit.
/// Preview polylines are produced by <see cref="BuildPreviewPolylines"/> —
/// the same outlines the commit bakes, minus identity stamping.
/// </summary>
public static class ShapeStrokeFactory
{
    /// <summary>WPF parity: drags shorter than this (in DIPs) are taps.</summary>
    public const double DragThreshold = 4.0;

    /// <summary>Kinds whose outline is a single open segment (ruler-snappable).</summary>
    public static bool IsOpenKind(InkShapeKind kind) =>
        kind is InkShapeKind.Line or InkShapeKind.Arrow or InkShapeKind.DashedLine;

    /// <summary>
    /// The raw outline segments the drag preview draws: arrow → shaft+head
    /// (two polylines), everything else → one closed/open outline.
    /// <paramref name="dashed"/> does not change preview geometry — the WPF
    /// preview renders the outline with a relative StrokeDashArray instead.
    /// </summary>
    public static List<IReadOnlyList<PointD>> BuildPreviewPolylines(
        InkShapeKind kind, PointD start, PointD end, double strokeSize)
    {
        var segments = new List<IReadOnlyList<PointD>>();
        if (kind == InkShapeKind.Arrow)
        {
            StrokeGeometry.BuildArrowGeometry(start, end, strokeSize, out var shaft, out var head);
            segments.Add(shaft);
            segments.Add(head);
        }
        else
        {
            var outline = StrokeGeometry.BuildShapeOutline(
                kind == InkShapeKind.DashedLine ? InkShapeKind.Line : kind, start, end);
            if (outline.Count > 0)
                segments.Add(outline);
        }
        return segments;
    }

    /// <summary>
    /// Builds the committed stroke set for the shape. Every part shares one
    /// freshly generated <see cref="ShapeStrokeIdentity.GroupId"/>, a uniform
    /// colour/size, <c>FitToCurve = false</c> and <c>IgnorePressure = true</c>
    /// (uniform-width shape ink, exactly like the WPF DrawingAttributes the
    /// shape tool applies). Returns an empty list for degenerate gestures.
    /// </summary>
    /// <param name="segments">
    /// Optional per-segment rewrite (e.g. the ruler constraint) applied to
    /// each outline segment before baking; return null to drop the segment.
    /// </param>
    public static List<InkStrokeData> BuildShapeStrokes(
        InkShapeKind kind,
        PointD start,
        PointD end,
        byte r, byte g, byte b, byte a,
        double strokeSize,
        bool dashed,
        Func<IReadOnlyList<PointD>, IReadOnlyList<PointD>> segmentConstraint = null)
    {
        var segments = BuildPreviewPolylines(kind, start, end, strokeSize);
        if (segments.Count == 0)
            return new List<InkStrokeData>();

        bool effectiveDashed = dashed || kind == InkShapeKind.DashedLine;
        StrokeGeometry.GetShapeDashPattern(strokeSize, out double dashLength, out double gapLength);
        string groupId = Guid.NewGuid().ToString("N");
        string kindName = kind.ToString();

        var strokes = new List<InkStrokeData>();
        int partIndex = 0;
        foreach (var segment in segments)
        {
            IReadOnlyList<PointD> effective = segment;
            if (segmentConstraint != null)
            {
                effective = segmentConstraint(segment);
                if (effective == null || effective.Count < 2)
                    continue;
            }
            else if (effective.Count < 2)
            {
                continue;
            }

            if (effectiveDashed)
            {
                foreach (var dash in StrokeGeometry.BuildDashedPolyline(effective, dashLength, gapLength))
                {
                    if (dash.Count < 2)
                        continue;
                    strokes.Add(CreateStroke(dash, r, g, b, a, strokeSize, groupId, kindName, partIndex++, true));
                }
            }
            else
            {
                strokes.Add(CreateStroke(effective, r, g, b, a, strokeSize, groupId, kindName, partIndex++, false));
            }
        }
        return strokes;
    }

    private static InkStrokeData CreateStroke(
        IReadOnlyList<PointD> points,
        byte r, byte g, byte b, byte a,
        double size,
        string groupId, string kind, int partIndex, bool isDashed) => new()
    {
        Points = points
            .Select(p => new InkPointData(p.X, p.Y, 0.5f))
            .ToList(),
        R = r, G = g, B = b, A = a,
        Size = size,
        IsHighlighter = false,
        FitToCurve = false,
        IgnorePressure = true,
        ShapeGroupId = groupId,
        ShapeKind = kind,
        ShapePartIndex = partIndex,
        IsDashedShape = isDashed,
    };
}
