using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caelum.InkGeometry;
using Caelum.Models;

namespace Caelum.Ink;

// ==================================================================
// Task 8 Phase A — text/sticky annotation undo actions.
//
// The WPF editor keeps these actions as private nested classes that hold
// (PdfPageControl page, Grid container) pairs and replay quiet mutations
// on the page (AddTextContainerQuiet / RemoveTextContainerQuiet /
// SetStickyNotePositionQuiet / ...). To keep Core UI-free while preserving
// the exact same semantics, the page dependency is abstracted behind
// IAnnotationContainerHost: the WinUI PdfPageControl implements it with
// real Grid containers; headless tests drive the same actions with a fake
// host. Container references are opaque objects — the actions never
// inspect them beyond identity.
// ==================================================================

/// <summary>
/// UI-free contract for the quiet container mutations that the annotation
/// undo actions replay — the Core port of the WPF PdfPageControl quiet
/// methods. Implementations must apply the change WITHOUT raising
/// user-action events (no undo recursion, no per-item completion events)
/// but SHOULD raise the normal mutation/visual notifications so overlays
/// and thumbnails refresh.
/// </summary>
public interface IAnnotationContainerHost
{
    /// <summary>Remove a text/sticky container without touching its payload.</summary>
    bool RemoveTextContainerQuiet(object container);

    /// <summary>Re-add a container removed through the quiet path.</summary>
    void AddTextContainerQuiet(object container);

    /// <summary>True while the container is hosted on the page's overlay canvas.</summary>
    bool ContainsTextContainer(object container);

    /// <summary>The annotation payload stored behind the container (sticky note model etc).</summary>
    object GetOverlayData(object container);

    /// <summary>Attach an annotation payload to the container (cross-page transfer).</summary>
    void SetOverlayData(object container, object data);

    /// <summary>
    /// Raw encoded image bytes (PNG/JPEG) behind an image container, or null
    /// for non-image containers (WPF GetImageData). The payload dict is
    /// per-host, so a cross-page move must hand the bytes to the receiving
    /// host — the default no-op keeps text-only test hosts valid.
    /// </summary>
    byte[] GetImageData(object container) => null;

    /// <summary>Register image bytes for a container that arrived from another host.</summary>
    void SetImageData(object container, byte[] data) { }

    /// <summary>
    /// Re-add a persisted text-quad highlight to the host's highlight list
    /// and repaint it (WPF AddHighlight — undo/redo of a text-highlight
    /// gesture; default no-op for hosts without a highlight layer).
    /// </summary>
    void AddHighlight(HighlightAnnotation highlight) { }

    /// <summary>Remove a persisted text-quad highlight (WPF RemoveHighlight).</summary>
    void RemoveHighlight(HighlightAnnotation highlight) { }

    /// <summary>Move a sticky note marker to a clamped position; false when not a sticky container.</summary>
    bool SetStickyNotePositionQuiet(object container, PointD position);

    /// <summary>Update sticky note text; false when not a sticky container.</summary>
    bool SetStickyNoteTextQuiet(object container, string text);

    /// <summary>Move a text container to a canvas position.</summary>
    void SetTextContainerPositionQuiet(object container, PointD position);

    /// <summary>
    /// Apply normalized bounds to a text container. autoWidth/autoHeight are
    /// the persist-as-auto flags (null leaves the current flag untouched).
    /// </summary>
    void SetTextContainerBoundsQuiet(object container, TextBoxBounds bounds, bool? autoWidth, bool? autoHeight);

    /// <summary>Set the editable text content; false when the container is gone.</summary>
    bool SetTextContentQuiet(object container, string text);

    /// <summary>Apply font size + foreground colour to the container's text.</summary>
    void SetTextStyleQuiet(object container, double fontSize, byte r, byte g, byte b);

    /// <summary>Apply a full formatting snapshot to the container's text.</summary>
    void SetTextFormatQuiet(object container, TextFormatSnapshot format);

