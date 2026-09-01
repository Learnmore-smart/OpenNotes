using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Pages;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ShapeToolTests
{
    [OneTimeSetUp]
    public void SetUpWpf()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows");

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

    private static readonly MethodInfo BuildShapeOutlineMethod =
        typeof(PdfPageControl).GetMethod("BuildShapeOutline", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.BuildShapeOutline was not found.");

    private static readonly MethodInfo CommitShapeMethod =
        typeof(PdfPageControl).GetMethod("CommitShape", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl.CommitShape was not found.");

    private static readonly FieldInfo ShapeAnchorField =
        typeof(PdfPageControl).GetField("_shapeAnchor", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl._shapeAnchor was not found.");

    private static readonly FieldInfo ShapeCurrentField =
        typeof(PdfPageControl).GetField("_shapeCurrent", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("PdfPageControl._shapeCurrent was not found.");

    [TestCase("Triangle", 4)]
    [TestCase("Diamond", 5)]
    [TestCase("Parallelogram", 5)]
    [TestCase("Pentagon", 6)]
    [TestCase("Hexagon", 7)]
    public void AdditionalPolygonShapesProduceClosedBoundedOutlines(string kindName, int expectedPointCount)
    {
        Assert.That(Enum.TryParse(kindName, out ShapeKind kind), Is.True,
            $"ShapeKind.{kindName} must be available to the production picker.");

        var start = new Point(10, 20);
        var end = new Point(110, 100);
        var points = InvokeBuildShapeOutline(kind, start, end);

        Assert.Multiple(() =>
        {
            Assert.That(points, Has.Count.EqualTo(expectedPointCount));
            Assert.That(points[0], Is.EqualTo(points[^1]), $"{kindName} must be closed.");
            Assert.That(points.All(point => point.X >= start.X && point.X <= end.X), Is.True,
                $"{kindName} must stay inside the horizontal drag bounds.");
            Assert.That(points.All(point => point.Y >= start.Y && point.Y <= end.Y), Is.True,
                $"{kindName} must stay inside the vertical drag bounds.");
            Assert.That(points.Distinct().Count(), Is.EqualTo(expectedPointCount - 1));
        });
    }

    [Test]
    public void TriangleAndParallelogramUseRecognizableGeometry()
    {
        Assert.That(Enum.TryParse("Triangle", out ShapeKind triangle), Is.True);
        Assert.That(Enum.TryParse("Parallelogram", out ShapeKind parallelogram), Is.True);

        var trianglePoints = InvokeBuildShapeOutline(triangle, new Point(0, 0), new Point(100, 80));
        var parallelogramPoints = InvokeBuildShapeOutline(parallelogram, new Point(0, 0), new Point(100, 80));

        Assert.Multiple(() =>
        {
            Assert.That(trianglePoints[0], Is.EqualTo(new Point(50, 0)), "Triangle starts at its top apex.");
            Assert.That(trianglePoints[1], Is.EqualTo(new Point(100, 80)));
            Assert.That(trianglePoints[2], Is.EqualTo(new Point(0, 80)));
            Assert.That(parallelogramPoints[0].X, Is.GreaterThan(0), "Top-left corner is slanted inward.");
            Assert.That(parallelogramPoints[1], Is.EqualTo(new Point(100, 0)));
            Assert.That(parallelogramPoints[2].X, Is.LessThan(100), "Bottom-right corner is slanted inward.");
            Assert.That(parallelogramPoints[3], Is.EqualTo(new Point(0, 80)));
        });
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void DashedRectangleCommitsOneGroupedHistoryBatch()
    {
        var page = new PdfPageControl
        {
            CurrentShape = ShapeKind.Rectangle,
            ShapeStrokeSize = 2
        };
        var dashedProperty = typeof(PdfPageControl).GetProperty("ShapeIsDashed");
        var committedEvent = typeof(PdfPageControl).GetEvent("ShapeCommittedUndoable");
        Assert.That(dashedProperty, Is.Not.Null);
        Assert.That(committedEvent, Is.Not.Null);
        if (dashedProperty == null || committedEvent == null)
            return;
        dashedProperty.SetValue(page, true);
        int individualEvents = 0;
        IReadOnlyList<Stroke>? committedBatch = null;
        page.StrokeCollectedUndoable += (_, _) => individualEvents++;
        EventHandler<IReadOnlyList<Stroke>> handler = (_, strokes) => committedBatch = strokes;
        committedEvent.AddEventHandler(page, handler);

        Commit(page, new Point(10, 20), new Point(130, 100));

        Assert.That(committedBatch, Is.Not.Null);
        var batch = committedBatch!;
        Assert.Multiple(() =>
        {
            Assert.That(individualEvents, Is.Zero, "Shape parts must not create per-stroke Undo entries.");
            Assert.That(batch, Has.Count.GreaterThan(4), "A dashed rectangle should contain real separated dash strokes.");
            Assert.That(page.GetStrokes(), Has.Count.EqualTo(batch.Count));
            Assert.That(batch.Select(stroke => ShapeStrokeMetadata.Read(stroke).GroupId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(batch.All(stroke => ShapeStrokeMetadata.Read(stroke).Kind == nameof(ShapeKind.Rectangle)), Is.True);
            Assert.That(batch.All(stroke => ShapeStrokeMetadata.Read(stroke).IsDashed), Is.True);
        });
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void SolidArrowCommitsShaftAndHeadAsOneHistoryBatch()
    {
        var editor = new EditorPage();
        var page = new PdfPageControl
        {
            CurrentShape = ShapeKind.Arrow,
            ShapeStrokeSize = 2
        };
        var dashedProperty = typeof(PdfPageControl).GetProperty("ShapeIsDashed");
        var committedEvent = typeof(PdfPageControl).GetEvent("ShapeCommittedUndoable");
        Assert.That(dashedProperty, Is.Not.Null);
        Assert.That(committedEvent, Is.Not.Null);
        if (dashedProperty == null || committedEvent == null)
            return;
        dashedProperty.SetValue(page, false);
        int individualEvents = 0;
        IReadOnlyList<Stroke>? committedBatch = null;
        page.StrokeCollectedUndoable += (_, _) => individualEvents++;
        EventHandler<IReadOnlyList<Stroke>> handler = (sender, strokes) =>
        {
            committedBatch = strokes;
            InvokeEditorPrivate(editor, "PageControl_ShapeCommittedUndoable", sender!, strokes);
        };
        committedEvent.AddEventHandler(page, handler);

        Commit(page, new Point(10, 20), new Point(130, 80));

        var batch = committedBatch!;
        Assert.Multiple(() =>
        {
            Assert.That(individualEvents, Is.Zero);
            Assert.That(batch, Has.Count.EqualTo(2));
            Assert.That(batch.Select(stroke => ShapeStrokeMetadata.Read(stroke).GroupId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(batch.All(stroke => !ShapeStrokeMetadata.Read(stroke).IsDashed), Is.True);
        });

        var undoStack = (IList)(typeof(EditorPage)
            .GetField("_undoStack", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(editor)
            ?? throw new AssertionException("EditorPage did not initialize its undo stack."));
        Assert.That(undoStack.Count, Is.EqualTo(1), "one arrow gesture must create one history entry");

        InvokeTask(undoStack[0]!, "UndoAsync");
        Assert.That(page.GetStrokes(), Is.Empty, "Undo must remove the shaft and pointer together.");

        InvokeTask(undoStack[0]!, "RedoAsync");
        Assert.That(page.GetStrokes(), Has.Count.EqualTo(2), "Redo must restore the whole arrow together.");
    }

    [Test]
    public void DashedRenderingIsAStyleControlInsteadOfAGeometryChoice()
    {
        string root = FindProjectRoot();
        string editorCode = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        int choicesStart = editorCode.IndexOf("var choices = new (ShapeKind Kind", StringComparison.Ordinal);
        int choicesEnd = editorCode.IndexOf("foreach (var choice in choices)", choicesStart, StringComparison.Ordinal);
        string choices = choicesStart >= 0 && choicesEnd > choicesStart
            ? editorCode.Substring(choicesStart, choicesEnd - choicesStart)
            : string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(choices, Does.Not.Contain("ShapeKind.DashedLine"));
            Assert.That(editorCode, Does.Contain("Editor.Shape.Style.Solid"));
            Assert.That(editorCode, Does.Contain("Editor.Shape.Style.Dashed"));
            Assert.That(editorCode, Does.Contain("page.ShapeIsDashed = _shapeIsDashed"));
        });
    }

    private static void Commit(PdfPageControl page, Point start, Point end)
    {
        ShapeAnchorField.SetValue(page, start);
        ShapeCurrentField.SetValue(page, end);
        CommitShapeMethod.Invoke(page, null);
    }

    private static void InvokeEditorPrivate(EditorPage editor, string methodName, params object[] args)
    {
        var method = typeof(EditorPage).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(EditorPage).FullName, methodName);
        method.Invoke(editor, args);
    }

    private static void InvokeTask(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        ((Task)(method.Invoke(instance, null)
            ?? throw new AssertionException($"{methodName} did not return a Task."))).GetAwaiter().GetResult();
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
    }

    private static IReadOnlyList<Point> InvokeBuildShapeOutline(ShapeKind kind, Point start, Point end) =>
        (IReadOnlyList<Point>)(BuildShapeOutlineMethod.Invoke(null, new object[] { kind, start, end })
            ?? throw new InvalidOperationException("BuildShapeOutline returned null."));
}
