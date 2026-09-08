using System;
using System.IO;

namespace Caelum.Services;

public static class WordDocumentImport
{
    public static bool IsWordPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".doc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".docm", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPdfPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsImportablePath(string path)
    {
        return IsPdfPath(path) || IsWordPath(path);
    }

    public static string BuildSiblingPdfPath(string wordPath)
    {
        if (string.IsNullOrWhiteSpace(wordPath))
            throw new ArgumentException("A Word path is required.", nameof(wordPath));

        string fullPath = Path.GetFullPath(wordPath);
        string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(fullPath);
        string filePath = Path.Combine(directory, baseName + ".pdf");
        int counter = 1;

        while (File.Exists(filePath))
        {
            filePath = Path.Combine(directory, $"{baseName} ({counter}).pdf");
            counter++;
        }

        return filePath;
    }
}
