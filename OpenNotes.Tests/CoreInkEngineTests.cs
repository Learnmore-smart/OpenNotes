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
/// Headless coverage for the Phase-A ink engine internals: outline
/// tessellation, the [x,y,p] sidecar conversion, the tokenized stroke
/// store and the undo actions that operate on it.
/// </summary>
[TestFixture]
public sealed class CoreInkEngineTests
{
    private static List<InkPointData> Spine(params (double x, double y)[] points) =>
        points.Select(p => new InkPointData(p.x, p.y)).ToList();

    private static List<InkPointData> SpineP(params (double x, double y, float p)[] points) =>
        points.Select(p => new InkPointData(p.x, p.y, p.p)).ToList();

    // ------------------------------------------------------------------
    // StrokeOutline tessellation
    // ------------------------------------------------------------------

    [Test]
    public void Outline_UniformStroke_WidthMatchesPressureLaw()
    {
        // Horizontal segment, p=0.5 → half-width = size·0.5.
        var outline = StrokeOutline.BuildFillOutline(
            Spine((0, 50), (100, 50)), size: 10, ignorePressure: false, fitToCurve: false);
        Assert.That(outline, Has.Count.GreaterThanOrEqualTo(4));
        double minY = outline.Min(p => p.Y);
        double maxY = outline.Max(p => p.Y);
        Assert.Multiple(() =>
        {
            Assert.That(minY, Is.EqualTo(45).Within(0.01));
            Assert.That(maxY, Is.EqualTo(55).Within(0.01));
        });
    }

    [Test]
    public void Outline_FullPressure_WiderThanNominal()
    {
        // p=1.0 → half = size·0.875 (diameter 1.75×).
        var outline = StrokeOutline.BuildFillOutline(
            SpineP((0, 50, 1.0f), (100, 50, 1.0f)), size: 10, ignorePressure: false, fitToCurve: false);
        Assert.That(outline.Max(p => p.Y) - outline.Min(p => p.Y), Is.EqualTo(17.5).Within(0.01));
    }

    [Test]
    public void Outline_IgnorePressure_RendersUniform()
    {
        var outline = StrokeOutline.BuildFillOutline(
            SpineP((0, 50, 0.1f), (100, 50, 1.0f)), size: 10, ignorePressure: true, fitToCurve: false);
        // Uniform effective pressure 0.5 → constant half-width 5.
        var ys = outline.Select(p => p.Y);
        Assert.That(ys.Max() - ys.Min(), Is.EqualTo(10).Within(0.01));
    }

    [Test]
    public void Outline_TaperedStroke_Tapers()
    {
        var outline = StrokeOutline.BuildFillOutline(
            SpineP((0, 50, 0.0f), (100, 50, 1.0f)), size: 10, ignorePressure: false, fitToCurve: false);
        var left = outline.Where(p => p.X < 20).Select(p => p.Y);
        var right = outline.Where(p => p.X > 80).Select(p => p.Y);
        Assert.That(right.Max() - right.Min(), Is.GreaterThan(left.Max() - left.Min()));
    }

    [Test]
    public void Outline_SinglePoint_ProducesDot()
    {
        var outline = StrokeOutline.BuildFillOutline(
            SpineP((50, 50, 0.5f)), size: 10, ignorePressure: false, fitToCurve: false);
        Assert.That(outline.Count, Is.GreaterThanOrEqualTo(8));
        Assert.That(outline.Max(p => p.Y) - outline.Min(p => p.Y), Is.EqualTo(10).Within(0.01));
        Assert.That(outline.Max(p => p.X) - outline.Min(p => p.X), Is.EqualTo(10).Within(0.01));
    }

    [Test]
    public void Outline_ClosedShape_EndCapsCoverEnds()
    {
        // The outline must extend past the spine ends by the end cap radius.
        var outline = StrokeOutline.BuildFillOutline(
            Spine((50, 50), (100, 50)), size: 10, ignorePressure: false, fitToCurve: false);
        Assert.That(outline.Min(p => p.X), Is.EqualTo(45).Within(0.01));
        Assert.That(outline.Max(p => p.X), Is.EqualTo(105).Within(0.01));
    }

    [Test]
    public void Outline_FitToCurve_Densifies()
    {
        var spine = Spine((0, 50), (50, 40), (100, 50));
        var raw = StrokeOutline.BuildFillOutline(spine, 4, false, fitToCurve: false);
        var fitted = StrokeOutline.BuildFillOutline(spine, 4, false, fitToCurve: true);
        Assert.That(fitted.Count, Is.GreaterThan(raw.Count));
    }