    /// <summary>Translate strokes + containers by a delta (WPF MoveItemsDirectly).</summary>
    void MoveItemsDirectly(
        IReadOnlyList<InkStrokeData> strokes,
        IReadOnlyList<object> containers,
        double deltaX, double deltaY);

    /// <summary>Scale strokes + containers around a centre point.</summary>
    void ScaleItemsDirectly(
        IReadOnlyList<InkStrokeData> strokes,
        IReadOnlyList<object> containers,
        double scaleFactor, PointD center);

    /// <summary>Rotate strokes + containers around a centre point.</summary>
    void RotateItemsDirectly(
        IReadOnlyList<InkStrokeData> strokes,
        IReadOnlyList<object> containers,
        double degrees, PointD center);

    /// <summary>
    /// Drops any live selection — the paste-undo path clears it before
    /// removing the pasted items so it cannot reference dead containers
    /// (WPF ItemsAddedAction.UndoAsync → page.ClearSelection).
    /// </summary>
    void ClearSelection();
}

/// <summary>
/// Guarded container transfers shared by the annotation undo actions.
/// Every leg verifies the remove actually detached the container, wraps
/// the add (a hosted container throws on re-parent in WinUI), and checks
/// membership afterwards — a failed leg rolls the container back onto the
/// source so it never ends up unhosted (WPF guarded-transfer parity).
/// </summary>
internal static class AnnotationContainerTransfer
{
    /// <summary>Quiet remove; false when the host no longer owns the container.</summary>
    public static bool Remove(IAnnotationContainerHost host, object container)
    {
        if (host == null || container == null)
            return false;
        try
        {
            return host.RemoveTextContainerQuiet(container);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Quiet add + membership verification; false when the host rejected
    /// the container (already parented elsewhere, detached canvas, …).
    /// </summary>
    public static bool Add(IAnnotationContainerHost host, object container)
    {
        if (host == null || container == null)
            return false;
        try
        {
            host.AddTextContainerQuiet(container);
        }
        catch
        {
            return false;
        }
        return host.ContainsTextContainer(container);
    }

    /// <summary>
    /// Moves one container between hosts carrying its overlay payload —
    /// sticky-note models follow the marker (WPF TransferOverlayData).
    /// False legs roll the container back onto the source host.
    /// </summary>
    public static bool Move(
        IAnnotationContainerHost from, IAnnotationContainerHost to, object container)
    {
        if (from == null || to == null || container == null)
            return false;

        object data;
        try
        {
            data = from.GetOverlayData(container);
        }
        catch
        {
            data = null;
        }

        if (!Remove(from, container))
            return false;
        if (!Add(to, container))
        {
            // Roll the container back onto the source — partial transfers
            // must not leave it unhosted.
            Remove(to, container);
            Add(from, container);
            return false;
        }
        if (data != null)
        {
            try
            {
                to.SetOverlayData(container, data);
            }
            catch
            {
                // Payload loss is non-fatal: the container landed.
            }
        }

        // Image payloads live in a per-host dictionary just like overlay
        // models (WPF TransferImageData): the bytes must follow the
        // reparented container or a cross-page move renders but loses the
        // image on save/copy. Target-side null check keeps an existing
        // registration authoritative (WPF parity).
        byte[] imageData;
        try
        {
            imageData = from.GetImageData(container);
        }
        catch
        {
            imageData = null;
        }
        if (imageData != null)
        {
            try
            {
                if (to.GetImageData(container) == null)
                    to.SetImageData(container, imageData);
            }
            catch
            {
                // Payload loss is non-fatal: the container landed.
            }
        }
        return true;
    }
}

/// <summary>Immutable bold/italic/family/alignment snapshot (WPF TextFormatChangedAction before/after).</summary>
public readonly record struct TextFormatSnapshot(
    bool Bold,
    bool Italic,
    string FontFamily,
    string Alignment);

/// <summary>Immutable font-size + RGB snapshot (WPF TextStyleChangedAction before/after).</summary>
public readonly record struct TextStyleSnapshot(double FontSize, byte R, byte G, byte B);

// ------------------------------------------------------------------
// Text box lifecycle
// ------------------------------------------------------------------

/// <summary>A text box was created. Undo removes the container; redo re-adds it.</summary>
public sealed class TextBoxAddedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;

    public TextBoxAddedAction(IAnnotationContainerHost host, object container)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    public string Description => "Add text annotation";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.RemoveTextContainerQuiet(_container);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.AddTextContainerQuiet(_container);
        return Task.CompletedTask;
    }
}

