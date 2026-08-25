using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Caelum.Models;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
[NonParallelizable]
public sealed class ThemeSurfaceSourceTests
{
    private string _dataRoot = null!;

    [SetUp]
    public void SetUp()
    {
        var application = System.Windows.Application.Current ?? new System.Windows.Application();
        application.Resources.Clear();
        _dataRoot = Path.Combine(Path.GetTempPath(), "OpenNotesWave5", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, _dataRoot);
    }

    [TearDown]
    public void TearDown()
    {
        ThemeService.ResetForTests();
        System.Windows.Application.Current?.Resources.Clear();
        Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, null);
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Test]
    public void WorkspaceBackdropPropertyDefaultsNeutralAndLegacyJsonRoundTripsAllThreeValues()
    {
        var property = typeof(AppSettings).GetProperty("WorkspaceBackdrop");
        Assert.That(property, Is.Not.Null, "AppSettings must expose the optional WorkspaceBackdrop field.");

        var legacy = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.That(property!.GetValue(legacy), Is.EqualTo("Neutral"));

        foreach (var value in new[] { "Neutral", "Paper", "Slate" })
        {
            var input = new AppSettings();
            property.SetValue(input, value);
            var saved = AppSettingsService.Save(input);
            var loaded = AppSettingsService.Load();
            Assert.That(property.GetValue(saved), Is.EqualTo(value), value);
            Assert.That(property.GetValue(loaded), Is.EqualTo(value), value);
        }
    }

    [Test]
    public void WorkspaceBackdropInvalidValuesNormalizeWithoutMutatingCaller()
    {
        var property = typeof(AppSettings).GetProperty("WorkspaceBackdrop");
        Assert.That(property, Is.Not.Null);
        var source = new AppSettings();
        property!.SetValue(source, "  parchment  ");
        var before = JsonSerializer.Serialize(source);

        var sanitize = typeof(AppSettingsService).GetMethod("Sanitize", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(sanitize, Is.Not.Null);
        var sanitized = (AppSettings)sanitize!.Invoke(null, new object?[] { source })!;

        Assert.Multiple(() =>
        {
            Assert.That(property.GetValue(sanitized), Is.EqualTo("Neutral"));
            Assert.That(JsonSerializer.Serialize(source), Is.EqualTo(before));
        });
    }

    [Test]
    public void WorkspaceBackdropChangesWorkspaceBrushButHighContrastIgnoresDecoration()
    {
        var apply = typeof(ThemeService).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(method => method.Name == "Apply" && method.GetParameters().Length >= 4);
        Assert.That(apply, Is.Not.Null, "ThemeService.Apply must accept the persisted workspace backdrop.");

        var neutral = new object?[] { "Light", null, null, "Neutral" };
        apply!.Invoke(null, neutral);
        var neutralBrush = ApplicationResource("ThemeWorkspaceBackdropBrush");

        apply.Invoke(null, new object?[] { "Light", null, null, "Slate" });
        var slateBrush = ApplicationResource("ThemeWorkspaceBackdropBrush");
        Assert.That(slateBrush, Is.Not.EqualTo(neutralBrush), "Slate must visibly change workspace surround in Light mode.");

        apply.Invoke(null, new object?[] { "HighContrast", null, null, "Paper" });
        var currentBackdrop = typeof(ThemeService).GetProperty("CurrentWorkspaceBackdrop")?.GetValue(null);
        Assert.That(currentBackdrop, Is.EqualTo("Neutral"));
        Assert.That(ApplicationResource("ThemeWorkspaceBackdropBrush"), Is.EqualTo(ApplicationResource("ThemeCanvasBrush")));
    }

    [Test]
    public async Task WorkspaceBackdropDoesNotChangeRenderedPdfBitmapPixels()
    {
        string pdfPath = Path.Combine(_dataRoot, "backdrop-pixel-fixture.pdf");
        await PdfService.CreateBlankPdfAsync(pdfPath, widthPoints: 72, heightPoints: 72);

        await using var service = new PdfService();
        await service.LoadPdfAsync(pdfPath);
        byte[] baseline = await service.RenderPagePngBytesAsync(0);
        string baselineHash = Convert.ToHexString(SHA256.HashData(baseline));

        foreach (string backdrop in new[] { "Neutral", "Paper", "Slate" })
        {
            ThemeService.Apply("Light", workspaceBackdrop: backdrop);
            byte[] actual = await service.RenderPagePngBytesAsync(0);
            Assert.That(Convert.ToHexString(SHA256.HashData(actual)), Is.EqualTo(baselineHash), backdrop);
            Assert.That(actual, Is.EqualTo(baseline), $"PDF bitmap bytes changed for {backdrop} backdrop.");
        }
    }

    [Test]
    public void ThemeAndSettingsSourcesUseDynamicSemanticResourcesAndCompactSizing()
    {
        string root = FindProjectRoot();
        string app = File.ReadAllText(Path.Combine(root, "App.xaml"));
        string theme = File.ReadAllText(Path.Combine(root, "Services", "ThemeService.cs"));
        string editor = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        string pdf = File.ReadAllText(Path.Combine(root, "Controls", "PdfPageControl.xaml"));
        string settings = File.ReadAllText(Path.Combine(root, "SettingsWindow.xaml"));

        Assert.Multiple(() =>
        {
            foreach (var token in new[]
            {
                "ThemeWindowBrush", "ThemeWorkspaceBrush", "ThemeSidebarBrush", "ThemeToolbarBrush",
                "ThemeSurfaceBrush", "ThemeControlBrush", "ThemeBorderBrush", "ThemeTextBrush",
                "ThemeSubtleTextBrush", "ThemeAccentBrush", "ThemeFocusBrush", "ThemeSelectionBrush",
                "ThemeDangerBrush", "ThemeWorkspaceBackdropBrush"
            })
                Assert.That(app, Does.Contain(token), token);

            Assert.That(editor, Does.Contain("DynamicResource ThemeWorkspaceBackdropBrush"));
            Assert.That(theme, Does.Contain("SystemColors.WindowBrush"));
            Assert.That(theme, Does.Contain("SystemColors.HighlightBrush"));
            Assert.That(theme, Does.Contain("SystemColors.HighlightTextBrush"));
            Assert.That(settings, Does.Contain("SizeToContent"));
            Assert.That(settings, Does.Contain("ResizeMode=\"CanResize\""));
            Assert.That(settings, Does.Contain("VerticalScrollBarVisibility=\"Auto\""));
            Assert.That(settings, Does.Contain("WorkspaceBackdropComboBox"));
            Assert.That(pdf, Does.Contain("x:Name=\"PdfImage\""));
            Assert.That(pdf, Does.Contain("x:Name=\"PdfImageOverlay\""));
            Assert.That(pdf, Does.Not.Contain("ColorMatrix"));
            Assert.That(pdf, Does.Not.Contain("BitmapEffect"));
        });
    }

    [Test]
    public void ProductionChromeDoesNotReintroduceLegacyWarmLightPaletteLiterals()
    {
        string root = FindProjectRoot();
        var sourceFiles = new[]
        {
            Path.Combine(root, "App.xaml"),
            Path.Combine(root, "Services", "ThemeService.cs"),
            Path.Combine(root, "MainWindow.xaml"),
            Path.Combine(root, "Pages", "HomePage.xaml"),
            Path.Combine(root, "Pages", "EditorPage.xaml"),
            Path.Combine(root, "SettingsWindow.xaml")
        };
        var forbiddenWarmLiterals = new[]
        {
            "#F3EFE7", "#FFFDF8", "#F3EFE6", "#D7D3CB", "#D2CBC0",
            "#E6E1D8", "#FFFDF7", "#1E2933", "#66717B", "#E6EDF4",
            "#D3E0EB", "#DCEAF8", "#164C86", "#1C5D99", "#2872AF",
            "#124776", "#949694", "#B94B52"
        };

        Assert.Multiple(() =>
        {
            foreach (string file in sourceFiles)
            {
                string text = File.ReadAllText(file);
                foreach (string literal in forbiddenWarmLiterals)
                    Assert.That(text, Does.Not.Contain(literal), $"Legacy warm token {literal} in {Path.GetFileName(file)}");
            }
        });
    }

    [Test]
    public void SettingsUsesPillSwitchesAndHidesLegacyPenDefaults()
    {
        string root = FindProjectRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "SettingsWindow.xaml"));
        string source = File.ReadAllText(Path.Combine(root, "SettingsWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Key=\"SettingsSwitchStyle\""));
            Assert.That(xaml, Does.Contain("CornerRadius=\"12\""));
            Assert.That(xaml, Does.Contain("Style=\"{StaticResource SettingsSwitchStyle}\""));
            Assert.That(xaml, Does.Not.Contain("DefaultPenColorTextBox"));
            Assert.That(xaml, Does.Not.Contain("DefaultPenSizeTextBox"));
            Assert.That(source, Does.Not.Contain("DefaultPenColorTextBox"));
            Assert.That(source, Does.Not.Contain("DefaultPenSizeTextBox"));
        });
    }

    [Test]
    public void WorkspaceBackdropSupportsSixPersistedChoices()
    {
        string root = FindProjectRoot();
        string settingsSource = File.ReadAllText(Path.Combine(root, "SettingsWindow.xaml.cs"));
        string localization = File.ReadAllText(Path.Combine(root, "Services", "LocalizationService.cs"));

        foreach (string value in new[] { "Neutral", "Paper", "Mist", "Warm", "Slate", "Midnight" })
        {
            Assert.That(ThemeService.NormalizeWorkspaceBackdrop(value), Is.EqualTo(value), value);
            Assert.That(settingsSource, Does.Contain($"\"{value}\""), value);
        }

        Assert.Multiple(() =>
        {
            Assert.That(localization, Does.Contain("Settings.WorkspaceBackdropMist"));
            Assert.That(localization, Does.Contain("Settings.WorkspaceBackdropWarm"));
            Assert.That(localization, Does.Contain("Settings.WorkspaceBackdropMidnight"));
        });
    }

    private static string ApplicationResource(string key)
    {
        var application = System.Windows.Application.Current ?? throw new InvalidOperationException("WPF application is not initialized.");
        return application.Resources[key] is System.Windows.Media.SolidColorBrush brush
            ? brush.Color.ToString()
            : application.Resources[key]?.ToString() ?? string.Empty;
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
    }
}
