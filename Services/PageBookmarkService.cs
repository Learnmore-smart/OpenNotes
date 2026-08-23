using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Caelum.Services
{
    public sealed class PageBookmark
    {
        public int PageIndex { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>
    /// Describes a page-list mutation that can change the zero-based index of a bookmark.
    /// </summary>
    public enum PageBookmarkPageOperation
    {
        Insert,
        Delete,
        Move
    }

    /// <summary>Task 31: local, path-keyed custom page bookmarks.</summary>
    public static class PageBookmarkService
    {
        /// <summary>Maximum number of bookmark records accepted by one exact snapshot replacement.</summary>
        public const int MaxBookmarksPerDocument = 100_000;

        private static readonly object SyncRoot = new object();
        private static readonly string BookmarkPath = Path.Combine(
            ProductInfo.GetDataDirectory(), "bookmarks.json");

        public static IReadOnlyList<PageBookmark> Load(string filePath)
        {
            lock (SyncRoot)
            {
                try
                {
                    var all = ReadAll();
                    if (!all.TryGetValue(Normalize(filePath), out var bookmarks))
                        return Array.Empty<PageBookmark>();

                    return NormalizeBookmarks(bookmarks);
                }
                catch
                {
                    return Array.Empty<PageBookmark>();
                }
            }
        }

        public static IReadOnlyList<PageBookmark> Toggle(string filePath, int pageIndex)
        {
            ValidatePageIndex(pageIndex, nameof(pageIndex));

            lock (SyncRoot)
            {
                var all = ReadAll();
                string key = Normalize(filePath);
                if (!all.TryGetValue(key, out var bookmarks))
                    bookmarks = all[key] = new List<PageBookmark>();

                bookmarks = NormalizeBookmarks(bookmarks).ToList();
                all[key] = bookmarks;

                var existing = bookmarks.FirstOrDefault(bookmark => bookmark.PageIndex == pageIndex);
                if (existing != null)
                    bookmarks.Remove(existing);
                else
                    bookmarks.Add(new PageBookmark
                    {
                        PageIndex = pageIndex,
                        Label = LocalizationService.Format("Editor.BookmarkPage", pageIndex + 1)
                    });

                SaveAll(all);
                return NormalizeBookmarks(bookmarks);
            }
        }

        public static string GetDisplayLabel(PageBookmark bookmark)
        {
            if (bookmark == null)
                return string.Empty;

            string label = bookmark.Label?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(label))
                return LocalizationService.Format("Editor.BookmarkPage", bookmark.PageIndex + 1);

            foreach (var option in LocalizationService.GetLanguageOptions())
            {
                string generatedLabel = LocalizationService.FormatForLanguage(
                    "Editor.BookmarkPage", option.Language, bookmark.PageIndex + 1);
                if (string.Equals(label, generatedLabel, StringComparison.Ordinal))
                    return LocalizationService.Format("Editor.BookmarkPage", bookmark.PageIndex + 1);
            }

            return label;
        }

        /// <summary>
        /// Remaps an in-memory bookmark list after inserting a page at <paramref name="insertedPageIndex" />.
        /// The input list is never mutated. This overload is intentionally pure so page-editing callers
        /// can update bookmarks in the same operation as their PDF mutation and tests can verify the
        /// index rules without touching the user's AppData.
        /// </summary>
        public static IReadOnlyList<PageBookmark> RemapForInsert(
            IEnumerable<PageBookmark> bookmarks,
            int insertedPageIndex)
        {
            return RemapForInsert(bookmarks, insertedPageIndex, 1);
        }

        /// <summary>
        /// Remaps an in-memory bookmark list after inserting a contiguous range of pages.
        /// The input list is never mutated and every bookmark at or after the insertion point
        /// shifts by the full number of inserted pages.
        /// </summary>
        public static IReadOnlyList<PageBookmark> RemapForInsert(
            IEnumerable<PageBookmark> bookmarks,
            int insertedPageIndex,
            int insertedPageCount)
        {
            ValidateInsertPageArguments(insertedPageIndex, insertedPageCount);

            return Remap(bookmarks, bookmark =>
                bookmark.PageIndex >= insertedPageIndex
                    ? checked(bookmark.PageIndex + insertedPageCount)
                    : bookmark.PageIndex);
        }

        /// <summary>
        /// Remaps an in-memory bookmark list after deleting a page. The deleted page's bookmark
        /// is removed and later pages shift one position toward the front.
        /// </summary>
        public static IReadOnlyList<PageBookmark> RemapForDelete(
            IEnumerable<PageBookmark> bookmarks,
            int deletedPageIndex)
        {
            ValidatePageIndex(deletedPageIndex, nameof(deletedPageIndex));

            return NormalizeBookmarks(bookmarks)
                .Where(bookmark => bookmark.PageIndex != deletedPageIndex)
                .Select(bookmark => new PageBookmark
                {
                    PageIndex = bookmark.PageIndex > deletedPageIndex
                        ? bookmark.PageIndex - 1
                        : bookmark.PageIndex,
                    Label = bookmark.Label
                })
                .ToList();
        }

        /// <summary>
        /// Remaps an in-memory bookmark list after moving one page from one zero-based index to another.
        /// Pages between the source and destination shift by one; the moved page receives the destination index.
        /// </summary>
        public static IReadOnlyList<PageBookmark> RemapForMove(
            IEnumerable<PageBookmark> bookmarks,
            int fromPageIndex,
            int toPageIndex)
        {
            ValidatePageIndex(fromPageIndex, nameof(fromPageIndex));
            ValidatePageIndex(toPageIndex, nameof(toPageIndex));

            if (fromPageIndex == toPageIndex)
                return NormalizeBookmarks(bookmarks);

            return Remap(bookmarks, bookmark =>
            {
                int pageIndex = bookmark.PageIndex;
                if (pageIndex == fromPageIndex)
                    return toPageIndex;

                if (fromPageIndex < toPageIndex && pageIndex > fromPageIndex && pageIndex <= toPageIndex)
                    return pageIndex - 1;

                if (fromPageIndex > toPageIndex && pageIndex >= toPageIndex && pageIndex < fromPageIndex)
                    return checked(pageIndex + 1);

                return pageIndex;
            });
        }

        /// <summary>
        /// Applies an insert remap to the persisted bookmarks for a document.
        /// </summary>
        public static IReadOnlyList<PageBookmark> ApplyPageInsert(string filePath, int insertedPageIndex)
        {
            return ApplyPageInsert(filePath, insertedPageIndex, 1);
        }

        /// <summary>
        /// Applies a contiguous multi-page insert remap to the persisted bookmarks for a document.
        /// </summary>
        public static IReadOnlyList<PageBookmark> ApplyPageInsert(
            string filePath,
            int insertedPageIndex,
            int insertedPageCount)
        {
            ValidateInsertPageArguments(insertedPageIndex, insertedPageCount);
            return UpdatePersistedBookmarks(
                filePath,
                bookmarks => RemapForInsert(bookmarks, insertedPageIndex, insertedPageCount));
        }

        /// <summary>
        /// Applies a delete remap to the persisted bookmarks for a document.
        /// </summary>
        public static IReadOnlyList<PageBookmark> ApplyPageDelete(string filePath, int deletedPageIndex)
        {
            ValidatePageIndex(deletedPageIndex, nameof(deletedPageIndex));
            return UpdatePersistedBookmarks(filePath, bookmarks => RemapForDelete(bookmarks, deletedPageIndex));
        }

        /// <summary>
        /// Applies a reorder remap to the persisted bookmarks for a document.
        /// </summary>
        public static IReadOnlyList<PageBookmark> ApplyPageMove(string filePath, int fromPageIndex, int toPageIndex)
        {
            ValidatePageIndex(fromPageIndex, nameof(fromPageIndex));
            ValidatePageIndex(toPageIndex, nameof(toPageIndex));
            return UpdatePersistedBookmarks(filePath, bookmarks => RemapForMove(bookmarks, fromPageIndex, toPageIndex));
        }

        /// <summary>
        /// Applies a page operation to persisted bookmarks. This is the single entry point for callers
        /// that already model page operations as a discriminated enum.
        /// </summary>
        public static IReadOnlyList<PageBookmark> ApplyPageOperation(
            string filePath,
            PageBookmarkPageOperation operation,
            int pageIndex,
            int destinationPageIndex = -1)
        {
            return operation switch
            {
                PageBookmarkPageOperation.Insert => ApplyPageInsert(filePath, pageIndex),
                PageBookmarkPageOperation.Delete => ApplyPageDelete(filePath, pageIndex),
                PageBookmarkPageOperation.Move => ApplyPageMove(filePath, pageIndex, destinationPageIndex),
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown page bookmark operation.")
            };
        }

        /// <summary>
        /// Replaces the persisted bookmark snapshot for a document. This is intended for undo/redo
        /// integration, where restoring PDF bytes must restore the matching sidecar state as well.
        /// </summary>
        public static IReadOnlyList<PageBookmark> Replace(string filePath, IEnumerable<PageBookmark> bookmarks)
        {
            ArgumentNullException.ThrowIfNull(bookmarks);

            var snapshot = MaterializeSnapshot(bookmarks);

            lock (SyncRoot)
            {
                var all = ReadAll();
                string key = Normalize(filePath);
                var normalized = NormalizeBookmarks(snapshot);

                if (normalized.Count == 0)
                {
                    if (all.Remove(key))
                        SaveAll(all);
                    return Array.Empty<PageBookmark>();
                }

                all[key] = normalized.Select(Clone).ToList();
                SaveAll(all);
                return NormalizeBookmarks(normalized);
            }
        }

        private static Dictionary<string, List<PageBookmark>> ReadAll()
        {
            try
            {
                if (!File.Exists(BookmarkPath))
                    return new Dictionary<string, List<PageBookmark>>(StringComparer.OrdinalIgnoreCase);

                var parsed = JsonSerializer.Deserialize<Dictionary<string, List<PageBookmark>>>(File.ReadAllText(BookmarkPath));
                return parsed == null
                    ? new Dictionary<string, List<PageBookmark>>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, List<PageBookmark>>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, List<PageBookmark>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveAll(Dictionary<string, List<PageBookmark>> all)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BookmarkPath) ?? string.Empty);
            string temporaryPath = $"{BookmarkPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, BookmarkPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static string Normalize(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            try
            {
                return Path.GetFullPath(filePath);
            }
            catch
            {
                return filePath.Trim();
            }
        }

        private static IReadOnlyList<PageBookmark> UpdatePersistedBookmarks(
            string filePath,
            Func<IEnumerable<PageBookmark>, IReadOnlyList<PageBookmark>> remap)
        {
            ArgumentNullException.ThrowIfNull(remap);

            lock (SyncRoot)
            {
                var all = ReadAll();
                string key = Normalize(filePath);
                if (!all.TryGetValue(key, out var bookmarks))
                    return Array.Empty<PageBookmark>();

                var remapped = remap(bookmarks);
                all[key] = remapped.Select(Clone).ToList();
                SaveAll(all);
                return NormalizeBookmarks(remapped);
            }
        }

        private static IReadOnlyList<PageBookmark> Remap(
            IEnumerable<PageBookmark> bookmarks,
            Func<PageBookmark, int> mapPageIndex)
        {
            ArgumentNullException.ThrowIfNull(bookmarks);
            ArgumentNullException.ThrowIfNull(mapPageIndex);

            return NormalizeBookmarks(bookmarks)
                .Select(bookmark => new PageBookmark
                {
                    PageIndex = mapPageIndex(bookmark),
                    Label = bookmark.Label
                })
                .OrderBy(bookmark => bookmark.PageIndex)
                .ToList();
        }

        private static IReadOnlyList<PageBookmark> NormalizeBookmarks(IEnumerable<PageBookmark> bookmarks)
        {
            if (bookmarks == null)
                return Array.Empty<PageBookmark>();

            return bookmarks
                .Where(bookmark => bookmark != null && bookmark.PageIndex >= 0)
                .GroupBy(bookmark => bookmark.PageIndex)
                .Select(group => Clone(group.First()))
                .OrderBy(bookmark => bookmark.PageIndex)
                .ToList();
        }

        private static IReadOnlyList<PageBookmark> MaterializeSnapshot(IEnumerable<PageBookmark> bookmarks)
        {
            var snapshot = new List<PageBookmark>();
            foreach (var bookmark in bookmarks)
            {
                if (snapshot.Count >= MaxBookmarksPerDocument)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(bookmarks),
                        $"A bookmark snapshot cannot contain more than {MaxBookmarksPerDocument:N0} records.");
                }

                snapshot.Add(bookmark);
            }

            return snapshot;
        }

        private static PageBookmark Clone(PageBookmark bookmark)
        {
            return new PageBookmark
            {
                PageIndex = bookmark.PageIndex,
                Label = bookmark.Label ?? string.Empty
            };
        }

        private static void ValidatePageIndex(int pageIndex, string parameterName)
        {
            if (pageIndex < 0)
                throw new ArgumentOutOfRangeException(parameterName, pageIndex, "Page indices must be zero-based and non-negative.");
        }

        private static void ValidateInsertPageIndex(int insertedPageIndex)
        {
            ValidatePageIndex(insertedPageIndex, nameof(insertedPageIndex));
            if (insertedPageIndex == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(insertedPageIndex), "The inserted page index must leave room for existing page indices to shift.");
        }

        private static void ValidateInsertPageArguments(int insertedPageIndex, int insertedPageCount)
        {
            ValidateInsertPageIndex(insertedPageIndex);
            if (insertedPageCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(insertedPageCount), insertedPageCount, "The inserted page count must be positive.");
            if (insertedPageCount > int.MaxValue - insertedPageIndex)
                throw new ArgumentOutOfRangeException(nameof(insertedPageCount), insertedPageCount, "The inserted page range must fit within the supported page-index range.");
        }
    }
}