/// <summary>A text box was deleted. Undo re-adds the container; redo removes it.</summary>
public sealed class TextBoxDeletedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;

    public TextBoxDeletedAction(IAnnotationContainerHost host, object container)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    public string Description => "Delete text annotation";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.AddTextContainerQuiet(_container);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.RemoveTextContainerQuiet(_container);
        return Task.CompletedTask;
    }
}

// ------------------------------------------------------------------
// Text box edits / transforms
// ------------------------------------------------------------------

/// <summary>
/// One focus session's net text change (WPF TextEditSessionAction). Undo
/// restores the text captured when editing began; redo replays the
/// committed result. No-op sessions are never pushed.
/// </summary>
public sealed class TextEditSessionAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;
    private readonly string _beforeText;
    private readonly string _afterText;

    public TextEditSessionAction(
        IAnnotationContainerHost host, object container, string beforeText, string afterText)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _beforeText = beforeText ?? string.Empty;
        _afterText = afterText ?? string.Empty;
    }

    public string Description => "Edit text annotation";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.SetTextContentQuiet(_container, _beforeText);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.SetTextContentQuiet(_container, _afterText);
        return Task.CompletedTask;
    }
}

/// <summary>Font-size + colour change on a text box (WPF TextStyleChangedAction).</summary>
public sealed class TextStyleChangedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;
    private readonly TextStyleSnapshot _before;
    private readonly TextStyleSnapshot _after;

    public TextStyleChangedAction(
        IAnnotationContainerHost host, object container,
        TextStyleSnapshot before, TextStyleSnapshot after)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _before = before;
        _after = after;
    }

    public string Description => "Change text style";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.SetTextStyleQuiet(_container, _before.FontSize, _before.R, _before.G, _before.B);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.SetTextStyleQuiet(_container, _after.FontSize, _after.R, _after.G, _after.B);
        return Task.CompletedTask;
    }
}

/// <summary>Bold/italic/family/alignment change (WPF TextFormatChangedAction).</summary>
public sealed class TextFormatChangedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;
    private readonly TextFormatSnapshot _before;
    private readonly TextFormatSnapshot _after;

    public TextFormatChangedAction(
        IAnnotationContainerHost host, object container,
        TextFormatSnapshot before, TextFormatSnapshot after)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _before = before;
        _after = after;
    }

    public string Description => "Change text format";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.SetTextFormatQuiet(_container, _before);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.SetTextFormatQuiet(_container, _after);
        return Task.CompletedTask;
    }
}

/// <summary>A text box drag committed a new position (WPF TextBoxMovedAction).</summary>
public sealed class TextBoxMovedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;
    private readonly PointD _before;
    private readonly PointD _after;

    public TextBoxMovedAction(
        IAnnotationContainerHost host, object container, PointD before, PointD after)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _before = before;
        _after = after;
    }

    public string Description => "Move text annotation";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.SetTextContainerPositionQuiet(_container, _before);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.SetTextContainerPositionQuiet(_container, _after);
        return Task.CompletedTask;
    }
}

