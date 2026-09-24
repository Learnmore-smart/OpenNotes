using System;
using System.Collections.Generic;
using System.Linq;
using Caelum.Models;

// Named InkGeometry rather than Caelum.Geometry: inside Caelum.* code a bare
// "Geometry" identifier would otherwise resolve to this namespace instead of
// System.Windows.Media.Geometry (icons/PathIcon data in the WPF project).
namespace Caelum.InkGeometry;

/// <summary>
/// Minimal 2D point used by the UI-free ink math. Mirrors the
/// <c>System.Windows.Point</c>/<c>Vector</c> operations the ported code
/// relied on (subtraction, scaling, dot product).
/// </summary>
public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public static PointD operator *(PointD v, double s) => new(v.X * s, v.Y * s);
    public static PointD operator *(double s, PointD v) => new(v.X * s, v.Y * s);

    public double Dot(PointD other) => X * other.X + Y * other.Y;
}

/// <summary>
/// Minimal axis-aligned rectangle matching the <c>System.Windows.Rect</c>
/// semantics the ported ink math relies on: edge-inclusive
/// <see cref="Contains(PointD)"/>/<see cref="Contains(RectD)"/>, edge-inclusive
/// <see cref="IntersectsWith"/>, per-side <see cref="Inflated"/> and bounding
/// <see cref="Union"/>.
/// </summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointD TopLeft => new(Left, Top);
    public PointD TopRight => new(Right, Top);
    public PointD BottomLeft => new(Left, Bottom);
    public PointD BottomRight => new(Right, Bottom);

    /// <summary>Inclusive containment — a point on the edge counts as inside.</summary>
    public bool Contains(PointD point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    /// <summary>True when <paramref name="rect"/> is fully inside this rect (edges may touch).</summary>
    public bool Contains(RectD rect) =>
        rect.Left >= Left && rect.Right <= Right && rect.Top >= Top && rect.Bottom <= Bottom;

    /// <summary>True when the two rects overlap, including edge-only contact.</summary>
    public bool IntersectsWith(RectD rect) =>
        rect.Left <= Right && rect.Right >= Left && rect.Top <= Bottom && rect.Bottom >= Top;

    /// <summary>Returns a copy grown by <paramref name="width"/>/<paramref name="height"/> on every side.</summary>
    public RectD Inflated(double width, double height) =>
        new(X - width, Y - height, Width + 2 * width, Height + 2 * height);

    /// <summary>Smallest rect containing both this rect and <paramref name="rect"/>.</summary>
    public RectD Union(RectD rect)
    {
        double left = Math.Min(Left, rect.Left);
        double top = Math.Min(Top, rect.Top);
        double right = Math.Max(Right, rect.Right);
        double bottom = Math.Max(Bottom, rect.Bottom);
        return new RectD(left, top, right - left, bottom - top);
    }
}

/// <summary>
/// UI-free mirror of the shape-tool kind enum (<c>Caelum.Controls.ShapeKind</c>
/// on the WPF side). Member NAMES must stay identical — the WPF adapter maps
/// by name-switch and fails loud on unrecognized kinds. Member order is
/// irrelevant to the adapter but locked anyway for any ordinal consumers
/// (persistence uses the kind name string, not the ordinal).
/// </summary>
public enum InkShapeKind
{
    Line,
    Rectangle,
    Ellipse,
    Arrow,
    Triangle,
    Diamond,
    Parallelogram,
    Pentagon,
    Hexagon,
    DashedLine
}

/// <summary>
/// Pure, UI-free stroke/ink geometry: eraser rects and hidden-ink hits,
/// point/capsule hit-testing, stroke splitting, lasso/marquee containment,
/// shape outlines, dashed paths, scribble shape recognition and the ruler /
/// smoothing / ink-simulation point transforms. Everything here is a direct
/// port of the math that used to live inside the WPF page control on
/// <c>System.Windows.*</c> types — behaviour is identical by construction.
/// The only deliberate approximation is <see cref="SplitStrokeAtEraser"/>,
/// which models the eraser as a swept square stamp along its path
/// (<c>RectangleStylusShape</c> parity — a stamp per path point plus the
/// convex hull of each consecutive pair); the WPF layer still uses the
/// exact rendered-geometry clip (<c>Stroke.GetEraseResult</c>) at runtime,
/// so this pure split exists for the UI-free host and tests.
/// </summary>
public static class StrokeGeometry
{
    private const double ClosedShapeEndpointTolerance = 4.0;
    private const int EllipseSegmentCount = 64;

    // Scribble shape-recognition thresholds (ported verbatim).

    /// <summary>Fewer points cannot evidence a shape; recognition callers gate on this.</summary>
    public const int MinRecognizedShapePoints = 8;
    private const double MinRecognizedDiagonal = 24.0;          // px; tiny scribbles are left alone
    private const double ClosedGapRatio = 0.15;                 // first-last gap < 15% of perimeter → closed
    private const double LineMeanDeviationRatio = 0.06;         // mean perp deviation / diagonal
    private const double EllipseMinCircularity = 0.82;          // 1 - stdR/meanR of centroid distances
    private const double EllipseMinSweepRadians = 300.0 * Math.PI / 180.0;
    private const double RectMinRunFraction = 0.06;             // runs shorter than 6% of points are noise
    private const double RectMinRunCoverage = 0.80;             // dominant runs must cover ≥ 80% of points
    private const double RectSideStraightness = 0.06;           // mean deviation / side chord length
    private const double RectCornerToleranceRatio = 0.12;       // corner match tolerance / diagonal

    private readonly struct DirectionRun
    {
        public DirectionRun(int bucket, int start, int end)
        {
            Bucket = bucket;
            Start = start;
            End = end;
        }

        public int Bucket { get; }
        public int Start { get; }
        public int End { get; }
        public int Length => End - Start + 1;
    }

    // ------------------------------------------------------------------
    // Basic distances / projections
    // ------------------------------------------------------------------

    public static double Dist(PointD a, PointD b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double PerpendicularDistance(PointD p, PointD a, PointD b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < double.Epsilon)
            return Dist(p, a);
        return Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / len;
    }

    /// <summary>Shortest distance from <paramref name="point"/> to segment a→b.</summary>
    public static double DistanceToSegment(PointD point, PointD a, PointD b) =>
        Dist(point, ProjectToSegment(point, a, b));

    public static PointD ProjectToSegment(PointD point, PointD a, PointD b)
    {
        PointD edge = b - a;
        double lengthSquared = edge.X * edge.X + edge.Y * edge.Y;
        if (lengthSquared < 1e-4)
            return a;
        double t = (point - a).Dot(edge) / lengthSquared;
        t = Math.Max(0, Math.Min(1, t));
        return a + edge * t;
    }

    /// <summary>Maximum point-to-segment distance over the spine points.</summary>
    public static double MaxDistanceToSegment(IReadOnlyList<InkPointData> points, PointD a, PointD b)
    {
        double max = 0;
        foreach (var point in points)
        {
            var source = new PointD(point.X, point.Y);
            max = Math.Max(max, Dist(source, ProjectToSegment(source, a, b)));
        }
        return max;
    }

    /// <summary>
    /// Quantises a direction into four 45° buckets over the mod-180°
    /// range: 0 ≈ horizontal, 2 ≈ vertical, 1/3 ≈ diagonals ('up' and
    /// 'down' share a bucket). Perpendicular directions always land two
    /// buckets apart.
    /// </summary>
    public static int DirectionBucket(double dx, double dy)
    {
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double normalized = angle % 180.0;
        if (normalized < 0)
            normalized += 180.0;
        return (int)Math.Floor((normalized + 22.5) / 45.0) % 4;
    }

    // ------------------------------------------------------------------
    // Bounds
    // ------------------------------------------------------------------

    /// <summary>
    /// Axis-aligned bounds of a spine point list, optionally grown by
    /// <paramref name="inflate"/> on every side (e.g. half the stroke width
    /// to approximate the rendered outline).
    /// </summary>
    public static RectD GetBounds(IReadOnlyList<PointD> points, double inflate = 0.0)
    {
        if (points == null || points.Count == 0)
            return new RectD(0, 0, 0, 0);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new RectD(minX - inflate, minY - inflate,
            (maxX - minX) + 2 * inflate, (maxY - minY) + 2 * inflate);
    }

