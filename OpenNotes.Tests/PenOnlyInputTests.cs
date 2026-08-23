using Caelum.Controls;

namespace Caelum.Tests;

public class PenOnlyInputTests
{
    [Test]
    public void PenOnlyFilter_AppliesOnlyToFreehandAndShapeCreation()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PdfPageControl.IsPenOnlyInkCreationMode(CustomInkInputProcessingMode.Inking), Is.True);
            Assert.That(PdfPageControl.IsPenOnlyInkCreationMode(CustomInkInputProcessingMode.Shape), Is.True);
            Assert.That(PdfPageControl.IsPenOnlyInkCreationMode(CustomInkInputProcessingMode.HiddenInk), Is.False);
            Assert.That(PdfPageControl.IsPenOnlyInkCreationMode(CustomInkInputProcessingMode.Erasing), Is.False);
            Assert.That(PdfPageControl.IsPenOnlyInkCreationMode(CustomInkInputProcessingMode.None), Is.False);
        });
    }
}
