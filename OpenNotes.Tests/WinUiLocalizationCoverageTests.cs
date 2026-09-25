using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Caelum.Models;
using Caelum.Services;

namespace Caelum.Tests;

/// <summary>
/// Task 9 Phase B localization contract: every literal
/// <c>LocalizationService.Get/Format/GetForLanguage("Key")</c> key referenced
/// by the WinUI shell must resolve in the shared catalog — the catalog-level
/// test (<see cref="LocalizationCoverageTests"/>) proves each entry carries
/// three translations; this test proves no WinUI call site names a missing
/// key, in all three shipped languages.
/// </summary>
[TestFixture]
public sealed class WinUiLocalizationCoverageTests
{
    private static readonly Regex LiteralKeyCall = new(
        @"LocalizationService\.(?:Get|Format|GetForLanguage)\(\s*""([A-Za-z0-9_.]+)""",
        RegexOptions.Compiled);

    [Test]
    public void EveryLiteralLocalizationKeyUsedByWinUIResolvesInAllThreeLanguages()
    {
        var winUiRoot = Path.Combine(ProjectRoot(), "OpenNotes.WinUI");
        var keys = new SortedSet<string>(System.StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(winUiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match match in LiteralKeyCall.Matches(File.ReadAllText(file)))
                keys.Add(match.Groups[1].Value);
        }

        Assert.That(keys.Count, Is.GreaterThan(100),
            "The WinUI shell should reference well over a hundred literal localization keys");

        var catalog = LocalizationService.GetCatalog();
        foreach (var key in keys)
        {
            Assert.That(catalog.ContainsKey(key), Is.True,
                $"WinUI references '{key}' but the catalog has no entry for it");
            Assert.Multiple(() =>
            {
                Assert.That(LocalizationService.GetForLanguage(key, AppLanguage.English),
                    Is.Not.Null.And.Not.Empty, key);
                Assert.That(LocalizationService.GetForLanguage(key, AppLanguage.Chinese),
                    Is.Not.Null.And.Not.Empty, key);
                Assert.That(LocalizationService.GetForLanguage(key, AppLanguage.French),
                    Is.Not.Null.And.Not.Empty, key);
            });
        }
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
