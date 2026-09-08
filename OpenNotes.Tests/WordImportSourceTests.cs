using System.IO;

namespace Caelum.Tests;

[TestFixture]
public sealed class WordImportSourceTests
{
    [Test]
    public void HomeAndWindowAcceptWordFilesThenConvertBesideTheOriginal()
    {
        string root = FindProjectRoot();
        string home = Read(root, "Pages", "HomePage.xaml.cs");
        string helper = Read(root, "Pages", "HomePage.DragDropHelper.cs");
        string window = Read(root, "MainWindow.xaml.cs");
        string localization = Read(root, "Services", "LocalizationService.cs");

        Assert.Multiple(() =>
        {
            Assert.That(home, Does.Contain("Home.DocumentFilter"));
            Assert.That(home, Does.Contain("WordToPdfConverter.Default"));
            Assert.That(home, Does.Contain("WordDocumentImport.IsWordPath"));
            Assert.That(helper, Does.Contain("GetDroppedImportablePaths"));
            Assert.That(helper, Does.Contain("WordDocumentImport.IsImportablePath"));
            Assert.That(window, Does.Contain(".docx"));
            Assert.That(window, Does.Contain("WordToPdfConverter.Default"));
            Assert.That(localization, Does.Contain("[\"Home.DocumentFilter\"]"));
            Assert.That(localization, Does.Contain("[\"Home.WordConverterMissing\"]"));
            Assert.That(localization, Does.Contain("[\"Home.WordConvertFailed\"]"));
        });
    }

    private static string Read(string root, params string[] segments)
    {
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
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
