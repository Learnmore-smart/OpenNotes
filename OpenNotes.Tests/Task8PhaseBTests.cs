using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caelum.Ink;
using Caelum.InkGeometry;
using Caelum.Models;
using Caelum.Pdf;
using NUnit.Framework;

namespace Caelum.Tests;

/// <summary>
/// Task 8 Phase B headless coverage — the UI-free halves of the image /
/// persistent-highlight / text-markup / area-highlight / PDF-text-selection
/// port: <see cref="PdfTextSelectionGeometry"/> (the selection math the
/// WinUI PdfPageControl + EditorPage both call), the image-payload leg of
/// <see cref="AnnotationContainerTransfer"/>, the highlight undo actions,
/// and the source contract pinning the WinUI wiring so a later refactor
/// can't silently drop the pieces WPF relies on.
/// </summary>
[TestFixture]
public sealed class Task8PhaseBTests
{
    private static PdfService.PdfPageTextInfo TextInfo(
        params (int offset, char c, RectD bounds)[] chars) => new()
    {
        Text = new string(chars.Select(c => c.c).ToArray()),
        Characters = chars.Select(c => new PdfService.PdfTextCharacterInfo
        {
            Offset = c.offset,
            Character = c.c,
            Bounds = new[] { c.bounds },
            UnionBounds = c.bounds,
        }).ToList(),
    };

    private static RectD Glyph(double x, double y, double w = 8, double h = 14)
        => new(x, y, w, h);

    // ------------------------------------------------------------------
    // FindNearestTextOffset (WPF parity: padded containment wins, then
    // nearest rect under maxDistance).
    // ------------------------------------------------------------------

    [Test]
    public void FindNearestTextOffset_PaddedContainmentWinsImmediately()
    {
        var info = TextInfo(
            (0, 'a', Glyph(0, 0)),
            (1, 'b', Glyph(100, 0)));

        // 2 DIP past the right edge of glyph 0 — inside the 3-DIP pad, so
        // the far-neighbour comparison never runs.
        Assert.That(
            PdfTextSelectionGeometry.FindNearestTextOffset(
                info, new PointD(10, 7), 4.0),
            Is.EqualTo(0));
    }

    [Test]
    public void FindNearestTextOffset_NearestRectWithinMaxDistance()
    {
        var info = TextInfo(
            (0, 'a', Glyph(0, 0)),
            (1, 'b', Glyph(100, 0)));

        // 10 DIP right of glyph 0, 90 left of glyph 1 → glyph 0 wins.
        Assert.That(
            PdfTextSelectionGeometry.FindNearestTextOffset(
                info, new PointD(18, 7), 24.0),
            Is.EqualTo(0));
        Assert.That(
            PdfTextSelectionGeometry.FindNearestTextOffset(
                info, new PointD(80, 7), 24.0),
            Is.EqualTo(1));
    }

    [Test]
    public void FindNearestTextOffset_BeyondMaxDistance_ReturnsMinusOne()
    {
        var info = TextInfo((0, 'a', Glyph(0, 0)));
        Assert.That(
            PdfTextSelectionGeometry.FindNearestTextOffset(
                info, new PointD(500, 500), 24.0),
            Is.EqualTo(-1));
    }

    [Test]
    public void FindNearestTextOffset_InfiniteDistanceAlwaysAnswers()
    {
        var info = TextInfo((0, 'a', Glyph(0, 0)));
        // The drag path follows the pointer anywhere on the page.
        Assert.That(
            PdfTextSelectionGeometry.FindNearestTextOffset(
                info, new PointD(5000, 5000), double.PositiveInfinity),
            Is.EqualTo(0));
    }

    [Test]
    public void FindNearestTextOffset_EmptyOrBoundless_ReturnsMinusOne()
    {
        Assert.That(
            PdfTextSelectionGeometry.FindNearestTextOffset(
                null, new PointD(0, 0), double.PositiveInfinity),
            Is.EqualTo(-1));
        Assert.That(
            PdfTextSelectionGeometry.FindNearestTextOffset(
                new PdfService.PdfPageTextInfo(), new PointD(0, 0), double.PositiveInfinity),
            Is.EqualTo(-1));

        var boundless = new PdfService.PdfPageTextInfo
        {
            Text = "x",
            Characters = new[]
            {
                new PdfService.PdfTextCharacterInfo { Offset = 0, Character = 'x' },
            },
        };
        Assert.That(
            PdfTextSelectionGeometry.FindNearestTextOffset(
                boundless, new PointD(0, 0), double.PositiveInfinity),
            Is.EqualTo(-1));
    }

