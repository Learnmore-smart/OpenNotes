using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.Imaging;
using System.Windows.Ink;
using System.Windows.Input;
using Caelum.Models;
using Caelum.Pages;

namespace Caelum.Tests;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public sealed class ThumbnailCompositorTests
{
    [Test]
    public void LiveInkThumbnailCompositorDrawsOrdinaryStrokeOverBaseAndFreezesResult()
    {
        var assembly = typeof(EditorPage).Assembly;
        var compositorType = assembly.GetType("Caelum.Pages.ThumbnailCompositor", throwOnError: false);
        Assert.That(compositorType, Is.Not.Null,
            "The thumbnail compositor should exist before the live-ink path is wired.");

        var composite = compositorType!.GetMethod(
            "Composite",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(composite, Is.Not.Null);

        const int size = 24;
        byte[] pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }

        BitmapSource baseBitmap = BitmapSource.Create(
            size,
            size,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            size * 4);
        baseBitmap.Freeze();

        var stroke = new StrokeAnnotation
        {
            R = 220,
            G = 30,
            B = 45,
            A = 255,
            Size = 4,
            FitToCurve = false,
            Points = new List<double[]>
            {
                new[] { 4d, 4d },
                new[] { 20d, 20d }
            }
        };

        var result = composite.Invoke(null, new object[]
        {
            baseBitmap,
            new[] { stroke },
            (double)size,
            (double)size
        }) as BitmapSource;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsFrozen, Is.True);
        Assert.That(result.PixelWidth, Is.EqualTo(size));
        Assert.That(result.PixelHeight, Is.EqualTo(size));

        byte[] composedPixels = new byte[size * size * 4];
        result.CopyPixels(composedPixels, size * 4, 0);
        bool hasInkPixel = false;
        for (int i = 0; i < composedPixels.Length; i += 4)
        {
            if (composedPixels[i + 2] > 180
                && composedPixels[i + 2] > composedPixels[i]
                && composedPixels[i + 2] > composedPixels[i + 1])
            {
                hasInkPixel = true;
                break;
            }
        }

        Assert.That(hasInkPixel, Is.True,
            "The ordinary stroke must change at least one thumbnail pixel.");
    }

    [Test]
    public void ThumbnailRevisionGateInvalidatesOnlyOnePageAndRejectsStaleSessionResults()
    {
        var assembly = typeof(EditorPage).Assembly;
        var gateType = assembly.GetType("Caelum.Pages.ThumbnailRevisionGate", throwOnError: false);
        Assert.That(gateType, Is.Not.Null,
            "Page-local thumbnail revision state should exist before EditorPage integration.");

        object gate = Activator.CreateInstance(gateType!)!;
        Invoke(gateType!, gate, "BeginSession", 7);
        int pageZeroRevision = (int)Invoke(gateType!, gate, "CaptureRevision", 0)!;
        int pageOneRevision = (int)Invoke(gateType!, gate, "CaptureRevision", 1)!;

        Invoke(gateType!, gate, "InvalidatePage", 1);

        Assert.Multiple(() =>
        {
            Assert.That(Invoke(gateType!, gate, "IsCurrent", 0, 7, pageZeroRevision), Is.EqualTo(true));
            Assert.That(Invoke(gateType!, gate, "IsCurrent", 1, 7, pageOneRevision), Is.EqualTo(false));
            Assert.That(Invoke(gateType!, gate, "IsCurrent", 1, 7,
                (int)Invoke(gateType!, gate, "CaptureRevision", 1)!), Is.EqualTo(true));
            Assert.That(Invoke(gateType!, gate, "IsCurrent", 0, 6, pageZeroRevision), Is.EqualTo(false));
        });
    }

    [Test]
    public void LiveInkThumbnailCompositorUsesSourceDpiForDipScalingAndHighlighterAlpha()
    {
        var compositorType = typeof(EditorPage).Assembly
            .GetType("Caelum.Pages.ThumbnailCompositor", throwOnError: false);
        Assert.That(compositorType, Is.Not.Null);
        var composite = compositorType!.GetMethod(
            "Composite",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(composite, Is.Not.Null);

        const int pixelSize = 21;
        byte[] pixels = new byte[pixelSize * pixelSize * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }

        BitmapSource baseBitmap = BitmapSource.Create(
            pixelSize,
            pixelSize,
            42,
            42,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            pixelSize * 4);
        baseBitmap.Freeze();

        var highlighter = new StrokeAnnotation
        {
            R = 255,
            G = 20,
            B = 40,
            A = 128,
            Size = 8,
            IsHighlighter = true,
            FitToCurve = false,
            Points = new List<double[]>
            {
                new[] { 8d, 24d },
                new[] { 40d, 24d }
            }
        };

        var result = composite.Invoke(null, new object[]
        {
            baseBitmap,
            new[] { highlighter },
            48d,
            48d
        }) as BitmapSource;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.DpiX, Is.EqualTo(42).Within(0.01));
        Assert.That(result.DpiY, Is.EqualTo(42).Within(0.01));

        byte[] composedPixels = new byte[pixelSize * pixelSize * 4];
        result.CopyPixels(composedPixels, pixelSize * 4, 0);
        bool hasAlphaBlendedInk = false;
        for (int i = 0; i < composedPixels.Length; i += 4)
        {
            byte blue = composedPixels[i];
            byte green = composedPixels[i + 1];
            byte red = composedPixels[i + 2];
            if (red > 220 && green < 220 && blue < 220)
            {
                hasAlphaBlendedInk = true;
                break;
            }
        }

        Assert.That(hasAlphaBlendedInk, Is.True,
            "A 42-DPI highlighter stroke must be scaled from page DIPs and alpha-blended over the base.");
    }

    [Test]
    public void QuietStrokeMutationsRaiseThumbnailOnlyNotificationForUndoRedoDeletePaths()
    {
        var pageType = typeof(Caelum.Controls.PdfPageControl);
        var quietMutation = pageType.GetEvent("QuietStrokeMutation",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(quietMutation, Is.Not.Null,
            "Quiet history/delete mutations need a notification separate from InkMutated so thumbnails refresh without creating dirty/undo events.");

        var page = new Caelum.Controls.PdfPageControl { Width = 240, Height = 240 };
        int notifications = 0;
        EventHandler handler = (_, _) => notifications++;
        quietMutation!.AddEventHandler(page, handler);

        var stroke = new Stroke(new StylusPointCollection(new[]
        {
            new StylusPoint(20, 20),
            new StylusPoint(80, 80)
        }));

        var placement = page.AddStrokeQuiet(stroke);
        Assert.That(placement, Is.Not.Null);
        Assert.That(notifications, Is.EqualTo(1), "Redo/re-add must invalidate the affected page.");

        Assert.That(page.RemoveStrokeQuiet(placement), Is.True);
        Assert.That(notifications, Is.EqualTo(2), "Delete/undo-remove must invalidate the affected page.");

        Assert.That(page.AddStrokeQuiet(placement), Is.Not.Null);
        Assert.That(notifications, Is.EqualTo(3), "Undo/redo re-add must invalidate the affected page.");

        quietMutation.RemoveEventHandler(page, handler);
    }

    private static object? Invoke(Type type, object target, string name, params object[] args)
    {
        return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(target, args);
    }
}
