using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class ClipboardImageDecoderTests
{
    [Test]
    public void TryGetPngBytes_DecodesPngClipboardFormat()
    {
        byte[] png = CreatePng(Color.Red, 4, 3);
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(png), false);

        byte[] result = ClipboardImageDecoder.TryGetPngBytes(data);

        Assert.That(ClipboardImageDecoder.ContainsImage(data), Is.True);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.AsSpan(0, 8).ToArray(), Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
    }

    [Test]
    public void TryGetPngBytes_RasterizesEnhancedMetafile()
    {
        byte[] emf = CreateSampleEmf();
        var data = new DataObject();
        data.SetData(DataFormats.EnhancedMetafile, new MemoryStream(emf), false);

        byte[] result = ClipboardImageDecoder.TryGetPngBytes(data);

        Assert.That(ClipboardImageDecoder.ContainsImage(data), Is.True);
        Assert.That(result, Is.Not.Null);
        using var bitmap = new Bitmap(new MemoryStream(result));
        Assert.That(bitmap.Width, Is.GreaterThan(10));
        Assert.That(bitmap.Height, Is.GreaterThan(10));
    }

    [Test]
    public void TryGetPngBytes_ReturnsNullWhenClipboardHasNoImage()
    {
        var data = new DataObject();
        data.SetData(DataFormats.Text, "not an image");

        Assert.That(ClipboardImageDecoder.ContainsImage(data), Is.False);
        Assert.That(ClipboardImageDecoder.TryGetPngBytes(data), Is.Null);
    }

    private static byte[] CreatePng(Color color, int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(color);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static byte[] CreateSampleEmf()
    {
        using var reference = new Bitmap(1, 1);
        using var referenceGraphics = Graphics.FromImage(reference);
        IntPtr hdc = referenceGraphics.GetHdc();
        try
        {
            using var stream = new MemoryStream();
            using (var metafile = new Metafile(
                       stream,
                       hdc,
                       new Rectangle(0, 0, 160, 90),
                       MetafileFrameUnit.Pixel,
                       EmfType.EmfOnly))
            using (var graphics = Graphics.FromImage(metafile))
            {
                graphics.Clear(Color.White);
                using var brush = new SolidBrush(Color.FromArgb(0, 120, 215));
                graphics.FillRectangle(brush, 12, 8, 40, 70);
            }

            return stream.ToArray();
        }
        finally
        {
            referenceGraphics.ReleaseHdc(hdc);
        }
    }
}
