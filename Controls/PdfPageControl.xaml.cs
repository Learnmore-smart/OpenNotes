using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Ink;
using System.Runtime.CompilerServices;
using Caelum.Models;
using Caelum.Services;

namespace Caelum.Controls
{
    public enum CustomInkInputProcessingMode { None, Inking, Erasing, Shape, Laser, AreaHighlight, HiddenInk }

    public enum SelectionFilter { Both, DrawingsOnly, TextOnly }

    public enum SelectionShape { Rectangle, FreeForm }

    /// <summary>Sub-type of the shape tool (drag-to-draw shapes).</summary>
    public enum ShapeKind
    {
        Line,
        Rectangle,
        Ellipse,
        Arrow,
        Triangle,
        Diamond,
        Parallelogram,
        Pentagon,
        Hexagon
    }

    public sealed class SelectionMoveCompletedEventArgs : EventArgs
    {
        public SelectionMoveCompletedEventArgs(double deltaX, double deltaY, List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
        {
            DeltaX = deltaX;
            DeltaY = deltaY;
            SelectedStrokes = strokes;
            SelectedTextContainers = containers;
        }
        public double DeltaX { get; }
        public double DeltaY { get; }
        public List<System.Windows.Ink.Stroke> SelectedStrokes { get; }
        public List<System.Windows.Controls.Grid> SelectedTextContainers { get; }
    }

    public sealed class SelectionResizeCompletedEventArgs : EventArgs
    {
        public SelectionResizeCompletedEventArgs(double totalScale, Point anchor, List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
        {
            TotalScale = totalScale;
            Anchor = anchor;
            SelectedStrokes = strokes;
            SelectedTextContainers = containers;
        }
        public double TotalScale { get; }
        public Point Anchor { get; }
        public List<System.Windows.Ink.Stroke> SelectedStrokes { get; }
        public List<System.Windows.Controls.Grid> SelectedTextContainers { get; }
    }

    public sealed class PdfTextSelectionPointerEventArgs : EventArgs
    {
        public PdfTextSelectionPointerEventArgs(Point position, MouseButtonState leftButton)
        {
            Position = position;
            LeftButton = leftButton;
        }

        public Point Position { get; }
        public MouseButtonState LeftButton { get; }
    }

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
    /// Describes one completed Sticky Note marker move in page DIP coordinates.
    /// The editor turns this single pointer gesture into one undo action.
    /// </summary>
    public sealed class StickyNoteMovedEventArgs : EventArgs
    {
        public StickyNoteMovedEventArgs(Grid container, Point oldPosition, Point newPosition)
        {
            Container = container;
            OldPosition = oldPosition;
            NewPosition = newPosition;
        }

        public Grid Container { get; }
        public Point OldPosition { get; }
        public Point NewPosition { get; }
    }

    /// <summary>
    /// Payload describing the net effect of one erase gesture (stylus/mouse
    /// down → up). Eraser-mode agnostic: pixel-clip erasing reports the removed
    /// original strokes plus the fragment strokes created by clipping, while a
    /// whole-stroke eraser would report removed strokes with no fragments.
    /// </summary>
    public sealed class StrokesErasedEventArgs : EventArgs
    {
        public StrokesErasedEventArgs(List<System.Windows.Ink.Stroke> removedStrokes, List<System.Windows.Ink.Stroke> addedStrokes)
            : this(removedStrokes, addedStrokes, null, null)
        {
        }

        public StrokesErasedEventArgs(
            List<System.Windows.Ink.Stroke> removedStrokes,
            List<System.Windows.Ink.Stroke> addedStrokes,
            IReadOnlyList<StrokePlacement> removedPlacements,
            IReadOnlyList<StrokePlacement> addedPlacements)
        {
            RemovedStrokes = removedStrokes ?? new List<System.Windows.Ink.Stroke>();
            AddedStrokes = addedStrokes ?? new List<System.Windows.Ink.Stroke>();
            RemovedPlacements = removedPlacements ?? Array.Empty<StrokePlacement>();
            AddedPlacements = addedPlacements ?? Array.Empty<StrokePlacement>();
        }

        public List<System.Windows.Ink.Stroke> RemovedStrokes { get; }
        public List<System.Windows.Ink.Stroke> AddedStrokes { get; }
        public IReadOnlyList<StrokePlacement> RemovedPlacements { get; }
        public IReadOnlyList<StrokePlacement> AddedPlacements { get; }
    }

    /// <summary>
    /// Stable placement identity for one live stroke. The reference is kept
    /// only by ordinary erase/delete/move actions; shape replacement history
    /// remains snapshot-only. Token, side, index, and owner travel together
    /// so re-adding a stroke cannot silently append it or change its page.
    /// </summary>
    public sealed class StrokePlacement
    {
        public StrokePlacement(
            PdfPageControl owner,
            Stroke stroke,
            StrokeReplacementSnapshot snapshot,
            int index)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Index = index;
        }

        public PdfPageControl Owner { get; }
        public Stroke Stroke { get; }
        public StrokeReplacementSnapshot Snapshot { get; }
        public Guid Token => Snapshot.Token;
        public StrokeReplacementSide Side => Snapshot.Side;
        public int Index { get; }

        public StrokePlacement ForOwner(PdfPageControl owner, int index)
        {
            return new StrokePlacement(owner, Stroke, Snapshot, index);
        }
    }

    /// <summary>
    /// Payload for the hidden masks removed by one eraser gesture. Keeping the
    /// masks together makes a drag across several answers one undoable action,
    /// matching the ordinary ink eraser behaviour.
    /// </summary>
    public sealed class HiddenInksRemovedEventArgs : EventArgs
    {
        public HiddenInksRemovedEventArgs(IReadOnlyList<HiddenInkAnnotation> annotations)
        {
            Annotations = annotations ?? Array.Empty<HiddenInkAnnotation>();
        }

        public IReadOnlyList<HiddenInkAnnotation> Annotations { get; }
    }

    /// <summary>
    /// Payload for a successful scribble shape recognition. The page has
    /// already replaced the original in place; consumers receive only copied
    /// token/snapshot data so undo never retains live WPF strokes. Recognition
    /// raised by InkCanvas collection is a fresh gesture; the discriminator is
    /// retained so a future true replacement can keep snapshot-only history.
    /// </summary>
    public sealed class StrokeRecognizedEventArgs : EventArgs
    {
        public StrokeRecognizedEventArgs(
            Guid token,
            int originalIndex,
            StrokeReplacementSnapshot originalSnapshot,
            StrokeReplacementSnapshot idealSnapshot,
            bool isFreshStroke = false)
        {
            Token = token;
            OriginalIndex = originalIndex;
            OriginalSnapshot = originalSnapshot ?? throw new ArgumentNullException(nameof(originalSnapshot));
            IdealSnapshot = idealSnapshot ?? throw new ArgumentNullException(nameof(idealSnapshot));
            IsFreshStroke = isFreshStroke;
        }

        public Guid Token { get; }
        public int OriginalIndex { get; }
        public StrokeReplacementSnapshot OriginalSnapshot { get; }
        public StrokeReplacementSnapshot IdealSnapshot { get; }
        public bool IsFreshStroke { get; }
    }

    public sealed partial class PdfPageControl : UserControl, IInteractionCancellation
    {
        public static readonly DependencyProperty PageSourceProperty =
            DependencyProperty.Register(nameof(PageSource), typeof(BitmapSource), typeof(PdfPageControl), new PropertyMetadata(null, OnPageSourceChanged));

        public BitmapSource PageSource
        {
            get => (BitmapSource)GetValue(PageSourceProperty);
            set => SetValue(PageSourceProperty, value);
        }

        /// <summary>
        /// Pauses transient, frame-driven page effects while the owning editor
        /// is not the active tab. Persistent annotations are left untouched.
        /// </summary>
        public void SetHostActive(bool isActive)
        {
            if (!isActive)
                CancelInteraction("inactive host");

            if (_isHostActive == isActive)
                return;

            _isHostActive = isActive;
            if (!isActive)
            {
                StopSelectionDashAnimation();
                foreach (var visual in _hiddenInkVisuals.Values)
                    visual.Visibility = Visibility.Visible;
                StopAllHiddenInkRevealTimers();

                _isLaserDrawing = false;
                _laserPolyline = null;
                _liveLaserPolylines.Clear();
                LaserInkCanvas.Children.Clear();
                return;
            }

            if (HasSelection)
                UpdateSelectionVisuals();
        }

        /// <summary>
        /// True while this page owns a pointer/stylus gesture that has not yet
        /// reached its normal release path.
        /// </summary>
        public bool HasActiveInteraction =>
            _stickyDragContainer != null
            || _isSelecting
            || _isDraggingSelection
            || _isResizingSelection
            || _isShapeDragging
            || _isLaserDrawing
            || _isAreaHighlightDragging
            || _isErasing
            || PdfTextSelectionCanvas.IsMouseCaptured
            || PdfTextSelectionCanvas.IsStylusCaptured;

        /// <summary>
        /// Cancels every page-local transient gesture.  Transform snapshots
        /// are restored before capture is released so LostCapture re-entry
        /// cannot observe a half-applied move or emit a completion event.
        /// </summary>
        public void CancelInteraction(string reason = null)
        {
            _pendingPopupDismissalInkGesture = false;

            if (_stickyDragContainer != null)
                EndStickyPointer(_stickyDragContainer, canceled: true);

            CancelSelectionInteraction(restoreSnapshot: true);

            if (_isShapeDragging)
            {
                _isShapeDragging = false;
                ShapePreviewCanvas.Children.Clear();
                _shapePreviewPolylines.Clear();
                ReleaseInkCaptures();
            }

            if (_isAreaHighlightDragging)
            {
                _isAreaHighlightDragging = false;
                if (_areaHighlightPreview != null)
                    ShapePreviewCanvas.Children.Remove(_areaHighlightPreview);
                _areaHighlightPreview = null;
                ReleaseInkCaptures();
            }

            if (_isLaserDrawing)
            {
                _isLaserDrawing = false;
                _laserPolyline = null;
                LaserInkCanvas.Children.Clear();
                _liveLaserPolylines.Clear();
                ReleaseInkCaptures();
            }

            if (PdfTextSelectionCanvas.IsMouseCaptured)
                PdfTextSelectionCanvas.ReleaseMouseCapture();
            if (PdfTextSelectionCanvas.IsStylusCaptured)
                PdfTextSelectionCanvas.ReleaseStylusCapture();
            ClearPdfTextSelection();
        }

        private void ReleaseInkCaptures()
        {
            if (InkCanvas.IsMouseCaptured)
                InkCanvas.ReleaseMouseCapture();
            if (InkCanvas.IsStylusCaptured)
                InkCanvas.ReleaseStylusCapture();
        }

        /// <summary>
        /// Lets the editor favor cheaper interpolation during motion and
        /// restore sharper interpolation after scrolling or zooming settles.
        /// </summary>
        public void SetBitmapScalingMode(BitmapScalingMode scalingMode)
        {
            RenderOptions.SetBitmapScalingMode(PdfImage, scalingMode);
            RenderOptions.SetBitmapScalingMode(PdfImageOverlay, scalingMode);
        }

        public int PageIndex { get; set; }

        public Canvas TextOverlay => TextOverlayCanvas;

        public event EventHandler<MouseButtonEventArgs> TextOverlayPointerPressed;
        public event EventHandler<MouseButtonEventArgs> BackgroundPointerPressed;
        public event EventHandler<PdfTextSelectionPointerEventArgs> PdfTextSelectionPointerPressed;
        public event EventHandler<PdfTextSelectionPointerEventArgs> PdfTextSelectionPointerMoved;
        public event EventHandler<PdfTextSelectionPointerEventArgs> PdfTextSelectionPointerReleased;
        public event EventHandler InkMutated;
        /// <summary>Raised after a quiet undo/redo/delete stroke mutation. Unlike InkMutated, this never marks the document dirty or creates history.</summary>
        public event EventHandler QuietStrokeMutation;
        public event EventHandler<Stroke> StrokeCollectedUndoable;
        public event EventHandler<StrokesErasedEventArgs> StrokesErased;
        public event EventHandler<StrokeRecognizedEventArgs> StrokeRecognized;
        public event EventHandler<CustomInkInputProcessingMode> ModeChanged;
        public event EventHandler<AnnotationSelectionChangedEventArgs> SelectionChanged;
        public event EventHandler<SelectionMoveCompletedEventArgs> SelectionMoveCompleted;
        public event EventHandler<SelectionResizeCompletedEventArgs> SelectionResizeCompleted;
        /// <summary>Task 19: raised when an image annotation is added (EditorPage marks the document dirty).</summary>
        public event EventHandler ImagesChanged;
        /// <summary>Task 27: raised after an area-highlight drag commits; the editor pushes undo + dirty.</summary>
        public event EventHandler<Grid> AreaHighlightCreated;
        /// <summary>Task 26: raised when the user clicks a sticky-note icon (open the editing bubble).</summary>
        public event EventHandler<Grid> StickyNoteActivated;
        /// <summary>Task 26: raised after a sticky marker drag commits a bounded move.</summary>
        public event EventHandler<StickyNoteMovedEventArgs> StickyNoteMoved;
        /// <summary>Task 26: raised by Delete, the marker context menu, or keyboard deletion.</summary>
        public event EventHandler<Grid> StickyNoteDeleteRequested;
        /// <summary>Task 26: lets the owning editor register marker menus in its transient registry.</summary>
        public event EventHandler<ContextMenu> StickyNoteContextMenuCreated;
        /// <summary>Study mode: raised after one opaque hidden mask is committed.</summary>
        public event EventHandler<HiddenInkAnnotation> HiddenInkCreated;
        /// <summary>Study mode: raised when the eraser removes one mask.</summary>
        public event EventHandler<HiddenInkAnnotation> HiddenInkRemoved;
        /// <summary>Study mode: raised once after an eraser gesture removes one or more masks.</summary>
        public event EventHandler<HiddenInksRemovedEventArgs> HiddenInksRemoved;

        private DrawingAttributes _drawingAttributes;
        private CustomInkInputProcessingMode _currentMode = CustomInkInputProcessingMode.None;
        // Armed by EditorPage when an outside Pen/Highlighter popup click is
        // allowed to continue into this page. Native InkCanvas starts the
        // gesture before it knows whether the pointer will move; collection
        // time is the first boundary where a stationary tap can be removed
        // without publishing InkMutated or an undo action.
        private bool _pendingPopupDismissalInkGesture;
        private double _eraserSize = 20;
        private bool _isErasing;
        private StylusPointCollection _erasePoints;
        // Per-gesture erase accumulation (lazily initialised on first mutation,
        // flushed on stylus/mouse up) feeding the StrokesErased undo event.
        private List<Stroke> _eraseGestureRemovedStrokes;
        private List<Stroke> _eraseGestureAddedStrokes;
        private List<StrokePlacement> _eraseGestureRemovedPlacements;
        private List<StrokePlacement> _eraseGestureAddedPlacements;
        private List<HiddenInkAnnotation> _eraseGestureRemovedHiddenInks;
        private bool _isPdfTextSelectionEnabled;
        // Task 12.2: generation counter for the two-layer bitmap swap. Every
        // PageSource change bumps it; pending dispatcher callbacks compare
        // against the current value and no-op when a newer bitmap superseded
        // them (rapid zoom re-renders / scroll renders).
        private int _pageSourceSwapGeneration;

        // Selection transform state
        private bool _isSelectionMode;
        public bool IsSelectionMode => _isSelectionMode;
        private SelectionFilter _selectionFilter = SelectionFilter.Both;
        private SelectionShape _selectionShape = SelectionShape.Rectangle;
        private bool _isSelecting;
        private Point _selectionStartPoint;
        private System.Windows.Shapes.Rectangle _selectionRect;
        private System.Windows.Shapes.Polyline _freeSelectionPath;
        private System.Windows.Media.PointCollection _freeSelectionPoints;
        private List<Stroke> _selectedStrokes = new List<Stroke>();
        private List<Grid> _selectedTextContainers = new List<Grid>();
        // Task 19: image annotations live on ImageOverlayCanvas (below ink) as
        // Grid containers tagged ImageContainerTag, so they flow through the
        // existing selection pipeline as "container-like" items while being
        // tracked separately for save. _imageDataById keeps the raw encoded
        // bytes (PNG/JPEG) keyed by container instance; entries survive quiet
        // removals (undo / cross-page) and are purged wholesale in
        // ClearAllAnnotations.
        private const string ImageContainerTag = "imageContainer";
        private readonly List<Grid> _imageContainers = new List<Grid>();
        private readonly Dictionary<Grid, byte[]> _imageDataById = new Dictionary<Grid, byte[]>();
        // Task 25/26/27: sibling overlay containers on ImageOverlayCanvas.
        // All of them flow through the same selection/move/scale/delete
        // pipeline as image containers; the payload model object rides in
        // _overlayData so save/copy can rebuild the annotation models.
        private const string MarkupContainerTag = "textMarkup";
        private const string AreaHighlightContainerTag = "areaHighlight";
        private const string StickyNoteContainerTag = "stickyNote";
        private readonly Dictionary<Grid, object> _overlayData = new Dictionary<Grid, object>();
        private bool _isDraggingSelection;
        private Point _dragStartPoint;
        private double _totalDragDeltaX;
        private double _totalDragDeltaY;
        private bool _isResizingSelection;
        private int _resizeHandleIndex; // 0=TL, 1=TR, 2=BL, 3=BR
        private Point _resizeAnchorPoint;
        private double _resizeStartHandleDist;
        private double _lastResizeScale;
        private bool _suppressSelectionCaptureCancellation;

        private sealed class SelectionContainerSnapshot
        {
            public Grid Container;
            public Point Position;
            public double Width;
            public double Height;
            public double FontSize;
        }

        private sealed class SelectionInteractionSnapshot
        {
            public readonly Dictionary<Stroke, StylusPointCollection> StrokePoints =
                new Dictionary<Stroke, StylusPointCollection>();
            public readonly Dictionary<Stroke, DrawingAttributes> StrokeAttributes =
                new Dictionary<Stroke, DrawingAttributes>();
            public readonly List<SelectionContainerSnapshot> Containers =
                new List<SelectionContainerSnapshot>();
        }

        private SelectionInteractionSnapshot _selectionInteractionSnapshot;
        // Marching-ants per-item selection outlines (Task 6). One shared
        // CompositionTarget.Rendering driver advances StrokeDashOffset (and the
        // blue↔cyan color swap) for every per-item rect at once — no
        // per-rectangle storyboards, render-only property writes (no layout).
        private readonly List<System.Windows.Shapes.Rectangle> _perItemOutlines = new List<System.Windows.Shapes.Rectangle>();
        private bool _isSelectionDashAnimating;
        private DateTime _selectionDashLastTickUtc;
        private double _selectionDashOffset;
        private double _selectionColorPhaseSeconds;
        private const double SelectionDashSpeed = 15.0;            // dash units per second
        private const double SelectionDashPatternPeriod = 5.0;     // 3 (dash) + 2 (gap)
        private const double SelectionColorHalfCycleSeconds = 1.5; // blue ↔ cyan each half cycle
        // Tracks whether the stylus is currently inverted (physical eraser end),
        // which corresponds to Windows Ink's IsEraser / PointerPointProperties.IsEraser.
        // When inverted, we override the current mode to erase regardless of the
        // selected tool �?this is how standard Windows Ink pens work and is the
        // signal path used by Huawei M-Pencil when MateBook-E-Pen is active.
        private bool _isStylusInverted;

        // Study mode: the model list is independent from InkCanvas.Strokes so
        // ordinary erasing/selection cannot reveal or delete an answer.
        private readonly List<HiddenInkAnnotation> _hiddenInks = new List<HiddenInkAnnotation>();
        private readonly Dictionary<string, System.Windows.Shapes.Polyline> _hiddenInkVisuals =
            new Dictionary<string, System.Windows.Shapes.Polyline>(StringComparer.Ordinal);
        private readonly Dictionary<string, System.Windows.Threading.DispatcherTimer> _hiddenInkRevealTimers =
            new Dictionary<string, System.Windows.Threading.DispatcherTimer>(StringComparer.Ordinal);

        // Sticky markers are the only interactive children of ImageOverlayCanvas.
        // Keep their event delegates so removing/re-adding a note does not retain
        // a detached container or accumulate handlers across undo/redo.
        private sealed class StickyInteractionHandlers
        {
            public MouseButtonEventHandler MouseDown;
            public MouseEventHandler MouseMove;
            public MouseButtonEventHandler MouseUp;
            public StylusDownEventHandler StylusDown;
            public StylusEventHandler StylusMove;
            public StylusEventHandler StylusUp;
            public MouseEventHandler LostMouseCapture;
            public StylusEventHandler LostStylusCapture;
            public KeyEventHandler KeyDown;
        }

        private readonly Dictionary<Grid, StickyInteractionHandlers> _stickyInteractionHandlers =
            new Dictionary<Grid, StickyInteractionHandlers>();
        private Grid _stickyDragContainer;
        private Point _stickyDragStartPointer;
        private Point _stickyDragStartPosition;
        private bool _stickyDragMoved;
        private bool _stickyDragUsingStylus;
        private bool _suppressStickyCaptureCancellation;
        private const double StickyDragThreshold = 3.0;
        private const double StickyMarkerSize = 36.0;

        /// <summary>
        /// Solid colour used for newly created study masks. Existing loaded
        /// annotations keep their serialized RGB values; this only controls
        /// masks created in the current editor session.
        /// </summary>
        public Color HiddenInkMaskColor { get; set; } = Color.FromRgb(199, 205, 212);

        /// <summary>Width of newly created study masks in page DIP.</summary>
        public double HiddenInkSize { get; set; } = 28.0;

        /// <summary>How long a clicked mask stays revealed.</summary>
        public int HiddenInkRevealDurationMs { get; set; } = HiddenInkRevealState.DefaultRevealDurationMs;

        // Universal pen service for pressure / tilt / device detection
        private WindowsPenService _penService;

        /// <summary>
        /// Whether pressure-sensitive width variation is active.
        /// When true, each stroke is post-processed to vary width by pressure.
        /// </summary>
        public bool PressureEnabled { get; set; } = true;

        /// <summary>
        /// Whether tilt-based width variation is active.
        /// </summary>
        public bool TiltEnabled { get; set; } = true;

        /// <summary>
        /// When true, the eraser removes entire strokes whose bounds
        /// intersect the eraser rect instead of clipping them point-wise.
        /// Applies to the eraser tool, the inverted stylus end and the
        /// barrel-button path alike (they all funnel into
        /// <see cref="EraseStrokesAtPoints"/>).
        /// </summary>
        public bool WholeStrokeEraser { get; set; }

        /// <summary>
        /// When true, collected pen strokes (non-highlighter) are
        /// post-processed so their per-point PressureFactor follows drawing
        /// speed: slow segments render thick, fast segments thin.
        /// </summary>
        public bool InkSimulationEnabled { get; set; }

        /// <summary>
        /// When true, collected pen strokes (non-highlighter, enough points)
        /// are run through geometric shape recognition (line / rectangle /
        /// ellipse) and replaced in place by the ideal shape when the
        /// confidence gates pass. Synced from AppSettings by
        /// EditorPage.ApplyToolToAllPages.
        /// </summary>
        public bool ShapeRecognitionEnabled { get; set; }

        /// <summary>
        /// Task 15 (pen-only drawing / palm rejection): when true and the
        /// active mode creates ink (freehand Inking or Shape), input from
        /// pure mouse or real finger touch no longer creates ink — only
        /// pen/stylus devices do. Erasing, selection, text and PDF text
        /// selection modes are never filtered (mouse erasing stays useful).
        /// Synced from AppSettings by EditorPage.ApplyToolToAllPages.
        /// </summary>
        public bool PenOnlyMode { get; set; }

        /// <summary>
        /// Task 24: stroke smoothing level (0=Off, 1=Low, 2=Medium, 3=High),
        /// synced from AppSettings by EditorPage.ApplyToolToAllPages.
        /// Freshly collected freehand strokes run through
        /// <see cref="ApplySmoothing"/> after ruler snap / Shift
        /// straightening and before shape recognition / ink simulation.
        /// Off keeps the raw trajectory (FitToCurve disabled on the stroke);
        /// Low/Medium/High apply a moving average of 1/2/4 neighbours on
        /// each side with FitToCurve kept on.
        /// </summary>
        public int StrokeSmoothingLevel { get; set; } = 2;

        /// <summary>
        /// Task 22 (ruler): when non-null, returns the two endpoints of the
        /// active on-screen ruler's drawing edge, in this control's ROOT
        /// coordinates (EditorPage owns the ruler and performs the
        /// viewport→page translation via TranslatePoint at query time, so
        /// scrolling/zooming never invalidates the segment). A freshly
        /// collected freehand stroke whose every point lies within
        /// <see cref="RulerSnapTolerancePx"/> of that segment is projected
        /// onto it — the result is a perfectly straight line along the
        /// ruler. Assigned by EditorPage when the page control is created.
        /// </summary>
        public Func<(Point TopA, Point TopB, Point BottomA, Point BottomB)?> GetRulerGeometryInPageCoords { get; set; }

        /// <summary>
        /// Task 22: max point-to-ruler-edge distance (page px) for a stroke
        /// to qualify for snapping.
        /// </summary>
        private const double RulerSnapTolerancePx = 24.0;

        /// <summary>
        /// True while the shape tool is the active tool on this page
        /// (mirrors <see cref="CustomInkInputProcessingMode.Shape"/>;
        /// synced by EditorPage.ApplyToolToAllPages).
        /// </summary>
        public bool ShapeMode { get; set; }

        /// <summary>Currently selected shape sub-type (line/rect/ellipse/arrow).</summary>
        public ShapeKind CurrentShape { get; set; }

        /// <summary>Stroke color used when committing a shape.</summary>
        public Color ShapeColor { get; set; } = Colors.Black;

        /// <summary>Stroke width used when committing a shape.</summary>
        public double ShapeStrokeSize { get; set; } = 2.0;

        // Shape drag state (anchor = pointer-down point, current = live point).
        private const double ShapeDragThreshold = 4.0; // px; below this a tap commits nothing
        private const int EllipseSegmentCount = 64;
        private bool _isShapeDragging;
        private Point _shapeAnchor;
        private Point _shapeCurrent;
        private readonly List<System.Windows.Shapes.Polyline> _shapePreviewPolylines =
            new List<System.Windows.Shapes.Polyline>();

        // Task 20: laser pointer state. Live strokes are plain Polylines on
        // LaserInkCanvas (topmost, hit-test off); on pointer-up each one gets
        // an opacity fade animation and removes itself when finished. Nothing
        // here ever touches InkCanvas.Strokes / InkMutated / undo / dirty.
        private bool _isLaserDrawing;
        private bool _isHostActive = true;
        private bool _documentInputEnabled = true;

        /// <summary>
        /// Closes the page's input admission while its owning editor is
        /// snapshotting/disposing.  Rendering remains enabled; only model
        /// mutation controls inherit the disabled state.
        /// </summary>
        public void SetDocumentInputEnabled(bool enabled)
        {
            _documentInputEnabled = enabled;
            IsEnabled = enabled;
        }
        private System.Windows.Shapes.Polyline _laserPolyline;
        private readonly List<System.Windows.Shapes.Polyline> _liveLaserPolylines =
            new List<System.Windows.Shapes.Polyline>();
        private static readonly Color LaserColor = Color.FromRgb(0xFF, 0x3B, 0x30);
        private const double LaserStrokeThickness = 3.0;
        private const double LaserFadeDelaySeconds = 0.15;   // hold fully visible briefly
        private const double LaserFadeDurationSeconds = 0.9; // then fade out
        private const int MaxLiveLaserPolylines = 60;        // hard cap; oldest dropped at once

