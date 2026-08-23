using Caelum.Services;

namespace OpenNotes.Tests;

public sealed class PdfRenderPolicyTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("unknown")]
    public void NormalizeMode_InvalidValuesUseBalanced(string? value)
        => Assert.That(PdfRenderPolicy.NormalizeMode(value), Is.EqualTo(PdfRenderPolicy.Balanced));

    [Test]
    public void RetainedPages_AreBoundedAtDocumentEdges()
    {
        Assert.That(
            PdfRenderPolicy.GetRetainedPageIndices(0, 1, 20, PdfRenderPolicy.Balanced),
            Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(
            PdfRenderPolicy.GetRetainedPageIndices(18, 19, 20, PdfRenderPolicy.Balanced),
            Is.EqualTo(new[] { 17, 18, 19 }));
        Assert.That(
            PdfRenderPolicy.GetRetainedPageIndices(8, 9, 20, PdfRenderPolicy.BatterySaver),
            Is.EqualTo(new[] { 8, 9 }));
    }

    [TestCase(PdfRenderPolicy.BatterySaver, 1.35)]
    [TestCase(PdfRenderPolicy.Balanced, 2.0)]
    [TestCase(PdfRenderPolicy.BestQuality, 3.0)]
    public void CalculateRenderScale_RespectsModeCeiling(string mode, double expected)
        => Assert.That(
            PdfRenderPolicy.CalculateRenderScale(mode, 816, 1056, 8),
            Is.EqualTo(expected).Within(0.001));

    [Test]
    public void CalculateRenderScale_ConstrainsOversizedPageToByteBudget()
    {
        var profile = PdfRenderPolicy.GetProfile(PdfRenderPolicy.Balanced);
        var scale = PdfRenderPolicy.CalculateRenderScale(
            PdfRenderPolicy.Balanced,
            5000,
            5000,
            8);
        var bytes = PdfRenderPolicy.EstimateBitmapBytes(5000, 5000, scale);

        Assert.Multiple(() =>
        {
            Assert.That(scale, Is.LessThan(1));
            Assert.That(bytes, Is.LessThanOrEqualTo(profile.MaxBitmapBytes));
        });
    }

    [Test]
    public void NormalizeRequestedScale_PreservesThumbnailScale()
        => Assert.That(
            PdfRenderPolicy.NormalizeRequestedScale(0.22),
            Is.EqualTo(0.22).Within(0.0001));

    [Test]
    public void CalculateRenderDpi_UsesTrueThumbnailScaleAndSafeFallback()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PdfRenderPolicy.CalculateRenderDpi(0.22), Is.EqualTo(42));
            Assert.That(PdfRenderPolicy.CalculateRenderDpi(double.NaN), Is.EqualTo(192));
            Assert.That(PdfRenderPolicy.CalculateRenderDpi(3.0), Is.EqualTo(576));
        });
    }

    [Test]
    public void PdfServiceSupportsIdempotentAsyncResourceRelease()
        => Assert.That(
            typeof(IAsyncDisposable).IsAssignableFrom(typeof(PdfService)),
            Is.True);
}
