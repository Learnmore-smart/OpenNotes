using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caelum.InkGeometry;
using Caelum.Models;

namespace Caelum.Ink;

/// <summary>
/// UI-free undo contract for ink mutations — the Core port of the WPF
/// editor's private IUndoAction. Async shape retained for parity (undo
/// paths in the WPF editor are async); all current actions complete
/// synchronously.
/// </summary>
public interface IUndoAction
{
    string Description { get; }
    bool LeavesDocumentDirty { get; }
    Task UndoAsync();
    Task RedoAsync();
}

/// <summary>
/// A user stroke was added. Undo removes it quietly; redo reinserts it at
/// its captured placement (clamped index, original token).
/// </summary>
public sealed class InkStrokeAddedAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly InkStrokePlacement _placement;

    public InkStrokeAddedAction(InkStrokeStore store, InkStrokeData stroke)
        : this(store, store?.CaptureStrokePlacement(stroke))
    {
    }

    public InkStrokeAddedAction(InkStrokeStore store, InkStrokePlacement placement)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
    }

    public string Description => "Add stroke";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _store.RemoveStrokeQuiet(_placement);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _store.AddStrokeQuiet(_placement.ForOwner(_store, _placement.Index));
        return Task.CompletedTask;
    }
}

/// <summary>
/// One erase gesture = one undoable operation: the strokes the eraser
/// removed (whole or originals) and the fragments it added back. Mirrors
/// the WPF StrokesErasedAction ordering exactly:
/// undo removes fragments (descending index) then restores originals
/// (ascending index); redo is the mirror. Every step resolves the CURRENT
/// live stroke by token/side — recognition or polishing may have replaced
/// the captured reference between the gesture and a later undo/redo —
/// and a mid-sequence failure rolls the gesture back so history never
/// half-applies.
/// </summary>
public sealed class InkStrokesErasedAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly List<InkStrokePlacement> _removedOriginals;
    private readonly List<InkStrokePlacement> _addedFragments;

    public InkStrokesErasedAction(
        InkStrokeStore store,
        List<InkStrokePlacement> removedOriginals,
        List<InkStrokePlacement> addedFragments)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _removedOriginals = removedOriginals ?? new List<InkStrokePlacement>();
        _addedFragments = addedFragments ?? new List<InkStrokePlacement>();
    }

    public string Description => "Erase strokes";
    public bool LeavesDocumentDirty => true;
    public bool LastOperationSucceeded { get; private set; }

    public Task UndoAsync()
    {
        LastOperationSucceeded = false;
        var removedFragments = new List<InkStrokePlacement>();
        var restoredOriginals = new List<InkStrokePlacement>();
        try
        {
            foreach (var fragment in _addedFragments.OrderByDescending(p => p.Index))
            {
                if (!TryRemoveCurrentPlacement(fragment, out var removedFragment))
                {
                    RollbackUndo(removedFragments, restoredOriginals);
                    return Task.CompletedTask;
                }

                removedFragments.Add(removedFragment);
            }

            foreach (var original in _removedOriginals.OrderBy(p => p.Index))
            {
                var restored = _store.AddStrokeQuiet(original.ForOwner(_store, original.Index));
                if (restored == null)
                {
                    RollbackUndo(removedFragments, restoredOriginals);
                    return Task.CompletedTask;
                }

                restoredOriginals.Add(restored);
            }

            LastOperationSucceeded = true;
        }
        catch
        {
            RollbackUndo(removedFragments, restoredOriginals);
        }

        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        LastOperationSucceeded = false;
        var removedOriginals = new List<InkStrokePlacement>();
        var restoredFragments = new List<InkStrokePlacement>();
        try
        {
            foreach (var original in _removedOriginals.OrderByDescending(p => p.Index))
            {
                if (!TryRemoveCurrentPlacement(original, out var removedOriginal))
                {
                    RollbackRedo(removedOriginals, restoredFragments);
                    return Task.CompletedTask;
                }

                removedOriginals.Add(removedOriginal);
            }

            foreach (var fragment in _addedFragments.OrderBy(p => p.Index))
            {
                var restored = _store.AddStrokeQuiet(fragment.ForOwner(_store, fragment.Index));
                if (restored == null)
                {
                    RollbackRedo(removedOriginals, restoredFragments);
                    return Task.CompletedTask;
                }

                restoredFragments.Add(restored);
            }

            LastOperationSucceeded = true;
        }
        catch
        {
            RollbackRedo(removedOriginals, restoredFragments);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Erase history follows the logical token/side identity, because
    /// recognition can replace a stroke reference between the original
    /// gesture and a later redo. Capture the resolved live placement first,
    /// then remove that exact instance so rollback never targets a stale or
    /// unrelated reference.
    /// </summary>
    private bool TryRemoveCurrentPlacement(
        InkStrokePlacement expected,
        out InkStrokePlacement removed)
    {
        removed = null;
        if (!_store.TryCaptureCurrentStrokePlacement(expected, out var current)
            || !_store.RemoveStrokeQuietExact(current))
        {
            return false;
        }

        removed = current;
        return true;
    }

    private void RollbackUndo(
        IReadOnlyList<InkStrokePlacement> removedFragments,
        IReadOnlyList<InkStrokePlacement> restoredOriginals)
    {
        for (int index = restoredOriginals.Count - 1; index >= 0; index--)
            _store.RemoveStrokeQuietExact(restoredOriginals[index]);

        foreach (var fragment in removedFragments.OrderBy(p => p.Index))
            _store.AddStrokeQuiet(fragment.ForOwner(_store, fragment.Index));
    }

    private void RollbackRedo(
        IReadOnlyList<InkStrokePlacement> removedOriginals,
        IReadOnlyList<InkStrokePlacement> restoredFragments)
    {
        for (int index = restoredFragments.Count - 1; index >= 0; index--)
            _store.RemoveStrokeQuietExact(restoredFragments[index]);

        foreach (var original in removedOriginals.OrderBy(p => p.Index))
            _store.AddStrokeQuiet(original.ForOwner(_store, original.Index));
    }
}

/// <summary>
/// A scribble shape-recognition replaced one tokenized stroke in place.
/// Only immutable snapshots are retained, so erase/other actions can make a
/// later undo or redo a safe no-op. Ported from the WPF
/// StrokeReplacedAction — kept in Phase A because the erase undo path
/// resolves through token/side identity even when no recognizer ran yet.
/// </summary>
public sealed class InkStrokeReplacedAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly Guid _token;
    private readonly int _originalIndex;
    private readonly StrokeReplacementSnapshot _originalSnapshot;
    private readonly StrokeReplacementSnapshot _idealSnapshot;

    public InkStrokeReplacedAction(
        InkStrokeStore store,
        Guid token,
        int originalIndex,
        StrokeReplacementSnapshot originalSnapshot,
        StrokeReplacementSnapshot idealSnapshot)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _token = token;
        _originalIndex = originalIndex;
        _originalSnapshot = originalSnapshot;
        _idealSnapshot = idealSnapshot;
    }

    public string Description => "Replace stroke";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _store.TryReplaceStrokeQuiet(
            _token,
            StrokeReplacementSide.Ideal,
            _originalSnapshot,
            out _);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _store.TryReplaceStrokeQuiet(
            _token,
            StrokeReplacementSide.Original,
            _idealSnapshot,
            out _);
        return Task.CompletedTask;
    }
}

