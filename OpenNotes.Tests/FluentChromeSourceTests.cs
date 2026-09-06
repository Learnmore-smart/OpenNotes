using System.IO;

namespace Caelum.Tests;

[TestFixture]
public sealed class FluentChromeSourceTests
{
    [Test]
    public void TitleBarToolbarLoadingAndSearchUseWindows11Chrome()
    {
        string mainXaml = Read("MainWindow.xaml");
        string mainCode = Read("MainWindow.xaml.cs");
        string editorXaml = Read("Pages", "EditorPage.xaml");
        string appXaml = Read("App.xaml");
        string homeXaml = Read("Pages", "HomePage.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(mainXaml, Does.Contain("#C42B1C"));
            Assert.That(mainCode, Does.Contain("DwmSetWindowAttribute(handle, 33"));
            Assert.That(mainCode, Does.Contain("Icon = \"Home\""));
            Assert.That(mainCode, Does.Contain("\"FileText\""));
            Assert.That(mainCode, Does.Not.Contain("\\uE80F"));
            Assert.That(editorXaml, Does.Contain("VerticalAlignment=\"Center\""));
            Assert.That(editorXaml, Does.Contain("SnapsToDevicePixels"));
            Assert.That(editorXaml, Does.Not.Contain("Opacity=\"0.92\""));
            Assert.That(editorXaml, Does.Contain("Style=\"{StaticResource ModernTextBox}\""));
            Assert.That(appXaml, Does.Contain("x:Key=\"ModernTextBox\""));
            Assert.That(appXaml, Does.Contain("x:Key=\"ModernListBox\""));
            Assert.That(homeXaml, Does.Not.Contain("#B88E75"));
            Assert.That(homeXaml, Does.Not.Contain("#D94848"));
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
