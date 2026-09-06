using System.IO;
using System.Text.Json;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class RecentFilesServiceLibrarySurvivalTests
{
    private string _dataRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "OpenNotesLibrarySurvival_" + Guid.NewGuid().ToString("N"));
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
    public void GetLibraryEntriesKeepsMissingPdfInsteadOfPruningTheIndex()
    {
        string missing = Path.Combine(_dataRoot, "lecture-notes.pdf");
        RecentFilesService.AddOrPromote(missing, pageCount: 3, lastModifiedUtc: DateTime.UtcNow);

        var entries = RecentFilesService.GetLibraryEntries();
        Assert.That(entries.Any(entry =>
            entry.IsFile &&
            string.Equals(entry.Path, Path.GetFullPath(missing), StringComparison.OrdinalIgnoreCase)));

        string json = File.ReadAllText(Path.Combine(ProductInfo.GetDataDirectory(), "recent_files.json"));
        Assert.That(json, Does.Contain("lecture-notes.pdf"));
    }

    [Test]
    public void EmptyIndexRestoresFileEntriesFromBackup()
    {
        string pdf = Path.Combine(_dataRoot, "kept.pdf");
        File.WriteAllBytes(pdf, new byte[] { 1, 2, 3 });
        var backupEntries = new[]
        {
            new RecentFileEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                EntryType = "file",
                Path = Path.GetFullPath(pdf),
                LastOpenedUtc = DateTime.UtcNow
            }
        };

        string dataDir = ProductInfo.GetDataDirectory();
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(
            Path.Combine(dataDir, "recent_files.json.bak"),
            JsonSerializer.Serialize(backupEntries));
        File.WriteAllText(Path.Combine(dataDir, "recent_files.json"), "[]");

        var restored = RecentFilesService.GetLibraryEntries();
        Assert.That(restored.Any(entry =>
            entry.IsFile &&
            string.Equals(entry.Path, Path.GetFullPath(pdf), StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void EmptyIndexRestoresExistingPdfPathsFromBookmarks()
    {
        string pdf = Path.Combine(_dataRoot, "bookmarked.pdf");
        File.WriteAllBytes(pdf, new byte[] { 1, 2, 3, 4 });
        string dataDir = ProductInfo.GetDataDirectory();
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "recent_files.json"), "[]");
        string bookmarks = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [Path.GetFullPath(pdf)] = new[] { new { PageIndex = 0, Label = "Intro" } }
        });
        File.WriteAllText(Path.Combine(dataDir, "bookmarks.json"), bookmarks);

        var restored = RecentFilesService.GetLibraryEntries();
        Assert.That(restored.Any(entry =>
            entry.IsFile &&
            string.Equals(entry.Path, Path.GetFullPath(pdf), StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void GetLibraryDisplayNameFallsBackToPdfFileName()
    {
        var entry = new RecentFileEntry
        {
            EntryType = "file",
            DisplayName = string.Empty,
            Path = @"C:\Notes\Midterm.pdf"
        };

        Assert.That(RecentFilesService.GetLibraryDisplayName(entry), Is.EqualTo("Midterm.pdf"));
    }
}