    /// <summary><see cref="GetBounds(IReadOnlyList{PointD}, double)"/> for pressure-carrying spine points.</summary>
    public static RectD GetSpineBounds(IReadOnlyList<InkPointData> points, double inflate = 0.0)
    {
        if (points == null || points.Count == 0)
            return new RectD(0, 0, 0, 0);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new RectD(minX - inflate, minY - inflate,
            (maxX - minX) + 2 * inflate, (maxY - minY) + 2 * inflate);
    }

    // ------------------------------------------------------------------
    // Eraser path rects + hidden-ink intersection (ported verbatim)
    // ------------------------------------------------------------------

    /// <summary>One square stamp rect per eraser pointer point.</summary>
    public static List<RectD> CreateEraserRects(IReadOnlyList<PointD> points, double eraserSize)
    {
        var eraserRects = new List<RectD>(points.Count);
        foreach (var pt in points)
        {
            eraserRects.Add(new RectD(
                pt.X - eraserSize / 2,
                pt.Y - eraserSize / 2,
                eraserSize,
                eraserSize));
        }

        return eraserRects;
    }

    /// <summary>
    /// Hidden-ink eraser hit: a mask point inside an inflated eraser rect, or
    /// a mask segment crossing one, counts as touched. The radius mixes the
    /// eraser stamp and the mask's own stroke width.
    /// </summary>
    public static bool HiddenInkIntersectsEraser(
        HiddenInkAnnotation annotation,
        IReadOnlyList<RectD> eraserRects,
        double eraserSize)
    {
        if (annotation?.Points == null || annotation.Points.Count == 0)
            return false;

        double radius = Math.Max(1.0, eraserSize / 2.0 + annotation.Size / 2.0);
        var points = annotation.Points
            .Where(point => point != null && point.Length >= 2
                && double.IsFinite(point[0]) && double.IsFinite(point[1]))
            .Select(point => new PointD(point[0], point[1]))
            .ToList();

        for (int i = 0; i < points.Count; i++)
        {
            for (int rectIndex = 0; rectIndex < eraserRects.Count; rectIndex++)
            {
                var expanded = eraserRects[rectIndex].Inflated(radius, radius);
                if (i == 0
                    ? expanded.Contains(points[i])
                    : SegmentIntersectsRect(points[i - 1], points[i], expanded))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Liang–Barsky style segment/rect intersection.</summary>
    public static bool SegmentIntersectsRect(PointD start, PointD end, RectD rect)
    {
        // Non-finite endpoints poison the clip comparisons (NaN never fails
        // a relational test, so a NaN segment could report a hit).
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) ||
            !double.IsFinite(end.X) || !double.IsFinite(end.Y))
            return false;

        if (rect.Contains(start) || rect.Contains(end))
            return true;

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double tMin = 0.0;
        double tMax = 1.0;

        return ClipSegmentToBoundary(-dx, start.X - rect.Left, ref tMin, ref tMax)
            && ClipSegmentToBoundary(dx, rect.Right - start.X, ref tMin, ref tMax)
            && ClipSegmentToBoundary(-dy, start.Y - rect.Top, ref tMin, ref tMax)
            && ClipSegmentToBoundary(dy, rect.Bottom - start.Y, ref tMin, ref tMax);
    }

    private static bool ClipSegmentToBoundary(
        double p,
        double q,
        ref double tMin,
        ref double tMax)
    {
        const double epsilon = 1e-12;
        if (Math.Abs(p) < epsilon)
            return q >= 0;

        double ratio = q / p;
        if (p < 0)
        {
            if (ratio > tMax)
                return false;
            if (ratio > tMin)
                tMin = ratio;
        }
        else
        {
            if (ratio < tMin)
                return false;
            if (ratio < tMax)
                tMax = ratio;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Hit testing (spine capsules + the ported closed-shape/bounds rule)
    // ------------------------------------------------------------------

    /// <summary>
    /// Capsule hit-test: true when any spine segment passes within
    /// <paramref name="radius"/> of <paramref name="point"/>. The caller adds
    /// half the stroke width (and half the pointer stamp) to
    /// <paramref name="radius"/> to approximate the widened rendered outline.
    /// </summary>
    public static bool HitTestStroke(IReadOnlyList<InkPointData> points, PointD point, double radius)
    {
        if (points == null || points.Count == 0)
            return false;
        if (points.Count == 1)
            return Dist(new PointD(points[0].X, points[0].Y), point) <= radius;

        for (int i = 1; i < points.Count; i++)
        {
            if (DistanceToSegment(point, AsPoint(points[i - 1]), AsPoint(points[i])) <= radius)
                return true;
        }
        return false;
    }

    /// <summary>Capsule hit-test for pressure-free spine points.</summary>
    public static bool HitTestPolyline(IReadOnlyList<PointD> points, PointD point, double radius)
    {
        if (points == null || points.Count == 0)
            return false;
        if (points.Count == 1)
            return Dist(points[0], point) <= radius;

        for (int i = 1; i < points.Count; i++)
        {
            if (DistanceToSegment(point, points[i - 1], points[i]) <= radius)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Pure port of the selection click rule that runs after the (WPF-side)
    /// widened-path hit test: closed shapes (first ≈ last point) use their
    /// actual polygon interior and never fall back to bounds; open strokes
    /// are selectable anywhere inside their bounds.
    /// </summary>
    public static bool HitTestClosedOrBounds(IReadOnlyList<PointD> points, RectD bounds, PointD point)
    {
        if (points.Count >= 4)
        {
            var pFirst = points[0];
            var pLast = points[points.Count - 1];
            if (Math.Abs(pFirst.X - pLast.X) < ClosedShapeEndpointTolerance
                && Math.Abs(pFirst.Y - pLast.Y) < ClosedShapeEndpointTolerance)
            {
                return IsPointInPolygon(points, point);
            }
        }

        return bounds.Contains(point);
    }

    /// <summary>
    /// The pressure→half-width law measured from WPF
    /// <c>System.Windows.Ink.Stroke</c> rendering (empirical, exact on the
    /// probed corpus): rendered diameter = size·(0.25 + 1.5·p), so the
    /// half-width is size·(0.125 + 0.75·p). p=0.5 — the StylusPoint default
    /// and the effective value WPF uses when DrawingAttributes.IgnorePressure
    /// is set — renders at exactly the nominal size; p=1.0 renders at 1.75×,
    /// p=0.25 at 0.625×.
    /// </summary>
    public static double GetRenderedStrokeHalfWidth(double size, float pressure)
    {
        double p = pressure;
        if (p < 0.0) p = 0.0;
        else if (p > 1.0) p = 1.0;
        return size * (0.125 + 0.75 * p);
    }

    /// <summary>
    /// The per-point effective pressure: <paramref name="ignorePressure"/>
    /// pins every point to WPF's uniform-width pressure factor (0.5).
    /// </summary>
    private static float EffectivePressure(InkPointData point, bool ignorePressure)
        => ignorePressure ? 0.5f : point.Pressure;

    /// <summary>
    /// True when an eraser travelling along <paramref name="eraserPath"/>
    /// (an axis-aligned square stamp of <paramref name="eraserSize"/> swept
    /// along the path — WPF <c>RectangleStylusShape</c> parity) touches the
    /// rendered stroke outline. This is the whole-stroke eraser decision and
    /// the prefilter for <see cref="SplitStrokeAtEraser"/>.
    /// A spine point s(t) is touched when its rendered disc — radius
    /// <see cref="GetRenderedStrokeHalfWidth"/> of the (interpolated) point
    /// pressure — intersects the swept footprint: square stamps at every
    /// path point plus the convex swept hull of each consecutive pair.
    /// </summary>
    public static bool EraserHitsStroke(
        IReadOnlyList<InkPointData> strokePoints,
        double strokeSize,
        bool strokeIgnoresPressure,
        IReadOnlyList<PointD> eraserPath,
        double eraserSize)
        => EraserHitsStroke(
            strokePoints, strokeSize, strokeIgnoresPressure,
            BuildEraserFootprint(eraserPath, eraserSize));

    /// <summary>
    /// <see cref="EraserHitsStroke"/> with a prebuilt footprint — the batch
    /// path. Callers erasing many strokes along one eraser path (the ink
    /// surface evaluates every candidate per pointer update) build the
    /// pieces once via <see cref="BuildEraserFootprint"/> instead of paying
    /// the stamp+hull construction per stroke.
    /// </summary>
    public static bool EraserHitsStroke(
        IReadOnlyList<InkPointData> strokePoints,
        double strokeSize,
        bool strokeIgnoresPressure,
        IReadOnlyList<IReadOnlyList<PointD>> footprintPieces)
    {
        var pieces = footprintPieces;
        if (strokePoints == null || strokePoints.Count == 0 || pieces == null || pieces.Count == 0)
            return false;

        if (strokePoints.Count == 1)
        {
            var point = strokePoints[0];
            double w = GetRenderedStrokeHalfWidth(
                strokeSize, EffectivePressure(point, strokeIgnoresPressure));
            return PointWithinFootprint(AsPoint(point), pieces, w);
        }

        for (int i = 1; i < strokePoints.Count; i++)
        {
            var a = strokePoints[i - 1];
            var b = strokePoints[i];
            double wa = GetRenderedStrokeHalfWidth(
                strokeSize, EffectivePressure(a, strokeIgnoresPressure));
            double wb = GetRenderedStrokeHalfWidth(
                strokeSize, EffectivePressure(b, strokeIgnoresPressure));

            foreach (var piece in pieces)
            {
                if (CollectConvexStampIntervals(
                        AsPoint(a), AsPoint(b), wa, wb, piece, null))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Splits a stroke spine where the swept square-stamp eraser footprint
    /// (<paramref name="eraserSize"/>-sided axis-aligned stamps at every path
    /// point plus the convex hull swept between consecutive points, matching
    /// WPF <c>RectangleStylusShape</c>+<c>Stroke.GetEraseResult</c>) overlaps
    /// the rendered stroke outline.
    /// The stroke's own rendered half-width is applied per spine point via
    /// <see cref="GetRenderedStrokeHalfWidth"/> — pressure-varying strokes
    /// clip at their true visual edge. Returns the surviving fragments in
    /// draw order: an empty list means the stroke was fully erased; when the
    /// eraser misses, the result is a single fragment equal to the input
    /// points (the caller treats that as "unchanged", mirroring
    /// <c>Stroke.GetEraseResult</c> returning the original stroke). Fragment
    /// endpoints on cut boundaries are interpolated, including pressure.
    /// Boundary agreement with <c>Stroke.GetEraseResult</c> is exact to
    /// ~1e-2 DIP on straight strokes (cross-validated by the corpus test);
    /// corner grazes can differ by up to ~0.4·strokeWidth because WPF clips
    /// the quad patch while this model clips the node-disc union.
    /// </summary>
    public static List<List<InkPointData>> SplitStrokeAtEraser(
        IReadOnlyList<InkPointData> strokePoints,
        double strokeSize,
        bool strokeIgnoresPressure,
        IReadOnlyList<PointD> eraserPath,
        double eraserSize)
        => SplitStrokeAtEraser(
            strokePoints, strokeSize, strokeIgnoresPressure,
            BuildEraserFootprint(eraserPath, eraserSize));

    /// <summary>
    /// <see cref="SplitStrokeAtEraser"/> with a prebuilt footprint — the
    /// batch path for callers splitting many strokes along one eraser path
    /// (the ink surface evaluates every candidate per pointer update); build
    /// the pieces once via <see cref="BuildEraserFootprint"/>.
    /// </summary>
    public static List<List<InkPointData>> SplitStrokeAtEraser(
        IReadOnlyList<InkPointData> strokePoints,
        double strokeSize,
        bool strokeIgnoresPressure,
        IReadOnlyList<IReadOnlyList<PointD>> footprintPieces)
    {
        var fragments = new List<List<InkPointData>>();
        if (strokePoints == null || strokePoints.Count == 0)
            return fragments;

        var pieces = footprintPieces;
        if (pieces == null || pieces.Count == 0)
        {
            fragments.Add(new List<InkPointData>(strokePoints));
            return fragments;
        }

        if (strokePoints.Count == 1)
        {
            var point = strokePoints[0];
            double w = GetRenderedStrokeHalfWidth(
                strokeSize, EffectivePressure(point, strokeIgnoresPressure));
            if (!PointWithinFootprint(AsPoint(point), pieces, w))
                fragments.Add(new List<InkPointData> { point });
            return fragments;
        }

        List<InkPointData> current = null;
        for (int i = 1; i < strokePoints.Count; i++)
        {
            var a = strokePoints[i - 1];
            var b = strokePoints[i];
            double wa = GetRenderedStrokeHalfWidth(
                strokeSize, EffectivePressure(a, strokeIgnoresPressure));
            double wb = GetRenderedStrokeHalfWidth(
                strokeSize, EffectivePressure(b, strokeIgnoresPressure));

            var removed = new List<(double t0, double t1)>();
            foreach (var piece in pieces)
                CollectConvexStampIntervals(AsPoint(a), AsPoint(b), wa, wb, piece, removed);
            MergeIntervalsInPlace(removed);

            // Complement of the removed intervals within [0,1] = kept pieces.
            double cursor = 0.0;
            foreach (var (t0, t1) in removed)
            {
                if (t0 > cursor)
                    EmitKeptPiece(fragments, ref current, a, b, cursor, t0);
                if (t1 > cursor)
                    cursor = t1;
            }
            if (cursor < 1.0)
                EmitKeptPiece(fragments, ref current, a, b, cursor, 1.0);
            else
                CloseFragment(fragments, ref current);
        }

        CloseFragment(fragments, ref current);
        return fragments;
    }

    /// <summary>
    /// Appends the kept sub-interval [t0,t1] of segment a→b to the open
    /// fragment (or starts a new one when the previous piece ended erased).
    /// When t1 &lt; 1 the segment's end vertex is erased, so the fragment
    /// closes immediately.
    /// </summary>
    /// <param name="fragments">Closed fragments accumulate here.</param>
    /// <param name="current">The still-open fragment (may be null).</param>
    private static void EmitKeptPiece(
        List<List<InkPointData>> fragments,
        ref List<InkPointData> current,
        InkPointData a,
        InkPointData b,
        double t0,
        double t1)
    {
        if (t1 < t0)
            return;

        var start = LerpPoint(a, b, t0);
        if (current != null)
        {
            // A piece starting at the shared vertex (t0 == 0) continues the
            // open fragment — the vertex is already its last point. A piece
            // starting mid-segment follows an erased gap and must begin a
            // new fragment instead. Pressure is part of the vertex identity:
            // two consecutive spine points may share X/Y yet differ in
            // pressure, and dropping one would make an untouched stroke
            // return a fragment that differs from the input.
            var last = current[current.Count - 1];
            if (last.X != start.X || last.Y != start.Y || last.Pressure != start.Pressure)
            {
                CloseFragment(fragments, ref current);
                current = new List<InkPointData> { start };
            }
        }
        else
        {
            current = new List<InkPointData> { start };
        }

        var end = LerpPoint(a, b, t1);
        var tail = current[current.Count - 1];
        if (tail.X != end.X || tail.Y != end.Y || tail.Pressure != end.Pressure)
            current.Add(end);

        if (t1 < 1.0)
            CloseFragment(fragments, ref current);
    }

    private static void CloseFragment(
        List<List<InkPointData>> fragments,
        ref List<InkPointData> current)
    {
        if (current != null && current.Count > 0)
            fragments.Add(current);
        current = null;
    }

    private static InkPointData LerpPoint(InkPointData a, InkPointData b, double t)
    {
        if (t <= 0.0)
            return a;
        if (t >= 1.0)
            return b;
        return new InkPointData(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            (float)(a.Pressure + (b.Pressure - a.Pressure) * t));
    }

    // ------------------------------------------------------------------
    // Square-stamp eraser model (WPF RectangleStylusShape parity)
    //
    // The eraser footprint is the union of convex pieces: one axis-aligned
    // square stamp per path point, plus the swept hull (convex hexagon) of
    // each consecutive stamp pair — exactly the region a
    // RectangleStylusShape(size,size) covers while travelling the path.
    // A spine point s(t) is erased when dist(s(t), piece) ≤ w(t), where
    // w(t) is the stroke's rendered half-width lerped between the segment
    // endpoints — i.e. the node disc touches the stamp. WPF clips the
    // tapered quad patch against the same polygon; on the probed corpus the
    // two models produce identical clip parameters to ~1e-2 DIP on
    // non-degenerate geometry.
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the eraser footprint as a list of convex polygons: one square
    /// stamp per path point plus the convex hull of each adjacent stamp pair
    /// (the swept connector — a hexagon for off-axis motion, a rectangle for
    /// axis-aligned motion). Public so callers evaluating many strokes
    /// against one eraser path can hoist the construction out of the
    /// per-stroke loop (<see cref="EraserHitsStroke"/>/
    /// <see cref="SplitStrokeAtEraser"/> piece-taking overloads).
    /// </summary>
    public static List<IReadOnlyList<PointD>> BuildEraserFootprint(
        IReadOnlyList<PointD> eraserPath,
        double eraserSize)
    {
        var pieces = new List<IReadOnlyList<PointD>>();
        if (eraserPath == null || eraserPath.Count == 0 || eraserSize < 0.0)
            return pieces;

        double h = eraserSize * 0.5;
        foreach (var center in eraserPath)
            pieces.Add(SquareAt(center, h));

        for (int i = 1; i < eraserPath.Count; i++)
        {
            var hull = SweptSquareHull(eraserPath[i - 1], eraserPath[i], h);
            if (hull != null)
                pieces.Add(hull);
        }
        return pieces;
    }

    /// <summary>The four corners of the axis-aligned stamp centered at c.</summary>
    private static IReadOnlyList<PointD> SquareAt(PointD c, double h) =>
        new List<PointD>
        {
            new(c.X - h, c.Y - h),
            new(c.X + h, c.Y - h),
            new(c.X + h, c.Y + h),
            new(c.X - h, c.Y + h),
        };

    /// <summary>
    /// Convex hull of the two square stamps at e1/e2 — the region the square
    /// covers while sweeping from e1 to e2. Returns null for a degenerate
    /// (coincident) pair; the per-point stamps already cover that case.
    /// </summary>
    private static IReadOnlyList<PointD> SweptSquareHull(PointD e1, PointD e2, double h)
    {
        if (Math.Abs(e2.X - e1.X) <= 1e-9 && Math.Abs(e2.Y - e1.Y) <= 1e-9)
            return null;

        var points = new List<PointD>(8);
        foreach (var c in new[] { e1, e2 })
        {
            points.Add(new PointD(c.X - h, c.Y - h));
            points.Add(new PointD(c.X + h, c.Y - h));
            points.Add(new PointD(c.X + h, c.Y + h));
            points.Add(new PointD(c.X - h, c.Y + h));
        }
        return ConvexHull(points);
    }

    /// <summary>
    /// Andrew monotone-chain convex hull. Returns vertices in CCW order
    /// (mathematical orientation; screen-space Y direction is irrelevant
    /// because every consumer derives the inside side from the centroid).
    /// Collinear hull-edge points are dropped.
    /// </summary>
    private static List<PointD> ConvexHull(List<PointD> points)
    {
        var sorted = points
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToList();
        var hull = new List<PointD>(sorted.Count);

        // Lower hull
        foreach (var p in sorted)
        {
            while (hull.Count >= 2
                && Cross(hull[^1] - hull[^2], p - hull[^1]) <= 1e-12)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(p);
        }

        // Upper hull
        int lowerCount = hull.Count;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            var p = sorted[i];
            while (hull.Count > lowerCount
                && Cross(hull[^1] - hull[^2], p - hull[^1]) <= 1e-12)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(p);
        }

        if (hull.Count > 1)
            hull.RemoveAt(hull.Count - 1); // last == first
        return hull;
    }

    private static double Cross(PointD u, PointD v) => u.X * v.Y - u.Y * v.X;

    /// <summary>
    /// Collects the merged t-intervals of segment a→b (half-width lerped
    /// wa→wb) where dist(s(t), convexPolygon) ≤ w(t). When
    /// <paramref name="intervals"/> is null the call acts as an early-out
    /// predicate and returns true on the first hit.
    /// </summary>
    private static bool CollectConvexStampIntervals(
        PointD a,
        PointD b,
        double wa,
        double wb,
        IReadOnlyList<PointD> polygon,
        List<(double t0, double t1)> intervals)
    {
        bool hit = false;

        double ux = b.X - a.X;
        double uy = b.Y - a.Y;
        double segLenSq = ux * ux + uy * uy;
        if (segLenSq <= 1e-18)
        {
            // Degenerate spine point: disc-vs-polygon is a pure distance test.
            if (PointWithinPolygonDistance(a, polygon, wa))
            {
                if (intervals == null)
                    return true;
                intervals.Add((0.0, 1.0));
                return true;
            }
            return false;
        }

        // Centroid — inside side reference for every edge half-plane.
        double cx = 0, cy = 0;
        foreach (var v in polygon) { cx += v.X; cy += v.Y; }
        cx /= polygon.Count; cy /= polygon.Count;

        // 1. Interior interval: s(t) satisfies every edge half-plane.
        double lo = 0.0, hi = 1.0;
        bool insideOk = true;
        for (int k = 0; k < polygon.Count && insideOk; k++)
        {
            var v = polygon[k];
            var f = polygon[(k + 1) % polygon.Count];
            double ex = f.X - v.X, ey = f.Y - v.Y;
            double cRef = ex * (cy - v.Y) - ey * (cx - v.X);
            if (Math.Abs(cRef) <= 1e-12)
                continue; // degenerate edge
            double s = cRef > 0 ? 1.0 : -1.0;
            // g(t) = cross(e, s(t) − v); need s·g(t) ≥ 0.
            double g0 = ex * (a.Y - v.Y) - ey * (a.X - v.X);
            double g1 = ex * (uy) - ey * (ux);
            insideOk = RefineGeq(ref lo, ref hi, s * g0, s * g1);
        }
        if (insideOk && hi > lo)
        {
            if (intervals == null)
                return true;
            intervals.Add((lo, hi));
            hit = true;
        }

        // 2. Per edge: strip (projection inside + |perp| ≤ w(t)) + vertex caps.
        for (int k = 0; k < polygon.Count; k++)
        {
            var e = polygon[k];
            var f = polygon[(k + 1) % polygon.Count];
            double ex = f.X - e.X, ey = f.Y - e.Y;
            double edgeLenSq = ex * ex + ey * ey;
            if (edgeLenSq <= 1e-18)
            {
                if (TryAddVertexCapInterval(intervals, a, b, wa, wb, e))
                    hit = true;
                continue;
            }

            double edgeLen = Math.Sqrt(edgeLenSq);
            double nx = ex / edgeLen, ny = ey / edgeLen;

            double lo2 = 0.0, hi2 = 1.0;
            // proj(t) = dot(s(t)−e, n) must stay in [0, edgeLen]
            double h0 = (a.X - e.X) * nx + (a.Y - e.Y) * ny;
            double dh = ux * nx + uy * ny;
            bool ok = RefineGeq(ref lo2, ref hi2, h0, dh)
                && RefineGeq(ref lo2, ref hi2, edgeLen - h0, -dh);
            // perp(t) = cross(n, s(t)−e) must satisfy |perp| ≤ w(t)
            double q0 = nx * (a.Y - e.Y) - ny * (a.X - e.X);
            double dq = nx * uy - ny * ux;
            double dw = wb - wa;
            if (ok)
                ok = RefineGeq(ref lo2, ref hi2, wa - q0, dw - dq)
                    && RefineGeq(ref lo2, ref hi2, wa + q0, dw + dq);
            if (ok && hi2 > lo2)
            {
                if (intervals == null)
                    return true;
                intervals.Add((lo2, hi2));
                hit = true;
            }

            // Vertex caps: |s(t) − v|² ≤ w(t)² — quadratic in t.
            if (TryAddVertexCapInterval(intervals, a, b, wa, wb, e))
                hit = true;
        }

        return hit;
    }

    /// <summary>
    /// Intersects [lo,hi] with the solution set of c0 + c1·t ≥ −eps.
    /// Returns false when the result is empty.
    /// </summary>
    private static bool RefineGeq(ref double lo, ref double hi, double c0, double c1)
    {
        const double eps = 1e-9;
        if (Math.Abs(c1) <= 1e-12)
            return c0 >= -eps;

        double t = (-eps - c0) / c1;
        if (c1 > 0)
        {
            if (t > lo) lo = t;
        }
        else
        {
            if (t < hi) hi = t;
        }
        return hi > lo;
    }

    /// <summary>
    /// Adds the t-interval(s) where |s(t) − v| ≤ w(t). Both sides are
    /// linear in t, giving a quadratic inequality A t² + B t + C ≤ 0.
    /// A &lt; 0 (pressure grows faster than the spine moves) produces the
    /// "outside the roots" solution pair.
    /// </summary>
    private static bool TryAddVertexCapInterval(
        List<(double t0, double t1)> intervals,
        PointD a,
        PointD b,
        double wa,
        double wb,
        PointD v)
    {
        double ux = b.X - a.X, uy = b.Y - a.Y;
        double fx = a.X - v.X, fy = a.Y - v.Y;
        double dw = wb - wa;

        double A = ux * ux + uy * uy - dw * dw;
        double B = 2.0 * (fx * ux + fy * uy - wa * dw);
        double C = fx * fx + fy * fy - wa * wa;

        bool any = false;
        if (Math.Abs(A) <= 1e-12)
        {
            // Linear (or constant) inequality.
            if (Math.Abs(B) <= 1e-12)
            {
                if (C <= 0.0) { EmitInterval(intervals, 0.0, 1.0, ref any); }
            }
            else
            {
                double t = -C / B;
                if (B > 0) EmitInterval(intervals, 0.0, t, ref any);       // t ≤ −C/B
                else EmitInterval(intervals, t, 1.0, ref any);           // t ≥ −C/B
            }
            return any;
        }

        double disc = B * B - 4.0 * A * C;
        if (disc < 0.0)
        {
            // No real roots: sign is constant — positive for A>0 (no
            // interval), negative for A<0 (always within reach).
            if (A < 0.0)
                EmitInterval(intervals, 0.0, 1.0, ref any);
            return any;
        }

        double root = Math.Sqrt(disc);
        double tA = (-B - root) / (2.0 * A);
        double tB = (-B + root) / (2.0 * A);
        double r0 = Math.Min(tA, tB);
        double r1 = Math.Max(tA, tB);
        if (A > 0.0)
        {
            EmitInterval(intervals, r0, r1, ref any);
        }
        else
        {
            EmitInterval(intervals, 0.0, r0, ref any);
            EmitInterval(intervals, r1, 1.0, ref any);
        }
        return any;
    }

    private static void EmitInterval(
        List<(double t0, double t1)> intervals,
        double t0,
        double t1,
        ref bool any)
    {
        double c0 = Math.Max(0.0, t0);
        double c1 = Math.Min(1.0, t1);
        // A zero-width interval is a tangent touch, not an overlap —
        // admitting it would split a fragment at a single point.
        if (c1 <= c0)
            return;
        intervals?.Add((c0, c1));
        any = true;
    }

    /// <summary>Sorts and merges the collected intervals in place.</summary>
    private static void MergeIntervalsInPlace(List<(double t0, double t1)> intervals)
    {
        if (intervals.Count <= 1)
            return;

        intervals.Sort((x, y) => x.t0.CompareTo(y.t0));
        int write = 0;
        for (int read = 1; read < intervals.Count; read++)
        {
            var current = intervals[write];
            var next = intervals[read];
            if (next.t0 <= current.t1)
            {
                if (next.t1 > current.t1)
                    intervals[write] = (current.t0, next.t1);
            }
            else
            {
                intervals[++write] = next;
            }
        }
        intervals.RemoveRange(write + 1, intervals.Count - write - 1);
    }

    /// <summary>
    /// True when the point's rendered disc of radius w touches any footprint
    /// piece — inside the polygon counts as distance 0.
    /// </summary>
    private static bool PointWithinFootprint(
        PointD point,
        IReadOnlyList<IReadOnlyList<PointD>> pieces,
        double w)
    {
        foreach (var piece in pieces)
        {
            if (PointWithinPolygonDistance(point, piece, w))
                return true;
        }
        return false;
    }

    private static bool PointWithinPolygonDistance(
        PointD point, IReadOnlyList<PointD> polygon, double w)
    {
        bool inside = true;
        double cx = 0, cy = 0;
        foreach (var v in polygon) { cx += v.X; cy += v.Y; }
        cx /= polygon.Count; cy /= polygon.Count;

        for (int k = 0; k < polygon.Count; k++)
        {
            var v = polygon[k];
            var f = polygon[(k + 1) % polygon.Count];
            double ex = f.X - v.X, ey = f.Y - v.Y;
            double cRef = ex * (cy - v.Y) - ey * (cx - v.X);
            if (Math.Abs(cRef) <= 1e-12)
                continue;
            double s = cRef > 0 ? 1.0 : -1.0;
            double g = ex * (point.Y - v.Y) - ey * (point.X - v.X);
            if (s * g < -1e-9)
            {
                inside = false;
                break;
            }
        }
        if (inside)
            return true;

        for (int k = 0; k < polygon.Count; k++)
        {
            if (DistanceToSegment(point, polygon[k], polygon[(k + 1) % polygon.Count]) <= w)
                return true;
        }
        return false;
    }

    private static PointD AsPoint(InkPointData p) => new(p.X, p.Y);

    // ------------------------------------------------------------------
    // Lasso / marquee selection (ported verbatim)
    // ------------------------------------------------------------------

    /// <summary>Ray-cast point-in-polygon test (ported verbatim).</summary>
    public static bool IsPointInPolygon(IReadOnlyList<PointD> polygon, PointD p)
    {
        bool inside = false;
        int j = polygon.Count - 1;
        for (int i = 0; i < polygon.Count; i++)
        {
            if (((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y)) &&
                (p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                inside = !inside;
            j = i;
        }
        return inside;
    }

    /// <summary>All four rect corners inside the polygon.</summary>
    public static bool IsRectInsidePolygon(IReadOnlyList<PointD> polygon, RectD rect)
    {
        return IsPointInPolygon(polygon, rect.TopLeft) &&
               IsPointInPolygon(polygon, rect.TopRight) &&
               IsPointInPolygon(polygon, rect.BottomLeft) &&
               IsPointInPolygon(polygon, rect.BottomRight);
    }

    /// <summary>
    /// Lasso rule for a stroke: inside when its bounds fit in the polygon, or
    /// when ≥ 60% of spine points are inside. (A ≤ 3-point stroke that is
    /// fully inside already satisfies the ratio, so no special-casing is
    /// needed for small strokes.)
    /// </summary>
    public static bool IsStrokeInsidePolygon(
        IReadOnlyList<PointD> polygon,
        IReadOnlyList<PointD> strokePoints,
        RectD strokeBounds)
    {
        if (IsRectInsidePolygon(polygon, strokeBounds))
            return true;

        if (strokePoints.Count == 0)
            return false;

        int insideCount = 0;
        foreach (var pt in strokePoints)
        {
            if (IsPointInPolygon(polygon, pt))
                insideCount++;
        }

        return (double)insideCount / strokePoints.Count >= 0.6;
    }

    /// <summary>
    /// Lasso rule for overlay containers: inside when the whole rect fits, or
    /// its center is inside, or at least two corners are inside.
    /// </summary>
    public static bool IsContainerInsidePolygon(IReadOnlyList<PointD> polygon, RectD containerRect)
    {
        if (IsRectInsidePolygon(polygon, containerRect))
            return true;

        var center = new PointD(
            containerRect.Left + containerRect.Width / 2,
            containerRect.Top + containerRect.Height / 2);
        if (IsPointInPolygon(polygon, center))
            return true;

        int cornersIn = 0;
        if (IsPointInPolygon(polygon, containerRect.TopLeft)) cornersIn++;
        if (IsPointInPolygon(polygon, containerRect.TopRight)) cornersIn++;
        if (IsPointInPolygon(polygon, containerRect.BottomLeft)) cornersIn++;
        if (IsPointInPolygon(polygon, containerRect.BottomRight)) cornersIn++;

        return cornersIn >= 2;
    }

    /// <summary>
    /// Marquee rule for a stroke: inside when the rect fully contains the
    /// stroke bounds, or when ≥ 70% of spine points are inside.
    /// </summary>
    public static bool IsStrokeInsideRect(
        RectD selRect,
        IReadOnlyList<PointD> strokePoints,
        RectD strokeBounds)
    {
        if (selRect.Contains(strokeBounds))
            return true;

        if (strokePoints.Count == 0)
            return false;

        int insideCount = 0;
        foreach (var pt in strokePoints)
        {
            if (selRect.Contains(pt))
                insideCount++;
        }

        return (double)insideCount / strokePoints.Count >= 0.7;
    }

    // ------------------------------------------------------------------
    // Shape outlines / dashed paths / endpoint constraints (ported verbatim)
    // ------------------------------------------------------------------

    /// <summary>
    /// Outline point list for line / rectangle / ellipse and the polygon
    /// shapes. The ellipse is a parametric polygon with 64 segments, which
    /// renders crisp because shape strokes use FitToCurve=false.
    /// </summary>
    public static List<PointD> BuildShapeOutline(InkShapeKind kind, PointD start, PointD end)
    {
        switch (kind)
        {
            case InkShapeKind.Rectangle:
                return new List<PointD>
                {
                    start,
                    new PointD(end.X, start.Y),
                    end,
                    new PointD(start.X, end.Y),
                    start // closed
                };
            case InkShapeKind.Ellipse:
                var points = new List<PointD>(EllipseSegmentCount + 1);
                double cx = (start.X + end.X) / 2;
                double cy = (start.Y + end.Y) / 2;
                double rx = Math.Abs(end.X - start.X) / 2;
                double ry = Math.Abs(end.Y - start.Y) / 2;
                for (int i = 0; i <= EllipseSegmentCount; i++)
                {
                    double t = 2 * Math.PI * i / EllipseSegmentCount;
                    points.Add(new PointD(cx + rx * Math.Cos(t), cy + ry * Math.Sin(t)));
                }
                return points;
            case InkShapeKind.Triangle:
            {
                double left = Math.Min(start.X, end.X);
                double right = Math.Max(start.X, end.X);
                double top = Math.Min(start.Y, end.Y);
                double bottom = Math.Max(start.Y, end.Y);
                var apex = new PointD((left + right) / 2, top);
                return new List<PointD>
                {
                    apex,
                    new PointD(right, bottom),
                    new PointD(left, bottom),
                    apex
                };
            }
            case InkShapeKind.Diamond:
            {
                double left = Math.Min(start.X, end.X);
                double right = Math.Max(start.X, end.X);
                double top = Math.Min(start.Y, end.Y);
                double bottom = Math.Max(start.Y, end.Y);
                double diamondCx = (left + right) / 2;
                double diamondCy = (top + bottom) / 2;
                var first = new PointD(diamondCx, top);
                return new List<PointD>
                {
                    first,
                    new PointD(right, diamondCy),
                    new PointD(diamondCx, bottom),
                    new PointD(left, diamondCy),
                    first
                };
            }
            case InkShapeKind.Parallelogram:
            {
                double left = Math.Min(start.X, end.X);
                double right = Math.Max(start.X, end.X);
                double top = Math.Min(start.Y, end.Y);
                double bottom = Math.Max(start.Y, end.Y);
                double inset = (right - left) * 0.24;
                var first = new PointD(left + inset, top);
                return new List<PointD>
                {
                    first,
                    new PointD(right, top),
                    new PointD(right - inset, bottom),
                    new PointD(left, bottom),
                    first
                };
            }
            case InkShapeKind.Pentagon:
                return BuildRegularPolygonOutline(start, end, 5);
            case InkShapeKind.Hexagon:
                return BuildRegularPolygonOutline(start, end, 6);
            case InkShapeKind.Line:
            default:
                return new List<PointD> { start, end };
        }
    }

    private static List<PointD> BuildRegularPolygonOutline(PointD start, PointD end, int sides)
    {
        double left = Math.Min(start.X, end.X);
        double right = Math.Max(start.X, end.X);
        double top = Math.Min(start.Y, end.Y);
        double bottom = Math.Max(start.Y, end.Y);
        double cx = (left + right) / 2;
        double cy = (top + bottom) / 2;
        double rx = (right - left) / 2;
        double ry = (bottom - top) / 2;
        var points = new List<PointD>(sides + 1);

        for (int i = 0; i < sides; i++)
        {
            double angle = -Math.PI / 2 + (2 * Math.PI * i / sides);
            points.Add(new PointD(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle)));
        }

        points.Add(points[0]);
        return points;
    }

    /// <summary>
    /// Arrow geometry: a shaft (start → end) plus a two-wing head drawn as
    /// a 'V' through the end point. Head length scales with the stroke
    /// size and is capped at half the shaft length.
    /// </summary>
    public static void BuildArrowGeometry(
        PointD start,
        PointD end,
        double strokeSize,
        out List<PointD> shaft,
        out List<PointD> head)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);

        if (len < double.Epsilon)
        {
            shaft = new List<PointD> { start, end };
            head = new List<PointD> { end, end, end };
            return;
        }

        double ux = dx / len;   // unit direction
        double uy = dy / len;
        double px = -uy;        // unit perpendicular
        double py = ux;

        double headLen = Math.Min(Math.Max(strokeSize * 3.0, 10.0), len * 0.5);
        double headWidth = headLen * 0.6;

        var wing1 = new PointD(end.X - ux * headLen + px * headWidth,
                               end.Y - uy * headLen + py * headWidth);
        var wing2 = new PointD(end.X - ux * headLen - px * headWidth,
                               end.Y - uy * headLen - py * headWidth);

        shaft = new List<PointD> { start, end };
        head = new List<PointD> { wing1, end, wing2 };
    }

    /// <summary>
    /// Shift constraint for the shape tool. With Shift held, line/arrow snap
    /// their direction to the nearest multiple of 45° (the end point is
    /// re-projected onto the snapped ray at the original length) and
    /// rectangle/ellipse become square/circle (side = max(|dx|,|dy|), drag
    /// direction signs preserved). Without Shift the end point is returned
    /// unchanged.
    /// </summary>
    public static PointD ConstrainShapeEndpoints(PointD start, PointD end, InkShapeKind kind, bool isShift)
    {
        if (!isShift)
            return end;

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;

        switch (kind)
        {
            case InkShapeKind.Line:
            case InkShapeKind.Arrow:
            {
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < double.Epsilon)
                    return end;
                double snapped = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4.0)) * (Math.PI / 4.0);
                return new PointD(start.X + len * Math.Cos(snapped), start.Y + len * Math.Sin(snapped));
            }
            case InkShapeKind.Rectangle:
            case InkShapeKind.Ellipse:
            case InkShapeKind.Triangle:
            case InkShapeKind.Diamond:
            case InkShapeKind.Parallelogram:
            case InkShapeKind.Pentagon:
            case InkShapeKind.Hexagon:
            {
                // Square / circle: the larger extent wins; sign(0)
                // defaults to + so pure vertical/horizontal drags still
                // produce a full-size square.
                double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                double sx = dx >= 0 ? 1 : -1;
                double sy = dy >= 0 ? 1 : -1;
                return new PointD(start.X + sx * side, start.Y + sy * side);
            }
            default:
                return end;
        }
    }

    /// <summary>Dash a two-point line; delegates to <see cref="BuildDashedPolyline"/>.</summary>
    public static IReadOnlyList<IReadOnlyList<PointD>> BuildDashedLine(
        PointD start,
        PointD end,
        double dashLength,
        double gapLength) =>
        BuildDashedPolyline(new[] { start, end }, dashLength, gapLength);

    /// <summary>
    /// The persisted-ink dash pattern — the same lengths the WPF shape tool
    /// bakes into committed dashed strokes
    /// (<c>dashLength = max(size·4, 10)</c>, <c>gapLength = max(size·2.5, 6)</c>
    /// in <c>PdfPageControl.CommitShape</c>). Renderers that re-dash an
    /// <see cref="InkStrokeData.IsDashedShape"/> spine must use this pattern
    /// so the on-screen look matches what WPF committed. (The WPF drag
    /// PREVIEW uses a relative <c>StrokeDashArray {4, 2}</c> instead — close,
    /// but not identical; persisted ink is the contract.)
    /// </summary>
    public static void GetShapeDashPattern(
        double strokeSize,
        out double dashLength,
        out double gapLength)
    {
        dashLength = Math.Max(strokeSize * 4.0, 10.0);
        gapLength = Math.Max(strokeSize * 2.5, 6.0);
    }

    /// <summary>
    /// Splits a polyline into dash segments separated by empty gaps, carrying
    /// the dash/gap phase across corners (ported verbatim — real gaps contain
    /// no ink and therefore never hit or erase).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PointD>> BuildDashedPolyline(
        IReadOnlyList<PointD> points,
        double dashLength,
        double gapLength)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (dashLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(dashLength));
        if (gapLength < 0)
            throw new ArgumentOutOfRangeException(nameof(gapLength));
        var parts = new List<IReadOnlyList<PointD>>();
        if (points.Count < 2)
            return parts;

        bool drawingDash = true;
        double patternRemaining = dashLength;
        List<PointD> currentDash = null;

        for (int segmentIndex = 1; segmentIndex < points.Count; segmentIndex++)
        {
            PointD start = points[segmentIndex - 1];
            PointD end = points[segmentIndex];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= double.Epsilon)
                continue;

            double unitX = dx / length;
            double unitY = dy / length;
            double offset = 0;
            while (offset < length)
            {
                double step = Math.Min(patternRemaining, length - offset);
                PointD from = new(start.X + (unitX * offset), start.Y + (unitY * offset));
                PointD to = new(start.X + (unitX * (offset + step)), start.Y + (unitY * (offset + step)));

                if (drawingDash)
                {
                    currentDash ??= new List<PointD> { from };
                    if (currentDash[^1] != to)
                        currentDash.Add(to);
                }

                offset += step;
                patternRemaining -= step;
                if (patternRemaining <= double.Epsilon)
                {
                    if (drawingDash && currentDash is { Count: > 1 })
                        parts.Add(currentDash);
                    currentDash = null;
                    drawingDash = !drawingDash;
                    patternRemaining = drawingDash ? dashLength : gapLength;

                    // A zero-length gap means consecutive dashes are equivalent
                    // to one continuous stroke, but still must make progress.
                    if (!drawingDash && patternRemaining <= double.Epsilon)
                    {
                        drawingDash = true;
                        patternRemaining = dashLength;
                    }
                }
            }
        }

        if (currentDash is { Count: > 1 })
            parts.Add(currentDash);
        return parts;
    }

