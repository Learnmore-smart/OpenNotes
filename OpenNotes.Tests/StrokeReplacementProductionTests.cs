using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Pages;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
public sealed class StrokeReplacementProductionTests
{
    [OneTimeSetUp]
    public void NormalizeWpfEnvironment()
    {
        WindowsEnvironment.NormalizeForWpf();
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void PdfPageControl_EraseUndoThenShapeUndo_RestoresStablePlacementAndOrder()
    {
        var page = new PdfPageControl();
        page.AddStrokeQuiet(CreateStroke(10, 10, ignorePressure: false));
        var original = CreateStroke(40, 20, ignorePressure: false);
        page.AddStrokeQuiet(original);

        var originalPlacement = page.CaptureStrokePlacement(original);
        var idealSnapshot = originalPlacement.Snapshot
            .WithSide(StrokeReplacementSide.Ideal)
            .WithIgnorePressure(true);
        var shapeAction = CreateNestedAction(
            "StrokeReplacedAction",
            page,
            originalPlacement.Token,
            originalPlacement.Index,
            originalPlacement.Snapshot,
            idealSnapshot);
        InvokeTask(shapeAction, "RedoAsync");

        var idealPlacement = page.CaptureStrokePlacement(page.GetStrokes()[originalPlacement.Index]);
        page.RemoveStrokeQuiet(idealPlacement.Stroke);

        var eraseAction = CreateNestedAction(
            "StrokesErasedAction",
            page,
            new List<StrokePlacement> { idealPlacement },
            new List<StrokePlacement>());
        InvokeTask(eraseAction, "UndoAsync");

        InvokeTask(shapeAction, "UndoAsync");
        var restoredPlacement = page.CaptureStrokePlacement(page.GetStrokes()[originalPlacement.Index]);
        Assert.That(restoredPlacement.Token, Is.EqualTo(originalPlacement.Token));
        Assert.That(restoredPlacement.Side, Is.EqualTo(StrokeReplacementSide.Original));
        Assert.That(page.GetStrokes().Count, Is.EqualTo(2));
        Assert.That(page.GetStrokes()[0].StylusPoints[0].X, Is.EqualTo(10));
        Assert.That(page.GetStrokes()[1].StylusPoints[0].X, Is.EqualTo(40));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void FreshRecognizedStroke_UsesOneAddUndoRedoHistoryAction()
    {
        EnsureEditorResources();
        var editor = new EditorPage();
        var page = new PdfPageControl
        {
            ShapeRecognitionEnabled = true,
            StrokeSmoothingLevel = 2
        };
        var rawStroke = CreateRecognizedLineStroke();
        var rawPlacement = page.AddStrokeQuiet(rawStroke);
        Assert.That(rawPlacement, Is.Not.Null);

        bool recognized = false;
        page.StrokeRecognized += (sender, args) =>
        {
            recognized = true;
            InvokeEditorPrivate(editor, "PageControl_StrokeRecognized", sender, args);
        };

        var collected = typeof(PdfPageControl).GetMethod(
            "InkCanvas_StrokeCollected",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(PdfPageControl).FullName, "InkCanvas_StrokeCollected");
        collected.Invoke(
            page,
            new object[] { page, new InkCanvasStrokeCollectedEventArgs(rawStroke) });

        Assert.That(recognized, Is.True, "the real collection pipeline must recognize the smoothed line");
        var idealPlacement = page.CaptureStrokePlacement(page.GetStrokes()[0]);
        Assert.That(idealPlacement.Token, Is.EqualTo(rawPlacement.Token));
        Assert.That(idealPlacement.Side, Is.EqualTo(StrokeReplacementSide.Ideal));

        var undoStack = (IList)(typeof(EditorPage)
            .GetField("_undoStack", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(editor)
            ?? throw new AssertionException("EditorPage did not initialize its undo stack."));
        Assert.That(undoStack.Count, Is.EqualTo(1), "one recognized gesture must create one history entry");

        InvokeTask(undoStack[0], "UndoAsync");
        Assert.That(page.GetStrokes().Count, Is.EqualTo(0),
            "Undo must remove a freshly drawn recognized stroke rather than expose smoothing history");

        InvokeTask(undoStack[0], "RedoAsync");
        Assert.That(page.GetStrokes().Count, Is.EqualTo(1));
        var restored = page.CaptureStrokePlacement(page.GetStrokes()[0]);
        Assert.That(restored.Token, Is.EqualTo(rawPlacement.Token));
        Assert.That(restored.Side, Is.EqualTo(StrokeReplacementSide.Ideal));
        Assert.That(page.GetStrokes()[0].DrawingAttributes.FitToCurve, Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LegacyRecognizedEvent_DefaultsToSnapshotReplacementUndo()
    {
        EnsureEditorResources();
        var editor = new EditorPage();
        var page = new PdfPageControl();
        var original = CreateRecognizedLineStroke();
        var originalPlacement = page.AddStrokeQuiet(original);
        var originalSnapshot = originalPlacement.Snapshot;
        var idealSnapshot = originalSnapshot
            .WithSide(StrokeReplacementSide.Ideal)
            .WithIgnorePressure(true);

        Assert.That(page.TryReplaceStrokeQuiet(
            originalPlacement.Token,
            StrokeReplacementSide.Original,
            idealSnapshot,
            out _), Is.True);

        var args = new StrokeRecognizedEventArgs(
            originalPlacement.Token,
            originalPlacement.Index,
            originalSnapshot,
            idealSnapshot);
        Assert.That(args.IsFreshStroke, Is.False,
            "legacy four-argument recognition payloads must remain replacements");

        InvokeEditorPrivate(editor, "PageControl_StrokeRecognized", page, args);
        var undoStack = (IList)(typeof(EditorPage)
            .GetField("_undoStack", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(editor)
            ?? throw new AssertionException("EditorPage did not initialize its undo stack."));
        Assert.That(undoStack.Count, Is.EqualTo(1));

        InvokeTask(undoStack[0], "UndoAsync");
        Assert.That(page.GetStrokes().Count, Is.EqualTo(1));
        var restored = page.CaptureStrokePlacement(page.GetStrokes()[0]);
        Assert.That(restored.Token, Is.EqualTo(originalPlacement.Token));
        Assert.That(restored.Side, Is.EqualTo(StrokeReplacementSide.Original));
        Assert.That(page.GetStrokes()[0].DrawingAttributes.FitToCurve, Is.True);
        Assert.That(page.GetStrokes()[0].DrawingAttributes.IgnorePressure, Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void PdfPageControl_DeleteUndoThenShapeUndo_RestoresStablePlacementAndOrder()
    {
        var page = new PdfPageControl();
        page.AddStrokeQuiet(CreateStroke(10, 10, ignorePressure: false));
        var original = CreateStroke(40, 20, ignorePressure: false);
        page.AddStrokeQuiet(original);
        var originalPlacement = page.CaptureStrokePlacement(original);
        var idealSnapshot = originalPlacement.Snapshot
            .WithSide(StrokeReplacementSide.Ideal)
            .WithIgnorePressure(true);

        Assert.That(page.TryReplaceStrokeQuiet(
            originalPlacement.Token,
            StrokeReplacementSide.Original,
            idealSnapshot,
            out _), Is.True);
        var idealPlacement = page.CaptureStrokePlacement(page.GetStrokes()[originalPlacement.Index]);
        page.RemoveStrokeQuiet(idealPlacement.Stroke);

        var deleteAction = CreateNestedAction(
            "ItemsRemovedAction",
            page,
            new List<StrokePlacement> { idealPlacement },
            new List<System.Windows.Controls.Grid>());
        InvokeTask(deleteAction, "UndoAsync");

        Assert.That(page.TryReplaceStrokeQuiet(
            originalPlacement.Token,
            StrokeReplacementSide.Ideal,
            originalPlacement.Snapshot,
            out var restoredIndex), Is.True);
        Assert.That(restoredIndex, Is.EqualTo(originalPlacement.Index));
        Assert.That(page.GetStrokes().Count, Is.EqualTo(2));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ErasePlacementRedoAfterShapeRedo_ResolvesCurrentTokenWithoutDuplicate()
    {
        var page = new PdfPageControl();
        var baseline = CreateStroke(10, 10, ignorePressure: false);
        var original = CreateStroke(40, 20, ignorePressure: false);
        page.AddStrokeQuiet(baseline);
        page.AddStrokeQuiet(original);

        var originalPlacement = page.CaptureStrokePlacement(original);
        var idealSnapshot = originalPlacement.Snapshot
            .WithSide(StrokeReplacementSide.Ideal)
            .WithIgnorePressure(true);
        var shapeAction = CreateNestedAction(
            "StrokeReplacedAction",
            page,
            originalPlacement.Token,
            originalPlacement.Index,
            originalPlacement.Snapshot,
            idealSnapshot);
        InvokeTask(shapeAction, "RedoAsync");

        var idealPlacement = page.CaptureStrokePlacement(page.GetStrokes()[originalPlacement.Index]);
        page.RemoveStrokeQuiet(idealPlacement.Stroke);
        var eraseAction = CreateNestedAction(
            "StrokesErasedAction",
            page,
            new List<StrokePlacement> { idealPlacement },
            new List<StrokePlacement>());

        InvokeTask(eraseAction, "UndoAsync");
        var placementAfterUndo = page.CaptureStrokePlacement(page.GetStrokes()[originalPlacement.Index]);
        Assert.That(placementAfterUndo.Owner, Is.SameAs(page));
        Assert.That(placementAfterUndo.Token, Is.EqualTo(originalPlacement.Token));
        Assert.That(placementAfterUndo.Side, Is.EqualTo(StrokeReplacementSide.Ideal));
        Assert.That(placementAfterUndo.Index, Is.EqualTo(originalPlacement.Index));

        InvokeTask(shapeAction, "UndoAsync");
        InvokeTask(shapeAction, "RedoAsync");
        InvokeTask(eraseAction, "RedoAsync");

        Assert.That(page.GetStrokes().Count, Is.EqualTo(1));
        Assert.That(page.GetStrokes()[0], Is.SameAs(baseline));
        Assert.That(page.GetStrokes().Any(stroke =>
            page.CaptureStrokePlacement(stroke).Token == originalPlacement.Token), Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DeletePlacementRedoAfterShapeRedo_ResolvesCurrentTokenWithoutDuplicate()
    {
        var page = new PdfPageControl();
        var baseline = CreateStroke(10, 10, ignorePressure: false);
        var original = CreateStroke(40, 20, ignorePressure: false);
        page.AddStrokeQuiet(baseline);
        page.AddStrokeQuiet(original);

        var originalPlacement = page.CaptureStrokePlacement(original);
        var idealSnapshot = originalPlacement.Snapshot
            .WithSide(StrokeReplacementSide.Ideal)
            .WithIgnorePressure(true);
        var shapeAction = CreateNestedAction(
            "StrokeReplacedAction",
            page,
            originalPlacement.Token,
            originalPlacement.Index,
            originalPlacement.Snapshot,
            idealSnapshot);
        InvokeTask(shapeAction, "RedoAsync");

        var idealPlacement = page.CaptureStrokePlacement(page.GetStrokes()[originalPlacement.Index]);
        page.RemoveStrokeQuiet(idealPlacement.Stroke);
        var deleteAction = CreateNestedAction(
            "ItemsRemovedAction",
            page,
            new List<StrokePlacement> { idealPlacement },
            new List<System.Windows.Controls.Grid>());

        InvokeTask(deleteAction, "UndoAsync");
        Assert.That(page.CaptureStrokePlacement(page.GetStrokes()[originalPlacement.Index]).Owner, Is.SameAs(page));

        InvokeTask(shapeAction, "UndoAsync");
        InvokeTask(shapeAction, "RedoAsync");
        InvokeTask(deleteAction, "RedoAsync");

        Assert.That(page.GetStrokes().Count, Is.EqualTo(1));
        Assert.That(page.GetStrokes()[0], Is.SameAs(baseline));
        Assert.That(page.GetStrokes().Any(stroke =>
            page.CaptureStrokePlacement(stroke).Token == originalPlacement.Token), Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CrossPagePlacementRedoAfterShapeRedo_ResolvesCurrentOwnerWithoutDoubleStroke()
    {
        var source = new PdfPageControl();
        var target = new PdfPageControl();
        var sourceBaseline = CreateStroke(10, 10, ignorePressure: false);
        var original = CreateStroke(40, 20, ignorePressure: false);
        var targetBaseline = CreateStroke(90, 90, ignorePressure: false);
        source.AddStrokeQuiet(sourceBaseline);
        source.AddStrokeQuiet(original);
        target.AddStrokeQuiet(targetBaseline);

        var originalPlacement = source.CaptureStrokePlacement(original);
        var idealSnapshot = originalPlacement.Snapshot
            .WithSide(StrokeReplacementSide.Ideal)
            .WithIgnorePressure(true);
        var shapeAction = CreateNestedAction(
            "StrokeReplacedAction",
            source,
            originalPlacement.Token,
            originalPlacement.Index,
            originalPlacement.Snapshot,
            idealSnapshot);
        InvokeTask(shapeAction, "RedoAsync");

        var idealPlacement = source.CaptureStrokePlacement(source.GetStrokes()[originalPlacement.Index]);
        var moveAction = CreateNestedAction(
            "SelectionCrossPageMoveAction",
            source,
            target,
            0d,
            0d,
            0d,
            0d,
            new List<StrokePlacement> { idealPlacement },
            new List<System.Windows.Controls.Grid>());
        InvokeVoid(moveAction, "ExecuteInitialTransfer");
        Assert.That(source.GetStrokes().Count, Is.EqualTo(1));
        Assert.That(target.GetStrokes().Count, Is.EqualTo(2));

        InvokeTask(moveAction, "UndoAsync");
        var sourcePlacementAfterUndo = source.CaptureStrokePlacement(source.GetStrokes()[originalPlacement.Index]);
        Assert.That(sourcePlacementAfterUndo.Owner, Is.SameAs(source));
        Assert.That(sourcePlacementAfterUndo.Token, Is.EqualTo(originalPlacement.Token));
        Assert.That(sourcePlacementAfterUndo.Side, Is.EqualTo(StrokeReplacementSide.Ideal));
        Assert.That(sourcePlacementAfterUndo.Index, Is.EqualTo(originalPlacement.Index));

        InvokeTask(shapeAction, "UndoAsync");
        InvokeTask(shapeAction, "RedoAsync");
        InvokeTask(moveAction, "RedoAsync");

        Assert.That(source.GetStrokes().Count, Is.EqualTo(1));
        Assert.That(source.GetStrokes()[0], Is.SameAs(sourceBaseline));
        Assert.That(target.GetStrokes().Count, Is.EqualTo(2));
        Assert.That(target.GetStrokes()[0], Is.SameAs(targetBaseline));
        var targetPlacement = target.CaptureStrokePlacement(target.GetStrokes()[1]);
        Assert.That(targetPlacement.Owner, Is.SameAs(target));
        Assert.That(targetPlacement.Token, Is.EqualTo(originalPlacement.Token));
        Assert.That(targetPlacement.Side, Is.EqualTo(StrokeReplacementSide.Ideal));
        Assert.That(targetPlacement.Index, Is.EqualTo(1));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void AddPlacementRejectsSameTokenWhenStaleSideDiffers()
    {
        var page = new PdfPageControl();
        var token = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var currentStroke = CreateStroke(20, 20, ignorePressure: false);
        var currentPlacement = CreateDeterministicPlacement(
            page,
            currentStroke,
            token,
            StrokeReplacementSide.Original,
            index: 0);
        Assert.That(page.AddStrokeQuiet(currentPlacement), Is.Not.Null);

        var stalePlacement = CreateDeterministicPlacement(
            page,
            CreateStroke(60, 60, ignorePressure: true),
            token,
            StrokeReplacementSide.Ideal,
            index: 0);

        Assert.That(page.AddStrokeQuiet(stalePlacement), Is.Null);
        Assert.That(page.GetStrokes().Count, Is.EqualTo(1));
        Assert.That(page.GetStrokes()[0], Is.SameAs(currentStroke));
        Assert.That(page.CaptureStrokePlacement(page.GetStrokes()[0]).Side,
            Is.EqualTo(StrokeReplacementSide.Original));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CrossPageTransferRejectsTargetTokenConflictWithDifferentSide()
    {
        var source = new PdfPageControl();
        var target = new PdfPageControl();
        var token = Guid.Parse("22222222-2222-2222-2222-222222222222");
        source.AddStrokeQuiet(CreateStroke(10, 10, ignorePressure: false));
        var sourceStroke = CreateStroke(40, 20, ignorePressure: false);
        var sourcePlacement = CreateDeterministicPlacement(
            source,
            sourceStroke,
            token,
            StrokeReplacementSide.Original,
            index: 1);
        Assert.That(source.AddStrokeQuiet(sourcePlacement), Is.Not.Null);

        target.AddStrokeQuiet(CreateStroke(90, 90, ignorePressure: false));
        var conflictingTargetStroke = CreateStroke(120, 120, ignorePressure: true);
        var conflictingTargetPlacement = CreateDeterministicPlacement(
            target,
            conflictingTargetStroke,
            token,
            StrokeReplacementSide.Ideal,
            index: 1);
        Assert.That(target.AddStrokeQuiet(conflictingTargetPlacement), Is.Not.Null);

        var moveAction = CreateNestedAction(
            "SelectionCrossPageMoveAction",
            source,
            target,
            15d,
            0d,
            0d,
            0d,
            new List<StrokePlacement> { sourcePlacement },
            new List<System.Windows.Controls.Grid>());
        InvokeVoid(moveAction, "ExecuteInitialTransfer");

        Assert.That(source.GetStrokes().Count, Is.EqualTo(2));
        Assert.That(source.GetStrokes()[1], Is.SameAs(sourceStroke));
        Assert.That(target.GetStrokes().Count, Is.EqualTo(2));
        Assert.That(target.GetStrokes()[1], Is.SameAs(conflictingTargetStroke));
        var targetPlacement = target.CaptureStrokePlacement(target.GetStrokes()[1]);
        Assert.That(targetPlacement.Token, Is.EqualTo(token));
        Assert.That(targetPlacement.Side, Is.EqualTo(StrokeReplacementSide.Ideal));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CrossPageRedoUndoRetainsSourceWhenTargetTokenConflictsAfterShapeRedo()
    {
        var source = new PdfPageControl();
        var target = new PdfPageControl();
        var token = Guid.Parse("33333333-3333-3333-3333-333333333333");
        source.AddStrokeQuiet(CreateStroke(10, 10, ignorePressure: false));
        var originalStroke = CreateStroke(40, 20, ignorePressure: false);
        var originalPlacement = CreateDeterministicPlacement(
            source,
            originalStroke,
            token,
            StrokeReplacementSide.Original,
            index: 1);
        Assert.That(source.AddStrokeQuiet(originalPlacement), Is.Not.Null);
        target.AddStrokeQuiet(CreateStroke(90, 90, ignorePressure: false));

        var idealSnapshot = originalPlacement.Snapshot
            .WithSide(StrokeReplacementSide.Ideal)
            .WithIgnorePressure(true);
        var shapeAction = CreateNestedAction(
            "StrokeReplacedAction",
            source,
            token,
            originalPlacement.Index,
            originalPlacement.Snapshot,
            idealSnapshot);
        InvokeTask(shapeAction, "RedoAsync");
        var idealPlacement = source.CaptureStrokePlacement(source.GetStrokes()[1]);

        var moveAction = CreateNestedAction(
            "SelectionCrossPageMoveAction",
            source,
            target,
            25d,
            0d,
            0d,
            0d,
            new List<StrokePlacement> { idealPlacement },
            new List<System.Windows.Controls.Grid>());
        InvokeVoid(moveAction, "ExecuteInitialTransfer");
        InvokeTask(moveAction, "UndoAsync");

        InvokeTask(shapeAction, "UndoAsync");
        InvokeTask(shapeAction, "RedoAsync");
        var freshSourceIdeal = source.GetStrokes()[1];

        var freshTargetStroke = CreateStroke(200, 200, ignorePressure: true);
        var freshTargetPlacement = CreateDeterministicPlacement(
            target,
            freshTargetStroke,
            token,
            StrokeReplacementSide.Ideal,
            index: 1);
        Assert.That(target.AddStrokeQuiet(freshTargetPlacement), Is.Not.Null);

        InvokeTask(moveAction, "RedoAsync");

        // The target now contains an unrelated live stroke with the same
        // token/side.  It is a conflict, not an idempotent resolution: redo
        // must restore the source and must not claim success or move the
        // unrelated target stroke.
        Assert.That(source.GetStrokes().Count, Is.EqualTo(2));
        Assert.That(target.GetStrokes().Count, Is.EqualTo(2));
        Assert.That(target.GetStrokes()[1], Is.SameAs(freshTargetStroke));
        Assert.That(freshSourceIdeal, Is.SameAs(source.GetStrokes()[1]));
        Assert.That(freshTargetStroke.StylusPoints[0].X, Is.EqualTo(200).Within(0.0001));

        InvokeTask(moveAction, "UndoAsync");

        Assert.That(source.GetStrokes().Count, Is.EqualTo(2));
        Assert.That(target.GetStrokes().Count, Is.EqualTo(2));
        var restoredSourcePlacement = source.CaptureStrokePlacement(source.GetStrokes()[1]);
        Assert.That(restoredSourcePlacement.Token, Is.EqualTo(token));
        Assert.That(restoredSourcePlacement.Side, Is.EqualTo(StrokeReplacementSide.Ideal));
        Assert.That(restoredSourcePlacement.Index, Is.EqualTo(1));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CrossPageMoveUndoThenShapeUndo_RestoresSourceOwnerIndexAndToken()
    {
        var source = new PdfPageControl();
        var target = new PdfPageControl();
        source.AddStrokeQuiet(CreateStroke(10, 10, ignorePressure: false));
        var original = CreateStroke(40, 20, ignorePressure: false);
        source.AddStrokeQuiet(original);
        target.AddStrokeQuiet(CreateStroke(90, 90, ignorePressure: false));

        var originalPlacement = source.CaptureStrokePlacement(original);
        var idealSnapshot = originalPlacement.Snapshot
            .WithSide(StrokeReplacementSide.Ideal)
            .WithIgnorePressure(true);
        Assert.That(source.TryReplaceStrokeQuiet(
            originalPlacement.Token,
            StrokeReplacementSide.Original,
            idealSnapshot,
            out _), Is.True);

        var idealPlacement = source.CaptureStrokePlacement(source.GetStrokes()[originalPlacement.Index]);
        var moveAction = CreateNestedAction(
            "SelectionCrossPageMoveAction",
            source,
            target,
            0d,
            0d,
            0d,
            0d,
            new List<StrokePlacement> { idealPlacement },
            new List<System.Windows.Controls.Grid>());
        InvokeVoid(moveAction, "ExecuteInitialTransfer");

        Assert.That(source.GetStrokes().Count, Is.EqualTo(1));
        Assert.That(target.GetStrokes().Count, Is.EqualTo(2));

        InvokeTask(moveAction, "UndoAsync");
        Assert.That(source.GetStrokes().Count, Is.EqualTo(2));
        Assert.That(target.GetStrokes().Count, Is.EqualTo(1));
        Assert.That(source.GetStrokes()[originalPlacement.Index].StylusPoints[0].X, Is.EqualTo(40));

        Assert.That(source.TryReplaceStrokeQuiet(
            originalPlacement.Token,
            StrokeReplacementSide.Ideal,
            originalPlacement.Snapshot,
            out var restoredIndex), Is.True);
        Assert.That(restoredIndex, Is.EqualTo(originalPlacement.Index));
        Assert.That(source.GetStrokes().Count, Is.EqualTo(2));
        Assert.That(source.GetStrokes()[1].DrawingAttributes.IgnorePressure, Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CrossPageMove_SameTokenAndSideConflictKeepsSourceAndDoesNotClaimSuccess()
    {
        var source = new PdfPageControl();
        var target = new PdfPageControl();
        var sourceStroke = CreateStroke(20, 20, ignorePressure: false);
        source.AddStrokeQuiet(sourceStroke);
        var sourcePlacement = source.CaptureStrokePlacement(sourceStroke);

        // Seed the target with a different live stroke carrying the same
        // logical token/side.  This is the stale cross-page collision that
        // used to be reported as an idempotent Add and then deleted source.
        var conflictingStroke = CreateStroke(180, 180, ignorePressure: true);
        var conflictingPlacement = CreateDeterministicPlacement(
            target,
            conflictingStroke,
            sourcePlacement.Token,
            sourcePlacement.Side,
            0);
        Assert.That(target.AddStrokeQuiet(conflictingPlacement), Is.Not.Null);

        var moveAction = CreateNestedAction(
            "SelectionCrossPageMoveAction",
            source,
            target,
            0d,
            0d,
            0d,
            0d,
            new List<StrokePlacement> { sourcePlacement },
            new List<System.Windows.Controls.Grid>());

        InvokeVoid(moveAction, "ExecuteInitialTransfer");

        Assert.Multiple(() =>
        {
            Assert.That(source.GetStrokes().Count, Is.EqualTo(1),
                "the source must not be deleted when the target identity conflicts");
            Assert.That(target.GetStrokes().Count, Is.EqualTo(1));
            Assert.That(target.GetStrokes()[0], Is.SameAs(conflictingStroke));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CrossPageMultiSelectionConflictRollsBackEveryEarlierStrokeAndReportsFailure()
    {
        var source = new PdfPageControl();
        var target = new PdfPageControl();
        var sourceBaseline = CreateStroke(10, 10, ignorePressure: false);
        source.AddStrokeQuiet(sourceBaseline);

        var firstToken = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var secondToken = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var firstStroke = CreateStroke(40, 20, ignorePressure: false);
        var secondStroke = CreateStroke(70, 30, ignorePressure: false);
        var firstPlacement = CreateDeterministicPlacement(
            source, firstStroke, firstToken, StrokeReplacementSide.Original, index: 1);
        var secondPlacement = CreateDeterministicPlacement(
            source, secondStroke, secondToken, StrokeReplacementSide.Original, index: 2);
        Assert.That(source.AddStrokeQuiet(firstPlacement), Is.Not.Null);
        Assert.That(source.AddStrokeQuiet(secondPlacement), Is.Not.Null);

        var targetBaseline = CreateStroke(90, 90, ignorePressure: false);
        target.AddStrokeQuiet(targetBaseline);
        var conflictingStroke = CreateStroke(120, 120, ignorePressure: true);
        var conflictingPlacement = CreateDeterministicPlacement(
            target, conflictingStroke, secondToken, StrokeReplacementSide.Original, index: 1);
        Assert.That(target.AddStrokeQuiet(conflictingPlacement), Is.Not.Null);

        var moveAction = CreateNestedAction(
            "SelectionCrossPageMoveAction",
            source,
            target,
            15d,
            0d,
            0d,
            0d,
            new List<StrokePlacement> { firstPlacement, secondPlacement },
            new List<System.Windows.Controls.Grid>());

        bool transferred = InvokeBool(moveAction, "ExecuteInitialTransfer");

        Assert.Multiple(() =>
        {
            Assert.That(transferred, Is.False,
                "a partial multi-selection transfer must not be reported as an undoable action");
            Assert.That(source.GetStrokes().Count, Is.EqualTo(3));
            Assert.That(source.GetStrokes()[1], Is.SameAs(firstStroke));
            Assert.That(source.GetStrokes()[2], Is.SameAs(secondStroke));
            Assert.That(target.GetStrokes().Count, Is.EqualTo(2));
            Assert.That(target.GetStrokes()[0], Is.SameAs(targetBaseline));
            Assert.That(target.GetStrokes()[1], Is.SameAs(conflictingStroke));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CrossPageMultiSelectionSourceCaptureFailureRollsBackEarlierAdds()
    {
        var source = new PdfPageControl();
        var target = new PdfPageControl();
        var baseline = CreateStroke(10, 10, ignorePressure: false);
        source.AddStrokeQuiet(baseline);

        var firstToken = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var secondToken = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var firstStroke = CreateStroke(40, 20, ignorePressure: false);
        var secondStroke = CreateStroke(70, 30, ignorePressure: false);
        var firstPlacement = CreateDeterministicPlacement(
            source, firstStroke, firstToken, StrokeReplacementSide.Original, index: 1);
        var actualSecondPlacement = CreateDeterministicPlacement(
            source, secondStroke, secondToken, StrokeReplacementSide.Original, index: 2);
        Assert.That(source.AddStrokeQuiet(firstPlacement), Is.Not.Null);
        Assert.That(source.AddStrokeQuiet(actualSecondPlacement), Is.Not.Null);

        // The source still contains the second stroke, but its expected side
        // is stale. Capture must fail after the first item has already moved.
        var staleSecondPlacement = CreateDeterministicPlacement(
            source,
            secondStroke,
            secondToken,
            StrokeReplacementSide.Ideal,
            index: 2);
        target.AddStrokeQuiet(CreateStroke(90, 90, ignorePressure: false));

        var moveAction = CreateNestedAction(
            "SelectionCrossPageMoveAction",
            source,
            target,
            15d,
            0d,
            0d,
            0d,
            new List<StrokePlacement> { firstPlacement, staleSecondPlacement },
            new List<System.Windows.Controls.Grid>());

        bool transferred = InvokeBool(moveAction, "ExecuteInitialTransfer");

        Assert.Multiple(() =>
        {
            Assert.That(transferred, Is.False);
            Assert.That(source.GetStrokes().Count, Is.EqualTo(3));
            Assert.That(source.GetStrokes()[1], Is.SameAs(firstStroke));
            Assert.That(source.GetStrokes()[2], Is.SameAs(secondStroke));
            Assert.That(target.GetStrokes().Count, Is.EqualTo(1));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void SnapshotRoundTrip_PreservesPressureFactorAndIgnorePressureSide()
    {
        var page = new PdfPageControl();
        var original = CreateStroke(20, 20, ignorePressure: false);
        page.AddStrokeQuiet(original);
        var placement = page.CaptureStrokePlacement(original);
        var ideal = placement.Snapshot.WithSide(StrokeReplacementSide.Ideal).WithIgnorePressure(true);

        Assert.That(placement.Snapshot.Points[0].PressureFactor, Is.EqualTo(0.2).Within(0.0001));
        Assert.That(page.TryReplaceStrokeQuiet(
            placement.Token,
            StrokeReplacementSide.Original,
            ideal,
            out _), Is.True);
        Assert.That(page.GetStrokes()[0].StylusPoints[0].PressureFactor, Is.EqualTo(0.2).Within(0.0001));
        Assert.That(page.GetStrokes()[0].DrawingAttributes.IgnorePressure, Is.True);

        Assert.That(page.TryReplaceStrokeQuiet(
            placement.Token,
            StrokeReplacementSide.Ideal,
            placement.Snapshot,
            out _), Is.True);
        Assert.That(page.GetStrokes()[0].StylusPoints[0].PressureFactor, Is.EqualTo(0.2).Within(0.0001));
        Assert.That(page.GetStrokes()[0].DrawingAttributes.IgnorePressure, Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void GetStrokes_DoesNotExposeTheLiveMutableStrokeCollection()
    {
        var page = new PdfPageControl();
        page.AddStrokeQuiet(CreateStroke(10, 10, ignorePressure: false));

        var exposed = page.GetStrokes();

        Assert.That(exposed.Count, Is.EqualTo(1));
        exposed.Clear();
        Assert.That(page.GetStrokes().Count, Is.EqualTo(1));
    }

    private static Stroke CreateStroke(double x, double y, bool ignorePressure)
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(x, y, 0.2f),
            new StylusPoint(x + 20, y + 10, 0.8f)
        };
        return new Stroke(points)
        {
            DrawingAttributes = new DrawingAttributes
            {
                Color = Colors.Blue,
                Width = 3,
                Height = 3,
                IgnorePressure = ignorePressure,
                FitToCurve = true,
                IsHighlighter = false
            }
        };
    }

    private static Stroke CreateRecognizedLineStroke()
    {
        var points = new StylusPointCollection();
        for (int index = 0; index < 12; index++)
            points.Add(new StylusPoint(20 + index * 10, 40 + index * 4, 0.2f + index * 0.05f));

        return new Stroke(points)
        {
            DrawingAttributes = new DrawingAttributes
            {
                Color = Colors.Blue,
                Width = 3,
                Height = 3,
                IgnorePressure = false,
                FitToCurve = true,
                IsHighlighter = false
            }
        };
    }

    private static StrokePlacement CreateDeterministicPlacement(
        PdfPageControl owner,
        Stroke stroke,
        Guid token,
        StrokeReplacementSide side,
        int index)
    {
        var points = stroke.StylusPoints
            .Select(point => new StrokeReplacementPoint(point.X, point.Y, point.PressureFactor))
            .ToList();
        var attrs = stroke.DrawingAttributes;
        var color = attrs.Color;
        var snapshot = new StrokeReplacementSnapshot(
            token,
            side,
            points,
            color.R,
            color.G,
            color.B,
            color.A,
            attrs.Width,
            attrs.Height,
            attrs.IsHighlighter,
            attrs.FitToCurve,
            attrs.IgnorePressure);
        return new StrokePlacement(owner, stroke, snapshot, index);
    }

    private static object CreateNestedAction(string name, params object[] args)
    {
        var type = typeof(EditorPage).GetNestedType(
            name,
            BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(EditorPage).FullName, name);
        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: args,
            culture: null)
            ?? throw new InvalidOperationException($"Could not construct {name}.");
    }

    private static void InvokeEditorPrivate(EditorPage editor, string methodName, params object[] args)
    {
        var method = typeof(EditorPage).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(EditorPage).FullName, methodName);
        method.Invoke(editor, args);
    }

    private static void InvokeVoid(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        method.Invoke(instance, null);
    }

    private static bool InvokeBool(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        return method.Invoke(instance, null) is bool value && value;
    }

    private static void InvokeTask(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        var task = method.Invoke(instance, null) as Task
            ?? throw new InvalidOperationException($"{methodName} did not return Task.");
        task.GetAwaiter().GetResult();
    }

    private static void EnsureEditorResources()
    {
        var application = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        if (!application.Resources.Contains("ToolbarFocusVisualStyle"))
            application.Resources["ToolbarFocusVisualStyle"] = new Style(typeof(Control));
        if (!application.Resources.Contains("SleekScrollViewer"))
            application.Resources["SleekScrollViewer"] = new Style(typeof(ScrollViewer));
        if (!application.Resources.Contains("CompactComboBox"))
            application.Resources["CompactComboBox"] = new Style(typeof(ComboBox));
    }
}
