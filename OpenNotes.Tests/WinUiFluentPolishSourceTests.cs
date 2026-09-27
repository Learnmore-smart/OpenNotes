using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Caelum.Tests;

/// <summary>
/// T13-A launch-polish contracts — the WinUI editor's Fluent card
/// language (page-card radius/clip, flyout presenter chrome, search
/// panel, floating overlays), Lucide menu glyphs, the accent-armed
/// toolbar toggle icons and the sidebar thumbnail hover-lift are pinned
/// as source contracts; visual/behavioural verification lives in the
/// tools/winui-*.ps1 smokes.
/// </summary>
[TestFixture]
public sealed class WinUiFluentPolishSourceTests
{
    [Test]
    public void PageCardRoundsAndClipsToTheThemeRadius()
    {
        string xaml = Read("Controls", "PdfPageControl.xaml");

        Assert.Multiple(() =>
        {
            // Separate shadow caster and content surface share the same
            // corner radius so the page edge and its shadow agree.
            Assert.That(xaml, Does.Contain("x:Name=\"PageCardClip\" CornerRadius=\"{ThemeResource ThemeRadiusControl}\""));
            Assert.That(xaml, Does.Contain("<ThemeShadow/>"));
            Assert.That(xaml, Does.Contain("Translation=\"0,0,8\""));
            // The paper surface stays untinted white (bitmap parity).
            Assert.That(xaml, Does.Contain("x:Name=\"PageGrid\" Background=\"White\""));
        });
    }

