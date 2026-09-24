using System.IO;
using System.Linq;

namespace Caelum.Tests;

/// <summary>
/// Task 8 Phase A source contract — pins the WinUI text/sticky wiring so a
/// later refactor can't silently drop the pieces the WPF editor relies on:
/// tool wiring + quiet load, sticky popup lifecycle, inline text toolbar,
/// mixed selection transforms through the Core undo actions, the
/// strokes+text+sticky clipboard pipeline, and the T9-facing collectors.
/// Unlike the WPF source tests this fixture reads the WinUI tree — it is
/// the first source contract on the port itself.
/// </summary>
[TestFixture]
public sealed class EditorTextStickySourceTests
{
    [Test]
    public void EditorPageWiresTextToolStickyPopupAndUndoActions()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");

        Assert.Multiple(() =>
        {
            // Tool wiring + quiet annotation load (no undo entries).
            Assert.That(editor, Does.Contain("page.SetMode(_currentTool == ToolType.Text)"));
            Assert.That(editor, Does.Contain("LoadAnnotationsIntoPages"));
            Assert.That(editor, Does.Contain("_isLoadingAnnotations"));
            Assert.That(editor, Does.Contain("CreateTextBox("));
            Assert.That(editor, Does.Contain("page.AddStickyNote(note)"));

            // Text undo surface — one action per completed gesture/session.
            Assert.That(editor, Does.Contain("TextBoxAddedAction"));
            Assert.That(editor, Does.Contain("TextBoxDeletedAction"));
            Assert.That(editor, Does.Contain("TextEditSessionAction"));
            Assert.That(editor, Does.Contain("TextBoxMovedAction"));
            Assert.That(editor, Does.Contain("TextBoxResizedAction"));
            Assert.That(editor, Does.Contain("TextStyleChangedAction"));
            Assert.That(editor, Does.Contain("TextFormatChangedAction"));
            Assert.That(editor, Does.Contain("BeginTextEditSession"));
            Assert.That(editor, Does.Contain("CommitTextEditSession"));
            Assert.That(editor, Does.Contain("NudgeSelectedTextBox"));

            // Inline formatting toolbar + automation ids.
            Assert.That(editor, Does.Contain("EnsureInlineTextBoxToolbar"));
            Assert.That(editor, Does.Contain("PositionInlineTextBoxToolbar"));
            Assert.That(editor, Does.Contain("Editor.TextToolbar.Delete"));
            Assert.That(editor, Does.Contain("Editor.TextToolbar.FontSizeDown"));
            Assert.That(editor, Does.Contain("Editor.TextToolbar.FontSizeUp"));
            Assert.That(editor, Does.Contain("Editor.TextToolbar.Color"));
            Assert.That(editor, Does.Contain("Editor.TextToolbar.Bold"));
            Assert.That(editor, Does.Contain("Editor.TextToolbar.Italic"));
            Assert.That(editor, Does.Contain("Editor.TextToolbar.FontFamily"));
            Assert.That(editor, Does.Contain("Editor.TextToolbar.Alignment"));

            // Sticky popup lifecycle + handlers + undo actions.
            Assert.That(editor, Does.Contain("PageControl_StickyNoteActivated"));
            Assert.That(editor, Does.Contain("PageControl_StickyNoteMoved"));
            Assert.That(editor, Does.Contain("PageControl_StickyNoteDeleteRequested"));
            Assert.That(editor, Does.Contain("OpenStickyNoteEditor"));
            Assert.That(editor, Does.Contain("SaveStickyNoteEdit"));
            Assert.That(editor, Does.Contain("CancelStickyNoteEdit"));
            Assert.That(editor, Does.Contain("DeleteStickyNoteEdit"));
            Assert.That(editor, Does.Contain("StickyNoteAddedAction"));
            Assert.That(editor, Does.Contain("StickyNoteDeletedAction"));
            Assert.That(editor, Does.Contain("StickyNoteMovedAction"));
            Assert.That(editor, Does.Contain("StickyNoteEditAction"));
            Assert.That(editor, Does.Contain("Sticky.Editor.DragHandle"));
            Assert.That(editor, Does.Contain("Sticky.Save"));
            Assert.That(editor, Does.Contain("Sticky.Cancel"));
            Assert.That(editor, Does.Contain("Sticky.Delete"));

            // Mixed selection transforms + delete through ONE undo step.
            Assert.That(editor, Does.Contain("AnnotationSelectionMoveAction"));
            Assert.That(editor, Does.Contain("AnnotationSelectionResizeAction"));
            Assert.That(editor, Does.Contain("AnnotationSelectionRotateAction"));
            Assert.That(editor, Does.Contain("AnnotationSelectionCrossPageMoveAction"));
            Assert.That(editor, Does.Contain("AnnotationItemsRemovedAction"));
            Assert.That(editor, Does.Contain("AnnotationItemsAddedAction"));

            // Clipboard pipeline + collectors for the T9 save path.
            Assert.That(editor, Does.Contain("CopySelection"));
            Assert.That(editor, Does.Contain("CutSelection"));
            Assert.That(editor, Does.Contain("PasteSelection"));
            Assert.That(editor, Does.Contain("HasPasteableClipboard"));
            Assert.That(editor, Does.Contain("Editor.Action.Copy"));
            Assert.That(editor, Does.Contain("Editor.Action.Paste"));
            Assert.That(editor, Does.Contain("CollectAnnotations"));
            Assert.That(editor, Does.Contain("GetTextData()"));
            Assert.That(editor, Does.Contain("GetStickyNoteData()"));

            // Lifecycle: sessions die with the document/host teardown.
            Assert.That(editor, Does.Contain("CancelStickyNoteEdit();"));
            Assert.That(editor, Does.Contain("DeselectTextBox();"));
        });
    }

    [Test]
    public void PdfPageControlExposesOverlaySurfaceAndQuietMutators()
    {
        string page = Read("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            // Mode + overlay entry points.
            Assert.That(page, Does.Contain("public void SetMode(bool isTextMode)"));
            Assert.That(page, Does.Contain("TextOverlayPointerPressed"));
            Assert.That(page, Does.Contain("BackgroundPointerPressed"));
            Assert.That(page, Does.Contain("public Grid AddStickyNote(StickyNoteAnnotation note)"));

            // Quiet mutators the Core undo actions + editor load path use.
            Assert.That(page, Does.Contain("RemoveTextContainerQuiet"));
            Assert.That(page, Does.Contain("AddTextContainerQuiet"));
            Assert.That(page, Does.Contain("SetStickyNotePositionQuiet"));
            Assert.That(page, Does.Contain("SetStickyNoteTextQuiet"));
            Assert.That(page, Does.Contain("SetOverlayData"));
            Assert.That(page, Does.Contain("GetOverlayData"));

            // Collectors + per-container snapshot (clipboard).
            Assert.That(page, Does.Contain("public List<TextAnnotation> GetTextData()"));
            Assert.That(page, Does.Contain("public List<StickyNoteAnnotation> GetStickyNoteData()"));
            Assert.That(page, Does.Contain("TryGetTextAnnotation"));

            // Persist-as-auto flags ride the element (WPF attached DP parity).
            Assert.That(page, Does.Contain("TextAnnotationAutoWidthProperty"));
            Assert.That(page, Does.Contain("TextAnnotationAutoHeightProperty"));

            // Sticky events the editor subscribes to.
            Assert.That(page, Does.Contain("StickyNoteActivated"));
            Assert.That(page, Does.Contain("StickyNoteMoved"));
            Assert.That(page, Does.Contain("StickyNoteDeleteRequested"));
            Assert.That(page, Does.Contain("BuildStickyNoteContextMenu"));
            Assert.That(page, Does.Contain("RefreshStickyNoteContextMenuLocalization"));

            // Selection participation + Core host contract.
            Assert.That(page, Does.Contain("SelectedTextContainers"));
            Assert.That(page, Does.Contain("IAnnotationContainerHost"));
            Assert.That(page, Does.Contain("MoveItemsDirectly"));
            Assert.That(page, Does.Contain("ScaleItemsDirectly"));
            Assert.That(page, Does.Contain("RotateItemsDirectly"));
        });
    }

    [Test]
    public void CoreAnnotationUndoLedgerCoversTheWpfActionSet()
    {
        string core = Read(Path.Combine("..", "OpenNotes.Core", "Ink", "AnnotationUndoActions.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(core, Does.Contain("public interface IAnnotationContainerHost"));
            Assert.That(core, Does.Contain("bool RemoveTextContainerQuiet(object container)"));
            Assert.That(core, Does.Contain("void AddTextContainerQuiet(object container)"));
            Assert.That(core, Does.Contain("bool SetStickyNotePositionQuiet(object container, PointD position)"));
            Assert.That(core, Does.Contain("bool SetStickyNoteTextQuiet(object container, string text)"));
            Assert.That(core, Does.Contain("void SetTextContainerBoundsQuiet("));
            Assert.That(core, Does.Contain("bool SetTextContentQuiet(object container, string text)"));
            Assert.That(core, Does.Contain("void SetTextStyleQuiet("));
            Assert.That(core, Does.Contain("void SetTextFormatQuiet(object container, TextFormatSnapshot format)"));
            Assert.That(core, Does.Contain("void ClearSelection()"));

            Assert.That(core, Does.Contain("class TextBoxAddedAction"));
            Assert.That(core, Does.Contain("class TextBoxDeletedAction"));
            Assert.That(core, Does.Contain("class TextEditSessionAction"));
            Assert.That(core, Does.Contain("class TextStyleChangedAction"));
            Assert.That(core, Does.Contain("class TextFormatChangedAction"));
            Assert.That(core, Does.Contain("class TextBoxMovedAction"));
            Assert.That(core, Does.Contain("class TextBoxResizedAction"));
            Assert.That(core, Does.Contain("class StickyNoteAddedAction"));
            Assert.That(core, Does.Contain("class StickyNoteDeletedAction"));
            Assert.That(core, Does.Contain("class StickyNoteMovedAction"));
            Assert.That(core, Does.Contain("class StickyNoteEditAction"));
            Assert.That(core, Does.Contain("class AnnotationSelectionMoveAction"));
            Assert.That(core, Does.Contain("class AnnotationSelectionResizeAction"));
            Assert.That(core, Does.Contain("class AnnotationSelectionRotateAction"));
            Assert.That(core, Does.Contain("class AnnotationItemsAddedAction"));
            Assert.That(core, Does.Contain("class AnnotationItemsRemovedAction"));
            Assert.That(core, Does.Contain("class AnnotationSelectionCrossPageMoveAction"));
        });
    }

    private static string Read(params string[] segments)
    {
        // The WinUI tree sits next to the WPF project — walk up to the
        // solution folder (the directory that contains OpenNotes.WinUI.csproj)
        // rather than the WPF csproj the older source tests anchor on.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.WinUI", "OpenNotes.WinUI.csproj")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the solution root containing OpenNotes.WinUI.");
        return File.ReadAllText(Path.Combine(
            new[] { Path.Combine(root, "OpenNotes.WinUI") }.Concat(segments).ToArray()));
    }
}
