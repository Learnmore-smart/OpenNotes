using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Caelum.Pdf;

namespace Caelum.Services
{
    /// <summary>
    /// WPF facade over the UI-free <see cref="Caelum.Pdf.PdfService"/>.
    /// <para>
    /// Document ops (load, strip-and-redraw annotations, page edits, atomic
    /// saves, outline, text info, BGRA rendering) live in
    /// <c>OpenNotes.Core</c>. This class only adapts the render output to WPF
    /// types (<see cref="BitmapSource"/>/<see cref="BitmapImage"/>/PNG bytes)
    /// and re-exposes the Rect-based text/outline view models the editor
    /// already consumes.
    /// </para>
    /// <para>
    /// PNG encoding intentionally still runs through the GDI+ encoder on the
    /// BGRA buffer so <see cref="RenderPagePngBytesAsync"/> stays
    /// byte-for-byte identical to the pre-split path (same pixels, same
    /// encoder, same DPI metadata).
    /// </para>
    /// </summary>
    public class PdfService : Caelum.Pdf.PdfService
    {
        public PdfService()
        {
        }

        /// <summary>Test seam: inject a non-pdfium rasterizer.</summary>
        internal PdfService(IPdfRasterizerFactory rasterizerFactory)
            : base(rasterizerFactory)
        {
        }

        /// <summary>Character bounds in DIPs — WPF <see cref="Rect"/> twin of the Core record.</summary>
        public new sealed class PdfTextCharacterInfo
        {
            public int Offset { get; init; }
            public char Character { get; init; }
            public IReadOnlyList<Rect> Bounds { get; init; } = Array.Empty<Rect>();
            public Rect UnionBounds { get; init; }
        }

        public new sealed class PdfPageTextInfo
        {
            public string Text { get; init; } = string.Empty;
            public IReadOnlyList<PdfTextCharacterInfo> Characters { get; init; } = Array.Empty<PdfTextCharacterInfo>();
        }

        /// <summary>A lightweight outline node for the editor sidebar.</summary>
        public new sealed class PdfOutlineEntry
        {
            public string Title { get; init; } = string.Empty;
            public int PageIndex { get; init; } = -1;
            public IReadOnlyList<PdfOutlineEntry> Children { get; init; } = Array.Empty<PdfOutlineEntry>();
        }

        public bool TryGetCachedPageTextInfo(int pageIndex, out PdfPageTextInfo textInfo)
        {
            if (base.TryGetCachedPageTextInfo(pageIndex, out var coreInfo))
            {
                textInfo = ConvertTextInfo(coreInfo);
                return true;
            }

            textInfo = null;
            return false;
        }

        public new async Task<PdfPageTextInfo> GetPageTextInfoAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            var coreInfo = await base.GetPageTextInfoAsync(pageIndex, cancellationToken).ConfigureAwait(false);
            return ConvertTextInfo(coreInfo);
        }

        private static PdfPageTextInfo ConvertTextInfo(Caelum.Pdf.PdfService.PdfPageTextInfo coreInfo)
        {
            if (coreInfo == null)
                return null;

            var characters = new PdfTextCharacterInfo[coreInfo.Characters?.Count ?? 0];
            if (coreInfo.Characters != null)
            {
                for (int i = 0; i < coreInfo.Characters.Count; i++)
                {
                    var c = coreInfo.Characters[i];
                    var bounds = new Rect[c.Bounds?.Count ?? 0];
                    if (c.Bounds != null)
                    {
                        for (int b = 0; b < c.Bounds.Count; b++)
                        {
                            var r = c.Bounds[b];
                            bounds[b] = new Rect(r.X, r.Y, r.Width, r.Height);
                        }
                    }

                    characters[i] = new PdfTextCharacterInfo
                    {
                        Offset = c.Offset,
                        Character = c.Character,
                        Bounds = bounds,
                        UnionBounds = bounds.Length == 0
                            ? Rect.Empty
                            : new Rect(c.UnionBounds.X, c.UnionBounds.Y, c.UnionBounds.Width, c.UnionBounds.Height)
                    };
                }
            }

            return new PdfPageTextInfo
            {
                Text = coreInfo.Text,
                Characters = characters
            };
        }

