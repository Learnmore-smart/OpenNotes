using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Caelum.InkGeometry;
using Caelum.Models;

namespace Caelum.Controls;

/// <summary>
/// Thin interop layer between live WPF ink objects (<see cref="Stroke"/>,
/// <see cref="StylusPointCollection"/>, <see cref="Point"/>, <see cref="Rect"/>)
/// and the UI-free <see cref="InkStrokeData"/>/<see cref="PointD"/>/
/// <see cref="RectD"/> primitives the Core geometry engine consumes. All
/// conversions are coordinate/value copies — no behavioural logic lives here.
/// </summary>
internal static class WpfStrokeAdapter
{
    public static PointD ToPointD(Point point) => new(point.X, point.Y);

    public static Point ToPoint(PointD point) => new(point.X, point.Y);

    public static RectD ToRectD(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    public static Rect ToRect(RectD rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    public static List<PointD> ToPointDList(IReadOnlyList<Point> points)
    {
        var list = new List<PointD>(points.Count);
        foreach (var point in points)
            list.Add(new PointD(point.X, point.Y));
        return list;
    }

    public static List<PointD> ToPointDList(PointCollection points)
    {
        var list = new List<PointD>(points.Count);
        foreach (var point in points)
            list.Add(new PointD(point.X, point.Y));
        return list;
    }

    public static List<PointD> ToPointDList(StylusPointCollection points)
    {
        var list = new List<PointD>(points.Count);
        foreach (var point in points)
            list.Add(new PointD(point.X, point.Y));
        return list;
    }

    public static List<InkPointData> ToInkPoints(StylusPointCollection points)
    {
        var list = new List<InkPointData>(points.Count);
        foreach (var point in points)
            list.Add(new InkPointData(point.X, point.Y, point.PressureFactor));
        return list;
    }

    public static StylusPoint ToStylusPoint(InkPointData point) =>
        new(point.X, point.Y, point.Pressure);

    public static StylusPointCollection ToStylusPoints(IReadOnlyList<InkPointData> points)
    {
        var collection = new StylusPointCollection();
        foreach (var point in points)
            collection.Add(ToStylusPoint(point));
        return collection;
    }

    public static StylusPointCollection ToStylusPoints(IReadOnlyList<PointD> points)
    {
        var collection = new StylusPointCollection();
        foreach (var point in points)
            collection.Add(new StylusPoint(point.X, point.Y));
        return collection;
    }

    public static List<Point> ToPointList(IReadOnlyList<PointD> points)
    {
        var list = new List<Point>(points.Count);
        foreach (var point in points)
            list.Add(new Point(point.X, point.Y));
        return list;
    }

    /// <summary>
    /// Maps the WPF shape-tool kind onto its UI-free mirror. The enums share
    /// member names/order; anything unrecognized degrades to Line like the
    /// original switch default.
    /// </summary>
    public static InkShapeKind ToInkShapeKind(ShapeKind kind) => kind switch
    {
        ShapeKind.Rectangle => InkShapeKind.Rectangle,
        ShapeKind.Ellipse => InkShapeKind.Ellipse,
        ShapeKind.Arrow => InkShapeKind.Arrow,
        ShapeKind.Triangle => InkShapeKind.Triangle,
        ShapeKind.Diamond => InkShapeKind.Diamond,
        ShapeKind.Parallelogram => InkShapeKind.Parallelogram,
        ShapeKind.Pentagon => InkShapeKind.Pentagon,
        ShapeKind.Hexagon => InkShapeKind.Hexagon,
        ShapeKind.DashedLine => InkShapeKind.DashedLine,
        _ => InkShapeKind.Line
    };

    /// <summary>Snapshot of a live stroke as a UI-free payload.</summary>
    public static InkStrokeData ToInkStrokeData(Stroke stroke)
    {
        var attrs = stroke.DrawingAttributes;
        var color = attrs.Color;
        var data = new InkStrokeData
        {
            R = color.R,
            G = color.G,
            B = color.B,
            A = color.A,
            Size = attrs.Width,
            IsHighlighter = attrs.IsHighlighter,
            FitToCurve = attrs.FitToCurve,
            Points = ToInkPoints(stroke.StylusPoints)
        };
        data.ApplyShapeIdentity(ShapeStrokeMetadata.Read(stroke));
        return data;
    }

    /// <summary>
    /// Rebuilds a live stroke from a UI-free payload (used where the page
    /// pipeline needs a real <see cref="Stroke"/>; shape identity is written
    /// back through <see cref="ShapeStrokeMetadata.Apply"/>).
    /// </summary>
    public static Stroke ToStroke(InkStrokeData data)
    {
        var attrs = new DrawingAttributes
        {
            Color = Color.FromArgb(data.A, data.R, data.G, data.B),
            Width = data.Size > 0 ? data.Size : 2.0,
            Height = data.Size > 0 ? data.Size : 2.0,
            IsHighlighter = data.IsHighlighter,
            FitToCurve = data.FitToCurve
        };
        var identity = data.GetShapeIdentity();
        if (!string.IsNullOrWhiteSpace(identity.GroupId))
            attrs.IgnorePressure = true;

        var stroke = new Stroke(ToStylusPoints(data.Points)) { DrawingAttributes = attrs };
        if (!string.IsNullOrWhiteSpace(identity.GroupId) || !string.IsNullOrWhiteSpace(identity.Kind))
        {
            ShapeStrokeMetadata.Apply(
                stroke,
                identity.GroupId,
                identity.Kind,
                identity.PartIndex,
                identity.IsDashed);
        }
        return stroke;
    }
}
