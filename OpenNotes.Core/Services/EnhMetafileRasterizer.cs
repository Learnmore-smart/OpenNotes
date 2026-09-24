using System;
using System.Runtime.InteropServices;

namespace Caelum.Services
{
    /// <summary>
    /// Pure-Win32 Enhanced Metafile helpers shared by every UI shell. EMF is a
    /// GDI record format — both WPF (System.Drawing) and WinUI 3 resolve it by
    /// playing the record back into a memory DC, so this rasterizer lives in
    /// Core (no UI-framework dependency, headless-testable) and returns raw
    /// 32bpp BGRA pixels each shell wraps in its own bitmap type.
    /// </summary>
    public static class EnhMetafileRasterizer
    {
        private const int CF_ENHMETAFILE = 14;
        private const int WHITENESS = 0x00FF0062; // PatBlt: fill white
        private const int DIB_RGB_COLORS = 0;
        private const uint BI_RGB = 0;

        /// <summary>
        /// True when the Win32 clipboard carries a live CF_ENHMETAFILE handle.
        /// WinUI's <c>DataPackageView</c> does not surface metafiles, so PowerPoint
        /// / Word / CAD copies that only publish EMF are invisible without this
        /// check (WPF ClipboardImageDecoder EMF leg parity).
        /// </summary>
        public static bool IsClipboardEnhMetafileAvailable()
            => IsClipboardFormatAvailable(CF_ENHMETAFILE);

        /// <summary>
        /// Copies the current clipboard EMF out to a self-contained byte[]
        /// (GetEnhMetaFileBits). Returns false when no metafile is on the
        /// clipboard or the handle cannot be read.
        /// </summary>
        public static bool TryReadClipboardEnhMetafileBytes(out byte[] emfBytes)
        {
            emfBytes = null;
            if (!OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                IntPtr hEmf = GetClipboardData(CF_ENHMETAFILE);
                if (hEmf == IntPtr.Zero)
                    return false;

                uint size = GetEnhMetaFileBits(hEmf, 0, null);
                if (size == 0)
                    return false;

                byte[] buffer = new byte[size];
                if (GetEnhMetaFileBits(hEmf, size, buffer) == 0)
                    return false;

                emfBytes = buffer;
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }

        /// <summary>
        /// Rasterizes an EMF record to a white-backed 32bpp BGRA pixel buffer.
        /// Dimensions come from the metafile's rclBounds (pixel) rect with the
        /// rclFrame physical-size (.01 mm → 96 DIP) conversion as a floor for
        /// boundsless records — mirroring the WPF decoder's sizing rules.
        /// <paramref name="maxEdge"/> caps either axis so a hostile metafile
        /// cannot request unbounded memory (0 = no cap).
        /// </summary>
        public static bool TryRasterizeToBgra(
            byte[] emfBytes,
            int maxEdge,
            out byte[] bgraPixels,
            out int width,
            out int height)
        {
            bgraPixels = null;
            width = 0;
            height = 0;

            if (emfBytes == null || emfBytes.Length < Marshal.SizeOf<ENHMETAHEADER>())
                return false;

            IntPtr hEmf = SetEnhMetaFileBits((uint)emfBytes.Length, emfBytes);
            if (hEmf == IntPtr.Zero)
                return false;

            IntPtr hdcScreen = IntPtr.Zero;
            IntPtr hdcMem = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOld = IntPtr.Zero;

            try
            {
                var header = new ENHMETAHEADER();
                if (GetEnhMetaFileHeader(hEmf, (uint)Marshal.SizeOf<ENHMETAHEADER>(), header) == 0)
                    return false;

                width = header.rclBounds.right - header.rclBounds.left;
                height = header.rclBounds.bottom - header.rclBounds.top;
                if (width <= 0 || height <= 0)
                {
                    // Boundsless metafile: rclFrame is the intended physical
                    // size in .01 millimetres — convert at 96 DIP like WPF.
                    int frameW = (int)Math.Round(
                        (header.rclFrame.right - header.rclFrame.left) * 96.0 / 2540.0);
                    int frameH = (int)Math.Round(
                        (header.rclFrame.bottom - header.rclFrame.top) * 96.0 / 2540.0);
                    width = Math.Max(1, frameW);
                    height = Math.Max(1, frameH);
                }

                if (maxEdge > 0 && (width > maxEdge || height > maxEdge))
                {
                    double scale = Math.Min(
                        (double)maxEdge / width, (double)maxEdge / height);
                    width = Math.Max(1, (int)Math.Round(width * scale));
                    height = Math.Max(1, (int)Math.Round(height * scale));
                }

                hdcScreen = GetDC(IntPtr.Zero);
                if (hdcScreen == IntPtr.Zero)
                    return false;

                hdcMem = CreateCompatibleDC(hdcScreen);
                if (hdcMem == IntPtr.Zero)
                    return false;

                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height, // top-down
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = BI_RGB,
                    },
                };

                hBitmap = CreateDIBSection(
                    hdcScreen, ref bmi, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                    return false;

                hOld = SelectObject(hdcMem, hBitmap);

                // White backing like the WPF decoder's CompositingModeSourceCopy
                // clear — metafile records that never paint the background
                // must not come through as transparent black.
                PatBlt(hdcMem, 0, 0, width, height, WHITENESS);

                var playRect = new RECT { left = 0, top = 0, right = width, bottom = height };
                if (!PlayEnhMetaFile(hdcMem, hEmf, ref playRect))
                    return false;

                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                Marshal.Copy(bits, pixels, 0, pixels.Length);

                // GDI 32bpp DIBs leave the alpha channel at 0; normalize to
                // opaque so shells can wrap the buffer without a fix-up pass.
                for (int i = 3; i < pixels.Length; i += 4)
                    pixels[i] = 0xFF;

                bgraPixels = pixels;
                return true;
            }
            finally
            {
                if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
                    SelectObject(hdcMem, hOld);
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
                if (hdcMem != IntPtr.Zero)
                    DeleteDC(hdcMem);
                if (hdcScreen != IntPtr.Zero)
                    ReleaseDC(IntPtr.Zero, hdcScreen);
                DeleteEnhMetaFile(hEmf);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZEL
        {
            public int cx, cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private class ENHMETAHEADER
        {
            public uint iType;
            public uint nSize;
            public RECT rclBounds;
            public RECT rclFrame;
            public uint dSignature;
            public uint nVersion;
            public uint nBytes;
            public uint nRecords;
            public ushort nHandles;
            public ushort sReserved;
            public uint nDescription;
            public uint offDescription;
            public uint nPalEntries;
            public SIZEL szlDevice;
            public SIZEL szlMillimeters;
            public uint cbPixelFormat;
            public uint offPixelFormat;
            public uint bOpenGL;
            public SIZEL szlMicrometers;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern uint GetEnhMetaFileBits(IntPtr hEmf, uint nSize, byte[] buffer);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SetEnhMetaFileBits(uint nSize, byte[] pb);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteEnhMetaFile(IntPtr hEmf);

        [DllImport("gdi32.dll")]
        private static extern uint GetEnhMetaFileHeader(IntPtr hEmf, uint nSize,
            [In, Out] ENHMETAHEADER header);

        [DllImport("gdi32.dll")]
        private static extern bool PlayEnhMetaFile(IntPtr hdc, IntPtr hEmf, ref RECT rect);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
            uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr ho);

        [DllImport("gdi32.dll")]
        private static extern bool PatBlt(IntPtr hdc, int x, int y, int w, int h, uint rop);
    }
}
