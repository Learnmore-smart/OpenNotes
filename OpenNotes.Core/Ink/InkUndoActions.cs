using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
