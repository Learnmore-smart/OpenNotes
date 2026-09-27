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
/// the tools/winui-*.ps1 smokes. T14-B adds the tile-grid slot sizing
/// that keeps every tile's title/info rows inside its card, the Fluent
/// folder/document art, and the shared Hand-cursor attached property.
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

    [Test]
    public void DangerConfirmButtonStyleCarriesARealTemplateWithDangerStates()
    {
        string app = Read("App.xaml");
        string service = Read("Services", "WinUiDialogService.cs");

        Assert.Multiple(() =>
        {
            // The style lives in App.xaml (WPF DialogDangerButton role) with
            // a full ControlTemplate + VisualStateManager — a code-only
            // Background setter let the stock Button template repaint the
            // fill generic gray on PointerOver/Pressed, erasing the warning
            // color on the hover-to-click path.
            Assert.That(app, Does.Contain("x:Key=\"DialogDangerButtonStyle\""));
            Assert.That(app, Does.Contain("<ControlTemplate TargetType=\"Button\">"));
            Assert.That(app, Does.Contain("<VisualStateManager.VisualStateGroups>"));

            // Danger fill resting on the theme brush; hover/press paint a
            // translucent StateLayer darker/darkest; disabled dims.
            Assert.That(app, Does.Contain(
                "Value=\"{ThemeResource ThemeDangerBrush}\""));
            Assert.That(app, Does.Contain("<VisualState x:Name=\"PointerOver\">"));
            Assert.That(app, Does.Contain("<VisualState x:Name=\"Pressed\">"));
            Assert.That(app, Does.Contain("<VisualState x:Name=\"Disabled\">"));
            Assert.That(app, Does.Contain("x:Name=\"StateLayer\""));

            // The dialog service resolves the style instead of handing a
            // template-less Style to ContentDialog.PrimaryButtonStyle.
            Assert.That(service, Does.Contain("\"DialogDangerButtonStyle\""));
        });
    }

    [Test]
    public void HomeTileLayoutReservesLabelRowsAndDropsTheMarginRail()
    {
        string xaml = Read("Pages", "HomePage.xaml");

        Assert.Multiple(() =>
        {
            // T14-B root cause: UniformGridLayout cached item 0's
            // DesiredSize (the add tile, 200x184) as the effective slot
            // for EVERY element — folder/file cards were arranged into a
            // 160x160 box after their 20,12 margin, so the title/info
            // rows below the 160-DIP icon grid rendered outside the card
            // bounds (populated and Visible, but clipped). Explicit Min*
            // sizes replace first-item measurement entirely and also keep
            // slots stable when selection mode removes the add tile from
            // index 0. 204x232 (not a snug 200x230) leaves headroom for
            // TileBorderThickness >=1, which inflates the true desired
            // size to ~202-203w / ~230.3-231.4h — spec-review flag.
            Assert.That(xaml, Does.Contain("MinItemWidth=\"204\""));
            Assert.That(xaml, Does.Contain("MinItemHeight=\"232\""));

            // The stray accent strip beside the header is gone (the
            // element declaration, that is — a historical comment still
            // names it); ThemeMarginBrush has no remaining home-surface
            // use at all.
            Assert.That(xaml, Does.Not.Contain("x:Name=\"HomeMarginRail\""));
            Assert.That(xaml, Does.Not.Contain("ThemeMarginBrush"));

            // Title/info rows survive inside both content templates —
            // they must live on the tile card (Grid.Row 1/2 below the
            // 160-DIP icon grid), bound OneWay to the HomeTile.
            Assert.That(xaml, Does.Contain(
                "Text=\"{x:Bind FileName, Mode=OneWay}\" TextTrimming=\"CharacterEllipsis\""));
            Assert.That(xaml, Does.Contain(
                "Text=\"{x:Bind InfoText, Mode=OneWay}\" FontSize=\"12\""));
        });
    }

    [Test]
    public void TileIconsUseFluentFolderAndDocumentArt()
    {
        string xaml = Read("Pages", "HomePage.xaml");

        Assert.Multiple(() =>
        {
            // Folder: Path silhouette — back plate+tab, front plate and
            // the opening seam — still driven by the per-folder color
            // brush trio so Color personalization keeps working.
            Assert.That(xaml, Does.Contain(
                "Fill=\"{x:Bind FolderTabBrush, Mode=OneWay}\""));
            Assert.That(xaml, Does.Contain(
                "Fill=\"{x:Bind FolderBodyBrush, Mode=OneWay}\""));
            Assert.That(xaml, Does.Contain(
                "Stroke=\"{x:Bind FolderLineBrush, Mode=OneWay}\""));
            Assert.That(xaml, Does.Contain("L32,6 L40,14")); // tab notch

            // File: dog-eared paper sheet (BorderBrush hairline + fold
            // flap in SurfaceAlt) + subtle content lines + accent PDF
            // pill — replaces the accent-spine / margin-strip art.
            Assert.That(xaml, Does.Contain(
                "Fill=\"{ThemeResource ThemePaperBrush}\""));
            Assert.That(xaml, Does.Contain(
                "Fill=\"{ThemeResource ThemeSurfaceAltBrush}\""));
            Assert.That(xaml, Does.Contain("Text=\"PDF\""));
            Assert.That(xaml, Does.Contain("M58,0.5 L71.5,14 L58,14 Z")); // fold flap

            // The five art Paths share literal coordinate spaces, so
            // Stretch MUST be None — Fill/Uniform normalize each path's
            // own bounds to the slot, which painted the 1.4px seam over
            // the whole icon and blew the 14x14 fold into a giant
            // triangle (spec-review defect). No Stretch="Fill" remains.
            Assert.That(
                Regex.Matches(xaml, Regex.Escape("Stretch=\"None\"")).Count,
                Is.GreaterThanOrEqualTo(5));
            Assert.That(xaml, Does.Not.Contain("Stretch=\"Fill\""));

            // Icon grids + tile roots stay named for the smoke harness.
            Assert.That(xaml, Does.Contain("x:Name=\"IconGrid\""));
            Assert.That(xaml, Does.Contain("x:Name=\"FolderIconGrid\""));
            Assert.That(xaml, Does.Contain("x:Name=\"FolderTileBorder\""));
            Assert.That(xaml, Does.Contain("x:Name=\"FileTileBorder\""));

            // x:Bind templates don't populate DataContext — the tile
            // reaches code-behind via Tag; every tile element keeps it.
            Assert.That(
                Regex.Matches(xaml, Regex.Escape("Tag=\"{x:Bind}\"")).Count,
                Is.GreaterThanOrEqualTo(3));
        });
    }

    [Test]
    public void HandCursorHelperIsSharedAcrossHomeAndShell()
    {
        string helper = Read("Controls", "CursorExtensions.cs");
        string home = Read("Pages", "HomePage.xaml");
        string main = Read("MainWindow.xaml");

        Assert.Multiple(() =>
        {
            // UIElement.ProtectedCursor is protected-only in WinUI, so the
            // attached property reaches the non-public setter via
            // reflection (the same workaround CommunityToolkit's WinUI
            // cursor extension uses) and shares one immutable cursor.
            Assert.That(helper, Does.Contain("DependencyProperty.RegisterAttached"));
            Assert.That(helper, Does.Contain("\"ProtectedCursor\""));
            Assert.That(helper, Does.Contain(
                "BindingFlags.Instance | BindingFlags.NonPublic"));
            Assert.That(helper, Does.Contain(
                "InputSystemCursor.Create(InputSystemCursorShape.Hand)"));

            // Home surface: add/folder/file tiles, breadcrumb back,
            // empty-state CTAs and the six selection-bar buttons.
            Assert.That(
                Regex.Matches(home, Regex.Escape("CursorExtensions.Hand=\"True\"")).Count,
                Is.EqualTo(12), "tile + header + empty-state + selection-bar hit targets");
            foreach (var anchor in new[]
            {
                "x:Name=\"FolderTileBorder\"", "x:Name=\"FileTileBorder\"",
                "AutomationProperties.AutomationId=\"NavigateUpButton\"",
                "x:Name=\"EmptyStateActionButton\"", "x:Name=\"EmptyStateClearButton\"",
                "x:Name=\"DoneSelectionButton\""
            })
            {
                Assert.That(home, Does.Contain(anchor), anchor);
            }

            // Shell chrome: tab cards, nav cluster, tab/overflow and the
            // library select/sort buttons — T14-C adds the tab-close and
            // Min/Max/Close caption buttons (8 → 12).
            Assert.That(
                Regex.Matches(main, Regex.Escape("CursorExtensions.Hand=\"True\"")).Count,
                Is.EqualTo(12), "tabs + nav + toolbar + caption buttons");
            foreach (var anchor in new[]
            {
                "x:Name=\"NavBackButton\"", "x:Name=\"NavForwardButton\"",
                "x:Name=\"NavHomeButton\"", "x:Name=\"NewTabButton\"",
                "x:Name=\"MoreButton\"", "x:Name=\"SelectButton\"",
                "x:Name=\"SortButton\"", "x:Name=\"TabCloseButton\"",
                "x:Name=\"MinimizeButton\"", "x:Name=\"MaximizeButton\"",
                "x:Name=\"CloseButton\""
            })
            {
                Assert.That(main, Does.Contain(anchor), anchor);
            }
        });
    }

    [Test]
    public void EditorChromeRollsOutTheHandCursor()
    {
        // T14-C "hover on button → hand cursor" rollout: every toolbar
        // button/toggle, the zoom + page-jump cells, the sidebar chrome
        // and the clickable template roots carry the shared attached
        // property; code-created chrome uses CursorExtensions.SetHand.
        string editor = Read("Pages", "EditorPage.xaml");
        string editorCode = Read("Pages", "EditorPage.xaml.cs");
        string page = Read("Controls", "PdfPageControl.xaml.cs");
        string picker = Read("Controls", "PageTemplatePickerDialog.cs");

        Assert.Multiple(() =>
        {
            Assert.That(
                Regex.Matches(editor, Regex.Escape("CursorExtensions.Hand=\"True\"")).Count,
                Is.EqualTo(31), "toolbar + sidebar + search + template hit targets");
            foreach (var anchor in new[]
            {
                "AutomationId=\"Editor.UndoButton\"", "AutomationId=\"Editor.PenToolButton\"",
                "AutomationId=\"Editor.ZoomOutButton\"", "AutomationId=\"Editor.ZoomLabel\"",
                "AutomationId=\"Editor.PreviousPageButton\"", "AutomationId=\"Editor.NextPageButton\"",
                "AutomationId=\"Editor.Sidebar.Collapse\"", "AutomationId=\"Editor.Sidebar.Pages\"",
                "AutomationId=\"Editor.Sidebar.BookmarkToggle\"", "AutomationId=\"PdfSearchCloseButton\"",
                "x:Name=\"ThumbnailCardRoot\"", "x:Name=\"OutlineInvokeGlyphButton\""
            })
            {
                Assert.That(editor, Does.Contain(anchor), anchor);
            }

            // Code-created chrome: page insert/delete, inline text toolbar,
            // sticky editor, shared flyout toggle builders, swatches,
            // search-result rows (T14-C review nit).
            Assert.That(
                Regex.Matches(editorCode, Regex.Escape("CursorExtensions.SetHand(")).Count,
                Is.EqualTo(18), "code-created editor buttons + result rows");
            Assert.That(page, Does.Contain("CursorExtensions.SetHand(hitButton, true)"));
            Assert.That(
                Regex.Matches(picker, Regex.Escape("CursorExtensions.SetHand(")).Count,
                Is.EqualTo(2), "template cards + browse button");
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
