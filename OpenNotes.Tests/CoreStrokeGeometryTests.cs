using System.Collections.Generic;
using System.Linq;
using Caelum.InkGeometry;
using Caelum.Models;
using NUnit.Framework;

namespace Caelum.Tests;

/// <summary>
/// UI-free coverage for <see cref="StrokeGeometry"/> — the eraser/selection
/// math ported out of the WPF page control. These tests pin the pure
/// capsule/spine math the WinUI ink host will run on <see cref="InkStrokeData"/>
/// directly; the WPF adapter keeps the rendered-geometry code path for the
/// live editor.
/// </summary>
[TestFixture]
public sealed class CoreStrokeGeometryTests
{
    private static List<InkPointData> Spine(params (double x, double y)[] points) =>
        points.Select(p => new InkPointData(p.x, p.y)).ToList();

    // ------------------------------------------------------------------
    // Bounds
    // ------------------------------------------------------------------

    [Test]
    public void GetBounds_CoversAllPointsAndHonoursInflate()
    {
        var bounds = StrokeGeometry.GetBounds(new List<PointD>
        {
            new(10, 20), new(50, 5), new(30, 40)
        });

        Assert.Multiple(() =>
        {
            Assert.That(bounds.Left, Is.EqualTo(10));
            Assert.That(bounds.Top, Is.EqualTo(5));
            Assert.That(bounds.Right, Is.EqualTo(50));
            Assert.That(bounds.Bottom, Is.EqualTo(40));
        });

        var inflated = StrokeGeometry.GetBounds(new List<PointD> { new(10, 20), new(50, 40) }, inflate: 3);
        Assert.Multiple(() =>
        {
            Assert.That(inflated.Left, Is.EqualTo(7));
            Assert.That(inflated.Top, Is.EqualTo(17));
            Assert.That(inflated.Right, Is.EqualTo(53));
            Assert.That(inflated.Bottom, Is.EqualTo(43));
        });
    }

