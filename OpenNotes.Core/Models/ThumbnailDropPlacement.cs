using System;

namespace Caelum.Models;

/// <summary>
/// Converts a thumbnail insertion placement into the final page index used by
/// the PDF reorder operation.
/// </summary>
public static class ThumbnailDropPlacement
{
    /// <summary>
    /// Resolves a target row placed before or after it. The row is expressed in
    /// the original page list, while the returned index is expressed after the
    /// source page has been removed.
    /// </summary>
    public static int ResolveFinalIndex(
        int sourceIndex,
        int targetRow,
        bool placeBefore,
        int pageCount)
    {
        int slot = placeBefore
            ? targetRow
            : targetRow == int.MaxValue
                ? int.MaxValue
                : targetRow + 1;

        return ResolveFinalIndex(sourceIndex, slot, pageCount);
    }

    /// <summary>
    /// Resolves an insertion slot in the original page list. Valid slots are
    /// between zero and <paramref name="pageCount"/> inclusive; the upper
    /// bound represents insertion at the end. The source page is removed
    /// before the returned final index is calculated.
    /// </summary>
    public static int ResolveFinalIndex(int sourceIndex, int slot, int pageCount)
    {
        if (pageCount <= 0)
            return -1;

        int clampedSlot = Math.Clamp(slot, 0, pageCount);
        int finalIndex = clampedSlot > sourceIndex
            ? clampedSlot - 1
            : clampedSlot;

        return Math.Clamp(finalIndex, 0, pageCount - 1);
    }
}
