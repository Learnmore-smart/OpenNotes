using System.IO;
using System.Linq;

namespace Caelum.Tests;

/// <summary>
/// Task 9 Phase B source contract — pins the WinUI dialog/service parity so
/// a later refactor cannot silently drop the pieces the WPF shell relies on:
/// the shared dialog gate + XamlRoot requirement, the settings dialog's
/// stage/preview/revert contract, the template picker's two modes, the
/// editor's version-history restore sequence and structural page
/// operations, and the MainWindow settings entry point.
/// </summary>
[TestFixture]
public sealed class WinUiDialogsServicesSourceTests
{
    [Test]
    public void DialogServiceSerializesEveryContentDialogBehindOneGate()
    {
        string service = Read("Services", "WinUiDialogService.cs");

        Assert.Multiple(() =>
        {
            Assert.That(service, Does.Contain("private static readonly SemaphoreSlim DialogGate"));
            Assert.That(service, Does.Contain("public static async Task ShowInfoAsync("));
            Assert.That(service, Does.Contain("public static async Task ShowErrorAsync("));
            Assert.That(service, Does.Contain("public static async Task<bool?> ShowDialogAsync("));
            Assert.That(service, Does.Contain("public static async Task<bool?> ShowDangerConfirmAsync("));
            Assert.That(service, Does.Contain("internal static async Task<T> RunUnderDialogGateAsync<T>"));
            Assert.That(service, Does.Contain("await DialogGate.WaitAsync();"));
            Assert.That(service, Does.Contain("DialogGate.Release();"));
            // Explicit XamlRoot contract: a null root is a safe no-op, not a
            // crash (unpackaged WinUI has no implicit dialog host).
            Assert.That(service, Does.Contain("if (xamlRoot == null)"));
            Assert.That(service, Does.Contain("XamlRoot = xamlRoot"));
            // WPF parity: confirm → true, cancel/dismiss → false.
            Assert.That(service, Does.Contain("return result == ContentDialogResult.Primary;"));
            // The popup z-order no-port note documents the WPF
            // PopupZOrderHelper → XamlRoot-scoped flyout/dialog mapping.
            Assert.That(service, Does.Contain("PopupZOrderHelper"));
            Assert.That(service, Does.Contain("HWND_TOPMOST"));
        });
    }

    [Test]
    public void SettingsDialogStagesPreviewsAndRevertsOnDismiss()
    {
        string dialog = Read("SettingsDialog.cs");

        Assert.Multiple(() =>
        {
            Assert.That(dialog, Does.Contain("public sealed class SettingsDialog : ContentDialog"));
            Assert.That(dialog, Does.Contain("public SettingsDialog(AppSettings currentSettings)"));
            Assert.That(dialog, Does.Contain("public AppSettings SelectedSettings { get; private set; }"));
            Assert.That(dialog, Does.Contain("private static AppSettings CloneSettings(AppSettings source)"));
            // Live preview through the owner callback (WPF owner-call parity).
            Assert.That(dialog, Does.Contain("MainWindow.Current?.PreviewSettings("));
            // Cancel/Esc/light-dismiss all revert — the revert lives in
            // Closed because ContentDialogs can close without a button.
            Assert.That(dialog, Does.Contain("Closed +="));
            Assert.That(dialog, Does.Contain("if (!_confirmed)"));
            Assert.That(dialog, Does.Contain("PreviewSettings(_originalSettings)"));
            // Live language/theme re-localization + repaint subscriptions
            // are released on close.
            Assert.That(dialog, Does.Contain("LocalizationService.LanguageChanged += OnLanguageChanged"));
            Assert.That(dialog, Does.Contain("LocalizationService.LanguageChanged -= OnLanguageChanged"));
            Assert.That(dialog, Does.Contain("WinUiThemeService.ThemeApplied += OnThemeApplied"));
            Assert.That(dialog, Does.Contain("WinUiThemeService.ThemeApplied -= OnThemeApplied"));
            // Every WPF-exposed setting row exists (language, autosave,
            // pressure, pen-only, smoothing, theme, performance, backdrop).
            Assert.That(dialog, Does.Contain("_languageComboBox"));
            Assert.That(dialog, Does.Contain("_autoSaveIntervalComboBox"));
            Assert.That(dialog, Does.Contain("_pressureSwitch"));
            Assert.That(dialog, Does.Contain("_penOnlySwitch"));
            Assert.That(dialog, Does.Contain("_smoothingComboBox"));
            Assert.That(dialog, Does.Contain("_themeComboBox"));
            Assert.That(dialog, Does.Contain("_performanceModeComboBox"));
            Assert.That(dialog, Does.Contain("_workspaceBackdropComboBox"));
            // Unexposed fields are carried through the clone, never dropped.
            Assert.That(dialog, Does.Contain("WholeStrokeEraser = source.WholeStrokeEraser"));
            Assert.That(dialog, Does.Contain("PenPresets = "));
            Assert.That(dialog, Does.Contain("RecentPenColors = "));
        });
    }

