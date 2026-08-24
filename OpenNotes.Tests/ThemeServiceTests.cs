using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
[NonParallelizable]
public sealed class ThemeServiceTests
{
    private static readonly IReadOnlyDictionary<string, string> LightPalette =
        new Dictionary<string, string>
        {
            ["ThemeWindowBackgroundBrush"] = "#F3F4F6",
            ["ThemeSurfaceBrush"] = "#FFFFFF",
            ["ThemeSurfaceAltBrush"] = "#F8F9FA",
            ["ThemeCanvasBrush"] = "#E5E7EB",
            ["ThemeBorderBrush"] = "#D1D5DB",
            ["ThemeForegroundBrush"] = "#1F2937",
            ["ThemeSubtleForegroundBrush"] = "#4B5563",
            ["ThemeControlHoverBrush"] = "#EEF0F2",
            ["ThemeControlPressedBrush"] = "#E2E5E9",
            ["ThemeSelectionBrush"] = "#DBEAFE",
            ["ThemeSelectionForegroundBrush"] = "#1E40AF",
            ["ThemeAccentBrush"] = "#2563EB",
            ["ThemeAccentHoverBrush"] = "#1D4ED8",
            ["ThemeAccentPressedBrush"] = "#1E40AF",
            ["ThemeDisabledForegroundBrush"] = "#9CA3AF",
            ["ThemeScrollbarTrackBrush"] = "#1F52606C",
            ["ThemeScrollbarThumbBrush"] = "#A85C6975",
            ["ThemeScrollbarThumbHoverBrush"] = "#CC394B5A",
            ["ThemeScrollbarThumbPressedBrush"] = "#E8212D38",
            ["ThemeSliderTrackBrush"] = "#221C5D99",
            ["ThemeMenuSeparatorBrush"] = "#1F1E2933",
            ["ThemeDeskBrush"] = "#E5E7EB",
            ["ThemePaperBrush"] = "#FFFFFF",
            ["ThemePaperAltBrush"] = "#F8F9FA",
            ["ThemeInkBrush"] = "#2563EB",
            ["ThemeMarginBrush"] = "#C2414B",
            ["ThemeMarkBrush"] = "#D9A72E"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["ThemeWindowBackgroundBrush"] = "#0C141D",
            ["ThemeSurfaceBrush"] = "#17212C",
            ["ThemeSurfaceAltBrush"] = "#1D2A37",
            ["ThemeCanvasBrush"] = "#081019",
            ["ThemeBorderBrush"] = "#314151",
            ["ThemeForegroundBrush"] = "#EEF2F4",
            ["ThemeSubtleForegroundBrush"] = "#A9B5BF",
            ["ThemeControlHoverBrush"] = "#223343",
            ["ThemeControlPressedBrush"] = "#2C4054",
            ["ThemeSelectionBrush"] = "#203E5C",
            ["ThemeSelectionForegroundBrush"] = "#E2F0FD",
            ["ThemeAccentBrush"] = "#6EACEA",
            ["ThemeAccentHoverBrush"] = "#8ABEF0",
            ["ThemeAccentPressedBrush"] = "#4A8CCC",
            ["ThemeDisabledForegroundBrush"] = "#6F7B86",
            ["ThemeScrollbarTrackBrush"] = "#3D465666",
            ["ThemeScrollbarThumbBrush"] = "#B88999AA",
            ["ThemeScrollbarThumbHoverBrush"] = "#D8B6C0CA",
            ["ThemeScrollbarThumbPressedBrush"] = "#F0E5EBF0",
            ["ThemeSliderTrackBrush"] = "#526EACEA",
            ["ThemeMenuSeparatorBrush"] = "#3AEEF2F4",
            ["ThemeDeskBrush"] = "#0C141D",
            ["ThemePaperBrush"] = "#17212C",
            ["ThemePaperAltBrush"] = "#1D2A37",
            ["ThemeInkBrush"] = "#6EACEA",
            ["ThemeMarginBrush"] = "#ED7A80",
            ["ThemeMarkBrush"] = "#F2C75C"
        };

    [SetUp]
    public void SetUp()
    {
        var application = Application.Current ?? new Application();
        application.Resources.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        ThemeService.ResetForTests();
        Application.Current?.Resources.Clear();
    }

    [Test]
    public void ApplyingDarkThemeUpdatesEveryChromeBrushAndNormalizesTheThemeName()
    {
        ThemeService.Apply("  dArK  ");

        Assert.That(ThemeService.IsDark, Is.True);
        Assert.That(ThemeService.CurrentTheme, Is.EqualTo("Dark"));
        AssertPalette(DarkPalette, "#92C7F5");
    }

