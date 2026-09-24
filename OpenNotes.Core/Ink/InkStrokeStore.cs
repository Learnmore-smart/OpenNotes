using System;
using System.Collections.Generic;
using System.Linq;
using Caelum.Models;

namespace Caelum.Ink;

/// <summary>
/// What changed in an <see cref="InkStrokeStore"/> mutation.
/// </summary>
public enum InkStoreMutationKind
{
    Added,
    Removed,
    Replaced,
    Cleared,
    /// <summary>
    /// In-place point/size mutation (selection move/resize/rotate). No list
    /// membership change — surfaces should refresh the stroke's visual only.
    /// </summary>
    GeometryChanged,
}

/// <summary>
/// Store-level change notification. <see cref="Quiet"/> is always true for
/// the quiet primitives — the flag exists so hosts can distinguish a
/// store-internal change (thumbnail invalidate only) from a user action
/// (dirty + undo), which surfaces through the ink surface's own events.
/// </summary>
public sealed class InkStoreMutationEventArgs : EventArgs
{
    public InkStoreMutationEventArgs(
        InkStoreMutationKind kind, InkStrokeData stroke, int index, bool quiet)
    {
        Kind = kind;
        Stroke = stroke;
        Index = index;
        Quiet = quiet;
    }

    public InkStoreMutationKind Kind { get; }
    public InkStrokeData Stroke { get; }
    public int Index { get; }
    public bool Quiet { get; }
}

/// <summary>
/// One stroke's position-and-identity record — the Core port of the WPF
/// page's StrokePlacement. The token/side pair is the logical identity that
/// survives stroke replacement; <see cref="Index"/> is the z-order slot the
/// stroke occupied when the placement was captured. Undo restores by
/// re-inserting <see cref="Stroke"/> at the (clamped) captured index.
/// </summary>
public sealed class InkStrokePlacement
{
    internal InkStrokePlacement(
        InkStrokeStore owner,
        InkStrokeData stroke,
        StrokeReplacementSnapshot snapshot,
        int index)
    {
        Owner = owner;
        Stroke = stroke;
        Snapshot = snapshot;
        Token = snapshot?.Token ?? Guid.Empty;
        Side = snapshot?.Side ?? StrokeReplacementSide.Original;
        Index = index;
    }

    public InkStrokeStore Owner { get; }
    public InkStrokeData Stroke { get; }
    public StrokeReplacementSnapshot Snapshot { get; }
    public Guid Token { get; }
    public StrokeReplacementSide Side { get; }
    public int Index { get; }

    /// <summary>
    /// Rebinds the placement to a (possibly different) owner and index —
    /// undo/redo always reinserts through this so a stale collection slot
    /// degrades to a clamped insert rather than corrupting ordering.
    /// </summary>
    public InkStrokePlacement ForOwner(InkStrokeStore owner, int index)
        => new(owner, Stroke, Snapshot, index);
}

/// <summary>
/// Ordered, tokenized stroke collection for one page — the UI-free
/// equivalent of the WPF page's StrokeCollection + placement bookkeeping.
/// Every stroke carries a stable <see cref="Guid"/> token and a
/// <see cref="StrokeReplacementSide"/> so undo/redo and eraser splits can
/// refer to the logical stroke even after the live reference is replaced
/// (shape recognition) or split (eraser).
/// All mutators are "quiet": they maintain identity bookkeeping and raise
/// <see cref="Mutated"/> with Quiet=true, but never create undo actions —
/// the ink surface/editor decides what enters history.
/// </summary>
public sealed class InkStrokeStore
{
    private readonly List<InkStrokeData> _strokes = new();
    private readonly Dictionary<InkStrokeData, StrokeReplacementSnapshot> _metadata =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<InkStrokeData, InkStrokePlacement> _placementHistory =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Raised after every quiet mutation (add/remove/replace/clear).</summary>
    public event EventHandler<InkStoreMutationEventArgs> Mutated;

    public IReadOnlyList<InkStrokeData> Strokes => _strokes;
    public int Count => _strokes.Count;

    public int IndexOf(InkStrokeData stroke) => _strokes.IndexOf(stroke);

