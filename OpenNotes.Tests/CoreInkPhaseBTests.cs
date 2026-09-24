using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caelum.Ink;
using Caelum.InkGeometry;
using Caelum.Models;
using NUnit.Framework;

namespace Caelum.Tests;

/// <summary>
/// Task 7 Phase B headless coverage — the UI-free half of the WinUI ink
/// tools: selection transform math, lasso/marquee containment, hidden-ink
/// store + eraser hits, shape commit identity/dash baking, laser fade
/// timing, ruler constraints and the mixed-operation undo ledger.
/// Everything exercised here is what <c>PdfPageControl</c>/<c>EditorPage</c>
/// call; the tests pin the WPF-parity numbers so the WinUI port can't
/// silently drift.
/// </summary>
[TestFixture]
public sealed class CoreInkPhaseBTests
{
    private static List<InkPointData> Spine(params (double x, double y)[] points) =>
        points.Select(p => new InkPointData(p.x, p.y)).ToList();

    private static List<PointD> Pts(params (double x, double y)[] points) =>
        points.Select(p => new PointD(p.x, p.y)).ToList();

    private static InkStrokeData Stroke(
        List<InkPointData> points = null, double size = 4.0) => new()
    {
        Points = points ?? Spine((0, 0), (10, 0), (20, 0)),
        R = 10, G = 20, B = 30, A = 255,
        Size = size,
    };

    private static HiddenInkAnnotation Mask(
        string id = "mask1", double[][] points = null) => new()
    {
        Id = id,
        Points = (points ?? new[]
        {
            new[] { 0.0, 0.0 },
            new[] { 100.0, 0.0 },
            new[] { 100.0, 100.0 },
        }).ToList(),
    };

    // ------------------------------------------------------------------
    // Selection transform math
    // ------------------------------------------------------------------

    [Test]
    public void TranslateSpinePoints_MovesEveryPointAndPreservesPressure()
    {
        var spine = new List<InkPointData>
        {
            new(0, 0, 0.3f), new(10, 5, 0.8f), new(20, 10, 1.0f),
        };

        StrokeGeometry.TranslateSpinePoints(spine, 7.5, -2.5);

        Assert.That(spine[0].X, Is.EqualTo(7.5).Within(1e-9));
        Assert.That(spine[0].Y, Is.EqualTo(-2.5).Within(1e-9));
        Assert.That(spine[2].X, Is.EqualTo(27.5).Within(1e-9));
        Assert.That(spine[2].Y, Is.EqualTo(7.5).Within(1e-9));
        Assert.That(spine[1].Pressure, Is.EqualTo(0.8f).Within(1e-6));
    }

