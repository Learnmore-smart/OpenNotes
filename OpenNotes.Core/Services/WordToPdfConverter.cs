using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Caelum.Services;

public interface IWordPdfExporter
{
    bool IsAvailable();
    void Export(string wordPath, string pdfPath);
}

public sealed class WordConverterNotFoundException : InvalidOperationException
{
    public WordConverterNotFoundException()
        : base("Microsoft Word or LibreOffice is required to import Word documents.")
    {
    }

    public WordConverterNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class WordConversionException : InvalidOperationException
{
    public WordConversionException(string message)
        : base(message)
    {
    }

    public WordConversionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class CompositeWordPdfExporter : IWordPdfExporter
{
    private readonly IWordPdfExporter[] _exporters;

    public CompositeWordPdfExporter(params IWordPdfExporter[] exporters)
    {
        _exporters = exporters ?? Array.Empty<IWordPdfExporter>();
    }

    public bool IsAvailable()
    {
        return _exporters.Any(exporter => exporter != null && exporter.IsAvailable());
    }

    public void Export(string wordPath, string pdfPath)
    {
        Exception lastError = null;
        bool anyAvailable = false;

        foreach (var exporter in _exporters)
        {
            if (exporter == null || !exporter.IsAvailable())
                continue;

            anyAvailable = true;
            try
            {
                exporter.Export(wordPath, pdfPath);
                if (WordToPdfConverter.LooksLikePdf(pdfPath))
                    return;
                WordToPdfConverter.TryDelete(pdfPath);
            }
            catch (Exception ex)
            {
                WordToPdfConverter.TryDelete(pdfPath);
                lastError = ex;
            }
        }

        if (!anyAvailable)
            throw new WordConverterNotFoundException();

        throw new WordConversionException(
            lastError?.Message ?? "The Word document could not be converted to PDF.",
            lastError);
    }
}

public sealed class WordToPdfConverter
{
    public static WordToPdfConverter Default { get; } = new WordToPdfConverter(
        new CompositeWordPdfExporter(new WordComPdfExporter(), new LibreOfficePdfExporter()));

    private readonly IWordPdfExporter _exporter;

    public WordToPdfConverter(IWordPdfExporter exporter)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    }

    public async Task<string> ImportAsync(string wordPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await RunStaAsync(() => Import(wordPath), cancellationToken).ConfigureAwait(false);
    }

    public string Import(string wordPath)
    {
        if (string.IsNullOrWhiteSpace(wordPath))
            throw new ArgumentException("A Word path is required.", nameof(wordPath));
        if (!WordDocumentImport.IsWordPath(wordPath))
            throw new ArgumentException("The path is not a Word document.", nameof(wordPath));
        if (!File.Exists(wordPath))
            throw new FileNotFoundException("The Word document was not found.", wordPath);

        string pdfPath = WordDocumentImport.BuildSiblingPdfPath(wordPath);
        try
        {
            _exporter.Export(wordPath, pdfPath);
            if (!LooksLikePdf(pdfPath))
                throw new WordConversionException("The converter did not produce a PDF.");
            return pdfPath;
        }
        catch (WordConverterNotFoundException)
        {
            TryDelete(pdfPath);
            throw;
        }
        catch (WordConversionException)
        {
            TryDelete(pdfPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(pdfPath);
            throw new WordConversionException(ex.Message, ex);
        }
    }

    internal static bool LooksLikePdf(string pdfPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return false;

            using var stream = File.OpenRead(pdfPath);
            var header = new byte[4];
            return stream.Read(header, 0, 4) == 4
                && header[0] == (byte)'%'
                && header[1] == (byte)'P'
                && header[2] == (byte)'D'
                && header[3] == (byte)'F';
        }
        catch
        {
            return false;
        }
    }

    internal static void TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static Task<T> RunStaAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

internal sealed class WordComPdfExporter : IWordPdfExporter
{
    public bool IsAvailable()
    {
        try
        {
            return Type.GetTypeFromProgID("Word.Application") != null;
        }
        catch
        {
            return false;
        }
    }

    public void Export(string wordPath, string pdfPath)
    {
        var type = Type.GetTypeFromProgID("Word.Application")
            ?? throw new WordConverterNotFoundException();

            object word = null;
            dynamic document = null;
            try
            {
                word = Activator.CreateInstance(type);
                dynamic app = word;
                app.Visible = false;
                app.DisplayAlerts = 0;
                try { app.AutomationSecurity = 3; } catch { }

                document = app.Documents.Open(
                    FileName: Path.GetFullPath(wordPath),
                    ConfirmConversions: false,
                    ReadOnly: true,
                    AddToRecentFiles: false,
                    Visible: false);
                document.ExportAsFixedFormat(
                    OutputFileName: Path.GetFullPath(pdfPath),
                    ExportFormat: 17,
                    OpenAfterExport: false);
            }
            finally
            {
                if (document != null)
                {
                    try { document.Close(SaveChanges: false); } catch { }
                    ReleaseCom(document);
                }

            if (word != null)
            {
                try { ((dynamic)word).Quit(SaveChanges: false); } catch { }
                ReleaseCom(word);
            }
        }
    }

    private static void ReleaseCom(object value)
    {
        try
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch
        {
        }
    }
}

internal sealed class LibreOfficePdfExporter : IWordPdfExporter
{
    public bool IsAvailable()
    {
        return !string.IsNullOrWhiteSpace(FindSoffice());
    }

    public void Export(string wordPath, string pdfPath)
    {
        string soffice = FindSoffice()
            ?? throw new WordConverterNotFoundException();

        string tempRoot = Path.Combine(Path.GetTempPath(), "OpenNotes-lo-" + Guid.NewGuid().ToString("N"));
        string profileDir = Path.Combine(tempRoot, "profile");
        Directory.CreateDirectory(profileDir);

        try
        {
            string profileUri = new Uri(profileDir).AbsoluteUri;
            var start = new ProcessStartInfo
            {
                FileName = soffice,
                Arguments =
                    $"--headless --nologo --nolockcheck --norestore --nofirststartwizard --env:UserInstallation={Quote(profileUri)} --convert-to pdf --outdir {Quote(tempRoot)} {Quote(Path.GetFullPath(wordPath))}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(start)
                ?? throw new WordConversionException("LibreOffice failed to start.");
            if (!process.WaitForExit(180_000))
            {
                try { process.Kill(true); } catch { }
                throw new WordConversionException("LibreOffice conversion timed out.");
            }

            if (process.ExitCode != 0)
                throw new WordConversionException($"LibreOffice exited with code {process.ExitCode}.");

            string produced = Directory.GetFiles(tempRoot, "*.pdf").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(produced))
                throw new WordConversionException("LibreOffice did not produce a PDF.");

            File.Copy(produced, pdfPath, overwrite: false);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static string FindSoffice()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            string candidate = Path.Combine(root, "LibreOffice", "program", "soffice.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string Quote(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }
}
