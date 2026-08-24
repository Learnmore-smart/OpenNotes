using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenNotes.Tests;

[TestFixture]
public sealed class ApplicationIconTests
{
    [Test]
    public void ExecutableIconContainsNativeWindowsShellSizesThrough256Pixels()
    {
        string iconPath = FindRepositoryFile("Assets", "app-icon.ico");

        using FileStream stream = File.OpenRead(iconPath);
        using BinaryReader reader = new(stream);

        Assert.That(reader.ReadUInt16(), Is.EqualTo(0), "ICO reserved field must be zero.");
        Assert.That(reader.ReadUInt16(), Is.EqualTo(1), "File must be a Windows icon.");
        ushort imageCount = reader.ReadUInt16();
        List<int> widths = new(imageCount);

        for (int index = 0; index < imageCount; index++)
        {
            byte width = reader.ReadByte();
            reader.ReadByte(); // height
            reader.ReadBytes(14);
            widths.Add(width == 0 ? 256 : width);
        }

        int[] requiredSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        Assert.That(widths, Is.SupersetOf(requiredSizes),
            $"Executable icon frames were: {string.Join(", ", widths.Order())}");
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(relativeParts)} from the test output directory.");
    }
}
