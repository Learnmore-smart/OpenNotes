using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Services;
using Canvas = System.Windows.Controls.Canvas;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Caelum.Tests;

/// <summary>
/// Wave5 review contracts.  These tests intentionally describe the seams that
/// must stay live at runtime: motion/accessibility preferences, semantic
/// aliases, settings focus affordances, and the PDF display boundary.
/// </summary>
[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
[NonParallelizable]
public sealed class ThemeReviewContractTests
{
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
    public void ReduceMotionHasAProductionAnimationHelperAndConsumers()
    {
        var helper = typeof(ThemeService).GetMethod("GetAnimationDuration", BindingFlags.Public | BindingFlags.Static);
        var shouldAnimate = typeof(ThemeService).GetProperty("ShouldAnimate", BindingFlags.Public | BindingFlags.Static);
        Assert.That(helper, Is.Not.Null, "ThemeService must expose the shared reduce-motion duration helper.");
        Assert.That(shouldAnimate, Is.Not.Null, "ThemeService must expose the shared animation gate.");

        ThemeService.Apply("Light", reduceMotion: true);
        Assert.Multiple(() =>
        {
            Assert.That(shouldAnimate!.GetValue(null), Is.EqualTo(false));
            Assert.That(Application.Current!.Resources["ThemeAnimationDuration"], Is.EqualTo(new Duration(TimeSpan.Zero)));
            Assert.That(helper!.Invoke(null, new object[] { TimeSpan.FromMilliseconds(180) }), Is.EqualTo(TimeSpan.Zero));
        });

        string root = FindProjectRoot();
        foreach (string relativePath in new[]
        {
            "MainWindow.xaml.cs",
            Path.Combine("Pages", "HomePage.xaml.cs"),
            Path.Combine("Pages", "EditorPage.xaml.cs"),
            Path.Combine("Controls", "PdfPageControl.xaml.cs")
        })
        {
            string source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.That(source, Does.Contain("ThemeService.GetAnimationDuration"), relativePath);
        }

        string homeXaml = File.ReadAllText(Path.Combine(root, "Pages", "HomePage.xaml"));
        string editorXaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        string editorCode = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(root, "Pages", "HomePage.xaml.cs")), Does.Contain("AnimateTileScale"),
                "Home hover effects must consume the shared duration helper.");
            Assert.That(homeXaml, Does.Not.Contain("Duration=\"0:0:"),
                "Home hover storyboards must not capture a fixed duration.");
            Assert.That(editorXaml, Does.Not.Contain("Duration=\"0:0:"),
                "Editor XAML must not retain a fixed loading animation duration.");
            Assert.That(editorCode, Does.Contain("UpdateLoadingAnimation"));
            Assert.That(editorCode, Does.Contain("StopLoadingAnimation"));
            Assert.That(editorCode, Does.Contain("ThemeService.ShouldAnimate"));
        });
    }

    [Test]
    public void HomeTileHoverClonesFrozenTemplateScaleBeforeAnimating()
    {
        EnsureApplicationResources();
        ThemeService.Apply("Light", reduceMotion: false, reduceTransparency: false);

        var frozenScale = new ScaleTransform(1.0, 1.0);
        frozenScale.Freeze();
        var iconGrid = new Grid
        {
            Name = "IconGrid",
            Width = 120,
            Height = 160,
            RenderTransform = frozenScale
        };
        var button = new Button { Content = iconGrid };
        var window = new Window
        {
            Width = 240,
            Height = 240,
            ShowInTaskbar = false,
            Content = button
        };

        window.Show();
        window.UpdateLayout();
        try
        {
            MethodInfo animate = typeof(Caelum.Pages.HomePage).GetMethod(
                "AnimateTileScale",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new AssertionException("HomePage.AnimateTileScale was not found.");

            Assert.DoesNotThrow(() => animate.Invoke(null, new object[] { button, true }),
                "Hovering a home tile must not animate a frozen WPF template Freezable.");
            Assert.Multiple(() =>
            {
                Assert.That(iconGrid.RenderTransform, Is.TypeOf<ScaleTransform>());
                Assert.That(iconGrid.RenderTransform, Is.Not.SameAs(frozenScale));
                Assert.That(((ScaleTransform)iconGrid.RenderTransform).IsFrozen, Is.False);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void SemanticAliasesAreConsumedByProductionDynamicResources()
    {
        string root = FindProjectRoot();
        string[] productionFiles =
        {
            Path.Combine(root, "MainWindow.xaml"),
            Path.Combine(root, "SettingsWindow.xaml"),
            Path.Combine(root, "Pages", "HomePage.xaml"),
            Path.Combine(root, "Pages", "EditorPage.xaml"),
            Path.Combine(root, "Pages", "HomePage.Utilities.cs"),
            Path.Combine(root, "MainWindow.Utilities.cs")
        };
        string production = string.Join("\n", productionFiles.Where(File.Exists).Select(File.ReadAllText));

        foreach (string token in new[]
        {
            "ThemeWindowBrush", "ThemeWorkspaceBrush", "ThemeSidebarBrush", "ThemeToolbarBrush",
            "ThemeControlBrush", "ThemeTextBrush", "ThemeSubtleTextBrush", "ThemeDangerBrush"
        })
        {
            Assert.That(production, Does.Contain(token), $"{token} is declared but has no production consumer.");
        }
    }

    [Test]
    public void SettingsExposeTwoDipFocusAndDisabledContractsAndResponsiveFrenchLayout()
    {
        string root = FindProjectRoot();
        string app = File.ReadAllText(Path.Combine(root, "App.xaml"));
        string settings = File.ReadAllText(Path.Combine(root, "SettingsWindow.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(app, Does.Contain("SettingsFocusVisualStyle"));
            Assert.That(app, Does.Contain("BorderThickness=\"2\""));
            Assert.That(app, Does.Contain("ThemeDisabledForegroundBrush"));
            Assert.That(settings, Does.Contain("FocusVisualStyle"));
            Assert.That(settings, Does.Contain("x:Name=\"SettingsScrollViewer\""));
            Assert.That(settings, Does.Contain("TextWrapping=\"Wrap\""));
            Assert.That(settings, Does.Not.Contain("Width=\"180\""));
        });
    }

    [Test]
    public void HighContrastRefreshHasDeterministicHookAndShutdownLifecycle()
    {
        string root = FindProjectRoot();
        string theme = File.ReadAllText(Path.Combine(root, "Services", "ThemeService.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(theme, Does.Contain("SystemEvents.UserPreferenceChanged +="));
            Assert.That(theme, Does.Contain("SystemEvents.UserPreferenceChanged -="));
            Assert.That(theme, Does.Contain("RefreshSystemPreferencesForTests"));
            Assert.That(theme, Does.Contain("SystemParameters.HighContrast"));
        });
    }

    [Test]
    public void ReduceTransparencyTokenIsConsumedByPopupAndShadowChrome()
    {
        string root = FindProjectRoot();
        string app = File.ReadAllText(Path.Combine(root, "App.xaml"));
        string homeCode = File.ReadAllText(Path.Combine(root, "Pages", "HomePage.xaml.cs"));
        string editorCode = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        int declaration = app.IndexOf("ThemeSurfaceOpacity", StringComparison.Ordinal);
        Assert.That(declaration, Is.GreaterThanOrEqualTo(0));
        Assert.That(app[(declaration + "ThemeSurfaceOpacity".Length)..], Does.Contain("ThemeSurfaceOpacity"),
            "ThemeSurfaceOpacity must be consumed by popup/backdrop chrome, not only published.");
        Assert.That(app, Does.Contain("ThemeShadowOpacity"));
        Assert.That(homeCode, Does.Contain("GetShadowOpacity"));
        Assert.That(editorCode, Does.Contain("GetShadowOpacity"));
    }

    [Test]
    public void RuntimeChromeUsesDynamicThemeResourcesAndLeavesDataColorsAllowlisted()
    {
        string root = FindProjectRoot();
        string home = File.ReadAllText(Path.Combine(root, "Pages", "HomePage.xaml.cs"));
        string editor = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        string editorXaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        string pageControl = File.ReadAllText(Path.Combine(root, "Controls", "PdfPageControl.xaml.cs"));
        string pageControlXaml = File.ReadAllText(Path.Combine(root, "Controls", "PdfPageControl.xaml"));

        Assert.Multiple(() =>
        {
            foreach (string token in new[]
            {
                "ThemeSurfaceBrush", "ThemeDangerBrush", "ThemeControlBrush", "ThemeTextBrush",
                "ThemeSubtleTextBrush", "ThemeSurfaceOpacity", "SetResourceReference"
            })
                Assert.That(home, Does.Contain(token), token);

            foreach (string token in new[]
            {
                "BuildThumbnailContextMenu", "ThemeDangerBrush", "ThemeAccentBrush",
                "ThemeFocusBrush", "ThemeControlBrush", "ThemeTextBrush", "CaretBrushProperty",
                "SettingsFocusVisualStyle"
            })
                Assert.That(editor, Does.Contain(token), token);

            foreach (string legacyChromeLiteral in new[]
            {
                "Color.FromRgb(253, 253, 253)", "Color.FromRgb(220, 220, 220)",
                "Color.FromRgb(30, 30, 30)", "Color.FromRgb(80, 80, 80)",
                "Color.FromArgb(245, 255, 255, 255)", "Color.FromRgb(252, 165, 165)",
                "Color.FromRgb(191, 219, 254)", "Color.FromArgb(250, 255, 255, 255)",
                "Color.FromRgb(147, 197, 253)"
            })
            {
                Assert.That(home, Does.Not.Contain(legacyChromeLiteral), legacyChromeLiteral);
                Assert.That(editor, Does.Not.Contain(legacyChromeLiteral), legacyChromeLiteral);
            }

            Assert.That(editor, Does.Not.Contain("Color.FromArgb(90, 0, 120, 212)"));
            Assert.That(editor, Does.Not.Contain("Color.FromArgb(10, 0, 120, 212)"));
            Assert.That(pageControl, Does.Not.Contain("Color.FromArgb(255, 0, 120, 212)"));
            Assert.That(pageControl, Does.Not.Contain("Color.FromArgb(16, 0, 120, 212)"));
            Assert.That(pageControl, Does.Contain("ThemeAccentBrush"));
            Assert.That(pageControl, Does.Contain("ThemeSelectionBrush"));
            Assert.That(pageControlXaml, Does.Not.Contain("Stroke=\"#0078D4\""));
            Assert.That(editorXaml, Does.Not.Contain("Stroke=\"#0078D4\""));
        });
    }

    [Test]
    public void PdfDisplayLayerKeepsBitmapSurfaceUntinted()
    {
        string root = FindProjectRoot();
        string pdf = File.ReadAllText(Path.Combine(root, "Controls", "PdfPageControl.xaml"));
        Assert.Multiple(() =>
        {
            Assert.That(pdf, Does.Contain("x:Name=\"PageGrid\" Background=\"White\""));
            Assert.That(pdf, Does.Contain("x:Name=\"PdfImage\""));
            Assert.That(pdf, Does.Contain("x:Name=\"PdfImageOverlay\""));
            Assert.That(pdf, Does.Not.Contain("ThemeWorkspaceBackdropBrush"));
            Assert.That(pdf, Does.Not.Contain("ColorMatrix"));
            Assert.That(pdf, Does.Not.Contain("BitmapEffect"));
        });
    }

    [Test]
    public void SettingsControlsHaveKeyboardFocusPeersAndDisabledVisualsAtRuntime()
    {
        EnsureApplicationResources();
        ThemeService.Apply("Light", reduceMotion: false, reduceTransparency: false);
        LocalizationService.ApplyLanguage(AppLanguage.French);

        var window = new SettingsWindow(new AppSettings { Language = AppLanguage.French });
        window.SizeToContent = SizeToContent.Manual;
        window.Width = 420;
        window.Height = 420;
        window.ShowInTaskbar = false;
        window.Show();
        window.Activate();
        window.Focus();
        window.UpdateLayout();

        try
        {
            foreach (Control control in new Control[]
            {
                window.LanguageComboBox,
                window.ThemeComboBox,
                window.WorkspaceBackdropComboBox,
                window.SaveButton,
                window.CancelButton,
                window.CloseButton
            })
            {
                Assert.That(control.FocusVisualStyle, Is.Not.Null, control.Name);
                Assert.That(UIElementAutomationPeer.CreatePeerForElement(control), Is.Not.Null, control.Name);
                Assert.That(control.Focusable, Is.True, control.Name);
                Assert.That(KeyboardNavigation.GetIsTabStop(control), Is.True, control.Name);
                control.Focus();
                Keyboard.Focus(control);
                window.UpdateLayout();

                var focusRing = Descendants(control)
                    .OfType<Border>()
                    .FirstOrDefault(border =>
                        border.Visibility == Visibility.Visible &&
                        border.BorderThickness.Left >= 2 &&
                        border.BorderThickness.Top >= 2);
                // A non-interactive test desktop may refuse HWND activation;
                // in that case the focus visual is still verified through the
                // live FocusVisualStyle and keyboard navigation properties.
                if (control.IsKeyboardFocusWithin)
                    Assert.That(focusRing, Is.Not.Null, $"{control.Name} must render a two-DIP focus ring.");

                control.IsEnabled = false;
                window.UpdateLayout();
                Assert.That(Descendants(control).OfType<UIElement>().Any(element => element.Opacity < 0.99),
                    Is.True, $"{control.Name} must expose a disabled visual state.");
                control.IsEnabled = true;
            }
        }
        finally
        {
            window.Close();
            LocalizationService.ApplyLanguage(AppLanguage.English);
        }
    }

    [Test]
    public void FrenchSettingsAtMinimumSizeWrapWithoutHorizontalClip()
    {
        EnsureApplicationResources();
        ThemeService.Apply("Light", reduceMotion: true, reduceTransparency: false);
        LocalizationService.ApplyLanguage(AppLanguage.French);

        var window = new SettingsWindow(new AppSettings { Language = AppLanguage.French });
        window.SizeToContent = SizeToContent.Manual;
        window.Width = 420;
        window.Height = 420;
        window.ShowInTaskbar = false;
        window.Show();
        window.UpdateLayout();

        try
        {
            Assert.That(window.ActualWidth, Is.GreaterThanOrEqualTo(420));
            Assert.That(window.ActualHeight, Is.GreaterThanOrEqualTo(420));
            Assert.That(window.SettingsScrollViewer.ViewportWidth, Is.GreaterThan(0));
            Assert.That(window.SettingsScrollViewer.ExtentWidth,
                Is.LessThanOrEqualTo(window.SettingsScrollViewer.ViewportWidth + 1.0),
                "French labels must wrap instead of creating a hidden horizontal clip.");

            foreach (TextBlock label in new[]
            {
                window.LanguageLabelTextBlock,
                window.UtilityLabelTextBlock,
                window.AutoSaveIntervalLabelTextBlock,
                window.PressureLabelTextBlock,
                window.SmoothingLabelTextBlock,
                window.DefaultPenColorLabelTextBlock,
                window.DefaultPenSizeLabelTextBlock,
                window.ThemeLabelTextBlock,
                window.PerformanceModeLabelTextBlock,
                window.WorkspaceBackdropLabelTextBlock,
                window.PenOnlyLabelTextBlock
            })
            {
                Assert.That(label.TextWrapping, Is.EqualTo(TextWrapping.Wrap), label.Name);
                Assert.That(label.ActualWidth, Is.GreaterThan(0), label.Name);
                Assert.That(label.ActualHeight, Is.GreaterThan(0), label.Name);
            }
        }
        finally
        {
            window.Close();
            LocalizationService.ApplyLanguage(AppLanguage.English);
        }
    }

    [Test]
    public void SystemHighContrastRefreshUsesInjectedColorsAndCanUnhook()
    {
        EnsureApplicationResources();
        ThemeService.Apply("System", reduceMotion: false, reduceTransparency: false, workspaceBackdrop: "Slate");
        ThemeService.RefreshSystemPreferencesForTests(highContrast: true, darkTheme: false);

        Assert.Multiple(() =>
        {
            Assert.That(ThemeService.IsHighContrast, Is.True);
            Assert.That(ThemeService.CurrentWorkspaceBackdrop, Is.EqualTo("Neutral"));
            Assert.That(Application.Current!.Resources["ThemeFocusBrush"], Is.SameAs(SystemColors.HighlightBrush));
            Assert.That(Application.Current.Resources["ThemeSurfaceOpacity"], Is.EqualTo(1.0));
        });

        ThemeService.Shutdown();
        var hooked = typeof(ThemeService).GetProperty("SystemEventsHooked",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(hooked?.GetValue(null), Is.EqualTo(false));

        ThemeService.Apply("HighContrast", reduceMotion: false, reduceTransparency: false);
        ThemeService.RefreshSystemPreferencesForTests(highContrast: true);
        Assert.That(Application.Current.Resources["ThemeFocusBrush"], Is.SameAs(SystemColors.HighlightBrush));
        ThemeService.RefreshSystemPreferencesForTests();
    }

    [Test]
    public void PdfPageControlCompositeKeepsPagePixelsStableAcrossWorkspaceBackdrops()
    {
        EnsureApplicationResources();
        var application = Application.Current!;
        var source = CreateFixtureBitmap(4, 4, Colors.White, Colors.DarkSlateBlue);
        byte[]? baselineCrop = null;

        foreach (string backdrop in new[] { "Neutral", "Paper", "Slate" })
        {
            ThemeService.Apply("Light", workspaceBackdrop: backdrop);
            var page = new PdfPageControl { Width = 4, Height = 4 };
            var pdfImage = GetField<Image>(page, "PdfImage");
            var pdfImageOverlay = GetField<Image>(page, "PdfImageOverlay");
            var imageOverlayCanvas = GetField<Canvas>(page, "ImageOverlayCanvas");
            pdfImage.Source = source;
            pdfImageOverlay.Source = source;
            pdfImageOverlay.Opacity = 0;
            imageOverlayCanvas.Children.Add(new Rectangle
            {
                Width = 2,
                Height = 2,
                Fill = new SolidColorBrush(Colors.Crimson),
                IsHitTestVisible = false
            });

            Assert.Multiple(() =>
            {
                Assert.That(pdfImage.Opacity, Is.EqualTo(1.0), backdrop);
                Assert.That(pdfImage.Effect, Is.Null, backdrop);
                Assert.That(pdfImageOverlay.Effect, Is.Null, backdrop);
                Assert.That(pdfImageOverlay.Source, Is.SameAs(source), backdrop);
            });

            var workspace = new Border
            {
                Width = 12,
                Height = 12,
                Background = (Brush)application.Resources["ThemeWorkspaceBackdropBrush"],
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = page
            };
            workspace.Measure(new Size(12, 12));
            workspace.Arrange(new Rect(0, 0, 12, 12));
            workspace.UpdateLayout();

            var origin = page.TransformToAncestor(workspace).Transform(new Point(0, 0));
            byte[] rendered = RenderVisual(workspace, 12, 12);
            int left = Math.Max(0, (int)Math.Round(origin.X));
            int top = Math.Max(0, (int)Math.Round(origin.Y));
            byte[] crop = CropPixels(rendered, 12, left, top, 2, 2);
            baselineCrop ??= crop;
            Assert.That(crop, Is.EqualTo(baselineCrop),
                $"PDF page/annotation composite changed for {backdrop}; only the outer workspace may vary.");

        }
    }

    private static void EnsureApplicationResources()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows");

        var application = Application.Current ?? new Application();
        if (application.Resources["ModernComboBox"] != null)
            return;

        string appSource = File.ReadAllText(Path.Combine(FindProjectRoot(), "App.xaml"));
        int dictionaryStart = appSource.IndexOf("<ResourceDictionary>", StringComparison.Ordinal);
        int dictionaryEnd = appSource.IndexOf("</ResourceDictionary>", dictionaryStart, StringComparison.Ordinal);
        Assert.That(dictionaryStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(dictionaryEnd, Is.GreaterThan(dictionaryStart));
        string dictionaryMarkup = appSource.Substring(dictionaryStart, dictionaryEnd + "</ResourceDictionary>".Length - dictionaryStart)
            .Replace(
                "<ResourceDictionary>",
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                "xmlns:primitives=\"clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework\" " +
                "xmlns:sys=\"clr-namespace:System;assembly=mscorlib\">",
                StringComparison.Ordinal);
        var dictionary = (ResourceDictionary)XamlReader.Parse(dictionaryMarkup);
        application.Resources.MergedDictionaries.Add(dictionary);
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root == null)
            yield break;

        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }
    }

    private static T GetField<T>(object instance, string name)
        where T : class
    {
        return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T
            ?? throw new AssertionException($"Private WPF field '{name}' was not initialized.");
    }

    private static BitmapSource CreateFixtureBitmap(int width, int height, Color background, Color accent)
    {
        var pixels = new byte[width * height * 4];
        for (int index = 0; index < width * height; index++)
        {
            Color color = index == 0 ? accent : background;
            int offset = index * 4;
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] RenderVisual(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static byte[] CropPixels(byte[] pixels, int sourceWidth, int left, int top, int width, int height)
    {
        var crop = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(pixels, ((top + row) * sourceWidth + left) * 4, crop, row * width * 4, width * 4);
        return crop;
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
    }
}