        public new async Task<IReadOnlyList<PdfOutlineEntry>> GetOutlineAsync(CancellationToken cancellationToken = default)
        {
            var coreEntries = await base.GetOutlineAsync(cancellationToken).ConfigureAwait(false);
            return ConvertOutline(coreEntries);
        }

        private static IReadOnlyList<PdfOutlineEntry> ConvertOutline(IReadOnlyList<Caelum.Pdf.PdfService.PdfOutlineEntry> coreEntries)
        {
            if (coreEntries == null || coreEntries.Count == 0)
                return Array.Empty<PdfOutlineEntry>();

            var result = new PdfOutlineEntry[coreEntries.Count];
            for (int i = 0; i < coreEntries.Count; i++)
            {
                var e = coreEntries[i];
                result[i] = new PdfOutlineEntry
                {
                    Title = e.Title,
                    PageIndex = e.PageIndex,
                    Children = ConvertOutline(e.Children)
                };
            }
            return result;
        }

        public async Task<BitmapImage> RenderPageAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            var rendered = await RenderPageBgraAsync(pageIndex, 1.0, cancellationToken).ConfigureAwait(false);
            if (rendered == null)
                return null;

            byte[] pngBytes = EncodePng(rendered);
            var ms = new MemoryStream(pngBytes);
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                ms.Dispose();
            }
        }

        public async Task<byte[]> RenderPagePngBytesAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            return await RenderPagePngBytesAsync(pageIndex, 1.0, cancellationToken);
        }

        /// <summary>
        /// Same bytes as the pre-split path: the rasterizer BGRA output is
        /// wrapped in a GDI+ Bitmap (same pixel format, same DPI metadata) and
        /// encoded by the same GDI+ PNG encoder.
        /// </summary>
        public async Task<byte[]> RenderPagePngBytesAsync(int pageIndex, double dpiScale, CancellationToken cancellationToken = default)
        {
            var rendered = await RenderPageBgraAsync(pageIndex, dpiScale, cancellationToken).ConfigureAwait(false);
            if (rendered == null)
                return null;

            return EncodePng(rendered);
        }

        /// <summary>
        /// Fast render path: rasterizer BGRA → frozen BitmapSource directly,
        /// bypassing PNG encode/decode. ~5-10x faster than the PNG roundtrip.
        /// </summary>
        public async Task<BitmapSource> RenderPageBitmapSourceAsync(int pageIndex, double dpiScale, CancellationToken cancellationToken = default)
        {
            var rendered = await RenderPageBgraAsync(pageIndex, dpiScale, cancellationToken).ConfigureAwait(false);
            if (rendered == null)
                return null;

            var result = BitmapSource.Create(
                rendered.Width,
                rendered.Height,
                rendered.DpiX,
                rendered.DpiY,
                PixelFormats.Bgra32,
                null,
                rendered.Bgra,
                rendered.Stride);
            result.Freeze();
            return result;
        }

        /// <summary>
        /// Encodes the BGRA buffer through GDI+ exactly like the old render
        /// path did: Format32bppArgb bitmap + SetResolution + PNG save, so the
        /// output is byte-identical when the pixels match.
        /// </summary>
        private static byte[] EncodePng(PdfPageBitmap rendered)
        {
            using var bitmap = new Bitmap(rendered.Width, rendered.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            bitmap.SetResolution((float)rendered.DpiX, (float)rendered.DpiY);

            var data = bitmap.LockBits(
                new Rectangle(0, 0, rendered.Width, rendered.Height),
                ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                // A freshly created Format32bppArgb bitmap is top-down with a
                // width*4 stride — same layout as the rasterizer buffer.
                System.Diagnostics.Debug.Assert(data.Stride == rendered.Stride);
                int rowBytes = rendered.Width * 4;
                for (int y = 0; y < rendered.Height; y++)
                {
                    IntPtr dest = IntPtr.Add(data.Scan0, y * data.Stride);
                    System.Runtime.InteropServices.Marshal.Copy(rendered.Bgra, y * rendered.Stride, dest, rowBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }
}
