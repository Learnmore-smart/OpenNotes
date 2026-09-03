using Caelum.Models;
using Caelum.Services;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Caelum.Tests;

public class PdfServiceAnnotationSavingTests
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
    public async Task SaveAnnotationsToPdfAsync_SavesAnnotationsWithoutEOFError()
    {
        // Create a test PDF
        string filePath = Path.Combine(_tempDirectory, "test.pdf");
        CreateTestPdf(filePath);

        // Create annotations to save
        var annotations = new Dictionary<int, PageAnnotation>();
        var pageAnnots = new PageAnnotation();

        // Add a text annotation
        pageAnnots.Texts.Add(new TextAnnotation
        {
            Text = "Test annotation",
            X = 100,
            Y = 100,
            FontSize = 12,
            R = 0,
            G = 0,
            B = 0
        });

        annotations[0] = pageAnnots;

        // Save annotations - this should not throw an exception
        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(filePath, annotations);

        // Verify the PDF can be opened again
        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(document.PageCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SaveReload_CrispRectanglePreservesFitToCurveFalse()
    {
        string filePath = Path.Combine(_tempDirectory, "crisp-rectangle.pdf");
        CreateTestPdf(filePath);

        var rectangle = CreateCrispRectangleStroke();
        var writer = new PdfService();
        await writer.SaveAnnotationsToPdfAsync(
            filePath,
            new Dictionary<int, PageAnnotation>
            {
                [0] = new PageAnnotation { Strokes = new List<StrokeAnnotation> { rectangle } }
            });
        await writer.DisposeAsync();

        var reader = new PdfService();
        await reader.LoadPdfAsync(filePath);

        Assert.That(reader.ExtractedAnnotations[0].Strokes.Single().FitToCurve, Is.False,
            "a saved rectangle must not become a smoothed/circular stroke after reload");
        await reader.DisposeAsync();
    }

    [Test]
    public async Task SaveReload_LogicalShapeMetadataRoundTripsOnOwnedInk()
    {
        string filePath = Path.Combine(_tempDirectory, "logical-shape.pdf");
        CreateTestPdf(filePath);
        var stroke = CreateCrispRectangleStroke();
        stroke.ShapeGroupId = "shape-group-42";
        stroke.ShapeKind = "DashedLine";
        stroke.ShapePartIndex = 4;
        stroke.IsDashedShape = true;

        var writer = new PdfService();
        await writer.SaveAnnotationsToPdfAsync(
            filePath,
            new Dictionary<int, PageAnnotation>
            {
                [0] = new PageAnnotation { Strokes = new List<StrokeAnnotation> { stroke } }
            });
        await writer.DisposeAsync();

        var reader = new PdfService();
        await reader.LoadPdfAsync(filePath);
        var loaded = reader.ExtractedAnnotations[0].Strokes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.ShapeGroupId, Is.EqualTo("shape-group-42"));
            Assert.That(loaded.ShapeKind, Is.EqualTo("DashedLine"));
            Assert.That(loaded.ShapePartIndex, Is.EqualTo(4));
            Assert.That(loaded.IsDashedShape, Is.True);
        });
        await reader.DisposeAsync();
    }

    [Test]
    public async Task LoadPdfAsync_LegacyOwnedRectangleWithoutFitMetadataRecoversAsCrispShape()
    {
        string filePath = Path.Combine(_tempDirectory, "legacy-crisp-rectangle.pdf");
        CreateTestPdf(filePath);

        var writer = new PdfService();
        await writer.SaveAnnotationsToPdfAsync(
            filePath,
            new Dictionary<int, PageAnnotation>
            {
                [0] = new PageAnnotation
                {
                    Strokes = new List<StrokeAnnotation> { CreateCrispRectangleStroke() }
                }
            });
        await writer.DisposeAsync();

        // Simulate a PDF produced before FitToCurve metadata existed.
        using (var legacyDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify))
        {
            var annots = legacyDocument.Pages[0].Elements.GetArray("/Annots")!;
            var ownedInk = annots.Elements
                .Select(GetAnnotationDictionary)
                .Single(dict => dict.Elements.GetName("/Subtype") == "/Ink" &&
                    dict.Elements.GetString("/NM").StartsWith("wna_ink_", StringComparison.Ordinal));
            ownedInk.Elements.Remove("/WNAFitToCurve");
            legacyDocument.Save(filePath);
        }

        var reader = new PdfService();
        await reader.LoadPdfAsync(filePath);

        Assert.That(reader.ExtractedAnnotations[0].Strokes.Single().FitToCurve, Is.False,
            "legacy closed rectangle vertices should recover as a straight-edged shape");
        await reader.DisposeAsync();
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_PreservesExplicitTextBoxRectangle()
    {
        string filePath = Path.Combine(_tempDirectory, "sized-text.pdf");
        CreateTestPdf(filePath);

        var pageAnnots = new PageAnnotation();
        pageAnnots.Texts.Add(new TextAnnotation
        {
            Text = "A deliberately sized text box",
            X = 96,
            Y = 144,
            Width = 240,
            Height = 96,
            FontSize = 16
        });

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(
            filePath,
            new Dictionary<int, PageAnnotation> { [0] = pageAnnots });

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var annots = document.Pages[0].Elements.GetArray("/Annots");
        Assert.That(annots, Is.Not.Null);

        var textAnnot = GetAnnotationDictionary(annots!.Elements.Single());
        var rect = textAnnot.Elements.GetRectangle("/Rect");
        Assert.That(rect.X1, Is.EqualTo(72d).Within(0.01));
        Assert.That(rect.Y1, Is.EqualTo(612d).Within(0.01));
        Assert.That(rect.Width, Is.EqualTo(180d).Within(0.01));
        Assert.That(rect.Height, Is.EqualTo(72d).Within(0.01));

        var reloaded = PdfService.TryExtractFreeTextAnnotation(textAnnot, 792d, 96d / 72d);
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.X, Is.EqualTo(96d).Within(0.01));
        Assert.That(reloaded.Y, Is.EqualTo(144d).Within(0.01));
        Assert.That(reloaded.Width, Is.EqualTo(240d).Within(0.01));
        Assert.That(reloaded.Height, Is.EqualTo(96d).Within(0.01));
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_PreservesAutomaticTextBoxDimensions()
    {
        string filePath = Path.Combine(_tempDirectory, "automatic-text.pdf");
        CreateTestPdf(filePath);

        var pageAnnots = new PageAnnotation();
        pageAnnots.Texts.Add(new TextAnnotation
        {
            Text = "Automatic text box",
            X = 96,
            Y = 144,
            FontSize = 16
        });

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(
            filePath,
            new Dictionary<int, PageAnnotation> { [0] = pageAnnots });

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var annots = document.Pages[0].Elements.GetArray("/Annots");
        Assert.That(annots, Is.Not.Null);

        var textAnnot = GetAnnotationDictionary(annots!.Elements.Single());
        Assert.That(textAnnot.Elements.GetInteger("/WNAutoWidth"), Is.EqualTo(1));
        Assert.That(textAnnot.Elements.GetInteger("/WNAutoHeight"), Is.EqualTo(1));

        var reloaded = PdfService.TryExtractFreeTextAnnotation(textAnnot, 792d, 96d / 72d);
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.Width, Is.Zero);
        Assert.That(reloaded.Height, Is.Zero);
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_WritesPrintableAppearanceStreams_ForAllAnnotationTypes()
    {
        string filePath = Path.Combine(_tempDirectory, "printable.pdf");
        CreateTestPdf(filePath);

        var annotations = new Dictionary<int, PageAnnotation>();
        var pageAnnots = new PageAnnotation();
        pageAnnots.Texts.Add(new TextAnnotation
        {
            Text = "Printable text",
            X = 100,
            Y = 100,
            FontSize = 16,
            R = 10,
            G = 20,
            B = 30
        });
        pageAnnots.Strokes.Add(new StrokeAnnotation
        {
            R = 20,
            G = 40,
            B = 60,
            Size = 4,
            Points = new List<double[]>
            {
                new[] { 96d, 120d },
                new[] { 144d, 168d }
            }
        });
        pageAnnots.Highlights.Add(new HighlightAnnotation
        {
            R = 255,
            G = 235,
            B = 59,
            A = 128,
            Rects = new List<double[]>
            {
                new[] { 120d, 200d, 60d, 24d }
            }
        });
        annotations[0] = pageAnnots;

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(filePath, annotations);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var annots = document.Pages[0].Elements.GetArray("/Annots");
        Assert.That(annots, Is.Not.Null);
        Assert.That(annots!.Elements.Count, Is.EqualTo(3));

        var dictionaries = annots.Elements.Select(GetAnnotationDictionary).ToList();

        var textAnnot = dictionaries.Single(dict => dict.Elements.GetName("/Subtype") == "/FreeText");
        Assert.That(textAnnot.Elements.GetName("/Type"), Is.EqualTo("/Annot"));
        Assert.That(textAnnot.Elements.GetInteger("/F") & 4, Is.EqualTo(4));
        Assert.That(textAnnot.Elements.GetDictionary("/AP"), Is.Not.Null);

        var inkAnnot = dictionaries.Single(dict => dict.Elements.GetName("/Subtype") == "/Ink");
        Assert.That(inkAnnot.Elements.GetInteger("/F") & 4, Is.EqualTo(4));
        Assert.That(inkAnnot.Elements.GetDictionary("/AP"), Is.Not.Null);

        var highlightAnnot = dictionaries.Single(dict => dict.Elements.GetName("/Subtype") == "/Highlight");
        Assert.That(highlightAnnot.Elements.GetInteger("/F") & 4, Is.EqualTo(4));
        Assert.That(highlightAnnot.Elements.GetDictionary("/AP"), Is.Not.Null);

        var highlightRect = highlightAnnot.Elements.GetRectangle("/Rect");
        Assert.That(highlightRect.X1, Is.EqualTo(90d).Within(0.01));
        Assert.That(highlightRect.Y1, Is.EqualTo(624d).Within(0.01));
        Assert.That(highlightRect.Width, Is.EqualTo(45d).Within(0.01));
        Assert.That(highlightRect.Height, Is.EqualTo(18d).Within(0.01));
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_WritesHiddenInkAsOpaqueOwnedInkAndParsesItBack()
    {
        string filePath = Path.Combine(_tempDirectory, "hidden-ink.pdf");
        CreateTestPdf(filePath);

        var hidden = new HiddenInkAnnotation
        {
            Id = "study-answer",
            R = 250,
            G = 248,
            B = 240,
            A = 255,
            Size = 24,
            RevealDurationMs = 4250,
            Points = new List<double[]>
            {
                new[] { 96d, 120d },
                new[] { 144d, 120d }
            }
        };

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(
            filePath,
            new Dictionary<int, PageAnnotation>
            {
                [0] = new PageAnnotation { HiddenInks = new List<HiddenInkAnnotation> { hidden } }
            });

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var annots = document.Pages[0].Elements.GetArray("/Annots");
        Assert.That(annots, Is.Not.Null);
        var dict = GetAnnotationDictionary(annots!.Elements.Single());

        Assert.That(dict.Elements.GetName("/Subtype"), Is.EqualTo("/Ink"));
        Assert.That(dict.Elements.GetString("/NM"), Does.StartWith("wna_hidden_"));
        Assert.That(dict.Elements.GetReal("/CA"), Is.EqualTo(1d).Within(0.001));
        Assert.That(dict.Elements.GetInteger("/WNARevealMs"), Is.EqualTo(4250));
        Assert.That(dict.Elements.GetDictionary("/AP"), Is.Not.Null);

        var reloaded = PdfService.TryExtractHiddenInkAnnotation(dict, 792d, 96d / 72d);
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.Id, Is.EqualTo("study-answer"));
        Assert.That(reloaded.R, Is.EqualTo(250));
        Assert.That(reloaded.A, Is.EqualTo(255));
        Assert.That(reloaded.RevealDurationMs, Is.EqualTo(4250));
        Assert.That(reloaded.Size, Is.EqualTo(24d).Within(0.01));
        Assert.That(reloaded.Points, Has.Count.EqualTo(2));
        Assert.That(reloaded.Points[0][0], Is.EqualTo(96d).Within(0.01));
        Assert.That(reloaded.Points[0][1], Is.EqualTo(120d).Within(0.01));
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_PreservesForeignInkAnnotations()
    {
        string filePath = Path.Combine(_tempDirectory, "foreign-ink.pdf");
        CreateTestPdf(filePath);

        string injectedPath = Path.Combine(_tempDirectory, "foreign-ink.injected.pdf");
        using (var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify))
        {
            var page = document.Pages[0];
            var foreign = new PdfDictionary(document);
            foreign.Elements.SetName("/Type", "/Annot");
            foreign.Elements.SetName("/Subtype", "/Ink");
            foreign.Elements.SetString("/NM", "foreign-ink-001");
            foreign.Elements.SetInteger("/F", 4);

            var pointArray = new PdfArray();
            pointArray.Elements.Add(new PdfReal(72));
            pointArray.Elements.Add(new PdfReal(648));
            pointArray.Elements.Add(new PdfReal(144));
            pointArray.Elements.Add(new PdfReal(648));
            var inkList = new PdfArray();
            inkList.Elements.Add(pointArray);
            foreign.Elements.Add("/InkList", inkList);

            document.Internals.AddObject(foreign);
            var annots = page.Elements.GetArray("/Annots") ?? new PdfArray(document);
            if (!page.Elements.ContainsKey("/Annots"))
                page.Elements.Add("/Annots", annots);
            annots.Elements.Add(foreign.Reference);
            document.Save(injectedPath);
        }
        File.Move(injectedPath, filePath, true);

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(
            filePath,
            new Dictionary<int, PageAnnotation> { [0] = new PageAnnotation() });

        using var reopened = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var savedAnnots = reopened.Pages[0].Elements.GetArray("/Annots");
        Assert.That(savedAnnots, Is.Not.Null);
        var foreignInk = savedAnnots!.Elements
            .Select(GetAnnotationDictionary)
            .SingleOrDefault(dict => dict.Elements.GetString("/NM") == "foreign-ink-001");
        Assert.That(foreignInk, Is.Not.Null);
        Assert.That(foreignInk!.Elements.GetName("/Subtype"), Is.EqualTo("/Ink"));
        Assert.That(foreignInk.Elements.GetArray("/InkList"), Is.Not.Null);
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_CjkFreeTextEmbedsUnicodeFont()
    {
        string filePath = Path.Combine(_tempDirectory, "cjk.pdf");
        CreateTestPdf(filePath);

        var annotations = new Dictionary<int, PageAnnotation>();
        var pageAnnots = new PageAnnotation();
        pageAnnots.Texts.Add(new TextAnnotation
        {
            Text = "你好 world 你 好",
            X = 100,
            Y = 100,
            FontSize = 16,
            R = 200,
            G = 30,
            B = 40
        });
        annotations[0] = pageAnnots;

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(filePath, annotations);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var annots = document.Pages[0].Elements.GetArray("/Annots");
        Assert.That(annots, Is.Not.Null);
        Assert.That(annots!.Elements.Count, Is.EqualTo(1));

        var textAnnot = GetAnnotationDictionary(annots.Elements[0]);
        Assert.That(textAnnot.Elements.GetName("/Subtype"), Is.EqualTo("/FreeText"));
        Assert.That(textAnnot.Elements.GetString("/Contents"), Does.Contain("你好"));
        Assert.That(textAnnot.Elements.GetString("/DA"), Does.Contain("Tf"));

        var appearance = textAnnot.Elements.GetDictionary("/AP");
        Assert.That(appearance, Is.Not.Null);
        var form = appearance!.Elements.GetDictionary("/N");
        Assert.That(form, Is.Not.Null);
        Assert.That(form!.Elements.GetName("/Subtype"), Is.EqualTo("/Form"));

        var resources = form.Elements.GetDictionary("/Resources");
        Assert.That(resources, Is.Not.Null);
        var fonts = resources!.Elements.GetDictionary("/Font");
        Assert.That(fonts, Is.Not.Null);
        Assert.That(fonts!.Elements.Keys, Is.Not.Empty);

        string fontKey = fonts.Elements.Keys.Single();
        var font = fonts.Elements.GetDictionary(fontKey);
        Assert.That(font, Is.Not.Null);
        Assert.That(font!.Elements.GetName("/Subtype"), Is.EqualTo("/Type0"));

        var descendantFonts = font.Elements.GetArray("/DescendantFonts");
        Assert.That(descendantFonts, Is.Not.Null);
        var cidFont = GetAnnotationDictionary(descendantFonts!.Elements[0]);
        var descriptor = cidFont.Elements.GetDictionary("/FontDescriptor");
        Assert.That(descriptor, Is.Not.Null);
        Assert.That(descriptor!.Elements.ContainsKey("/FontFile2"), Is.True,
            "CJK font subset must be embedded so external viewers can render the text");
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_CjkFreeTextRoundTripsThroughParser()
    {
        string filePath = Path.Combine(_tempDirectory, "cjk-roundtrip.pdf");
        CreateTestPdf(filePath);

        var annotations = new Dictionary<int, PageAnnotation>();
        var pageAnnots = new PageAnnotation();
        pageAnnots.Texts.Add(new TextAnnotation
        {
            Text = "你好 world 你 好",
            X = 100,
            Y = 100,
            FontSize = 16,
            R = 200,
            G = 30,
            B = 40
        });
        annotations[0] = pageAnnots;

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(filePath, annotations);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var annots = document.Pages[0].Elements.GetArray("/Annots");
        Assert.That(annots, Is.Not.Null);

        var textAnnot = GetAnnotationDictionary(annots!.Elements[0]);
        var result = PdfService.TryExtractFreeTextAnnotation(textAnnot, 792.0, 96.0 / 72.0);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Text, Is.EqualTo("你好 world 你 好"));
        Assert.That(result.X, Is.EqualTo(100d).Within(0.01));
        Assert.That(result.Y, Is.EqualTo(100d).Within(0.01));
        Assert.That(result.FontSize, Is.EqualTo(16d).Within(0.01));
        Assert.That(result.R, Is.EqualTo(200));
        Assert.That(result.G, Is.EqualTo(30));
        Assert.That(result.B, Is.EqualTo(40));
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_MixedScriptTextsKeepSeparateAppearancePaths()
    {
        string filePath = Path.Combine(_tempDirectory, "mixed.pdf");
        CreateTestPdf(filePath);

        var annotations = new Dictionary<int, PageAnnotation>();
        var pageAnnots = new PageAnnotation();
        pageAnnots.Texts.Add(new TextAnnotation
        {
            Text = "Latin only",
            X = 80,
            Y = 120,
            FontSize = 14,
            R = 0,
            G = 0,
            B = 0
        });
        pageAnnots.Texts.Add(new TextAnnotation
        {
            Text = "中文注记",
            X = 100,
            Y = 200,
            FontSize = 14,
            R = 0,
            G = 0,
            B = 0
        });
        annotations[0] = pageAnnots;

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(filePath, annotations);

        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var annots = document.Pages[0].Elements.GetArray("/Annots");
        Assert.That(annots, Is.Not.Null);
        Assert.That(annots!.Elements.Count, Is.EqualTo(2));

        var freeTexts = annots.Elements.Select(GetAnnotationDictionary)
            .Where(dict => dict.Elements.GetName("/Subtype") == "/FreeText")
            .ToList();
        Assert.That(freeTexts.Count, Is.EqualTo(2));

        var latinAnnot = freeTexts.Single(dict => dict.Elements.GetString("/Contents") == "Latin only");
        var latinFont = GetAppearanceFont(latinAnnot);
        Assert.That(latinFont!.Elements.GetName("/Subtype"), Is.EqualTo("/Type1"));
        Assert.That(latinFont.Elements.GetName("/BaseFont"), Is.EqualTo("/Helvetica"));

        var cjkAnnot = freeTexts.Single(dict => dict.Elements.GetString("/Contents") == "中文注记");
        var cjkFont = GetAppearanceFont(cjkAnnot);
        Assert.That(cjkFont!.Elements.GetName("/Subtype"), Is.EqualTo("/Type0"));
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_ReSavingCjkFreeTextStaysValid()
    {
        // Simulates the in-app edit loop: save -> reload annotations -> save again.
        // The delete-and-rewrite flow must not corrupt the CJK annotation.
        string filePath = Path.Combine(_tempDirectory, "cjk-resave.pdf");
        CreateTestPdf(filePath);

        var annotations = new Dictionary<int, PageAnnotation>();
        var pageAnnots = new PageAnnotation();
        pageAnnots.Texts.Add(new TextAnnotation
        {
            Text = "你好 world 你 好",
            X = 100,
            Y = 100,
            FontSize = 16,
            R = 200,
            G = 30,
            B = 40
        });
        annotations[0] = pageAnnots;

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(filePath, annotations);

        Models.TextAnnotation reloaded;
        using (var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly))
        {
            var annots = document.Pages[0].Elements.GetArray("/Annots");
            reloaded = PdfService.TryExtractFreeTextAnnotation(GetAnnotationDictionary(annots!.Elements[0]), 792.0, 96.0 / 72.0)!;
        }
        Assert.That(reloaded, Is.Not.Null);

        var secondPass = new Dictionary<int, PageAnnotation>();
        var secondPage = new PageAnnotation();
        secondPage.Texts.Add(reloaded);
        secondPass[0] = secondPage;
        await service.SaveAnnotationsToPdfAsync(filePath, secondPass);

        using var reopened = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var annots2 = reopened.Pages[0].Elements.GetArray("/Annots");
        Assert.That(annots2, Is.Not.Null);
        Assert.That(annots2!.Elements.Count, Is.EqualTo(1));

        var textAnnot = GetAnnotationDictionary(annots2.Elements[0]);
        var result = PdfService.TryExtractFreeTextAnnotation(textAnnot, 792.0, 96.0 / 72.0);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Text, Is.EqualTo("你好 world 你 好"));

        var cjkFont = GetAppearanceFont(textAnnot);
        Assert.That(cjkFont!.Elements.GetName("/Subtype"), Is.EqualTo("/Type0"));
    }

    private const string OnePixelRedPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC";

    [Test]
    public async Task SaveAnnotationsToPdfAsync_ImageStampRoundTripsThroughParser()
    {
        string filePath = Path.Combine(_tempDirectory, "image-roundtrip.pdf");
        CreateTestPdf(filePath);

        var annotations = new Dictionary<int, PageAnnotation>();
        var pageAnnots = new PageAnnotation();
        pageAnnots.Images.Add(new ImageAnnotation
        {
            X = 96,
            Y = 192,
            Width = 288,
            Height = 192,
            Format = "png",
            ImageDataBase64 = OnePixelRedPngBase64
        });
        annotations[0] = pageAnnots;

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(filePath, annotations);

        Models.ImageAnnotation reloaded;
        using (var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly))
        {
            var annots = document.Pages[0].Elements.GetArray("/Annots");
            Assert.That(annots, Is.Not.Null);
            Assert.That(annots!.Elements.Count, Is.EqualTo(1));

            var stamp = GetAnnotationDictionary(annots.Elements[0]);
            Assert.That(stamp.Elements.GetName("/Subtype"), Is.EqualTo("/Stamp"));
            Assert.That(stamp.Elements.GetString("/NM"), Does.StartWith("wna_img_"));
            Assert.That(stamp.Elements.GetInteger("/F") & 4, Is.EqualTo(4));

            // The appearance is an XForm embedding the image XObject (the
            // visual for external viewers).
            var appearance = stamp.Elements.GetDictionary("/AP");
            var form = appearance!.Elements.GetDictionary("/N");
            Assert.That(form, Is.Not.Null);
            Assert.That(form!.Elements.GetName("/Subtype"), Is.EqualTo("/Form"));
            var formResources = form.Elements.GetDictionary("/Resources");
            var xobjects = formResources?.Elements.GetDictionary("/XObject");
            Assert.That(xobjects, Is.Not.Null);
            Assert.That(xobjects!.Elements.Keys, Is.Not.Empty,
                "image stamp appearance must embed an image XObject");

            reloaded = PdfService.TryExtractImageAnnotation(stamp, 792.0, 96.0 / 72.0);
        }

        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.ImageDataBase64, Is.EqualTo(OnePixelRedPngBase64));
        Assert.That(reloaded.Format, Is.EqualTo("png"));
        Assert.That(reloaded.X, Is.EqualTo(96d).Within(0.5));
        Assert.That(reloaded.Y, Is.EqualTo(192d).Within(0.5));
        Assert.That(reloaded.Width, Is.EqualTo(288d).Within(0.5));
        Assert.That(reloaded.Height, Is.EqualTo(192d).Within(0.5));
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_ReSavingReplacesOwnImageStampsAndKeepsForeignOnes()
    {
        string filePath = Path.Combine(_tempDirectory, "image-resave.pdf");
        CreateTestPdf(filePath);

        var annotations = new Dictionary<int, PageAnnotation>();
        var pageAnnots = new PageAnnotation();
        pageAnnots.Images.Add(new ImageAnnotation
        {
            X = 96,
            Y = 192,
            Width = 288,
            Height = 192,
            ImageDataBase64 = OnePixelRedPngBase64
        });
        annotations[0] = pageAnnots;

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(filePath, annotations);

        // Inject a FOREIGN /Stamp (no wna_img_ /NM prefix) — e.g. a seal made
        // by another app. Re-saving must keep it untouched.
        string injectedPath = Path.Combine(_tempDirectory, "image-resave.injected.pdf");
        using (var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify))
        {
            var page = document.Pages[0];
            var foreign = new PdfDictionary(document);
            foreign.Elements.SetName("/Type", "/Annot");
            foreign.Elements.SetName("/Subtype", "/Stamp");
            foreign.Elements.SetRectangle("/Rect", new PdfRectangle(new XRect(10, 700, 50, 20)));
            foreign.Elements.SetString("/NM", "foreign_seal_001");
            foreign.Elements.SetInteger("/F", 4);
            document.Internals.AddObject(foreign);
            var annots = page.Elements.GetArray("/Annots");
            annots!.Elements.Add(foreign.Reference);
            document.Save(injectedPath);
        }
        File.Move(injectedPath, filePath, true);

        // Second save (in-app edit loop): our own stamp is deleted and
        // rewritten, the foreign one survives.
        await service.SaveAnnotationsToPdfAsync(filePath, annotations);

        using var reopened = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var annots2 = reopened.Pages[0].Elements.GetArray("/Annots");
        Assert.That(annots2, Is.Not.Null);
        Assert.That(annots2!.Elements.Count, Is.EqualTo(2));

        var stamps = annots2.Elements.Select(GetAnnotationDictionary)
            .Where(dict => dict.Elements.GetName("/Subtype") == "/Stamp")
            .ToList();
        Assert.That(stamps.Count, Is.EqualTo(2));

        var own = stamps.Single(dict => (dict.Elements.GetString("/NM") ?? "").StartsWith("wna_img_"));
        var reloaded = PdfService.TryExtractImageAnnotation(own, 792.0, 96.0 / 72.0);
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.ImageDataBase64, Is.EqualTo(OnePixelRedPngBase64));

        var foreignKept = stamps.Single(dict => dict.Elements.GetString("/NM") == "foreign_seal_001");
        Assert.That(foreignKept.Elements.GetRectangle("/Rect").Width, Is.EqualTo(50d).Within(0.01));
    }

    [Test]
    public async Task LoadSaveReopen_PreservesLegacyWhiteHiddenInkAndOwnStreamLifecycle()
    {
        string filePath = Path.Combine(_tempDirectory, "legacy-white-reopen.pdf");
        CreateTestPdf(filePath);
        var legacyWhite = new HiddenInkAnnotation
        {
            Id = "legacy-white-pdf",
            R = 255,
            G = 255,
            B = 255,
            A = 255,
            Points = new List<double[]> { new[] { 96d, 120d }, new[] { 144d, 120d } }
        };

        var service = new PdfService();
        await service.SaveAnnotationsToPdfAsync(
            filePath,
            new Dictionary<int, PageAnnotation>
            {
                [0] = new PageAnnotation { HiddenInks = new List<HiddenInkAnnotation> { legacyWhite } }
            });
        await service.LoadPdfAsync(filePath);

        var loaded = service.ExtractedAnnotations[0].HiddenInks.Single();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Id, Is.EqualTo("legacy-white-pdf"));
            Assert.That(loaded.R, Is.EqualTo(255));
            Assert.That(loaded.G, Is.EqualTo(255));
            Assert.That(loaded.B, Is.EqualTo(255));
        });

        // Save again through the loaded service to exercise backing-stream
        // ownership, then dispose and reopen with a fresh service.
        await service.SaveAnnotationsToPdfAsync(filePath, service.ExtractedAnnotations);
        await service.DisposeAsync();

        var reopened = new PdfService();
        for (int iteration = 0; iteration < 2; iteration++)
        {
            await reopened.LoadPdfAsync(filePath);
            var roundTrip = reopened.ExtractedAnnotations[0].HiddenInks.Single();
            Assert.Multiple(() =>
            {
                Assert.That(roundTrip.R, Is.EqualTo(255));
                Assert.That(roundTrip.G, Is.EqualTo(255));
                Assert.That(roundTrip.B, Is.EqualTo(255));
            });

            await reopened.SaveAnnotationsToPdfAsync(filePath, reopened.ExtractedAnnotations);
            await reopened.DisposeAsync();
            reopened = new PdfService();
        }

        await reopened.DisposeAsync();
        File.Delete(filePath);
        Assert.That(File.Exists(filePath), Is.False, "backing stream must be released before the PDF is deleted");
    }

    [Test]
    public async Task LoadPdfAsync_MissingHiddenInkColorUsesNeutralGrayProductionDefault()
    {
        string filePath = Path.Combine(_tempDirectory, "missing-hidden-color.pdf");
        CreateHiddenInkPdfWithoutColor(filePath);

        var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        var hidden = service.ExtractedAnnotations[0].HiddenInks.Single();

        Assert.Multiple(() =>
        {
            Assert.That(hidden.R, Is.EqualTo(199));
            Assert.That(hidden.G, Is.EqualTo(205));
            Assert.That(hidden.B, Is.EqualTo(212));
            Assert.That(hidden.RevealDurationMs, Is.EqualTo(HiddenInkRevealState.DefaultRevealDurationMs));
        });
        await service.DisposeAsync();
    }

    private static PdfDictionary? GetAppearanceFont(PdfDictionary annotation)
    {
        var appearance = annotation.Elements.GetDictionary("/AP");
        var form = appearance?.Elements.GetDictionary("/N");
        var resources = form?.Elements.GetDictionary("/Resources");
        var fonts = resources?.Elements.GetDictionary("/Font");
        Assert.That(fonts, Is.Not.Null);
        string fontKey = fonts!.Elements.Keys.Single();
        return fonts.Elements.GetDictionary(fontKey);
    }

    private static PdfDictionary GetAnnotationDictionary(PdfItem item)
    {
        return (item as PdfReference)?.Value as PdfDictionary
            ?? item as PdfDictionary
            ?? throw new InvalidDataException("Annotation entry was not a PDF dictionary.");
    }

    private static void CreateTestPdf(string filePath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = 612; // 8.5 x 72
        page.Height = 792; // 11 x 72
        document.Save(filePath);
    }

    private static StrokeAnnotation CreateCrispRectangleStroke()
    {
        return new StrokeAnnotation
        {
            R = 47,
            G = 85,
            B = 212,
            A = 255,
            Size = 3,
            FitToCurve = false,
            Points = new List<double[]>
            {
                new[] { 96d, 120d },
                new[] { 240d, 120d },
                new[] { 240d, 240d },
                new[] { 96d, 240d },
                new[] { 96d, 120d }
            }
        };
    }

    private static void CreateHiddenInkPdfWithoutColor(string filePath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = 612;
        page.Height = 792;

        var annotation = new PdfDictionary(document);
        annotation.Elements.SetName("/Type", "/Annot");
        annotation.Elements.SetName("/Subtype", "/Ink");
        annotation.Elements.SetString("/NM", "wna_hidden_missing-color");
        annotation.Elements.SetInteger("/F", 4);
        var border = new PdfDictionary(document);
        border.Elements.SetReal("/W", 2.0);
        annotation.Elements["/BS"] = border;
        var points = new PdfArray();
        points.Elements.Add(new PdfReal(72));
        points.Elements.Add(new PdfReal(648));
        points.Elements.Add(new PdfReal(108));
        points.Elements.Add(new PdfReal(648));
        var inkList = new PdfArray();
        inkList.Elements.Add(points);
        annotation.Elements.Add("/InkList", inkList);
        document.Internals.AddObject(annotation);
        var annots = new PdfArray(document);
        annots.Elements.Add(annotation.Reference);
        page.Elements.Add("/Annots", annots);
        document.Save(filePath);
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_DoesNotMaterializeMissingCropBox()
    {
        string filePath = Path.Combine(_tempDirectory, "edge-compat.pdf");
        CreateTestPdf(filePath);

        using (var source = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly))
        {
            Assert.That(source.Pages[0].Elements.ContainsKey("/CropBox"), Is.False,
                "The regression fixture must start without an explicit CropBox.");
        }

        await using var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        await service.SaveAnnotationsToPdfAsync(filePath, new Dictionary<int, PageAnnotation>
        {
            [0] = new PageAnnotation
            {
                Texts = new List<TextAnnotation>
                {
                    new TextAnnotation { Text = "Edge test", X = 100, Y = 100, FontSize = 14, R = 0, G = 0, B = 0 }
                },
                Strokes = new List<StrokeAnnotation>
                {
                    new StrokeAnnotation
                    {
                        R = 0, G = 120, B = 215, A = 255, Size = 2,
                        Points = new List<double[]> { new[] { 50d, 50d }, new[] { 100d, 100d } }
                    }
                }
            }
        });

        using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(doc.PageCount, Is.EqualTo(1));
        Assert.That(doc.Pages[0].Elements.ContainsKey("/CropBox"), Is.False,
            "Saving must not turn the default MediaBox fallback into an explicit zero-area CropBox.");
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_RemovesExistingZeroAreaCropBox()
    {
        string filePath = Path.Combine(_tempDirectory, "corrupted-cropbox.pdf");
        CreateTestPdf(filePath);

        using (var corruptDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify))
        {
            var page = corruptDoc.Pages[0];
            page.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(0, 0, 0, 0)));
            corruptDoc.Save(filePath);
        }

        using (var corruptDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly))
        {
            Assert.That(corruptDoc.Pages[0].Elements.ContainsKey("/CropBox"), Is.True,
                "The regression fixture must contain an explicit zero-area CropBox.");
            Assert.That(PdfAtomicFile.HasUsableArea(corruptDoc.Pages[0].Elements.GetRectangle("/CropBox")), Is.False);
        }

        await using var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        await service.SaveAnnotationsToPdfAsync(filePath, service.ExtractedAnnotations);

        using var repairedDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(repairedDoc.Pages[0].Elements.ContainsKey("/CropBox"), Is.False,
            "An explicit zero-area CropBox must be removed so viewers fall back to MediaBox.");
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_PreservesValidExplicitCropBox()
    {
        string filePath = Path.Combine(_tempDirectory, "valid-cropbox.pdf");
        CreateTestPdf(filePath);

        using (var source = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify))
        {
            source.Pages[0].Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(36, 48, 540, 696)));
            source.Save(filePath);
        }

        await using var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        await service.SaveAnnotationsToPdfAsync(filePath, service.ExtractedAnnotations);

        using var saved = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(saved.Pages[0].Elements.ContainsKey("/CropBox"), Is.True);
        var cropBox = saved.Pages[0].Elements.GetRectangle("/CropBox");
        Assert.Multiple(() =>
        {
            Assert.That(cropBox.X1, Is.EqualTo(36).Within(0.001));
            Assert.That(cropBox.Y1, Is.EqualTo(48).Within(0.001));
            Assert.That(cropBox.X2, Is.EqualTo(576).Within(0.001));
            Assert.That(cropBox.Y2, Is.EqualTo(744).Within(0.001));
        });
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_Chapter15WavesSimulation_RepairsZeroCropBoxAndPreservesAllAnnotations()
    {
        string filePath = Path.Combine(_tempDirectory, "Chapter15_waves_simulated.pdf");

        // 1. Create an 87-page presentation PDF with 960x540 dimensions
        using (var doc = new PdfDocument())
        {
            for (int i = 0; i < 87; i++)
            {
                var page = doc.AddPage();
                page.Width = 960;
                page.Height = 540;

                // Corrupt every page with an invalid zero-area /CropBox [0 0 0 0]
                page.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(0, 0, 0, 0)));
            }

            // 2. Add over 1,500 handwritten ink strokes across 41 pages (pages 0..40, 37 strokes each = 1,517 strokes)
            int strokeCounter = 0;
            for (int pageIdx = 0; pageIdx < 41; pageIdx++)
            {
                var page = doc.Pages[pageIdx];
                var annots = new PdfArray(doc);
                for (int s = 0; s < 37; s++)
                {
                    strokeCounter++;
                    var inkAnnot = new PdfDictionary(doc);
                    inkAnnot.Elements.SetName("/Type", "/Annot");
                    inkAnnot.Elements.SetName("/Subtype", "/Ink");
                    inkAnnot.Elements.SetString("/NM", $"wna_ink_{Guid.NewGuid():N}");

                    var inkList = new PdfArray(doc);
                    var strokePoints = new PdfArray(doc);
                    // Sample stroke with multiple points in 960x540 coordinate space
                    double startX = 100 + (s * 10);
                    double startY = 100 + (s * 5);
                    strokePoints.Elements.Add(new PdfReal(startX));
                    strokePoints.Elements.Add(new PdfReal(startY));
                    strokePoints.Elements.Add(new PdfReal(startX + 20));
                    strokePoints.Elements.Add(new PdfReal(startY + 30));
                    strokePoints.Elements.Add(new PdfReal(startX + 40));
                    strokePoints.Elements.Add(new PdfReal(startY + 10));
                    inkList.Elements.Add(strokePoints);

                    inkAnnot.Elements.Add("/InkList", inkList);
                    inkAnnot.Elements.SetRectangle("/Rect", new PdfSharpCore.Pdf.PdfRectangle(new XRect(startX, startY, 40, 30)));

                    var colorArray = new PdfArray(doc);
                    colorArray.Elements.Add(new PdfReal(0.1));
                    colorArray.Elements.Add(new PdfReal(0.2));
                    colorArray.Elements.Add(new PdfReal(0.8));
                    inkAnnot.Elements.Add("/C", colorArray);

                    annots.Elements.Add(inkAnnot);
                }
                page.Elements.Add("/Annots", annots);
            }

            doc.Save(filePath);
            Assert.That(strokeCounter, Is.EqualTo(1517));
        }

        // 3. Load via PdfService, verify extraction and display dimensions
        await using var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        Assert.That(service.PageCount, Is.EqualTo(87));

        int extractedStrokeCount = 0;
        foreach (var kvp in service.ExtractedAnnotations)
        {
            extractedStrokeCount += kvp.Value.Strokes.Count;
        }
        Assert.That(extractedStrokeCount, Is.EqualTo(1517), "All 1,517 strokes must be extracted cleanly.");

        // 4. Save annotations back
        await service.SaveAnnotationsToPdfAsync(filePath, service.ExtractedAnnotations);

        // 5. Verify the saved file standards compliance and geometry
        using var verifiedDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(verifiedDoc.PageCount, Is.EqualTo(87));

        for (int i = 0; i < verifiedDoc.PageCount; i++)
        {
            var page = verifiedDoc.Pages[i];
            Assert.That(page.Elements.ContainsKey("/CropBox"), Is.False,
                $"Page {i} must NOT have an explicit zero-area CropBox.");
            Assert.That(page.Width.Point, Is.EqualTo(960).Within(0.001),
                $"Page {i} Width must be 960 points.");
            Assert.That(page.Height.Point, Is.EqualTo(540).Within(0.001),
                $"Page {i} Height must be 540 points.");
        }

        // 6. Verify page rendering runs cleanly without exceptions
        var renderedFirstPage = await service.RenderPageAsync(0);
        Assert.That(renderedFirstPage, Is.Not.Null);
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_MixedPages_HandlesValidCorruptAndMissingCropBoxes()
    {
        string filePath = Path.Combine(_tempDirectory, "mixed-cropboxes.pdf");

        using (var doc = new PdfDocument())
        {
            // Page 0: Valid explicit CropBox
            var p0 = doc.AddPage();
            p0.Width = 612; p0.Height = 792;
            p0.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(36, 48, 540, 696)));

            // Page 1: Default missing CropBox (MediaBox only)
            var p1 = doc.AddPage();
            p1.Width = 612; p1.Height = 792;

            // Page 2: Corrupted zero-area CropBox [0 0 0 0]
            var p2 = doc.AddPage();
            p2.Width = 612; p2.Height = 792;
            p2.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(0, 0, 0, 0)));

            // Page 3: Corrupted degenerate horizontal line CropBox [0 0 612 0]
            var p3 = doc.AddPage();
            p3.Width = 612; p3.Height = 792;
            p3.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(0, 0, 612, 0)));

            // Page 4: Corrupted degenerate vertical line CropBox [0 0 0 792]
            var p4 = doc.AddPage();
            p4.Width = 612; p4.Height = 792;
            p4.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(0, 0, 0, 792)));

            // Page 5: Inverted valid CropBox [576 744 36 48] (Math.Abs width=540, height=696)
            var p5 = doc.AddPage();
            p5.Width = 612; p5.Height = 792;
            p5.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XPoint(576, 744), new XPoint(36, 48)));

            // Page 6: Degenerate point CropBox [100 100 100 100]
            var p6 = doc.AddPage();
            p6.Width = 612; p6.Height = 792;
            p6.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(100, 100, 0, 0)));

            // Page 7: Corrupted malformed CropBox array with fewer than 4 elements
            var p7 = doc.AddPage();
            p7.Width = 612; p7.Height = 792;
            var malformedArray = new PdfArray(doc);
            malformedArray.Elements.Add(new PdfReal(0));
            malformedArray.Elements.Add(new PdfReal(0));
            p7.Elements.Add("/CropBox", malformedArray);

            doc.Save(filePath);
        }

        await using var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        await service.SaveAnnotationsToPdfAsync(filePath, service.ExtractedAnnotations);

        using var saved = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(saved.PageCount, Is.EqualTo(8));

        // Page 0: Valid CropBox preserved
        Assert.That(saved.Pages[0].Elements.ContainsKey("/CropBox"), Is.True);
        var cb0 = saved.Pages[0].Elements.GetRectangle("/CropBox");
        Assert.That(cb0.Width, Is.EqualTo(540).Within(0.001));
        Assert.That(cb0.Height, Is.EqualTo(696).Within(0.001));

        // Page 1: No CropBox materialized
        Assert.That(saved.Pages[1].Elements.ContainsKey("/CropBox"), Is.False);

        // Page 2: Corrupted [0 0 0 0] stripped
        Assert.That(saved.Pages[2].Elements.ContainsKey("/CropBox"), Is.False);

        // Page 3: Degenerate horizontal stripped
        Assert.That(saved.Pages[3].Elements.ContainsKey("/CropBox"), Is.False);

        // Page 4: Degenerate vertical stripped
        Assert.That(saved.Pages[4].Elements.ContainsKey("/CropBox"), Is.False);

        // Page 5: Inverted valid CropBox preserved
        Assert.That(saved.Pages[5].Elements.ContainsKey("/CropBox"), Is.True);
        var cb5 = saved.Pages[5].Elements.GetRectangle("/CropBox");
        Assert.That(Math.Abs(cb5.Width), Is.EqualTo(540).Within(0.001));
        Assert.That(Math.Abs(cb5.Height), Is.EqualTo(696).Within(0.001));

        // Page 6: Degenerate point stripped
        Assert.That(saved.Pages[6].Elements.ContainsKey("/CropBox"), Is.False);

        // Page 7: Malformed array stripped
        Assert.That(saved.Pages[7].Elements.ContainsKey("/CropBox"), Is.False);
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_InheritedPagesCropBox_RemovesZeroAreaCropBoxFromPagesNode()
    {
        string filePath = Path.Combine(_tempDirectory, "inherited-pages-cropbox.pdf");
        CreateTestPdf(filePath);

        using (var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify))
        {
            // Corrupt the root Pages collection dictionary with a zero-area CropBox
            doc.Pages.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(0, 0, 0, 0)));
            doc.Save(filePath);
        }

        await using var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        await service.SaveAnnotationsToPdfAsync(filePath, service.ExtractedAnnotations);

        using var saved = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(saved.Pages.Elements.ContainsKey("/CropBox"), Is.False,
            "The root Pages dictionary must have invalid zero-area CropBox removed.");
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_DeepPageTreeHierarchy_RemovesZeroAreaCropBoxFromIntermediateNodes()
    {
        string filePath = Path.Combine(_tempDirectory, "deep-page-tree-cropbox.pdf");
        CreateTestPdf(filePath);

        using (var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify))
        {
            var page = doc.Pages[0];
            var intermediatePages = new PdfDictionary(doc);
            intermediatePages.Elements.SetName("/Type", "/Pages");
            intermediatePages.Elements.SetInteger("/Count", 1);
            intermediatePages.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(0, 0, 0, 0)));
            intermediatePages.Elements.Add("/Parent", doc.Pages);
            doc.Internals.AddObject(intermediatePages);

            var kids = new PdfArray(doc);
            kids.Elements.Add(page);
            intermediatePages.Elements.Add("/Kids", kids);

            page.Elements["/Parent"] = intermediatePages;

            var rootKids = doc.Pages.Elements.GetArray("/Kids");
            if (rootKids != null)
            {
                rootKids.Elements.Clear();
                rootKids.Elements.Add(intermediatePages);
            }

            doc.Save(filePath);
        }

        await using var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        await service.SaveAnnotationsToPdfAsync(filePath, service.ExtractedAnnotations);

        using var saved = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        var page0 = saved.Pages[0];
        Assert.That(page0.Elements.ContainsKey("/CropBox"), Is.False);
        if (page0.Elements.ContainsKey("/Parent"))
        {
            var parent = page0.Elements.GetDictionary("/Parent");
            Assert.That(parent.Elements.ContainsKey("/CropBox"), Is.False,
                "Intermediate /Pages node must have invalid zero-area CropBox removed.");
        }
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_IndirectReferenceCropBox_RemovesZeroAreaIndirectCropBox()
    {
        string filePath = Path.Combine(_tempDirectory, "indirect-cropbox.pdf");
        CreateTestPdf(filePath);

        using (var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify))
        {
            var cropBoxArray = new PdfArray(doc);
            cropBoxArray.Elements.Add(new PdfReal(0));
            cropBoxArray.Elements.Add(new PdfReal(0));
            cropBoxArray.Elements.Add(new PdfReal(0));
            cropBoxArray.Elements.Add(new PdfReal(0));
            doc.Internals.AddObject(cropBoxArray);
            doc.Pages[0].Elements.Add("/CropBox", cropBoxArray);
            doc.Save(filePath);
        }

        await using var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        await service.SaveAnnotationsToPdfAsync(filePath, service.ExtractedAnnotations);

        using var saved = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(saved.Pages[0].Elements.ContainsKey("/CropBox"), Is.False,
            "Indirect zero-area CropBox must be removed.");
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_ValidNegativeCoordinateCropBox_PreservesCropBox()
    {
        string filePath = Path.Combine(_tempDirectory, "negative-cropbox.pdf");
        CreateTestPdf(filePath);

        using (var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify))
        {
            // Valid CropBox with negative origins: [-50, -50, 450, 450] (Width=500, Height=500)
            doc.Pages[0].Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(-50, -50, 500, 500)));
            doc.Save(filePath);
        }

        await using var service = new PdfService();
        await service.LoadPdfAsync(filePath);
        await service.SaveAnnotationsToPdfAsync(filePath, service.ExtractedAnnotations);

        using var saved = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(saved.Pages[0].Elements.ContainsKey("/CropBox"), Is.True,
            "Valid CropBox with negative coordinates must be preserved.");
        var cb = saved.Pages[0].Elements.GetRectangle("/CropBox");
        Assert.Multiple(() =>
        {
            Assert.That(cb.X1, Is.EqualTo(-50).Within(0.001));
            Assert.That(cb.Y1, Is.EqualTo(-50).Within(0.001));
            Assert.That(cb.Width, Is.EqualTo(500).Within(0.001));
            Assert.That(cb.Height, Is.EqualTo(500).Within(0.001));
        });
    }

    [Test]
    public async Task SaveAnnotationsToPdfAsync_PreservesOutlinesAndAllAnnotationTypesWithZeroCropBoxRepair()
    {
        string filePath = Path.Combine(_tempDirectory, "outlines-annotations-repair.pdf");

        // 1. Create a 2-page document with outlines and zero-area CropBox
        using (var doc = new PdfDocument())
        {
            var p0 = doc.AddPage();
            p0.Width = 612; p0.Height = 792;
            p0.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(0, 0, 0, 0)));

            var p1 = doc.AddPage();
            p1.Width = 612; p1.Height = 792;
            p1.Elements.SetRectangle("/CropBox", new PdfSharpCore.Pdf.PdfRectangle(new XRect(0, 0, 0, 0)));

            // Add bookmarks/outlines
            var rootOutline = doc.Outlines.Add("Chapter 1", p0, true);
            rootOutline.Outlines.Add("Section 1.1", p1, true);

            doc.Save(filePath);
        }

        // 2. Add annotations across pages
        var annotations = new Dictionary<int, PageAnnotation>();
        var page0Annots = new PageAnnotation();
        page0Annots.Texts.Add(new TextAnnotation { Text = "Chapter Title", X = 50, Y = 50, FontSize = 16, R = 0, G = 0, B = 0 });
        page0Annots.Strokes.Add(new StrokeAnnotation
        {
            R = 200, G = 50, B = 50, A = 255, Size = 3,
            Points = new List<double[]> { new[] { 10d, 10d }, new[] { 20d, 20d }, new[] { 30d, 30d } }
        });
        page0Annots.Highlights.Add(new HighlightAnnotation
        {
            R = 255, G = 255, B = 0, A = 128,
            Rects = new List<double[]> { new[] { 50d, 50d, 100d, 20d } }
        });
        page0Annots.StickyNotes.Add(new StickyNoteAnnotation
        {
            Id = "sticky-1",
            X = 200, Y = 200, Text = "Review this",
            R = 255, G = 235, B = 59
        });
        byte[] testImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG header stub
        // Create a valid 1x1 PNG image for ImageAnnotation
        using (var bmp = new System.Drawing.Bitmap(1, 1))
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            page0Annots.Images.Add(new ImageAnnotation
            {
                X = 300, Y = 300, Width = 50, Height = 50,
                ImageDataBase64 = Convert.ToBase64String(ms.ToArray())
            });
        }
        annotations[0] = page0Annots;

        await using (var service = new PdfService())
        {
            await service.LoadPdfAsync(filePath);
            await service.SaveAnnotationsToPdfAsync(filePath, annotations);
        }

        // 3. Verify the repaired document
        using var savedDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.That(savedDoc.PageCount, Is.EqualTo(2));
        Assert.That(savedDoc.Pages[0].Elements.ContainsKey("/CropBox"), Is.False, "Page 0 zero-area CropBox must be removed.");
        Assert.That(savedDoc.Pages[1].Elements.ContainsKey("/CropBox"), Is.False, "Page 1 zero-area CropBox must be removed.");

        // Verify outlines are preserved
        Assert.That(savedDoc.Outlines.Count, Is.EqualTo(1));
        Assert.That(savedDoc.Outlines[0].Title, Is.EqualTo("Chapter 1"));
        Assert.That(savedDoc.Outlines[0].Outlines.Count, Is.EqualTo(1));
        Assert.That(savedDoc.Outlines[0].Outlines[0].Title, Is.EqualTo("Section 1.1"));

        // Verify annotations round-trip
        await using var verifyService = new PdfService();
        await verifyService.LoadPdfAsync(filePath);
        Assert.That(verifyService.ExtractedAnnotations.ContainsKey(0), Is.True);
        var extracted = verifyService.ExtractedAnnotations[0];
        Assert.That(extracted.Texts.Count, Is.EqualTo(1));
        Assert.That(extracted.Texts[0].Text, Is.EqualTo("Chapter Title"));
        Assert.That(extracted.Strokes.Count, Is.EqualTo(1));
        Assert.That(extracted.Highlights.Count, Is.EqualTo(1));
        Assert.That(extracted.StickyNotes.Count, Is.EqualTo(1));
        Assert.That(extracted.Images.Count, Is.EqualTo(1));
    }
}
