using Caelum.Models;
using Caelum.Services;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using System.IO;

namespace Caelum.Tests;

public class PdfServicePageEditingTests
{
    private string _tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "CaelumTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }

    [Test]
    public async Task InsertPageAsync_InsertsAtRequestedIndex_AndKeepsNeighborPageSizes()
    {
        string filePath = Path.Combine(_tempDirectory, "insert.pdf");
        CreatePdf(filePath, (200, 300), (410, 520));

        var service = new PdfService();

        await service.InsertPageAsync(filePath, 1, PageInsertTemplate.Notebook);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);

        Assert.That(document.PageCount, Is.EqualTo(3));
        Assert.That(document.Pages[0].Width.Point, Is.EqualTo(200).Within(0.01));
        Assert.That(document.Pages[1].Width.Point, Is.EqualTo(410).Within(0.01));
        Assert.That(document.Pages[1].Height.Point, Is.EqualTo(520).Within(0.01));
        Assert.That(document.Pages[2].Width.Point, Is.EqualTo(410).Within(0.01));
    }

    [Test]
    public async Task DeletePageAsync_RemovesRequestedPage_AndLeavesRemainingPages()
    {
        string filePath = Path.Combine(_tempDirectory, "delete.pdf");
        CreatePdf(filePath, (210, 320), (330, 440), (470, 580));

        var service = new PdfService();

        await service.DeletePageAsync(filePath, 1);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);

        Assert.That(document.PageCount, Is.EqualTo(2));
        Assert.That(document.Pages[0].Width.Point, Is.EqualTo(210).Within(0.01));
        Assert.That(document.Pages[1].Width.Point, Is.EqualTo(470).Within(0.01));
        Assert.That(document.Pages[1].Height.Point, Is.EqualTo(580).Within(0.01));
    }

    [Test]
    public void DeletePageAsync_Throws_WhenRemovingTheLastPage()
    {
        string filePath = Path.Combine(_tempDirectory, "single.pdf");
        CreatePdf(filePath, (200, 300));

        var service = new PdfService();

        Assert.That(async () => await service.DeletePageAsync(filePath, 0), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task CreateBlankPdfAsync_CreatesSinglePageUsingRequestedTemplate()
    {
        string filePath = Path.Combine(_tempDirectory, "created.pdf");

        await PdfService.CreateBlankPdfAsync(filePath, widthPoints: 320, heightPoints: 500, template: PageInsertTemplate.Notebook);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);

        Assert.That(document.PageCount, Is.EqualTo(1));
        Assert.That(document.Pages[0].Width.Point, Is.EqualTo(320).Within(0.01));
        Assert.That(document.Pages[0].Height.Point, Is.EqualTo(500).Within(0.01));
        Assert.That(document.Pages[0].Contents.Elements.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task ReorderPagesAsync_MovesRequestedPageAndPreservesSizes()
    {
        string filePath = Path.Combine(_tempDirectory, "reorder.pdf");
        CreatePdf(filePath, (200, 300), (410, 520), (620, 730));

        await new PdfService().ReorderPagesAsync(filePath, 2, 0);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(document.PageCount, Is.EqualTo(3));
        Assert.That(document.Pages[0].Width.Point, Is.EqualTo(620).Within(0.01));
        Assert.That(document.Pages[1].Width.Point, Is.EqualTo(200).Within(0.01));
        Assert.That(document.Pages[2].Width.Point, Is.EqualTo(410).Within(0.01));
    }

    [Test]
    public async Task ReorderPagesAsync_MovesForwardPageAndPreservesPageOrder()
    {
        string filePath = Path.Combine(_tempDirectory, "reorder-forward.pdf");
        CreatePdf(filePath, (210, 310), (320, 420), (430, 530));

        await new PdfService().ReorderPagesAsync(filePath, fromIndex: 0, toIndex: 2);

        AssertPageWidths(filePath, 320, 430, 210);
    }

    [Test]
    public async Task ReorderPagesAsync_MovesBackwardPageAndPreservesPageOrder()
    {
        string filePath = Path.Combine(_tempDirectory, "reorder-backward.pdf");
        CreatePdf(filePath, (210, 310), (320, 420), (430, 530));

        await new PdfService().ReorderPagesAsync(filePath, fromIndex: 2, toIndex: 0);

        AssertPageWidths(filePath, 430, 210, 320);
    }

    [Test]
    public async Task ReorderPagesAsync_MovesPageToEndAndPreservesPageOrder()
    {
        string filePath = Path.Combine(_tempDirectory, "reorder-end.pdf");
        CreatePdf(filePath, (210, 310), (320, 420), (430, 530), (540, 640));

        await new PdfService().ReorderPagesAsync(filePath, fromIndex: 0, toIndex: 4);

        AssertPageWidths(filePath, 320, 430, 540, 210);
    }

    [Test]
    public async Task DuplicatePageAsync_AddsCopyAfterRequestedPage()
    {
        string filePath = Path.Combine(_tempDirectory, "duplicate.pdf");
        CreatePdf(filePath, (200, 300), (410, 520));

        await new PdfService().DuplicatePageAsync(filePath, 0);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(document.PageCount, Is.EqualTo(3));
        Assert.That(document.Pages[1].Width.Point, Is.EqualTo(200).Within(0.01));
        Assert.That(document.Pages[1].Height.Point, Is.EqualTo(300).Within(0.01));
    }

    [Test]
    public async Task RotatePageAsync_PersistsQuarterTurnMetadata()
    {
        string filePath = Path.Combine(_tempDirectory, "rotate.pdf");
        CreatePdf(filePath, (200, 300));

        await new PdfService().RotatePageAsync(filePath, 0);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(document.Pages[0].Elements.GetInteger("/Rotate"), Is.EqualTo(90));
    }

    [Test]
    public async Task RotatePageAsync_SwapsDisplayAspectAndKeepsOwnedDrawingAttachedToPage()
    {
        string filePath = Path.Combine(_tempDirectory, "rotate-drawing.pdf");
        CreatePdf(filePath, (200, 300));
        var annotations = new Dictionary<int, PageAnnotation>
        {
            [0] = new PageAnnotation
            {
                Strokes =
                {
                    new StrokeAnnotation
                    {
                        R = 20,
                        G = 80,
                        B = 220,
                        FitToCurve = false,
                        ShapeGroupId = "shape-rotate",
                        ShapeKind = "rectangle",
                        Points =
                        {
                            new[] { 10d, 20d },
                            new[] { 50d, 80d }
                        }
                    }
                },
                HiddenInks =
                {
                    new HiddenInkAnnotation
                    {
                        Id = "hidden-rotate",
                        Points =
                        {
                            new[] { 20d, 30d },
                            new[] { 60d, 90d }
                        }
                    }
                }
            }
        };

        var writer = new PdfService();
        var rotated = new PdfService();
        var reloaded = new PdfService();
        try
        {
            await writer.SaveAnnotationsToPdfAsync(filePath, annotations);
            await writer.RotatePageAsync(filePath, 0);
            await rotated.LoadPdfAsync(filePath);

            var (width, height) = rotated.GetPageSizeInDips(0);
            var displayedStroke = rotated.ExtractedAnnotations[0].Strokes.Single();
            var displayedHiddenInk = rotated.ExtractedAnnotations[0].HiddenInks.Single();
            Assert.Multiple(() =>
            {
                Assert.That(width, Is.EqualTo(400).Within(0.75),
                    "A 200x300-point page rotated 90 degrees must display landscape width.");
                Assert.That(height, Is.EqualTo(200 * 96d / 72d).Within(0.75));
                Assert.That(displayedStroke.Points[0][0], Is.EqualTo(380).Within(0.01));
                Assert.That(displayedStroke.Points[0][1], Is.EqualTo(10).Within(0.01));
                Assert.That(displayedStroke.Points[1][0], Is.EqualTo(320).Within(0.01));
                Assert.That(displayedStroke.Points[1][1], Is.EqualTo(50).Within(0.01));
                Assert.That(displayedStroke.ShapeGroupId, Is.EqualTo("shape-rotate"));
                Assert.That(displayedStroke.ShapeKind, Is.EqualTo("rectangle"));
                Assert.That(displayedHiddenInk.Points[0][0], Is.EqualTo(370).Within(0.01));
                Assert.That(displayedHiddenInk.Points[0][1], Is.EqualTo(20).Within(0.01));
                Assert.That(displayedHiddenInk.Points[1][0], Is.EqualTo(310).Within(0.01));
                Assert.That(displayedHiddenInk.Points[1][1], Is.EqualTo(60).Within(0.01));
            });

            await rotated.SaveAnnotationsToPdfAsync(filePath, rotated.ExtractedAnnotations);
            await reloaded.LoadPdfAsync(filePath);
            var stableStroke = reloaded.ExtractedAnnotations[0].Strokes.Single();
            var stableHiddenInk = reloaded.ExtractedAnnotations[0].HiddenInks.Single();
            Assert.That(stableStroke.Points[0][0], Is.EqualTo(380).Within(0.01));
            Assert.That(stableStroke.Points[0][1], Is.EqualTo(10).Within(0.01));
            Assert.That(stableStroke.Points[1][0], Is.EqualTo(320).Within(0.01));
            Assert.That(stableStroke.Points[1][1], Is.EqualTo(50).Within(0.01),
                "Saving a rotated page must inverse-transform the displayed drawing exactly once.");
            Assert.That(stableHiddenInk.Points[0][0], Is.EqualTo(370).Within(0.01));
            Assert.That(stableHiddenInk.Points[0][1], Is.EqualTo(20).Within(0.01));
            Assert.That(stableHiddenInk.Points[1][0], Is.EqualTo(310).Within(0.01));
            Assert.That(stableHiddenInk.Points[1][1], Is.EqualTo(60).Within(0.01));
        }
        finally
        {
            await reloaded.DisposeAsync();
            await rotated.DisposeAsync();
            await writer.DisposeAsync();
        }
    }

    [Test]
    public async Task RotatePageAsync_MapsDrawingThroughEveryQuarterTurnWithoutDrift()
    {
        for (int quarterTurns = 1; quarterTurns <= 3; quarterTurns++)
        {
            string filePath = Path.Combine(_tempDirectory, $"rotate-{quarterTurns}-turns.pdf");
            CreatePdf(filePath, (200, 300));
            var annotations = new Dictionary<int, PageAnnotation>
            {
                [0] = new PageAnnotation
                {
                    Strokes =
                    {
                        new StrokeAnnotation
                        {
                            Points =
                            {
                                new[] { 10d, 20d },
                                new[] { 50d, 80d }
                            }
                        }
                    }
                }
            };

            var writer = new PdfService();
            var rotated = new PdfService();
            var reloaded = new PdfService();
            try
            {
                await writer.SaveAnnotationsToPdfAsync(filePath, annotations);
                await writer.RotatePageAsync(filePath, 0, quarterTurns);
                await rotated.LoadPdfAsync(filePath);

                var (width, height) = rotated.GetPageSizeInDips(0);
                double portraitWidth = 200d * 96d / 72d;
                double portraitHeight = 300d * 96d / 72d;
                Assert.Multiple(() =>
                {
                    Assert.That(width, Is.EqualTo(quarterTurns % 2 == 0 ? portraitWidth : portraitHeight).Within(0.75));
                    Assert.That(height, Is.EqualTo(quarterTurns % 2 == 0 ? portraitHeight : portraitWidth).Within(0.75));
                });

                double[][] expected = quarterTurns switch
                {
                    1 => new[] { new[] { 380d, 10d }, new[] { 320d, 50d } },
                    2 => new[] { new[] { 200d * 96d / 72d - 10d, 380d }, new[] { 200d * 96d / 72d - 50d, 320d } },
                    _ => new[] { new[] { 20d, 200d * 96d / 72d - 10d }, new[] { 80d, 200d * 96d / 72d - 50d } }
                };

                var displayedStroke = rotated.ExtractedAnnotations[0].Strokes.Single();
                AssertStrokePoints(displayedStroke, expected, $"quarter turn {quarterTurns}");

                await rotated.SaveAnnotationsToPdfAsync(filePath, rotated.ExtractedAnnotations);
                await reloaded.LoadPdfAsync(filePath);
                AssertStrokePoints(
                    reloaded.ExtractedAnnotations[0].Strokes.Single(),
                    expected,
                    $"quarter turn {quarterTurns} after save/reload");
            }
            finally
            {
                await reloaded.DisposeAsync();
                await rotated.DisposeAsync();
                await writer.DisposeAsync();
            }
        }
    }

    private static void AssertStrokePoints(StrokeAnnotation stroke, double[][] expected, string message)
    {
        Assert.That(stroke.Points, Has.Count.EqualTo(expected.Length), message);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.That(stroke.Points[index][0], Is.EqualTo(expected[index][0]).Within(0.01), message);
            Assert.That(stroke.Points[index][1], Is.EqualTo(expected[index][1]).Within(0.01), message);
        }
    }

    [Test]
    public async Task InsertPdfPagesAsync_InsertsInclusiveSourceRange()
    {
        string targetPath = Path.Combine(_tempDirectory, "target.pdf");
        string sourcePath = Path.Combine(_tempDirectory, "source.pdf");
        CreatePdf(targetPath, (200, 300));
        CreatePdf(sourcePath, (410, 520), (620, 730), (830, 940));

        await new PdfService().InsertPdfPagesAsync(targetPath, sourcePath, 1, 1, 2);

        using var document = PdfReader.Open(targetPath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(document.PageCount, Is.EqualTo(3));
        Assert.That(document.Pages[0].Width.Point, Is.EqualTo(200).Within(0.01));
        Assert.That(document.Pages[1].Width.Point, Is.EqualTo(620).Within(0.01));
        Assert.That(document.Pages[2].Width.Point, Is.EqualTo(830).Within(0.01));
    }

    [TestCase(PageInsertTemplate.Dotted)]
    [TestCase(PageInsertTemplate.Music)]
    [TestCase(PageInsertTemplate.Cornell)]
    [TestCase(PageInsertTemplate.Checklist)]
    [TestCase(PageInsertTemplate.TwoColumn)]
    public async Task CreateBlankPdfAsync_NewTemplatesWriteVectorContent(PageInsertTemplate template)
    {
        string filePath = Path.Combine(_tempDirectory, $"{template}.pdf");

        await PdfService.CreateBlankPdfAsync(filePath, template: template);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(document.PageCount, Is.EqualTo(1));
        Assert.That(document.Pages[0].Contents.Elements.Count, Is.GreaterThan(0));
    }

    private static void CreatePdf(string filePath, params (double Width, double Height)[] pageSizes)
    {
        using var document = new PdfDocument();

        foreach (var (width, height) in pageSizes)
        {
            var page = document.AddPage();
            page.Width = width;
            page.Height = height;
        }

        document.Save(filePath);
    }

    private static void AssertPageWidths(string filePath, params double[] expectedWidths)
    {
        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);

        Assert.That(document.PageCount, Is.EqualTo(expectedWidths.Length));
        var actualWidths = Enumerable.Range(0, document.PageCount)
            .Select(index => document.Pages[index].Width.Point)
            .ToArray();

        Assert.That(actualWidths, Is.EqualTo(expectedWidths).Within(0.01));
    }
}
