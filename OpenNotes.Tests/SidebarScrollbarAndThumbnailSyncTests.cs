using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Caelum.Pages;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public sealed class SidebarScrollbarAndThumbnailSyncTests
{
    private static string FindProjectRoot()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "OpenNotes.sln")))
                return current;
            current = Path.GetDirectoryName(current);
        }
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
    }

    [Test]
    public void AppXamlDefinesThinRoundedScrollbarStyles()
    {
        var root = FindProjectRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "App.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(appXaml, Does.Contain("SleekScrollBarThumb"));
            Assert.That(appXaml, Does.Contain("SleekVerticalScrollBar"));
            Assert.That(appXaml, Does.Contain("SleekHorizontalScrollBar"));
            Assert.That(appXaml, Does.Contain("SleekScrollViewer"));
            Assert.That(appXaml, Does.Contain("<Style TargetType=\"ScrollBar\" BasedOn=\"{StaticResource SleekVerticalScrollBar}\">"));
            Assert.That(appXaml, Does.Contain("<Style TargetType=\"ScrollViewer\" BasedOn=\"{StaticResource SleekScrollViewer}\"/>"));
            Assert.That(appXaml, Does.Contain("CornerRadius=\"4\""));
        });
    }

    [Test]
    public void EditorPageSidebarHasDisabledHorizontalScrollbarsAndActiveItemStyle()
    {
        var root = FindProjectRoot();
        var editorXaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var editorCs = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(editorXaml, Does.Contain("x:Name=\"ThumbnailListBox\""));
            Assert.That(editorXaml, Does.Contain("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\""));
            Assert.That(editorXaml, Does.Contain("ThemeSelectionBrush"));
            Assert.That(editorXaml, Does.Contain("ThemeAccentBrush"));
            Assert.That(editorCs, Does.Contain("ScrollThumbnailItemToCenter"));
            Assert.That(editorCs, Does.Contain("UpdateThumbnailSelection"));
        });
    }

    [Test]
    public void ScrollThumbnailItemToCenterMethodExistsOnEditorPage()
    {
        var method = typeof(EditorPage).GetMethod("ScrollThumbnailItemToCenter", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "ScrollThumbnailItemToCenter method should exist on EditorPage.");
    }

    [Test]
    public void TextBoxChromeBackgroundIsAlwaysTransparentInSource()
    {
        var root = FindProjectRoot();
        var editorCs = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));

        int applyChromeIndex = editorCs.IndexOf("private void ApplyTextBoxChrome(", StringComparison.Ordinal);
        Assert.That(applyChromeIndex, Is.GreaterThan(0));
        int methodEndIndex = editorCs.IndexOf("private void", applyChromeIndex + 30, StringComparison.Ordinal);
        if (methodEndIndex < 0) methodEndIndex = editorCs.Length;
        var methodBody = editorCs.Substring(applyChromeIndex, methodEndIndex - applyChromeIndex);

        Assert.Multiple(() =>
        {
            Assert.That(methodBody, Does.Not.Contain("b.SetResourceReference(Border.BackgroundProperty"));
            Assert.That(methodBody, Does.Contain("b.Background = Brushes.Transparent;"));
            Assert.That(methodBody, Does.Contain("textBox.Background = Brushes.Transparent;"));
        });
    }
}
