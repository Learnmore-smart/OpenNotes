using System;
using System.IO;
using System.Linq;
using Caelum.Models;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
public sealed class PageTemplatePickerLayoutTests
{
    [Test]
    public void PickerUsesScrollableThreeByThreeGalleryWithNineMappedTemplates()
    {
        string root = FindProjectRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "PageTemplatePickerWindow.xaml"));
        string source = File.ReadAllText(Path.Combine(root, "PageTemplatePickerWindow.xaml.cs"));
        string[] cardNames =
        {
            "BlankCard", "NotebookCard", "LinedCard", "QuadrilleCard", "DottedCard",
            "MusicCard", "CornellCard", "ChecklistCard", "TwoColumnCard"
        };

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Width=\"980\""));
            Assert.That(xaml, Does.Contain("ResizeMode=\"CanResize\""));
            Assert.That(xaml, Does.Contain("x:Name=\"TemplateScrollViewer\""));
            Assert.That(xaml, Does.Contain("VerticalScrollBarVisibility=\"Auto\""));
            Assert.That(xaml, Does.Contain("HorizontalScrollBarVisibility=\"Auto\""));
            Assert.That(xaml, Does.Contain("Columns=\"3\" Rows=\"3\""));
            Assert.That(Enum.GetValues<PageInsertTemplate>(), Has.Length.EqualTo(9));
            foreach (string cardName in cardNames)
            {
                Assert.That(xaml, Does.Contain($"x:Name=\"{cardName}\""));
                Assert.That(source, Does.Contain($"{cardName}.Tag = SelectedTemplate =="));
            }
        });
    }

    private static string FindProjectRoot()
    {
        string? current = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "OpenNotes.csproj")))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate OpenNotes.csproj.");
    }
}
