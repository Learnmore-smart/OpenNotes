using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Caelum.Services
{
    /// <summary>
    /// WinUI port of the WPF <c>ClipboardImageDecoder</c> (Task 19): turns
    /// whatever image payload a source app put on the clipboard into PNG bytes
    /// that <see cref="OpenNotes.Core.Pdf.PdfService"/> persists unchanged.
    ///
    /// Legs, in WPF priority order:
    ///  1. the "PNG" clipboard format (browsers/Office PNG payloads),
    ///  2. the WinRT Bitmap format (WIC decode → PNG re-encode),
    ///  3. the Win32 CF_ENHMETAFILE handle — invisible to DataPackageView, so
    ///     it is read through user32 and rasterized by the UI-free
    ///     <see cref="Caelum.Services.EnhMetafileRasterizer"/> in Core, then
    ///     wrapped in a SoftwareBitmap and PNG-encoded.
    ///
    /// The WPF DIB leg needs no direct equivalent: DataPackageView already
    /// merges CF_DIB/CF_BITMAP publishers into the Bitmap format.
    /// </summary>
    public static class ClipboardImageDecoder
    {
        /// <summary>Cap on a decoded bitmap edge (WPF parity: 16384).</summary>
        private const int MaxDecodeEdge = 16384;

        /// <summary>
        /// True when <paramref name="content"/> (or the raw Win32 clipboard)
        /// advertises any image payload we can decode.
        /// </summary>
        public static bool ContainsImage(DataPackageView content, bool includeWin32Clipboard = true)
        {
            if (content != null &&
                (content.Contains("PNG") || content.Contains(StandardDataFormats.Bitmap)))
            {
                return true;
            }
            return includeWin32Clipboard
                && EnhMetafileRasterizer.IsClipboardEnhMetafileAvailable();
        }

        /// <summary>
        /// Best-effort image extraction → PNG bytes. Null when nothing
        /// decodable is present. Never throws on a malformed payload — a bad
        /// clipboard must not kill Ctrl+V (WPF decoder contract).
        /// </summary>
        public static async Task<byte[]> TryGetPngBytesAsync(
            DataPackageView content,
            bool includeWin32Clipboard = true)
        {
            if (content != null)
            {
                // Leg 1 — "PNG" format: bytes are already PNG-encoded.
                if (content.Contains("PNG"))
                {
                    byte[] png = await TryReadDataAsync(content, "PNG").ConfigureAwait(true);
                    if (IsPng(png))
                        return png;
                }

                // Leg 2 — Bitmap format: WIC-decode whatever the source
                // published (BMP/DIB/PNG/JPEG …) and re-encode as PNG.
                if (content.Contains(StandardDataFormats.Bitmap))
                {
                    try
                    {
                        var reference = await content.GetBitmapAsync();
                        if (reference != null)
                        {
                            using var stream = await reference.OpenReadAsync();
                            byte[] png = await ReencodeToPngAsync(stream).ConfigureAwait(true);
                            if (png != null)
                                return png;
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // fall through to the Win32 leg
                    }
                }
            }

            // Leg 3 — CF_ENHMETAFILE (PowerPoint/Word/CAD copies that never
            // publish a raster format). Rasterize in Core, encode here.
            if (includeWin32Clipboard &&
                EnhMetafileRasterizer.TryReadClipboardEnhMetafileBytes(out byte[] emfBytes) &&
                EnhMetafileRasterizer.TryRasterizeToBgra(
                    emfBytes, MaxDecodeEdge, out byte[] bgra, out int width, out int height))
            {
                var bitmap = new SoftwareBitmap(
                    BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
                bitmap.CopyFromBuffer(bgra.AsBuffer());
                byte[] png = await EncodeToPngAsync(bitmap).ConfigureAwait(true);
                if (png != null)
                    return png;
            }

            return null;
        }

        private static async Task<byte[]> TryReadDataAsync(DataPackageView content, string format)
        {
            try
            {
                object data = await content.GetDataAsync(format);
                switch (data)
                {
                    case IRandomAccessStreamWithContentType stream:
                        return await ReadStreamAsync(stream);
                    case IRandomAccessStream raw:
                        return await ReadStreamAsync(raw);
                    case IBuffer buffer:
                        return buffer.ToArray();
                    case IStorageItem item when item is StorageFile file:
                        return (await FileIO.ReadBufferAsync(file)).ToArray();
                    default:
                        return null;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }

        private static async Task<byte[]> ReadStreamAsync(IRandomAccessStream stream)
        {
            if (stream == null || stream.Size == 0 || stream.Size > int.MaxValue)
                return null;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            byte[] bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            return bytes;
        }

        /// <summary>WIC decode → SoftwareBitmap → PNG re-encode.</summary>
        private static async Task<byte[]> ReencodeToPngAsync(IRandomAccessStream stream)
        {
            try
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                if (decoder.PixelWidth > MaxDecodeEdge || decoder.PixelHeight > MaxDecodeEdge)
                    return null;
                var bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                return await EncodeToPngAsync(bitmap);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }

        private static async Task<byte[]> EncodeToPngAsync(SoftwareBitmap bitmap)
        {
            try
            {
                using var output = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
                using var reader = new DataReader(output.GetInputStreamAt(0));
                await reader.LoadAsync((uint)output.Size);
                byte[] bytes = new byte[output.Size];
                reader.ReadBytes(bytes);
                return bytes;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }

        private static bool IsPng(byte[] bytes)
            => bytes != null && bytes.Length >= 4
                && bytes[0] == 0x89 && bytes[1] == 0x50
                && bytes[2] == 0x4E && bytes[3] == 0x47;
    }
}
