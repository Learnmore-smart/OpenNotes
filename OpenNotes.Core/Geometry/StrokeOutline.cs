using System;
using System.Collections.Generic;
using Caelum.Models;

namespace Caelum.InkGeometry;

/// <summary>
/// Tessellates an <see cref="InkStrokeData"/> spine into a closed fill
/// outline: variable half-width offsets on both sides plus round caps at
/// both ends. The per-point half-width follows
/// <see cref="StrokeGeometry.GetRenderedStrokeHalfWidth"/> — the empirical
/// WPF <c>System.Windows.Ink</c> pressure law — so the rendered silhouette
/// matches what the WPF editor drew for the same DrawingAttributes.
/// Optional <paramref name="fitToCurve"/> resampling applies the same
/// Catmull-Rom-style subdivision WPF performs, which both smooths the path
/// and densifies sparse spines so corner facets stay small.
/// The returned polygon is a simple vertex list (first point not repeated);
/// degenerate inputs produce an empty list rather than throwing.
/// </summary>
public static class StrokeOutline
{
    /// <summary>
    /// Subdivisions per spine segment when FitToCurve resampling is active.
    /// Four matches the visual density of WPF's bezier fit closely enough for
    /// fill outlines while keeping the polygon small.
    /// </summary>
    private const int FitToCurveSubdivisions = 4;

    /// <summary>Vertices per round end cap (excluding the seam vertices).</summary>
    private const int CapSegments = 7;

    /// <summary>Vertices used to approximate a single-point tap disc.</summary>
    private const int DotSegments = 12;

    /// <summary>
    /// Builds the closed fill outline for a stroke. Returns an empty list for
    /// empty input. A single point produces a small disc of the rendered
    /// radius (WPF draws a tap as a filled ellipse stamp).
    /// </summary>
    public static List<PointD> BuildFillOutline(
        IReadOnlyList<InkPointData> spine,
        double size,
        bool ignorePressure,
        bool fitToCurve)
    {
        if (spine == null || spine.Count == 0 || size <= 0.0)
            return new List<PointD>();

        var pts = fitToCurve ? ResampleCatmullRom(spine) : spine;
        if (pts.Count == 0)
            return new List<PointD>();

        if (pts.Count == 1)
            return BuildDotOutline(pts[0], size, ignorePressure);

        int n = pts.Count;
        var half = new double[n];
        var normal = new PointD[n];
        for (int i = 0; i < n; i++)
        {
            double p = ignorePressure ? 0.5 : pts[i].Pressure;
            half[i] = StrokeGeometry.GetRenderedStrokeHalfWidth(size, (float)p);
            normal[i] = AveragedNormal(pts, i);
        }

        // Left chain forward, then end cap, then right chain backward, then
        // start cap — one closed polygon.
        var outline = new List<PointD>(2 * n + 2 * CapSegments + 4);
        for (int i = 0; i < n; i++)
            outline.Add(new PointD(
                pts[i].X + normal[i].X * half[i],
                pts[i].Y + normal[i].Y * half[i]));

        AppendCapArc(outline, new PointD(pts[n - 1].X, pts[n - 1].Y), normal[n - 1], half[n - 1]);

        for (int i = n - 1; i >= 0; i--)
            outline.Add(new PointD(
                pts[i].X - normal[i].X * half[i],
                pts[i].Y - normal[i].Y * half[i]));

        AppendCapArc(outline, new PointD(pts[0].X, pts[0].Y), new PointD(-normal[0].X, -normal[0].Y), half[0]);

        return outline;
    }

    /// <summary>
    /// Convenience overload taking the stroke record directly.
    /// </summary>
    public static List<PointD> BuildFillOutline(InkStrokeData stroke)
    {
        if (stroke == null)
            return new List<PointD>();
        return BuildFillOutline(stroke.Points, stroke.Size, stroke.IgnorePressure, stroke.FitToCurve);
    }

