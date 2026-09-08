using System.IO;

namespace Caelum.Tests;

[TestFixture]
public sealed class EditorSelectionChromeSourceTests
{
    [Test]
    public void SelectionActionBarCopiesPastesAndDeletesThroughExistingUndo()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        string page = Read("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(editor, Does.Contain("EnsureSelectionActionBar"));
            Assert.That(editor, Does.Contain("UpdateSelectionActionBar"));
            Assert.That(editor, Does.Contain("Editor.Action.Copy"));
            Assert.That(editor, Does.Contain("Editor.Action.Paste"));
            Assert.That(editor, Does.Contain("Editor.Action.Delete"));
            Assert.That(editor, Does.Contain("HasPasteableClipboard"));
            Assert.That(editor, Does.Contain("ClipboardImageDecoder"));
            Assert.That(editor, Does.Contain("TryCopySelectedPdfTextToClipboard"));
            Assert.That(editor, Does.Contain("CopySelection();"));
            Assert.That(editor, Does.Contain("PasteSelection();"));
            Assert.That(editor, Does.Contain("DeleteSelection();"));
            Assert.That(editor, Does.Contain("_transientUiRegistry.Register(_selectionActionBar)"));
            Assert.That(editor, Does.Contain("PopupZOrderHelper.FixPopupTopmost(_selectionActionBar)"));
            Assert.That(page, Does.Contain("BlankContextRequested"));
            Assert.That(page, Does.Contain("StylusSystemGesture"));
            Assert.That(editor, Does.Contain("Editor.Action.SelectAll"));
            Assert.That(editor, Does.Contain("Editor.Action.RefreshPage"));
            Assert.That(editor, Does.Contain("RefreshCurrentDocumentPreservingEditsAsync"));
            Assert.That(editor, Does.Contain("SaveCurrentDocumentAsync()"));
        });
    }

    [Test]
    public void ShapeCommitAutoSelectsUntilBlankClick()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        string page = Read("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(editor, Does.Contain("PageControl_ShapeCommittedUndoable"));
            Assert.That(editor, Does.Contain("ActivateTool(ToolType.Select)"));
            Assert.That(editor, Does.Contain("page.SelectItems(strokes"));
            Assert.That(page, Does.Contain("SelectionRotateCompleted"));
            Assert.That(page, Does.Contain("RotateItemsDirectly"));
            Assert.That(editor, Does.Contain("class SelectionRotateAction"));
            Assert.That(editor, Does.Contain("private double _rulerLength"));
            Assert.That(editor, Does.Contain("MinRulerLength"));
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
