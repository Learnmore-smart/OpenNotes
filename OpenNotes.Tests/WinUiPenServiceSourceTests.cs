using System;
using System.IO;
using System.Linq;

namespace Caelum.Tests;

/// <summary>
/// Window-scoped PenService contract (final-review fix): the WinUI port
/// originally built one <c>PenService</c> per <c>EditorPage</c>, so every
/// page subclassed the SAME MainWindow HWND and re-registered the same
/// Win+F19/F20 hotkey ids. Duplicate <c>RegisterHotKey</c> ids fail on the
/// same HWND (tabs 2+ silently lost the hotkeys, and the owning tab's
/// teardown unregistered them app-wide), while every subclass proc still
/// observed the shared WM_HOTKEY — the eraser toggle hit inactive editors.
/// The service is now owned once by <c>MainWindow</c>, which routes
/// <c>ToolToggleRequested</c>/<c>PenDeviceDetected</c> to the ACTIVE tab's
/// editor only (WPF <c>IsActiveEditorPage</c> parity); the editor re-checks
/// <c>_isHostActive</c>/<c>_resourcesReleased</c> inside the callbacks.
/// </summary>
[TestFixture]
public sealed class WinUiPenServiceSourceTests
{
    [Test]
    public void PenServiceIsOwnedOnceByMainWindowAndRoutedToTheActiveEditor()
    {
        string main = Read("MainWindow.xaml.cs");
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Exactly one construction site, living in MainWindow — editor
            // pages must never new up their own service again.
            Assert.That(main, Does.Contain("_penService = new PenService();"));
            Assert.That(CountOccurrences(main, "new PenService("), Is.EqualTo(1));
            Assert.That(editor, Does.Not.Contain("new Caelum.Services.PenService("));
            Assert.That(editor, Does.Not.Contain("new PenService("));

            // The window subscribes both events and routes them to the
            // ACTIVE tab's editor — never a broadcast to every page.
            Assert.That(main, Does.Contain("internal PenService GetOrCreatePenService()"));
            Assert.That(main, Does.Contain("_penService.ToolToggleRequested += PenService_ToolToggleRequested"));
            Assert.That(main, Does.Contain("_penService.PenDeviceDetected += PenService_PenDeviceDetected"));
            Assert.That(main, Does.Contain("_penService.Initialize(this)"));
            Assert.That(
                CountOccurrences(main, "_activeTab?.Frame?.Content as EditorPage"),
                Is.GreaterThanOrEqualTo(2),
                "both PenService events must be routed through the active tab's editor");
            Assert.That(main, Does.Contain("?.HandlePenToolToggle()"));
            Assert.That(main, Does.Contain("?.HandlePenDeviceDetected(info)"));

            // The window disposes the single instance on Closed.
            int closedStart = main.IndexOf(
                "private void MainWindow_Closed(object sender, WindowEventArgs args)",
                StringComparison.Ordinal);
            Assert.That(closedStart, Is.GreaterThanOrEqualTo(0));
            string closedBody = main[closedStart..];
            int nextMember = closedBody.IndexOf("\n        private ", 1, StringComparison.Ordinal);
            if (nextMember > 0)
                closedBody = closedBody[..nextMember];
            Assert.That(closedBody, Does.Contain("_penService?.Dispose();"));
        });
    }

    [Test]
    public void EditorPageConsumesTheSharedServiceAndGatesTheCallbacks()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // The page pulls the shared instance instead of constructing/
            // initializing/subscribing its own.
            Assert.That(editor, Does.Contain("_penService = GetMainWindow()?.GetOrCreatePenService();"));
            Assert.That(editor, Does.Not.Contain("_penService.Initialize("));
            Assert.That(editor, Does.Not.Contain("_penService.ToolToggleRequested +="));
            Assert.That(editor, Does.Not.Contain("_penService.PenDeviceDetected +="));

            // Routed entry points exist and keep the host-active/released
            // gate (the WPF IsActiveEditorPage() callback recheck).
            int toggleStart = editor.IndexOf(
                "internal void HandlePenToolToggle()", StringComparison.Ordinal);
            Assert.That(toggleStart, Is.GreaterThanOrEqualTo(0));
            string toggleBody = editor[toggleStart..];
            int nextMember = toggleBody.IndexOf("\n        internal ", 1, StringComparison.Ordinal);
            if (nextMember < 0)
                nextMember = toggleBody.IndexOf("\n        private ", 1, StringComparison.Ordinal);
            if (nextMember > 0)
                toggleBody = toggleBody[..nextMember];
            Assert.That(toggleBody, Does.Contain("!_isHostActive || _resourcesReleased"));
            Assert.That(toggleBody, Does.Contain("ToggleEraserMode();"));
            Assert.That(
                CountOccurrences(toggleBody, "!_isHostActive || _resourcesReleased"),
                Is.GreaterThanOrEqualTo(2),
                "the toggle gate must apply before enqueue AND inside the callback");

            int detectedStart = editor.IndexOf(
                "internal void HandlePenDeviceDetected(Caelum.Services.PenDeviceInfo info)",
                StringComparison.Ordinal);
            Assert.That(detectedStart, Is.GreaterThanOrEqualTo(0));
            string detectedBody = editor[detectedStart..Math.Min(editor.Length, detectedStart + 4000)];
            Assert.That(detectedBody, Does.Contain("!_isHostActive || _resourcesReleased"));

            // Release drops only the reference — the window owns disposal.
            Assert.That(editor, Does.Not.Contain("_penService?.Dispose();"));

            // Probing feed is preserved: the shared service still reaches
            // every ink surface.
            Assert.That(editor, Does.Contain("PushPenServiceToPages"));
            Assert.That(editor, Does.Contain("page.Ink.SetPenService(_penService)"));
        });
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static string Read(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(
            new[] { Path.Combine(ProjectRoot(), "OpenNotes.WinUI") }.Concat(segments).ToArray()));
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.WinUI", "OpenNotes.WinUI.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not locate the solution root containing OpenNotes.WinUI.");
    }
}
