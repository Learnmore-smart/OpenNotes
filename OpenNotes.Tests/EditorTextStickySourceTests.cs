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

    [Test]
    public void SpecFixContract_HitTestEscapeOrderingAndTransientSweep()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        string page = Read("Controls", "PdfPageControl.xaml.cs");

        Assert.Multiple(() =>
        {
            // P1 — the resize handle Grid carries a non-null Background in
            // its creation block; a background-less panel is pointer-dead.
            int handleCtor = editor.IndexOf(
                "new TextResizeHandleElement", StringComparison.Ordinal);
            Assert.That(handleCtor, Is.GreaterThanOrEqualTo(0),
                "TextResizeHandleElement creation not found");
            int cursorLine = editor.IndexOf(
                "resizeHandle.SetCursor", handleCtor, StringComparison.Ordinal);
            Assert.That(cursorLine, Is.GreaterThan(handleCtor));
            string ctorBlock = editor.Substring(handleCtor, cursorLine - handleCtor);
            Assert.That(ctorBlock, Does.Contain("Background ="),
                "Resize handle must assign a (transparent) Background to hit-test");

            // P2a — generic Escape runs BEFORE the textInputFocused bail,
            // and the resize-restore Escape keeps precedence ahead of it.
            int preview = editor.IndexOf(
                "private void EditorPage_PreviewKeyDown", StringComparison.Ordinal);
            Assert.That(preview, Is.GreaterThanOrEqualTo(0));
            int resizeEscape = editor.IndexOf(
                "_resizingTextContainer != null", preview, StringComparison.Ordinal);
            int escapeSweep = editor.IndexOf(
                "CloseTransientUi(\"escape\")", preview, StringComparison.Ordinal);
            int focusBail = editor.IndexOf(
                "if (textInputFocused)", preview, StringComparison.Ordinal);
            Assert.That(resizeEscape, Is.GreaterThan(0),
                "Escape resize-restore branch missing");
            Assert.That(escapeSweep, Is.GreaterThan(resizeEscape),
                "Generic Escape must follow the resize-restore branch");
            Assert.That(focusBail, Is.GreaterThan(escapeSweep),
                "Escape must be handled before the textInputFocused bail");
            int escapeTool = editor.IndexOf(
                "ActivateTool(ToolType.None)", escapeSweep, StringComparison.Ordinal);
            Assert.That(escapeTool, Is.GreaterThan(escapeSweep));
            Assert.That(escapeTool, Is.LessThan(focusBail),
                "Escape branch must ActivateTool(None) before the focus bail");

            // P2b — SetHostActive sweeps transient UI ahead of the no-op
            // early return (WPF ordering); ReleaseResources shares the helper.
            int setHost = editor.IndexOf(
                "public void SetHostActive(bool isActive)", StringComparison.Ordinal);
            Assert.That(setHost, Is.GreaterThanOrEqualTo(0));
            int sweep = editor.IndexOf(
                "CloseTransientUi(\"inactive editor\")", setHost, StringComparison.Ordinal);
            int guard = editor.IndexOf(
                "_isHostActive == isActive", setHost, StringComparison.Ordinal);
            Assert.That(sweep, Is.GreaterThan(setHost),
                "SetHostActive must call CloseTransientUi");
            Assert.That(sweep, Is.LessThan(guard),
                "Transient sweep must precede the no-op early return");
            Assert.That(editor, Does.Contain("CloseTransientUi(\"release\")"),
                "ReleaseResources should share the transient sweep");

            // The page-level interaction cancel must also end a captured
            // sticky-marker drag (WPF InteractionCancellation.CancelAll).
            int cancel = page.IndexOf(
                "public void CancelInteraction()", StringComparison.Ordinal);
            Assert.That(cancel, Is.GreaterThanOrEqualTo(0));
            int stickyCancel = page.IndexOf("CancelStickyDrag();", cancel, StringComparison.Ordinal);
            int shapeClear = page.IndexOf("ClearShapePreview();", cancel, StringComparison.Ordinal);
            Assert.That(stickyCancel, Is.GreaterThan(cancel));
            Assert.That(stickyCancel, Is.LessThan(shapeClear),
                "CancelInteraction should cancel the sticky drag");
        });
    }

    [Test]
    public void SpecFixContract_ToolbarFocusCaptureOwnershipAndGestureGuards()
    {
        string editor = Read("Pages", "EditorPage.xaml.cs");
        string page = Read("Controls", "PdfPageControl.xaml.cs");
        string core = Read(Path.Combine("..", "OpenNotes.Core", "Ink", "AnnotationUndoActions.cs"));

        Assert.Multiple(() =>
        {
            // P1 — interactive-chrome focus guard: the helper walks visual
            // ancestors, recognizes the inline toolbar + control types, and
            // gates the nudge/Delete branches inside PreviewKeyDown.
            Assert.That(editor, Does.Contain("private bool IsInteractiveEditorChrome(DependencyObject focused)"));
            int chrome = editor.IndexOf(
                "private bool IsInteractiveEditorChrome", StringComparison.Ordinal);
            int chromeEnd = editor.IndexOf(
                "private static bool TryGetTextBoxNudge", chrome, StringComparison.Ordinal);
            string chromeBody = editor.Substring(chrome, chromeEnd - chrome);
            Assert.That(chromeBody, Does.Contain("_inlineTextBoxToolbar"));
            Assert.That(chromeBody, Does.Contain("case ButtonBase"));
            Assert.That(chromeBody, Does.Contain("case ComboBox"));
            Assert.That(chromeBody, Does.Contain("VisualTreeHelper.GetParent"));

            int preview = editor.IndexOf(
                "private void EditorPage_PreviewKeyDown", StringComparison.Ordinal);
            Assert.That(preview, Is.GreaterThanOrEqualTo(0));
            int chromeGuard = editor.IndexOf(
                "IsInteractiveEditorChrome(focusedElement)", preview, StringComparison.Ordinal);
            int nudge = editor.IndexOf(
                "TryGetTextBoxNudge(e.Key", preview, StringComparison.Ordinal);
            int deleteBranch = editor.IndexOf(
                "e.Key == VirtualKey.Delete || e.Key == VirtualKey.Back",
                preview, StringComparison.Ordinal);
            Assert.That(chromeGuard, Is.GreaterThan(preview),
                "PreviewKeyDown must call the chrome guard");
            Assert.That(nudge, Is.GreaterThan(chromeGuard),
                "Nudge must run only after the chrome guard");
            Assert.That(deleteBranch, Is.GreaterThan(chromeGuard),
                "Delete/Back must run only after the chrome guard");

            // P2s.1 — the resize capture lives on the HANDLE element, not
            // the container; cancellation releases that exact element.
            Assert.That(editor, Does.Contain("TextResizeHandleElement _resizingTextHandleElement"));
            int cancelResize = editor.IndexOf(
                "private void CancelTextResize(bool restoreBounds)", StringComparison.Ordinal);
            Assert.That(cancelResize, Is.GreaterThanOrEqualTo(0));
            int nextMethod = editor.IndexOf("private ", cancelResize + 20, StringComparison.Ordinal);
            string cancelBody = editor.Substring(cancelResize, nextMethod - cancelResize);
            Assert.That(cancelBody, Does.Contain("_resizingTextHandleElement?.ReleasePointerCaptures()"));
            Assert.That(cancelBody, Does.Not.Contain("resizingContainer.ReleasePointerCaptures()"));

            // P2s.2 — programmatic selection cancel drops overlay captures.
            int cancelSel = page.IndexOf(
                "private void CancelSelectionInteraction(", StringComparison.Ordinal);
            Assert.That(cancelSel, Is.GreaterThanOrEqualTo(0));
            int cancelSelEnd = page.IndexOf("SetPageZIndex(0);", cancelSel, StringComparison.Ordinal);
            string cancelSelBody = page.Substring(cancelSel, cancelSelEnd - cancelSel);
            Assert.That(cancelSelBody,
                Does.Contain("SelectionOverlayCanvas.ReleasePointerCaptures()"));

            // P2s.3 — the per-box SizeChanged hook is lifecycle-owned:
            // Unloaded unsubscribes, Loaded re-binds after reparenting.
            Assert.That(editor, Does.Contain("sizeTrackingHandler"));
            Assert.That(editor, Does.Contain("container.Unloaded += (s, e) => UnsubscribeSizeTracking()"));
            Assert.That(editor, Does.Contain("container.Loaded += (s, e) => SubscribeSizeTracking"));
            Assert.That(editor, Does.Contain(".SizeChanged -= sizeTrackingHandler"));

            // P2s.4 — Core guarded transfers + membership probe.
            Assert.That(core, Does.Contain("static class AnnotationContainerTransfer"));
            Assert.That(core, Does.Contain("bool ContainsTextContainer(object container)"));
            Assert.That(core, Does.Contain("AnnotationContainerTransfer.Move("));
            Assert.That(page, Does.Contain(
                "bool IAnnotationContainerHost.ContainsTextContainer(object container)"));

            // P2s.5 — undo/redo restore in-flight geometry before applying.
            int undo = editor.IndexOf("private async Task PerformUndoAsync()", StringComparison.Ordinal);
            int redo = editor.IndexOf("private async Task PerformRedoAsync()", StringComparison.Ordinal);
            Assert.That(undo, Is.GreaterThanOrEqualTo(0));
            Assert.That(redo, Is.GreaterThanOrEqualTo(0));
            string undoBody = editor.Substring(undo, redo - undo);
            Assert.That(undoBody, Does.Contain("CancelTextBoxDrag(restoreBounds: true)"));
            Assert.That(undoBody, Does.Contain("CancelTextResize(restoreBounds: true)"));
            Assert.That(undoBody, Does.Not.Contain("restoreBounds: false"));
            string redoBody = editor.Substring(redo,
                Math.Min(4000, editor.Length - redo));
            Assert.That(redoBody, Does.Contain("CancelTextBoxDrag(restoreBounds: true)"));
            Assert.That(redoBody, Does.Contain("CancelTextResize(restoreBounds: true)"));

            // P2s.6 — leaving the Text tool cancels a live box drag too.
            int activate = editor.IndexOf("private void ActivateTool(", StringComparison.Ordinal);
            Assert.That(activate, Is.GreaterThanOrEqualTo(0));
            int toolBlock = editor.IndexOf("if (tool != ToolType.Text)", activate, StringComparison.Ordinal);
            int toolBlockEnd = editor.IndexOf("CommitTextEditSession();", toolBlock, StringComparison.Ordinal);
            string toolBody = editor.Substring(toolBlock, toolBlockEnd - toolBlock);
            Assert.That(toolBody, Does.Contain("CancelTextBoxDrag(restoreBounds: true)"));

            // P3s.7 — pointer-id tracking on both gesture families.
            Assert.That(editor, Does.Contain("uint? _dragPointerId"));
            Assert.That(editor, Does.Contain("uint? _textResizePointerId"));
            Assert.That(editor, Does.Contain("e.Pointer.PointerId != _textResizePointerId.Value"));
            Assert.That(editor, Does.Contain("e.Pointer.PointerId != _dragPointerId.Value"));
            Assert.That(page, Does.Contain("_stickyDragPointerId"));

            // P3s.8 — cross-page drop folds the target-page clamp into the
            // action delta; paste clamps through TextAnnotationGeometry.
            Assert.That(editor, Does.Contain("double effDx = clamped.X - _dragStartX - adjustX"));
            int pasteIdx = editor.IndexOf(
                "private async void PasteSelection()", StringComparison.Ordinal);
            Assert.That(pasteIdx, Is.GreaterThanOrEqualTo(0));
            int pasteEnd = editor.IndexOf("MarkDirty();", pasteIdx, StringComparison.Ordinal);
            string pasteBody = editor.Substring(pasteIdx, pasteEnd - pasteIdx);
            Assert.That(pasteBody, Does.Contain("TextAnnotationGeometry.ClampToPage"));

            // P3s.9 — no synchronous UpdateLayout in the live resize path.
            int applyIdx = editor.IndexOf(
                "private void ApplyTextContainerBounds(", StringComparison.Ordinal);
            Assert.That(applyIdx, Is.GreaterThanOrEqualTo(0));
            int applyEnd = editor.IndexOf("// ── Eight-handle resize", applyIdx, StringComparison.Ordinal);
            string applyBody = editor.Substring(applyIdx, applyEnd - applyIdx);
            Assert.That(applyBody, Does.Not.Contain("UpdateLayout()"));

            // P3s.10 — live moves coalesce; release rebuilds synchronously.
            int moveIdx = page.IndexOf(
                "public void MoveItemsDirectly(", StringComparison.Ordinal);
            Assert.That(moveIdx, Is.GreaterThanOrEqualTo(0));
            int moveEnd = page.IndexOf("public void ScaleSelection(", moveIdx, StringComparison.Ordinal);
            string moveBody = page.Substring(moveIdx, moveEnd - moveIdx);
            Assert.That(moveBody, Does.Contain("QueueSelectionVisualsUpdate()"));
            int completeIdx = page.IndexOf(
                "private void CompleteSelectionGesture()", StringComparison.Ordinal);
            Assert.That(completeIdx, Is.GreaterThanOrEqualTo(0));
            int completeEnd = page.IndexOf("if (!_isSelecting)", completeIdx, StringComparison.Ordinal);
            string completeBody = page.Substring(completeIdx, completeEnd - completeIdx);
            Assert.That(completeBody, Does.Contain("UpdateSelectionVisuals();"));

            // P3s.11 — the sticky marker keeps exactly ONE flyout path:
            // explicit ShowAt, no framework auto-open on the button.
            Assert.That(page, Does.Contain("flyout.ShowAt(container)"));
            Assert.That(page, Does.Not.Contain("hitButton.ContextFlyout"));

            // P3s.13 — quiet mutators notify thumbnail/dirty observers.
            Assert.That(page, Does.Contain("InkMutated?.Invoke(this, EventArgs.Empty);"));

            // P3s.14 — the text edit session carries page + load-session
            // anchors so a stale LostFocus can't record a dead undo.
            Assert.That(editor, Does.Contain("PdfPageControl _textEditSessionPage"));
            Assert.That(editor, Does.Contain("_textEditSessionId"));
            int commitIdx = editor.IndexOf(
                "private void CommitTextEditSession()", StringComparison.Ordinal);
            Assert.That(commitIdx, Is.GreaterThanOrEqualTo(0));
            int commitEnd = editor.IndexOf("PushUndoAction(new TextEditSessionAction", commitIdx, StringComparison.Ordinal);
            string commitBody = editor.Substring(commitIdx, commitEnd - commitIdx);
            Assert.That(commitBody, Does.Contain("sessionId != _loadSessionId"));
            Assert.That(commitBody, Does.Contain("!ReferenceEquals(page, sessionPage)"));
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
