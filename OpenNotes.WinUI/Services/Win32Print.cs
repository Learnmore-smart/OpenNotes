using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Caelum.Services
{
    /// <summary>
    /// Win32 GDI print path — the unpackaged-WinUI stand-in for WPF's
    /// <c>System.Windows.Controls.PrintDialog</c> +
    /// <c>PrintDocument(paginator, name)</c> pair. The WinRT
    /// <c>Windows.Graphics.Printing</c> task pipeline wants packaged
    /// CoreWindow plumbing, so this shell prints the way the WPF dialog did
    /// underneath: <c>PrintDlgEx</c> on the MainWindow HWND picks the
    /// printer/page-range/copies, then a printer DC spools each rendered
    /// page through <c>StretchDIBits</c>.
    ///
    /// Copies + collation deliberately bypass the driver: the returned
    /// DEVMODE's dmCopies/dmCollate are read (the dialog stores them there
    /// under <c>PD_USEDEVMODECOPIESANDCOLLATE</c>) and then reset to
    /// one-copy-uncollated before <c>CreateDC</c>, so the job loop below is
    /// the single replication authority on every driver.
    /// </summary>
    internal static class Win32Print
    {
        // ── PRINTDLGEX / comdlg32 ────────────────────────────────────────

        private const uint PD_ALLPAGES = 0x00000000;
        private const uint PD_PAGENUMS = 0x00000002;
        private const uint PD_COLLATE = 0x00000010;
        private const uint PD_USEDEVMODECOPIESANDCOLLATE = 0x00040000;

        private const uint PD_RESULT_CANCEL = 0;
        private const uint PD_RESULT_PRINT = 1;

        // DEVMODEW byte offsets into the fixed prefix (ABI-stable since NT4).
        private const int DmCopiesOffset = 86;
        private const int DmCollateOffset = 100;

        // ── GDI ────────────────────────────────────────────────────────

        private const int HORZRES = 8;          // printable width, device px
        private const int VERTRES = 10;         // printable height, device px
        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;
        private const int PHYSICALWIDTH = 110;
        private const int PHYSICALHEIGHT = 111;

        private const int BI_RGB = 0;
        private const uint DIB_RGB_COLORS = 0;
        private const uint SRCCOPY = 0x00CC0020;
        private const int HALFTONE = 4;
        private const int GDI_ERROR = unchecked((int)0xFFFFFFFF);

        [StructLayout(LayoutKind.Sequential)]
        private struct PRINTPAGERANGE
        {
            public uint nFromPage;
            public uint nToPage;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVNAMES
        {
            public ushort wDriverOffset;
            public ushort wDeviceOffset;
            public ushort wOutputOffset;
            public ushort wDefault;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PRINTDLGEX
        {
            public uint lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hDevMode;
            public IntPtr hDevNames;
            public IntPtr hDC;
            public uint Flags;
            public uint Flags2;
            public uint ExclusionFlags;
            public uint nPageRanges;
            public uint nMaxPageRanges;
            public IntPtr lpPageRanges;
            public uint nMinPage;
            public uint nMaxPage;
            public uint nCopies;
            public IntPtr hInstance;
            public IntPtr lpPrintTemplateName;
            public IntPtr lpCallback;
            public uint nPropertyPages;
            public IntPtr lphPropertyPages;
            public uint nStartPage;
            public uint dwResultAction;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DOCINFO
        {
            public int cbSize;
            public string lpszDocName;
            public string lpszOutput;
            public string lpszDatatype;
            public uint fwType;
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

        [DllImport("comdlg32.dll", EntryPoint = "PrintDlgExW", CharSet = CharSet.Unicode)]
        private static extern int PrintDlgEx(ref PRINTDLGEX lpppd);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);

        // Internal: Win32PrintJob::Dispose releases the dialog-owned handles.
        [DllImport("kernel32.dll")]
        internal static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDCW(
            string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int StartDocW(IntPtr hdc, ref DOCINFO lpdi);

        [DllImport("gdi32.dll")]
        private static extern int StartPage(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int EndPage(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int EndDoc(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int AbortDoc(IntPtr hdc);

        // Internal: Win32PrintJob::Dispose owns the printer DC lifetime.
        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        private static extern int StretchDIBits(
            IntPtr hdc,
            int xDest, int yDest, int DestWidth, int DestHeight,
            int xSrc, int ySrc, int SrcWidth, int SrcHeight,
            IntPtr lpBits, ref BITMAPINFOHEADER lpbmi,
            uint iUsage, uint dwRop);

        /// <summary>
        /// Shows the classic Win32 print sheet (WPF <c>PrintDialog.ShowDialog</c>
        /// parity) and, on Print, returns a ready job: a live printer DC
        /// created from the user's DEVMODE with copies/collate stripped for
        /// managed replication, plus the dialog's page-range selection.
        /// Null on Cancel/Apply. Throws on dialog failure (no default
        /// printer surfaces as a PrintDlgEx HRESULT → the caller's
        /// PrintFailed UX).
        /// </summary>
        [SupportedOSPlatform("windows10.0.17763.0")]
        public static Win32PrintJob TryShowPrintDialog(IntPtr ownerHwnd, int pageCount)
        {
            // 32 slots absorb any realistic disjoint "Pages" input; the
            // dialog reports an error to the user when it overflows.
            const int rangeCapacity = 32;
            IntPtr rangesBuffer = Marshal.AllocHGlobal(
                rangeCapacity * Marshal.SizeOf<PRINTPAGERANGE>());
            try
            {
                var dialog = new PRINTDLGEX
                {
                    lStructSize = (uint)Marshal.SizeOf<PRINTDLGEX>(),
                    hwndOwner = ownerHwnd,
                    // No PD_RETURNDC: the printer DC is created below from the
                    // user's DEVMODE after copies/collate are lifted out, so a
                    // driver can never double-replicate the job.
                    Flags = PD_ALLPAGES | PD_USEDEVMODECOPIESANDCOLLATE | PD_COLLATE,
                    nPageRanges = 0,
                    nMaxPageRanges = rangeCapacity,
                    lpPageRanges = rangesBuffer,
                    nMinPage = 1,
                    nMaxPage = (uint)Math.Max(1, pageCount),
                    nCopies = 1,
                };

                int hr = PrintDlgEx(ref dialog);
                if (hr != 0)
                {
                    FreeGlobal(ref dialog);
                    throw Marshal.GetExceptionForHR(hr)
                        ?? new Win32Exception(hr, $"PrintDlgEx failed (0x{hr:X8})");
                }

                if (dialog.dwResultAction != PD_RESULT_PRINT)
                {
                    FreeGlobal(ref dialog);
                    return null;
                }

                return CreateJob(ref dialog, rangesBuffer, pageCount);
            }
            finally
            {
                Marshal.FreeHGlobal(rangesBuffer);
            }
        }

        private static Win32PrintJob CreateJob(
            ref PRINTDLGEX dialog, IntPtr rangesBuffer, int pageCount)
        {
            // The dialog owns these allocations on success — without them
            // there is no printer selection to build on (defensive: the API
            // contract says they are always filled, but a null GlobalLock
            // dereference is an uncatchable AV).
            if (dialog.hDevMode == IntPtr.Zero || dialog.hDevNames == IntPtr.Zero)
            {
                FreeGlobal(ref dialog);
                throw new Win32Exception("PrintDlgEx returned no printer selection.");
            }

            var job = new Win32PrintJob
            {
                DevModeHandle = dialog.hDevMode,
                DevNamesHandle = dialog.hDevNames,
                PageCount = pageCount,
            };
            // Ownership moved to the job — the caller frees only on error.
            dialog.hDevMode = IntPtr.Zero;
            dialog.hDevNames = IntPtr.Zero;

            try
            {
                string driver = ReadDevNamesString(job.DevNamesHandle, 0);
                string device = ReadDevNamesString(job.DevNamesHandle, 2);
                string output = ReadDevNamesString(job.DevNamesHandle, 4);
                job.PrinterName = device;

                IntPtr devMode = GlobalLock(job.DevModeHandle);
                try
                {
                    int dmCopies = Marshal.ReadInt16(devMode, DmCopiesOffset);
                    job.Copies = Math.Clamp(
                        Math.Max((int)dialog.nCopies, dmCopies), 1, 999);
                    job.CollateCopies = Marshal.ReadInt16(devMode, DmCollateOffset) != 0;

                    // See class summary: the job loop owns replication.
                    Marshal.WriteInt16(devMode, DmCopiesOffset, (short)1);
                    Marshal.WriteInt16(devMode, DmCollateOffset, (short)0);

                    job.PrinterDC = CreateDCW(driver, device, output, devMode);
                    if (job.PrinterDC == IntPtr.Zero)
                        job.PrinterDC = CreateDCW("WINSPOOL", device, null, devMode);
                    if (job.PrinterDC == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    GlobalUnlock(job.DevModeHandle);
                }

                job.PrinterDpiX = GetDeviceCaps(job.PrinterDC, LOGPIXELSX);
                job.PrinterDpiY = GetDeviceCaps(job.PrinterDC, LOGPIXELSY);
                job.PrintableWidthPx = GetDeviceCaps(job.PrinterDC, HORZRES);
                job.PrintableHeightPx = GetDeviceCaps(job.PrinterDC, VERTRES);
                // HORZRES/VERTRES are the printable rect on real printer DCs;
                // the physical extent is the fallback for drivers that don't.
                if (job.PrintableWidthPx <= 0)
                    job.PrintableWidthPx = GetDeviceCaps(job.PrinterDC, PHYSICALWIDTH);
                if (job.PrintableHeightPx <= 0)
                    job.PrintableHeightPx = GetDeviceCaps(job.PrinterDC, PHYSICALHEIGHT);
                if (job.PrintableWidthPx <= 0 || job.PrintableHeightPx <= 0)
                    throw new Win32Exception("The printer reported no printable area.");

                SetStretchBltMode(job.PrinterDC, HALFTONE);

                if ((dialog.Flags & PD_PAGENUMS) != 0 && dialog.nPageRanges > 0)
                {
                    var ranges = new List<(int From, int To)>((int)dialog.nPageRanges);
                    int rangeStride = Marshal.SizeOf<PRINTPAGERANGE>();
                    for (int i = 0; i < (int)dialog.nPageRanges; i++)
                    {
                        var range = Marshal.PtrToStructure<PRINTPAGERANGE>(
                            rangesBuffer + i * rangeStride);
                        int from = (int)Math.Clamp(range.nFromPage, 1u, (uint)pageCount);
                        int to = (int)Math.Clamp(range.nToPage, 1u, (uint)pageCount);
                        if (from <= to)
                            ranges.Add((from, to));
                    }
                    job.PageRanges = ranges;
                }

                return job;
            }
            catch
            {
                job.Dispose();
                throw;
            }
        }

        private static string ReadDevNamesString(IntPtr hDevNames, int nameOffsetField)
        {
            if (hDevNames == IntPtr.Zero)
                return null;
            IntPtr ptr = GlobalLock(hDevNames);
            try
            {
                var names = Marshal.PtrToStructure<DEVNAMES>(ptr);
                ushort offset = nameOffsetField == 0 ? names.wDriverOffset
                    : nameOffsetField == 2 ? names.wDeviceOffset
                    : names.wOutputOffset;
                if (offset == 0)
                    return null;
                return Marshal.PtrToStringUni(ptr + offset);
            }
            finally
            {
                GlobalUnlock(hDevNames);
            }
        }

        private static void FreeGlobal(ref PRINTDLGEX dialog)
        {
            if (dialog.hDevMode != IntPtr.Zero)
            {
                GlobalFree(dialog.hDevMode);
                dialog.hDevMode = IntPtr.Zero;
            }
            if (dialog.hDevNames != IntPtr.Zero)
            {
                GlobalFree(dialog.hDevNames);
                dialog.hDevNames = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Spools one DOCINFO job: for each rendered page, GDI-stretches the
        /// BGRA raster into the aspect-fitted printable-area rect computed by
        /// <see cref="PrintPageGeometry.FitPageToPrintableArea"/> — the
        /// FixedPage-centered equivalent of WPF's CreatePrintDocument.
        /// Runs off the UI thread (pdfium + GDI both serialize there).
        /// </summary>
        [SupportedOSPlatform("windows10.0.17763.0")]
        public static void PrintPages(
            Win32PrintJob job,
            IReadOnlyList<PrintablePageImage> pages,
            string jobName,
            CancellationToken cancellationToken)
        {
            if (job == null || job.PrinterDC == IntPtr.Zero)
                throw new InvalidOperationException("The print job has no printer DC.");
            if (pages == null || pages.Count == 0)
                return;

            var docInfo = new DOCINFO
            {
                cbSize = Marshal.SizeOf<DOCINFO>(),
                lpszDocName = string.IsNullOrWhiteSpace(jobName) ? "OpenNotes" : jobName,
                lpszOutput = null,
                lpszDatatype = null,
                fwType = 0,
            };

            if (StartDocW(job.PrinterDC, ref docInfo) <= 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                if (job.CollateCopies || job.Copies <= 1)
                {
                    for (int copy = 0; copy < job.Copies; copy++)
                        SpoolPageSequence(job, pages, cancellationToken);
                }
                else
                {
                    // Uncollated: N copies of page 1, then N of page 2, ...
                    foreach (var page in pages)
                    {
                        for (int copy = 0; copy < job.Copies; copy++)
                            SpoolPage(job, page, cancellationToken);
                    }
                }
            }
            catch
            {
                AbortDoc(job.PrinterDC);
                throw;
            }

            if (EndDoc(job.PrinterDC) <= 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private static void SpoolPageSequence(
            Win32PrintJob job,
            IReadOnlyList<PrintablePageImage> pages,
            CancellationToken cancellationToken)
        {
            foreach (var page in pages)
                SpoolPage(job, page, cancellationToken);
        }

        private static void SpoolPage(
            Win32PrintJob job,
            PrintablePageImage page,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dest = PrintPageGeometry.FitPageToPrintableArea(
                page.WidthPoints, page.HeightPoints,
                job.PrintableWidthPx, job.PrintableHeightPx);
            if (dest.Width <= 0 || dest.Height <= 0)
                return;

            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = page.PixelWidth,
                // Negative height: the pdfium buffer is top-down.
                biHeight = -page.PixelHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
                biSizeImage = (uint)(page.Stride * page.PixelHeight),
            };

            GCHandle pixels = GCHandle.Alloc(page.Bgra, GCHandleType.Pinned);
            try
            {
                if (StartPage(job.PrinterDC) <= 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                int written = StretchDIBits(
                    job.PrinterDC,
                    dest.X, dest.Y, dest.Width, dest.Height,
                    0, 0, page.PixelWidth, page.PixelHeight,
                    pixels.AddrOfPinnedObject(), ref header,
                    DIB_RGB_COLORS, SRCCOPY);
                if (written == GDI_ERROR)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (EndPage(job.PrinterDC) <= 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                pixels.Free();
            }
        }
    }

    /// <summary>
    /// One user's print-dialog answer plus the live printer DC built from
    /// it. Owns the DEVMODE/DEVNAMES handles and the DC — Dispose releases
    /// all three.
    /// </summary>
    internal sealed class Win32PrintJob : IDisposable
    {
        public IntPtr PrinterDC { get; set; }
        public IntPtr DevModeHandle { get; set; }
        public IntPtr DevNamesHandle { get; set; }
        public string PrinterName { get; set; }
        public int PageCount { get; set; }
        public int Copies { get; set; } = 1;
        public bool CollateCopies { get; set; }
        public int PrinterDpiX { get; set; }
        public int PrinterDpiY { get; set; }
        public int PrintableWidthPx { get; set; }
        public int PrintableHeightPx { get; set; }

        /// <summary>1-based inclusive ranges; null/empty means the whole document.</summary>
        public IReadOnlyList<(int From, int To)> PageRanges { get; set; }

        /// <summary>The printer DPI to rasterize at (min of the two axes).</summary>
        public int PrinterDpi =>
            PrinterDpiX > 0 && PrinterDpiY > 0 ? Math.Min(PrinterDpiX, PrinterDpiY)
                : Math.Max(PrinterDpiX, PrinterDpiY);

        /// <summary>
        /// Expands the dialog's page selection into zero-based indexes in
        /// print order (range order preserved, duplicates kept — same as the
        /// OS print path).
        /// </summary>
        public IReadOnlyList<int> OrderedPageIndexes()
        {
            var indexes = new List<int>();
            if (PageRanges == null || PageRanges.Count == 0)
            {
                for (int i = 0; i < PageCount; i++)
                    indexes.Add(i);
                return indexes;
            }

            foreach (var (from, to) in PageRanges)
            {
                for (int p = Math.Max(1, from); p <= Math.Min(PageCount, to); p++)
                    indexes.Add(p - 1);
            }
            return indexes;
        }

        public void Dispose()
        {
            if (PrinterDC != IntPtr.Zero)
            {
                Win32Print.DeleteDC(PrinterDC);
                PrinterDC = IntPtr.Zero;
            }
            if (DevModeHandle != IntPtr.Zero)
            {
                Win32Print.GlobalFree(DevModeHandle);
                DevModeHandle = IntPtr.Zero;
            }
            if (DevNamesHandle != IntPtr.Zero)
            {
                Win32Print.GlobalFree(DevNamesHandle);
                DevNamesHandle = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// One rendered page waiting to spool: a top-down BGRA buffer (the Core
    /// <c>PdfPageBitmap</c> payload) plus the page's size in PDF points for
    /// the fit math. The WPF twin carried a frozen <c>BitmapSource</c>; the
    /// GDI path needs the raw bytes instead.
    /// </summary>
    internal sealed class PrintablePageImage
    {
        public int PageIndex { get; init; }
        public byte[] Bgra { get; init; }
        public int PixelWidth { get; init; }
        public int PixelHeight { get; init; }
        public int Stride { get; init; }
        public double WidthPoints { get; init; }
        public double HeightPoints { get; init; }
    }
}
