using System.Linq;
using Caelum.Services;

namespace OpenNotes.Tests;

[TestFixture]
public class PageBookmarkServiceTests
{
    [Test]
    public void RemapForInsert_ShiftsBookmarksAtAndAfterInsertedPage_WithoutMutatingInput()
    {
        var source = Bookmarks((0, "cover"), (2, "chapter"), (4, "summary"));

        var result = PageBookmarkService.RemapForInsert(source, 2);

        Assert.That(PageIndexes(result), Is.EqualTo(new[] { 0, 3, 5 }));
        Assert.That(result.Select(bookmark => bookmark.Label), Is.EqualTo(new[] { "cover", "chapter", "summary" }));
        Assert.That(PageIndexes(source), Is.EqualTo(new[] { 0, 2, 4 }));
    }

    [Test]
    public void RemapForInsert_WithContiguousPageCount_ShiftsLaterBookmarksByFullCount()
    {
        var source = Bookmarks((1, "before"), (3, "at-insert"), (6, "later"));

        var result = PageBookmarkService.RemapForInsert(source, 3, 3);

        Assert.That(PageIndexes(result), Is.EqualTo(new[] { 1, 6, 9 }));
        Assert.That(result.Select(bookmark => bookmark.Label), Is.EqualTo(new[] { "before", "at-insert", "later" }));
        Assert.That(PageIndexes(source), Is.EqualTo(new[] { 1, 3, 6 }));
    }

    [Test]
    public void RemapForDelete_RemovesDeletedBookmark_AndShiftsLaterPages()
    {
        var source = Bookmarks((1, "one"), (3, "deleted"), (5, "later"));

        var result = PageBookmarkService.RemapForDelete(source, 3);

        Assert.That(PageIndexes(result), Is.EqualTo(new[] { 1, 4 }));
        Assert.That(result.Select(bookmark => bookmark.Label), Is.EqualTo(new[] { "one", "later" }));
    }

    [Test]
    public void RemapForMove_ForwardMovesSourceAndShiftsPagesBetween()
    {
        var source = Bookmarks((0, "zero"), (1, "source"), (2, "middle"), (3, "destination"), (5, "after"));

        var result = PageBookmarkService.RemapForMove(source, 1, 3);

        Assert.That(PageIndexes(result), Is.EqualTo(new[] { 0, 1, 2, 3, 5 }));
        Assert.That(result.Select(bookmark => bookmark.Label), Is.EqualTo(new[] { "zero", "middle", "destination", "source", "after" }));
    }

    [Test]
    public void RemapForMove_BackwardMovesSourceAndShiftsPagesBetween()
    {
        var source = Bookmarks((0, "zero"), (2, "destination"), (3, "middle"), (4, "source"), (5, "after"));

        var result = PageBookmarkService.RemapForMove(source, 4, 2);

        Assert.That(PageIndexes(result), Is.EqualTo(new[] { 0, 2, 3, 4, 5 }));
        Assert.That(result.Select(bookmark => bookmark.Label), Is.EqualTo(new[] { "zero", "source", "destination", "middle", "after" }));
    }

    [Test]
    public void RemapForMove_WhenSourceAndDestinationMatch_ReturnsNormalizedClones()
    {
        var source = new[]
        {
            new PageBookmark { PageIndex = 2, Label = "last" },
            new PageBookmark { PageIndex = -1, Label = "invalid" },
            new PageBookmark { PageIndex = 2, Label = "duplicate" },
            new PageBookmark { PageIndex = 0, Label = null! }
        };

        var result = PageBookmarkService.RemapForMove(source, 2, 2);

        Assert.That(PageIndexes(result), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(result[0].Label, Is.Empty);
        Assert.That(result[1].Label, Is.EqualTo("last"));
        Assert.That(result[0], Is.Not.SameAs(source[3]));
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void RemapForInsert_RejectsNegativePageIndex(int pageIndex)
    {
        Assert.That(
            () => PageBookmarkService.RemapForInsert(Array.Empty<PageBookmark>(), pageIndex),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ApplyPageOperation_RequiresDestinationForMove()
    {
        Assert.That(
            () => PageBookmarkService.ApplyPageOperation(
                @"C:\missing\document.pdf",
                PageBookmarkPageOperation.Move,
                1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Replace_RejectsSnapshotsLargerThanTheServiceBound()
    {
        var oversizedSnapshot = Enumerable
            .Range(0, PageBookmarkService.MaxBookmarksPerDocument + 1)
            .Select(pageIndex => new PageBookmark { PageIndex = pageIndex });

        Assert.That(
            () => PageBookmarkService.Replace(@"C:\missing\document.pdf", oversizedSnapshot),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static PageBookmark[] Bookmarks(params (int PageIndex, string Label)[] values)
    {
        return values
            .Select(value => new PageBookmark { PageIndex = value.PageIndex, Label = value.Label })
            .ToArray();
    }

    private static int[] PageIndexes(IEnumerable<PageBookmark> bookmarks)
    {
        return bookmarks.Select(bookmark => bookmark.PageIndex).ToArray();
    }
}
