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
/// Task 8 Phase A headless coverage — the UI-free annotation undo ledger.
/// A <see cref="FakeHost"/> stands in for the WinUI PdfPageControl
/// (<see cref="IAnnotationContainerHost"/>): the actions replay quiet
/// mutations against it exactly the way the WPF nested classes replayed
/// them against a real page, so these tests pin the undo/redo semantics —
/// one action per gesture, sticky payloads travelling with their marker,
/// selection transforms as single steps, and the cross-page transfer
/// bookkeeping (including the failure flag the editor checks before
/// keeping the action on the stack).
/// </summary>
[TestFixture]
public sealed class CoreAnnotationUndoTests
{
    private static List<InkPointData> Spine(params (double x, double y)[] points) =>
        points.Select(p => new InkPointData(p.x, p.y)).ToList();

    private static InkStrokeData Stroke(params (double x, double y)[] points) => new()
    {
        Points = points.Length > 0 ? Spine(points) : Spine((0, 0), (10, 0), (20, 0)),
        R = 10, G = 20, B = 30, A = 255,
        Size = 4,
    };

    private static StickyNoteAnnotation Note(string id = "note1", string text = "hi") => new()
    {
        Id = id,
        X = 40,
        Y = 50,
        Text = text,
        R = 255, G = 235, B = 59,
    };

    // ------------------------------------------------------------------
    // Text lifecycle
    // ------------------------------------------------------------------

    [Test]
    public async Task TextBoxAddedAction_UndoRemoves_RedoReAdds()
    {
        var host = new FakeHost();
        var container = new object();
        host.Containers.Add(container);
        var action = new TextBoxAddedAction(host, container);

        await action.UndoAsync();
        Assert.That(host.Containers, Does.Not.Contain(container));

        await action.RedoAsync();
        Assert.That(host.Containers, Does.Contain(container));
        Assert.That(action.LeavesDocumentDirty, Is.True);
    }

    [Test]
    public async Task TextBoxDeletedAction_UndoReAdds_RedoRemoves()
    {
        var host = new FakeHost();
        var container = new object();
        var action = new TextBoxDeletedAction(host, container);

        await action.UndoAsync();
        Assert.That(host.Containers, Does.Contain(container));

        await action.RedoAsync();
        Assert.That(host.Containers, Does.Not.Contain(container));
    }

    [Test]
    public async Task TextEditSessionAction_ReplaysBeforeAndAfter()
    {
        var host = new FakeHost();
        var container = new object();
        host.Containers.Add(container);
        var action = new TextEditSessionAction(host, container, "before", "after");

        await action.UndoAsync();
        Assert.That(host.Texts[container], Is.EqualTo("before"));

        await action.RedoAsync();
        Assert.That(host.Texts[container], Is.EqualTo("after"));
    }

    [Test]
    public async Task TextStyleChangedAction_ReplaysFontSizeAndColor()
    {
        var host = new FakeHost();
        var container = new object();
        host.Containers.Add(container);
        var before = new TextStyleSnapshot(12, 1, 2, 3);
        var after = new TextStyleSnapshot(24, 250, 128, 0);
        var action = new TextStyleChangedAction(host, container, before, after);

        await action.UndoAsync();
        Assert.That(host.Styles[container], Is.EqualTo(before));

        await action.RedoAsync();
        Assert.That(host.Styles[container], Is.EqualTo(after));
    }

    [Test]
    public async Task TextFormatChangedAction_ReplaysFullSnapshot()
    {
        var host = new FakeHost();
        var container = new object();
        host.Containers.Add(container);
        var before = new TextFormatSnapshot(false, false, "Segoe UI", "Left");
        var after = new TextFormatSnapshot(true, true, "Consolas", "Center");
        var action = new TextFormatChangedAction(host, container, before, after);

        await action.UndoAsync();
        Assert.That(host.Formats[container], Is.EqualTo(before));

        await action.RedoAsync();
        Assert.That(host.Formats[container], Is.EqualTo(after));
    }

