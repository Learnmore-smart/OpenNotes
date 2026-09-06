using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caelum.Services
{
    public sealed class RecentFileEntry
    {
        public string Id { get; set; } = string.Empty;

        public string EntryType { get; set; } = RecentFilesService.FileEntryType;

        public string ParentFolderId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public bool IsNotebook { get; set; }

        public string Path { get; set; } = string.Empty;

        public int PageCount { get; set; }

        public DateTime? LastModifiedUtc { get; set; }

        public DateTime LastOpenedUtc { get; set; }

        public string Color { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsFolder => string.Equals(EntryType, RecentFilesService.FolderEntryType, StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsFile => !IsFolder;
    }

    /// <summary>
    /// Stores library metadata for files and virtual folders in a local JSON file.
    /// </summary>
    public static class RecentFilesService
    {
        internal const string FileEntryType = "file";
        internal const string FolderEntryType = "folder";
        private static readonly object _lock = new();

        private static string FilePath
        {
            get
            {
                var dir = ProductInfo.GetDataDirectory();
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "recent_files.json");
            }
        }

        private static string BackupFilePath =>
            Path.Combine(ProductInfo.GetDataDirectory(), "recent_files.json.bak");

        private static string BookmarksFilePath =>
            Path.Combine(ProductInfo.GetDataDirectory(), "bookmarks.json");

        private static string LegacyFilePath =>
            Path.Combine(ProductInfo.GetDataDirectory(), "recent_files.txt");

        public static string GetLibraryDisplayName(RecentFileEntry entry)
        {
            if (entry == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(entry.DisplayName))
                return entry.DisplayName.Trim();

            if (string.IsNullOrWhiteSpace(entry.Path))
                return string.Empty;

            try
            {
                return Path.GetFileName(entry.Path);
            }
            catch (Exception)
            {
                return entry.Path.Trim();
            }
        }

        /// <summary>
        /// Returns the list of recent file entries (most recent first).
        /// </summary>
        public static List<RecentFileEntry> GetRecentEntries()
        {
            lock (_lock)
            {
                var entries = Load();
                if (PruneAndRefreshMetadata(entries))
                    Save(entries);

                return entries
                    .Where(entry => entry.IsFile)
                    .OrderByDescending(entry => entry.LastOpenedUtc)
                    .Select(CloneEntry)
                    .ToList();
            }
        }

        /// <summary>
        /// Returns the list of recent file paths (most recent first).
        /// </summary>
        public static List<string> GetRecentFiles()
        {
            lock (_lock)
            {
                return GetRecentEntries().Select(entry => entry.Path).ToList();
            }
        }

        public static List<RecentFileEntry> GetLibraryEntries(string parentFolderId = null)
        {
            lock (_lock)
            {
                var entries = Load();
                if (PruneAndRefreshMetadata(entries))
                    Save(entries);

                var normalizedParentFolderId = NormalizeFolderId(parentFolderId);
                return entries
                    .Where(entry => string.Equals(NormalizeFolderId(entry.ParentFolderId), normalizedParentFolderId, StringComparison.OrdinalIgnoreCase))
                    .Select(CloneEntry)
                    .ToList();
            }
        }

        public static RecentFileEntry GetFolder(string folderId)
        {
            if (string.IsNullOrWhiteSpace(folderId))
                return null;

            lock (_lock)
            {
                var entries = Load();
                var folder = entries.FirstOrDefault(entry =>
                    entry.IsFolder &&
                    string.Equals(entry.Id, folderId, StringComparison.OrdinalIgnoreCase));
                return folder == null ? null : CloneEntry(folder);
            }
        }

        public static int GetDirectChildCount(string folderId)
        {
            lock (_lock)
            {
                var entries = Load();
                var normalizedFolderId = NormalizeFolderId(folderId);
                return entries.Count(entry =>
                    string.Equals(NormalizeFolderId(entry.ParentFolderId), normalizedFolderId, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Adds or promotes a file path to the top of the recent list.
        /// </summary>
        public static void AddOrPromote(string filePath)
            => AddOrPromote(filePath, null, null);

        public static void AddOrPromote(string filePath, int? pageCount, DateTime? lastModifiedUtc)
            => AddOrPromote(filePath, pageCount, lastModifiedUtc, null, false);

        public static void AddOrPromote(string filePath, int? pageCount, DateTime? lastModifiedUtc, string parentFolderId, bool isNotebook = false)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            lock (_lock)
            {
                var entries = Load();
                var normalizedPath = NormalizePath(filePath);
                var existing = entries.FirstOrDefault(entry =>
                    entry.IsFile &&
                    string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
                var effectiveFolderId = ResolveParentFolderId(entries, existing, parentFolderId);

                if (existing == null)
                {
                    entries.Insert(0, CreateEntry(normalizedPath, pageCount, lastModifiedUtc, effectiveFolderId, isNotebook));
                }
                else
                {
                    existing.Path = normalizedPath;
                    existing.EntryType = FileEntryType;
                    existing.ParentFolderId = effectiveFolderId;
                    existing.IsNotebook = existing.IsNotebook || isNotebook;
                    existing.LastOpenedUtc = DateTime.UtcNow;

                    if (pageCount.HasValue)
                        existing.PageCount = pageCount.Value;

                    if (lastModifiedUtc.HasValue)
                        existing.LastModifiedUtc = lastModifiedUtc.Value;
                    else if (File.Exists(normalizedPath))
                        existing.LastModifiedUtc = File.GetLastWriteTimeUtc(normalizedPath);
                }

                Save(entries);
            }
        }

        /// <summary>
        /// Updates metadata for an existing recent file entry.
        /// </summary>
        public static void UpdateMetadata(string filePath, int? pageCount = null, DateTime? lastModifiedUtc = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            lock (_lock)
            {
                var entries = Load();
                var entry = entries.FirstOrDefault(item => string.Equals(item.Path, filePath, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    entries.Insert(0, CreateEntry(filePath, pageCount, lastModifiedUtc, null, false));
                }
                else
                {
                    if (pageCount.HasValue)
                        entry.PageCount = pageCount.Value;

                    if (lastModifiedUtc.HasValue)
                        entry.LastModifiedUtc = lastModifiedUtc.Value;
                    else if (File.Exists(filePath))
                        entry.LastModifiedUtc = File.GetLastWriteTimeUtc(filePath);
                }

                Save(entries);
            }
        }

        /// <summary>
        /// Removes a specific path from the recent list.
        /// </summary>
        public static void Remove(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            lock (_lock)
            {
                var entries = Load();
                entries.RemoveAll(entry => string.Equals(entry.Path, filePath, StringComparison.OrdinalIgnoreCase));
                Save(entries);
            }
        }

        public static RecentFileEntry CreateFolder(string name, string parentFolderId = null)
        {
            var trimmedName = name?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
                return null;

            lock (_lock)
            {
                var entries = Load();
                var folder = new RecentFileEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    EntryType = FolderEntryType,
                    ParentFolderId = NormalizeParentFolderId(entries, parentFolderId),
                    DisplayName = trimmedName,
                    LastModifiedUtc = DateTime.UtcNow,
                    LastOpenedUtc = DateTime.UtcNow
                };

                entries.Insert(0, folder);
                Save(entries);
                return CloneEntry(folder);
            }
        }

        public static bool RenameFolder(string folderId, string newName)
        {
            if (string.IsNullOrWhiteSpace(folderId) || string.IsNullOrWhiteSpace(newName))
                return false;

            lock (_lock)
            {
                var entries = Load();
                var folder = entries.FirstOrDefault(entry =>
                    entry.IsFolder &&
                    string.Equals(entry.Id, folderId, StringComparison.OrdinalIgnoreCase));
                if (folder == null)
                    return false;

                folder.DisplayName = newName.Trim();
                folder.LastModifiedUtc = DateTime.UtcNow;
                Save(entries);
                return true;
            }
        }

        public static bool RemoveFolder(string folderId)
        {
            if (string.IsNullOrWhiteSpace(folderId))
                return false;

            lock (_lock)
            {
                var entries = Load();
                var folder = entries.FirstOrDefault(entry =>
                    entry.IsFolder &&
                    string.Equals(entry.Id, folderId, StringComparison.OrdinalIgnoreCase));
                if (folder == null)
                    return false;

                var targetParentId = NormalizeFolderId(folder.ParentFolderId);
                foreach (var child in entries.Where(entry => string.Equals(NormalizeFolderId(entry.ParentFolderId), folderId, StringComparison.OrdinalIgnoreCase)))
                {
                    child.ParentFolderId = targetParentId;
                }

                entries.Remove(folder);
                Save(entries);
                return true;
            }
        }

        public static bool MoveToFolder(string filePath, string folderId)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            lock (_lock)
            {
                var entries = Load();
                var normalizedFolderId = NormalizeParentFolderId(entries, folderId);
                var normalizedPath = NormalizePath(filePath);
                var entry = entries.FirstOrDefault(item =>
                    item.IsFile &&
                    string.Equals(item.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    entry = CreateEntry(normalizedPath, null, null, normalizedFolderId, false);
                    entries.Insert(0, entry);
                }
                else
                {
                    entry.ParentFolderId = normalizedFolderId;
                    entry.LastOpenedUtc = DateTime.UtcNow;
                }

                Save(entries);
                return true;
            }
        }

        public static bool MoveToLibraryRoot(string filePath)
        {
            return MoveToFolder(filePath, null);
        }

        public static bool SetFolderColor(string folderId, string color)
        {
            if (string.IsNullOrWhiteSpace(folderId))
                return false;

            string normalized = NormalizeFolderColor(color);
            lock (_lock)
            {
                var entries = Load();
                var folder = entries.FirstOrDefault(entry =>
                    entry.IsFolder &&
                    string.Equals(entry.Id, folderId, StringComparison.OrdinalIgnoreCase));
                if (folder == null)
                    return false;

                folder.Color = normalized;
                Save(entries);
                return true;
            }
        }

        internal static string NormalizeFolderColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
                return string.Empty;

            string trimmed = color.Trim();
            if (trimmed.StartsWith('#') && (trimmed.Length == 7 || trimmed.Length == 9))
                return trimmed.ToUpperInvariant();

            return string.Empty;
        }

        public static bool UpdatePath(string oldPath, string newPath)
        {
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
                return false;

            lock (_lock)
            {
                var entries = Load();
                var normalizedOldPath = NormalizePath(oldPath);
                var normalizedNewPath = NormalizePath(newPath);
                var entry = entries.FirstOrDefault(item =>
                    item.IsFile &&
                    string.Equals(item.Path, normalizedOldPath, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    return false;

                entries.RemoveAll(item =>
                    !ReferenceEquals(item, entry) &&
                    item.IsFile &&
                    string.Equals(item.Path, normalizedNewPath, StringComparison.OrdinalIgnoreCase));

                entry.Path = normalizedNewPath;
                entry.LastModifiedUtc = File.Exists(normalizedNewPath)
                    ? File.GetLastWriteTimeUtc(normalizedNewPath)
                    : DateTime.UtcNow;
                entry.LastOpenedUtc = DateTime.UtcNow;
                Save(entries);
                return true;
            }
        }

        private static List<RecentFileEntry> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return RestoreIfEmpty(LoadLegacy());

                var json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json))
                    return RestoreIfEmpty(new List<RecentFileEntry>());

                try
                {
                    var entries = JsonSerializer.Deserialize<List<RecentFileEntry>>(json) ?? new List<RecentFileEntry>();
                    entries = RestoreIfEmpty(entries);
                    if (NormalizeEntries(entries))
                        Save(entries);

                    return entries;
                }
                catch (JsonException)
                {
                    var legacyPaths = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                    return RestoreIfEmpty(legacyPaths.Select(path => CreateEntry(path, null, null, null, false)).ToList());
                }
            }
            catch
            {
                return RestoreIfEmpty(new List<RecentFileEntry>());
            }
        }

        private static List<RecentFileEntry> RestoreIfEmpty(List<RecentFileEntry> entries)
        {
            entries ??= new List<RecentFileEntry>();
            if (entries.Any(entry =>
                    entry != null &&
                    (entry.IsFolder || (entry.IsFile && !string.IsNullOrWhiteSpace(entry.Path)))))
                return entries;

            var restored = TryReadEntries(BackupFilePath);
            if (restored.Count > 0)
            {
                Save(restored);
                return restored;
            }

            restored = RecoverFromBookmarks();
            if (restored.Count > 0)
                Save(restored);

            return restored.Count > 0 ? restored : entries;
        }

        private static List<RecentFileEntry> TryReadEntries(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new List<RecentFileEntry>();

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<RecentFileEntry>();

                var entries = JsonSerializer.Deserialize<List<RecentFileEntry>>(json) ?? new List<RecentFileEntry>();
                return entries
                    .Where(entry => entry != null && entry.IsFile && !string.IsNullOrWhiteSpace(entry.Path))
                    .Select(CloneEntry)
                    .ToList();
            }
            catch
            {
                return new List<RecentFileEntry>();
            }
        }

        private static List<RecentFileEntry> RecoverFromBookmarks()
        {
            try
            {
                if (!File.Exists(BookmarksFilePath))
                    return new List<RecentFileEntry>();

                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(BookmarksFilePath));
                if (parsed == null || parsed.Count == 0)
                    return new List<RecentFileEntry>();

                var recovered = new List<RecentFileEntry>();
                foreach (var path in parsed.Keys)
                {
                    if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!File.Exists(path))
                        continue;

                    recovered.Add(CreateEntry(path, null, File.GetLastWriteTimeUtc(path), null, false));
                }

                return recovered;
            }
            catch
            {
                return new List<RecentFileEntry>();
            }
        }

        private static List<RecentFileEntry> LoadLegacy()
        {
            try
            {
                if (!File.Exists(LegacyFilePath)) return new List<RecentFileEntry>();

                var content = File.ReadAllText(LegacyFilePath);
                var entries = content
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Trim())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(path => CreateEntry(path, null, null, null, false))
                    .ToList();

                if (entries.Count > 0)
                    Save(entries);

                return entries;
            }
            catch
            {
                return new List<RecentFileEntry>();
            }
        }

        private static bool PruneAndRefreshMetadata(List<RecentFileEntry> entries)
        {
            bool changed = false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    entries.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (entry.IsFolder)
                {
                    if (entry.LastOpenedUtc == default)
                    {
                        entry.LastOpenedUtc = DateTime.UtcNow;
                        changed = true;
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Path))
                {
                    entries.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (!File.Exists(entry.Path))
                    continue;

                DateTime currentLastModifiedUtc;
                try
                {
                    currentLastModifiedUtc = File.GetLastWriteTimeUtc(entry.Path);
                }
                catch
                {
                    continue;
                }

                if (!entry.LastModifiedUtc.HasValue || entry.LastModifiedUtc.Value != currentLastModifiedUtc)
                {
                    entry.LastModifiedUtc = currentLastModifiedUtc;
                    if (entry.PageCount > 0)
                        entry.PageCount = 0;
                    changed = true;
                }

                if (entry.LastOpenedUtc == default)
                {
                    entry.LastOpenedUtc = currentLastModifiedUtc;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool NormalizeEntries(List<RecentFileEntry> entries)
        {
            bool changed = false;
            var deduped = new List<RecentFileEntry>();
            var seenFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries.ToList())
            {
                if (entry == null)
                {
                    changed = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    entry.Id = Guid.NewGuid().ToString("N");
                    changed = true;
                }

                entry.ParentFolderId = NormalizeFolderId(entry.ParentFolderId);

                if (string.IsNullOrWhiteSpace(entry.EntryType))
                {
                    entry.EntryType = string.IsNullOrWhiteSpace(entry.Path) ? FolderEntryType : FileEntryType;
                    changed = true;
                }

                if (entry.IsFolder)
                {
                    if (!string.IsNullOrEmpty(entry.Path))
                    {
                        entry.Path = string.Empty;
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(entry.DisplayName))
                    {
                        entry.DisplayName = "Folder";
                        changed = true;
                    }

                    entry.PageCount = 0;
                    entry.IsNotebook = false;
                    deduped.Add(entry);
                    continue;
                }

                var normalizedPath = NormalizePath(entry.Path);
                if (!string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    entry.Path = normalizedPath;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(entry.Path))
                {
                    changed = true;
                    continue;
                }

                if (!seenFilePaths.Add(entry.Path))
                {
                    changed = true;
                    continue;
                }

                deduped.Add(entry);
            }

            var validFolderIds = deduped
                .Where(entry => entry.IsFolder)
                .Select(entry => entry.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in deduped)
            {
                if (string.IsNullOrWhiteSpace(entry.ParentFolderId))
                    continue;

                if (!validFolderIds.Contains(entry.ParentFolderId))
                {
                    entry.ParentFolderId = string.Empty;
                    changed = true;
                }
            }

            if (changed)
            {
                entries.Clear();
                entries.AddRange(deduped);
            }

            return changed;
        }

        private static RecentFileEntry CreateEntry(string filePath, int? pageCount, DateTime? lastModifiedUtc, string parentFolderId, bool isNotebook)
        {
            DateTime? effectiveLastModifiedUtc = lastModifiedUtc;
            if (!effectiveLastModifiedUtc.HasValue && File.Exists(filePath))
                effectiveLastModifiedUtc = File.GetLastWriteTimeUtc(filePath);

            return new RecentFileEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                EntryType = FileEntryType,
                ParentFolderId = NormalizeFolderId(parentFolderId),
                Path = NormalizePath(filePath),
                PageCount = pageCount ?? 0,
                LastModifiedUtc = effectiveLastModifiedUtc,
                LastOpenedUtc = DateTime.UtcNow,
                IsNotebook = isNotebook
            };
        }

        private static RecentFileEntry CloneEntry(RecentFileEntry entry)
        {
            return new RecentFileEntry
            {
                Id = entry.Id,
                EntryType = entry.EntryType,
                ParentFolderId = entry.ParentFolderId,
                DisplayName = entry.DisplayName,
                IsNotebook = entry.IsNotebook,
                Path = entry.Path,
                PageCount = entry.PageCount,
                LastModifiedUtc = entry.LastModifiedUtc,
                LastOpenedUtc = entry.LastOpenedUtc,
                Color = entry.Color ?? string.Empty
            };
        }

        private static string NormalizeParentFolderId(List<RecentFileEntry> entries, string parentFolderId)
        {
            var normalizedFolderId = NormalizeFolderId(parentFolderId);
            if (string.IsNullOrWhiteSpace(normalizedFolderId))
                return string.Empty;

            return entries.Any(entry =>
                    entry.IsFolder &&
                    string.Equals(entry.Id, normalizedFolderId, StringComparison.OrdinalIgnoreCase))
                ? normalizedFolderId
                : string.Empty;
        }

        private static string ResolveParentFolderId(List<RecentFileEntry> entries, RecentFileEntry existing, string requestedParentFolderId)
        {
            if (!string.IsNullOrWhiteSpace(requestedParentFolderId))
                return NormalizeParentFolderId(entries, requestedParentFolderId);

            if (existing != null)
                return NormalizeFolderId(existing.ParentFolderId);

            return string.Empty;
        }

        private static string NormalizeFolderId(string folderId)
        {
            return string.IsNullOrWhiteSpace(folderId) ? string.Empty : folderId.Trim();
        }

        private static string NormalizePath(string filePath)
        {
            try
            {
                return Path.GetFullPath(filePath).Trim();
            }
            catch
            {
                return filePath?.Trim() ?? string.Empty;
            }
        }

        private static void Save(List<RecentFileEntry> entries)
        {
            try
            {
                int incomingFiles = entries.Count(entry => entry != null && entry.IsFile);
                if (incomingFiles == 0 && File.Exists(FilePath))
                {
                    var existing = TryReadEntries(FilePath);
                    if (existing.Count > 0)
                    {
                        TryWriteBackup(File.ReadAllText(FilePath));
                        return;
                    }
                }

                if (File.Exists(FilePath) && incomingFiles > 0)
                    TryWriteBackup(File.ReadAllText(FilePath));

                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecentFilesService] Save failed: {ex.Message}");
            }
        }

        private static void TryWriteBackup(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
                    return;

                File.WriteAllText(BackupFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecentFilesService] Backup failed: {ex.Message}");
            }
        }
    }
}
