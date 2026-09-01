using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;

namespace Caelum.Models;

public readonly record struct ShapeStrokeIdentity(
    string GroupId,
    string Kind,
    int PartIndex,
    bool IsDashed);

public static class ShapeStrokeMetadata
{
    private static readonly Guid GroupIdKey = new("767C2E92-6A10-4D55-9B79-5DCA69089B28");
    private static readonly Guid KindKey = new("4F97F528-AB40-4091-8899-326019220E6F");
    private static readonly Guid PartIndexKey = new("3A1127F7-2EA3-48D8-A5E4-B376AEAC2C87");
    private static readonly Guid DashedKey = new("D3F93EA9-D2D1-476A-BF14-9F267205983A");

    public static void Apply(Stroke stroke, string groupId, string kind, int partIndex, bool isDashed)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        SetProperty(stroke, GroupIdKey, groupId ?? string.Empty);
        SetProperty(stroke, KindKey, kind ?? string.Empty);
        SetProperty(stroke, PartIndexKey, partIndex);
        SetProperty(stroke, DashedKey, isDashed);
    }

    public static ShapeStrokeIdentity Read(Stroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        return new ShapeStrokeIdentity(
            ReadProperty(stroke, GroupIdKey, string.Empty),
            ReadProperty(stroke, KindKey, string.Empty),
            ReadProperty(stroke, PartIndexKey, 0),
            ReadProperty(stroke, DashedKey, false));
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
        if (dashLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(dashLength));
        if (gapLength < 0)
            throw new ArgumentOutOfRangeException(nameof(gapLength));
        var parts = new List<IReadOnlyList<Point>>();
        if (points.Count < 2)
            return parts;

        bool drawingDash = true;
        double patternRemaining = dashLength;
        List<Point> currentDash = null;

        for (int segmentIndex = 1; segmentIndex < points.Count; segmentIndex++)
        {
            Point start = points[segmentIndex - 1];
            Point end = points[segmentIndex];
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
                Point from = new(start.X + (unitX * offset), start.Y + (unitY * offset));
                Point to = new(start.X + (unitX * (offset + step)), start.Y + (unitY * (offset + step)));

                if (drawingDash)
                {
                    currentDash ??= new List<Point> { from };
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
