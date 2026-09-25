using Caelum.Services;

namespace Caelum.Tests;

/// <summary>
/// Behavioural half of the G1 print port: the pure page-fit/raster-DPI math
/// the WinUI GDI spooler shares with WPF's <c>CreatePrintDocument</c> fit
/// (uniform aspect-preserving scale into the printable area, centered) and
/// the bounded-raster DPI resolver.
/// </summary>
[TestFixture]
public sealed class PrintPageGeometryTests
{
    // US Letter in PDF points.
    private const double LetterW = 612.0;
    private const double LetterH = 792.0;

    [Test]
    public void ResolvePrintRenderDpiClampsToThePrinterAndTheCeiling()
    {
        double letterArea = LetterW * LetterH;

        Assert.Multiple(() =>
        {
            Assert.That(PrintPageGeometry.ResolvePrintRenderDpi(300, letterArea),
                Is.EqualTo(300), "printer DPI passes through");
            Assert.That(PrintPageGeometry.ResolvePrintRenderDpi(1200, letterArea),
                Is.EqualTo(PrintPageGeometry.MaxPrintDpi), "600 DPI cap");
            Assert.That(PrintPageGeometry.ResolvePrintRenderDpi(50, letterArea),
                Is.EqualTo(PrintPageGeometry.MinPrintDpi), "never below screen DPI");
            Assert.That(PrintPageGeometry.ResolvePrintRenderDpi(0, letterArea),
                Is.EqualTo(PrintPageGeometry.MinPrintDpi), "bad caps → floor");
        });
    }

    [Test]
    public void ResolvePrintRenderDpiShrinksWhenTheJobWouldExceedTheRasterBudget()
    {
        // 200 Letter pages ≈ 9.7e7 pt² — at 600 DPI that would rasterize to
        // ~6.8 GP of BGRA; the resolver must pull the DPI under the cap.
        double hugeArea = 200 * LetterW * LetterH;
        int dpi = PrintPageGeometry.ResolvePrintRenderDpi(600, hugeArea);

        Assert.Multiple(() =>
        {
            Assert.That(dpi, Is.LessThan(600), "budget clamp engaged");
            Assert.That(dpi, Is.GreaterThanOrEqualTo(PrintPageGeometry.MinPrintDpi),
                "never below the floor");
            // Round-trip: the resolved DPI must actually fit the budget.
            double pixels = hugeArea * (dpi / 72.0) * (dpi / 72.0);
            Assert.That(pixels, Is.LessThanOrEqualTo(PrintPageGeometry.MaxPrintRasterPixels));
        });
    }

    [Test]
    public void FitPageToPrintableAreaCentersAPortraitPage()
    {
        // 300 DPI Letter printable area is 2400×3000 px; a Letter page fits
        // height-limited and stays horizontally centered.
        var rect = PrintPageGeometry.FitPageToPrintableArea(LetterW, LetterH, 2400, 3000);

        Assert.Multiple(() =>
        {
            Assert.That(rect.Height, Is.EqualTo(3000));
            Assert.That(rect.Width, Is.EqualTo((int)System.Math.Round(LetterW * 3000.0 / LetterH)));
            Assert.That(rect.X, Is.EqualTo((2400 - rect.Width) / 2));
            Assert.That(rect.Y, Is.EqualTo(0));
        });
    }

    [Test]
    public void FitPageToPrintableAreaCentersALandscapePage()
    {
        var rect = PrintPageGeometry.FitPageToPrintableArea(LetterH, LetterW, 2400, 3000);

        Assert.Multiple(() =>
        {
            Assert.That(rect.Width, Is.EqualTo(2400), "width-limited fit");
            Assert.That(rect.X, Is.EqualTo(0));
            Assert.That(rect.Height, Is.EqualTo((int)System.Math.Round(LetterW * 2400.0 / LetterH)));
            Assert.That(rect.Y, Is.EqualTo((3000 - rect.Height) / 2));
        });
    }

    [Test]
    public void FitPageToPrintableAreaUpscalesSmallPagesIntoTheArea()
    {
        // A half-letter page must still fill the printable area — WPF's
        // Math.Min scale has no upscale cap, matching the FixedDocument fit.
        var rect = PrintPageGeometry.FitPageToPrintableArea(306, 396, 2400, 3000);

        Assert.Multiple(() =>
        {
            Assert.That(rect.Height, Is.EqualTo(3000));
            Assert.That(rect.Width, Is.LessThan(2400).And.GreaterThan(0));
            Assert.That(rect.X, Is.GreaterThan(0));
        });
    }

    [Test]
    public void FitPageToPrintableAreaGuardsDegenerateInputs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PrintPageGeometry.FitPageToPrintableArea(0, 0, 2400, 3000),
                Is.EqualTo(new PrintPageRect(0, 0, 0, 0)), "zero-size page → nothing to spool");
            Assert.That(PrintPageGeometry.FitPageToPrintableArea(LetterW, LetterH, 0, 3000),
                Is.EqualTo(new PrintPageRect(0, 0, 0, 0)), "no printable width → empty");
            Assert.That(PrintPageGeometry.FitPageToPrintableArea(LetterW, LetterH, 2400, 0),
                Is.EqualTo(new PrintPageRect(0, 0, 0, 0)), "no printable height → empty");
            // A 1-px rounding overflow must never exceed the area (the WPF
            // FixedPage relied on the clip; the GDI path clamps instead).
            var rect = PrintPageGeometry.FitPageToPrintableArea(LetterW, LetterH, 2400, 3000);
            Assert.That(rect.Width, Is.LessThanOrEqualTo(2400));
            Assert.That(rect.Height, Is.LessThanOrEqualTo(3000));
        });
    }
}
