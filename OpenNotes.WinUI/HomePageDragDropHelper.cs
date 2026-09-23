using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caelum.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Caelum.Pages
{
    /// <summary>
    /// WinUI port of the WPF <c>Pages/HomePage.DragDropHelper.cs</c>. The WPF
    /// helper inspected <c>IDataObject</c> synchronously; WinUI exposes the
    /// payload as a <see cref="DataPackageView"/> whose storage items can only
    /// be enumerated asynchronously, so the surface splits in two:
    ///
    /// <list type="bullet">
    /// <item>Synchronous <c>Contains</c> probes for DragOver highlight
    /// decisions (<see cref="HasLibraryTilePaths"/>,
    /// <see cref="HasStorageItems"/>).</item>
    /// <item>Async payload readers used from Drop handlers and
    /// DragOver deferrals (<see cref="GetLibraryTilePathsAsync"/>,
    /// <see cref="GetDroppedImportablePathsAsync"/>).</item>
    /// </list>
    ///
    /// The in-app library-tile payload travels as a newline-joined string
    /// under the same custom format names as WPF —
    /// <c>Caelum.LibraryTilePath[s]</c> — set on the drag
    /// <see cref="DataPackage"/> in <c>FileTile_DragStarting</c>.
    /// </summary>
    internal static class HomePageDragDropHelper
    {
        internal const string LibraryTilePathDataFormat = "Caelum.LibraryTilePath";
        internal const string LibraryTilePathsDataFormat = "Caelum.LibraryTilePaths";

        /// <summary>Joins tile paths for <c>DataPackage.SetData</c>.</summary>
        internal static string PackLibraryTilePaths(IEnumerable<string> paths)
        {
            return string.Join("\n", NormalizePaths(paths));
        }

        internal static bool HasLibraryTilePaths(DataPackageView data)
        {
            return data != null &&
                   (data.Contains(LibraryTilePathsDataFormat) || data.Contains(LibraryTilePathDataFormat));
        }

        internal static bool HasStorageItems(DataPackageView data)
        {
            return data != null && data.Contains(StandardDataFormats.StorageItems);
        }

        internal static async Task<string[]> GetLibraryTilePathsAsync(DataPackageView data)
        {
            if (data == null)
                return Array.Empty<string>();

            try
            {
                if (data.Contains(LibraryTilePathsDataFormat) &&
                    await data.GetDataAsync(LibraryTilePathsDataFormat) is string packed)
                {
                    return NormalizePaths(packed.Split('\n'));
                }

                if (data.Contains(LibraryTilePathDataFormat) &&
                    await data.GetDataAsync(LibraryTilePathDataFormat) is string single)
                {
                    return NormalizePaths(new[] { single });
                }
            }
            catch
            {
                // Malformed custom payload — treat as "not a library drag".
            }

            return Array.Empty<string>();
        }

        internal static async Task<string[]> GetDroppedImportablePathsAsync(DataPackageView data)
        {
            if (!HasStorageItems(data))
                return Array.Empty<string>();

            try
            {
                var items = await data.GetStorageItemsAsync();
                if (items == null || items.Count == 0)
                    return Array.Empty<string>();

                return NormalizePaths(items
                    .OfType<StorageFile>()
                    .Select(item => item.Path)
                    .Where(WordDocumentImport.IsImportablePath));
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// DragOver-safe approximation of the WPF
        /// <c>HasSupportedFolderDropPayload</c>: storage items cannot be
        /// enumerated synchronously, so this accepts any file drop and the
        /// Drop handler filters by <see cref="WordDocumentImport.IsImportablePath"/>.
        /// </summary>
        internal static bool HasSupportedFolderDropPayload(DataPackageView data)
        {
            return HasLibraryTilePaths(data) || HasStorageItems(data);
        }

        private static string[] NormalizePaths(IEnumerable<string> paths)
        {
            return (paths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