    [Test]
    public void ApplyingUnknownThemeFallsBackToTheCompleteLightPalette()
    {
        ThemeService.Apply("Dark");
        ThemeService.Apply("Solarized");

        Assert.That(ThemeService.IsDark, Is.False);
        Assert.That(ThemeService.CurrentTheme, Is.EqualTo("Light"));
        AssertPalette(LightPalette, "#154F86");
    }

    [Test]
    public void ReapplyingTheSameThemeKeepsOneConsistentResourceSet()
    {
        ThemeService.Apply("Dark");
        ThemeService.Apply("Dark");

        Assert.That(Application.Current.Resources.Count, Is.GreaterThanOrEqualTo(DarkPalette.Count + 3));
        AssertPalette(DarkPalette, "#92C7F5");
    }

    [Test]
    public void ExplicitAccessibilityOverridesPublishReducedMotionAndTransparencyTokens()
    {
        ThemeService.Apply("Dark", reduceMotion: true, reduceTransparency: true);

        Assert.That(ThemeService.ReduceMotion, Is.True);
        Assert.That(ThemeService.ReduceTransparency, Is.True);
        Assert.That(Application.Current.Resources["ThemeAnimationDuration"], Is.EqualTo(new Duration(TimeSpan.Zero)));
        Assert.That(Application.Current.Resources["ThemeSurfaceOpacity"], Is.EqualTo(1.0));
    }

