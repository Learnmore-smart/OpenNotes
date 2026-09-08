using System.IO;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class WordDocumentImportTests
{
    [TestCase("notes.docx")]
    [TestCase("NOTES.DOC")]
    [TestCase(@"C:\Docs\lecture.docm")]
    public void IsWordPath_AcceptsWordExtensions(string path)
    {
        Assert.That(WordDocumentImport.IsWordPath(path), Is.True);
        Assert.That(WordDocumentImport.IsImportablePath(path), Is.True);
        Assert.That(WordDocumentImport.IsPdfPath(path), Is.False);
    }

    [TestCase("notes.pdf")]
    [TestCase(@"C:\Docs\scan.PDF")]
    public void IsPdfPath_AcceptsPdfExtensions(string path)
    {
        Assert.That(WordDocumentImport.IsPdfPath(path), Is.True);
        Assert.That(WordDocumentImport.IsImportablePath(path), Is.True);
        Assert.That(WordDocumentImport.IsWordPath(path), Is.False);
    }

    [TestCase("notes.txt")]
    [TestCase("notes.rtf")]
    [TestCase("")]
    public void IsImportablePath_RejectsOtherTypes(string path)
    {
        Assert.That(WordDocumentImport.IsImportablePath(path), Is.False);
    }

    [Test]
    public void BuildSiblingPdfPath_UsesSameDirectoryAndBaseName()
    {
        string directory = CreateTempDirectory();
        string wordPath = Path.Combine(directory, "essay.docx");
        File.WriteAllText(wordPath, "word");

        string pdfPath = WordDocumentImport.BuildSiblingPdfPath(wordPath);

        Assert.That(pdfPath, Is.EqualTo(Path.Combine(directory, "essay.pdf")));
        Assert.That(File.Exists(wordPath), Is.True);
    }

    [Test]
    public void BuildSiblingPdfPath_AddsCounterWhenPdfAlreadyExists()
    {
        string directory = CreateTempDirectory();
        string wordPath = Path.Combine(directory, "essay.docx");
        File.WriteAllText(wordPath, "word");
        File.WriteAllText(Path.Combine(directory, "essay.pdf"), "existing");
        File.WriteAllText(Path.Combine(directory, "essay (1).pdf"), "existing-1");

        string pdfPath = WordDocumentImport.BuildSiblingPdfPath(wordPath);

        Assert.That(pdfPath, Is.EqualTo(Path.Combine(directory, "essay (2).pdf")));
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "OpenNotesWordImport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
