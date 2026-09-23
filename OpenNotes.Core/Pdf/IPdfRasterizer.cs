using System;
using System.Collections.Generic;
using System.IO;

namespace Caelum.Pdf
{
    /// <summary>
    /// UI-agnostic view of a loaded, renderable PDF document. This is the only
    /// surface the Core <see cref="PdfService"/> needs from a raster backend;
    /// the WPF shell converts <see cref="PdfPageBitmap"/> into BitmapSource.
    /// <para>
    /// Member semantics deliberately mirror <c>PdfiumViewer.PdfDocument</c>:
    /// page sizes are single-precision points, text bounds are returned in
    /// unrotated PDF page coordinates (bottom-left origin), and
    /// <see cref="RectangleFromPdf"/> maps them into top-left device space at
    /// 72 DPI exactly like the old WPF code path did.
    /// </para>
    /// </summary>
    public interface IPdfRasterizer : IDisposable
    {
        /// <summary>Number of pages in the document (0 when closed).</summary>
        int PageCount { get; }

        /// <summary>Page sizes in PDF points, cached at load (mirrors <c>PdfDocument.PageSizes</c>).</summary>
        IReadOnlyList<PdfPageSize> PageSizes { get; }

        /// <summary>
        /// Renders one page into a fresh BGRA buffer (byte order B,G,R,A per
        /// pixel — identical to <c>PixelFormats.Bgra32</c> and to the
        /// <c>Format32bppArgb</c> memory layout the PdfiumViewer path produced).
        /// When <paramref name="renderAnnotations"/> is true the page is
        /// rendered with annotations through the form-fill pipeline,
        /// preserving the old <c>PdfRenderFlags.Annotations</c> behavior;
        /// false matches the old <c>(PdfRenderFlags)0</c> base render.
        /// </summary>
        PdfPageBitmap RenderPageBgra(int pageIndex, int pixelWidth, int pixelHeight, bool renderAnnotations = true);

        /// <summary>Extracts the full text of a page (mirrors <c>PdfDocument.GetPdfText(int)</c>).</summary>
        string GetPageText(int pageIndex);

        /// <summary>
        /// Returns merged character bounds in unrotated PDF page coordinates
        /// for <paramref name="length"/> characters starting at
        /// <paramref name="offset"/> (mirrors <c>PdfDocument.GetTextBounds</c>,
        /// including the horizontal-run merge heuristic).
        /// </summary>
        IReadOnlyList<PdfRectF> GetTextBounds(int pageIndex, int offset, int length);

        /// <summary>
        /// Maps a PDF-space rectangle into device space (top-left origin,
        /// natural page size in points — mirrors <c>PdfDocument.RectangleFromPdf</c>).
        /// </summary>
        PdfRectI RectangleFromPdf(int pageIndex, PdfRectF rect);
    }

    /// <summary>
    /// Creates <see cref="IPdfRasterizer"/> instances. The Core service calls
    /// the stream overload after stripping its own annotations; the path
    /// overload is the raw-file fallback used when stripping fails.
    /// </summary>
    public interface IPdfRasterizerFactory
    {
        /// <summary>
        /// Loads a document from a seekable stream. The rasterizer takes
        /// ownership of the stream (it stays open for the document's lifetime
        /// and is disposed with it — same contract as PdfiumViewer).
        /// </summary>
        IPdfRasterizer LoadFromStream(Stream stream);

        /// <summary>Loads a document from a file path.</summary>
        IPdfRasterizer LoadFromFile(string path);
    }

    /// <summary>Page size in PDF points (single precision, matching the PdfiumViewer SizeF surface).</summary>
    public readonly struct PdfPageSize
    {
        public PdfPageSize(float width, float height)
        {
            Width = width;
            Height = height;
        }

        public float Width { get; }
        public float Height { get; }
    }

    /// <summary>
    /// Single-precision rectangle in PDF page coordinates (bottom-left origin).
    /// Mirrors the RectangleF geometry the old code consumed.
    /// </summary>
    public readonly struct PdfRectF
    {
        public PdfRectF(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public float Left => X;
        public float Top => Y;
        public float Right => X + Width;
        public float Bottom => Y + Height;

        /// <summary>Matches PdfiumViewer's PdfRectangle.IsValid.</summary>
        public bool IsValid => Width != 0 && Height != 0;
    }

    /// <summary>Integer rectangle in device space (top-left origin), matching the old System.Drawing.Rectangle result.</summary>
    public readonly struct PdfRectI
    {
        public PdfRectI(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
    }

    /// <summary>
    /// One rendered page: a top-down BGRA pixel buffer (B,G,R,A byte order),
    /// plus the DPI the caller requested so adapters can stamp it onto
    /// bitmap metadata without recomputing.
    /// </summary>
    public sealed class PdfPageBitmap
    {
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>Bytes per row (always <c>Width * 4</c> today).</summary>
        public int Stride { get; set; }

        /// <summary>B,G,R,A bytes, <c>Stride * Height</c> long.</summary>
        public byte[] Bgra { get; set; } = Array.Empty<byte>();

        /// <summary>Horizontal DPI the page was rendered at.</summary>
        public double DpiX { get; set; }

        /// <summary>Vertical DPI the page was rendered at.</summary>
        public double DpiY { get; set; }
    }
}
