using System;
using System.IO;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
public sealed class WindowsEnvironmentTests
{
    [Test]
    public void MissingWindirIsRecoveredFromValidSystemRootBeforeWpfStarts()
    {
        var originalWindir = Environment.GetEnvironmentVariable("WINDIR");
        var originalSystemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        var validSystemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.That(validSystemRoot, Is.Not.Empty);
        Assert.That(Directory.Exists(validSystemRoot), Is.True);

        try
        {
            Environment.SetEnvironmentVariable("WINDIR", null);
            Environment.SetEnvironmentVariable("SystemRoot", validSystemRoot);

            WindowsEnvironment.NormalizeForWpf();

            Assert.That(
                Environment.GetEnvironmentVariable("WINDIR"),
                Is.EqualTo(validSystemRoot));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDIR", originalWindir);
            Environment.SetEnvironmentVariable("SystemRoot", originalSystemRoot);
        }
    }
}
