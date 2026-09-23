using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Caelum.Controls
{
    /// <summary>
    /// V6 WinUI port of WPF <c>Controls/PdfPageControl.xaml.cs</c>, scoped to
    /// the Task 6 shell: page frame, fixed DIP size, rendered bitmap slot and
    /// the named overlay canvases the annotation tasks (T7/T8/T9) attach to.
    ///
    /// Not ported (deferred):
    /// <list type="bullet">
    /// <item>WPF <c>InkCanvas</c> input/processing — the WinUI ink surface is
    /// Task 7.</item>
    /// <item><c>SetBitmapScalingMode</c> — WPF toggled
    /// <c>RenderOptions.BitmapScalingMode</c> during scroll/zoom; WinUI
    /// images always sample at full quality so the hook is unnecessary.</item>
    /// <item>Stroke/annotation selection and text-hit overlays — Tasks 7/8.</item>
    /// </list>
    /// </summary>
    public sealed partial class PdfPageControl : UserControl
    {
        public PdfPageControl()
        {
            InitializeComponent();
        }

        /// <summary>Zero-based page index inside the loaded document.</summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// The rasterized page bitmap. Assignment mirrors the WPF
        /// <c>PageSource</c> setter: replacing the source releases the old
        /// SoftwareBitmapSource reference so the working-set trim can reclaim
        /// it by assigning <see langword="null"/>.
        /// </summary>
        public SoftwareBitmapSource PageSource
        {
            get => PdfImage.Source as SoftwareBitmapSource;
            set => PdfImage.Source = value;
        }

        /// <summary>
        /// Sets the rendered page bitmap. WPF staged the swap through
        /// <c>PdfImageOverlay</c> to avoid a blank frame; Task 7 ports the
        /// swap animation if profiling shows the flash on WinUI.
        /// </summary>
        public void SetPageImage(SoftwareBitmapSource source) => PageSource = source;

        /// <summary>
        /// WPF gated ink input on the active tab. Kept as a no-op seam until
        /// the ink surface lands (T7).
        /// </summary>
        public void SetHostActive(bool isActive)
        {
            // T7: forward to the ink surface / selection overlays.
        }

        /// <summary>
        /// WPF enabled/disabled document input during modal flows. No-op until
        /// the annotation surfaces exist (T7/T8).
        /// </summary>
        public void SetDocumentInputEnabled(bool enabled)
        {
            // T7/T8: forward to ink + overlay hit-testing.
        }

        /// <summary>
        /// Clears the transient PDF-text search highlight overlay. The layer
        /// itself is Task 8, but clearing is shell-safe now.
        /// </summary>
        public void ClearPdfTextSelection()
        {
            PdfTextSelectionCanvas.Children.Clear();
            PdfTextSelectionCanvas.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// T8: paints the text-bound rectangles produced by search hits.
        /// </summary>
        public void SetPdfTextSelectionRects(System.Collections.Generic.IReadOnlyList<Windows.Foundation.Rect> rects)
        {
            // T8: highlight rectangles on PdfTextSelectionCanvas.
        }

        /// <summary>
        /// T8: refreshes localized sticky-note context menus on this page.
        /// </summary>
        public void RefreshStickyNoteContextMenuLocalization()
        {
            // T8.
        }
    }
}