    [Test]
    public void ScaleSpinePoints_ScalesAboutAnchor_AndResetsPressure()
    {
        var anchor = new PointD(10, 10);
        var spine = new List<InkPointData>
        {
            new(10, 10, 0.9f),   // on the anchor — must not move
            new(20, 10, 0.4f),
            new(10, 30, 0.2f),
        };

        StrokeGeometry.ScaleSpinePoints(spine, 2.0, anchor);

        Assert.That(spine[0].X, Is.EqualTo(10).Within(1e-9));
        Assert.That(spine[0].Y, Is.EqualTo(10).Within(1e-9));
        Assert.That(spine[1].X, Is.EqualTo(30).Within(1e-9));
        Assert.That(spine[1].Y, Is.EqualTo(10).Within(1e-9));
        Assert.That(spine[2].X, Is.EqualTo(10).Within(1e-9));
        Assert.That(spine[2].Y, Is.EqualTo(50).Within(1e-9));
        // WPF parity: the InkCanvas forced a uniform pressure after scaling.
        Assert.That(spine[1].Pressure, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(spine[2].Pressure, Is.EqualTo(0.5f).Within(1e-6));
    }

    [Test]
    public void RotateSpinePoints_Rotates90Degrees_PreservesPressure()
    {
        var center = new PointD(10, 10);
        var spine = new List<InkPointData> { new(20, 10, 0.7f) };

        StrokeGeometry.RotateSpinePoints(spine, 90, center);

        Assert.That(spine[0].X, Is.EqualTo(10).Within(1e-6));
        Assert.That(spine[0].Y, Is.EqualTo(20).Within(1e-6));
        Assert.That(spine[0].Pressure, Is.EqualTo(0.7f).Within(1e-6));
    }

    [Test]
    public void ScalePoint_UniformScaleAboutAnchor()
    {
        var scaled = StrokeGeometry.ScalePoint(new PointD(30, 10), 0.5, new PointD(10, 10));
        Assert.That(scaled.X, Is.EqualTo(20).Within(1e-9));
        Assert.That(scaled.Y, Is.EqualTo(10).Within(1e-9));
    }

    [Test]
    public void GetRenderedStrokeBounds_InflatesSpineByHalfWidth()
    {
        // Uniform pressure 0.5 → rendered half width = size * 0.5 = 2.
        var stroke = Stroke(size: 4.0);
        var bounds = StrokeGeometry.GetRenderedStrokeBounds(stroke);

        Assert.That(bounds.X, Is.EqualTo(-2).Within(1e-9));
        Assert.That(bounds.Y, Is.EqualTo(-2).Within(1e-9));
        Assert.That(bounds.Right, Is.EqualTo(22).Within(1e-9));
        Assert.That(bounds.Bottom, Is.EqualTo(2).Within(1e-9));
    }

    [Test]
    public void GetSelectionBounds_UnionsAllRenderedStrokes()
    {
        var a = Stroke(Spine((0, 0), (10, 0)), size: 2.0);
        var b = Stroke(Spine((50, 40), (60, 40)), size: 2.0);

        var bounds = StrokeGeometry.GetSelectionBounds(new[] { a, b });

        Assert.That(bounds.X, Is.LessThan(0));
        Assert.That(bounds.Right, Is.GreaterThan(60));
        Assert.That(bounds.Bottom, Is.GreaterThan(40));
        Assert.That(StrokeGeometry.GetSelectionBounds(Array.Empty<InkStrokeData>()).Width,
            Is.EqualTo(0));
    }

    [Test]
    public void GetRotateHandlePoint_Sits22DipsAboveTopCenter()
    {
        var handle = StrokeGeometry.GetRotateHandlePoint(new RectD(10, 20, 40, 30));
        Assert.That(handle.X, Is.EqualTo(30).Within(1e-9));
        Assert.That(handle.Y, Is.EqualTo(-2).Within(1e-9));
    }

    [Test]
    public void GetOppositeCorner_ReturnsDiagonalAnchor()
    {
        var bounds = new RectD(10, 20, 40, 30); // TL(10,20) TR(50,20) BL(10,50) BR(50,30? no: 50,50)
        // handleIndex: 0=TL→BR, 1=TR→BL, 2=BL→TR, 3=BR→TL
        Assert.That(StrokeGeometry.GetOppositeCorner(bounds, 0), Is.EqualTo(new PointD(50, 50)));
        Assert.That(StrokeGeometry.GetOppositeCorner(bounds, 1), Is.EqualTo(new PointD(10, 50)));
        Assert.That(StrokeGeometry.GetOppositeCorner(bounds, 2), Is.EqualTo(new PointD(50, 20)));
        Assert.That(StrokeGeometry.GetOppositeCorner(bounds, 3), Is.EqualTo(new PointD(10, 20)));
    }

    [Test]
    public void TryGetResizeHandleIndex_HitsOnlyThe8x8CornerRects()
    {
        var bounds = new RectD(100, 100, 200, 100);

        // Centre of the TL handle rect (handle is 8x8 centred on the corner).
        Assert.That(StrokeGeometry.TryGetResizeHandleIndex(
            bounds, new PointD(100, 100), out int index), Is.True);
        Assert.That(index, Is.EqualTo(0));
        Assert.That(StrokeGeometry.TryGetResizeHandleIndex(
            bounds, new PointD(300, 200), out index), Is.True);
        Assert.That(index, Is.EqualTo(3));
        // Middle of a long edge — no handle there.
        Assert.That(StrokeGeometry.TryGetResizeHandleIndex(
            bounds, new PointD(200, 100), out index), Is.False);
        Assert.That(index, Is.EqualTo(-1));
    }

    // ------------------------------------------------------------------
    // Lasso / marquee containment
    // ------------------------------------------------------------------

    [Test]
    public void IsStrokeInsidePolygon_FullyBoundedStroke_Selects()
    {
        var lasso = new List<PointD>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100),
        };
        var spine = Pts((40, 40), (50, 50), (60, 60));
        var bounds = new RectD(40, 40, 20, 20);

