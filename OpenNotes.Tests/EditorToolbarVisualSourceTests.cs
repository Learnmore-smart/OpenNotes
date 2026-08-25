using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
public sealed class EditorToolbarVisualSourceTests
{
    [Test]
    public void VisibleApplicationChromeUsesOnlyNamedLucideIcons()
    {
        var root = FindProjectRoot();
        var relativeFiles = new[]
        {
            "MainWindow.xaml", "MainWindow.xaml.cs", "MainWindow.Utilities.cs",
            "SettingsWindow.xaml", "PageTemplatePickerWindow.xaml",
            Path.Combine("Pages", "HomePage.xaml"), Path.Combine("Pages", "HomePage.xaml.cs"),
            Path.Combine("Pages", "HomePage.Utilities.cs"),
            Path.Combine("Pages", "EditorPage.xaml"), Path.Combine("Pages", "EditorPage.xaml.cs"),
            Path.Combine("Pages", "EditorPage.Utilities.cs")
        };
        string production = string.Join("\n", relativeFiles.Select(file =>
            File.ReadAllText(Path.Combine(root, file))));
        string editorXaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(production, Does.Not.Contain("Segoe MDL2 Assets"));
            Assert.That(production, Does.Not.Match(@"&#xE[0-9A-Fa-f]{3};"));
            Assert.That(production, Does.Contain("LucideIcon"));
            Assert.That(production, Does.Not.Contain("★"));
            Assert.That(production, Does.Not.Contain("☆"));
            Assert.That(editorXaml, Does.Not.Contain("Content=\"×\""));
            Assert.That(editorXaml, Does.Contain("x:Name=\"PenOnlyButton\""));
            Assert.That(editorXaml, Does.Contain("x:Name=\"PenOnlyIcon\" Kind=\"PenLine\""));
        });
    }

    [Test]
    public void ToolbarDoesNotExposeObsoleteCommandsOrPresetSlots()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var utilities = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.Utilities.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Not.Contain("PresetSlotsPanel"));
            Assert.That(source, Does.Not.Contain("PresetSlotsPanel"));
            Assert.That(source, Does.Not.Contain("InitializePenPresetSlots();"));
            Assert.That(source, Does.Not.Contain("settings.PenPresets = BuildDefaultPenPresets"));
            Assert.That(xaml, Does.Not.Contain("FitWidthButton"));
            Assert.That(xaml, Does.Not.Contain("FitPageButton"));
            Assert.That(source, Does.Not.Contain("FitWidthButton_Click"));
            Assert.That(source, Does.Not.Contain("FitPageButton_Click"));
            Assert.That(utilities, Does.Not.Contain("FitWidthButton"));
            Assert.That(utilities, Does.Not.Contain("FitPageButton"));
            Assert.That(source, Does.Not.Contain("InkAnalysisTooltip"));
            Assert.That(source, Does.Not.Contain("InkAnalysisUnavailable"));
        });
    }

    [Test]
    public void LaserAndHighlighterUseLiveVectorVisuals()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        int highlighterStart = xaml.IndexOf("x:Name=\"HighlighterIcon\"", StringComparison.Ordinal);
        int highlighterEnd = xaml.IndexOf("</ToggleButton>", highlighterStart, StringComparison.Ordinal);
        string highlighterBlock = highlighterStart >= 0 && highlighterEnd > highlighterStart
            ? xaml.Substring(highlighterStart, highlighterEnd - highlighterStart)
            : string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Name=\"LaserIcon\""));
            Assert.That(xaml, Does.Contain("<controls:LucideIcon x:Name=\"LaserIcon\""));
            Assert.That(xaml, Does.Not.Contain("Text=\"&#xE790;\""));
            Assert.That(xaml, Does.Contain("x:Name=\"HighlighterIcon\""));
            Assert.That(xaml, Does.Contain("<controls:LucideIcon x:Name=\"HighlighterIcon\""));
            Assert.That(xaml, Does.Not.Contain("Text=\"&#xE7E6;\""));
            Assert.That(highlighterBlock, Does.Not.Contain("ThemeMarkBrush"));
            Assert.That(source, Does.Contain("HighlighterColorIndicator.Background"));
            Assert.That(source, Does.Not.Contain("HighlighterIcon.Fill"));
            Assert.That(source, Does.Contain("_highlighterColor"));
            Assert.That(source, Does.Contain("_highlighterSize = v"));
            Assert.That(source, Does.Contain("_highlighterPopupSizePreview.StrokeThickness"));
            Assert.That(source, Does.Contain("UpdateHighlighterModePreviewVisuals();"));
            Assert.That(source, Does.Contain("Color.FromArgb"));
            Assert.That(source, Does.Contain("BuildHighlighterModePreview"));
        });
    }

    [Test]
    public void ShapeAndHighlighterChoicesExposeVectorPreviewsWithoutCheckmarkOverlay()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        int shapeSectionStart = source.IndexOf("private void AddShapeSubTypeSection", StringComparison.Ordinal);
        int shapeSectionEnd = source.IndexOf("private void AddEraserModeSection", shapeSectionStart, StringComparison.Ordinal);
        int vectorButtonStart = source.IndexOf("private static ToggleButton BuildVectorModeToggleButton", StringComparison.Ordinal);
        int vectorButtonEnd = source.IndexOf("private static void StyleVectorModeToggleButton", vectorButtonStart, StringComparison.Ordinal);
        Assert.That(shapeSectionStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(shapeSectionEnd, Is.GreaterThan(shapeSectionStart));
        Assert.That(vectorButtonStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(vectorButtonEnd, Is.GreaterThan(vectorButtonStart));
        string shapeSection = source.Substring(shapeSectionStart, shapeSectionEnd - shapeSectionStart);
        string vectorButton = source.Substring(vectorButtonStart, vectorButtonEnd - vectorButtonStart);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("BuildShapePreview"));
            Assert.That(source, Does.Contain("BuildHighlighterModePreview"));
            Assert.That(source, Does.Contain("Editor.Shape.Line"));
            Assert.That(source, Does.Contain("Editor.Shape.Rectangle"));
            Assert.That(source, Does.Contain("Editor.Shape.Ellipse"));
            Assert.That(source, Does.Contain("Editor.Shape.Arrow"));
            Assert.That(source, Does.Contain("Editor.Shape.Triangle"));
            Assert.That(source, Does.Contain("Editor.Shape.Diamond"));
            Assert.That(source, Does.Contain("Editor.Shape.Parallelogram"));
            Assert.That(source, Does.Contain("Editor.Shape.Pentagon"));
            Assert.That(source, Does.Contain("Editor.Shape.Hexagon"));
            Assert.That(shapeSection, Does.Contain("new UniformGrid { Columns = 3 }"));
            Assert.That(vectorButton, Does.Not.Contain("CheckMark"));
            Assert.That(vectorButton, Does.Not.Contain("M2,5 L4.5,8 L9,2"));
            Assert.That(source, Does.Contain("IsChecked"));
            Assert.That(source, Does.Contain("AutomationProperties.SetAutomationId"));
            Assert.That(source, Does.Contain("ToolTipService.SetToolTip"));
        });
    }

    [Test]
    public void ToolbarPolishUsesLocalizedTooltipsFixedSidebarAndCenteredPageJump()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var utilities = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.Utilities.cs"));

        int sidebarStart = xaml.IndexOf("<Border x:Name=\"DocumentSidebar\"", StringComparison.Ordinal);
        int sidebarEnd = xaml.IndexOf(">", sidebarStart, StringComparison.Ordinal);
        int toolbarStart = xaml.IndexOf("<Border x:Name=\"ToolbarBorder\"", StringComparison.Ordinal);
        int toolbarEnd = xaml.IndexOf("</Border>", xaml.IndexOf("</ScrollViewer>", toolbarStart, StringComparison.Ordinal), StringComparison.Ordinal);
        int iconColorStart = source.IndexOf("private void UpdateToolIconColors", StringComparison.Ordinal);
        int iconColorEnd = source.IndexOf("// Task 15: pen-only drawing", iconColorStart, StringComparison.Ordinal);
        Assert.That(sidebarStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(sidebarEnd, Is.GreaterThan(sidebarStart));
        Assert.That(toolbarStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(toolbarEnd, Is.GreaterThan(toolbarStart));
        Assert.That(iconColorStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(iconColorEnd, Is.GreaterThan(iconColorStart));

        string sidebarDeclaration = xaml.Substring(sidebarStart, sidebarEnd - sidebarStart);
        string toolbar = xaml.Substring(toolbarStart, toolbarEnd - toolbarStart);
        string iconColorMethod = source.Substring(iconColorStart, iconColorEnd - iconColorStart);

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Key=\"EditorToolTipStyle\""));
            Assert.That(xaml, Does.Contain("TargetType=\"{x:Type ToolTip}\""));
            Assert.That(xaml, Does.Contain("ToolTipService.InitialShowDelay"));
            Assert.That(utilities, Does.Contain("LocalizationService.Get(\"Editor.PenTooltip\")"));
            Assert.That(utilities, Does.Contain("ToolTipService.SetToolTip(control, label)"));
            Assert.That(xaml, Does.Not.Contain("SidebarResizeThumbStyle"));
            Assert.That(xaml, Does.Not.Contain("Editor.Sidebar.Resize"));
            Assert.That(utilities, Does.Not.Contain("Editor.SidebarResize"));
            Assert.That(sidebarDeclaration, Does.Contain("Width=\"184\""));
            Assert.That(sidebarDeclaration, Does.Not.Contain("MinWidth="));
            Assert.That(sidebarDeclaration, Does.Not.Contain("MaxWidth="));
            Assert.That(sidebarDeclaration, Does.Contain("BorderThickness=\"1,1,0,1\""));
            Assert.That(toolbar, Does.Contain("x:Name=\"ToolbarOverlayGrid\""));
            Assert.That(toolbar, Does.Contain("x:Name=\"PageJumpReservedSpace\""));
            Assert.That(toolbar, Does.Contain("x:Name=\"CenteredPageJumpHost\""));
            Assert.That(toolbar, Does.Contain("HorizontalAlignment=\"Center\""));
            Assert.That(toolbar, Does.Not.Contain("PenIconContrast"));
            Assert.That(toolbar, Does.Not.Contain("PenIconBackplate"));
            Assert.That(toolbar, Does.Not.Contain("Width=\"19\" Height=\"19\""));
            Assert.That(toolbar, Does.Contain("x:Name=\"PenColorIndicator\""));
            Assert.That(toolbar, Does.Contain("x:Name=\"HighlighterColorIndicator\""));
            Assert.That(toolbar, Does.Not.Contain("Stroke=\"{DynamicResource ThemeMarkBrush}\""));
            Assert.That(toolbar, Does.Not.Contain("Stroke=\"{DynamicResource ThemeMarginBrush}\""));
            Assert.That(iconColorMethod, Does.Contain("PenColorIndicator"));
            Assert.That(iconColorMethod, Does.Contain("HighlighterColorIndicator"));
            Assert.That(iconColorMethod, Does.Not.Contain("PenIcon.Stroke"));
            Assert.That(iconColorMethod, Does.Not.Contain("HighlighterIcon.Fill"));
        });
    }

    [Test]
    public void ToolbarControlsHaveStableAutomationIdsAndLocalizedTooltipPath()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var utilities = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.Utilities.cs"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var requiredIds = new[]
        {
            "Editor.UndoButton", "Editor.RedoButton", "Editor.PenToolButton",
            "Editor.HighlighterToolButton", "Editor.StickyNoteToolButton",
            "Editor.EraserToolButton", "Editor.ShapeToolButton",
            "Editor.LaserToolButton", "Editor.RulerToolButton",
            "Editor.SelectToolButton", "Editor.TextToolButton", "Editor.SavePdfButton",
            "Editor.VersionHistoryButton", "Editor.PageJump", "Editor.PenOnlyButton",
            "Editor.ZoomOutButton", "Editor.ZoomInButton", "Editor.RotatePageButton"
        };

        foreach (var id in requiredIds)
            Assert.That(xaml, Does.Contain($"AutomationProperties.AutomationId=\"{id}\""), id);

        var dynamicIds = new[]
        {
            "Editor.Select.Shape.Rectangle", "Editor.Select.Shape.FreeForm",
            "Editor.Select.Filter.Both", "Editor.Select.Filter.Drawings", "Editor.Select.Filter.Text",
            "Editor.Text.Delete", "Editor.Text.Smaller", "Editor.Text.Bigger", "Editor.Text.Color",
            "Editor.Text.Bold", "Editor.Text.Italic", "Editor.Text.FontFamily", "Editor.Text.Alignment"
        };
        foreach (var id in dynamicIds)
            Assert.That(utilities + Environment.NewLine + source, Does.Contain(id), id);

        Assert.Multiple(() =>
        {
            Assert.That(utilities, Does.Contain("ApplyToolbarAccessibilityMetadata"));
            Assert.That(utilities, Does.Contain("ToolTipService.SetToolTip"));
            Assert.That(utilities, Does.Contain("AutomationProperties.SetName"));
            Assert.That(utilities, Does.Contain("AutomationProperties.SetHelpText"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void HighlighterPreviewProductionPathUsesLiveSizeAndModeOpacity()
    {
        // WPF's Path dependency properties require an initialized application
        // (and the font cache reads WINDIR during FrameworkElement startup).
        // Keep this production-callback test self-contained when it is run as
        // a focused fixture instead of after another WPF test fixture.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows");
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var helper = typeof(Caelum.Pages.EditorPage).GetMethod(
            "ApplyHighlighterPreviewVisual",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(helper, Is.Not.Null, "The production preview refresh helper must exist.");

        var modeType = typeof(Caelum.Pages.EditorPage).GetNestedType(
            "HighlighterApplyMode",
            BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(modeType, Is.Not.Null);

        var color = Color.FromRgb(12, 100, 210);
        var source = File.ReadAllText(Path.Combine(FindProjectRoot(), "Pages", "EditorPage.xaml.cs"));
        var refreshStart = source.IndexOf("private void UpdateHighlighterModePreviewVisuals", StringComparison.Ordinal);
        var refreshEnd = source.IndexOf("private void ApplyHighlighterPreviewColor", refreshStart, StringComparison.Ordinal);
        Assert.That(refreshStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(refreshEnd, Is.GreaterThan(refreshStart));
        var refreshSource = source.Substring(refreshStart, refreshEnd - refreshStart);
        Assert.That(refreshSource, Does.Contain("foreach (var pair in _highlighterModePreviews)"));
        Assert.That(refreshSource, Does.Contain("ApplyHighlighterPreviewColor(pair.Key, pair.Value)"));

        var expected = new Dictionary<string, (byte StrokeAlpha, byte FillAlpha)>
        {
            ["Freehand"] = (140, 0),
            ["TextHighlight"] = (120, 0),
            ["Underline"] = (255, 0),
            ["StrikeOut"] = (255, 0),
            ["Squiggly"] = (255, 0),
            ["AreaHighlight"] = (220, 76)
        };

        foreach (var entry in expected)
        {
            var mode = Enum.Parse(modeType!, entry.Key);
            var preview = new ShapePath();
            helper!.Invoke(null, new object[] { mode, preview, color, 13.5d });

            Assert.That(preview.StrokeThickness, Is.EqualTo(13.5d), entry.Key);
            Assert.That((preview.Stroke as SolidColorBrush)?.Color.A, Is.EqualTo(entry.Value.StrokeAlpha), entry.Key);
            var fill = (preview.Fill as SolidColorBrush)?.Color.A ?? (byte)0;
            Assert.That(fill, Is.EqualTo(entry.Value.FillAlpha), entry.Key);
        }
    }

    [Test]
    public void DynamicPopupControlsAreRealKeyboardAccessiblePeers()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var settingsStart = source.IndexOf("private static ToggleButton BuildSettingToggleRow", StringComparison.Ordinal);
        var settingsEnd = source.IndexOf("private void SaveSetting", settingsStart, StringComparison.Ordinal);
        Assert.That(settingsStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(settingsEnd, Is.GreaterThan(settingsStart));
        var settingsBlock = source.Substring(settingsStart, settingsEnd - settingsStart);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private static ToggleButton BuildModeToggleButton"));
            Assert.That(source, Does.Contain("private static ToggleButton BuildSettingToggleRow"));
            Assert.That(source, Does.Contain("new Button"));
            Assert.That(source, Does.Contain("RefreshRecentColorsRow"));
            Assert.That(source, Does.Contain("button.Click"));
            Assert.That(source, Does.Contain("KeyboardNavigation.SetIsTabStop"));
            Assert.That(source, Does.Contain("ToolbarToggleButtonStyle"));
            Assert.That(source, Does.Contain("AutomationProperties.SetHelpText"));
            Assert.That(source, Does.Contain("AutomationProperties.SetName"));
            Assert.That(source, Does.Contain("AutomationProperties.SetAutomationId"));
            Assert.That(source, Does.Contain("Editor.Pen.Size"));
            Assert.That(source, Does.Contain("Editor.Highlighter.Size"));
            Assert.That(source, Does.Contain("Editor.Eraser.Size"));
            Assert.That(source, Does.Contain("Editor.Shape.Size"));
            Assert.That(source, Does.Contain("Height = 32"));
            Assert.That(source, Does.Contain("KeyDown"));
            Assert.That(settingsBlock, Does.Contain("MinWidth = 32"));
            Assert.That(settingsBlock, Does.Contain("MinHeight = 32"));
        });
    }

    [Test]
    public void VectorModesExposeNonColorCheckedCueAndFocusContrast()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var app = File.ReadAllText(Path.Combine(root, "App.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ActiveBar"));
            Assert.That(source, Does.Not.Contain("CheckMark"));
            Assert.That(source, Does.Not.Contain("M2,5 L4.5,8 L9,2"));
            Assert.That(source, Does.Contain("Visibility.Visible"));
            Assert.That(source, Does.Contain("StyleVectorModeToggleButton"));
            Assert.That(source, Does.Contain("IsChecked = active"));
            Assert.That(app, Does.Contain("ToolbarFocusVisualStyle"));
            Assert.That(source, Does.Contain("FocusVisualStyle"));
        });
    }

    [Test]
    public void ColorMarkersUseThemeContrastAndRecentSelectionRefresh()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var popupStart = source.IndexOf("private Popup BuildToolPopup", StringComparison.Ordinal);
        var textPopupStart = source.IndexOf("private void InitializeTextBoxPopup", StringComparison.Ordinal);
        var markerStart = source.IndexOf("private static Border CreateColorSelectionIndicator", StringComparison.Ordinal);
        var markerEnd = source.IndexOf("private void TrackToolPopupOpenedHandler", StringComparison.Ordinal);
        Assert.That(popupStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(textPopupStart, Is.GreaterThan(popupStart));
        Assert.That(markerStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(markerEnd, Is.GreaterThan(markerStart));
        var popupSource = source.Substring(popupStart, textPopupStart - popupStart);
        var markerSource = source.Substring(markerStart, markerEnd - markerStart);

        Assert.Multiple(() =>
        {
            Assert.That(popupSource, Does.Contain("UpdateColorMarkers"));
            Assert.That(popupSource, Does.Contain("ThemeFocusBrush"));
            Assert.That(popupSource, Does.Contain("ThemeSurfaceBrush"));
            Assert.That(source, Does.Contain("selectedChanged"));
            Assert.That(markerSource, Does.Not.Contain("Brushes.White"));
            Assert.That(popupSource, Does.Contain("double cellSize = 32"));
            Assert.That(source, Does.Contain("Width = 32"));
            Assert.That(source, Does.Contain("Height = 32"));
        });
    }

    [Test]
    public void PopupRebuildDetachesHandlersAndRestoresZOrderContract()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var utilities = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.Utilities.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DetachToolPopupHandlers"));
            Assert.That(source, Does.Contain("Opened -=").Or.Contain("Opened -= "));
            Assert.That(source, Does.Contain("FixToolPopupZOrder"));
            Assert.That(utilities, Does.Contain("FixToolPopupZOrder"));
            Assert.That(utilities, Does.Contain("CreateToolPopups"));
            Assert.That(source, Does.Contain("PopupZOrderHelper.FixPopupTopmost"));
        });
    }

    [Test]
    public void HighContrastPenUsesOwnerForegroundAndSeparateColorIndicator()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var theme = File.ReadAllText(Path.Combine(root, "Services", "ThemeService.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Not.Contain("PenIconBackplate"));
            Assert.That(xaml, Does.Not.Contain("PenIconContrast"));
            Assert.That(xaml, Does.Contain("x:Name=\"PenColorIndicator\""));
            Assert.That(xaml, Does.Contain("AncestorType=ToggleButton"));
            Assert.That(xaml, Does.Contain("ThemeFocusBrush"));
            Assert.That(source, Does.Contain("PenColorIndicator.Background"));
            Assert.That(source, Does.Not.Contain("PenIcon.Stroke"));
            Assert.That(source, Does.Not.Contain("PenIconContrast"));
            Assert.That(source, Does.Not.Contain("PenIconBackplate"));
            Assert.That(theme, Does.Contain("HighContrastPalette"));
            Assert.That(theme, Does.Contain("ThemeFocusBrush"));
        });
    }

    [Test]
    public void PopupRebuildUsesDetachableIdempotentZOrderRegistration()
    {
        var root = FindProjectRoot();
        var helper = File.ReadAllText(Path.Combine(root, "Services", "PopupZOrderHelper.cs"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var utilities = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.Utilities.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(helper, Does.Contain("UnfixPopupTopmost"));
            Assert.That(helper, Does.Contain("TryGetValue"));
            Assert.That(helper, Does.Contain("popup.Opened -= handler"));
            Assert.That(helper, Does.Contain("ContextMenuOpenedHandlers"));
            Assert.That(helper, Does.Contain("ComboBoxDropDownOpenedHandlers"));
            Assert.That(helper, Does.Contain("FixContextMenuTopmost"));
            Assert.That(helper, Does.Contain("FixComboBoxPopupTopmost"));
            Assert.That(source, Does.Contain("PopupZOrderHelper.UnfixPopupTopmost"));
            Assert.That(utilities, Does.Contain("DetachToolPopupHandlers"));
            Assert.That(utilities, Does.Contain("FixToolPopupZOrder"));
        });
    }

    [Test]
    public void HighlighterToolbarColorIndicatorSharesProductionPreviewAlpha()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var start = source.IndexOf("private void UpdateToolIconColors", StringComparison.Ordinal);
        var end = source.IndexOf("// Task 15: pen-only drawing", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var block = source.Substring(start, end - start);

        Assert.Multiple(() =>
        {
            Assert.That(block, Does.Contain("GetHighlighterPreviewStrokeColor(HighlighterApplyMode.Freehand, _highlighterColor)"));
            Assert.That(block, Does.Contain("HighlighterColorIndicator.Background"));
            Assert.That(block, Does.Not.Contain("HighlighterIcon.Fill"));
            Assert.That(block, Does.Not.Contain("HighlighterIcon.Stroke"));
        });
    }

    [Test]
    public void AreaHighlightMainPreviewSharesProductionFillOpacity()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Controls", "PdfPageControl.xaml.cs"));
        var start = source.IndexOf("private void BeginAreaHighlightDrag", StringComparison.Ordinal);
        var end = source.IndexOf("private void UpdateAreaHighlightDrag", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var block = source.Substring(start, end - start);

        Assert.Multiple(() =>
        {
            Assert.That(block, Does.Contain("AreaHighlightOpacity"));
            Assert.That(block, Does.Not.Contain("Color.FromArgb(48,"));
        });
    }

    [Test]
    public void SmokeScriptsUseProductionEditorIdsAndNeverFitPageEntry()
    {
        var root = FindProjectRoot();
        var helper = File.ReadAllText(Path.Combine(root, "tools", "OpenNotesEditorAutomationIds.ps1"));
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var scripts = new[]
        {
            "Test-OpenNotesEditorSmoke.ps1",
            "Test-OpenNotesPointerSmoke.ps1",
            "Test-OpenNotesAdvancedPointerSmoke.ps1",
            "Test-OpenNotesHiddenInkSmoke.ps1",
            "Test-OpenNotesCrossPageKeyboardSmoke.ps1"
        };

        foreach (var id in new[]
        {
            "Editor.UndoButton", "Editor.RedoButton", "Editor.PenToolButton",
            "Editor.HighlighterToolButton", "HiddenInkToolButton", "Editor.StickyNoteToolButton",
            "Editor.EraserToolButton", "Editor.ShapeToolButton", "Editor.LaserToolButton",
            "Editor.RulerToolButton", "Editor.SelectToolButton", "Editor.TextToolButton",
            "Editor.SavePdfButton", "Editor.ZoomOutButton", "Editor.ZoomInButton"
        })
        {
            Assert.That(xaml, Does.Contain($"AutomationProperties.AutomationId=\"{id}\""), id);
            Assert.That(helper, Does.Contain(id), id);
        }

        foreach (var scriptName in scripts)
        {
            var script = File.ReadAllText(Path.Combine(root, "tools", scriptName));
            Assert.That(script, Does.Not.Contain("FitPageButton"), scriptName);
            Assert.That(script, Does.Contain("EditorAutomationIds"), scriptName);
            Assert.That(script, Does.Not.Contain("'TextToolButton'"), scriptName);
            Assert.That(script, Does.Not.Contain("'PenToolButton'"), scriptName);
            Assert.That(script, Does.Not.Contain("'SavePdfButton'"), scriptName);
        }
    }

    [Test]
    public void SmokeScriptsUseSharedSurfaceAndHandleAliases()
    {
        var root = FindProjectRoot();
        var helper = File.ReadAllText(Path.Combine(root, "tools", "OpenNotesEditorAutomationIds.ps1"));
        var scripts = new[]
        {
            "Test-OpenNotesEditorSmoke.ps1",
            "Test-OpenNotesPointerSmoke.ps1",
            "Test-OpenNotesAdvancedPointerSmoke.ps1",
            "Test-OpenNotesHiddenInkSmoke.ps1",
            "Test-OpenNotesCrossPageKeyboardSmoke.ps1"
        };

        Assert.Multiple(() =>
        {
            Assert.That(helper, Does.Contain("Get-EditorPageAutomationId"));
            Assert.That(helper, Does.Contain("TextResizeHandleBottomRight"));
            Assert.That(helper, Does.Contain("TextAnnotationDragHandle"));
        });

        foreach (var scriptName in scripts)
        {
            var script = File.ReadAllText(Path.Combine(root, "tools", scriptName));
            Assert.That(script, Does.Contain("OpenNotesEditorAutomationIds.ps1"), scriptName);
            Assert.That(script, Does.Contain("$EditorAutomationIds.PdfScrollViewer"), scriptName);
            Assert.That(script, Does.Not.Contain("Find-DescendantByAutomationId (Find-MainWindow $process.Id) 'PdfScrollViewer'"), scriptName);
            Assert.That(script, Does.Not.Contain("Find-DescendantByAutomationId $mainWindow \"PdfPageControl.$pageIndex\""), scriptName);
        }
    }

    [Test]
    public void AlignmentItemsUseLocalizedValuesAndPreserveSelectionOnRefresh()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var utilities = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.Utilities.cs"));
        var localization = File.ReadAllText(Path.Combine(root, "Services", "LocalizationService.cs"));
        var verifier = File.ReadAllText(Path.Combine(root, "tools", "verify-i18n.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("TextAlignmentOption"));
            Assert.That(source, Does.Contain("BuildTextAlignmentOptions"));
            Assert.That(source, Does.Contain("SelectedValuePath"));
            Assert.That(source, Does.Contain("RefreshTextAlignmentOptions"));
            Assert.That(source, Does.Not.Contain("ItemsSource = new[] { \"Left\", \"Center\", \"Right\" }"));
            Assert.That(utilities, Does.Contain("RefreshTextAlignmentOptions"));
            Assert.That(localization, Does.Contain("Editor.AlignmentLeft"));
            Assert.That(localization, Does.Contain("Editor.AlignmentCenter"));
            Assert.That(localization, Does.Contain("Editor.AlignmentRight"));
            Assert.That(verifier, Does.Contain("ItemsSource"));
            Assert.That(verifier, Does.Contain("TextAlignmentOption"));
        });
    }

    [Test]
    public void PopupStateColorsUseThemeTokensAndSharedFocusTemplate()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var app = File.ReadAllText(Path.Combine(root, "App.xaml"));
        var templateStart = source.IndexOf("private static ControlTemplate CreateIconButtonTemplate", StringComparison.Ordinal);
        var templateEnd = source.IndexOf("private static ControlTemplate CreatePageChromeButtonTemplate", templateStart, StringComparison.Ordinal);
        Assert.That(templateStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(templateEnd, Is.GreaterThan(templateStart));
        var template = source.Substring(templateStart, templateEnd - templateStart);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("CreateIconButtonTemplate()"));
            Assert.That(source, Does.Not.Contain("CreateIconButtonTemplate(\"#E8E8E8\""));
            Assert.That(source, Does.Not.Contain("CreateIconButtonTemplate(\"#FEE2E2\""));
            Assert.That(source, Does.Not.Contain("CreateIconButtonTemplate(\"#E5E7EB\""));
            Assert.That(source, Does.Not.Contain("CreateIconButtonTemplate(\"#E0E7FF\""));
            Assert.That(template, Does.Contain("ThemeControlHoverBrush"));
            Assert.That(template, Does.Contain("ThemeControlPressedBrush"));
            Assert.That(template, Does.Contain("ThemeDisabledForegroundBrush"));
            Assert.That(template, Does.Contain("ThemeFocusBrush"));
            Assert.That(template, Does.Contain("DynamicResourceExtension"));
            Assert.That(template, Does.Not.Contain("ColorConverter"));
            Assert.That(source, Does.Not.Contain("CreatePageChromeButtonTemplate(\"#"));
            Assert.That(source, Does.Contain("ApplyToolbarFocusVisualStyle(slider)"));
            Assert.That(app, Does.Contain("FocusRing"));
            Assert.That(app, Does.Contain("IsKeyboardFocusWithin"));
            Assert.That(app, Does.Contain("IsEnabled"));
            Assert.That(app, Does.Contain("ThemeDisabledForegroundBrush"));
        });
    }

    [Test]
    public void Wave3ToolbarStateColorsUseLiveThemeReferences()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));

        var rulerStart = source.IndexOf("private void SetRulerVisible", StringComparison.Ordinal);
        var rulerEnd = source.IndexOf("private void EnsureRulerVisual", rulerStart, StringComparison.Ordinal);
        Assert.That(rulerStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(rulerEnd, Is.GreaterThan(rulerStart));
        var rulerBlock = source.Substring(rulerStart, rulerEnd - rulerStart);

        var fontGroupStart = source.IndexOf("var fontButtonGroup = new Border", StringComparison.Ordinal);
        var fontGroupEnd = source.IndexOf("panel.Children.Add(deleteButton)", fontGroupStart, StringComparison.Ordinal);
        Assert.That(fontGroupStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(fontGroupEnd, Is.GreaterThan(fontGroupStart));
        var fontGroupBlock = source.Substring(fontGroupStart, fontGroupEnd - fontGroupStart);

        var indicatorStart = source.IndexOf("_colorIndicator = new Border", StringComparison.Ordinal);
        var indicatorEnd = source.IndexOf("var colorButton = new Button", indicatorStart, StringComparison.Ordinal);
        Assert.That(indicatorStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(indicatorEnd, Is.GreaterThan(indicatorStart));
        var indicatorBlock = source.Substring(indicatorStart, indicatorEnd - indicatorStart);

        var colorPopupStart = source.IndexOf("var colorPopupBorder = new Border", StringComparison.Ordinal);
        var colorPopupEnd = source.IndexOf("colorPopupBorder.SetResourceReference", colorPopupStart, StringComparison.Ordinal);
        Assert.That(colorPopupStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(colorPopupEnd, Is.GreaterThan(colorPopupStart));
        var colorPopupBlock = source.Substring(colorPopupStart, colorPopupEnd - colorPopupStart);

        Assert.Multiple(() =>
        {
            Assert.That(rulerBlock, Does.Contain("RulerIcon?.SetResourceReference"));
            Assert.That(rulerBlock, Does.Contain("\"ThemeAccentBrush\""));
            Assert.That(rulerBlock, Does.Contain("\"ThemeForegroundBrush\""));
            Assert.That(rulerBlock, Does.Not.Contain("Color.FromRgb(0x00, 0x78, 0xD4)"));
            Assert.That(rulerBlock, Does.Not.Contain("Color.FromRgb(0x55, 0x55, 0x55)"));

            Assert.That(fontGroupBlock, Does.Contain("fontButtonGroup.SetResourceReference"));
            Assert.That(fontGroupBlock, Does.Contain("\"ThemeSurfaceAltBrush\""));
            Assert.That(fontGroupBlock, Does.Not.Contain("Color.FromArgb"));

            Assert.That(indicatorBlock, Does.Contain("_colorIndicator.SetResourceReference"));
            Assert.That(indicatorBlock, Does.Contain("\"ThemeBorderBrush\""));
            Assert.That(indicatorBlock, Does.Contain("Background = new SolidColorBrush(_textColor)"));
            Assert.That(indicatorBlock, Does.Not.Contain("BorderBrush = new SolidColorBrush(Color.FromArgb"));

            Assert.That(colorPopupBlock, Does.Not.Contain("Background = new SolidColorBrush"));
            Assert.That(colorPopupBlock, Does.Not.Contain("BorderBrush = new SolidColorBrush"));

            // ThemeSubtleHeader/ThemeDivider and popup state helpers own
            // these properties; a fixed initializer here would mask later
            // dark/high-contrast resource updates.
            Assert.That(source, Does.Not.Contain(
                "Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80))"));
            Assert.That(source, Does.Not.Contain(
                "Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0))"));
            Assert.That(source, Does.Not.Contain(
                "Background = new SolidColorBrush(Color.FromArgb(6, 0, 0, 0))"));
            Assert.That(source, Does.Not.Contain(
                "Background = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0))"));
            Assert.That(source, Does.Not.Contain(
                "BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))"));
            Assert.That(source, Does.Not.Contain(
                "BorderBrush = new SolidColorBrush(Color.FromArgb(36, 0, 0, 0))"));
        });
    }

    [Test]
    public void SelectionShapeAndFilterUseSemanticTogglePeersAndNonColorCues()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var popupStart = source.IndexOf("private void CreateSelectionPopup", StringComparison.Ordinal);
        var popupEnd = source.IndexOf("private void ScaleSelection", popupStart, StringComparison.Ordinal);
        Assert.That(popupStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(popupEnd, Is.GreaterThan(popupStart));
        var popup = source.Substring(popupStart, popupEnd - popupStart);

        Assert.Multiple(() =>
        {
            Assert.That(popup, Does.Contain("ToggleButton MakeShapeButton"));
            Assert.That(popup, Does.Contain("ToggleButton MakeFilterButton"));
            Assert.That(popup, Does.Contain("IsChecked"));
            Assert.That(popup, Does.Contain("ThemeFocusBrush"));
            Assert.That(popup, Does.Contain("ActiveBar"));
            Assert.That(popup, Does.Contain("MinWidth = 32"));
            Assert.That(popup, Does.Contain("MinHeight = 32"));
            Assert.That(popup, Does.Contain("ApplyToolbarPopupToggleStyle"));
            Assert.That(popup, Does.Not.Contain("\n            Button MakeShapeButton"));
            Assert.That(popup, Does.Not.Contain("\n            Button MakeFilterButton"));
        });
    }

    [Test]
    public void EditorSmokeFailsClosedForMissingRequiredProductionControls()
    {
        var root = FindProjectRoot();
        var smoke = File.ReadAllText(Path.Combine(root, "tools", "Test-OpenNotesEditorSmoke.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(smoke, Does.Contain("requiredAutomationIds"));
            Assert.That(smoke, Does.Contain("Required editor control missing"));
            Assert.That(smoke, Does.Contain("throw"));
            Assert.That(smoke, Does.Contain("optionalAutomationIds"));
            Assert.That(smoke, Does.Contain("OPTIONAL_CONTROL_MISSING"));
        });
    }

    [Test]
    public void EditorChromeUsesNamedLucideVectorsAndACompletePageNavigator()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var iconPath = Path.Combine(root, "Controls", "LucideIcon.cs");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(iconPath), Is.True, "The editor must use a font-independent named vector renderer.");
            Assert.That(xaml, Does.Contain("controls:LucideIcon"));
            Assert.That(xaml, Does.Contain("Kind=\"Undo2\""));
            Assert.That(xaml, Does.Contain("Kind=\"PenLine\""));
            Assert.That(xaml, Does.Contain("Kind=\"PanelLeftClose\""));
            Assert.That(xaml, Does.Contain("x:Name=\"PreviousPageButton\""));
            Assert.That(xaml, Does.Contain("x:Name=\"NextPageButton\""));
            Assert.That(xaml, Does.Contain("<UniformGrid x:Name=\"SidebarNavBar\""));
            Assert.That(source, Does.Contain("PreviousPageButton_Click"));
            Assert.That(source, Does.Contain("NextPageButton_Click"));
        });
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
    }
}
