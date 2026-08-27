using System;
using System.IO;

namespace Caelum.Services;

public static class ProductInfo
{
    public const string DisplayName = "OpenNotes";
    public const string LegacyName = "Caelum";
    public const string LegacyDataDirectoryName = "Caelum";
    public const string LegacyAppxIdentity = "WindowsNotesApp";
    public const string RepositoryUrl = "https://github.com/Learnmore-smart/Windows-Notes";
    public const string WebsiteUrl = "https://learnmore-smart.github.io/Windows-Notes/";
    public const string Version = "5.2.3";
    public const string DataRootOverrideEnvironmentVariable = "OPENNOTES_DATA_ROOT";
    public static string Description => LocalizationService.Get("Product.Description");

    public static string GetDataDirectory()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(DataRootOverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            try
            {
                return Path.Combine(Path.GetFullPath(overrideRoot), LegacyDataDirectoryName);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException)
            {
                // Invalid test-only input falls back to the production-compatible location.
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyDataDirectoryName);
    }
}