    [Test]
    public void GetBounds_EmptyAndSpineVariantsReturnEmptyRect()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.GetBounds(new List<PointD>()), Is.EqualTo(new RectD(0, 0, 0, 0)));
            Assert.That(StrokeGeometry.GetSpineBounds(new List<InkPointData>()), Is.EqualTo(new RectD(0, 0, 0, 0)));
            Assert.That(StrokeGeometry.GetSpineBounds(Spine((4, 6)), inflate: 2),
                Is.EqualTo(new RectD(2, 4, 4, 4)));
        });
    }

    // ------------------------------------------------------------------
    // Hit testing (widened stroke outline approximation)
    // ------------------------------------------------------------------

    [Test]
    public void HitTestStroke_PointInsideWidenedOutlineHits()
    {
        var spine = Spine((0, 50), (100, 50));

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.HitTestStroke(spine, new PointD(50, 54), radius: 5), Is.True,
                "A point inside the widened stroke outline must hit.");
            Assert.That(StrokeGeometry.HitTestStroke(spine, new PointD(50, 50), radius: 5), Is.True);
        });
    }

    [Test]
    public void HitTestStroke_PointOutsideWidenedOutlineMisses()
    {
        var spine = Spine((0, 50), (100, 50));

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.HitTestStroke(spine, new PointD(50, 56), radius: 5), Is.False);
            Assert.That(StrokeGeometry.HitTestStroke(spine, new PointD(120, 50), radius: 5), Is.False,
                "A point beyond the segment end cap must miss.");
        });
    }

    [Test]
    public void HitTestClosedOrBounds_ClosedShapeUsesPolygonInterior()
    {
        // Triangle (10,10)-(110,110) as produced by BuildShapeOutline.
        var outline = StrokeGeometry.BuildShapeOutline(
            InkShapeKind.Triangle, new PointD(10, 10), new PointD(110, 110));
        var bounds = StrokeGeometry.GetBounds(outline);

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.HitTestClosedOrBounds(outline, bounds, new PointD(60, 60)), Is.True,
                "The triangle interior must hit.");
            Assert.That(StrokeGeometry.HitTestClosedOrBounds(outline, bounds, new PointD(15, 15)), Is.False,
                "Inside the bounds but outside the triangle must NOT hit — no bounds fallback for closed shapes.");
        });
    }

    [Test]
    public void HitTestClosedOrBounds_OpenStrokeFallsBackToBounds()
    {
        var spine = new List<PointD> { new(20, 20), new(20, 120), new(120, 120) };
        var bounds = StrokeGeometry.GetBounds(spine);

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.HitTestClosedOrBounds(spine, bounds, new PointD(90, 45)), Is.True);
            Assert.That(StrokeGeometry.HitTestClosedOrBounds(spine, bounds, new PointD(140, 45)), Is.False);
        });
    }

    // ------------------------------------------------------------------
    // Eraser hit + split
    // ------------------------------------------------------------------

    [Test]
    public void EraserHitsStroke_DiagonalWhoseBoundsOnlyOverlapMisses()
    {
        // Mirrors the pinned WPF rule: bounds overlap is never the decision.
        var spine = Spine((0, 0), (100, 100));
        var eraserPath = new List<PointD> { new(10, 90) };

        var bounds = StrokeGeometry.GetSpineBounds(spine, inflate: 10);
        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.CreateEraserRects(eraserPath, 20)[0]
                    .IntersectsWith(bounds), Is.True,
                "The eraser rect really does overlap the inflated bounds — prefilter would pass.");
            Assert.That(StrokeGeometry.EraserHitsStroke(spine, eraserPath, hitRadius: 10), Is.False,
                "The spine itself stays far away, so the eraser must not touch.");
        });
    }

    [Test]
    public void EraserHitsStroke_CrossingPathHits()
    {
        var spine = Spine((0, 50), (100, 50));

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.EraserHitsStroke(
                spine, new List<PointD> { new(50, 50) }, hitRadius: 10), Is.True);
            Assert.That(StrokeGeometry.EraserHitsStroke(
                spine, new List<PointD> { new(25, 20), new(75, 80) }, hitRadius: 10), Is.True,
                "A swept path must hit even when both endpoints are far away.");
        });
    }

    [Test]
    public void SplitStrokeAtEraser_SplitsSparseLineAtCrossing()
    {
        var spine = Spine((0, 50), (100, 50));

        var fragments = StrokeGeometry.SplitStrokeAtEraser(
            spine, new List<PointD> { new(50, 50) }, hitRadius: 10);

        Assert.That(fragments, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(fragments[0][0], Is.EqualTo(new InkPointData(0, 50)));
            Assert.That(fragments[0][^1].X, Is.EqualTo(40).Within(1e-9));
            Assert.That(fragments[0][^1].Y, Is.EqualTo(50).Within(1e-9));
            Assert.That(fragments[1][0].X, Is.EqualTo(60).Within(1e-9));
            Assert.That(fragments[1][0].Y, Is.EqualTo(50).Within(1e-9));
            Assert.That(fragments[1][^1], Is.EqualTo(new InkPointData(100, 50)));
        });
    }

    [Test]
    public void SplitStrokeAtEraser_SweptSegmentCutsBetweenUpdates()
    {
        var spine = Spine((0, 50), (100, 50));

        // Eraser travels (50,0) -> (50,100): both endpoints miss the ink but
        // the swept capsule crosses it, so the split must still happen.
        var fragments = StrokeGeometry.SplitStrokeAtEraser(
            spine, new List<PointD> { new(50, 0), new(50, 100) }, hitRadius: 10);

        Assert.That(fragments, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(fragments[0][^1].X, Is.EqualTo(40).Within(1e-9));
            Assert.That(fragments[1][0].X, Is.EqualTo(60).Within(1e-9));
        });
    }

    [Test]
    public void SplitStrokeAtEraser_InterpolatesCutPointPressure()
    {
        var spine = new List<InkPointData>
        {
            new(0, 50, 0.2f),
            new(100, 50, 1.0f)
        };

        var fragments = StrokeGeometry.SplitStrokeAtEraser(
            spine, new List<PointD> { new(50, 50) }, hitRadius: 10);

        Assert.That(fragments, Has.Count.EqualTo(2));
        // Cut at t=0.4: pressure = 0.2 + (1.0-0.2)*0.4 = 0.52
        Assert.That(fragments[0][^1].Pressure, Is.EqualTo(0.52f).Within(0.001f));
    }

    [Test]
    public void SplitStrokeAtEraser_MultiSegmentKeepsBothSides()
    {
        // Dense-ish polyline crossed once: the tail vertices survive on the
        // right fragment, the head vertices on the left.
        var spine = Spine((0, 50), (30, 50), (70, 50), (100, 50));

        var fragments = StrokeGeometry.SplitStrokeAtEraser(
            spine, new List<PointD> { new(50, 50) }, hitRadius: 10);

        Assert.That(fragments, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(fragments[0].Select(p => p.X), Is.EqualTo(new[] { 0.0, 30.0, 40.0 }).Within(1e-9));
            Assert.That(fragments[1].Select(p => p.X), Is.EqualTo(new[] { 60.0, 70.0, 100.0 }).Within(1e-9));
        });
    }

    [Test]
    public void SplitStrokeAtEraser_MissReturnsSingleUnchangedFragment()
    {
        var spine = Spine((0, 50), (100, 50));

        var fragments = StrokeGeometry.SplitStrokeAtEraser(
            spine, new List<PointD> { new(50, 200) }, hitRadius: 10);

        Assert.That(fragments, Has.Count.EqualTo(1));
        Assert.That(fragments[0], Is.EqualTo(spine));
    }

    [Test]
    public void SplitStrokeAtEraser_FullCoverageErasesEverything()
    {
        var spine = Spine((0, 50), (100, 50));

        var fragments = StrokeGeometry.SplitStrokeAtEraser(
            spine, new List<PointD> { new(50, 50) }, hitRadius: 200);

        Assert.That(fragments, Is.Empty);
    }

    [Test]
    public void SplitStrokeAtEraser_DegenerateInputsAreSafe()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.SplitStrokeAtEraser(
                new List<InkPointData>(), new List<PointD> { new(0, 0) }, 10), Is.Empty);
            Assert.That(StrokeGeometry.SplitStrokeAtEraser(
                Spine((50, 50)), new List<PointD> { new(50, 50) }, 10), Is.Empty,
                "A single point inside the eraser is fully erased.");
            Assert.That(StrokeGeometry.SplitStrokeAtEraser(
                Spine((50, 200)), new List<PointD> { new(50, 50) }, 10), Has.Count.EqualTo(1),
                "A single point outside the eraser survives.");
            Assert.That(StrokeGeometry.EraserHitsStroke(
                new List<InkPointData>(), new List<PointD> { new(0, 0) }, 10), Is.False);
        });
    }

    // ------------------------------------------------------------------
    // Eraser rects + hidden-ink intersection
    // ------------------------------------------------------------------

    [Test]
    public void CreateEraserRects_CentresSquareStampsOnEachPoint()
    {
        var rects = StrokeGeometry.CreateEraserRects(
            new List<PointD> { new(10, 20), new(30, 40) }, eraserSize: 10);

        Assert.That(rects, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(rects[0], Is.EqualTo(new RectD(5, 15, 10, 10)));
            Assert.That(rects[1], Is.EqualTo(new RectD(25, 35, 10, 10)));
        });
    }

    [Test]
    public void HiddenInkIntersectsEraser_PointInsideInflatedRectHits()
    {
        var mask = new HiddenInkAnnotation
        {
            Size = 12,
            Points = new List<double[]> { new[] { 50d, 50d } }
        };
        var rects = StrokeGeometry.CreateEraserRects(new List<PointD> { new(50, 50) }, eraserSize: 20);

        Assert.That(StrokeGeometry.HiddenInkIntersectsEraser(mask, rects, eraserSize: 20), Is.True);
    }

    [Test]
    public void HiddenInkIntersectsEraser_SegmentCrossingRectHitsButFarMisses()
    {
        var crossing = new HiddenInkAnnotation
        {
            Size = 4,
            Points = new List<double[]> { new[] { 0d, 50d }, new[] { 100d, 50d } }
        };
        var far = new HiddenInkAnnotation
        {
            Size = 4,
            Points = new List<double[]> { new[] { 0d, 200d }, new[] { 100d, 200d } }
        };
        var rects = StrokeGeometry.CreateEraserRects(new List<PointD> { new(50, 50) }, eraserSize: 20);

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.HiddenInkIntersectsEraser(crossing, rects, 20), Is.True,
                "A mask segment crossing the inflated rect counts even without a vertex inside.");
            Assert.That(StrokeGeometry.HiddenInkIntersectsEraser(far, rects, 20), Is.False);
            Assert.That(StrokeGeometry.HiddenInkIntersectsEraser(
                new HiddenInkAnnotation { Points = new List<double[]>() }, rects, 20), Is.False);
        });
    }

    // ------------------------------------------------------------------
    // Lasso / marquee selection
    // ------------------------------------------------------------------

    [Test]
    public void IsPointInPolygon_RayCastInsideAndOutside()
    {
        var triangle = new List<PointD> { new(0, 0), new(100, 0), new(50, 100) };

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.IsPointInPolygon(triangle, new PointD(50, 50)), Is.True);
            Assert.That(StrokeGeometry.IsPointInPolygon(triangle, new PointD(5, 90)), Is.False);
        });
    }

    [Test]
    public void IsStrokeInsideRect_MarqueeContainmentAndSeventyPercentRule()
    {
        var spine = new List<PointD> { new(10, 10), new(50, 10), new(90, 10) };
        var bounds = StrokeGeometry.GetBounds(spine);

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.IsStrokeInsideRect(
                new RectD(0, 0, 100, 100), spine, bounds), Is.True,
                "Full containment short-circuits.");
            Assert.That(StrokeGeometry.IsStrokeInsideRect(
                new RectD(5, 5, 50, 50), spine, bounds), Is.False,
                "Only 2 of 3 points inside (0.667 < 0.7) must not select.");
        });
    }

    [Test]
    public void IsStrokeInsideRect_SeventyPercentBoundary()
    {
        var spine = new List<PointD>
        {
            new(10, 10), new(20, 10), new(30, 10), new(40, 10), new(50, 10),
            new(60, 10), new(70, 10), new(80, 10), new(90, 10), new(200, 10)
        };
        var bounds = StrokeGeometry.GetBounds(spine);
        // Rect covers x∈[0,100]: 9 of 10 points inside = 0.9 ≥ 0.7 → inside.
        Assert.That(StrokeGeometry.IsStrokeInsideRect(
            new RectD(0, 0, 100, 100), spine, bounds), Is.True);
        // Rect covers x∈[0,60]: 6 of 10 = 0.6 < 0.7 → outside.
        Assert.That(StrokeGeometry.IsStrokeInsideRect(
            new RectD(0, 0, 60, 100), spine, bounds), Is.False);
    }

    [Test]
    public void IsStrokeInsidePolygon_LassoAroundStrokeSelects()
    {
        var lasso = new List<PointD>
        {
            new(40, 40), new(160, 40), new(170, 100), new(160, 160),
            new(40, 160), new(30, 100), new(40, 40)
        };
        var ellipse = StrokeGeometry.BuildShapeOutline(
            InkShapeKind.Ellipse, new PointD(50, 50), new PointD(150, 150));
        var bounds = StrokeGeometry.GetBounds(ellipse);

        Assert.That(StrokeGeometry.IsStrokeInsidePolygon(lasso, ellipse, bounds), Is.True);
    }

    // ------------------------------------------------------------------
    // Shape outlines / constraints / dashed paths
    // ------------------------------------------------------------------

    [Test]
    public void BuildShapeOutline_RectangleClosesAndLineStaysOpen()
    {
        var rect = StrokeGeometry.BuildShapeOutline(
            InkShapeKind.Rectangle, new PointD(10, 20), new PointD(60, 80));
        var line = StrokeGeometry.BuildShapeOutline(
            InkShapeKind.Line, new PointD(10, 20), new PointD(60, 80));
        var ellipse = StrokeGeometry.BuildShapeOutline(
            InkShapeKind.Ellipse, new PointD(10, 20), new PointD(60, 80));

        Assert.Multiple(() =>
        {
            Assert.That(rect, Has.Count.EqualTo(5));
            Assert.That(rect[0], Is.EqualTo(rect[^1]));
            Assert.That(line, Has.Count.EqualTo(2));
            Assert.That(ellipse, Has.Count.EqualTo(65));
            Assert.That(ellipse[^1].X, Is.EqualTo(ellipse[0].X).Within(1e-9));
            Assert.That(ellipse[^1].Y, Is.EqualTo(ellipse[0].Y).Within(1e-9),
                "The parametric ellipse closes to within float noise.");
        });
    }

    [Test]
    public void ConstrainShapeEndpoints_ShiftSnapsLineTo45Degrees()
    {
        var snapped = StrokeGeometry.ConstrainShapeEndpoints(
            new PointD(0, 0), new PointD(100, 20), InkShapeKind.Line, isShift: true);
        Assert.That(snapped.Y / snapped.X, Is.EqualTo(0).Within(1e-9),
            "A shallow drag snaps to horizontal.");

        var squared = StrokeGeometry.ConstrainShapeEndpoints(
            new PointD(0, 0), new PointD(100, 40), InkShapeKind.Rectangle, isShift: true);
        Assert.That(squared, Is.EqualTo(new PointD(100, 100)),
            "Shift+rectangle becomes a square on the longer axis.");

        var free = StrokeGeometry.ConstrainShapeEndpoints(
            new PointD(0, 0), new PointD(100, 20), InkShapeKind.Line, isShift: false);
        Assert.That(free, Is.EqualTo(new PointD(100, 20)));
    }

    [Test]
    public void BuildDashedPolyline_CarriesPhaseAcrossCorners()
    {
        var parts = StrokeGeometry.BuildDashedPolyline(
            new List<PointD> { new(0, 0), new(15, 0), new(15, 20) },
            dashLength: 20, gapLength: 5);

        Assert.Multiple(() =>
        {
            Assert.That(parts, Has.Count.EqualTo(2));
            Assert.That(parts[0], Has.Count.EqualTo(3));
            Assert.That(parts[0][1], Is.EqualTo(new PointD(15, 0)));
            Assert.That(parts[0][2], Is.EqualTo(new PointD(15, 5)));
            Assert.That(parts[1][0], Is.EqualTo(new PointD(15, 10)));
        });
    }

    // ------------------------------------------------------------------
    // Scribble shape recognition
    // ------------------------------------------------------------------

    [Test]
    public void TryRecognizeShape_OpenNearlyStraightStrokeBecomesLine()
    {
        var scribble = new List<PointD>();
        for (int i = 0; i <= 20; i++)
            scribble.Add(new PointD(i * 5, 50 + (i % 3) - 1)); // tiny jitter

        Assert.That(StrokeGeometry.TryRecognizeShape(scribble, out var outline), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(outline, Has.Count.EqualTo(2));
            Assert.That(outline[0], Is.EqualTo(scribble[0]));
            Assert.That(outline[1], Is.EqualTo(scribble[^1]));
        });
    }

    [Test]
    public void TryRecognizeShape_RoughRectangleBecomesRectangle()
    {
        // Hand-drawn-ish axis-aligned rectangle, closed, ~8 points per side.
        var scribble = new List<PointD>();
        for (int i = 0; i <= 10; i++) scribble.Add(new PointD(10 + i * 8, 10 + (i % 2)));
        for (int i = 1; i <= 10; i++) scribble.Add(new PointD(90 + (i % 2), 10 + i * 8));
        for (int i = 1; i <= 10; i++) scribble.Add(new PointD(90 - i * 8, 90 + (i % 2)));
        for (int i = 1; i <= 10; i++) scribble.Add(new PointD(10 + (i % 2), 90 - i * 8));

        Assert.That(StrokeGeometry.TryRecognizeShape(scribble, out var outline), Is.True);
        Assert.That(outline, Has.Count.EqualTo(5));
        var bounds = StrokeGeometry.GetBounds(outline);
        Assert.Multiple(() =>
        {
            Assert.That(bounds.Left, Is.EqualTo(10).Within(1.0));
            Assert.That(bounds.Top, Is.EqualTo(10).Within(1.0));
            Assert.That(bounds.Right, Is.EqualTo(92).Within(2.0));
            Assert.That(bounds.Bottom, Is.EqualTo(92).Within(2.0));
        });
    }

    [Test]
    public void TryRecognizeShape_RoughCircleBecomesEllipse()
    {
        var scribble = new List<PointD>();
        for (int i = 0; i < 48; i++)
        {
            double t = 2 * System.Math.PI * i / 48;
            double wobble = 1.0 + 0.05 * System.Math.Sin(3 * t);
            scribble.Add(new PointD(
                100 + 40 * wobble * System.Math.Cos(t),
                100 + 40 * wobble * System.Math.Sin(t)));
        }

        Assert.That(StrokeGeometry.TryRecognizeShape(scribble, out var outline), Is.True);
        Assert.That(outline, Has.Count.EqualTo(65), "An ellipse outline is a 64-segment closed polygon.");
    }

    [Test]
    public void TryRecognizeShape_RejectsZScribbleAndTinyStroke()
    {
        var zigzag = new List<PointD>();
        for (int i = 0; i <= 12; i++)
            zigzag.Add(new PointD(i * 10, (i % 2) * 60));

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.TryRecognizeShape(zigzag, out _), Is.False);
            Assert.That(StrokeGeometry.TryRecognizeShape(
                new List<PointD> { new(0, 0), new(1, 1), new(2, 0) }, out _), Is.False,
                "A 3-point scribble is rejected by the MinRecognizedShapePoints gate.");
        });
    }

    // ------------------------------------------------------------------
    // Stroke post-processing transforms
    // ------------------------------------------------------------------

    [Test]
    public void SimulateInkFlow_SlowThickFastThin_AndStationaryNull()
    {
        var spine = new List<InkPointData>
        {
            new(0, 0), new(1, 0), new(100, 0)
        };

        var result = StrokeGeometry.SimulateInkFlow(spine);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result[0].Pressure, Is.GreaterThan(result[2].Pressure),
                "The slow first segment must render thicker than the fast second one.");
            Assert.That(result[0].Pressure, Is.LessThanOrEqualTo(1.0f));
            Assert.That(result[2].Pressure, Is.GreaterThanOrEqualTo(0.25f));
            Assert.That(StrokeGeometry.SimulateInkFlow(Spine((5, 5), (5, 5), (5, 5))), Is.Null,
                "A stationary stroke has nothing to simulate.");
        });
    }

    [Test]
    public void SmoothPoints_MovingAverageKeepsPressureAndShape()
    {
        var spine = new List<InkPointData>
        {
            new(0, 0, 0.4f), new(10, 10, 0.6f), new(20, 0, 0.8f)
        };

        var smoothed = StrokeGeometry.SmoothPoints(spine, level: 1);

        Assert.That(smoothed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(smoothed, Has.Count.EqualTo(3));
            Assert.That(smoothed[1].X, Is.EqualTo(10).Within(1e-9));
            Assert.That(smoothed[1].Y, Is.EqualTo(10.0 / 3).Within(1e-9),
                "The centre point averages its ±1 neighbours (window clamped at ends).");
            Assert.That(smoothed[1].Pressure, Is.EqualTo(0.6f),
                "Pressure always comes from the original centre point.");
            Assert.That(StrokeGeometry.SmoothPoints(Spine((0, 0), (10, 10)), level: 1), Is.Null);
            Assert.That(StrokeGeometry.SmoothPoints(spine, level: 0), Is.Null);
        });
    }

    [Test]
    public void TryGetStraightEndpoints_RejectsTapDot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.TryGetStraightEndpoints(
                Spine((5, 5), (5.005, 5.005)), out _, out _), Is.False);
            Assert.That(StrokeGeometry.TryGetStraightEndpoints(
                Spine((0, 0), (100, 100)), out var first, out var last), Is.True);
            Assert.That(first, Is.EqualTo(new InkPointData(0, 0)));
            Assert.That(last, Is.EqualTo(new InkPointData(100, 100)));
        });
    }

    [Test]
    public void ConstrainPointsToRuler_CrossingStrokeClipsAtEdge()
    {
        // Ruler body: y ∈ [100,140] between x ∈ [0,200]. Stroke dives through.
        var result = StrokeGeometry.ConstrainPointsToRuler(
            Spine((50, 50), (50, 150)),
            new PointD(0, 100), new PointD(200, 100),
            new PointD(0, 140), new PointD(200, 140),
            snapTolerance: 24);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[^1], Is.EqualTo(new InkPointData(50, 100, result[^1].Pressure)),
                "The stroke must stop exactly on the ruler edge it crossed.");
        });
    }

    [Test]
    public void ConstrainPointsToRuler_InsideBodyRejectsAndNearEdgeSnaps()
    {
        var inside = StrokeGeometry.ConstrainPointsToRuler(
            Spine((50, 120), (60, 120)),
            new PointD(0, 100), new PointD(200, 100),
            new PointD(0, 140), new PointD(200, 140),
            snapTolerance: 24);
        Assert.That(inside, Is.Null, "Ink inside the ruler body produces nothing.");

        var nearEdge = Spine((10, 90), (190, 92));
        var snapped = StrokeGeometry.ConstrainPointsToRuler(
            nearEdge,
            new PointD(0, 100), new PointD(200, 100),
            new PointD(0, 140), new PointD(200, 140),
            snapTolerance: 24);
        Assert.That(snapped, Is.Not.Null.And.Not.SameAs(nearEdge));
        Assert.Multiple(() =>
        {
            Assert.That(snapped[0].Y, Is.EqualTo(100).Within(1e-9));
            Assert.That(snapped[1].Y, Is.EqualTo(100).Within(1e-9));
            Assert.That(snapped[0].Pressure, Is.EqualTo(nearEdge[0].Pressure));
        });

        var far = Spine((10, 10), (190, 10));
        var unchanged = StrokeGeometry.ConstrainPointsToRuler(
            far,
            new PointD(0, 100), new PointD(200, 100),
            new PointD(0, 140), new PointD(200, 140),
            snapTolerance: 24);
        Assert.That(unchanged, Is.SameAs(far), "A stroke far from the ruler stays untouched.");
    }

    // ------------------------------------------------------------------
    // Segment/rect intersection + ink data round trip
    // ------------------------------------------------------------------

    [Test]
    public void SegmentIntersectsRect_EndpointsCrossingsAndMisses()
    {
        var rect = new RectD(10, 10, 20, 20);

        Assert.Multiple(() =>
        {
            Assert.That(StrokeGeometry.SegmentIntersectsRect(
                new PointD(15, 15), new PointD(50, 50), rect), Is.True,
                "Endpoint inside counts.");
            Assert.That(StrokeGeometry.SegmentIntersectsRect(
                new PointD(0, 20), new PointD(50, 20), rect), Is.True,
                "A through-crossing with no vertex inside counts.");
            Assert.That(StrokeGeometry.SegmentIntersectsRect(
                new PointD(0, 0), new PointD(5, 5), rect), Is.False);
        });
    }

    [Test]
    public void InkStrokeData_ShapeIdentityRoundTripsIntoFields()
    {
        var data = new InkStrokeData
        {
            Points = Spine((0, 0), (10, 0)),
            R = 1, G = 2, B = 3, A = 200,
            Size = 6, IsHighlighter = true, FitToCurve = false
        };
        data.ApplyShapeIdentity(new ShapeStrokeIdentity("g1", "DashedLine", 3, true));

        var identity = data.GetShapeIdentity();
        Assert.Multiple(() =>
        {
            Assert.That(identity.GroupId, Is.EqualTo("g1"));
            Assert.That(identity.Kind, Is.EqualTo("DashedLine"));
            Assert.That(identity.PartIndex, Is.EqualTo(3));
            Assert.That(identity.IsDashed, Is.True);
            Assert.That(data.ShapeGroupId, Is.EqualTo("g1"));
            Assert.That(data.IsDashedShape, Is.True);
        });
    }
}