    // ------------------------------------------------------------------
    // Scribble shape recognition (ported verbatim)
    // ------------------------------------------------------------------

    /// <summary>
    /// Classifies a freshly collected freehand spine as a line / rectangle /
    /// ellipse and produces the ideal outline. Returns false when none of the
    /// confidence gates pass — the caller then leaves the stroke untouched.
    /// </summary>
    public static bool TryRecognizeShape(IReadOnlyList<PointD> points, out List<PointD> outline)
    {
        outline = null;
        if (points == null)
            return false;
        int n = points.Count;
        if (n < MinRecognizedShapePoints)
            return false;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        var bounds = new RectD(minX, minY, maxX - minX, maxY - minY);
        double diag = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
        if (diag < MinRecognizedDiagonal)
            return false;

        double perimeter = 0;
        for (int i = 1; i < n; i++)
            perimeter += Dist(points[i - 1], points[i]);
        if (perimeter <= double.Epsilon)
            return false;

        bool closed = Dist(points[0], points[n - 1]) < ClosedGapRatio * perimeter;

        if (closed)
        {
            // Rectangles are tested first: a near-square hand-drawn
            // rectangle also passes the ellipse circularity gate and
            // would otherwise be snapped to a circle.
            if (LooksLikeRectangle(points, bounds, diag))
                outline = BuildShapeOutline(InkShapeKind.Rectangle, bounds.TopLeft, bounds.BottomRight);
            else if (LooksLikeEllipse(points))
                outline = BuildShapeOutline(InkShapeKind.Ellipse, bounds.TopLeft, bounds.BottomRight);
            else
                return false;
        }
        else
        {
            if (!LooksLikeLine(points, diag))
                return false;
            outline = BuildShapeOutline(InkShapeKind.Line, points[0], points[n - 1]);
        }

        return true;
    }

