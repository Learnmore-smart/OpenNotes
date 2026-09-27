using System;
using System.IO;
using System.Linq;

namespace Caelum.Tests;

/// <summary>
/// T14-A field-crash leg (v6.0.3 WER: stowed 0xC000027B in
/// Microsoft.UI.Xaml.dll, secondary combase E_INVALIDARG, raised mid-stroke)
/// plus the self-crossing-stroke hole artefact.
///
/// (1) WinUI <c>PathGeometry</c> defaults to <c>FillRule.EvenOdd</c>, so a
/// stroke outline that crosses itself punches a hole. The renderer must set
/// <c>FillRule.Nonzero</c> (WPF ink parity).
/// (2) A NaN/Infinity outline vertex throws <c>ArgumentException</c> inside
/// the XAML geometry setters on the pointer-move path — the suspected
/// E_INVALIDARG source. <c>AddOutlineFigure</c> must scan for non-finite
/// vertices and <c>UpdateStrokePath</c> must not let a rebuild throw.
/// (3) The app must journal unhandled exceptions to a file so the next
/// field crash ships a managed stack — and must never mark them handled.
/// </summary>
[TestFixture]
public sealed class WinUiInkRenderingSourceTests
{
    [Test]
    public void StrokeGeometryUsesNonzeroFillRuleSoCrossingsFillSolid()
    {
        string renderer = Read("Rendering", "StrokeRenderer.cs");

        Assert.Multiple(() =>
        {
            Assert.That(renderer, Does.Contain("FillRule = FillRule.Nonzero"),
                "PathGeometry defaults to EvenOdd — self-crossing outlines get holes without this");
            Assert.That(renderer, Does.Not.Contain("FillRule.EvenOdd"));
        });
    }

    [Test]
    public void OutlineFigureSkipsNonFiniteVerticesAndUpdateRebuildIsGuarded()
    {
        string renderer = Read("Rendering", "StrokeRenderer.cs");

        Assert.Multiple(() =>
        {
            // The E_INVALIDARG chokepoint: every outline vertex is checked
            // before it reaches PathFigure/PolyLineSegment.
            Assert.That(renderer, Does.Contain("double.IsFinite"));

            int addFigure = renderer.IndexOf(
                "private static void AddOutlineFigure", StringComparison.Ordinal);
            Assert.That(addFigure, Is.GreaterThanOrEqualTo(0));
            int finiteScan = renderer.IndexOf(
                "double.IsFinite", addFigure, StringComparison.Ordinal);
            int startPoint = renderer.IndexOf(
                "StartPoint = new Point", addFigure, StringComparison.Ordinal);
            Assert.That(finiteScan, Is.GreaterThanOrEqualTo(0)
                .And.LessThan(startPoint),
                "the finite scan must run before any vertex reaches the XAML figure");

            // The per-pointer-move rebuild is defensive — a throw mid-stroke
            // is a fatal stowed exception, so it logs and keeps last-good.
            int update = renderer.IndexOf(
                "public static void UpdateStrokePath", StringComparison.Ordinal);
            Assert.That(update, Is.GreaterThanOrEqualTo(0));
            int end = renderer.IndexOf("\n    ///", update, StringComparison.Ordinal);
            string body = end > update ? renderer[update..end] : renderer[update..];
            Assert.That(body, Does.Contain("try"));
            Assert.That(body, Does.Contain("catch (Exception"));
            Assert.That(body, Does.Contain("path.Data = BuildGeometry(stroke)"));

            // Faults are never silent — Debug output plus the crash journal.
            Assert.That(renderer, Does.Contain("Debug.WriteLine"));
            Assert.That(renderer, Does.Contain("CrashLogger.Log"));
        });
    }

    [Test]
    public void PointerToGeometryPathsDropNonFiniteInput()
    {
        string surface = Read("Controls", "InkSurface.cs");
        string page = Read("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            // Shared finite checks exist in both files (PointD + the raw
            // PointerPoint Position struct on the surface).
            Assert.That(surface, Does.Contain("IsFinite(PointD "));
            Assert.That(surface, Does.Contain("IsFinite(Point "));
            Assert.That(page, Does.Contain("IsFinite(PointD "));

            // Press-time ingest gate: a non-finite press never seeds a
            // gesture, and stroke packets are filtered before they can
            // poison the outline or the saved ink.
            Assert.That(surface, Does.Contain("!IsFinite(point.Position)"));
            Assert.That(surface, Does.Contain("!IsFinite(p.Position)"));
            Assert.That(surface, Does.Contain("IsFinite(current.Position)"));

            // Both cursor indicators bail before Canvas.SetLeft/SetTop.
            Assert.That(
                CountOccurrences(surface, "indicator == null || !IsFinite(pagePoint)"),
                Is.EqualTo(2),
                "eraser + brush indicators must both skip non-finite positions");

            // Page sinks: selection overlay press+move, shape-drag handlers,
            // preview segments, hidden-ink masks, and all three laser loops.
            Assert.That(
                CountOccurrences(page, "if (!IsFinite(pos))"),
                Is.EqualTo(2),
                "selection press + move must both gate on finite input");
            Assert.That(page, Does.Contain("!IsFinite(e.Anchor) || !IsFinite(e.Current)"));
            Assert.That(page, Does.Contain("!IsFinite(e.Current)"));
            Assert.That(
                CountOccurrences(page, "if (!IsFinite(p))"),
                Is.GreaterThanOrEqualTo(4),
                "shape preview + all three laser loops must skip non-finite points");
            Assert.That(page, Does.Contain("!double.IsFinite(pt[0])"),
                "hidden-ink mask vertices (loaded or live) are checked too");
        });
    }

    [Test]
    public void AppHooksCrashLoggingWithoutSwallowing()
    {
        string app = Read("App.xaml.cs");
        string logger = Read("Services", "CrashLogger.cs");

        Assert.Multiple(() =>
        {
            Assert.That(app, Does.Contain("UnhandledException +="));
            Assert.That(app, Does.Contain("AppDomain.CurrentDomain.UnhandledException"));
            Assert.That(app, Does.Contain("TaskScheduler.UnobservedTaskException"));
            Assert.That(app, Does.Contain("e.Handled = false"),
                "the XAML handler logs only — handled crashes never reach WER");
            Assert.That(app, Does.Not.Contain(".SetObserved()"),
                "unobserved task exceptions are journaled, never suppressed");

            // Entries land under the shared data root's logs directory.
            Assert.That(logger, Does.Contain("ProductInfo.GetDataDirectory()"));
            Assert.That(logger, Does.Contain("\"logs\""));
            Assert.That(logger, Does.Contain("crash-"));
            Assert.That(logger, Does.Contain("AggregateException"),
                "UnobservedTaskException arrives aggregated — flatten the inner chain");

            // The writer is fully defensive — nothing may throw out of a
            // crash handler.
            Assert.That(logger, Does.Contain("try"));
            Assert.That(logger, Does.Contain("catch"));
        });
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static string Read(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(
            new[] { Path.Combine(ProjectRoot(), "OpenNotes.WinUI") }.Concat(segments).ToArray()));
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.WinUI", "OpenNotes.WinUI.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not locate the solution root containing OpenNotes.WinUI.");
    }
}