    // ------------------------------------------------------------------
    // ShouldMergeSelectionRects (35% vertical overlap + 8 DIP gap rule).
    // ------------------------------------------------------------------

    [Test]
    public void ShouldMergeSelectionRects_SameLineSmallGap_Merges()
    {
        var current = Glyph(0, 0);      // right edge at 8
        var next = Glyph(12, 1);        // gap 4 ≤ 8, vertical overlap ≈ 13/14
        Assert.That(PdfTextSelectionGeometry.ShouldMergeSelectionRects(current, next), Is.True);
    }

    [Test]
    public void ShouldMergeSelectionRects_WideGap_DoesNotMerge()
    {
        var current = Glyph(0, 0);
        var next = Glyph(40, 0);        // gap 32 > 8
        Assert.That(PdfTextSelectionGeometry.ShouldMergeSelectionRects(current, next), Is.False);
    }

    [Test]
    public void ShouldMergeSelectionRects_DifferentLine_DoesNotMerge()
    {
        var current = Glyph(0, 0);
        var next = Glyph(4, 20);        // no vertical overlap
        Assert.That(PdfTextSelectionGeometry.ShouldMergeSelectionRects(current, next), Is.False);
    }

    // ------------------------------------------------------------------
    // BuildSelectionRects.
    // ------------------------------------------------------------------

    [Test]
    public void BuildSelectionRects_MergesAdjacentSameLineGlyphs()
    {
        var info = TextInfo(
            (0, 'h', Glyph(0, 0)),
            (1, 'i', Glyph(10, 0, w: 4)),
            (2, 'x', Glyph(100, 0)));

        var rects = PdfTextSelectionGeometry.BuildSelectionRects(info, 0, 2);
        Assert.That(rects.Count, Is.EqualTo(2));
        Assert.That(rects[0].Left, Is.EqualTo(0));
        Assert.That(rects[0].Right, Is.EqualTo(14));   // h + i merged
        Assert.That(rects[1].Left, Is.EqualTo(100));   // x stays separate
    }

    [Test]
    public void BuildSelectionRects_ClampsAndNormalizesTheRange()
    {
        var info = TextInfo(
            (0, 'a', Glyph(0, 0)),
            (1, 'b', Glyph(50, 0)));

        // Reversed + out-of-range offsets still answer the same span.
        var forward = PdfTextSelectionGeometry.BuildSelectionRects(info, 0, 1);
        var reversed = PdfTextSelectionGeometry.BuildSelectionRects(info, 99, -5);
        Assert.That(reversed.Count, Is.EqualTo(forward.Count));
        Assert.That(reversed[1].Left, Is.EqualTo(50));
    }

    [Test]
    public void BuildSelectionRects_MultiBoundCharacterContributesEveryBound()
    {
        var info = new PdfService.PdfPageTextInfo
        {
            Text = "x",
            Characters = new[]
            {
                new PdfService.PdfTextCharacterInfo
                {
                    Offset = 0,
                    Character = 'x',
                    Bounds = new[] { Glyph(0, 0), Glyph(0, 30) },
                    UnionBounds = new RectD(0, 0, 8, 44),
                },
            },
        };
        var rects = PdfTextSelectionGeometry.BuildSelectionRects(info, 0, 0);
        Assert.That(rects.Count, Is.EqualTo(2));
    }

    // ------------------------------------------------------------------
    // BuildTextMarkupAnnotation — union origin + relative rects.
    // ------------------------------------------------------------------

    [Test]
    public void BuildTextMarkupAnnotation_RelativizesRectsToUnionBounds()
    {
        var rects = new List<RectD>
        {
            new(100, 200, 30, 10),
            new(140, 200, 20, 10),
            new(100, 220, 60, 10),
        };
        var markup = PdfTextSelectionGeometry.BuildTextMarkupAnnotation(
            rects, TextMarkupKind.Underline, 255, 0, 0);

        Assert.That(markup.Kind, Is.EqualTo("Underline"));
        Assert.That(markup.X, Is.EqualTo(100));
        Assert.That(markup.Y, Is.EqualTo(200));
        Assert.That(markup.R, Is.EqualTo(255));
        Assert.That(markup.Rects.Count, Is.EqualTo(3));
        Assert.That(markup.Rects[0], Is.EqualTo(new[] { 0.0, 0.0, 30.0, 10.0 }));
        Assert.That(markup.Rects[1], Is.EqualTo(new[] { 40.0, 0.0, 20.0, 10.0 }));
        Assert.That(markup.Rects[2], Is.EqualTo(new[] { 0.0, 20.0, 60.0, 10.0 }));
    }

