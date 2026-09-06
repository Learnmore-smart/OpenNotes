using System.Windows;
using Caelum.Controls;

namespace Caelum.Tests;

[Apartment(System.Threading.ApartmentState.STA)]
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

    [Test]
    public void HighlighterPreviewStrokeThickness_NeverBlobs_AcrossEntireSliderRange()
    {
        var helper = typeof(Caelum.Pages.EditorPage).GetMethod(
            "GetHighlighterPreviewStrokeThickness",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.That(helper, Is.Not.Null);

        var modeType = typeof(Caelum.Pages.EditorPage).GetNestedType(
            "HighlighterApplyMode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.That(modeType, Is.Not.Null);

        double[] testSizes = [2.0, 5.0, 8.0, 16.0, 24.0, 36.0, 48.0];

        foreach (var modeName in Enum.GetNames(modeType!))
        {
            var mode = Enum.Parse(modeType, modeName);
            foreach (var size in testSizes)
            {
                var thickness = (double)helper!.Invoke(null, [mode, size])!;
                Assert.That(thickness, Is.GreaterThanOrEqualTo(1.0), $"{modeName} at size {size} is too thin.");
                Assert.That(thickness, Is.LessThanOrEqualTo(3.0), $"{modeName} at size {size} is too thick and causes blob regression.");
            }
        }
    }

    [Test]
    public void BuildHighlighterModePreview_CreatesDistinctValidGeometries_ForAllSixModes()
    {
        var helper = typeof(Caelum.Pages.EditorPage).GetMethod(
            "BuildHighlighterModePreview",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.That(helper, Is.Not.Null);

        var modeType = typeof(Caelum.Pages.EditorPage).GetNestedType(
            "HighlighterApplyMode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.That(modeType, Is.Not.Null);

        var geometries = new HashSet<string>();

        foreach (var modeName in Enum.GetNames(modeType!))
        {
            var mode = Enum.Parse(modeType, modeName);
            var path = (System.Windows.Shapes.Path)helper!.Invoke(null, [mode])!;
            Assert.That(path, Is.Not.Null, modeName);
            Assert.That(path.Data, Is.Not.Null, modeName);

            string dataString = path.Data.ToString();
            Assert.That(string.IsNullOrWhiteSpace(dataString), Is.False, modeName);
            Assert.That(geometries.Add(dataString), Is.True, $"Duplicate geometry found for mode {modeName}");
        }

        Assert.That(geometries.Count, Is.EqualTo(6));
    }

    [Test]
    public void AddHighlightAnnotation_And_RemoveHighlight_MaintainsVisualAndDataIntegrity()
    {
        var page = new PdfPageControl();
        var rects = new[]
        {
            new Rect(10, 20, 100, 25),
            new Rect(10, 50, 80, 25)
        };

        var annot = page.AddHighlightAnnotation(rects, System.Windows.Media.Colors.Yellow);
        Assert.That(annot, Is.Not.Null);
        Assert.That(page.GetHighlights().Count, Is.EqualTo(1));
        Assert.That(page.GetHighlights()[0], Is.SameAs(annot));
        Assert.That(page.HighlightsCanvas.Children.Count, Is.EqualTo(2));

        // Remove (Undo)
        page.RemoveHighlight(annot);
        Assert.That(page.GetHighlights().Count, Is.EqualTo(0));
        Assert.That(page.HighlightsCanvas.Children.Count, Is.EqualTo(0));

        // Re-add (Redo)
        page.AddHighlight(annot);
        Assert.That(page.GetHighlights().Count, Is.EqualTo(1));
        Assert.That(page.HighlightsCanvas.Children.Count, Is.EqualTo(2));
    }

    [Test]
    public void IsHighlighterTool_RecognizesAllHighlighterSubModes()
    {
        var helper = typeof(Caelum.Pages.EditorPage).GetMethod(
            "IsHighlighterTool",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.That(helper, Is.Not.Null);

        var toolType = typeof(Caelum.Pages.EditorPage).GetNestedType(
            "ToolType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.That(toolType, Is.Not.Null);

        var highlighter = Enum.Parse(toolType!, "Highlighter");
        var textHighlight = Enum.Parse(toolType!, "TextHighlight");
        var areaHighlight = Enum.Parse(toolType!, "AreaHighlight");
        var pen = Enum.Parse(toolType!, "Pen");
        var eraser = Enum.Parse(toolType!, "Eraser");
        var none = Enum.Parse(toolType!, "None");

        Assert.That((bool)helper!.Invoke(null, [highlighter])!, Is.True);
        Assert.That((bool)helper!.Invoke(null, [textHighlight])!, Is.True);
        Assert.That((bool)helper!.Invoke(null, [areaHighlight])!, Is.True);

        Assert.That((bool)helper!.Invoke(null, [pen])!, Is.False);
        Assert.That((bool)helper!.Invoke(null, [eraser])!, Is.False);
        Assert.That((bool)helper!.Invoke(null, [none])!, Is.False);
    }
}
