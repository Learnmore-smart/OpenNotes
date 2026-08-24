using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Caelum.Models;
using Caelum.Services;

namespace Caelum.Tests;

[NonParallelizable]
public sealed class AppSettingsCompatibilityTests
{
    private string _dataRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "OpenNotesWave1", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, _dataRoot);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, null);
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Test]
    public void LegacyThreePenPresetsSurviveDeserializeSanitizeCloneAndSave()
    {
        const string legacyJson = """
        {
          "PenPresets": [
            { "Tool": "Pen", "ColorHex": "#112233", "Size": 2.25 },
            { "Tool": "Highlighter", "ColorHex": "#AABBCC", "Size": 8 },
            { "Tool": "Pen", "ColorHex": "#DDEEFF", "Size": 4.5 }
          ]
        }
        """;

        var source = JsonSerializer.Deserialize<AppSettings>(legacyJson)!;
        var before = JsonSerializer.Serialize(source);
        var sanitized = Sanitize(source);

        AssertPresets(sanitized.PenPresets);
        Assert.That(JsonSerializer.Serialize(source), Is.EqualTo(before), "Sanitize must not mutate the caller");

        var saved = AppSettingsService.Save(source);
        var loaded = AppSettingsService.Load();

        AssertPresets(saved.PenPresets);
        AssertPresets(loaded.PenPresets);
        Assert.That(File.Exists(Path.Combine(_dataRoot, ProductInfo.LegacyDataDirectoryName, "settings.json")), Is.True);
    }

    [Test]
    public void MissingOrEmptyPenPresetsRemainEmptyAndDoNotTriggerUiDefaultWrite()
    {
        var missing = JsonSerializer.Deserialize<AppSettings>("{}")!;
        var empty = new AppSettings { PenPresets = new List<PenPreset>() };

        Assert.Multiple(() =>
        {
            Assert.That(Sanitize(missing).PenPresets, Is.Empty);
            Assert.That(Sanitize(empty).PenPresets, Is.Empty);
        });

        string root = FindProjectRoot();
        string editorCode = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        int start = editorCode.IndexOf("private void InitializePenPresetSlots", StringComparison.Ordinal);
        int end = editorCode.IndexOf("private static List<PenPreset> BuildDefaultPenPresets", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        string initializer = editorCode.Substring(start, end - start);
        Assert.That(initializer, Does.Not.Contain("settings.PenPresets = BuildDefaultPenPresets"));
        Assert.That(initializer, Does.Not.Contain("AppSettingsService.Save(settings)"));
    }

    [Test]
    public void SanitizeDeepCopiesPenPresetEntriesAndDoesNotMutateInputList()
    {
        var input = new AppSettings
        {
            PenPresets = new List<PenPreset>
            {
                new() { Tool = "Pen", ColorHex = "#112233", Size = 2 }
            }
        };

        var sanitized = Sanitize(input);
        input.PenPresets[0].ColorHex = "#FFFFFF";
        input.PenPresets.Add(new PenPreset { Tool = "Pen", ColorHex = "#000000", Size = 1 });

        Assert.That(sanitized.PenPresets, Has.Count.EqualTo(1));
        Assert.That(sanitized.PenPresets[0].ColorHex, Is.EqualTo("#112233"));
    }

    [Test]
    public void DataRootOverride_IsEvaluatedPerOperation_NotStaticInitialization()
    {
        var secondRoot = Path.Combine(Path.GetTempPath(), "OpenNotesWave1", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(secondRoot);
        try
        {
            Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, _dataRoot);
            AppSettingsService.Save(new AppSettings
            {
                PenPresets = new List<PenPreset>
                {
                    new() { Tool = "Pen", ColorHex = "#112233", Size = 2 }
                }
            });

            Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, secondRoot);
            AppSettingsService.Save(new AppSettings
            {
                PenPresets = new List<PenPreset>
                {
                    new() { Tool = "Highlighter", ColorHex = "#AABBCC", Size = 8 }
                }
            });

            Assert.That(File.Exists(Path.Combine(_dataRoot, ProductInfo.LegacyDataDirectoryName, "settings.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(secondRoot, ProductInfo.LegacyDataDirectoryName, "settings.json")), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, _dataRoot);
            if (Directory.Exists(secondRoot))
                Directory.Delete(secondRoot, recursive: true);
        }
    }

    private static AppSettings Sanitize(AppSettings settings)
    {
        var method = typeof(AppSettingsService).GetMethod(
            "Sanitize",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(AppSettingsService).FullName, "Sanitize");
        return (AppSettings)method.Invoke(null, new object?[] { settings })!;
    }

    private static void AssertPresets(IReadOnlyList<PenPreset> presets)
    {
        Assert.That(presets, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(presets[0].Tool, Is.EqualTo("Pen"));
            Assert.That(presets[0].ColorHex, Is.EqualTo("#112233"));
            Assert.That(presets[0].Size, Is.EqualTo(2.25));
            Assert.That(presets[1].Tool, Is.EqualTo("Highlighter"));
            Assert.That(presets[1].ColorHex, Is.EqualTo("#AABBCC"));
            Assert.That(presets[1].Size, Is.EqualTo(8));
            Assert.That(presets[2].Tool, Is.EqualTo("Pen"));
            Assert.That(presets[2].ColorHex, Is.EqualTo("#DDEEFF"));
            Assert.That(presets[2].Size, Is.EqualTo(4.5));
        });
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
