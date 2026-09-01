using System.IO;
using System.Text.RegularExpressions;
using Caelum.Models;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class LocalizationCoverageTests
{
    [SetUp]
    public void SetUp()
    {
        LocalizationService.ApplyLanguage(AppLanguage.English);
    }

    [Test]
    public void EveryCatalogEntryHasThreeNonEmptyTranslationsWithMatchingPlaceholders()
    {
        var catalog = LocalizationService.GetCatalog();

        Assert.That(catalog, Is.Not.Empty);
        foreach (var entry in catalog)
        {
            Assert.Multiple(() =>
            {
                Assert.That(entry.Key, Is.Not.Empty);
                Assert.That(entry.Value.English, Is.Not.Empty, entry.Key);
                Assert.That(entry.Value.Chinese, Is.Not.Empty, entry.Key);
                Assert.That(entry.Value.French, Is.Not.Empty, entry.Key);
                Assert.That(Placeholders(entry.Value.English), Is.EqualTo(Placeholders(entry.Value.Chinese)), entry.Key);
                Assert.That(Placeholders(entry.Value.English), Is.EqualTo(Placeholders(entry.Value.French)), entry.Key);
            });
        }
    }

    [Test]
    public void DashedLineHasTheRequestedThreeLanguageLabel()
    {
        var entry = LocalizationService.GetCatalog()["Editor.ShapeDashedLine"];
        Assert.Multiple(() =>
        {
            Assert.That(entry.English, Is.EqualTo("Dashed line"));
            Assert.That(entry.Chinese, Is.EqualTo("虚线"));
            Assert.That(entry.French, Is.EqualTo("Ligne pointillée"));
        });
    }

    [Test]
    public void UpdateCheckHasCompleteThreeLanguageCopyWithMatchingPlaceholders()
    {
        var catalog = LocalizationService.GetCatalog();
        var expected = new Dictionary<string, (string English, string Chinese, string French)>
        {
            ["Main.CheckForUpdates"] = ("Check for updates", "检查更新", "Rechercher des mises à jour"),
            ["Main.CheckingForUpdates"] = ("Checking for updates…", "正在检查更新…", "Recherche de mises à jour…"),
            ["Main.UpdateAvailableTitle"] = ("Update available", "有可用更新", "Mise à jour disponible"),
            ["Main.UpdateAvailableMessage"] = (
                "Version {1} is available. You are using {0}.",
                "新版本 {1} 可用。当前版本：{0}。",
                "La version {1} est disponible. Vous utilisez la version {0}."),
            ["Main.UpToDateTitle"] = ("You're up to date", "已是最新版本", "OpenNotes est à jour"),
            ["Main.UpToDateMessage"] = (
                "You're using the latest version ({0}).",
                "当前已是最新版本（{0}）。",
                "Vous utilisez la dernière version ({0})."),
            ["Main.UpdateCheckFailedTitle"] = (
                "Couldn't check for updates",
                "无法检查更新",
                "Impossible de rechercher les mises à jour"),
            ["Main.UpdateCheckFailedMessage"] = (
                "Check your internet connection and try again.",
                "请检查网络连接后重试。",
                "Vérifiez votre connexion Internet et réessayez."),
            ["Main.ViewRelease"] = ("View release", "查看更新", "Voir la version")
        };

        foreach (var entry in expected)
        {
            Assert.That(catalog.ContainsKey(entry.Key), Is.True, entry.Key);
            Assert.That(catalog[entry.Key], Is.EqualTo(entry.Value), entry.Key);
        }
    }

    [Test]
    public void ApplyingLanguageRaisesLanguageChanged()
    {
        int notifications = 0;
        EventHandler handler = (_, _) => notifications++;
        LocalizationService.LanguageChanged += handler;

        try
        {
            LocalizationService.ApplyLanguage(AppLanguage.Chinese);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(LocalizationService.Get("Main.AboutMessage"), Does.Contain("OpenNotes"));
        }
        finally
        {
            LocalizationService.LanguageChanged -= handler;
        }
    }

    [Test]
    public void ApplyingTheCurrentLanguageDoesNotPublishASecondRefresh()
    {
        int notifications = 0;
        EventHandler handler = (_, _) => notifications++;
        LocalizationService.LanguageChanged += handler;

        try
        {
            LocalizationService.ApplyLanguage(AppLanguage.English);
            Assert.That(notifications, Is.Zero);
        }
        finally
        {
            LocalizationService.LanguageChanged -= handler;
        }
    }

    [Test]
    public void UnknownKeysFailLoudlyInsteadOfRenderingTheKey()
    {
        Assert.That(() => LocalizationService.Get("Missing.Key"), Throws.TypeOf<KeyNotFoundException>());
    }

    [Test]
    public void ToolbarObsoleteEntriesAreNotExposedByAnyLanguageCatalog()
    {
        var catalog = LocalizationService.GetCatalog();

        Assert.Multiple(() =>
        {
            Assert.That(catalog.ContainsKey("Editor.InkAnalysisUnavailable"), Is.False);
            Assert.That(catalog.ContainsKey("Editor.InkAnalysisTooltip"), Is.False);
            Assert.That(catalog.ContainsKey("Editor.FitWidthTooltip"), Is.False);
            Assert.That(catalog.ContainsKey("Editor.FitPageTooltip"), Is.False);
            Assert.That(catalog.ContainsKey("Editor.PresetTooltip"), Is.False);
            Assert.That(catalog.ContainsKey("Editor.PresetClickApply"), Is.False);
            Assert.That(catalog.ContainsKey("Editor.PresetRightClickSave"), Is.False);
        });
    }

    [Test]
    public void OpenPagesRefreshLocalizationThroughTheirLoadedLifecycle()
    {
        var projectRoot = FindProjectRoot();
        var editorSource = File.ReadAllText(Path.Combine(projectRoot, "Pages", "EditorPage.xaml.cs")).Replace("\r\n", "\n");
        var editorUtilitiesSource = File.ReadAllText(Path.Combine(projectRoot, "Pages", "EditorPage.Utilities.cs")).Replace("\r\n", "\n");
        var homeSource = File.ReadAllText(Path.Combine(projectRoot, "Pages", "HomePage.xaml.cs")).Replace("\r\n", "\n");

        Assert.Multiple(() =>
        {
            Assert.That(editorSource, Does.Contain("LocalizationService.LanguageChanged += EditorPage_LanguageChanged;"));
            Assert.That(editorSource, Does.Contain("LocalizationService.LanguageChanged -= EditorPage_LanguageChanged;"));
            Assert.That(editorSource, Does.Contain("private void EditorPage_LanguageChanged"));
            Assert.That(editorSource, Does.Contain("ApplyLocalization();\n            InitializePenService();"));
            Assert.That(editorUtilitiesSource, Does.Contain("RefreshTextBoxToolbarLocalization"));
            Assert.That(editorUtilitiesSource, Does.Contain("RefreshTextResizeHandleTooltips"));
            Assert.That(editorUtilitiesSource, Does.Contain("RefreshLocalizedDocumentSidebar"));
            Assert.That(homeSource, Does.Contain("LocalizationService.LanguageChanged += HomePage_LanguageChanged;"));
            Assert.That(homeSource, Does.Contain("LocalizationService.LanguageChanged -= HomePage_LanguageChanged;"));
            Assert.That(homeSource, Does.Contain("private void HomePage_LanguageChanged"));
            Assert.That(homeSource, Does.Contain("ApplyLocalization();\n            await EnsureLibraryLoadedAsync();"));
        });
    }

    [Test]
    public void PerformanceSettingsAreLocalizedAndPreserved()
    {
        var catalog = LocalizationService.GetCatalog();
        var projectRoot = FindProjectRoot();
        var settingsXaml = File.ReadAllText(Path.Combine(projectRoot, "SettingsWindow.xaml"));
        var settingsSource = File.ReadAllText(Path.Combine(projectRoot, "SettingsWindow.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(catalog.ContainsKey("Settings.Performance"), Is.True);
            Assert.That(catalog.ContainsKey("Settings.PerformanceBatterySaver"), Is.True);
            Assert.That(catalog.ContainsKey("Settings.PerformanceBalanced"), Is.True);
            Assert.That(catalog.ContainsKey("Settings.PerformanceBestQuality"), Is.True);
            Assert.That(settingsXaml, Does.Contain("x:Name=\"PerformanceModeComboBox\""));
            Assert.That(settingsSource, Does.Contain("selected.PerformanceMode = GetPerformanceModeValue"));
            Assert.That(settingsSource, Does.Contain("PerformanceMode = source.PerformanceMode"));
        });
    }

    private static IReadOnlyList<string> Placeholders(string value)
    {
        return Regex.Matches(value ?? string.Empty, @"\{\d+\}")
            .Select(match => match.Value)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
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