    /// <summary>
    /// Open stroke whose points hug the first→last chord: the mean
    /// perpendicular deviation stays below 6% of the diagonal.
    /// </summary>
    private static bool LooksLikeLine(IReadOnlyList<PointD> points, double diag)
    {
        var a = points[0];
        var b = points[points.Count - 1];
        double sum = 0;
        foreach (var p in points)
            sum += PerpendicularDistance(p, a, b);
        return sum / points.Count < LineMeanDeviationRatio * diag;
    }

    /// <summary>
    /// Closed stroke whose points stay at a near-constant distance from
    /// the centroid (circularity &gt; 0.82) while sweeping at least 300°
    /// around it. The ideal fit is axis-aligned to the original bounds.
    /// </summary>
    private static bool LooksLikeEllipse(IReadOnlyList<PointD> points)
    {
        double cx = 0, cy = 0;
        foreach (var p in points)
        {
            cx += p.X;
            cy += p.Y;
        }
        cx /= points.Count;
        cy /= points.Count;

        double sumR = 0, sumR2 = 0;
        var angles = new List<double>(points.Count);
        foreach (var p in points)
        {
            double dx = p.X - cx, dy = p.Y - cy;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r < 1e-6)
                continue; // centroid-coincident points carry no angle
            sumR += r;
            sumR2 += r * r;
            angles.Add(Math.Atan2(dy, dx));
        }
        if (angles.Count < 4)
            return false;

