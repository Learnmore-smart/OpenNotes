using Caelum.Models;

namespace Caelum.Tests;

public class ThumbnailDropPlacementTests
{
    [TestCase(0, 3, true, 2)]
    [TestCase(0, 3, false, 3)]
    [TestCase(3, 1, true, 1)]
    [TestCase(3, 1, false, 2)]
    [TestCase(0, 1, true, 0)]
    [TestCase(0, 1, false, 1)]
    [TestCase(2, 3, true, 2)]
    [TestCase(2, 3, false, 3)]
    public void ResolveFinalIndex_AccountsForSourceRemoval(
        int source,
        int targetRow,
        bool placeBefore,
        int expected)
    {
        Assert.That(
            ThumbnailDropPlacement.ResolveFinalIndex(source, targetRow, placeBefore, 4),
            Is.EqualTo(expected));
    }

    [TestCase(1, 0, false, 1)]
    [TestCase(1, 1, true, 1)]
    [TestCase(1, 1, false, 1)]
    [TestCase(1, 2, true, 1)]
    [TestCase(3, 3, true, 3)]
    [TestCase(3, 3, false, 3)]
    public void ResolveFinalIndex_ReturnsSameIndexForAdjacentOrSelfPlacement(
        int source,
        int targetRow,
        bool placeBefore,
        int expected)
    {
        Assert.That(
            ThumbnailDropPlacement.ResolveFinalIndex(source, targetRow, placeBefore, 4),
            Is.EqualTo(expected));
    }

    [TestCase(2, -1, 4, 0)]
    [TestCase(2, 99, 4, 3)]
    [TestCase(0, 4, 4, 3)]
    public void ResolveFinalIndex_ClampsDirectInsertionSlots(
        int source,
        int slot,
        int pageCount,
        int expected)
    {
        Assert.That(
            ThumbnailDropPlacement.ResolveFinalIndex(source, slot, pageCount),
            Is.EqualTo(expected));
    }
}