    [Test]
    public void BuildTextMarkupAnnotation_EmptyInput_ReturnsEmptyModelAtOrigin()
    {
        var markup = PdfTextSelectionGeometry.BuildTextMarkupAnnotation(
            Array.Empty<RectD>(), TextMarkupKind.Squiggly, 1, 2, 3);
        Assert.That(markup.X, Is.EqualTo(0));
        Assert.That(markup.Y, Is.EqualTo(0));
        Assert.That(markup.Rects.Count, Is.EqualTo(0));
        Assert.That(markup.Kind, Is.EqualTo("Squiggly"));
    }

    // ------------------------------------------------------------------
    // NormalizeAreaHighlightRect + ComputeImagePlacementSize.
    // ------------------------------------------------------------------

    [Test]
    public void NormalizeAreaHighlightRect_ReverseDragYieldsPositiveSize()
    {
        var rect = PdfTextSelectionGeometry.NormalizeAreaHighlightRect(
            new PointD(80, 120), new PointD(20, 40));
        Assert.That(rect.X, Is.EqualTo(20));
        Assert.That(rect.Y, Is.EqualTo(40));
        Assert.That(rect.Width, Is.EqualTo(60));
        Assert.That(rect.Height, Is.EqualTo(80));
    }

    [Test]
    public void ComputeImagePlacementSize_ExplicitDimensionsWinVerbatim()
    {
        var (w, h) = PdfTextSelectionGeometry.ComputeImagePlacementSize(
            800, 1000, 2000, 1000, 123.0, 45.0);
        Assert.That(w, Is.EqualTo(123));
        Assert.That(h, Is.EqualTo(45));
    }

    [Test]
    public void ComputeImagePlacementSize_FitsInsideFortyPercentKeepingAspect()
    {
        // 2000×1000 px into a 800×1000 page → max 320×400 → scale 0.16.
        var (w, h) = PdfTextSelectionGeometry.ComputeImagePlacementSize(
            800, 1000, 2000, 1000, null, null);
        Assert.That(w, Is.EqualTo(320).Within(0.001));
        Assert.That(h, Is.EqualTo(160).Within(0.001));
    }

    [Test]
    public void ComputeImagePlacementSize_DegenerateInputFallsBackToOneDip()
    {
        var (w, h) = PdfTextSelectionGeometry.ComputeImagePlacementSize(
            800, 1000, 0, 0, null, null);
        Assert.That(w, Is.EqualTo(1));
        Assert.That(h, Is.EqualTo(1));
    }

    // ------------------------------------------------------------------
    // Cross-page image payload transfer + highlight undo actions.
    // ------------------------------------------------------------------

    [Test]
    public void TransferMove_CarriesImageBytesToTheReceivingHost()
    {
        var from = new FakeImageHost();
        var to = new FakeImageHost();
        var container = new object();
        var payload = new byte[] { 1, 2, 3 };
        from.Containers.Add(container);
        from.SetImageData(container, payload);

        Assert.That(AnnotationContainerTransfer.Move(from, to, container), Is.True);
        Assert.That(to.GetImageData(container), Is.EqualTo(payload));
        Assert.That(to.Containers, Does.Contain(container));
        Assert.That(from.Containers, Does.Not.Contain(container));
    }

    [Test]
    public void TransferMove_ExistingTargetImageRegistrationWins()
    {
        var from = new FakeImageHost();
        var to = new FakeImageHost();
        var container = new object();
        var targetPayload = new byte[] { 9, 9, 9 };
        from.Containers.Add(container);
        from.SetImageData(container, new byte[] { 1, 1, 1 });
        to.SetImageData(container, targetPayload);

        Assert.That(AnnotationContainerTransfer.Move(from, to, container), Is.True);
        // WPF parity: a pre-existing target registration is authoritative.
        Assert.That(to.GetImageData(container), Is.EqualTo(targetPayload));
    }

