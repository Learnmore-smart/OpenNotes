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
}
