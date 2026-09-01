using System.IO;

namespace Caelum.Tests;

[TestFixture]
public sealed class MainWindowUpdateCheckSourceTests
{
    [Test]
    public void MoreMenuOrdersAndLocalizesTheManualUpdateCommand()
    {
        string root = FindProjectRoot();
        string xaml = Normalize(File.ReadAllText(Path.Combine(root, "MainWindow.xaml")));
        string utilities = Normalize(File.ReadAllText(Path.Combine(root, "MainWindow.Utilities.cs")));

        int settings = xaml.IndexOf("x:Name=\"SettingsMenuItem\"", StringComparison.Ordinal);
        int updates = xaml.IndexOf("x:Name=\"CheckForUpdatesMenuItem\"", StringComparison.Ordinal);
        int about = xaml.IndexOf("x:Name=\"AboutMenuItem\"", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(settings, Is.GreaterThanOrEqualTo(0));
            Assert.That(updates, Is.GreaterThan(settings));
            Assert.That(about, Is.GreaterThan(updates));
            Assert.That(xaml, Does.Contain(
                "x:Name=\"CheckForUpdatesMenuItem\" Click=\"CheckForUpdates_Click\""));
            Assert.That(utilities, Does.Contain(
                "CheckForUpdatesMenuItem.Header = LocalizationService.Get("));
            Assert.That(utilities, Does.Contain("\"Main.CheckForUpdates\""));
            Assert.That(utilities, Does.Contain("\"Main.CheckingForUpdates\""));
        });
    }

    [Test]
    public void UpdateCommandRestoresBusyStateAndCancelsWithTheWindow()
    {
        string root = FindProjectRoot();
        string source = Normalize(File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs")));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private async void CheckForUpdates_Click"));
            Assert.That(source, Does.Contain("_isUpdateCheckInProgress"));
            Assert.That(source, Does.Contain("CheckForUpdatesMenuItem.IsEnabled = false"));
            Assert.That(source, Does.Contain("finally"));
            Assert.That(source, Does.Contain("CheckForUpdatesMenuItem.IsEnabled = true"));
            Assert.That(source, Does.Contain("_updateCheckCts?.Cancel();"));
            Assert.That(source, Does.Contain("UpdateCheckService.IsTrustedReleaseUri"));
            Assert.That(source, Does.Contain("UseShellExecute = true"));
        });
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the OpenNotes project root.");
    }
}
