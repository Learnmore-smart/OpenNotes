using System.Text.Json;
using System.IO;
using Caelum.Models;
using Caelum.Services;
using PdfSharpCore.Pdf;

namespace Caelum.Tests;

public class HiddenInkTests
{
    [Test]
    public void NewHiddenInk_UsesOpaqueNeutralGrayMaskAndThreeSecondReveal()
    {
        var hidden = new HiddenInkAnnotation();

        Assert.That(hidden.R, Is.EqualTo(199));
        Assert.That(hidden.G, Is.EqualTo(205));
        Assert.That(hidden.B, Is.EqualTo(212));
        Assert.That(hidden.A, Is.EqualTo(255));
        Assert.That(hidden.Size, Is.GreaterThan(0));
        Assert.That(hidden.RevealDurationMs, Is.EqualTo(HiddenInkRevealState.DefaultRevealDurationMs));
        Assert.That(hidden.Id, Is.Not.Empty);
    }

    [Test]
    public void ExplicitLegacyWhiteMaskSurvivesJsonRoundTrip()
    {
        var legacyWhite = new HiddenInkAnnotation
        {
            Id = "legacy-white",
            R = 255,
            G = 255,
            B = 255,
            A = 255,
            Points = new List<double[]> { new[] { 1d, 2d }, new[] { 3d, 4d } }
        };

        var roundTrip = JsonSerializer.Deserialize<HiddenInkAnnotation>(JsonSerializer.Serialize(legacyWhite));

        Assert.That(roundTrip, Is.Not.Null);
        Assert.That(roundTrip!.R, Is.EqualTo(255));
        Assert.That(roundTrip.G, Is.EqualTo(255));
        Assert.That(roundTrip.B, Is.EqualTo(255));
    }

    [Test]
    public void RevealStateIsTransientAndNeverSerialized()
    {
        var hidden = new HiddenInkAnnotation
        {
            Id = "transient-reveal",
            Points = new List<double[]> { new[] { 10d, 10d }, new[] { 20d, 20d } }
        };
        DateTimeOffset revealUntil = HiddenInkRevealState.GetRevealUntil(
            DateTimeOffset.Parse("2026-08-20T12:00:00Z"));

        string json = JsonSerializer.Serialize(hidden);

        Assert.Multiple(() =>
        {
            Assert.That(HiddenInkRevealState.IsRevealed(revealUntil.AddMilliseconds(-1), revealUntil), Is.True);
            Assert.That(json, Does.Not.Contain("RevealUntil"));
            Assert.That(json, Does.Not.Contain("IsRevealed"));
            Assert.That(json, Does.Not.Contain("Visibility"));
        });
    }

    [Test]
    public void HiddenInkToolbarUsesThemedCardVectorMarkAndLocalizedRevealTooltip()
    {
        string root = FindProjectRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        int start = xaml.IndexOf("x:Name=\"HiddenInkToolButton\"", StringComparison.Ordinal);
        int end = xaml.IndexOf("</ToggleButton>", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));

        string button = xaml.Substring(start, end - start);
        Assert.Multiple(() =>
        {
            Assert.That(button, Does.Contain("<controls:LucideIcon"));
            Assert.That(button, Does.Contain("Kind=\"PanelTop\""));
            Assert.That(button, Does.Contain("ThemeSubtleForegroundBrush"));
            Assert.That(button, Does.Contain("AutomationProperties.AutomationId=\"HiddenInkToolButton\""));
            Assert.That(button, Does.Not.Contain("E890"));
        });

