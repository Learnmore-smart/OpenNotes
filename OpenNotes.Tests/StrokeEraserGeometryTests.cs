using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Pages;
using Caelum.Services;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class StrokeEraserGeometryTests
{
    private static readonly MethodInfo BeginEraseGestureMethod =
        typeof(PdfPageControl).GetMethod("BeginEraseGesture", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.BeginEraseGesture was not found.");

    private static readonly MethodInfo EraseStrokesAtPointsMethod =
        typeof(PdfPageControl).GetMethod("EraseStrokesAtPoints", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.EraseStrokesAtPoints was not found.");

    private static readonly MethodInfo EndEraseGestureMethod =
        typeof(PdfPageControl).GetMethod("EndEraseGesture", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.EndEraseGesture was not found.");

    private static readonly MethodInfo MouseUpMethod =
        typeof(PdfPageControl).GetMethod("InkCanvas_MouseUp", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.InkCanvas_MouseUp was not found.");

    [OneTimeSetUp]
    public void NormalizeWpfEnvironment()
    {
        WindowsEnvironment.NormalizeForWpf();
    }

    [Test]
    public void WholeStroke_DoesNotRemoveDiagonalWhoseBoundsOnlyOverlapEraser()
    {
        var page = CreatePage(wholeStroke: true);
        page.AddStrokeQuiet(CreateStroke(new Point(0, 0), new Point(100, 100)));

        RunEraseGesture(page, new Point(10, 90));

        Assert.That(page.GetStrokes().Count, Is.EqualTo(1),
            "An eraser rectangle may overlap a stroke's broad bounds without touching the visible diagonal path.");
    }

    [Test]
    public void PixelEraser_SplitsSparseStraightLineAtCrossing()
    {
        var page = CreatePage(wholeStroke: false);
        page.AddStrokeQuiet(CreateStroke(new Point(0, 50), new Point(100, 50)));

        EraseGesture(page, new Point(50, 50));

        Assert.That(page.GetStrokes().Count, Is.EqualTo(2),
            "Pixel erasing must split a sparse two-point line at the crossed segment, not only inspect stroke vertices.");
    }

    [Test]
    public void PixelEraser_DoesNotRemoveDiagonalWhoseBoundsOnlyOverlapEraser()
    {
        var page = CreatePage(wholeStroke: false);
        page.AddStrokeQuiet(CreateStroke(new Point(0, 0), new Point(100, 100)));

        var payload = RunEraseGesture(page, new Point(10, 90));

        Assert.Multiple(() =>
        {
            Assert.That(page.GetStrokes().Count, Is.EqualTo(1),
                "Pixel erasing must not remove a diagonal merely because its bounds overlap the eraser rectangle.");
            Assert.That(payload, Is.Null,
                "A path miss must not publish a misleading erase history payload.");
        });
    }

    [Test]
    public void PixelEraser_ConnectsPreviousAndCurrentUpdatesAcrossFastMove()
    {
        var page = CreatePage(wholeStroke: false);
        page.AddStrokeQuiet(CreateStroke(new Point(0, 50), new Point(100, 50)));

        var payload = RunEraseGesture(
            page,
            new Point(25, 20),
            new Point(75, 80));

        Assert.Multiple(() =>
        {
            Assert.That(page.GetStrokes().Count, Is.EqualTo(2),
                "A fast eraser move must sweep the segment between updates, even when both endpoints miss the ink.");
            Assert.That(payload, Is.Not.Null,
                "A swept crossing must publish one erase payload for the gesture.");
        });
    }

    [Test]
    public void Eraser_AcceptsStylusPacketsWithExtendedDeviceProperties()
    {
        var page = CreatePage(wholeStroke: true);
        page.AddStrokeQuiet(CreateStroke(new Point(0, 50), new Point(100, 50)));

        BeginEraseGestureMethod.Invoke(page, null);
        Assert.DoesNotThrow(
            () => EraseStrokesAtPointsMethod.Invoke(
                page,
                new object[] { ToExtendedStylusPoints(new Point(50, 50)) }),
            "Real digitizer packets may include tilt/button properties and must not crash the eraser path.");
        EndEraseGestureMethod.Invoke(page, null);

        Assert.That(page.GetStrokes(), Is.Empty,
            "Normalizing device packets to coordinates must preserve whole-stroke erasing.");
    }

    [Test]
    public void UndoThenEraseAgain_CreatesASecondValidEraseGesture()
    {
        var page = CreatePage(wholeStroke: false);
        var original = CreateStroke(new Point(0, 50), new Point(100, 50));
        page.AddStrokeQuiet(original);

        var firstErase = EraseGesture(page, new Point(50, 50));
        Assert.That(firstErase.RemovedPlacements, Has.Count.EqualTo(1));
        Assert.That(firstErase.AddedPlacements, Has.Count.EqualTo(2));

        var undoAction = CreateStrokesErasedAction(
            page,
            firstErase.RemovedPlacements,
            firstErase.AddedPlacements);
        InvokeTask(undoAction, "UndoAsync");
        Assert.That(page.GetStrokes(), Has.One.SameAs(original),
            "Undo must restore the original live stroke before the next gesture.");

        var secondErase = EraseGesture(page, new Point(50, 50));

        Assert.Multiple(() =>
        {
            Assert.That(page.GetStrokes(), Has.Count.EqualTo(2));
            Assert.That(page.GetStrokes(), Has.None.SameAs(original));
            Assert.That(secondErase.RemovedPlacements, Has.Count.EqualTo(1));
            Assert.That(secondErase.AddedPlacements, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void EraserModePopup_PropagatesUpdatedModeWhenCachedSettingsAreStale()
    {
        EnsureEditorResources();
        var previousSettings = AppSettingsService.Load();
        var settings = AppSettingsService.Load();
        settings.WholeStrokeEraser = false;
        AppSettingsService.Save(settings);

        try
        {
            var editor = new EditorPage();
            var page = new PdfPageControl();
            var pageControls = typeof(EditorPage).GetField(
                "_pageControls",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new AssertionException("EditorPage._pageControls was not found.");
            ((System.Collections.IList)pageControls.GetValue(editor)!).Add(page);

            page.WholeStrokeEraser = false;

            var wholeButton = FindEraserModeButton(editor, "Editor.Eraser.WholeStroke");
            wholeButton.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent));

            Assert.That(page.WholeStrokeEraser, Is.True,
                "Changing eraser mode must propagate the newly saved setting to registered pages immediately.");
        }
        finally
        {
            AppSettingsService.Save(previousSettings);
        }
    }

    [Test]
    public void CancelInteraction_ClearsMouseEraseStateAndPreviousPoint()
    {
        var page = CreatePage(wholeStroke: false);
        page.AddStrokeQuiet(CreateStroke(new Point(0, 50), new Point(100, 50)));

        // Exercise the mouse-like private erase seam without requiring a
        // foreground device. The production mouse-down path owns this flag;
        // setting it here models the in-flight state after that down event.
        BeginEraseGestureMethod.Invoke(page, null);
        EraseStrokesAtPointsMethod.Invoke(
            page,
            new object[] { ToStylusPoints(new Point(200, 200)) });
        SetPrivateValue(page, "_isErasing", true);
        Assert.That(page.HasActiveInteraction, Is.True);

        page.CancelInteraction("test cancellation");

        Assert.Multiple(() =>
        {
            Assert.That(page.HasActiveInteraction, Is.False,
                "Cancelling an erase must release the page's active interaction state.");
            Assert.That(GetPrivateValue(page, "_isErasing"), Is.EqualTo(false));
            Assert.That(GetPrivateValue(page, "_erasePoints"), Is.Null);
            Assert.That(GetPrivateValue(page, "_lastErasePoint"), Is.Null,
                "A cancelled mouse erase must not leak its previous pointer point into a later gesture.");
            Assert.That(page.InkCanvas.IsMouseCaptured, Is.False);
            Assert.That(page.InkCanvas.IsStylusCaptured, Is.False);
        });
    }

    [Test]
    public void MouseUp_CommitsEraseBeforeReleasingMouseCapture()
    {
        var page = CreatePage(wholeStroke: false);
        page.SetInputMode(CustomInkInputProcessingMode.Erasing);
        page.AddStrokeQuiet(CreateStroke(new Point(0, 50), new Point(100, 50)));
        StrokesErasedEventArgs? payload = null;
        page.StrokesErased += (_, args) => payload = args;

        var host = new Window
        {
            Width = 320,
            Height = 240,
            Content = page,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow
        };

        try
        {
            host.Show();
            host.UpdateLayout();
            Assert.That(page.InkCanvas.CaptureMouse(), Is.True,
                "The regression requires the production LostMouseCapture route.");

            BeginEraseGestureMethod.Invoke(page, null);
            EraseStrokesAtPointsMethod.Invoke(
                page,
                new object[] { ToStylusPoints(new Point(50, 50)) });
            SetPrivateValue(page, "_isErasing", true);

            var mouseUp = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Mouse.MouseUpEvent,
                Source = page.InkCanvas
            };
            MouseUpMethod.Invoke(page, new object[] { page.InkCanvas, mouseUp });

            Assert.Multiple(() =>
            {
                Assert.That(payload, Is.Not.Null,
                    "Mouse-up must publish the erase transaction instead of LostMouseCapture rolling it back.");
                Assert.That(page.GetStrokes(), Has.Count.EqualTo(2));
                Assert.That(page.InkCanvas.IsMouseCaptured, Is.False);
                Assert.That(page.HasActiveInteraction, Is.False);
            });
        }
        finally
        {
            host.Close();
        }
    }

    [Test]
    public void CancelInteraction_RollsBackInkAndHiddenInkWithoutCompletionEvents()
    {
        var page = CreatePage(wholeStroke: false);
        var original = CreateStroke(new Point(0, 50), new Point(100, 50));
        page.AddStrokeQuiet(original);
        var hidden = new HiddenInkAnnotation
        {
            Id = "cancel-hidden",
            Size = 12,
            Points = new List<double[]> { new[] { 50d, 50d } }
        };
        page.AddHiddenInkQuiet(hidden);

        int strokesErasedEvents = 0;
        int hiddenInksRemovedEvents = 0;
        page.StrokesErased += (_, _) => strokesErasedEvents++;
        page.HiddenInksRemoved += (_, _) => hiddenInksRemovedEvents++;

        BeginEraseGestureMethod.Invoke(page, null);
        EraseStrokesAtPointsMethod.Invoke(
            page,
            new object[] { ToStylusPoints(new Point(50, 50)) });
        SetPrivateValue(page, "_isErasing", true);

        Assert.That(page.GetStrokes(), Has.Count.EqualTo(2));
        Assert.That(page.GetHiddenInkData(), Is.Empty);

        page.CancelInteraction("test cancellation");

        var restoredHidden = page.GetHiddenInkData();
        Assert.Multiple(() =>
        {
            Assert.That(page.GetStrokes(), Has.One.SameAs(original),
                "Cancelling an erase must restore the original stroke and discard clipped fragments.");
            Assert.That(restoredHidden, Has.Count.EqualTo(1));
            Assert.That(restoredHidden[0].Id, Is.EqualTo(hidden.Id));
            Assert.That(restoredHidden[0].Points[0], Is.EqualTo(hidden.Points[0]));
            Assert.That(strokesErasedEvents, Is.Zero,
                "Cancellation must not publish an undoable StrokesErased completion event.");
            Assert.That(hiddenInksRemovedEvents, Is.Zero,
                "Cancellation must not publish an undoable HiddenInksRemoved completion event.");
            Assert.That(page.HasActiveInteraction, Is.False);
            Assert.That(GetPrivateValue(page, "_eraseGestureRemovedPlacements"), Is.Null);
            Assert.That(GetPrivateValue(page, "_eraseGestureAddedPlacements"), Is.Null);
            Assert.That(GetPrivateValue(page, "_eraseGestureRemovedHiddenInks"), Is.Null);
            Assert.That(GetPrivateValue(page, "_lastErasePoint"), Is.Null);
        });
    }

    [Test]
    public void EraseUndo_ReportsConflictAndLeavesCollectionUnchanged()
    {
        var page = CreatePage(wholeStroke: false);
        var original = CreateStroke(new Point(0, 50), new Point(100, 50));
        page.AddStrokeQuiet(original);
        var erase = EraseGesture(page, new Point(50, 50));
        Assert.That(erase.AddedPlacements, Has.Count.EqualTo(2));

        // Keep both live fragments in the page, but give the lower-index
        // expected placement the wrong replacement side. The higher-index
        // fragment is removed first, so the action must roll it back when the
        // token/side lookup rejects the invalid lower-index placement.
        var invalidFragment = erase.AddedPlacements[0];
        var invalidPlacement = new StrokePlacement(
            page,
            invalidFragment.Stroke,
            invalidFragment.Snapshot.WithSide(StrokeReplacementSide.Ideal),
            invalidFragment.Index);

        var action = CreateStrokesErasedAction(
            page,
            erase.RemovedPlacements,
            new[] { invalidPlacement, erase.AddedPlacements[1] });
        InvokeTask(action, "UndoAsync");

        var successProperty = action.GetType().GetProperty("LastOperationSucceeded")
            ?? throw new AssertionException("StrokesErasedAction.LastOperationSucceeded was not found.");
        var succeeded = (bool)successProperty.GetValue(action)!;

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False,
                "A token/side conflict must make erase undo fail instead of silently changing the document.");
            Assert.That(page.GetStrokes(), Has.Count.EqualTo(2));
            Assert.That(page.GetStrokes(), Has.One.SameAs(erase.AddedPlacements[0].Stroke));
            Assert.That(page.GetStrokes(), Has.One.SameAs(erase.AddedPlacements[1].Stroke));
            Assert.That(page.GetStrokes(), Has.None.SameAs(original));
        });
    }

    private static PdfPageControl CreatePage(bool wholeStroke)
    {
        var page = new PdfPageControl
        {
            WholeStrokeEraser = wholeStroke
        };
        page.SetEraserSize(20);
        return page;
    }

    private static Stroke CreateStroke(params Point[] points)
    {
        var stylusPoints = new StylusPointCollection();
        foreach (var point in points)
            stylusPoints.Add(new StylusPoint(point.X, point.Y));

        return new Stroke(stylusPoints)
        {
            DrawingAttributes = new DrawingAttributes
            {
                Color = Colors.Blue,
                Width = 3,
                Height = 3,
                FitToCurve = true,
                IgnorePressure = true,
                IsHighlighter = false
            }
        };
    }

    private static StrokesErasedEventArgs EraseGesture(PdfPageControl page, Point point)
    {
        return RunEraseGesture(page, point)
            ?? throw new AssertionException("The erase gesture did not report a StrokesErased payload.");
    }

    private static StrokesErasedEventArgs? RunEraseGesture(PdfPageControl page, params Point[] points)
    {
        StrokesErasedEventArgs? payload = null;
        EventHandler<StrokesErasedEventArgs> handler = (_, args) => payload = args;
        page.StrokesErased += handler;
        try
        {
            BeginEraseGestureMethod.Invoke(page, null);
            foreach (var point in points)
                EraseStrokesAtPointsMethod.Invoke(page, new object[] { ToStylusPoints(point) });
            EndEraseGestureMethod.Invoke(page, null);
        }
        finally
        {
            page.StrokesErased -= handler;
        }

        return payload;
    }

    private static ToggleButton FindEraserModeButton(EditorPage editor, string automationId)
    {
        var popup = GetPrivateField<Popup>(editor, "_eraserPopup");
        if (popup.Child is Border border)
        {
            var panel = border.Child switch
            {
                StackPanel direct => direct,
                ScrollViewer { Content: StackPanel scrolled } => scrolled,
                _ => null
            };
            if (panel != null)
            {
                foreach (var row in panel.Children.OfType<StackPanel>())
                {
                    foreach (var button in row.Children.OfType<ToggleButton>())
                    {
                        if (string.Equals(
                            AutomationProperties.GetAutomationId(button),
                            automationId,
                            StringComparison.Ordinal))
                        {
                            return button;
                        }
                    }
                }
            }
        }

        throw new AssertionException($"Eraser mode button '{automationId}' was not found.");
    }

    private static T GetPrivateField<T>(object target, string name)
        where T : class
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T
            ?? throw new AssertionException($"Private field '{name}' was not initialized.");
    }

    private static void SetPrivateValue(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Private field '{name}' was not found.");
        field.SetValue(target, value);
    }

    private static object? GetPrivateValue(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Private field '{name}' was not found.");
        return field.GetValue(target);
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

    private static StylusPointCollection ToStylusPoints(Point point)
    {
        return new StylusPointCollection
        {
            new StylusPoint(point.X, point.Y)
        };
    }

    private static StylusPointCollection ToExtendedStylusPoints(Point point)
    {
        var description = new StylusPointDescription(new[]
        {
            new StylusPointPropertyInfo(StylusPointProperties.X),
            new StylusPointPropertyInfo(StylusPointProperties.Y),
            new StylusPointPropertyInfo(StylusPointProperties.NormalPressure),
            new StylusPointPropertyInfo(StylusPointProperties.XTiltOrientation)
        });
        var points = new StylusPointCollection(description);
        points.Add(new StylusPoint(point.X, point.Y, 0.5f, description, new[] { 1800 }));
        return points;
    }

    private static object CreateStrokesErasedAction(
        PdfPageControl page,
        IReadOnlyList<StrokePlacement> removed,
        IReadOnlyList<StrokePlacement> added)
    {
        var actionType = typeof(EditorPage).GetNestedType(
            "StrokesErasedAction",
            BindingFlags.NonPublic)
            ?? throw new AssertionException("EditorPage.StrokesErasedAction was not found.");
        var constructor = actionType.GetConstructor(new[]
        {
            typeof(PdfPageControl),
            typeof(List<StrokePlacement>),
            typeof(List<StrokePlacement>)
        }) ?? throw new AssertionException("EditorPage.StrokesErasedAction placement constructor was not found.");

        return constructor.Invoke(new object[]
        {
            page,
            removed.ToList(),
            added.ToList()
        });
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
}
