using Caelum.Services;
using System;
using System.IO;

namespace Caelum.Tests;

[TestFixture]
public sealed class ProductInfoTests
{
    [Test]
    public void VisibleBrandUsesOpenNotesWhileLegacyStorageIdentityRemainsStable()
    {
        Assert.That(ProductInfo.DisplayName, Is.EqualTo("OpenNotes"));
        Assert.That(ProductInfo.Version, Is.EqualTo("5.2.4"));
        Assert.That(ProductInfo.LegacyName, Is.EqualTo("Caelum"));
        Assert.That(ProductInfo.LegacyDataDirectoryName, Is.EqualTo("Caelum"));
        Assert.That(ProductInfo.LegacyAppxIdentity, Is.EqualTo("WindowsNotesApp"));
        Assert.That(ProductInfo.RepositoryUrl, Does.Contain("Learnmore-smart/Windows-Notes"));
    }

    [Test]
    public void DataDirectoryUsesLegacyPathByDefaultAndOptInOverrideForIsolatedRuns()
    {
        var previous = Environment.GetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable);
        var overrideRoot = Path.Combine(Path.GetTempPath(), "OpenNotesProductInfoTests_" + Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, null);
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductInfo.LegacyDataDirectoryName);
            Assert.That(ProductInfo.GetDataDirectory(), Is.EqualTo(defaultPath));

            Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, overrideRoot);
            Assert.That(ProductInfo.GetDataDirectory(), Is.EqualTo(
                Path.Combine(Path.GetFullPath(overrideRoot), ProductInfo.LegacyDataDirectoryName)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProductInfo.DataRootOverrideEnvironmentVariable, previous);
        }
    }
}
