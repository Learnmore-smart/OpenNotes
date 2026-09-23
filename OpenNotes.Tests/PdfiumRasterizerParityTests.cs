using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Caelum.Pdf;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfiumViewer;

namespace Caelum.Tests;

/// <summary>
/// Task 3 parity gate: the Core <see cref="PdfiumRasterizer"/> (pure P/Invoke)
/// must produce byte-identical pixels and identical text/geometry answers as
/// the legacy <see cref="PdfiumViewer.PdfDocument"/> path it replaces.
/// </summary>
public class PdfiumRasterizerParityTests
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

    /// <summary>3-page fixture with vector + text content (non-trivial pixels and text layer).</summary>
    private byte[] BuildFixturePdfBytes()
    {
        using var document = new PdfSharpCore.Pdf.PdfDocument();
        for (int i = 0; i < 3; i++)
        {
            var page = document.AddPage();
            page.Width = 612;
            page.Height = 792;

            using var gfx = XGraphics.FromPdfPage(page);
            var bg = i == 1 ? XColor.FromArgb(255, 253, 249, 238) : XColors.White;
            gfx.DrawRectangle(new XSolidBrush(bg), 0, 0, 612, 792);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 30 + i * 60, 90, 160)), 40 + i * 20, 60, 200, 90);
            gfx.DrawLine(new XPen(XColor.FromArgb(255, 200, 40, 40), 2.5), 40, 200, 560, 260 + i * 30);
            gfx.DrawEllipse(new XPen(XColor.FromArgb(255, 40, 160, 80), 1.5), 300, 400, 180, 120);
            // NOTE: no XFont/DrawString here on purpose. Once any PdfService
            // static initializer installs the app's CJK-only font resolver,
            // arbitrary family names can no longer resolve — a raw standard-14
            // /Helv content stream keeps this fixture resolver-free and
            // deterministic regardless of test ordering.
            AddStandardFontText(page, document, $"Parity fixture page {i + 1}", 18 + i * 4, 60, 340 + i * 10);
        }

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Writes a text run using the PDF standard-14 Helvetica via raw content
    /// stream ops — no font resolution or embedding needed.
    /// </summary>
    private static void AddStandardFontText(
        PdfSharpCore.Pdf.PdfPage page,
        PdfSharpCore.Pdf.PdfDocument document,
        string text,
        double size,
        double x,
        double y)
    {
        var font = new PdfDictionary(document);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/Helvetica");
        font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        document.Internals.AddObject(font);

        var fonts = new PdfDictionary(document);
        fonts.Elements["/Helv"] = font.Reference;
        if (page.Elements["/Resources"] is not PdfDictionary resources)
        {
            resources = new PdfDictionary(document);
            page.Elements["/Resources"] = resources;
        }
        resources.Elements["/Font"] = fonts;

        string ops = $"BT /Helv {size.ToString(CultureInfo.InvariantCulture)} Tf " +
                     $"{x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)} Td " +
                     $"({text}) Tj ET\n";
        var content = new PdfDictionary(document);
        content.CreateStream(Encoding.Latin1.GetBytes(ops));
        document.Internals.AddObject(content);

        if (page.Elements["/Contents"] is not PdfArray contents)
        {
            contents = new PdfArray(document);
            page.Elements["/Contents"] = contents;
        }
        contents.Elements.Add(content.Reference);
    }

    private static byte[] RenderWithPdfiumViewer(byte[] pdfBytes, int pageIndex, int width, int height, int dpi)
    {
        using var stream = new MemoryStream(pdfBytes, writable: false);
        using var document = PdfiumViewer.PdfDocument.Load(stream);
        using var image = (Bitmap)document.Render(pageIndex, width, height, dpi, dpi, PdfRenderFlags.Annotations);
        var data = image.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            Assert.That(Math.Abs(data.Stride), Is.EqualTo(width * 4), "GDI+ stride assumption broke");
            var bytes = new byte[width * 4 * height];
            for (int row = 0; row < height; row++)
                Marshal_CopyRow(data.Scan0, data.Stride, row, bytes, width * 4);
            return bytes;
        }
        finally
        {
            image.UnlockBits(data);
        }
    }

    private static void Marshal_CopyRow(IntPtr scan0, int stride, int row, byte[] dest, int rowBytes)
    {
        IntPtr src = IntPtr.Add(scan0, row * stride);
        System.Runtime.InteropServices.Marshal.Copy(src, dest, row * rowBytes, rowBytes);
    }

    [Test]
    public void RenderPageBgra_IsByteIdenticalToPdfiumViewerRender()
    {
        byte[] pdfBytes = BuildFixturePdfBytes();
        using var rasterizer = new PdfiumRasterizer(new MemoryStream(pdfBytes, writable: false));

        Assert.That(rasterizer.PageCount, Is.EqualTo(3));

        foreach (int dpi in new[] { 96, 144, 192 })
        {
            for (int page = 0; page < 3; page++)
            {
                var size = rasterizer.PageSizes[page];
                int width = (int)(size.Width * dpi / 72.0f);
                int height = (int)(size.Height * dpi / 72.0f);

                byte[] expected = RenderWithPdfiumViewer(pdfBytes, page, width, height, dpi);
                var actual = rasterizer.RenderPageBgra(page, width, height);

                Assert.Multiple(() =>
                {
                    Assert.That(actual.Width, Is.EqualTo(width));
                    Assert.That(actual.Height, Is.EqualTo(height));
                    Assert.That(actual.Stride, Is.EqualTo(width * 4));
                    Assert.That(actual.Bgra.Length, Is.EqualTo(expected.Length));
                });

                string expectedHash = Convert.ToHexString(SHA256.HashData(expected));
                string actualHash = Convert.ToHexString(SHA256.HashData(actual.Bgra));
                Assert.That(actual.Bgra, Is.EqualTo(expected),
                    $"page {page} @ {dpi}dpi BGRA mismatch (expected sha {expectedHash}, actual {actualHash})");
            }
        }
    }

    [Test]
    public void DocumentMetadata_MatchesPdfiumViewer()
    {
        byte[] pdfBytes = BuildFixturePdfBytes();
        using var viewerDoc = PdfiumViewer.PdfDocument.Load(new MemoryStream(pdfBytes, writable: false));
        using var rasterizer = new PdfiumRasterizer(new MemoryStream(pdfBytes, writable: false));

        Assert.That(rasterizer.PageCount, Is.EqualTo(viewerDoc.PageCount));
        for (int i = 0; i < rasterizer.PageCount; i++)
        {
            Assert.That(rasterizer.PageSizes[i].Width, Is.EqualTo(viewerDoc.PageSizes[i].Width));
            Assert.That(rasterizer.PageSizes[i].Height, Is.EqualTo(viewerDoc.PageSizes[i].Height));
        }
    }

    [Test]
    public void PageTextAndBounds_MatchPdfiumViewer()
    {
        byte[] pdfBytes = BuildFixturePdfBytes();
        using var viewerDoc = PdfiumViewer.PdfDocument.Load(new MemoryStream(pdfBytes, writable: false));
        using var rasterizer = new PdfiumRasterizer(new MemoryStream(pdfBytes, writable: false));

        for (int page = 0; page < 3; page++)
        {
            string expectedText = viewerDoc.GetPdfText(page) ?? string.Empty;
            string actualText = rasterizer.GetPageText(page) ?? string.Empty;
            Assert.That(actualText, Is.EqualTo(expectedText), $"page {page} text");

            int length = expectedText.Length;
            if (length == 0)
                continue;

            var expectedBounds = viewerDoc.GetTextBounds(new PdfTextSpan(page, 0, length));
            var actualBounds = rasterizer.GetTextBounds(page, 0, length);
            Assert.That(actualBounds.Count, Is.EqualTo(expectedBounds.Count), $"page {page} bound count");
            for (int i = 0; i < expectedBounds.Count; i++)
            {
                var e = expectedBounds[i].Bounds;
                var a = actualBounds[i];
                Assert.Multiple(() =>
                {
                    Assert.That(a.X, Is.EqualTo(e.X).Within(0.001), $"page {page} bound {i} X");
                    Assert.That(a.Y, Is.EqualTo(e.Y).Within(0.001), $"page {page} bound {i} Y");
                    Assert.That(a.Width, Is.EqualTo(e.Width).Within(0.001), $"page {page} bound {i} W");
                    Assert.That(a.Height, Is.EqualTo(e.Height).Within(0.001), $"page {page} bound {i} H");
                });

                var expectedDevice = viewerDoc.RectangleFromPdf(page, e);
                var actualDevice = rasterizer.RectangleFromPdf(page, a);
                Assert.Multiple(() =>
                {
                    Assert.That(actualDevice.X, Is.EqualTo(expectedDevice.X), $"page {page} bound {i} devX");
                    Assert.That(actualDevice.Y, Is.EqualTo(expectedDevice.Y), $"page {page} bound {i} devY");
                    Assert.That(actualDevice.Width, Is.EqualTo(expectedDevice.Width), $"page {page} bound {i} devW");
                    Assert.That(actualDevice.Height, Is.EqualTo(expectedDevice.Height), $"page {page} bound {i} devH");
                });
            }
        }
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        byte[] pdfBytes = BuildFixturePdfBytes();
        var rasterizer = new PdfiumRasterizer(new MemoryStream(pdfBytes, writable: false));
        rasterizer.Dispose();
        Assert.DoesNotThrow(() => rasterizer.Dispose());
        Assert.Throws<ObjectDisposedException>(() => rasterizer.RenderPageBgra(0, 10, 10));
    }
}