/// <summary>
/// An eight-handle resize committed new bounds (WPF TextBoxResizedAction).
/// The auto-width/auto-height persist flags travel with the bounds so undo
/// restores the exact layout mode the box had before the gesture.
/// </summary>
public sealed class TextBoxResizedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;
    private readonly TextBoxBounds _before;
    private readonly TextBoxBounds _after;
    private readonly bool? _beforeAutoWidth;
    private readonly bool? _beforeAutoHeight;
    private readonly bool? _afterAutoWidth;
    private readonly bool? _afterAutoHeight;

    public TextBoxResizedAction(
        IAnnotationContainerHost host, object container,
        TextBoxBounds before, TextBoxBounds after,
        bool? beforeAutoWidth, bool? beforeAutoHeight,
        bool? afterAutoWidth, bool? afterAutoHeight)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _before = before;
        _after = after;
        _beforeAutoWidth = beforeAutoWidth;
        _beforeAutoHeight = beforeAutoHeight;
        _afterAutoWidth = afterAutoWidth;
        _afterAutoHeight = afterAutoHeight;
    }

    public string Description => "Resize text annotation";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.SetTextContainerBoundsQuiet(
            _container, _before, _beforeAutoWidth, _beforeAutoHeight);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.SetTextContainerBoundsQuiet(
            _container, _after, _afterAutoWidth, _afterAutoHeight);
        return Task.CompletedTask;
    }
}

// ------------------------------------------------------------------
// Sticky notes
// ------------------------------------------------------------------

/// <summary>A sticky note was placed. Undo removes the marker; redo re-adds it.</summary>
public sealed class StickyNoteAddedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;

    public StickyNoteAddedAction(IAnnotationContainerHost host, object container)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    public string Description => "Add sticky note";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.RemoveTextContainerQuiet(_container);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.AddTextContainerQuiet(_container);
        return Task.CompletedTask;
    }
}

/// <summary>A sticky note was deleted. Undo re-adds the marker; redo removes it.</summary>
public sealed class StickyNoteDeletedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;

    public StickyNoteDeletedAction(IAnnotationContainerHost host, object container)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    public string Description => "Delete sticky note";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.AddTextContainerQuiet(_container);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.RemoveTextContainerQuiet(_container);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A sticky marker moved (drag or keyboard nudge). Positions are applied
/// through the quiet setter so page-bounds clamping stays in force on
/// undo/redo (WPF StickyNoteMovedAction).
/// </summary>
public sealed class StickyNoteMovedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;
    private readonly PointD _before;
    private readonly PointD _after;

    public StickyNoteMovedAction(
        IAnnotationContainerHost host, object container, PointD before, PointD after)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _before = before;
        _after = after;
    }

    public string Description => "Move sticky note";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.SetStickyNotePositionQuiet(_container, _before);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.SetStickyNotePositionQuiet(_container, _after);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A sticky note editor commit (Save). Undo/redo replay the text through the
/// quiet setter; if the container was deleted the model payload still keeps
/// the reverted value so a later restore shows the right text (WPF
/// StickyNoteEditAction fallback).
/// </summary>
public sealed class StickyNoteEditAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly object _container;
    private readonly StickyNoteAnnotation _note;
    private readonly string _beforeText;
    private readonly string _afterText;

    public StickyNoteEditAction(
        IAnnotationContainerHost host, object container,
        StickyNoteAnnotation note, string beforeText, string afterText)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _note = note ?? throw new ArgumentNullException(nameof(note));
        _beforeText = beforeText ?? string.Empty;
        _afterText = afterText ?? string.Empty;
    }

    public string Description => "Edit sticky note";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        if (!_host.SetStickyNoteTextQuiet(_container, _beforeText))
            _note.Text = _beforeText;
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        if (!_host.SetStickyNoteTextQuiet(_container, _afterText))
            _note.Text = _afterText;
        return Task.CompletedTask;
    }
}

// ------------------------------------------------------------------
// Combined selection transforms (strokes + containers, one undo step)
// ------------------------------------------------------------------

