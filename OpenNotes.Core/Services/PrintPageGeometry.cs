using System;

namespace Caelum.Services
{
    /// <summary>
    /// Pure page-fit/raster-planning math shared by the print pipelines.
    /// The WPF shell applied this inside <c>CreatePrintDocument</c> (fit a
    /// page into <c>PrintDialog.PrintableAreaWidth/Height</c> preserving
    /// aspect, centered) and inside <c>RenderPrintablePages</c> (220-DPI
    /// baseline). The WinUI GDI path needs the same fit expressed in printer
    /// device pixels plus a DPI resolver that trades resolution for a bounded
    /// total raster budget when a long document is printed at high DPI.
    /// </summary>
    public static class PrintPageGeometry
    {
        /// <summary>Never render below screen-preview quality.</summary>
        public const int MinPrintDpi = 96;

        /// <summary>Upper bound for the page raster — typical printers are 300/600 DPI.</summary>
        public const int MaxPrintDpi = 600;

        /// <summary>
        /// Total BGRA pixel budget across every page of one print job
        /// (250 MP ≈ 1 GB of 4-byte pixels). The WPF pipeline buffered all
        /// pages at a fixed 220 DPI — ~453 MP for 100 Letter pages — so the
        /// resolver only kicks in for documents WPF already handled worse.
        /// </summary>
        public const long MaxPrintRasterPixels = 250_000_000;

        /// <summary>
        /// Picks the rasterization DPI for a print job: the printer's own DPI
        /// clamped to [<see cref="MinPrintDpi"/>, <see cref="MaxPrintDpi"/>],
        /// then reduced when the whole job would exceed
        /// <see cref="MaxPrintRasterPixels"/>.
        /// </summary>
        /// <param name="printerDpi">
        /// The printer device's effective DPI (min of X/Y when they differ;
        /// callers resolve that first). Non-positive values fall back to the
        /// minimum render DPI.
        /// </param>
        /// <param name="totalPageAreaPoints">
        /// Sum of width×height in PDF points over every page about to be
        /// rendered. &lt;= 0 disables the budget clamp.
        /// </param>
        public static int ResolvePrintRenderDpi(int printerDpi, double totalPageAreaPoints)
        {
            int dpi = printerDpi <= 0
                ? MinPrintDpi
                : Math.Clamp(printerDpi, MinPrintDpi, MaxPrintDpi);

            if (totalPageAreaPoints <= 0)
                return dpi;

            // pixels = areaPoints * (dpi/72)^2  →  dpi = 72*sqrt(budget/area)
            double capDpi = 72.0 * Math.Sqrt(MaxPrintRasterPixels / totalPageAreaPoints);
            if (capDpi < dpi)
                dpi = Math.Max(MinPrintDpi, (int)Math.Floor(capDpi));

            return dpi;
        }

        /// <summary>
        /// The print-time destination rectangle for one page, in printer
        /// device pixels relative to the printable-area origin — the WPF
        /// <c>CreatePrintDocument</c> fit: uniform scale into the printable
        /// area preserving aspect ratio, centered, never upscaled past the
        /// area (the driver clips a ±1 px rounding overflow; we clamp it
        /// off instead).
        /// </summary>
        public static PrintPageRect FitPageToPrintableArea(
            double pageWidthPoints,
            double pageHeightPoints,
            int printableWidthPx,
            int printableHeightPx)
        {
            if (printableWidthPx <= 0 || printableHeightPx <= 0)
                return new PrintPageRect(0, 0, 0, 0);

            // Degenerate page sizes behave like WPF's zero-size image: pin
            // the origin and leave nothing to spool.
            if (pageWidthPoints <= 0 || pageHeightPoints <= 0)
                return new PrintPageRect(0, 0, 0, 0);

            double scale = Math.Min(
                printableWidthPx / pageWidthPoints,
                printableHeightPx / pageHeightPoints);
            int width = Math.Min(printableWidthPx,
                Math.Max(1, (int)Math.Round(pageWidthPoints * scale)));
            int height = Math.Min(printableHeightPx,
                Math.Max(1, (int)Math.Round(pageHeightPoints * scale)));
            int x = Math.Max(0, (printableWidthPx - width) / 2);
            int y = Math.Max(0, (printableHeightPx - height) / 2);
            return new PrintPageRect(x, y, width, height);
        }
    }

    /// <summary>Integer device-pixel destination rectangle for a printed page.</summary>
    public readonly struct PrintPageRect
    {
        public PrintPageRect(int x, int y, int width, int height)
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
}
