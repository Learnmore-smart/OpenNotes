using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using Caelum.InkGeometry;
using Caelum.Models;
using NUnit.Framework;

namespace Caelum.Tests;

/// <summary>
/// Parity corpus: <see cref="StrokeGeometry.SplitStrokeAtEraser"/> /
/// <see cref="StrokeGeometry.EraserHitsStroke"/> (square-stamp model) vs WPF
/// <see cref="Stroke.GetEraseResult(System.Collections.Generic.IEnumerable{System.Windows.Point}, StylusShape)"/>
/// with <see cref="RectangleStylusShape"/> — the exact call the WPF editor's
/// eraser path makes. Fragment spine extents are compared point-for-point.
/// Known divergence: FitToCurve resamples the WPF spine before erasing, so
/// fit-to-curve cases are asserted on fragment count only.
/// </summary>
[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class WpfCoreEraserParityTests
{
    private const double ExtentTolerance = 0.6; // DIP; quad-patch vs disc-union edge slack

    private static Stroke WpfStroke(
        IReadOnlyList<(double x, double y, float p)> points,
        double size,
        bool ignorePressure,
        bool fitToCurve = false)
    {
        return new Stroke(new StylusPointCollection(
            points.Select(p => new StylusPoint(p.x, p.y, p.p))))
        {
            DrawingAttributes = new DrawingAttributes
            {
                Width = size,
                Height = size,
                StylusTip = StylusTip.Ellipse,
                IgnorePressure = ignorePressure,
                FitToCurve = fitToCurve,
                IsHighlighter = false
            }
        };
    }

    private static List<(double minX, double maxX)> WpfFragmentXExtents(
        Stroke stroke, IReadOnlyList<Point> path, double eraserSize)
    {
        var result = stroke.GetEraseResult(path, new RectangleStylusShape(eraserSize, eraserSize));
        return result
            .Select(s =>
            {
                var xs = s.StylusPoints.Select(p => p.X).ToList();
                return (xs.Min(), xs.Max());
            })
            .OrderBy(e => e.Item1)
            .ToList();
    }

    private static List<(double minX, double maxX)> CoreFragmentXExtents(
        IReadOnlyList<(double x, double y, float p)> points,
        double strokeSize,
        bool ignorePressure,
        IReadOnlyList<Point> path,
        double eraserSize)
    {
        var spine = points.Select(p => new InkPointData(p.x, p.y, p.p)).ToList();
        var pathD = path.Select(p => new PointD(p.X, p.Y)).ToList();
        return StrokeGeometry
            .SplitStrokeAtEraser(spine, strokeSize, ignorePressure, pathD, eraserSize)
            .Select(f => (f.Min(p => p.X), f.Max(p => p.X)))
            .OrderBy(e => e.Item1)
            .ToList();
    }

    private static void AssertParity(
        string name,
        IReadOnlyList<(double x, double y, float p)> points,
        double strokeSize,
        bool ignorePressure,
        IReadOnlyList<Point> path,
        double eraserSize,
        bool fitToCurve = false)
    {
        var wpf = WpfFragmentXExtents(
            WpfStroke(points, strokeSize, ignorePressure, fitToCurve), path, eraserSize);
        var core = CoreFragmentXExtents(points, strokeSize, ignorePressure, path, eraserSize);

        Assert.That(core.Count, Is.EqualTo(wpf.Count),
            $"{name}: fragment count differs (wpf=[{string.Join("; ", wpf)}], core=[{string.Join("; ", core)}])");

        for (int i = 0; i < wpf.Count; i++)
        {
            Assert.That(core[i].minX, Is.EqualTo(wpf[i].minX).Within(ExtentTolerance),
                $"{name} frag[{i}] left extent (wpf={wpf[i].minX:0.###}, core={core[i].minX:0.###})");
            Assert.That(core[i].maxX, Is.EqualTo(wpf[i].maxX).Within(ExtentTolerance),
                $"{name} frag[{i}] right extent (wpf={wpf[i].maxX:0.###}, core={core[i].maxX:0.###})");
        }
    }

    // ------------------------------------------------------------------
    // Corpus
    // ------------------------------------------------------------------

    private static readonly (double x, double y, float p)[] HorizontalP05 =
        { (0, 50, 0.5f), (100, 50, 0.5f) };

    private static readonly (double x, double y, float p)[] HorizontalP05Dense =
    {
        (0, 50, 0.5f), (25, 50, 0.5f), (50, 50, 0.5f), (75, 50, 0.5f), (100, 50, 0.5f)
    };

    private static readonly (double x, double y, float p)[] Tapered =
        { (0, 50, 0.2f), (100, 50, 1.0f) };

    private static readonly (double x, double y, float p)[] TaperedReverse =
        { (0, 50, 1.0f), (100, 50, 0.2f) };

    private static readonly (double x, double y, float p)[] PressureStep =
    {
        (0, 50, 0.3f), (30, 50, 0.3f), (60, 50, 0.9f), (100, 50, 0.9f)
    };

    private static readonly (double x, double y, float p)[] ShallowCurve =
        { (0, 50, 0.5f), (25, 42, 0.5f), (50, 40, 0.5f), (75, 42, 0.5f), (100, 50, 0.5f) };

    private static readonly (double x, double y, float p)[] Vertical =
        { (50, 0, 0.5f), (50, 100, 0.5f) };

    private static readonly (double x, double y, float p)[] Diagonal =
        { (0, 0, 0.5f), (100, 100, 0.5f) };

    private static readonly (double x, double y, float p)[] Dot =
        { (50, 50, 0.5f) };

    [Test]
    public void SingleStamp_CenterCrossing_SplitsInTwo()
        => AssertParity("center", HorizontalP05, 4, false,
            new[] { new Point(50, 50) }, 20);

    [Test]
    public void SingleStamp_OffsetWithinReach_SplitsInTwo()
        => AssertParity("offset55", HorizontalP05, 4, false,
            new[] { new Point(50, 55) }, 20);

    [Test]
    public void SingleStamp_EdgeOfSquare_SplitsInTwo()
        => AssertParity("edge57", HorizontalP05, 4, false,
            new[] { new Point(50, 57) }, 20);

    [Test]
    public void SingleStamp_GrazingInsideDiscRange_SplitsInTwo()
        // Square bottom edge at y=51, spine at y=50, stroke half-width 2 →
        // the node disc reaches 1 DIP past the edge → still clipped.
        => AssertParity("graze61", HorizontalP05, 4, false,
            new[] { new Point(50, 61) }, 20);

    [Test]
    public void SingleStamp_BeyondReach_Misses()
        // Edge at y=53, spine at y=50: dist 3 > half-width 2 → miss.
        => AssertParity("miss63", HorizontalP05, 4, false,
            new[] { new Point(50, 63) }, 20);

    [Test]
    public void SingleStamp_IgnorePressure_SameAsP05()
        => AssertParity("ignorePressure", HorizontalP05, 4, true,
            new[] { new Point(50, 50) }, 20);

    [Test]
    public void SingleStamp_DenseSpine_SplitsInTwo()
        => AssertParity("dense", HorizontalP05Dense, 4, false,
            new[] { new Point(50, 50) }, 20);

    [Test]
    public void SweptPath_BetweenUpdates_SplitsInTwo()
        => AssertParity("swept", HorizontalP05, 4, false,
            new[] { new Point(25, 20), new Point(75, 80) }, 20);

    [Test]
    public void SweptPath_ParallelOutside_Misses()
        => AssertParity("sweptMiss", HorizontalP05, 4, false,
            new[] { new Point(25, 75), new Point(75, 75) }, 20);

    [Test]
    public void SweptPath_AlongSpine_ErasesAll()
        // Eraser drags along the stroke itself: 20-wide stamp over a 4-wide
        // stroke removes everything.
        => AssertParity("alongSpine", HorizontalP05Dense, 4, false,
            new[] { new Point(0, 50), new Point(50, 50), new Point(100, 50) }, 20);

    [Test]
    public void TwoSeparateStamps_ThreeFragments()
    {
        var wpf = WpfFragmentXExtents(
            WpfStroke(HorizontalP05Dense, 4, false),
            new[] { new Point(25, 50), new Point(75, 50) }, 10);
        var core = CoreFragmentXExtents(
            HorizontalP05Dense, 4, false,
            new[] { new Point(25, 50), new Point(75, 50) }, 10);
        Assert.That(core.Count, Is.EqualTo(wpf.Count));
        for (int i = 0; i < wpf.Count; i++)
        {
            Assert.That(core[i].minX, Is.EqualTo(wpf[i].minX).Within(ExtentTolerance));
            Assert.That(core[i].maxX, Is.EqualTo(wpf[i].maxX).Within(ExtentTolerance));
        }
    }

    [Test]
    public void Tapered_ThinEndCutsEarlier()
        // Analytic: removed while 40−100t ≤ 2.75+6t → t ≥ 0.3514 → x ≈ 35.14
        // (probe measured 35.14 / 66.76; core solves ~35.14 / ~66.7).
        => AssertParity("tapered", Tapered, 10, false,
            new[] { new Point(50, 56) }, 20);

    [Test]
    public void Tapered_ThickEndCutsLater()
        => AssertParity("taperedRev", TaperedReverse, 10, false,
            new[] { new Point(50, 56) }, 20);

    [Test]
    public void PressureStep_CutFollowsLocalWidth()
        => AssertParity("step", PressureStep, 10, false,
            new[] { new Point(45, 50) }, 10);

    [Test]
    public void FullPressure_WiderReachThanHalfPressure()
        => AssertParity("fullP", new[] { (0.0, 50.0, 1.0f), (100.0, 50.0, 1.0f) }, 4, false,
            new[] { new Point(50, 56) }, 20);

    [Test]
    public void Curve_VertexStamp_CutsPerSegment()
        => AssertParity("curve", ShallowCurve, 4, false,
            new[] { new Point(50, 45) }, 8);

    [Test]
    public void FitToCurveResample_FragmentCountMatches()
        // WPF resamples the spine when FitToCurve=true — extents may diverge;
        // count parity is the contract.
        => AssertParity("fitCurve", ShallowCurve, 4, true,
            new[] { new Point(50, 45) }, 20, fitToCurve: true);

    [Test]
    public void DotStamp_InsideSquare_Erased()
    {
        var wpf = WpfFragmentXExtents(
            WpfStroke(Dot, 4, false), new[] { new Point(52, 52) }, 20);
        var spine = new List<InkPointData> { new(50, 50, 0.5f) };
        var core = StrokeGeometry.SplitStrokeAtEraser(
            spine, 4, false, new List<PointD> { new(52, 52) }, 20);
        Assert.That(wpf, Is.Empty);
        Assert.That(core, Is.Empty);
    }

    [Test]
    public void VerticalSpine_SweptHorizontally_Splits()
    {
        var wpfResult = WpfStroke(Vertical, 4, false)
            .GetEraseResult(new[] { new Point(0, 50), new Point(100, 50) },
                new RectangleStylusShape(20, 20));
        var spine = Vertical.Select(p => new InkPointData(p.x, p.y, p.p)).ToList();
        var core = StrokeGeometry.SplitStrokeAtEraser(
            spine, 4, false,
            new List<PointD> { new(0, 50), new(100, 50) }, 20);
        Assert.That(core.Count, Is.EqualTo(wpfResult.Count));
        Assert.That(core.Count, Is.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(core[0].Max(p => p.Y), Is.EqualTo(38).Within(ExtentTolerance));
            Assert.That(core[1].Min(p => p.Y), Is.EqualTo(62).Within(ExtentTolerance));
        });
    }

    [Test]
    public void DiagonalSpine_StampOffAxis_MissesLikeWpf()
    {
        var wpf = WpfFragmentXExtents(
            WpfStroke(Diagonal, 4, false), new[] { new Point(10, 90) }, 20);
        var spine = Diagonal.Select(p => new InkPointData(p.x, p.y, p.p)).ToList();
        var core = StrokeGeometry.SplitStrokeAtEraser(
            spine, 4, false, new List<PointD> { new(10, 90) }, 20);
        // Bounds overlap but geometry misses — both return the single input.
        Assert.That(wpf.Count, Is.EqualTo(1));
        Assert.That(core.Count, Is.EqualTo(1));
    }

    [Test]
    public void DiagonalSpine_StampOnAxis_Splits()
        => AssertParity("diag", Diagonal, 4, false,
            new[] { new Point(50, 50) }, 20);

    [Test]
    public void StampAtStrokeEndpoint_LeavesOneFragment()
        => AssertParity("endpoint", HorizontalP05, 4, false,
            new[] { new Point(5, 50) }, 20);

    [Test]
    public void EraserHitsStroke_MatchesWpfHitTest()
    {
        // EraserHitsStroke is the whole-stroke/prefilter predicate — assert
        // agreement with Stroke.HitTest on the boundary ladder the probe used.
        var spine = HorizontalP05.Select(p => new InkPointData(p.x, p.y, p.p)).ToList();
        var stroke = WpfStroke(HorizontalP05, 4, false);
        var eraser = new RectangleStylusShape(20, 20);
        foreach (var cy in new[] { 55.0, 56.0, 57.0, 61.0, 63.0, 65.0 })
        {
            var path = new[] { new Point(50, cy) };
            bool wpfHit = stroke.HitTest(path, eraser);
            bool coreHit = StrokeGeometry.EraserHitsStroke(
                spine, 4, false, new List<PointD> { new(50, cy) }, 20);
            Assert.That(coreHit, Is.EqualTo(wpfHit), $"cy={cy}");
        }
    }

    [Test]
    public void EraserHitsStroke_TaperedStroke_MatchesWpfHitTest()
    {
        var spine = Tapered.Select(p => new InkPointData(p.x, p.y, p.p)).ToList();
        var stroke = WpfStroke(Tapered, 10, false);
        var eraser = new RectangleStylusShape(20, 20);
        foreach (var (cx, cy) in new[] { (50.0, 56.0), (5.0, 54.0), (5.0, 55.0), (95.0, 60.0) })
        {
            var path = new[] { new Point(cx, cy) };
            Assert.That(
                StrokeGeometry.EraserHitsStroke(spine, 10, false,
                    new List<PointD> { new(cx, cy) }, 20),
                Is.EqualTo(stroke.HitTest(path, eraser)),
                $"({cx},{cy})");
        }
    }
}