    /// <summary>
    /// Returns the stroke's identity token, assigning one on first sight.
    /// New strokes start on the <see cref="StrokeReplacementSide.Original"/>
    /// side — shape replacement flips the side via
    /// <see cref="TryReplaceStrokeQuiet"/>.
    /// </summary>
    public Guid EnsureStrokeToken(InkStrokeData stroke)
    {
        if (stroke == null)
            return Guid.Empty;
        if (_metadata.TryGetValue(stroke, out var snapshot))
            return snapshot.Token;

        snapshot = stroke.CaptureSnapshot(Guid.NewGuid(), StrokeReplacementSide.Original);
        _metadata[stroke] = snapshot;
        return snapshot.Token;
    }

    /// <summary>
    /// The placement first captured for this stroke, or a fresh capture at
    /// the stroke's current index. Cached placements keep the token stable
    /// across undo bookkeeping even when indices shift.
    /// </summary>
    public InkStrokePlacement CaptureStrokePlacement(InkStrokeData stroke)
    {
        if (stroke == null)
            return null;
        EnsureStrokeToken(stroke);
        if (_placementHistory.TryGetValue(stroke, out var existing))
            return existing;

        var snapshot = _metadata[stroke];
        var placement = new InkStrokePlacement(
            this, stroke, snapshot, _strokes.IndexOf(stroke));
        _placementHistory[stroke] = placement;
        return placement;
    }

    /// <summary>Appends a stroke quietly; returns its captured placement.</summary>
    public InkStrokePlacement AddStrokeQuiet(InkStrokeData stroke)
    {
        if (stroke == null)
            return null;
        _strokes.Add(stroke);
        EnsureStrokeToken(stroke);
        var placement = CaptureStrokePlacement(stroke);
        RaiseMutated(InkStoreMutationKind.Added, stroke, _strokes.Count - 1);
        return placement;
    }

    /// <summary>
    /// Reinserts a stroke from a recorded placement at its (clamped) index —
    /// the undo/redo restore path. The placement's snapshot identity is
    /// reattached so the token and side survive the round-trip.
    /// </summary>
    public InkStrokePlacement AddStrokeQuiet(InkStrokePlacement placement)
    {
        if (placement == null || placement.Stroke == null || placement.Snapshot == null)
            return null;

        int index = Math.Max(0, Math.Min(placement.Index, _strokes.Count));
        _strokes.Insert(index, placement.Stroke);
        _metadata[placement.Stroke] = placement.Snapshot;
        var rebound = placement.ForOwner(this, index);
        _placementHistory[placement.Stroke] = rebound;
        RaiseMutated(InkStoreMutationKind.Added, placement.Stroke, index);
        return rebound;
    }

    /// <summary>
    /// Removes the stroke instance directly — the simple removal used by the
    /// eraser pipeline after it has resolved the live reference itself.
    /// </summary>
    public bool RemoveStrokeQuiet(InkStrokeData stroke)
    {
        if (stroke == null)
            return false;
        int index = _strokes.IndexOf(stroke);
        if (index < 0)
            return false;

        _strokes.RemoveAt(index);
        _metadata.Remove(stroke);
        _placementHistory.Remove(stroke);
        RaiseMutated(InkStoreMutationKind.Removed, stroke, index);
        return true;
    }

    /// <summary>
    /// Removes the current live stroke matching a recorded placement's
    /// token/side — safe when the original reference was replaced.
    /// </summary>
    public bool RemoveStrokeQuiet(InkStrokePlacement placement)
    {
        if (placement == null)
            return false;
        return TryCaptureCurrentStrokePlacement(placement.Token, placement.Side, out var current)
            && RemoveStrokeQuietExact(current);
    }

    /// <summary>
    /// Removes exactly the captured live instance — no token resolution.
    /// Fails if the stroke already left the collection.
    /// </summary>
    public bool RemoveStrokeQuietExact(InkStrokePlacement placement)
    {
        if (placement == null || placement.Stroke == null)
            return false;
        int index = _strokes.IndexOf(placement.Stroke);
        if (index < 0)
            return false;

        _strokes.RemoveAt(index);
        _metadata.Remove(placement.Stroke);
        _placementHistory.Remove(placement.Stroke);
        RaiseMutated(InkStoreMutationKind.Removed, placement.Stroke, index);
        return true;
    }