    [Test]
    public void PageTemplatePickerCoversBothModesBehindTheGate()
    {
        string dialog = Read("Controls", "PageTemplatePickerDialog.cs");

        Assert.Multiple(() =>
        {
            Assert.That(dialog, Does.Contain("public sealed class PageTemplatePickerDialog : ContentDialog"));
            Assert.That(dialog, Does.Contain("public PageTemplatePickerDialog()"));
            Assert.That(dialog, Does.Contain("public PageTemplatePickerDialog(string initialFolderPath)"));
            Assert.That(dialog, Does.Contain("public PageInsertTemplate SelectedTemplate"));
            Assert.That(dialog, Does.Contain("public string SelectedFolderPath { get; private set; }"));
            Assert.That(dialog, Does.Contain("public bool IsConfirmed { get; private set; }"));
            // All nine WPF templates in display order.
            foreach (var name in new[]
                { "Blank", "Notebook", "Lined", "Quadrille", "Dotted",
                  "Music", "Cornell", "Checklist", "TwoColumn" })
            {
                Assert.That(dialog, Does.Contain($"PageInsertTemplate.{name}"), name);
            }
            // Insert mode: a card click confirms + closes.
            Assert.That(dialog, Does.Contain("IsConfirmed = true;"));
            Assert.That(dialog, Does.Contain("Hide();"));
            // Notebook mode: Create stays disabled until a folder exists.
            Assert.That(dialog, Does.Contain(
                "IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(SelectedFolderPath)"));
            // Theme/language subscriptions released on close.
            Assert.That(dialog, Does.Contain("WinUiThemeService.ThemeApplied -= OnThemeApplied"));
            Assert.That(dialog, Does.Contain("LocalizationService.LanguageChanged -= OnLanguageChanged"));
        });

        // Both call sites serialize the dialog through the shared gate.
        string editor = Read("Pages", "EditorPage.xaml.cs");
        string home = Read("Pages", "HomePage.xaml.cs");
        Assert.Multiple(() =>
        {
            Assert.That(editor, Does.Contain(
                "await WinUiDialogService.RunUnderDialogGateAsync("));
            Assert.That(editor, Does.Contain("new PageTemplatePickerDialog { XamlRoot = xamlRoot }"));
            Assert.That(home, Does.Contain(
                "new PageTemplatePickerDialog(GetDefaultNotebookDirectory())"));
            Assert.That(home, Does.Contain(
                "await WinUiDialogService.RunUnderDialogGateAsync(() => dialog.ShowAsync().AsTask())"));
        });
    }

    [Test]
    public void EditorPageCarriesTheVersionHistoryRestoreSequence()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        string xaml = Read("Pages", "EditorPage.xaml");

        Assert.Multiple(() =>
        {
            // Toolbar wiring: the button is enabled and bound.
            Assert.That(xaml, Does.Contain(
                "x:Name=\"VersionHistoryButton\" Click=\"VersionHistory_Click\""));
            int buttonStart = xaml.IndexOf("x:Name=\"VersionHistoryButton\"", System.StringComparison.Ordinal);
            string tag = xaml.Substring(
                xaml.LastIndexOf("<Button", buttonStart, System.StringComparison.Ordinal),
                xaml.IndexOf('>', buttonStart) - xaml.LastIndexOf("<Button", buttonStart, System.StringComparison.Ordinal));
            Assert.That(tag, Does.Not.Contain("IsEnabled=\"False\""));

            Assert.That(editor, Does.Contain("private void VersionHistory_Click(object sender, RoutedEventArgs e)"));
            // Session/path capture so a mid-menu document swap can't act on
            // the replacement document.
            Assert.That(editor, Does.Contain("int menuSessionId = _loadSessionId;"));
            Assert.That(editor, Does.Contain("string menuPath = _currentPdfPath;"));
            Assert.That(editor, Does.Contain("VersionControlService.GetVersions(_currentPdfPath)"));
            Assert.That(editor, Does.Contain("Editor.NoVersionHistory"));
            // Restore sequence: snapshot current first (reversible) → sweep
            // every layer → clear the undo ledger → repaint → mark dirty.
            Assert.That(editor, Does.Contain("VersionControlService.LoadVersionAsync("));
            Assert.That(editor, Does.Contain("VersionControlService.SaveVersionAsync("));
            Assert.That(editor, Does.Contain("ClearAllAnnotations();"));
            Assert.That(editor, Does.Contain("ClearUndoRedoHistory();"));
            Assert.That(editor, Does.Contain("_pdfService.ExtractedAnnotations = data;"));
            Assert.That(editor, Does.Contain("await LoadAnnotationsIntoPagesAsync();"));
            Assert.That(editor, Does.Contain("MarkDirty();"));
            Assert.That(editor, Does.Contain("Editor.RestoredVersion"));
            Assert.That(editor, Does.Contain("Editor.VersionLoadFailed"));
            // The flyout registers with transient-UI cleanup.
            Assert.That(editor, Does.Contain("_transientFlyout = flyout;"));
            Assert.That(editor, Does.Contain("flyout.ShowAt(VersionHistoryButton);"));
            // Per-page sweep (WPF ClearAllAnnotations parity).
            Assert.That(editor, Does.Contain("private void ClearAllAnnotations()"));
        });