    [Test]
    public void TransferMove_NoImagePayload_StillTransfersContainer()
    {
        var from = new FakeImageHost();
        var to = new FakeImageHost();
        var container = new object();
        from.Containers.Add(container);

        Assert.That(AnnotationContainerTransfer.Move(from, to, container), Is.True);
        Assert.That(to.GetImageData(container), Is.Null);
        Assert.That(to.Containers, Does.Contain(container));
    }

    [Test]
    public async Task HighlightAddedAction_UndoRemoves_RedoRestores()
    {
        var host = new FakeImageHost();
        var highlight = new HighlightAnnotation { R = 255, G = 235, B = 59, A = 120 };
        var action = new HighlightAddedAction(host, highlight);

        await action.UndoAsync();
        Assert.That(host.Highlights, Is.Empty);
        await action.RedoAsync();
        Assert.That(host.Highlights, Does.Contain(highlight));
    }

    [Test]
    public async Task HighlightRemovedAction_UndoRestores_RedoRemoves()
    {
        var host = new FakeImageHost();
        var highlight = new HighlightAnnotation { R = 255, G = 235, B = 59, A = 120 };
        // The action models "the highlight was already removed" — the list
        // starts empty; undo restores the model, redo removes it again.
        var action = new HighlightRemovedAction(host, highlight);

        await action.UndoAsync();
        Assert.That(host.Highlights, Does.Contain(highlight));
        await action.RedoAsync();
        Assert.That(host.Highlights, Is.Empty);
    }

    [Test]
    public void DefaultHostMembers_KeepTextOnlyHostsValid()
    {
        // A host that never overrides the Phase-B members must still
        // transfer containers — GetImageData defaults to null, the
        // Set/Add/Remove defaults no-op (existing-test-host contract).
        var from = new MinimalHost();
        var to = new MinimalHost();
        var container = new object();
        from.Containers.Add(container);
        Assert.That(AnnotationContainerTransfer.Move(from, to, container), Is.True);
        Assert.That(to.Containers, Does.Contain(container));
    }

    // ------------------------------------------------------------------
    // WinUI source contract — pins the Phase-B wiring in the port.
    // ------------------------------------------------------------------

