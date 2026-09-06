using System.IO;
using System.Windows;
using Caelum.Models;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class TabDragCoordinatorTests
{
    [TearDown]
    public void TearDown()
    {
        TabDragCoordinator.CancelDrag();
    }

    [Test]
    public void BeginDragPublishesTheSameLiveTabAndPayloadCanBeReadByAnotherWindow()
    {
        var tab = new AppTab { FilePath = @"C:\Docs\notes.pdf" };
        var payload = TabDragCoordinator.BeginDrag(null, tab);
        var data = new DataObject();
        data.SetData(TabDragCoordinator.DragDataFormat, payload);

        Assert.That(payload.Tab, Is.SameAs(tab));
        Assert.That(payload.TabId, Is.EqualTo(tab.Id));
        Assert.That(TabDragCoordinator.TryGetPayload(data, out var received), Is.True);
        Assert.That(received, Is.SameAs(payload));
        Assert.That(TabDragCoordinator.CompleteDrag(payload, DragDropEffects.Move), Is.False);
    }

    [Test]
    public void AcceptedDockDoesNotRequestOutsideDetach()
    {
        var payload = TabDragCoordinator.BeginDrag(null, new AppTab());

        Assert.That(TabDragCoordinator.AcceptDrop(payload), Is.True);
        Assert.That(TabDragCoordinator.CompleteDrag(payload, DragDropEffects.None), Is.False);
    }

    [Test]
    public void UncancelledDropOutsideRegisteredWindowsRequestsDetach()
    {
        var payload = TabDragCoordinator.BeginDrag(null, new AppTab());

        Assert.That(TabDragCoordinator.CompleteDrag(payload, DragDropEffects.None), Is.True);
    }

    [Test]
    public void CancelledDragNeverRequestsDetach()
    {
        var payload = TabDragCoordinator.BeginDrag(null, new AppTab());
        TabDragCoordinator.CancelDrag(payload);

        Assert.That(TabDragCoordinator.CompleteDrag(payload, DragDropEffects.None), Is.False);
    }

    [Test]
    public void MainWindowUsesLiveFrameTransferAndAllowsDuplicateFileTabs()
    {
        var source = ReadProjectFile("MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("TransferTabTo"));
            Assert.That(source, Does.Contain("_frameEditors.Remove(tab.Frame)"));
            Assert.That(source, Does.Contain("_frameEditors[state.Frame]"));
            Assert.That(source, Does.Contain("new MainWindow(createHomeTab: false)"));
            Assert.That(source, Does.Not.Contain("// Check if this file is already open"));
            Assert.That(source, Does.Contain("TabDragCoordinator.BeginDrag"));
            Assert.That(source, Does.Contain("TabDragPreview"));
            Assert.That(source, Does.Contain("GiveFeedback"));
        });
    }

    [Test]
    public void DetachMovesTheLiveTabInsteadOfReopeningTheFile()
    {
        var source = ReadProjectFile("MainWindow.xaml.cs");
        int detachStart = source.IndexOf("private void DetachTabToNewWindow(AppTab tab)", StringComparison.Ordinal);
        int openStart = source.IndexOf("public void OpenFileInNewTab(", StringComparison.Ordinal);
        Assert.That(detachStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(openStart, Is.GreaterThan(detachStart));
        string detachBody = source[detachStart..openStart];

        Assert.Multiple(() =>
        {
            Assert.That(detachBody, Does.Contain("TransferTabTo"));
            Assert.That(detachBody, Does.Contain("new MainWindow(createHomeTab: false)"));
            Assert.That(detachBody, Does.Not.Contain("OpenFileInNewTab"));
            Assert.That(detachBody, Does.Not.Contain("NavigateActiveTabToFile"));
        });
    }

    [Test]
    public void TabDragOverTheWindowKeepsMoveEffectSoPageDropsDoNotDetach()
    {
        var source = ReadProjectFile("MainWindow.xaml.cs");
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private void Window_DragOver"));
            Assert.That(source, Does.Not.Contain(
                """
                if (TabDragCoordinator.TryGetPayload(e.Data, out _))
                {
                    e.Effects = DragDropEffects.None;
                """));
            Assert.That(source, Does.Contain("e.Effects = DragDropEffects.Move"));
        });
    }

    [Test]
    public void MainWindowKeepsStableTitleAndIndependentTaskbarWindows()
    {
        var xaml = ReadProjectFile("MainWindow.xaml");
        var source = ReadProjectFile("MainWindow.Utilities.cs");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Title=\"OpenNotes\""));
            Assert.That(xaml, Does.Contain("ShowInTaskbar=\"True\""));
            Assert.That(source, Does.Contain("Title = ProductInfo.DisplayName;"));
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
