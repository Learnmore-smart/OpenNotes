using Caelum.Models;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Linq;

namespace Caelum.Tests;

public sealed class ShapeStrokeMetadataTests
{
    [Test]
    public void BuildDashedLine_CreatesMultipleInkSegmentsSeparatedByRealGaps()
    {
        var parts = ShapeStrokeMetadata.BuildDashedLine(
            new Point(0, 0), new Point(100, 0), dashLength: 12, gapLength: 8);

        Assert.That(parts.Count, Is.GreaterThan(1));
        for (int index = 1; index < parts.Count; index++)
            Assert.That(parts[index][0].X - parts[index - 1][^1].X, Is.GreaterThan(0));
    }

    [Test]
    public void BuildDashedPolyline_CarriesDashPhaseAcrossCorners()
    {
        var parts = ShapeStrokeMetadata.BuildDashedPolyline(
            new[]
            {
                new Point(0, 0),
                new Point(15, 0),
                new Point(15, 20)
            },
            dashLength: 20,
            gapLength: 5);

        Assert.Multiple(() =>
        {
            Assert.That(parts, Has.Count.EqualTo(2));
            Assert.That(parts[0], Has.Count.EqualTo(3),
                "The first dash should continue around the corner instead of restarting.");
            Assert.That(parts[0][1], Is.EqualTo(new Point(15, 0)));
            Assert.That(parts[0][2], Is.EqualTo(new Point(15, 5)));
            Assert.That(parts[1][0], Is.EqualTo(new Point(15, 10)));
        });
    }

    [Test]
    public void ApplyAndRead_RoundTripsLogicalShapeIdentityOnWpfStroke()
    {
        var stroke = new Stroke(new StylusPointCollection
        {
            new StylusPoint(0, 0),
            new StylusPoint(20, 0)
        });

        ShapeStrokeMetadata.Apply(stroke, "group-1", "DashedLine", 3, isDashed: true);
        var metadata = ShapeStrokeMetadata.Read(stroke);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.GroupId, Is.EqualTo("group-1"));
            Assert.That(metadata.Kind, Is.EqualTo("DashedLine"));
            Assert.That(metadata.PartIndex, Is.EqualTo(3));
            Assert.That(metadata.IsDashed, Is.True);
        });
    }

    [Test]
    public void PixelEraseFragmentsRetainLogicalShapeIdentity()
    {
        var stroke = new Stroke(new StylusPointCollection
        {
            new StylusPoint(0, 0),
            new StylusPoint(100, 0)
        });
        ShapeStrokeMetadata.Apply(stroke, "group-erase", "DashedLine", 2, true);

        var fragments = stroke.GetEraseResult(
                new[] { new Point(50, -10), new Point(50, 10) },
                new RectangleStylusShape(8, 8))
            .Cast<Stroke>()
            .ToList();

        Assert.That(fragments, Is.Not.Empty);
        Assert.That(fragments.Select(ShapeStrokeMetadata.Read), Has.All.Matches<ShapeStrokeIdentity>(identity =>
            identity.GroupId == "group-erase" && identity.Kind == "DashedLine" && identity.PartIndex == 2 && identity.IsDashed));
    }
}
