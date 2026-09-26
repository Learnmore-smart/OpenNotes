using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Caelum.Tests;

/// <summary>
/// T13-B launch-polish contracts — the WinUI home/library surface (hero
/// type ramp, Fluent empty states, Lucide menu glyphs, dashed add-tile),
/// the shared ContentDialog chrome (severity icons, body typography,
/// radius tokens), the shell toolbar/search cluster and the toast
/// overlay (pill chrome + gated fade/rise + retained-storyboard stop)
/// are pinned as source contracts; behavioural verification lives in
/// the tools/winui-*.ps1 smokes.
/// </summary>
[TestFixture]
public sealed class WinUiHomeDialogPolishSourceTests
{
    [Test]
    public void LucideIconCoversTheHomeAndDialogGlyphSet()
    {
        string icons = Read("Controls", "LucideIcon.cs");

        Assert.Multiple(() =>
        {
            // New kinds the home/dialog leg consumes.
            foreach (var kind in new[]
            {
                "Import", "FolderPlus", "FolderMinus", "FileMinus",
                "Info", "Clock", "ArrowDownAZ", "AlertTriangle", "Palette"
            })
            {
                Assert.That(icons, Does.Contain($"[\"{kind}\"]"), kind);
            }

            // Raw glyph callers (toast args, legacy call sites) resolve
            // through the legacy table instead of falling back to Circle.
            Assert.That(icons, Does.Contain("0xE790"));
            Assert.That(icons, Does.Contain("0xE721"));
            Assert.That(icons, Does.Contain("0xE72B"));
            Assert.That(icons, Does.Contain("0xE8CB"));
        });
    }

