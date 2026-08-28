using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using Caelum.Controls;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public sealed class RulerInteractionTests
{
    [Test]
    public void CrossingStrokeStopsAtNearRulerEdge()
    {
        var result = Constrain(CreateStroke(
            new Point(50, 20), new Point(50, 40), new Point(50, 60), new Point(50, 90)));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StylusPoints[^1].Y, Is.EqualTo(50).Within(0.01));
        Assert.That(result.StylusPoints.All(point => point.Y <= 50.01), Is.True,
            "Ink must not continue through the ruler body.");
    }

    [Test]
    public void StrokeStartingInsideRulerBodyIsRejected()
    {
        var result = Constrain(CreateStroke(new Point(50, 60), new Point(50, 85)));
        Assert.That(result, Is.Null, "A stroke initiated on the ruler body must not create ink.");
    }

    [Test]
    public void StrokeAlongNearestLongEdgeSnapsToThatEdge()
    {
        var result = Constrain(CreateStroke(
            new Point(20, 46), new Point(50, 45), new Point(80, 46)));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StylusPoints.All(point => Math.Abs(point.Y - 50) < 0.01), Is.True,
            "A stroke close to the top long edge must snap to that edge.");
    }

    [Test]
    public void StrokeStartingExactlyOnLongEdgeCanDrawAlongIt()
    {
        var result = Constrain(CreateStroke(
            new Point(20, 50), new Point(50, 50), new Point(80, 50)));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StylusPoints.All(point => Math.Abs(point.Y - 50) < 0.01), Is.True);
    }

    [Test]
    public void StrokeStartingOnEdgeAndTurningIntoBodyIsRejected()
    {
        var result = Constrain(CreateStroke(new Point(20, 50), new Point(20, 60)));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void CollectedCrossingStrokePublishesOneOrdinaryUndoableStroke()
    {
        EnsureWpfEnvironment();
        var page = new PdfPageControl { StrokeSmoothingLevel = 0 };
        page.SetInputMode(CustomInkInputProcessingMode.Inking);
        page.GetRulerGeometryInPageCoords = () => (
            new Point(0, 50), new Point(100, 50),
            new Point(0, 70), new Point(100, 70));
        var original = CreateStroke(
            new Point(50, 20), new Point(50, 40), new Point(50, 60), new Point(50, 90));
        page.InkCanvas.Strokes.Add(original);
        var published = new List<Stroke>();
        page.StrokeCollectedUndoable += (_, stroke) => published.Add(stroke);

        var method = typeof(PdfPageControl).GetMethod(
            "InkCanvas_StrokeCollected", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("PdfPageControl.InkCanvas_StrokeCollected was not found.");
        method.Invoke(page, new object[] { page, new InkCanvasStrokeCollectedEventArgs(original) });

        Assert.Multiple(() =>
        {
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(page.GetStrokes(), Has.Count.EqualTo(1));
            Assert.That(page.GetStrokes()[0].StylusPoints.All(point => point.Y <= 50.01), Is.True);
        });
    }

    private static Stroke? Constrain(Stroke stroke)
    {
        var method = typeof(PdfPageControl).GetMethod(
            "ConstrainStrokeToRuler", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "PdfPageControl must expose a pure ruler constraint seam.");
        return (Stroke?)method!.Invoke(null, new object[]
        {
            stroke,
            new Point(0, 50), new Point(100, 50),
            new Point(0, 70), new Point(100, 70)
        });
    }

    private static Stroke CreateStroke(params Point[] points)
    {
        var stylusPoints = new StylusPointCollection();
        foreach (var point in points)
            stylusPoints.Add(new StylusPoint(point.X, point.Y));
        return new Stroke(stylusPoints);
    }

    private static void EnsureWpfEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows");
    }
}
