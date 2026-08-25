using System.Reflection;
using System.Windows;
using Caelum.Controls;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
public sealed class ShapeToolTests
{
    private static readonly MethodInfo BuildShapeOutlineMethod =
        typeof(PdfPageControl).GetMethod("BuildShapeOutline", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.BuildShapeOutline was not found.");

    [TestCase("Triangle", 4)]
    [TestCase("Diamond", 5)]
    [TestCase("Parallelogram", 5)]
    [TestCase("Pentagon", 6)]
    [TestCase("Hexagon", 7)]
    public void AdditionalPolygonShapesProduceClosedBoundedOutlines(string kindName, int expectedPointCount)
    {
        Assert.That(Enum.TryParse(kindName, out ShapeKind kind), Is.True,
            $"ShapeKind.{kindName} must be available to the production picker.");

        var start = new Point(10, 20);
        var end = new Point(110, 100);
        var points = InvokeBuildShapeOutline(kind, start, end);

        Assert.Multiple(() =>
        {
            Assert.That(points, Has.Count.EqualTo(expectedPointCount));
            Assert.That(points[0], Is.EqualTo(points[^1]), $"{kindName} must be closed.");
            Assert.That(points.All(point => point.X >= start.X && point.X <= end.X), Is.True,
                $"{kindName} must stay inside the horizontal drag bounds.");
            Assert.That(points.All(point => point.Y >= start.Y && point.Y <= end.Y), Is.True,
                $"{kindName} must stay inside the vertical drag bounds.");
            Assert.That(points.Distinct().Count(), Is.EqualTo(expectedPointCount - 1));
        });
    }

    [Test]
    public void TriangleAndParallelogramUseRecognizableGeometry()
    {
        Assert.That(Enum.TryParse("Triangle", out ShapeKind triangle), Is.True);
        Assert.That(Enum.TryParse("Parallelogram", out ShapeKind parallelogram), Is.True);

        var trianglePoints = InvokeBuildShapeOutline(triangle, new Point(0, 0), new Point(100, 80));
        var parallelogramPoints = InvokeBuildShapeOutline(parallelogram, new Point(0, 0), new Point(100, 80));

        Assert.Multiple(() =>
        {
            Assert.That(trianglePoints[0], Is.EqualTo(new Point(50, 0)), "Triangle starts at its top apex.");
            Assert.That(trianglePoints[1], Is.EqualTo(new Point(100, 80)));
            Assert.That(trianglePoints[2], Is.EqualTo(new Point(0, 80)));
            Assert.That(parallelogramPoints[0].X, Is.GreaterThan(0), "Top-left corner is slanted inward.");
            Assert.That(parallelogramPoints[1], Is.EqualTo(new Point(100, 0)));
            Assert.That(parallelogramPoints[2].X, Is.LessThan(100), "Bottom-right corner is slanted inward.");
            Assert.That(parallelogramPoints[3], Is.EqualTo(new Point(0, 80)));
        });
    }

    private static IReadOnlyList<Point> InvokeBuildShapeOutline(ShapeKind kind, Point start, Point end) =>
        (IReadOnlyList<Point>)(BuildShapeOutlineMethod.Invoke(null, new object[] { kind, start, end })
            ?? throw new InvalidOperationException("BuildShapeOutline returned null."));
}