    /// <summary>
    /// Unit normal at spine vertex i: the perpendicular of the averaged
    /// adjacent edge directions. Endpoints take their single edge's normal.
    /// Zero-length edges contribute nothing; a fully degenerate neighbourhood
    /// falls back to a fixed normal so the outline still closes.
    /// </summary>
    private static PointD AveragedNormal(IReadOnlyList<InkPointData> pts, int i)
    {
        double dx = 0, dy = 0;
        if (i > 0)
        {
            double ex = pts[i].X - pts[i - 1].X;
            double ey = pts[i].Y - pts[i - 1].Y;
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len > 1e-9) { dx += ex / len; dy += ey / len; }
        }
        if (i < pts.Count - 1)
        {
            double ex = pts[i + 1].X - pts[i].X;
            double ey = pts[i + 1].Y - pts[i].Y;
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len > 1e-9) { dx += ex / len; dy += ey / len; }
        }

        double dlen = Math.Sqrt(dx * dx + dy * dy);
        if (dlen <= 1e-9)
            return new PointD(0, -1); // arbitrary stable normal
        return new PointD(-dy / dlen, dx / dlen);
    }

    /// <summary>
    /// Appends the half-disc cap at a stroke endpoint. <paramref name="startNormal"/>
    /// is the normal of the chain vertex the arc starts from; the arc sweeps
    /// 180° through the outward direction and ends at the opposite offset.
    /// </summary>
    private static void AppendCapArc(
        List<PointD> outline, PointD center, PointD startNormal, double radius)
    {
        if (radius <= 0.0)
            return;

        // Outward direction: rotate the start normal −90° (m.y, −m.x). For
        // the end cap m=+n gives +d (forward); for the start cap m=−n gives
        // −d — both arcs bulge outward from the spine instead of folding in.
        double ox = startNormal.Y, oy = -startNormal.X;
        for (int k = 1; k <= CapSegments; k++)
        {
            double angle = Math.PI * k / (CapSegments + 1);
            double c = Math.Cos(angle), s = Math.Sin(angle);
            outline.Add(new PointD(
                center.X + radius * (startNormal.X * c + ox * s),
                center.Y + radius * (startNormal.Y * c + oy * s)));
        }
    }

    /// <summary>Tap stamp: a small regular polygon standing in for the disc.</summary>
    private static List<PointD> BuildDotOutline(InkPointData point, double size, bool ignorePressure)
    {
        double p = ignorePressure ? 0.5 : point.Pressure;
        double r = StrokeGeometry.GetRenderedStrokeHalfWidth(size, (float)p);
        var outline = new List<PointD>(DotSegments);
        if (r <= 0.0)
            return outline;
        for (int k = 0; k < DotSegments; k++)
        {
            double angle = 2.0 * Math.PI * k / DotSegments;
            outline.Add(new PointD(point.X + r * Math.Cos(angle), point.Y + r * Math.Sin(angle)));
        }
        return outline;
    }

    /// <summary>
    /// Catmull-Rom resample of the spine (endpoint-duplicated, uniform
    /// parametrization): four subdivisions per segment, pressure lerped.
    /// Approximates WPF's FitToCurve bezier smoothing without depending on
    /// WPF internals — the outline contract is a filled hull either way.
    /// </summary>
    public static List<InkPointData> ResampleCatmullRom(IReadOnlyList<InkPointData> spine)
    {
        var result = new List<InkPointData>();
        if (spine == null || spine.Count == 0)
            return result;
        if (spine.Count < 3)
        {
            result.AddRange(spine);
            return result;
        }

        for (int i = 0; i < spine.Count - 1; i++)
        {
            var p0 = spine[Math.Max(0, i - 1)];
            var p1 = spine[i];
            var p2 = spine[i + 1];
            var p3 = spine[Math.Min(spine.Count - 1, i + 2)];

            result.Add(p1);
            for (int k = 1; k < FitToCurveSubdivisions; k++)
            {
                double t = (double)k / FitToCurveSubdivisions;
                double t2 = t * t, t3 = t2 * t;
                double x = 0.5 * ((2 * p1.X)
                    + (-p0.X + p2.X) * t
                    + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2
                    + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                double y = 0.5 * ((2 * p1.Y)
                    + (-p0.Y + p2.Y) * t
                    + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2
                    + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                float pr = (float)(0.5 * ((2 * p1.Pressure)
                    + (-p0.Pressure + p2.Pressure) * t
                    + (2 * p0.Pressure - 5 * p1.Pressure + 4 * p2.Pressure - p3.Pressure) * t2
                    + (-p0.Pressure + 3 * p1.Pressure - 3 * p2.Pressure + p3.Pressure) * t3));
                if (pr < 0f) pr = 0f; else if (pr > 1f) pr = 1f;
                result.Add(new InkPointData(x, y, pr));
            }
        }
        result.Add(spine[^1]);
        return result;
    }
}
