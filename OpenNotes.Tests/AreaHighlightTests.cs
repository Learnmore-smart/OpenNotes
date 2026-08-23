using System.Windows;
using Caelum.Controls;

namespace Caelum.Tests;

public class AreaHighlightTests
{
    [Test]
    public void NormalizeAreaHighlightRect_UsesTopLeftAndPositiveSize_ForReverseDrag()
    {
        var result = PdfPageControl.NormalizeAreaHighlightRect(
            new Point(80, 120),
            new Point(20, 40));

        Assert.That(result.X, Is.EqualTo(20));
        Assert.That(result.Y, Is.EqualTo(40));
        Assert.That(result.Width, Is.EqualTo(60));
        Assert.That(result.Height, Is.EqualTo(80));
    }
}