    [Test]
    public async Task TextBoxMovedAction_ReplaysPositions()
    {
        var host = new FakeHost();
        var container = new object();
        host.Containers.Add(container);
        var action = new TextBoxMovedAction(host, container, new PointD(10, 20), new PointD(80, 90));

        await action.UndoAsync();
        Assert.That(host.Positions[container], Is.EqualTo(new PointD(10, 20)));

        await action.RedoAsync();
        Assert.That(host.Positions[container], Is.EqualTo(new PointD(80, 90)));
    }

    [Test]
    public async Task TextBoxResizedAction_RestoresBoundsAndAutoFlags()
    {
        var host = new FakeHost();
        var container = new object();
        host.Containers.Add(container);
        var before = new TextBoxBounds(10, 20, 120, 48);
        var after = new TextBoxBounds(10, 20, 300, 160);
        var action = new TextBoxResizedAction(
            host, container, before, after,
            beforeAutoWidth: true, beforeAutoHeight: false,
            afterAutoWidth: false, afterAutoHeight: false);

        await action.UndoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(host.Bounds[container], Is.EqualTo(before));
            Assert.That(host.AutoWidth[container], Is.True);
            Assert.That(host.AutoHeight[container], Is.False);
        });

        await action.RedoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(host.Bounds[container], Is.EqualTo(after));
            Assert.That(host.AutoWidth[container], Is.False);
        });
    }

    // ------------------------------------------------------------------
    // Sticky notes
    // ------------------------------------------------------------------

    [Test]
    public async Task StickyNoteAddedAction_UndoRemoves_RedoReAdds()
    {
        var host = new FakeHost();
        var container = new object();
        host.MarkSticky(container);
        var action = new StickyNoteAddedAction(host, container);

        await action.UndoAsync();
        Assert.That(host.Containers, Does.Not.Contain(container));

        await action.RedoAsync();
        Assert.That(host.Containers, Does.Contain(container));
    }

    [Test]
    public async Task StickyNoteMovedAction_ReplaysPositions()
    {
        var host = new FakeHost();
        var container = new object();
        host.MarkSticky(container);
        var action = new StickyNoteMovedAction(host, container, new PointD(5, 5), new PointD(90, 90));

        await action.UndoAsync();
        Assert.That(host.Positions[container], Is.EqualTo(new PointD(5, 5)));

        await action.RedoAsync();
        Assert.That(host.Positions[container], Is.EqualTo(new PointD(90, 90)));
    }

    [Test]
    public async Task StickyNoteEditAction_ReplaysText()
    {
        var host = new FakeHost();
        var container = new object();
        host.MarkSticky(container);
        var note = Note();
        var action = new StickyNoteEditAction(host, container, note, "old", "new");

        await action.UndoAsync();
        Assert.That(host.Texts[container], Is.EqualTo("old"));

        await action.RedoAsync();
        Assert.That(host.Texts[container], Is.EqualTo("new"));
    }

    [Test]
    public async Task StickyNoteEditAction_FallsBackToModelWhenContainerGone()
    {
        // WPF StickyNoteEditAction: when the marker was deleted the model
        // payload still takes the reverted text so a later re-add shows it.
        var host = new FakeHost();
        var container = new object(); // never added → quiet setter fails
        var note = Note(text: "new");
        var action = new StickyNoteEditAction(host, container, note, "old", "new");

        await action.UndoAsync();
        Assert.That(note.Text, Is.EqualTo("old"));

        await action.RedoAsync();
        Assert.That(note.Text, Is.EqualTo("new"));
    }

    [Test]
    public async Task StickyNoteDeletedAction_UndoReAdds_RedoRemoves()
    {
        var host = new FakeHost();
        var container = new object();
        host.MarkSticky(container);
        var action = new StickyNoteDeletedAction(host, container);

        await action.UndoAsync();
        Assert.That(host.Containers, Does.Contain(container));

        await action.RedoAsync();
        Assert.That(host.Containers, Does.Not.Contain(container));
    }

    // ------------------------------------------------------------------
    // Combined selection transforms — one action per completed gesture
    // ------------------------------------------------------------------

    [Test]
    public async Task AnnotationSelectionMoveAction_AppliesNegativeAndPositiveDelta()
    {
        var host = new FakeHost();
        var stroke = Stroke();
        var container = new object();
        var action = new AnnotationSelectionMoveAction(
            host, new[] { stroke }, new[] { container }, 30, 40);

        await action.UndoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(host.Moves.Count, Is.EqualTo(1));
            Assert.That(host.Moves[0].dx, Is.EqualTo(-30));
            Assert.That(host.Moves[0].dy, Is.EqualTo(-40));
        });

        await action.RedoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(host.Moves.Count, Is.EqualTo(2));
            Assert.That(host.Moves[1].dx, Is.EqualTo(30));
            Assert.That(host.Moves[1].dy, Is.EqualTo(40));
        });
    }

    [Test]
    public async Task AnnotationSelectionResizeAction_UndoAppliesInverseScale()
    {
        var host = new FakeHost();
        var anchor = new PointD(100, 100);
        var action = new AnnotationSelectionResizeAction(
            host, Array.Empty<InkStrokeData>(), new[] { new object() }, 2.0, anchor);

        await action.UndoAsync();
        Assert.That(host.Scales[0].scale, Is.EqualTo(0.5).Within(0.0001));

        await action.RedoAsync();
        Assert.That(host.Scales[1].scale, Is.EqualTo(2.0).Within(0.0001));
    }

    [Test]
    public async Task AnnotationSelectionRotateAction_ReplaysDegrees()
    {
        var host = new FakeHost();
        var center = new PointD(50, 50);
        var action = new AnnotationSelectionRotateAction(
            host, Array.Empty<InkStrokeData>(), new[] { new object() }, 90, center);

        await action.UndoAsync();
        Assert.That(host.Rotations[0].degrees, Is.EqualTo(-90));

        await action.RedoAsync();
        Assert.That(host.Rotations[1].degrees, Is.EqualTo(90));
    }

    // ------------------------------------------------------------------
    // Combined add / remove
    // ------------------------------------------------------------------

    [Test]
    public async Task AnnotationItemsAddedAction_UndoClearsSelectionThenRemoves()
    {
        var store = new InkStrokeStore();
        var host = new FakeHost();
        var stroke = Stroke();
        var container = new object();
        store.AddStrokeQuiet(stroke);
        host.Containers.Add(container);
        var placement = store.CaptureStrokePlacement(stroke);

        var action = new AnnotationItemsAddedAction(
            store, new List<InkStrokePlacement> { placement }, host, new[] { container });

        await action.UndoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(host.ClearSelectionCalls, Is.EqualTo(1));
            Assert.That(store.IndexOf(stroke), Is.LessThan(0));
            Assert.That(host.Containers, Does.Not.Contain(container));
        });

        await action.RedoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(store.IndexOf(stroke), Is.GreaterThanOrEqualTo(0));
            Assert.That(host.Containers, Does.Contain(container));
        });
    }

    [Test]
    public async Task AnnotationItemsRemovedAction_RestoresPlacementsThenContainers()
    {
        var store = new InkStrokeStore();
        var host = new FakeHost();
        var s1 = Stroke();
        var s2 = Stroke();
        var container = new object();
        store.AddStrokeQuiet(s1);
        store.AddStrokeQuiet(s2);
        var p1 = store.CaptureStrokePlacement(s1);
        var p2 = store.CaptureStrokePlacement(s2);
        store.RemoveStrokeQuiet(s1);
        store.RemoveStrokeQuiet(s2);

        var action = new AnnotationItemsRemovedAction(
            store, new List<InkStrokePlacement> { p1, p2 }, host, new[] { container });

        await action.UndoAsync();
        Assert.Multiple(() =>
        {
            // Z-order preserved: s1 re-inserted ahead of s2.
            Assert.That(store.IndexOf(s1), Is.EqualTo(0));
            Assert.That(store.IndexOf(s2), Is.EqualTo(1));
            Assert.That(host.Containers, Does.Contain(container));
        });

        await action.RedoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(store.IndexOf(s1), Is.LessThan(0));
            Assert.That(store.IndexOf(s2), Is.LessThan(0));
            Assert.That(host.Containers, Does.Not.Contain(container));
        });
    }

    // ------------------------------------------------------------------
    // Cross-page move — transfer, undo, redo, failure flag
    // ------------------------------------------------------------------

    [Test]
    public async Task CrossPageMove_TransfersStrokesAndStickyPayload()
    {
        var source = new InkStrokeStore();
        var target = new InkStrokeStore();
        var sourceHost = new FakeHost();
        var targetHost = new FakeHost();
        // The action is constructed AFTER the live drag — the stroke already
        // sits at its post-drag position (y=25 for dy=25 from an origin of 0).
        var stroke = Stroke((0, 25), (10, 25));
        var container = new object();
        var note = Note();
        source.AddStrokeQuiet(stroke);
        sourceHost.MarkSticky(container);
        sourceHost.OverlayData[container] = note;
        var placement = source.CaptureStrokePlacement(stroke);

        var action = new AnnotationSelectionCrossPageMoveAction(
            source, target, sourceHost, targetHost,
            new[] { stroke }, new[] { container },
            dx: 15, dy: 25, adjustX: 0, adjustY: -100,
            sourcePlacements: new List<InkStrokePlacement> { placement });

        Assert.That(action.ExecuteInitialTransfer(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(source.IndexOf(stroke), Is.LessThan(0));
            Assert.That(target.IndexOf(stroke), Is.GreaterThanOrEqualTo(0));
            Assert.That(sourceHost.Containers, Does.Not.Contain(container));
            Assert.That(targetHost.Containers, Does.Contain(container));
            // The sticky model rides with the marker.
            Assert.That(targetHost.OverlayData[container], Is.SameAs(note));
            // The transfer applies only the container→page adjust (the drag
            // delta was already baked in by the live gesture): 25 - 100.
            Assert.That(stroke.Points[0].Y, Is.EqualTo(-75));
            // The container leg replays the same adjust on the target host.
            Assert.That(targetHost.Moves, Has.Count.EqualTo(1));
            Assert.That(targetHost.Moves[0].dy, Is.EqualTo(-100));
        });
        // Idempotent — a second call is a no-op success.
        Assert.That(action.ExecuteInitialTransfer(), Is.True);

        await action.UndoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(action.LastOperationSucceeded, Is.True);
            Assert.That(source.IndexOf(stroke), Is.GreaterThanOrEqualTo(0));
            Assert.That(target.IndexOf(stroke), Is.LessThan(0));
            Assert.That(sourceHost.Containers, Does.Contain(container));
            Assert.That(targetHost.Containers, Does.Not.Contain(container));
            Assert.That(sourceHost.OverlayData[container], Is.SameAs(note));
            // Undo returns to the PRE-GESTURE position: -(dx + adjust)
            // = -(25 - 100) = +75 → -75 + 75 = 0.
            Assert.That(stroke.Points[0].Y, Is.EqualTo(0));
        });

        await action.RedoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(action.LastOperationSucceeded, Is.True);
            Assert.That(target.IndexOf(stroke), Is.GreaterThanOrEqualTo(0));
            Assert.That(targetHost.Containers, Does.Contain(container));
            Assert.That(targetHost.OverlayData[container], Is.SameAs(note));
            // Redo replays +(dx + adjust): 0 + (25 - 100) = -75.
            Assert.That(stroke.Points[0].Y, Is.EqualTo(-75));
        });
    }

    [Test]
    public async Task CrossPageMove_UndoFailsWhenStrokeNoLongerInTarget()
    {
        var source = new InkStrokeStore();
        var target = new InkStrokeStore();
        var sourceHost = new FakeHost();
        var targetHost = new FakeHost();
        var stroke = Stroke();
        source.AddStrokeQuiet(stroke);
        var placement = source.CaptureStrokePlacement(stroke);

        var action = new AnnotationSelectionCrossPageMoveAction(
            source, target, sourceHost, targetHost,
            new[] { stroke }, Array.Empty<object>(),
            dx: 0, dy: 0, adjustX: 0, adjustY: 0,
            sourcePlacements: new List<InkStrokePlacement> { placement });

        action.ExecuteInitialTransfer();
        // Simulate the stroke being erased on the target page afterwards.
        target.RemoveStrokeQuiet(stroke);

        await action.UndoAsync();
        Assert.That(action.LastOperationSucceeded, Is.False);

        // Redo after a failed undo also flags the gap instead of corrupting.
        await action.RedoAsync();
        Assert.That(action.LastOperationSucceeded, Is.False);
    }

    [Test]
    public void CrossPageMove_InitialTransferFailsWhenNothingExists()
    {
        var source = new InkStrokeStore();
        var target = new InkStrokeStore();
        var action = new AnnotationSelectionCrossPageMoveAction(
            source, target, new FakeHost(), new FakeHost(),
            new[] { Stroke() }, Array.Empty<object>(),
            dx: 0, dy: 0, adjustX: 0, adjustY: 0,
            sourcePlacements: new List<InkStrokePlacement>());

        // The stroke was never added to the source — nothing transfers and
        // the editor must not push this action (WPF parity).
        Assert.That(action.ExecuteInitialTransfer(), Is.False);
    }

    // ------------------------------------------------------------------
    // Fake host — mirrors the WinUI PdfPageControl quiet-mutation contract.
    // ------------------------------------------------------------------

    private sealed class FakeHost : IAnnotationContainerHost
    {
        private readonly HashSet<object> _sticky = new();

        public HashSet<object> Containers { get; } = new();
        public Dictionary<object, object> OverlayData { get; } = new();
        public Dictionary<object, PointD> Positions { get; } = new();
        public Dictionary<object, TextBoxBounds> Bounds { get; } = new();
        public Dictionary<object, string> Texts { get; } = new();
        public Dictionary<object, TextStyleSnapshot> Styles { get; } = new();
        public Dictionary<object, TextFormatSnapshot> Formats { get; } = new();
        public Dictionary<object, bool> AutoWidth { get; } = new();
        public Dictionary<object, bool> AutoHeight { get; } = new();
        public List<(double dx, double dy)> Moves { get; } = new();
        public List<(double scale, PointD center)> Scales { get; } = new();
        public List<(double degrees, PointD center)> Rotations { get; } = new();
        public int ClearSelectionCalls { get; private set; }

        public void MarkSticky(object container)
        {
            _sticky.Add(container);
            Containers.Add(container);
        }

        public bool RemoveTextContainerQuiet(object container) => Containers.Remove(container);

        public void AddTextContainerQuiet(object container) => Containers.Add(container);

        public object GetOverlayData(object container)
            => OverlayData.TryGetValue(container, out var data) ? data : null!;

        public void SetOverlayData(object container, object data)
        {
            if (data == null) OverlayData.Remove(container);
            else OverlayData[container] = data;
        }

        public bool SetStickyNotePositionQuiet(object container, PointD position)
        {
            if (!_sticky.Contains(container))
                return false;
            Positions[container] = position;
            return true;
        }

        public bool SetStickyNoteTextQuiet(object container, string text)
        {
            if (!_sticky.Contains(container))
                return false;
            Texts[container] = text;
            return true;
        }

        public void SetTextContainerPositionQuiet(object container, PointD position)
            => Positions[container] = position;

        public void SetTextContainerBoundsQuiet(
            object container, TextBoxBounds bounds, bool? autoWidth, bool? autoHeight)
        {
            Bounds[container] = bounds;
            if (autoWidth.HasValue) AutoWidth[container] = autoWidth.Value;
            if (autoHeight.HasValue) AutoHeight[container] = autoHeight.Value;
        }

        public bool SetTextContentQuiet(object container, string text)
        {
            if (!Containers.Contains(container))
                return false;
            Texts[container] = text;
            return true;
        }

        public void SetTextStyleQuiet(object container, double fontSize, byte r, byte g, byte b)
            => Styles[container] = new TextStyleSnapshot(fontSize, r, g, b);

        public void SetTextFormatQuiet(object container, TextFormatSnapshot format)
            => Formats[container] = format;

        public void MoveItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double deltaX, double deltaY)
        {
            Moves.Add((deltaX, deltaY));
            foreach (var stroke in strokes ?? Array.Empty<InkStrokeData>())
                StrokeGeometry.TranslateSpinePoints(stroke.Points, deltaX, deltaY);
            foreach (var container in containers ?? Array.Empty<object>())
            {
                if (Positions.TryGetValue(container, out var pos))
                    Positions[container] = new PointD(pos.X + deltaX, pos.Y + deltaY);
            }
        }

        public void ScaleItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double scaleFactor, PointD center)
            => Scales.Add((scaleFactor, center));

        public void RotateItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double degrees, PointD center)
            => Rotations.Add((degrees, center));

        public void ClearSelection() => ClearSelectionCalls++;
    }
}