        string page = Read("Controls", "PdfPageControl.xaml.cs");
        Assert.That(page, Does.Contain("public void ClearAllAnnotations()"));
    }

    [Test]
    public void EditorPageCarriesLeaseGuardedStructuralPageOperations()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(editor, Does.Contain(
                "private async Task InsertPageAtAsync("));
            Assert.That(editor, Does.Contain(
                "private async Task DeletePageAtAsync("));
            // Dirty flush precedes the binary rewrite (stale-base guard).
            Assert.That(editor, Does.Contain("_documentSaveCoordinator.IsDirty"));
            Assert.That(editor, Does.Contain("await AutoSaveAsync(currentLease)"));
            Assert.That(editor, Does.Contain("await _pdfService.InsertPageAsync(filePath, insertIndex, picker.SelectedTemplate)"));
            Assert.That(editor, Does.Contain("await _pdfService.DeletePageAsync(filePath, pageIndex)"));
            Assert.That(editor, Does.Contain("ReloadDocumentForOperationAsync(filePath, currentLease)"));
            Assert.That(editor, Does.Contain("new DocumentSnapshotAction("));
            Assert.That(editor, Does.Contain("ApplyPageInsert(filePath, insertedPageIndex)"));
            Assert.That(editor, Does.Contain("ApplyPageDelete(filePath, pageIndex)"));
            // Single-page documents keep their last page (WPF blocked toast).
            Assert.That(editor, Does.Contain("_pageControls.Count <= 1"));
            Assert.That(editor, Does.Contain("Editor.PageDeleteBlocked"));
            // Hover chrome + insert gaps are runtime-built (WPF parity).
            Assert.That(editor, Does.Contain("private FrameworkElement CreatePageInsertGap(int insertIndex)"));
            Assert.That(editor, Does.Contain("private FrameworkElement CreatePageHost(PdfPageControl pageControl)"));
        });
    }

    [Test]
    public void MainWindowWiresSettingsThroughPreviewApplyAndTheGate()
    {
        string window = Read("MainWindow.xaml.cs");
        string xaml = Read("MainWindow.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain(
                "x:Name=\"SettingsMenuItem\" Click=\"Settings_Click\""));
            Assert.That(xaml, Does.Not.Contain(
                "x:Name=\"SettingsMenuItem\" IsEnabled=\"False\""));

            Assert.That(window, Does.Contain("private async void Settings_Click("));
            Assert.That(window, Does.Contain("public void PreviewSettings(AppSettings settings)"));
            Assert.That(window, Does.Contain("private void ApplySettings(AppSettings settings)"));
            Assert.That(window, Does.Contain("private async Task OpenSettingsDialogAsync()"));
            // WPF PreviewSettings parity: language + theme/backdrop + every
            // open editor.
            Assert.That(window, Does.Contain("LocalizationService.ApplyLanguage(settings.Language)"));
            Assert.That(window, Does.Contain(
                "WinUiThemeService.Apply(settings.Theme, workspaceBackdrop: settings.WorkspaceBackdrop)"));
            Assert.That(window, Does.Contain("editor.ApplySettings(settings);"));
            // Persist → preview on confirm; cancel reverts the preview.
            Assert.That(window, Does.Contain("AppSettingsService.Save(settings)"));
            Assert.That(window, Does.Contain("new SettingsDialog(originalSettings) { XamlRoot = xamlRoot }"));
            Assert.That(window, Does.Contain(
                "await WinUiDialogService.RunUnderDialogGateAsync("));
            Assert.That(window, Does.Contain("Main.SettingsSaved"));
            Assert.That(window, Does.Contain("PreviewSettings(originalSettings);"));
        });
    }

    [Test]
    public void EditorApplySettingsCarriesThePerformanceModeReRenderBranch()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(editor, Does.Contain("public void ApplySettings(AppSettings settings)"));
            Assert.That(editor, Does.Contain("string previousPerformanceMode = CurrentPerformanceMode;"));
            Assert.That(editor, Does.Contain("_pagesInitiallyRendered.Clear();"));
            Assert.That(editor, Does.Contain("_pagesRenderedAtScale.Clear();"));
            Assert.That(editor, Does.Contain("TrimPageBitmapWorkingSet(visiblePages);"));
            Assert.That(editor, Does.Contain("KickViewportRender();"));
        });
    }

    private static string Read(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(
            new[] { Path.Combine(ProjectRoot(), "OpenNotes.WinUI") }.Concat(segments).ToArray()))
            .Replace("\r\n", "\n");
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