        Assert.That(StrokeGeometry.IsStrokeInsidePolygon(lasso, spine, bounds), Is.True);
    }

    [Test]
    public void IsStrokeInsidePolygon_SixtyPercentPointRule()
    {
        var lasso = new List<PointD>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100),
        };
        // 3 of 5 points inside = 60% → selected even though the bounds stick out.
        var spine = Pts((10, 10), (20, 20), (30, 30), (200, 200), (300, 300));
        var bounds = new RectD(10, 10, 290, 290);

        Assert.That(StrokeGeometry.IsStrokeInsidePolygon(lasso, spine, bounds), Is.True);

        // 2 of 5 inside = 40% → not selected.
        var mostlyOut = Pts((10, 10), (20, 20), (200, 200), (300, 300), (400, 400));
        Assert.That(StrokeGeometry.IsStrokeInsidePolygon(
            lasso, mostlyOut, new RectD(10, 10, 390, 390)), Is.False);
    }

    [Test]
    public void IsStrokeInsideRect_FullContainment_OrSeventyPercent()
    {
        var marquee = new RectD(0, 0, 100, 100);

        Assert.That(StrokeGeometry.IsStrokeInsideRect(
            marquee, Pts((10, 10), (20, 20)), new RectD(10, 10, 10, 10)), Is.True);

        // 4 of 5 inside (80%) while the bounds overflow → still selected.
        var spine = Pts((10, 10), (20, 20), (30, 30), (40, 40), (500, 500));
        Assert.That(StrokeGeometry.IsStrokeInsideRect(
            marquee, spine, new RectD(10, 10, 490, 490)), Is.True);

        // 3 of 5 inside (60%) → below the 70% marquee threshold.
        var lessIn = Pts((10, 10), (20, 20), (30, 30), (500, 500), (600, 600));
        Assert.That(StrokeGeometry.IsStrokeInsideRect(
            marquee, lessIn, new RectD(10, 10, 590, 590)), Is.False);
    }

    [Test]
    public void IsContainerInsidePolygon_CenterOrTwoCorners()
    {
        var lasso = new List<PointD>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100),
        };

        // Rect fully inside.
        Assert.That(StrokeGeometry.IsContainerInsidePolygon(
            lasso, new RectD(10, 10, 20, 20)), Is.True);
        // Only the centre lands inside (no corner does) → still selected.
        Assert.That(StrokeGeometry.IsContainerInsidePolygon(
            lasso, new RectD(60, -10, 20, 120)), Is.True);
        // Far outside.
        Assert.That(StrokeGeometry.IsContainerInsidePolygon(
            lasso, new RectD(200, 200, 10, 10)), Is.False);
    }

    [Test]
    public void HitTestClosedOrBounds_ClosedUsesPolygon_OpenUsesBounds()
    {
        // Closed triangle — the interior hits, but a bounds point outside
        // the triangle region must NOT (closed shapes use the polygon, not
        // their bounding box).
        var triangle = new List<PointD>
        {
            new(0, 0), new(100, 0), new(50, 50), new(0, 0),
        };
        var bounds = new RectD(0, 0, 100, 50);
        Assert.That(StrokeGeometry.HitTestClosedOrBounds(
            triangle, bounds, new PointD(50, 20)), Is.True);
        Assert.That(StrokeGeometry.HitTestClosedOrBounds(
            triangle, bounds, new PointD(10, 45)), Is.False);

        // Same point set without the closing point → bounds test, so the
        // off-triangle bounds point counts as a hit.
        var open = new List<PointD> { new(0, 0), new(100, 0), new(50, 50) };
        Assert.That(StrokeGeometry.HitTestClosedOrBounds(
            open, bounds, new PointD(10, 45)), Is.True);
    }

    // ------------------------------------------------------------------
    // Hidden ink store + eraser + undo actions
    // ------------------------------------------------------------------

    [Test]
    public void HiddenInkStore_SanitizesDefaultsAndKeepsIdsUnique()
    {
        var store = new HiddenInkStore();
        var mask = new HiddenInkAnnotation
        {
            Id = "dup",
            Size = 0,             // → 28
            A = 0,                // → 255
            RevealDurationMs = -5 // → default
        };
        var added = store.AddQuiet(mask);

        Assert.That(added.Size, Is.EqualTo(28.0));
        Assert.That(added.A, Is.EqualTo(255));
        Assert.That(added.RevealDurationMs,
            Is.EqualTo(HiddenInkRevealState.DefaultRevealDurationMs));

        // A second mask with the same Id gets a fresh unique one.
        var clone = new HiddenInkAnnotation { Id = "dup" };
        store.AddQuiet(clone);
        Assert.That(clone.Id, Is.Not.EqualTo("dup"));
        Assert.That(store.Count, Is.EqualTo(2));
    }

    [Test]
    public void HiddenInkStore_IndexOfMatchesById_NotInstance()
    {
        var store = new HiddenInkStore();
        store.AddQuiet(Mask("a"));
        store.AddQuiet(Mask("b"));

        // A different instance carrying the same Id resolves the same slot.
        Assert.That(store.IndexOf(Mask("b")), Is.EqualTo(1));
        Assert.That(store.IndexOf(Mask("missing")), Is.EqualTo(-1));
        Assert.That(store.RemoveQuiet(Mask("a")), Is.True);
        Assert.That(store.IndexOf(Mask("b")), Is.EqualTo(0));
    }

    [Test]
    public void HiddenInkStore_InsertQuiet_ClampsIndexAndRaisesChanged()
    {
        var store = new HiddenInkStore();
        int changes = 0;
        store.Changed += (_, __) => changes++;

        store.InsertQuiet(99, Mask("x")); // beyond end → appended
        Assert.That(store.IndexOf(Mask("x")), Is.EqualTo(0));
        Assert.That(changes, Is.EqualTo(1));

        store.Clear();
        Assert.That(store.Count, Is.EqualTo(0));
        Assert.That(changes, Is.EqualTo(2));
    }

    [Test]
    public void HiddenInkIntersectsEraser_DetectsTouchAndMiss()
    {
        var mask = Mask(points: new List<double[]>
        {
            new[] { 100.0, 100.0 },
            new[] { 140.0, 100.0 },
        }.ToArray());

        // Eraser rect right on a mask point.
        var hit = new List<RectD> { new(95, 95, 10, 10) };
        Assert.That(StrokeGeometry.HiddenInkIntersectsEraser(mask, hit, 4.0), Is.True);

        // Eraser rect between the two mask points → segment crossing counts.
        var mid = new List<RectD> { new(115, 98, 10, 4) };
        Assert.That(StrokeGeometry.HiddenInkIntersectsEraser(mask, mid, 4.0), Is.True);

        // Far away → no hit.
        var miss = new List<RectD> { new(500, 500, 10, 10) };
        Assert.That(StrokeGeometry.HiddenInkIntersectsEraser(mask, miss, 4.0), Is.False);

        // Empty/degenerate annotation never hits.
        Assert.That(StrokeGeometry.HiddenInkIntersectsEraser(
            new HiddenInkAnnotation(), hit, 4.0), Is.False);
    }

    [Test]
    public async Task HiddenInkRemovedAction_RestoresAtCapturedIndex()
    {
        var store = new HiddenInkStore();
        var first = store.AddQuiet(Mask("first"));
        var second = store.AddQuiet(Mask("second"));
        var third = store.AddQuiet(Mask("third"));

        int index = store.IndexOf(second);
        store.RemoveQuiet(second);
        var action = new HiddenInkRemovedAction(store, second, index);

        await action.UndoAsync();
        Assert.That(store.Count, Is.EqualTo(3));
        Assert.That(store.IndexOf(second), Is.EqualTo(1)); // middle slot back
        Assert.That(store.IndexOf(third), Is.EqualTo(2));

        await action.RedoAsync();
        Assert.That(store.Count, Is.EqualTo(2));
        Assert.That(store.IndexOf(first), Is.EqualTo(0));
        Assert.That(store.IndexOf(third), Is.EqualTo(1));
    }

    [Test]
    public async Task HiddenInksRemovedAction_RestoresAllAtCapturedIndices()
    {
        var store = new HiddenInkStore();
        var a = store.AddQuiet(Mask("a"));
        var b = store.AddQuiet(Mask("b"));
        var c = store.AddQuiet(Mask("c"));

        // Erase gesture removes b then c (indices captured before removal).
        var entries = new List<(HiddenInkAnnotation Item, int Index)>
        {
            (b, 1), (c, 2),
        };
        store.RemoveQuiet(c);
        store.RemoveQuiet(b);
        var action = new HiddenInksRemovedAction(store, entries);

        await action.UndoAsync();
        Assert.That(store.IndexOf(a), Is.EqualTo(0));
        Assert.That(store.IndexOf(b), Is.EqualTo(1));
        Assert.That(store.IndexOf(c), Is.EqualTo(2));

        await action.RedoAsync();
        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(store.IndexOf(a), Is.EqualTo(0));
    }

    [Test]
    public async Task HiddenInkAddedAction_UndoRemoves_RedoReadds()
    {
        var store = new HiddenInkStore();
        var mask = store.AddQuiet(Mask("m"));
        var action = new HiddenInkAddedAction(store, mask);

        await action.UndoAsync();
        Assert.That(store.Count, Is.EqualTo(0));
        await action.RedoAsync();
        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(store.Items[0].Id, Is.EqualTo("m"));
    }

    // ------------------------------------------------------------------
    // Shape commit
    // ------------------------------------------------------------------

    [Test]
    public void BuildShapeStrokes_Line_SingleStrokeWithGroupIdentity()
    {
        var strokes = ShapeStrokeFactory.BuildShapeStrokes(
            InkShapeKind.Line, new PointD(0, 0), new PointD(100, 50),
            10, 20, 30, 255, 4.0, dashed: false);

        Assert.That(strokes.Count, Is.EqualTo(1));
        var stroke = strokes[0];
        Assert.That(stroke.ShapeGroupId, Is.Not.Empty);
        Assert.That(stroke.ShapeKind, Is.EqualTo(nameof(InkShapeKind.Line)));
        Assert.That(stroke.ShapePartIndex, Is.EqualTo(0));
        Assert.That(stroke.IsDashedShape, Is.False);
        Assert.That(stroke.FitToCurve, Is.False);
        Assert.That(stroke.IgnorePressure, Is.True);
        Assert.That(stroke.R, Is.EqualTo(10));
        Assert.That(stroke.Size, Is.EqualTo(4.0));
        Assert.That(stroke.Points.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void BuildShapeStrokes_Arrow_TwoPartsShareGroupId()
    {
        var strokes = ShapeStrokeFactory.BuildShapeStrokes(
            InkShapeKind.Arrow, new PointD(0, 0), new PointD(100, 0),
            0, 0, 0, 255, 4.0, dashed: false);

        Assert.That(strokes.Count, Is.EqualTo(2)); // shaft + head
        Assert.That(strokes[0].ShapeGroupId, Is.EqualTo(strokes[1].ShapeGroupId));
        Assert.That(strokes[0].ShapeKind, Is.EqualTo(nameof(InkShapeKind.Arrow)));
        Assert.That(strokes.Select(s => s.ShapePartIndex),
            Is.EqualTo(new[] { 0, 1 }));
        Assert.That(strokes.All(s => !s.IsDashedShape), Is.True);
    }

    [Test]
    public void BuildShapeStrokes_Dashed_BakesMultipleSegmentsWithSharedGroup()
    {
        var strokes = ShapeStrokeFactory.BuildShapeStrokes(
            InkShapeKind.Line, new PointD(0, 0), new PointD(200, 0),
            0, 0, 0, 255, 4.0, dashed: true);

        // A 200-DIP line with the shape dash pattern must produce > 1 baked dash.
        Assert.That(strokes.Count, Is.GreaterThan(1));
        Assert.That(strokes.Select(s => s.ShapeGroupId).Distinct().Count(), Is.EqualTo(1));
        Assert.That(strokes.All(s => s.IsDashedShape), Is.True);
        Assert.That(strokes.Select(s => s.ShapePartIndex),
            Is.EqualTo(Enumerable.Range(0, strokes.Count).ToArray()));
    }

    [Test]
    public void BuildShapeStrokes_DashedLineKind_IsDashedEvenWithoutFlag()
    {
        var strokes = ShapeStrokeFactory.BuildShapeStrokes(
            InkShapeKind.DashedLine, new PointD(0, 0), new PointD(200, 0),
            0, 0, 0, 255, 4.0, dashed: false);

        Assert.That(strokes.Count, Is.GreaterThan(1));
        Assert.That(strokes.All(s => s.IsDashedShape), Is.True);
        Assert.That(strokes[0].ShapeKind, Is.EqualTo(nameof(InkShapeKind.DashedLine)));
    }

    [Test]
    public void BuildShapeStrokes_SegmentConstraint_DropsNullSegments()
    {
        // Arrow produces shaft (2 pts) + head (3 pts); keeping only the
        // 2-point segment proves a null return drops the head.
        var strokes = ShapeStrokeFactory.BuildShapeStrokes(
            InkShapeKind.Arrow, new PointD(0, 0), new PointD(100, 0),
            0, 0, 0, 255, 4.0, dashed: false,
            segmentConstraint: seg => seg.Count == 2 ? seg : null);

        Assert.That(strokes.Count, Is.EqualTo(1));
        Assert.That(strokes[0].ShapePartIndex, Is.EqualTo(0));
    }

    [Test]
    public void BuildShapeStrokes_ClosedPolygon_ProducesClosedOutline()
    {
        var strokes = ShapeStrokeFactory.BuildShapeStrokes(
            InkShapeKind.Rectangle, new PointD(0, 0), new PointD(50, 30),
            0, 0, 0, 255, 4.0, dashed: false);

        Assert.That(strokes.Count, Is.EqualTo(1));
        var pts = strokes[0].Points;
        Assert.That(pts.Count, Is.EqualTo(5)); // 4 corners + closing point
        Assert.That(pts[0].X, Is.EqualTo(pts[^1].X).Within(1e-9));
        Assert.That(pts[0].Y, Is.EqualTo(pts[^1].Y).Within(1e-9));
    }

    [Test]
    public void IsOpenKind_MatchesRulerSnappableShapes()
    {
        Assert.That(ShapeStrokeFactory.IsOpenKind(InkShapeKind.Line), Is.True);
        Assert.That(ShapeStrokeFactory.IsOpenKind(InkShapeKind.Arrow), Is.True);
        Assert.That(ShapeStrokeFactory.IsOpenKind(InkShapeKind.DashedLine), Is.True);
        Assert.That(ShapeStrokeFactory.IsOpenKind(InkShapeKind.Rectangle), Is.False);
        Assert.That(ShapeStrokeFactory.IsOpenKind(InkShapeKind.Ellipse), Is.False);
    }

    // ------------------------------------------------------------------
    // Laser fade
    // ------------------------------------------------------------------

    [Test]
    public void LaserFade_HoldsThenFadesLinearly()
    {
        Assert.That(LaserInkFade.GetOpacity(0.0, animate: true), Is.EqualTo(1.0));
        Assert.That(LaserInkFade.GetOpacity(
            LaserInkFade.HoldSeconds, animate: true), Is.EqualTo(1.0));

        double mid = LaserInkFade.HoldSeconds + LaserInkFade.FadeSeconds / 2;
        Assert.That(LaserInkFade.GetOpacity(mid, animate: true),
            Is.EqualTo(0.5).Within(1e-9));

        double end = LaserInkFade.HoldSeconds + LaserInkFade.FadeSeconds;
        Assert.That(LaserInkFade.GetOpacity(end, animate: true), Is.EqualTo(0.0));
        Assert.That(LaserInkFade.GetOpacity(end + 10, animate: true), Is.EqualTo(0.0));

        Assert.That(LaserInkFade.IsExpired(end, animate: true), Is.True);
        Assert.That(LaserInkFade.IsExpired(end - 0.001, animate: true), Is.False);
    }

    [Test]
    public void LaserFade_NoAnimation_ExpiresImmediately()
    {
        Assert.That(LaserInkFade.GetOpacity(0.0, animate: false), Is.EqualTo(0.0));
        Assert.That(LaserInkFade.IsExpired(0.0, animate: false), Is.True);
    }

    [Test]
    public void LaserFade_Constants_MatchWpfTimings()
    {
        Assert.That(LaserInkFade.HoldSeconds, Is.EqualTo(0.15));
        Assert.That(LaserInkFade.FadeSeconds, Is.EqualTo(0.9));
        Assert.That(LaserInkFade.MaxLivePolylines, Is.EqualTo(60));
        Assert.That(LaserInkFade.StrokeThickness, Is.EqualTo(3.0));
        // WPF laser red.
        Assert.That((LaserInkFade.ColorR, LaserInkFade.ColorG, LaserInkFade.ColorB),
            Is.EqualTo((0xFF, 0x3B, 0x30)));
    }

    // ------------------------------------------------------------------
    // Ruler constraint
    // ------------------------------------------------------------------

    // Axis-aligned 200×40 ruler: top edge y=0, bottom edge y=40, x∈[0,200].
    private static readonly PointD TopA = new(0, 0);
    private static readonly PointD TopB = new(200, 0);
    private static readonly PointD BottomA = new(0, 40);
    private static readonly PointD BottomB = new(200, 40);

    [Test]
    public void ConstrainPointsToRuler_StrokeStartingInside_ReturnsNull()
    {
        var spine = Spine((50, 20), (60, 20), (70, 20)); // inside the body
        var result = StrokeGeometry.ConstrainPointsToRuler(
            spine, TopA, TopB, BottomA, BottomB, snapTolerance: 12.0);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ConstrainPointsToRuler_StrokeCrossingIn_ClipsAtFirstIntersection()
    {
        // Starts above the ruler, dives through the top edge into the body.
        var spine = Spine((50, -20), (50, 20));
        var result = StrokeGeometry.ConstrainPointsToRuler(
            spine, TopA, TopB, BottomA, BottomB, snapTolerance: 12.0);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[^1].Y, Is.EqualTo(0).Within(1e-6)); // clipped at y=0
        Assert.That(result[^1].X, Is.EqualTo(50).Within(1e-6));
    }

    [Test]
    public void ConstrainPointsToRuler_StrokeNearEdge_SnapsOntoEdge()
    {
        // Parallel to the top edge, 4 DIPs above it → snaps to y=0.
        var spine = Spine((20, -4), (80, -4), (180, -4));
        var result = StrokeGeometry.ConstrainPointsToRuler(
            spine, TopA, TopB, BottomA, BottomB, snapTolerance: 12.0);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result.All(p => Math.Abs(p.Y) < 1e-6), Is.True);
        // X range preserved by the projection.
        Assert.That(result[0].X, Is.EqualTo(20).Within(1e-6));
        Assert.That(result[^1].X, Is.EqualTo(180).Within(1e-6));
    }

    [Test]
    public void ConstrainPointsToRuler_FarStroke_Unchanged()
    {
        var spine = Spine((20, -50), (80, -50));
        var result = StrokeGeometry.ConstrainPointsToRuler(
            spine, TopA, TopB, BottomA, BottomB, snapTolerance: 12.0);

        Assert.That(result, Is.SameAs(spine));
    }

    // ------------------------------------------------------------------
    // Undo actions / mixed-operation ordering
    // ------------------------------------------------------------------

    [Test]
    public async Task InkStrokesAddedAction_UndoRemoves_RedoRestoresAtPlacements()
    {
        var store = new InkStrokeStore();
        var a = store.AddStrokeQuiet(Stroke());
        var b = store.AddStrokeQuiet(Stroke(Spine((50, 50), (60, 50))));
        var action = new InkStrokesAddedAction(store, new List<InkStrokePlacement> { a, b });

        await action.UndoAsync();
        Assert.That(store.Count, Is.EqualTo(0));

        await action.RedoAsync();
        Assert.That(store.Count, Is.EqualTo(2));
        Assert.That(store.IndexOf(a.Stroke), Is.EqualTo(0));
        Assert.That(store.IndexOf(b.Stroke), Is.EqualTo(1));
    }

    [Test]
    public async Task InkStrokesRemovedAction_UndoRestoresZOrder()
    {
        var store = new InkStrokeStore();
        var a = store.AddStrokeQuiet(Stroke());
        var b = store.AddStrokeQuiet(Stroke(Spine((50, 50), (60, 50))));
        var c = store.AddStrokeQuiet(Stroke(Spine((90, 90), (95, 90))));

        // Delete removes b + c; placements captured before removal.
        var pB = store.CaptureStrokePlacement(b.Stroke);
        var pC = store.CaptureStrokePlacement(c.Stroke);
        store.RemoveStrokeQuiet(b.Stroke);
        store.RemoveStrokeQuiet(c.Stroke);
        var action = new InkStrokesRemovedAction(store, new List<InkStrokePlacement> { pB, pC });

        await action.UndoAsync();
        Assert.That(store.Count, Is.EqualTo(3));
        Assert.That(store.IndexOf(a.Stroke), Is.EqualTo(0));
        Assert.That(store.IndexOf(b.Stroke), Is.EqualTo(1));
        Assert.That(store.IndexOf(c.Stroke), Is.EqualTo(2));

        await action.RedoAsync();
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task InkSelectionMoveAction_UndoAppliesInverseDelta()
    {
        var store = new InkStrokeStore();
        var stroke = Stroke();
        store.AddStrokeQuiet(stroke);
        var action = new InkSelectionMoveAction(store, new[] { stroke }, 15, -5);

        await action.RedoAsync(); // the drag already ran; redo repeats it
        Assert.That(stroke.Points[0].X, Is.EqualTo(15).Within(1e-9));
        Assert.That(stroke.Points[0].Y, Is.EqualTo(-5).Within(1e-9));

        await action.UndoAsync();
        Assert.That(stroke.Points[0].X, Is.EqualTo(0).Within(1e-9));
        Assert.That(stroke.Points[0].Y, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public async Task InkSelectionMoveAction_SkipsStrokesNoLongerInStore()
    {
        var store = new InkStrokeStore();
        var stroke = Stroke();
        store.AddStrokeQuiet(stroke);
        var action = new InkSelectionMoveAction(store, new[] { stroke }, 10, 10);

        // Stroke erased after the gesture — the undo/redo must be a safe no-op.
        store.RemoveStrokeQuiet(stroke);
        await action.UndoAsync();
        await action.RedoAsync();
        Assert.That(stroke.Points[0].X, Is.EqualTo(0).Within(1e-9));
        Assert.That(store.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task InkSelectionResizeAction_ScalesPointsAndSize_InvertsOnUndo()
    {
        var store = new InkStrokeStore();
        var stroke = Stroke(size: 4.0);
        store.AddStrokeQuiet(stroke);
        var anchor = new PointD(0, 0);
        var action = new InkSelectionResizeAction(store, new[] { stroke }, 2.0, anchor);

        await action.RedoAsync();
        Assert.That(stroke.Points[2].X, Is.EqualTo(40).Within(1e-9));
        Assert.That(stroke.Size, Is.EqualTo(8.0).Within(1e-9));

        await action.UndoAsync();
        Assert.That(stroke.Points[2].X, Is.EqualTo(20).Within(1e-9));
        Assert.That(stroke.Size, Is.EqualTo(4.0).Within(1e-9));
    }

    [Test]
    public async Task InkSelectionRotateAction_UndoCounterRotates()
    {
        var store = new InkStrokeStore();
        var stroke = Stroke(Spine((20, 10)));
        store.AddStrokeQuiet(stroke);
        var center = new PointD(10, 10);
        var action = new InkSelectionRotateAction(store, new[] { stroke }, 90, center);

        await action.RedoAsync();
        Assert.That(stroke.Points[0].X, Is.EqualTo(10).Within(1e-6));
        Assert.That(stroke.Points[0].Y, Is.EqualTo(20).Within(1e-6));

        await action.UndoAsync();
        Assert.That(stroke.Points[0].X, Is.EqualTo(20).Within(1e-6));
        Assert.That(stroke.Points[0].Y, Is.EqualTo(10).Within(1e-6));
    }

    [Test]
    public async Task InkSelectionCrossPageMoveAction_TransfersAndRestores()
    {
        var source = new InkStrokeStore();
        var target = new InkStrokeStore();
        var stroke = Stroke();
        var placement = source.AddStrokeQuiet(stroke);

        var action = new InkSelectionCrossPageMoveAction(
            source, target, new[] { stroke },
            dx: 10, dy: 5, adjustX: 0, adjustY: -800,
            sourcePlacements: new List<InkStrokePlacement> { placement });

        Assert.That(action.ExecuteInitialTransfer(), Is.True);
        Assert.That(source.Count, Is.EqualTo(0));
        Assert.That(target.Count, Is.EqualTo(1));

        // The initial transfer only relocates ownership; the pointer delta was
        // already applied live by the page (WPF parity), so undo subtracts
        // dx+adjust AND puts the stroke back at its captured source index.
        StrokeGeometry.TranslateSpinePoints(stroke.Points, 10, 5 - 800); // simulate live drag
        await action.UndoAsync();
        Assert.That(source.Count, Is.EqualTo(1));
        Assert.That(target.Count, Is.EqualTo(0));
        Assert.That(stroke.Points[0].X, Is.EqualTo(0).Within(1e-9));
        Assert.That(stroke.Points[0].Y, Is.EqualTo(0).Within(1e-9));

        await action.RedoAsync();
        Assert.That(source.Count, Is.EqualTo(0));
        Assert.That(target.Count, Is.EqualTo(1));
        Assert.That(stroke.Points[0].X, Is.EqualTo(10).Within(1e-9));
        Assert.That(stroke.Points[0].Y, Is.EqualTo(-795).Within(1e-9));
    }

    [Test]
    public async Task InkStrokesStyleChangedAction_RestoresPerStrokeAppearance()
    {
        var store = new InkStrokeStore();
        var stroke = Stroke(size: 4.0);
        store.AddStrokeQuiet(stroke);

        var before = new Dictionary<InkStrokeData, (byte R, byte G, byte B, byte A, double Size)>
        {
            [stroke] = (10, 20, 30, 255, 4.0),
        };
        var after = new Dictionary<InkStrokeData, (byte R, byte G, byte B, byte A, double Size)>
        {
            [stroke] = (200, 100, 50, 255, 9.0),
        };
        // Apply the "after" values the way the selection action bar does.
        stroke.R = 200; stroke.G = 100; stroke.B = 50; stroke.Size = 9.0;

        var action = new InkStrokesStyleChangedAction(store, before, after);

        await action.UndoAsync();
        Assert.That((stroke.R, stroke.G, stroke.B, stroke.Size),
            Is.EqualTo((10, 20, 30, 4.0)));

        await action.RedoAsync();
        Assert.That((stroke.R, stroke.G, stroke.B, stroke.Size),
            Is.EqualTo((200, 100, 50, 9.0)));
    }

    [Test]
    public async Task MixedSequence_DrawEraseMoveHiddenInk_UndoRedoRestoresOrder()
    {
        var store = new InkStrokeStore();
        var hidden = new HiddenInkStore();
        var undo = new Stack<IUndoAction>();

        // 1) draw stroke A
        var a = Stroke();
        var pA = store.AddStrokeQuiet(a);
        undo.Push(new InkStrokesAddedAction(store, new List<InkStrokePlacement> { pA }));

        // 2) draw stroke B then erase it
        var b = Stroke(Spine((50, 50), (60, 50)));
        var pB = store.AddStrokeQuiet(b);
        undo.Push(new InkStrokesAddedAction(store, new List<InkStrokePlacement> { pB }));
        var pBBeforeErase = store.CaptureStrokePlacement(b);
        store.RemoveStrokeQuiet(b);
        undo.Push(new InkStrokesErasedAction(
            store, new List<InkStrokePlacement> { pBBeforeErase },
            new List<InkStrokePlacement>()));

        // 3) move stroke A by (30, 0) — the page applied the live delta already
        StrokeGeometry.TranslateSpinePoints(a.Points, 30, 0);
        undo.Push(new InkSelectionMoveAction(store, new[] { a }, 30, 0));

        // 4) add a hidden mask
        var mask = hidden.AddQuiet(Mask("m1"));
        undo.Push(new HiddenInkAddedAction(hidden, mask));

        // ── Undo the whole stack ──────────────────────────────────────
        await undo.Pop().UndoAsync(); // hidden add → mask gone
        Assert.That(hidden.Count, Is.EqualTo(0));

        await undo.Pop().UndoAsync(); // move → A back at x=0
        Assert.That(a.Points[0].X, Is.EqualTo(0).Within(1e-9));

        await undo.Pop().UndoAsync(); // erase → B restored
        Assert.That(store.IndexOf(b), Is.EqualTo(1));

        await undo.Pop().UndoAsync(); // add B → gone
        Assert.That(store.Count, Is.EqualTo(1));

        await undo.Pop().UndoAsync(); // add A → gone
        Assert.That(store.Count, Is.EqualTo(0));

        // ── Redo in the same order ────────────────────────────────────
        var redo = new List<IUndoAction>
        {
            new InkStrokesAddedAction(store, new List<InkStrokePlacement> { pA }),
            new InkStrokesAddedAction(store, new List<InkStrokePlacement> { pB }),
            new InkStrokesErasedAction(
                store, new List<InkStrokePlacement> { pBBeforeErase },
                new List<InkStrokePlacement>()),
            new InkSelectionMoveAction(store, new[] { a }, 30, 0),
            new HiddenInkAddedAction(hidden, mask),
        };
        foreach (var action in redo)
            await action.RedoAsync();

        Assert.That(store.Count, Is.EqualTo(1));      // A only — B erased again
        Assert.That(store.IndexOf(a), Is.EqualTo(0));
        Assert.That(a.Points[0].X, Is.EqualTo(30).Within(1e-9));
        Assert.That(hidden.Count, Is.EqualTo(1));
    }
}
