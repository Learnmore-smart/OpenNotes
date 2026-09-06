using System.IO;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class RecentFilesServiceFolderColorTests
{
    private string _dataRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "OpenNotesFolderColor_" + Guid.NewGuid().ToString("N"));
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
    public void FolderColorRoundTripsAndLegacyEntriesDefaultEmpty()
    {
        var folder = RecentFilesService.CreateFolder("Lecture");
        Assert.That(folder, Is.Not.Null);
        Assert.That(folder!.Color, Is.Null.Or.Empty);

        Assert.That(RecentFilesService.SetFolderColor(folder.Id, "#2563EB"), Is.True);
        var loaded = RecentFilesService.GetFolder(folder.Id);
        Assert.That(loaded?.Color, Is.EqualTo("#2563EB"));
    }
}
