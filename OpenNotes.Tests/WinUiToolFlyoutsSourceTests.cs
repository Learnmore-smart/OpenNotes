using System;
using System.IO;
using System.Linq;

namespace Caelum.Tests;

/// <summary>
/// G2/G3/G4 parity contracts — the WinUI tool flyouts
/// (<c>ShowPenFlyout</c>/<c>ShowEraserFlyout</c>), the recent-colors row
/// shared by the pen/highlighter/text palettes, and the single-tool-flyout
/// mutual-exclusion sweep (WPF <c>CloseToolPopups</c>) are pinned as source
/// contracts; the list math lives in <see cref="RecentColorsTests"/> and
/// runtime verification in tools/winui-*.ps1 smokes.
/// </summary>
[TestFixture]
public sealed class WinUiToolFlyoutsSourceTests
{
    [Test]
    public void PenFlyoutPortsTheWpfPenPopupSurface()
    {
        string source = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Entry point + toolbar dispatch (WPF ToggleToolButton parity).
            Assert.That(source, Does.Contain("private void ShowPenFlyout(FrameworkElement anchor)"));
            Assert.That(source, Does.Contain("ShowPenFlyout(clicked);"));
            Assert.That(source, Does.Contain("ShowToolFlyout(flyout, ToolType.Pen, anchor);"));

            // Size slider: WPF range 0.5–8 at 0.25 steps, Editor.Pen.Size.
            Assert.That(source, Does.Contain("Minimum = 0.5"));
            Assert.That(source, Does.Contain("Maximum = 8,"));
            Assert.That(source, Does.Contain("StepFrequency = 0.25"));
            Assert.That(source, Does.Contain("\"Editor.Pen.Size\""));

            // Live preview line follows slider + palette picks.
            Assert.That(source, Does.Contain("_penFlyoutSizePreview"));
            Assert.That(source, Does.Contain("_penFlyoutSizePreview.StrokeThickness"));
            Assert.That(source, Does.Contain("_penFlyoutSizePreview.Stroke = new SolidColorBrush(color)"));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.PopupPreview\")"));

            // Colour pick updates the toolbar colour bar (WPF
            // UpdateToolIconColors parity) and pushes to all pages.
            Assert.That(source, Does.Contain("PenColorIndicator.Background = new SolidColorBrush(color)"));

            // Behaviour toggles persisted via SaveSetting (WPF
            // AddPenBehaviourToggles: EnablePressure/InkSimulation/
            // ShapeRecognition with their pinned AutomationIds).
            Assert.That(source, Does.Contain("\"Editor.Pen.Pressure\""));
            Assert.That(source, Does.Contain("\"Editor.Pen.InkSimulation\""));
            Assert.That(source, Does.Contain("\"Editor.Pen.ShapeRecognition\""));
            Assert.That(source, Does.Contain("SaveSetting(s => s.EnablePressure = v)"));
            Assert.That(source, Does.Contain("SaveSetting(s => s.InkSimulation = v)"));
            Assert.That(source, Does.Contain("SaveSetting(s => s.ShapeRecognition = v)"));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.Pressure\")"));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.InkSimulation\")"));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.ShapeRecognition\")"));

            // Smoothing segmented row → AppSettings.StrokeSmoothing
            // (WPF AddPenSmoothingSection, Editor.Pen.Smoothing.{i}).
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.SmoothingHeader\")"));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.SmoothingOff\")"));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.SmoothingLow\")"));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.SmoothingMid\")"));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.SmoothingHigh\")"));
            Assert.That(source, Does.Contain("$\"Editor.Pen.Smoothing.{i}\""));
            Assert.That(source, Does.Contain("SaveSetting(settings => settings.StrokeSmoothing = level)"));

            // Scroll-cap wrapper (WPF EnableToolPopupScrolling parity) and
            // every slider change applies to all pages while armed.
            Assert.That(source, Does.Contain("WrapToolFlyoutContent(panel)"));
        });
    }

    [Test]
    public void HighlighterFlyoutPortsTheWpfSizePreviewBandAndDividers()
    {
        string source = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Entry point + toolbar dispatch (WPF _highlighterPopup parity).
            Assert.That(source, Does.Contain("private void ShowHighlighterFlyout(FrameworkElement anchor)"));
            Assert.That(source, Does.Contain("ShowHighlighterFlyout(clicked);"));
            Assert.That(source, Does.Contain("ShowToolFlyout(flyout, ToolType.Highlighter, anchor);"));

            // Size-preview band (WPF AddSizePreviewSection, isHighlighter
            // branch): a horizontal stroke at the live size inside the
            // alt-surface well, painted at the freehand alpha and following
            // the size slider and palette/recents picks live.
            Assert.That(source, Does.Contain("_highlighterFlyoutSizePreview"));
            Assert.That(source, Does.Contain("_highlighterFlyoutSizePreview.StrokeThickness = args.NewValue"));
            Assert.That(source, Does.Contain("_highlighterFlyoutSizePreview.Stroke = new SolidColorBrush("));
            Assert.That(source, Does.Contain("GetHighlighterPreviewStrokeColor(HighlighterApplyMode.Freehand,"));
            Assert.That(source, Does.Contain("StrokeThickness = _highlighterSize"));
            Assert.That(source, Does.Contain("previewBorder.Clip = new RectangleGeometry"));

            // Section rule lines (WPF ThemeDivider parity) ride the shared
            // section-header helper so every non-first section is separated;
            // the headerless pen behaviour grid inserts one explicitly.
            Assert.That(source, Does.Contain("private static Border PopupSectionDivider("));
            Assert.That(source, Does.Contain("PopupSectionDivider(topMargin)"));
            Assert.That(source, Does.Contain("panel.Children.Add(PopupSectionDivider())"));
            Assert.That(source, Does.Contain("private static FrameworkElement PopupSectionHeader("));
        });
    }

    [Test]
    public void EraserFlyoutPortsTheWpfEraserPopupSurface()
    {
        string source = Read("Pages", "EditorPage.xaml.cs");
        string xaml = Read("Pages", "EditorPage.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private void ShowEraserFlyout(FrameworkElement anchor)"));
            Assert.That(source, Does.Contain("ShowEraserFlyout(clicked);"));
            Assert.That(source, Does.Contain("ShowToolFlyout(flyout, ToolType.Eraser, anchor);"));

            // Pixel / whole-stroke mode row persisted to
            // AppSettings.WholeStrokeEraser (WPF AddEraserModeSection ids).
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.EraserModeHeader\")"));
            Assert.That(source, Does.Contain("\"Editor.Eraser.Pixel\""));
            Assert.That(source, Does.Contain("\"Editor.Eraser.WholeStroke\""));
            Assert.That(source, Does.Contain("settings.WholeStrokeEraser = whole"));
            Assert.That(source, Does.Contain("_applicationSettings = AppSettingsService.Save(settings)"));

            // Size slider: WPF range 4–80 at 1.0 steps, Editor.Eraser.Size,
            // plus the centred preview ellipse flash (WPF
            // ShowEraserSizePreview — the overlay element already exists).
            Assert.That(source, Does.Contain("Minimum = 4"));
            Assert.That(source, Does.Contain("Maximum = 80"));
            Assert.That(source, Does.Contain("\"Editor.Eraser.Size\""));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.PopupEraserSize\")"));
            Assert.That(source, Does.Contain("private void ShowEraserSizePreview(double size)"));
            Assert.That(source, Does.Contain("EraserSizePreviewEllipse.Width = size"));
            Assert.That(source, Does.Contain("Task.Delay(1200)"));
            Assert.That(xaml, Does.Contain("x:Name=\"EraserSizePreviewEllipse\""));
        });
    }

    [Test]
    public void RecentColorsRowCoversPenHighlighterAndTextPalettes()
    {
        string source = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Shared builder + refresh entry (WPF recentSection/
            // RefreshRecentColorsRow parity) and the pinned swatch ids.
            Assert.That(source, Does.Contain("BuildRecentColorsSection(out var recentRow)"));
            Assert.That(source, Does.Contain("private void RefreshRecentColorsRow("));
            Assert.That(source, Does.Contain("$\"Editor.Color.Recent.{recentIndex}\""));
            Assert.That(source, Does.Contain("RecentColors.MaxRecentColors"));
            Assert.That(source, Does.Contain("LocalizationService.Get(\"Editor.Recent\")"));

            // All three persisted lists wired (WPF: pen + highlighter popups
            // + the text-colour popup; the shape flyout intentionally has no
            // recents — WPF keeps shape colours session-only).
            Assert.That(source, Does.Contain("RecordRecentColor(s.RecentPenColors, color)"));
            Assert.That(source, Does.Contain("RecordRecentColor(s.RecentHighlighterColors, color)"));
            Assert.That(source, Does.Contain("RecordRecentColor(s.RecentTextColors, picked)"));
            Assert.That(source, Does.Contain("AppSettingsService.Load().RecentPenColors"));
            Assert.That(source, Does.Contain("AppSettingsService.Load().RecentHighlighterColors"));
            Assert.That(source, Does.Contain("AppSettingsService.Load().RecentTextColors"));

            // The cached text-colour flyout repopulates the row on every
            // open (WPF popup.Opened parity).
            Assert.That(source, Does.Contain("_textColorFlyout.Opening +="));

            // SaveSetting/RecordRecentColor wrappers keep the WPF shapes.
            Assert.That(source, Does.Contain("private void SaveSetting(Action<AppSettings> mutate)"));
            Assert.That(source, Does.Contain("private void RecordRecentColor(List<string> list, Windows.UI.Color color)"));
            Assert.That(source, Does.Contain("private static bool TryParseRecentColor(string hex, out Windows.UI.Color color)"));
        });
    }

    [Test]
    public void ToolFlyoutsKeepSingleSurfaceAndTransientSweepParity()
    {
        string source = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // WPF CloseToolPopups parity: one tool flyout at a time; the
            // highlighter bucket keeps its flyout across apply-mode hops.
            Assert.That(source, Does.Contain("private void CloseToolFlyouts(ToolType keepOpen = ToolType.None)"));
            Assert.That(source, Does.Contain("private static ToolType ToolFlyoutOwner(ToolType tool)"));
            Assert.That(source, Does.Contain("IsHighlighterTool(tool) ? ToolType.Highlighter : tool"));
            Assert.That(source, Does.Contain("CloseToolFlyouts(next)"));
            Assert.That(source, Does.Contain("CloseToolFlyouts(tool)"));

            // Every tool flyout registers with the transient sweep so
            // Escape/tab-deactivate/release dismisses it (WPF
            // _transientUiRegistry parity).
            Assert.That(source, Does.Contain("_transientFlyout = flyout;"));
            Assert.That(source, Does.Contain("_toolFlyout.Hide()"));

            // All five tool flyouts route through the shared show helper.
            foreach (var owner in new[] { "Pen", "Highlighter", "Eraser", "Shape", "Select" })
            {
                Assert.That(source,
                    Does.Contain($"ShowToolFlyout(flyout, ToolType.{owner}, anchor);"),
                    $"tool flyout owner {owner} missing");
            }
        });
    }

    [Test]
    public void CoreRecentColorsCarriesTheWpfListContract()
    {
        var core = File.ReadAllText(Path.Combine(
            ProjectRoot(), "OpenNotes.Core", "Services", "RecentColors.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(core, Does.Contain("public const int MaxRecentColors = 8"));
            Assert.That(core, Does.Contain("public static void Record(List<string> list, string hex)"));
            Assert.That(core, Does.Contain("public static bool TryParse(string hex, out byte a, out byte r, out byte g, out byte b)"));
            // Dedupe is case-insensitive and the cap trims the tail.
            Assert.That(core, Does.Contain("StringComparison.OrdinalIgnoreCase"));
            Assert.That(core, Does.Contain("list.Insert(0, hex)"));
            Assert.That(core, Does.Contain("list.RemoveRange(MaxRecentColors"));
        });
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
