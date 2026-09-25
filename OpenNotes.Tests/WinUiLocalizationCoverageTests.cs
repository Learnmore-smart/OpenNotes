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
    /// <summary>
    /// Literal-first-argument call sites only. Limits: the key must be the
    /// FIRST argument and written as a plain <c>[A-Za-z0-9_.]+</c> literal —
    /// ternaries/variables are covered by the second pass below instead.
    /// </summary>
    private static readonly Regex LiteralKeyCall = new(
        @"LocalizationService\.(?:Get|Format|GetForLanguage)\(\s*""([A-Za-z0-9_.]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Call-site probe for the second pass: keys that reach the catalog
    /// through a computed argument — e.g. the picker's mode ternary
    /// <c>Get(mode ? "Home.X" : "Editor.Y")</c> — are invisible to
    /// <see cref="LiteralKeyCall"/>. For each call we scan its balanced-paren
    /// argument span for DOTTED key-shaped literals (<c>Section.Name</c>).
    /// Limits: literals only — interpolated/concatenated key names and
    /// non-dotted literals are out of scope; a dotted non-key literal inside
    /// an argument expression (none exist today — format args like
    /// <c>ToString("g")</c> have no dot) would false-positive.
    /// </summary>
    private static readonly Regex CallSiteStart = new(
        @"LocalizationService\.(?:Get|Format|GetForLanguage)\(",
        RegexOptions.Compiled);

    private static readonly Regex DottedKeyLiteral = new(
        @"""([A-Za-z0-9_]+\.[A-Za-z0-9_.]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Key lookup tables: <c>PageTemplatePickerDialog.TemplateOptions</c>
    /// stores keys as tuple literals consumed later via
    /// <c>Get(titleKey)</c>/<c>Get(hintKey)</c> — indirection no call-site
    /// regex can see, so the tuple shape is scanned directly.
    /// </summary>
    private static readonly Regex TemplateOptionTuple = new(
        @"\(\s*PageInsertTemplate\.[A-Za-z0-9_]+\s*,\s*""([A-Za-z0-9_.]+)""\s*,\s*""([A-Za-z0-9_.]+)""\s*\)",
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
            string source = File.ReadAllText(file);
            foreach (Match match in LiteralKeyCall.Matches(source))
                keys.Add(match.Groups[1].Value);

            // Second pass: dotted key literals inside the call's balanced
            // argument expression (ternary operands etc.).
            foreach (Match call in CallSiteStart.Matches(source))
            {
                var (start, end) = ArgumentSpan(source, call.Index + call.Length - 1);
                foreach (Match literal in DottedKeyLiteral.Matches(source.Substring(start, end - start)))
                    keys.Add(literal.Groups[1].Value);
            }

            // Third pass: key-table tuples (TemplateOptions TitleKey/HintKey).
            foreach (Match tuple in TemplateOptionTuple.Matches(source))
            {
                keys.Add(tuple.Groups[1].Value);
                keys.Add(tuple.Groups[2].Value);
            }
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

    /// <summary>
    /// Returns the (start, end) content span inside the parentheses whose
    /// open paren sits at <paramref name="openParen"/> — tracking nesting
    /// depth and skipping over string literals so a ")" inside an argument
    /// string or a nested call doesn't end the span early.
    /// </summary>
    private static (int Start, int End) ArgumentSpan(string source, int openParen)
    {
        int depth = 0;
        int i = openParen;
        int start = i + 1;
        while (i < source.Length)
        {
            char ch = source[i];
            if (ch == '"')
            {
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }
                    if (source[i] == '"')
                        break;
                    i++;
                }
            }
            else if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth == 0)
                    return (start, i);
            }
            i++;
        }
        return (start, source.Length);
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