        // Task 27: area-highlight drag state. Same gesture contract as the
        // shape tool (drag-to-draw on InkCanvas with native inking off); the
        // preview rectangle lives on ShapePreviewCanvas and the committed
        // annotation is an overlay container on ImageOverlayCanvas.
        private bool _isAreaHighlightDragging;
        private Point _areaHighlightAnchor;
        private System.Windows.Shapes.Rectangle _areaHighlightPreview;
        /// <summary>Fill colour (alpha applied internally, default ~30%).</summary>
        public Color AreaHighlightColor { get; set; } = Color.FromRgb(0xFF, 0xEB, 0x3B);
        public byte AreaHighlightOpacity { get; set; } = 76; // ~30% opacity
        private const double AreaHighlightDragThreshold = 4.0;

        public static Rect NormalizeAreaHighlightRect(Point anchor, Point current)
        {
            return new Rect(
                Math.Min(anchor.X, current.X),
                Math.Min(anchor.Y, current.Y),
                Math.Abs(current.X - anchor.X),
                Math.Abs(current.Y - anchor.Y));
        }

        // Custom stroke collection to prevent WPF's InkCanvas from clearing strokes
        // during visibility or EditingMode toggles.
        private readonly StrokeCollection _strokes = new StrokeCollection();

        // Shape recognition is session-only: each live stroke has a stable
        // token and replacement side. Reference identity is intentional
        // because WPF Stroke equality is not a persistence identity.
        private sealed class StrokeMetadata
        {
            public StrokeMetadata(Guid token, StrokeReplacementSide side)
            {
                Token = token;
                Side = side;
            }

            public Guid Token { get; }
            public StrokeReplacementSide Side { get; set; }
        }

        private sealed class StrokeReferenceComparer : IEqualityComparer<Stroke>
        {
            public bool Equals(Stroke x, Stroke y) => ReferenceEquals(x, y);
            public int GetHashCode(Stroke obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly Dictionary<Stroke, StrokeMetadata> _strokeMetadata =
            new Dictionary<Stroke, StrokeMetadata>(new StrokeReferenceComparer());

        // The page uses the same pure token/index ledger as the unit tests.
        // WPF strokes remain the rendering layer; this state is rebuilt from
        // that layer before token operations and updated by quiet mutations.
        private StrokeReplacementState _replacementState =
            new StrokeReplacementState(Array.Empty<StrokeReplacementEntry>());

        // A removed live stroke can be restored with its original token, side,
        // and index even after it is no longer present in InkCanvas.Strokes.
        private readonly Dictionary<Stroke, StrokePlacement> _strokePlacementHistory =
            new Dictionary<Stroke, StrokePlacement>(new StrokeReferenceComparer());

        public PdfPageControl()
        {
            InitializeComponent();

            // Assign stable stroke collection
            InkCanvas.Strokes = _strokes;

            _drawingAttributes = new DrawingAttributes
            {
                Color = Colors.Black,
                Width = 2,
                Height = 2,
                FitToCurve = true,
                StylusTip = StylusTip.Ellipse,
                // Ensure WPF captures and applies pressure data from the digitiser.
                // This makes stroke width vary with pen pressure on all devices
                // that report NormalPressure (Surface Pen, Wacom, etc.).
                IgnorePressure = false
            };

            InkCanvas.DefaultDrawingAttributes = _drawingAttributes;
            InkCanvas.UseCustomCursor = true;
            InkCanvas.Cursor = Cursors.None;
            InkCanvas.EditingMode = InkCanvasEditingMode.None;
            InkCanvas.EditingModeInverted = InkCanvasEditingMode.None; // Disable native inverted erasing so we can use custom logic
            InkCanvas.StrokeCollected += InkCanvas_StrokeCollected;
            InkCanvas.StrokeErasing += InkCanvas_StrokeErasing;
            InkCanvas.StrokeErased += InkCanvas_StrokeErased;

            // Task 15 (pen-only mode): preview-level device filtering. The
            // InkCanvas starts mouse ink on MouseLeftButtonDown and stylus
            // ink (incl. promoted touch) on StylusDown; marking the
            // tunneling preview event handled suppresses both, so no stroke
            // is created from a blocked device while PenOnlyMode is on.
            InkCanvas.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(InkCanvas_PreviewMouseLeftButtonDown_PenOnly), true);
            InkCanvas.AddHandler(UIElement.PreviewStylusDownEvent,
                new StylusDownEventHandler(InkCanvas_PreviewStylusDown_PenOnly), true);

            TextOverlayCanvas.IsHitTestVisible = false;
            PdfTextSelectionCanvas.IsHitTestVisible = false;
            InkCanvas.IsHitTestVisible = true;

            Loaded += PdfPageControl_Loaded;
            Unloaded += PdfPageControl_Unloaded;
        }

        /// <summary>
        /// Clamps a marker's top-left position to the measured page surface.
        /// All values are DIP; callers may pass a zero-sized/unmeasured page.
        /// </summary>
        public static Point ClampStickyNotePosition(Point position, Size pageSize, Size markerSize)
        {
            double pageWidth = double.IsNaN(pageSize.Width) ? 0 : Math.Max(0, pageSize.Width);
            double pageHeight = double.IsNaN(pageSize.Height) ? 0 : Math.Max(0, pageSize.Height);
            double markerWidth = double.IsNaN(markerSize.Width) ? 0 : Math.Max(0, markerSize.Width);
            double markerHeight = double.IsNaN(markerSize.Height) ? 0 : Math.Max(0, markerSize.Height);
            double maxX = Math.Max(0, pageWidth - markerWidth);
            double maxY = Math.Max(0, pageHeight - markerHeight);
            double x = double.IsNaN(position.X) ? 0 : position.X;
            double y = double.IsNaN(position.Y) ? 0 : position.Y;
            return new Point(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
        }

        /// <summary>
        /// Returns true if the given stylus device is a finger touch rather
        /// than a pen/stylus.  Used to let finger events pass through to the
        /// WPF manipulation system for pan/zoom gestures.
        /// </summary>
        private static bool IsTouchFinger(StylusDevice device)
        {
            if (device == null) return false;
            var tablet = device.TabletDevice;
            if (tablet == null) return false;
            // Pen devices report as Stylus. Some pen-as-touch devices (e.g.
            // Huawei M-Pencil) report as Touch but have multiple stylus buttons.
            // Real fingers have TabletDeviceType.Touch with �? button.
            if (tablet.Type == System.Windows.Input.TabletDeviceType.Stylus)
                return false;
            return tablet.Type == System.Windows.Input.TabletDeviceType.Touch
                && device.StylusButtons.Count <= 1;
        }

        /// <summary>
        /// Task 15: true while the active input mode creates ink — the only
        /// modes <see cref="PenOnlyMode"/> applies to (freehand Inking and
        /// Shape). Eraser / select / text / PDF-text-selection modes are
        /// never device-filtered.
        /// </summary>
        private bool IsInkCreationModeActive => IsPenOnlyInkCreationMode(_currentMode);

        /// <summary>
        /// Returns the input modes that PenOnlyMode must filter. Hidden Ink is
        /// intentionally excluded because its study-mask workflow supports
        /// both mouse and pen drawing.
        /// </summary>
        internal static bool IsPenOnlyInkCreationMode(CustomInkInputProcessingMode mode)
        {
            return mode == CustomInkInputProcessingMode.Inking ||
                   mode == CustomInkInputProcessingMode.Shape;
        }

        /// <summary>
        /// Task 15: true when input from this device must not create ink
        /// while pen-only mode is on — pure mouse (no stylus device, real
        /// mouse input is never promoted to stylus in WPF) or a real finger
        /// (<see cref="IsTouchFinger"/>: Touch tablet type with ≤1 stylus
        /// button). True stylus pens — including pen-as-touch devices like
        /// the Huawei M-Pencil (Touch type, multiple stylus buttons) — are
        /// allowed through, mirroring the device discrimination used for
        /// pen scrolling and PDF text selection.
        /// </summary>
        private static bool ShouldBlockNonPenInk(StylusDevice device)
        {
            return device == null || IsTouchFinger(device);
        }

        /// <summary>
        /// Set the shared <see cref="WindowsPenService"/> so this control can
        /// probe stylus devices and read pressure/tilt capabilities.
        /// </summary>
        public void SetPenService(WindowsPenService service)
        {
            _penService = service;
            if (service != null)
            {
                PressureEnabled = service.PressureEnabled;
                TiltEnabled = service.TiltEnabled;
            }
        }

        private void InkCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            if (TrySuppressPendingPopupTap(e.Stroke))
            {
                e.Handled = true;
                return;
            }

            if (_currentMode == CustomInkInputProcessingMode.HiddenInk)
            {
                CommitHiddenInkStroke(e.Stroke);
                e.Handled = true;
                return;
            }

            PreserveTapStroke(e.Stroke);
            EnsureStrokeToken(e.Stroke);

            var stroke = e.Stroke;

            // Task 22: ruler snap. When the on-screen ruler is visible and
            // the stroke was drawn along its edge (every point within
            // RulerSnapTolerancePx of the edge segment), project all points
            // onto the edge so the result is a perfectly straight line along
            // the ruler. Runs first so the projected stroke continues
            // through the normal pipeline (Shift straightening, shape
            // recognition, ink simulation, undo event with the replaced
            // stroke — same replacement pattern as ApplyInkSimulation).
            // Only the freehand Inking mode (pen / highlighter) reaches this
            // event with stroke creation — Shape/Laser commit strokes
            // manually and the eraser never collects — but the mode gate
            // keeps that contract explicit.
            if (_currentMode == CustomInkInputProcessingMode.Inking)
            {
                stroke = ApplyRulerConstraint(stroke);
                if (stroke == null)
                {
                    e.Handled = true;
                    return;
                }
            }

            // Task 21: Shift straight-line constraint for freehand pen and
            // highlighter. WPF InkCanvas owns the per-point collection, so a
            // live per-point intercept is impractical — instead, when Shift
            // is held at the moment the stroke is collected, the whole
            // stroke is replaced by a straight first→last two-point stroke
            // (same in-place replacement pattern as ApplyInkSimulation, so
            // one undo step still covers the stroke). Limitation, by design:
            // per-stroke rather than live — the Shift state at stylus-up
            // decides, mid-stroke changes are not tracked.
            if (IsShiftHeld())
                stroke = StraightenShiftStroke(stroke);

            // Task 24: stroke smoothing. Runs AFTER ruler snap and Shift
            // straightening (their outputs are the user's intended
            // trajectory — a snapped/straightened line is already smooth,
            // and averaging collinear points keeps it collinear) and BEFORE
            // shape recognition / ink simulation (recognition on smoothed
            // points is fine — noise reduction helps the geometric gates —
            // and ink simulation reads the final point set). "Off" must
            // preserve the truly raw trajectory, so it swaps the stroke for
            // a FitToCurve=false clone instead of skipping.
            stroke = ApplySmoothing(stroke);

            // Shape recognition runs before ink simulation: a recognized
            // stroke is replaced wholesale by its ideal shape (uniform
            // width), so simulating ink on the original would be wasted.
            if (ShapeRecognitionEnabled && !stroke.DrawingAttributes.IsHighlighter
                && stroke.StylusPoints.Count >= MinRecognizedShapePoints
                && TryRecognizeShape(stroke, out var idealStroke))
            {
                var token = EnsureStrokeToken(stroke);
                var originalSnapshot = CaptureStrokeSnapshot(
                    stroke,
                    token,
                    StrokeReplacementSide.Original);
                var idealSnapshot = CaptureStrokeSnapshot(
                    idealStroke,
                    token,
                    StrokeReplacementSide.Ideal);

                if (ReplaceRecognizedStroke(
                    stroke,
                    idealStroke,
                    token,
                    idealSnapshot,
                    out var originalIndex))
                {
                    InkMutated?.Invoke(this, EventArgs.Empty);
                    StrokeRecognized?.Invoke(
                        this,
                        new StrokeRecognizedEventArgs(
                            token,
                            originalIndex,
                            originalSnapshot,
                            idealSnapshot,
                            isFreshStroke: true));
                }
                return;
            }

            if (InkSimulationEnabled && !stroke.DrawingAttributes.IsHighlighter && stroke.StylusPoints.Count >= 3)
                stroke = ApplyInkSimulation(stroke);

            InkMutated?.Invoke(this, EventArgs.Empty);
            StrokeCollectedUndoable?.Invoke(this, stroke);
        }

        /// <summary>
        /// Arms the page-local boundary used when EditorPage closes a
        /// Pen/Highlighter popup from an outside page pointer. The pointer
        /// remains unhandled so native InkCanvas can still become a real drag;
        /// a stationary tap is removed in <see cref="InkCanvas_StrokeCollected" />.
        /// </summary>
        internal void ArmPendingPopupDismissalGesture()
        {
            if (_currentMode == CustomInkInputProcessingMode.Inking)
                _pendingPopupDismissalInkGesture = true;
        }

        private bool TrySuppressPendingPopupTap(Stroke stroke)
        {
            if (!_pendingPopupDismissalInkGesture)
                return false;

            _pendingPopupDismissalInkGesture = false;
            if (_currentMode != CustomInkInputProcessingMode.Inking
                || StrokeCrossedSystemDragThreshold(stroke))
            {
                return false;
            }

            // The native InkCanvas has already inserted the collected stroke,
            // but the normal mutation/undo events have not fired yet. Remove
            // it quietly so a popup-dismissal tap has no visible or history
            // side effects.
            RemoveStrokeQuiet(stroke);
            return true;
        }

        private static bool StrokeCrossedSystemDragThreshold(Stroke stroke)
        {
            if (stroke?.StylusPoints == null || stroke.StylusPoints.Count < 2)
                return false;

            var start = stroke.StylusPoints[0];
            double horizontal = SystemParameters.MinimumHorizontalDragDistance;
            double vertical = SystemParameters.MinimumVerticalDragDistance;
            foreach (var point in stroke.StylusPoints)
            {
                if (Math.Abs(point.X - start.X) >= horizontal
                    || Math.Abs(point.Y - start.Y) >= vertical)
                {
                    return true;
                }
            }

            return false;
        }

        private void CommitHiddenInkStroke(Stroke stroke)
        {
            if (stroke == null)
                return;

            // The native InkCanvas briefly owns the live preview. Move the
            // completed stroke out immediately so ordinary ink persistence,
            // erasing, and undo cannot mistake a mask for normal ink.
            InkCanvas.Strokes.Remove(stroke);
            _strokeMetadata.Remove(stroke);

            var hidden = new HiddenInkAnnotation
            {
                R = HiddenInkMaskColor.R,
                G = HiddenInkMaskColor.G,
                B = HiddenInkMaskColor.B,
                // Hidden Ink is deliberately a full-opacity cover. Keeping
                // the alpha channel opaque prevents the answer from leaking
                // through in the live editor even when a caller supplies a
                // translucent Color value.
                A = 255,
                Size = Math.Max(1.0, HiddenInkSize),
                RevealDurationMs = HiddenInkRevealDurationMs > 0
                    ? HiddenInkRevealDurationMs
                    : HiddenInkRevealState.DefaultRevealDurationMs
            };

            foreach (var point in stroke.StylusPoints)
                hidden.Points.Add(new[] { point.X, point.Y });

            if (hidden.Points.Count == 0)
                return;

            if (hidden.Points.Count == 1)
            {
                var point = hidden.Points[0];
                hidden.Points.Add(new[] { point[0] + 0.1, point[1] });
            }

            AddHiddenInkInternal(hidden, raiseCreated: true);
        }

        private Guid EnsureStrokeToken(
            Stroke stroke,
            StrokeReplacementSide side = StrokeReplacementSide.Original,
            Guid? preferredToken = null)
        {
            if (stroke == null)
                return Guid.Empty;

            if (_strokeMetadata.TryGetValue(stroke, out var metadata))
                return metadata.Token;

            var token = preferredToken.GetValueOrDefault();
            if (token == Guid.Empty)
                token = Guid.NewGuid();

            _strokeMetadata[stroke] = new StrokeMetadata(token, side);
            return token;
        }

        private StrokeReplacementSide GetStrokeSide(Stroke stroke)
        {
            return _strokeMetadata.TryGetValue(stroke, out var metadata)
                ? metadata.Side
                : StrokeReplacementSide.Original;
        }

        private void RegisterStroke(
            Stroke stroke,
            Guid token,
            StrokeReplacementSide side = StrokeReplacementSide.Original)
        {
            if (stroke == null || token == Guid.Empty)
                return;

            _strokeMetadata[stroke] = new StrokeMetadata(token, side);
        }

        private void SynchronizeReplacementState()
        {
            var entries = new List<StrokeReplacementEntry>(_strokes.Count);
            foreach (var stroke in _strokes)
            {
                var token = EnsureStrokeToken(stroke);
                var side = GetStrokeSide(stroke);
                entries.Add(new StrokeReplacementEntry(
                    CaptureStrokeSnapshot(stroke, token, side)));
            }

            _replacementState = new StrokeReplacementState(entries);
        }

        private StrokePlacement AddStrokeToCollection(
            Stroke stroke,
            Guid? token = null,
            StrokeReplacementSide side = StrokeReplacementSide.Original,
            int? index = null)
        {
            if (stroke == null)
                return null;

            int existingIndex = _strokes.IndexOf(stroke);
            if (existingIndex >= 0)
                return CaptureStrokePlacement(stroke);

            int insertionIndex = index.GetValueOrDefault(_strokes.Count);
            insertionIndex = Math.Max(0, Math.Min(insertionIndex, _strokes.Count));
            _strokes.Insert(insertionIndex, stroke);
            RegisterStroke(stroke, token ?? Guid.NewGuid(), side);
            var placement = CaptureStrokePlacement(stroke);
            SynchronizeReplacementState();
            return placement;
        }

        private void ReplaceStrokeAt(
            int index,
            Stroke replacement,
            Guid token,
            StrokeReplacementSide side)
        {
            if (index < 0 || index >= _strokes.Count || replacement == null || token == Guid.Empty)
                return;

            var previous = _strokes[index];
            var previousPlacement = CaptureStrokePlacement(previous);
            _strokePlacementHistory[previous] = previousPlacement;
            _strokeMetadata.Remove(previous);
            _strokes[index] = replacement;
            RegisterStroke(replacement, token, side);
            _strokePlacementHistory[replacement] = CaptureStrokePlacement(replacement);
            SynchronizeReplacementState();
        }

        private StrokeReplacementSnapshot CaptureStrokeSnapshot(
            Stroke stroke,
            Guid token,
            StrokeReplacementSide side)
        {
            var attrs = stroke.DrawingAttributes;
            var points = stroke.StylusPoints
                .Select(point => new StrokeReplacementPoint(point.X, point.Y, point.PressureFactor))
                .ToList();
            var color = attrs.Color;
            return new StrokeReplacementSnapshot(
                token,
                side,
                points,
                color.R,
                color.G,
                color.B,
                color.A,
                attrs.Width,
                attrs.Height,
                attrs.IsHighlighter,
                attrs.FitToCurve,
                attrs.IgnorePressure);
        }

        private static Stroke CreateStrokeFromSnapshot(StrokeReplacementSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Points.Count == 0)
                return null;

            var points = new StylusPointCollection();
            foreach (var point in snapshot.Points)
                points.Add(new StylusPoint(point.X, point.Y, point.PressureFactor));
            if (points.Count == 1)
                points.Add(new StylusPoint(points[0].X + 0.1, points[0].Y, points[0].PressureFactor));

            var stroke = new Stroke(points)
            {
                DrawingAttributes = new DrawingAttributes
                {
                    Color = Color.FromArgb(snapshot.A, snapshot.R, snapshot.G, snapshot.B),
                    Width = snapshot.Width,
                    Height = snapshot.Height,
                    IsHighlighter = snapshot.IsHighlighter,
                    FitToCurve = snapshot.FitToCurve,
                    IgnorePressure = snapshot.IgnorePressure
                }
            };
            return stroke;
        }

        /// <summary>
        /// Captures the live stroke's token, side, immutable snapshot, index,
        /// and owning page. The returned record remains valid while the stroke
        /// is temporarily removed for an undoable action.
        /// </summary>
        public StrokePlacement CaptureStrokePlacement(Stroke stroke)
        {
            if (stroke == null)
                throw new ArgumentNullException(nameof(stroke));

            int index = _strokes.IndexOf(stroke);
            if (index < 0)
            {
                if (_strokePlacementHistory.TryGetValue(stroke, out var historical))
                    return historical;
                throw new ArgumentException("The stroke does not belong to this page.", nameof(stroke));
            }

            var token = EnsureStrokeToken(stroke);
            var snapshot = CaptureStrokeSnapshot(stroke, token, GetStrokeSide(stroke));
            var placement = new StrokePlacement(this, stroke, snapshot, index);
            _strokePlacementHistory[stroke] = placement;
            return placement;
        }

        /// <summary>
        /// Resolves a logical placement to the current live stroke while
        /// retaining the page's token/side contract.  Replacement actions may
        /// legitimately swap the WPF stroke reference; callers that need to
        /// roll back a transfer must capture that live reference before
        /// removing it, otherwise a stale snapshot can be restored instead.
        /// </summary>
        public bool TryCaptureCurrentStrokePlacement(
            StrokePlacement expected,
            out StrokePlacement current)
        {
            current = null;
            if (expected == null || !ReferenceEquals(expected.Owner, this))
                return false;

            if (!TryResolveCurrentStroke(expected, out var currentStroke, out _))
                return false;

            current = CaptureStrokePlacement(currentStroke);
            return true;
        }

        /// <summary>
        /// Resolves a currently live stroke by its stable token and replacement
        /// side. This is used by fresh recognition history after the page has
        /// swapped the collected stroke for its Ideal shape; it deliberately
        /// returns a live placement only after both token and side match.
        /// </summary>
        public bool TryCaptureCurrentStrokePlacement(
            Guid token,
            StrokeReplacementSide expectedSide,
            out StrokePlacement current)
        {
            current = null;
            if (token == Guid.Empty
                || !TryFindCurrentStroke(token, out var currentStroke, out _)
                || !_strokeMetadata.TryGetValue(currentStroke, out var metadata)
                || metadata.Side != expectedSide)
            {
                return false;
            }

            current = CaptureStrokePlacement(currentStroke);
            return true;
        }

        /// <summary>
        /// Removes only the exact live stroke represented by a placement.
        /// Unlike the normal logical-token removal used by replacement-aware
        /// undo, this is the identity guard required by cross-page transfer
        /// rollback so an unrelated same-token target can never be deleted.
        /// </summary>
        public bool RemoveStrokeQuietExact(StrokePlacement placement)
        {
            if (placement == null
                || !ReferenceEquals(placement.Owner, this)
                || placement.Stroke == null
                || !_strokes.Contains(placement.Stroke))
            {
                return false;
            }

            if (!_strokeMetadata.TryGetValue(placement.Stroke, out var metadata)
                || metadata.Token != placement.Token
                || metadata.Side != placement.Side)
            {
                return false;
            }

            return RemoveStrokeQuiet(placement.Stroke);
        }

        private bool TryFindCurrentStroke(
            Guid token,
            out Stroke currentStroke,
            out int currentIndex)
        {
            currentStroke = null;
            currentIndex = -1;
            if (token == Guid.Empty)
                return false;

            SynchronizeReplacementState();
            for (int index = 0; index < _strokes.Count; index++)
            {
                var candidate = _strokes[index];
                if (!_strokeMetadata.TryGetValue(candidate, out var metadata)
                    || metadata.Token != token)
                {
                    continue;
                }

                currentStroke = candidate;
                currentIndex = index;
                return true;
            }

            return false;
        }

