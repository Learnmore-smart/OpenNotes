using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caelum.Models;

namespace Caelum.Services
{
    public class VersionControlService
    {
        public const int MaxVersions = 50;

        /// <summary>
        /// Version-directory path for a document — pure path math, never
        /// touches the file system. Read paths (<see cref="GetVersions"/>)
        /// must not create the directory just to enumerate it.
        /// </summary>
        private static string GetVersionDir(string filePath)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return Path.Combine(ProductInfo.GetDataDirectory(), "VersionHistory", hash);
        }

        /// <summary>Write path only: resolve the version dir and create it.</summary>
        private static string EnsureVersionDir(string filePath)
        {
            var dir = GetVersionDir(filePath);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static Task SaveVersionAsync(
            string filePath,
            Dictionary<int, PageAnnotation> annotations,
            CancellationToken cancellationToken = default)
        {
            // Serialize + write + prune are pure sidecar I/O — keep them off
            // the caller's thread (the editor's save pipeline invokes this
            // from the UI dispatcher). Ordering is preserved: callers still
            // await the task, so the version lands only after the atomic PDF
            // save it mirrors has already succeeded.
            return Task.Run(
                () => SaveVersionCoreAsync(filePath, annotations, cancellationToken),
                cancellationToken);
        }

        private static async Task SaveVersionCoreAsync(
            string filePath,
            Dictionary<int, PageAnnotation> annotations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = EnsureVersionDir(filePath);
            // Include milliseconds and a short random suffix so two saves in
            // the same clock tick never overwrite one another.
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var file = Path.Combine(dir, $"{timestamp}_{Guid.NewGuid():N}.json");

            var json = JsonSerializer.Serialize(annotations);
            await File.WriteAllTextAsync(file, json, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            PruneVersions(filePath);
        }

        private static void PruneVersions(string filePath)
        {
            var versions = GetVersions(filePath); // newest first (与 GetVersions 契约一致)
            for (int i = MaxVersions; i < versions.Count; i++)
            {
                try { File.Delete(versions[i]); } catch { /* best-effort */ }
            }
        }

        public static List<string> GetVersions(string filePath)
        {
            var dir = GetVersionDir(filePath);
            if (!Directory.Exists(dir)) return new List<string>();
            var files = Directory.GetFiles(dir, "*.json");
            var list = new List<string>(files);
            // Creation time is not stable across copies/restores. Last-write
            // time reflects the order in which snapshots were actually saved.
            list.Sort((a,b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            return list;
        }

        public static async Task<Dictionary<int, PageAnnotation>> LoadVersionAsync(
            string versionFilePath,
            CancellationToken cancellationToken = default)
        {
            var json = await File.ReadAllTextAsync(versionFilePath, cancellationToken);
            return JsonSerializer.Deserialize<Dictionary<int, PageAnnotation>>(json);
        }
    }
}
