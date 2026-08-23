using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Caelum.Models;

namespace Caelum.Services
{
    public class VersionControlService
    {
        public const int MaxVersions = 50;

        private static string GetVersionDir(string filePath)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            var dir = Path.Combine(ProductInfo.GetDataDirectory(), "VersionHistory", hash);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static async Task SaveVersionAsync(string filePath, Dictionary<int, PageAnnotation> annotations)
        {
            var dir = GetVersionDir(filePath);
            // Include milliseconds and a short random suffix so two saves in
            // the same clock tick never overwrite one another.
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var file = Path.Combine(dir, $"{timestamp}_{Guid.NewGuid():N}.json");

            var json = JsonSerializer.Serialize(annotations);
            await File.WriteAllTextAsync(file, json);

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

        public static async Task<Dictionary<int, PageAnnotation>> LoadVersionAsync(string versionFilePath)
        {
            var json = await File.ReadAllTextAsync(versionFilePath);
            return JsonSerializer.Deserialize<Dictionary<int, PageAnnotation>>(json);
        }
    }
}
