using System.IO;

namespace Caelum.Tests;

[TestFixture]
public sealed class HomePageLibrarySourceTests
{
    [Test]
    public void MoveSelectionUsesOnScreenFoldersInsteadOfBottomMenu()
    {
        string utilities = Read("Pages", "HomePage.Utilities.cs");
        string page = Read("Pages", "HomePage.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(utilities, Does.Not.Contain("PlacementMode.Top"));
            Assert.That(utilities, Does.Contain("IsChoosingMoveTarget"));
            Assert.That(page, Does.Contain("IsChoosingMoveTarget"));
            Assert.That(utilities, Does.Contain("Home.Selection.Delete"));
            Assert.That(utilities, Does.Contain("TrySendToRecycleBin"));
        });
    }

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
    }
}
