using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Caelum.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Annotations;
using PdfSharpCore.Pdf.Advanced;
using System.Linq;
using PdfiumViewer;

using PdfiumPdfDocument = PdfiumViewer.PdfDocument;
using PdfSharpPdfRectangle = PdfSharpCore.Pdf.PdfRectangle;

namespace Caelum.Services
{
    public class PdfService : IAsyncDisposable
    {
        static PdfService()
        {
            ConfigurePdfFontResolver();
        }

        private readonly SemaphoreSlim _documentLock = new SemaphoreSlim(1, 1);
        // Save/dispose admission is acquired before _documentLock.  Dispose
        // therefore cannot publish DisposeStarted while an admitted save is
        // between its final state check and a native reload/create.
        private readonly SemaphoreSlim _lifetimeGate = new SemaphoreSlim(1, 1);
        private const double PdfPointToDipScale = 96.0 / 72.0;
        private PdfiumPdfDocument _pdfDocument;
        private Stream _pdfBackingStream;
        private string _sourceFilePath;
        private readonly Dictionary<int, PdfPageTextInfo> _pageTextInfoCache = new Dictionary<int, PdfPageTextInfo>();
        private const int DisposeActive = 0;
        private const int DisposeStarted = 1;
        private const int DisposeCompleted = 2;
        private int _disposeState;
        // Replaced after a failed disposal attempt so ReleaseResourcesAsync
        // can retry the same service instead of permanently observing the
        // first exception.
        private TaskCompletionSource<bool> _disposeCompletion = NewDisposeCompletion();
        private static readonly Regex RichTextBreakRegex = new Regex(@"<\s*br\s*/?\s*>|<\s*/p\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RichTextTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex DefaultAppearanceFontSizeRegex = new Regex(@"(?<size>[+-]?\d+(?:\.\d+)?)\s+Tf\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DefaultAppearanceRgbRegex = new Regex(@"(?<r>[+-]?\d*\.?\d+)\s+(?<g>[+-]?\d*\.?\d+)\s+(?<b>[+-]?\d*\.?\d+)\s+rg\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DefaultAppearanceGrayRegex = new Regex(@"(?<gray>[+-]?\d*\.?\d+)\s+g\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CssFontSizeRegex = new Regex(@"font-size\s*:\s*(?<size>[+-]?\d+(?:\.\d+)?)\s*(?<unit>pt|px)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CssFontRegex = new Regex(@"font\s*:[^;]*?(?<size>[+-]?\d+(?:\.\d+)?)\s*(?<unit>pt|px)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CssHexColorRegex = new Regex(@"color\s*:\s*(?<value>#[0-9a-f]{3}|#[0-9a-f]{6})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CssRgbColorRegex = new Regex(@"color\s*:\s*rgb\s*\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CssFontFamilyRegex = new Regex(@"font-family\s*:\s*(?<family>[^;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CssTextAlignRegex = new Regex(@"text-align\s*:\s*(?<alignment>left|center|right)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class LoadedPdfDocument
        {
            public PdfiumPdfDocument Document { get; init; }
            public Stream BackingStream { get; init; }
            public Dictionary<int, Models.PageAnnotation> ExtractedAnnotations { get; init; } = new();
        }

        /// <summary>
        /// Maps between the PDF page's unrotated default user space and the
        /// top-left DIP coordinate space shown by Pdfium/WPF. PdfSharpCore's
        /// Page.Width/Page.Height are rotation-aware, while annotation
        /// coordinates remain relative to the raw page box.
        /// </summary>
        private readonly struct PdfPageDisplayGeometry
        {
            public PdfPageDisplayGeometry(double left, double bottom, double right, double top, int rotationDegrees)
            {
                Left = left;
                Bottom = bottom;
                Right = right;
                Top = top;
                RotationDegrees = rotationDegrees;
            }

            public double Left { get; }
            public double Bottom { get; }
            public double Right { get; }
            public double Top { get; }
            public double WidthPoints => Right - Left;
            public double HeightPoints => Top - Bottom;
            public double WidthDips => WidthPoints * PdfPointToDipScale;
            public double HeightDips => HeightPoints * PdfPointToDipScale;
            public int RotationDegrees { get; }

            public Point PdfToDisplayDips(double pdfX, double pdfY)
            {
                double x = (pdfX - Left) * PdfPointToDipScale;
                double y = (Top - pdfY) * PdfPointToDipScale;
                return RotationDegrees switch
                {
                    90 => new Point(HeightDips - y, x),
                    180 => new Point(WidthDips - x, HeightDips - y),
                    270 => new Point(y, WidthDips - x),
                    _ => new Point(x, y)
                };
            }

            public Point DisplayDipsToPdf(double displayX, double displayY)
            {
                double x;
                double y;
                switch (RotationDegrees)
                {
                    case 90:
                        x = displayY;
                        y = HeightDips - displayX;
                        break;
                    case 180:
                        x = WidthDips - displayX;
                        y = HeightDips - displayY;
                        break;
                    case 270:
                        x = WidthDips - displayY;
                        y = displayX;
                        break;
                    default:
                        x = displayX;
                        y = displayY;
                        break;
                }

                return new Point(
                    Left + (x / PdfPointToDipScale),
                    Top - (y / PdfPointToDipScale));
            }
        }

        public sealed class PdfTextCharacterInfo
        {
            public int Offset { get; init; }
            public char Character { get; init; }
            public IReadOnlyList<Rect> Bounds { get; init; } = Array.Empty<Rect>();
            public Rect UnionBounds { get; init; }
        }

        public sealed class PdfPageTextInfo
        {
            public string Text { get; init; } = string.Empty;
            public IReadOnlyList<PdfTextCharacterInfo> Characters { get; init; } = Array.Empty<PdfTextCharacterInfo>();
        }

        /// <summary>Task 31: a lightweight outline node for the editor sidebar.</summary>
        public sealed class PdfOutlineEntry
        {
            public string Title { get; init; } = string.Empty;
            public int PageIndex { get; init; } = -1;
            public IReadOnlyList<PdfOutlineEntry> Children { get; init; } = Array.Empty<PdfOutlineEntry>();
        }

        public int PageCount => _pdfDocument?.PageCount ?? 0;
        public Dictionary<int, Models.PageAnnotation> ExtractedAnnotations { get; set; } = new();

        public static async Task CreateBlankPdfAsync(
            string filePath,
            double widthPoints = 612,
            double heightPoints = 792,
            PageInsertTemplate template = PageInsertTemplate.Blank)
        {
            // Blank-document creation is also a physical write. Join the
            // process-wide path lease so a new-document/import workflow cannot
            // overwrite a concurrent structural or annotation replacement of
            // the same path.
            await PdfSaveCoordinator.RunExclusiveAsync(
                filePath,
                () => Task.Run(() =>
                {
                    string directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);

                    string tempPath = PdfAtomicFile.CreateTempPath(filePath);
                    try
                    {
                        using var document = new PdfSharpCore.Pdf.PdfDocument();
                        var page = document.AddPage();
                        page.Width = widthPoints;
                        page.Height = heightPoints;
                        ApplyPageTemplate(page, template);
                        PdfAtomicFile.SaveDocument(document, tempPath);
                        PdfAtomicFile.Replace(tempPath, filePath);
                    }
                    finally
                    {
                        PdfAtomicFile.TryDelete(tempPath);
                    }
                })).ConfigureAwait(false);
        }

        public async Task AppendBlankPageAsync(string filePath, double? widthPoints = null, double? heightPoints = null)
        {
            await InsertPageAsync(filePath, int.MaxValue, PageInsertTemplate.Blank, widthPoints, heightPoints).ConfigureAwait(false);
        }

        public async Task InsertPageAsync(string filePath, int insertIndex, PageInsertTemplate template, double? widthPoints = null, double? heightPoints = null)
        {
            await RunDocumentWriteAsync(filePath, async () =>
            {
                await Task.Run(() => InsertPageCore(filePath, insertIndex, template, widthPoints, heightPoints), CancellationToken.None).ConfigureAwait(false);
                await ReloadDocumentFromFileAsync(filePath).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        public async Task DeletePageAsync(string filePath, int pageIndex)
        {
            await RunDocumentWriteAsync(filePath, async () =>
            {
                await Task.Run(() => DeletePageCore(filePath, pageIndex), CancellationToken.None).ConfigureAwait(false);
                await ReloadDocumentFromFileAsync(filePath).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the page size in device-independent pixels (at 192 DPI rendering).
        /// Fast 鈥?no rendering required; uses cached page sizes from the loaded document.
        /// </summary>
        public (double Width, double Height) GetPageSizeInDips(int pageIndex)
        {
            if (_pdfDocument == null || pageIndex < 0 || pageIndex >= _pdfDocument.PageCount)
                return (0, 0);

            // Render at 192 DPI but BitmapSource reports DIPs as pixelWidth * 96 / dpi.
            // So effective DIP size = (pagePoints * 192/72) * 96/192 = pagePoints * 96/72.
            const double renderDpi = 192.0;
            var size = _pdfDocument.PageSizes[pageIndex];
            int pixelW = (int)(size.Width * renderDpi / 72.0);
            int pixelH = (int)(size.Height * renderDpi / 72.0);
            double w = pixelW * 96.0 / renderDpi;
            double h = pixelH * 96.0 / renderDpi;
            return (w, h);
        }

        public async Task LoadPdfAsync(string filePath, CancellationToken cancellationToken = default)
        {
            // Loads replace the in-memory/native view of the same PDF. Join
            // the process-wide path lease so a reload cannot observe a
            // structural/annotation write halfway through its replacement.
            await PdfSaveCoordinator.RunExclusiveAsync(
                filePath,
                () => LoadPdfCoreAsync(filePath, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        public bool TryGetCachedPageTextInfo(int pageIndex, out PdfPageTextInfo textInfo)
        {
            return _pageTextInfoCache.TryGetValue(pageIndex, out textInfo);
        }

        public async Task<PdfPageTextInfo> GetPageTextInfoAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _documentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_pageTextInfoCache.TryGetValue(pageIndex, out var cached))
                    return cached;

                if (_pdfDocument == null || pageIndex < 0 || pageIndex >= _pdfDocument.PageCount)
                    return new PdfPageTextInfo();

                var textInfo = await Task.Run(() => BuildPageTextInfo(pageIndex, cancellationToken), cancellationToken).ConfigureAwait(false);
                _pageTextInfoCache[pageIndex] = textInfo;
                return textInfo;
            }
            finally
            {
                _documentLock.Release();
            }
        }

        private PdfPageTextInfo BuildPageTextInfo(int pageIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text = _pdfDocument.GetPdfText(pageIndex) ?? string.Empty;
            var characters = new List<PdfTextCharacterInfo>(text.Length);

            for (int offset = 0; offset < text.Length; offset++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var span = new PdfTextSpan(pageIndex, offset, 1);
                var bounds = GetTextBoundsInDips(pageIndex, span);

                Rect unionBounds = Rect.Empty;
                for (int i = 0; i < bounds.Count; i++)
                {
                    if (unionBounds.IsEmpty)
                        unionBounds = bounds[i];
                    else
                        unionBounds.Union(bounds[i]);
                }

                characters.Add(new PdfTextCharacterInfo
                {
                    Offset = offset,
                    Character = text[offset],
                    Bounds = bounds,
                    UnionBounds = unionBounds
                });
            }

            return new PdfPageTextInfo
            {
                Text = text,
                Characters = characters
            };
        }

        private IReadOnlyList<Rect> GetTextBoundsInDips(int pageIndex, PdfTextSpan span)
        {
            var pdfBounds = _pdfDocument.GetTextBounds(span);
            if (pdfBounds == null || pdfBounds.Count == 0)
                return Array.Empty<Rect>();

            var bounds = new List<Rect>(pdfBounds.Count);
            foreach (var pdfRect in pdfBounds)
            {
                if (!pdfRect.IsValid)
                    continue;

                var deviceRect = _pdfDocument.RectangleFromPdf(pageIndex, pdfRect.Bounds);
                if (deviceRect.Width <= 0 || deviceRect.Height <= 0)
                    continue;

                bounds.Add(new Rect(
                    deviceRect.X * PdfPointToDipScale,
                    deviceRect.Y * PdfPointToDipScale,
                    deviceRect.Width * PdfPointToDipScale,
                    deviceRect.Height * PdfPointToDipScale));
            }

            return bounds.Count == 0 ? Array.Empty<Rect>() : bounds;
        }

        private async Task LoadPdfCoreAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            await _lifetimeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await _documentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    DisposeCurrentDocument();
                    _sourceFilePath = filePath;

                    var loaded = await Task.Run(() => LoadPdfDocument(filePath, cancellationToken), cancellationToken).ConfigureAwait(false);
                    _pdfDocument = loaded.Document;
                    _pdfBackingStream = loaded.BackingStream;
                    ExtractedAnnotations = loaded.ExtractedAnnotations;
                }
                finally
                {
                    _documentLock.Release();
                }
            }
            finally
            {
                _lifetimeGate.Release();
            }
        }

        private LoadedPdfDocument LoadPdfDocument(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"PDF file not found: {filePath}");

            MemoryStream strippedStream = null;
            try
            {
                strippedStream = new MemoryStream();
                Dictionary<int, Models.PageAnnotation> extractedAnnotations;

                using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    extractedAnnotations = ExtractAndStripAnnotations(sourceStream, strippedStream, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                strippedStream.Position = 0;

                return new LoadedPdfDocument
                {
                    Document = PdfiumPdfDocument.Load(strippedStream),
                    BackingStream = strippedStream,
                    ExtractedAnnotations = extractedAnnotations
                };
            }
            catch (OperationCanceledException)
            {
                strippedStream?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                strippedStream?.Dispose();
                System.Diagnostics.Debug.WriteLine($"LoadPdfCoreAsync: annotation stripping failed: {ex.Message}");

                return new LoadedPdfDocument
                {
                    Document = PdfiumPdfDocument.Load(filePath),
                    ExtractedAnnotations = new Dictionary<int, Models.PageAnnotation>()
                };
            }
        }

        private void DisposeCurrentDocument()
        {
            _pageTextInfoCache.Clear();
            Exception firstFailure = null;
            var document = _pdfDocument;
            if (document != null)
            {
                try
                {
                    document.Dispose();
                    _pdfDocument = null;
                }
                catch (Exception ex)
                {
                    // Keep the owner reachable for a later retry. A failed
                    // native Dispose must not be converted into a leaked,
                    // forgotten document by clearing the field first.
                    firstFailure = ex;
                }
            }

            var backingStream = _pdfBackingStream;
            if (backingStream != null)
            {
                try
                {
                    backingStream.Dispose();
                    _pdfBackingStream = null;
                }
                catch (Exception ex)
                {
                    // Preserve a failed stream owner for the same retryable
                    // disposal contract, while still attempting every other
                    // resource above.
                    firstFailure ??= ex;
                }
            }

            if (firstFailure != null)
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }

        private async Task ReloadDocumentFromFileAsync(string filePath)
        {
            ThrowIfDisposed();
            DisposeCurrentDocument();
            _sourceFilePath = filePath;

            LoadedPdfDocument loaded = null;
            try
            {
                loaded = await Task.Run(() => LoadPdfDocument(filePath, CancellationToken.None), CancellationToken.None).ConfigureAwait(false);
                ThrowIfDisposed();
                _pdfDocument = loaded.Document;
                _pdfBackingStream = loaded.BackingStream;
                ExtractedAnnotations = loaded.ExtractedAnnotations;
                loaded = null;
            }
            finally
            {
                DisposeLoadedPdf(loaded);
            }
        }

        private static void DisposeLoadedPdf(LoadedPdfDocument loaded)
        {
            if (loaded == null)
                return;

            Exception firstFailure = null;
            try
            {
                loaded.Document?.Dispose();
            }
            catch (Exception ex)
            {
                firstFailure = ex;
            }

            try
            {
                loaded.BackingStream?.Dispose();
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }

            if (firstFailure != null)
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }

        /// <summary>
        /// Admits every write which changes a PDF and reloads the in-memory
        /// document under one lifetime/document critical section.  The order is
        /// path coordinator, lifetime gate, then document lock; DisposeAsync
        /// uses the latter two in the same order, so a queued write cannot pass
        /// a disposal checkpoint and recreate native state afterwards.
        /// </summary>
        private Task RunDocumentWriteAsync(string filePath, Func<Task> writeAsync)
            => RunDocumentWriteAsync(new[] { filePath }, writeAsync);

        private Task RunDocumentWriteAsync(IReadOnlyCollection<string> paths, Func<Task> writeAsync)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(writeAsync);
            return PdfSaveCoordinator.RunExclusiveAsync(paths, () =>
                RunDocumentWriteUnderLifetimeAsync(writeAsync));
        }

        private async Task RunDocumentWriteUnderLifetimeAsync(Func<Task> writeAsync)
        {
            ThrowIfDisposed();
            await _lifetimeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await _documentLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    await writeAsync().ConfigureAwait(false);
                    ThrowIfDisposed();
                }
                finally
                {
                    _documentLock.Release();
                }
            }
            finally
            {
                _lifetimeGate.Release();
            }
        }

        private static void InsertPageCore(string filePath, int insertIndex, PageInsertTemplate template, double? widthPoints, double? heightPoints)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF to update not found.", filePath);

            string tempPath = CreatePdfTempPath(filePath);

            try
            {
                using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var document = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Modify))
                {
                    int safeInsertIndex = Math.Max(0, Math.Min(insertIndex, document.PageCount));
                    var referencePage = document.PageCount == 0
                        ? null
                        : document.Pages[Math.Min(safeInsertIndex, document.PageCount - 1)];

                    var page = document.InsertPage(safeInsertIndex);
                    page.Width = widthPoints ?? referencePage?.Width.Point ?? 612;
                    page.Height = heightPoints ?? referencePage?.Height.Point ?? 792;

                    ApplyPageTemplate(page, template);
                    SaveModifiedDocument(document, tempPath);
                }

                PdfAtomicFile.Replace(tempPath, filePath);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        private static void DeletePageCore(string filePath, int pageIndex)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF to update not found.", filePath);

            string tempPath = CreatePdfTempPath(filePath);

            try
            {
                using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var document = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Modify))
                {
                    if (document.PageCount <= 1)
                        throw new InvalidOperationException("At least one page must remain in the document.");

                    if (pageIndex < 0 || pageIndex >= document.PageCount)
                        throw new ArgumentOutOfRangeException(nameof(pageIndex));

                    document.Pages.RemoveAt(pageIndex);
                    SaveModifiedDocument(document, tempPath);
                }

                PdfAtomicFile.Replace(tempPath, filePath);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        private static void ReorderPagesCore(string filePath, int fromIndex, int toIndex)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF to update not found.", filePath);

            using var source = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            if (fromIndex < 0 || fromIndex >= source.PageCount)
                throw new ArgumentOutOfRangeException(nameof(fromIndex));

            var order = Enumerable.Range(0, source.PageCount).ToList();
            int page = order[fromIndex];
            order.RemoveAt(fromIndex);
            int safeTarget = Math.Max(0, Math.Min(toIndex, order.Count));
            order.Insert(safeTarget, page);

            string tempPath = CreatePdfTempPath(filePath);
            try
            {
                using var output = new PdfSharpCore.Pdf.PdfDocument();
                foreach (int sourceIndex in order)
                    output.AddPage(source.Pages[sourceIndex]);
                PdfAtomicFile.SaveDocument(output, tempPath);
                PdfAtomicFile.Replace(tempPath, filePath);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        private static void DuplicatePageCore(string filePath, int pageIndex)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF to update not found.", filePath);

            using var source = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            if (pageIndex < 0 || pageIndex >= source.PageCount)
                throw new ArgumentOutOfRangeException(nameof(pageIndex));

            string tempPath = CreatePdfTempPath(filePath);
            try
            {
                using var output = new PdfSharpCore.Pdf.PdfDocument();
                for (int i = 0; i < source.PageCount; i++)
                {
                    output.AddPage(source.Pages[i]);
                    if (i == pageIndex)
                        output.AddPage(source.Pages[i]);
                }
                PdfAtomicFile.SaveDocument(output, tempPath);
                PdfAtomicFile.Replace(tempPath, filePath);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        private static void RotatePageCore(string filePath, int pageIndex, int quarterTurns)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF to update not found.", filePath);

            string tempPath = CreatePdfTempPath(filePath);
            try
            {
                using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var document = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Modify))
                {
                    if (pageIndex < 0 || pageIndex >= document.PageCount)
                        throw new ArgumentOutOfRangeException(nameof(pageIndex));

                    var page = document.Pages[pageIndex];
                    int existing = page.Elements.ContainsKey("/Rotate") ? page.Elements.GetInteger("/Rotate") : 0;
                    int normalized = ((existing + (quarterTurns * 90)) % 360 + 360) % 360;
                    page.Elements.SetInteger("/Rotate", normalized);
                    SaveModifiedDocument(document, tempPath);
                }

                PdfAtomicFile.Replace(tempPath, filePath);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        private static PdfPageDisplayGeometry GetPageDisplayGeometry(PdfSharpCore.Pdf.PdfPage page)
        {
            // Do not use page.CropBox here. PdfSharpCore calls GetRectangle(..., create: true)
            // and materializes a missing value as [0 0 0 0], which Edge clips to a blank page.
            PdfSharpPdfRectangle box = page.Elements.ContainsKey("/CropBox")
                ? page.Elements.GetRectangle("/CropBox")
                : null;

            if (!PdfAtomicFile.HasUsableArea(box))
            {
                if (page.Elements.ContainsKey("/CropBox"))
                    page.Elements.Remove("/CropBox");

                box = page.MediaBox;
            }

            double left = Math.Min(box.X1, box.X2);
            double right = Math.Max(box.X1, box.X2);
            double bottom = Math.Min(box.Y1, box.Y2);
            double top = Math.Max(box.Y1, box.Y2);
            int rotation = ((page.Rotate % 360) + 360) % 360;
            rotation = ((rotation + 45) / 90 * 90) % 360;
            return new PdfPageDisplayGeometry(left, bottom, right, top, rotation);
        }

        private static void InsertPdfPagesCore(string targetPath, string sourcePath, int insertIndex, int startPage, int endPage)
        {
            if (!File.Exists(targetPath))
                throw new FileNotFoundException("Target PDF not found.", targetPath);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Source PDF not found.", sourcePath);
            if (string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Source and target PDF must be different files.");

            using var target = PdfReader.Open(targetPath, PdfDocumentOpenMode.Import);
            using var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            int first = Math.Max(0, Math.Min(startPage, source.PageCount - 1));
            int last = Math.Max(first, Math.Min(endPage, source.PageCount - 1));
            int safeInsert = Math.Max(0, Math.Min(insertIndex, target.PageCount));
            string tempPath = CreatePdfTempPath(targetPath);

            try
            {
                using var output = new PdfSharpCore.Pdf.PdfDocument();
                for (int i = 0; i < target.PageCount; i++)
                {
                    if (i == safeInsert)
                    {
                        for (int sourceIndex = first; sourceIndex <= last; sourceIndex++)
                            output.AddPage(source.Pages[sourceIndex]);
                    }
                    output.AddPage(target.Pages[i]);
                }

                if (safeInsert == target.PageCount)
                {
                    for (int sourceIndex = first; sourceIndex <= last; sourceIndex++)
                        output.AddPage(source.Pages[sourceIndex]);
                }

                PdfAtomicFile.SaveDocument(output, tempPath);
                PdfAtomicFile.Replace(tempPath, targetPath);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        private static void InsertImagePageCore(string targetPath, string imagePath, int insertIndex)
        {
            if (!File.Exists(targetPath))
                throw new FileNotFoundException("Target PDF not found.", targetPath);
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image not found.", imagePath);

            string tempPath = CreatePdfTempPath(targetPath);
            try
            {
                using (var sourceStream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var document = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Modify))
                using (var image = XImage.FromFile(imagePath))
                {
                    int safeInsert = Math.Max(0, Math.Min(insertIndex, document.PageCount));
                    var reference = document.PageCount == 0 ? null : document.Pages[Math.Min(safeInsert, document.PageCount - 1)];
                    var page = document.InsertPage(safeInsert);
                    double pageWidth = reference?.Width.Point ?? 612;
                    double pageHeight = reference?.Height.Point ?? 792;
                    page.Width = pageWidth;
                    page.Height = pageHeight;

                    double sourceWidth = Math.Max(1, image.PixelWidth);
                    double sourceHeight = Math.Max(1, image.PixelHeight);
                    double scale = Math.Min(pageWidth / sourceWidth, pageHeight / sourceHeight);
                    double drawWidth = sourceWidth * scale;
                    double drawHeight = sourceHeight * scale;
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawImage(image, (pageWidth - drawWidth) / 2, (pageHeight - drawHeight) / 2, drawWidth, drawHeight);
                    SaveModifiedDocument(document, tempPath);
                }

                PdfAtomicFile.Replace(tempPath, targetPath);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        private IReadOnlyList<PdfOutlineEntry> ReadOutlineCore(string filePath, CancellationToken cancellationToken)
        {
            using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            return ReadOutlineNodes(document, document.Outlines, cancellationToken);
        }

        private static IReadOnlyList<PdfOutlineEntry> ReadOutlineNodes(PdfSharpCore.Pdf.PdfDocument document, PdfSharpCore.Pdf.PdfOutlineCollection nodes, CancellationToken cancellationToken)
        {
            var result = new List<PdfOutlineEntry>();
            foreach (var outline in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int pageIndex = -1;
                if (outline.DestinationPage != null)
                {
                    for (int i = 0; i < document.PageCount; i++)
                    {
                        if (ReferenceEquals(document.Pages[i], outline.DestinationPage))
                        {
                            pageIndex = i;
                            break;
                        }
                    }
                }
                result.Add(new PdfOutlineEntry
                {
                    Title = string.IsNullOrWhiteSpace(outline.Title) ? "Untitled" : outline.Title,
                    PageIndex = pageIndex,
                    Children = outline.HasChildren ? ReadOutlineNodes(document, outline.Outlines, cancellationToken) : Array.Empty<PdfOutlineEntry>()
                });
            }
            return result;
        }

        private static string CreatePdfTempPath(string filePath)
        {
            return PdfAtomicFile.CreateTempPath(filePath);
        }

        private static void TryDeleteFile(string filePath)
        {
            PdfAtomicFile.TryDelete(filePath);
        }

        private static void SaveModifiedDocument(PdfSharpCore.Pdf.PdfDocument document, string tempPath)
        {
            PdfAtomicFile.SaveDocument(document, tempPath);
        }

        private static void ApplyPageTemplate(PdfSharpCore.Pdf.PdfPage page, PageInsertTemplate template)
        {
            using var gfx = XGraphics.FromPdfPage(page);
            double width = page.Width.Point;
            double height = page.Height.Point;

            gfx.DrawRectangle(new XSolidBrush(GetTemplateBackground(template)), 0, 0, width, height);

            switch (template)
            {
                case PageInsertTemplate.Notebook:
                    DrawNotebookTemplate(gfx, width, height);
                    break;
                case PageInsertTemplate.Lined:
                    DrawLinedTemplate(gfx, width, height);
                    break;
                case PageInsertTemplate.Quadrille:
                    DrawQuadrilleTemplate(gfx, width, height);
                    break;
                case PageInsertTemplate.Dotted:
                    DrawDottedTemplate(gfx, width, height);
                    break;
                case PageInsertTemplate.Music:
                    DrawMusicTemplate(gfx, width, height);
                    break;
                case PageInsertTemplate.Cornell:
                    DrawCornellTemplate(gfx, width, height);
                    break;
                case PageInsertTemplate.Checklist:
                    DrawChecklistTemplate(gfx, width, height);
                    break;
                case PageInsertTemplate.TwoColumn:
                    DrawTwoColumnTemplate(gfx, width, height);
                    break;
            }
        }

        private static XColor GetTemplateBackground(PageInsertTemplate template)
        {
            return template == PageInsertTemplate.Notebook
                ? XColor.FromArgb(255, 253, 249, 238)
                : XColors.White;
        }

        private static void DrawNotebookTemplate(XGraphics gfx, double width, double height)
        {
            DrawLinedTemplate(gfx, width, height, topMargin: 46, leftMargin: 54, rightMargin: 36, lineSpacing: 24, lineColor: XColor.FromArgb(255, 200, 221, 252));

            var marginPen = new XPen(XColor.FromArgb(255, 239, 68, 68), 1.3);
            double marginX = 78;
            gfx.DrawLine(marginPen, marginX, 28, marginX, height - 28);
        }

        private static void DrawLinedTemplate(XGraphics gfx, double width, double height, double topMargin = 40, double leftMargin = 30, double rightMargin = 30, double lineSpacing = 24, XColor? lineColor = null)
        {
            var pen = new XPen(lineColor ?? XColor.FromArgb(255, 203, 213, 225), 0.9);
            for (double y = topMargin; y < height - 24; y += lineSpacing)
                gfx.DrawLine(pen, leftMargin, y, width - rightMargin, y);
        }

        private static void DrawQuadrilleTemplate(XGraphics gfx, double width, double height)
        {
            var majorPen = new XPen(XColor.FromArgb(255, 191, 219, 254), 0.95);
            var minorPen = new XPen(XColor.FromArgb(255, 219, 234, 254), 0.65);
            const double spacing = 18;

            for (double x = 24; x < width - 24; x += spacing)
            {
                bool isMajor = Math.Abs(((x - 24) / spacing) % 4) < 0.001;
                gfx.DrawLine(isMajor ? majorPen : minorPen, x, 24, x, height - 24);
            }

            for (double y = 24; y < height - 24; y += spacing)
            {
                bool isMajor = Math.Abs(((y - 24) / spacing) % 4) < 0.001;
                gfx.DrawLine(isMajor ? majorPen : minorPen, 24, y, width - 24, y);
            }
        }

        /// <summary>Task 30: reorder pages while preserving their PDF contents.</summary>
        public async Task ReorderPagesAsync(string filePath, int fromIndex, int toIndex)
        {
            await RunDocumentWriteAsync(filePath, async () =>
            {
                await Task.Run(() => ReorderPagesCore(filePath, fromIndex, toIndex)).ConfigureAwait(false);
                await ReloadDocumentFromFileAsync(filePath).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>Task 30: duplicate one page immediately after itself.</summary>
        public async Task DuplicatePageAsync(string filePath, int pageIndex)
        {
            await RunDocumentWriteAsync(filePath, async () =>
            {
                await Task.Run(() => DuplicatePageCore(filePath, pageIndex)).ConfigureAwait(false);
                await ReloadDocumentFromFileAsync(filePath).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>Task 31: read the PDF outline using PdfSharpCore.</summary>
        public async Task<IReadOnlyList<PdfOutlineEntry>> GetOutlineAsync(CancellationToken cancellationToken = default)
        {
            string filePath = _sourceFilePath;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return Array.Empty<PdfOutlineEntry>();

            return await Task.Run(() => ReadOutlineCore(filePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Task 34: rotate one page by 90 degrees and persist /Rotate.</summary>
        public async Task RotatePageAsync(string filePath, int pageIndex, int quarterTurns = 1)
        {
            await RunDocumentWriteAsync(filePath, async () =>
            {
                await Task.Run(() => RotatePageCore(filePath, pageIndex, quarterTurns)).ConfigureAwait(false);
                await ReloadDocumentFromFileAsync(filePath).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>Task 37: import a page range from another PDF at an index.</summary>
        public async Task InsertPdfPagesAsync(string targetPath, string sourcePath, int insertIndex, int startPage, int endPage)
        {
            // Import reads source bytes while replacing target bytes.  Hold
            // both process-wide path leases in the coordinator's deterministic
            // order so another instance cannot rewrite the source halfway
            // through the import and crossed A<-B/B<-A calls cannot deadlock.
            await RunDocumentWriteAsync(new[] { targetPath, sourcePath }, async () =>
            {
                await Task.Run(() => InsertPdfPagesCore(targetPath, sourcePath, insertIndex, startPage, endPage)).ConfigureAwait(false);
                await ReloadDocumentFromFileAsync(targetPath).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>Task 37: create a new page and center an image on it.</summary>
        public async Task InsertImagePageAsync(string targetPath, string imagePath, int insertIndex)
        {
            await RunDocumentWriteAsync(targetPath, async () =>
            {
                await Task.Run(() => InsertImagePageCore(targetPath, imagePath, insertIndex)).ConfigureAwait(false);
                await ReloadDocumentFromFileAsync(targetPath).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private static void DrawDottedTemplate(XGraphics gfx, double width, double height)
        {
            var brush = new XSolidBrush(XColor.FromArgb(255, 203, 213, 225));
            const double spacing = 18;
            for (double y = 24; y < height - 24; y += spacing)
            {
                for (double x = 24; x < width - 24; x += spacing)
                    gfx.DrawEllipse(brush, x - 0.8, y - 0.8, 1.6, 1.6);
            }
        }

        private static void DrawMusicTemplate(XGraphics gfx, double width, double height)
        {
            var pen = new XPen(XColor.FromArgb(255, 148, 163, 184), 0.8);
            const double groupSpacing = 72;
            const double lineSpacing = 8;
            for (double top = 38; top < height - 42; top += groupSpacing)
            {
                for (int line = 0; line < 5; line++)
                    gfx.DrawLine(pen, 28, top + line * lineSpacing, width - 28, top + line * lineSpacing);
            }
        }

        private static void DrawCornellTemplate(XGraphics gfx, double width, double height)
        {
            var pen = new XPen(XColor.FromArgb(255, 148, 163, 184), 1.0);
            var faintPen = new XPen(XColor.FromArgb(255, 203, 213, 225), 0.7);
            double top = 52;
            double bottom = height - 92;
            double cueWidth = Math.Min(150, width * 0.25);
            gfx.DrawLine(pen, 28, top, width - 28, top);
            gfx.DrawLine(pen, 28 + cueWidth, top, 28 + cueWidth, bottom);
            gfx.DrawLine(pen, 28, bottom, width - 28, bottom);
            gfx.DrawLine(faintPen, 28, height - 60, width - 28, height - 60);
        }

        private static void DrawChecklistTemplate(XGraphics gfx, double width, double height)
        {
            var boxPen = new XPen(XColor.FromArgb(255, 59, 130, 246), 1.0);
            var linePen = new XPen(XColor.FromArgb(255, 203, 213, 225), 0.8);
            const double left = 42;
            const double boxSize = 11;
            const double rowSpacing = 30;

            for (double y = 42; y < height - 30; y += rowSpacing)
            {
                gfx.DrawRectangle(boxPen, left, y - boxSize + 1, boxSize, boxSize);
                gfx.DrawLine(linePen, left + 22, y, width - 36, y);
            }
        }

        private static void DrawTwoColumnTemplate(XGraphics gfx, double width, double height)
        {
            var dividerPen = new XPen(XColor.FromArgb(255, 147, 197, 253), 1.1);
            var linePen = new XPen(XColor.FromArgb(255, 203, 213, 225), 0.8);
            double center = width / 2.0;
            const double outerMargin = 32;
            const double gutter = 18;

            gfx.DrawLine(dividerPen, center, 30, center, height - 30);
            for (double y = 48; y < height - 26; y += 26)
            {
                gfx.DrawLine(linePen, outerMargin, y, center - gutter, y);
                gfx.DrawLine(linePen, center + gutter, y, width - outerMargin, y);
            }
        }

        private Dictionary<int, Models.PageAnnotation> ExtractAndStripAnnotations(Stream sourceStream, Stream outputStream, CancellationToken cancellationToken)
        {
            var extractedAnnotations = new Dictionary<int, Models.PageAnnotation>();
            var extractedStickyNoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const double dipDpi = 96.0;
            double scale = dipDpi / 72.0;

            using var document = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Modify);

            for (int i = 0; i < document.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.Pages[i];
                double pageHeight = page.Height.Point;
                var pageGeometry = GetPageDisplayGeometry(page);
                var pageAnnots = new Models.PageAnnotation();

                var annots = page.Elements.GetArray("/Annots");
                if (annots != null)
                {
                    var elementsToRemove = new List<PdfItem>();

                    foreach (var annotItem in annots.Elements)
                    {
                        var dict = (annotItem as PdfReference)?.Value as PdfDictionary ?? annotItem as PdfDictionary;
                        if (dict != null)
                        {
                            var subtype = dict.Elements.GetName("/Subtype");

                            if (subtype == "/FreeText" && HasNmPrefix(dict, TextNmPrefix))
                            {
                                var textAnnotation = TryExtractFreeTextAnnotation(dict, pageHeight, scale);
                                if (textAnnotation != null)
                                {
                                    pageAnnots.Texts.Add(textAnnotation);
                                    elementsToRemove.Add(annotItem);
                                }
                            }
                            else if (subtype == "/Ink")
                            {
                                var inkList = dict.Elements.GetArray("/InkList");
                                bool isHiddenInk = HasNmPrefix(dict, HiddenInkNmPrefix);
                                if (!isHiddenInk && !HasNmPrefix(dict, InkNmPrefix))
                                    continue; // preserve foreign ink byte-for-byte

                                string hiddenInkId = dict.Elements.GetString("/NM") ?? string.Empty;
                                if (hiddenInkId.StartsWith(HiddenInkNmPrefix, StringComparison.Ordinal))
                                    hiddenInkId = hiddenInkId.Substring(HiddenInkNmPrefix.Length);
                                bool extractedInk = false;
                                if (inkList != null && inkList.Elements.Count > 0)
                                {
                                    foreach (var strokeItem in inkList.Elements)
                                    {
                                        var pointArray = (strokeItem as PdfReference)?.Value as PdfArray ?? strokeItem as PdfArray;
                                        if (pointArray != null && pointArray.Elements.Count >= 2)
                                        {
                                            var cArray = dict.Elements.GetArray("/C");
                                            byte r = 255, g = 255, b = 255;
                                            if (cArray != null && cArray.Elements.Count >= 3)
                                            {
                                                r = (byte)(GetDouble(cArray.Elements[0]) * 255);
                                                g = (byte)(GetDouble(cArray.Elements[1]) * 255);
                                                b = (byte)(GetDouble(cArray.Elements[2]) * 255);
                                            }

                                            double ca = dict.Elements.ContainsKey("/CA") ? GetDouble(dict.Elements["/CA"], 1.0) : 1.0;
                                            byte a = (byte)(Math.Clamp(ca, 0.0, 1.0) * 255);

                                            var bs = dict.Elements.GetDictionary("/BS");
                                            double size = (bs != null
                                                ? (bs.Elements.ContainsKey("/W") ? GetDouble(bs.Elements["/W"], 2.0) : 2.0)
                                                : 2.0) * scale;

                                            var points = new List<double[]>();

                                            for (int pIdx = 0; pIdx < pointArray.Elements.Count - 1; pIdx += 2)
                                            {
                                                double ptX = GetDouble(pointArray.Elements[pIdx]);
                                                double ptY = GetDouble(pointArray.Elements[pIdx + 1]);
                                                Point displayPoint = pageGeometry.PdfToDisplayDips(ptX, ptY);
                                                points.Add(new[] { displayPoint.X, displayPoint.Y });
                                            }

                                            if (points.Count > 0)
                                            {
                                                if (isHiddenInk)
                                                {
                                                    var hiddenAnnotation = new Models.HiddenInkAnnotation
                                                    {
                                                        Id = string.IsNullOrWhiteSpace(hiddenInkId)
                                                            ? Guid.NewGuid().ToString("N")
                                                            : hiddenInkId,
                                                        A = 255,
                                                        Size = size,
                                                        RevealDurationMs = isHiddenInk
                                                            && dict.Elements.ContainsKey("/WNARevealMs")
                                                            && dict.Elements.GetInteger("/WNARevealMs") > 0
                                                            ? dict.Elements.GetInteger("/WNARevealMs")
                                                            : Models.HiddenInkRevealState.DefaultRevealDurationMs,
                                                        Points = points
                                                    };
                                                    // A legacy hidden mask may omit /C. Keep
                                                    // ordinary ink's historical white fallback,
                                                    // but let the model's new neutral-gray default
                                                    // supply the production Hidden Ink fallback.
                                                    if (cArray != null && cArray.Elements.Count >= 3)
                                                    {
                                                        hiddenAnnotation.R = r;
                                                        hiddenAnnotation.G = g;
                                                        hiddenAnnotation.B = b;
                                                    }
                                                    pageAnnots.HiddenInks.Add(hiddenAnnotation);
                                                }
                                                else
                                                {
                                                    pageAnnots.Strokes.Add(new Models.StrokeAnnotation
                                                    {
                                                        R = r,
                                                        G = g,
                                                        B = b,
                                                        A = a,
                                                        IsHighlighter = ca < 1.0,
                                                        Size = size,
                                                        FitToCurve = dict.Elements.ContainsKey("/WNAFitToCurve")
                                                            ? dict.Elements.GetInteger("/WNAFitToCurve") != 0
                                                            : !LooksLikeLegacyCrispRectangle(points),
                                                        ShapeGroupId = dict.Elements.GetString("/WNAShapeGroup") ?? string.Empty,
                                                        ShapeKind = dict.Elements.GetString("/WNAShapeKind") ?? string.Empty,
                                                        ShapePartIndex = dict.Elements.ContainsKey("/WNAShapePart")
                                                            ? dict.Elements.GetInteger("/WNAShapePart")
                                                            : 0,
                                                        IsDashedShape = dict.Elements.ContainsKey("/WNAShapeDashed")
                                                            && dict.Elements.GetInteger("/WNAShapeDashed") != 0,
                                                        Points = points
                                                    });
                                                }
                                                extractedInk = true;
                                            }
                                        }
                                    }
                                }
                                if (extractedInk)
                                    elementsToRemove.Add(annotItem);
                            }
                            else if (subtype == "/Highlight" &&
                                (HasNmPrefix(dict, AreaHighlightNmPrefix) || HasNmPrefix(dict, HighlightNmPrefix)))
                            {
                                var highlightNm = dict.Elements.GetString("/NM");
                                if (highlightNm != null && highlightNm.StartsWith(AreaHighlightNmPrefix, StringComparison.Ordinal))
                                {
                                    // Task 27: our own rectangular area highlight —
                                    // rebuild the area model from the quad rect.
                                    var areaHighlight = TryExtractAreaHighlight(dict, pageHeight, scale);
                                    if (areaHighlight != null)
                                    {
                                        pageAnnots.AreaHighlights.Add(areaHighlight);
                                        elementsToRemove.Add(annotItem);
                                    }
                                }
                                else
                                {
                                var quadPoints = dict.Elements.GetArray("/QuadPoints");
                                if (quadPoints != null && quadPoints.Elements.Count >= 8)
                                {
                                    var highlightAnnot = new Models.HighlightAnnotation();

                                    var cArray = dict.Elements.GetArray("/C");
                                    if (cArray != null && cArray.Elements.Count >= 3)
                                    {
                                        highlightAnnot.R = (byte)(GetDouble(cArray.Elements[0]) * 255);
                                        highlightAnnot.G = (byte)(GetDouble(cArray.Elements[1]) * 255);
                                        highlightAnnot.B = (byte)(GetDouble(cArray.Elements[2]) * 255);
                                    }

                                    double ca = dict.Elements.ContainsKey("/CA") ? GetDouble(dict.Elements["/CA"], 1.0) : 1.0;
                                    highlightAnnot.A = (byte)(ca * 255);

                                    // QuadPoints format can vary between Edge, Chrome, and Acrobat
                                    for (int pIdx = 0; pIdx < quadPoints.Elements.Count - 7; pIdx += 8)
                                    {
                                        double qx1 = GetDouble(quadPoints.Elements[pIdx]);
                                        double qy1 = GetDouble(quadPoints.Elements[pIdx + 1]);
                                        double qx2 = GetDouble(quadPoints.Elements[pIdx + 2]);
                                        double qy2 = GetDouble(quadPoints.Elements[pIdx + 3]);
                                        double qx3 = GetDouble(quadPoints.Elements[pIdx + 4]);
                                        double qy3 = GetDouble(quadPoints.Elements[pIdx + 5]);
                                        double qx4 = GetDouble(quadPoints.Elements[pIdx + 6]);
                                        double qy4 = GetDouble(quadPoints.Elements[pIdx + 7]);

                                        double minX = Math.Min(Math.Min(qx1, qx2), Math.Min(qx3, qx4));
                                        double maxX = Math.Max(Math.Max(qx1, qx2), Math.Max(qx3, qx4));
                                        double minY = Math.Min(Math.Min(qy1, qy2), Math.Min(qy3, qy4));
                                        double maxY = Math.Max(Math.Max(qy1, qy2), Math.Max(qy3, qy4));

                                        double x_ui = minX * scale;
                                        double w_ui = (maxX - minX) * scale;
                                        double h_ui = (maxY - minY) * scale;
                                        double y_ui = (pageHeight - maxY) * scale; // Invert Y

                                        highlightAnnot.Rects.Add(new[] { x_ui, y_ui, w_ui, h_ui });
                                    }

                                    if (highlightAnnot.Rects.Count > 0)
                                    {
                                        pageAnnots.Highlights.Add(highlightAnnot);
                                        elementsToRemove.Add(annotItem);
                                    }
                                }
                                }
                            }
                            else if ((subtype == "/Underline" || subtype == "/StrikeOut" || subtype == "/Squiggly")
                                && HasNmPrefix(dict, TextMarkupNmPrefix))
                            {
                                // Task 25: only our own text markups are
                                // extracted. Foreign annotations remain in the
                                // source PDF so a later save cannot rewrite or
                                // discard another application's metadata.
                                var markup = TryExtractTextMarkup(dict, subtype, pageHeight, scale);
                                if (markup != null)
                                {
                                    pageAnnots.TextMarkups.Add(markup);
                                    elementsToRemove.Add(annotItem);
                                }
                            }
                            else if (subtype == "/Text")
                            {
                                // Task 26: our own sticky notes. Foreign /Text
                                // annotations (comment threads from other apps)
                                // stay untouched for pdfium to render.
                                var stickyNote = TryExtractStickyNote(dict, pageHeight, scale);
                                if (stickyNote != null)
                                {
                                    EnsureUniqueStickyNoteId(stickyNote, extractedStickyNoteIds);
                                    pageAnnots.StickyNotes.Add(stickyNote);
                                    elementsToRemove.Add(annotItem);
                                }
                            }
                            else if (subtype == "/Stamp")
                            {
                                // Task 19: our own image annotations. Foreign
                                // /Stamp annotations (signatures, seals) are
                                // left untouched for pdfium to render.
                                var imageAnnotation = TryExtractImageAnnotation(dict, pageHeight, scale);
                                if (imageAnnotation != null)
                                {
                                    pageAnnots.Images.Add(imageAnnotation);
                                    elementsToRemove.Add(annotItem);
                                }
                            }
                        }
                    }

                    foreach (var item in elementsToRemove)
                    {
                        annots.Elements.Remove(item);
                    }
                }

                if (pageAnnots.Strokes.Count > 0 || pageAnnots.Texts.Count > 0 || pageAnnots.Highlights.Count > 0
                    || pageAnnots.Images.Count > 0 || pageAnnots.TextMarkups.Count > 0
                    || pageAnnots.AreaHighlights.Count > 0 || pageAnnots.StickyNotes.Count > 0
                    || pageAnnots.HiddenInks.Count > 0)
                {
                    extractedAnnotations[i] = pageAnnots;
                }
            }

            PdfAtomicFile.RemoveInvalidCropBoxes(document);
            document.Save(outputStream);
            return extractedAnnotations;
        }

        public async Task<BitmapImage> RenderPageAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _documentLock.WaitAsync(cancellationToken);
            try
            {
                if (_pdfDocument == null) return null;
                if (pageIndex < 0 || pageIndex >= _pdfDocument.PageCount) return null;

                const int renderDpi = 192;
                var size = _pdfDocument.PageSizes[pageIndex];
                int width = (int)(size.Width * renderDpi / 72.0);
                int height = (int)(size.Height * renderDpi / 72.0);

                using (var image = _pdfDocument.Render(pageIndex, width, height, renderDpi, renderDpi, PdfRenderFlags.Annotations))
                {
                    using (var ms = new MemoryStream())
                    {
                        image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Seek(0, SeekOrigin.Begin);

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }
                }
            }
            finally
            {
                _documentLock.Release();
            }
        }

        public async Task<byte[]> RenderPagePngBytesAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            return await RenderPagePngBytesAsync(pageIndex, 1.0, cancellationToken);
        }

        public async Task<byte[]> RenderPagePngBytesAsync(int pageIndex, double dpiScale, CancellationToken cancellationToken = default)
        {
            System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync: start for page {pageIndex}, dpiScale={dpiScale}");

            if (_pdfDocument == null)
            {
                System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync: _pdfDocument is null");
                return null;
            }
            if (pageIndex < 0 || pageIndex >= _pdfDocument.PageCount)
            {
                System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync: pageIndex {pageIndex} out of range");
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _documentLock.WaitAsync(cancellationToken);
            try
            {
                if (_pdfDocument == null)
                {
                    System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync: _pdfDocument is null after lock");
                    return null;
                }
                if (pageIndex < 0 || pageIndex >= _pdfDocument.PageCount)
                {
                    System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync: pageIndex check failed after lock");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync: getting page {pageIndex}");
                int renderDpi = PdfRenderPolicy.CalculateRenderDpi(dpiScale);
                var size = _pdfDocument.PageSizes[pageIndex];
                int width = (int)(size.Width * renderDpi / 72.0);
                int height = (int)(size.Height * renderDpi / 72.0);

                System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync: page {pageIndex} size: {width}x{height}");

                using (var image = _pdfDocument.Render(pageIndex, width, height, renderDpi, renderDpi, PdfRenderFlags.Annotations))
                {
                    using (var ms = new MemoryStream())
                    {
                        image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        var bytes = ms.ToArray();
                        System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync: returning {bytes.Length} bytes");
                        return bytes;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"RenderPagePngBytesAsync STACK: {ex.StackTrace}");
                throw;
            }
            finally
            {
                _documentLock.Release();
            }
        }

        /// <summary>
        /// Fast render path: converts GDI+ Bitmap 鈫?frozen BitmapSource directly,
        /// bypassing PNG encode/decode. ~5-10x faster than the PNG roundtrip.
        /// </summary>
        public async Task<BitmapSource> RenderPageBitmapSourceAsync(int pageIndex, double dpiScale, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _documentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_pdfDocument == null) return null;
                if (pageIndex < 0 || pageIndex >= _pdfDocument.PageCount) return null;

                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int renderDpi = PdfRenderPolicy.CalculateRenderDpi(dpiScale);
                    var size = _pdfDocument.PageSizes[pageIndex];
                    int width = (int)(size.Width * renderDpi / 72.0);
                    int height = (int)(size.Height * renderDpi / 72.0);

                    using var gdiBitmap = (System.Drawing.Bitmap)_pdfDocument.Render(pageIndex, width, height, renderDpi, renderDpi, PdfRenderFlags.Annotations);
                    var bmpData = gdiBitmap.LockBits(
                        new System.Drawing.Rectangle(0, 0, width, height),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    try
                    {
                        var result = BitmapSource.Create(
                            width, height,
                            renderDpi, renderDpi,
                            PixelFormats.Bgra32,
                            null,
                            bmpData.Scan0,
                            bmpData.Stride * height,
                            bmpData.Stride);
                        result.Freeze();
                        return result;
                    }
                    finally
                    {
                        gdiBitmap.UnlockBits(bmpData);
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _documentLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Acquire the same admission boundary as saves before publishing
            // DisposeStarted. An already-admitted save completes (or fails)
            // before disposal begins; a waiter observes the state only after
            // it acquires this gate and cannot reload a native document.
            await _lifetimeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Interlocked.CompareExchange(ref _disposeState, DisposeStarted, DisposeActive) != DisposeActive)
                {
                    await _disposeCompletion.Task.ConfigureAwait(false);
                    return;
                }

                await _documentLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    DisposeCurrentDocument();
                    ExtractedAnnotations = new Dictionary<int, Models.PageAnnotation>();
                    _sourceFilePath = null;
                }
                finally
                {
                    _documentLock.Release();
                }

                Volatile.Write(ref _disposeState, DisposeCompleted);
                _disposeCompletion.TrySetResult(true);
                GC.SuppressFinalize(this);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _disposeState, DisposeActive);
                _disposeCompletion.TrySetException(ex);
                _disposeCompletion = NewDisposeCompletion();
                throw;
            }
            finally
            {
                _lifetimeGate.Release();
            }
        }

        public Task SaveAnnotationsToPdfAsync(string filePath, Dictionary<int, Models.PageAnnotation> annotations)
        {
            ThrowIfDisposed();
            // PdfService instances have their own document lifetime lock, but
            // an editor can hold more than one instance for the same path
            // (autosave/manual or separate tabs). The shared coordinator must
            // cover the entire write and optional reload, not only the temp
            // file construction, so every reader sees a complete PDF.
            return PdfSaveCoordinator.RunExclusiveAsync(
                filePath,
                () => SaveAnnotationsToPdfCoreAsync(filePath, annotations));
        }

        private async Task SaveAnnotationsToPdfCoreAsync(
            string filePath,
            Dictionary<int, Models.PageAnnotation> annotations)
        {
            await RunDocumentWriteUnderLifetimeAsync(async () =>
            {
                ThrowIfDisposed();
                bool requiresReload = _pdfBackingStream == null;
                if (requiresReload)
                    DisposeCurrentDocument();

                await Task.Run(() => SaveAnnotationsCore(filePath, annotations), CancellationToken.None).ConfigureAwait(false);

                if (requiresReload)
                {
                    ThrowIfDisposed();
                    await ReloadDocumentFromFileAsync(filePath).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposeState) != DisposeActive)
                throw new ObjectDisposedException(nameof(PdfService));
        }

        private static TaskCompletionSource<bool> NewDisposeCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void SaveAnnotationsCore(string filePath, Dictionary<int, Models.PageAnnotation> annotations)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("PDF to save not found.");

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.IsReadOnly)
                throw new UnauthorizedAccessException($"The file \"{Path.GetFileName(filePath)}\" is read-only. Please disable read-only mode in file properties and try again.");

            string tempPath = CreatePdfTempPath(filePath);

            try
            {
                // Read the entire PDF into memory first to avoid file locking issues
                byte[] pdfBytes;
                using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    pdfBytes = new byte[sourceStream.Length];
                    int bytesRead = sourceStream.Read(pdfBytes, 0, pdfBytes.Length);
                    if (bytesRead != sourceStream.Length)
                    {
                        throw new IOException($"Failed to read complete PDF file. Expected {sourceStream.Length} bytes, but read {bytesRead} bytes.");
                    }
                }

                // Use a memory stream for PDF operations to avoid file system issues
                using (var memoryStream = new MemoryStream(pdfBytes))
                using (var document = PdfReader.Open(memoryStream, PdfDocumentOpenMode.Modify))
                {
                    const double dipDpi = 96.0;
                    double scale = 72.0 / dipDpi;
                    var stickyNoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    for (int i = 0; i < document.PageCount; i++)
                    {
                        var pdfPage = document.Pages[i];
                        double pageHeight = pdfPage.Height.Point;
                        var pageGeometry = GetPageDisplayGeometry(pdfPage);

                        var annots = pdfPage.Elements.GetArray("/Annots");
                        if (annots != null)
                        {
                            var toRemove = new List<PdfItem>();
                            foreach (var item in annots.Elements)
                            {
                                var dict = (item as PdfReference)?.Value as PdfDictionary ?? item as PdfDictionary;
                                if (dict != null)
                                {
                                var sub = dict.Elements.GetName("/Subtype");
                                // Only annotations owned by OpenNotes are
                                // replaced. Foreign annotations remain in the
                                // original PDF untouched.
                                if ((sub == "/FreeText" && HasNmPrefix(dict, TextNmPrefix)) ||
                                    (sub == "/Ink" && (HasNmPrefix(dict, InkNmPrefix) || HasNmPrefix(dict, HiddenInkNmPrefix))) ||
                                    (sub == "/Highlight" && (HasNmPrefix(dict, HighlightNmPrefix) || HasNmPrefix(dict, AreaHighlightNmPrefix))) ||
                                    ((sub == "/Underline" || sub == "/StrikeOut" || sub == "/Squiggly") && HasNmPrefix(dict, TextMarkupNmPrefix)) ||
                                    (sub == "/Stamp" && IsOwnImageStamp(dict)) ||
                                    (sub == "/Text" && HasNmPrefix(dict, StickyNoteNmPrefix)))
                                    toRemove.Add(item);
                                }
                            }

                            foreach (var item in toRemove)
                                annots.Elements.Remove(item);
                        }

                        if (!annotations.TryGetValue(i, out var pageAnnots))
                            continue;

                        foreach (var textItem in pageAnnots.Texts)
                        {
                            var textLines = (textItem.Text ?? string.Empty).Split('\n');
                            double pdfFontSize = textItem.FontSize * scale;
                            double lineHeight = pdfFontSize * 1.4;

                            // CJK path: the hand-written /Helv appearance stream is WinAnsi-only and
                            // garbles non-ASCII text. Build the appearance with XGraphics + an embedded
                            // Unicode CID font subset instead (any renderer can display it).
                            if (ContainsNonAscii(textItem.Text) &&
                                TryCreateCjkFreeTextAnnotation(document, textItem, textLines, pdfFontSize, lineHeight, scale, pageHeight, out var cjkAnnot))
                            {
                                AddAnnotationToPage(pdfPage, cjkAnnot);
                                continue;
                            }

                            // Measure the standard-font path with the selected
                            // text font when the platform resolver can provide
                            // it. The old character-count estimate made narrow
                            // glyphs wrap too late and wide glyphs wrap too soon.
                            var measuredLineWidths = new Dictionary<string, double>(StringComparer.Ordinal);
                            double measuredMaxWidth = 0;
                            bool measuredLatinLayout = false;
                            try
                            {
                                var fontStyle = textItem.Bold && textItem.Italic
                                    ? XFontStyle.BoldItalic
                                    : textItem.Bold
                                        ? XFontStyle.Bold
                                        : textItem.Italic
                                            ? XFontStyle.Italic
                                            : XFontStyle.Regular;
                                var family = string.IsNullOrWhiteSpace(textItem.FontFamily)
                                    ? "Segoe UI"
                                    : textItem.FontFamily;
                                using var measureGfx = XGraphics.CreateMeasureContext(
                                    new XSize(1000, 1000), XGraphicsUnit.Point, XPageDirection.Downwards);
                                var measureFont = new XFont(family, pdfFontSize, fontStyle, XPdfFontOptions.UnicodeDefault);
                                if (textItem.Width > 0)
                                {
                                    // A persisted text rectangle is a real
                                    // layout constraint.
                                    textLines = WrapMeasuredTextLines(
                                        textItem.Text,
                                        Math.Max(1.0, textItem.Width * scale - 8.0),
                                        measureFont,
                                        measureGfx);
                                }

                                foreach (var line in textLines)
                                {
                                    var width = measureGfx.MeasureString(line, measureFont).Width;
                                    measuredLineWidths[line] = width;
                                    measuredMaxWidth = Math.Max(measuredMaxWidth, width);
                                }
                                measuredLatinLayout = true;
                            }
                            catch
                            {
                                // Keep the legacy Helvetica-safe fallback if
                                // a requested Windows font is unavailable.
                            }

                            double estimatedWidth = measuredLatinLayout
                                ? Math.Max(150 * scale, measuredMaxWidth + 12)
                                : Math.Max(150 * scale, textLines.Max(l => l.Length) * pdfFontSize * 0.55 + 12);
                            double estimatedHeight = textLines.Length * lineHeight + pdfFontSize * 0.4;
                            double w = textItem.Width > 0 ? textItem.Width * scale : estimatedWidth;
                            if (textItem.Width > 0 && !measuredLatinLayout)
                            {
                                textLines = WrapTextLines(
                                    textItem.Text,
                                    Math.Max(1.0, w - 8.0),
                                    Math.Max(1.0, pdfFontSize * 0.55));
                            }
                            double h = textItem.Height > 0 ? textItem.Height * scale : estimatedHeight;
                            if (textItem.Height <= 0)
                                h = textLines.Length * lineHeight + pdfFontSize * 0.4;

                            double x = textItem.X * scale;
                            double y = pageHeight - (textItem.Y * scale) - h;

                            var xRect = new XRect(x, y, w, h);
                            var annot = new PdfDictionary(document);
                            annot.Elements.SetName(PdfAnnotation.Keys.Subtype, "/FreeText");
                            annot.Elements.SetRectangle(PdfAnnotation.Keys.Rect, new PdfSharpPdfRectangle(xRect));
                            annot.Elements.SetString(
                                PdfAnnotation.Keys.Contents,
                                textItem.Text,
                                ContainsNonAscii(textItem.Text) ? PdfStringEncoding.Unicode : PdfStringEncoding.RawEncoding);
                            annot.Elements.SetString("/NM", $"{TextNmPrefix}{Guid.NewGuid()}");
                            annot.Elements.SetInteger("/F", 4); // Printable
                            SetTextAnnotationLayoutMetadata(annot, textItem);

                            double r2 = textItem.R / 255.0, g2 = textItem.G / 255.0, b2 = textItem.B / 255.0;
                            // Remove border
                            var bsForText = new PdfDictionary();
                            bsForText.Elements.SetInteger("/W", 0);
                            annot.Elements["/BS"] = bsForText;

                            // /DA — required by spec
                            annot.Elements.SetString("/DA", $"/Helv {pdfFontSize:F2} Tf {r2:F3} {g2:F3} {b2:F3} rg");
                            annot.Elements.SetString("/DS", BuildRichTextStyleString(textItem));

                            // Build appearance stream as raw PDF content stream
                            var apStream = new StringBuilder();
                            apStream.AppendLine("q");
                            apStream.AppendLine($"{r2:F3} {g2:F3} {b2:F3} rg");
                            apStream.AppendLine("BT");
                            apStream.AppendLine($"/Helv {pdfFontSize:F2} Tf");
                            double yApStream = h - lineHeight + (lineHeight - pdfFontSize) / 2;
                            double previousX = 4;
                            for (int li = 0; li < textLines.Length; li++)
                            {
                                // Escape parentheses in content
                                string escaped = textLines[li].Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                                double textWidth = measuredLineWidths.TryGetValue(textLines[li], out var measuredWidth)
                                    ? measuredWidth
                                    : Math.Max(0, textLines[li].Length * pdfFontSize * 0.55);
                                double lineX = GetAlignedTextOffset(textWidth, w, textItem.Alignment);
                                if (li == 0)
                                    apStream.AppendLine($"{lineX:F2} {yApStream:F2} Td");
                                else
                                    apStream.AppendLine($"{lineX - previousX:F2} {-lineHeight:F2} Td");
                                previousX = lineX;
                                apStream.AppendLine($"({escaped}) Tj");
                            }
                            apStream.AppendLine("ET");
                            apStream.AppendLine("Q");

                            var apNormal = CreateAppearanceStream(
                                document,
                                w,
                                h,
                                apStream.ToString(),
                                CreateStandardFontResources(document, textItem.Bold, textItem.Italic));
                            annot.Elements["/AP"] = CreateAppearanceDictionary(document, apNormal);

                            AddAnnotationToPage(pdfPage, annot);
                        }

                        foreach (var stroke in pageAnnots.Strokes)
                        {
                            if (stroke.Points.Count == 0) continue;

                            var dict = new PdfDictionary(document);
                            dict.Elements.SetName(PdfAnnotation.Keys.Subtype, "/Ink");
                            dict.Elements.SetString("/NM", $"{InkNmPrefix}{Guid.NewGuid()}");
                            dict.Elements.SetInteger("/F", 4);
                            dict.Elements.SetInteger("/WNAFitToCurve", stroke.FitToCurve ? 1 : 0);
                            if (!string.IsNullOrWhiteSpace(stroke.ShapeGroupId))
                                dict.Elements.SetString("/WNAShapeGroup", stroke.ShapeGroupId);
                            if (!string.IsNullOrWhiteSpace(stroke.ShapeKind))
                                dict.Elements.SetString("/WNAShapeKind", stroke.ShapeKind);
                            if (!string.IsNullOrWhiteSpace(stroke.ShapeGroupId) || stroke.ShapePartIndex != 0)
                                dict.Elements.SetInteger("/WNAShapePart", stroke.ShapePartIndex);
                            if (stroke.IsDashedShape)
                                dict.Elements.SetInteger("/WNAShapeDashed", 1);

                            var colorArray = new PdfArray();
                            colorArray.Elements.Add(new PdfReal(stroke.R / 255.0));
                            colorArray.Elements.Add(new PdfReal(stroke.G / 255.0));
                            colorArray.Elements.Add(new PdfReal(stroke.B / 255.0));
                            dict.Elements.Add("/C", colorArray);

                            double opacity = stroke.IsHighlighter ? 0.5 : stroke.A / 255.0;
                            if (opacity < 1.0)
                                dict.Elements.SetReal("/CA", opacity);

                            double strokeWidth = Math.Max(stroke.Size * scale, 0.5);
                            var bsDict = new PdfDictionary();
                            bsDict.Elements.SetName("/Type", "/Border");
                            bsDict.Elements.SetReal("/W", strokeWidth);
                            dict.Elements.Add("/BS", bsDict);

                            var pdfPoints = new List<Point>(stroke.Points.Count);
                            var inkListArray = new PdfArray();
                            var pointArray = new PdfArray();
                            foreach (var pt in stroke.Points)
                            {
                                Point pdfPoint = pageGeometry.DisplayDipsToPdf(pt[0], pt[1]);
                                double pdfX = pdfPoint.X;
                                double pdfY = pdfPoint.Y;
                                pointArray.Elements.Add(new PdfReal(pdfX));
                                pointArray.Elements.Add(new PdfReal(pdfY));
                                pdfPoints.Add(new Point(pdfX, pdfY));
                            }
                            inkListArray.Elements.Add(pointArray);
                            dict.Elements.Add("/InkList", inkListArray);

                            if (pdfPoints.Count == 1)
                                pdfPoints.Add(new Point(pdfPoints[0].X + strokeWidth, pdfPoints[0].Y));

                            double padding = Math.Max(strokeWidth, 1.0);
                            double minX = Math.Max(pageGeometry.Left, pdfPoints.Min(point => point.X) - padding);
                            double maxX = Math.Min(pageGeometry.Right, pdfPoints.Max(point => point.X) + padding);
                            double minY = Math.Max(pageGeometry.Bottom, pdfPoints.Min(point => point.Y) - padding);
                            double maxY = Math.Min(pageGeometry.Top, pdfPoints.Max(point => point.Y) + padding);
                            double appearanceWidth = Math.Max(1.0, maxX - minX);
                            double appearanceHeight = Math.Max(1.0, maxY - minY);

                            dict.Elements.SetRectangle(
                                PdfAnnotation.Keys.Rect,
                                new PdfSharpPdfRectangle(new XRect(minX, minY, appearanceWidth, appearanceHeight)));

                            var appearanceStream = new StringBuilder();
                            appearanceStream.AppendLine("q");
                            if (opacity < 1.0)
                                appearanceStream.AppendLine("/GS1 gs");
                            appearanceStream.AppendLine($"{stroke.R / 255.0:F3} {stroke.G / 255.0:F3} {stroke.B / 255.0:F3} RG");
                            appearanceStream.AppendLine($"{strokeWidth:F2} w");
                            appearanceStream.AppendLine("1 J");
                            appearanceStream.AppendLine("1 j");
                            appearanceStream.AppendLine($"{pdfPoints[0].X - minX:F2} {pdfPoints[0].Y - minY:F2} m");
                            for (int pointIndex = 1; pointIndex < pdfPoints.Count; pointIndex++)
                            {
                                var point = pdfPoints[pointIndex];
                                appearanceStream.AppendLine($"{point.X - minX:F2} {point.Y - minY:F2} l");
                            }
                            appearanceStream.AppendLine("S");
                            appearanceStream.AppendLine("Q");

                            var appearance = CreateAppearanceStream(
                                document,
                                appearanceWidth,
                                appearanceHeight,
                                appearanceStream.ToString(),
                                CreateAppearanceResources(document, opacity, stroke.IsHighlighter));
                            dict.Elements["/AP"] = CreateAppearanceDictionary(document, appearance);

                            AddAnnotationToPage(pdfPage, dict);
                        }

                        // Study mode masks are exported as opaque /Ink
                        // annotations with an ownership prefix. Their live
                        // reveal state is deliberately ignored: a saved PDF
                        // must remain covered when opened elsewhere.
                        foreach (var hiddenInk in pageAnnots.HiddenInks ?? new List<Models.HiddenInkAnnotation>())
                        {
                            WriteHiddenInkAnnotation(document, pdfPage, hiddenInk, scale, pageGeometry);
                        }

                        foreach (var highlight in pageAnnots.Highlights)
                        {
                            WriteHighlightAnnotation(document, pdfPage, highlight, scale, pageHeight, HighlightNmPrefix);
                        }

                        // Task 27: area highlights reuse the /Highlight writer with
                        // a single rect quad and their own /NM prefix so the loader
                        // can tell them apart from text-quad highlights.
                        foreach (var area in pageAnnots.AreaHighlights)
                        {
                            var asHighlight = new Models.HighlightAnnotation
                            {
                                R = area.R,
                                G = area.G,
                                B = area.B,
                                A = area.A
                            };
                            asHighlight.Rects.Add(new[] { area.X, area.Y, area.Width, area.Height });
                            WriteHighlightAnnotation(document, pdfPage, asHighlight, scale, pageHeight, AreaHighlightNmPrefix);
                        }

                        // Task 25: underline / strike-out / squiggly markups.
                        foreach (var markup in pageAnnots.TextMarkups)
                        {
                            WriteTextMarkupAnnotation(document, pdfPage, markup, scale, pageHeight);
                        }

                        // Task 26: sticky notes as standard /Text annotations.
                        foreach (var note in pageAnnots.StickyNotes)
                        {
                            WriteStickyNoteAnnotation(document, pdfPage, note, scale, pageHeight, stickyNoteIds);
                        }

                        // Task 19: image annotations — /Stamp with an XForm
                        // appearance that draws the image (visual for external
                        // viewers) and the original encoded bytes in /Contents
                        // as base64 (lossless round-trip for our own loader).
                        foreach (var image in pageAnnots.Images)
                        {
                            byte[] imageBytes;
                            try { imageBytes = Convert.FromBase64String(image.ImageDataBase64 ?? ""); }
                            catch { continue; }
                            if (imageBytes.Length == 0) continue;

                            double w = Math.Max(1.0, image.Width * scale);
                            double h = Math.Max(1.0, image.Height * scale);
                            double x = image.X * scale;
                            double y = pageHeight - (image.Y * scale) - h;

                            try
                            {
                                // PdfSharpCore's FromStream takes a stream FACTORY —
                                // it re-reads the stream while saving the document,
                                // so hand it a fresh MemoryStream per call.
                                var xImage = XImage.FromStream(() => new MemoryStream(imageBytes));

                                var form = new XForm(document, new XSize(w, h));
                                using (var gfx = XGraphics.FromForm(form))
                                {
                                    gfx.DrawImage(xImage, 0, 0, w, h);
                                }
                                form.DrawingFinished();

                                var pdfForm = XFormPdfFormProperty?.GetValue(form) as PdfDictionary;
                                if (pdfForm == null)
                                    continue;

                                var annot = new PdfDictionary(document);
                                annot.Elements.SetName(PdfAnnotation.Keys.Subtype, "/Stamp");
                                annot.Elements.SetRectangle(PdfAnnotation.Keys.Rect, new PdfSharpPdfRectangle(new XRect(x, y, w, h)));
                                annot.Elements.SetString(PdfAnnotation.Keys.Contents, Convert.ToBase64String(imageBytes));
                                annot.Elements.SetString("/NM", $"wna_img_{Guid.NewGuid()}");
                                annot.Elements.SetInteger("/F", 4); // Printable
                                annot.Elements["/AP"] = CreateAppearanceDictionary(document, pdfForm);

                                AddAnnotationToPage(pdfPage, annot);
                            }
                            catch
                            {
                                // A single broken image must not abort the save.
                            }
                        }
                    }

                    // Save and flush the complete document before replacing
                    // the target path.
                    PdfAtomicFile.SaveDocument(document, tempPath);
                }

                // Now that the document is saved and streams are closed, move the temp file
                try
                {
                    PdfAtomicFile.Replace(tempPath, filePath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new UnauthorizedAccessException(
                        $"Cannot save to \"{Path.GetFileName(filePath)}\". The file may be open in another program (e.g., PDF reader). " +
                        $"Please close the file and try again.", ex);
                }
                catch (IOException ex)
                {
                    throw new IOException(
                        $"Cannot save to \"{Path.GetFileName(filePath)}\". The file may be open in another program. " +
                        $"Please close the file and try again.", ex);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private void WriteHiddenInkAnnotation(
            PdfSharpCore.Pdf.PdfDocument document,
            PdfSharpCore.Pdf.PdfPage pdfPage,
            Models.HiddenInkAnnotation hiddenInk,
            double scale,
            PdfPageDisplayGeometry pageGeometry)
        {
            if (hiddenInk?.Points == null || hiddenInk.Points.Count == 0)
                return;

            var points = hiddenInk.Points
                .Where(point => point != null && point.Length >= 2
                    && double.IsFinite(point[0]) && double.IsFinite(point[1]))
                .Select(point => pageGeometry.DisplayDipsToPdf(point[0], point[1]))
                .ToList();
            if (points.Count == 0)
                return;
            if (points.Count == 1)
                points.Add(new Point(points[0].X + Math.Max(hiddenInk.Size * scale, 0.5), points[0].Y));

            double strokeWidth = Math.Max(hiddenInk.Size * scale, 0.5);
            double padding = Math.Max(strokeWidth, 1.0);
            double minX = Math.Max(pageGeometry.Left, points.Min(point => point.X) - padding);
            double maxX = Math.Min(pageGeometry.Right, points.Max(point => point.X) + padding);
            double minY = Math.Max(pageGeometry.Bottom, points.Min(point => point.Y) - padding);
            double maxY = Math.Min(pageGeometry.Top, points.Max(point => point.Y) + padding);
            double appearanceWidth = Math.Max(1.0, maxX - minX);
            double appearanceHeight = Math.Max(1.0, maxY - minY);

            var dict = new PdfDictionary(document);
            dict.Elements.SetName(PdfAnnotation.Keys.Subtype, "/Ink");
            dict.Elements.SetString("/NM", $"{HiddenInkNmPrefix}{hiddenInk.Id}");
            dict.Elements.SetInteger("/F", 4);
            dict.Elements.SetInteger("/WNARevealMs", hiddenInk.RevealDurationMs > 0
                ? hiddenInk.RevealDurationMs
                : Models.HiddenInkRevealState.DefaultRevealDurationMs);

            var colorArray = new PdfArray();
            colorArray.Elements.Add(new PdfReal(hiddenInk.R / 255.0));
            colorArray.Elements.Add(new PdfReal(hiddenInk.G / 255.0));
            colorArray.Elements.Add(new PdfReal(hiddenInk.B / 255.0));
            dict.Elements.Add("/C", colorArray);
            dict.Elements.SetReal("/CA", 1.0);

            var bsDict = new PdfDictionary();
            bsDict.Elements.SetName("/Type", "/Border");
            bsDict.Elements.SetReal("/W", strokeWidth);
            dict.Elements.Add("/BS", bsDict);

            var inkListArray = new PdfArray();
            var pointArray = new PdfArray();
            foreach (var point in points)
            {
                pointArray.Elements.Add(new PdfReal(point.X));
                pointArray.Elements.Add(new PdfReal(point.Y));
            }
            inkListArray.Elements.Add(pointArray);
            dict.Elements.Add("/InkList", inkListArray);
            dict.Elements.SetRectangle(
                PdfAnnotation.Keys.Rect,
                new PdfSharpPdfRectangle(new XRect(minX, minY, appearanceWidth, appearanceHeight)));

            var appearanceStream = new StringBuilder();
            appearanceStream.AppendLine("q");
            appearanceStream.AppendLine($"{hiddenInk.R / 255.0:F3} {hiddenInk.G / 255.0:F3} {hiddenInk.B / 255.0:F3} RG");
            appearanceStream.AppendLine($"{strokeWidth:F2} w");
            appearanceStream.AppendLine("1 J");
            appearanceStream.AppendLine("1 j");
            appearanceStream.AppendLine($"{points[0].X - minX:F2} {points[0].Y - minY:F2} m");
            for (int index = 1; index < points.Count; index++)
                appearanceStream.AppendLine($"{points[index].X - minX:F2} {points[index].Y - minY:F2} l");
            appearanceStream.AppendLine("S");
            appearanceStream.AppendLine("Q");

            var appearance = CreateAppearanceStream(
                document,
                appearanceWidth,
                appearanceHeight,
                appearanceStream.ToString(),
                CreateAppearanceResources(document, 1.0, false));
            dict.Elements["/AP"] = CreateAppearanceDictionary(document, appearance);
            AddAnnotationToPage(pdfPage, dict);
        }

        private void AddAnnotationToPage(PdfSharpCore.Pdf.PdfPage page, PdfDictionary annotation)
        {
            if (!annotation.Elements.ContainsKey("/Type"))
                annotation.Elements.SetName("/Type", "/Annot");
            if (!annotation.Elements.ContainsKey("/F"))
                annotation.Elements.SetInteger("/F", 4);
            if (!annotation.Elements.ContainsKey("/P") && page.Reference != null)
                annotation.Elements["/P"] = page.Reference;
            // Register as an indirect object (PDF spec §12.3.3 requires annotations to be indirect
            // objects referenced by N 0 R). Inline annotation dicts can confuse strict viewers like Edge.
            if (annotation.Reference == null)
                page.Owner.Internals.AddObject(annotation);

            var annots = page.Elements.GetArray("/Annots");
            if (annots == null)
            {
                annots = new PdfArray(page.Owner);
                page.Elements.Add("/Annots", annots);
            }
            annots.Elements.Add(annotation.Reference);
        }

        /// <summary>
        /// Writes one /Highlight annotation (text quads AND Task 27 rectangular
        /// area highlights — the latter pass their own /NM prefix so the loader
        /// can distinguish them).
        /// </summary>
        private void WriteHighlightAnnotation(
            PdfSharpCore.Pdf.PdfDocument document,
            PdfSharpCore.Pdf.PdfPage pdfPage,
            Models.HighlightAnnotation highlight,
            double scale,
            double pageHeight,
            string nmPrefix)
        {
            if (highlight.Rects.Count == 0) return;

            var dict = new PdfDictionary(document);
            dict.Elements.SetName(PdfAnnotation.Keys.Subtype, "/Highlight");
            dict.Elements.SetString("/NM", $"{nmPrefix}{Guid.NewGuid():N}");
            dict.Elements.SetInteger("/F", 4);

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            var quadPoints = new PdfArray();
            var appearanceRects = new List<XRect>(highlight.Rects.Count);

            foreach (var rectInfo in highlight.Rects)
            {
                double x_ui = rectInfo[0];
                double y_ui = rectInfo[1];
                double w_ui = rectInfo[2];
                double h_ui = rectInfo[3];

                double x1 = x_ui * scale;
                double y1 = pageHeight - (y_ui * scale); // Top Y in PDF coords
                double x2 = (x_ui + w_ui) * scale;
                double y2 = pageHeight - ((y_ui + h_ui) * scale); // Bottom Y in PDF coords

                minX = Math.Min(minX, Math.Min(x1, x2));
                minY = Math.Min(minY, Math.Min(y1, y2));
                maxX = Math.Max(maxX, Math.Max(x1, x2));
                maxY = Math.Max(maxY, Math.Max(y1, y2));
                appearanceRects.Add(new XRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y1 - y2)));

                // QuadPoints: [TL.X, TL.Y, TR.X, TR.Y, BL.X, BL.Y, BR.X, BR.Y]
                quadPoints.Elements.Add(new PdfReal(x1));
                quadPoints.Elements.Add(new PdfReal(y1));
                quadPoints.Elements.Add(new PdfReal(x2));
                quadPoints.Elements.Add(new PdfReal(y1));
                quadPoints.Elements.Add(new PdfReal(x1));
                quadPoints.Elements.Add(new PdfReal(y2));
                quadPoints.Elements.Add(new PdfReal(x2));
                quadPoints.Elements.Add(new PdfReal(y2));
            }

            dict.Elements.SetRectangle(PdfAnnotation.Keys.Rect, new PdfSharpPdfRectangle(new XRect(minX, minY, maxX - minX, maxY - minY)));
            dict.Elements.Add("/QuadPoints", quadPoints);

            var colorArray = new PdfArray();
            colorArray.Elements.Add(new PdfReal(highlight.R / 255.0));
            colorArray.Elements.Add(new PdfReal(highlight.G / 255.0));
            colorArray.Elements.Add(new PdfReal(highlight.B / 255.0));
            dict.Elements.Add("/C", colorArray);

            double opacity = highlight.A / 255.0;
            if (opacity < 1.0)
                dict.Elements.SetReal("/CA", opacity);

            var appearanceStream = new StringBuilder();
            appearanceStream.AppendLine("q");
            appearanceStream.AppendLine("/GS1 gs");
            appearanceStream.AppendLine($"{highlight.R / 255.0:F3} {highlight.G / 255.0:F3} {highlight.B / 255.0:F3} rg");
            foreach (var rect in appearanceRects)
            {
                appearanceStream.AppendLine($"{rect.X - minX:F2} {rect.Y - minY:F2} {rect.Width:F2} {rect.Height:F2} re");
                appearanceStream.AppendLine("f");
            }
            appearanceStream.AppendLine("Q");

            var appearance = CreateAppearanceStream(
                document,
                Math.Max(1.0, maxX - minX),
                Math.Max(1.0, maxY - minY),
                appearanceStream.ToString(),
                CreateAppearanceResources(document, opacity, true));
            dict.Elements["/AP"] = CreateAppearanceDictionary(document, appearance);

            AddAnnotationToPage(pdfPage, dict);
        }

        /// <summary>
        /// Task 25: writes one /Underline, /StrikeOut or /Squiggly annotation.
        /// QuadPoints come from the model's rects; the appearance stream draws
        /// a line at the baseline (underline), mid-height (strike-out) or a
        /// zigzag (squiggly) per rect, in the bbox-local coordinate system.
        /// </summary>
        private void WriteTextMarkupAnnotation(
            PdfSharpCore.Pdf.PdfDocument document,
            PdfSharpCore.Pdf.PdfPage pdfPage,
            Models.TextMarkupAnnotation markup,
            double scale,
            double pageHeight)
        {
            if (markup == null || markup.Rects.Count == 0) return;

            string subtype = markup.ParsedKind switch
            {
                Models.TextMarkupKind.StrikeOut => "/StrikeOut",
                Models.TextMarkupKind.Squiggly => "/Squiggly",
                _ => "/Underline",
            };

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            var quadPoints = new PdfArray();
            var pdfRects = new List<(double X, double Top, double Bottom, double W)>(markup.Rects.Count);

            foreach (var rect in markup.Rects)
            {
                if (rect == null || rect.Length < 4) continue;

                double x1 = (markup.X + rect[0]) * scale;
                double top = pageHeight - (markup.Y + rect[1]) * scale;
                double x2 = (markup.X + rect[0] + rect[2]) * scale;
                double bottom = pageHeight - (markup.Y + rect[1] + rect[3]) * scale;

                minX = Math.Min(minX, Math.Min(x1, x2));
                minY = Math.Min(minY, Math.Min(top, bottom));
                maxX = Math.Max(maxX, Math.Max(x1, x2));
                maxY = Math.Max(maxY, Math.Max(top, bottom));
                pdfRects.Add((x1, top, bottom, Math.Abs(x2 - x1)));

                quadPoints.Elements.Add(new PdfReal(x1));
                quadPoints.Elements.Add(new PdfReal(top));
                quadPoints.Elements.Add(new PdfReal(x2));
                quadPoints.Elements.Add(new PdfReal(top));
                quadPoints.Elements.Add(new PdfReal(x1));
                quadPoints.Elements.Add(new PdfReal(bottom));
                quadPoints.Elements.Add(new PdfReal(x2));
                quadPoints.Elements.Add(new PdfReal(bottom));
            }

            if (pdfRects.Count == 0) return;

            var dict = new PdfDictionary(document);
            dict.Elements.SetName(PdfAnnotation.Keys.Subtype, subtype);
            dict.Elements.SetString("/NM", $"{TextMarkupNmPrefix}{Guid.NewGuid():N}");
            dict.Elements.SetInteger("/F", 4);
            dict.Elements.SetRectangle(PdfAnnotation.Keys.Rect, new PdfSharpPdfRectangle(new XRect(minX, minY, maxX - minX, maxY - minY)));
            dict.Elements.Add("/QuadPoints", quadPoints);

            var colorArray = new PdfArray();
            colorArray.Elements.Add(new PdfReal(markup.R / 255.0));
            colorArray.Elements.Add(new PdfReal(markup.G / 255.0));
            colorArray.Elements.Add(new PdfReal(markup.B / 255.0));
            dict.Elements.Add("/C", colorArray);

            // Appearance: one line (or zigzag) per rect in bbox-local coords.
            const double lineOffset = 1.2;   // underline/squiggly lift above the baseline
            const double squiggleAmplitude = 1.1;
            const double squiggleWavelength = 5.0;

            var appearanceStream = new StringBuilder();
            appearanceStream.AppendLine("q");
            appearanceStream.AppendLine($"{markup.R / 255.0:F3} {markup.G / 255.0:F3} {markup.B / 255.0:F3} RG");
            appearanceStream.AppendLine("1 w");
            appearanceStream.AppendLine("1 J");

            foreach (var (x, top, bottom, w) in pdfRects)
            {
                double rx = x - minX;
                double rTop = top - minY;
                double rBottom = bottom - minY;

                switch (markup.ParsedKind)
                {
                    case Models.TextMarkupKind.StrikeOut:
                        appearanceStream.AppendLine($"{rx:F2} {(rTop + rBottom) / 2:F2} m {(rx + w):F2} {(rTop + rBottom) / 2:F2} l S");
                        break;
                    case Models.TextMarkupKind.Squiggly:
                    {
                        // Zigzag along the baseline: alternate ±amplitude every half wavelength.
                        var points = new List<(double X, double Y)>();
                        double yBase = rBottom + lineOffset;
                        double phase = 0;
                        for (double px = rx; px <= rx + w + 0.01; px += squiggleWavelength / 2)
                        {
                            double clampedX = Math.Min(px, rx + w);
                            points.Add((clampedX, yBase + (phase % 2 == 0 ? squiggleAmplitude : -squiggleAmplitude)));
                            phase++;
                        }
                        if (points.Count >= 2)
                        {
                            appearanceStream.AppendLine($"{points[0].X:F2} {points[0].Y:F2} m");
                            for (int i = 1; i < points.Count; i++)
                                appearanceStream.AppendLine($"{points[i].X:F2} {points[i].Y:F2} l");
                            appearanceStream.AppendLine("S");
                        }
                        break;
                    }
                    default: // Underline
                        appearanceStream.AppendLine($"{rx:F2} {rBottom + lineOffset:F2} m {(rx + w):F2} {rBottom + lineOffset:F2} l S");
                        break;
                }
            }
            appearanceStream.AppendLine("Q");

            var appearance = CreateAppearanceStream(
                document,
                Math.Max(1.0, maxX - minX),
                Math.Max(1.0, maxY - minY),
                appearanceStream.ToString(),
                CreateAppearanceResources(document, 1.0, false));
            dict.Elements["/AP"] = CreateAppearanceDictionary(document, appearance);

            AddAnnotationToPage(pdfPage, dict);
        }

        /// <summary>
        /// Task 26: writes one sticky note as a standard /Text annotation.
        /// /Rect carries the icon position; the text rides in /Contents
        /// (Unicode-encoded so CJK survives). No /AP — every standard viewer
        /// (Edge included) draws its own note icon for /Text annotations.
        /// </summary>
        private void WriteStickyNoteAnnotation(
            PdfSharpCore.Pdf.PdfDocument document,
            PdfSharpCore.Pdf.PdfPage pdfPage,
            Models.StickyNoteAnnotation note,
            double scale,
            double pageHeight,
            ISet<string> usedNoteIds = null)
        {
            if (note == null) return;

            // Persist marker geometry in PDF points (the /Rect remains the
            // interoperable fallback for viewers that ignore WNA metadata).
            const double legacyIconSizePt = 22.0;
            double iconWidthPt = note.Width > 0 ? note.Width * scale : legacyIconSizePt;
            double iconHeightPt = note.Height > 0 ? note.Height * scale : legacyIconSizePt;
            double x = note.X * scale;
            double yTop = pageHeight - note.Y * scale;
            double yBottom = yTop - iconHeightPt;

            var dict = new PdfDictionary(document);
            dict.Elements.SetName(PdfAnnotation.Keys.Subtype, "/Text");
            // Keep the model identity in /NM so a PDF round trip does not
            // silently orphan the editor marker or its undo/redo references.
            string noteId = EnsureUniqueStickyNoteId(note, usedNoteIds);
            dict.Elements.SetString("/NM", $"{StickyNoteNmPrefix}{noteId}");
            dict.Elements.SetInteger("/F", 4);
            dict.Elements.SetRectangle(PdfAnnotation.Keys.Rect,
                new PdfSharpPdfRectangle(new XRect(x, yBottom, iconWidthPt, iconHeightPt)));
            dict.Elements.SetReal("/WNAWidth", iconWidthPt);
            dict.Elements.SetReal("/WNAHeight", iconHeightPt);

            if (!string.IsNullOrEmpty(note.Text))
                dict.Elements.SetString(PdfAnnotation.Keys.Contents, note.Text, PdfStringEncoding.Unicode);

            // A recognizable comment icon in viewers that honour /Name+color.
            dict.Elements.SetName("/Name", "/Comment");
            var colorArray = new PdfArray();
            colorArray.Elements.Add(new PdfReal(note.R / 255.0));
            colorArray.Elements.Add(new PdfReal(note.G / 255.0));
            colorArray.Elements.Add(new PdfReal(note.B / 255.0));
            dict.Elements.Add("/C", colorArray);

            AddAnnotationToPage(pdfPage, dict);
        }

        private static PdfDictionary CreateAppearanceDictionary(PdfSharpCore.Pdf.PdfDocument document, PdfDictionary normalAppearance)
        {
            var appearanceDictionary = new PdfDictionary(document);
            appearanceDictionary.Elements["/N"] = normalAppearance.Reference;
            return appearanceDictionary;
        }

        private static string BuildRichTextStyleString(Models.TextAnnotation text)
        {
            if (text == null)
                return "font-family:Segoe UI;font-size:12pt;text-align:left";

            string family = string.IsNullOrWhiteSpace(text.FontFamily) ? "Segoe UI" : text.FontFamily;
            string weight = text.Bold ? "bold" : "normal";
            string style = text.Italic ? "italic" : "normal";
            string alignment = string.IsNullOrWhiteSpace(text.Alignment) ? "left" : text.Alignment.ToLowerInvariant();
            return $"font-family:{family};font-size:{text.FontSize.ToString("F2", CultureInfo.InvariantCulture)}pt;font-weight:{weight};font-style:{style};text-align:{alignment}";
        }

        private static double GetAlignedTextOffset(string line, double fontSize, double boxWidth, string alignment)
        {
            double estimatedWidth = Math.Max(0, (line?.Length ?? 0) * fontSize * 0.55);
            return GetAlignedTextOffset(estimatedWidth, boxWidth, alignment);
        }

        private static double GetAlignedTextOffset(double textWidth, double boxWidth, string alignment)
        {
            double estimatedWidth = Math.Max(0, textWidth);
            if (string.Equals(alignment, "Center", StringComparison.OrdinalIgnoreCase))
                return Math.Max(4, (boxWidth - estimatedWidth) / 2);
            if (string.Equals(alignment, "Right", StringComparison.OrdinalIgnoreCase))
                return Math.Max(4, boxWidth - estimatedWidth - 4);
            return 4;
        }

        private static string[] WrapTextLines(string text, double maxWidth, double estimatedCharacterWidth)
        {
            if (string.IsNullOrEmpty(text))
                return new[] { string.Empty };

            var lines = new List<string>();
            foreach (var logicalLine in text.Replace("\r", string.Empty).Split('\n'))
            {
                if (logicalLine.Length == 0)
                {
                    lines.Add(string.Empty);
                    continue;
                }

                var current = new StringBuilder();
                foreach (char character in logicalLine)
                {
                    string candidate = current.ToString() + character;
                    if (current.Length > 0
                        && candidate.Length * estimatedCharacterWidth > maxWidth)
                    {
                        lines.Add(current.ToString().TrimEnd());
                        current.Clear();
                        if (character == ' ')
                            continue;
                    }

                    current.Append(character);
                }

                if (current.Length > 0)
                    lines.Add(current.ToString().TrimEnd());
            }

            return lines.Count == 0 ? new[] { string.Empty } : lines.ToArray();
        }

        private static string[] WrapMeasuredTextLines(
            string text,
            double maxWidth,
            XFont font,
            XGraphics measureGfx)
        {
            if (string.IsNullOrEmpty(text))
                return new[] { string.Empty };

            var lines = new List<string>();
            foreach (var logicalLine in text.Replace("\r", string.Empty).Split('\n'))
            {
                if (logicalLine.Length == 0)
                {
                    lines.Add(string.Empty);
                    continue;
                }

                var current = new StringBuilder();
                foreach (char character in logicalLine)
                {
                    string candidate = current.ToString() + character;
                    if (current.Length > 0
                        && measureGfx.MeasureString(candidate, font).Width > maxWidth)
                    {
                        lines.Add(current.ToString().TrimEnd());
                        current.Clear();
                        if (char.IsWhiteSpace(character))
                            continue;
                    }

                    current.Append(character);
                }

                if (current.Length > 0)
                    lines.Add(current.ToString().TrimEnd());
            }

            return lines.Count == 0 ? new[] { string.Empty } : lines.ToArray();
        }

        private static PdfDictionary CreateStandardFontResources(
            PdfSharpCore.Pdf.PdfDocument document,
            bool bold = false,
            bool italic = false)
        {
            var font = new PdfDictionary(document);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type1");
            string baseFont = bold && italic
                ? "/Helvetica-BoldOblique"
                : bold
                    ? "/Helvetica-Bold"
                    : italic
                        ? "/Helvetica-Oblique"
                        : "/Helvetica";
            font.Elements.SetName("/BaseFont", baseFont);
            font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
            document.Internals.AddObject(font);

            var fonts = new PdfDictionary(document);
            fonts.Elements["/Helv"] = font.Reference;

            var resources = new PdfDictionary(document);
            resources.Elements["/Font"] = fonts;
            return resources;
        }

        // ----- CJK (non-ASCII) FreeText appearance -----

        // PdfSharpCore's default font resolver (PdfSharpCore.Utils.FontResolver, lazily installed by
        // GlobalFontSettings on first XFont use) only indexes *.ttf files from the Windows font
        // directories. TTC-packaged CJK fonts ("msyh.ttc" Microsoft YaHei, "simsun.ttc" SimSun) are
        // invisible to it, so we probe TTF candidates by file name in priority order.
        private static readonly (string FamilyName, string FileName)[] CjkFontCandidates =
        {
            ("SimHei", "simhei.ttf"),
            ("DengXian", "deng.ttf"),
            ("KaiTi", "simkai.ttf"),
            ("FangSong", "simfang.ttf"),
        };

        private static readonly Lazy<string> CjkFontFamilyLazy = new Lazy<string>(ResolveCjkFontFamilyName);
        private static readonly Lazy<string> CjkFontPathLazy = new Lazy<string>(ResolveCjkFontPath);
        private static int _pdfFontResolverConfigured;

        private sealed class OpenNotesPdfFontResolver : IFontResolver
        {
            private const string FaceName = "OpenNotes-CJK-Regular";
            private readonly string _familyName;
            private readonly string _fontPath;

            public OpenNotesPdfFontResolver(string familyName, string fontPath)
            {
                _familyName = familyName;
                _fontPath = fontPath;
            }

            public string DefaultFontName => _familyName;

            public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            {
                return string.Equals(familyName, _familyName, StringComparison.OrdinalIgnoreCase)
                    ? new FontResolverInfo(FaceName, isBold, isItalic)
                    : null;
            }

            public byte[] GetFont(string faceName)
            {
                return string.Equals(faceName, FaceName, StringComparison.Ordinal)
                    ? File.ReadAllBytes(_fontPath)
                    : null;
            }
        }

        // XForm.PdfForm is internal in PdfSharpCore 1.3.67; reflect it once to reach the form's
        // PdfFormXObject (whose getter also registers the XObject as an indirect object).
        private static readonly PropertyInfo XFormPdfFormProperty = typeof(XForm).GetProperty(
            "PdfForm", BindingFlags.Instance | BindingFlags.NonPublic);

        private static string ResolveCjkFontFamilyName()
        {
            string[] fontDirectories =
            {
                Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts"),
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Fonts"),
            };

            foreach (var candidate in CjkFontCandidates)
            {
                foreach (string directory in fontDirectories)
                {
                    try
                    {
                        if (File.Exists(Path.Combine(directory, candidate.FileName)))
                            return candidate.FamilyName;
                    }
                    catch
                    {
                        // Probing must never throw.
                    }
                }
            }

            return null;
        }

        private static string ResolveCjkFontPath()
        {
            string[] fontDirectories =
            {
                Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts"),
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Fonts"),
            };

            foreach (var candidate in CjkFontCandidates)
            {
                foreach (string directory in fontDirectories)
                {
                    try
                    {
                        string path = Path.Combine(directory, candidate.FileName);
                        if (File.Exists(path))
                            return path;
                    }
                    catch
                    {
                        // Probing must never throw.
                    }
                }
            }

            return null;
        }

        private static void ConfigurePdfFontResolver()
        {
            if (Interlocked.Exchange(ref _pdfFontResolverConfigured, 1) != 0)
                return;

            string familyName = CjkFontFamilyLazy.Value;
            string fontPath = CjkFontPathLazy.Value;
            if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(fontPath))
                return;

            try
            {
                // Configure before the first XFont is created. PdfSharpCore's default resolver
                // enumerates the per-user font directory, which may be inaccessible in a packaged
                // desktop process. The resolver below only reads the known system font we need.
                GlobalFontSettings.FontResolver = new OpenNotesPdfFontResolver(familyName, fontPath);
            }
            catch
            {
                // A host may have configured its own resolver already. Keep that configuration.
            }
        }

        private static bool ContainsNonAscii(string text)
        {
            foreach (char c in text)
            {
                if (c > 127)
                    return true;
            }
            return false;
        }

        private static bool TryCreateCjkFreeTextAnnotation(
            PdfSharpCore.Pdf.PdfDocument document,
            Models.TextAnnotation textItem,
            string[] textLines,
            double pdfFontSize,
            double lineHeight,
            double scale,
            double pageHeight,
            out PdfDictionary annotation)
        {
            annotation = null;

            ConfigurePdfFontResolver();
            string fontFamily = CjkFontFamilyLazy.Value;
            if (fontFamily == null)
                return false;

            XFont font;
            try
            {
                // Unicode encoding makes PdfSharpCore create a PdfType0Font (Identity-H CID)
                // with an automatically embedded subset (FontFile2) — renderable everywhere.
                var fontStyle = textItem.Bold && textItem.Italic
                    ? XFontStyle.BoldItalic
                    : textItem.Bold
                        ? XFontStyle.Bold
                        : textItem.Italic
                            ? XFontStyle.Italic
                            : XFontStyle.Regular;
                font = new XFont(fontFamily, pdfFontSize, fontStyle, XPdfFontOptions.UnicodeDefault);
            }
            catch
            {
                return false; // fall back to the legacy /Helv path
            }

            var explicitLines = (textItem.Text ?? string.Empty).Split('\n');
            double maxLineWidth;
            try
            {
                using (var measureGfx = XGraphics.CreateMeasureContext(
                           new XSize(1000, 1000), XGraphicsUnit.Point, XPageDirection.Downwards))
                {
                    maxLineWidth = 0;
                    foreach (string line in textLines)
                        maxLineWidth = Math.Max(maxLineWidth, measureGfx.MeasureString(line, font).Width);
                }
            }
            catch
            {
                return false;
            }

            double w = textItem.Width > 0
                ? textItem.Width * scale
                : Math.Max(150 * scale, maxLineWidth + 12);
            string[] wrappedLines = explicitLines;
            if (textItem.Width > 0)
            {
                try
                {
                    using var wrapMeasureGfx = XGraphics.CreateMeasureContext(
                        new XSize(1000, 1000), XGraphicsUnit.Point, XPageDirection.Downwards);
                    wrappedLines = WrapMeasuredTextLines(
                        textItem.Text,
                        Math.Max(1.0, w - 8.0),
                        font,
                        wrapMeasureGfx);
                }
                catch
                {
                    return false;
                }
            }
            double h = textItem.Height > 0
                ? textItem.Height * scale
                : Math.Max(1.0, wrappedLines.Length * lineHeight + pdfFontSize * 0.4);

            double x = textItem.X * scale;
            double y = pageHeight - (textItem.Y * scale) - h;

            var annot = new PdfDictionary(document);
            annot.Elements.SetName(PdfAnnotation.Keys.Subtype, "/FreeText");
            annot.Elements.SetRectangle(PdfAnnotation.Keys.Rect, new PdfSharpPdfRectangle(new XRect(x, y, w, h)));
            // Plain SetString serializes with PdfSharpCore's RawEncoding, which truncates every
            // character to its low 8 bits and corrupts CJK text. The Unicode encoding writes a
            // standard UTF-16BE hex string (<FEFF...>) instead.
            annot.Elements.SetString(PdfAnnotation.Keys.Contents, textItem.Text, PdfStringEncoding.Unicode);
            annot.Elements.SetString("/NM", $"{TextNmPrefix}{Guid.NewGuid()}");
            annot.Elements.SetInteger("/F", 4); // Printable
            SetTextAnnotationLayoutMetadata(annot, textItem);

            // Remove border
            var bsForText = new PdfDictionary();
            bsForText.Elements.SetInteger("/W", 0);
            annot.Elements["/BS"] = bsForText;

            XForm form;
            try
            {
                form = new XForm(document, new XSize(w, h));
                var gfx = XGraphics.FromForm(form);
                var brush = new XSolidBrush(XColor.FromArgb(textItem.R, textItem.G, textItem.B));
                // Match the baselines of the legacy latin appearance stream: center the first line
                // inside its 1.4em slot, then advance line by lineHeight.
                double ascent = font.GetHeight() * font.CellAscent / font.CellSpace;
                double firstLineTop = lineHeight - (lineHeight - pdfFontSize) / 2 - ascent;
                for (int li = 0; li < wrappedLines.Length; li++)
                {
                    if (wrappedLines[li].Length > 0)
                    {
                        double lineWidth = gfx.MeasureString(wrappedLines[li], font).Width;
                        double lineX = GetAlignedTextOffset(lineWidth, w, textItem.Alignment);
                        gfx.DrawString(wrappedLines[li], font, brush, lineX, firstLineTop + li * lineHeight);
                    }
                }

                form.DrawingFinished();
            }
            catch
            {
                return false;
            }

            var pdfForm = XFormPdfFormProperty?.GetValue(form) as PdfDictionary;
            if (pdfForm == null)
                return false;

            annot.Elements["/AP"] = CreateAppearanceDictionary(document, pdfForm);

            double r2 = textItem.R / 255.0, g2 = textItem.G / 255.0, b2 = textItem.B / 255.0;
            annot.Elements.SetString("/DA", $"{GetFormFontResourceName(pdfForm)} {pdfFontSize:F2} Tf {r2:F3} {g2:F3} {b2:F3} rg");
            annot.Elements.SetString("/DS", BuildRichTextStyleString(textItem));

            annotation = annot;
            return true;
        }

        private static string GetFormFontResourceName(PdfDictionary form)
        {
            var resources = form.Elements.GetDictionary("/Resources");
            var fonts = resources?.Elements.GetDictionary("/Font");
            if (fonts != null)
            {
                foreach (string key in fonts.Elements.Keys)
                {
                    if (key.StartsWith("/", StringComparison.Ordinal))
                        return key;
                }
            }

            return "/F1";
        }

        private static PdfDictionary CreateAppearanceResources(PdfSharpCore.Pdf.PdfDocument document, double opacity, bool useMultiplyBlend)
        {
            if (opacity >= 0.999 && !useMultiplyBlend)
                return null;

            var graphicsState = new PdfDictionary(document);
            graphicsState.Elements.SetName("/Type", "/ExtGState");
            graphicsState.Elements.SetReal("/CA", opacity);
            graphicsState.Elements.SetReal("/ca", opacity);
            if (useMultiplyBlend)
                graphicsState.Elements.SetName("/BM", "/Multiply");
            document.Internals.AddObject(graphicsState);

            var extGState = new PdfDictionary(document);
            extGState.Elements["/GS1"] = graphicsState.Reference;

            var resources = new PdfDictionary(document);
            resources.Elements["/ExtGState"] = extGState;
            return resources;
        }

        private static PdfDictionary CreateAppearanceStream(
            PdfSharpCore.Pdf.PdfDocument document,
            double width,
            double height,
            string contentStream,
            PdfDictionary resources)
        {
            var appearanceStream = new PdfDictionary(document);
            appearanceStream.Elements.SetName("/Type", "/XObject");
            appearanceStream.Elements.SetName("/Subtype", "/Form");
            appearanceStream.Elements.SetInteger("/FormType", 1);

            var bbox = new PdfArray(document);
            bbox.Elements.Add(new PdfReal(0));
            bbox.Elements.Add(new PdfReal(0));
            bbox.Elements.Add(new PdfReal(width));
            bbox.Elements.Add(new PdfReal(height));
            appearanceStream.Elements["/BBox"] = bbox;

            if (resources != null)
                appearanceStream.Elements["/Resources"] = resources;

            appearanceStream.CreateStream(Encoding.Latin1.GetBytes(contentStream));
            document.Internals.AddObject(appearanceStream);
            return appearanceStream;
        }

        internal static Models.TextAnnotation TryExtractFreeTextAnnotation(PdfDictionary dict, double pageHeight, double scale)
        {
            if (dict == null)
                return null;

            var rect = dict.Elements.GetRectangle("/Rect");
            string text = ExtractAnnotationText(dict);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var annotation = new Models.TextAnnotation
            {
                Text = text,
                X = rect.X1 * scale,
                Y = (pageHeight - rect.Y1 - rect.Height) * scale,
                Width = IsAutomaticTextDimension(dict, "/WNAutoWidth") ? 0 : rect.Width * scale,
                Height = IsAutomaticTextDimension(dict, "/WNAutoHeight") ? 0 : rect.Height * scale,
                FontSize = 18,
                R = 0,
                G = 0,
                B = 0
            };

            if (TryExtractFontSizeFromDefaultAppearance(dict.Elements.GetString("/DA"), scale, out var fontSizeFromDa))
                annotation.FontSize = fontSizeFromDa;
            else if (TryExtractFontSizeFromStyleString(dict.Elements.GetString("/DS"), scale, out var fontSizeFromStyle))
                annotation.FontSize = fontSizeFromStyle;
            else if (TryExtractFontSizeFromStyleString(dict.Elements.GetString("/RC"), scale, out var fontSizeFromRichText))
                annotation.FontSize = fontSizeFromRichText;

            if (TryExtractColorFromDefaultAppearance(dict.Elements.GetString("/DA"), out var r, out var g, out var b) ||
                TryExtractColorFromStyleString(dict.Elements.GetString("/DS"), out r, out g, out b) ||
                TryExtractColorFromStyleString(dict.Elements.GetString("/RC"), out r, out g, out b) ||
                TryExtractColorFromArray(dict.Elements.GetArray("/C"), out r, out g, out b))
            {
                annotation.R = r;
                annotation.G = g;
                annotation.B = b;
            }

            string richStyle = dict.Elements.GetString("/DS");
            if (string.IsNullOrWhiteSpace(richStyle))
                richStyle = dict.Elements.GetString("/RC");
            if (!string.IsNullOrWhiteSpace(richStyle))
            {
                annotation.Bold = richStyle.IndexOf("font-weight:bold", StringComparison.OrdinalIgnoreCase) >= 0
                    || richStyle.IndexOf("font-weight:700", StringComparison.OrdinalIgnoreCase) >= 0;
                annotation.Italic = richStyle.IndexOf("font-style:italic", StringComparison.OrdinalIgnoreCase) >= 0;

                var familyMatch = CssFontFamilyRegex.Match(richStyle);
                if (familyMatch.Success)
                    annotation.FontFamily = familyMatch.Groups["family"].Value.Trim().Trim('"', '\'');

                var alignmentMatch = CssTextAlignRegex.Match(richStyle);
                if (alignmentMatch.Success)
                    annotation.Alignment = char.ToUpperInvariant(alignmentMatch.Groups["alignment"].Value[0])
                        + alignmentMatch.Groups["alignment"].Value.Substring(1).ToLowerInvariant();
            }

            return annotation;
        }

        private static bool IsAutomaticTextDimension(PdfDictionary dict, string key)
        {
            // Older OpenNotes PDFs did not persist the distinction between an
            // automatic text box and a deliberate rectangle. Treating a
            // missing marker as automatic preserves their zero-dimension model
            // after the first load/save cycle.
            return dict?.Elements == null
                || !dict.Elements.ContainsKey(key)
                || dict.Elements.GetInteger(key) != 0;
        }

        private static void SetTextAnnotationLayoutMetadata(
            PdfDictionary annotation,
            Models.TextAnnotation textItem)
        {
            annotation.Elements.SetInteger("/WNAutoWidth", textItem?.Width > 0 ? 0 : 1);
            annotation.Elements.SetInteger("/WNAutoHeight", textItem?.Height > 0 ? 0 : 1);
        }

        // ----- Task 19: image annotations (/Stamp) -----

        private const string OwnImageStampNmPrefix = "wna_img_";

        private static bool IsOwnImageStamp(PdfDictionary dict)
        {
            var nm = dict?.Elements.GetString("/NM");
            return nm != null && nm.StartsWith(OwnImageStampNmPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Rebuilds an <see cref="Models.ImageAnnotation"/> from one of our own
        /// /Stamp annotations. The original encoded bytes ride in /Contents as
        /// base64 (the /AP XForm is only the visual for external viewers).
        /// Returns null for anything that is not ours.
        /// </summary>
        internal static Models.ImageAnnotation TryExtractImageAnnotation(PdfDictionary dict, double pageHeight, double scale)
        {
            if (dict == null)
                return null;

            // Strict ownership check: a foreign /Stamp could carry arbitrary
            // /Contents text; only the /NM prefix identifies ours.
            if (!IsOwnImageStamp(dict))
                return null;

            var contents = dict.Elements.GetString("/Contents");
            if (string.IsNullOrWhiteSpace(contents))
                return null;

            byte[] bytes;
            try { bytes = Convert.FromBase64String(contents); }
            catch { return null; }
            if (bytes.Length == 0)
                return null;

            var rect = dict.Elements.GetRectangle("/Rect");
            return new Models.ImageAnnotation
            {
                ImageDataBase64 = contents,
                Format = DetectImageFormat(bytes),
                X = rect.X1 * scale,
                Y = (pageHeight - rect.Y1 - rect.Height) * scale,
                Width = rect.Width * scale,
                Height = rect.Height * scale
            };
        }

        /// <summary>Sniffs the encoded image format from the magic bytes.</summary>
        internal static string DetectImageFormat(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4)
                return "png";

            // PNG: 89 50 4E 47; JPEG: FF D8 FF.
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "png";
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "jpeg";

            return "png";
        }

        /// <summary>
        /// Recovers rectangles saved by OpenNotes versions that predate the
        /// private FitToCurve flag. Their five vertices are still intact in
        /// /InkList; only the WPF rendering hint was lost.
        /// </summary>
        private static bool LooksLikeLegacyCrispRectangle(IReadOnlyList<double[]> points)
        {
            if (points.Count != 5 || points.Any(point => point.Length < 2))
                return false;

            static double DistanceSquared(double[] left, double[] right)
            {
                double dx = left[0] - right[0];
                double dy = left[1] - right[1];
                return (dx * dx) + (dy * dy);
            }

            if (DistanceSquared(points[0], points[4]) > 0.25)
                return false;

            for (int corner = 0; corner < 4; corner++)
            {
                var current = points[corner];
                var next = points[(corner + 1) % 4];
                var afterNext = points[(corner + 2) % 4];
                double firstX = next[0] - current[0];
                double firstY = next[1] - current[1];
                double secondX = afterNext[0] - next[0];
                double secondY = afterNext[1] - next[1];
                double firstLengthSquared = (firstX * firstX) + (firstY * firstY);
                double secondLengthSquared = (secondX * secondX) + (secondY * secondY);
                if (firstLengthSquared < 4 || secondLengthSquared < 4)
                    return false;

                double dot = (firstX * secondX) + (firstY * secondY);
                double perpendicularTolerance = Math.Sqrt(firstLengthSquared * secondLengthSquared) * 0.02;
                if (Math.Abs(dot) > perpendicularTolerance)
                    return false;
            }

            return true;
        }

        // ----- Task 25/26/27: text markup / area highlight / sticky note -----

        private const string TextNmPrefix = "wna_text_";
        private const string InkNmPrefix = "wna_ink_";
        private const string HighlightNmPrefix = "wna_hl_";
        private const string HiddenInkNmPrefix = "wna_hidden_";
        private const string AreaHighlightNmPrefix = "wna_areahl_";
        private const string StickyNoteNmPrefix = "wna_note_";
        private const string TextMarkupNmPrefix = "wna_markup_";

        private static bool HasNmPrefix(PdfDictionary dict, string prefix)
        {
            var nm = dict?.Elements.GetString("/NM");
            return nm != null && nm.StartsWith(prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Rebuilds one study mask from an owned /Ink annotation. PDF stores
        /// one path per hidden mask; foreign /Ink annotations are rejected by
        /// the wna_hidden_ ownership prefix.
        /// </summary>
        internal static Models.HiddenInkAnnotation TryExtractHiddenInkAnnotation(
            PdfDictionary dict,
            double pageHeight,
            double scale)
        {
            if (dict == null || !HasNmPrefix(dict, HiddenInkNmPrefix))
                return null;

            var inkList = dict.Elements.GetArray("/InkList");
            if (inkList == null || inkList.Elements.Count == 0)
                return null;

            var pointArray = (inkList.Elements[0] as PdfReference)?.Value as PdfArray
                ?? inkList.Elements[0] as PdfArray;
            if (pointArray == null || pointArray.Elements.Count < 2)
                return null;

            var nm = dict.Elements.GetString("/NM") ?? string.Empty;
            var id = nm.StartsWith(HiddenInkNmPrefix, StringComparison.Ordinal)
                ? nm.Substring(HiddenInkNmPrefix.Length)
                : Guid.NewGuid().ToString("N");
            var annotation = new Models.HiddenInkAnnotation
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                A = 255,
                RevealDurationMs = dict.Elements.ContainsKey("/WNARevealMs")
                    ? dict.Elements.GetInteger("/WNARevealMs")
                    : Models.HiddenInkRevealState.DefaultRevealDurationMs
            };
            if (annotation.RevealDurationMs <= 0)
                annotation.RevealDurationMs = Models.HiddenInkRevealState.DefaultRevealDurationMs;

            var cArray = dict.Elements.GetArray("/C");
            if (cArray != null && cArray.Elements.Count >= 3)
            {
                annotation.R = (byte)(Math.Clamp(GetDouble(cArray.Elements[0]), 0.0, 1.0) * 255);
                annotation.G = (byte)(Math.Clamp(GetDouble(cArray.Elements[1]), 0.0, 1.0) * 255);
                annotation.B = (byte)(Math.Clamp(GetDouble(cArray.Elements[2]), 0.0, 1.0) * 255);
            }

            var bs = dict.Elements.GetDictionary("/BS");
            annotation.Size = (bs != null
                ? (bs.Elements.ContainsKey("/W") ? GetDouble(bs.Elements["/W"], 2.0) : 2.0)
                : 2.0) * scale;
            if (annotation.Size <= 0)
                annotation.Size = 2.0;

            for (int index = 0; index < pointArray.Elements.Count - 1; index += 2)
            {
                double pdfX = GetDouble(pointArray.Elements[index]);
                double pdfY = GetDouble(pointArray.Elements[index + 1]);
                annotation.Points.Add(new[] { pdfX * scale, (pageHeight - pdfY) * scale });
            }

            return annotation.Points.Count == 0 ? null : annotation;
        }

        /// <summary>
        /// Task 25: rebuilds a <see cref="Models.TextMarkupAnnotation"/> from an
        /// /Underline, /StrikeOut or /Squiggly annotation. QuadPoints quads are
        /// converted to page DIP rects; the bounding box becomes the model's
        /// X/Y origin and the rects are stored relative to it.
        /// </summary>
        internal static Models.TextMarkupAnnotation TryExtractTextMarkup(PdfDictionary dict, string subtype, double pageHeight, double scale)
        {
            if (dict == null)
                return null;

            var quadPoints = dict.Elements.GetArray("/QuadPoints");
            if (quadPoints == null || quadPoints.Elements.Count < 8)
                return null;

            string kind = subtype switch
            {
                "/StrikeOut" => nameof(Models.TextMarkupKind.StrikeOut),
                "/Squiggly" => nameof(Models.TextMarkupKind.Squiggly),
                _ => nameof(Models.TextMarkupKind.Underline),
            };

            var markup = new Models.TextMarkupAnnotation { Kind = kind };

            var cArray = dict.Elements.GetArray("/C");
            if (cArray != null && cArray.Elements.Count >= 3)
            {
                markup.R = (byte)(GetDouble(cArray.Elements[0]) * 255);
                markup.G = (byte)(GetDouble(cArray.Elements[1]) * 255);
                markup.B = (byte)(GetDouble(cArray.Elements[2]) * 255);
            }

            double minX = double.MaxValue, minY = double.MaxValue;

            for (int pIdx = 0; pIdx + 7 < quadPoints.Elements.Count; pIdx += 8)
            {
                double qx1 = GetDouble(quadPoints.Elements[pIdx]);
                double qy1 = GetDouble(quadPoints.Elements[pIdx + 1]);
                double qx2 = GetDouble(quadPoints.Elements[pIdx + 2]);
                double qy2 = GetDouble(quadPoints.Elements[pIdx + 3]);
                double qx3 = GetDouble(quadPoints.Elements[pIdx + 4]);
                double qy3 = GetDouble(quadPoints.Elements[pIdx + 5]);
                double qx4 = GetDouble(quadPoints.Elements[pIdx + 6]);
                double qy4 = GetDouble(quadPoints.Elements[pIdx + 7]);

                double qxMin = Math.Min(Math.Min(qx1, qx2), Math.Min(qx3, qx4));
                double qxMax = Math.Max(Math.Max(qx1, qx2), Math.Max(qx3, qx4));
                double qyMin = Math.Min(Math.Min(qy1, qy2), Math.Min(qy3, qy4));
                double qyMax = Math.Max(Math.Max(qy1, qy2), Math.Max(qy3, qy4));

                double x_ui = qxMin * scale;
                double w_ui = (qxMax - qxMin) * scale;
                double h_ui = (qyMax - qyMin) * scale;
                double y_ui = (pageHeight - qyMax) * scale;

                minX = Math.Min(minX, x_ui);
                minY = Math.Min(minY, y_ui);
                markup.Rects.Add(new[] { x_ui, y_ui, w_ui, h_ui });
            }

            if (markup.Rects.Count == 0)
                return null;

            // Re-base the rects relative to the bounding-box origin.
            for (int i = 0; i < markup.Rects.Count; i++)
            {
                markup.Rects[i] = new[]
                {
                    markup.Rects[i][0] - minX,
                    markup.Rects[i][1] - minY,
                    markup.Rects[i][2],
                    markup.Rects[i][3]
                };
            }

            markup.X = minX;
            markup.Y = minY;
            return markup;
        }

        /// <summary>
        /// Task 27: rebuilds our own rectangular area highlight (identified by
        /// the wna_areahl_ /NM prefix) from the first QuadPoints quad.
        /// </summary>
        internal static Models.AreaHighlightAnnotation TryExtractAreaHighlight(PdfDictionary dict, double pageHeight, double scale)
        {
            if (dict == null || !HasNmPrefix(dict, AreaHighlightNmPrefix))
                return null;

            double x, y, w, h;
            var quadPoints = dict.Elements.GetArray("/QuadPoints");
            if (quadPoints != null && quadPoints.Elements.Count >= 8)
            {
                double qx1 = GetDouble(quadPoints.Elements[0]);
                double qy1 = GetDouble(quadPoints.Elements[1]);
                double qx3 = GetDouble(quadPoints.Elements[4]);
                double qy3 = GetDouble(quadPoints.Elements[5]);
                double qxMin = Math.Min(qx1, qx3);
                double qxMax = Math.Max(qx1, qx3);
                double qyMin = Math.Min(qy1, qy3);
                double qyMax = Math.Max(qy1, qy3);
                x = qxMin * scale;
                w = (qxMax - qxMin) * scale;
                h = (qyMax - qyMin) * scale;
                y = (pageHeight - qyMax) * scale;
            }
            else
            {
                var rect = dict.Elements.GetRectangle("/Rect");
                x = rect.X1 * scale;
                y = (pageHeight - rect.Y1 - rect.Height) * scale;
                w = rect.Width * scale;
                h = rect.Height * scale;
            }

            if (w <= 0 || h <= 0)
                return null;

            var area = new Models.AreaHighlightAnnotation { X = x, Y = y, Width = w, Height = h };

            var cArray = dict.Elements.GetArray("/C");
            if (cArray != null && cArray.Elements.Count >= 3)
            {
                area.R = (byte)(GetDouble(cArray.Elements[0]) * 255);
                area.G = (byte)(GetDouble(cArray.Elements[1]) * 255);
                area.B = (byte)(GetDouble(cArray.Elements[2]) * 255);
            }
            double ca = dict.Elements.ContainsKey("/CA") ? GetDouble(dict.Elements["/CA"], 1.0) : 1.0;
            area.A = (byte)(ca * 255);

            return area;
        }

        /// <summary>
        /// Task 26: rebuilds one of our own sticky notes (identified by the
        /// wna_note_ /NM prefix) — icon position from /Rect, text from
        /// /Contents. Returns null for foreign /Text annotations.
        /// </summary>
        internal static Models.StickyNoteAnnotation TryExtractStickyNote(PdfDictionary dict, double pageHeight, double scale)
        {
            if (dict == null || !HasNmPrefix(dict, StickyNoteNmPrefix))
                return null;

            var rect = dict.Elements.GetRectangle("/Rect");
            string nm = dict.Elements.GetString("/NM");
            string id = nm != null && nm.StartsWith(StickyNoteNmPrefix, StringComparison.Ordinal)
                ? nm.Substring(StickyNoteNmPrefix.Length)
                : null;
            double widthPt = GetDouble(dict.Elements.GetValue("/WNAWidth"), rect.Width);
            double heightPt = GetDouble(dict.Elements.GetValue("/WNAHeight"), rect.Height);
            var note = new Models.StickyNoteAnnotation
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                X = rect.X1 * scale,
                Y = (pageHeight - rect.Y1 - rect.Height) * scale,
                Text = ExtractAnnotationText(dict),
                Width = widthPt * scale,
                Height = heightPt * scale
            };

            var colorArray = dict.Elements.GetArray("/C");
            if (colorArray != null && colorArray.Elements.Count >= 3)
            {
                note.R = (byte)Math.Round(Math.Max(0, Math.Min(1, GetDouble(colorArray.Elements[0]))) * 255);
                note.G = (byte)Math.Round(Math.Max(0, Math.Min(1, GetDouble(colorArray.Elements[1]))) * 255);
                note.B = (byte)Math.Round(Math.Max(0, Math.Min(1, GetDouble(colorArray.Elements[2]))) * 255);
            }
            return note;
        }

        private static string EnsureUniqueStickyNoteId(
            Models.StickyNoteAnnotation note,
            ISet<string> usedIds)
        {
            if (note == null)
                return string.Empty;

            string candidate = note.Id?.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
                candidate = Guid.NewGuid().ToString("N");

            if (usedIds != null)
            {
                while (!usedIds.Add(candidate))
                    candidate = Guid.NewGuid().ToString("N");
            }

            note.Id = candidate;
            return candidate;
        }

        private static string ExtractAnnotationText(PdfDictionary dict)
        {
            string contents = NormalizeAnnotationText(GetDecodedPdfString(dict, "/Contents"));
            if (!string.IsNullOrWhiteSpace(contents))
                return contents;

            string richText = NormalizeAnnotationText(ConvertRichTextToPlainText(GetDecodedPdfString(dict, "/RC")));
            if (!string.IsNullOrWhiteSpace(richText))
                return richText;

            return NormalizeAnnotationText(GetDecodedPdfString(dict, "/V"));
        }

        private static string GetDecodedPdfString(PdfDictionary dict, string key)
        {
            if (dict == null || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            try
            {
                if (dict.Elements.GetValue(key) is PdfString pdfString)
                    return pdfString.Value ?? string.Empty;
            }
            catch
            {
                // Older or malformed PDFs can expose a value that cannot be
                // resolved through GetObject. Keep the legacy fallback below.
            }

            return dict.Elements.GetString(key) ?? string.Empty;
        }

        private static string ConvertRichTextToPlainText(string richText)
        {
            if (string.IsNullOrWhiteSpace(richText))
                return string.Empty;

            string normalized = RichTextBreakRegex.Replace(richText, "\n");
            normalized = RichTextTagRegex.Replace(normalized, string.Empty);
            return WebUtility.HtmlDecode(normalized).Trim();
        }

        private static string NormalizeAnnotationText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim('\0');
        }

        private static bool TryExtractFontSizeFromDefaultAppearance(string defaultAppearance, double scale, out double fontSize)
        {
            fontSize = 0;
            if (string.IsNullOrWhiteSpace(defaultAppearance))
                return false;

            var match = DefaultAppearanceFontSizeRegex.Match(defaultAppearance);
            if (!match.Success)
                return false;

            if (!TryParseInvariantDouble(match.Groups["size"].Value, out var parsed))
                return false;

            fontSize = parsed * scale;
            return fontSize > 0;
        }

        private static bool TryExtractFontSizeFromStyleString(string styleText, double scale, out double fontSize)
        {
            fontSize = 0;
            if (string.IsNullOrWhiteSpace(styleText))
                return false;

            var match = CssFontSizeRegex.Match(styleText);
            if (!match.Success)
                match = CssFontRegex.Match(styleText);

            if (!match.Success || !TryParseInvariantDouble(match.Groups["size"].Value, out var parsed))
                return false;

            string unit = match.Groups["unit"].Value;
            fontSize = string.Equals(unit, "px", StringComparison.OrdinalIgnoreCase)
                ? parsed
                : parsed * scale;
            return fontSize > 0;
        }

        private static bool TryExtractColorFromDefaultAppearance(string defaultAppearance, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(defaultAppearance))
                return false;

            var rgbMatch = DefaultAppearanceRgbRegex.Match(defaultAppearance);
            if (rgbMatch.Success &&
                TryParseInvariantDouble(rgbMatch.Groups["r"].Value, out var red) &&
                TryParseInvariantDouble(rgbMatch.Groups["g"].Value, out var green) &&
                TryParseInvariantDouble(rgbMatch.Groups["b"].Value, out var blue))
            {
                r = ToByte(red * 255.0);
                g = ToByte(green * 255.0);
                b = ToByte(blue * 255.0);
                return true;
            }

            var grayMatch = DefaultAppearanceGrayRegex.Match(defaultAppearance);
            if (!grayMatch.Success || !TryParseInvariantDouble(grayMatch.Groups["gray"].Value, out var gray))
                return false;

            byte value = ToByte(gray * 255.0);
            r = value;
            g = value;
            b = value;
            return true;
        }

        private static bool TryExtractColorFromStyleString(string styleText, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(styleText))
                return false;

            var rgbMatch = CssRgbColorRegex.Match(styleText);
            if (rgbMatch.Success &&
                byte.TryParse(rgbMatch.Groups["r"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) &&
                byte.TryParse(rgbMatch.Groups["g"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out g) &&
                byte.TryParse(rgbMatch.Groups["b"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
            {
                return true;
            }

            var hexMatch = CssHexColorRegex.Match(styleText);
            if (!hexMatch.Success)
                return false;

            return TryParseHexColor(hexMatch.Groups["value"].Value, out r, out g, out b);
        }

        private static bool TryExtractColorFromArray(PdfArray colorArray, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (colorArray == null || colorArray.Elements.Count < 3)
                return false;

            r = ToByte(GetDouble(colorArray.Elements[0]) * 255.0);
            g = ToByte(GetDouble(colorArray.Elements[1]) * 255.0);
            b = ToByte(GetDouble(colorArray.Elements[2]) * 255.0);
            return true;
        }

        private static bool TryParseHexColor(string colorText, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(colorText) || colorText[0] != '#')
                return false;

            string hex = colorText.Substring(1);
            if (hex.Length == 3)
                hex = string.Concat(hex.Select(ch => new string(ch, 2)));

            if (hex.Length != 6)
                return false;

            return
                byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
                byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
                byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
        }

        private static bool TryParseInvariantDouble(string rawValue, out double value)
        {
            return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
        }

        private static double GetDouble(PdfItem item, double defaultValue = 0)
        {
            if (item is PdfReal r) return r.Value;
            if (item is PdfInteger i) return i.Value;
            if (item is PdfReference pref && pref.Value != null) return GetDouble(pref.Value, defaultValue);
            return defaultValue;
        }
    }
}