        private bool TryResolveCurrentStroke(
            StrokePlacement placement,
            out Stroke currentStroke,
            out int currentIndex)
        {
            currentStroke = null;
            currentIndex = -1;
            if (placement == null || !ReferenceEquals(placement.Owner, this))
                return false;

            if (!TryFindCurrentStroke(placement.Token, out currentStroke, out currentIndex))
                return false;

            if (!_strokeMetadata.TryGetValue(currentStroke, out var metadata)
                || metadata.Side != placement.Side)
            {
                currentStroke = null;
                currentIndex = -1;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Replaces a tokenized stroke at its existing collection index. The
        /// operation is quiet for undo/redo and returns false when another
        /// action has already removed or changed the token; it never appends.
        /// </summary>
        public bool TryReplaceStrokeQuiet(
            Guid token,
            StrokeReplacementSide expectedSide,
            StrokeReplacementSnapshot replacement,
            out int index)
        {
            index = -1;
            if (token == Guid.Empty || replacement == null || replacement.Token != token)
                return false;

            SynchronizeReplacementState();
            if (!_replacementState.TryReplaceStrokeQuiet(
                token,
                expectedSide,
                replacement,
                out index))
                return false;

            var replacementStroke = CreateStrokeFromSnapshot(replacement);
            if (replacementStroke == null)
            {
                index = -1;
                return false;
            }

            ReplaceStrokeAt(index, replacementStroke, token, replacement.Side);
            QuietStrokeMutation?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Rebuilds the stroke with velocity-based PressureFactor so slow
        /// segments render thick and fast segments thin (ink-flow
        /// simulation). StylusPoints carry no timestamp, so the distance
        /// between consecutive points is used as the speed proxy (the
        /// collection rate is roughly constant). Speeds are normalised by
        /// the fastest segment in the stroke: slow → ~1.0 (capped), fast →
        /// ~0.25 (floored). The replacement stroke is swapped into
        /// <see cref="_strokes"/> in place of the original and returned so
        /// the undo event references the stroke that is actually in the
        /// collection.
        /// </summary>
        private Stroke ApplyInkSimulation(Stroke stroke)
        {
            var points = stroke.StylusPoints;
            int count = points.Count;

            var stepDist = new double[count];
            double maxDist = 0;
            for (int i = 1; i < count; i++)
            {
                double dx = points[i].X - points[i - 1].X;
                double dy = points[i].Y - points[i - 1].Y;
                stepDist[i] = Math.Sqrt(dx * dx + dy * dy);
                if (stepDist[i] > maxDist)
                    maxDist = stepDist[i];
            }

            // Stationary stroke (all points coincident) — nothing to simulate.
            if (maxDist <= double.Epsilon)
                return stroke;

            var simulatedPoints = new StylusPointCollection();
            for (int i = 0; i < count; i++)
            {
                var sp = points[i];
                double speedNorm = stepDist[i] / maxDist;
                sp.PressureFactor = (float)Math.Max(0.25, 1.0 - 0.75 * speedNorm);
                simulatedPoints.Add(sp);
            }

            var replacement = new Stroke(simulatedPoints)
            {
                DrawingAttributes = stroke.DrawingAttributes.Clone()
            };

            int index = _strokes.IndexOf(stroke);
            if (index >= 0)
                ReplaceStrokeAt(index, replacement, EnsureStrokeToken(stroke), GetStrokeSide(stroke));

            return replacement;
        }

        /// <summary>
        /// Task 21: replaces a freshly collected freehand stroke with a
        /// straight two-point stroke (first → last), cloning the original
        /// drawing attributes (colour, width, IsHighlighter are all
        /// preserved) with FitToCurve=false. The replacement is swapped into
        /// <see cref="_strokes"/> in place — mirroring
        /// <see cref="ApplyInkSimulation"/> — so the caller continues with
        /// the replacement stroke and a single undo step covers it. A
        /// two-point stroke also skips shape recognition (min point gate)
        /// and ink simulation (min 3 points), which is correct: the user's
        /// explicit intent (a straight line) wins over heuristics.
        /// </summary>
        private Stroke StraightenShiftStroke(Stroke stroke)
        {
            var points = stroke.StylusPoints;
            if (points.Count < 2)
                return stroke;

            var first = points[0];
            var last = points[points.Count - 1];
            double dx = last.X - first.X;
            double dy = last.Y - first.Y;
            if (dx * dx + dy * dy < 1e-4) // tap dot — nothing to straighten
                return stroke;

            var straightened = new StylusPointCollection { first, last };
            var attributes = stroke.DrawingAttributes.Clone();
            attributes.FitToCurve = false;
            var replacement = new Stroke(straightened) { DrawingAttributes = attributes };

            int index = _strokes.IndexOf(stroke);
            if (index >= 0)
                ReplaceStrokeAt(index, replacement, EnsureStrokeToken(stroke), GetStrokeSide(stroke));
            else
                AddStrokeToCollection(replacement);

            return replacement;
        }

        /// <summary>
        /// Task 22: projects a freshly collected stroke onto the on-screen
        /// ruler's edge when the ruler is visible and the whole stroke was
        /// drawn close to it (max point-to-segment distance below
        /// <see cref="RulerSnapTolerancePx"/>). Every point keeps its
        /// PressureFactor and is clamped to the segment (t ∈ [0, 1]) so the
        /// resulting line never extends past the ruler's ends. The
        /// replacement is swapped into <see cref="_strokes"/> in place — the
        /// same pattern as <see cref="ApplyInkSimulation"/> — so a single
        /// undo step covers the snapped stroke and it saves like any other
        /// stroke. Returns the original stroke untouched when there is no
        /// ruler, the edge is degenerate or the stroke is not near it.
        /// </summary>
        private Stroke ApplyRulerConstraint(Stroke stroke)
        {
            var geometry = GetRulerGeometryInPageCoords?.Invoke();
            if (geometry == null) return stroke;

            Point topA = RootGrid.TranslatePoint(geometry.Value.TopA, InkCanvas);
            Point topB = RootGrid.TranslatePoint(geometry.Value.TopB, InkCanvas);
            Point bottomA = RootGrid.TranslatePoint(geometry.Value.BottomA, InkCanvas);
            Point bottomB = RootGrid.TranslatePoint(geometry.Value.BottomB, InkCanvas);
            var replacement = ConstrainStrokeToRuler(stroke, topA, topB, bottomA, bottomB);

            if (replacement == null)
            {
                RemoveStrokeQuiet(stroke);
                return null;
            }
            if (ReferenceEquals(replacement, stroke))
                return stroke;

            int index = _strokes.IndexOf(stroke);
            if (index >= 0)
                ReplaceStrokeAt(index, replacement, EnsureStrokeToken(stroke), GetStrokeSide(stroke));
            else
                AddStrokeToCollection(replacement);
            return replacement;
        }

        private static Stroke ConstrainStrokeToRuler(
            Stroke stroke,
            Point topA,
            Point topB,
            Point bottomA,
            Point bottomB)
        {
            var points = stroke?.StylusPoints;
            if (points == null || points.Count == 0)
                return stroke;

            var quad = new[] { topA, topB, bottomB, bottomA };
            var first = new Point(points[0].X, points[0].Y);
            if (IsPointInsideConvexQuad(first, quad))
                return null;

            for (int i = 1; i < points.Count; i++)
            {
                var from = new Point(points[i - 1].X, points[i - 1].Y);
                var to = new Point(points[i].X, points[i].Y);
                if (!TryFindFirstQuadIntersection(from, to, quad, out double entryT, out Point entry))
                {
                    // A gesture may begin exactly on the edge. That boundary
                    // point is allowed for along-edge drawing, but moving from
                    // it into the body must still produce no ink.
                    if (IsPointInsideConvexQuad(to, quad))
                        return null;
                    continue;
                }

                var clipped = new StylusPointCollection();
                for (int j = 0; j < i; j++)
                    clipped.Add(points[j]);
                float pressure = (float)(points[i - 1].PressureFactor
                    + (points[i].PressureFactor - points[i - 1].PressureFactor) * entryT);
                clipped.Add(new StylusPoint(entry.X, entry.Y, pressure));
                return CloneStroke(stroke, clipped);
            }

            double topDistance = MaxDistanceToSegment(points, topA, topB);
            double bottomDistance = MaxDistanceToSegment(points, bottomA, bottomB);
            if (Math.Min(topDistance, bottomDistance) >= RulerSnapTolerancePx)
                return stroke;

            Point edgeA = topDistance <= bottomDistance ? topA : bottomA;
            Point edgeB = topDistance <= bottomDistance ? topB : bottomB;
            var snapped = new StylusPointCollection();
            foreach (var point in points)
            {
                Point projected = ProjectToSegment(new Point(point.X, point.Y), edgeA, edgeB);
                snapped.Add(new StylusPoint(projected.X, projected.Y, point.PressureFactor));
            }
            return CloneStroke(stroke, snapped);
        }

        private static Stroke CloneStroke(Stroke source, StylusPointCollection points)
        {
            return new Stroke(points) { DrawingAttributes = source.DrawingAttributes.Clone() };
        }

        private static double MaxDistanceToSegment(StylusPointCollection points, Point a, Point b)
        {
            double max = 0;
            foreach (var point in points)
            {
                var source = new Point(point.X, point.Y);
                max = Math.Max(max, PointDistance(source, ProjectToSegment(source, a, b)));
            }
            return max;
        }

        private static Point ProjectToSegment(Point point, Point a, Point b)
        {
            Vector edge = b - a;
            double lengthSquared = edge.X * edge.X + edge.Y * edge.Y;
            if (lengthSquared < 1e-4)
                return a;
            double t = Vector.Multiply(point - a, edge) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            return a + edge * t;
        }

        private static bool IsPointInsideConvexQuad(Point point, IReadOnlyList<Point> quad)
        {
            double? sign = null;
            for (int i = 0; i < quad.Count; i++)
            {
                Point a = quad[i];
                Point b = quad[(i + 1) % quad.Count];
                double cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
                if (Math.Abs(cross) < 1e-7)
                    return false;
                double current = Math.Sign(cross);
                if (sign.HasValue && current != sign.Value)
                    return false;
                sign = current;
            }
            return sign.HasValue;
        }

        private static bool TryFindFirstQuadIntersection(
            Point from,
            Point to,
            IReadOnlyList<Point> quad,
            out double firstT,
            out Point intersection)
        {
            firstT = double.MaxValue;
            intersection = default;
            for (int i = 0; i < quad.Count; i++)
            {
                if (TryIntersectSegments(from, to, quad[i], quad[(i + 1) % quad.Count], out double t)
                    && t > 1e-7 && t < firstT)
                {
                    firstT = t;
                    intersection = from + (to - from) * t;
                }
            }
            return firstT != double.MaxValue;
        }

        private static bool TryIntersectSegments(Point p, Point p2, Point q, Point q2, out double t)
        {
            Vector r = p2 - p;
            Vector s = q2 - q;
            double cross = r.X * s.Y - r.Y * s.X;
            if (Math.Abs(cross) < 1e-7)
            {
                t = 0;
                return false;
            }
            Vector qp = q - p;
            t = (qp.X * s.Y - qp.Y * s.X) / cross;
            double u = (qp.X * r.Y - qp.Y * r.X) / cross;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }

        /// <summary>
        /// Task 24: smooths a freshly collected freehand stroke according to
        /// <see cref="StrokeSmoothingLevel"/> (0=Off, 1=Low, 2=Medium,
        /// 3=High). Levels 1-3 replace each point with the moving average of
        /// its w = 1/2/4 neighbours on each side (index range clamped at the
        /// endpoints), preserving the ORIGINAL centre point's PressureFactor;
        /// FitToCurve stays on so rendering adds the usual curve fit on top.
        /// Level 0 keeps the points untouched but swaps the stroke for a
        /// FitToCurve=false clone, because WPF's curve-fit rendering would
        /// otherwise smooth the visual even with smoothing "Off". The
        /// replacement is swapped into <see cref="_strokes"/> in place — the
        /// same pattern as <see cref="ApplyInkSimulation"/> — so one undo
        /// step covers the stroke and it saves like any other stroke.
        /// Strokes with fewer than 3 points (taps, Shift-straightened lines)
        /// have nothing to average and are returned as-is.
        /// </summary>
        private Stroke ApplySmoothing(Stroke stroke)
        {
            int level = StrokeSmoothingLevel;
            var points = stroke.StylusPoints;

            if (level <= 0)
            {
                // Off: true raw trajectory — keep the points, drop the
                // curve-fit rendering. Already-raw strokes (e.g. the Shift
                // straightened replacement sets FitToCurve=false) skip the
                // swap entirely.
                if (!stroke.DrawingAttributes.FitToCurve)
                    return stroke;

                var rawPoints = new StylusPointCollection();
                foreach (var sp in points)
                    rawPoints.Add(sp);

                var rawAttributes = stroke.DrawingAttributes.Clone();
                rawAttributes.FitToCurve = false;

                var rawReplacement = new Stroke(rawPoints) { DrawingAttributes = rawAttributes };
                int rawIndex = _strokes.IndexOf(stroke);
                if (rawIndex >= 0)
                    ReplaceStrokeAt(rawIndex, rawReplacement, EnsureStrokeToken(stroke), GetStrokeSide(stroke));
                else
                    AddStrokeToCollection(rawReplacement);
                return rawReplacement;
            }

            int count = points.Count;
            if (count < 3)
                return stroke;

            int window = level == 1 ? 1 : level == 2 ? 2 : 4;
            // Clamp the effective window for short strokes: with a window
            // wider than half the stroke every averaged point collapses
            // toward the centroid and the stroke shrinks to a dot.
            int maxWindow = (count - 1) / 2;
            if (window > maxWindow)
                window = maxWindow;

            var smoothed = new StylusPointCollection();
            for (int i = 0; i < count; i++)
            {
                int lo = Math.Max(0, i - window);
                int hi = Math.Min(count - 1, i + window);
                double sumX = 0, sumY = 0;
                for (int j = lo; j <= hi; j++)
                {
                    sumX += points[j].X;
                    sumY += points[j].Y;
                }
                int n = hi - lo + 1;
                smoothed.Add(new StylusPoint(sumX / n, sumY / n, points[i].PressureFactor));
            }

            var attributes = stroke.DrawingAttributes.Clone();
            attributes.FitToCurve = true;

            var replacement = new Stroke(smoothed) { DrawingAttributes = attributes };
            int index = _strokes.IndexOf(stroke);
            if (index >= 0)
                ReplaceStrokeAt(index, replacement, EnsureStrokeToken(stroke), GetStrokeSide(stroke));
            else
                AddStrokeToCollection(replacement);
            return replacement;
        }

        private static void PreserveTapStroke(Stroke stroke)
        {
            if (stroke?.StylusPoints == null || stroke.StylusPoints.Count != 1)
            {
                return;
            }

            // A tap that should render as a dot may arrive as a single stylus
            // point. Expand it to a tiny segment so WPF keeps it visible.
            var point = stroke.StylusPoints[0];
            stroke.StylusPoints.Add(new StylusPoint(point.X + 0.1, point.Y));
        }

        private void InkCanvas_StrokeErasing(object sender, InkCanvasStrokeErasingEventArgs e)
        {
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        private void InkCanvas_StrokeErased(object sender, RoutedEventArgs e)
        {
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        private void PdfPageControl_Loaded(object sender, RoutedEventArgs e)
        {
            // A virtualised page may unload while its visual tree remains in
            // the annotation model. Reattach only the marker-local handlers
            // when it returns; this keeps pointer captures and ContextMenu
            // z-order hooks from accumulating across page virtualization.
            foreach (var container in ImageOverlayCanvas.Children.OfType<Grid>()
                .Where(IsStickyNoteContainer)
                .ToList())
            {
                AttachStickyNoteHandlers(container);
            }

            // Reveal is a session-only interaction. Revirtualising a page
            // must never carry a previously revealed mask into the new view.
            foreach (var visual in _hiddenInkVisuals.Values)
                visual.Visibility = Visibility.Visible;
            StopAllHiddenInkRevealTimers();
            UpdateHiddenInkHitTesting();

            TextOverlayCanvas.MouseDown += TextOverlayCanvas_MouseDown;
            PdfTextSelectionCanvas.MouseLeftButtonDown += PdfTextSelectionCanvas_MouseLeftButtonDown;
            PdfTextSelectionCanvas.MouseMove += PdfTextSelectionCanvas_MouseMove;
            PdfTextSelectionCanvas.MouseLeftButtonUp += PdfTextSelectionCanvas_MouseLeftButtonUp;
            PdfTextSelectionCanvas.StylusDown += PdfTextSelectionCanvas_StylusDown;
            PdfTextSelectionCanvas.StylusMove += PdfTextSelectionCanvas_StylusMove;
            PdfTextSelectionCanvas.StylusUp += PdfTextSelectionCanvas_StylusUp;
            PageGrid.MouseDown += PageGrid_MouseDown;
            InkCanvas.MouseLeftButtonDown += InkCanvas_MouseLeftButtonDown;
            InkCanvas.MouseMove += InkCanvas_MouseMove;
            InkCanvas.MouseUp += InkCanvas_MouseUp;
            InkCanvas.LostMouseCapture += InkCanvas_LostMouseCapture;
            InkCanvas.StylusDown += InkCanvas_StylusDown;
            InkCanvas.StylusMove += InkCanvas_StylusMove;
            InkCanvas.StylusUp += InkCanvas_StylusUp;
            InkCanvas.LostStylusCapture += InkCanvas_LostStylusCapture;
            InkCanvas.StylusInAirMove += InkCanvas_StylusInAirMove;
            InkCanvas.StylusButtonDown += InkCanvas_StylusButtonDown;
            InkCanvas.StylusButtonUp += InkCanvas_StylusButtonUp;
            InkCanvas.MouseEnter += InkCanvas_MouseEnter;
            InkCanvas.MouseLeave += InkCanvas_MouseLeave;
            SelectionOverlayCanvas.MouseLeftButtonDown += SelectionOverlayCanvas_MouseLeftButtonDown;
            SelectionOverlayCanvas.MouseMove += SelectionOverlayCanvas_MouseMove;
            SelectionOverlayCanvas.MouseLeftButtonUp += SelectionOverlayCanvas_MouseLeftButtonUp;
            SelectionOverlayCanvas.LostMouseCapture += SelectionOverlayCanvas_LostMouseCapture;
            SelectionOverlayCanvas.StylusDown += SelectionOverlayCanvas_StylusDown;
            SelectionOverlayCanvas.StylusMove += SelectionOverlayCanvas_StylusMove;
            SelectionOverlayCanvas.StylusUp += SelectionOverlayCanvas_StylusUp;
            SelectionOverlayCanvas.LostStylusCapture += SelectionOverlayCanvas_LostStylusCapture;

            // Fix for auto-scroll bug: Prevent ScrollViewer from scrolling when InkCanvas gets focus
            this.RequestBringIntoView += PdfPageControl_RequestBringIntoView;
        }

        private void PdfPageControl_Unloaded(object sender, RoutedEventArgs e)
        {
            CancelInteraction("page unloaded");
            foreach (var container in _stickyInteractionHandlers.Keys.ToList())
                DetachStickyNoteHandlers(container);

            _isErasing = false;
            _erasePoints = null;
            EndEraseGesture();
            StopSelectionDashAnimation();
            _isLaserDrawing = false;
            _laserPolyline = null;
            _liveLaserPolylines.Clear();
            LaserInkCanvas.Children.Clear();
            foreach (var visual in _hiddenInkVisuals.Values)
                visual.Visibility = Visibility.Visible;
            StopAllHiddenInkRevealTimers();
            TextOverlayCanvas.MouseDown -= TextOverlayCanvas_MouseDown;
            PdfTextSelectionCanvas.MouseLeftButtonDown -= PdfTextSelectionCanvas_MouseLeftButtonDown;
            PdfTextSelectionCanvas.MouseMove -= PdfTextSelectionCanvas_MouseMove;
            PdfTextSelectionCanvas.MouseLeftButtonUp -= PdfTextSelectionCanvas_MouseLeftButtonUp;
            PdfTextSelectionCanvas.StylusDown -= PdfTextSelectionCanvas_StylusDown;
            PdfTextSelectionCanvas.StylusMove -= PdfTextSelectionCanvas_StylusMove;
            PdfTextSelectionCanvas.StylusUp -= PdfTextSelectionCanvas_StylusUp;
            PageGrid.MouseDown -= PageGrid_MouseDown;
            InkCanvas.MouseLeftButtonDown -= InkCanvas_MouseLeftButtonDown;
            InkCanvas.MouseMove -= InkCanvas_MouseMove;
            InkCanvas.MouseUp -= InkCanvas_MouseUp;
            InkCanvas.LostMouseCapture -= InkCanvas_LostMouseCapture;
            InkCanvas.StylusDown -= InkCanvas_StylusDown;
            InkCanvas.StylusMove -= InkCanvas_StylusMove;
            InkCanvas.StylusUp -= InkCanvas_StylusUp;
            InkCanvas.LostStylusCapture -= InkCanvas_LostStylusCapture;
            InkCanvas.StylusInAirMove -= InkCanvas_StylusInAirMove;
            InkCanvas.StylusButtonDown -= InkCanvas_StylusButtonDown;
            InkCanvas.StylusButtonUp -= InkCanvas_StylusButtonUp;
            InkCanvas.MouseEnter -= InkCanvas_MouseEnter;
            InkCanvas.MouseLeave -= InkCanvas_MouseLeave;
            SelectionOverlayCanvas.MouseLeftButtonDown -= SelectionOverlayCanvas_MouseLeftButtonDown;
            SelectionOverlayCanvas.MouseMove -= SelectionOverlayCanvas_MouseMove;
            SelectionOverlayCanvas.MouseLeftButtonUp -= SelectionOverlayCanvas_MouseLeftButtonUp;
            SelectionOverlayCanvas.LostMouseCapture -= SelectionOverlayCanvas_LostMouseCapture;
            SelectionOverlayCanvas.StylusDown -= SelectionOverlayCanvas_StylusDown;
            SelectionOverlayCanvas.StylusMove -= SelectionOverlayCanvas_StylusMove;
            SelectionOverlayCanvas.StylusUp -= SelectionOverlayCanvas_StylusUp;
            SelectionOverlayCanvas.LostStylusCapture -= SelectionOverlayCanvas_LostStylusCapture;

            this.RequestBringIntoView -= PdfPageControl_RequestBringIntoView;
        }

        private void PdfPageControl_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void InkCanvas_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_currentMode == CustomInkInputProcessingMode.Erasing || _isStylusInverted
                || _currentMode == CustomInkInputProcessingMode.Inking
                || _currentMode == CustomInkInputProcessingMode.HiddenInk)
            {
                UpdateBrushIndicatorStyle();
                EraserIndicator.Visibility = Visibility.Visible;
                Cursor = Cursors.None;
                UpdateEraserIndicatorPosition(e.GetPosition(PageGrid));
            }
        }

        private void InkCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            EraserIndicator.Visibility = Visibility.Collapsed;
            Cursor = Cursors.Arrow;
            _isStylusInverted = false;
        }

        private void InkCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _pendingPopupDismissalInkGesture = false;
            if (_isErasing || HasPendingEraseGesture())
            {
                _isErasing = false;
                _erasePoints = null;
                EndEraseGesture();
            }
        }

        private void InkCanvas_LostStylusCapture(object sender, StylusEventArgs e)
        {
            _pendingPopupDismissalInkGesture = false;
            if (_isErasing || HasPendingEraseGesture())
            {
                _isErasing = false;
                _erasePoints = null;
                EndEraseGesture();
            }
        }

        /// <summary>
        /// Detects when the stylus is hovering in inverted (eraser) mode.
        /// This is the standard Windows Ink path for IsEraser: when the user
        /// flips the pen to the eraser end, StylusDevice.Inverted becomes true
        /// even while hovering, before contact.  Also triggered by Huawei
        /// M-Pencil when MateBook-E-Pen patches AcAppDaemon.exe.
        /// </summary>
        private void InkCanvas_StylusInAirMove(object sender, StylusEventArgs e)
        {
            // Probe the device early while hovering so capabilities are known
            // before the first stroke lands.
            _penService?.ProbeDevice(e.StylusDevice);

            bool inverted = e.StylusDevice?.Inverted == true;

            if (inverted != _isStylusInverted)
            {
                _isStylusInverted = inverted;
                Console.WriteLine(
                    $"[PdfPageControl] Stylus Inverted changed �?{inverted} (device={e.StylusDevice?.Name})");

                if (inverted)
                {
                    // Show eraser indicator while hovering with inverted pen
                    InkCanvas.EditingMode = InkCanvasEditingMode.None; // suppress inking
                    EraserIndicator.Visibility = Visibility.Visible;
                    InkCanvas.Cursor = Cursors.None;
                }
                else if (_currentMode != CustomInkInputProcessingMode.Erasing)
                {
                    // Pen flipped back to normal �?restore previous mode
                    EraserIndicator.Visibility = Visibility.Collapsed;
                    SetInputMode(_currentMode);
                }
            }

            if (inverted)
            {
                UpdateBrushIndicatorStyle();
                UpdateEraserIndicatorPosition(e.GetPosition(PageGrid));
            }
            else if (_currentMode == CustomInkInputProcessingMode.Inking
                || _currentMode == CustomInkInputProcessingMode.HiddenInk)
            {
                UpdateBrushIndicatorStyle();
                UpdateEraserIndicatorPosition(e.GetPosition(PageGrid));
            }
        }

        private void TextOverlayCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Only forward clicks directly on the canvas background, not on child elements like TextBoxes
            if (e.OriginalSource == TextOverlayCanvas)
                TextOverlayPointerPressed?.Invoke(this, e);
        }

        private void PageGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentMode == CustomInkInputProcessingMode.None)
            {
                if (e.OriginalSource is DependencyObject source &&
                    IsDescendantOf(source, TextOverlayCanvas) &&
                    source != TextOverlayCanvas)
                {
                    return;
                }

                BackgroundPointerPressed?.Invoke(this, e);
            }
        }

