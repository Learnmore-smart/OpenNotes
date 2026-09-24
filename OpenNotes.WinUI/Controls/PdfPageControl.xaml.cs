using System;
using System.Collections.Generic;
using System.Linq;
using Caelum.Ink;
using Caelum.InkGeometry;
using Caelum.Models;
using Caelum.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
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

    /// <summary>A completed selection move gesture (strokes only — no containers in Phase B).</summary>
    public sealed class SelectionMoveCompletedEventArgs : EventArgs
    {
        public SelectionMoveCompletedEventArgs(
            double deltaX, double deltaY, IReadOnlyList<InkStrokeData> strokes)
        {
            DeltaX = deltaX;
            DeltaY = deltaY;
            Strokes = strokes ?? Array.Empty<InkStrokeData>();
        }

        public double DeltaX { get; }
        public double DeltaY { get; }
        public IReadOnlyList<InkStrokeData> Strokes { get; }
    }

    /// <summary>A completed selection resize gesture (uniform scale + anchor).</summary>
    public sealed class SelectionResizeCompletedEventArgs : EventArgs
    {
        public SelectionResizeCompletedEventArgs(
            double totalScale, PointD anchor, IReadOnlyList<InkStrokeData> strokes)
        {
            TotalScale = totalScale;
            Anchor = anchor;
            Strokes = strokes ?? Array.Empty<InkStrokeData>();
        }

        public double TotalScale { get; }
        public PointD Anchor { get; }
        public IReadOnlyList<InkStrokeData> Strokes { get; }
    }

    /// <summary>A completed selection rotate gesture (total degrees + centre).</summary>
    public sealed class SelectionRotateCompletedEventArgs : EventArgs
    {
        public SelectionRotateCompletedEventArgs(
            double totalDegrees, PointD center, IReadOnlyList<InkStrokeData> strokes)
        {
            TotalDegrees = totalDegrees;
            Center = center;
            Strokes = strokes ?? Array.Empty<InkStrokeData>();
        }

        public double TotalDegrees { get; }
        public PointD Center { get; }
        public IReadOnlyList<InkStrokeData> Strokes { get; }
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

    /// <summary>
    /// Per-selection-gesture stroke snapshot so a cancelled gesture (capture
    /// lost / Escape / tool switch) restores the exact pre-gesture geometry —
    /// WPF SelectionInteractionSnapshot parity, strokes only.
    /// </summary>
    internal sealed class SelectionInteractionSnapshot
    {
        public readonly Dictionary<InkStrokeData, (List<InkPointData> Points, double Size)> Strokes =
            new(ReferenceEqualityComparer.Instance);
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
    /// <item>Text/sticky/image overlay selection — those annotation kinds are
    /// Task 8; the selection engine is strokes-only for now and the
    /// <see cref="SelectionFilter"/> keeps its TextOnly arm inert.</item>
    /// </list>
    /// </summary>
    public sealed partial class PdfPageControl : UserControl
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
            ClearShapePreview();
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
            UpdateHiddenInkHitTesting();
            if (!enabled)
            {
                InkSurface.CancelInteraction();
                CancelSelectionInteraction(restoreSnapshot: true);
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

        // ==================================================================
        // Tool plumbing (Phase B)
        // ==================================================================

        /// <summary>
        /// Mirrors the WPF SetInputMode: maps the editor-level mode onto the
        /// ink surface tool. Inking leaves <see cref="InkSurface.Tool"/> alone
        /// (the caller sets Pen/Highlighter first); AreaHighlight is a T8
        /// stub that maps to None for now.
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
                CustomInkInputProcessingMode.Inking
                    => InkSurface.Tool is InkSurfaceTool.Pen or InkSurfaceTool.Highlighter
                        ? InkSurface.Tool
                        : InkSurfaceTool.Pen,
                _ => InkSurfaceTool.None,
            };

            UpdateHiddenInkHitTesting();
            if (mode != CustomInkInputProcessingMode.Shape)
                ClearShapePreview();
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
            StopAllHiddenInkRevealTimers();
            StopSelectionDashTimer();
            _laserFadeTimer?.Stop();
            _laserFadeTimer = null;
            LaserInkCanvas.Children.Clear();
            _liveLaserPolylines.Clear();
            _laserCompletedAt.Clear();
            _laserPolyline = null;
        }

        // ==================================================================
        // Selection (Phase B — strokes only)
        // ==================================================================

        /// <summary>True while any stroke is selected.</summary>
        public bool HasSelection => _selectedStrokes.Count > 0;

        /// <summary>The selected strokes (group-expanded) in z-order.</summary>
        public IReadOnlyList<InkStrokeData> SelectedStrokes => _selectedStrokes;

        /// <summary>Union of the selected strokes' rendered bounds.</summary>
        public Rect GetSelectionBounds()
        {
            var bounds = StrokeGeometry.GetSelectionBounds(_selectedStrokes);
            if (_selectedStrokes.Count == 0)
                return Rect.Empty;
            return new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        /// <summary>
        /// Bulk-select items (shape groups expanded). Empty input falls
        /// through to ClearSelection — WPF SelectItems parity.
        /// </summary>
        public void SelectItems(IEnumerable<InkStrokeData> strokes)
        {
            _selectedStrokes.Clear();
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
            RefreshSelectionAfterToggle();
        }

        /// <summary>Selects every ink stroke on the page (Ctrl+A).</summary>
        public void SelectAllAnnotations() => SelectItems(InkSurface.Store.Strokes);

        /// <summary>Clears the selection and its overlay visuals.</summary>
        public void ClearSelection()
        {
            _selectedStrokes.Clear();
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
            InkSurface.Store.NotifyGeometryChanged(restored);
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
                var bounds = StrokeGeometry.GetSelectionBounds(_selectedStrokes);
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
                RotateItemsDirectly(_selectedStrokes, delta, _rotateCenter);
            }
            else if (_isDraggingSelection)
            {
                var deltaX = pos.X - _dragStartPoint.X;
                var deltaY = pos.Y - _dragStartPoint.Y;
                _totalDragDeltaX += deltaX;
                _totalDragDeltaY += deltaY;
                MoveItemsDirectly(_selectedStrokes, deltaX, deltaY);
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
                        _lastResizeScale, _resizeAnchorPoint, _selectedStrokes.ToList()));
                _lastResizeScale = 1.0;
                _selectionInteractionSnapshot = null;
                return;
            }

            if (_isRotatingSelection)
            {
                _isRotatingSelection = false;
                if (Math.Abs(_totalRotationDegrees) > 0.5)
                    SelectionRotateCompleted?.Invoke(this, new SelectionRotateCompletedEventArgs(
                        _totalRotationDegrees, _rotateCenter, _selectedStrokes.ToList()));
                _lastRotationDegrees = 0;
                _totalRotationDegrees = 0;
                _selectionInteractionSnapshot = null;
                return;
            }

            if (_isDraggingSelection)
            {
                _isDraggingSelection = false;
                if (Math.Abs(_totalDragDeltaX) > 0.5 || Math.Abs(_totalDragDeltaY) > 0.5)
                    SelectionMoveCompleted?.Invoke(this, new SelectionMoveCompletedEventArgs(
                        _totalDragDeltaX, _totalDragDeltaY, _selectedStrokes.ToList()));
                _totalDragDeltaX = 0;
                _totalDragDeltaY = 0;
                _selectionInteractionSnapshot = null;
                return;
            }

            if (!_isSelecting)
                return;
            _isSelecting = false;

            _selectedStrokes.Clear();
            bool isClick = _selectionShape == SelectionShape.FreeForm
                ? _freeSelectionPoints == null || _freeSelectionPoints.Count <= 2
                : _selectionRect == null
                    || (_selectionRect.Width < 4 && _selectionRect.Height < 4);

            if (isClick)
            {
                // Topmost-first point hit (WPF click parity).
                if (_selectionFilter != SelectionFilter.TextOnly)
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

            if (_selectedStrokes.Count == 0)
            {
                SelectionOverlayCanvas.Children.Clear();
                StopSelectionDashTimer();
            }
            else
            {
                UpdateSelectionVisuals();
            }
        }

        private void HandleCtrlClickToggle(PointD point)
        {
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
            => MoveItemsDirectly(_selectedStrokes, deltaX, deltaY);

        public void MoveItemsDirectly(IReadOnlyList<InkStrokeData> strokes, double deltaX, double deltaY)
        {
            if (strokes == null || strokes.Count == 0)
                return;
            foreach (var stroke in strokes)
                StrokeGeometry.TranslateSpinePoints(stroke.Points, deltaX, deltaY);
            InkSurface.Store.NotifyGeometryChanged(strokes);
        }

        public void ScaleSelection(double scaleFactor, PointD center)
            => ScaleItemsDirectly(_selectedStrokes, scaleFactor, center);

        public void ScaleItemsDirectly(IReadOnlyList<InkStrokeData> strokes, double scaleFactor, PointD center)
        {
            if (strokes == null || strokes.Count == 0)
                return;
            foreach (var stroke in strokes)
            {
                StrokeGeometry.ScaleSpinePoints(stroke.Points, scaleFactor, center);
                stroke.Size *= scaleFactor;
            }
            InkSurface.Store.NotifyGeometryChanged(strokes);
        }

        public void RotateItemsDirectly(IReadOnlyList<InkStrokeData> strokes, double degrees, PointD center)
        {
            if (strokes == null || strokes.Count == 0 || Math.Abs(degrees) < 0.001)
                return;
            foreach (var stroke in strokes)
                StrokeGeometry.RotateSpinePoints(stroke.Points, degrees, center);
            InkSurface.Store.NotifyGeometryChanged(strokes);
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
            var boundsD = StrokeGeometry.GetSelectionBounds(_selectedStrokes);
            if (_selectedStrokes.Count == 0)
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

            // Per-item marching-ants outlines — each selected stroke keeps
            // its own dashed rect (WPF Task-6 behavior).
            _perItemOutlines.Clear();
            foreach (var stroke in _selectedStrokes)
            {
                var strokeBounds = StrokeGeometry.GetRenderedStrokeBounds(stroke).Inflated(3, 3);
                AddPerItemOutline(strokeBounds);
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
            var bounds = StrokeGeometry.GetSelectionBounds(_selectedStrokes);
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
        // Shape tool (Phase B)
        // ==================================================================

        private void Ink_ShapeDragStarted(object sender, ShapeDragEventArgs e)
        {
            _isShapeDragging = true;
            _shapeAnchor = e.Anchor;
            _shapeCurrent = e.Current;
            _shapeShiftHeld = e.ShiftHeld;
            ClearShapePreview();
        }

        private void Ink_ShapeDragUpdated(object sender, ShapeDragEventArgs e)
        {
            if (!_isShapeDragging)
                return;
            _shapeCurrent = e.Current;
            _shapeShiftHeld = e.ShiftHeld;
            UpdateShapePreview();
        }

        private void Ink_ShapeDragEnded(object sender, ShapeDragEventArgs e)
        {
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
            _isShapeDragging = false;
            ClearShapePreview();
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
