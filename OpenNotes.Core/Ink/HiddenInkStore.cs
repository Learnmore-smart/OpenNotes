using System;
using System.Collections.Generic;
using Caelum.Models;

namespace Caelum.Ink;

/// <summary>
/// Ordered, UI-free per-page collection of <see cref="HiddenInkAnnotation"/>
/// masks — the hidden-ink counterpart of <see cref="InkStrokeStore"/>.
/// Masks are intentionally kept out of the ordinary stroke store so lasso
/// selection and stroke erasing never touch them (WPF parity: the masks live
/// on HiddenInkCanvas, not on InkCanvas).
/// All mutators are "quiet" and raise <see cref="Changed"/>; undo ownership
/// belongs to the editor via the HiddenInk*Action types.
/// </summary>
public sealed class HiddenInkStore
{
    private readonly List<HiddenInkAnnotation> _items = new();

    /// <summary>Raised after every quiet mutation (add/insert/remove/clear).</summary>
    public event EventHandler Changed;

    public IReadOnlyList<HiddenInkAnnotation> Items => _items;
    public int Count => _items.Count;

    /// <summary>Index lookup by annotation Id (identity survives clones).</summary>
    public int IndexOf(HiddenInkAnnotation item)
    {
        if (item == null)
            return -1;
        for (int i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].Id, item.Id, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    public bool Contains(HiddenInkAnnotation item) => IndexOf(item) >= 0;

    /// <summary>Appends a mask after sanitizing; returns the stored instance.</summary>
    public HiddenInkAnnotation AddQuiet(HiddenInkAnnotation item)
        => InsertQuiet(_items.Count, item);

    /// <summary>
    /// Inserts at a clamped index — the undo restore path. Sanitizes like
    /// AddQuiet so restored masks keep a stable unique Id.
    /// </summary>
    public HiddenInkAnnotation InsertQuiet(int index, HiddenInkAnnotation item)
    {
        if (item == null)
            return null;
        Sanitize(item);
        index = Math.Max(0, Math.Min(index, _items.Count));
        _items.Insert(index, item);
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    /// <summary>Removes the mask carrying the item's Id.</summary>
    public bool RemoveQuiet(HiddenInkAnnotation item)
    {
        int index = IndexOf(item);
        if (index < 0)
            return false;
        _items.RemoveAt(index);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Empties the store quietly.</summary>
    public void Clear()
    {
        if (_items.Count == 0)
            return;
        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Mirrors WPF AddHiddenInk defaults: a mask must have an Id, a positive
    /// size, a fully-opaque channel and a positive reveal duration.
    /// </summary>
    private void Sanitize(HiddenInkAnnotation item)
    {
        if (string.IsNullOrEmpty(item.Id))
            item.Id = Guid.NewGuid().ToString("N");
        else
        {
            // Keep Ids unique — a duplicated loaded id would collide in the
            // page's visual dictionary.
            while (_items.Exists(existing =>
                string.Equals(existing.Id, item.Id, StringComparison.Ordinal)))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }
        }
        if (item.Size <= 0)
            item.Size = 28.0;
        if (item.A == 0)
            item.A = 255;
        if (item.RevealDurationMs <= 0)
            item.RevealDurationMs = HiddenInkRevealState.DefaultRevealDurationMs;
    }
}