        private void PdfTextSelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPdfTextSelectionEnabled)
                return;

            PdfTextSelectionCanvas.CaptureMouse();
            PdfTextSelectionPointerPressed?.Invoke(this, new PdfTextSelectionPointerEventArgs(e.GetPosition(PageGrid), e.LeftButton));
            e.Handled = true;
        }

        private void PdfTextSelectionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPdfTextSelectionEnabled)
                return;

            PdfTextSelectionPointerMoved?.Invoke(this, new PdfTextSelectionPointerEventArgs(e.GetPosition(PageGrid), e.LeftButton));
            if (PdfTextSelectionCanvas.IsMouseCaptured)
                e.Handled = true;
        }

        private void PdfTextSelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPdfTextSelectionEnabled)
                return;

            PdfTextSelectionPointerReleased?.Invoke(this, new PdfTextSelectionPointerEventArgs(e.GetPosition(PageGrid), e.LeftButton));
            if (PdfTextSelectionCanvas.IsMouseCaptured)
                PdfTextSelectionCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void PdfTextSelectionCanvas_StylusDown(object sender, StylusDownEventArgs e)
        {
            if (!_isPdfTextSelectionEnabled)
                return;

            // Let finger touches pass through to WPF manipulation (pan/zoom).
            // Only pen/stylus devices should trigger PDF text selection.
            if (IsTouchFinger(e.StylusDevice))
                return;

            PdfTextSelectionCanvas.CaptureStylus();
            PdfTextSelectionPointerPressed?.Invoke(this, new PdfTextSelectionPointerEventArgs(e.GetPosition(PageGrid), MouseButtonState.Pressed));
            e.Handled = true;
        }

        private void PdfTextSelectionCanvas_StylusMove(object sender, StylusEventArgs e)
        {
            if (!_isPdfTextSelectionEnabled)
                return;

            if (IsTouchFinger(e.StylusDevice))
                return;

            PdfTextSelectionPointerMoved?.Invoke(this, new PdfTextSelectionPointerEventArgs(e.GetPosition(PageGrid), MouseButtonState.Pressed));
            if (PdfTextSelectionCanvas.IsStylusCaptured)
                e.Handled = true;
        }

        private void PdfTextSelectionCanvas_StylusUp(object sender, StylusEventArgs e)
        {
            if (!_isPdfTextSelectionEnabled)
                return;

            if (IsTouchFinger(e.StylusDevice))
                return;

            PdfTextSelectionPointerReleased?.Invoke(this, new PdfTextSelectionPointerEventArgs(e.GetPosition(PageGrid), MouseButtonState.Released));
            if (PdfTextSelectionCanvas.IsStylusCaptured)
                PdfTextSelectionCanvas.ReleaseStylusCapture();
            e.Handled = true;
        }

        private bool _isBarrelButtonPressed = false;
        private DateTime _lastBarrelButtonDownTime = DateTime.MinValue;
        private CustomInkInputProcessingMode _previousMode = CustomInkInputProcessingMode.Inking;

        private static readonly Guid[] SideButtonGuids = new[]
        {
            StylusPointProperties.BarrelButton.Id,
            StylusPointProperties.SecondaryTipButton.Id,
            // NOTE: TipButton is the primary pen tip contact, NOT a side button.
            // Including it here caused every pen-down to be treated as a barrel-button
            // press, breaking inking on Huawei M-Pencil and similar devices.
        };

        private void InkCanvas_StylusButtonDown(object sender, StylusButtonEventArgs e)
        {
            Console.WriteLine($"[PdfPageControl] StylusButtonDown: name={e.StylusButton.Name}, GUID={e.StylusButton.Guid}, device={e.StylusDevice?.Name}");

            // Ignore the tip button �?it fires on every pen contact and is NOT a side button.
            if (e.StylusButton.Guid == StylusPointProperties.TipButton.Id)
                return;

            bool isSideButton = false;
            foreach (var guid in SideButtonGuids)
            {
                if (e.StylusButton.Guid == guid)
                {
                    isSideButton = true;
                    break;
                }
            }

            if (isSideButton || e.StylusButton.Name.Contains("Barrel") || e.StylusButton.Name.Contains("Side") || e.StylusButton.Name.Contains("Secondary"))
            {
                _isBarrelButtonPressed = true;

                if ((DateTime.Now - _lastBarrelButtonDownTime).TotalMilliseconds < 500)
                {
                    if (_currentMode == CustomInkInputProcessingMode.Erasing)
                    {
                        SetInputMode(_previousMode);
                    }
                    else
                    {
                        _previousMode = _currentMode;
                        SetInputMode(CustomInkInputProcessingMode.Erasing);
                    }
                    _lastBarrelButtonDownTime = DateTime.MinValue;
                }
                else
                {
                    _lastBarrelButtonDownTime = DateTime.Now;
                }

                if (InkCanvas.EditingMode != InkCanvasEditingMode.None)
                {
                    InkCanvas.EditingMode = InkCanvasEditingMode.None;
                }
            }
        }

        private void InkCanvas_StylusButtonUp(object sender, StylusButtonEventArgs e)
        {
            if (e.StylusButton.Guid == StylusPointProperties.TipButton.Id)
                return;

            bool isSideButton = false;
            foreach (var guid in SideButtonGuids)
            {
                if (e.StylusButton.Guid == guid)
                {
                    isSideButton = true;
                    break;
                }
            }

            if (isSideButton || e.StylusButton.Name.Contains("Barrel") || e.StylusButton.Name.Contains("Side") || e.StylusButton.Name.Contains("Secondary"))
            {
                _isBarrelButtonPressed = false;
                SetInputMode(_currentMode);
            }
        }

        /// <summary>
        /// Task 15: blocks InkCanvas native mouse inking while pen-only mode
        /// is active. WPF InkCanvas starts mouse ink on MouseLeftButtonDown,
        /// so marking the tunneling preview event handled (handledEventsToo)
        /// prevents the collection from ever starting. Pen contact promotes
        /// to mouse with a non-null StylusDevice and passes through; pure
        /// mouse (null device) and finger-promoted events are blocked.
        /// </summary>
        private void InkCanvas_PreviewMouseLeftButtonDown_PenOnly(object sender, MouseButtonEventArgs e)
        {
            if (PenOnlyMode && IsInkCreationModeActive && ShouldBlockNonPenInk(e.StylusDevice))
                e.Handled = true;
        }

        /// <summary>
        /// Task 15: blocks finger-touch ink on the InkCanvas while pen-only
        /// mode is active. The InkCanvas collects stylus input — including
        /// touch promoted to stylus — natively from StylusDown, so touch must
        /// be stopped before the bubbling event fires. Freehand finger
        /// strokes are normally already intercepted upstream (EditorPage
        /// turns single-finger touch into pan while a drawing tool is
        /// active); this also covers the shape tool, whose touches are not
        /// intercepted there. Pen-only applies to ink creation only — the
        /// eraser keeps working for every device.
        /// </summary>
        private void InkCanvas_PreviewStylusDown_PenOnly(object sender, StylusDownEventArgs e)
        {
            if (PenOnlyMode && IsInkCreationModeActive && ShouldBlockNonPenInk(e.StylusDevice))
                e.Handled = true;
        }

        private void InkCanvas_StylusDown(object sender, StylusDownEventArgs e)
        {
            // Probe the stylus device so WindowsPenService can detect its
            // capabilities (pressure levels, tilt, barrel button, etc.).
            _penService?.ProbeDevice(e.StylusDevice);

            bool shouldErase = e.Inverted || _isStylusInverted
                || _isBarrelButtonPressed
                || _currentMode == CustomInkInputProcessingMode.Erasing;

            Console.WriteLine($"[PdfPageControl] StylusDown: Inverted={e.Inverted}, _isStylusInverted={_isStylusInverted}, barrel={_isBarrelButtonPressed}, mode={_currentMode}, shouldErase={shouldErase}, device={e.StylusDevice?.Name}");

            // Task 15: pen-only mode — a finger must not start a shape drag
            // (palm rejection). The preview stylus filter normally blocks
            // earlier; this guard keeps the block in place even if the
            // event reaches here unhandled.
            if (!shouldErase && PenOnlyMode
                && _currentMode == CustomInkInputProcessingMode.Shape
                && ShouldBlockNonPenInk(e.StylusDevice))
            {
                return;
            }

            // Shape tool: begin a shape drag instead of freehand ink. An
            // inverted pen / barrel button still erases (shouldErase wins).
            if (!shouldErase && _currentMode == CustomInkInputProcessingMode.Shape)
            {
                BeginShapeDrag(e.GetPosition(InkCanvas));
                InkCanvas.CaptureStylus();
                e.Handled = true;
                return;
            }

            // Task 20: laser pointer — start an ephemeral polyline on the
            // laser layer. No pen-only guard here: mouse input is explicitly
            // allowed for the laser tool.
            if (!shouldErase && _currentMode == CustomInkInputProcessingMode.Laser)
            {
                BeginLaserStroke(e.GetPosition(InkCanvas));
                InkCanvas.CaptureStylus();
                e.Handled = true;
                return;
            }

            // Task 27: area highlight — drag-to-draw a translucent rectangle.
            if (!shouldErase && _currentMode == CustomInkInputProcessingMode.AreaHighlight)
            {
                BeginAreaHighlightDrag(e.GetPosition(InkCanvas));
                InkCanvas.CaptureStylus();
                e.Handled = true;
                return;
            }

            if (shouldErase)
            {
                BeginEraseGesture();
                _isErasing = true;
                // Ensure InkCanvas doesn't draw while we're erasing
                if (InkCanvas.EditingMode != InkCanvasEditingMode.None)
                    InkCanvas.EditingMode = InkCanvasEditingMode.None;

                // Show eraser indicator at contact point
                UpdateBrushIndicatorStyle();
                EraserIndicator.Visibility = Visibility.Visible;
                InkCanvas.Cursor = Cursors.None;
                UpdateEraserIndicatorPosition(e.GetPosition(PageGrid));

                _erasePoints = e.GetStylusPoints(InkCanvas);
                EraseStrokesAtPoints(_erasePoints);
                e.Handled = true;
            }
        }

        private void InkCanvas_StylusMove(object sender, StylusEventArgs e)
        {
            if (_isShapeDragging)
            {
                UpdateShapeDrag(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            // Task 20: laser pointer live drawing.
            if (_isLaserDrawing)
            {
                UpdateLaserStroke(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            // Task 27: area-highlight live preview.
            if (_isAreaHighlightDragging)
            {
                UpdateAreaHighlightDrag(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            bool shouldErase = _isErasing
                && (e.StylusDevice?.Inverted == true || _isStylusInverted
                    || _isBarrelButtonPressed
                    || _currentMode == CustomInkInputProcessingMode.Erasing);

            if (shouldErase)
            {
                UpdateEraserIndicatorPosition(e.GetPosition(PageGrid));
                var newPoints = e.GetStylusPoints(InkCanvas);
                EraseStrokesAtPoints(newPoints);
            }
        }

        private void InkCanvas_StylusUp(object sender, StylusEventArgs e)
        {
            // Safety: always clear barrel-button flag on pen lift to prevent stuck state.
            // On some Huawei digitizers, StylusButtonUp may not fire reliably for all buttons.
            _isBarrelButtonPressed = false;

            if (_isShapeDragging)
            {
                InkCanvas.ReleaseStylusCapture();
                EndShapeDrag(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            // Task 20: laser pointer — release the live polyline and start
            // its fade-out.
            if (_isLaserDrawing)
            {
                InkCanvas.ReleaseStylusCapture();
                EndLaserStroke(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            // Task 27: area highlight — commit the dragged rectangle.
            if (_isAreaHighlightDragging)
            {
                InkCanvas.ReleaseStylusCapture();
                EndAreaHighlightDrag(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            if (_isErasing)
            {
                _isErasing = false;
                _erasePoints = null;
                EndEraseGesture();

                // If the pen is still inverted (hovering after lift-off), keep
                // showing the eraser indicator.  Otherwise restore the mode.
                if (!_isStylusInverted && _currentMode != CustomInkInputProcessingMode.Erasing)
                {
                    EraserIndicator.Visibility = Visibility.Collapsed;
                    SetInputMode(_currentMode);
                }
            }

            // A PenOnly-blocked or otherwise intercepted stylus gesture may
            // reach StylusUp without ever raising StrokeCollected. Do not let
            // its popup-dismissal intent suppress a later unrelated stroke.
            _pendingPopupDismissalInkGesture = false;
        }

        private void InkCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Skip mouse events synthesised from stylus contact — the stylus
            // handlers own that gesture.
            if (e.StylusDevice != null)
                return;

            // Task 15: pen-only mode — the mouse must not start a shape
            // drag. (The preview mouse filter normally blocks earlier; this
            // guard keeps the block in place even if the event reaches here
            // unhandled.)
            if (PenOnlyMode
                && _currentMode == CustomInkInputProcessingMode.Shape
                && ShouldBlockNonPenInk(e.StylusDevice))
                return;

            if (_currentMode == CustomInkInputProcessingMode.Shape && !_isStylusInverted)
            {
                BeginShapeDrag(e.GetPosition(InkCanvas));
                InkCanvas.CaptureMouse();
                e.Handled = true;
            }
            // Task 20: laser pointer accepts mouse input by design (no
            // pen-only guard — pointing with a mouse is a primary use case).
            else if (_currentMode == CustomInkInputProcessingMode.Laser && !_isStylusInverted)
            {
                BeginLaserStroke(e.GetPosition(InkCanvas));
                InkCanvas.CaptureMouse();
                e.Handled = true;
            }
            else if (_currentMode == CustomInkInputProcessingMode.AreaHighlight && !_isStylusInverted)
            {
                BeginAreaHighlightDrag(e.GetPosition(InkCanvas));
                InkCanvas.CaptureMouse();
                e.Handled = true;
            }
            else if (_currentMode == CustomInkInputProcessingMode.Erasing && !_isStylusInverted)
            {
                BeginEraseGesture();
                EraseStrokesAtPoints(new StylusPointCollection
                {
                    new StylusPoint(e.GetPosition(InkCanvas).X, e.GetPosition(InkCanvas).Y)
                });
                InkCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void InkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isShapeDragging && e.StylusDevice == null)
            {
                UpdateShapeDrag(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            // Task 20: laser pointer live drawing (mouse path).
            if (_isLaserDrawing && e.StylusDevice == null)
            {
                UpdateLaserStroke(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            if (_isAreaHighlightDragging && e.StylusDevice == null)
            {
                UpdateAreaHighlightDrag(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            if (_currentMode == CustomInkInputProcessingMode.Erasing
                || _currentMode == CustomInkInputProcessingMode.Inking
                || _currentMode == CustomInkInputProcessingMode.HiddenInk)
            {
                UpdateBrushIndicatorStyle();
                var point = e.GetPosition(PageGrid);
                UpdateEraserIndicatorPosition(point);

                if (_currentMode == CustomInkInputProcessingMode.Erasing && e.LeftButton == MouseButtonState.Pressed)
                {
                    EraseStrokesAtPoints(new StylusPointCollection { new StylusPoint(e.GetPosition(InkCanvas).X, e.GetPosition(InkCanvas).Y) });
                }
            }
        }

        private void UpdateBrushIndicatorStyle()
        {
            if (_currentMode == CustomInkInputProcessingMode.Erasing || _isStylusInverted)
            {
                EraserIndicator.Width = _eraserSize;
                EraserIndicator.Height = _eraserSize;
                EraserIndicator.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ThemeAccentBrush");
                EraserIndicator.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ThemeSelectionBrush");
            }
            else if (_currentMode == CustomInkInputProcessingMode.Inking
                || _currentMode == CustomInkInputProcessingMode.HiddenInk)
            {
                double size = _currentMode == CustomInkInputProcessingMode.HiddenInk
                    ? HiddenInkSize
                    : _drawingAttributes.Width;
                EraserIndicator.Width = Math.Max(4, size);
                EraserIndicator.Height = Math.Max(4, size);

                Color c = _currentMode == CustomInkInputProcessingMode.HiddenInk
                    ? HiddenInkMaskColor
                    : _drawingAttributes.Color;
                EraserIndicator.Stroke = new SolidColorBrush(Color.FromArgb(200, c.R, c.G, c.B));
                EraserIndicator.Fill = _currentMode == CustomInkInputProcessingMode.HiddenInk
                    ? new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B))
                    : _drawingAttributes.IsHighlighter
                        ? new SolidColorBrush(Color.FromArgb(50, c.R, c.G, c.B))
                        : Brushes.Transparent;
            }
        }

        private void UpdateEraserIndicatorPosition(Point point)
        {
            double w = EraserIndicator.Width;
            double h = EraserIndicator.Height;
            Canvas.SetLeft(EraserIndicator, point.X - w / 2);
            Canvas.SetTop(EraserIndicator, point.Y - h / 2);
            EraserIndicator.Visibility = Visibility.Visible;
        }

        private void InkCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isShapeDragging && e.StylusDevice == null)
            {
                InkCanvas.ReleaseMouseCapture();
                EndShapeDrag(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            // Task 20: laser pointer — release the live polyline and start
            // its fade-out (mouse path).
            if (_isLaserDrawing && e.StylusDevice == null)
            {
                InkCanvas.ReleaseMouseCapture();
                EndLaserStroke(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            if (_isAreaHighlightDragging && e.StylusDevice == null)
            {
                InkCanvas.ReleaseMouseCapture();
                EndAreaHighlightDrag(e.GetPosition(InkCanvas));
                e.Handled = true;
                return;
            }

            if (_currentMode == CustomInkInputProcessingMode.Erasing && e.StylusDevice == null
                && InkCanvas.IsMouseCaptured)
            {
                InkCanvas.ReleaseMouseCapture();
            }

            _isErasing = false;
            // Mouse-erase gestures never set _isErasing (they erase straight
            // from MouseMove while the left button is pressed), so the gesture
            // is always flushed here; for pen input this is a no-op because
            // StylusUp has already flushed it.
            EndEraseGesture();

            // A PenOnly-blocked or otherwise intercepted mouse gesture may
            // reach MouseUp without ever raising StrokeCollected. Do not let
            // its popup-dismissal intent suppress a later unrelated stroke.
            _pendingPopupDismissalInkGesture = false;
        }

        #region Shape tool (drag-to-draw line / rectangle / ellipse / arrow)

        private void BeginShapeDrag(Point position)
        {
            _isShapeDragging = true;
            _shapeAnchor = position;
            _shapeCurrent = position;

            ShapePreviewCanvas.Children.Clear();
            _shapePreviewPolylines.Clear();

            // Arrow preview = shaft + head 'V' (two polylines); others = one.
            int previewCount = CurrentShape == ShapeKind.Arrow ? 2 : 1;
            for (int i = 0; i < previewCount; i++)
                _shapePreviewPolylines.Add(CreateShapePreviewPolyline());

            UpdateShapePreview();
        }

        private void UpdateShapeDrag(Point position)
        {
            // Task 21: the live preview runs through the same Shift
            // constraint as the commit, so what you see is what you get.
            _shapeCurrent = ConstrainShapeEndpoints(_shapeAnchor, position, CurrentShape, IsShiftHeld());
            UpdateShapePreview();
        }

        private void EndShapeDrag(Point position)
        {
            if (!_isShapeDragging)
                return;

            _isShapeDragging = false;
            ShapePreviewCanvas.Children.Clear();
            _shapePreviewPolylines.Clear();

            double dx = position.X - _shapeAnchor.X;
            double dy = position.Y - _shapeAnchor.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < ShapeDragThreshold)
                return; // treat as a tap — no accidental dots

            // Task 21: committed endpoints share the constraint with the
            // preview (state of Shift at pointer-up decides).
            _shapeCurrent = ConstrainShapeEndpoints(_shapeAnchor, position, CurrentShape, IsShiftHeld());
            CommitShape();
        }

        private static bool IsShiftHeld()
        {
            return (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        }

        /// <summary>
        /// Task 21: Shift constraint for the shape tool. With Shift held,
        /// line/arrow snap their direction to the nearest multiple of 45°
        /// (the end point is re-projected onto the snapped ray at the
        /// original length) and rectangle/ellipse become square/circle
        /// (side = max(|dx|,|dy|), drag direction signs preserved). Without
        /// Shift the end point is returned unchanged. Feeding both the live
        /// preview and the final commit keeps them consistent.
        /// </summary>
        private static Point ConstrainShapeEndpoints(Point start, Point end, ShapeKind kind, bool isShift)
        {
            if (!isShift)
                return end;

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;

            switch (kind)
            {
                case ShapeKind.Line:
                case ShapeKind.Arrow:
                {
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < double.Epsilon)
                        return end;
                    double snapped = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4.0)) * (Math.PI / 4.0);
                    return new Point(start.X + len * Math.Cos(snapped), start.Y + len * Math.Sin(snapped));
                }
                case ShapeKind.Rectangle:
                case ShapeKind.Ellipse:
                case ShapeKind.Triangle:
                case ShapeKind.Diamond:
                case ShapeKind.Parallelogram:
                case ShapeKind.Pentagon:
                case ShapeKind.Hexagon:
                {
                    // Square / circle: the larger extent wins; sign(0)
                    // defaults to + so pure vertical/horizontal drags still
                    // produce a full-size square.
                    double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    double sx = dx >= 0 ? 1 : -1;
                    double sy = dy >= 0 ? 1 : -1;
                    return new Point(start.X + sx * side, start.Y + sy * side);
                }
                default:
                    return end;
            }
        }

        private void UpdateShapePreview()
        {
            if (CurrentShape == ShapeKind.Arrow)
            {
                BuildArrowGeometry(_shapeAnchor, _shapeCurrent, ShapeStrokeSize, out var shaft, out var head);
                _shapePreviewPolylines[0].Points = new PointCollection(shaft);
                _shapePreviewPolylines[1].Points = new PointCollection(head);
            }
            else
            {
                _shapePreviewPolylines[0].Points =
                    new PointCollection(BuildShapeOutline(CurrentShape, _shapeAnchor, _shapeCurrent));
            }
        }

        private System.Windows.Shapes.Polyline CreateShapePreviewPolyline()
        {
            var preview = new System.Windows.Shapes.Polyline
            {
                Stroke = new SolidColorBrush(ShapeColor),
                StrokeThickness = Math.Max(1.0, ShapeStrokeSize),
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Opacity = 0.6,
                IsHitTestVisible = false
            };
            ShapePreviewCanvas.Children.Add(preview);
            return preview;
        }

        /// <summary>
        /// Outline point list for line / rectangle / ellipse. The ellipse is a
        /// parametric polygon with <see cref="EllipseSegmentCount"/> segments,
        /// which renders crisp because shape strokes use FitToCurve=false.
        /// </summary>
        private static List<Point> BuildShapeOutline(ShapeKind kind, Point start, Point end)
        {
            switch (kind)
            {
                case ShapeKind.Rectangle:
                    return new List<Point>
                    {
                        start,
                        new Point(end.X, start.Y),
                        end,
                        new Point(start.X, end.Y),
                        start // closed
                    };
                case ShapeKind.Ellipse:
                    var points = new List<Point>(EllipseSegmentCount + 1);
                    double cx = (start.X + end.X) / 2;
                    double cy = (start.Y + end.Y) / 2;
                    double rx = Math.Abs(end.X - start.X) / 2;
                    double ry = Math.Abs(end.Y - start.Y) / 2;
                    for (int i = 0; i <= EllipseSegmentCount; i++)
                    {
                        double t = 2 * Math.PI * i / EllipseSegmentCount;
                        points.Add(new Point(cx + rx * Math.Cos(t), cy + ry * Math.Sin(t)));
                    }
                    return points;
                case ShapeKind.Triangle:
                {
                    double left = Math.Min(start.X, end.X);
                    double right = Math.Max(start.X, end.X);
                    double top = Math.Min(start.Y, end.Y);
                    double bottom = Math.Max(start.Y, end.Y);
                    var apex = new Point((left + right) / 2, top);
                    return new List<Point>
                    {
                        apex,
                        new Point(right, bottom),
                        new Point(left, bottom),
                        apex
                    };
                }
                case ShapeKind.Diamond:
                {
                    double left = Math.Min(start.X, end.X);
                    double right = Math.Max(start.X, end.X);
                    double top = Math.Min(start.Y, end.Y);
                    double bottom = Math.Max(start.Y, end.Y);
                    double diamondCx = (left + right) / 2;
                    double diamondCy = (top + bottom) / 2;
                    var first = new Point(diamondCx, top);
                    return new List<Point>
                    {
                        first,
                        new Point(right, diamondCy),
                        new Point(diamondCx, bottom),
                        new Point(left, diamondCy),
                        first
                    };
                }
                case ShapeKind.Parallelogram:
                {
                    double left = Math.Min(start.X, end.X);
                    double right = Math.Max(start.X, end.X);
                    double top = Math.Min(start.Y, end.Y);
                    double bottom = Math.Max(start.Y, end.Y);
                    double inset = (right - left) * 0.24;
                    var first = new Point(left + inset, top);
                    return new List<Point>
                    {
                        first,
                        new Point(right, top),
                        new Point(right - inset, bottom),
                        new Point(left, bottom),
                        first
                    };
                }
                case ShapeKind.Pentagon:
                    return BuildRegularPolygonOutline(start, end, 5);
                case ShapeKind.Hexagon:
                    return BuildRegularPolygonOutline(start, end, 6);
                case ShapeKind.Line:
                default:
                    return new List<Point> { start, end };
            }
        }

        private static List<Point> BuildRegularPolygonOutline(Point start, Point end, int sides)
        {
            double left = Math.Min(start.X, end.X);
            double right = Math.Max(start.X, end.X);
            double top = Math.Min(start.Y, end.Y);
            double bottom = Math.Max(start.Y, end.Y);
            double cx = (left + right) / 2;
            double cy = (top + bottom) / 2;
            double rx = (right - left) / 2;
            double ry = (bottom - top) / 2;
            var points = new List<Point>(sides + 1);

            for (int i = 0; i < sides; i++)
            {
                double angle = -Math.PI / 2 + (2 * Math.PI * i / sides);
                points.Add(new Point(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle)));
            }

            points.Add(points[0]);
            return points;
        }

        /// <summary>
        /// Arrow geometry: a shaft (start → end) plus a two-wing head drawn as
        /// a 'V' through the end point. Head length scales with the stroke
        /// size and is capped at half the shaft length.
        /// </summary>
        private static void BuildArrowGeometry(Point start, Point end, double strokeSize,
            out List<Point> shaft, out List<Point> head)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);

            if (len < double.Epsilon)
            {
                shaft = new List<Point> { start, end };
                head = new List<Point> { end, end, end };
                return;
            }

            double ux = dx / len;   // unit direction
            double uy = dy / len;
            double px = -uy;        // unit perpendicular
            double py = ux;

            double headLen = Math.Min(Math.Max(strokeSize * 3.0, 10.0), len * 0.5);
            double headWidth = headLen * 0.6;

            var wing1 = new Point(end.X - ux * headLen + px * headWidth,
                                  end.Y - uy * headLen + py * headWidth);
            var wing2 = new Point(end.X - ux * headLen - px * headWidth,
                                  end.Y - uy * headLen - py * headWidth);

            shaft = new List<Point> { start, end };
            head = new List<Point> { wing1, end, wing2 };
        }

        /// <summary>
        /// Converts the finished drag into ink strokes and feeds them through
        /// the standard pipeline (InkCanvas.Strokes → InkMutated →
        /// StrokeCollectedUndoable), so undo / selection / copy-paste / save
        /// all work exactly like freehand ink. Line/Rectangle/Ellipse commit
        /// ONE stroke; the arrow commits TWO (shaft + head 'V') → two undo
        /// steps by design.
        /// </summary>
        private void CommitShape()
        {
            var attributes = new DrawingAttributes
            {
                Color = ShapeColor,
                Width = ShapeStrokeSize,
                Height = ShapeStrokeSize,
                FitToCurve = false,   // crisp polygon edges
                IsHighlighter = false,
                IgnorePressure = true // uniform width, no pressure jitter
            };

            List<List<Point>> segments;
            if (CurrentShape == ShapeKind.Arrow)
            {
                BuildArrowGeometry(_shapeAnchor, _shapeCurrent, ShapeStrokeSize, out var shaft, out var head);
                segments = new List<List<Point>> { shaft, head };
            }
            else
            {
                segments = new List<List<Point>>
                {
                    BuildShapeOutline(CurrentShape, _shapeAnchor, _shapeCurrent)
                };
            }

            var committed = new List<Stroke>();
            foreach (var segment in segments)
            {
                var stylusPoints = new StylusPointCollection();
                foreach (var p in segment)
                    stylusPoints.Add(new StylusPoint(p.X, p.Y));

                var stroke = new Stroke(stylusPoints) { DrawingAttributes = attributes.Clone() };
                AddStrokeToCollection(stroke);
                committed.Add(stroke);
            }

            InkMutated?.Invoke(this, EventArgs.Empty);
            foreach (var stroke in committed)
                StrokeCollectedUndoable?.Invoke(this, stroke);
        }

        #endregion

        #region Laser pointer (ephemeral ink, Task 20)

        /// <summary>
        /// Starts a live laser polyline at the given position. The polyline
        /// lives on <see cref="LaserInkCanvas"/> only — it never enters
        /// InkCanvas.Strokes, so undo / dirty / save are all unaffected.
        /// </summary>
        private void BeginLaserStroke(Point position)
        {
            _isLaserDrawing = true;

            var polyline = new System.Windows.Shapes.Polyline
            {
                Stroke = new SolidColorBrush(LaserColor),
                StrokeThickness = LaserStrokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
                Points = new PointCollection { position }
            };

            LaserInkCanvas.Children.Add(polyline);
            _liveLaserPolylines.Add(polyline);
            _laserPolyline = polyline;

            // Hard cap on simultaneously live strokes: drop the oldest at
            // once (it would fade within a second anyway).
            while (_liveLaserPolylines.Count > MaxLiveLaserPolylines)
            {
                var oldest = _liveLaserPolylines[0];
                _liveLaserPolylines.RemoveAt(0);
                LaserInkCanvas.Children.Remove(oldest);
                if (ReferenceEquals(_laserPolyline, oldest))
                    _laserPolyline = null;
            }
        }

        private void UpdateLaserStroke(Point position)
        {
            var polyline = _laserPolyline;
            if (polyline == null)
                return;

            var points = polyline.Points;
            if (points.Count > 0)
            {
                var last = points[points.Count - 1];
                double dx = position.X - last.X;
                double dy = position.Y - last.Y;
                if (dx * dx + dy * dy < 0.25) // < 0.5 px — skip duplicate points
                    return;
            }
            points.Add(position);
        }

        /// <summary>
        /// Finishes the live polyline and starts its fade-out: fully visible
        /// for <see cref="LaserFadeDelaySeconds"/>, then Opacity animates to
        /// 0 over <see cref="LaserFadeDurationSeconds"/> and the element
        /// removes itself from the layer when the animation completes.
        /// </summary>
        private void EndLaserStroke(Point position)
        {
            if (!_isLaserDrawing)
                return;

            _isLaserDrawing = false;
            UpdateLaserStroke(position);
            var polyline = _laserPolyline;
            _laserPolyline = null;
            if (polyline == null)
                return;

            TimeSpan fadeDuration = ThemeService.GetAnimationDuration(TimeSpan.FromSeconds(LaserFadeDurationSeconds));
            if (fadeDuration == TimeSpan.Zero || !ThemeService.ShouldAnimate)
            {
                LaserInkCanvas.Children.Remove(polyline);
                _liveLaserPolylines.Remove(polyline);
                return;
            }

            var fade = new DoubleAnimation(1.0, 0.0, fadeDuration)
            {
                BeginTime = TimeSpan.FromSeconds(LaserFadeDelaySeconds)
            };
            fade.Completed += (s, _) =>
            {
                LaserInkCanvas.Children.Remove(polyline);
                _liveLaserPolylines.Remove(polyline);
            };
            polyline.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        #endregion

        #region Area highlight tool

        private void BeginAreaHighlightDrag(Point position)
        {
            _isAreaHighlightDragging = true;
            _areaHighlightAnchor = position;

            _areaHighlightPreview = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromArgb(220, AreaHighlightColor.R, AreaHighlightColor.G, AreaHighlightColor.B)),
                Fill = new SolidColorBrush(Color.FromArgb(AreaHighlightOpacity, AreaHighlightColor.R, AreaHighlightColor.G, AreaHighlightColor.B)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                IsHitTestVisible = false
            };

            ShapePreviewCanvas.Children.Add(_areaHighlightPreview);
            UpdateAreaHighlightDrag(position);
        }

        private void UpdateAreaHighlightDrag(Point position)
        {
            if (!_isAreaHighlightDragging || _areaHighlightPreview == null)
                return;

            Rect rect = NormalizeAreaHighlightRect(_areaHighlightAnchor, position);
            Canvas.SetLeft(_areaHighlightPreview, rect.Left);
            Canvas.SetTop(_areaHighlightPreview, rect.Top);
            _areaHighlightPreview.Width = rect.Width;
            _areaHighlightPreview.Height = rect.Height;
        }

        private void EndAreaHighlightDrag(Point position)
        {
            if (!_isAreaHighlightDragging)
                return;

            Rect rect = NormalizeAreaHighlightRect(_areaHighlightAnchor, position);
            _isAreaHighlightDragging = false;

            if (_areaHighlightPreview != null)
            {
                ShapePreviewCanvas.Children.Remove(_areaHighlightPreview);
                _areaHighlightPreview = null;
            }

            if (rect.Width < AreaHighlightDragThreshold || rect.Height < AreaHighlightDragThreshold)
                return;

            Grid container = AddAreaHighlight(new AreaHighlightAnnotation
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                R = AreaHighlightColor.R,
                G = AreaHighlightColor.G,
                B = AreaHighlightColor.B,
                A = AreaHighlightOpacity
            });

            if (container != null)
                AreaHighlightCreated?.Invoke(this, container);
        }

        #endregion

        #region Scribble shape recognition (pen tool)

        // --- tunable heuristics (deliberate shapes pass, scribbles fail) ---
        private const int MinRecognizedShapePoints = 8;             // fewer points cannot evidence a shape
        private const double MinRecognizedDiagonal = 24.0;          // px; tiny scribbles are left alone
        private const double ClosedGapRatio = 0.15;                 // first-last gap < 15% of perimeter → closed
        private const double LineMeanDeviationRatio = 0.06;         // mean perp deviation / diagonal
        private const double EllipseMinCircularity = 0.82;          // 1 - stdR/meanR of centroid distances
        private const double EllipseMinSweepRadians = 300.0 * Math.PI / 180.0;
        private const double RectMinRunFraction = 0.06;             // runs shorter than 6% of points are noise
        private const double RectMinRunCoverage = 0.80;             // dominant runs must cover ≥ 80% of points
        private const double RectSideStraightness = 0.06;           // mean deviation / side chord length
        private const double RectCornerToleranceRatio = 0.12;       // corner match tolerance / diagonal

        /// <summary>Contiguous span of points sharing one direction bucket.</summary>
        private readonly struct DirectionRun
        {
            public DirectionRun(int bucket, int start, int end)
            {
                Bucket = bucket;
                Start = start;
                End = end;
            }

            public int Bucket { get; }
            public int Start { get; }
            public int End { get; }
            public int Length => End - Start + 1;
        }

        /// <summary>
        /// Swaps a freshly collected original for its ideal snapshot at the
        /// same collection index. A stale/missing token is a quiet failure;
        /// this method never appends an ideal stroke.
        /// </summary>
        private bool ReplaceRecognizedStroke(
            Stroke original,
            Stroke ideal,
            Guid token,
            StrokeReplacementSnapshot idealSnapshot,
            out int index)
        {
            index = -1;
            if (original == null || ideal == null || token == Guid.Empty)
                return false;

            if (!_strokeMetadata.TryGetValue(original, out var metadata)
                || metadata.Token != token
                || metadata.Side != StrokeReplacementSide.Original)
            {
                return false;
            }

            return TryReplaceStrokeQuiet(
                token,
                StrokeReplacementSide.Original,
                idealSnapshot,
                out index);
        }

        /// <summary>
        /// Attempts to classify a freshly collected freehand stroke as a
        /// line / rectangle / ellipse and to produce the ideal replacement
        /// stroke (original colour and width, FitToCurve=false,
        /// IgnorePressure=true). Returns false when none of the confidence
        /// gates pass — the original stroke is then left untouched.
        /// </summary>
        private bool TryRecognizeShape(Stroke stroke, out Stroke idealStroke)
        {
            idealStroke = null;

            var points = new List<Point>(stroke.StylusPoints.Count);
            foreach (var sp in stroke.StylusPoints)
                points.Add(new Point(sp.X, sp.Y));
            int n = points.Count;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            var bounds = new Rect(new Point(minX, minY), new Point(maxX, maxY));
            double diag = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
            if (diag < MinRecognizedDiagonal)
                return false;

            double perimeter = 0;
            for (int i = 1; i < n; i++)
                perimeter += Dist(points[i - 1], points[i]);
            if (perimeter <= double.Epsilon)
                return false;

            bool closed = Dist(points[0], points[n - 1]) < ClosedGapRatio * perimeter;

            List<Point> outline;
            if (closed)
            {
                // Rectangles are tested first: a near-square hand-drawn
                // rectangle also passes the ellipse circularity gate and
                // would otherwise be snapped to a circle.
                if (LooksLikeRectangle(points, bounds, diag))
                    outline = BuildShapeOutline(ShapeKind.Rectangle, bounds.TopLeft, bounds.BottomRight);
                else if (LooksLikeEllipse(points))
                    outline = BuildShapeOutline(ShapeKind.Ellipse, bounds.TopLeft, bounds.BottomRight);
                else
                    return false;
            }
            else
            {
                if (!LooksLikeLine(points, diag))
                    return false;
                outline = BuildShapeOutline(ShapeKind.Line, points[0], points[n - 1]);
            }

            var stylusPoints = new StylusPointCollection();
            foreach (var p in outline)
                stylusPoints.Add(new StylusPoint(p.X, p.Y));

            var attributes = stroke.DrawingAttributes.Clone();
            attributes.FitToCurve = false;    // crisp polygon edges like the shape tool
            attributes.IgnorePressure = true; // uniform width, no pressure jitter

            idealStroke = new Stroke(stylusPoints) { DrawingAttributes = attributes };
            return true;
        }

        /// <summary>
        /// Open stroke whose points hug the first→last chord: the mean
        /// perpendicular deviation stays below 6% of the diagonal.
        /// </summary>
        private static bool LooksLikeLine(List<Point> points, double diag)
        {
            var a = points[0];
            var b = points[points.Count - 1];
            double sum = 0;
            foreach (var p in points)
                sum += PerpendicularDistance(p, a, b);
            return sum / points.Count < LineMeanDeviationRatio * diag;
        }

        /// <summary>
        /// Closed stroke whose points stay at a near-constant distance from
        /// the centroid (circularity &gt; 0.82) while sweeping at least 300°
        /// around it. The ideal fit is axis-aligned to the original bounds.
        /// </summary>
        private static bool LooksLikeEllipse(List<Point> points)
        {
            double cx = 0, cy = 0;
            foreach (var p in points)
            {
                cx += p.X;
                cy += p.Y;
            }
            cx /= points.Count;
            cy /= points.Count;

            double sumR = 0, sumR2 = 0;
            var angles = new List<double>(points.Count);
            foreach (var p in points)
            {
                double dx = p.X - cx, dy = p.Y - cy;
                double r = Math.Sqrt(dx * dx + dy * dy);
                if (r < 1e-6)
                    continue; // centroid-coincident points carry no angle
                sumR += r;
                sumR2 += r * r;
                angles.Add(Math.Atan2(dy, dx));
            }
            if (angles.Count < 4)
                return false;

            int m = angles.Count;
            double meanR = sumR / m;
            if (meanR <= double.Epsilon)
                return false;
            double stdR = Math.Sqrt(Math.Max(0, sumR2 / m - meanR * meanR));
            if (1 - stdR / meanR <= EllipseMinCircularity)
                return false;

            // Angular coverage = 2π minus the largest gap between sorted
            // angles (the wrap-around gap included).
            angles.Sort();
            double maxGap = angles[0] + 2 * Math.PI - angles[m - 1];
            for (int i = 1; i < m; i++)
            {
                double gap = angles[i] - angles[i - 1];
                if (gap > maxGap)
                    maxGap = gap;
            }
            return 2 * Math.PI - maxGap >= EllipseMinSweepRadians;
        }

        /// <summary>
        /// Closed stroke with exactly four dominant direction runs: local
        /// directions (5-point window) are quantised into four 45° buckets
        /// (mod 180°), short runs are dropped as noise, and the survivors
        /// must alternate between two perpendicular buckets, be straight,
        /// cover most of the stroke and turn near the four corners of the
        /// fitted bounds (rejects rotated rects, diamonds and trapezoids).
        /// </summary>
        private static bool LooksLikeRectangle(List<Point> points, Rect bounds, double diag)
        {
            int n = points.Count;

            var buckets = new int[n];
            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - 2);
                int hi = Math.Min(n - 1, i + 2);
                double dx = points[hi].X - points[lo].X;
                double dy = points[hi].Y - points[lo].Y;
                buckets[i] = (dx == 0 && dy == 0) ? -1 : DirectionBucket(dx, dy);
            }

            // Contiguous same-bucket runs.
            var runs = new List<DirectionRun>();
            for (int i = 0; i < n; )
            {
                int j = i;
                while (j + 1 < n && buckets[j + 1] == buckets[i])
                    j++;
                runs.Add(new DirectionRun(buckets[i], i, j));
                i = j + 1;
            }

            // Drop noise runs (corner arcs, jitter); merge same-bucket
            // neighbours that only a dropped run separated.
            int minRunPoints = Math.Max(2, (int)Math.Ceiling(n * RectMinRunFraction));
            var dominant = new List<DirectionRun>();
            foreach (var run in runs)
            {
                if (run.Bucket < 0 || run.Length < minRunPoints)
                    continue;
                if (dominant.Count > 0 && dominant[^1].Bucket == run.Bucket)
                    dominant[^1] = new DirectionRun(run.Bucket, dominant[^1].Start, run.End);
                else
                    dominant.Add(run);
            }

            // A closed stroke may start mid-side: then the first and last
            // dominant runs are the two halves of one side (same bucket).
            bool wrapped = dominant.Count > 1
                && dominant[0].Start == 0 && dominant[^1].End == n - 1
                && dominant[0].Bucket == dominant[^1].Bucket;
            int sideCount = dominant.Count - (wrapped ? 1 : 0);
            if (sideCount != 4)
                return false;

            // Consecutive sides must be perpendicular (bucket +2 mod 4).
            for (int k = 0; k + 1 < dominant.Count; k++)
                if (dominant[k + 1].Bucket != (dominant[k].Bucket + 2) % 4)
                    return false;
            if (!wrapped && dominant[0].Bucket != (dominant[^1].Bucket + 2) % 4)
                return false;

            // Dominant runs must cover most of the stroke.
            int covered = 0;
            foreach (var run in dominant)
                covered += run.Length;
            if (covered < RectMinRunCoverage * n)
                return false;

            // Each side must be straight: mean perpendicular deviation from
            // its run chord below 6% of the chord length.
            foreach (var run in dominant)
            {
                var a = points[run.Start];
                var b = points[run.End];
                double chord = Dist(a, b);
                if (chord <= double.Epsilon)
                    return false;
                double sum = 0;
                for (int i = run.Start; i <= run.End; i++)
                    sum += PerpendicularDistance(points[i], a, b);
                if (sum / run.Length > RectSideStraightness * chord)
                    return false;
            }

            // Detected corners (midpoints of the transitions between
            // consecutive sides) and the four bounds corners must match
            // each other within 12% of the diagonal.
            var detectedCorners = new List<Point>();
            for (int k = 0; k + 1 < dominant.Count; k++)
            {
                int mid = (dominant[k].End + dominant[k + 1].Start) / 2;
                detectedCorners.Add(points[mid]);
            }
            if (!wrapped)
            {
                int mid = (dominant[^1].End + n + dominant[0].Start) / 2;
                detectedCorners.Add(points[mid % n]);
            }
            if (detectedCorners.Count != 4)
                return false;

            double cornerTolerance = RectCornerToleranceRatio * diag;
            var boundsCorners = new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft };
            foreach (var detected in detectedCorners)
            {
                double nearest = double.MaxValue;
                foreach (var corner in boundsCorners)
                    nearest = Math.Min(nearest, Dist(detected, corner));
                if (nearest > cornerTolerance)
                    return false;
            }
            foreach (var corner in boundsCorners)
            {
                double nearest = double.MaxValue;
                foreach (var detected in detectedCorners)
                    nearest = Math.Min(nearest, Dist(detected, corner));
                if (nearest > cornerTolerance)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Quantises a direction into four 45° buckets over the mod-180°
        /// range: 0 ≈ horizontal, 2 ≈ vertical, 1/3 ≈ diagonals ('up' and
        /// 'down' share a bucket). Perpendicular directions always land two
        /// buckets apart.
        /// </summary>
        private static int DirectionBucket(double dx, double dy)
        {
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double normalized = angle % 180.0;
            if (normalized < 0)
                normalized += 180.0;
            return (int)Math.Floor((normalized + 22.5) / 45.0) % 4;
        }

        private static double Dist(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double PerpendicularDistance(Point p, Point a, Point b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < double.Epsilon)
                return Dist(p, a);
            return Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / len;
        }

        #endregion

        private void EraseStrokesAtPoints(StylusPointCollection points)
        {
            if (points == null || points.Count == 0)
                return;

            var eraserRects = CreateEraserRects(points);
            if (eraserRects.Count == 0)
                return;

            // Hidden Ink lives above InkCanvas, so include it in the same
            // eraser gesture rather than waiting for a direct click on its
            // Polyline. This covers mouse drags, inverted pens and barrel
            // erasing consistently.
            EraseHiddenInksAtRects(eraserRects);

            if (InkCanvas.Strokes.Count == 0)
                return;

            var candidateBounds = eraserRects[0];
            for (int i = 1; i < eraserRects.Count; i++)
                candidateBounds.Union(eraserRects[i]);

            var candidateStrokes = InkCanvas.Strokes
                .Cast<Stroke>()
                .Where(stroke => stroke.GetBounds().IntersectsWith(candidateBounds))
                .ToList();

            if (candidateStrokes.Count == 0)
                return;

            bool mutated = false;
            foreach (var stroke in candidateStrokes)
            {
                if (WholeStrokeEraser)
                {
                    // Whole-stroke mode: remove the entire stroke when its
                    // bounds intersect any individual eraser rect. Routed
                    // through ApplyErasedStroke with no fragments so the
                    // StrokesErased undo payload contains originals only.
                    bool boundsHit = false;
                    for (int i = 0; i < eraserRects.Count; i++)
                    {
                        if (stroke.GetBounds().IntersectsWith(eraserRects[i]))
                        {
                            boundsHit = true;
                            break;
                        }
                    }

                    if (!boundsHit)
                        continue;

                    ApplyErasedStroke(stroke, new List<Stroke>());
                    mutated = true;
                    continue;
                }

                if (!stroke.StylusPoints.Any(sp => PointHitsEraser(new Point(sp.X, sp.Y), eraserRects)))
                    continue;

                var clippedStrokes = ClipStrokeByErasers(stroke, eraserRects);
                ApplyErasedStroke(stroke, clippedStrokes);

                mutated = true;
            }

            if (mutated)
                InkMutated?.Invoke(this, EventArgs.Empty);
        }

        private void EraseHiddenInksAtRects(IReadOnlyList<Rect> eraserRects)
        {
            var hitMasks = _hiddenInks
                .Where(annotation => HiddenInkIntersectsEraser(annotation, eraserRects))
                .ToList();

            foreach (var annotation in hitMasks)
            {
                if (annotation == null)
                    continue;

                _eraseGestureRemovedHiddenInks ??= new List<HiddenInkAnnotation>();
                _eraseGestureRemovedHiddenInks.Add(CloneHiddenInk(annotation));
                RemoveHiddenInkQuiet(annotation);
            }
        }

        private bool HiddenInkIntersectsEraser(
            HiddenInkAnnotation annotation,
            IReadOnlyList<Rect> eraserRects)
        {
            if (annotation?.Points == null || annotation.Points.Count == 0)
                return false;

            double radius = Math.Max(1.0, _eraserSize / 2.0 + annotation.Size / 2.0);
            var points = annotation.Points
                .Where(point => point != null && point.Length >= 2
                    && double.IsFinite(point[0]) && double.IsFinite(point[1]))
                .Select(point => new Point(point[0], point[1]))
                .ToList();

            for (int i = 0; i < points.Count; i++)
            {
                for (int rectIndex = 0; rectIndex < eraserRects.Count; rectIndex++)
                {
                    var expanded = eraserRects[rectIndex];
                    expanded.Inflate(radius, radius);
                    if (i == 0
                        ? expanded.Contains(points[i])
                        : SegmentIntersectsRect(points[i - 1], points[i], expanded))
                        return true;
                }
            }

            return false;
        }

        private static bool SegmentIntersectsRect(Point start, Point end, Rect rect)
        {
            if (rect.Contains(start) || rect.Contains(end))
                return true;

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double tMin = 0.0;
            double tMax = 1.0;

            return ClipSegmentToBoundary(-dx, start.X - rect.Left, ref tMin, ref tMax)
                && ClipSegmentToBoundary(dx, rect.Right - start.X, ref tMin, ref tMax)
                && ClipSegmentToBoundary(-dy, start.Y - rect.Top, ref tMin, ref tMax)
                && ClipSegmentToBoundary(dy, rect.Bottom - start.Y, ref tMin, ref tMax);
        }

        private static bool ClipSegmentToBoundary(
            double p,
            double q,
            ref double tMin,
            ref double tMax)
        {
            const double epsilon = 1e-12;
            if (Math.Abs(p) < epsilon)
                return q >= 0;

            double ratio = q / p;
            if (p < 0)
            {
                if (ratio > tMax)
                    return false;
                if (ratio > tMin)
                    tMin = ratio;
            }
            else
            {
                if (ratio < tMin)
                    return false;
                if (ratio < tMax)
                    tMax = ratio;
            }

            return true;
        }

        /// <summary>
        /// Applies one erase modification (remove the original stroke, add the
        /// clipped fragments) while accumulating the gesture state that is later
        /// reported through <see cref="StrokesErased"/> for undo. A stroke that
        /// was itself created earlier in the same gesture (a fragment being
        /// re-clipped) cancels out instead of being recorded, so the payload
        /// only contains net changes relative to the start of the gesture.
        /// </summary>
        private void ApplyErasedStroke(Stroke removedStroke, List<Stroke> addedStrokes)
        {
            if (_eraseGestureRemovedStrokes == null)
            {
                _eraseGestureRemovedStrokes = new List<Stroke>();
                _eraseGestureAddedStrokes = new List<Stroke>();
                _eraseGestureRemovedPlacements = new List<StrokePlacement>();
                _eraseGestureAddedPlacements = new List<StrokePlacement>();
            }

            int addedIndex = _eraseGestureAddedStrokes.IndexOf(removedStroke);
            if (addedIndex >= 0)
            {
                _eraseGestureAddedStrokes.RemoveAt(addedIndex);
                _eraseGestureAddedPlacements.RemoveAt(addedIndex);
            }
            else
            {
                _eraseGestureRemovedStrokes.Add(removedStroke);
                _eraseGestureRemovedPlacements.Add(CaptureStrokePlacement(removedStroke));
            }

            RemoveStrokeQuiet(removedStroke);
            foreach (var newStroke in addedStrokes)
            {
                var placement = AddStrokeToCollection(newStroke);
                _eraseGestureAddedStrokes.Add(newStroke);
                _eraseGestureAddedPlacements.Add(placement);
            }
        }

        /// <summary>
        /// Ends the current erase gesture and raises <see cref="StrokesErased"/>
        /// with the net removed/added strokes so the editor can push an undo
        /// action. Safe to call when no gesture is active (no-op).
        /// </summary>
        private void BeginEraseGesture()
        {
            // A new pointer gesture must never inherit a pending payload from
            // a cancelled/lost-capture gesture.
            _eraseGestureRemovedStrokes = null;
            _eraseGestureAddedStrokes = null;
            _eraseGestureRemovedPlacements = null;
            _eraseGestureAddedPlacements = null;
            _eraseGestureRemovedHiddenInks = null;
        }

        private bool HasPendingEraseGesture()
        {
            return (_eraseGestureRemovedStrokes?.Count ?? 0) > 0
                || (_eraseGestureAddedStrokes?.Count ?? 0) > 0
                || (_eraseGestureRemovedHiddenInks?.Count ?? 0) > 0;
        }

        private void EndEraseGesture()
        {
            var removed = _eraseGestureRemovedStrokes;
            var added = _eraseGestureAddedStrokes;
            var removedPlacements = _eraseGestureRemovedPlacements;
            var addedPlacements = _eraseGestureAddedPlacements;
            var removedHiddenInks = _eraseGestureRemovedHiddenInks;
            _eraseGestureRemovedStrokes = null;
            _eraseGestureAddedStrokes = null;
            _eraseGestureRemovedPlacements = null;
            _eraseGestureAddedPlacements = null;
            _eraseGestureRemovedHiddenInks = null;

            if (removed != null && added != null
                && (removed.Count > 0 || added.Count > 0))
            {
                StrokesErased?.Invoke(
                    this,
                    new StrokesErasedEventArgs(
                        removed,
                        added,
                        removedPlacements,
                        addedPlacements));
            }

            if (removedHiddenInks != null && removedHiddenInks.Count > 0)
            {
                HiddenInksRemoved?.Invoke(
                    this,
                    new HiddenInksRemovedEventArgs(removedHiddenInks));
            }
        }

        private List<Rect> CreateEraserRects(StylusPointCollection points)
        {
            var eraserRects = new List<Rect>(points.Count);
            foreach (var pt in points)
            {
                eraserRects.Add(new Rect(
                    pt.X - _eraserSize / 2,
                    pt.Y - _eraserSize / 2,
                    _eraserSize,
                    _eraserSize));
            }

            return eraserRects;
        }

        private static bool PointHitsEraser(Point point, IReadOnlyList<Rect> eraserRects)
        {
            for (int i = 0; i < eraserRects.Count; i++)
            {
                if (eraserRects[i].Contains(point))
                    return true;
            }

            return false;
        }

        private List<Stroke> ClipStrokeByErasers(Stroke stroke, IReadOnlyList<Rect> eraserRects)
        {
            var result = new List<Stroke>();
            var stylusPoints = stroke.StylusPoints;
            var currentSegment = new StylusPointCollection();

            for (int i = 0; i < stylusPoints.Count; i++)
            {
                var pt = stylusPoints[i];
                var point = new Point(pt.X, pt.Y);
                bool inEraser = PointHitsEraser(point, eraserRects);

                if (!inEraser)
                {
                    currentSegment.Add(pt);
                }
                else if (currentSegment.Count > 1)
                {
                    var newStroke = new Stroke(currentSegment.Clone())
                    {
                        DrawingAttributes = stroke.DrawingAttributes.Clone()
                    };
                    result.Add(newStroke);
                    currentSegment.Clear();
                }
                else
                {
                    currentSegment.Clear();
                }
            }

            if (currentSegment.Count > 1)
            {
                var newStroke = new Stroke(currentSegment.Clone())
                {
                    DrawingAttributes = stroke.DrawingAttributes.Clone()
                };
                result.Add(newStroke);
            }

            return result;
        }

        private static void OnPageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (PdfPageControl)d;
            control.SwapPageSource((BitmapSource)e.NewValue);
        }

        /// <summary>
        /// Task 12.2: flicker-free bitmap swap via a two-layer technique. A
        /// direct <c>PdfImage.Source = newBitmap</c> can flash — the old bitmap
        /// is released while the new one is still being decoded/composited, and
        /// the HighQuality re-interpolation is briefly visible mid-view. Instead:
        /// stage the new bitmap on <see cref="PdfImageOverlay"/> (same layout
        /// slot, drawn over PdfImage) for two frames so it is fully composited,
        /// then reveal it and swap the main image underneath in the same render
        /// pass (invisible — the overlay covers it), and clear the overlay one
        /// frame later. Layout never changes: both images use Stretch=Uniform
        /// inside the fixed-size page, so only the pixels change.
        /// </summary>
        private void SwapPageSource(BitmapSource newBitmap)
        {
            int generation = ++_pageSourceSwapGeneration;

            if (newBitmap == null)
            {
                // Clearing (e.g. document unload) — drop both layers directly;
                // a staged swap of "nothing" has nothing to hide.
                PdfImage.Source = null;
                PdfImageOverlay.Source = null;
                PdfImageOverlay.Opacity = 0;
                return;
            }

            // There is no old frame to preserve on first render (or after a
            // working-set eviction), so avoid three dispatcher/render passes.
            if (PdfImage.Source == null && PdfImageOverlay.Source == null)
            {
                PdfImage.Source = newBitmap;
                return;
            }

            // Stage the new bitmap on the overlay. Opacity 0.001 rather than 0:
            // a fully transparent visual may be skipped by the render walk, and
            // we NEED these warm-up frames to decode/composite the bitmap so
            // the reveal frame is instantaneous. At 0.1% alpha it is invisible.
            PdfImageOverlay.Source = newBitmap;
            PdfImageOverlay.Opacity = 0.001;

            // Wait two render frames (each BeginInvoke at Render priority runs
            // before that frame's render pass), then reveal + swap underneath.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != _pageSourceSwapGeneration) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (generation != _pageSourceSwapGeneration) return;

                    // The overlay has been composited for ≥2 frames. Reveal it
                    // and swap the main image source underneath in the SAME
                    // render pass — the fully opaque overlay hides the swap.
                    PdfImageOverlay.Opacity = 1;
                    PdfImage.Source = newBitmap;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (generation != _pageSourceSwapGeneration) return;

                        // The main image has now rendered the new bitmap for a
                        // full frame — safe to drop the overlay.
                        PdfImageOverlay.Source = null;
                        PdfImageOverlay.Opacity = 0;
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }), System.Windows.Threading.DispatcherPriority.Render);
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        public void SetMode(bool isTextMode)
        {
            // Text annotations should only be directly interactive while the text
            // tool is active. In every other mode, let the input fall through to
            // the drawing/selection layers underneath.
            TextOverlayCanvas.IsHitTestVisible = isTextMode;
            TextOverlayCanvas.Background = isTextMode ? Brushes.Transparent : null;
        }

        public void SetPdfTextSelectionEnabled(bool enabled)
        {
            _isPdfTextSelectionEnabled = enabled;
            PdfTextSelectionCanvas.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            PdfTextSelectionCanvas.IsHitTestVisible = enabled;

            if (!enabled)
            {
                if (PdfTextSelectionCanvas.IsMouseCaptured)
                    PdfTextSelectionCanvas.ReleaseMouseCapture();
                if (PdfTextSelectionCanvas.IsStylusCaptured)
                    PdfTextSelectionCanvas.ReleaseStylusCapture();
                ClearPdfTextSelection();
                return;
            }

            // When PDF text selection is enabled, we need InkCanvas to NOT intercept events
            InkCanvas.IsHitTestVisible = false;
            Cursor = Cursors.IBeam;
        }

        public void SetPdfTextSelectionRects(IEnumerable<Rect> rects)
        {
            PdfTextSelectionCanvas.Children.Clear();
            if (rects == null)
                return;

            foreach (var rect in rects)
            {
                if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                    continue;

                var highlight = new System.Windows.Shapes.Rectangle
                {
                    Width = rect.Width,
                    Height = rect.Height,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };

                highlight.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ThemeSelectionBrush");
                highlight.Opacity = 0.45;

                Canvas.SetLeft(highlight, rect.X);
                Canvas.SetTop(highlight, rect.Y);
                PdfTextSelectionCanvas.Children.Add(highlight);
            }
        }

        public void ClearPdfTextSelection()
        {
            PdfTextSelectionCanvas.Children.Clear();
        }

        public DrawingAttributes CopyDefaultDrawingAttributes()
        {
            return _drawingAttributes.Clone();
        }

        public void SetInkAttributes(DrawingAttributes attributes)
        {
            _drawingAttributes = attributes.Clone();
            _drawingAttributes.FitToCurve = true;
            // Honour the pressure toggle: enabled → digitiser pressure (and
            // ink simulation) produce natural width variation; disabled →
            // every stroke renders at uniform width.
            _drawingAttributes.IgnorePressure = !PressureEnabled;
            InkCanvas.DefaultDrawingAttributes = _drawingAttributes;
        }

        public void SetInputMode(CustomInkInputProcessingMode mode)
        {
            if (mode != CustomInkInputProcessingMode.Inking)
                _pendingPopupDismissalInkGesture = false;

            if (mode != CustomInkInputProcessingMode.Erasing &&
                (_isErasing || HasPendingEraseGesture()))
            {
                _isErasing = false;
                _erasePoints = null;
                EndEraseGesture();
            }

            if (mode != CustomInkInputProcessingMode.Shape)
            {
                // Leaving shape mode: drop any in-flight drag/preview so no
                // stale dashed outline survives a tool switch.
                _isShapeDragging = false;
                ShapePreviewCanvas.Children.Clear();
            }

            if (mode != CustomInkInputProcessingMode.Laser)
            {
                // Task 20: leaving laser mode drops any in-flight laser drag;
                // already-fading polylines stay and remove themselves.
                _isLaserDrawing = false;
                _laserPolyline = null;
            }

            if (mode != CustomInkInputProcessingMode.AreaHighlight)
            {
                _isAreaHighlightDragging = false;
                if (_areaHighlightPreview != null)
                {
                    ShapePreviewCanvas.Children.Remove(_areaHighlightPreview);
                    _areaHighlightPreview = null;
                }
            }

            _currentMode = mode;
            UpdateHiddenInkHitTesting();
            switch (mode)
            {
                case CustomInkInputProcessingMode.Inking:
                    if (!_isPdfTextSelectionEnabled)
                        InkCanvas.IsHitTestVisible = true;
                    InkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    // Keep indicator visible since we're updating it on mouse/stylus move now
                    InkCanvas.Cursor = Cursors.Cross;
                    Cursor = Cursors.Arrow;
                    break;
                case CustomInkInputProcessingMode.HiddenInk:
                    if (!_isPdfTextSelectionEnabled)
                        InkCanvas.IsHitTestVisible = true;
                    // Let InkCanvas collect the gesture. StrokeCollected
                    // immediately moves it to the opaque mask layer.
                    InkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    InkCanvas.Cursor = Cursors.Cross;
                    Cursor = Cursors.Arrow;
                    break;
                case CustomInkInputProcessingMode.Erasing:
                    if (!_isPdfTextSelectionEnabled)
                        InkCanvas.IsHitTestVisible = true;
                    InkCanvas.EditingMode = InkCanvasEditingMode.None;
                    InkCanvas.Cursor = Cursors.None;
                    break;
                case CustomInkInputProcessingMode.Shape:
                    // InkCanvas must receive pointer input, but WPF must NOT
                    // collect freehand ink — the shape handlers own the gesture.
                    if (!_isPdfTextSelectionEnabled)
                        InkCanvas.IsHitTestVisible = true;
                    InkCanvas.EditingMode = InkCanvasEditingMode.None;
                    InkCanvas.Cursor = Cursors.Cross;
                    Cursor = Cursors.Arrow;
                    break;
                case CustomInkInputProcessingMode.Laser:
                    // Task 20: same input contract as the shape tool — hit
                    // testing on (to capture the gesture), native inking off
                    // (the laser handlers own the pointer and draw on
                    // LaserInkCanvas instead).
                    if (!_isPdfTextSelectionEnabled)
                        InkCanvas.IsHitTestVisible = true;
                    InkCanvas.EditingMode = InkCanvasEditingMode.None;
                    InkCanvas.Cursor = Cursors.Cross;
                    Cursor = Cursors.Arrow;
                    break;
                case CustomInkInputProcessingMode.AreaHighlight:
                    // The area tool owns the pointer gesture and renders a
                    // translucent overlay instead of collecting native ink.
                    if (!_isPdfTextSelectionEnabled)
                        InkCanvas.IsHitTestVisible = true;
                    InkCanvas.EditingMode = InkCanvasEditingMode.None;
                    InkCanvas.Cursor = Cursors.Cross;
                    Cursor = Cursors.Arrow;
                    break;
                case CustomInkInputProcessingMode.None:
                    if (!_isPdfTextSelectionEnabled)
                        InkCanvas.IsHitTestVisible = false; // Allow events to pass through for scrolling
                    else
                        InkCanvas.IsHitTestVisible = false; // Keep it false for text selection
                    InkCanvas.EditingMode = InkCanvasEditingMode.None;
                    EraserIndicator.Visibility = Visibility.Collapsed;
                    InkCanvas.Cursor = Cursors.Arrow;
                    Cursor = Cursors.Arrow;
                    break;
            }
            ModeChanged?.Invoke(this, mode);
        }

        public void SetEraserSize(double size)
        {
            _eraserSize = size;
        }

        public StrokeCollection GetStrokes()
        {
            // Preserve the historical StrokeCollection return type for read-
            // only callers while preventing callers from mutating the live
            // collection without updating token/placement metadata.
            var copy = new StrokeCollection();
            foreach (var stroke in _strokes)
                copy.Add(stroke);
            return copy;
        }

        public void ClearInk()
        {
            InkCanvas.Strokes.Clear();
            _strokeMetadata.Clear();
            _strokePlacementHistory.Clear();
            _replacementState = new StrokeReplacementState(Array.Empty<StrokeReplacementEntry>());
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        public List<StrokeAnnotation> GetStrokeData()
        {
            var list = new List<StrokeAnnotation>();
            foreach (var stroke in InkCanvas.Strokes)
            {
                var attrs = stroke.DrawingAttributes;
                var color = attrs.Color;
                var sa = new StrokeAnnotation
                {
                    R = color.R,
                    G = color.G,
                    B = color.B,
                    A = color.A,
                    Size = attrs.Width,
                    IsHighlighter = attrs.IsHighlighter,
                    FitToCurve = attrs.FitToCurve
                };
                foreach (var pt in stroke.StylusPoints)
                {
                    sa.Points.Add(new[] { pt.X, pt.Y });
                }
                list.Add(sa);
            }
            return list;
        }

        public Stroke AddStroke(StrokeAnnotation sa)
        {
            if (sa.Points == null || sa.Points.Count == 0) return null;

            var color = Color.FromArgb(sa.A, sa.R, sa.G, sa.B);
            var attrs = new DrawingAttributes
            {
                Color = color,
                Width = sa.Size > 0 ? sa.Size : 2.0,
                Height = sa.Size > 0 ? sa.Size : 2.0,
                IsHighlighter = sa.IsHighlighter,
                FitToCurve = sa.FitToCurve
            };

            var stylusPoints = new StylusPointCollection();
            foreach (var pt in sa.Points)
            {
                if (pt == null || pt.Length < 2) continue;
                stylusPoints.Add(new StylusPoint(pt[0], pt[1]));
            }

            if (stylusPoints.Count > 0)
            {
                if (stylusPoints.Count == 1)
                {
                    stylusPoints.Add(new StylusPoint(stylusPoints[0].X + 0.1, stylusPoints[0].Y));
                }

                var stroke = new Stroke(stylusPoints);
                stroke.DrawingAttributes = attrs;
                AddStrokeToCollection(stroke);
                return stroke;
            }

            return null;
        }

        /// <summary>
        /// Adds a persisted study mask in its hidden state. Loading and undo
        /// use this path; user-created strokes use the private commit path so
        /// the editor can push one dedicated undo command.
        /// </summary>
        public void AddHiddenInk(HiddenInkAnnotation annotation)
        {
            AddHiddenInkInternal(annotation, raiseCreated: false);
        }

        public void AddHiddenInkQuiet(HiddenInkAnnotation annotation)
        {
            AddHiddenInkInternal(annotation, raiseCreated: false);
        }

        public void RemoveHiddenInkQuiet(HiddenInkAnnotation annotation)
        {
            if (annotation == null)
                return;

            var existing = _hiddenInks.FirstOrDefault(item =>
                string.Equals(item.Id, annotation.Id, StringComparison.Ordinal));
            if (existing == null)
                return;

            StopHiddenInkRevealTimer(existing.Id);
            _hiddenInks.Remove(existing);
            if (_hiddenInkVisuals.TryGetValue(existing.Id, out var visual))
            {
                HiddenInkCanvas.Children.Remove(visual);
                _hiddenInkVisuals.Remove(existing.Id);
            }
        }

        private void RemoveHiddenInk(HiddenInkAnnotation annotation, bool raiseRemoved)
        {
            var existing = _hiddenInks.FirstOrDefault(item =>
                item != null && annotation != null
                && string.Equals(item.Id, annotation.Id, StringComparison.Ordinal));
            if (existing == null)
                return;

            if (raiseRemoved)
                HiddenInkRemoved?.Invoke(this, existing);
            RemoveHiddenInkQuiet(existing);
        }

        public IReadOnlyList<HiddenInkAnnotation> GetHiddenInkData()
        {
            return _hiddenInks.Select(CloneHiddenInk).ToList();
        }

        private void AddHiddenInkInternal(HiddenInkAnnotation annotation, bool raiseCreated)
        {
            if (annotation == null || annotation.Points == null || annotation.Points.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(annotation.Id))
                annotation.Id = Guid.NewGuid().ToString("N");

            if (_hiddenInks.Any(item => string.Equals(item.Id, annotation.Id, StringComparison.Ordinal)))
                annotation.Id = Guid.NewGuid().ToString("N");

            annotation.Size = Math.Max(1.0, annotation.Size);
            annotation.A = 255;
            annotation.RevealDurationMs = annotation.RevealDurationMs > 0
                ? annotation.RevealDurationMs
                : HiddenInkRevealState.DefaultRevealDurationMs;
            _hiddenInks.Add(annotation);
            RenderHiddenInkVisual(annotation);

            if (raiseCreated)
                HiddenInkCreated?.Invoke(this, annotation);
        }

        private void RenderHiddenInkVisual(HiddenInkAnnotation annotation)
        {
            var points = new PointCollection();
            foreach (var point in annotation.Points)
            {
                if (point != null && point.Length >= 2
                    && double.IsFinite(point[0]) && double.IsFinite(point[1]))
                {
                    points.Add(new Point(point[0], point[1]));
                }
            }

            if (points.Count == 0)
                return;

            if (points.Count == 1)
                points.Add(new Point(points[0].X + 0.1, points[0].Y));

            var visual = new System.Windows.Shapes.Polyline
            {
                Points = points,
                Stroke = new SolidColorBrush(Color.FromArgb(255, annotation.R, annotation.G, annotation.B)),
                StrokeThickness = Math.Max(1.0, annotation.Size),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Tag = annotation,
                Cursor = Cursors.Hand,
                IsHitTestVisible = IsHiddenInkInteractiveMode()
            };
            visual.MouseLeftButtonDown += HiddenInkVisual_MouseLeftButtonDown;
            visual.StylusDown += HiddenInkVisual_StylusDown;
            HiddenInkCanvas.Children.Add(visual);
            _hiddenInkVisuals[annotation.Id] = visual;
        }

        private bool IsHiddenInkInteractiveMode()
        {
            // Eraser keeps the underlying InkCanvas as the owner so a drag can
            // remove masks. Hidden Ink itself remains clickable: after writing
            // a mask, the same tool can immediately reveal an existing mask.
            return _currentMode != CustomInkInputProcessingMode.Erasing;
        }

        private void UpdateHiddenInkHitTesting()
        {
            bool isInteractive = IsHiddenInkInteractiveMode();
            foreach (var visual in _hiddenInkVisuals.Values)
                visual.IsHitTestVisible = isInteractive;
        }

        private void HiddenInkVisual_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsTouchFinger(e.StylusDevice))
                return;
            HandleHiddenInkVisualPress(sender);
            e.Handled = true;
        }

        private void HiddenInkVisual_StylusDown(object sender, StylusDownEventArgs e)
        {
            if (IsTouchFinger(e.StylusDevice))
                return;
            HandleHiddenInkVisualPress(sender);
            e.Handled = true;
        }

        private void HandleHiddenInkVisualPress(object sender)
        {
            if (sender is not System.Windows.Shapes.Polyline visual
                || visual.Tag is not HiddenInkAnnotation annotation)
            {
                return;
            }

            if (_currentMode == CustomInkInputProcessingMode.Erasing)
                RemoveHiddenInk(annotation, raiseRemoved: true);
            else
                RevealHiddenInk(annotation, visual);
        }

        private void RevealHiddenInk(HiddenInkAnnotation annotation, System.Windows.Shapes.Polyline visual)
        {
            visual.Visibility = Visibility.Collapsed;
            StopHiddenInkRevealTimer(annotation.Id);

            int durationMs = annotation.RevealDurationMs > 0
                ? annotation.RevealDurationMs
                : HiddenInkRevealState.DefaultRevealDurationMs;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                _hiddenInkRevealTimers.Remove(annotation.Id);
                if (_hiddenInkVisuals.TryGetValue(annotation.Id, out var currentVisual))
                    currentVisual.Visibility = Visibility.Visible;
            };
            _hiddenInkRevealTimers[annotation.Id] = timer;
            timer.Start();
        }

        private void StopHiddenInkRevealTimer(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
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
        }

        private static HiddenInkAnnotation CloneHiddenInk(HiddenInkAnnotation source)
        {
            var copy = new HiddenInkAnnotation
            {
                Id = source.Id,
                R = source.R,
                G = source.G,
                B = source.B,
                A = source.A,
                Size = source.Size,
                RevealDurationMs = source.RevealDurationMs
            };
            foreach (var point in source.Points ?? new List<double[]>())
            {
                if (point != null && point.Length >= 2)
                    copy.Points.Add(new[] { point[0], point[1] });
            }
            return copy;
        }

        public void ClearStrokes()
        {
            InkCanvas.Strokes.Clear();
            _strokeMetadata.Clear();
            _strokePlacementHistory.Clear();
            _replacementState = new StrokeReplacementState(Array.Empty<StrokeReplacementEntry>());
        }

        public void ClearAllAnnotations()
        {
            ClearStrokes();
            TextOverlayCanvas.Children.Clear();
            foreach (var container in _stickyInteractionHandlers.Keys.ToList())
                DetachStickyNoteHandlers(container);
            ImageOverlayCanvas.Children.Clear();
            _imageContainers.Clear();
            _imageDataById.Clear();
            _overlayData.Clear();
            _highlights.Clear();
            HighlightsCanvas.Children.Clear();
            StopAllHiddenInkRevealTimers();
            _hiddenInks.Clear();
            _hiddenInkVisuals.Clear();
            HiddenInkCanvas.Children.Clear();
        }

        public bool RemoveStrokeQuiet(Stroke stroke)
        {
            if (stroke == null)
                return false;

            int index = _strokes.IndexOf(stroke);
            if (index < 0)
                return false;

            _strokePlacementHistory[stroke] = CaptureStrokePlacement(stroke);
            _strokes.RemoveAt(index);
            _strokeMetadata.Remove(stroke);
            SynchronizeReplacementState();
            QuietStrokeMutation?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public StrokePlacement AddStrokeQuiet(Stroke stroke)
        {
            if (stroke == null)
                return null;

            if (_strokePlacementHistory.TryGetValue(stroke, out var placement))
            {
                int index = ReferenceEquals(placement.Owner, this)
                    ? placement.Index
                    : _strokes.Count;
                return AddStrokeQuiet(placement.ForOwner(this, index));
            }

            var added = AddStrokeToCollection(stroke);
            if (added != null)
                QuietStrokeMutation?.Invoke(this, EventArgs.Empty);
            return added;
        }

        public bool RemoveStrokeQuiet(StrokePlacement placement)
        {
            if (!TryResolveCurrentStroke(placement, out var currentStroke, out _))
                return false;

            return RemoveStrokeQuiet(currentStroke);
        }

        public StrokePlacement AddStrokeQuiet(StrokePlacement placement)
        {
            if (placement == null
                || !ReferenceEquals(placement.Owner, this)
                || placement.Token == Guid.Empty)
                return null;

            if (TryFindCurrentStroke(placement.Token, out var currentStroke, out _))
            {
                if (!_strokeMetadata.TryGetValue(currentStroke, out var currentMetadata)
                    || currentMetadata.Side != placement.Side)
                {
                    return null;
                }

                // A token/side pair identifies one logical replacement on
                // one page.  A different live stroke with the same pair is a
                // target conflict, not an idempotent re-add.  Returning it as
                // success would make a cross-page transfer remove its source
                // while silently retaining the unrelated target stroke.
                if (!ReferenceEquals(currentStroke, placement.Stroke))
                    return null;

                return CaptureStrokePlacement(currentStroke);
            }

            if (_strokes.Contains(placement.Stroke))
            {
                if (!_strokeMetadata.TryGetValue(placement.Stroke, out var currentMetadata)
                    || currentMetadata.Token != placement.Token
                    || currentMetadata.Side != placement.Side)
                {
                    return null;
                }

                return CaptureStrokePlacement(placement.Stroke);
            }

            var added = AddStrokeToCollection(
                placement.Stroke,
                placement.Token,
                placement.Side,
                placement.Index);
            if (added != null)
                QuietStrokeMutation?.Invoke(this, EventArgs.Empty);
            return added;
        }

        // ----- Task 19: image annotations -----

        /// <summary>
        /// Decodes the given encoded image bytes (PNG/JPEG) and places an image
        /// annotation container on ImageOverlayCanvas (below the ink layer).
        /// The container is an ordinary Grid (Tag=ImageContainerTag) so the
        /// existing selection/move/scale/delete pipeline treats it exactly like
        /// a text container. Top-left lands at <paramref name="position"/>,
        /// clamped to the page. When explicit dimensions are supplied (load /
        /// paste of a copied image) they are used verbatim; otherwise the image
        /// is fitted inside 40% of the page preserving its aspect ratio.
        /// Returns the container, or null when the bytes cannot be decoded.
        /// </summary>
        public Grid AddImage(byte[] imageBytes, Point position, double? explicitWidth = null, double? explicitHeight = null)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return null;

            BitmapImage bitmap;
            try
            {
                using var stream = new MemoryStream(imageBytes);
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                // OnLoad decodes eagerly and detaches from the stream so the
                // buffer can be reused for the PDF save path afterwards.
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            catch
            {
                return null;
            }

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
                IsHitTestVisible = false
            };

            var container = new Grid
            {
                Width = width,
                Height = height,
                Tag = ImageContainerTag,
                // SelectionOverlayCanvas owns image interaction; an image
                // visual must never block the page's drawing surface.
                IsHitTestVisible = false
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

        /// <summary>Image containers currently on the page (in insertion order).</summary>
        public IReadOnlyList<Grid> ImageContainers => _imageContainers;

        /// <summary>Raw encoded bytes (PNG/JPEG) behind an image container, or null.</summary>
        public byte[] GetImageData(Grid container)
        {
            return container != null && _imageDataById.TryGetValue(container, out var data) ? data : null;
        }

        /// <summary>
        /// Registers image payload for a container that arrived from another
        /// page (cross-page moves re-parent the Grid but the payload dict is
        /// per-control, so the moving side transfers it explicitly).
        /// </summary>
        public void SetImageData(Grid container, byte[] data)
        {
            if (container == null || data == null) return;
            _imageDataById[container] = data;
        }

        public void RemoveImageData(Grid container)
        {
            if (container != null)
                _imageDataById.Remove(container);
        }

        internal static bool IsImageContainer(Grid container)
        {
            return container != null && (container.Tag as string) == ImageContainerTag;
        }

        /// <summary>
        /// Task 25/26/27: true for every container-kind that lives on
        /// <see cref="ImageOverlayCanvas"/> (images, text markups, area
        /// highlights, sticky notes). All of them share the image pipeline:
        /// overlay placement, marquee/Ctrl+click selection, explicit-size
        /// scaling and quiet re-parenting for undo / cross-page moves.
        /// </summary>
        internal static bool IsOverlayContainer(Grid container)
        {
            if (container == null)
                return false;
            if (IsImageContainer(container))
                return true;
            var tag = container.Tag as string;
            return tag == MarkupContainerTag
                || tag == AreaHighlightContainerTag
                || tag == StickyNoteContainerTag;
        }

        /// <summary>Overlay payload model (TextMarkup / AreaHighlight / StickyNote) behind a container.</summary>
        public object GetOverlayData(Grid container)
        {
            return container != null && _overlayData.TryGetValue(container, out var data) ? data : null;
        }

        public void SetOverlayData(Grid container, object data)
        {
            if (container == null || data == null) return;
            _overlayData[container] = data;
        }

        /// <summary>
        /// Task 25: builds the underline / strike-out / squiggly visual as an
        /// overlay container. The lines are drawn once into an inner Canvas
        /// inside a Viewbox (Stretch=Fill) so corner-handle rescaling of the
        /// container scales the drawing with zero re-render logic. The
        /// annotation model (position + relative rects) rides in
        /// <see cref="_overlayData"/> and is re-synced from the container
        /// position by the editor on save.
        /// </summary>
        public Grid AddTextMarkup(TextMarkupAnnotation markup)
        {
            if (markup?.Rects == null || markup.Rects.Count == 0)
                return null;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var rect in markup.Rects)
            {
                if (rect == null || rect.Length < 4) continue;
                minX = Math.Min(minX, rect[0]);
                minY = Math.Min(minY, rect[1]);
                maxX = Math.Max(maxX, rect[0] + rect[2]);
                maxY = Math.Max(maxY, rect[1] + rect[3]);
            }
            if (minX > maxX)
                return null;

            double width = Math.Max(2.0, maxX - minX);
            double height = Math.Max(2.0, maxY - minY);
            var color = Color.FromRgb(markup.R, markup.G, markup.B);
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            var canvas = new Canvas { Width = width, Height = height };
            double lineThickness = Math.Max(1.4, height * 0.06);
            var kind = markup.ParsedKind;

            foreach (var rect in markup.Rects)
            {
                if (rect == null || rect.Length < 4) continue;
                double x = rect[0] - minX;
                double y = rect[1] - minY;
                double w = Math.Max(1.0, rect[2]);
                double h = Math.Max(1.0, rect[3]);

                if (kind == TextMarkupKind.Squiggly)
                {
                    var zigzag = new System.Windows.Shapes.Polyline
                    {
                        Stroke = brush,
                        StrokeThickness = lineThickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        IsHitTestVisible = false
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
                    canvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = x, Y1 = lineY,
                        X2 = x + w, Y2 = lineY,
                        Stroke = brush,
                        StrokeThickness = lineThickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false
                    });
                }
            }

            var container = new Grid
            {
                Width = width,
                Height = height,
                Tag = MarkupContainerTag,
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            container.Children.Add(new Viewbox
            {
                Stretch = Stretch.Fill,
                StretchDirection = StretchDirection.Both,
                IsHitTestVisible = false,
                Child = canvas
            });

            Canvas.SetLeft(container, Math.Max(0, markup.X));
            Canvas.SetTop(container, Math.Max(0, markup.Y));
            ImageOverlayCanvas.Children.Add(container);
            _overlayData[container] = markup;
            ImagesChanged?.Invoke(this, EventArgs.Empty);
            return container;
        }

        /// <summary>
        /// Task 27: free-form rectangular area highlight as an overlay
        /// container (Grid whose Background is the semi-transparent color —
        /// it stretches automatically when the container is rescaled).
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
                IsHitTestVisible = false
            };

            Canvas.SetLeft(container, Math.Max(0, area.X));
            Canvas.SetTop(container, Math.Max(0, area.Y));
            ImageOverlayCanvas.Children.Add(container);
            _overlayData[container] = area;
            ImagesChanged?.Invoke(this, EventArgs.Empty);
            return container;
        }

        /// <summary>
        /// Task 26: collapsed sticky-note icon as an overlay container. The
        /// marker is the only ImageOverlayCanvas child that is hit-testable.
        /// Its pointer gesture captures directly on the marker, clamps to the
        /// page and raises either Activated (click) or Moved (drag).
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
                Background = new SolidColorBrush(Color.FromRgb(note.R, note.G, note.B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xA7, 0x2C)),
                BorderThickness = new Thickness(1),
                Child = new System.Windows.Shapes.Path
                {
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform,
                    Data = Geometry.Parse("M4,3 L16,3 L20,7 L20,20 L4,20 Z M16,3 L16,8 L20,8 M7,12 L17,12 M7,16 L15,16"),
                    Fill = new SolidColorBrush(Color.FromRgb(0x7A, 0x5C, 0x0E)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                }
            };

            var container = new Grid
            {
                Width = markerWidth,
                Height = markerHeight,
                Tag = StickyNoteContainerTag,
                Background = Brushes.Transparent,
                IsHitTestVisible = true,
                Focusable = true
            };
            container.Children.Add(icon);

            KeyboardNavigation.SetIsTabStop(container, true);
            container.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "ToolbarFocusVisualStyle");
            AutomationProperties.SetAutomationId(container, $"StickyNote.{note.Id}");
            string stickyLabel = LocalizationService.Get("Editor.StickyNoteTooltip");
            AutomationProperties.SetName(container, stickyLabel);
            AutomationProperties.SetHelpText(container, stickyLabel);
            ToolTipService.SetToolTip(container, note.Text ?? string.Empty);
            container.ContextMenu = BuildStickyNoteContextMenu(container);
            StickyNoteContextMenuCreated?.Invoke(this, container.ContextMenu);
            AttachStickyNoteHandlers(container);

            ImageOverlayCanvas.Children.Add(container);
            _overlayData[container] = note;
            SetStickyNotePositionQuiet(container, new Point(note.X, note.Y));
            ImagesChanged?.Invoke(this, EventArgs.Empty);
            return container;
        }

        /// <summary>
        /// PDF annotation names are page-local in the sidecar format, but a
        /// malformed file can repeat or omit them. Repair only the identity;
        /// text, geometry and colour remain untouched.
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

        private void AttachStickyNoteHandlers(Grid container)
        {
            if (container == null || _stickyInteractionHandlers.ContainsKey(container))
                return;

            var handlers = new StickyInteractionHandlers
            {
                MouseDown = StickyNote_MouseLeftButtonDown,
                MouseMove = StickyNote_MouseMove,
                MouseUp = StickyNote_MouseLeftButtonUp,
                StylusDown = StickyNote_StylusDown,
                StylusMove = StickyNote_StylusMove,
                StylusUp = StickyNote_StylusUp,
                LostMouseCapture = StickyNote_LostMouseCapture,
                LostStylusCapture = StickyNote_LostStylusCapture,
                KeyDown = StickyNote_KeyDown
            };
            container.MouseLeftButtonDown += handlers.MouseDown;
            container.MouseMove += handlers.MouseMove;
            container.MouseLeftButtonUp += handlers.MouseUp;
            container.StylusDown += handlers.StylusDown;
            container.StylusMove += handlers.StylusMove;
            container.StylusUp += handlers.StylusUp;
            container.LostMouseCapture += handlers.LostMouseCapture;
            container.LostStylusCapture += handlers.LostStylusCapture;
            container.KeyDown += handlers.KeyDown;
            if (container.ContextMenu != null)
                PopupZOrderHelper.FixContextMenuTopmost(container.ContextMenu);
            _stickyInteractionHandlers[container] = handlers;
        }

        private void DetachStickyNoteHandlers(Grid container)
        {
            if (container == null || !_stickyInteractionHandlers.TryGetValue(container, out var handlers))
                return;

            container.MouseLeftButtonDown -= handlers.MouseDown;
            container.MouseMove -= handlers.MouseMove;
            container.MouseLeftButtonUp -= handlers.MouseUp;
            container.StylusDown -= handlers.StylusDown;
            container.StylusMove -= handlers.StylusMove;
            container.StylusUp -= handlers.StylusUp;
            container.LostMouseCapture -= handlers.LostMouseCapture;
            container.LostStylusCapture -= handlers.LostStylusCapture;
            container.KeyDown -= handlers.KeyDown;
            if (container.ContextMenu != null)
                PopupZOrderHelper.UnfixContextMenuTopmost(container.ContextMenu);
            if (ReferenceEquals(_stickyDragContainer, container))
                EndStickyPointer(container, canceled: true);
            _stickyInteractionHandlers.Remove(container);
        }

        /// <summary>
        /// PopupZOrderHelper hooks belong to the live marker owner.  A
        /// transient sweep can release those exact delegates without removing
        /// the marker input handlers; the matching ensure method reattaches one
        /// hook when the page becomes interactive again.
        /// </summary>
        public void UnfixTransientUiHooks()
        {
            foreach (var container in _stickyInteractionHandlers.Keys.ToList())
            {
                if (container.ContextMenu != null)
                    PopupZOrderHelper.UnfixContextMenuTopmost(container.ContextMenu);
            }
        }

        public void EnsureTransientUiHooks()
        {
            foreach (var container in _stickyInteractionHandlers.Keys.ToList())
            {
                if (container.ContextMenu != null)
                    PopupZOrderHelper.FixContextMenuTopmost(container.ContextMenu);
            }
        }

        private ContextMenu BuildStickyNoteContextMenu(Grid container)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = container,
                Tag = "StickyNote.ContextMenu"
            };
            menu.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush");

            var delete = new MenuItem
            {
                Header = LocalizationService.Get("Editor.DeleteTooltip"),
                MinHeight = 32,
                Tag = "StickyNote.Delete"
            };
            AutomationProperties.SetAutomationId(delete, "Sticky.Delete.ContextMenu");
            string deleteLabel = LocalizationService.Get("Editor.DeleteTooltip");
            AutomationProperties.SetName(delete, deleteLabel);
            AutomationProperties.SetHelpText(delete, deleteLabel);
            delete.Click += (sender, args) =>
            {
                StickyNoteDeleteRequested?.Invoke(this, container);
                args.Handled = true;
            };
            menu.Items.Add(delete);
            return menu;
        }

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

        /// <summary>Quietly moves a Sticky Note without raising a user action.</summary>
        public bool SetStickyNotePositionQuiet(Grid container, Point position)
        {
            if (container == null || !IsOverlayContainer(container)
                || GetOverlayData(container) is not StickyNoteAnnotation note)
                return false;

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
            return true;
        }

        /// <summary>Quietly updates note text for an undo/redo action.</summary>
        public bool SetStickyNoteTextQuiet(Grid container, string text)
        {
            if (container == null || !IsStickyNoteContainer(container)
                || GetOverlayData(container) is not StickyNoteAnnotation note)
                return false;

            note.Text = text ?? string.Empty;
            ToolTipService.SetToolTip(container, note.Text);
            return true;
        }

        private bool IsStickyNoteContainer(Grid container)
            => container != null && (container.Tag as string) == StickyNoteContainerTag;

        private void BeginStickyPointer(Grid container, Point pointer, bool stylus)
        {
            if (!IsStickyNoteContainer(container))
                return;

            if (ReferenceEquals(_stickyDragContainer, container))
                EndStickyPointer(container, canceled: true);
            if (_stickyDragContainer != null)
                EndStickyPointer(_stickyDragContainer, canceled: true);

            _stickyDragContainer = container;
            _stickyDragStartPointer = pointer;
            _stickyDragStartPosition = new Point(
                double.IsNaN(Canvas.GetLeft(container)) ? 0 : Canvas.GetLeft(container),
                double.IsNaN(Canvas.GetTop(container)) ? 0 : Canvas.GetTop(container));
            _stickyDragMoved = false;
            _stickyDragUsingStylus = stylus;
            container.Focus();
            if (stylus)
                container.CaptureStylus();
            else
                container.CaptureMouse();
        }

        private void UpdateStickyPointer(Grid container, Point pointer)
        {
            if (!ReferenceEquals(_stickyDragContainer, container))
                return;

            var delta = pointer - _stickyDragStartPointer;
            if (!_stickyDragMoved
                && Math.Abs(delta.X) < StickyDragThreshold
                && Math.Abs(delta.Y) < StickyDragThreshold)
                return;

            _stickyDragMoved = true;
            SetStickyNotePositionQuiet(
                container,
                new Point(_stickyDragStartPosition.X + delta.X, _stickyDragStartPosition.Y + delta.Y));
        }

        private void EndStickyPointer(Grid container, bool canceled)
        {
            if (!ReferenceEquals(_stickyDragContainer, container))
                return;

            _suppressStickyCaptureCancellation = true;
            try
            {
                if (_stickyDragUsingStylus && container.IsStylusCaptured)
                    container.ReleaseStylusCapture();
                if (!_stickyDragUsingStylus && container.IsMouseCaptured)
                    container.ReleaseMouseCapture();
            }
            finally
            {
                _suppressStickyCaptureCancellation = false;
            }

            bool moved = _stickyDragMoved;
            var oldPosition = _stickyDragStartPosition;
            var newPosition = new Point(
                double.IsNaN(Canvas.GetLeft(container)) ? oldPosition.X : Canvas.GetLeft(container),
                double.IsNaN(Canvas.GetTop(container)) ? oldPosition.Y : Canvas.GetTop(container));
            _stickyDragContainer = null;
            _stickyDragMoved = false;
            _stickyDragUsingStylus = false;

            if (canceled)
            {
                // Deactivation/unload can interrupt a captured gesture before
                // MouseUp/StylusUp. Never leave an unrecorded half-move in the
                // model; the next activation can start a fresh gesture.
                if (moved)
                    SetStickyNotePositionQuiet(container, oldPosition);
                return;
            }
            if (moved)
            {
                StickyNoteMoved?.Invoke(this, new StickyNoteMovedEventArgs(container, oldPosition, newPosition));
            }
            else
            {
                StickyNoteActivated?.Invoke(this, container);
            }
        }

        private void StickyNote_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null || e.ChangedButton != MouseButton.Left)
                return;
            BeginStickyPointer(sender as Grid, e.GetPosition(this), stylus: false);
            e.Handled = true;
        }

        private void StickyNote_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.StylusDevice != null || e.LeftButton != MouseButtonState.Pressed)
                return;
            UpdateStickyPointer(sender as Grid, e.GetPosition(this));
            if (ReferenceEquals(_stickyDragContainer, sender))
                e.Handled = true;
        }

        private void StickyNote_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null || e.ChangedButton != MouseButton.Left)
                return;
            EndStickyPointer(sender as Grid, canceled: false);
            e.Handled = true;
        }

        private void StickyNote_StylusDown(object sender, StylusDownEventArgs e)
        {
            BeginStickyPointer(sender as Grid, e.GetPosition(this), stylus: true);
            e.Handled = true;
        }

        private void StickyNote_StylusMove(object sender, StylusEventArgs e)
        {
            UpdateStickyPointer(sender as Grid, e.GetPosition(this));
            if (ReferenceEquals(_stickyDragContainer, sender))
                e.Handled = true;
        }

        private void StickyNote_StylusUp(object sender, StylusEventArgs e)
        {
            EndStickyPointer(sender as Grid, canceled: false);
            e.Handled = true;
        }

        private void StickyNote_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!_suppressStickyCaptureCancellation
                && sender is Grid container
                && ReferenceEquals(_stickyDragContainer, container))
            {
                EndStickyPointer(container, canceled: true);
            }
        }

        private void StickyNote_LostStylusCapture(object sender, StylusEventArgs e)
        {
            if (!_suppressStickyCaptureCancellation
                && sender is Grid container
                && ReferenceEquals(_stickyDragContainer, container))
            {
                EndStickyPointer(container, canceled: true);
            }
        }

        private void StickyNote_KeyDown(object sender, KeyEventArgs e)
        {
            var container = sender as Grid;
            if (!IsStickyNoteContainer(container)
                || GetOverlayData(container) is not StickyNoteAnnotation note)
                return;

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                StickyNoteActivated?.Invoke(this, container);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                StickyNoteDeleteRequested?.Invoke(this, container);
                e.Handled = true;
                return;
            }

            double step = e.KeyboardDevice?.Modifiers.HasFlag(ModifierKeys.Shift) == true ? 16.0 : 4.0;
            double dx = 0;
            double dy = 0;
            switch (e.Key)
            {
                case Key.Left: dx = -step; break;
                case Key.Right: dx = step; break;
                case Key.Up: dy = -step; break;
                case Key.Down: dy = step; break;
                default: return;
            }

            var oldPosition = new Point(note.X, note.Y);
            if (!SetStickyNotePositionQuiet(container, new Point(oldPosition.X + dx, oldPosition.Y + dy)))
                return;

            var newPosition = new Point(note.X, note.Y);
            if (Math.Abs(newPosition.X - oldPosition.X) > 0.01
                || Math.Abs(newPosition.Y - oldPosition.Y) > 0.01)
            {
                StickyNoteMoved?.Invoke(this,
                    new StickyNoteMovedEventArgs(container, oldPosition, newPosition));
            }
            e.Handled = true;
        }

        /// <summary>All overlay containers currently on the page (images excluded).</summary>
        public IReadOnlyList<Grid> GetOverlayContainers()
        {
            var result = new List<Grid>();
            foreach (var child in ImageOverlayCanvas.Children)
            {
                if (child is Grid container && !IsImageContainer(container) && IsOverlayContainer(container))
                    result.Add(container);
            }
            return result;
        }

        /// <summary>Refreshes marker context-menu labels after a language change.</summary>
        public void RefreshStickyNoteContextMenuLocalization()
        {
            foreach (var container in GetOverlayContainers().Where(IsStickyNoteContainer))
            {
                string stickyLabel = LocalizationService.Get("Editor.StickyNoteTooltip");
                AutomationProperties.SetName(container, stickyLabel);
                AutomationProperties.SetHelpText(container, stickyLabel);
                if (container.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault() is not MenuItem delete)
                    continue;

                string label = LocalizationService.Get("Editor.DeleteTooltip");
                delete.Header = label;
                AutomationProperties.SetName(delete, label);
                AutomationProperties.SetHelpText(delete, label);
            }
        }

        public bool RemoveTextContainerQuiet(Grid container)
        {
            // Task 19/25/26/27: containers live on two layers — text on
            // TextOverlayCanvas, images + overlay annotations (markup / area
            // highlight / sticky note) on ImageOverlayCanvas (below ink).
            // Removal is centralized here so undo / delete / cross-page flows
            // stay layer-agnostic. The payload dicts keep their entries so a
            // later re-add (undo / move back) restores the item as-is.
            if (container == null)
                return false;

            if (ReferenceEquals(container.Parent, ImageOverlayCanvas))
            {
                if (IsStickyNoteContainer(container))
                    DetachStickyNoteHandlers(container);
                ImageOverlayCanvas.Children.Remove(container);
                _imageContainers.Remove(container);
                return true;
            }
            if (ReferenceEquals(container.Parent, TextOverlayCanvas))
            {
                TextOverlayCanvas.Children.Remove(container);
                return true;
            }

            return false;
        }

        public void AddTextContainerQuiet(Grid container)
        {
            if (IsOverlayContainer(container))
            {
                ImageOverlayCanvas.Children.Add(container);
                if (IsStickyNoteContainer(container))
                    AttachStickyNoteHandlers(container);
                if (IsImageContainer(container) && !_imageContainers.Contains(container))
                    _imageContainers.Add(container);
            }
            else
            {
                TextOverlayCanvas.Children.Add(container);
            }
        }

        private void CaptureSelectionInteractionSnapshot()
        {
            if (_selectionInteractionSnapshot != null)
                return;

            var snapshot = new SelectionInteractionSnapshot();
            foreach (var stroke in _selectedStrokes)
            {
                if (stroke == null)
                    continue;

                var points = new StylusPointCollection();
                foreach (var point in stroke.StylusPoints)
                    points.Add(point);
                snapshot.StrokePoints[stroke] = points;
                snapshot.StrokeAttributes[stroke] = stroke.DrawingAttributes.Clone();
            }

            foreach (var container in _selectedTextContainers)
            {
                if (container == null)
                    continue;

                var textBox = container.Children.OfType<TextBox>().FirstOrDefault();
                snapshot.Containers.Add(new SelectionContainerSnapshot
                {
                    Container = container,
                    Position = new Point(
                        double.IsNaN(Canvas.GetLeft(container)) ? 0 : Canvas.GetLeft(container),
                        double.IsNaN(Canvas.GetTop(container)) ? 0 : Canvas.GetTop(container)),
                    Width = container.Width,
                    Height = container.Height,
                    FontSize = textBox?.FontSize ?? double.NaN
                });
            }

            _selectionInteractionSnapshot = snapshot;
        }

        private void RestoreSelectionInteractionSnapshot()
        {
            var snapshot = _selectionInteractionSnapshot;
            if (snapshot == null)
                return;

            foreach (var pair in snapshot.StrokePoints)
            {
                if (pair.Key == null)
                    continue;
                var points = new StylusPointCollection();
                foreach (var point in pair.Value)
                    points.Add(point);
                pair.Key.StylusPoints = points;
                if (snapshot.StrokeAttributes.TryGetValue(pair.Key, out var attributes))
                    pair.Key.DrawingAttributes = attributes.Clone();
            }

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
            }
        }

        private void CancelSelectionInteraction(bool restoreSnapshot)
        {
            bool active = _isSelecting || _isDraggingSelection || _isResizingSelection
                || _selectionInteractionSnapshot != null
                || SelectionOverlayCanvas.IsMouseCaptured
                || SelectionOverlayCanvas.IsStylusCaptured;
            if (!active)
                return;

            if (restoreSnapshot)
                RestoreSelectionInteractionSnapshot();

            _suppressSelectionCaptureCancellation = true;
            try
            {
                if (SelectionOverlayCanvas.IsMouseCaptured)
                    SelectionOverlayCanvas.ReleaseMouseCapture();
                if (SelectionOverlayCanvas.IsStylusCaptured)
                    SelectionOverlayCanvas.ReleaseStylusCapture();
            }
            finally
            {
                _suppressSelectionCaptureCancellation = false;
            }

            _isSelecting = false;
            _isDraggingSelection = false;
            _isResizingSelection = false;
            _lastResizeScale = 1.0;
            _totalDragDeltaX = 0;
            _totalDragDeltaY = 0;
            if (this.Parent is System.Windows.Controls.Grid pageGrid)
                System.Windows.Controls.Panel.SetZIndex(pageGrid, 0);
            _freeSelectionPath = null;
            _freeSelectionPoints = null;
            _selectionRect = null;
            SelectionOverlayCanvas.Children.Clear();
            _selectionInteractionSnapshot = null;
            UpdateSelectionVisuals();
        }

        private void SelectionOverlayCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!_suppressSelectionCaptureCancellation)
                CancelSelectionInteraction(restoreSnapshot: true);
        }

        private void SelectionOverlayCanvas_LostStylusCapture(object sender, StylusEventArgs e)
        {
            if (!_suppressSelectionCaptureCancellation)
                CancelSelectionInteraction(restoreSnapshot: true);
        }

        public void SetSelectionMode(bool enabled)
        {
            _isSelectionMode = enabled;
            SelectionOverlayCanvas.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            SelectionOverlayCanvas.IsHitTestVisible = enabled;

            if (!enabled)
            {
                CancelSelectionInteraction(restoreSnapshot: true);
                ClearSelection();
                if (SelectionOverlayCanvas.IsMouseCaptured)
                    SelectionOverlayCanvas.ReleaseMouseCapture();
                if (SelectionOverlayCanvas.IsStylusCaptured)
                    SelectionOverlayCanvas.ReleaseStylusCapture();
            }
            else
            {
                InkCanvas.IsHitTestVisible = false;
                Cursor = Cursors.Cross;
            }
        }

        public void SetSelectionFilter(SelectionFilter filter)
        {
            _selectionFilter = filter;
        }

        public void SetSelectionShape(SelectionShape shape)
        {
            _selectionShape = shape;
        }

        public void ClearSelection()
        {
            _selectedStrokes.Clear();
            _selectedTextContainers.Clear();
            _isSelecting = false;
            _isDraggingSelection = false;
            _isResizingSelection = false;
            _lastResizeScale = 1.0;
            _totalDragDeltaX = 0;
            _totalDragDeltaY = 0;
            _freeSelectionPath = null;
            _freeSelectionPoints = null;
            SelectionOverlayCanvas.Children.Clear();
            _selectionRect = null;
            StopSelectionDashAnimation();
            SelectionChanged?.Invoke(this, new AnnotationSelectionChangedEventArgs(false, Rect.Empty));
        }

        /// <summary>
        /// Replaces the current selection with the given strokes + text
        /// containers in bulk (Task 8.2: auto-select pasted content). Mirrors
        /// the marquee-completion path: empty input falls through to
        /// ClearSelection, otherwise visuals (bbox + per-item outlines +
        /// handles) are rebuilt and SelectionChanged fires.
        /// </summary>
        public void SelectItems(IEnumerable<Stroke> strokes, IEnumerable<Grid> containers)
        {
            _selectedStrokes.Clear();
            _selectedTextContainers.Clear();

            if (strokes != null)
            {
                foreach (var stroke in strokes)
                {
                    if (stroke != null && !_selectedStrokes.Contains(stroke))
                        _selectedStrokes.Add(stroke);
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

        /// <summary>
        /// Task 33: selects every annotation on the page, including text,
        /// images, PDF markups, area highlights and sticky notes.
        /// </summary>
        public void SelectAllAnnotations()
        {
            var strokes = InkCanvas.Strokes.Cast<Stroke>().ToList();
            var containers = TextOverlayCanvas.Children.OfType<Grid>()
                .Concat(ImageOverlayCanvas.Children.OfType<Grid>().Where(IsOverlayContainer))
                .ToList();
            SelectItems(strokes, containers);
        }

        public void MoveSelection(double deltaX, double deltaY)
        {
            if (_selectedStrokes.Count == 0 && _selectedTextContainers.Count == 0)
                return;

            MoveItemsDirectly(_selectedStrokes, _selectedTextContainers, deltaX, deltaY);
        }

        public void MoveItemsDirectly(List<Stroke> strokes, List<Grid> containers, double deltaX, double deltaY)
        {
            if (strokes.Count == 0 && containers.Count == 0)
                return;

            foreach (var stroke in strokes)
            {
                var newPoints = new StylusPointCollection();
                foreach (var pt in stroke.StylusPoints)
                {
                    newPoints.Add(new StylusPoint(pt.X + deltaX, pt.Y + deltaY, pt.PressureFactor));
                }
                stroke.StylusPoints = newPoints;
            }

            foreach (var container in containers)
            {
                var left = Canvas.GetLeft(container);
                var top = Canvas.GetTop(container);
                if (IsStickyNoteContainer(container))
                {
                    SetStickyNotePositionQuiet(container,
                        new Point(
                            (double.IsNaN(left) ? 0 : left) + deltaX,
                            (double.IsNaN(top) ? 0 : top) + deltaY));
                }
                else
                {
                    Canvas.SetLeft(container, left + deltaX);
                    Canvas.SetTop(container, top + deltaY);
                }
            }

            UpdateSelectionVisuals();
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        public void ScaleSelection(double scaleFactor, Point center)
        {
            if (_selectedStrokes.Count == 0 && _selectedTextContainers.Count == 0)
                return;

            ScaleItemsDirectly(_selectedStrokes, _selectedTextContainers, scaleFactor, center);
        }

        public void ScaleItemsDirectly(List<Stroke> strokes, List<Grid> containers, double scaleFactor, Point center)
        {
            if (strokes.Count == 0 && containers.Count == 0)
                return;

            foreach (var stroke in strokes)
            {
                var newPoints = new StylusPointCollection();
                foreach (var pt in stroke.StylusPoints)
                {
                    var newX = center.X + (pt.X - center.X) * scaleFactor;
                    var newY = center.Y + (pt.Y - center.Y) * scaleFactor;
                    newPoints.Add(new StylusPoint(newX, newY));
                }
                stroke.StylusPoints = newPoints;

                stroke.DrawingAttributes.Width *= scaleFactor;
                stroke.DrawingAttributes.Height *= scaleFactor;
            }

            foreach (var container in containers)
            {
                var left = Canvas.GetLeft(container);
                var top = Canvas.GetTop(container);
                var newLeft = center.X + (left - center.X) * scaleFactor;
                var newTop = center.Y + (top - center.Y) * scaleFactor;

                if (IsOverlayContainer(container))
                {
                    // Task 19/25/26/27: overlay containers scale via their
                    // explicit size; the inner content follows automatically
                    // (images: Stretch=Uniform; markup: Viewbox; area
                    // highlight: Background; sticky: centered icon).
                    container.Width = Math.Max(1.0, container.Width * scaleFactor);
                    container.Height = Math.Max(1.0, container.Height * scaleFactor);
                    if (IsStickyNoteContainer(container)
                        && GetOverlayData(container) is StickyNoteAnnotation note)
                    {
                        note.Width = container.Width;
                        note.Height = container.Height;
                        // A selection resize is a real geometry edit, not just
                        // a visual transform.  Keep the serialized DIP origin
                        // in sync and clamp against the new marker dimensions
                        // before the next save/undo/cross-page operation.
                        SetStickyNotePositionQuiet(container, new Point(newLeft, newTop));
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
                    {
                        tb.FontSize *= scaleFactor;
                    }
                }
            }

            UpdateSelectionVisuals();
            InkMutated?.Invoke(this, EventArgs.Empty);
        }

        public Rect GetSelectionBounds()
        {
            if (_selectedStrokes.Count == 0 && _selectedTextContainers.Count == 0)
                return Rect.Empty;

            var bounds = Rect.Empty;

            foreach (var stroke in _selectedStrokes)
            {
                var strokeBounds = stroke.GetBounds();
                if (bounds.IsEmpty)
                    bounds = strokeBounds;
                else
                    bounds.Union(strokeBounds);
            }

            foreach (var container in _selectedTextContainers)
            {
                var left = Canvas.GetLeft(container);
                var top = Canvas.GetTop(container);
                // Image containers carry an explicit Width/Height (Task 19);
                // fall back to it before the first layout pass so the paste
                // auto-select bbox is correct immediately. Text containers
                // leave Width/Height NaN → 0, matching the old behaviour.
                var width = container.ActualWidth > 0 ? container.ActualWidth
                    : (!double.IsNaN(container.Width) && container.Width > 0 ? container.Width : 0);
                var height = container.ActualHeight > 0 ? container.ActualHeight
                    : (!double.IsNaN(container.Height) && container.Height > 0 ? container.Height : 0);
                var rect = new Rect(left, top, width, height);
                if (bounds.IsEmpty)
                    bounds = rect;
                else
                    bounds.Union(rect);
            }

            return bounds;
        }

        public bool HasSelection => _selectedStrokes.Count > 0 || _selectedTextContainers.Count > 0;

        public List<Stroke> SelectedStrokes => _selectedStrokes;
        public List<Grid> SelectedTextContainers => _selectedTextContainers;

        private void UpdateSelectionVisuals()
        {
            var bounds = GetSelectionBounds();
            if (bounds.IsEmpty)
            {
                StopSelectionDashAnimation();
                return;
            }

            SelectionOverlayCanvas.Children.Clear();
            var selectionBorder = new System.Windows.Shapes.Rectangle
            {
                Width = bounds.Width + 8,
                Height = bounds.Height + 8,
                Stroke = Brushes.Transparent,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Fill = Brushes.Transparent,
                Cursor = Cursors.SizeAll
            };
            selectionBorder.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ThemeAccentBrush");
            selectionBorder.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ThemeSelectionBrush");
            selectionBorder.Opacity = 0.18;
            Canvas.SetLeft(selectionBorder, bounds.Left - 4);
            Canvas.SetTop(selectionBorder, bounds.Top - 4);
            SelectionOverlayCanvas.Children.Add(selectionBorder);

            // Per-item marching-ants outlines (Task 6): each selected stroke /
            // text container gets its own dashed rect so overlapping items stay
            // individually distinguishable. The list is rebuilt only when the
            // selection changes, while the animation itself updates render-only
            // properties, so every selected item can retain its own outline.
            _perItemOutlines.Clear();
            foreach (var stroke in _selectedStrokes)
            {
                var strokeBounds = stroke.GetBounds();
                strokeBounds.Inflate(3, 3);
                AddPerItemOutline(strokeBounds);
            }
            foreach (var container in _selectedTextContainers)
            {
                var width = container.ActualWidth > 0 ? container.ActualWidth : container.RenderSize.Width;
                var height = container.ActualHeight > 0 ? container.ActualHeight : container.RenderSize.Height;
                var containerBounds = new Rect(Canvas.GetLeft(container), Canvas.GetTop(container), width, height);
                containerBounds.Inflate(3, 3);
                AddPerItemOutline(containerBounds);
            }

            if (_perItemOutlines.Count > 0)
                StartSelectionDashAnimation();
            else
                StopSelectionDashAnimation();

            var handles = new[] {
                new Point(bounds.Left - 4, bounds.Top - 4),    // 0: TL
                new Point(bounds.Right + 4, bounds.Top - 4),   // 1: TR
                new Point(bounds.Left - 4, bounds.Bottom + 4), // 2: BL
                new Point(bounds.Right + 4, bounds.Bottom + 4) // 3: BR
            };

            var handleCursors = new[] {
                Cursors.SizeNWSE,  // TL
                Cursors.SizeNESW,  // TR
                Cursors.SizeNESW,  // BL
                Cursors.SizeNWSE,  // BR
            };

            for (int i = 0; i < handles.Length; i++)
            {
                var handlePos = handles[i];
                var handle = new System.Windows.Shapes.Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = Brushes.Transparent,
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 1.5,
                    Cursor = handleCursors[i],
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 6,
                        ShadowDepth = 0,
                         Opacity = ThemeService.GetShadowOpacity(),
                        Color = Colors.Black
                    }
                };
                handle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ThemeSurfaceBrush");
                handle.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ThemeAccentBrush");
                Canvas.SetLeft(handle, handlePos.X - 6);
                Canvas.SetTop(handle, handlePos.Y - 6);
                SelectionOverlayCanvas.Children.Add(handle);
            }

            SelectionChanged?.Invoke(this, new AnnotationSelectionChangedEventArgs(true, bounds));
        }

        private void AddPerItemOutline(Rect bounds)
        {
            var outline = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(bounds.Width, 1),
                Height = Math.Max(bounds.Height, 1),
                Stroke = Brushes.Transparent,
                StrokeThickness = 1.2,
                StrokeDashArray = PerItemOutlineDashArray,
                StrokeDashOffset = _selectionDashOffset,
                Fill = null,
                IsHitTestVisible = false,
                Tag = "perItemOutline"
            };
            outline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ThemeAccentBrush");
            Canvas.SetLeft(outline, bounds.Left);
            Canvas.SetTop(outline, bounds.Top);
            SelectionOverlayCanvas.Children.Add(outline);
            _perItemOutlines.Add(outline);
        }

        private void StartSelectionDashAnimation()
        {
            if (!_isHostActive || !ThemeService.ShouldAnimate || _isSelectionDashAnimating)
                return;
            _isSelectionDashAnimating = true;
            _selectionDashLastTickUtc = DateTime.UtcNow;
            // Unsubscribe-before-subscribe guards against double subscription.
            System.Windows.Media.CompositionTarget.Rendering -= SelectionDashAnimation_Tick;
            System.Windows.Media.CompositionTarget.Rendering += SelectionDashAnimation_Tick;
        }

        private void StopSelectionDashAnimation()
        {
            _perItemOutlines.Clear();
            if (!_isSelectionDashAnimating)
                return;
            _isSelectionDashAnimating = false;
            System.Windows.Media.CompositionTarget.Rendering -= SelectionDashAnimation_Tick;
        }

        private void SelectionDashAnimation_Tick(object sender, EventArgs e)
        {
            // Bail out (and unsubscribe) when the page was unloaded without
            // ClearSelection — the static Rendering event would otherwise root
            // this control forever.
            if (!_isSelectionDashAnimating || !ThemeService.ShouldAnimate || _perItemOutlines.Count == 0 || !IsLoaded)
            {
                StopSelectionDashAnimation();
                return;
            }

            var now = DateTime.UtcNow;
            var elapsed = (now - _selectionDashLastTickUtc).TotalSeconds;
            _selectionDashLastTickUtc = now;
            if (elapsed < 0 || elapsed > 1.0)
                elapsed = 0; // clamp after stalls (tab switch, debugger break)

            _selectionDashOffset = (_selectionDashOffset + elapsed * SelectionDashSpeed) % SelectionDashPatternPeriod;
            _selectionColorPhaseSeconds += elapsed;
            if (_selectionColorPhaseSeconds >= SelectionColorHalfCycleSeconds * 2)
                _selectionColorPhaseSeconds -= SelectionColorHalfCycleSeconds * 2;

            // Switch between the live accent and focus brushes so the
            // marching-ants cue remains visible in every runtime palette,
            // including system HighContrast.
            var brushKey = _selectionColorPhaseSeconds >= SelectionColorHalfCycleSeconds
                ? "ThemeFocusBrush"
                : "ThemeAccentBrush";
            var brush = Application.Current?.TryFindResource(brushKey) as Brush
                ?? Brushes.Transparent;
            foreach (var outline in _perItemOutlines)
            {
                outline.StrokeDashOffset = _selectionDashOffset;
                if (!ReferenceEquals(outline.Stroke, brush))
                    outline.Stroke = brush;
            }
        }

        // Frozen so all per-item rects can share one instance without
        // per-shape inheritance-context tracking.
        private static readonly DoubleCollection PerItemOutlineDashArray = CreateFrozenDashArray();

        private static DoubleCollection CreateFrozenDashArray()
        {
            var dash = new DoubleCollection { 3, 2 };
            dash.Freeze();
            return dash;
        }

        private static bool IsDescendantOf(DependencyObject descendant, DependencyObject ancestor)
        {
            var current = descendant;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;

                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }

            return false;
        }

        private static Point GetOppositeCorner(Rect bounds, int handleIndex)
        {
            return handleIndex switch
            {
                0 => new Point(bounds.Right, bounds.Bottom),  // TL �?BR
                1 => new Point(bounds.Left, bounds.Bottom),   // TR �?BL
                2 => new Point(bounds.Right, bounds.Top),     // BL �?TR
                3 => new Point(bounds.Left, bounds.Top),      // BR �?TL
                _ => new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)
            };
        }

        private static double PointDistance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Cursor GetResizeCursor(int handleIndex)
        {
            return handleIndex switch
            {
                0 => Cursors.SizeNWSE,  // TL
                1 => Cursors.SizeNESW,  // TR
                2 => Cursors.SizeNESW,  // BL
                3 => Cursors.SizeNWSE,  // BR
                _ => Cursors.SizeAll
            };
        }

        public void InvokeSelectionMouseDownCore(Point point)
        {
            SelectionOverlayCanvas_MouseLeftButtonDownCore(point);
        }

        public void InvokeSelectionMouseMoveCore(Point point)
        {
            SelectionOverlayCanvas_MouseMoveCore(point);
        }

        public void InvokeSelectionMouseUpCore()
        {
            SelectionOverlayCanvas_MouseLeftButtonUpCore();
        }

        private void SelectionOverlayCanvas_MouseLeftButtonDownCore(Point point, bool fromStylus = false)
        {
            if (!_isSelectionMode) return;

            // Task 7: Ctrl+click toggles the topmost item under the cursor
            // (multi-select) instead of clearing + starting a new marquee.
            // Mouse only — pen input rarely combines with a held Ctrl key, so
            // the stylus path keeps the classic marquee behavior.
            if (!fromStylus && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                HandleCtrlClickToggle(point);
                return;
            }

            if (HasSelection)
            {
                var bounds = GetSelectionBounds();

                // Check corner handles first (resize)
                var cornerHandles = new[] {
                    new Point(bounds.Left - 4, bounds.Top - 4),    // 0: TL
                    new Point(bounds.Right + 4, bounds.Top - 4),   // 1: TR
                    new Point(bounds.Left - 4, bounds.Bottom + 4), // 2: BL
                    new Point(bounds.Right + 4, bounds.Bottom + 4) // 3: BR
                };
                for (int i = 0; i < cornerHandles.Length; i++)
                {
                    var hitRect = new Rect(cornerHandles[i].X - 8, cornerHandles[i].Y - 8, 16, 16);
                    if (hitRect.Contains(point))
                    {
                        CaptureSelectionInteractionSnapshot();
                        _isResizingSelection = true;
                        _resizeHandleIndex = i;
                        _resizeAnchorPoint = GetOppositeCorner(bounds, i);
                        _resizeStartHandleDist = PointDistance(cornerHandles[i], _resizeAnchorPoint);
                        if (_resizeStartHandleDist < 1.0) _resizeStartHandleDist = 1.0;
                        _lastResizeScale = 1.0;
                        CaptureSelectionInput(fromStylus);
                        if (this.Parent is System.Windows.Controls.Grid pg1) { System.Windows.Controls.Panel.SetZIndex(pg1, 999); }
                        return;
                    }
                }

                var inflatedBounds = bounds;
                inflatedBounds.Inflate(8, 8);

                if (inflatedBounds.Contains(point))
                {
                    CaptureSelectionInteractionSnapshot();
                    _isDraggingSelection = true;
                    _dragStartPoint = point;
                    _totalDragDeltaX = 0;
                    _totalDragDeltaY = 0;
                    CaptureSelectionInput(fromStylus);
                    if (this.Parent is System.Windows.Controls.Grid pg2) { System.Windows.Controls.Panel.SetZIndex(pg2, 999); }
                    return;
                }
            }

            ClearSelection();
            _isSelecting = true;
            _selectionStartPoint = point;
            CaptureSelectionInput(fromStylus);
            if (this.Parent is System.Windows.Controls.Grid pg3) { System.Windows.Controls.Panel.SetZIndex(pg3, 999); }

            if (_selectionShape == SelectionShape.FreeForm)
            {
                _freeSelectionPoints = new System.Windows.Media.PointCollection { point };
                _freeSelectionPath = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Points = _freeSelectionPoints
                };
                SelectionOverlayCanvas.Children.Add(_freeSelectionPath);
            }
            else
            {
                _selectionRect = new System.Windows.Shapes.Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 212))
                };
                Canvas.SetLeft(_selectionRect, point.X);
                Canvas.SetTop(_selectionRect, point.Y);
                SelectionOverlayCanvas.Children.Add(_selectionRect);
            }
        }

        private void CaptureSelectionInput(bool fromStylus)
        {
            if (fromStylus)
                SelectionOverlayCanvas.CaptureStylus();
            else
                SelectionOverlayCanvas.CaptureMouse();
        }

        // --- Task 7: Ctrl+click multi-select (Select tool, mouse only) ---

        private void HandleCtrlClickToggle(Point point)
        {
            // Text containers sit above ink in the layer stack, so text wins
            // when both hit at the click point (topmost visual). Children are
            // scanned in reverse z-order (last = topmost). Image containers
            // (Task 19) sit below ink but above the page bitmap, so they are
            // probed after text and before strokes.
            if (_selectionFilter != SelectionFilter.DrawingsOnly)
            {
                for (int i = TextOverlayCanvas.Children.Count - 1; i >= 0; i--)
                {
                    if (TextOverlayCanvas.Children[i] is Grid container && HitTextContainer(container, point))
                    {
                        ToggleTextContainerSelection(container);
                        return;
                    }
                }

                for (int i = ImageOverlayCanvas.Children.Count - 1; i >= 0; i--)
                {
                    if (ImageOverlayCanvas.Children[i] is Grid container && HitTextContainer(container, point))
                    {
                        ToggleTextContainerSelection(container);
                        return;
                    }
                }
            }

            if (_selectionFilter != SelectionFilter.TextOnly)
            {
                for (int i = InkCanvas.Strokes.Count - 1; i >= 0; i--)
                {
                    var stroke = InkCanvas.Strokes[i];
                    if (HitStroke(stroke, point))
                    {
                        ToggleStrokeSelection(stroke);
                        return;
                    }
                }
            }

            // Ctrl+click on empty space: keep the current selection unchanged.
        }

        private static bool HitTextContainer(Grid container, Point point)
        {
            var width = container.ActualWidth > 0 ? container.ActualWidth : container.RenderSize.Width;
            var height = container.ActualHeight > 0 ? container.ActualHeight : container.RenderSize.Height;
            var containerRect = new Rect(Canvas.GetLeft(container), Canvas.GetTop(container), width, height);
            return containerRect.Contains(point);
        }

        private static bool HitStroke(Stroke stroke, Point point)
        {
            // 1. Hit-test along the stroke path with an 8px diameter tolerance
            if (stroke.HitTest(point, 8))
                return true;

            // 2. Closed shapes use their actual polygon interior. Do not fall
            // back to the axis-aligned bounds for these strokes, or a click in
            // a corner outside a triangle/ellipse would select it.
            var pts = stroke.StylusPoints;
            if (pts.Count >= 4)
            {
                var pFirst = new Point(pts[0].X, pts[0].Y);
                var pLast = new Point(pts[pts.Count - 1].X, pts[pts.Count - 1].Y);
                if (Math.Abs(pFirst.X - pLast.X) < 4.0 && Math.Abs(pFirst.Y - pLast.Y) < 4.0)
                {
                    var poly = new System.Windows.Media.PointCollection(pts.Count);
                    for (int i = 0; i < pts.Count; i++)
                        poly.Add(new Point(pts[i].X, pts[i].Y));

                    if (IsPointInPolygon(poly, point))
                        return true;

                    return false;
                }
            }

            // 3. Freehand/open drawings are selectable anywhere in their
            // visible bounded area. This restores the broad-stroke behavior
            // without expanding the hit target beyond the stroke bounds.
            return stroke.GetBounds().Contains(point);
        }

        private void ToggleStrokeSelection(Stroke stroke)
        {
            if (_selectedStrokes.Contains(stroke))
            {
                _selectedStrokes.Remove(stroke);
                RefreshSelectionAfterToggle();
            }
            else
            {
                _selectedStrokes.Add(stroke);
                UpdateSelectionVisuals();
            }
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

        private void RefreshSelectionAfterToggle()
        {
            if (!HasSelection)
                ClearSelection();
            else
                UpdateSelectionVisuals();
        }

        private void SelectionOverlayCanvas_MouseMoveCore(Point point)
        {
            if (!_isSelectionMode) return;

            if (_isResizingSelection)
            {
                var dist = PointDistance(_resizeAnchorPoint, point);
                if (dist < 1.0) dist = 1.0;
                var totalScale = dist / _resizeStartHandleDist;
                if (totalScale < 0.01) totalScale = 0.01;
                var deltaScale = totalScale / _lastResizeScale;
                _lastResizeScale = totalScale;
                ScaleSelection(deltaScale, _resizeAnchorPoint);
                Cursor = GetResizeCursor(_resizeHandleIndex);
            }
            else if (_isDraggingSelection)
            {
                var deltaX = point.X - _dragStartPoint.X;
                var deltaY = point.Y - _dragStartPoint.Y;
                _totalDragDeltaX += deltaX;
                _totalDragDeltaY += deltaY;
                MoveSelection(deltaX, deltaY);
                _dragStartPoint = point;
                Cursor = Cursors.SizeAll;
            }
            else if (_isSelecting)
            {
                if (_selectionShape == SelectionShape.FreeForm && _freeSelectionPath != null)
                {
                    _freeSelectionPoints.Add(point);
                }
                else if (_selectionRect != null)
                {
                    var x = Math.Min(_selectionStartPoint.X, point.X);
                    var y = Math.Min(_selectionStartPoint.Y, point.Y);
                    var width = Math.Abs(point.X - _selectionStartPoint.X);
                    var height = Math.Abs(point.Y - _selectionStartPoint.Y);

                    Canvas.SetLeft(_selectionRect, x);
                    Canvas.SetTop(_selectionRect, y);
                    _selectionRect.Width = width;
                    _selectionRect.Height = height;
                }
            }
            else if (HasSelection)
            {
                var bounds = GetSelectionBounds();

                // Show resize cursor when hovering over corner handles
                var cornerHandles = new[] {
                    new Point(bounds.Left - 4, bounds.Top - 4),
                    new Point(bounds.Right + 4, bounds.Top - 4),
                    new Point(bounds.Left - 4, bounds.Bottom + 4),
                    new Point(bounds.Right + 4, bounds.Bottom + 4)
                };
                for (int i = 0; i < cornerHandles.Length; i++)
                {
                    var hitRect = new Rect(cornerHandles[i].X - 8, cornerHandles[i].Y - 8, 16, 16);
                    if (hitRect.Contains(point))
                    {
                        Cursor = GetResizeCursor(i);
                        return;
                    }
                }

                var inflatedBounds = bounds;
                inflatedBounds.Inflate(8, 8);
                Cursor = inflatedBounds.Contains(point) ? Cursors.SizeAll : Cursors.Cross;
            }
            else
            {
                Cursor = Cursors.Cross;
            }
        }

        private void SelectionOverlayCanvas_MouseLeftButtonUpCore()
        {
            if (!_isSelectionMode) return;

            if (_isResizingSelection)
            {
                _isResizingSelection = false;
                if (this.Parent is System.Windows.Controls.Grid pGrid) { System.Windows.Controls.Panel.SetZIndex(pGrid, 0); }
                _suppressSelectionCaptureCancellation = true;
                try
                {
                    if (SelectionOverlayCanvas.IsMouseCaptured)
                        SelectionOverlayCanvas.ReleaseMouseCapture();
                    if (SelectionOverlayCanvas.IsStylusCaptured)
                        SelectionOverlayCanvas.ReleaseStylusCapture();
                }
                finally
                {
                    _suppressSelectionCaptureCancellation = false;
                }

                if (Math.Abs(_lastResizeScale - 1.0) > 0.001)
                    SelectionResizeCompleted?.Invoke(this, new SelectionResizeCompletedEventArgs(
                        _lastResizeScale, _resizeAnchorPoint,
                        new List<Stroke>(_selectedStrokes),
                        new List<Grid>(_selectedTextContainers)));
                _lastResizeScale = 1.0;
                _selectionInteractionSnapshot = null;
            }
            else if (_isDraggingSelection)
            {
                _isDraggingSelection = false;
                if (this.Parent is System.Windows.Controls.Grid pGrid) { System.Windows.Controls.Panel.SetZIndex(pGrid, 0); }
                _suppressSelectionCaptureCancellation = true;
                try
                {
                    if (SelectionOverlayCanvas.IsMouseCaptured)
                        SelectionOverlayCanvas.ReleaseMouseCapture();
                    if (SelectionOverlayCanvas.IsStylusCaptured)
                        SelectionOverlayCanvas.ReleaseStylusCapture();
                }
                finally
                {
                    _suppressSelectionCaptureCancellation = false;
                }

                if (Math.Abs(_totalDragDeltaX) > 0.5 || Math.Abs(_totalDragDeltaY) > 0.5)
                    SelectionMoveCompleted?.Invoke(this, new SelectionMoveCompletedEventArgs(
                        _totalDragDeltaX, _totalDragDeltaY,
                        new List<Stroke>(_selectedStrokes),
                        new List<Grid>(_selectedTextContainers)));
                _totalDragDeltaX = 0;
                _totalDragDeltaY = 0;
                _selectionInteractionSnapshot = null;
            }
            else if (_isSelecting)
            {
                _isSelecting = false;
                if (this.Parent is System.Windows.Controls.Grid pGrid) { System.Windows.Controls.Panel.SetZIndex(pGrid, 0); }
                _suppressSelectionCaptureCancellation = true;
                try
                {
                    if (SelectionOverlayCanvas.IsMouseCaptured)
                        SelectionOverlayCanvas.ReleaseMouseCapture();
                    if (SelectionOverlayCanvas.IsStylusCaptured)
                        SelectionOverlayCanvas.ReleaseStylusCapture();
                }
                finally
                {
                    _suppressSelectionCaptureCancellation = false;
                }

                _selectedStrokes.Clear();
                _selectedTextContainers.Clear();

                bool isClick = false;
                if (_selectionShape == SelectionShape.FreeForm)
                {
                    isClick = _freeSelectionPoints == null || _freeSelectionPoints.Count <= 2;
                }
                else
                {
                    isClick = _selectionRect == null || (_selectionRect.Width < 4 && _selectionRect.Height < 4);
                }

                if (isClick)
                {
                    Point clickPoint = _selectionStartPoint;
                    bool hitSomething = false;

                    if (_selectionFilter != SelectionFilter.DrawingsOnly)
                    {
                        for (int i = TextOverlayCanvas.Children.Count - 1; i >= 0; i--)
                        {
                            if (TextOverlayCanvas.Children[i] is Grid container && HitTextContainer(container, clickPoint))
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
                                if (ImageOverlayCanvas.Children[i] is Grid container && HitTextContainer(container, clickPoint))
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
                        for (int i = InkCanvas.Strokes.Count - 1; i >= 0; i--)
                        {
                            var stroke = InkCanvas.Strokes[i];
                            if (HitStroke(stroke, clickPoint))
                            {
                                _selectedStrokes.Add(stroke);
                                hitSomething = true;
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
                        foreach (var stroke in InkCanvas.Strokes)
                        {
                            if (IsStrokeInsidePolygon(polygon, stroke))
                                _selectedStrokes.Add(stroke);
                        }
                    }

                    if (_selectionFilter != SelectionFilter.DrawingsOnly)
                    {
                        foreach (var element in TextOverlayCanvas.Children)
                        {
                            if (element is Grid container)
                            {
                                var containerRect = new Rect(Canvas.GetLeft(container), Canvas.GetTop(container), container.ActualWidth, container.ActualHeight);
                                if (IsContainerInsidePolygon(polygon, containerRect))
                                    _selectedTextContainers.Add(container);
                            }
                        }

                        // Task 19: image containers join the selection as
                        // container-like items (they sit below ink).
                        foreach (var element in ImageOverlayCanvas.Children)
                        {
                            if (element is Grid container)
                            {
                                var containerRect = new Rect(Canvas.GetLeft(container), Canvas.GetTop(container), container.ActualWidth, container.ActualHeight);
                                if (IsContainerInsidePolygon(polygon, containerRect))
                                    _selectedTextContainers.Add(container);
                            }
                        }
                    }
                }
                else if (_selectionRect != null)
                {
                    var selX = Canvas.GetLeft(_selectionRect);
                    var selY = Canvas.GetTop(_selectionRect);
                    var selRect = new Rect(selX, selY, _selectionRect.Width, _selectionRect.Height);

                    if (_selectionFilter != SelectionFilter.TextOnly)
                    {
                        foreach (var stroke in InkCanvas.Strokes)
                        {
                            if (IsStrokeInsideRect(selRect, stroke))
                                _selectedStrokes.Add(stroke);
                        }
                    }

                    if (_selectionFilter != SelectionFilter.DrawingsOnly)
                    {
                        foreach (var element in TextOverlayCanvas.Children)
                        {
                            if (element is Grid container)
                            {
                                var containerRect = new Rect(Canvas.GetLeft(container), Canvas.GetTop(container), container.ActualWidth, container.ActualHeight);
                                if (selRect.Contains(containerRect))
                                    _selectedTextContainers.Add(container);
                            }
                        }

                        // Task 19: image containers join the selection as
                        // container-like items (they sit below ink).
                        foreach (var element in ImageOverlayCanvas.Children)
                        {
                            if (element is Grid container)
                            {
                                var containerRect = new Rect(Canvas.GetLeft(container), Canvas.GetTop(container), container.ActualWidth, container.ActualHeight);
                                if (selRect.Contains(containerRect))
                                    _selectedTextContainers.Add(container);
                            }
                        }
                    }
                }

                _freeSelectionPath = null;
                _freeSelectionPoints = null;
                _selectionRect = null;

                // Auto-clear visuals if nothing was caught
                if (_selectedStrokes.Count == 0 && _selectedTextContainers.Count == 0)
                {
                    SelectionOverlayCanvas.Children.Clear();
                    StopSelectionDashAnimation();
                }
                else
                    UpdateSelectionVisuals();
            }
        }

        private static bool IsPointInPolygon(System.Windows.Media.PointCollection polygon, Point p)
        {
            bool inside = false;
            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; i++)
            {
                if (((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y)) &&
                    (p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                    inside = !inside;
                j = i;
            }
            return inside;
        }

        private static bool IsRectInsidePolygon(System.Windows.Media.PointCollection polygon, Rect rect)
        {
            return IsPointInPolygon(polygon, rect.TopLeft) &&
                   IsPointInPolygon(polygon, rect.TopRight) &&
                   IsPointInPolygon(polygon, rect.BottomLeft) &&
                   IsPointInPolygon(polygon, rect.BottomRight);
        }

        private static bool IsStrokeInsidePolygon(System.Windows.Media.PointCollection polygon, Stroke stroke)
        {
            if (IsRectInsidePolygon(polygon, stroke.GetBounds()))
                return true;

            var pts = stroke.StylusPoints;
            if (pts.Count == 0) return false;

            int insideCount = 0;
            foreach (var pt in pts)
            {
                if (IsPointInPolygon(polygon, new Point(pt.X, pt.Y)))
                    insideCount++;
            }

            return (double)insideCount / pts.Count >= 0.6 || (pts.Count <= 3 && insideCount == pts.Count);
        }

        private static bool IsContainerInsidePolygon(System.Windows.Media.PointCollection polygon, Rect containerRect)
        {
            if (IsRectInsidePolygon(polygon, containerRect))
                return true;

            var center = new Point(containerRect.Left + containerRect.Width / 2, containerRect.Top + containerRect.Height / 2);
            if (IsPointInPolygon(polygon, center))
                return true;

            int cornersIn = 0;
            if (IsPointInPolygon(polygon, containerRect.TopLeft)) cornersIn++;
            if (IsPointInPolygon(polygon, containerRect.TopRight)) cornersIn++;
            if (IsPointInPolygon(polygon, containerRect.BottomLeft)) cornersIn++;
            if (IsPointInPolygon(polygon, containerRect.BottomRight)) cornersIn++;

            return cornersIn >= 2;
        }

        private static bool IsStrokeInsideRect(Rect selRect, Stroke stroke)
        {
            if (selRect.Contains(stroke.GetBounds()))
                return true;

            var pts = stroke.StylusPoints;
            if (pts.Count == 0) return false;

            int insideCount = 0;
            foreach (var pt in pts)
            {
                if (selRect.Contains(new Point(pt.X, pt.Y)))
                    insideCount++;
            }

            return (double)insideCount / pts.Count >= 0.7;
        }

        private void SelectionOverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelectionMode) return;
            var point = e.GetPosition(SelectionOverlayCanvas);
            SelectionOverlayCanvas_MouseLeftButtonDownCore(point);
            e.Handled = true;
        }

        private void SelectionOverlayCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelectionMode) return;
            var point = e.GetPosition(SelectionOverlayCanvas);
            SelectionOverlayCanvas_MouseMoveCore(point);
            e.Handled = true;
        }

        private void SelectionOverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelectionMode) return;
            SelectionOverlayCanvas_MouseLeftButtonUpCore();
            e.Handled = true;
        }

        private void SelectionOverlayCanvas_StylusDown(object sender, StylusDownEventArgs e)
        {
            if (!_isSelectionMode) return;
            var point = e.GetPosition(SelectionOverlayCanvas);
            SelectionOverlayCanvas_MouseLeftButtonDownCore(point, fromStylus: true);
            e.Handled = true;
        }

        private void SelectionOverlayCanvas_StylusMove(object sender, StylusEventArgs e)
        {
            if (!_isSelectionMode) return;
            var point = e.GetPosition(SelectionOverlayCanvas);
            SelectionOverlayCanvas_MouseMoveCore(point);
            e.Handled = true;
        }

        private void SelectionOverlayCanvas_StylusUp(object sender, StylusEventArgs e)
        {
            if (!_isSelectionMode) return;
            SelectionOverlayCanvas_MouseLeftButtonUpCore();
            e.Handled = true;
        }

        private readonly List<HighlightAnnotation> _highlights = new();

        public IReadOnlyList<HighlightAnnotation> GetHighlights() => _highlights;

        public void AddHighlightAnnotation(IReadOnlyList<Rect> rects, Color color)
        {
            var highlight = new HighlightAnnotation
            {
                R = color.R,
                G = color.G,
                B = color.B,
                A = 120 // Semi-transparent overlay
            };

            foreach (var r in rects)
            {
                highlight.Rects.Add(new double[] { r.X, r.Y, r.Width, r.Height });
            }

            _highlights.Add(highlight);
            RenderHighlightVisual(highlight);
        }

        public void AddHighlight(HighlightAnnotation highlight)
        {
            _highlights.Add(highlight);
            RenderHighlightVisual(highlight);
        }

        private void RenderHighlightVisual(HighlightAnnotation highlight)
        {
            var color = Color.FromArgb(highlight.A, highlight.R, highlight.G, highlight.B);
            var brush = new SolidColorBrush(color);

            foreach (var rectInfo in highlight.Rects)
            {
                if (rectInfo.Length >= 4)
                {
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = rectInfo[2],
                        Height = rectInfo[3],
                        Fill = brush,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(rect, rectInfo[0]);
                    Canvas.SetTop(rect, rectInfo[1]);
                    HighlightsCanvas.Children.Add(rect);
                }
            }
        }
    }
}