        int m = angles.Count;
        double meanR = sumR / m;
        if (meanR <= double.Epsilon)
            return false;
        double stdR = Math.Sqrt(Math.Max(0, sumR2 / m - meanR * meanR));
        if (1 - stdR / meanR <= EllipseMinCircularity)
            return false;

        // Angular coverage = 2π minus the largest gap between sorted
        // angles (the wrap-around gap included).
        angles.Sort();
        double maxGap = angles[0] + 2 * Math.PI - angles[m - 1];
        for (int i = 1; i < m; i++)
        {
            double gap = angles[i] - angles[i - 1];
            if (gap > maxGap)
                maxGap = gap;
        }
        return 2 * Math.PI - maxGap >= EllipseMinSweepRadians;
    }

    /// <summary>
    /// Closed stroke with exactly four dominant direction runs: local
    /// directions (5-point window) are quantised into four 45° buckets
    /// (mod 180°), short runs are dropped as noise, and the survivors
    /// must alternate between two perpendicular buckets, be straight,
    /// cover most of the stroke and turn near the four corners of the
    /// fitted bounds (rejects rotated rects, diamonds and trapezoids).
    /// </summary>
    private static bool LooksLikeRectangle(IReadOnlyList<PointD> points, RectD bounds, double diag)
    {
        int n = points.Count;

        var buckets = new int[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - 2);
            int hi = Math.Min(n - 1, i + 2);
            double dx = points[hi].X - points[lo].X;
            double dy = points[hi].Y - points[lo].Y;
            buckets[i] = (dx == 0 && dy == 0) ? -1 : DirectionBucket(dx, dy);
        }

        // Contiguous same-bucket runs.
        var runs = new List<DirectionRun>();
        for (int i = 0; i < n;)
        {
            int j = i;
            while (j + 1 < n && buckets[j + 1] == buckets[i])
                j++;
            runs.Add(new DirectionRun(buckets[i], i, j));
            i = j + 1;
        }

        // Drop noise runs (corner arcs, jitter); merge same-bucket
        // neighbours that only a dropped run separated.
        int minRunPoints = Math.Max(2, (int)Math.Ceiling(n * RectMinRunFraction));
        var dominant = new List<DirectionRun>();
        foreach (var run in runs)
        {
            if (run.Bucket < 0 || run.Length < minRunPoints)
                continue;
            if (dominant.Count > 0 && dominant[^1].Bucket == run.Bucket)
                dominant[^1] = new DirectionRun(run.Bucket, dominant[^1].Start, run.End);
            else
                dominant.Add(run);
        }

        // A closed stroke may start mid-side: then the first and last
        // dominant runs are the two halves of one side (same bucket).
        bool wrapped = dominant.Count > 1
            && dominant[0].Start == 0 && dominant[^1].End == n - 1
            && dominant[0].Bucket == dominant[^1].Bucket;
        int sideCount = dominant.Count - (wrapped ? 1 : 0);
        if (sideCount != 4)
            return false;

        // Consecutive sides must be perpendicular (bucket +2 mod 4).
        for (int k = 0; k + 1 < dominant.Count; k++)
            if (dominant[k + 1].Bucket != (dominant[k].Bucket + 2) % 4)
                return false;
        if (!wrapped && dominant[0].Bucket != (dominant[^1].Bucket + 2) % 4)
            return false;

        // Dominant runs must cover most of the stroke.
        int covered = 0;
        foreach (var run in dominant)
            covered += run.Length;
        if (covered < RectMinRunCoverage * n)
            return false;

        // Each side must be straight: mean perpendicular deviation from
        // its run chord below 6% of the chord length.
        foreach (var run in dominant)
        {
            var a = points[run.Start];
            var b = points[run.End];
            double chord = Dist(a, b);
            if (chord <= double.Epsilon)
                return false;
            double sum = 0;
            for (int i = run.Start; i <= run.End; i++)
                sum += PerpendicularDistance(points[i], a, b);
            if (sum / run.Length > RectSideStraightness * chord)
                return false;
        }

        // Detected corners (midpoints of the transitions between
        // consecutive sides) and the four bounds corners must match
        // each other within 12% of the diagonal.
        var detectedCorners = new List<PointD>();
        for (int k = 0; k + 1 < dominant.Count; k++)
        {
            int mid = (dominant[k].End + dominant[k + 1].Start) / 2;
            detectedCorners.Add(points[mid]);
        }
        if (!wrapped)
        {
            int mid = (dominant[^1].End + n + dominant[0].Start) / 2;
            detectedCorners.Add(points[mid % n]);
        }
        if (detectedCorners.Count != 4)
            return false;

        double cornerTolerance = RectCornerToleranceRatio * diag;
        var boundsCorners = new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft };
        foreach (var detected in detectedCorners)
        {
            double nearest = double.MaxValue;
            foreach (var corner in boundsCorners)
                nearest = Math.Min(nearest, Dist(detected, corner));
            if (nearest > cornerTolerance)
                return false;
        }
        foreach (var corner in boundsCorners)
        {
            double nearest = double.MaxValue;
            foreach (var detected in detectedCorners)
                nearest = Math.Min(nearest, Dist(detected, corner));
            if (nearest > cornerTolerance)
                return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Stroke post-processing transforms (ported verbatim)
    // ------------------------------------------------------------------

    /// <summary>
    /// Velocity-based pressure simulation: slow segments get PressureFactor
    /// ~1.0 (capped), fast ones ~0.25 (floored); the distance between
    /// consecutive points is the speed proxy, normalised by the fastest
    /// segment. Returns null for a stationary stroke (all points
    /// coincident) — nothing to simulate.
    /// </summary>
    public static IReadOnlyList<InkPointData> SimulateInkFlow(IReadOnlyList<InkPointData> points)
    {
        int count = points.Count;

        var stepDist = new double[count];
        double maxDist = 0;
        for (int i = 1; i < count; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            double dy = points[i].Y - points[i - 1].Y;
            stepDist[i] = Math.Sqrt(dx * dx + dy * dy);
            if (stepDist[i] > maxDist)
                maxDist = stepDist[i];
        }

        // Stationary stroke (all points coincident) — nothing to simulate.
        if (maxDist <= double.Epsilon)
            return null;

        var simulatedPoints = new List<InkPointData>(count);
        for (int i = 0; i < count; i++)
        {
            var sp = points[i];
            double speedNorm = stepDist[i] / maxDist;
            simulatedPoints.Add(new InkPointData(
                sp.X,
                sp.Y,
                (float)Math.Max(0.25, 1.0 - 0.75 * speedNorm)));
        }
        return simulatedPoints;
    }

    /// <summary>
    /// Moving-average smoothing: levels 1-3 replace each point with the mean
    /// of its w = 1/2/4 neighbours on each side (index range clamped at the
    /// endpoints), preserving the ORIGINAL centre point's pressure. Returns
    /// null when the level is 0 or the stroke has fewer than 3 points —
    /// nothing to average.
    /// </summary>
    public static IReadOnlyList<InkPointData> SmoothPoints(IReadOnlyList<InkPointData> points, int level)
    {
        int count = points.Count;
        if (level <= 0 || count < 3)
            return null;

        int window = level == 1 ? 1 : level == 2 ? 2 : 4;
        // Clamp the effective window for short strokes: with a window
        // wider than half the stroke every averaged point collapses
        // toward the centroid and the stroke shrinks to a dot.
        int maxWindow = (count - 1) / 2;
        if (window > maxWindow)
            window = maxWindow;

        var smoothed = new List<InkPointData>(count);
        for (int i = 0; i < count; i++)
        {
            int lo = Math.Max(0, i - window);
            int hi = Math.Min(count - 1, i + window);
            double sumX = 0, sumY = 0;
            for (int j = lo; j <= hi; j++)
            {
                sumX += points[j].X;
                sumY += points[j].Y;
            }
            int n = hi - lo + 1;
            smoothed.Add(new InkPointData(sumX / n, sumY / n, points[i].Pressure));
        }
        return smoothed;
    }

    /// <summary>
    /// Shift-straighten endpoints: the first and last points of a stroke that
    /// actually moved (≥ 0.01 px displacement — a tap dot has nothing to
    /// straighten).
    /// </summary>
    public static bool TryGetStraightEndpoints(
        IReadOnlyList<InkPointData> points,
        out InkPointData first,
        out InkPointData last)
    {
        first = default;
        last = default;
        if (points == null || points.Count < 2)
            return false;

        first = points[0];
        last = points[points.Count - 1];
        double dx = last.X - first.X;
        double dy = last.Y - first.Y;
        return dx * dx + dy * dy >= 1e-4;
    }

    /// <summary>
    /// Ruler constraint for a freshly collected stroke. Returns:
    /// null — the stroke started inside the ruler body or crossed into it
    /// (caller removes the stroke);
    /// the same <paramref name="points"/> reference — unchanged (far from
    /// the ruler, or degenerate input);
    /// a new list — clipped at the first quad-edge crossing, or every point
    /// projected onto the nearest long edge when the whole stroke stays
    /// within <paramref name="snapTolerance"/> of it (t ∈ [0, 1], pressures
    /// preserved / interpolated at the clip point).
    /// </summary>
    public static IReadOnlyList<InkPointData> ConstrainPointsToRuler(
        IReadOnlyList<InkPointData> points,
        PointD topA,
        PointD topB,
        PointD bottomA,
        PointD bottomB,
        double snapTolerance)
    {
        if (points == null || points.Count == 0)
            return points;

        var quad = new[] { topA, topB, bottomB, bottomA };
        var first = new PointD(points[0].X, points[0].Y);
        if (IsPointInsideConvexQuad(first, quad))
            return null;

        for (int i = 1; i < points.Count; i++)
        {
            var from = new PointD(points[i - 1].X, points[i - 1].Y);
            var to = new PointD(points[i].X, points[i].Y);
            if (!TryFindFirstQuadIntersection(from, to, quad, out double entryT, out PointD entry))
            {
                // A gesture may begin exactly on the edge. That boundary
                // point is allowed for along-edge drawing, but moving from
                // it into the body must still produce no ink.
                if (IsPointInsideConvexQuad(to, quad))
                    return null;
                continue;
            }

            var clipped = new List<InkPointData>();
            for (int j = 0; j < i; j++)
                clipped.Add(points[j]);
            float pressure = (float)(points[i - 1].Pressure
                + (points[i].Pressure - points[i - 1].Pressure) * entryT);
            clipped.Add(new InkPointData(entry.X, entry.Y, pressure));
            return clipped;
        }

        double topDistance = MaxDistanceToSegment(points, topA, topB);
        double bottomDistance = MaxDistanceToSegment(points, bottomA, bottomB);
        if (Math.Min(topDistance, bottomDistance) >= snapTolerance)
            return points;

        PointD edgeA = topDistance <= bottomDistance ? topA : bottomA;
        PointD edgeB = topDistance <= bottomDistance ? topB : bottomB;
        var snapped = new List<InkPointData>(points.Count);
        foreach (var point in points)
        {
            PointD projected = ProjectToSegment(new PointD(point.X, point.Y), edgeA, edgeB);
            snapped.Add(new InkPointData(projected.X, projected.Y, point.Pressure));
        }
        return snapped;
    }

    public static bool IsPointInsideConvexQuad(PointD point, IReadOnlyList<PointD> quad)
    {
        double? sign = null;
        for (int i = 0; i < quad.Count; i++)
        {
            PointD a = quad[i];
            PointD b = quad[(i + 1) % quad.Count];
            double cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            if (Math.Abs(cross) < 1e-7)
                return false;
            double current = Math.Sign(cross);
            if (sign.HasValue && current != sign.Value)
                return false;
            sign = current;
        }
        return sign.HasValue;
    }

    public static bool TryFindFirstQuadIntersection(
        PointD from,
        PointD to,
        IReadOnlyList<PointD> quad,
        out double firstT,
        out PointD intersection)
    {
        firstT = double.MaxValue;
        intersection = default;
        for (int i = 0; i < quad.Count; i++)
        {
            if (TryIntersectSegments(from, to, quad[i], quad[(i + 1) % quad.Count], out double t)
                && t > 1e-7 && t < firstT)
            {
                firstT = t;
                intersection = from + (to - from) * t;
            }
        }
        return firstT != double.MaxValue;
    }

    public static bool TryIntersectSegments(PointD p, PointD p2, PointD q, PointD q2, out double t)
    {
        PointD r = p2 - p;
        PointD s = q2 - q;
        double cross = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(cross) < 1e-7)
        {
            t = 0;
            return false;
        }
        PointD qp = q - p;
        t = (qp.X * s.Y - qp.Y * s.X) / cross;
        double u = (qp.X * r.Y - qp.Y * r.X) / cross;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }
}
