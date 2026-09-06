using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class RecycleBinServiceTests
{
    [Test]
    public void EncodeDoubleNullPathTerminatesWithTwoNulls()
    {
        string encoded = RecycleBinService.EncodeDoubleNullPath(@"C:\Docs\notes.pdf");

        Assert.That(encoded, Does.StartWith(@"C:\Docs\notes.pdf"));
        Assert.That(encoded.EndsWith("\0\0", StringComparison.Ordinal), Is.True);
        Assert.That(encoded.Length, Is.EqualTo(@"C:\Docs\notes.pdf".Length + 2));
    }

    [Test]
    public void EncodeDoubleNullPathRejectsBlankInput()
    {
        Assert.That(RecycleBinService.EncodeDoubleNullPath("  "), Is.EqualTo("\0\0"));
    }

    [Test]
    public void TrySendToRecycleBinReturnsFalseForMissingFile()
    {
        Assert.That(
            RecycleBinService.TrySendToRecycleBin(@"C:\OpenNotes-missing-" + Guid.NewGuid().ToString("N") + ".pdf"),
            Is.False);
    }
}