    [Test]
    public void HomePageFileTileChromeUsesSharedThemeTokens()
    {
        var projectRoot = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(projectRoot, "Pages", "HomePage.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Value=\"{DynamicResource ThemeControlHoverBrush}\""));
            Assert.That(xaml, Does.Contain("Value=\"{DynamicResource ThemeSelectionBrush}\""));
            Assert.That(xaml, Does.Contain("Foreground=\"{DynamicResource ThemeForegroundBrush}\""));
            Assert.That(xaml, Does.Contain("Foreground=\"{DynamicResource ThemeSubtleForegroundBrush}\""));
        });
    }

    [Test]
    public void EditorChromeUsesThemeResourcesForCombosAndRuntimePopups()
    {
        var projectRoot = FindProjectRoot();
        var appXaml = File.ReadAllText(Path.Combine(projectRoot, "App.xaml"));
        var editorXaml = File.ReadAllText(Path.Combine(projectRoot, "Pages", "EditorPage.xaml"));
        var editorCode = File.ReadAllText(Path.Combine(projectRoot, "Pages", "EditorPage.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(appXaml, Does.Contain("ItemContainerStyle\" Value=\"{DynamicResource ModernComboBoxItem}\""));
            Assert.That(appXaml, Does.Contain("x:Key=\"CompactComboBox\""));
            Assert.That(editorXaml, Does.Contain("Background=\"{DynamicResource ThemeSurfaceBrush}\""));
            Assert.That(editorXaml, Does.Contain("BorderBrush=\"{DynamicResource ThemeBorderBrush}\""));
            Assert.That(editorCode, Does.Contain("ThemeSubtleForegroundBrush"));
            Assert.That(editorCode, Does.Contain("ThemeSelectionForegroundBrush"));
            Assert.That(editorCode, Does.Contain("FixPopupTopmost(_stickyNotePopup)"));
            Assert.That(editorCode, Does.Contain("FixComboBoxPopupTopmost(_textFontFamilyCombo)"));
        });
    }

    [Test]
    public void PrimaryDesktopSurfacesUseThePaperInkMaterialTokens()
    {
        var projectRoot = FindProjectRoot();
        var appXaml = File.ReadAllText(Path.Combine(projectRoot, "App.xaml"));
        var mainXaml = File.ReadAllText(Path.Combine(projectRoot, "MainWindow.xaml"));
        var homeXaml = File.ReadAllText(Path.Combine(projectRoot, "Pages", "HomePage.xaml"));
        var editorXaml = File.ReadAllText(Path.Combine(projectRoot, "Pages", "EditorPage.xaml"));
        var settingsXaml = File.ReadAllText(Path.Combine(projectRoot, "SettingsWindow.xaml"));
        var pickerXaml = File.ReadAllText(Path.Combine(projectRoot, "PageTemplatePickerWindow.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(appXaml, Does.Contain("Value=\"{DynamicResource ThemePaperBrush}\""));
            Assert.That(mainXaml, Does.Contain("Background=\"{DynamicResource ThemeDeskBrush}\""));
            Assert.That(mainXaml, Does.Contain("x:Name=\"BrandMarginRail\""));
            Assert.That(mainXaml, Does.Contain("Background=\"{DynamicResource ThemeMarginBrush}\""));
            Assert.That(homeXaml, Does.Contain("x:Name=\"HomeMarginRail\""));
            Assert.That(homeXaml, Does.Contain("BorderBrush=\"{DynamicResource ThemeInkBrush}\""));
            Assert.That(editorXaml, Does.Contain("Background=\"{DynamicResource ThemePaperBrush}\""));
            Assert.That(editorXaml, Does.Contain("Foreground=\"{DynamicResource ThemeMarkBrush}\""));
            Assert.That(editorXaml, Does.Contain("Foreground=\"{DynamicResource ThemeMarginBrush}\""));
            Assert.That(settingsXaml, Does.Contain("x:Name=\"SettingsMarginRail\""));
            Assert.That(settingsXaml, Does.Contain("Background=\"{DynamicResource ThemePaperBrush}\""));
            Assert.That(pickerXaml, Does.Contain("Fill=\"{DynamicResource ThemePaperBrush}\""));
            Assert.That(pickerXaml, Does.Contain("Stroke=\"{DynamicResource ThemeMarginBrush}\""));
            Assert.That(pickerXaml, Does.Contain("Stroke=\"{DynamicResource ThemeInkBrush}\""));
        });
    }

    [Test]
    public void EditorPreviewKeyDownLeavesResizeHandleArrowsForTheHandleHandler()
    {
        var projectRoot = FindProjectRoot();
        var editorCode = File.ReadAllText(Path.Combine(projectRoot, "Pages", "EditorPage.xaml.cs"));
        var previewHandler = editorCode.IndexOf("private async void EditorPage_PreviewKeyDown", StringComparison.Ordinal);
        var nudgeBranch = editorCode.IndexOf("NudgeSelectedTextBox", previewHandler, StringComparison.Ordinal);
        var resizeHandleGuard = editorCode.IndexOf("TextResizeHandleBorder", previewHandler, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(previewHandler, Is.GreaterThanOrEqualTo(0));
            Assert.That(resizeHandleGuard, Is.GreaterThan(previewHandler));
            Assert.That(nudgeBranch, Is.GreaterThan(resizeHandleGuard));
        });
    }

    [Test]
    public void RuntimePdfPagesExposeStableAutomationIds()
    {
        var projectRoot = FindProjectRoot();
        var editorCode = File.ReadAllText(Path.Combine(projectRoot, "Pages", "EditorPage.xaml.cs"));

        Assert.That(
            editorCode,
            Does.Contain("AutomationProperties.SetAutomationId(pageControl, $\"PdfPageControl.{i}\")"));
    }

    [Test]
    public void RuntimeTextDragHandleExposesStableAutomationAndLocalizedName()
    {
        var projectRoot = FindProjectRoot();
        var editorCode = File.ReadAllText(Path.Combine(projectRoot, "Pages", "EditorPage.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(editorCode, Does.Contain("AutomationProperties.SetAutomationId(dragHandle, \"TextAnnotationDragHandle\")"));
            Assert.That(editorCode, Does.Contain("LocalizationService.Get(\"Editor.MoveTextBox\")"));
        });
    }

    private static void AssertPalette(
        IReadOnlyDictionary<string, string> expectedPalette,
        string expectedFocusColor)
    {
        var application = Application.Current ?? throw new InvalidOperationException("WPF application is not initialized.");
        // ThemeService also publishes the accessibility tokens that sit beside
        // the brush palette: focus colour, animation duration and surface
        // opacity. Keep this assertion aligned with that public resource set.
        Assert.That(application.Resources.Count, Is.GreaterThanOrEqualTo(expectedPalette.Count + 3));

        Assert.Multiple(() =>
        {
            foreach (var entry in expectedPalette)
            {
                var brush = application.Resources[entry.Key] as SolidColorBrush
                    ?? throw new InvalidOperationException($"Missing brush resource: {entry.Key}");
                Assert.That(brush.Color, Is.EqualTo(ParseColor(entry.Value)), entry.Key);
            }

            var focusBrush = application.Resources["ThemeFocusBrush"] as SolidColorBrush
                ?? throw new InvalidOperationException("Missing brush resource: ThemeFocusBrush");
            Assert.That(focusBrush.Color, Is.EqualTo(ParseColor(expectedFocusColor)), "ThemeFocusBrush");

            var animationDuration = application.Resources["ThemeAnimationDuration"] is Duration duration
                ? duration
                : throw new InvalidOperationException("Missing duration resource: ThemeAnimationDuration");
            Assert.That(animationDuration.TimeSpan, Is.EqualTo(TimeSpan.FromMilliseconds(160)), "ThemeAnimationDuration");
            Assert.That(application.Resources["ThemeSurfaceOpacity"], Is.EqualTo(0.96), "ThemeSurfaceOpacity");
        });
    }

    private static Color ParseColor(string value)
    {
        return (Color)ColorConverter.ConvertFromString(value);
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
