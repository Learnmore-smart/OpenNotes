using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Caelum.Services;

public static class ClipboardImageDecoder
{
    private const uint CfEnhMetafile = 14;

    public static bool ContainsImage(IDataObject data)
    {
        if (data == null)
            return false;

        return HasFormat(data, DataFormats.Bitmap)
            || HasFormat(data, DataFormats.Dib)
            || HasFormat(data, DataFormats.EnhancedMetafile)
            || HasFormat(data, "PNG")
            || HasFormat(data, "DeviceIndependentBitmap")
            || HasFormat(data, "System.Drawing.Bitmap");
    }

    public static byte[] TryGetPngBytes(IDataObject data, bool includeWin32Clipboard = false)
    {
        if (data == null)
            return null;

        return TryFromPngFormat(data)
            ?? TryFromBitmapSource(data)
            ?? TryFromDib(data)
            ?? TryFromEnhancedMetafile(data)
            ?? (includeWin32Clipboard ? TryFromWin32EnhMetafile() : null);
    }

    public static bool HasWin32EnhMetafile()
    {
        return IsWin32EnhMetafileAvailable();
    }

    private static bool HasFormat(IDataObject data, string format)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(format) && data.GetDataPresent(format, true);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] TryFromPngFormat(IDataObject data)
    {
        try
        {
            if (!HasFormat(data, "PNG"))
                return null;

            using var stream = CopyToMemoryStream(data.GetData("PNG"));
            if (stream == null)
                return null;

            stream.Position = 0;
            var header = new byte[4];
            if (stream.Read(header, 0, 4) != 4 ||
                header[0] != 137 || header[1] != 80 || header[2] != 78 || header[3] != 71)
            {
                return null;
            }

            stream.Position = 0;
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] TryFromBitmapSource(IDataObject data)
    {
        try
        {
            var source = data.GetData(DataFormats.Bitmap, true) as BitmapSource
                ?? data.GetData("System.Drawing.Bitmap", true) as BitmapSource;
            if (source == null)
                return null;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] TryFromDib(IDataObject data)
    {
        try
        {
            using var stream = CopyToMemoryStream(data.GetData(DataFormats.Dib, true))
                ?? CopyToMemoryStream(data.GetData("DeviceIndependentBitmap"));
            if (stream == null || stream.Length < 40)
                return null;

            byte[] dib = stream.ToArray();
            byte[] bmp = WrapDibAsBmp(dib);
            using var image = Image.FromStream(new MemoryStream(bmp));
            using var png = new MemoryStream();
            image.Save(png, ImageFormat.Png);
            return png.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] TryFromEnhancedMetafile(IDataObject data)
    {
        try
        {
            if (!HasFormat(data, DataFormats.EnhancedMetafile))
                return null;

            object value = data.GetData(DataFormats.EnhancedMetafile, true);
            if (value is Metafile metafile)
                return RasterizeMetafile(metafile);

            using var stream = CopyToMemoryStream(value);
            return stream == null ? null : RasterizeEnhancedMetafile(stream.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static byte[] TryFromWin32EnhMetafile()
    {
        if (!IsWin32EnhMetafileAvailable())
            return null;

        if (!OpenClipboard(IntPtr.Zero))
            return null;

        try
        {
            IntPtr handle = GetClipboardData(CfEnhMetafile);
            if (handle == IntPtr.Zero)
                return null;

            uint size = GetEnhMetaFileBits(handle, 0, null);
            if (size == 0)
                return null;

            var bits = new byte[size];
            if (GetEnhMetaFileBits(handle, size, bits) == 0)
                return null;

            return RasterizeEnhancedMetafile(bits);
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool IsWin32EnhMetafileAvailable()
    {
        try
        {
            return IsClipboardFormatAvailable(CfEnhMetafile);
        }
        catch
        {
            return false;
        }
    }

    internal static byte[] RasterizeEnhancedMetafile(byte[] emfBytes)
    {
        if (emfBytes == null || emfBytes.Length == 0)
            return null;

        using var input = new MemoryStream(emfBytes, writable: false);
        using var metafile = new Metafile(input);
        return RasterizeMetafile(metafile);
    }

    private static byte[] RasterizeMetafile(Metafile metafile)
    {
        var header = metafile.GetMetafileHeader();
        double dpiX = header.DpiX > 1 ? header.DpiX : 96;
        double dpiY = header.DpiY > 1 ? header.DpiY : 96;
        int himetricWidth = Math.Max(1, (int)Math.Round(Math.Abs(header.Bounds.Width) * 96 / 2540.0));
        int himetricHeight = Math.Max(1, (int)Math.Round(Math.Abs(header.Bounds.Height) * 96 / 2540.0));

        int pixelWidth = himetricWidth;
        int pixelHeight = himetricHeight;
        try
        {
            GraphicsUnit unit = GraphicsUnit.Pixel;
            var bounds = metafile.GetBounds(ref unit);
            pixelWidth = Math.Max(1, (int)Math.Ceiling(Math.Abs(bounds.Width)));
            pixelHeight = Math.Max(1, (int)Math.Ceiling(Math.Abs(bounds.Height)));
        }
        catch
        {
        }

        int width = Math.Max(himetricWidth, pixelWidth);
        int height = Math.Max(himetricHeight, pixelHeight);

        const int maxEdge = 4096;
        if (width > maxEdge || height > maxEdge)
        {
            double scale = Math.Min((double)maxEdge / width, (double)maxEdge / height);
            width = Math.Max(1, (int)Math.Round(width * scale));
            height = Math.Max(1, (int)Math.Round(height * scale));
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution((float)dpiX, (float)dpiY);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(metafile, new Rectangle(0, 0, width, height));
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    private static byte[] WrapDibAsBmp(byte[] dib)
    {
        int headerSize = BitConverter.ToInt32(dib, 0);
        short bitCount = BitConverter.ToInt16(dib, 14);
        int paletteEntries = 0;
        if (bitCount <= 8)
        {
            int colorsUsed = BitConverter.ToInt32(dib, 32);
            paletteEntries = colorsUsed > 0 ? colorsUsed : 1 << bitCount;
        }

        int pixelOffset = 14 + headerSize + paletteEntries * 4;
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(pixelOffset).CopyTo(bmp, 10);
        Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
        return bmp;
    }

    private static MemoryStream CopyToMemoryStream(object value)
    {
        switch (value)
        {
            case MemoryStream memory:
                return new MemoryStream(memory.ToArray());
            case Stream stream:
                var copy = new MemoryStream();
                stream.CopyTo(copy);
                copy.Position = 0;
                return copy;
            case byte[] bytes:
                return new MemoryStream(bytes);
            default:
                return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("gdi32.dll")]
    private static extern uint GetEnhMetaFileBits(IntPtr hemf, uint cbBuffer, [Out] byte[] lpbBuffer);
}
