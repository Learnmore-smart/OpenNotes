using System;
using Caelum.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Caelum.Controls
{
    /// <summary>
    /// V6 WinUI port of WPF <c>Controls/PdfPageControl.xaml.cs</c>: page
    /// frame, fixed DIP size, rendered bitmap slot, the custom
    /// <see cref="InkSurface"/> (Task 7 Phase A: pen/highlighter/eraser) and
    /// the named overlay canvases later tasks (T8/T9) attach to.
    ///
    /// Not ported (deferred):
    /// <list type="bullet">
    /// <item><c>SetBitmapScalingMode</c> — WPF toggled
    /// <c>RenderOptions.BitmapScalingMode</c> during scroll/zoom; WinUI
    /// images always sample at full quality so the hook is unnecessary.</item>
    /// <item>Selection, shapes, hidden ink, laser, ruler overlays — Task 7
    /// Phase B.</item>
    /// </list>
    /// </summary>
    public sealed partial class PdfPageControl : UserControl
    {
        private bool _hostActive = true;
        private bool _documentInputEnabled = true;

        public PdfPageControl()
        {
            InitializeComponent();

            // The eraser cursor lives in EraserCanvas (above the ink layer);
            // the surface moves/sizes it during erase gestures and hover.
            InkSurface.EraserIndicator = EraserIndicator;

            InkSurface.StrokeCollected += (s, stroke) =>
                StrokeCollected?.Invoke(this, stroke);
            InkSurface.StrokeRecognized += (s, e) =>
                StrokeRecognized?.Invoke(this, e);
            InkSurface.StrokesErased += (s, e) =>
                StrokesErased?.Invoke(this, e);
            InkSurface.InkMutated += (s, e) =>
                InkMutated?.Invoke(this, e);
        }

        /// <summary>Zero-based page index inside the loaded document.</summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// The custom ink surface (stroke store + pointer pipeline). EditorPage
        /// configures tool/colour/size fields directly.
        /// </summary>
        public InkSurface Ink => InkSurface;

        /// <summary>
        /// A user pen/highlighter stroke completed — the editor pushes the
        /// undo action. Never raised for quiet loads.
        /// </summary>
        public event EventHandler<InkStrokeData> StrokeCollected;

        /// <summary>
        /// A collected stroke was recognized as a shape and replaced by its
        /// ideal outline in the store — the editor pushes the
        /// <see cref="Caelum.Ink.InkStrokeReplacedAction"/> undo action.
        /// </summary>
        public event EventHandler<InkStrokeRecognizedEventArgs> StrokeRecognized;

        /// <summary>One erase gesture finished (net placements payload).</summary>
        public event EventHandler<InkStrokesErasedEventArgs> StrokesErased;

        /// <summary>Any visible ink change — hosts invalidate thumbnails.</summary>
        public event EventHandler InkMutated;

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
        /// Quiet annotation-load path: appends the stroke without raising
        /// <see cref="StrokeCollected"/> (no undo entry), mirroring the WPF
        /// loader which adds sidecar strokes outside history.
        /// </summary>
        public InkStrokeData AddStroke(StrokeAnnotation annotation)
            => InkSurface.AddStroke(annotation);

        /// <summary>
        /// Cancels in-flight ink gestures (discards a live stroke, rolls back
        /// a partial erase). Called on tool switch and page teardown.
        /// </summary>
        public void CancelInteraction() => InkSurface.CancelInteraction();

        /// <summary>
        /// WPF gated ink input on the active tab.
        /// </summary>
        public void SetHostActive(bool isActive)
        {
            _hostActive = isActive;
            ApplyInputGate();
        }

        /// <summary>
        /// WPF enabled/disabled document input during modal flows.
        /// </summary>
        public void SetDocumentInputEnabled(bool enabled)
        {
            _documentInputEnabled = enabled;
            ApplyInputGate();
        }

        private void ApplyInputGate()
        {
            var enabled = _hostActive && _documentInputEnabled;
            InkSurface.InputEnabled = enabled;
            if (!enabled)
                InkSurface.CancelInteraction();
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