        string utilities = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.Utilities.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(utilities, Does.Contain("Editor.HiddenInkTooltip"));
            Assert.That(utilities, Does.Contain("AutomationProperties.SetName(HiddenInkToolButton"));
            Assert.That(utilities, Does.Contain("AutomationProperties.SetHelpText(HiddenInkToolButton"));
        });
    }

    [Test]
    public void RevealState_HidesAtTheDeadlineAndRevealsBeforeIt()
    {
        var revealedAt = DateTimeOffset.Parse("2026-08-20T12:00:00Z");
        var until = HiddenInkRevealState.GetRevealUntil(revealedAt, TimeSpan.FromSeconds(3));

        Assert.That(HiddenInkRevealState.IsRevealed(revealedAt.AddSeconds(2.999), until), Is.True);
        Assert.That(HiddenInkRevealState.IsRevealed(until, until), Is.False);
        Assert.That(HiddenInkRevealState.IsRevealed(until.AddMilliseconds(1), until), Is.False);
    }

    [Test]
    public void RevealState_UsesDefaultDuration_WhenDurationIsMissingOrNonPositive()
    {
        var revealedAt = DateTimeOffset.Parse("2026-08-20T12:00:00Z");
        var expectedUntil = revealedAt.AddMilliseconds(HiddenInkRevealState.DefaultRevealDurationMs);

        Assert.That(HiddenInkRevealState.GetRevealUntil(revealedAt), Is.EqualTo(expectedUntil));
        Assert.That(HiddenInkRevealState.GetRevealUntil(revealedAt, TimeSpan.Zero), Is.EqualTo(expectedUntil));
        Assert.That(HiddenInkRevealState.GetRevealUntil(revealedAt, TimeSpan.FromMilliseconds(-1)),
            Is.EqualTo(expectedUntil));
    }

    [Test]
    public void HiddenInk_SerializesItsGeometryAndRevealSettings()
    {
        var hidden = new HiddenInkAnnotation
        {
            Id = "study-word",
            R = 250,
            G = 248,
            B = 240,
            Size = 28,
            RevealDurationMs = 4250,
            Points = new List<double[]> { new[] { 10d, 20d }, new[] { 80d, 20d } }
        };

        var json = JsonSerializer.Serialize(hidden);
        var roundTrip = JsonSerializer.Deserialize<HiddenInkAnnotation>(json);

        Assert.That(roundTrip, Is.Not.Null);
        Assert.That(roundTrip!.Id, Is.EqualTo("study-word"));
        Assert.That(roundTrip.R, Is.EqualTo(250));
        Assert.That(roundTrip.Size, Is.EqualTo(28));
        Assert.That(roundTrip.RevealDurationMs, Is.EqualTo(4250));
        Assert.That(roundTrip.Points, Has.Count.EqualTo(2));
        Assert.That(roundTrip.Points[1][0], Is.EqualTo(80));
    }

    [Test]
    public void AnnotationData_SerializesAndRestoresHiddenInkPageData()
    {
        var data = new AnnotationData
        {
            Pages = new Dictionary<string, PageAnnotation>
            {
                ["3"] = new PageAnnotation
                {
                    HiddenInks = new List<HiddenInkAnnotation>
                    {
                        new()
                        {
                            Id = "page-three-answer",
                            R = 248,
                            G = 246,
                            B = 238,
                            Size = 31.5,
                            RevealDurationMs = 1800,
                            Points = new List<double[]>
                            {
                                new[] { 12.5, 24.25 },
                                new[] { 90.75, 24.25 }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(data);
        var roundTrip = JsonSerializer.Deserialize<AnnotationData>(json);

        Assert.That(roundTrip, Is.Not.Null);
        Assert.That(roundTrip!.Pages, Does.ContainKey("3"));
        var hidden = roundTrip.Pages["3"].HiddenInks.Single();
        Assert.That(hidden.Id, Is.EqualTo("page-three-answer"));
        Assert.That(hidden.R, Is.EqualTo(248));
        Assert.That(hidden.G, Is.EqualTo(246));
        Assert.That(hidden.B, Is.EqualTo(238));
        Assert.That(hidden.Size, Is.EqualTo(31.5));
        Assert.That(hidden.RevealDurationMs, Is.EqualTo(1800));
        Assert.That(hidden.Points[0][0], Is.EqualTo(12.5));
        Assert.That(hidden.Points[1][1], Is.EqualTo(24.25));
    }

    [Test]
    public void TryExtractHiddenInkAnnotation_ConvertsPdfGeometryAndStyle()
    {
        const double pageHeight = 792.0;
        const double scale = 96.0 / 72.0;

        using var document = new PdfDocument();
        var annotation = new PdfDictionary(document);
        annotation.Elements.SetString("/NM", "wna_hidden_study-answer");

        var color = new PdfArray();
        color.Elements.Add(new PdfReal(250.0 / 255.0));
        color.Elements.Add(new PdfReal(248.0 / 255.0));
        color.Elements.Add(new PdfReal(240.0 / 255.0));
        annotation.Elements.Add("/C", color);

        var border = new PdfDictionary(document);
        border.Elements.SetReal("/W", 18.0);
        annotation.Elements.Add("/BS", border);
        annotation.Elements.SetReal("/CA", 0.25);

        var pointArray = new PdfArray();
        pointArray.Elements.Add(new PdfReal(72));
        pointArray.Elements.Add(new PdfReal(648));
        pointArray.Elements.Add(new PdfReal(144));
        pointArray.Elements.Add(new PdfReal(648));
        var inkList = new PdfArray();
        inkList.Elements.Add(pointArray);
        annotation.Elements.Add("/InkList", inkList);

        var hidden = PdfService.TryExtractHiddenInkAnnotation(annotation, pageHeight, scale);

        Assert.That(hidden, Is.Not.Null);
        Assert.That(hidden!.Id, Is.EqualTo("study-answer"));
        Assert.That(hidden.R, Is.EqualTo(250));
        Assert.That(hidden.G, Is.EqualTo(248));
        Assert.That(hidden.B, Is.EqualTo(240));
        Assert.That(hidden.A, Is.EqualTo(255));
        Assert.That(hidden.Size, Is.EqualTo(24d).Within(0.001));
        Assert.That(hidden.Points, Has.Count.EqualTo(2));
        Assert.That(hidden.Points[0][0], Is.EqualTo(96d).Within(0.001));
        Assert.That(hidden.Points[0][1], Is.EqualTo(192d).Within(0.001));
        Assert.That(hidden.Points[1][0], Is.EqualTo(192d).Within(0.001));
        Assert.That(hidden.Points[1][1], Is.EqualTo(192d).Within(0.001));
    }

    [Test]
    public void TryExtractHiddenInkAnnotation_RejectsForeignInk()
    {
        using var document = new PdfDocument();
        var annotation = new PdfDictionary(document);
        annotation.Elements.SetString("/NM", "foreign-ink");

        var pointArray = new PdfArray();
        pointArray.Elements.Add(new PdfReal(72));
        pointArray.Elements.Add(new PdfReal(648));
        var inkList = new PdfArray();
        inkList.Elements.Add(pointArray);
        annotation.Elements.Add("/InkList", inkList);

        Assert.That(
            PdfService.TryExtractHiddenInkAnnotation(annotation, 792.0, 96.0 / 72.0),
            Is.Null);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
    }
}