/// <summary>
/// Mixed-selection move committed as ONE undo step (WPF SelectionMoveAction
/// with containers). The host replays ±delta through MoveItemsDirectly so
/// strokes and containers always travel together.
/// </summary>
public sealed class AnnotationSelectionMoveAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly IReadOnlyList<InkStrokeData> _strokes;
    private readonly IReadOnlyList<object> _containers;
    private readonly double _dx;
    private readonly double _dy;

    public AnnotationSelectionMoveAction(
        IAnnotationContainerHost host,
        IReadOnlyList<InkStrokeData> strokes,
        IReadOnlyList<object> containers,
        double deltaX, double deltaY)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _strokes = strokes ?? Array.Empty<InkStrokeData>();
        _containers = containers ?? Array.Empty<object>();
        _dx = deltaX;
        _dy = deltaY;
    }

    public string Description => "Move items";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.MoveItemsDirectly(_strokes, _containers, -_dx, -_dy);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.MoveItemsDirectly(_strokes, _containers, _dx, _dy);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Mixed-selection resize committed as ONE undo step (WPF
/// SelectionResizeAction with containers). Undo applies the inverse scale.
/// </summary>
public sealed class AnnotationSelectionResizeAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly IReadOnlyList<InkStrokeData> _strokes;
    private readonly IReadOnlyList<object> _containers;
    private readonly double _totalScale;
    private readonly PointD _anchor;

    public AnnotationSelectionResizeAction(
        IAnnotationContainerHost host,
        IReadOnlyList<InkStrokeData> strokes,
        IReadOnlyList<object> containers,
        double totalScale, PointD anchor)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _strokes = strokes ?? Array.Empty<InkStrokeData>();
        _containers = containers ?? Array.Empty<object>();
        _totalScale = totalScale;
        _anchor = anchor;
    }

    public string Description => "Resize items";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        if (Math.Abs(_totalScale) < 0.0001)
            return Task.CompletedTask;
        _host.ScaleItemsDirectly(_strokes, _containers, 1.0 / _totalScale, _anchor);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.ScaleItemsDirectly(_strokes, _containers, _totalScale, _anchor);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Mixed-selection rotation committed as ONE undo step (WPF
/// SelectionRotateAction with containers).
/// </summary>
public sealed class AnnotationSelectionRotateAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly IReadOnlyList<InkStrokeData> _strokes;
    private readonly IReadOnlyList<object> _containers;
    private readonly double _degrees;
    private readonly PointD _center;

    public AnnotationSelectionRotateAction(
        IAnnotationContainerHost host,
        IReadOnlyList<InkStrokeData> strokes,
        IReadOnlyList<object> containers,
        double degrees, PointD center)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _strokes = strokes ?? Array.Empty<InkStrokeData>();
        _containers = containers ?? Array.Empty<object>();
        _degrees = degrees;
        _center = center;
    }

    public string Description => "Rotate items";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.RotateItemsDirectly(_strokes, _containers, -_degrees, _center);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.RotateItemsDirectly(_strokes, _containers, _degrees, _center);
        return Task.CompletedTask;
    }
}

// ------------------------------------------------------------------
// Persistent text-quad highlights (Task 8 Phase B)
// ------------------------------------------------------------------

/// <summary>
/// A text-quad highlight was created from a PDF text selection (WPF
/// HighlightAddedAction). The model lives on the host's highlight list —
/// not a container — so undo/redo replay through the host's highlight
/// mutators rather than the container transfer path.
/// </summary>
public sealed class HighlightAddedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly HighlightAnnotation _highlight;

    public HighlightAddedAction(IAnnotationContainerHost host, HighlightAnnotation highlight)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _highlight = highlight ?? throw new ArgumentNullException(nameof(highlight));
    }

    public string Description => "Add highlight";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.RemoveHighlight(_highlight);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.AddHighlight(_highlight);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A persisted text-quad highlight was removed. Undo re-adds the model;
/// redo removes it again (mirror of <see cref="HighlightAddedAction"/>).
/// </summary>
public sealed class HighlightRemovedAction : IUndoAction
{
    private readonly IAnnotationContainerHost _host;
    private readonly HighlightAnnotation _highlight;