    /// <summary>
    /// Resolves the live stroke currently carrying
    /// <paramref name="expected"/>'s token on the expected side and captures
    /// its placement now (fresh index). False when nothing matches — the
    /// caller treats that as a safe no-op, never an append.
    /// </summary>
    public bool TryCaptureCurrentStrokePlacement(
        InkStrokePlacement expected, out InkStrokePlacement current)
        => TryCaptureCurrentStrokePlacement(
            expected?.Token ?? Guid.Empty,
            expected?.Side ?? StrokeReplacementSide.Original,
            out current);

    /// <summary>Token/side lookup for the live stroke — see overload.</summary>
    public bool TryCaptureCurrentStrokePlacement(
        Guid token, StrokeReplacementSide side, out InkStrokePlacement current)
    {
        current = null;
        if (token == Guid.Empty)
            return false;

        for (int i = 0; i < _strokes.Count; i++)
        {
            var stroke = _strokes[i];
            if (_metadata.TryGetValue(stroke, out var snapshot)
                && snapshot.Token == token && snapshot.Side == side)
            {
                current = new InkStrokePlacement(this, stroke, snapshot, i);
                _placementHistory[stroke] = current;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Token-identity replacement: swaps the stroke currently carrying
    /// <paramref name="token"/> on <paramref name="expectedSide"/> for a new
    /// live stroke built from <paramref name="replacement"/>, in place. Fails
    /// (false, index −1) without touching the collection when the token is
    /// absent or on the wrong side — the deterministic-ledger contract.
    /// </summary>
    public bool TryReplaceStrokeQuiet(
        Guid token,
        StrokeReplacementSide expectedSide,
        StrokeReplacementSnapshot replacement,
        out int index)
    {
        index = -1;
        if (token == Guid.Empty || replacement == null || replacement.Token != token)
            return false;

        for (int i = 0; i < _strokes.Count; i++)
        {
            var old = _strokes[i];
            if (!_metadata.TryGetValue(old, out var snapshot)
                || snapshot.Token != token || snapshot.Side != expectedSide)
            {
                continue;
            }

            var next = InkStrokeData.FromSnapshot(replacement);
            _strokes[i] = next;
            _metadata.Remove(old);
            _metadata[next] = replacement;
            _placementHistory.Remove(old);
            var placement = new InkStrokePlacement(this, next, replacement, i);
            _placementHistory[next] = placement;
            index = i;
            RaiseMutated(InkStoreMutationKind.Replaced, next, i);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The current replacement side of the stroke carrying
    /// <paramref name="token"/>, or null when no such stroke exists.
    /// </summary>
    public StrokeReplacementSide? GetStrokeSide(Guid token)
    {
        foreach (var stroke in _strokes)
        {
            if (_metadata.TryGetValue(stroke, out var snapshot) && snapshot.Token == token)
                return snapshot.Side;
        }
        return null;
    }

    /// <summary>
    /// Notifies that these strokes' spine points/size were mutated in place
    /// (selection move/scale/rotate). Strokes no longer in the store are
    /// skipped so undo of a stale selection is a safe no-op.
    /// </summary>
    public void NotifyGeometryChanged(IEnumerable<InkStrokeData> strokes)
    {
        if (strokes == null)
            return;
        foreach (var stroke in strokes)
        {
            int index = _strokes.IndexOf(stroke);
            if (index >= 0)
                RaiseMutated(InkStoreMutationKind.GeometryChanged, stroke, index);
        }
    }

    /// <summary>Empties the collection quietly.</summary>
    public void Clear()
    {
        if (_strokes.Count == 0)
            return;
        _strokes.Clear();
        _metadata.Clear();
        _placementHistory.Clear();
        RaiseMutated(InkStoreMutationKind.Cleared, null, -1);
    }

    private void RaiseMutated(InkStoreMutationKind kind, InkStrokeData stroke, int index)
        => Mutated?.Invoke(this, new InkStoreMutationEventArgs(kind, stroke, index, quiet: true));
}