    [Test]
    public void Outline_DegenerateInputs_Empty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StrokeOutline.BuildFillOutline(new List<InkPointData>(), 4, false, false), Is.Empty);
            Assert.That(StrokeOutline.BuildFillOutline(null, 4, false, false), Is.Empty);
            Assert.That(StrokeOutline.BuildFillOutline(Spine((0, 0), (10, 10)), 0, false, false), Is.Empty);
        });
    }

    // ------------------------------------------------------------------
    // [x,y,p] persistence round-trips
    // ------------------------------------------------------------------

    [Test]
    public void Annotation_RoundTrip_PreservesPressure()
    {
        var stroke = new InkStrokeData
        {
            Points = SpineP((0, 50, 0.2f), (50, 60, 0.7f), (100, 50, 1.0f)),
            R = 10, G = 20, B = 30, A = 200,
            Size = 4.5,
            IsHighlighter = true,
            FitToCurve = false,
            ShapeGroupId = "g1", ShapeKind = "Arrow", ShapePartIndex = 2, IsDashedShape = true,
            IgnorePressure = true, // session-only — must not serialize
        };

        var sa = stroke.ToAnnotation();
        Assert.Multiple(() =>
        {
            Assert.That(sa.Points, Has.Count.EqualTo(3));
            Assert.That(sa.Points[0], Is.EqualTo(new[] { 0.0, 50.0, 0.2 }).Within(1e-6));
            Assert.That(sa.Points[2][2], Is.EqualTo(1.0).Within(1e-6));
            Assert.That(sa.IsHighlighter, Is.True);
            Assert.That(sa.ShapeGroupId, Is.EqualTo("g1"));
        });

        var back = InkStrokeData.FromAnnotation(sa);
        Assert.Multiple(() =>
        {
            Assert.That(back.Points.Select(p => p.Pressure),
                Is.EqualTo(new[] { 0.2f, 0.7f, 1.0f }).Within(1e-5));
            Assert.That(back.A, Is.EqualTo(200));
            Assert.That(back.IsDashedShape, Is.True);
            // IgnorePressure is a rendering choice — reloads to false.
            Assert.That(back.IgnorePressure, Is.False);
        });
    }

    [Test]
    public void Annotation_LegacyXY_LoadsWithDefaultPressure()
    {
        var sa = new StrokeAnnotation
        {
            Size = 2.0,
            Points = { new double[] { 0, 0 }, new double[] { 100, 50 } },
        };
        var stroke = InkStrokeData.FromAnnotation(sa);
        Assert.That(stroke.Points.Select(p => p.Pressure),
            Is.EqualTo(new[] { 0.5f, 0.5f }));
    }

    [Test]
    public void Annotation_MalformedPoints_Skipped()
    {
        var sa = new StrokeAnnotation
        {
            Points =
            {
                new double[] { 0, 0, 0.9 },
                new double[] { 5 },                    // too short
                new double[] { double.NaN, 5, 0.5 },   // non-finite
                new double[] { 10, 10, 3.0 },          // pressure clamps to 1
                null,
            },
        };
        var stroke = InkStrokeData.FromAnnotation(sa);
        Assert.That(stroke.Points, Has.Count.EqualTo(2));
        Assert.That(stroke.Points[1].Pressure, Is.EqualTo(1.0f));
    }

    [Test]
    public void Annotation_RoundTrip_ThroughJson_PreservesPressure()
    {
        // The sidecar round-trip is JSON — verify [x,y,p] survives
        // serialization (System.Text.Json writes double[] natively).
        var stroke = new InkStrokeData { Points = SpineP((1.5, 2.5, 0.33f)) };
        var sa = stroke.ToAnnotation();
        string json = System.Text.Json.JsonSerializer.Serialize(sa);
        var sa2 = System.Text.Json.JsonSerializer.Deserialize<StrokeAnnotation>(json);
        var back = InkStrokeData.FromAnnotation(sa2);
        Assert.Multiple(() =>
        {
            Assert.That(back.Points[0].X, Is.EqualTo(1.5));
            Assert.That(back.Points[0].Pressure, Is.EqualTo(0.33f).Within(1e-6));
        });
    }

    // ------------------------------------------------------------------
    // InkStrokeStore — tokens, placements, quiet ops
    // ------------------------------------------------------------------

    [Test]
    public void Store_TokensStable_PerInstance()
    {
        var store = new InkStrokeStore();
        var s1 = new InkStrokeData { Points = Spine((0, 0), (10, 10)) };
        var s2 = new InkStrokeData { Points = Spine((20, 20), (30, 30)) };
        store.AddStrokeQuiet(s1);
        store.AddStrokeQuiet(s2);

        var t1 = store.EnsureStrokeToken(s1);
        var t2 = store.EnsureStrokeToken(s2);
        Assert.Multiple(() =>
        {
            Assert.That(t1, Is.Not.EqualTo(Guid.Empty));
            Assert.That(t2, Is.Not.EqualTo(Guid.Empty));
            Assert.That(t1, Is.Not.EqualTo(t2));
            Assert.That(store.EnsureStrokeToken(s1), Is.EqualTo(t1), "same instance → same token");
        });
    }

    [Test]
    public void Store_PlacementCapturesIndex()
    {
        var store = new InkStrokeStore();
        var s1 = new InkStrokeData { Points = Spine((0, 0), (10, 10)) };
        var s2 = new InkStrokeData { Points = Spine((20, 20), (30, 30)) };
        store.AddStrokeQuiet(s1);
        store.AddStrokeQuiet(s2);

        var p1 = store.CaptureStrokePlacement(s1);
        var p2 = store.CaptureStrokePlacement(s2);
        Assert.Multiple(() =>
        {
            Assert.That(p1.Index, Is.EqualTo(0));
            Assert.That(p2.Index, Is.EqualTo(1));
            Assert.That(ReferenceEquals(p2, store.CaptureStrokePlacement(s2)),
                "history returns the first captured placement");
        });
    }

    [Test]
    public void Store_QuietRemoveAtIndex_RestoresAtIndex()
    {
        var store = new InkStrokeStore();
        var a = new InkStrokeData { Points = Spine((0, 0), (1, 0)) };
        var b = new InkStrokeData { Points = Spine((0, 10), (1, 10)) };
        var c = new InkStrokeData { Points = Spine((0, 20), (1, 20)) };
        store.AddStrokeQuiet(a);
        store.AddStrokeQuiet(b);
        store.AddStrokeQuiet(c);

        var pb = store.CaptureStrokePlacement(b);
        store.RemoveStrokeQuietExact(pb);
        Assert.That(store.Strokes, Has.Count.EqualTo(2));

        store.AddStrokeQuiet(pb.ForOwner(store, pb.Index));
        Assert.That(store.Strokes[1], Is.SameAs(b), "restored to original z-order slot");
        Assert.That(store.EnsureStrokeToken(b), Is.EqualTo(pb.Token), "token survives restore");
    }

    [Test]
    public void Store_ReplaceByToken_RespectsSide()
    {
        var store = new InkStrokeStore();
        var original = new InkStrokeData { Points = Spine((0, 0), (50, 0)) };
        store.AddStrokeQuiet(original);
        var token = store.EnsureStrokeToken(original);
        var originalSnapshot = store.CaptureStrokePlacement(original).Snapshot;

        // Wrong side → no-op, never appends.
        var ideal = original.Clone();
        ideal.Points = Spine((0, 0), (100, 0));
        var idealSnapshot = new StrokeReplacementSnapshot(
            token, StrokeReplacementSide.Ideal,
            ideal.Points.Select(p => new StrokeReplacementPoint(p.X, p.Y, p.Pressure)),
            ideal.R, ideal.G, ideal.B, ideal.A, ideal.Size, ideal.Size,
            ideal.IsHighlighter, ideal.FitToCurve, ideal.IgnorePressure);

        Assert.That(store.TryReplaceStrokeQuiet(
            token, StrokeReplacementSide.Ideal, idealSnapshot, out _), Is.False,
            "expectedSide mismatch → no replacement");
        Assert.That(store.Count, Is.EqualTo(1));

        Assert.That(store.TryReplaceStrokeQuiet(
            token, StrokeReplacementSide.Original, idealSnapshot, out int index), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(index, Is.EqualTo(0));
            Assert.That(store.Strokes[0].Points, Has.Count.EqualTo(2));
            Assert.That(store.Strokes[0].Points[1].X, Is.EqualTo(100));
            Assert.That(store.GetStrokeSide(token), Is.EqualTo(StrokeReplacementSide.Ideal));
        });
    }

    [Test]
    public void Store_MutationsFireQuietEvents()
    {
        var store = new InkStrokeStore();
        var kinds = new List<InkStoreMutationKind>();
        store.Mutated += (s, e) => kinds.Add(e.Kind);

        var stroke = new InkStrokeData { Points = Spine((0, 0), (1, 0)) };
        store.AddStrokeQuiet(stroke);
        store.RemoveStrokeQuiet(stroke);

        Assert.That(kinds, Is.EqualTo(new[] { InkStoreMutationKind.Added, InkStoreMutationKind.Removed }));
    }

    // ------------------------------------------------------------------
    // Undo actions
    // ------------------------------------------------------------------

    [Test]
    public async Task StrokeAdded_UndoRemoves_RedoRestoresIndex()
    {
        var store = new InkStrokeStore();
        var a = new InkStrokeData { Points = Spine((0, 0), (1, 0)) };
        var b = new InkStrokeData { Points = Spine((0, 5), (1, 5)) };
        store.AddStrokeQuiet(a);
        store.AddStrokeQuiet(b);

        var action = new InkStrokeAddedAction(store, a);
        // Undo removes a even though it sits under b.
        await action.UndoAsync();
        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(store.Strokes[0], Is.SameAs(b));

        await action.RedoAsync();
        Assert.That(store.Strokes[0], Is.SameAs(a), "redo restores at captured index");
    }

    [Test]
    public async Task StrokesErased_UndoRestoresOriginals_RedoReappliesSplit()
    {
        var store = new InkStrokeStore();
        var original = new InkStrokeData { Points = Spine((0, 50), (100, 50)) };
        store.AddStrokeQuiet(original);

        // Simulate an erase: split at the centre, capture the gesture payload.
        var fragments = StrokeGeometry.SplitStrokeAtEraser(
            original.Points, 4, false, new List<PointD> { new(50, 50) }, 20);
        Assert.That(fragments, Has.Count.EqualTo(2));

        var removedPlacement = store.CaptureStrokePlacement(original);
        store.RemoveStrokeQuiet(original);
        var addedPlacements = new List<InkStrokePlacement>();
        foreach (var frag in fragments)
        {
            var clone = original.Clone();
            clone.Points = frag;
            addedPlacements.Add(store.AddStrokeQuiet(clone));
        }
        Assert.That(store.Count, Is.EqualTo(2));

        var action = new InkStrokesErasedAction(
            store, new List<InkStrokePlacement> { removedPlacement }, addedPlacements);

        await action.UndoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(action.LastOperationSucceeded, Is.True);
            Assert.That(store.Count, Is.EqualTo(1));
            Assert.That(store.Strokes[0], Is.SameAs(original), "the original instance returns");
        });

        await action.RedoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(action.LastOperationSucceeded, Is.True);
            Assert.That(store.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task StrokesErased_UndoPreservesZOrder()
    {
        var store = new InkStrokeStore();
        var bottom = new InkStrokeData { Points = Spine((0, 50), (100, 50)) };
        var top = new InkStrokeData { Points = Spine((0, 60), (100, 60)) };
        store.AddStrokeQuiet(bottom);
        store.AddStrokeQuiet(top);

        // Erase the bottom stroke (index 0) leaving one fragment.
        var removedPlacement = store.CaptureStrokePlacement(bottom);
        store.RemoveStrokeQuiet(bottom);
        var frag = bottom.Clone();
        frag.Points = Spine((0, 50), (30, 50));
        var addedPlacement = store.AddStrokeQuiet(frag);

        var action = new InkStrokesErasedAction(
            store,
            new List<InkStrokePlacement> { removedPlacement },
            new List<InkStrokePlacement> { addedPlacement });

        await action.UndoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(store.Count, Is.EqualTo(2));
            Assert.That(store.Strokes[0], Is.SameAs(bottom), "restored below the untouched stroke");
            Assert.That(store.Strokes[1], Is.SameAs(top));
        });
    }

    [Test]
    public async Task StrokeReplaced_UndoRedoSwapsSides()
    {
        var store = new InkStrokeStore();
        var original = new InkStrokeData { Points = Spine((0, 0), (30, 40)) };
        store.AddStrokeQuiet(original);
        var token = store.EnsureStrokeToken(original);
        var originalSnapshot = store.CaptureStrokePlacement(original).Snapshot;

        var ideal = original.Clone();
        ideal.Points = Spine((0, 0), (100, 0), (100, 100));
        var idealSnapshot = new StrokeReplacementSnapshot(
            token, StrokeReplacementSide.Ideal,
            ideal.Points.Select(p => new StrokeReplacementPoint(p.X, p.Y, p.Pressure)),
            ideal.R, ideal.G, ideal.B, ideal.A, ideal.Size, ideal.Size,
            ideal.IsHighlighter, ideal.FitToCurve, ideal.IgnorePressure);
        store.TryReplaceStrokeQuiet(token, StrokeReplacementSide.Original, idealSnapshot, out _);

        var action = new InkStrokeReplacedAction(store, token, 0, originalSnapshot, idealSnapshot);
        await action.UndoAsync();
        Assert.That(store.Strokes[0].Points[1].X, Is.EqualTo(30), "undo restores the drawn stroke");
        Assert.That(store.GetStrokeSide(token), Is.EqualTo(StrokeReplacementSide.Original));

        await action.RedoAsync();
        Assert.That(store.Strokes[0].Points, Has.Count.EqualTo(3), "redo restores the ideal shape");
        Assert.That(store.GetStrokeSide(token), Is.EqualTo(StrokeReplacementSide.Ideal));
    }

    [Test]
    public async Task StrokeReplaced_AfterErase_IsNoOp()
    {
        // Undo a replacement after the replaced stroke was erased: the token
        // is gone, so the action must no-op rather than resurrect ink.
        var store = new InkStrokeStore();
        var original = new InkStrokeData { Points = Spine((0, 0), (30, 40)) };
        store.AddStrokeQuiet(original);
        var token = store.EnsureStrokeToken(original);
        var snapshot = store.CaptureStrokePlacement(original).Snapshot;
        store.RemoveStrokeQuiet(original);

        var idealSnapshot = new StrokeReplacementSnapshot(
            token, StrokeReplacementSide.Ideal,
            snapshot.Points,
            snapshot.R, snapshot.G, snapshot.B, snapshot.A,
            snapshot.Width, snapshot.Height, snapshot.IsHighlighter,
            snapshot.FitToCurve, snapshot.IgnorePressure);

        var action = new InkStrokeReplacedAction(store, token, 0, snapshot, idealSnapshot);
        await action.UndoAsync();
        await action.RedoAsync();
        Assert.That(store.Count, Is.EqualTo(0), "erased token → replacement is a safe no-op");
    }

    // ------------------------------------------------------------------
    // Scribble shape recognition (the CompleteStroke pipeline gates)
    // ------------------------------------------------------------------

    /// <summary>Hand-drawn-ish axis-aligned rectangle — 41 points, closed.</summary>
    private static List<InkPointData> RectangleScribble()
    {
        var points = new List<InkPointData>();
        for (int i = 0; i <= 10; i++) points.Add(new InkPointData(10 + i * 8, 10 + (i % 2)));
        for (int i = 1; i <= 10; i++) points.Add(new InkPointData(90 + (i % 2), 10 + i * 8));
        for (int i = 1; i <= 10; i++) points.Add(new InkPointData(90 - i * 8, 90 + (i % 2)));
        for (int i = 1; i <= 10; i++) points.Add(new InkPointData(10 + (i % 2), 90 - i * 8));
        return points;
    }

    /// <summary>
    /// The InkSurface.CompleteStroke commit decision: recognition-enabled +
    /// a non-highlighter stroke over the point gate runs the replace; every
    /// other case falls through to the quiet add (what StrokeCollected
    /// covers).
    /// </summary>
    private static bool CommitStrokeLikeSurface(
        InkStrokeStore store,
        InkStrokeData stroke,
        bool shapeRecognitionEnabled,
        out RecognizedStrokeReplacement replacement)
    {
        replacement = null!; // Core signature is nullable-oblivious; null = "no replacement"
        if (shapeRecognitionEnabled && !stroke.IsHighlighter
            && stroke.Points.Count >= StrokeGeometry.MinRecognizedShapePoints
            && ScribbleShapeRecognition.TryReplaceWithRecognizedStroke(
                store, stroke, out replacement))
        {
            return true;
        }

        store.AddStrokeQuiet(stroke);
        return false;
    }

    [Test]
    public async Task Recognize_Enabled_ReplacesStroke_UndoRestoresOriginal()
    {
        var store = new InkStrokeStore();
        var rawPoints = RectangleScribble();
        var stroke = new InkStrokeData
        {
            Points = new List<InkPointData>(rawPoints),
            R = 10, G = 20, B = 30, A = 255,
            Size = 3.0,
            FitToCurve = true,
        };

        Assert.That(
            CommitStrokeLikeSurface(store, stroke, true, out var replacement),
            Is.True, "recognizable scribble + enabled → replaced");
        Assert.Multiple(() =>
        {
            Assert.That(store.Count, Is.EqualTo(1), "the replace is in place — never appended");
            Assert.That(replacement.OriginalIndex, Is.EqualTo(0));
            Assert.That(store.Strokes[0], Is.Not.SameAs(stroke),
                "the store holds the snapshot-built ideal, not the raw instance");
            Assert.That(store.Strokes[0].Points, Has.Count.EqualTo(5),
                "a rectangle outline is a 4-corner closed polygon");
            Assert.That(store.Strokes[0].FitToCurve, Is.False);
            Assert.That(store.Strokes[0].IgnorePressure, Is.True);
            Assert.That(store.GetStrokeSide(replacement.Token),
                Is.EqualTo(StrokeReplacementSide.Ideal));
            // The ideal inherits the original colour/size payload.
            Assert.That(store.Strokes[0].R, Is.EqualTo(10));
            Assert.That(store.Strokes[0].Size, Is.EqualTo(3.0));
        });

        // The editor pushes InkStrokeReplacedAction: undo restores the raw
        // scribble, redo restores the ideal outline.
        var action = new InkStrokeReplacedAction(
            store,
            replacement.Token,
            replacement.OriginalIndex,
            replacement.OriginalSnapshot,
            replacement.IdealSnapshot);

        await action.UndoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(store.Strokes[0].Points, Has.Count.EqualTo(rawPoints.Count),
                "undo restores the user's raw stroke");
            Assert.That(store.Strokes[0].Points[0].X, Is.EqualTo(rawPoints[0].X));
            Assert.That(store.GetStrokeSide(replacement.Token),
                Is.EqualTo(StrokeReplacementSide.Original));
        });

        await action.RedoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(store.Strokes[0].Points, Has.Count.EqualTo(5),
                "redo restores the ideal shape");
            Assert.That(store.GetStrokeSide(replacement.Token),
                Is.EqualTo(StrokeReplacementSide.Ideal));
        });
    }

    [Test]
    public void Recognize_Disabled_KeepsRawStroke()
    {
        var store = new InkStrokeStore();
        var stroke = new InkStrokeData { Points = RectangleScribble() };

        Assert.That(
            CommitStrokeLikeSurface(store, stroke, false, out var replacement),
            Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(replacement, Is.Null);
            Assert.That(store.Count, Is.EqualTo(1));
            Assert.That(store.Strokes[0], Is.SameAs(stroke), "the raw stroke stays");
            Assert.That(store.Strokes[0].Points, Has.Count.EqualTo(41));
            Assert.That(store.GetStrokeSide(store.EnsureStrokeToken(stroke)),
                Is.EqualTo(StrokeReplacementSide.Original));
        });
    }

    [Test]
    public void Recognize_Gates_SkipHighlighterShortAndUnrecognizable()
    {
        var store = new InkStrokeStore();
        var highlighter = new InkStrokeData
        {
            Points = RectangleScribble(),
            IsHighlighter = true,
        };
        var shortStroke = new InkStrokeData { Points = Spine((0, 0), (50, 0), (100, 0)) };
        var zigzag = new InkStrokeData
        {
            Points = Enumerable.Range(0, 13)
                .Select(i => new InkPointData(i * 10, (i % 2) * 60))
                .ToList(),
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                ScribbleShapeRecognition.TryReplaceWithRecognizedStroke(store, highlighter, out _),
                Is.False, "highlighters are never recognized (WPF gate)");
            Assert.That(
                ScribbleShapeRecognition.TryReplaceWithRecognizedStroke(store, shortStroke, out _),
                Is.False, "below MinRecognizedShapePoints");
            Assert.That(
                ScribbleShapeRecognition.TryReplaceWithRecognizedStroke(store, zigzag, out _),
                Is.False, "no shape matches a zigzag");
            Assert.That(store.Count, Is.EqualTo(0),
                "a failed recognition leaves the store untouched");
        });
    }
}
