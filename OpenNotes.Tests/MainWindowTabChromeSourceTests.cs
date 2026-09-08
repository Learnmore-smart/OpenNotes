using System.IO;

namespace Caelum.Tests;

[TestFixture]
public sealed class MainWindowTabChromeSourceTests
{
    [Test]
    public void InactiveTabCloseDoesNotDependOnClickAfterRebuild()
    {
        string source = ReadProjectFile("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("closeBtn.PreviewMouseLeftButtonDown"));
            Assert.That(source, Does.Contain("CloseTab(capturedTab)"));
            Assert.That(source, Does.Contain("Width = 32"));
            Assert.That(source, Does.Contain("Height = 32"));
            Assert.That(source, Does.Contain("RefreshTabBarChrome"));

            int activateStart = source.IndexOf("private void ActivateTab(AppTab tab)", StringComparison.Ordinal);
            int closeStart = source.IndexOf("private async void CloseTab(AppTab tab)", StringComparison.Ordinal);
            Assert.That(activateStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(closeStart, Is.GreaterThan(activateStart));
            string activateBody = source[activateStart..closeStart];
            Assert.That(activateBody, Does.Contain("RefreshTabBarChrome();"));
            Assert.That(activateBody, Does.Not.Contain("RebuildTabBar();"));
        });
    }

    [Test]
    public void TabCloseWorksAfterWritingWithoutMousePromotion()
    {
        string source = ReadProjectFile("MainWindow.xaml.cs");
        int closeStart = source.IndexOf("private async void CloseTab(AppTab tab)", StringComparison.Ordinal);
        Assert.That(closeStart, Is.GreaterThanOrEqualTo(0));
        string closeBody = source[closeStart..];
        int nextMember = closeBody.IndexOf("\r\n        private ", 1, StringComparison.Ordinal);
        if (nextMember < 0)
            nextMember = closeBody.IndexOf("\n        private ", 1, StringComparison.Ordinal);
        if (nextMember > 0)
            closeBody = closeBody[..nextMember];

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("closeBtn.PreviewStylusDown"));
            Assert.That(source, Does.Contain("Stylus.SetIsPressAndHoldEnabled(closeBtn, false)"));
            Assert.That(source, Does.Contain("private void ReleasePointerCapturesForChrome()"));
            Assert.That(source, Does.Contain("Mouse.Capture(null)"));
            Assert.That(source, Does.Contain("Stylus.Capture(null)"));
            Assert.That(closeBody, Does.Contain("ReleasePointerCapturesForChrome();"));
            Assert.That(closeBody, Does.Contain("PrepareForCloseAsync(timeout.Token).WaitAsync(timeout.Token)"));
        });
    }

    [Test]
    public void WindowHasVisibleOutlineAndResizeBorder()
    {
        string xaml = ReadProjectFile("MainWindow.xaml");
        string app = ReadProjectFile("App.xaml");
        string theme = ReadProjectFile("Services", "ThemeService.cs");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Name=\"CloseButton\""));
            Assert.That(xaml, Does.Contain("PreviewStylusDown=\"ChromeButton_PreviewStylusDown\""));
            Assert.That(xaml, Does.Contain("ThemeWindowOutlineBrush"));
            Assert.That(xaml, Does.Contain("ResizeBorderThickness"));
            Assert.That(app, Does.Contain("ThemeWindowOutlineBrush"));
            Assert.That(theme, Does.Contain("ThemeWindowOutlineBrush"));
        });
    }

    private static string ReadProjectFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
    }
}
