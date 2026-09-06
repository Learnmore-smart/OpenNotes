using System.Windows;
using Caelum.Models;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
public sealed class AnnotationTransformTests
{
    [Test]
    public void RotatePointAroundCenterKeepsDistance()
    {
        var center = new Point(10, 10);
        var point = new Point(20, 10);

        var rotated = AnnotationTransform.RotatePoint(point, center, 90);

        Assert.That(rotated.X, Is.EqualTo(10).Within(0.001));
        Assert.That(rotated.Y, Is.EqualTo(20).Within(0.001));
    }

    [Test]
    public void NormalizeDegreesWrapsToSignedRange()
    {
        Assert.That(AnnotationTransform.NormalizeDegrees(270), Is.EqualTo(-90).Within(0.001));
        Assert.That(AnnotationTransform.NormalizeDegrees(-270), Is.EqualTo(90).Within(0.001));
        Assert.That(AnnotationTransform.NormalizeDegrees(0), Is.EqualTo(0));
    }

    [Test]
    public void TextImageAndStickyRotationDefaultToZero()
    {
        Assert.That(new TextAnnotation().RotationDegrees, Is.EqualTo(0));
        Assert.That(new ImageAnnotation().RotationDegrees, Is.EqualTo(0));
        Assert.That(new StickyNoteAnnotation().RotationDegrees, Is.EqualTo(0));
    }
}