    [Test]
    public void EditorMenusCarryLucideGlyphsOnEveryItem()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        string page = Read("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            // Shared glyph factory + the page-context-menu item helper.
            Assert.That(editor, Does.Contain("private static PathIcon MenuIcon(string kind)"));
            Assert.That(editor, Does.Contain("LucideIcon.GetIconGeometry(kind)"));
            Assert.That(editor, Does.Contain("Icon = iconKind == null ? null : MenuIcon(iconKind)"));

            // Blank-surface menu.
            Assert.That(editor, Does.Contain("Icon = MenuIcon(\"Copy\")"));
            Assert.That(editor, Does.Contain("Icon = MenuIcon(\"ClipboardPaste\")"));
            Assert.That(editor, Does.Contain("Icon = MenuIcon(\"SquareCheck\")"));
            Assert.That(editor, Does.Contain("Icon = MenuIcon(\"RefreshCw\")"));

            // Page-context menu (Print + export/insert/rotate helpers).
            Assert.That(editor, Does.Contain("Icon = MenuIcon(\"Printer\")"));
            Assert.That(editor, Does.Contain("\"ImageDown\""));
            Assert.That(editor, Does.Contain("\"Images\""));
            Assert.That(editor, Does.Contain("\"FilePlus\""));
            Assert.That(editor, Does.Contain("\"RotateCcw\""));

            // Thumbnail, bookmark and version-history menus.
            Assert.That(editor, Does.Contain("Icon = MenuIcon(\"BookmarkMinus\")"));
            Assert.That(editor, Does.Contain("Icon = MenuIcon(\"History\")"));

            // Sticky-note context menu (built inside the page control).
            Assert.That(page, Does.Contain("Icon = new PathIcon"));
            Assert.That(page, Does.Contain("LucideIcon.GetIconGeometry(\"Trash2\")"));
        });
    }

    [Test]
    public void ToolFlyoutsUseTheSharedFluentCardChrome()
    {
        string xaml = Read("Pages", "EditorPage.xaml");
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // The presenter template re-states surface/border/card radius
            // plus the ThemeShadow the stock template lacks.
            Assert.That(xaml, Does.Contain("x:Key=\"EditorFlyoutPresenterStyle\""));
            Assert.That(xaml, Does.Contain("Value=\"{ThemeResource ThemeRadiusCard}\""));
            Assert.That(xaml, Does.Contain("Translation=\"0,0,32\""));
            Assert.That(xaml, Does.Contain("<ThemeShadow/>"));

            // Stamped on every tool flyout + the persistent text-colour
            // flyout; MenuFlyout presenters stay stock.
            Assert.That(editor, Does.Contain("private void ApplyToolFlyoutChrome(Flyout flyout)"));
            Assert.That(editor, Does.Contain("flyout.FlyoutPresenterStyle = presenterStyle"));
            Assert.That(editor, Does.Contain("ApplyToolFlyoutChrome(_textColorFlyout)"));
            Assert.That(editor, Does.Contain("ApplyToolFlyoutChrome(flyout)"));
        });
    }

    [Test]
    public void ArmedToolbarTogglesAccentTheirGlyphs()
    {
        string xaml = Read("Pages", "EditorPage.xaml");
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Every tool-strip toggle icon rides its owner's Foreground so
            // the armed state accents the glyph through the button (VSM
            // setters can't reach the control root from the template).
            foreach (var button in new[]
            {
                "PenToolButton", "HighlighterToolButton", "HiddenInkToolButton",
                "StickyNoteToolButton", "EraserToolButton", "ShapeToolButton",
                "LaserToolButton", "SelectToolButton", "TextToolButton",
                "RulerToolButton", "PenOnlyButton"
            })
            {
                Assert.That(xaml,
                    Does.Contain($"Stroke=\"{{Binding Foreground, ElementName={button}}}\""),
                    $"{button} icon must bind the button Foreground");
            }

            // Combined checked states keep full visuals inline.
            Assert.That(xaml, Does.Contain("x:Name=\"Checked\""));
            Assert.That(xaml, Does.Contain("x:Name=\"CheckedPointerOver\""));
            Assert.That(xaml, Does.Contain("x:Name=\"CheckedPressed\""));
            Assert.That(xaml, Does.Contain("x:Name=\"CheckedDisabled\""));

            // The accent/text Foreground is re-resolved imperatively at
            // every IsChecked write and on ThemeApplied (brush swap).
            Assert.That(editor, Does.Contain("private void ApplyToolButtonAccent(ToggleButton button)"));
            Assert.That(editor, Does.Contain("private void ApplyToolbarToggleAccents()"));
            Assert.That(editor, Does.Contain("ApplyToolButtonAccent(RulerToolButton)"));
            Assert.That(editor, Does.Contain("ApplyToolButtonAccent(PenOnlyButton)"));
        });
    }

    [Test]
    public void SearchPanelKeepsAutomationContractAndGainsEmptyStates()
    {
        string xaml = Read("Pages", "EditorPage.xaml");
        string editor = Read("Pages", "EditorPage.xaml.cs");
        string l10n = ReadCore("Services", "LocalizationService.cs");

        // Slice RunPdfSearchAsync so the completed-path ordering can be
        // pinned below (re-review regression).
        int bodyStart = editor.IndexOf("private async Task RunPdfSearchAsync(", StringComparison.Ordinal);
        int bodyEnd = bodyStart < 0 ? -1 : editor.IndexOf(
            "private async void PdfSearchResultsListBox_SelectionChanged", bodyStart, StringComparison.Ordinal);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(bodyEnd, Is.GreaterThan(bodyStart));
        string searchBody = editor.Substring(bodyStart, bodyEnd - bodyStart);
        // T14-C review: the row is built as `var item = new ListViewItem
        // {...}` so `CursorExtensions.SetHand(item, true)` can stamp it
        // before `Items.Add(item)` — anchor on the add call.
        int resultItems = searchBody.IndexOf("PdfSearchResultsListBox.Items.Add(item);", StringComparison.Ordinal);
        int tailClear = resultItems < 0 ? -1 : searchBody.IndexOf(
            "_pdfSearchInFlight = false;", resultItems, StringComparison.Ordinal);
        int finalStamp = tailClear < 0 ? -1 : searchBody.IndexOf(
            "UpdatePdfSearchEmptyState();", tailClear, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            // Automation ids + card chrome.
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"PdfSearchPanel\""));
            Assert.That(xaml, Does.Contain("x:Name=\"PdfSearchTextBox\""));
            Assert.That(xaml, Does.Contain("x:Name=\"PdfSearchResultsListBox\""));
            Assert.That(xaml, Does.Contain("x:Name=\"PdfSearchStatusTextBlock\""));
            Assert.That(xaml, Does.Contain("x:Name=\"PdfSearchEmptyState\""));
            Assert.That(xaml, Does.Contain("x:Key=\"PdfSearchResultItemStyle\""));

            // Item style stamped per direct-added hit; empty-state helper
            // covers both the untouched hint and the zero-hit message.
            Assert.That(editor, Does.Contain("Resources[\"PdfSearchResultItemStyle\"]"));
            Assert.That(editor, Does.Contain("private void UpdatePdfSearchEmptyState()"));
            Assert.That(editor, Does.Contain("LocalizationService.Get(\"Editor.SearchEmptyHint\")"));
            Assert.That(editor, Does.Contain("LocalizationService.Get(\"Editor.SearchNoResults\")"));

            // In-flight state is an explicit flag, not a localized status-
            // text compare (a finished 0-hit search must not read as
            // "Searching…" after a language flip, and the overlay re-stamps
            // the moment a new search starts so a stale "No matches" can't
            // linger mid-flight).
            Assert.That(editor, Does.Contain("private bool _pdfSearchInFlight"));
            Assert.That(editor, Does.Contain("_pdfSearchResults.Count == 0 && !_pdfSearchInFlight"));
            Assert.That(editor, Does.Contain("_pdfSearchInFlight = true;"));
            Assert.That(editor, Does.Not.Contain("string.Equals(PdfSearchStatusTextBlock?.Text"));

            // Regression pin: a completed run clears the flag in the
            // synchronous tail BEFORE the final empty-state stamp —
            // stamping while the flag is still true suppresses the
            // "No matches" overlay, and the finally's guarded clear runs
            // after the last stamp with nothing left to re-stamp, so a
            // finished 0-hit search would hide the overlay forever.
            Assert.That(resultItems, Is.GreaterThanOrEqualTo(0));
            Assert.That(tailClear, Is.GreaterThan(resultItems),
                "the in-flight flag must be cleared in the completed-path tail");
            Assert.That(finalStamp, Is.GreaterThan(tailClear),
                "the final UpdatePdfSearchEmptyState stamp must run after the tail clear");

            // Both strings exist in every shipped locale (en/zh-Hans/fr).
            Assert.That(l10n, Does.Contain("[\"Editor.SearchNoResults\"]"));
            Assert.That(l10n, Does.Contain("[\"Editor.SearchEmptyHint\"]"));
        });
    }

    [Test]
    public void ThumbnailCardsLiftOnHoverWithReducedMotionFallback()
    {
        string xaml = Read("Pages", "EditorPage.xaml");
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Template hooks + the named shadow surface the handlers lift.
            Assert.That(xaml, Does.Contain("PointerEntered=\"ThumbnailCard_PointerEntered\""));
            Assert.That(xaml, Does.Contain("PointerExited=\"ThumbnailCard_PointerExited\""));
            Assert.That(xaml, Does.Contain("x:Name=\"ThumbnailCardSurface\""));
            Assert.That(xaml, Does.Contain("Loaded=\"ThumbnailImage_Loaded\""));

            // −1 DIP translate + deeper ThemeShadow z, gated on
            // ShouldAnimate; the retained storyboard is stopped before
            // reseeding so recycled rows never keep a stuck lift.
            Assert.That(editor, Does.Contain("private void SetThumbnailCardLifted(FrameworkElement root, bool lifted, bool animate = true)"));
            Assert.That(editor, Does.Contain("lifted ? -1.0 : 0.0"));
            Assert.That(editor, Does.Contain("lifted ? 16f : 4f"));
            Assert.That(editor, Does.Contain("WinUiThemeService.ShouldAnimate"));
            Assert.That(editor, Does.Contain("retained.Stop()"));

            // The template root is named and hit-testable across its full
            // extent (Transparent background covers the card/label gap so
            // Entered/Exited can't bounce-fire), and the Loaded reseed finds
            // it by name instead of a fragile Parent.Parent chain.
            Assert.That(xaml, Does.Contain("x:Name=\"ThumbnailCardRoot\""));
            Assert.That(xaml, Does.Match("x:Name=\"ThumbnailCardRoot\"[^>]*Background=\"Transparent\""));
            Assert.That(editor, Does.Contain("FindAncestorByName(element, \"ThumbnailCardRoot\")"));
            Assert.That(editor, Does.Contain("private static FrameworkElement FindAncestorByName(DependencyObject start, string name)"));
            Assert.That(editor, Does.Not.Contain("(element.Parent as FrameworkElement)?.Parent"));
        });
    }

    [Test]
    public void FloatingOverlaysRefreshImperativeBrushesOnThemeApplied()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        string page = Read("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            // Inline text toolbar + sticky-note editor both carry the card
            // chrome (radius token + z-lifted shadow) — and are re-resolved
            // inside EditorPage_ThemeApplied rather than holding stale
            // brushes until reopen.
            Assert.That(editor, Does.Contain("ResolveThemeCornerRadius(\"ThemeRadiusPill\", 12)"));
            Assert.That(editor, Does.Contain("ResolveThemeCornerRadius(\"ThemeRadiusCard\", 10)"));
            Assert.That(editor, Does.Contain("private void RefreshStickyNoteEditorTheme()"));
            Assert.That(editor, Does.Contain("page.RefreshPageChromeTheme()"));

            // Hidden-but-retained surfaces are dropped too, not just live
            // ones: the floating toolbar and a collapsed ruler both rebuild
            // fresh on next show instead of resurfacing stale brushes.
            Assert.That(editor, Does.Contain("bool toolbarWasVisible = _inlineTextBoxToolbar.Visibility == Visibility.Visible"));
            Assert.That(editor, Does.Contain("else if (!_rulerVisible && _rulerVisual != null)"));

            // Page-level imperative chrome re-stamps on the same sweep —
            // including the painted text-selection/search-hit highlights
            // whose ThemeSelectionBrush fill is resolved once at creation.
            Assert.That(page, Does.Contain("public void RefreshPageChromeTheme()"));
            Assert.That(page, Does.Contain("ResolveAccentStroke()"));
            Assert.That(page, Does.Contain("ResolveAccentTranslucentFill(30)"));
            Assert.That(page, Does.Contain("var selectionBrush = ResolveSelectionBrush();"));
        });
    }

    [Test]
    public void EditorChromeCarriesNoStandaloneVerticalSeparators()
    {
        // T14-C "remove all vertical lines": the toolbar pill, the zoom and
        // page-jump sub-pills, the inline text toolbar and the shell brand
        // cluster paint zero decorative 1-DIP vertical rules — grouping is
        // whitespace (6-DIP leader margins) + the shared pill chrome.
        // Surface outlines (BorderThickness="1" pill/panel edges), the
        // horizontal flyout section rules and MenuFlyoutSeparators stay.
        string editor = Read("Pages", "EditorPage.xaml");
        string editorCode = Read("Pages", "EditorPage.xaml.cs");
        string main = Read("MainWindow.xaml");

        int toolbarStart = editor.IndexOf(
            "<Border x:Name=\"ToolbarBorder\"", StringComparison.Ordinal);
        int toolbarEnd = editor.IndexOf(
            "</Border>", editor.IndexOf("</ScrollViewer>", toolbarStart, StringComparison.Ordinal) + 1,
            StringComparison.Ordinal);
        Assert.That(toolbarStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(toolbarEnd, Is.GreaterThan(toolbarStart));
        string toolbar = editor.Substring(toolbarStart, toolbarEnd - toolbarStart);

        int textToolbarStart = editorCode.IndexOf(
            "private void EnsureInlineTextBoxToolbar()", StringComparison.Ordinal);
        int textToolbarEnd = editorCode.IndexOf(
            "_inlineTextBoxToolbar = border;", textToolbarStart, StringComparison.Ordinal);
        Assert.That(textToolbarStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(textToolbarEnd, Is.GreaterThan(textToolbarStart));
        string textToolbar = editorCode.Substring(
            textToolbarStart, textToolbarEnd - textToolbarStart);

        Assert.Multiple(() =>
        {
            // The separator style + brush are gone file-wide.
            Assert.That(editor, Does.Not.Contain("ToolbarSeparatorStyle"));
            Assert.That(editor, Does.Not.Contain("ThemeMenuSeparatorBrush"));

            // Toolbar + page-jump/zoom pill slices: no 1–3 DIP vertical bars.
            Assert.That(toolbar, Does.Not.Match(@"Width=""[1-3]"""));

            // Former separator sites keep the group segmentation via a
            // 6-DIP leader margin on the three group-leading buttons.
            Assert.That(
                Regex.Matches(toolbar, Regex.Escape("Margin=\"5,0,1,0\"")).Count,
                Is.EqualTo(3), "pen / text / pen-only group gaps");

            // Inline text toolbar (code-created): no 1-DIP separator
            // Borders — regex-anchored so Width = 14/104 don't match.
            Assert.That(textToolbar, Does.Not.Match(@"\bWidth\s*=\s*1\s*,"));

            // Shell chrome: the 3×16 brand accent rail is gone (the only
            // vertical mark the chrome band painted).
            Assert.That(main, Does.Not.Contain("ThemeMarginBrush"));
            Assert.That(main, Does.Not.Match(@"Width=""[1-3]""\s+Height=""16"""));
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