    public HighlightRemovedAction(IAnnotationContainerHost host, HighlightAnnotation highlight)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _highlight = highlight ?? throw new ArgumentNullException(nameof(highlight));
    }

    public string Description => "Remove highlight";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _host.AddHighlight(_highlight);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _host.RemoveHighlight(_highlight);
        return Task.CompletedTask;
    }
}

// ------------------------------------------------------------------
// Combined add/remove + cross-page move
// ------------------------------------------------------------------

/// <summary>
/// A mixed paste (strokes + containers) committed as ONE undoable unit —
/// WPF ItemsAddedAction. Undo clears the live selection first (paste
/// auto-selects) then removes strokes (descending index) and containers;
/// redo restores strokes at their captured placements then re-adds the
/// containers.
/// </summary>
public sealed class AnnotationItemsAddedAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly IAnnotationContainerHost _host;
    private readonly List<InkStrokePlacement> _placements;
    private readonly IReadOnlyList<object> _containers;

    public AnnotationItemsAddedAction(
        InkStrokeStore store,
        List<InkStrokePlacement> placements,
        IAnnotationContainerHost host,
        IReadOnlyList<object> containers)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _placements = placements ?? new List<InkStrokePlacement>();
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _containers = containers ?? Array.Empty<object>();
    }

    public string Description => "Add items";
    public bool LeavesDocumentDirty => true;

    /// <summary>
    /// False when a container leg no-opped (host no longer owns the
    /// container) — the editor keeps the action on the stack for a retry,
    /// same contract as the cross-page move actions.
    /// </summary>
    public bool LastOperationSucceeded { get; private set; } = true;

    public Task UndoAsync()
    {
        _host.ClearSelection();
        foreach (var placement in _placements.OrderByDescending(p => p.Index))
            _store.RemoveStrokeQuiet(placement);
        bool ok = true;
        foreach (var container in _containers)
            ok &= AnnotationContainerTransfer.Remove(_host, container);
        LastOperationSucceeded = ok;
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        foreach (var placement in _placements.OrderBy(p => p.Index))
            _store.AddStrokeQuiet(placement.ForOwner(_store, placement.Index));
        bool ok = true;
        foreach (var container in _containers)
            ok &= AnnotationContainerTransfer.Add(_host, container);
        LastOperationSucceeded = ok;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A mixed selection (strokes + containers) deleted as ONE undoable unit —
/// WPF ItemsRemovedAction. Undo restores strokes at their captured
/// placements (ascending index) and re-adds every container; redo removes
/// both legs again (placements descending so z-order indices stay valid).
/// </summary>
public sealed class AnnotationItemsRemovedAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly IAnnotationContainerHost _host;
    private readonly List<InkStrokePlacement> _placements;
    private readonly IReadOnlyList<object> _containers;

    public AnnotationItemsRemovedAction(
        InkStrokeStore store,
        List<InkStrokePlacement> placements,
        IAnnotationContainerHost host,
        IReadOnlyList<object> containers)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _placements = placements ?? new List<InkStrokePlacement>();
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _containers = containers ?? Array.Empty<object>();
    }

    public string Description => "Remove items";
    public bool LeavesDocumentDirty => true;

    /// <summary>False when a container leg no-opped — see <see cref="AnnotationItemsAddedAction"/>.</summary>
    public bool LastOperationSucceeded { get; private set; } = true;

    public Task UndoAsync()
    {
        var restored = new List<InkStrokeData>(_placements.Count);
        foreach (var placement in _placements.OrderBy(p => p.Index))
        {
            _store.AddStrokeQuiet(placement.ForOwner(_store, placement.Index));
            if (placement.Stroke != null)
                restored.Add(placement.Stroke);
        }
        bool ok = true;
        foreach (var container in _containers)
            ok &= AnnotationContainerTransfer.Add(_host, container);
        if (restored.Count > 0)
            _store.NotifyGeometryChanged(restored);
        LastOperationSucceeded = ok;
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        foreach (var placement in _placements.OrderByDescending(p => p.Index))
            _store.RemoveStrokeQuiet(placement);
        bool ok = true;
        foreach (var container in _containers)
            ok &= AnnotationContainerTransfer.Remove(_host, container);
        LastOperationSucceeded = ok;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A mixed selection dragged across pages committed as ONE undoable unit —
/// WPF SelectionCrossPageMoveAction. The stroke leg mirrors
/// <see cref="InkSelectionCrossPageMoveAction"/> (pre-transfer spine adjust
/// so the target's visuals build at the final coordinates); the container
/// leg moves each container between the two hosts, transferring its
/// annotation payload so sticky-note models follow the marker.
/// </summary>
public sealed class AnnotationSelectionCrossPageMoveAction : IUndoAction
{
    private static readonly IReadOnlyList<InkStrokeData> NoStrokes =
        Array.Empty<InkStrokeData>();

    private readonly InkStrokeStore _sourceStore;
    private readonly InkStrokeStore _targetStore;
    private readonly IAnnotationContainerHost _sourceHost;
    private readonly IAnnotationContainerHost _targetHost;
    private readonly List<InkStrokeData> _strokes;
    private readonly List<object> _containers;
    private readonly double _dx;
    private readonly double _dy;
    private readonly double _adjustX;
    private readonly double _adjustY;
    private readonly List<InkStrokePlacement> _sourcePlacements;

    public AnnotationSelectionCrossPageMoveAction(
        InkStrokeStore sourceStore,
        InkStrokeStore targetStore,
        IAnnotationContainerHost sourceHost,
        IAnnotationContainerHost targetHost,
        IReadOnlyList<InkStrokeData> strokes,
        IReadOnlyList<object> containers,
        double dx, double dy,
        double adjustX, double adjustY,
        List<InkStrokePlacement> sourcePlacements)
    {
        _sourceStore = sourceStore ?? throw new ArgumentNullException(nameof(sourceStore));
        _targetStore = targetStore ?? throw new ArgumentNullException(nameof(targetStore));
        _sourceHost = sourceHost ?? throw new ArgumentNullException(nameof(sourceHost));
        _targetHost = targetHost ?? throw new ArgumentNullException(nameof(targetHost));
        _strokes = strokes?.ToList() ?? new List<InkStrokeData>();
        _containers = containers?.ToList() ?? new List<object>();
        _dx = dx;
        _dy = dy;
        _adjustX = adjustX;
        _adjustY = adjustY;
        _sourcePlacements = sourcePlacements ?? new List<InkStrokePlacement>();
    }

    public string Description => "Move items across pages";
    public bool LeavesDocumentDirty => true;
    public bool LastOperationSucceeded { get; private set; } = true;

    /// <summary>The target-store indices the strokes landed in (post-transfer).</summary>
    public List<int> TargetIndices { get; } = new();
    private bool _containersTransferred;

    /// <summary>
    /// Moves strokes + containers into the target and applies the
    /// container→page coordinate adjust (WPF ExecuteInitialTransfer →
    /// targetPage.MoveItemsDirectly parity); idempotent. Returns false when
    /// nothing could be transferred — the editor then skips the undo push.
    /// </summary>
    public bool ExecuteInitialTransfer()
    {
        if (TargetIndices.Count > 0 || _containersTransferred)
            return true;

        try
        {
            // Strokes: translate BEFORE the add so the target's Added mutation
            // builds the visual at the final coordinates (same contract as
            // InkSelectionCrossPageMoveAction).
            var sorted = _strokes
                .OrderBy(s => _sourceStore.IndexOf(s))
                .Where(s => _sourceStore.IndexOf(s) >= 0)
                .ToList();
            foreach (var stroke in sorted)
            {
                _sourceStore.RemoveStrokeQuiet(stroke);
                StrokeGeometry.TranslateSpinePoints(stroke.Points, _adjustX, _adjustY);
                _targetStore.AddStrokeQuiet(stroke);
                TargetIndices.Add(_targetStore.IndexOf(stroke));
            }
            if (sorted.Count > 0)
                _targetStore.NotifyGeometryChanged(sorted);

            // Containers: guarded transfer — a container the source no
            // longer hosts (or the target rejects) is skipped instead of
            // splitting the gesture's state mid-flight.
            foreach (var container in _containers)
            {
                if (AnnotationContainerTransfer.Move(_sourceHost, _targetHost, container))
                    _containersTransferred = true;
                else
                    LastOperationSucceeded = false;
            }

            // The container→page coordinate adjust applies to the containers
            // here (strokes were pre-adjusted above). WPF replays the same
            // adjust through MoveItemsDirectly for both legs.
            if (_containersTransferred)
            {
                var moved = _containers
                    .Where(c => _targetHost.ContainsTextContainer(c))
                    .ToList();
                if (moved.Count > 0)
                    _targetHost.MoveItemsDirectly(NoStrokes, moved, _adjustX, _adjustY);
            }
        }
        catch
        {
            // A host failure mid-gesture must not corrupt the editor path —
            // report whatever actually transferred so the caller can still
            // push an undo for the completed leg.
            LastOperationSucceeded = false;
        }

        return TargetIndices.Count > 0 || _containersTransferred;
    }

    public Task UndoAsync()
    {
        var live = _strokes.Where(s => _targetStore.IndexOf(s) >= 0)
            .OrderByDescending(s => _targetStore.IndexOf(s))
            .ToList();
        if (live.Count != _strokes.Count)
        {
            LastOperationSucceeded = false;
            return Task.CompletedTask;
        }

        foreach (var stroke in live)
            _targetStore.RemoveStrokeQuiet(stroke);
        foreach (var placement in _sourcePlacements.OrderBy(p => p.Index))
            _sourceStore.AddStrokeQuiet(placement.ForOwner(_sourceStore, placement.Index));

        bool containersOk = true;
        foreach (var container in _containers)
            containersOk &= AnnotationContainerTransfer.Move(_targetHost, _sourceHost, container);

        double totalDx = _dx + _adjustX, totalDy = _dy + _adjustY;
        foreach (var stroke in _strokes)
            StrokeGeometry.TranslateSpinePoints(stroke.Points, -totalDx, -totalDy);
        if (_strokes.Count > 0)
            _sourceStore.NotifyGeometryChanged(_strokes);
        // Adjust only the containers that actually made it back.
        var movedBack = _containers
            .Where(c => _sourceHost.ContainsTextContainer(c))
            .ToList();
        if (movedBack.Count > 0)
            _sourceHost.MoveItemsDirectly(NoStrokes, movedBack, -totalDx, -totalDy);

        LastOperationSucceeded = containersOk;
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        var live = _strokes.Where(s => _sourceStore.IndexOf(s) >= 0).ToList();
        if (live.Count != _strokes.Count)
        {
            LastOperationSucceeded = false;
            return Task.CompletedTask;
        }

        double totalDx = _dx + _adjustX, totalDy = _dy + _adjustY;
        foreach (var stroke in _strokes)
            StrokeGeometry.TranslateSpinePoints(stroke.Points, totalDx, totalDy);
        foreach (var stroke in live.OrderByDescending(s => _sourceStore.IndexOf(s)))
            _sourceStore.RemoveStrokeQuiet(stroke);
        foreach (var stroke in _strokes)
            _targetStore.AddStrokeQuiet(stroke);
        if (_strokes.Count > 0)
            _targetStore.NotifyGeometryChanged(_strokes);

        bool containersOk = true;
        foreach (var container in _containers)
            containersOk &= AnnotationContainerTransfer.Move(_sourceHost, _targetHost, container);
        var moved = _containers
            .Where(c => _targetHost.ContainsTextContainer(c))
            .ToList();
        if (moved.Count > 0)
            _targetHost.MoveItemsDirectly(NoStrokes, moved, totalDx, totalDy);

        LastOperationSucceeded = containersOk;
        return Task.CompletedTask;
    }
}
