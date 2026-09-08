using System.IO;
using System.Text;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class WordToPdfConverterTests
{
    [Test]
    public void Import_WritesSiblingPdfAndLeavesWordUnchanged()
    {
        string directory = CreateTempDirectory();
        string wordPath = Path.Combine(directory, "essay.docx");
        byte[] original = { 1, 2, 3, 4 };
        File.WriteAllBytes(wordPath, original);
        DateTime lastWrite = File.GetLastWriteTimeUtc(wordPath);

        var converter = new WordToPdfConverter(new RecordingExporter());
        string pdfPath = converter.Import(wordPath);

        Assert.That(pdfPath, Is.EqualTo(Path.Combine(directory, "essay.pdf")));
        Assert.That(Encoding.ASCII.GetString(File.ReadAllBytes(pdfPath)), Does.StartWith("%PDF"));
        Assert.That(File.ReadAllBytes(wordPath), Is.EqualTo(original));
        Assert.That(File.GetLastWriteTimeUtc(wordPath), Is.EqualTo(lastWrite));
    }

    [Test]
    public void Import_DoesNotOverwriteAnExistingPdf()
    {
        string directory = CreateTempDirectory();
        string wordPath = Path.Combine(directory, "essay.docx");
        File.WriteAllBytes(wordPath, new byte[] { 9 });
        File.WriteAllText(Path.Combine(directory, "essay.pdf"), "keep-me");

        var converter = new WordToPdfConverter(new RecordingExporter());
        string pdfPath = converter.Import(wordPath);

        Assert.That(pdfPath, Is.EqualTo(Path.Combine(directory, "essay (1).pdf")));
        Assert.That(File.ReadAllText(Path.Combine(directory, "essay.pdf")), Is.EqualTo("keep-me"));
        Assert.That(File.Exists(wordPath), Is.True);
    }

    [Test]
    public void Import_DeletesIncompletePdfWhenExporterFails()
    {
        string directory = CreateTempDirectory();
        string wordPath = Path.Combine(directory, "essay.docx");
        File.WriteAllBytes(wordPath, new byte[] { 7 });

        var converter = new WordToPdfConverter(new FailingExporter());

        Assert.That(() => converter.Import(wordPath), Throws.TypeOf<WordConversionException>());
        Assert.That(File.Exists(Path.Combine(directory, "essay.pdf")), Is.False);
        Assert.That(File.Exists(wordPath), Is.True);
    }

    [Test]
    public void Import_ThrowsWhenNoExporterIsAvailable()
    {
        string directory = CreateTempDirectory();
        string wordPath = Path.Combine(directory, "essay.docx");
        File.WriteAllBytes(wordPath, new byte[] { 7 });

        var converter = new WordToPdfConverter(new CompositeWordPdfExporter());

        Assert.That(() => converter.Import(wordPath), Throws.TypeOf<WordConverterNotFoundException>());
        Assert.That(File.Exists(wordPath), Is.True);
        Assert.That(File.Exists(Path.Combine(directory, "essay.pdf")), Is.False);
    }

    [Test]
    public void CompositeExporter_UsesTheNextAvailableExporterAfterAFailure()
    {
        string directory = CreateTempDirectory();
        string wordPath = Path.Combine(directory, "essay.docx");
        File.WriteAllBytes(wordPath, new byte[] { 7 });
        var fallback = new RecordingExporter();
        var converter = new WordToPdfConverter(new CompositeWordPdfExporter(
            new FailingExporter { Available = true },
            fallback));

        string pdfPath = converter.Import(wordPath);

        Assert.That(fallback.ExportCount, Is.EqualTo(1));
        Assert.That(File.Exists(pdfPath), Is.True);
    }

    [Test]
    public void Import_RejectsNonWordPaths()
    {
        string directory = CreateTempDirectory();
        string pdfPath = Path.Combine(directory, "essay.pdf");
        File.WriteAllText(pdfPath, "not-word");

        var converter = new WordToPdfConverter(new RecordingExporter());

        Assert.That(() => converter.Import(pdfPath), Throws.ArgumentException);
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "OpenNotesWordConvert_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class RecordingExporter : IWordPdfExporter
    {
        public int ExportCount { get; private set; }

        public bool IsAvailable() => true;

        public void Export(string wordPath, string pdfPath)
        {
            ExportCount++;
            File.WriteAllText(pdfPath, "%PDF-1.4\n%%EOF\n");
        }
    }

    private sealed class FailingExporter : IWordPdfExporter
    {
        public bool Available { get; set; } = true;

        public bool IsAvailable() => Available;

        public void Export(string wordPath, string pdfPath)
        {
            File.WriteAllText(pdfPath, "not-a-pdf");
            throw new InvalidOperationException("export-failed");
        }
    }
}