// ------------------------------------------------------------------
// Task 7 Phase B — selection + shape-group + hidden-ink actions.
// Same contract as the Phase A actions: quiet store primitives only;
// token/side identity is used wherever a live reference could have been
// replaced (recognition) between the gesture and a later undo/redo.
// ------------------------------------------------------------------

/// <summary>
/// A multi-stroke add committed as ONE undoable unit — shape-group commits
/// (arrow shaft+head, baked dashes) and any future multi-stroke insert.
/// Undo removes all parts (descending index), redo reinserts them at their
/// captured placements (ascending index). Ported from the WPF
/// ItemsAddedAction minus its container handling.
/// </summary>
public sealed class InkStrokesAddedAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly List<InkStrokePlacement> _placements;

    public InkStrokesAddedAction(InkStrokeStore store, List<InkStrokePlacement> placements)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _placements = placements ?? new List<InkStrokePlacement>();
    }

    public string Description => "Add items";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        foreach (var placement in _placements.OrderByDescending(p => p.Index))
            _store.RemoveStrokeQuiet(placement);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        foreach (var placement in _placements.OrderBy(p => p.Index))
            _store.AddStrokeQuiet(placement.ForOwner(_store, placement.Index));
        return Task.CompletedTask;
    }
}

/// <summary>
/// A multi-stroke removal committed as ONE undoable unit — Delete on a
/// selection. Undo restores all strokes at their captured placements
/// (ascending index); redo removes them again (descending). Ported from the
/// WPF ItemsRemovedAction minus containers.
/// </summary>
public sealed class InkStrokesRemovedAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly List<InkStrokePlacement> _placements;

    public InkStrokesRemovedAction(InkStrokeStore store, List<InkStrokePlacement> placements)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _placements = placements ?? new List<InkStrokePlacement>();
    }

    public string Description => "Remove items";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        foreach (var placement in _placements.OrderBy(p => p.Index))
            _store.AddStrokeQuiet(placement.ForOwner(_store, placement.Index));
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        foreach (var placement in _placements.OrderByDescending(p => p.Index))
            _store.RemoveStrokeQuiet(placement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Selection move: undo applies −delta, redo +delta. The stroke spine is
/// mutated in place and the store is notified so surfaces refresh the
/// affected visuals. Skips strokes no longer in the store (post-erase
/// undo is a safe no-op), matching the WPF ItemsMoveAction which resolves
/// through the same live collection.
/// </summary>
public sealed class InkSelectionMoveAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly List<InkStrokeData> _strokes;
    private readonly double _dx;
    private readonly double _dy;

    public InkSelectionMoveAction(
        InkStrokeStore store, IReadOnlyList<InkStrokeData> strokes, double dx, double dy)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _strokes = strokes?.ToList() ?? new List<InkStrokeData>();
        _dx = dx;
        _dy = dy;
    }

    public string Description => "Move items";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync() => Apply(-_dx, -_dy);
    public Task RedoAsync() => Apply(_dx, _dy);

    private Task Apply(double dx, double dy)
    {
        var live = _strokes.Where(s => _store.IndexOf(s) >= 0).ToList();
        foreach (var stroke in live)
            Caelum.InkGeometry.StrokeGeometry.TranslateSpinePoints(stroke.Points, dx, dy);
        _store.NotifyGeometryChanged(live);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Uniform selection scale about a fixed anchor: undo scales by
/// 1/totalScale, redo by totalScale. Spine points are rescaled and the
/// stroke size is multiplied (WPF ScaleItemsDirectly parity).
/// </summary>
public sealed class InkSelectionResizeAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly List<InkStrokeData> _strokes;
    private readonly double _totalScale;
    private readonly PointD _anchor;

    public InkSelectionResizeAction(
        InkStrokeStore store,
        IReadOnlyList<InkStrokeData> strokes,
        double totalScale,
        PointD anchor)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _strokes = strokes?.ToList() ?? new List<InkStrokeData>();
        _totalScale = totalScale;
        _anchor = anchor;
    }

    public string Description => "Resize items";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync() => Apply(1.0 / _totalScale);
    public Task RedoAsync() => Apply(_totalScale);

    private Task Apply(double scale)
    {
        var live = _strokes.Where(s => _store.IndexOf(s) >= 0).ToList();
        foreach (var stroke in live)
        {
            Caelum.InkGeometry.StrokeGeometry.ScaleSpinePoints(stroke.Points, scale, _anchor);
            stroke.Size *= scale;
        }
        _store.NotifyGeometryChanged(live);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Selection rotation about the bounds centre: undo rotates by
/// −totalDegrees, redo by +totalDegrees (WPF ItemsRotateAction parity).
/// </summary>
public sealed class InkSelectionRotateAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly List<InkStrokeData> _strokes;
    private readonly double _totalDegrees;
    private readonly PointD _center;

    public InkSelectionRotateAction(
        InkStrokeStore store,
        IReadOnlyList<InkStrokeData> strokes,
        double totalDegrees,
        PointD center)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _strokes = strokes?.ToList() ?? new List<InkStrokeData>();
        _totalDegrees = totalDegrees;
        _center = center;
    }

    public string Description => "Rotate items";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync() => Apply(-_totalDegrees);
    public Task RedoAsync() => Apply(_totalDegrees);

    private Task Apply(double degrees)
    {
        var live = _strokes.Where(s => _store.IndexOf(s) >= 0).ToList();
        foreach (var stroke in live)
            Caelum.InkGeometry.StrokeGeometry.RotateSpinePoints(stroke.Points, degrees, _center);
        _store.NotifyGeometryChanged(live);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Cross-page selection move: strokes translate by (dx,dy) and transfer from
/// the source store to the target store in a single undoable operation.
/// Ported from the WPF SelectionCrossPageMoveAction minus containers.
/// <paramref name="adjustX"/>/<paramref name="adjustY"/> is the container→page
/// coordinate delta added on top of the pointer delta (WPF passes the
/// page-offset correction for strokes dragged between non-overlapping page
/// frames).
/// Call <see cref="ExecuteInitialTransfer"/> once after constructing.
/// </summary>
public sealed class InkSelectionCrossPageMoveAction : IUndoAction
{
    private readonly InkStrokeStore _sourceStore;
    private readonly InkStrokeStore _targetStore;
    private readonly List<InkStrokeData> _strokes;
    private readonly double _dx;
    private readonly double _dy;
    private readonly double _adjustX;
    private readonly double _adjustY;
    private readonly List<InkStrokePlacement> _sourcePlacements;

    public InkSelectionCrossPageMoveAction(
        InkStrokeStore sourceStore,
        InkStrokeStore targetStore,
        IReadOnlyList<InkStrokeData> strokes,
        double dx, double dy,
        double adjustX, double adjustY,
        List<InkStrokePlacement> sourcePlacements)
    {
        _sourceStore = sourceStore ?? throw new ArgumentNullException(nameof(sourceStore));
        _targetStore = targetStore ?? throw new ArgumentNullException(nameof(targetStore));
        _strokes = strokes?.ToList() ?? new List<InkStrokeData>();
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

    /// <summary>
    /// Moves the strokes into the target store; idempotent. Returns false
    /// when nothing could be transferred — the editor then skips the undo
    /// push (WPF SelectionCrossPageMoveAction parity).
    /// </summary>
    public bool ExecuteInitialTransfer()
    {
        if (TargetIndices.Count > 0)
            return true;
        var sorted = _strokes
            .OrderBy(s => _sourceStore.IndexOf(s))
            .Where(s => _sourceStore.IndexOf(s) >= 0)
            .ToList();
        foreach (var stroke in sorted)
        {
            _sourceStore.RemoveStrokeQuiet(stroke);
            _targetStore.AddStrokeQuiet(stroke);
            TargetIndices.Add(_targetStore.IndexOf(stroke));
        }
        return TargetIndices.Count > 0;
    }

    public Task UndoAsync()
    {
        // Reverse: pull back to the source store at captured placements and
        // re-apply the inverse transform.
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

        double totalDx = _dx + _adjustX, totalDy = _dy + _adjustY;
        foreach (var stroke in _strokes)
            Caelum.InkGeometry.StrokeGeometry.TranslateSpinePoints(stroke.Points, -totalDx, -totalDy);
        _sourceStore.NotifyGeometryChanged(_strokes);
        LastOperationSucceeded = true;
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
            Caelum.InkGeometry.StrokeGeometry.TranslateSpinePoints(stroke.Points, totalDx, totalDy);
        foreach (var stroke in live.OrderByDescending(s => _sourceStore.IndexOf(s)))
            _sourceStore.RemoveStrokeQuiet(stroke);
        foreach (var stroke in _strokes)
            _targetStore.AddStrokeQuiet(stroke);
        _targetStore.NotifyGeometryChanged(_strokes);
        LastOperationSucceeded = true;
        return Task.CompletedTask;
    }
}

// ------------------------------------------------------------------
// Hidden ink — masks live in their own store so these actions never touch
// the ordinary stroke ledger.
// ------------------------------------------------------------------

/// <summary>A hidden-ink mask was added; undo removes it (by Id).</summary>
public sealed class HiddenInkAddedAction : IUndoAction
{
    private readonly HiddenInkStore _store;
    private readonly HiddenInkAnnotation _item;

    public HiddenInkAddedAction(HiddenInkStore store, HiddenInkAnnotation item)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public string Description => "Add hidden ink";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _store.RemoveQuiet(_item);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _store.AddQuiet(_item);
        return Task.CompletedTask;
    }
}

/// <summary>A hidden-ink mask was removed; undo restores it at its index.</summary>
public sealed class HiddenInkRemovedAction : IUndoAction
{
    private readonly HiddenInkStore _store;
    private readonly HiddenInkAnnotation _item;
    private readonly int _index;

    public HiddenInkRemovedAction(HiddenInkStore store, HiddenInkAnnotation item, int index)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _index = index;
    }

    public string Description => "Remove hidden ink";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        _store.InsertQuiet(_index, _item);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        _store.RemoveQuiet(_item);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Batch hidden-ink removal from one erase gesture — undo restores all masks
/// at their captured indices (ascending), redo removes them again.
/// </summary>
public sealed class HiddenInksRemovedAction : IUndoAction
{
    private readonly HiddenInkStore _store;
    private readonly List<(HiddenInkAnnotation Item, int Index)> _entries;

    public HiddenInksRemovedAction(
        HiddenInkStore store, IEnumerable<(HiddenInkAnnotation Item, int Index)> entries)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _entries = entries?.ToList() ?? new List<(HiddenInkAnnotation, int)>();
    }

    public string Description => "Remove hidden inks";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync()
    {
        foreach (var entry in _entries.OrderBy(e => e.Index))
            _store.InsertQuiet(entry.Index, entry.Item);
        return Task.CompletedTask;
    }

    public Task RedoAsync()
    {
        foreach (var entry in _entries.OrderByDescending(e => e.Index))
            _store.RemoveQuiet(entry.Item);
        return Task.CompletedTask;
    }
}


