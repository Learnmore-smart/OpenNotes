using System;
using System.IO;
using PdfSharpCore.Pdf;

namespace Caelum.Services;

/// <summary>
/// Shared same-directory PDF replacement primitives.  A complete temp file
/// is flushed before the final Move, so a failed write never truncates the
/// existing target and the temp artifact is always cleaned up.
/// </summary>
internal static class PdfAtomicFile
{
    internal static string CreateTempPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("A target path is required.", nameof(targetPath));

        return Path.Combine(
            Path.GetDirectoryName(targetPath) ?? string.Empty,
            $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
    }

    internal static void SaveDocument(PdfDocument document, string tempPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var outputStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        document.Save(outputStream, false);
        outputStream.Flush(true);
    }

    internal static void CopyFile(string sourcePath, string targetPath)
    {
        string tempPath = CreateTempPath(targetPath);
        try
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(true);
            }

            Replace(tempPath, targetPath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    internal static void Replace(string tempPath, string targetPath, Action<string, string> move = null)
    {
        try
        {
            if (move != null)
                move(tempPath, targetPath);
            else
                File.Move(tempPath, targetPath, true);
        }
        finally
        {
            // File.Move removes a successful temp.  This also handles an
            // injected/failed replace without touching the original target.
            TryDelete(tempPath);
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup must never mask the original save/replacement error.
        }
    }
}