    [Test]
    public void HomeSurfaceUsesFluentTypeRampAndLucideGlyphsOnly()
    {
        string xaml = Read("Pages", "HomePage.xaml");

        Assert.Multiple(() =>
        {
            // No Segoe glyphs remain on the home surface.
            Assert.That(xaml, Does.Not.Contain("<FontIcon"));
            Assert.That(xaml, Does.Contain("xmlns:controls=\"using:Caelum.Controls\""));

            // Hero title/subtitle ride the Fluent type ramp.
            Assert.That(xaml, Does.Contain(
                "x:Name=\"HomeTitleTextBlock\" Style=\"{ThemeResource TitleTextBlockStyle}\""));
            Assert.That(xaml, Does.Contain(
                "x:Name=\"HomeSubtitleTextBlock\" Style=\"{ThemeResource BodyTextBlockStyle}\""));

            // Dashed add-tile affordance kept (dash + accent + hover fill),
            // glyph migrated to Lucide.
            Assert.That(xaml, Does.Contain("StrokeDashArray=\"6,4\""));
            Assert.That(xaml, Does.Contain("Stroke=\"{ThemeResource ThemeAccentBrush}\""));
            Assert.That(xaml, Does.Contain("Kind=\"Plus\""));
            Assert.That(xaml, Does.Contain("Fill=\"{x:Bind TileBackground, Mode=OneWay}\""));

            // Selection badge + breadcrumb + drop-overlay glyphs are Lucide.
            Assert.That(xaml, Does.Contain("Kind=\"Check\""));
            Assert.That(xaml, Does.Contain("Kind=\"ArrowLeft\""));
            Assert.That(xaml, Does.Contain("Kind=\"Import\""));

            // Preserved automation contract.
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"HomeTitleTextBlock\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"NavigateUpButton\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"TilesGrid\""));
        });
    }

    [Test]
    public void HomeEmptyStateCarriesIconTitleHintAndContextualCtas()
    {
        string xaml = Read("Pages", "HomePage.xaml");
        string home = Read("Pages", "HomePage.xaml.cs");
        string utilities = Read("Pages", "HomePage.Utilities.cs");
        string l10n = ReadCore("Services", "LocalizationService.cs");

        Assert.Multiple(() =>
        {
            // Bound panel: repeater collapses, icon disc + title + hint +
            // CTA row appear instead.
            Assert.That(xaml, Does.Contain("x:Name=\"HomeEmptyState\""));
            Assert.That(xaml, Does.Contain(
                "Visibility=\"{x:Bind EmptyStateVisibility, Mode=OneWay}\""));
            Assert.That(xaml, Does.Contain(
                "Visibility=\"{x:Bind TilesRepeaterVisibility, Mode=OneWay}\""));
            Assert.That(xaml, Does.Contain("Kind=\"FolderOpen\""));
            Assert.That(xaml, Does.Contain("Kind=\"Search\""));
            Assert.That(xaml, Does.Contain("Style=\"{ThemeResource AccentButtonStyle}\""));
            Assert.That(xaml, Does.Contain(
                "AutomationProperties.AutomationId=\"HomeEmptyStateAction\""));
            Assert.That(xaml, Does.Contain(
                "AutomationProperties.AutomationId=\"HomeEmptyStateClearSearch\""));

            // Variant logic: search-active swaps icon/title/CTA, and the
            // refresh stamp runs inside the repeater rebuild so every
            // filter keystroke re-evaluates it.
            Assert.That(home, Does.Contain("private bool HasContentTiles"));
            Assert.That(home, Does.Contain("private void RefreshEmptyState()"));
            Assert.That(home, Does.Contain("RefreshEmptyState();"));
            Assert.That(home, Does.Contain(
                "LocalizationService.Format(\"Home.Empty.SearchTitle\", _searchQuery)"));
            Assert.That(home, Does.Contain("private void EmptyStateClearSearch_Click"));
            Assert.That(home, Does.Contain("GetMainWindow()?.ClearHomeSearch()"));
            Assert.That(utilities, Does.Contain("RefreshEmptyState();"));

            // Catalog coverage (en/zh/fr tuples live in one literal).
            foreach (var key in new[]
            {
                "Home.Empty.LibraryTitle", "Home.Empty.LibraryHint",
                "Home.Empty.LibraryAction", "Home.Empty.SearchTitle",
                "Home.Empty.SearchHint", "Home.Empty.SearchClear"
            })
            {
                Assert.That(l10n, Does.Contain($"[\"{key}\"]"), key);
            }
        });
    }

    [Test]
    public void HomeContextMenusRideTheSharedLucidePathIconFactory()
    {
        string home = Read("Pages", "HomePage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(home, Does.Not.Contain("FontIcon"));
            Assert.That(home, Does.Contain(
                "private static PathIcon MenuIcon(string kind)"));
            Assert.That(home, Does.Contain("LucideIcon.GetIconGeometry(kind)"));
            Assert.That(home, Does.Contain("Icon = MenuIcon(iconKind)"));

            // Semantic kind coverage across both context menus + the
            // add-tile flyout.
            foreach (var kind in new[]
            {
                "\"FileText\"", "\"FolderPlus\"", "\"FilePlus\"",
                "\"Pencil\"", "\"SquareCheck\"", "\"Move\"", "\"Copy\"",
                "\"FolderOpen\"", "\"Download\"", "\"Trash2\"",
                "\"FileMinus\"", "\"FolderMinus\""
            })
            {
                Assert.That(home, Does.Contain(kind), kind);
            }
        });
    }

    [Test]
    public void ShellChromeKeepsCaptionsSegoeAndMovesEverythingElseToLucide()
    {
        string xaml = Read("MainWindow.xaml");
        string window = Read("MainWindow.xaml.cs");

        // Exactly three FontIcons remain: the Min/Max/Close caption glyphs
        // (Segoe caption font is the OS-correct chrome there; hover white
        // rides the ContentPresenter Foreground).
        int fontIconCount = Regex.Matches(xaml, "<FontIcon").Count;
        Assert.That(fontIconCount, Is.EqualTo(3),
            "only Minimize/Maximize/Close may keep Segoe caption glyphs");

        Assert.Multiple(() =>
        {
            // Brand glyph beside the product name (cheap T13-B titlebar
            // polish — no chrome restructure).
            Assert.That(xaml, Does.Contain("Kind=\"PenLine\""));

            // Tab strip + nav + toolbar icons are Lucide.
            Assert.That(xaml, Does.Contain("Kind=\"{x:Bind Icon, Mode=OneWay}\""));
            Assert.That(xaml, Does.Contain("Kind=\"X\""));
            Assert.That(xaml, Does.Contain("Kind=\"ArrowLeft\""));
            Assert.That(xaml, Does.Contain("Kind=\"ArrowRight\""));
            Assert.That(xaml, Does.Contain("Kind=\"Home\""));
            Assert.That(xaml, Does.Contain("Kind=\"Plus\""));
            Assert.That(xaml, Does.Contain("Kind=\"Ellipsis\""));
            Assert.That(xaml, Does.Contain("Kind=\"ListFilter\""));
            Assert.That(xaml, Does.Contain("Kind=\"ArrowUpDown\""));

            // Menu flyout items carry the shared PathIcon factory.
            Assert.That(window, Does.Contain(
                "private static PathIcon MenuIcon(string kind)"));
            Assert.That(window, Does.Contain("SettingsMenuItem.Icon ??= MenuIcon(\"Settings\")"));
            Assert.That(window, Does.Contain("CheckForUpdatesMenuItem.Icon ??= MenuIcon(\"RefreshCw\")"));
            Assert.That(window, Does.Contain("AboutMenuItem.Icon ??= MenuIcon(\"Info\")"));
            Assert.That(window, Does.Contain("SortByNameMenuItem.Icon ??= MenuIcon(\"ArrowDownAZ\")"));
            Assert.That(window, Does.Contain("SortByDateMenuItem.Icon ??= MenuIcon(\"Clock\")"));

            // Active Select state re-tints the Lucide Stroke, not the old
            // FontIcon Foreground.
            Assert.That(window, Does.Contain("SelectButtonIcon.Stroke = foreground"));
            Assert.That(window, Does.Not.Contain("SelectButtonIcon.Foreground"));
        });
    }

    [Test]
    public void SearchClusterReadsAsAFluentTextBox()
    {
        string xaml = Read("MainWindow.xaml");
        string window = Read("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            // 32px Fluent control height + 13px body + token radius; the
            // accent focus hairline is code-painted (custom chrome replaces
            // the stock TextBox focus underline).
            Assert.That(xaml, Does.Contain("x:Name=\"SearchBoxBorder\""));
            Assert.That(xaml, Does.Contain("Height=\"32\""));
            Assert.That(xaml, Does.Contain(
                "CornerRadius=\"{ThemeResource ThemeRadiusControl}\""));
            Assert.That(xaml, Does.Contain("Kind=\"Search\""));
            Assert.That(xaml, Does.Contain("GotFocus=\"SearchBox_GotFocus\""));
            Assert.That(xaml, Does.Contain("LostFocus=\"SearchBox_LostFocus\""));
            Assert.That(xaml, Does.Contain(
                "AutomationProperties.AutomationId=\"SearchBox\""));

            Assert.That(window, Does.Contain("private void UpdateSearchBoxBorder()"));
            Assert.That(window, Does.Contain(
                "ResolveThemeBrush(\"ThemeAccentBrush\", \"#2563EB\")"));
            Assert.That(window, Does.Contain("internal void ClearHomeSearch()"));

            // Theme swaps re-resolve the focus hairline.
            Assert.That(window, Does.Contain("UpdateSearchBoxBorder();"));
        });
    }

    [Test]
    public void ToastCarriesFluentPillChromeAndAGatedFadeRise()
    {
        string xaml = Read("MainWindow.xaml");
        string window = Read("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            // Pill radius token + ThemeShadow + z-lift + rise transform.
            Assert.That(xaml, Does.Contain("x:Name=\"ToastBorder\""));
            Assert.That(xaml, Does.Contain(
                "CornerRadius=\"{ThemeResource ThemeRadiusPill}\""));
            Assert.That(xaml, Does.Contain("x:Name=\"ToastRiseTransform\""));
            Assert.That(xaml, Does.Contain("Translation=\"0,0,16\""));
            Assert.That(xaml, Does.Contain("x:Name=\"ToastIcon\" Kind=\"Check\""));
            Assert.That(xaml, Does.Contain(
                "AutomationProperties.AutomationId=\"ToastBorder\""));
            Assert.That(xaml, Does.Contain(
                "AutomationProperties.AutomationId=\"ToastText\""));

            // The icon is a real Lucide control now — glyph strings resolve
            // through the icon's legacy table rather than a name map.
            Assert.That(window, Does.Contain(
                "private static string NormalizeToastIconKind(string icon)"));
            Assert.That(window, Does.Contain("ToastIcon.Kind = NormalizeToastIconKind(iconGlyph)"));
            Assert.That(window, Does.Not.Contain("ToastIcon.Glyph"));
            Assert.That(window, Does.Not.Contain("MapToastIconGlyph"));

            // Motion: retained storyboard stopped before re-seeding; a
            // cancelled dismiss must not collapse a re-shown toast; the
            // whole path honors ShouldAnimate + shared durations.
            Assert.That(window, Does.Contain("private Storyboard _toastStoryboard"));
            Assert.That(window, Does.Contain("_toastStoryboard?.Stop();"));
            Assert.That(window, Does.Contain("AnimateToastIn(fadeIn)"));
            Assert.That(window, Does.Contain("AnimateToastOut(fadeOut, cts)"));
            Assert.That(window, Does.Contain("cts.IsCancellationRequested"));
            Assert.That(window, Does.Contain("WinUiThemeService.GetAnimationDuration"));
            Assert.That(window, Does.Contain("WinUiThemeService.ShouldAnimate"));
        });
    }

    [Test]
    public void SharedDialogsCarrySeverityIconsAndStandardBodyTypography()
    {
        string service = Read("Services", "WinUiDialogService.cs");

        Assert.Multiple(() =>
        {
            // Severity glyphs: info accent / error + danger-confirm danger.
            Assert.That(service, Does.Contain("iconKind: \"Info\""));
            Assert.That(service, Does.Contain("iconKind: \"AlertCircle\""));
            Assert.That(service, Does.Contain("iconKind: \"AlertTriangle\""));
            Assert.That(service, Does.Contain("new LucideIcon"));
            Assert.That(service, Does.Contain("ResolveDialogBrush"));

            // Fluent body metrics: 14px over a 20px line height on the
            // subtle brush.
            Assert.That(service, Does.Contain("LineHeight = 20"));
            Assert.That(service, Does.Contain("\"ThemeSubtleForegroundBrush\""));

            // The danger primary button stays theme-fed.
            Assert.That(service, Does.Contain("\"ThemeDangerBrush\""));
            Assert.That(service, Does.Contain("BuildDangerButtonStyle"));
        });
    }

    [Test]
    public void TemplatePickerAndLocalDialogsUseRadiusTokens()
    {
        string picker = Read("Controls", "PageTemplatePickerDialog.cs");
        string home = Read("Pages", "HomePage.xaml.cs");
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(picker, Does.Contain("ResCornerRadius(\"ThemeRadiusCard\", 10)"));
            Assert.That(picker, Does.Contain("ResCornerRadius(\"ThemeRadiusControl\", 8)"));
            Assert.That(picker, Does.Not.Contain("CornerRadius = new CornerRadius(10)"));

            // The rename/create prompt shares the dialog body typography.
            Assert.That(home, Does.Contain(
                "Foreground = ResolveThemeBrush(\"ThemeSubtleForegroundBrush\", \"#6B7280\")"));
            // The page-range prompt follows the same convention.
            Assert.That(editor, Does.Contain(
                "ResolveThemeBrush(\"ThemeSubtleForegroundBrush\", Color.FromArgb(0xFF, 0x6B, 0x72, 0x80))"));
        });
    }

    private static string Read(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(
            new[] { Path.Combine(ProjectRoot(), "OpenNotes.WinUI") }.Concat(segments).ToArray()));
    }

    private static string ReadCore(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(
            new[] { Path.Combine(ProjectRoot(), "OpenNotes.Core") }.Concat(segments).ToArray()));
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
