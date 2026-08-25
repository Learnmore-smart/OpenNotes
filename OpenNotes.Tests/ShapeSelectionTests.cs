using System.Reflection;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Caelum.Controls;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public sealed class ShapeSelectionTests
{
    private static readonly MethodInfo HitStrokeMethod =
        typeof(PdfPageControl).GetMethod("HitStroke", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.HitStroke was not found.");

    private static readonly MethodInfo IsStrokeInsidePolygonMethod =
        typeof(PdfPageControl).GetMethod("IsStrokeInsidePolygon", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.IsStrokeInsidePolygon was not found.");

    private static readonly MethodInfo IsStrokeInsideRectMethod =
        typeof(PdfPageControl).GetMethod("IsStrokeInsideRect", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.IsStrokeInsideRect was not found.");

    private static readonly MethodInfo BuildShapeOutlineMethod =
        typeof(PdfPageControl).GetMethod("BuildShapeOutline", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.BuildShapeOutline was not found.");

    [Test]
    public void ClickInsideClosedShapeHitsShape()
    {
        // A triangle from (10, 10) to (110, 110)
        var points = (IReadOnlyList<Point>)BuildShapeOutlineMethod.Invoke(null, new object[] { ShapeKind.Triangle, new Point(10, 10), new Point(110, 110) })!;
        var stylusPoints = new StylusPointCollection();
        foreach (var p in points)
            stylusPoints.Add(new StylusPoint(p.X, p.Y));
        var stroke = new Stroke(stylusPoints);

        // Center of triangle should hit
        var centerPoint = new Point(60, 60);
        var isHit = (bool)HitStrokeMethod.Invoke(null, new object[] { stroke, centerPoint })!;
        Assert.That(isHit, Is.True, "Clicking inside a triangle shape must hit the stroke.");

        // Point outside triangle should not hit
        var outsidePoint = new Point(15, 15); // near top-left empty corner
        var isOutsideHit = (bool)HitStrokeMethod.Invoke(null, new object[] { stroke, outsidePoint })!;
        Assert.That(isOutsideHit, Is.False, "Point outside triangle geometry should not hit.");
    }

    [Test]
    public void ClickOnLineShapeHitsStroke()
    {
        var linePoints = new[] { new StylusPoint(20, 20), new StylusPoint(100, 100) };
        var stroke = new Stroke(new StylusPointCollection(linePoints));

        // Click close to the line (e.g. (60, 62) - within 2px of (60, 60))
        var nearPoint = new Point(60, 62);
        var isHit = (bool)HitStrokeMethod.Invoke(null, new object[] { stroke, nearPoint })!;
        Assert.That(isHit, Is.True, "Clicking on/near a line shape must hit the stroke.");
    }

    [Test]
    public void LassoPolygonAroundShapeSelectsStroke()
    {
        // Create an ellipse shape from (50, 50) to (150, 150)
        var points = (IReadOnlyList<Point>)BuildShapeOutlineMethod.Invoke(null, new object[] { ShapeKind.Ellipse, new Point(50, 50), new Point(150, 150) })!;
        var stylusPoints = new StylusPointCollection();
        foreach (var p in points)
            stylusPoints.Add(new StylusPoint(p.X, p.Y));
        var stroke = new Stroke(stylusPoints);

        // A round-ish lasso surrounding the ellipse (e.g. hexagon loosely around it)
        var lasso = new PointCollection
        {
            new Point(40, 40),
            new Point(160, 40),
            new Point(170, 100),
            new Point(160, 160),
            new Point(40, 160),
            new Point(30, 100),
            new Point(40, 40)
        };

        var isInside = (bool)IsStrokeInsidePolygonMethod.Invoke(null, new object[] { lasso, stroke })!;
        Assert.That(isInside, Is.True, "Lasso drawn around an ellipse must successfully select the shape stroke.");
    }

    [Test]
    public void RectMarqueeContainsShapeSelectsStroke()
    {
        var points = (IReadOnlyList<Point>)BuildShapeOutlineMethod.Invoke(null, new object[] { ShapeKind.Rectangle, new Point(50, 50), new Point(100, 100) })!;
        var stylusPoints = new StylusPointCollection();
        foreach (var p in points)
            stylusPoints.Add(new StylusPoint(p.X, p.Y));
        var stroke = new Stroke(stylusPoints);

        var marquee = new Rect(40, 40, 70, 70);
        var isInside = (bool)IsStrokeInsideRectMethod.Invoke(null, new object[] { marquee, stroke })!;
        Assert.That(isInside, Is.True, "Rectangle marquee covering the shape must select it.");
    }
}