/// <summary>
/// Selection drawing-style change (colour and/or stroke size) applied to a
/// group of strokes as ONE undoable unit — the WPF StrokeStyleChangedAction
/// port. Values are stored per stroke (before/after) so undo restores each
/// stroke's own prior appearance, not a single shared value. Strokes no
/// longer in the store are skipped.
/// </summary>
public sealed class InkStrokesStyleChangedAction : IUndoAction
{
    private readonly InkStrokeStore _store;
    private readonly Dictionary<InkStrokeData, (byte R, byte G, byte B, byte A, double Size)> _before;
    private readonly Dictionary<InkStrokeData, (byte R, byte G, byte B, byte A, double Size)> _after;

    public InkStrokesStyleChangedAction(
        InkStrokeStore store,
        Dictionary<InkStrokeData, (byte R, byte G, byte B, byte A, double Size)> before,
        Dictionary<InkStrokeData, (byte R, byte G, byte B, byte A, double Size)> after)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _before = before ?? new Dictionary<InkStrokeData, (byte, byte, byte, byte, double)>();
        _after = after ?? new Dictionary<InkStrokeData, (byte, byte, byte, byte, double)>();
    }

    public string Description => "Change stroke style";
    public bool LeavesDocumentDirty => true;

    public Task UndoAsync() => Apply(_before);
    public Task RedoAsync() => Apply(_after);

    private Task Apply(
        Dictionary<InkStrokeData, (byte R, byte G, byte B, byte A, double Size)> values)
    {
        var live = values.Keys.Where(s => _store.IndexOf(s) >= 0).ToList();
        foreach (var stroke in live)
        {
            var v = values[stroke];
            stroke.R = v.R;
            stroke.G = v.G;
            stroke.B = v.B;
            stroke.A = v.A;
            stroke.Size = v.Size;
        }
        _store.NotifyGeometryChanged(live);
        return Task.CompletedTask;
    }
}