    [Test]
    public void PdfPageControlImplementsPhaseBSurface()
    {
        string page = ReadWinUi("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            // Image pipeline: decode → container → byte registry → event.
            Assert.That(page, Does.Contain("AddImageAsync"));
            Assert.That(page, Does.Contain("_imageDataById"));
            Assert.That(page, Does.Contain("ImagesChanged?.Invoke"));
            Assert.That(page, Does.Contain("GetImageData"));
            Assert.That(page, Does.Contain("SetImageData"));

            // Overlay annotations + highlight list.
            Assert.That(page, Does.Contain("AddTextMarkup"));
            Assert.That(page, Does.Contain("AddAreaHighlight"));
            Assert.That(page, Does.Contain("_highlights"));
            Assert.That(page, Does.Contain("AddHighlightAnnotation"));
            Assert.That(page, Does.Contain("RefreshHighlightsVisuals"));

            // Area-highlight drag + event.
            Assert.That(page, Does.Contain("BeginAreaHighlightDrag"));
            Assert.That(page, Does.Contain("EndAreaHighlightDrag"));
            Assert.That(page, Does.Contain("AreaHighlightCreated?.Invoke"));
            Assert.That(page, Does.Contain("AreaHighlightDragThreshold"));

            // PDF text-selection canvas contract.
            Assert.That(page, Does.Contain("SetPdfTextSelectionEnabled"));
            Assert.That(page, Does.Contain("SetPdfTextSelectionRects"));
            Assert.That(page, Does.Contain("ClearPdfTextSelection"));
            Assert.That(page, Does.Contain("PdfTextSelectionCanvas_PointerPressed"));
            Assert.That(page, Does.Contain("PdfTextSelectionCanvas_PointerMoved"));
            Assert.That(page, Does.Contain("PdfTextSelectionCanvas_PointerReleased"));
            Assert.That(page, Does.Contain("CapturePointer(e.Pointer)"));

            // Quiet paths + cross-page payload for undo/move.
            Assert.That(page, Does.Contain("IAnnotationContainerHost.GetImageData"));
            Assert.That(page, Does.Contain("IAnnotationContainerHost.SetImageData"));
            Assert.That(page, Does.Contain("IAnnotationContainerHost.AddHighlight"));
            Assert.That(page, Does.Contain("IAnnotationContainerHost.RemoveHighlight"));

            // Cleanup must cover every new registry.
            int releaseIdx = page.IndexOf("void ReleaseResources", StringComparison.Ordinal);
            Assert.That(releaseIdx, Is.GreaterThanOrEqualTo(0));
            string releaseBody = page.Substring(
                releaseIdx, page.IndexOf("\n        }", releaseIdx, StringComparison.Ordinal) - releaseIdx);
            Assert.That(releaseBody, Does.Contain("_imageDataById.Clear()"));
            Assert.That(releaseBody, Does.Contain("_highlights.Clear()"));
            Assert.That(releaseBody, Does.Contain("PdfTextSelectionCanvas.Children.Clear()"));
        });
    }

    [Test]
    public void PdfPageControlXamlKeepsThePhaseBLayerOrder()
    {
        string xaml = ReadWinUi("Controls", "PdfPageControl.xaml");

        int image = xaml.IndexOf("x:Name=\"ImageOverlayCanvas\"", StringComparison.Ordinal);
        int ink = xaml.IndexOf("x:Name=\"InkSurface\"", StringComparison.Ordinal);
        int highlights = xaml.IndexOf("x:Name=\"HighlightsCanvas\"", StringComparison.Ordinal);
        int selection = xaml.IndexOf("x:Name=\"PdfTextSelectionCanvas\"", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(image, Is.GreaterThanOrEqualTo(0));
            Assert.That(highlights, Is.GreaterThanOrEqualTo(0));
            Assert.That(selection, Is.GreaterThanOrEqualTo(0));
            // WPF z-order: images under ink, selection above highlights.
            Assert.That(image, Is.LessThan(ink));
            Assert.That(highlights, Is.LessThan(selection));
        });
    }

    [Test]
    public void InkSurfaceRoutesAreaHighlightThroughShapeDragWithoutPenOnlyBlock()
    {
        string ink = ReadWinUi("Controls", "InkSurface.cs");

        Assert.Multiple(() =>
        {
            Assert.That(ink, Does.Contain("AreaHighlight,"));
            Assert.That(ink, Does.Contain("Tool == InkSurfaceTool.AreaHighlight"));

            // wantsShape covers the tool; the inkCreation pen-only gate must
            // not (a mouse drag draws an area highlight under pen-only).
            int wantsShapeIdx = ink.IndexOf("bool wantsShape", StringComparison.Ordinal);
            Assert.That(wantsShapeIdx, Is.GreaterThanOrEqualTo(0));
            string wantsShape = ink.Substring(
                wantsShapeIdx, ink.IndexOf(';', wantsShapeIdx) - wantsShapeIdx);
            Assert.That(wantsShape, Does.Contain("InkSurfaceTool.AreaHighlight"));

            int inkCreationIdx = ink.IndexOf("bool inkCreation", StringComparison.Ordinal);
            Assert.That(inkCreationIdx, Is.GreaterThanOrEqualTo(0));
            string inkCreation = ink.Substring(
                inkCreationIdx, ink.IndexOf(';', inkCreationIdx) - inkCreationIdx);
            Assert.That(inkCreation, Does.Not.Contain("AreaHighlight"));
        });
    }

    [Test]
    public void EditorPageWiresPhaseBPipelines()
    {
        string editor = ReadWinUi("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Six-mode highlighter flyout + mode routing.
            Assert.That(editor, Does.Contain("ShowHighlighterFlyout"));
            Assert.That(editor, Does.Contain("HighlighterApplyMode.Freehand"));
            Assert.That(editor, Does.Contain("HighlighterApplyMode.TextHighlight"));
            Assert.That(editor, Does.Contain("HighlighterApplyMode.Underline"));
            Assert.That(editor, Does.Contain("HighlighterApplyMode.StrikeOut"));
            Assert.That(editor, Does.Contain("HighlighterApplyMode.Squiggly"));
            Assert.That(editor, Does.Contain("HighlighterApplyMode.AreaHighlight"));
            Assert.That(editor, Does.Contain("ActivateHighlighterModeTool"));
            Assert.That(editor, Does.Contain("Editor.Highlighter.Size"));

            // Tool routing for the two non-button tools.
            Assert.That(editor, Does.Contain("ToolType.TextHighlight"));
            Assert.That(editor, Does.Contain("ToolType.AreaHighlight"));
            Assert.That(editor, Does.Contain("CustomInkInputProcessingMode.AreaHighlight"));
            Assert.That(editor, Does.Contain("SetPdfTextSelectionEnabled"));

            // Real PDF text selection + copy.
            Assert.That(editor, Does.Contain("PageControl_PdfTextSelectionPointerPressed"));
            Assert.That(editor, Does.Contain("PageControl_PdfTextSelectionPointerMoved"));
            Assert.That(editor, Does.Contain("PageControl_PdfTextSelectionPointerReleased"));
            Assert.That(editor, Does.Contain("TryCopySelectedPdfTextToClipboard"));
            Assert.That(editor, Does.Contain("PdfTextSelectionGeometry.FindNearestTextOffset"));
            Assert.That(editor, Does.Contain("PdfTextSelectionGeometry.BuildSelectionRects"));

            // Persistent annotation commits on selection release.
            Assert.That(editor, Does.Contain("AddHighlightAnnotation"));
            Assert.That(editor, Does.Contain("HighlightAddedAction"));
            Assert.That(editor, Does.Contain("BuildTextMarkupAnnotation"));

            // Image clipboard + drop paths.
            Assert.That(editor, Does.Contain("PasteClipboardImageAsync"));
            Assert.That(editor, Does.Contain("PasteClipboardImageOrSelection"));
            Assert.That(editor, Does.Contain("ClipboardImageDecoder.TryGetPngBytesAsync"));
            Assert.That(editor, Does.Contain("EditorPage_Drop"));
            Assert.That(editor, Does.Contain("EditorPage_DragOver"));
            Assert.That(editor, Does.Contain("EditorRootGrid.AllowDrop = true"));
            Assert.That(editor, Does.Contain("EditorRootGrid.Drop += EditorPage_Drop"));

            // Undo for a committed area-highlight drag.
            Assert.That(editor, Does.Contain("PageControl_AreaHighlightCreated"));

            // Async annotation load covering every Phase-B collection.
            Assert.That(editor, Does.Contain("LoadAnnotationsIntoPagesAsync"));
            Assert.That(editor, Does.Contain("page.AddHighlight(hl)"));
            Assert.That(editor, Does.Contain("page.AddTextMarkup(markup)"));
            Assert.That(editor, Does.Contain("page.AddAreaHighlight(area)"));
            Assert.That(editor, Does.Contain("await page.AddImageAsync("));
        });
    }

    [Test]
    public void CollectAnnotationsCoversEveryPhaseBCollection()
    {
        string editor = ReadWinUi("Pages", "EditorPage.xaml.cs");
        int collectIdx = editor.IndexOf(
            "internal Dictionary<int, PageAnnotation> CollectAnnotations()", StringComparison.Ordinal);
        Assert.That(collectIdx, Is.GreaterThanOrEqualTo(0));
        int endIdx = editor.IndexOf("\n        }", collectIdx, StringComparison.Ordinal);
        string body = editor.Substring(collectIdx, endIdx - collectIdx);

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("pa.Images.Add(new ImageAnnotation"));
            Assert.That(body, Does.Contain("pa.TextMarkups.Add(copy)"));
            Assert.That(body, Does.Contain("pa.AreaHighlights.Add(new AreaHighlightAnnotation"));
            Assert.That(body, Does.Contain("Highlights = page.GetHighlights().ToList()"));
            Assert.That(body, Does.Contain("GetImageData(imageContainer)"));
            Assert.That(body, Does.Contain("ImageDataBase64 = Convert.ToBase64String"));
            Assert.That(body, Does.Contain("RotationDegrees = PdfPageControl.ReadAnnotationRotation"));
            Assert.That(body, Does.Contain("scaleX"));
        });
    }

    [Test]
    public void ClipboardImageDecoderKeepsTheThreeWpfLegs()
    {
        string decoder = ReadWinUi("Services", "ClipboardImageDecoder.cs");

        Assert.Multiple(() =>
        {
            Assert.That(decoder, Does.Contain("content.Contains(\"PNG\")"));
            Assert.That(decoder, Does.Contain("StandardDataFormats.Bitmap"));
            Assert.That(decoder, Does.Contain("EnhMetafileRasterizer.TryReadClipboardEnhMetafileBytes"));
            Assert.That(decoder, Does.Contain("EnhMetafileRasterizer.TryRasterizeToBgra"));
            Assert.That(decoder, Does.Contain("BitmapEncoder.PngEncoderId"));
        });
    }

    // ------------------------------------------------------------------
    // Fake hosts + file helper.
    // ------------------------------------------------------------------

    /// <summary>
    /// Minimal Phase-B host — tracks containers, image payloads and the
    /// highlight list the way the WinUI PdfPageControl does.
    /// </summary>
    private sealed class FakeImageHost : IAnnotationContainerHost
    {
        public HashSet<object> Containers { get; } = new();
        public Dictionary<object, byte[]> Images { get; } = new();
        public List<HighlightAnnotation> Highlights { get; } = new();

        public bool RemoveTextContainerQuiet(object container) => Containers.Remove(container);
        public void AddTextContainerQuiet(object container) => Containers.Add(container);
        public bool ContainsTextContainer(object container) => Containers.Contains(container);
        public object GetOverlayData(object container) => null!;
        public void SetOverlayData(object container, object data) { }
        public byte[] GetImageData(object container)
            => Images.TryGetValue(container, out var data) ? data : null;
        public void SetImageData(object container, byte[] data) => Images[container] = data;
        public void AddHighlight(HighlightAnnotation highlight) => Highlights.Add(highlight);
        public void RemoveHighlight(HighlightAnnotation highlight) => Highlights.Remove(highlight);
        public bool SetStickyNotePositionQuiet(object container, PointD position) => false;
        public bool SetStickyNoteTextQuiet(object container, string text) => false;
        public void SetTextContainerPositionQuiet(object container, PointD position) { }
        public void SetTextContainerBoundsQuiet(
            object container, TextBoxBounds bounds, bool? autoWidth, bool? autoHeight) { }
        public bool SetTextContentQuiet(object container, string text) => false;
        public void SetTextStyleQuiet(object container, double fontSize, byte r, byte g, byte b) { }
        public void SetTextFormatQuiet(object container, TextFormatSnapshot format) { }
        public void MoveItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double deltaX, double deltaY) { }
        public void ScaleItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double scaleFactor, PointD center) { }
        public void RotateItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double degrees, PointD center) { }
        public void ClearSelection() { }
    }

    /// <summary>Text-only host relying on every Phase-B default member.</summary>
    private sealed class MinimalHost : IAnnotationContainerHost
    {
        public HashSet<object> Containers { get; } = new();
        public bool RemoveTextContainerQuiet(object container) => Containers.Remove(container);
        public void AddTextContainerQuiet(object container) => Containers.Add(container);
        public bool ContainsTextContainer(object container) => Containers.Contains(container);
        public object GetOverlayData(object container) => null!;
        public void SetOverlayData(object container, object data) { }
        public bool SetStickyNotePositionQuiet(object container, PointD position) => false;
        public bool SetStickyNoteTextQuiet(object container, string text) => false;
        public void SetTextContainerPositionQuiet(object container, PointD position) { }
        public void SetTextContainerBoundsQuiet(
            object container, TextBoxBounds bounds, bool? autoWidth, bool? autoHeight) { }
        public bool SetTextContentQuiet(object container, string text) => false;
        public void SetTextStyleQuiet(object container, double fontSize, byte r, byte g, byte b) { }
        public void SetTextFormatQuiet(object container, TextFormatSnapshot format) { }
        public void MoveItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double deltaX, double deltaY) { }
        public void ScaleItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double scaleFactor, PointD center) { }
        public void RotateItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double degrees, PointD center) { }
        public void ClearSelection() { }
    }

    private static string ReadWinUi(params string[] segments)
    {
        // Same anchor as EditorTextStickySourceTests — walk up to the
        // solution folder containing OpenNotes.WinUI.csproj.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.WinUI", "OpenNotes.WinUI.csproj")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not locate the solution root containing OpenNotes.WinUI.");
        return File.ReadAllText(Path.Combine(
            new[] { Path.Combine(root, "OpenNotes.WinUI") }.Concat(segments).ToArray()));
    }
}
