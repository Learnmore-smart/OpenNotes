using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;
using Caelum.InkGeometry;

namespace Caelum.Models;

/// <summary>
/// WPF adapter over the UI-free shape-identity primitives in OpenNotes.Core.
/// <see cref="ShapeStrokeIdentity"/>, the stable property keys and the
/// dash-path math now live in Core; this facade keeps the live-
/// <see cref="Stroke"/> extended-property interop (Apply/Read) and the
/// System.Windows.Point-based dash builders the shape tool and tests use.
/// </summary>
public static class ShapeStrokeMetadata
{
    public static void Apply(Stroke stroke, string groupId, string kind, int partIndex, bool isDashed)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        SetProperty(stroke, ShapeStrokeMetadataKeys.GroupId, groupId ?? string.Empty);
        SetProperty(stroke, ShapeStrokeMetadataKeys.Kind, kind ?? string.Empty);
        SetProperty(stroke, ShapeStrokeMetadataKeys.PartIndex, partIndex);
        SetProperty(stroke, ShapeStrokeMetadataKeys.IsDashed, isDashed);
    }

    public static ShapeStrokeIdentity Read(Stroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        return new ShapeStrokeIdentity(
            ReadProperty(stroke, ShapeStrokeMetadataKeys.GroupId, string.Empty),
            ReadProperty(stroke, ShapeStrokeMetadataKeys.Kind, string.Empty),
            ReadProperty(stroke, ShapeStrokeMetadataKeys.PartIndex, 0),
            ReadProperty(stroke, ShapeStrokeMetadataKeys.IsDashed, false));
    }

    public static IReadOnlyList<IReadOnlyList<Point>> BuildDashedLine(
        Point start,
        Point end,
        double dashLength,
        double gapLength) =>
        BuildDashedPolyline(new[] { start, end }, dashLength, gapLength);

    public static IReadOnlyList<IReadOnlyList<Point>> BuildDashedPolyline(
        IReadOnlyList<Point> points,
        double dashLength,
        double gapLength)
    {
        ArgumentNullException.ThrowIfNull(points);
        var corePoints = new List<PointD>(points.Count);
        foreach (var point in points)
            corePoints.Add(new PointD(point.X, point.Y));

        var parts = StrokeGeometry.BuildDashedPolyline(corePoints, dashLength, gapLength);
        var result = new List<IReadOnlyList<Point>>(parts.Count);
        foreach (var part in parts)
        {
            var segment = new List<Point>(part.Count);
            foreach (var point in part)
                segment.Add(new Point(point.X, point.Y));
            result.Add(segment);
        }
        return result;
    }

    private static void SetProperty(Stroke stroke, Guid key, object value)
    {
        if (stroke.ContainsPropertyData(key))
            stroke.RemovePropertyData(key);
        stroke.AddPropertyData(key, value);
    }

    private static T ReadProperty<T>(Stroke stroke, Guid key, T fallback)
    {
        return stroke.ContainsPropertyData(key) && stroke.GetPropertyData(key) is T value
            ? value
            : fallback;
    }
}
