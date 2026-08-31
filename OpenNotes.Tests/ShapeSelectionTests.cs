using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Pages;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public sealed class ShapeSelectionTests
{
    [Test]
    public void SelectingOneLogicalShapePartExpandsToTheWholeGroup()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();
        var page = new PdfPageControl();
        var first = CreateStroke(new Point(10, 10), new Point(30, 10));
        var second = CreateStroke(new Point(40, 10), new Point(60, 10));
        ShapeStrokeMetadata.Apply(first, "dash-group", "DashedLine", 0, true);
        ShapeStrokeMetadata.Apply(second, "dash-group", "DashedLine", 1, true);
        page.AddStrokeQuiet(first);
        page.AddStrokeQuiet(second);

        page.SelectItems(new[] { first }, Array.Empty<Grid>());

        Assert.That(page.SelectedStrokes, Is.EquivalentTo(new[] { first, second }));
    }

    [Test]
    public void SelectedDrawingStyleAppliesToWholeLogicalGroupAndRetainsSelection()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();
        var editor = new EditorPage();
        var page = new PdfPageControl();
        var first = CreateStroke(new Point(10, 10), new Point(30, 10));
        var second = CreateStroke(new Point(40, 10), new Point(60, 10));
        ShapeStrokeMetadata.Apply(first, "style-group", "DashedLine", 0, true);
        ShapeStrokeMetadata.Apply(second, "style-group", "DashedLine", 1, true);
        page.AddStrokeQuiet(first);
        page.AddStrokeQuiet(second);
        page.SelectItems(new[] { first }, Array.Empty<Grid>());
        SetPrivateField(editor, "_activeSelectionPage", page);

        var apply = typeof(EditorPage).GetMethod(
            "ApplySelectedDrawingStyle",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        apply.Invoke(editor, new object?[] { Colors.Red, 6d });

        Assert.Multiple(() =>
        {
            Assert.That(page.SelectedStrokes, Is.EquivalentTo(new[] { first, second }));
            Assert.That(page.SelectedStrokes.Select(stroke => stroke.DrawingAttributes.Color),
                Has.All.EqualTo(Colors.Red));
            Assert.That(page.SelectedStrokes.Select(stroke => stroke.DrawingAttributes.Width),
                Has.All.EqualTo(6d));
        });
    }

    private static readonly MethodInfo HitStrokeMethod =
        typeof(PdfPageControl).GetMethod("HitStroke", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.HitStroke was not found.");

    private static readonly MethodInfo HandleCtrlClickToggleMethod =
        typeof(PdfPageControl).GetMethod("HandleCtrlClickToggle", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.HandleCtrlClickToggle was not found.");

    private static readonly MethodInfo ShouldClosePopupOnPointerDownMethod =
        typeof(EditorPage).GetMethod("ShouldClosePopupOnPointerDown", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("EditorPage.ShouldClosePopupOnPointerDown was not found.");

    private static readonly MethodInfo EditorPagePreviewMouseDownMethod =
        typeof(EditorPage).GetMethod("EditorPage_PreviewMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("EditorPage.EditorPage_PreviewMouseDown was not found.");

    private static readonly MethodInfo IsStrokeInsidePolygonMethod =
        typeof(PdfPageControl).GetMethod("IsStrokeInsidePolygon", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.IsStrokeInsidePolygon was not found.");

    private static readonly MethodInfo IsStrokeInsideRectMethod =
        typeof(PdfPageControl).GetMethod("IsStrokeInsideRect", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.IsStrokeInsideRect was not found.");

    private static readonly MethodInfo BuildShapeOutlineMethod =
        typeof(PdfPageControl).GetMethod("BuildShapeOutline", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.BuildShapeOutline was not found.");

    [Test]
    public void ClickInsideClosedShapeHitsShape()
    {
        // A triangle from (10, 10) to (110, 110)
        var points = (IReadOnlyList<Point>)BuildShapeOutlineMethod.Invoke(null, new object[] { ShapeKind.Triangle, new Point(10, 10), new Point(110, 110) })!;
        var stylusPoints = new StylusPointCollection();
        foreach (var p in points)
            stylusPoints.Add(new StylusPoint(p.X, p.Y));
        var stroke = new Stroke(stylusPoints);

        // Center of triangle should hit
        var centerPoint = new Point(60, 60);
        var isHit = (bool)HitStrokeMethod.Invoke(null, new object[] { stroke, centerPoint })!;
        Assert.That(isHit, Is.True, "Clicking inside a triangle shape must hit the stroke.");

        // Point outside triangle should not hit
        var outsidePoint = new Point(15, 15); // near top-left empty corner
        var isOutsideHit = (bool)HitStrokeMethod.Invoke(null, new object[] { stroke, outsidePoint })!;
        Assert.That(isOutsideHit, Is.False, "Point outside triangle geometry should not hit.");
    }

    [Test]
    public void ClickOnLineShapeHitsStroke()
    {
        var linePoints = new[] { new StylusPoint(20, 20), new StylusPoint(100, 100) };
        var stroke = new Stroke(new StylusPointCollection(linePoints));

        // Click close to the line (e.g. (60, 62) - within 2px of (60, 60))
        var nearPoint = new Point(60, 62);
        var isHit = (bool)HitStrokeMethod.Invoke(null, new object[] { stroke, nearPoint })!;
        Assert.That(isHit, Is.True, "Clicking on/near a line shape must hit the stroke.");
    }

    [Test]
    public void BroadOpenStrokeInteriorUsesBoundedFallback()
    {
        var stroke = CreateStroke(
            new Point(20, 20),
            new Point(20, 120),
            new Point(120, 120));

        var interiorPoint = new Point(90, 45);
        var isHit = (bool)HitStrokeMethod.Invoke(null, new object[] { stroke, interiorPoint })!;
        Assert.That(isHit, Is.True,
            "A broad open drawing must remain selectable from its visible bounded area, not only on its sampled path.");

        var outsidePoint = new Point(140, 45);
        var isOutsideHit = (bool)HitStrokeMethod.Invoke(null, new object[] { stroke, outsidePoint })!;
        Assert.That(isOutsideHit, Is.False, "The bounded fallback must not select outside the drawing bounds.");
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void OpenSelectPopupDoesNotConsumeFirstCanvasSelectionClick()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();

        var editor = new EditorPage();
        SetCurrentTool(editor, "Select");
        var popup = GetPrivateField<Popup>(editor, "_selectionPopup");

        try
        {
            popup.IsOpen = true;

            var invocationArguments = new object?[] { new Border(), false };
            var closesPopup = (bool)ShouldClosePopupOnPointerDownMethod.Invoke(
                editor, invocationArguments)!;

            Assert.That(closesPopup, Is.True, "An outside canvas pointer must still dismiss the Select popup.");
            Assert.That(invocationArguments[1], Is.EqualTo(false),
                "The dismissal pointer must continue to the canvas so the first selection click is not lost.");
        }
        finally
        {
            popup.IsOpen = false;
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void FirstPostDismissalCanvasGestureSelectsStrokeThroughEditorRoute()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();

        var editor = new EditorPage();
        SetCurrentTool(editor, "Select");
        var popup = GetPrivateField<Popup>(editor, "_selectionPopup");
        var page = new PdfPageControl { Width = 240, Height = 240 };
        var stroke = CreateStroke(
            new Point(20, 20),
            new Point(20, 120),
            new Point(120, 120));

        page.AddStrokeQuiet(stroke);
        page.SetSelectionFilter(SelectionFilter.DrawingsOnly);
        page.SetSelectionShape(SelectionShape.FreeForm);
        page.SetSelectionMode(true);
        var pageControls = typeof(EditorPage).GetField("_pageControls", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("EditorPage._pageControls was not found.");
        ((System.Collections.IList)pageControls.GetValue(editor)!).Add(page);

        try
        {
            popup.IsOpen = true;
            var pointer = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseDownEvent
            };
            pointer.Source = new Border();

            EditorPagePreviewMouseDownMethod.Invoke(editor, new object[] { editor, pointer });

            // This is the routed seam: an unhandled dismissal pointer reaches
            // the real page selection delegate as the first canvas gesture.
            if (!pointer.Handled)
            {
                page.InvokeSelectionMouseDownCore(new Point(90, 45));
                page.InvokeSelectionMouseUpCore();
            }

            Assert.That(pointer.Handled, Is.False,
                "The outside Select-popup pointer must remain available to the canvas route.");
            Assert.That(page.SelectedStrokes, Is.EqualTo(new[] { stroke }),
                "The first post-dismissal canvas gesture must select the stroke.");
        }
        finally
        {
            page.SetSelectionMode(false);
            popup.IsOpen = false;
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void SamePageClickCtrlToggleAndEmptyClickPreserveSelectionSet()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();

        var page = new PdfPageControl { Width = 240, Height = 240 };
        var first = CreateStroke(
            new Point(20, 20),
            new Point(20, 120),
            new Point(120, 120));
        var second = CreateStroke(
            new Point(20, 140),
            new Point(20, 220),
            new Point(120, 220));

        page.AddStrokeQuiet(first);
        page.AddStrokeQuiet(second);
        page.SetSelectionFilter(SelectionFilter.DrawingsOnly);
        page.SetSelectionShape(SelectionShape.FreeForm);
        page.SetSelectionMode(true);

        try
        {
            // A normal click establishes the first item on this page.
            page.InvokeSelectionMouseDownCore(new Point(90, 45));
            page.InvokeSelectionMouseUpCore();
            Assert.That(page.SelectedStrokes, Is.EqualTo(new[] { first }));

            // Ctrl-click adds a second item without clearing the first.
            HandleCtrlClickToggleMethod.Invoke(page, new object[] { new Point(90, 165) });
            Assert.That(page.SelectedStrokes, Is.EquivalentTo(new[] { first, second }));

            // Ctrl-clicking empty page space keeps the same-page set intact.
            HandleCtrlClickToggleMethod.Invoke(page, new object[] { new Point(200, 40) });
            Assert.That(page.SelectedStrokes, Is.EquivalentTo(new[] { first, second }));

            // Ctrl-clicking the first item removes only that item.
            HandleCtrlClickToggleMethod.Invoke(page, new object[] { new Point(90, 45) });
            Assert.That(page.SelectedStrokes, Is.EqualTo(new[] { second }));
        }
        finally
        {
            page.SetSelectionMode(false);
        }
    }

    [Test]
    public void CancelInteractionClearsDelegatedSelectionRoute()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();

        var editor = new EditorPage();
        var page = new PdfPageControl();
        SetPrivateField(editor, "_isDelegatingSelection", true);
        SetPrivateField(editor, "_selectionDelegateTarget", page);

        editor.CancelInteraction("selection regression test");

        Assert.Multiple(() =>
        {
            Assert.That(GetPrivateValue<bool>(editor, "_isDelegatingSelection"), Is.False);
            Assert.That(GetPrivateValue<PdfPageControl?>(editor, "_selectionDelegateTarget"), Is.Null);
        });
    }

    [Test]
    public void StylusSelectionUsesStylusCaptureAndReleasesBothCaptureKinds()
    {
        string source = File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "Controls", "PdfPageControl.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("CaptureSelectionInput(fromStylus);"),
                "Selection starts must capture the route that originated the gesture.");
            Assert.That(source, Does.Contain("SelectionOverlayCanvas.ReleaseStylusCapture();"),
                "Normal selection completion must release stylus capture as well as mouse capture.");
        });
    }

    [Test]
    public void LassoPolygonAroundShapeSelectsStroke()
    {
        // Create an ellipse shape from (50, 50) to (150, 150)
        var points = (IReadOnlyList<Point>)BuildShapeOutlineMethod.Invoke(null, new object[] { ShapeKind.Ellipse, new Point(50, 50), new Point(150, 150) })!;
        var stylusPoints = new StylusPointCollection();
        foreach (var p in points)
            stylusPoints.Add(new StylusPoint(p.X, p.Y));
        var stroke = new Stroke(stylusPoints);

        // A round-ish lasso surrounding the ellipse (e.g. hexagon loosely around it)
        var lasso = new PointCollection
        {
            new Point(40, 40),
            new Point(160, 40),
            new Point(170, 100),
            new Point(160, 160),
            new Point(40, 160),
            new Point(30, 100),
            new Point(40, 40)
        };

        var isInside = (bool)IsStrokeInsidePolygonMethod.Invoke(null, new object[] { lasso, stroke })!;
        Assert.That(isInside, Is.True, "Lasso drawn around an ellipse must successfully select the shape stroke.");
    }

    [Test]
    public void RectMarqueeContainsShapeSelectsStroke()
    {
        var points = (IReadOnlyList<Point>)BuildShapeOutlineMethod.Invoke(null, new object[] { ShapeKind.Rectangle, new Point(50, 50), new Point(100, 100) })!;
        var stylusPoints = new StylusPointCollection();
        foreach (var p in points)
            stylusPoints.Add(new StylusPoint(p.X, p.Y));
        var stroke = new Stroke(stylusPoints);

        var marquee = new Rect(40, 40, 70, 70);
        var isInside = (bool)IsStrokeInsideRectMethod.Invoke(null, new object[] { marquee, stroke })!;
        Assert.That(isInside, Is.True, "Rectangle marquee covering the shape must select it.");
    }

    private static Stroke CreateStroke(params Point[] points)
    {
        var stylusPoints = new StylusPointCollection();
        foreach (var point in points)
            stylusPoints.Add(new StylusPoint(point.X, point.Y));
        return new Stroke(stylusPoints);
    }

    private static void SetCurrentTool(EditorPage editor, string toolName)
    {
        var field = typeof(EditorPage).GetField("_currentTool", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("EditorPage._currentTool was not found.");
        field.SetValue(editor, Enum.Parse(field.FieldType, toolName));
    }

    private static T GetPrivateField<T>(object target, string name)
        where T : class
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T
            ?? throw new AssertionException($"Private field '{name}' was not initialized.");
    }

    private static T GetPrivateValue<T>(object target, string name)
    {
        object? value = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);
        return value is null ? default! : (T)value;
    }

    private static void SetPrivateField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Private field '{name}' was not found.");
        field.SetValue(target, value);
    }

    private static void EnsureTestApplication()
    {
        var application = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        if (application.Resources["SleekScrollViewer"] == null)
            application.Resources["SleekScrollViewer"] = new Style(typeof(ScrollViewer));
        if (application.Resources["CompactComboBox"] == null)
            application.Resources["CompactComboBox"] = new Style(typeof(ComboBox));
        if (application.Resources["ToolbarFocusVisualStyle"] == null)
            application.Resources["ToolbarFocusVisualStyle"] = new Style(typeof(Control));
    }

    private static void EnsureWpfEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows");
    }
}
