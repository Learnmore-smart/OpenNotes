using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Caelum.Ink;
using Caelum.InkGeometry;
using Caelum.Models;
using Caelum.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Caelum.Controls
{
    /// <summary>
    /// Input-mode mirror of the WPF page's CustomInkInputProcessingMode —
    /// selection lives outside this enum (SetSelectionMode) exactly like WPF.
    /// </summary>
    public enum CustomInkInputProcessingMode { None, Inking, Erasing, Shape, Laser, AreaHighlight, HiddenInk }

    /// <summary>Marquee shape — WPF SelectionShape parity.</summary>
    public enum SelectionShape { Rectangle, FreeForm }

    /// <summary>Which annotation kinds a marquee/click may pick up.</summary>
    public enum SelectionFilter { Both, DrawingsOnly, TextOnly }

    /// <summary>Selection state change — bounds are page-DIP coordinates.</summary>
    public sealed class AnnotationSelectionChangedEventArgs : EventArgs
    {
        public AnnotationSelectionChangedEventArgs(bool hasSelection, Rect bounds)
        {
            HasSelection = hasSelection;
            Bounds = bounds;
        }

        public bool HasSelection { get; }
        public Rect Bounds { get; }
    }

    /// <summary>
    /// Pointer payload for the PDF text-selection layer — position in
    /// page-DIP coordinates plus whether the primary button is held
    /// (WPF PdfTextSelectionPointerEventArgs.LeftButton parity).
    /// </summary>
    public sealed class PdfTextSelectionPointerEventArgs : EventArgs
    {
        public PdfTextSelectionPointerEventArgs(Point position, bool isLeftButtonPressed)
        {
            Position = position;
            IsLeftButtonPressed = isLeftButtonPressed;
        }

        public Point Position { get; }
        public bool IsLeftButtonPressed { get; }
    }

    /// <summary>A completed selection move gesture (strokes + text/sticky containers).</summary>
    public sealed class SelectionMoveCompletedEventArgs : EventArgs
    {
        public SelectionMoveCompletedEventArgs(
            double deltaX, double deltaY, IReadOnlyList<InkStrokeData> strokes,
            IReadOnlyList<Grid> containers = null)
        {
            DeltaX = deltaX;
            DeltaY = deltaY;
            Strokes = strokes ?? Array.Empty<InkStrokeData>();
            SelectedTextContainers = containers ?? Array.Empty<Grid>();
        }

        public double DeltaX { get; }
        public double DeltaY { get; }
        public IReadOnlyList<InkStrokeData> Strokes { get; }

        /// <summary>Text/sticky containers moved with the selection (WPF parity).</summary>
        public IReadOnlyList<Grid> SelectedTextContainers { get; }
    }

    /// <summary>A completed selection resize gesture (uniform scale + anchor).</summary>
    public sealed class SelectionResizeCompletedEventArgs : EventArgs
    {
        public SelectionResizeCompletedEventArgs(
            double totalScale, PointD anchor, IReadOnlyList<InkStrokeData> strokes,
            IReadOnlyList<Grid> containers = null)
        {
            TotalScale = totalScale;
            Anchor = anchor;
            Strokes = strokes ?? Array.Empty<InkStrokeData>();
            SelectedTextContainers = containers ?? Array.Empty<Grid>();
        }

        public double TotalScale { get; }
        public PointD Anchor { get; }
        public IReadOnlyList<InkStrokeData> Strokes { get; }
        public IReadOnlyList<Grid> SelectedTextContainers { get; }
    }

    /// <summary>A completed selection rotate gesture (total degrees + centre).</summary>
    public sealed class SelectionRotateCompletedEventArgs : EventArgs
    {
        public SelectionRotateCompletedEventArgs(
            double totalDegrees, PointD center, IReadOnlyList<InkStrokeData> strokes,
            IReadOnlyList<Grid> containers = null)
        {
            TotalDegrees = totalDegrees;
            Center = center;
            Strokes = strokes ?? Array.Empty<InkStrokeData>();
            SelectedTextContainers = containers ?? Array.Empty<Grid>();
        }

        public double TotalDegrees { get; }
        public PointD Center { get; }
        public IReadOnlyList<InkStrokeData> Strokes { get; }
        public IReadOnlyList<Grid> SelectedTextContainers { get; }
    }

    /// <summary>A sticky-note marker finished a move (drag or keyboard nudge).</summary>
    public sealed class StickyNoteMovedEventArgs : EventArgs
    {
        public StickyNoteMovedEventArgs(Grid container, PointD oldPosition, PointD newPosition)
        {
            Container = container;
            OldPosition = oldPosition;
            NewPosition = newPosition;
        }

        public Grid Container { get; }
        public PointD OldPosition { get; }
        public PointD NewPosition { get; }
    }

    /// <summary>
    /// Payload for <see cref="PdfPageControl.HiddenInksRemoved"/> — one erase
    /// gesture may remove several masks; the entries carry their pre-removal
    /// store indices so the undo action can restore them in place.
    /// </summary>
    public sealed class HiddenInksRemovedEventArgs : EventArgs
    {
        public HiddenInksRemovedEventArgs(
            IReadOnlyList<(HiddenInkAnnotation Item, int Index)> entries)
        {
            Entries = entries ?? Array.Empty<(HiddenInkAnnotation, int)>();
            Annotations = Entries.Select(e => e.Item).ToList();
        }

        public IReadOnlyList<(HiddenInkAnnotation Item, int Index)> Entries { get; }
        public IReadOnlyList<HiddenInkAnnotation> Annotations { get; }
    }

    /// <summary>Pre-gesture geometry for one selected container (WPF SelectionContainerSnapshot).</summary>
    internal sealed class SelectionContainerSnapshot
    {
        public Grid Container;
        public PointD Position;
        public double Width;
        public double Height;
        public double FontSize;
        public double RotationDegrees;
    }

    /// <summary>
    /// Per-selection-gesture stroke/container snapshot so a cancelled gesture
    /// (capture lost / Escape / tool switch) restores the exact pre-gesture
    /// geometry — WPF SelectionInteractionSnapshot parity.
    /// </summary>
    internal sealed class SelectionInteractionSnapshot
    {
        public readonly Dictionary<InkStrokeData, (List<InkPointData> Points, double Size)> Strokes =
            new(ReferenceEqualityComparer.Instance);
        public readonly List<SelectionContainerSnapshot> Containers = new();
    }

    /// <summary>
    /// V6 WinUI port of WPF <c>Controls/PdfPageControl.xaml.cs</c>: page
    /// frame, fixed DIP size, rendered bitmap slot, the custom
    /// <see cref="InkSurface"/> and the Phase-B overlay tools — lasso/marquee
    /// selection with move/resize/rotate, the drag-to-shape tool with live
    /// preview + ruler constraint, hidden-ink masks with timed reveal, and
    /// the ephemeral laser pointer.
    ///
    /// Not ported (later tasks):
    /// <list type="bullet">
    /// <item><c>SetBitmapScalingMode</c> — WPF toggled
    /// <c>RenderOptions.BitmapScalingMode</c>; WinUI always samples full
    /// quality.</item>
    /// </list>
    /// </summary>
    public sealed partial class PdfPageControl : UserControl, IAnnotationContainerHost
    {
        private bool _hostActive = true;
        private bool _documentInputEnabled = true;

        // ── Tool / mode state ────────────────────────────────────────────
        private CustomInkInputProcessingMode _currentMode = CustomInkInputProcessingMode.None;
        private bool _isSelectionMode;
        private SelectionShape _selectionShape = SelectionShape.Rectangle;
        private SelectionFilter _selectionFilter = SelectionFilter.Both;

        // ── Selection state (Phase B — strokes only; containers are T8) ──
        private readonly List<InkStrokeData> _selectedStrokes = new();
        private readonly List<Grid> _selectedTextContainers = new();
        private bool _isSelecting;
        private bool _isDraggingSelection;
        private bool _isResizingSelection;
        private bool _isRotatingSelection;
        private PointD _selectionStartPoint;
        private PointD _dragStartPoint;
        private PointD _resizeAnchorPoint;
        private PointD _rotateCenter;
        private double _rotateStartPointerAngle;
        private double _lastRotationDegrees;
        private double _totalRotationDegrees;
        private double _totalDragDeltaX;
        private double _totalDragDeltaY;
        private double _resizeStartHandleDist = 1.0;
        private double _lastResizeScale = 1.0;
        private int _resizeHandleIndex;
        private uint? _selectionPointerId;
        private SelectionInteractionSnapshot _selectionInteractionSnapshot;
        private List<PointD> _freeSelectionPoints;
        private Polyline _freeSelectionPath;
        private Rectangle _selectionRect;
        private readonly List<Rectangle> _perItemOutlines = new();

        // Marching-ants animation — WPF CompositionTarget.Rendering becomes a
        // DispatcherQueueTimer (~30 fps is plenty for a dash cue).
        private DispatcherQueueTimer _selectionDashTimer;
        private double _selectionDashOffset;
        private double _selectionColorPhaseSeconds;
        private DateTimeOffset _selectionDashLastTickUtc;
        private const double SelectionDashSpeed = 15.0;            // dash units per second
        private const double SelectionDashPatternPeriod = 5.0;     // 3 (dash) + 2 (gap)
        private const double SelectionColorHalfCycleSeconds = 1.5; // accent ↔ focus
        private static readonly DoubleCollection PerItemOutlineDashArray = new() { 3, 2 };

        // ── Shape state ──────────────────────────────────────────────────
        private bool _isShapeDragging;
        private PointD _shapeAnchor;
        private PointD _shapeCurrent;
        private bool _shapeShiftHeld;
        private readonly List<Polyline> _shapePreviewPolylines = new();

        // ── Area highlight (Task 8 Phase B) — same drag contract as the
        //    shape tool; the preview rect rides ShapePreviewCanvas and the
        //    committed annotation becomes an ImageOverlayCanvas container ──
        private bool _isAreaHighlightDragging;
        private PointD _areaHighlightAnchor;
        private Rectangle _areaHighlightPreview;

        /// <summary>Fill colour for the next area highlight (alpha applied internally).</summary>
        public Color AreaHighlightColor { get; set; } = Color.FromArgb(255, 0xFF, 0xEB, 0x3B);

        /// <summary>Fill alpha for the next area highlight (~30% default).</summary>
        public byte AreaHighlightOpacity { get; set; } = 76;

        /// <summary>WPF AreaHighlightDragThreshold — sub-4 DIP drags are taps.</summary>
        public const double AreaHighlightDragThreshold = 4.0;

        /// <summary>WPF NormalizeAreaHighlightRect — anchor/current → axis-aligned rect.</summary>
        public static Rect NormalizeAreaHighlightRect(PointD anchor, PointD current)
            => new(
                Math.Min(anchor.X, current.X),
                Math.Min(anchor.Y, current.Y),
                Math.Abs(current.X - anchor.X),
                Math.Abs(current.Y - anchor.Y));

        // ── Persistent text-quad highlights ──────────────────────────────
        private readonly List<HighlightAnnotation> _highlights = new();

        // ── PDF text selection (Task 8 Phase B) — the editor owns offset
        //    tracking; the page forwards pointer traffic and paints rects ──
        private bool _isPdfTextSelectionEnabled;
        private uint? _pdfTextSelectionPointerId;

        // ── Hidden ink ───────────────────────────────────────────────────
        private readonly Dictionary<string, Polyline> _hiddenInkVisuals = new();
        private readonly Dictionary<string, DispatcherQueueTimer> _hiddenInkRevealTimers = new();
        private bool _selectionVisualsDirty;
        private bool _selectionVisualsUpdateQueued;
        private List<(HiddenInkAnnotation Item, int Index)> _eraseGestureRemovedHiddenInks;

        // ── Laser ────────────────────────────────────────────────────────
        private Polyline _laserPolyline;
        private readonly List<Polyline> _liveLaserPolylines = new();
        private readonly Dictionary<Polyline, DateTimeOffset> _laserCompletedAt = new();
        private DispatcherQueueTimer _laserFadeTimer;

        // ── Overlay annotations ──────────────────────────────────────────
        private const string MarkupContainerTag = "textMarkup";
        private const string AreaHighlightContainerTag = "areaHighlight";
        private const string StickyNoteContainerTag = "stickyNote";
        private const string ImageContainerTag = "ImageAnnotation";

        /// <summary>Image containers (Grid with a child Image visual).</summary>
        private readonly List<Grid> _imageContainers = new();

        /// <summary>
        /// Image container → raw encoded bytes (page-local; WPF keeps the
        /// same container-keyed shape so a cross-page move transfers the
        /// payload by element reference).
        /// </summary>
        private readonly Dictionary<Grid, byte[]> _imageDataById = new();

        /// <summary>
        /// Persist-as-auto flags ride the container element itself (WPF
        /// attached DP parity) so a cross-page move keeps them without a
        /// page-side dictionary lookup.
        /// </summary>
        public static readonly DependencyProperty TextAnnotationAutoWidthProperty =
            DependencyProperty.RegisterAttached(
                "TextAnnotationAutoWidth",
                typeof(bool),
                typeof(PdfPageControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty TextAnnotationAutoHeightProperty =
            DependencyProperty.RegisterAttached(
                "TextAnnotationAutoHeight",
                typeof(bool),
                typeof(PdfPageControl),
                new PropertyMetadata(false));

        public static bool GetTextAnnotationAutoWidth(DependencyObject element)
            => element != null && (bool)element.GetValue(TextAnnotationAutoWidthProperty);

        public static void SetTextAnnotationAutoWidth(DependencyObject element, bool value)
            => element?.SetValue(TextAnnotationAutoWidthProperty, value);

        public static bool GetTextAnnotationAutoHeight(DependencyObject element)
            => element != null && (bool)element.GetValue(TextAnnotationAutoHeightProperty);

        public static void SetTextAnnotationAutoHeight(DependencyObject element, bool value)
            => element?.SetValue(TextAnnotationAutoHeightProperty, value);
        private readonly Dictionary<Grid, object> _overlayData = new();

        // ── Sticky-note marker drag state ────────────────────────────────
        private const double StickyMarkerSize = 36.0;
        private const double StickyDragThreshold = 3.0;
        private Grid _stickyDragContainer;
        private PointD _stickyDragStartPointer;
        private PointD _stickyDragStartPosition;
        private bool _stickyDragMoved;
        private uint? _stickyDragPointerId;
        private bool _suppressStickyCaptureCancellation;

        // ── Public configuration (EditorPage pushes these) ───────────────

        /// <summary>Active shape kind for <see cref="CustomInkInputProcessingMode.Shape"/>.</summary>
        public InkShapeKind CurrentShape { get; set; } = InkShapeKind.Line;

        /// <summary>Shape stroke colour (WPF default: black).</summary>
        public Color ShapeColor { get; set; } = Color.FromArgb(255, 0, 0, 0);

        /// <summary>Shape stroke width (WPF default 2.0).</summary>
        public double ShapeStrokeSize { get; set; } = 2.0;

        /// <summary>Committed shape is baked to dash segments.</summary>
        public bool ShapeIsDashed { get; set; }

        /// <summary>Hidden-ink mask colour (neutral study gray).</summary>
        public Color HiddenInkMaskColor { get; set; } = Color.FromArgb(255, 199, 205, 212);

        /// <summary>Hidden-ink mask stroke width (WPF HiddenInkSize = 28).</summary>
        public double HiddenInkSize { get; set; } = 28.0;

        /// <summary>Click-reveal duration for a mask (default 3000 ms).</summary>
        public int HiddenInkRevealDurationMs { get; set; } = HiddenInkRevealState.DefaultRevealDurationMs;

        /// <summary>The page's hidden-ink masks — ordered, Id-keyed, UI-free.</summary>
        public HiddenInkStore HiddenInkStore { get; } = new();

        /// <summary>
        /// Ruler hook supplied by the editor: returns the ruler's long-edge
        /// endpoints in THIS page's coordinate space, or null when hidden.
        /// Fed to the ink surface and consulted by the shape tool.
        /// </summary>
        public Func<(PointD TopA, PointD TopB, PointD BottomA, PointD BottomB)?> GetRulerGeometryInPageCoords { get; set; }

        public PdfPageControl()
        {
            InitializeComponent();

            // The eraser cursor lives in EraserCanvas (above the ink layer);
            // the surface moves/sizes it during erase gestures and hover.
            InkSurface.EraserIndicator = EraserIndicator;
            InkSurface.RulerGeometryProvider = () => GetRulerGeometryInPageCoords?.Invoke();
            InkSurface.HiddenInkColor = HiddenInkMaskColor;
            InkSurface.HiddenInkSize = HiddenInkSize;

            InkSurface.StrokeCollected += (s, stroke) =>
                StrokeCollected?.Invoke(this, stroke);
            InkSurface.StrokeRecognized += (s, e) =>
                StrokeRecognized?.Invoke(this, e);
            InkSurface.StrokesErased += (s, e) =>
                StrokesErased?.Invoke(this, e);
            InkSurface.InkMutated += (s, e) =>
            {
                // Selection visuals track live geometry — move/scale/rotate
                // and their undo rebuild the bounds rectangle and handles.
                // NotifyGeometryChanged raises one Mutated PER STROKE, so
                // coalesce the chrome rebuild to at most once per frame.
                if (HasSelection)
                    QueueSelectionVisualsUpdate();
                InkMutated?.Invoke(this, e);
            };

            // Phase B wiring — the surface owns the pointer pipeline; the page
            // owns preview/commit visuals on its own overlay canvases.
            InkSurface.HiddenInkStrokeCommitted += Ink_HiddenInkStrokeCommitted;
            InkSurface.EraserPathUpdated += Ink_EraserPathUpdated;
            InkSurface.EraseGestureCompleted += Ink_EraseGestureCompleted;
            InkSurface.EraseGestureCancelled += Ink_EraseGestureCancelled;
            InkSurface.ShapeDragStarted += Ink_ShapeDragStarted;
            InkSurface.ShapeDragUpdated += Ink_ShapeDragUpdated;
            InkSurface.ShapeDragEnded += Ink_ShapeDragEnded;
            InkSurface.ShapeDragCancelled += Ink_ShapeDragCancelled;
            InkSurface.LaserStrokeStarted += Ink_LaserStrokeStarted;
            InkSurface.LaserStrokePointsAppended += Ink_LaserStrokePointsAppended;
            InkSurface.LaserStrokeCompleted += Ink_LaserStrokeCompleted;

            SelectionOverlayCanvas.PointerPressed += SelectionOverlay_PointerPressed;
            SelectionOverlayCanvas.PointerMoved += SelectionOverlay_PointerMoved;
            SelectionOverlayCanvas.PointerReleased += SelectionOverlay_PointerReleased;
            SelectionOverlayCanvas.PointerCanceled += SelectionOverlay_PointerCanceled;
            SelectionOverlayCanvas.PointerCaptureLost += SelectionOverlay_PointerCaptureLost;
            SelectionOverlayCanvas.PointerExited += SelectionOverlay_PointerExited;
            SelectionOverlayCanvas.DoubleTapped += SelectionOverlay_DoubleTapped;
            SelectionOverlayCanvas.RightTapped += SelectionOverlay_RightTapped;

            // Task 8: text-overlay background presses (create/deselect) and
            // page-background presses (sticky placement / deselect) — WPF
            // TextOverlayCanvas_MouseDown + PageGrid_MouseDown parity.
            TextOverlayCanvas.PointerPressed += TextOverlayCanvas_PointerPressed;
            PageGrid.PointerPressed += PageGrid_PointerPressed;

            // Task 8 Phase B: PDF text selection — the layer is only armed
            // (visible + hit-testable) while the editor enables it; pointer
            // traffic is forwarded so the editor owns the offset math.
            PdfTextSelectionCanvas.PointerPressed += PdfTextSelectionCanvas_PointerPressed;
            PdfTextSelectionCanvas.PointerMoved += PdfTextSelectionCanvas_PointerMoved;
            PdfTextSelectionCanvas.PointerReleased += PdfTextSelectionCanvas_PointerReleased;
            PdfTextSelectionCanvas.PointerCanceled += PdfTextSelectionCanvas_PointerCanceled;
            PdfTextSelectionCanvas.PointerCaptureLost += PdfTextSelectionCanvas_PointerCaptureLost;

            HiddenInkStore.Changed += (s, e) =>
            {
                // Incremental sync: a single Added/Removed mask updates one
                // visual (reveal timers + hit-test state on the others stay
                // untouched); Cleared/unknown kinds rebuild the layer.
                switch (e?.Kind)
                {
                    case InkStoreMutationKind.Added when e.Item != null:
                        AddHiddenInkVisual(e.Item, e.Index);
                        break;
                    case InkStoreMutationKind.Removed when e.Item != null:
                        // WPF RemoveHiddenInkQuiet parity — a removed mask's
                        // reveal timer must not keep ticking on a dead visual.
                        StopHiddenInkRevealTimer(e.Item.Id);
                        if (_hiddenInkVisuals.TryGetValue(e.Item.Id, out var dead))
                        {
                            HiddenInkCanvas.Children.Remove(dead);
                            _hiddenInkVisuals.Remove(e.Item.Id);
                        }
                        break;
                    case InkStoreMutationKind.Cleared:
                        StopAllHiddenInkRevealTimers();
                        RebuildHiddenInkVisuals();
                        break;
                    default:
                        RebuildHiddenInkVisuals();
                        break;
                }
            };

            // WPF HiddenInkCanvas ClipToBounds="True" parity — WinUI Canvas
            // has no ClipToBounds member, so the clip rect tracks the
            // element size directly (masks drawn off-page must not bleed
            // into neighbouring pages/chrome).
            HiddenInkCanvas.SizeChanged += (s, e) =>
            {
                HiddenInkCanvas.Clip = new RectangleGeometry
                {
                    Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
                };
            };
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
        /// <see cref="Ink.InkStrokeReplacedAction"/> undo action.
        /// </summary>
        public event EventHandler<InkStrokeRecognizedEventArgs> StrokeRecognized;

        /// <summary>One erase gesture finished (net placements payload).</summary>
        public event EventHandler<InkStrokesErasedEventArgs> StrokesErased;

        /// <summary>Any visible ink change — hosts invalidate thumbnails.</summary>
        public event EventHandler InkMutated;

        /// <summary>Selection appeared/changed/cleared — the editor tracks the active page.</summary>
        public event EventHandler<AnnotationSelectionChangedEventArgs> SelectionChanged;

        /// <summary>A selection move gesture completed (total delta, stroke refs).</summary>
        public event EventHandler<SelectionMoveCompletedEventArgs> SelectionMoveCompleted;

        /// <summary>A selection resize gesture completed (total scale + anchor).</summary>
        public event EventHandler<SelectionResizeCompletedEventArgs> SelectionResizeCompleted;

        /// <summary>A selection rotate gesture completed (total degrees + centre).</summary>
        public event EventHandler<SelectionRotateCompletedEventArgs> SelectionRotateCompleted;

        /// <summary>
        /// A shape drag committed its strokes into the store — the editor
        /// pushes one undo action covering the whole group, then switches to
        /// Select with the new shape selected (WPF ShapeCommittedUndoable).
        /// </summary>
        public event EventHandler<IReadOnlyList<InkStrokeData>> ShapeCommittedUndoable;

        /// <summary>A hidden-ink mask was created (drawn and committed).</summary>
        public event EventHandler<HiddenInkAnnotation> HiddenInkCreated;

        /// <summary>One erase gesture removed masks (batch payload for undo).</summary>
        public event EventHandler<HiddenInksRemovedEventArgs> HiddenInksRemoved;

        /// <summary>Right-tap / double-tap on empty page — editor shows the page menu.</summary>
        public event EventHandler BlankContextRequested;

        // ── Task 8 Phase A: text/sticky overlay events ───────────────────

        /// <summary>
        /// Press on the <see cref="TextOverlayCanvas"/> background while the
        /// Text tool is armed — the editor creates/deselects a text box (WPF
        /// TextOverlayPointerPressed). The event forwards the routed args so
        /// the editor can mark <see cref="PointerRoutedEventArgs.Handled"/>.
        /// </summary>
        public event EventHandler<PointerRoutedEventArgs> TextOverlayPointerPressed;

        /// <summary>
        /// Press on the page background (not inside the text overlay) while
        /// the ink surface idles — the editor uses it for sticky-note
        /// placement and text deselection (WPF BackgroundPointerPressed).
        /// </summary>
        public event EventHandler<PointerRoutedEventArgs> BackgroundPointerPressed;

        /// <summary>
        /// The image/markup/area overlay set changed — the editor marks the
        /// document dirty (WPF ImagesChanged; it also fires for markup/area
        /// containers since they share the image pipeline there).
        /// </summary>
        public event EventHandler ImagesChanged;

        /// <summary>
        /// An area-highlight drag committed — the container payload is the
        /// created Grid; the editor pushes the undo action and selects it
        /// (WPF AreaHighlightCreated).
        /// </summary>
        public event EventHandler<Grid> AreaHighlightCreated;

        /// <summary>
        /// PDF text-selection layer pointer traffic — the editor owns the
        /// offset math and raises these only while the layer is armed
        /// (WPF PdfTextSelectionPointerPressed/Moved/Released).
        /// </summary>
        public event EventHandler<PdfTextSelectionPointerEventArgs> PdfTextSelectionPointerPressed;
        public event EventHandler<PdfTextSelectionPointerEventArgs> PdfTextSelectionPointerMoved;
        public event EventHandler<PdfTextSelectionPointerEventArgs> PdfTextSelectionPointerReleased;

        /// <summary>Sticky marker tapped or Enter/Space-activated — open the editor popup.</summary>
        public event EventHandler<Grid> StickyNoteActivated;

        /// <summary>Sticky marker finished a move (drag threshold crossed or keyboard nudge).</summary>
        public event EventHandler<StickyNoteMovedEventArgs> StickyNoteMoved;

        /// <summary>Delete requested via context menu or the Delete key.</summary>
        public event EventHandler<Grid> StickyNoteDeleteRequested;

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
        public void CancelInteraction()
        {
            InkSurface.CancelInteraction();
            CancelSelectionInteraction(restoreSnapshot: true);
            // WPF InteractionCancellation.CancelAll covers the marker drag
            // too — a captured sticky gesture is still an interaction.
            CancelStickyDrag();
            ClearShapePreview();
            // WPF CancelAll parity: drop an in-flight area-highlight drag and
            // release the PDF text-selection capture; the transient rects go
            // away with the gesture (the editor rebuilds if it keeps state).
            if (_isAreaHighlightDragging)
            {
                _isAreaHighlightDragging = false;
                ClearAreaHighlightPreview();
            }
            if (_pdfTextSelectionPointerId != null)
            {
                PdfTextSelectionCanvas.ReleasePointerCaptures();
                _pdfTextSelectionPointerId = null;
            }
        }

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
            // WPF parity: in None mode the ink surface must not intercept
            // presses — sticky-note markers live on the layer below it.
            InkSurface.IsHitTestVisible = enabled && _currentMode != CustomInkInputProcessingMode.None;
            UpdateHiddenInkHitTesting();
            if (!enabled)
            {
                InkSurface.CancelInteraction();
                CancelSelectionInteraction(restoreSnapshot: true);
                CancelStickyDrag();
                // Hidden tabs keep no ticking ants (WPF SetHostActive parity).
                StopSelectionDashTimer();
            }
            else if (HasSelection)
            {
                // StopSelectionDashTimer cleared the per-item outlines —
                // rebuild the chrome once on reactivation.
                UpdateSelectionVisuals();
            }
        }

        /// <summary>
        /// Arms/disarms the PDF text-selection layer (WPF
        /// SetPdfTextSelectionEnabled): while armed the canvas is visible and
        /// hit-testable and forwards pointer traffic to the editor; disarming
        /// releases capture and drops the painted rects. The ink surface is
        /// already hit-test transparent in the modes that enable this
        /// (None / TextHighlight), so no extra gating is needed here.
        /// </summary>
        public void SetPdfTextSelectionEnabled(bool enabled)
        {
            _isPdfTextSelectionEnabled = enabled;
            PdfTextSelectionCanvas.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            PdfTextSelectionCanvas.IsHitTestVisible = enabled;

            if (!enabled)
            {
                if (_pdfTextSelectionPointerId != null)
                {
                    PdfTextSelectionCanvas.ReleasePointerCaptures();
                    _pdfTextSelectionPointerId = null;
                }
                ClearPdfTextSelection();
            }
        }

        /// <summary>
        /// Paints the merged selection rectangles (active drag or search-hit
        /// highlight) on <see cref="PdfTextSelectionCanvas"/> — WPF
        /// SetPdfTextSelectionRects: rounded rects filled with the theme
        /// selection brush at 45% opacity.
        /// </summary>
        public void SetPdfTextSelectionRects(IReadOnlyList<Rect> rects)
        {
            PdfTextSelectionCanvas.Children.Clear();
            if (rects == null)
                return;

            var fill = ResolveSelectionBrush();
            foreach (var rect in rects)
            {
                if (rect.Width <= 0 || rect.Height <= 0)
                    continue;

                var highlight = new Rectangle
                {
                    Width = rect.Width,
                    Height = rect.Height,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = fill,
                    Opacity = 0.45,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(highlight, rect.X);
                Canvas.SetTop(highlight, rect.Y);
                PdfTextSelectionCanvas.Children.Add(highlight);
            }
        }

        /// <summary>Clears the painted selection/search rectangles (WPF ClearPdfTextSelection).</summary>
        public void ClearPdfTextSelection()
        {
            PdfTextSelectionCanvas.Children.Clear();
        }

        private Brush ResolveSelectionBrush()
            => Application.Current.Resources.TryGetValue("ThemeSelectionBrush", out var resource)
                && resource is Brush themed
                ? themed
                : new SolidColorBrush(Color.FromArgb(255, 0x25, 0x63, 0xEB));

        private void PdfTextSelectionCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPdfTextSelectionEnabled)
                return;
            var point = e.GetCurrentPoint(PdfTextSelectionCanvas);
            // Only the primary button begins a selection drag; a second
            // pointer while one is active is ignored (single-gesture layer).
            if (!point.Properties.IsLeftButtonPressed || _pdfTextSelectionPointerId != null)
                return;
            if (!PdfTextSelectionCanvas.CapturePointer(e.Pointer))
                return;
            _pdfTextSelectionPointerId = e.Pointer.PointerId;
            PdfTextSelectionPointerPressed?.Invoke(this,
                new PdfTextSelectionPointerEventArgs(ToPagePoint(point.Position), true));
            e.Handled = true;
        }

        private void PdfTextSelectionCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPdfTextSelectionEnabled)
                return;
            var point = e.GetCurrentPoint(PdfTextSelectionCanvas);
            bool pressed = point.Properties.IsLeftButtonPressed
                || (e.Pointer.PointerDeviceType == PointerDeviceType.Pen && point.IsInContact);
            PdfTextSelectionPointerMoved?.Invoke(this,
                new PdfTextSelectionPointerEventArgs(ToPagePoint(point.Position), pressed));
            if (_pdfTextSelectionPointerId == e.Pointer.PointerId)
                e.Handled = true;
        }

        private void PdfTextSelectionCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPdfTextSelectionEnabled)
                return;
            var point = e.GetCurrentPoint(PdfTextSelectionCanvas);
            if (_pdfTextSelectionPointerId == e.Pointer.PointerId)
            {
                _pdfTextSelectionPointerId = null;
                PdfTextSelectionCanvas.ReleasePointerCaptures();
            }
            PdfTextSelectionPointerReleased?.Invoke(this,
                new PdfTextSelectionPointerEventArgs(ToPagePoint(point.Position), false));
            e.Handled = true;
        }

        private void PdfTextSelectionCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_pdfTextSelectionPointerId == e.Pointer.PointerId)
            {
                _pdfTextSelectionPointerId = null;
                PdfTextSelectionCanvas.ReleasePointerCaptures();
            }
        }

        private void PdfTextSelectionCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_pdfTextSelectionPointerId == e.Pointer.PointerId)
                _pdfTextSelectionPointerId = null;
        }

        /// <summary>Canvas-space → PageGrid-space point (identity today; kept for the WPF GetPosition(PageGrid) contract).</summary>
        private Point ToPagePoint(Point position) => position;

        /// <summary>
        /// Refreshes marker automation labels + context-flyout item text
        /// after a language change (WPF RefreshStickyNoteContextMenuLocalization).
        /// </summary>
        public void RefreshStickyNoteContextMenuLocalization()
        {
            foreach (var container in GetOverlayContainers().Where(IsStickyNoteContainer))
            {
                string stickyLabel = LocalizationService.Get("Editor.StickyNoteTooltip");
                AutomationProperties.SetName(container, stickyLabel);
                AutomationProperties.SetHelpText(container, stickyLabel);
                var flyout = container.ContextFlyout as MenuFlyout
                    ?? container.Children.OfType<Button>().FirstOrDefault()?.ContextFlyout as MenuFlyout;
                if (flyout?.Items.OfType<MenuFlyoutItem>().FirstOrDefault() is not MenuFlyoutItem delete)
                    continue;

                string label = LocalizationService.Get("Editor.DeleteTooltip");
                delete.Text = label;
                AutomationProperties.SetName(delete, label);
                AutomationProperties.SetHelpText(delete, label);
            }
        }

        // ==================================================================
        // Tool plumbing (Phase B)
        // ==================================================================

        /// <summary>
        /// Mirrors the WPF SetInputMode: maps the editor-level mode onto the
        /// ink surface tool. Inking leaves <see cref="InkSurface.Tool"/> alone
        /// (the caller sets Pen/Highlighter first); AreaHighlight rides the
        /// surface's shape-drag pipeline — the shape-drag handlers below
        /// route to the area-highlight path while this mode is armed.
        /// </summary>
        public void SetInputMode(CustomInkInputProcessingMode mode)
        {
            _currentMode = mode;

            // Sync the mask attributes the surface's hidden-ink pipeline and
            // brush indicator consult.
            InkSurface.HiddenInkColor = HiddenInkMaskColor;
            InkSurface.HiddenInkSize = HiddenInkSize;

            InkSurface.Tool = mode switch
            {
                CustomInkInputProcessingMode.Erasing => InkSurfaceTool.Eraser,
                CustomInkInputProcessingMode.HiddenInk => InkSurfaceTool.HiddenInk,
                CustomInkInputProcessingMode.Laser => InkSurfaceTool.Laser,
                CustomInkInputProcessingMode.Shape => InkSurfaceTool.Shape,
                CustomInkInputProcessingMode.AreaHighlight => InkSurfaceTool.AreaHighlight,
                CustomInkInputProcessingMode.Inking
                    => InkSurface.Tool is InkSurfaceTool.Pen or InkSurfaceTool.Highlighter
                        ? InkSurface.Tool
                        : InkSurfaceTool.Pen,
                _ => InkSurfaceTool.None,
            };

            UpdateHiddenInkHitTesting();
            // WPF parity: a passive ink surface is hit-test transparent so
            // sticky markers / page background / the PDF text-selection layer
            // receive the press.
            InkSurface.IsHitTestVisible =
                _hostActive && _documentInputEnabled
                && mode != CustomInkInputProcessingMode.None;
            if (mode != CustomInkInputProcessingMode.Shape)
                ClearShapePreview();
            if (mode != CustomInkInputProcessingMode.AreaHighlight && _isAreaHighlightDragging)
            {
                _isAreaHighlightDragging = false;
                ClearAreaHighlightPreview();
            }
            if (mode != CustomInkInputProcessingMode.Laser && _laserPolyline != null)
                EndLaserStroke();
        }

        /// <summary>
        /// WPF SetSelectionMode parity: toggles the overlay's visibility and
        /// hit-testing and restores/cancels an in-flight selection gesture.
        /// Does NOT touch the ink surface — the editor pairs this with
        /// SetInputMode(None) for the Select tool.
        /// </summary>
        public void SetSelectionMode(bool enabled)
        {
            if (_isSelectionMode == enabled)
                return;
            _isSelectionMode = enabled;

            if (enabled)
            {
                SelectionOverlayCanvas.Visibility = Visibility.Visible;
                SelectionOverlayCanvas.IsHitTestVisible = true;
            }
            else
            {
                CancelSelectionInteraction(restoreSnapshot: true);
                ClearSelection();
                SelectionOverlayCanvas.IsHitTestVisible = false;
                SelectionOverlayCanvas.Visibility = Visibility.Collapsed;
                ProtectedCursor = null;
            }
        }

        public void SetSelectionShape(SelectionShape shape) => _selectionShape = shape;
        public void SetSelectionFilter(SelectionFilter filter) => _selectionFilter = filter;

        /// <summary>
        /// Stops reveal timers, fades and in-flight gestures — teardown path
        /// called by the editor before unloading/recycling the page.
        /// </summary>
        public void ReleaseResources()
        {
            CancelInteraction();
            CancelStickyDrag();
            StopAllHiddenInkRevealTimers();
            StopSelectionDashTimer();
            _laserFadeTimer?.Stop();
            _laserFadeTimer = null;
            LaserInkCanvas.Children.Clear();
            _liveLaserPolylines.Clear();
            _laserCompletedAt.Clear();
            _laserPolyline = null;
            _selectedTextContainers.Clear();
            _overlayData.Clear();
            _imageContainers.Clear();
            _imageDataById.Clear();
            _highlights.Clear();
            HighlightsCanvas.Children.Clear();
            PdfTextSelectionCanvas.Children.Clear();
        }

        // ==================================================================
        // Selection (strokes + text/sticky overlay containers)
        // ==================================================================

        /// <summary>The text overlay canvas — the editor's text containers live here (WPF TextOverlay).</summary>
        public Canvas TextOverlay => TextOverlayCanvas;

        /// <summary>
        /// Text annotations are only directly interactive while the Text tool
        /// is armed; every other mode lets input fall through to the
        /// drawing/selection layers underneath (WPF SetMode).
        /// </summary>
        public void SetMode(bool isTextMode)
        {
            TextOverlayCanvas.IsHitTestVisible = isTextMode;
            TextOverlayCanvas.Background = isTextMode
                ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
                : null;
        }

        /// <summary>True while any stroke or container is selected.</summary>
        public bool HasSelection => _selectedStrokes.Count > 0 || _selectedTextContainers.Count > 0;

        /// <summary>The selected strokes (group-expanded) in z-order.</summary>
        public IReadOnlyList<InkStrokeData> SelectedStrokes => _selectedStrokes;

        /// <summary>The selected text/sticky containers (WPF SelectedTextContainers).</summary>
        public List<Grid> SelectedTextContainers => _selectedTextContainers;

        /// <summary>Union of the selected items' rendered bounds (rotation-aware).</summary>
        public Rect GetSelectionBounds()
        {
            if (_selectedStrokes.Count == 0 && _selectedTextContainers.Count == 0)
                return Rect.Empty;

            var bounds = Rect.Empty;
            if (_selectedStrokes.Count > 0)
            {
                var strokeBounds = StrokeGeometry.GetSelectionBounds(_selectedStrokes);
                bounds = new Rect(strokeBounds.X, strokeBounds.Y, strokeBounds.Width, strokeBounds.Height);
            }

            foreach (var container in _selectedTextContainers)
            {
                var rect = GetContainerAxisAlignedBounds(container);
                if (bounds.IsEmpty)
                    bounds = rect;
                else
                    bounds.Union(rect);
            }
            return bounds;
        }

        /// <summary>
        /// Bulk-select items (shape groups expanded). Empty input falls
        /// through to ClearSelection — WPF SelectItems parity.
        /// </summary>
        public void SelectItems(IEnumerable<InkStrokeData> strokes, IEnumerable<Grid> containers = null)
        {
            _selectedStrokes.Clear();
            _selectedTextContainers.Clear();
            if (strokes != null)
            {
                foreach (var stroke in strokes)
                {
                    if (stroke == null)
                        continue;
                    foreach (var logical in GetLogicalShapeStrokes(stroke))
                    {
                        if (!_selectedStrokes.Contains(logical))
                            _selectedStrokes.Add(logical);
                    }
                }
            }
            if (containers != null)
            {
                foreach (var container in containers)
                {
                    if (container != null && !_selectedTextContainers.Contains(container))
                        _selectedTextContainers.Add(container);
                }
            }
            RefreshSelectionAfterToggle();
        }

        /// <summary>Selects every annotation on the page (Ctrl+A) — ink, text and overlay containers.</summary>
        public void SelectAllAnnotations()
        {
            var containers = TextOverlayCanvas.Children.OfType<Grid>()
                .Concat(GetOverlayContainers())
                .ToList();
            SelectItems(InkSurface.Store.Strokes, containers);
        }

        /// <summary>Clears the selection and its overlay visuals.</summary>
        public void ClearSelection()
        {
            _selectedStrokes.Clear();
            _selectedTextContainers.Clear();
            _isSelecting = false;
            _freeSelectionPath = null;
            _freeSelectionPoints = null;
            _selectionRect = null;
            _selectionInteractionSnapshot = null;
            SelectionOverlayCanvas.Children.Clear();
            _perItemOutlines.Clear();
            StopSelectionDashTimer();
            SelectionChanged?.Invoke(this,
                new AnnotationSelectionChangedEventArgs(false, Rect.Empty));
        }

        /// <summary>
        /// Every store stroke sharing this stroke's ShapeGroupId (itself
        /// included) — or just the stroke when it is ordinary ink.
        /// </summary>
        private List<InkStrokeData> GetLogicalShapeStrokes(InkStrokeData stroke)
        {
            if (stroke == null || string.IsNullOrWhiteSpace(stroke.ShapeGroupId))
                return stroke == null ? new List<InkStrokeData>() : new List<InkStrokeData> { stroke };
            return InkSurface.Store.Strokes
                .Where(s => string.Equals(s.ShapeGroupId, stroke.ShapeGroupId, StringComparison.Ordinal))
                .ToList();
        }

        /// <summary>
        /// The WPF HitStroke port: a diameter-8 point hit against the rendered
        /// stroke (≈ spine within 4px + max rendered half-width), else the
        /// closed-shape-interior / open-bounds rule.
        /// </summary>
        private bool HitStroke(InkStrokeData stroke, PointD point)
        {
            if (stroke?.Points == null || stroke.Points.Count == 0)
                return false;
            double halfWidth = stroke.Points.Max(
                p => StrokeGeometry.GetRenderedStrokeHalfWidth(stroke.Size, (float)p.Pressure));
            if (StrokeGeometry.HitTestStroke(stroke.Points, point, halfWidth + 4.0))
                return true;
            return StrokeGeometry.HitTestClosedOrBounds(
                stroke.Points.Select(p => new PointD(p.X, p.Y)).ToList(),
                StrokeGeometry.GetSpineBounds(stroke.Points, halfWidth),
                point);
        }

        // ── Selection snapshot (cancel restores pre-gesture geometry) ────

        private void CaptureSelectionInteractionSnapshot()
        {
            if (_selectionInteractionSnapshot != null)
                return;
            var snapshot = new SelectionInteractionSnapshot();
            foreach (var stroke in _selectedStrokes)
            {
                if (stroke == null)
                    continue;
                snapshot.Strokes[stroke] = (new List<InkPointData>(stroke.Points), stroke.Size);
            }
            foreach (var container in _selectedTextContainers)
            {
                if (container == null)
                    continue;
                var left = Canvas.GetLeft(container);
                var top = Canvas.GetTop(container);
                snapshot.Containers.Add(new SelectionContainerSnapshot
                {
                    Container = container,
                    Position = new PointD(
                        double.IsNaN(left) ? 0 : left,
                        double.IsNaN(top) ? 0 : top),
                    Width = double.IsNaN(container.Width) ? double.NaN : container.Width,
                    Height = double.IsNaN(container.Height) ? double.NaN : container.Height,
                    FontSize = container.Children.OfType<TextBox>().FirstOrDefault()?.FontSize
                        ?? double.NaN,
                    RotationDegrees = ReadAnnotationRotation(container),
                });
            }
            _selectionInteractionSnapshot = snapshot;
        }

        private void RestoreSelectionInteractionSnapshot()
        {
            var snapshot = _selectionInteractionSnapshot;
            if (snapshot == null)
                return;
            var restored = new List<InkStrokeData>();
            foreach (var pair in snapshot.Strokes)
            {
                var stroke = pair.Key;
                if (stroke == null)
                    continue;
                stroke.Points = new List<InkPointData>(pair.Value.Points);
                stroke.Size = pair.Value.Size;
                restored.Add(stroke);
            }
            if (restored.Count > 0)
                InkSurface.Store.NotifyGeometryChanged(restored);

            foreach (var item in snapshot.Containers)
            {
                var container = item.Container;
                if (container == null)
                    continue;

                if (IsStickyNoteContainer(container))
                    SetStickyNotePositionQuiet(container, item.Position);
                else
                {
                    Canvas.SetLeft(container, item.Position.X);
                    Canvas.SetTop(container, item.Position.Y);
                }

                if (!double.IsNaN(item.Width) && item.Width > 0)
                    container.Width = item.Width;
                if (!double.IsNaN(item.Height) && item.Height > 0)
                    container.Height = item.Height;
                var textBox = container.Children.OfType<TextBox>().FirstOrDefault();
                if (textBox != null && !double.IsNaN(item.FontSize) && item.FontSize > 0)
                    textBox.FontSize = item.FontSize;
                ApplyAnnotationRotation(container, item.RotationDegrees);
            }
        }

        private void CancelSelectionInteraction(bool restoreSnapshot)
        {
            bool active = _isSelecting || _isDraggingSelection || _isResizingSelection
                || _isRotatingSelection || _selectionInteractionSnapshot != null
                || _selectionPointerId != null;
            if (!active)
                return;

            if (restoreSnapshot)
                RestoreSelectionInteractionSnapshot();

            _isSelecting = false;
            _isDraggingSelection = false;
            _isResizingSelection = false;
            _isRotatingSelection = false;
            _selectionPointerId = null;
            // Programmatic cancels must drop the pointer capture too — a
            // captured overlay keeps routing the stream to a dead gesture
            // (PointerCaptureLost re-entry is a no-op: the id is already
            // null by this point).
            SelectionOverlayCanvas.ReleasePointerCaptures();
            _lastResizeScale = 1.0;
            _lastRotationDegrees = 0;
            _totalRotationDegrees = 0;
            _totalDragDeltaX = 0;
            _totalDragDeltaY = 0;
            _freeSelectionPath = null;
            _freeSelectionPoints = null;
            _selectionRect = null;
            _selectionInteractionSnapshot = null;
            SetPageZIndex(0);
            SelectionOverlayCanvas.Children.Clear();
            UpdateSelectionVisuals();
        }

        /// <summary>Keep the dragged page above its neighbours during a gesture.</summary>
        private void SetPageZIndex(int z)
        {
            if (Parent is UIElement parent)
                Canvas.SetZIndex(parent, z);
        }

        // ── Selection overlay pointer pipeline ───────────────────────────

        private void SelectionOverlay_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isSelectionMode || !_hostActive || !_documentInputEnabled)
                return;

            var point = e.GetCurrentPoint(SelectionOverlayCanvas);
            var device = point.PointerDeviceType;
            if (device != PointerDeviceType.Mouse && device != PointerDeviceType.Pen)
                return;
            if (!point.Properties.IsLeftButtonPressed)
                return;
            if (_selectionPointerId != null)
            {
                e.Handled = true;
                return;
            }

            var pos = new PointD(point.Position.X, point.Position.Y);

            // Ctrl+click (mouse only) toggles the topmost item — WPF parity.
            if (device == PointerDeviceType.Mouse
                && (e.KeyModifiers & VirtualKeyModifiers.Control) != 0)
            {
                HandleCtrlClickToggle(pos);
                e.Handled = true;
                return;
            }

            if (HasSelection)
            {
                var unionBounds = GetSelectionBounds();
                var bounds = new RectD(unionBounds.X, unionBounds.Y, unionBounds.Width, unionBounds.Height);
                var rotateHandle = StrokeGeometry.GetRotateHandlePoint(bounds);
                if (new RectD(rotateHandle.X - 10, rotateHandle.Y - 10, 20, 20).Contains(pos))
                {
                    CaptureSelectionInteractionSnapshot();
                    _isRotatingSelection = true;
                    _rotateCenter = new PointD(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                    _rotateStartPointerAngle =
                        Math.Atan2(pos.Y - _rotateCenter.Y, pos.X - _rotateCenter.X) * 180.0 / Math.PI;
                    _lastRotationDegrees = 0;
                    _totalRotationDegrees = 0;
                    CaptureSelectionPointer(e);
                    SetPageZIndex(999);
                    e.Handled = true;
                    return;
                }

                if (StrokeGeometry.TryGetResizeHandleIndex(bounds, pos, out int handleIndex))
                {
                    CaptureSelectionInteractionSnapshot();
                    _isResizingSelection = true;
                    _resizeHandleIndex = handleIndex;
                    _resizeAnchorPoint = StrokeGeometry.GetOppositeCorner(bounds, handleIndex);
                    var handleRect = StrokeGeometry.GetSelectionCornerHandleRects(bounds)[handleIndex];
                    _resizeStartHandleDist = StrokeGeometry.Dist(
                        new PointD(handleRect.X + handleRect.Width / 2, handleRect.Y + handleRect.Height / 2),
                        _resizeAnchorPoint);
                    if (_resizeStartHandleDist < 1.0)
                        _resizeStartHandleDist = 1.0;
                    _lastResizeScale = 1.0;
                    CaptureSelectionPointer(e);
                    SetPageZIndex(999);
                    e.Handled = true;
                    return;
                }

                var inflated = bounds.Inflated(8, 8);
                if (inflated.Contains(pos))
                {
                    CaptureSelectionInteractionSnapshot();
                    _isDraggingSelection = true;
                    _dragStartPoint = pos;
                    _totalDragDeltaX = 0;
                    _totalDragDeltaY = 0;
                    CaptureSelectionPointer(e);
                    SetPageZIndex(999);
                    e.Handled = true;
                    return;
                }
            }

            ClearSelection();
            _isSelecting = true;
            _selectionStartPoint = pos;
            CaptureSelectionPointer(e);
            SetPageZIndex(999);

            if (_selectionShape == SelectionShape.FreeForm)
            {
                _freeSelectionPoints = new List<PointD> { pos };
                _freeSelectionPath = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    IsHitTestVisible = false,
                };
                _freeSelectionPath.Points.Add(new Point(pos.X, pos.Y));
                SelectionOverlayCanvas.Children.Add(_freeSelectionPath);
            }
            else
            {
                _selectionRect = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 212)),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(_selectionRect, pos.X);
                Canvas.SetTop(_selectionRect, pos.Y);
                SelectionOverlayCanvas.Children.Add(_selectionRect);
            }
            e.Handled = true;
        }

        private void CaptureSelectionPointer(PointerRoutedEventArgs e)
        {
            SelectionOverlayCanvas.CapturePointer(e.Pointer);
            _selectionPointerId = e.Pointer.PointerId;
        }

        private void SelectionOverlay_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var current = e.GetCurrentPoint(SelectionOverlayCanvas);
            var pos = new PointD(current.Position.X, current.Position.Y);

            if (_selectionPointerId == null || e.Pointer.PointerId != _selectionPointerId.Value)
            {
                if (_isSelectionMode && HasSelection && !_isDraggingSelection
                    && !_isResizingSelection && !_isRotatingSelection && !_isSelecting)
                {
                    UpdateHoverCursor(pos);
                }
                return;
            }

            if (_isResizingSelection)
            {
                var dist = StrokeGeometry.Dist(_resizeAnchorPoint, pos);
                if (dist < 1.0) dist = 1.0;
                var totalScale = dist / _resizeStartHandleDist;
                if (totalScale < 0.01) totalScale = 0.01;
                var deltaScale = totalScale / _lastResizeScale;
                _lastResizeScale = totalScale;
                ScaleSelection(deltaScale, _resizeAnchorPoint);
            }
            else if (_isRotatingSelection)
            {
                double pointerAngle = Math.Atan2(pos.Y - _rotateCenter.Y, pos.X - _rotateCenter.X)
                    * 180.0 / Math.PI;
                double total = AnnotationRotation.NormalizeDegrees(pointerAngle - _rotateStartPointerAngle);
                double delta = total - _lastRotationDegrees;
                _lastRotationDegrees = total;
                _totalRotationDegrees = total;
                RotateItemsDirectly(_selectedStrokes, _selectedTextContainers, delta, _rotateCenter);
            }
            else if (_isDraggingSelection)
            {
                var deltaX = pos.X - _dragStartPoint.X;
                var deltaY = pos.Y - _dragStartPoint.Y;
                _totalDragDeltaX += deltaX;
                _totalDragDeltaY += deltaY;
                MoveItemsDirectly(_selectedStrokes, _selectedTextContainers, deltaX, deltaY);
                _dragStartPoint = pos;
            }
            else if (_isSelecting)
            {
                if (_selectionShape == SelectionShape.FreeForm && _freeSelectionPath != null)
                {
                    _freeSelectionPoints.Add(pos);
                    _freeSelectionPath.Points.Add(new Point(pos.X, pos.Y));
                }
                else if (_selectionRect != null)
                {
                    var x = Math.Min(_selectionStartPoint.X, pos.X);
                    var y = Math.Min(_selectionStartPoint.Y, pos.Y);
                    Canvas.SetLeft(_selectionRect, x);
                    Canvas.SetTop(_selectionRect, y);
                    _selectionRect.Width = Math.Abs(pos.X - _selectionStartPoint.X);
                    _selectionRect.Height = Math.Abs(pos.Y - _selectionStartPoint.Y);
                }
            }
            e.Handled = true;
        }

        private void SelectionOverlay_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_selectionPointerId == null || e.Pointer.PointerId != _selectionPointerId.Value)
                return;
            _selectionPointerId = null;
            SelectionOverlayCanvas.ReleasePointerCapture(e.Pointer);
            CompleteSelectionGesture();
            e.Handled = true;
        }

        private void SelectionOverlay_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_selectionPointerId == null || e.Pointer.PointerId != _selectionPointerId.Value)
                return;
            _selectionPointerId = null;
            CancelSelectionInteraction(restoreSnapshot: true);
            e.Handled = true;
        }

        private void SelectionOverlay_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            // WPF LostMouseCapture parity — a lost capture cancels the gesture
            // and restores the snapshot (release sets _selectionPointerId null
            // first so a normal pointer-up doesn't reach this path).
            if (_selectionPointerId == null)
                return;
            _selectionPointerId = null;
            CancelSelectionInteraction(restoreSnapshot: true);
        }

        private void SelectionOverlay_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_selectionPointerId == null)
                ProtectedCursor = null;
        }

        private void SelectionOverlay_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (!_isSelectionMode)
                return;
            CancelSelectionInteraction(restoreSnapshot: true);
            ClearSelection();
            BlankContextRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void SelectionOverlay_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (!_isSelectionMode)
                return;
            BlankContextRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        /// <summary>
        /// Pointer-up: completes whichever selection gesture was active —
        /// fires the completion events (undo actions live in the editor) or
        /// resolves the marquee/lasso into a selection.
        /// </summary>
        private void CompleteSelectionGesture()
        {
            SetPageZIndex(0);

            if (_isResizingSelection)
            {
                _isResizingSelection = false;
                if (Math.Abs(_lastResizeScale - 1.0) > 0.001)
                    SelectionResizeCompleted?.Invoke(this, new SelectionResizeCompletedEventArgs(
                        _lastResizeScale, _resizeAnchorPoint,
                        _selectedStrokes.ToList(), _selectedTextContainers.ToList()));
                _lastResizeScale = 1.0;
                _selectionInteractionSnapshot = null;
                // Live packets coalesced through QueueSelectionVisualsUpdate
                // — pointer release commits a synchronous final rebuild.
                UpdateSelectionVisuals();
                return;
            }

            if (_isRotatingSelection)
            {
                _isRotatingSelection = false;
                if (Math.Abs(_totalRotationDegrees) > 0.5)
                    SelectionRotateCompleted?.Invoke(this, new SelectionRotateCompletedEventArgs(
                        _totalRotationDegrees, _rotateCenter,
                        _selectedStrokes.ToList(), _selectedTextContainers.ToList()));
                _lastRotationDegrees = 0;
                _totalRotationDegrees = 0;
                _selectionInteractionSnapshot = null;
                UpdateSelectionVisuals();
                return;
            }

            if (_isDraggingSelection)
            {
                _isDraggingSelection = false;
                if (Math.Abs(_totalDragDeltaX) > 0.5 || Math.Abs(_totalDragDeltaY) > 0.5)
                    SelectionMoveCompleted?.Invoke(this, new SelectionMoveCompletedEventArgs(
                        _totalDragDeltaX, _totalDragDeltaY,
                        _selectedStrokes.ToList(), _selectedTextContainers.ToList()));
                _totalDragDeltaX = 0;
                _totalDragDeltaY = 0;
                _selectionInteractionSnapshot = null;
                UpdateSelectionVisuals();
                return;
            }

            if (!_isSelecting)
                return;
            _isSelecting = false;

            _selectedStrokes.Clear();
            _selectedTextContainers.Clear();
            bool isClick = _selectionShape == SelectionShape.FreeForm
                ? _freeSelectionPoints == null || _freeSelectionPoints.Count <= 2
                : _selectionRect == null
                    || (_selectionRect.Width < 4 && _selectionRect.Height < 4);

            if (isClick)
            {
                // Topmost-first point hit (WPF click parity): text containers
                // sit above ink, overlay containers below ink but above the
                // bitmap, so the probe order is text → overlay → strokes.
                PointD clickPoint = _selectionStartPoint;
                bool hitSomething = false;

                if (_selectionFilter != SelectionFilter.DrawingsOnly)
                {
                    for (int i = TextOverlayCanvas.Children.Count - 1; i >= 0; i--)
                    {
                        if (TextOverlayCanvas.Children[i] is Grid container
                            && HitTextContainer(container, clickPoint))
                        {
                            _selectedTextContainers.Add(container);
                            hitSomething = true;
                            break;
                        }
                    }

                    if (!hitSomething)
                    {
                        for (int i = ImageOverlayCanvas.Children.Count - 1; i >= 0; i--)
                        {
                            if (ImageOverlayCanvas.Children[i] is Grid container
                                && IsOverlayContainer(container)
                                && HitTextContainer(container, clickPoint))
                            {
                                _selectedTextContainers.Add(container);
                                hitSomething = true;
                                break;
                            }
                        }
                    }
                }

                if (!hitSomething && _selectionFilter != SelectionFilter.TextOnly)
                {
                    for (int i = InkSurface.Store.Strokes.Count - 1; i >= 0; i--)
                    {
                        var stroke = InkSurface.Store.Strokes[i];
                        if (HitStroke(stroke, _selectionStartPoint))
                        {
                            _selectedStrokes.AddRange(GetLogicalShapeStrokes(stroke));
                            break;
                        }
                    }
                }
            }
            else if (_selectionShape == SelectionShape.FreeForm && _freeSelectionPoints?.Count > 2)
            {
                var polygon = _freeSelectionPoints;
                if (_selectionFilter != SelectionFilter.TextOnly)
                {
                    foreach (var stroke in InkSurface.Store.Strokes)
                    {
                        var spine = stroke.Points.Select(p => new PointD(p.X, p.Y)).ToList();
                        if (StrokeGeometry.IsStrokeInsidePolygon(
                                polygon, spine, StrokeGeometry.GetSpineBounds(stroke.Points, stroke.Size)))
                        {
                            _selectedStrokes.Add(stroke);
                        }
                    }
                }

                if (_selectionFilter != SelectionFilter.DrawingsOnly)
                {
                    foreach (var container in TextOverlayCanvas.Children.OfType<Grid>())
                    {
                        var containerRect = GetContainerRect(container);
                        if (StrokeGeometry.IsContainerInsidePolygon(polygon, containerRect))
                            _selectedTextContainers.Add(container);
                    }
                    foreach (var container in ImageOverlayCanvas.Children.OfType<Grid>()
                        .Where(IsOverlayContainer))
                    {
                        var containerRect = GetContainerRect(container);
                        if (StrokeGeometry.IsContainerInsidePolygon(polygon, containerRect))
                            _selectedTextContainers.Add(container);
                    }
                }
            }
            else if (_selectionRect != null)
            {
                var selRect = new RectD(
                    Canvas.GetLeft(_selectionRect), Canvas.GetTop(_selectionRect),
                    _selectionRect.Width, _selectionRect.Height);
                if (_selectionFilter != SelectionFilter.TextOnly)
                {
                    foreach (var stroke in InkSurface.Store.Strokes)
                    {
                        var spine = stroke.Points.Select(p => new PointD(p.X, p.Y)).ToList();
                        if (StrokeGeometry.IsStrokeInsideRect(
                                selRect, spine, StrokeGeometry.GetSpineBounds(stroke.Points, stroke.Size)))
                        {
                            _selectedStrokes.Add(stroke);
                        }
                    }
                }

                if (_selectionFilter != SelectionFilter.DrawingsOnly)
                {
                    foreach (var container in TextOverlayCanvas.Children.OfType<Grid>())
                    {
                        var containerRect = GetContainerRect(container);
                        if (selRect.Contains(containerRect))
                            _selectedTextContainers.Add(container);
                    }
                    foreach (var container in ImageOverlayCanvas.Children.OfType<Grid>()
                        .Where(IsOverlayContainer))
                    {
                        var containerRect = GetContainerRect(container);
                        if (selRect.Contains(containerRect))
                            _selectedTextContainers.Add(container);
                    }
                }
            }

            _freeSelectionPath = null;
            _freeSelectionPoints = null;
            _selectionRect = null;

            // Group expansion — every part of a logical shape selects together.
            if (_selectedStrokes.Count > 0)
            {
                var expanded = _selectedStrokes
                    .SelectMany(GetLogicalShapeStrokes)
                    .Distinct()
                    .ToList();
                _selectedStrokes.Clear();
                _selectedStrokes.AddRange(expanded);
            }

            if (_selectedStrokes.Count == 0 && _selectedTextContainers.Count == 0)
            {
                SelectionOverlayCanvas.Children.Clear();
                StopSelectionDashTimer();
            }
            else
            {
                UpdateSelectionVisuals();
            }
        }

        /// <summary>Container hit-test: axis-aligned rect contains the point (WPF HitTextContainer).</summary>
        private static bool HitTextContainer(Grid container, PointD point)
            => GetContainerRect(container).Contains(point);

        /// <summary>Canvas-space rect for a container (ActualSize preferred over declared).</summary>
        private static RectD GetContainerRect(FrameworkElement container)
        {
            var left = Canvas.GetLeft(container);
            var top = Canvas.GetTop(container);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            var width = container.ActualWidth > 0 ? container.ActualWidth
                : (!double.IsNaN(container.Width) && container.Width > 0 ? container.Width : 0);
            var height = container.ActualHeight > 0 ? container.ActualHeight
                : (!double.IsNaN(container.Height) && container.Height > 0 ? container.Height : 0);
            return new RectD(left, top, width, height);
        }

        private void HandleCtrlClickToggle(PointD point)
        {
            // Text containers sit above ink — topmost-first probe order
            // (text → overlay → strokes), WPF HandleCtrlClickToggle parity.
            if (_selectionFilter != SelectionFilter.DrawingsOnly)
            {
                for (int i = TextOverlayCanvas.Children.Count - 1; i >= 0; i--)
                {
                    if (TextOverlayCanvas.Children[i] is Grid container
                        && HitTextContainer(container, point))
                    {
                        ToggleTextContainerSelection(container);
                        return;
                    }
                }
                for (int i = ImageOverlayCanvas.Children.Count - 1; i >= 0; i--)
                {
                    if (ImageOverlayCanvas.Children[i] is Grid container
                        && IsOverlayContainer(container)
                        && HitTextContainer(container, point))
                    {
                        ToggleTextContainerSelection(container);
                        return;
                    }
                }
            }

            if (_selectionFilter != SelectionFilter.TextOnly)
            {
                for (int i = InkSurface.Store.Strokes.Count - 1; i >= 0; i--)
                {
                    var stroke = InkSurface.Store.Strokes[i];
                    if (HitStroke(stroke, point))
                    {
                        ToggleStrokeSelection(stroke);
                        return;
                    }
                }
            }
            // Ctrl+click on empty space keeps the current selection.
        }

        private void ToggleTextContainerSelection(Grid container)
        {
            if (_selectedTextContainers.Contains(container))
            {
                _selectedTextContainers.Remove(container);
                RefreshSelectionAfterToggle();
            }
            else
            {
                _selectedTextContainers.Add(container);
                UpdateSelectionVisuals();
            }
        }

        private void ToggleStrokeSelection(InkStrokeData stroke)
        {
            var logical = GetLogicalShapeStrokes(stroke);
            if (logical.All(candidate => _selectedStrokes.Contains(candidate)))
            {
                foreach (var candidate in logical)
                    _selectedStrokes.Remove(candidate);
                RefreshSelectionAfterToggle();
            }
            else
            {
                foreach (var candidate in logical)
                {
                    if (!_selectedStrokes.Contains(candidate))
                        _selectedStrokes.Add(candidate);
                }
                UpdateSelectionVisuals();
            }
        }

        private void RefreshSelectionAfterToggle()
        {
            if (!HasSelection)
                ClearSelection();
            else
                UpdateSelectionVisuals();
        }

        // ── Live transforms (the page applies deltas incrementally; the
        //    undo actions replay the total delta through the same Core math) ──

        public void MoveSelection(double deltaX, double deltaY)
        {
            if (_selectedStrokes.Count == 0 && _selectedTextContainers.Count == 0)
                return;
            MoveItemsDirectly(_selectedStrokes, _selectedTextContainers, deltaX, deltaY);
        }

        /// <summary>
        /// WPF MoveItemsDirectly parity — strokes translate spines, sticky
        /// markers move through the clamped quiet setter, other containers
        /// move their canvas position; visuals + mutation event follow.
        /// </summary>
        public void MoveItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<Grid> containers,
            double deltaX, double deltaY)
        {
            if ((strokes == null || strokes.Count == 0) && (containers == null || containers.Count == 0))
                return;

            if (strokes != null)
            {
                foreach (var stroke in strokes)
                    StrokeGeometry.TranslateSpinePoints(stroke.Points, deltaX, deltaY);
                InkSurface.Store.NotifyGeometryChanged(strokes);
            }

            if (containers != null)
            {
                foreach (var container in containers)
                {
                    if (container == null)
                        continue;
                    var left = Canvas.GetLeft(container);
                    var top = Canvas.GetTop(container);
                    if (IsStickyNoteContainer(container))
                    {
                        SetStickyNotePositionQuiet(container, new PointD(
                            (double.IsNaN(left) ? 0 : left) + deltaX,
                            (double.IsNaN(top) ? 0 : top) + deltaY));
                    }
                    else
                    {
                        Canvas.SetLeft(container, (double.IsNaN(left) ? 0 : left) + deltaX);
                        Canvas.SetTop(container, (double.IsNaN(top) ? 0 : top) + deltaY);
                    }
                }
            }

            // Live drag packets coalesce through the dispatcher queue — the
            // pointer-release path calls UpdateSelectionVisuals()
            // synchronously for the final chrome rebuild.
            QueueSelectionVisualsUpdate();
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        public void ScaleSelection(double scaleFactor, PointD center)
        {
            if (_selectedStrokes.Count == 0 && _selectedTextContainers.Count == 0)
                return;
            ScaleItemsDirectly(_selectedStrokes, _selectedTextContainers, scaleFactor, center);
        }

        /// <summary>
        /// WPF ScaleItemsDirectly parity — overlay containers scale their
        /// explicit size (sticky syncs the model + reclamps); text containers
        /// scale position and font size.
        /// </summary>
        public void ScaleItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<Grid> containers,
            double scaleFactor, PointD center)
        {
            if ((strokes == null || strokes.Count == 0) && (containers == null || containers.Count == 0))
                return;

            if (strokes != null)
            {
                foreach (var stroke in strokes)
                {
                    StrokeGeometry.ScaleSpinePoints(stroke.Points, scaleFactor, center);
                    stroke.Size *= scaleFactor;
                }
                InkSurface.Store.NotifyGeometryChanged(strokes);
            }

            if (containers != null)
            {
                foreach (var container in containers)
                {
                    if (container == null)
                        continue;
                    var left = double.IsNaN(Canvas.GetLeft(container)) ? 0 : Canvas.GetLeft(container);
                    var top = double.IsNaN(Canvas.GetTop(container)) ? 0 : Canvas.GetTop(container);
                    var newLeft = center.X + (left - center.X) * scaleFactor;
                    var newTop = center.Y + (top - center.Y) * scaleFactor;

                    if (IsOverlayContainer(container))
                    {
                        // Overlay containers scale via explicit size; the
                        // inner content follows automatically (sticky:
                        // centred icon).
                        container.Width = Math.Max(1.0, container.Width * scaleFactor);
                        container.Height = Math.Max(1.0, container.Height * scaleFactor);
                        if (IsStickyNoteContainer(container)
                            && GetOverlayData(container) is StickyNoteAnnotation note)
                        {
                            note.Width = container.Width;
                            note.Height = container.Height;
                            // A selection resize is a real geometry edit —
                            // keep the serialized DIP origin in sync and
                            // reclamp against the new marker dimensions.
                            SetStickyNotePositionQuiet(container, new PointD(newLeft, newTop));
                        }
                        else
                        {
                            Canvas.SetLeft(container, newLeft);
                            Canvas.SetTop(container, newTop);
                        }
                    }
                    else
                    {
                        Canvas.SetLeft(container, newLeft);
                        Canvas.SetTop(container, newTop);
                        var tb = container.Children.OfType<TextBox>().FirstOrDefault();
                        if (tb != null)
                            tb.FontSize *= scaleFactor;
                    }
                }
            }

            UpdateSelectionVisuals();
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// WPF RotateItemsDirectly parity — containers rotate their centre
        /// about the selection centre and accumulate a RenderTransform
        /// rotation; sticky notes sync <see cref="StickyNoteAnnotation.RotationDegrees"/>.
        /// </summary>
        public void RotateItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<Grid> containers,
            double degrees, PointD center)
        {
            if ((strokes == null || strokes.Count == 0) && (containers == null || containers.Count == 0))
                return;
            if (Math.Abs(degrees) < 0.001)
                return;

            if (strokes != null)
            {
                foreach (var stroke in strokes)
                    StrokeGeometry.RotateSpinePoints(stroke.Points, degrees, center);
                InkSurface.Store.NotifyGeometryChanged(strokes);
            }

            if (containers != null)
            {
                foreach (var container in containers)
                {
                    if (container == null)
                        continue;

                    double width = container.ActualWidth > 0 ? container.ActualWidth
                        : (!double.IsNaN(container.Width) && container.Width > 0 ? container.Width : 0);
                    double height = container.ActualHeight > 0 ? container.ActualHeight
                        : (!double.IsNaN(container.Height) && container.Height > 0 ? container.Height : 0);
                    double left = double.IsNaN(Canvas.GetLeft(container)) ? 0 : Canvas.GetLeft(container);
                    double top = double.IsNaN(Canvas.GetTop(container)) ? 0 : Canvas.GetTop(container);
                    var itemCenter = new PointD(left + width / 2, top + height / 2);
                    var rotatedCenter = AnnotationRotation.RotatePoint(itemCenter, center, degrees);
                    var newLeft = rotatedCenter.X - width / 2;
                    var newTop = rotatedCenter.Y - height / 2;

                    if (IsStickyNoteContainer(container))
                        SetStickyNotePositionQuiet(container, new PointD(newLeft, newTop));
                    else
                    {
                        Canvas.SetLeft(container, newLeft);
                        Canvas.SetTop(container, newTop);
                    }

                    ApplyAnnotationRotation(container, ReadAnnotationRotation(container) + degrees);
                    if (GetOverlayData(container) is StickyNoteAnnotation note)
                        note.RotationDegrees = ReadAnnotationRotation(container);
                }
            }

            UpdateSelectionVisuals();
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        // ── Selection visuals ────────────────────────────────────────────

        /// <summary>
        /// Coalesces selection-chrome rebuilds: NotifyGeometryChanged fires
        /// one Mutated per stroke, so an N-stroke drag would otherwise run
        /// the full overlay rebuild N times per pointer packet.
        /// </summary>
        private void QueueSelectionVisualsUpdate()
        {
            _selectionVisualsDirty = true;
            if (_selectionVisualsUpdateQueued)
                return;
            _selectionVisualsUpdateQueued = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                _selectionVisualsUpdateQueued = false;
                if (!_selectionVisualsDirty)
                    return;
                _selectionVisualsDirty = false;
                if (HasSelection)
                    UpdateSelectionVisuals();
            });
        }

        private void UpdateSelectionVisuals()
        {
            // A synchronous rebuild also satisfies a queued request.
            _selectionVisualsDirty = false;
            var unionBounds = GetSelectionBounds();
            var boundsD = new RectD(unionBounds.X, unionBounds.Y, unionBounds.Width, unionBounds.Height);
            if (!HasSelection)
            {
                StopSelectionDashTimer();
                return;
            }

            SelectionOverlayCanvas.Children.Clear();
            var selectionBorder = new Rectangle
            {
                Width = boundsD.Width + 8,
                Height = boundsD.Height + 8,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Opacity = 0.18,
                IsHitTestVisible = false,
            };
            selectionBorder.Stroke = TryFindBrush("ThemeAccentBrush");
            selectionBorder.Fill = TryFindBrush("ThemeSelectionBrush");
            Canvas.SetLeft(selectionBorder, boundsD.X - 4);
            Canvas.SetTop(selectionBorder, boundsD.Y - 4);
            SelectionOverlayCanvas.Children.Add(selectionBorder);

            // Per-item marching-ants outlines — each selected stroke and
            // container keeps its own dashed rect (WPF Task-6 behavior).
            _perItemOutlines.Clear();
            foreach (var stroke in _selectedStrokes)
            {
                var strokeBounds = StrokeGeometry.GetRenderedStrokeBounds(stroke).Inflated(3, 3);
                AddPerItemOutline(strokeBounds);
            }
            foreach (var container in _selectedTextContainers)
            {
                var containerBounds = GetContainerRect(container).Inflated(3, 3);
                AddPerItemOutline(containerBounds);
            }
            if (_perItemOutlines.Count > 0)
                StartSelectionDashTimer();
            else
                StopSelectionDashTimer();

            // 4 corner resize handles + rotate stem/knob (WPF layout).
            var handleRects = StrokeGeometry.GetSelectionCornerHandleRects(boundsD);
            foreach (var hr in handleRects)
            {
                var handle = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false,
                };
                handle.Fill = TryFindBrush("ThemeSurfaceBrush");
                handle.Stroke = TryFindBrush("ThemeAccentBrush");
                Canvas.SetLeft(handle, hr.X + hr.Width / 2 - 6);
                Canvas.SetTop(handle, hr.Y + hr.Height / 2 - 6);
                SelectionOverlayCanvas.Children.Add(handle);
            }

            var rotateHandle = StrokeGeometry.GetRotateHandlePoint(boundsD);
            var stem = new Line
            {
                X1 = boundsD.X + boundsD.Width / 2,
                Y1 = boundsD.Y - 4,
                X2 = rotateHandle.X,
                Y2 = rotateHandle.Y,
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            };
            stem.Stroke = TryFindBrush("ThemeAccentBrush");
            SelectionOverlayCanvas.Children.Add(stem);

            var rotateKnob = new Ellipse
            {
                Width = 14,
                Height = 14,
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            };
            rotateKnob.Fill = TryFindBrush("ThemeSurfaceBrush");
            rotateKnob.Stroke = TryFindBrush("ThemeAccentBrush");
            Canvas.SetLeft(rotateKnob, rotateHandle.X - 7);
            Canvas.SetTop(rotateKnob, rotateHandle.Y - 7);
            SelectionOverlayCanvas.Children.Add(rotateKnob);

            SelectionChanged?.Invoke(this, new AnnotationSelectionChangedEventArgs(
                true, new Rect(boundsD.X, boundsD.Y, boundsD.Width, boundsD.Height)));
        }

        private void AddPerItemOutline(RectD bounds)
        {
            var outline = new Rectangle
            {
                Width = Math.Max(bounds.Width, 1),
                Height = Math.Max(bounds.Height, 1),
                StrokeThickness = 1.2,
                StrokeDashArray = PerItemOutlineDashArray,
                StrokeDashOffset = _selectionDashOffset,
                IsHitTestVisible = false,
            };
            outline.Stroke = TryFindBrush("ThemeAccentBrush");
            Canvas.SetLeft(outline, bounds.X);
            Canvas.SetTop(outline, bounds.Y);
            SelectionOverlayCanvas.Children.Add(outline);
            _perItemOutlines.Add(outline);
        }

        private void StartSelectionDashTimer()
        {
            if (!_hostActive || !WinUiThemeService.ShouldAnimate || _selectionDashTimer != null)
                return;
            _selectionDashLastTickUtc = DateTimeOffset.UtcNow;
            _selectionDashTimer = DispatcherQueue.CreateTimer();
            _selectionDashTimer.Interval = TimeSpan.FromMilliseconds(33);
            _selectionDashTimer.Tick += SelectionDashTimer_Tick;
            _selectionDashTimer.Start();
        }

        private void StopSelectionDashTimer()
        {
            _perItemOutlines.Clear();
            if (_selectionDashTimer == null)
                return;
            _selectionDashTimer.Stop();
            _selectionDashTimer = null;
        }

        private void SelectionDashTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (_perItemOutlines.Count == 0 || !WinUiThemeService.ShouldAnimate || !IsLoaded)
            {
                StopSelectionDashTimer();
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var elapsed = (now - _selectionDashLastTickUtc).TotalSeconds;
            _selectionDashLastTickUtc = now;
            if (elapsed < 0 || elapsed > 1.0)
                elapsed = 0; // clamp after stalls (tab switch, debugger break)

            _selectionDashOffset = (_selectionDashOffset + elapsed * SelectionDashSpeed)
                % SelectionDashPatternPeriod;
            _selectionColorPhaseSeconds += elapsed;
            if (_selectionColorPhaseSeconds >= SelectionColorHalfCycleSeconds * 2)
                _selectionColorPhaseSeconds -= SelectionColorHalfCycleSeconds * 2;

            // Alternate accent/focus so the cue survives every palette (HC too).
            var brushKey = _selectionColorPhaseSeconds >= SelectionColorHalfCycleSeconds
                ? "ThemeFocusBrush"
                : "ThemeAccentBrush";
            var brush = TryFindBrush(brushKey);
            foreach (var outline in _perItemOutlines)
            {
                outline.StrokeDashOffset = _selectionDashOffset;
                if (brush != null && !ReferenceEquals(outline.Stroke, brush))
                    outline.Stroke = brush;
            }
        }

        private static Brush TryFindBrush(string key)
        {
            if (Application.Current?.Resources != null
                && Application.Current.Resources.TryGetValue(key, out var value)
                && value is Brush brush)
            {
                return brush;
            }
            return null;
        }

        private void UpdateHoverCursor(PointD pos)
        {
            var unionBounds = GetSelectionBounds();
            var bounds = new RectD(unionBounds.X, unionBounds.Y, unionBounds.Width, unionBounds.Height);
            if (StrokeGeometry.TryGetResizeHandleIndex(bounds, pos, out int handleIndex))
            {
                ProtectedCursor = InputSystemCursor.Create(handleIndex switch
                {
                    0 or 3 => InputSystemCursorShape.SizeNorthwestSoutheast,
                    1 or 2 => InputSystemCursorShape.SizeNortheastSouthwest,
                    _ => InputSystemCursorShape.SizeAll,
                });
                return;
            }
            var rotateHandle = StrokeGeometry.GetRotateHandlePoint(bounds);
            if (new RectD(rotateHandle.X - 10, rotateHandle.Y - 10, 20, 20).Contains(pos))
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
                return;
            }
            ProtectedCursor = InputSystemCursor.Create(
                bounds.Inflated(8, 8).Contains(pos)
                    ? InputSystemCursorShape.SizeAll
                    : InputSystemCursorShape.Cross);
        }

        // ==================================================================
        // Overlay containers + sticky notes (Task 8 Phase A)
        // ==================================================================

        /// <summary>
        /// True for every container kind that lives on
        /// <see cref="ImageOverlayCanvas"/> — images, text markups, area
        /// highlights and sticky notes all share the same pipeline: overlay
        /// placement, marquee/Ctrl+click selection, explicit-size scaling
        /// and quiet re-parenting for undo / cross-page moves (WPF
        /// IsOverlayContainer).
        /// </summary>
        internal static bool IsOverlayContainer(Grid container)
        {
            if (container?.Tag is not string tag)
                return false;
            return tag == ImageContainerTag
                || tag == MarkupContainerTag
                || tag == AreaHighlightContainerTag
                || tag == StickyNoteContainerTag;
        }

        /// <summary>Image annotation containers (WPF IsImageContainer).</summary>
        internal static bool IsImageContainer(Grid container)
            => container != null && (container.Tag as string) == ImageContainerTag;

        private bool IsStickyNoteContainer(Grid container)
            => container != null && (container.Tag as string) == StickyNoteContainerTag;

        /// <summary>The annotation payload stored behind a container (WPF GetOverlayData).</summary>
        public object GetOverlayData(Grid container)
            => container != null && _overlayData.TryGetValue(container, out var data) ? data : null;

        /// <summary>Attach an annotation payload to a container (cross-page transfer).</summary>
        public void SetOverlayData(Grid container, object data)
        {
            if (container == null)
                return;
            _overlayData[container] = data;
        }

        /// <summary>
        /// All non-image overlay containers on the page (WPF
        /// GetOverlayContainers — images ride their own
        /// <see cref="ImageContainers"/> list because their payload lives in
        /// <see cref="_imageDataById"/>, not <see cref="_overlayData"/>).
        /// </summary>
        public IReadOnlyList<Grid> GetOverlayContainers()
        {
            var result = new List<Grid>();
            foreach (var child in ImageOverlayCanvas.Children)
            {
                if (child is Grid container
                    && !IsImageContainer(container)
                    && IsOverlayContainer(container))
                    result.Add(container);
            }
            return result;
        }

        /// <summary>Image containers currently on the page, in insertion order (WPF ImageContainers).</summary>
        public IReadOnlyList<Grid> ImageContainers => _imageContainers;

        /// <summary>Raw encoded bytes (PNG/JPEG) behind an image container, or null (WPF GetImageData).</summary>
        public byte[] GetImageData(Grid container)
            => container != null && _imageDataById.TryGetValue(container, out var data) ? data : null;

        /// <summary>
        /// Registers image payload for a container that arrived from another
        /// page — cross-page moves re-parent the Grid but the payload dict is
        /// per-control, so the moving side transfers it explicitly (WPF
        /// SetImageData).
        /// </summary>
        public void SetImageData(Grid container, byte[] data)
        {
            if (container == null || data == null)
                return;
            _imageDataById[container] = data;
        }

        /// <summary>Drops the image payload for a container leaving the page (WPF RemoveImageData).</summary>
        public void RemoveImageData(Grid container)
        {
            if (container != null)
                _imageDataById.Remove(container);
        }

        /// <summary>
        /// Removes a container from whichever overlay layer hosts it — text
        /// boxes on <see cref="TextOverlayCanvas"/>, images and overlay
        /// annotations (markup / area highlight / sticky note) on
        /// <see cref="ImageOverlayCanvas"/>. The payload dictionaries keep
        /// their entries so a later re-add (undo / move back) restores the
        /// item as-is (WPF RemoveTextContainerQuiet).
        /// </summary>
        public bool RemoveTextContainerQuiet(Grid container)
        {
            if (container == null)
                return false;

            bool removed;
            if (ReferenceEquals(container.Parent, ImageOverlayCanvas))
            {
                ImageOverlayCanvas.Children.Remove(container);
                _imageContainers.Remove(container);
                removed = true;
            }
            else if (ReferenceEquals(container.Parent, TextOverlayCanvas))
            {
                TextOverlayCanvas.Children.Remove(container);
                removed = true;
            }
            else
            {
                removed = false;
            }
            // Quiet mutators still notify thumbnail/dirty observers — the
            // host contract calls for visual notifications, just no undo
            // recursion.
            if (removed)
                InkMutated?.Invoke(this, EventArgs.Empty);
            return removed;
        }

        /// <summary>
        /// Re-adds a container after the quiet remove — every overlay kind
        /// (images, markups, area highlights, sticky markers) goes back on
        /// <see cref="ImageOverlayCanvas"/> (below ink), text boxes on
        /// <see cref="TextOverlayCanvas"/> (WPF AddTextContainerQuiet).
        /// </summary>
        public void AddTextContainerQuiet(Grid container)
        {
            if (container == null)
                return;
            if (IsOverlayContainer(container))
            {
                ImageOverlayCanvas.Children.Add(container);
                if (IsImageContainer(container) && !_imageContainers.Contains(container))
                    _imageContainers.Add(container);
            }
            else
            {
                TextOverlayCanvas.Children.Add(container);
            }
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Page-space size used for sticky clamping (WPF GetStickyPageSize).</summary>
        private Size GetStickyPageSize()
        {
            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;
            if (width <= 0) width = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : RootGrid.Width;
            if (height <= 0) height = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : RootGrid.Height;
            if (width <= 0) width = 1584;
            if (height <= 0) height = 2245;
            return new Size(Math.Max(0, width), Math.Max(0, height));
        }

        /// <summary>Clamps a sticky marker inside the page bounds (WPF ClampStickyNotePosition).</summary>
        public static PointD ClampStickyNotePosition(PointD position, Size pageSize, Size markerSize)
        {
            double maxX = Math.Max(0, pageSize.Width - markerSize.Width);
            double maxY = Math.Max(0, pageSize.Height - markerSize.Height);
            return new PointD(
                Math.Clamp(position.X, 0, maxX),
                Math.Clamp(position.Y, 0, maxY));
        }

        /// <summary>
        /// Creates the sticky-note marker container on
        /// <see cref="ImageOverlayCanvas"/> (below ink) — WPF AddStickyNote:
        /// 36-DIP rounded icon with the note glyph, <c>StickyNote.{id}</c>
        /// AutomationId, tooltip = note text, context flyout with Delete,
        /// pointer drag + keyboard move/activate/delete.
        /// </summary>
        public Grid AddStickyNote(StickyNoteAnnotation note)
        {
            if (note == null)
                return null;

            EnsureStickyNoteIdentity(note);

            double markerWidth = note.Width >= 32 && !double.IsNaN(note.Width)
                ? note.Width
                : StickyMarkerSize;
            double markerHeight = note.Height >= 32 && !double.IsNaN(note.Height)
                ? note.Height
                : StickyMarkerSize;
            note.Width = markerWidth;
            note.Height = markerHeight;

            var icon = new Border
            {
                Width = markerWidth,
                Height = markerHeight,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(255, note.R, note.G, note.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0xD4, 0xA7, 0x2C)),
                BorderThickness = new Thickness(1),
                Child = new Microsoft.UI.Xaml.Shapes.Path
                {
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform,
                    Data = LucideIcon.ParseIconGeometry(
                        "M4,3 L16,3 L20,7 L20,20 L4,20 Z M16,3 L16,8 L20,8 M7,12 L17,12 M7,16 L15,16"),
                    Fill = new SolidColorBrush(Color.FromArgb(255, 0x7A, 0x5C, 0x0E)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                },
            };

            // UiaGrid keeps the marker UIA-visible (plain Grid exposes no
            // peer); the inner Button supplies focus/tab-stop/keyboard since
            // Grid cannot take focus in WinUI.
            var container = new UiaGrid
            {
                Width = markerWidth,
                Height = markerHeight,
                Tag = StickyNoteContainerTag,
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                IsHitTestVisible = true,
            };

            var hitButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Content = icon,
                IsTabStop = true,
            };
            hitButton.AddHandler(PointerPressedEvent,
                new PointerEventHandler(StickyNote_PointerPressed), handledEventsToo: true);
            hitButton.AddHandler(PointerReleasedEvent,
                new PointerEventHandler(StickyNote_PointerReleased), handledEventsToo: true);
            hitButton.PointerMoved += StickyNote_PointerMoved;
            hitButton.PointerCaptureLost += StickyNote_PointerCaptureLost;
            hitButton.PointerCanceled += StickyNote_PointerCanceled;
            hitButton.KeyDown += StickyNote_KeyDown;
            var flyout = BuildStickyNoteContextMenu(container);
            // Deterministic right-tap path (WPF ContextMenu parity): the
            // button's own ContextFlyout is NOT set — the framework would
            // auto-open it and race this explicit ShowAt into a double
            // open. e.Handled suppresses the container's retrieval anchor
            // from auto-opening on the bubbled tap.
            hitButton.RightTapped += (s, e) =>
            {
                e.Handled = true;
                flyout.ShowAt(container);
            };
            container.Children.Add(hitButton);
            container.ContextFlyout = flyout; // retrievable for localization refresh

            AutomationProperties.SetAutomationId(container, $"StickyNote.{note.Id}");
            string stickyLabel = LocalizationService.Get("Editor.StickyNoteTooltip");
            AutomationProperties.SetName(container, stickyLabel);
            AutomationProperties.SetHelpText(container, stickyLabel);
            ToolTipService.SetToolTip(container, note.Text ?? string.Empty);

            ImageOverlayCanvas.Children.Add(container);
            _overlayData[container] = note;
            SetStickyNotePositionQuiet(container, new PointD(note.X, note.Y));
            ApplyAnnotationRotation(container, note.RotationDegrees);
            InkMutated?.Invoke(this, EventArgs.Empty);
            return container;
        }

        /// <summary>
        /// PDF annotation names are page-local in the sidecar format, but a
        /// malformed file can repeat or omit them. Repair only the identity;
        /// text, geometry and colour remain untouched (WPF
        /// EnsureStickyNoteIdentity).
        /// </summary>
        private void EnsureStickyNoteIdentity(StickyNoteAnnotation note)
        {
            string candidate = note.Id?.Trim();
            bool used = !string.IsNullOrWhiteSpace(candidate)
                && _overlayData.Values
                    .OfType<StickyNoteAnnotation>()
                    .Any(existing => string.Equals(existing.Id?.Trim(), candidate,
                        StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(candidate) || used)
            {
                do
                    candidate = Guid.NewGuid().ToString("N");
                while (_overlayData.Values
                    .OfType<StickyNoteAnnotation>()
                    .Any(existing => string.Equals(existing.Id, candidate,
                        StringComparison.OrdinalIgnoreCase)));
            }

            note.Id = candidate;
        }

        private MenuFlyout BuildStickyNoteContextMenu(Grid container)
        {
            var flyout = new MenuFlyout();
            var delete = new MenuFlyoutItem
            {
                Text = LocalizationService.Get("Editor.DeleteTooltip"),
                Tag = "StickyNote.Delete",
            };
            AutomationProperties.SetAutomationId(delete, "Sticky.Delete.ContextMenu");
            string deleteLabel = LocalizationService.Get("Editor.DeleteTooltip");
            AutomationProperties.SetName(delete, deleteLabel);
            AutomationProperties.SetHelpText(delete, deleteLabel);
            delete.Click += (sender, args) =>
            {
                StickyNoteDeleteRequested?.Invoke(this, container);
            };
            flyout.Items.Add(delete);
            return flyout;
        }

        /// <summary>Quietly moves a sticky marker (clamped + model-synced) — undo/keyboard path.</summary>
        public bool SetStickyNotePositionQuiet(Grid container, PointD position)
        {
            if (container == null || !IsOverlayContainer(container)
                || GetOverlayData(container) is not StickyNoteAnnotation note)
            {
                return false;
            }

            var clamped = ClampStickyNotePosition(
                position,
                GetStickyPageSize(),
                new Size(container.Width > 0 ? container.Width : StickyMarkerSize,
                    container.Height > 0 ? container.Height : StickyMarkerSize));
            Canvas.SetLeft(container, clamped.X);
            Canvas.SetTop(container, clamped.Y);
            note.X = clamped.X;
            note.Y = clamped.Y;
            ToolTipService.SetToolTip(container, note.Text ?? string.Empty);
            InkMutated?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Quietly updates note text for an undo/redo action.</summary>
        public bool SetStickyNoteTextQuiet(Grid container, string text)
        {
            if (container == null || !IsStickyNoteContainer(container)
                || GetOverlayData(container) is not StickyNoteAnnotation note)
            {
                return false;
            }

            note.Text = text ?? string.Empty;
            ToolTipService.SetToolTip(container, note.Text);
            InkMutated?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // ── Sticky marker pointer pipeline (WPF mouse+stylus unified) ─────

        private void BeginStickyPointer(Grid container, PointD pointer, uint pointerId)
        {
            if (!IsStickyNoteContainer(container))
                return;
            if (!_hostActive || !_documentInputEnabled)
                return;

            if (_stickyDragContainer != null)
                EndStickyPointer(_stickyDragContainer, canceled: true);

            _stickyDragContainer = container;
            _stickyDragPointerId = pointerId;
            _stickyDragStartPointer = pointer;
            var left = Canvas.GetLeft(container);
            var top = Canvas.GetTop(container);
            _stickyDragStartPosition = new PointD(
                double.IsNaN(left) ? 0 : left,
                double.IsNaN(top) ? 0 : top);
            _stickyDragMoved = false;
            container.Children.OfType<Button>().FirstOrDefault()
                ?.Focus(FocusState.Pointer);
        }

        private void UpdateStickyPointer(Grid container, PointD pointer)
        {
            if (!ReferenceEquals(_stickyDragContainer, container))
                return;

            var dx = pointer.X - _stickyDragStartPointer.X;
            var dy = pointer.Y - _stickyDragStartPointer.Y;
            if (!_stickyDragMoved
                && Math.Abs(dx) < StickyDragThreshold
                && Math.Abs(dy) < StickyDragThreshold)
            {
                return;
            }

            _stickyDragMoved = true;
            SetStickyNotePositionQuiet(container, new PointD(
                _stickyDragStartPosition.X + dx,
                _stickyDragStartPosition.Y + dy));
        }

        private void EndStickyPointer(Grid container, bool canceled)
        {
            if (!ReferenceEquals(_stickyDragContainer, container))
                return;

            _suppressStickyCaptureCancellation = true;
            try
            {
                container.ReleasePointerCaptures();
                container.Children.OfType<Button>().FirstOrDefault()
                    ?.ReleasePointerCaptures();
            }
            finally
            {
                _suppressStickyCaptureCancellation = false;
            }

            bool moved = _stickyDragMoved;
            var oldPosition = _stickyDragStartPosition;
            var left = Canvas.GetLeft(container);
            var top = Canvas.GetTop(container);
            var newPosition = new PointD(
                double.IsNaN(left) ? oldPosition.X : left,
                double.IsNaN(top) ? oldPosition.Y : top);
            _stickyDragContainer = null;
            _stickyDragPointerId = null;
            _stickyDragMoved = false;

            if (canceled)
            {
                // Deactivation/unload can interrupt a captured gesture before
                // release — never leave an unrecorded half-move in the model.
                if (moved)
                    SetStickyNotePositionQuiet(container, oldPosition);
                return;
            }
            if (moved)
            {
                StickyNoteMoved?.Invoke(this,
                    new StickyNoteMovedEventArgs(container, oldPosition, newPosition));
            }
            else
            {
                StickyNoteActivated?.Invoke(this, container);
            }
        }

        /// <summary>Cancels an in-flight sticky drag (tool switch / teardown).</summary>
        private void CancelStickyDrag()
        {
            if (_stickyDragContainer != null)
                EndStickyPointer(_stickyDragContainer, canceled: true);
        }

        private void StickyNote_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var container = sender is Button button ? button.Parent as Grid : sender as Grid;
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
                return;
            BeginStickyPointer(container, new PointD(point.Position.X, point.Position.Y),
                e.Pointer.PointerId);
            if (ReferenceEquals(_stickyDragContainer, container))
            {
                // Capture on the button — the element whose Moved/Released
                // handlers drive the drag. Capturing on the parent container
                // would route the stream past the button whenever ButtonBase
                // skips its own internal capture (the press is marked
                // handled), which would silently kill drag + activation.
                (sender as UIElement)?.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void StickyNote_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var container = sender is Button button ? button.Parent as Grid : sender as Grid;
            if (_stickyDragContainer == null || !ReferenceEquals(_stickyDragContainer, container)
                || e.Pointer.PointerId != _stickyDragPointerId)
            {
                return;
            }
            var point = e.GetCurrentPoint(this);
            UpdateStickyPointer(container, new PointD(point.Position.X, point.Position.Y));
            e.Handled = true;
        }

        private void StickyNote_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            var container = sender is Button button ? button.Parent as Grid : sender as Grid;
            if (_stickyDragContainer == null || !ReferenceEquals(_stickyDragContainer, container)
                || e.Pointer.PointerId != _stickyDragPointerId)
            {
                return;
            }
            EndStickyPointer(container, canceled: false);
            e.Handled = true;
        }

        private void StickyNote_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            var container = sender is Button button ? button.Parent as Grid : sender as Grid;
            if (!_suppressStickyCaptureCancellation
                && container != null
                && ReferenceEquals(_stickyDragContainer, container))
            {
                EndStickyPointer(container, canceled: true);
            }
        }

        private void StickyNote_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            var container = sender is Button button ? button.Parent as Grid : sender as Grid;
            if (container != null
                && ReferenceEquals(_stickyDragContainer, container)
                && (_stickyDragPointerId == null
                    || e.Pointer.PointerId == _stickyDragPointerId.Value))
            {
                EndStickyPointer(container, canceled: true);
            }
        }

        private void StickyNote_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var container = sender is Button button ? button.Parent as Grid : sender as Grid;
            if (!IsStickyNoteContainer(container)
                || GetOverlayData(container) is not StickyNoteAnnotation note)
            {
                return;
            }

            if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space)
            {
                StickyNoteActivated?.Invoke(this, container);
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Delete)
            {
                StickyNoteDeleteRequested?.Invoke(this, container);
                e.Handled = true;
                return;
            }

            double step = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down) ? 16.0 : 4.0;
            double dx = 0;
            double dy = 0;
            switch (e.Key)
            {
                case VirtualKey.Left: dx = -step; break;
                case VirtualKey.Right: dx = step; break;
                case VirtualKey.Up: dy = -step; break;
                case VirtualKey.Down: dy = step; break;
                default: return;
            }

            var oldPosition = new PointD(note.X, note.Y);
            if (!SetStickyNotePositionQuiet(container,
                    new PointD(oldPosition.X + dx, oldPosition.Y + dy)))
            {
                return;
            }

            var newPosition = new PointD(note.X, note.Y);
            if (Math.Abs(newPosition.X - oldPosition.X) > 0.01
                || Math.Abs(newPosition.Y - oldPosition.Y) > 0.01)
            {
                StickyNoteMoved?.Invoke(this,
                    new StickyNoteMovedEventArgs(container, oldPosition, newPosition));
            }
            e.Handled = true;
        }

        // ── Annotation rotation helpers (WPF Apply/ReadAnnotationRotation) ─

        /// <summary>Applies a normalized centre-origin RenderTransform rotation.</summary>
        public static void ApplyAnnotationRotation(FrameworkElement element, double degrees)
        {
            if (element == null)
                return;

            element.RenderTransformOrigin = new Point(0.5, 0.5);
            double normalized = AnnotationRotation.NormalizeDegrees(degrees);
            element.RenderTransform = Math.Abs(normalized) < 0.01
                ? null
                : new RotateTransform { Angle = normalized };
        }

        /// <summary>Reads the normalized rotation from the element's RenderTransform.</summary>
        public static double ReadAnnotationRotation(FrameworkElement element)
        {
            return element?.RenderTransform is RotateTransform rotate
                ? AnnotationRotation.NormalizeDegrees(rotate.Angle)
                : 0;
        }

        /// <summary>
        /// Axis-aligned bounds of a container accounting for its rotation
        /// (rotated corners projected back to an axis-aligned rect).
        /// </summary>
        private static Rect GetContainerAxisAlignedBounds(FrameworkElement container)
        {
            var rectD = GetContainerRect(container);
            double rotation = ReadAnnotationRotation(container);
            if (Math.Abs(rotation) < 0.01)
                return new Rect(rectD.X, rectD.Y, rectD.Width, rectD.Height);

            var center = new PointD(rectD.X + rectD.Width / 2, rectD.Y + rectD.Height / 2);
            var corners = new[]
            {
                new PointD(rectD.X, rectD.Y),
                new PointD(rectD.X + rectD.Width, rectD.Y),
                new PointD(rectD.X + rectD.Width, rectD.Y + rectD.Height),
                new PointD(rectD.X, rectD.Y + rectD.Height),
            };
            var rotated = corners.Select(c => AnnotationRotation.RotatePoint(c, center, rotation)).ToList();
            double minX = rotated.Min(p => p.X), maxX = rotated.Max(p => p.X);
            double minY = rotated.Min(p => p.Y), maxY = rotated.Max(p => p.Y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        // ── Text-overlay / page-background press forwarding ──────────────

        private void TextOverlayCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Only forward clicks directly on the canvas background, not on
            // child elements like TextBoxes (WPF parity).
            if (ReferenceEquals(e.OriginalSource, TextOverlayCanvas))
                TextOverlayPointerPressed?.Invoke(this, e);
        }

        private void PageGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_currentMode != CustomInkInputProcessingMode.None
                || !_hostActive || !_documentInputEnabled)
            {
                return;
            }
            if (e.OriginalSource is DependencyObject source
                && IsDescendantOf(source, TextOverlayCanvas)
                && !ReferenceEquals(source, TextOverlayCanvas))
            {
                return;
            }
            BackgroundPointerPressed?.Invoke(this, e);
        }

        private static bool IsDescendantOf(DependencyObject descendant, DependencyObject ancestor)
        {
            var current = descendant;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        // ── Text-container layout flags + collectors ─────────────────────

        /// <summary>
        /// Persist-as-auto flags for a text container — stored as attached
        /// DPs on the element (WPF EditorPage attached-property parity) so
        /// quiet remove/add and cross-page moves preserve them for free.
        /// </summary>
        public void SetTextAutoSize(Grid container, bool autoWidth, bool autoHeight)
        {
            if (container == null)
                return;
            SetTextAnnotationAutoWidth(container, autoWidth);
            SetTextAnnotationAutoHeight(container, autoHeight);
        }

        public bool IsTextAnnotationAutoWidth(Grid container)
            => GetTextAnnotationAutoWidth(container);

        public bool IsTextAnnotationAutoHeight(Grid container)
            => GetTextAnnotationAutoHeight(container);

        /// <summary>
        /// Live text-annotation collector (WPF CollectAnnotations parity):
        /// rebuilds a TextAnnotation per container from the current TextBox
        /// state — always in sync with what the user sees.
        /// </summary>
        public List<TextAnnotation> GetTextData()
        {
            var annotations = new List<TextAnnotation>();
            foreach (var element in TextOverlayCanvas.Children)
            {
                if (element is Grid container
                    && TryGetTextAnnotation(container) is TextAnnotation annotation)
                {
                    annotations.Add(annotation);
                }
            }
            return annotations;
        }

        /// <summary>
        /// Builds the live <see cref="TextAnnotation"/> for ONE container —
        /// null when the container carries no editable TextBox (WPF
        /// CopySelection's per-container snapshot path).
        /// </summary>
        public TextAnnotation TryGetTextAnnotation(Grid container)
        {
            var textBox = container?.Children.OfType<TextBox>().FirstOrDefault();
            if (textBox == null)
                return null;

            var bounds = GetContainerRect(container);
            var foreground = textBox.Foreground as SolidColorBrush;
            return new TextAnnotation
            {
                X = bounds.X,
                Y = bounds.Y,
                Text = textBox.Text ?? string.Empty,
                R = foreground?.Color.R ?? 0,
                G = foreground?.Color.G ?? 0,
                B = foreground?.Color.B ?? 0,
                FontSize = textBox.FontSize,
                Width = IsTextAnnotationAutoWidth(container) ? 0 : bounds.Width,
                Height = IsTextAnnotationAutoHeight(container) ? 0 : bounds.Height,
                Bold = textBox.FontWeight.Weight >= Microsoft.UI.Text.FontWeights.Bold.Weight,
                Italic = textBox.FontStyle == Windows.UI.Text.FontStyle.Italic,
                FontFamily = textBox.FontFamily?.Source ?? "Segoe UI",
                Alignment = textBox.TextAlignment.ToString(),
                RotationDegrees = ReadAnnotationRotation(container),
            };
        }

        /// <summary>
        /// Live sticky-note collector (WPF CollectAnnotations parity):
        /// clones each marker's payload with the current container geometry.
        /// </summary>
        public List<StickyNoteAnnotation> GetStickyNoteData()
        {
            var notes = new List<StickyNoteAnnotation>();
            foreach (var container in GetOverlayContainers())
            {
                if (GetOverlayData(container) is not StickyNoteAnnotation note)
                    continue;
                var left = Canvas.GetLeft(container);
                var top = Canvas.GetTop(container);
                notes.Add(new StickyNoteAnnotation
                {
                    Id = note.Id,
                    X = double.IsNaN(left) ? note.X : left,
                    Y = double.IsNaN(top) ? note.Y : top,
                    Text = note.Text ?? string.Empty,
                    Width = container.Width > 0 ? container.Width : note.Width,
                    Height = container.Height > 0 ? container.Height : note.Height,
                    R = note.R,
                    G = note.G,
                    B = note.B,
                    RotationDegrees = ReadAnnotationRotation(container),
                });
            }
            return notes;
        }

        // ==================================================================
        // Image annotations + text markups + area highlights (Task 8 Phase B)
        // ==================================================================

        /// <summary>
        /// Decodes encoded image bytes (PNG/JPEG/etc.) and drops a container
        /// on <see cref="ImageOverlayCanvas"/> at the clamped position — WPF
        /// AddImage parity. Without explicit dimensions the bitmap fits into
        /// 40% of the page; the raw bytes stay in <see cref="_imageDataById"/>
        /// for the collector/save path. Returns null for undecodable input.
        /// </summary>
        public async Task<Grid> AddImageAsync(
            byte[] imageBytes, Point position,
            double? explicitWidth = null, double? explicitHeight = null)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return null;

            BitmapImage bitmap;
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(imageBytes.AsBuffer());
                stream.Seek(0);
                bitmap = new BitmapImage();
                // Decode happens eagerly inside SetSourceAsync — the stream
                // can be disposed afterwards (WPF BitmapCacheOption.OnLoad).
                await bitmap.SetSourceAsync(stream);
            }
            catch
            {
                return null;
            }
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                return null;

            double pageWidth = ActualWidth > 0 ? ActualWidth : Width;
            double pageHeight = ActualHeight > 0 ? ActualHeight : Height;
            if (pageWidth <= 0 || pageHeight <= 0)
            {
                pageWidth = 1584;
                pageHeight = 2245;
            }

            double width, height;
            if (explicitWidth > 0 && explicitHeight > 0)
            {
                width = explicitWidth.Value;
                height = explicitHeight.Value;
            }
            else
            {
                double maxW = pageWidth * 0.4;
                double maxH = pageHeight * 0.4;
                double fit = Math.Min(maxW / bitmap.PixelWidth, maxH / bitmap.PixelHeight);
                width = Math.Max(1.0, bitmap.PixelWidth * fit);
                height = Math.Max(1.0, bitmap.PixelHeight * fit);
            }

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
            };

            var container = new Grid
            {
                Width = width,
                Height = height,
                Tag = ImageContainerTag,
                // SelectionOverlayCanvas owns image interaction; an image
                // visual must never block the page's drawing surface.
                IsHitTestVisible = false,
            };
            container.Children.Add(image);

            Canvas.SetLeft(container, Math.Max(0, Math.Min(position.X, Math.Max(0, pageWidth - width))));
            Canvas.SetTop(container, Math.Max(0, Math.Min(position.Y, Math.Max(0, pageHeight - height))));

            ImageOverlayCanvas.Children.Add(container);
            _imageContainers.Add(container);
            _imageDataById[container] = imageBytes;

            ImagesChanged?.Invoke(this, EventArgs.Empty);
            return container;
        }

        /// <summary>
        /// Underline / strike-out / squiggly visual as an overlay container —
        /// WPF AddTextMarkup: the lines are drawn once into an inner Canvas
        /// inside a Viewbox (Stretch=Fill) so corner-handle rescaling scales
        /// the drawing with zero re-render logic. The model (position +
        /// relative rects) rides in <see cref="_overlayData"/>.
        /// </summary>
        public Grid AddTextMarkup(TextMarkupAnnotation markup)
        {
            if (markup?.Rects == null || markup.Rects.Count == 0)
                return null;

            double minX = double.MaxValue, minY = double.MaxValue,
                   maxX = double.MinValue, maxY = double.MinValue;
            foreach (var rect in markup.Rects)
            {
                if (rect == null || rect.Length < 4)
                    continue;
                minX = Math.Min(minX, rect[0]);
                minY = Math.Min(minY, rect[1]);
                maxX = Math.Max(maxX, rect[0] + rect[2]);
                maxY = Math.Max(maxY, rect[1] + rect[3]);
            }
            if (minX > maxX)
                return null;

            double width = Math.Max(2.0, maxX - minX);
            double height = Math.Max(2.0, maxY - minY);
            var brush = new SolidColorBrush(Color.FromArgb(255, markup.R, markup.G, markup.B));

            var canvas = new Canvas { Width = width, Height = height };
            double lineThickness = Math.Max(1.4, height * 0.06);
            var kind = markup.ParsedKind;

            foreach (var rect in markup.Rects)
            {
                if (rect == null || rect.Length < 4)
                    continue;
                double x = rect[0] - minX;
                double y = rect[1] - minY;
                double w = Math.Max(1.0, rect[2]);
                double h = Math.Max(1.0, rect[3]);

                if (kind == TextMarkupKind.Squiggly)
                {
                    var zigzag = new Polyline
                    {
                        Stroke = brush,
                        StrokeThickness = lineThickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        IsHitTestVisible = false,
                    };
                    double baseline = y + h - lineThickness; // hug the text baseline
                    const double wavelength = 6.0;
                    const double amplitude = 1.6;
                    for (double px = x; px <= x + w + 0.01; px += wavelength / 2)
                    {
                        double phase = ((px - x) / (wavelength / 2)) % 2.0;
                        zigzag.Points.Add(new Point(px, baseline + (phase < 1.0 ? -amplitude : amplitude)));
                    }
                    canvas.Children.Add(zigzag);
                }
                else
                {
                    double lineY = kind == TextMarkupKind.StrikeOut
                        ? y + h / 2
                        : y + h - lineThickness; // underline hugs the baseline
                    canvas.Children.Add(new Line
                    {
                        X1 = x, Y1 = lineY,
                        X2 = x + w, Y2 = lineY,
                        Stroke = brush,
                        StrokeThickness = lineThickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false,
                    });
                }
            }

            var container = new Grid
            {
                Width = width,
                Height = height,
                Tag = MarkupContainerTag,
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                IsHitTestVisible = false,
            };
            container.Children.Add(new Viewbox
            {
                Stretch = Stretch.Fill,
                StretchDirection = StretchDirection.Both,
                IsHitTestVisible = false,
                Child = canvas,
            });

            Canvas.SetLeft(container, Math.Max(0, markup.X));
            Canvas.SetTop(container, Math.Max(0, markup.Y));
            ImageOverlayCanvas.Children.Add(container);
            _overlayData[container] = markup;
            ImagesChanged?.Invoke(this, EventArgs.Empty);
            return container;
        }

        /// <summary>
        /// Free-form rectangular area highlight — a Grid whose Background is
        /// the semi-transparent colour so it stretches automatically when the
        /// container is rescaled (WPF AddAreaHighlight).
        /// </summary>
        public Grid AddAreaHighlight(AreaHighlightAnnotation area)
        {
            if (area == null || area.Width <= 0 || area.Height <= 0)
                return null;

            var container = new Grid
            {
                Width = area.Width,
                Height = area.Height,
                Tag = AreaHighlightContainerTag,
                Background = new SolidColorBrush(Color.FromArgb(area.A, area.R, area.G, area.B)),
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(container, Math.Max(0, area.X));
            Canvas.SetTop(container, Math.Max(0, area.Y));
            ImageOverlayCanvas.Children.Add(container);
            _overlayData[container] = area;
            ImagesChanged?.Invoke(this, EventArgs.Empty);
            return container;
        }

        // ── Persistent text-quad highlights (Task 8 Phase B) ─────────────

        /// <summary>The persisted text-quad highlights on this page (WPF GetHighlights).</summary>
        public IReadOnlyList<HighlightAnnotation> GetHighlights() => _highlights;

        /// <summary>
        /// Creates + registers + renders a highlight from absolute rects —
        /// the text-selection commit path (WPF AddHighlightAnnotation, fixed
        /// 120 alpha).
        /// </summary>
        public HighlightAnnotation AddHighlightAnnotation(IReadOnlyList<Rect> rects, Color color)
        {
            var highlight = new HighlightAnnotation
            {
                R = color.R,
                G = color.G,
                B = color.B,
                A = 120, // Semi-transparent overlay
            };
            foreach (var r in rects)
                highlight.Rects.Add(new[] { r.X, r.Y, r.Width, r.Height });

            _highlights.Add(highlight);
            RenderHighlightVisual(highlight);
            return highlight;
        }

        /// <summary>Registers + renders an existing model (load/undo-redo path — WPF AddHighlight).</summary>
        public void AddHighlight(HighlightAnnotation highlight)
        {
            if (highlight == null)
                return;
            _highlights.Add(highlight);
            RenderHighlightVisual(highlight);
        }

        /// <summary>Deregisters a model and repaints the layer (WPF RemoveHighlight).</summary>
        public void RemoveHighlight(HighlightAnnotation highlight)
        {
            if (highlight == null)
                return;
            _highlights.Remove(highlight);
            RefreshHighlightsVisuals();
        }

        /// <summary>Repaints every persisted highlight (WPF RefreshHighlightsVisuals).</summary>
        public void RefreshHighlightsVisuals()
        {
            HighlightsCanvas.Children.Clear();
            foreach (var hl in _highlights)
                RenderHighlightVisual(hl);
        }

        private void RenderHighlightVisual(HighlightAnnotation highlight)
        {
            var brush = new SolidColorBrush(
                Color.FromArgb(highlight.A, highlight.R, highlight.G, highlight.B));
            foreach (var rectInfo in highlight.Rects)
            {
                if (rectInfo == null || rectInfo.Length < 4)
                    continue;
                var rect = new Rectangle
                {
                    Width = rectInfo[2],
                    Height = rectInfo[3],
                    Fill = brush,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(rect, rectInfo[0]);
                Canvas.SetTop(rect, rectInfo[1]);
                HighlightsCanvas.Children.Add(rect);
            }
        }

        // ── IAnnotationContainerHost (Core undo replay) ──────────────────

        bool IAnnotationContainerHost.RemoveTextContainerQuiet(object container)
            => RemoveTextContainerQuiet(container as Grid);

        void IAnnotationContainerHost.AddTextContainerQuiet(object container)
            => AddTextContainerQuiet(container as Grid);

        bool IAnnotationContainerHost.ContainsTextContainer(object container)
            => container is Grid grid
                && (ReferenceEquals(grid.Parent, ImageOverlayCanvas)
                    || ReferenceEquals(grid.Parent, TextOverlayCanvas));

        object IAnnotationContainerHost.GetOverlayData(object container)
            => GetOverlayData(container as Grid);

        void IAnnotationContainerHost.SetOverlayData(object container, object data)
            => SetOverlayData(container as Grid, data);

        byte[] IAnnotationContainerHost.GetImageData(object container)
            => GetImageData(container as Grid);

        void IAnnotationContainerHost.SetImageData(object container, byte[] data)
            => SetImageData(container as Grid, data);

        void IAnnotationContainerHost.AddHighlight(HighlightAnnotation highlight)
            => AddHighlight(highlight);

        void IAnnotationContainerHost.RemoveHighlight(HighlightAnnotation highlight)
            => RemoveHighlight(highlight);

        bool IAnnotationContainerHost.SetStickyNotePositionQuiet(object container, PointD position)
            => SetStickyNotePositionQuiet(container as Grid, position);

        bool IAnnotationContainerHost.SetStickyNoteTextQuiet(object container, string text)
            => SetStickyNoteTextQuiet(container as Grid, text);

        void IAnnotationContainerHost.SetTextContainerPositionQuiet(object container, PointD position)
        {
            if (container is not Grid grid)
                return;
            Canvas.SetLeft(grid, position.X);
            Canvas.SetTop(grid, position.Y);
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        void IAnnotationContainerHost.SetTextContainerBoundsQuiet(
            object container, TextBoxBounds bounds, bool? autoWidth, bool? autoHeight)
        {
            if (container is not Grid grid)
                return;
            ApplyTextContainerBoundsQuiet(grid, bounds, autoWidth, autoHeight);
        }

        /// <summary>
        /// Normalizes + clamps bounds, stores auto-size flags and applies the
        /// canvas geometry — the quiet counterpart of the editor's
        /// ApplyTextContainerBounds (undo/redo path).
        /// </summary>
        private void ApplyTextContainerBoundsQuiet(
            Grid container, TextBoxBounds bounds, bool? autoWidth, bool? autoHeight)
        {
            bounds = TextAnnotationGeometry.Normalize(bounds);
            bool aw = autoWidth ?? IsTextAnnotationAutoWidth(container);
            bool ah = autoHeight ?? IsTextAnnotationAutoHeight(container);
            SetTextAutoSize(container, aw, ah);

            Canvas.SetLeft(container, bounds.X);
            Canvas.SetTop(container, bounds.Y);
            // Auto-size legs stay NaN so the container keeps sizing to the
            // TextBox content — the editor's live path does the same (a
            // fixed restore would freeze an auto annotation forever).
            container.Width = aw ? double.NaN : bounds.Width;
            container.Height = ah ? double.NaN : bounds.Height;

            var textBox = container.Children.OfType<TextBox>().FirstOrDefault();
            if (textBox != null)
            {
                textBox.MaxWidth = double.PositiveInfinity;
                textBox.Width = double.NaN;
                textBox.Height = double.NaN;
            }

            container.InvalidateMeasure();
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        bool IAnnotationContainerHost.SetTextContentQuiet(object container, string text)
        {
            if (container is not Grid grid)
                return false;
            var textBox = grid.Children.OfType<TextBox>().FirstOrDefault();
            if (textBox == null)
                return false;
            textBox.Text = text ?? string.Empty;
            InkMutated?.Invoke(this, EventArgs.Empty);
            return true;
        }

        void IAnnotationContainerHost.SetTextStyleQuiet(
            object container, double fontSize, byte r, byte g, byte b)
        {
            if (container is not Grid grid)
                return;
            var textBox = grid.Children.OfType<TextBox>().FirstOrDefault();
            if (textBox == null)
                return;
            textBox.FontSize = fontSize;
            textBox.Foreground = new SolidColorBrush(Color.FromArgb(255, r, g, b));
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        void IAnnotationContainerHost.SetTextFormatQuiet(object container, TextFormatSnapshot format)
        {
            if (container is not Grid grid)
                return;
            var textBox = grid.Children.OfType<TextBox>().FirstOrDefault();
            if (textBox == null)
                return;
            textBox.FontWeight = format.Bold
                ? Microsoft.UI.Text.FontWeights.Bold
                : Microsoft.UI.Text.FontWeights.Normal;
            textBox.FontStyle = format.Italic
                ? Windows.UI.Text.FontStyle.Italic
                : Windows.UI.Text.FontStyle.Normal;
            if (!string.IsNullOrWhiteSpace(format.FontFamily))
                textBox.FontFamily = new FontFamily(format.FontFamily);
            if (Enum.TryParse(format.Alignment, out TextAlignment alignment))
                textBox.TextAlignment = alignment;
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        void IAnnotationContainerHost.MoveItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double deltaX, double deltaY)
            => MoveItemsDirectly(strokes, containers?.OfType<Grid>().ToList(), deltaX, deltaY);

        void IAnnotationContainerHost.ScaleItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double scaleFactor, PointD center)
            => ScaleItemsDirectly(strokes, containers?.OfType<Grid>().ToList(), scaleFactor, center);

        void IAnnotationContainerHost.RotateItemsDirectly(
            IReadOnlyList<InkStrokeData> strokes, IReadOnlyList<object> containers,
            double degrees, PointD center)
            => RotateItemsDirectly(strokes, containers?.OfType<Grid>().ToList(), degrees, center);

        void IAnnotationContainerHost.ClearSelection() => ClearSelection();

        // ==================================================================
        // Shape tool (Phase B)
        // ==================================================================

        // The surface shares one drag pipeline between the Shape tool and
        // the AreaHighlight tool (InkSurfaceTool.Shape / AreaHighlight) —
        // the page routes by its input mode so only the armed gesture runs.

        private void Ink_ShapeDragStarted(object sender, ShapeDragEventArgs e)
        {
            if (_currentMode == CustomInkInputProcessingMode.AreaHighlight)
            {
                BeginAreaHighlightDrag(e.Anchor);
                return;
            }
            _isShapeDragging = true;
            _shapeAnchor = e.Anchor;
            _shapeCurrent = e.Current;
            _shapeShiftHeld = e.ShiftHeld;
            ClearShapePreview();
        }

        private void Ink_ShapeDragUpdated(object sender, ShapeDragEventArgs e)
        {
            if (_isAreaHighlightDragging)
            {
                UpdateAreaHighlightDrag(e.Current);
                return;
            }
            if (!_isShapeDragging)
                return;
            _shapeCurrent = e.Current;
            _shapeShiftHeld = e.ShiftHeld;
            UpdateShapePreview();
        }

        private void Ink_ShapeDragEnded(object sender, ShapeDragEventArgs e)
        {
            if (_isAreaHighlightDragging)
            {
                EndAreaHighlightDrag(e.Current);
                return;
            }
            if (!_isShapeDragging)
                return;
            _isShapeDragging = false;
            _shapeCurrent = e.Current;
            _shapeShiftHeld = e.ShiftHeld;
            ClearShapePreview();

            // WPF parity: sub-threshold drags are taps — nothing commits.
            if (StrokeGeometry.Dist(_shapeAnchor, _shapeCurrent) < ShapeStrokeFactory.DragThreshold)
                return;
            CommitShape();
        }

        private void Ink_ShapeDragCancelled(object sender, EventArgs e)
        {
            if (_isAreaHighlightDragging)
            {
                _isAreaHighlightDragging = false;
                ClearAreaHighlightPreview();
            }
            _isShapeDragging = false;
            ClearShapePreview();
        }

        // ==================================================================
        // Area highlight (Task 8 Phase B) — drag-to-rect on ShapePreviewCanvas;
        // the committed annotation is an ImageOverlayCanvas container (WPF
        // Begin/Update/EndAreaHighlightDrag parity).
        // ==================================================================

        private void BeginAreaHighlightDrag(PointD position)
        {
            _isAreaHighlightDragging = true;
            _areaHighlightAnchor = position;

            _areaHighlightPreview = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromArgb(220,
                    AreaHighlightColor.R, AreaHighlightColor.G, AreaHighlightColor.B)),
                Fill = new SolidColorBrush(Color.FromArgb(AreaHighlightOpacity,
                    AreaHighlightColor.R, AreaHighlightColor.G, AreaHighlightColor.B)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                IsHitTestVisible = false,
            };
            ShapePreviewCanvas.Children.Add(_areaHighlightPreview);
            UpdateAreaHighlightDrag(position);
        }

        private void UpdateAreaHighlightDrag(PointD position)
        {
            if (!_isAreaHighlightDragging || _areaHighlightPreview == null)
                return;
            var rect = NormalizeAreaHighlightRect(_areaHighlightAnchor, position);
            Canvas.SetLeft(_areaHighlightPreview, rect.X);
            Canvas.SetTop(_areaHighlightPreview, rect.Y);
            _areaHighlightPreview.Width = rect.Width;
            _areaHighlightPreview.Height = rect.Height;
        }

        private void EndAreaHighlightDrag(PointD position)
        {
            if (!_isAreaHighlightDragging)
                return;
            var rect = NormalizeAreaHighlightRect(_areaHighlightAnchor, position);
            _isAreaHighlightDragging = false;
            ClearAreaHighlightPreview();

            if (rect.Width < AreaHighlightDragThreshold || rect.Height < AreaHighlightDragThreshold)
                return;

            var container = AddAreaHighlight(new AreaHighlightAnnotation
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                R = AreaHighlightColor.R,
                G = AreaHighlightColor.G,
                B = AreaHighlightColor.B,
                A = AreaHighlightOpacity,
            });
            if (container != null)
                AreaHighlightCreated?.Invoke(this, container);
        }

        private void ClearAreaHighlightPreview()
        {
            if (_areaHighlightPreview != null)
            {
                ShapePreviewCanvas.Children.Remove(_areaHighlightPreview);
                _areaHighlightPreview = null;
            }
        }

        /// <summary>
        /// Live preview polylines on ShapePreviewCanvas — Shift constraints
        /// are applied to the preview exactly as at commit time, and the
        /// ruler constraint hides a segment that enters the ruler body.
        /// </summary>
        private void UpdateShapePreview()
        {
            ClearShapePreview();
            var end = StrokeGeometry.ConstrainShapeEndpoints(
                _shapeAnchor, _shapeCurrent, CurrentShape, _shapeShiftHeld);
            var segments = ShapeStrokeFactory.BuildPreviewPolylines(
                CurrentShape, _shapeAnchor, end, ShapeStrokeSize);

            bool dashed = ShapeIsDashed || CurrentShape == InkShapeKind.DashedLine;
            var strokeBrush = new SolidColorBrush(ShapeColor);
            foreach (var segment in segments)
            {
                var constrained = ApplyRulerToShapeSegment(segment);
                if (constrained == null)
                    continue;
                var polyline = new Polyline
                {
                    Stroke = strokeBrush,
                    StrokeThickness = ShapeStrokeSize,
                    IsHitTestVisible = false,
                };
                if (dashed)
                    polyline.StrokeDashArray = new DoubleCollection { 4, 2 };
                foreach (var p in constrained)
                    polyline.Points.Add(new Point(p.X, p.Y));
                _shapePreviewPolylines.Add(polyline);
                ShapePreviewCanvas.Children.Add(polyline);
            }
        }

        private void ClearShapePreview()
        {
            _shapePreviewPolylines.Clear();
            ShapePreviewCanvas.Children.Clear();
        }

        /// <summary>
        /// Ruler constraint for shape segments — open kinds only (Line/Arrow/
        /// DashedLine); a segment that starts inside or enters the ruler body
        /// is dropped (null), a near-edge segment is projected onto the edge.
        /// </summary>
        private IReadOnlyList<PointD> ApplyRulerToShapeSegment(IReadOnlyList<PointD> segment)
        {
            var ruler = GetRulerGeometryInPageCoords?.Invoke();
            if (!ruler.HasValue || !ShapeStrokeFactory.IsOpenKind(CurrentShape))
                return segment;

            var inkPoints = segment
                .Select(p => new InkPointData(p.X, p.Y, 0.5f))
                .ToList();
            var constrained = StrokeGeometry.ConstrainPointsToRuler(
                inkPoints, ruler.Value.TopA, ruler.Value.TopB,
                ruler.Value.BottomA, ruler.Value.BottomB, InkSurface.RulerSnapTolerance);
            if (constrained == null || constrained.Count < 2)
                return null;
            return constrained.Select(p => new PointD(p.X, p.Y)).ToList();
        }

        /// <summary>
        /// Bakes the shape into the store via the Core factory — shared group
        /// id, part indices, dashed flag — then reports the commit so the
        /// editor pushes one undo action and selects the group.
        /// </summary>
        private void CommitShape()
        {
            var end = StrokeGeometry.ConstrainShapeEndpoints(
                _shapeAnchor, _shapeCurrent, CurrentShape, _shapeShiftHeld);
            var strokes = ShapeStrokeFactory.BuildShapeStrokes(
                CurrentShape, _shapeAnchor, end,
                ShapeColor.R, ShapeColor.G, ShapeColor.B, ShapeColor.A,
                ShapeStrokeSize, ShapeIsDashed,
                ApplyRulerToShapeSegment);

            if (strokes.Count == 0)
                return;

            foreach (var stroke in strokes)
                InkSurface.Store.AddStrokeQuiet(stroke);
            InkMutated?.Invoke(this, EventArgs.Empty);
            ShapeCommittedUndoable?.Invoke(this, strokes);
        }

        // ==================================================================
        // Hidden ink (Phase B)
        // ==================================================================

        private void Ink_HiddenInkStrokeCommitted(object sender, IReadOnlyList<PointD> points)
        {
            if (points == null || points.Count == 0)
                return;

            var annotation = new HiddenInkAnnotation
            {
                Id = Guid.NewGuid().ToString("N"),
                R = HiddenInkMaskColor.R,
                G = HiddenInkMaskColor.G,
                B = HiddenInkMaskColor.B,
                A = 255,
                Size = HiddenInkSize,
                RevealDurationMs = HiddenInkRevealDurationMs > 0
                    ? HiddenInkRevealDurationMs
                    : HiddenInkRevealState.DefaultRevealDurationMs,
                Points = points
                    .Select(p => new double[] { p.X, p.Y })
                    .ToList(),
            };

            HiddenInkStore.AddQuiet(annotation);
            HiddenInkCreated?.Invoke(this, annotation);
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Quiet load path — sidecar masks enter without events.</summary>
        public void AddHiddenInk(HiddenInkAnnotation annotation)
            => HiddenInkStore.AddQuiet(annotation);

        /// <summary>Serializes the live masks for persistence.</summary>
        public List<HiddenInkAnnotation> GetHiddenInkData()
            => HiddenInkStore.Items
                .Select(CloneHiddenInk)
                .ToList();

        private static HiddenInkAnnotation CloneHiddenInk(HiddenInkAnnotation source) => new()
        {
            Id = source.Id,
            R = source.R,
            G = source.G,
            B = source.B,
            A = source.A,
            Size = source.Size,
            RevealDurationMs = source.RevealDurationMs,
            Points = source.Points?.Select(p => (double[])p.Clone()).ToList() ?? new List<double[]>(),
        };

        // ── Hidden-ink visuals ───────────────────────────────────────────

        private void RebuildHiddenInkVisuals()
        {
            HiddenInkCanvas.Children.Clear();
            _hiddenInkVisuals.Clear();
            foreach (var annotation in HiddenInkStore.Items)
                AddHiddenInkVisual(annotation);
        }

        private void AddHiddenInkVisual(HiddenInkAnnotation annotation, int insertIndex = -1)
        {
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(255, annotation.R, annotation.G, annotation.B)),
                StrokeThickness = annotation.Size,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Tag = annotation.Id,
            };
            if (annotation.Points != null)
            {
                foreach (var pt in annotation.Points)
                {
                    if (pt == null || pt.Length < 2)
                        continue;
                    polyline.Points.Add(new Point(pt[0], pt[1]));
                }
                // A 1-point tap still renders as a dot (WPF PreserveTap parity).
                if (polyline.Points.Count == 1)
                {
                    var p = polyline.Points[0];
                    polyline.Points.Add(new Point(p.X + 0.1, p.Y + 0.1));
                }
            }
            polyline.PointerPressed += HiddenInkVisual_PointerPressed;
            polyline.IsHitTestVisible = IsHiddenInkInteractive();
            _hiddenInkVisuals[annotation.Id] = polyline;
            // Undo restores InsertQuiet at a captured index — keep the visual
            // z-order aligned with the store instead of always appending.
            if (insertIndex >= 0 && insertIndex < HiddenInkCanvas.Children.Count)
                HiddenInkCanvas.Children.Insert(insertIndex, polyline);
            else
                HiddenInkCanvas.Children.Add(polyline);
        }

        private bool IsHiddenInkInteractive()
            => _hostActive && _documentInputEnabled
                && _currentMode != CustomInkInputProcessingMode.Erasing;

        private void UpdateHiddenInkHitTesting()
        {
            bool interactive = IsHiddenInkInteractive();
            foreach (var visual in _hiddenInkVisuals.Values)
                visual.IsHitTestVisible = interactive;
        }

        private void HiddenInkVisual_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Polyline visual || visual.Tag is not string id)
                return;
            var annotation = HiddenInkStore.Items
                .FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
            if (annotation == null)
                return;

            e.Handled = true;
            // WPF HandleHiddenInkVisualPress also removed the mask here in
            // Erasing mode — that branch is dead on WinUI because the visual
            // is non-hit-testable while erasing (IsHiddenInkInteractive);
            // erasing-mode removal happens through the swept-eraser path
            // (Ink_EraserPathUpdated). A press therefore always reveals.
            RevealHiddenInk(annotation, visual);
        }

        /// <summary>
        /// Click-reveal: the mask hides for its configured duration, then
        /// comes back (WPF RevealHiddenInk parity). A mask removed while
        /// revealed simply never reappears — the timer checks membership.
        /// </summary>
        private void RevealHiddenInk(HiddenInkAnnotation annotation, Polyline visual)
        {
            visual.Visibility = Visibility.Collapsed;
            visual.IsHitTestVisible = false;

            if (_hiddenInkRevealTimers.TryGetValue(annotation.Id, out var existing))
            {
                existing.Stop();
                _hiddenInkRevealTimers.Remove(annotation.Id);
            }

            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(
                Math.Max(50, annotation.RevealDurationMs));
            timer.IsRepeating = false;
            timer.Tick += (s, args) =>
            {
                _hiddenInkRevealTimers.Remove(annotation.Id);
                timer.Stop();
                if (_hiddenInkVisuals.TryGetValue(annotation.Id, out var current))
                {
                    current.Visibility = Visibility.Visible;
                    current.IsHitTestVisible = IsHiddenInkInteractive();
                }
            };
            _hiddenInkRevealTimers[annotation.Id] = timer;
            timer.Start();
        }

        /// <summary>Stops one mask's reveal timer (removed-mask path).</summary>
        private void StopHiddenInkRevealTimer(string id)
        {
            if (id == null)
                return;
            if (_hiddenInkRevealTimers.TryGetValue(id, out var timer))
            {
                timer.Stop();
                _hiddenInkRevealTimers.Remove(id);
            }
        }

        private void StopAllHiddenInkRevealTimers()
        {
            foreach (var timer in _hiddenInkRevealTimers.Values)
                timer.Stop();
            _hiddenInkRevealTimers.Clear();
            foreach (var visual in _hiddenInkVisuals.Values)
            {
                visual.Visibility = Visibility.Visible;
                visual.IsHitTestVisible = IsHiddenInkInteractive();
            }
        }

        // ── Hidden-ink erase integration ─────────────────────────────────

        private void Ink_EraserPathUpdated(object sender, EraserPathEventArgs e)
        {
            if (HiddenInkStore.Count == 0)
                return;
            var eraserRects = StrokeGeometry.CreateEraserRects(e.Path, e.EraserSize);
            foreach (var annotation in HiddenInkStore.Items.ToList())
            {
                if (!StrokeGeometry.HiddenInkIntersectsEraser(annotation, eraserRects, e.EraserSize))
                    continue;
                int index = HiddenInkStore.IndexOf(annotation);
                if (index < 0)
                    continue;
                var clone = CloneHiddenInk(annotation);
                HiddenInkStore.RemoveQuiet(annotation);
                (_eraseGestureRemovedHiddenInks ??= new()).Add((clone, index));
            }
        }

        private void Ink_EraseGestureCompleted(object sender, EventArgs e)
        {
            var removed = _eraseGestureRemovedHiddenInks;
            _eraseGestureRemovedHiddenInks = null;
            if (removed != null && removed.Count > 0)
                HiddenInksRemoved?.Invoke(this, new HiddenInksRemovedEventArgs(removed));
        }

        private void Ink_EraseGestureCancelled(object sender, EventArgs e)
        {
            var removed = _eraseGestureRemovedHiddenInks;
            _eraseGestureRemovedHiddenInks = null;
            if (removed == null)
                return;
            // Capture-loss rollback — masks go back at their indices quietly.
            foreach (var entry in removed.OrderBy(en => en.Index))
                HiddenInkStore.InsertQuiet(entry.Index, entry.Item);
        }

        // ==================================================================
        // Laser pointer (Phase B) — ephemeral, never stored/undo/persisted
        // ==================================================================

        private void Ink_LaserStrokeStarted(object sender, LaserStrokeEventArgs e)
        {
            _laserPolyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(255,
                    LaserInkFade.ColorR, LaserInkFade.ColorG, LaserInkFade.ColorB)),
                StrokeThickness = LaserInkFade.StrokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            foreach (var p in e.Points)
                _laserPolyline.Points.Add(new Point(p.X, p.Y));
            LaserInkCanvas.Children.Add(_laserPolyline);
            _liveLaserPolylines.Add(_laserPolyline);

            // WPF parity: cap the live set — the oldest drop at once.
            while (_liveLaserPolylines.Count > LaserInkFade.MaxLivePolylines)
            {
                var oldest = _liveLaserPolylines[0];
                _liveLaserPolylines.RemoveAt(0);
                _laserCompletedAt.Remove(oldest);
                LaserInkCanvas.Children.Remove(oldest);
            }
        }

        private void Ink_LaserStrokePointsAppended(object sender, LaserStrokeEventArgs e)
        {
            if (_laserPolyline == null)
                return;
            foreach (var p in e.Points)
                _laserPolyline.Points.Add(new Point(p.X, p.Y));
        }

        private void Ink_LaserStrokeCompleted(object sender, LaserStrokeEventArgs e)
        {
            if (_laserPolyline != null)
            {
                foreach (var p in e.Points)
                    _laserPolyline.Points.Add(new Point(p.X, p.Y));
                _laserCompletedAt[_laserPolyline] = DateTimeOffset.UtcNow;
                _laserPolyline = null;
                EnsureLaserFadeTimer();
            }
        }

        /// <summary>Ends the in-flight laser polyline without new points (tool switch).</summary>
        private void EndLaserStroke()
        {
            if (_laserPolyline == null)
                return;
            _laserCompletedAt[_laserPolyline] = DateTimeOffset.UtcNow;
            _laserPolyline = null;
            EnsureLaserFadeTimer();
        }

        private void EnsureLaserFadeTimer()
        {
            if (_laserFadeTimer != null)
                return;
            _laserFadeTimer = DispatcherQueue.CreateTimer();
            _laserFadeTimer.Interval = TimeSpan.FromMilliseconds(30);
            _laserFadeTimer.Tick += LaserFadeTimer_Tick;
            _laserFadeTimer.Start();
        }

        private void LaserFadeTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            var now = DateTimeOffset.UtcNow;
            bool animate = WinUiThemeService.ShouldAnimate;
            var expired = new List<Polyline>();
            foreach (var pair in _laserCompletedAt)
            {
                double elapsed = (now - pair.Value).TotalSeconds;
                if (LaserInkFade.IsExpired(elapsed, animate))
                {
                    expired.Add(pair.Key);
                    continue;
                }
                pair.Key.Opacity = LaserInkFade.GetOpacity(elapsed, animate);
            }
            foreach (var polyline in expired)
            {
                _laserCompletedAt.Remove(polyline);
                _liveLaserPolylines.Remove(polyline);
                LaserInkCanvas.Children.Remove(polyline);
            }
            if (_laserCompletedAt.Count == 0 && _laserFadeTimer != null)
            {
                _laserFadeTimer.Stop();
                _laserFadeTimer = null;
            }
        }
    }
}
