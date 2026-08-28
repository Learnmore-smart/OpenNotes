using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Shapes;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Pages;
using NUnit.Framework;

namespace Caelum.Tests;

public sealed class EditorPopupDismissalTests
{
    private static readonly MethodInfo ShouldClosePopupOnPointerDownMethod =
        typeof(EditorPage).GetMethod("ShouldClosePopupOnPointerDown", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("EditorPage.ShouldClosePopupOnPointerDown was not found.");

    private static readonly MethodInfo EditorPagePreviewMouseDownMethod =
        typeof(EditorPage).GetMethod("EditorPage_PreviewMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("EditorPage.EditorPage_PreviewMouseDown was not found.");

    private static readonly MethodInfo InkCanvasStrokeCollectedMethod =
        typeof(PdfPageControl).GetMethod("InkCanvas_StrokeCollected", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.InkCanvas_StrokeCollected was not found.");

    private static readonly MethodInfo InkCanvasPreviewMouseLeftButtonDownPenOnlyMethod =
        typeof(PdfPageControl).GetMethod("InkCanvas_PreviewMouseLeftButtonDown_PenOnly", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.InkCanvas_PreviewMouseLeftButtonDown_PenOnly was not found.");

    private static readonly MethodInfo InkCanvasMouseUpMethod =
        typeof(PdfPageControl).GetMethod("InkCanvas_MouseUp", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.InkCanvas_MouseUp was not found.");

    private static readonly MethodInfo HiddenInkVisualMouseDownMethod =
        typeof(PdfPageControl).GetMethod("HiddenInkVisual_MouseLeftButtonDown", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.HiddenInkVisual_MouseLeftButtonDown was not found.");

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void StationaryOutsidePenPopupGestureDismissesWithoutInkHistoryOrDirtyState()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();
        var (editor, page, popup) = CreateHarness("Pen", CustomInkInputProcessingMode.Inking);

        try
        {
            popup.IsOpen = true;
            var pointer = CreateMouseDown(page.InkCanvas);

            EditorPagePreviewMouseDownMethod.Invoke(editor, new object[] { editor, pointer });

            Assert.Multiple(() =>
            {
                Assert.That(popup.IsOpen, Is.False, "The outside pointer must close the Pen popup.");
                Assert.That(pointer.Handled, Is.False,
                    "The native ink route must remain available until click-vs-drag is known.");
            });

            var tap = CreateStroke(new Point(120, 160));
            page.InkCanvas.Strokes.Add(tap);
            InkCanvasStrokeCollectedMethod.Invoke(
                page,
                new object[] { page, new InkCanvasStrokeCollectedEventArgs(tap) });

            Assert.Multiple(() =>
            {
                Assert.That(page.GetStrokes(), Is.Empty,
                    "A stationary outside-popup click must not leave an InkCanvas stroke.");
                Assert.That(GetUndoStack(editor), Is.Empty,
                    "A popup-dismissal click must not create an undo action.");
                Assert.That(editor.IsDirty, Is.False,
                    "A popup-dismissal click must not mark the document dirty.");
            });
        }
        finally
        {
            popup.IsOpen = false;
            RemoveHarnessPage(editor, page);
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void DismissingUnrelatedPopupWhilePenIsActiveDoesNotSuppressNativeInk()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();
        var (editor, page, penPopup) = CreateHarness("Pen", CustomInkInputProcessingMode.Inking);
        var unrelatedPopup = GetPrivateField<Popup>(editor, "_selectionPopup");

        try
        {
            unrelatedPopup.IsOpen = true;
            var pointer = CreateMouseDown(page.InkCanvas);

            EditorPagePreviewMouseDownMethod.Invoke(editor, new object[] { editor, pointer });

            Assert.That(unrelatedPopup.IsOpen, Is.False,
                "The outside page pointer must dismiss the unrelated transient surface.");

            var tap = CreateStroke(new Point(120, 160));
            page.InkCanvas.Strokes.Add(tap);
            InkCanvasStrokeCollectedMethod.Invoke(
                page,
                new object[] { page, new InkCanvasStrokeCollectedEventArgs(tap) });

            Assert.Multiple(() =>
            {
                Assert.That(page.GetStrokes(), Has.Count.EqualTo(1),
                    "Dismissing an unrelated popup must not arm Pen-popup tap suppression.");
                Assert.That(GetUndoStack(editor), Has.Count.EqualTo(1),
                    "The native stroke after unrelated popup dismissal must create ordinary history.");
                Assert.That(editor.IsDirty, Is.True,
                    "The native stroke after unrelated popup dismissal must retain dirty state.");
            });
        }
        finally
        {
            unrelatedPopup.IsOpen = false;
            penPopup.IsOpen = false;
            RemoveHarnessPage(editor, page);
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void OutsidePenPopupGestureThatCrossesSystemDragThresholdDrawsNormally()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();
        var (editor, page, popup) = CreateHarness("Pen", CustomInkInputProcessingMode.Inking);

        try
        {
            popup.IsOpen = true;
            var pointer = CreateMouseDown(page.InkCanvas);
            EditorPagePreviewMouseDownMethod.Invoke(editor, new object[] { editor, pointer });

            double dx = SystemParameters.MinimumHorizontalDragDistance + 2;
            double dy = SystemParameters.MinimumVerticalDragDistance + 2;
            var drag = CreateStroke(
                new Point(120, 160),
                new Point(120 + dx, 160 + dy),
                new Point(140 + dx, 180 + dy));
            page.InkCanvas.Strokes.Add(drag);
            InkCanvasStrokeCollectedMethod.Invoke(
                page,
                new object[] { page, new InkCanvasStrokeCollectedEventArgs(drag) });

            Assert.Multiple(() =>
            {
                Assert.That(page.GetStrokes(), Has.Count.EqualTo(1),
                    "A real drag from the popup-dismissal pointer must remain a drawing.");
                Assert.That(GetUndoStack(editor), Has.Count.EqualTo(1),
                    "A real drag must retain the ordinary one-stroke undo action.");
                Assert.That(editor.IsDirty, Is.True,
                    "A real drag must retain the ordinary dirty-state mutation.");
            });
        }
        finally
        {
            popup.IsOpen = false;
            RemoveHarnessPage(editor, page);
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void PenOnlyBlockedOutsidePopupMouseUpClearsPendingGestureBeforeNextShortStroke()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();
        var (editor, page, popup) = CreateHarness("Pen", CustomInkInputProcessingMode.Inking);

        try
        {
            popup.IsOpen = true;
            var outsideDown = CreateMouseDown(page.InkCanvas);
            EditorPagePreviewMouseDownMethod.Invoke(editor, new object[] { editor, outsideDown });

            page.PenOnlyMode = true;
            var blockedDown = CreateMouseDown(page.InkCanvas);
            InkCanvasPreviewMouseLeftButtonDownPenOnlyMethod.Invoke(
                page,
                new object[] { page, blockedDown });

            Assert.That(blockedDown.Handled, Is.True,
                "PenOnly must block the synthetic mouse down before native InkCanvas collection starts.");

            var blockedUp = CreateMouseUp(page.InkCanvas);
            InkCanvasMouseUpMethod.Invoke(page, new object[] { page, blockedUp });
            page.PenOnlyMode = false;

            var unrelatedShortStroke = CreateStroke(new Point(120, 160));
            page.InkCanvas.Strokes.Add(unrelatedShortStroke);
            InkCanvasStrokeCollectedMethod.Invoke(
                page,
                new object[] { page, new InkCanvasStrokeCollectedEventArgs(unrelatedShortStroke) });

            Assert.Multiple(() =>
            {
                Assert.That(page.GetStrokes(), Has.Count.EqualTo(1),
                    "A later short stroke must not inherit a pending popup-dismissal flag after a no-collection MouseUp.");
                Assert.That(GetUndoStack(editor), Has.Count.EqualTo(1),
                    "The later short stroke must create its ordinary undo action.");
                Assert.That(editor.IsDirty, Is.True,
                    "The later short stroke must retain its ordinary dirty-state mutation.");
            });
        }
        finally
        {
            popup.IsOpen = false;
            RemoveHarnessPage(editor, page);
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void InteractiveHiddenInkOutsidePopupDoesNotArmNativeInkDismissal()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();
        var (editor, page, popup) = CreateHarness("Pen", CustomInkInputProcessingMode.Inking);
        var annotation = new HiddenInkAnnotation
        {
            Id = "popup-hidden-ink",
            Size = 28,
            Points = new List<double[]>
            {
                new[] { 80d, 80d },
                new[] { 180d, 80d }
            }
        };

        try
        {
            page.AddHiddenInk(annotation);
            var hiddenInkCanvas = GetPrivateField<Canvas>(page, "HiddenInkCanvas");
            var hiddenVisual = hiddenInkCanvas.Children.OfType<Polyline>().Single();
            Assert.That(hiddenVisual.IsHitTestVisible, Is.True,
                "The inking page must expose the existing Hidden Ink path as an interactive overlay.");

            popup.IsOpen = true;
            var outsideDown = CreateMouseDown(hiddenVisual);
            EditorPagePreviewMouseDownMethod.Invoke(editor, new object[] { editor, outsideDown });

            var hiddenInkPress = CreateMouseDown(hiddenVisual);
            HiddenInkVisualMouseDownMethod.Invoke(
                page,
                new object[] { hiddenVisual, hiddenInkPress });

            Assert.That(hiddenInkPress.Handled, Is.True,
                "The interactive Hidden Ink path consumes its own pointer gesture instead of starting native ink.");

            var unrelatedShortStroke = CreateStroke(new Point(120, 160));
            page.InkCanvas.Strokes.Add(unrelatedShortStroke);
            InkCanvasStrokeCollectedMethod.Invoke(
                page,
                new object[] { page, new InkCanvasStrokeCollectedEventArgs(unrelatedShortStroke) });

            Assert.Multiple(() =>
            {
                Assert.That(page.GetStrokes(), Has.Count.EqualTo(1),
                    "A later short native stroke must not inherit popup-dismissal state from an interactive Hidden Ink click.");
                Assert.That(GetUndoStack(editor), Has.Count.EqualTo(1),
                    "The later native stroke must create its ordinary undo action.");
                Assert.That(editor.IsDirty, Is.True,
                    "The later native stroke must retain its ordinary dirty-state mutation.");
            });
        }
        finally
        {
            page.RemoveHiddenInkQuiet(annotation);
            popup.IsOpen = false;
            RemoveHarnessPage(editor, page);
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void TextComboBoxChoicesRemainInsideTransientToolbarSurface()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();
        var editor = new EditorPage();
        var fontCombo = GetPrivateField<ComboBox>(editor, "_textFontFamilyCombo");
        var alignmentCombo = GetPrivateField<ComboBox>(editor, "_textAlignmentCombo");
        var textPopup = GetPrivateField<Popup>(editor, "_textColorPopup");

        try
        {
            textPopup.IsOpen = true;
            foreach (var combo in new[] { fontCombo, alignmentCombo })
            {
                var item = new ComboBoxItem { Content = combo.Items[1] };
                combo.ItemsSource = null;
                combo.Items.Add(item);

                var arguments = new object?[] { item, false };
                var closes = (bool)ShouldClosePopupOnPointerDownMethod.Invoke(editor, arguments)!;
                Assert.That(closes, Is.False,
                    "Clicking a font/alignment choice must not close transient UI before SelectionChanged commits it.");
            }
        }
        finally
        {
            textPopup.IsOpen = false;
        }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void FontAndAlignmentSelectionsCommitFormatHistoryAndDirtyState()
    {
        EnsureWpfEnvironment();
        EnsureTestApplication();
        var editor = new EditorPage();
        var page = new PdfPageControl();
        var container = new Grid();
        var textBox = new TextBox
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            TextAlignment = TextAlignment.Left
        };
        container.Children.Add(textBox);
        page.AddTextContainerQuiet(container);
        GetPrivateField<IList>(editor, "_pageControls").Add(page);
        SetPrivateField(editor, "_selectedTextBox", textBox);

        var fontCombo = GetPrivateField<ComboBox>(editor, "_textFontFamilyCombo");
        var alignmentCombo = GetPrivateField<ComboBox>(editor, "_textAlignmentCombo");
        fontCombo.SelectedItem = "Arial";
        alignmentCombo.SelectedItem = alignmentCombo.Items.Cast<object>().Single(item =>
            item.GetType().GetProperty("Value")?.GetValue(item) is TextAlignment value
            && value == TextAlignment.Center);

        Assert.Multiple(() =>
        {
            Assert.That(textBox.FontFamily.Source, Is.EqualTo("Arial"));
            Assert.That(textBox.TextAlignment, Is.EqualTo(TextAlignment.Center));
            Assert.That(GetUndoStack(editor), Has.Count.EqualTo(2));
            Assert.That(editor.IsDirty, Is.True);
        });
    }

    private static (EditorPage Editor, PdfPageControl Page, Popup Popup) CreateHarness(
        string toolName,
        CustomInkInputProcessingMode inputMode)
    {
        var editor = new EditorPage();
        SetCurrentTool(editor, toolName);

        var page = new PdfPageControl { Width = 360, Height = 360 };
        page.SetInputMode(inputMode);
        page.InkMutated += (sender, args) => InvokePrivate(editor, "PageControl_InkMutated", sender, args);
        page.StrokeCollectedUndoable += (sender, stroke) =>
            InvokePrivate(editor, "PageControl_StrokeCollectedUndoable", sender, stroke);

        var pages = GetPrivateField<IList>(editor, "_pageControls");
        pages.Add(page);
        var pagesContainer = GetPrivateField<Panel>(editor, "PagesContainer");
        pagesContainer.Children.Add(page);

        string popupField = toolName switch
        {
            "Pen" => "_penPopup",
            "Highlighter" => "_highlighterPopup",
            _ => throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Unsupported popup test tool.")
        };
        return (editor, page, GetPrivateField<Popup>(editor, popupField));
    }

    private static void RemoveHarnessPage(EditorPage editor, PdfPageControl page)
    {
        GetPrivateField<IList>(editor, "_pageControls").Remove(page);
        var pagesContainer = GetPrivateField<Panel>(editor, "PagesContainer");
        pagesContainer.Children.Remove(page);
    }

    private static MouseButtonEventArgs CreateMouseDown(DependencyObject source)
    {
        var pointer = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseDownEvent
        };
        pointer.Source = source;
        return pointer;
    }

    private static MouseButtonEventArgs CreateMouseUp(DependencyObject source)
    {
        var pointer = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseUpEvent
        };
        pointer.Source = source;
        return pointer;
    }

    private static Stroke CreateStroke(params Point[] points)
    {
        var stylusPoints = new StylusPointCollection();
        foreach (var point in points)
            stylusPoints.Add(new StylusPoint(point.X, point.Y));
        return new Stroke(stylusPoints);
    }

    private static IList GetUndoStack(EditorPage editor)
    {
        return GetPrivateField<IList>(editor, "_undoStack");
    }

    private static void SetCurrentTool(EditorPage editor, string toolName)
    {
        var field = typeof(EditorPage).GetField("_currentTool", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("EditorPage._currentTool was not found.");
        field.SetValue(editor, Enum.Parse(field.FieldType, toolName));
    }

    private static T GetPrivateField<T>(object target, string name)
    {
        return (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
            ?? throw new AssertionException($"Private field '{name}' was not initialized."));
    }

    private static void SetPrivateField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Private field '{name}' was not found.");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        method.Invoke(target, args);
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
