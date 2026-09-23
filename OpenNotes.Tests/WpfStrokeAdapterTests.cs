using System.Collections.Generic;
using System.Threading;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Caelum.Controls;
using Caelum.Models;
using NUnit.Framework;

namespace Caelum.Tests;

/// <summary>
/// Round-trip coverage for <see cref="WpfStrokeAdapter"/> — the only
/// interop layer between live WPF <see cref="Stroke"/> objects and the
/// UI-free <see cref="InkStrokeData"/> payloads the WinUI host consumes.
/// STA because the tests construct WPF ink objects, matching sibling
/// fixtures (<see cref="StrokeEraserGeometryTests"/>).
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class WpfStrokeAdapterTests
{
    private static Stroke MakeStroke(
        IEnumerable<(double x, double y, float pressure)> points,
        byte r = 10,
        byte g = 20,
        byte b = 30,
        byte a = 200,
        double size = 3.5,
        bool isHighlighter = false,
        bool fitToCurve = false)
    {
        var stylusPoints = new StylusPointCollection();
        foreach (var (x, y, pressure) in points)
            stylusPoints.Add(new StylusPoint(x, y, pressure));

        return new Stroke(stylusPoints)
        {
            DrawingAttributes = new DrawingAttributes
            {
                Color = Color.FromArgb(a, r, g, b),
                Width = size,
                Height = size,
                IsHighlighter = isHighlighter,
                FitToCurve = fitToCurve
            }
        };
    }

    [Test]
    public void OrdinaryStroke_RoundTripsThroughInkStrokeData()
    {
        var source = MakeStroke(
            new[]
            {
                (0.0, 0.0, 0.4f),
                (10.0, 5.0, 0.7f),
                (25.0, 2.0, 0.9f)
            },
            r: 200,
            g: 40,
            b: 60,
            a: 255,
            size: 4.0,
            isHighlighter: true,
            fitToCurve: true);

        var data = WpfStrokeAdapter.ToInkStrokeData(source);
        var roundTrip = WpfStrokeAdapter.ToStroke(data);

        Assert.Multiple(() =>
        {
            Assert.That(roundTrip, Is.Not.Null);
            Assert.That(roundTrip.StylusPoints, Has.Count.EqualTo(3));
            Assert.That(roundTrip.StylusPoints[1].X, Is.EqualTo(10.0));
            Assert.That(roundTrip.StylusPoints[1].Y, Is.EqualTo(5.0));
            Assert.That(roundTrip.StylusPoints[1].PressureFactor, Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(roundTrip.DrawingAttributes.Color, Is.EqualTo(Color.FromArgb(255, 200, 40, 60)));
            Assert.That(roundTrip.DrawingAttributes.Width, Is.EqualTo(4.0));
            Assert.That(roundTrip.DrawingAttributes.IsHighlighter, Is.True);
            Assert.That(roundTrip.DrawingAttributes.FitToCurve, Is.True);
            Assert.That(roundTrip.ContainsPropertyData(ShapeStrokeMetadataKeys.GroupId), Is.False,
                "Ordinary ink carries no shape identity.");
        });
    }

    [Test]
    public void SinglePointStroke_ExpandsToDotSegment()
    {
        var data = new InkStrokeData
        {
            Points = new List<InkPointData> { new(50.0, 60.0, 0.8f) },
            R = 5,
            G = 6,
            B = 7,
            A = 255,
            Size = 2.0
        };

        var stroke = WpfStrokeAdapter.ToStroke(data);

        Assert.Multiple(() =>
        {
            Assert.That(stroke, Is.Not.Null);
            Assert.That(stroke.StylusPoints, Has.Count.EqualTo(2),
                "A one-point payload must expand like AddStroke/PreserveTapStroke so WPF renders a dot.");
            Assert.That(stroke.StylusPoints[1].X, Is.EqualTo(50.1).Within(0.001));
            Assert.That(stroke.StylusPoints[1].Y, Is.EqualTo(60.0).Within(0.001));
            Assert.That(stroke.StylusPoints[1].PressureFactor, Is.EqualTo(0.8f).Within(0.001f));
        });
    }

    [Test]
    public void ShapeStrokeMetadata_RoundTripsThroughAdapter()
    {
        var source = MakeStroke(new[] { (0.0, 0.0, 0.5f), (10.0, 0.0, 0.5f) });
        ShapeStrokeMetadata.Apply(source, "group-1", "Arrow", 2, isDashed: true);

        var data = WpfStrokeAdapter.ToInkStrokeData(source);
        Assert.Multiple(() =>
        {
            Assert.That(data.ShapeGroupId, Is.EqualTo("group-1"));
            Assert.That(data.ShapeKind, Is.EqualTo("Arrow"));
            Assert.That(data.ShapePartIndex, Is.EqualTo(2));
            Assert.That(data.IsDashedShape, Is.True);
        });

        var roundTrip = WpfStrokeAdapter.ToStroke(data);
        var identity = ShapeStrokeMetadata.Read(roundTrip);
        Assert.Multiple(() =>
        {
            Assert.That(identity.GroupId, Is.EqualTo("group-1"));
            Assert.That(identity.Kind, Is.EqualTo("Arrow"));
            Assert.That(identity.PartIndex, Is.EqualTo(2));
            Assert.That(identity.IsDashed, Is.True);
            Assert.That(roundTrip.DrawingAttributes.IgnorePressure, Is.True,
                "Shape parts use uniform width.");
        });
    }

    [Test]
    public void NullOrEmptyPoints_ReturnNullStroke()
    {
        var nullPoints = new InkStrokeData { Points = null };
        var emptyPoints = new InkStrokeData { Points = new List<InkPointData>() };

        Assert.Multiple(() =>
        {
            Assert.That(WpfStrokeAdapter.ToStroke(nullPoints), Is.Null);
            Assert.That(WpfStrokeAdapter.ToStroke(emptyPoints), Is.Null);
            Assert.That(WpfStrokeAdapter.ToStroke(null), Is.Null);
        });
    }

    [Test]
    public void ToInkShapeKind_ThrowsOnUnrecognizedKind()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WpfStrokeAdapter.ToInkShapeKind(ShapeKind.Line),
                Is.EqualTo(Caelum.InkGeometry.InkShapeKind.Line));
            Assert.That(WpfStrokeAdapter.ToInkShapeKind(ShapeKind.Hexagon),
                Is.EqualTo(Caelum.InkGeometry.InkShapeKind.Hexagon));
            Assert.That(
                () => WpfStrokeAdapter.ToInkShapeKind((ShapeKind)999),
                Throws.InstanceOf<System.ArgumentOutOfRangeException>(),
                "A new ShapeKind member must fail loud, not silently degrade to Line.");
        });
    }
}
