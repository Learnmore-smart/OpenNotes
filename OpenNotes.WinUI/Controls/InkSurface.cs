using System;
using System.Collections.Generic;
using System.Linq;
using Caelum.Ink;
using Caelum.InkGeometry;
using Caelum.Models;
using Caelum.Rendering;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Caelum.Controls;

/// <summary>
/// Which ink tool the surface currently applies. The editor's wider tool
/// enum maps onto this; <see cref="None"/> ignores input (the page-level
/// selection overlay handles Select itself).
/// </summary>
public enum InkSurfaceTool
{
    None,
    Pen,
    Highlighter,
    Eraser,
    /// <summary>Collects a stroke then commits it as a mask (Phase B).</summary>
    HiddenInk,
    /// <summary>Emits raw points for the ephemeral laser layer (Phase B).</summary>
    Laser,
    /// <summary>Drag-to-shape; the page owns preview + commit (Phase B).</summary>
    Shape,
    /// <summary>
    /// Drag-to-rect for the persistent area highlight (Task 8 Phase B).
    /// Rides the same drag event pipeline as <see cref="Shape"/> — the page
    /// routes by its input mode — but is NOT an ink-creation tool, so
    /// pen-only mode never blocks the mouse here (WPF AreaHighlight parity).
    /// </summary>
    AreaHighlight,
}

/// <summary>Payload for the laser-stroke events (raw page-DIP points).</summary>
public sealed class LaserStrokeEventArgs : EventArgs
{
    public LaserStrokeEventArgs(IReadOnlyList<PointD> points)
        => Points = points ?? Array.Empty<PointD>();

    /// <summary>New points since the last raise (page-DIP coordinates).</summary>
    public IReadOnlyList<PointD> Points { get; }
}

/// <summary>Payload for the shape-drag events (anchor/current + Shift).</summary>
public sealed class ShapeDragEventArgs : EventArgs
{
    public ShapeDragEventArgs(PointD anchor, PointD current, bool shiftHeld)
    {
        Anchor = anchor;
        Current = current;
        ShiftHeld = shiftHeld;
    }

    /// <summary>The pointer-down anchor point.</summary>
    public PointD Anchor { get; }
    /// <summary>The latest pointer position.</summary>
    public PointD Current { get; }
    /// <summary>Live Shift state — sampled per event like WPF's Keyboard.IsKeyDown.</summary>
    public bool ShiftHeld { get; }
}

/// <summary>Payload for <see cref="InkSurface.EraserPathUpdated"/>.</summary>
public sealed class EraserPathEventArgs : EventArgs
{
    public EraserPathEventArgs(IReadOnlyList<PointD> path, double eraserSize)
    {
        Path = path;
        EraserSize = eraserSize;
    }

    /// <summary>The swept path this update covered (page-DIP coordinates).</summary>
    public IReadOnlyList<PointD> Path { get; }
    public double EraserSize { get; }
}

/// <summary>
/// Net result of one erase gesture (pointer-down → up): the strokes the
/// eraser removed (whole or pre-split originals) and the fragments it
/// added back. One gesture = one undo payload, matching the WPF
/// StrokesErased contract.
/// </summary>
public sealed class InkStrokesErasedEventArgs : EventArgs
{
    public InkStrokesErasedEventArgs(
        IReadOnlyList<InkStrokePlacement> removedPlacements,
        IReadOnlyList<InkStrokePlacement> addedPlacements)
    {
        RemovedPlacements = removedPlacements ?? Array.Empty<InkStrokePlacement>();
        AddedPlacements = addedPlacements ?? Array.Empty<InkStrokePlacement>();
    }

    public IReadOnlyList<InkStrokePlacement> RemovedPlacements { get; }
    public IReadOnlyList<InkStrokePlacement> AddedPlacements { get; }
}

/// <summary>
/// Payload of <see cref="InkSurface.StrokeRecognized"/>: the shared stroke
/// token, the index the raw stroke occupied, and the Original/Ideal
/// snapshots — everything an <see cref="InkStrokeReplacedAction"/> needs.
/// Mirrors the WPF <c>StrokeRecognizedEventArgs</c> minus the fresh-stroke
/// flag (every surface raise IS a fresh stroke).
/// </summary>
public sealed class InkStrokeRecognizedEventArgs : EventArgs
{
    public InkStrokeRecognizedEventArgs(
        Guid token,
        int originalIndex,
        StrokeReplacementSnapshot originalSnapshot,
        StrokeReplacementSnapshot idealSnapshot)
    {
        Token = token;
        OriginalIndex = originalIndex;
        OriginalSnapshot = originalSnapshot;
        IdealSnapshot = idealSnapshot;
    }

    public Guid Token { get; }
    public int OriginalIndex { get; }
    public StrokeReplacementSnapshot OriginalSnapshot { get; }
    public StrokeReplacementSnapshot IdealSnapshot { get; }
}

/// <summary>
/// The WinUI custom ink surface — the Task-7 replacement for WPF's
/// InkCanvas input path. It is a <see cref="Canvas"/> subclass: persisted
/// strokes live as <see cref="Path"/> children in draw order (child i =
/// <see cref="Store"/> stroke i), and one extra transient child renders the
/// in-flight stroke. Pointer packets become <see cref="InkPointData"/> in
/// page-DIP coordinates (the surface fills the page grid 1:1, so element
/// coordinates ARE page coordinates at any zoom), pressure comes from
/// <see cref="PointerPointProperties.Pressure"/>, and erasing runs the
/// Core square-stamp splitter incrementally while the pointer moves —
/// the same visible-during-drag behavior as WPF.
///
/// Input arbitration (WPF parity):
/// - Pen draws/erases; mouse draws/erases unless <see cref="PenOnlyMode"/>
///   and an ink-creation tool is active (pen-only blocks non-pen INK, not
///   erasing — same as the WPF IsInkCreationModeActive gate).
/// - Touch is never ink input in Phase A (it pans the ScrollViewer) — but a
///   second pointer arriving DURING an ink gesture is marked handled so a
///   palm/touch can't pan the document mid-stroke.
/// - An inverted pen (<see cref="PointerPointProperties.IsEraser"/>) or a
///   held barrel button erases regardless of the active tool. The
///   draw-vs-erase decision is sampled once at pointer-down and held for
///   the whole gesture — a mid-gesture barrel/inversion change takes
///   effect on the NEXT stroke (WPF arbitrates the same way at
///   stroke-collect boundaries). The WPF double-barrel press-to-toggle
///   is not ported.
/// - <c>PointerCanceled</c> and <c>PointerCaptureLost</c> both roll the
///   in-flight gesture back through <see cref="CancelInteraction"/>; a
///   failed <c>CapturePointer</c> begins no gesture at all.
/// </summary>
public sealed partial class InkSurface : Canvas
{
    // ── Configuration (EditorPage pushes these via ApplySettings) ────────

    /// <summary>Active tool; <see cref="InkSurfaceTool.None"/> ignores input.</summary>
    public InkSurfaceTool Tool { get; set; } = InkSurfaceTool.None;

    /// <summary>Pen stroke colour (RGBA).</summary>
    public Color PenColor { get; set; } = Color.FromArgb(255, 0, 0, 0);

    /// <summary>Highlighter colour including its translucency (WPF: a=140).</summary>
    public Color HighlighterColor { get; set; } = Color.FromArgb(140, 255, 255, 0);

    public double PenSize { get; set; } = 1.5;

    /// <summary>Highlighter stroke width — matches the WPF 8.0 default.</summary>
    public double HighlighterSize { get; set; } = 8.0;
    public double EraserSize { get; set; } = 20.0;

    /// <summary>AppSettings.PenOnlyMode — blocks non-pen ink creation.</summary>
    public bool PenOnlyMode { get; set; }

    /// <summary>AppSettings.EnablePressure — off ⇒ IgnorePressure strokes.</summary>
    public bool EnablePressure { get; set; } = true;

    /// <summary>AppSettings.WholeStrokeEraser — delete vs split.</summary>
    public bool WholeStrokeEraser { get; set; }

    /// <summary>AppSettings.InkSimulation — velocity-based pressure synthesis.</summary>
    public bool InkSimulationEnabled { get; set; }

    /// <summary>
    /// AppSettings.ShapeRecognition — a completed non-highlighter scribble
    /// that classifies as a line/rectangle/ellipse is replaced by its ideal
    /// outline stroke in the store (same token, Ideal side) and reported
    /// through <see cref="StrokeRecognized"/> instead of
    /// <see cref="StrokeCollected"/>.
    /// </summary>
    public bool ShapeRecognitionEnabled { get; set; }

    /// <summary>AppSettings.StrokeSmoothing — 0=Off(raw), 1-3 moving average.</summary>
    public int StrokeSmoothingLevel { get; set; } = 2;

    /// <summary>Host-gated input (modal dialogs, inactive document).</summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>Hidden-ink mask colour (the opaque mask the tool draws).</summary>
    public Color HiddenInkColor { get; set; } = Color.FromArgb(255, 199, 205, 212);

    /// <summary>Hidden-ink mask stroke width (WPF HiddenInkSize = 28).</summary>
    public double HiddenInkSize { get; set; } = 28.0;

    /// <summary>Ruler snap distance in page DIPs (WPF ruler tolerance).</summary>
    public const double RulerSnapTolerance = 24.0;

    /// <summary>
    /// Phase B ruler hook: returns the ruler's long-edge endpoints in page-DIP
    /// coordinates, or null when the ruler overlay is hidden. Consulted once
    /// per completed pen/highlighter stroke — same post-collect timing as the
    /// WPF InkCanvas_StrokeCollected ruler snap.
    /// </summary>
    public Func<(PointD TopA, PointD TopB, PointD BottomA, PointD BottomB)?> RulerGeometryProvider { get; set; }

    /// <summary>
    /// The eraser cursor element (host-owned Ellipse in the same coordinate
    /// space). The surface moves/sizes it during erase gestures and hover —
    /// and restyles it as the brush cursor in pen/highlighter/hidden-ink
    /// modes (WPF UpdateBrushIndicatorStyle parity).
    /// </summary>
    public UIElement EraserIndicator { get; set; }

    // ── State ────────────────────────────────────────────────────────────

    /// <summary>The page's stroke collection (z-order = list order).</summary>
    public InkStrokeStore Store { get; } = new();

    private readonly Dictionary<InkStrokeData, Path> _strokeVisuals =
        new(ReferenceEqualityComparer.Instance);
    private Path _livePath;
    private InkStrokeData _liveStroke;

    private Caelum.Services.PenService _penService;
    private uint? _activePointerId;
    private bool _isDrawing;
    private bool _isErasing;
    private PointD? _lastErasePoint;

    private List<InkStrokePlacement> _eraseRemovedPlacements;
    private List<InkStrokePlacement> _eraseAddedPlacements;
    private List<InkStrokeData> _eraseRemovedStrokes;
    private List<InkStrokeData> _eraseAddedStrokes;

    // Phase B: shape drag + laser draw are surface gestures the PAGE renders
    // (preview on ShapePreviewCanvas, live polyline on LaserInkCanvas).
    private bool _isShapeDragging;
    private PointD _shapeAnchor;
    private bool _isLaserDrawing;
    private PointD? _lastLaserPoint;

    // ── Events (EditorPage turns these into undo actions + dirty) ────────

    /// <summary>A completed pen/highlighter stroke entered the store.</summary>
    public event EventHandler<InkStrokeData> StrokeCollected;

    /// <summary>
    /// A collected pen stroke was recognized as a shape and replaced by its
    /// ideal outline inside the store — the editor pushes the
    /// <see cref="InkStrokeReplacedAction"/> undo action (undo restores the
    /// user's raw stroke). Raised instead of <see cref="StrokeCollected"/>.
    /// </summary>
    public event EventHandler<InkStrokeRecognizedEventArgs> StrokeRecognized;

    /// <summary>An erase gesture completed with net removals/additions.</summary>
    public event EventHandler<InkStrokesErasedEventArgs> StrokesErased;

    /// <summary>
    /// A hidden-ink stroke completed (Tool=<see cref="InkSurfaceTool.HiddenInk"/>):
    /// raw collected spine, release point included. The page converts it to a
    /// <see cref="HiddenInkAnnotation"/> mask — the stroke never enters the
    /// store, matching WPF CommitHiddenInkStroke which removes the temporary
    /// ink stroke and keeps only the mask.
    /// </summary>
    public event EventHandler<IReadOnlyList<PointD>> HiddenInkStrokeCommitted;

    /// <summary>Laser drag began (first point, page-DIP).</summary>
    public event EventHandler<LaserStrokeEventArgs> LaserStrokeStarted;

    /// <summary>Laser drag moved — batched new points since the last raise.</summary>
    public event EventHandler<LaserStrokeEventArgs> LaserStrokePointsAppended;

    /// <summary>Laser drag completed (final release point).</summary>
    public event EventHandler<LaserStrokeEventArgs> LaserStrokeCompleted;

    /// <summary>Shape drag began — <see cref="ShapeDragEventArgs.Anchor"/>.</summary>
    public event EventHandler<ShapeDragEventArgs> ShapeDragStarted;

    /// <summary>Shape drag moved — live preview should follow Current/Shift.</summary>
    public event EventHandler<ShapeDragEventArgs> ShapeDragUpdated;

    /// <summary>Shape drag ended — the page commits the shape strokes.</summary>
    public event EventHandler<ShapeDragEventArgs> ShapeDragEnded;

    /// <summary>Shape drag cancelled (capture lost / tool switch).</summary>
    public event EventHandler ShapeDragCancelled;

    /// <summary>
    /// One eraser update swept <see cref="EraserPathEventArgs.Path"/> — raised
    /// on every erase step (press stamp and each move batch) so the page can
    /// hit-test its non-stroke layers (hidden-ink masks) against the same
    /// footprint. Fires even when the stroke store is empty.
    /// </summary>
    public event EventHandler<EraserPathEventArgs> EraserPathUpdated;

    /// <summary>An erase gesture finished — after <see cref="StrokesErased"/>.</summary>
    public event EventHandler EraseGestureCompleted;

    /// <summary>
    /// An erase gesture was cancelled AFTER it already mutated the store —
    /// the strokes were restored; the page restores any hidden-ink masks it
    /// removed during the gesture (WPF CancelSelectionGesture parity).
    /// </summary>
    public event EventHandler EraseGestureCancelled;

    /// <summary>
    /// Any visible ink change (drawn stroke, erase mutation, quiet add or
    /// remove — including undo/redo restores). Hosts invalidate thumbnails.
    /// </summary>
    public event EventHandler InkMutated;

    public InkSurface()
    {
        // A Canvas without a background never receives pointer input.
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Store.Mutated += Store_Mutated;

        // WinUI 3 exposes pointer input only through routed events (the
        // UWP-style OnPointer* overrides do not exist).
        PointerPressed += InkSurface_PointerPressed;
        PointerMoved += InkSurface_PointerMoved;
        PointerReleased += InkSurface_PointerReleased;
        PointerCanceled += InkSurface_PointerCanceled;
        PointerCaptureLost += InkSurface_PointerCaptureLost;
        PointerExited += InkSurface_PointerExited;
    }

    // ── Public surface API ───────────────────────────────────────────────

    /// <summary>
    /// Quiet load path: appends a stroke from a sidecar record without
    /// raising StrokeCollected (no undo action, no dirty — mirroring the
    /// WPF loader which adds loaded strokes outside history).
    /// </summary>
    public InkStrokeData AddStroke(StrokeAnnotation annotation)
    {
        var stroke = InkStrokeData.FromAnnotation(annotation);
        if (stroke.Points.Count == 0)
            return null;
        // WPF AddStroke parity: a stroke belonging to a logical shape group
        // renders uniform-width (the shape tool commits IgnorePressure=true;
        // the flag never serializes, so the load path re-derives it).
        if (!string.IsNullOrWhiteSpace(stroke.ShapeGroupId))
            stroke.IgnorePressure = true;
        Store.AddStrokeQuiet(stroke);
        return stroke;
    }

    /// <summary>
    /// Cancels any in-flight gesture: a live stroke is discarded, an erase
    /// gesture is rolled back. Safe to call at any time.
    /// </summary>
    public void CancelInteraction()
    {
        if (_isDrawing)
            DiscardLiveStroke();
        if (_isErasing || HasPendingEraseGesture())
            CancelEraseGesture();
        if (_isShapeDragging)
        {
            _isShapeDragging = false;
            ShapeDragCancelled?.Invoke(this, EventArgs.Empty);
        }
        if (_isLaserDrawing)
        {
            // Treat a cancelled laser drag like a release — the page fades
            // the live polyline instead of orphaning it (WPF leaves it; the
            // fade-removal path is strictly better and keeps the layer clean).
            _isLaserDrawing = false;
            LaserStrokeCompleted?.Invoke(this,
                new LaserStrokeEventArgs(
                    _lastLaserPoint.HasValue ? new[] { _lastLaserPoint.Value } : Array.Empty<PointD>()));
        }
        ReleaseActivePointer();
        HideEraserIndicator();
    }

    /// <summary>
    /// Shared pen service — the surface feeds it pen packets for capability
    /// probing (WPF SetPenService parity). It deliberately does NOT sync
    /// <see cref="EnablePressure"/> from <c>service.PressureEnabled</c>: that
    /// flag is never written back from settings, so syncing it would clobber
    /// the real value — <c>EditorPage.ApplyToolToAllPages</c> owns
    /// <see cref="EnablePressure"/> from <c>AppSettings.EnablePressure</c>.
    /// </summary>
    public void SetPenService(Caelum.Services.PenService service)
    {
        _penService = service;
    }

    /// <summary>Positions and shows the eraser indicator over a page point.</summary>
    private void ShowEraserIndicatorAt(PointD pagePoint)
    {
        var indicator = EraserIndicator;
        if (indicator == null || !IsFinite(pagePoint))
            return;
        indicator.Visibility = Visibility.Visible;
        if (indicator is FrameworkElement fe)
        {
            fe.Width = EraserSize;
            fe.Height = EraserSize;
            // Restore the eraser styling — the brush indicator may have
            // overridden Stroke/Fill during an inking-mode hover.
            if (fe is Microsoft.UI.Xaml.Shapes.Ellipse ellipse)
            {
                ellipse.Stroke = ResolveThemeBrush("ThemeAccentBrush",
                    Color.FromArgb(255, 37, 99, 235));
                ellipse.Fill = ResolveThemeBrush("ThemeSelectionBrush",
                    Color.FromArgb(60, 37, 99, 235));
            }
        }
        SetLeft(indicator, pagePoint.X - EraserSize / 2.0);
        SetTop(indicator, pagePoint.Y - EraserSize / 2.0);
    }

    private void HideEraserIndicator()
    {
        if (EraserIndicator != null)
            EraserIndicator.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Brush-style indicator — WPF UpdateBrushIndicatorStyle parity: eraser
    /// mode shows the eraser ring; inking modes show a ring sized to the
    /// stroke (hidden-ink uses the mask size and a translucent fill).
    /// </summary>
    private void ShowBrushIndicatorAt(PointD pagePoint)
    {
        var indicator = EraserIndicator;
        if (indicator == null || !IsFinite(pagePoint))
            return;

        bool hiddenInk = Tool == InkSurfaceTool.HiddenInk;
        bool highlighter = Tool == InkSurfaceTool.Highlighter;
        double size = hiddenInk ? HiddenInkSize : highlighter ? HighlighterSize : PenSize;
        Color c = hiddenInk ? HiddenInkColor : highlighter ? HighlighterColor : PenColor;

        indicator.Visibility = Visibility.Visible;
        if (indicator is Microsoft.UI.Xaml.Shapes.Ellipse ellipse)
        {
            ellipse.Width = Math.Max(4, size);
            ellipse.Height = Math.Max(4, size);
            ellipse.Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Color.FromArgb(200, c.R, c.G, c.B));
            ellipse.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Color.FromArgb(
                    hiddenInk ? (byte)90 : highlighter ? (byte)50 : (byte)0,
                    c.R, c.G, c.B));
            size = ellipse.Width;
        }
        else if (indicator is FrameworkElement fe)
        {
            fe.Width = Math.Max(4, size);
            fe.Height = Math.Max(4, size);
            size = fe.Width;
        }
        SetLeft(indicator, pagePoint.X - size / 2.0);
        SetTop(indicator, pagePoint.Y - size / 2.0);
    }

    /// <summary>Theme-brush lookup (Application.Resources resolves the active dictionary).</summary>
    private static Microsoft.UI.Xaml.Media.Brush ResolveThemeBrush(string key, Color fallback)
    {
        if (Application.Current?.Resources?.TryGetValue(key, out var value) == true
            && value is Microsoft.UI.Xaml.Media.Brush brush)
            return brush;
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(fallback);
    }

    // ── Pointer pipeline ─────────────────────────────────────────────────

    private void InkSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!InputEnabled)
            return;

        if (_activePointerId.HasValue)
        {
            // A second pointer (palm touch, second pen, another mouse) during
            // an active ink gesture must not bubble to the ScrollViewer and
            // pan the document mid-stroke — swallow it. WPF never sees this
            // case: InkCanvas owns the stylus capture exclusively.
            e.Handled = true;
            return;
        }

        var point = e.GetCurrentPoint(this);
        var device = point.PointerDeviceType;
        var props = point.Properties;

        bool isPen = device == PointerDeviceType.Pen;
        bool isMouse = device == PointerDeviceType.Mouse;
        if (!isPen && !isMouse)
            return; // touch (and anything else) is never ink input in Phase A

        if (isPen)
        {
            _penService?.ProbePointer(point);
            if (props.IsBarrelButtonPressed)
                _penService?.NoteBarrelButton();
        }

        bool invertedEraser = isPen && props.IsEraser;
        bool barrelErase = isPen && props.IsBarrelButtonPressed;
        bool wantsErase = Tool == InkSurfaceTool.Eraser || invertedEraser || barrelErase;

        // WPF parity: pen-only blocks non-pen INK creation — Inking and Shape
        // are ink-creation modes (IsPenOnlyInkCreationMode), hidden-ink/laser/
        // eraser are not, so a mouse still draws masks, laser lines and erases
        // under pen-only.
        bool inkCreation = !wantsErase
            && (Tool == InkSurfaceTool.Pen
                || Tool == InkSurfaceTool.Highlighter
                || Tool == InkSurfaceTool.Shape);
        if (PenOnlyMode && inkCreation && !isPen)
            return;

        bool wantsDraw = !wantsErase
            && (Tool == InkSurfaceTool.Pen
                || Tool == InkSurfaceTool.Highlighter
                || Tool == InkSurfaceTool.HiddenInk);
        // Area-highlight drags reuse the shape-drag pipeline — the page
        // routes by input mode. It is deliberately NOT in inkCreation above:
        // WPF lets the mouse draw an area highlight even under pen-only.
        bool wantsShape = !wantsErase
            && (Tool == InkSurfaceTool.Shape || Tool == InkSurfaceTool.AreaHighlight);
        bool wantsLaser = !wantsErase && Tool == InkSurfaceTool.Laser;
        if (!wantsErase && !wantsDraw && !wantsShape && !wantsLaser)
            return; // Tool == None — input belongs to the selection overlay

        // A non-finite press point can't seed a gesture — it would ride
        // into XAML geometry and the saved stroke. Swallow like any other
        // rejected press (T14-A).
        if (!IsFinite(point.Position))
        {
            e.Handled = true;
            return;
        }

        // Capture failure means moves/releases for this pointer may never
        // reach us — do not begin an untracked gesture (and keep the press
        // handled so it can't start a ScrollViewer pan either).
        if (!CapturePointer(e.Pointer))
        {
            e.Handled = true;
            return;
        }
        _activePointerId = e.Pointer.PointerId;

        if (wantsErase)
        {
            BeginEraseGesture();
            _isErasing = true;
            ShowEraserIndicatorAt(ToPointD(point.Position));
            if (EraseAtPoint(ToPointD(point.Position)))
                InkMutated?.Invoke(this, EventArgs.Empty);
        }
        else if (wantsShape)
        {
            _isShapeDragging = true;
            _shapeAnchor = ToPointD(point.Position);
            ShapeDragStarted?.Invoke(this,
                new ShapeDragEventArgs(_shapeAnchor, _shapeAnchor, IsShiftHeld(e)));
        }
        else if (wantsLaser)
        {
            _isLaserDrawing = true;
            var start = ToPointD(point.Position);
            _lastLaserPoint = start;
            LaserStrokeStarted?.Invoke(this, new LaserStrokeEventArgs(new[] { start }));
        }
        else
        {
            BeginStroke(point);
        }
        e.Handled = true;
    }

    private static bool IsShiftHeld(PointerRoutedEventArgs e) =>
        (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Shift) != 0;

    private void InkSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var current = e.GetCurrentPoint(this);
        var device = current.PointerDeviceType;
        bool eraserish = Tool == InkSurfaceTool.Eraser
            || (device == PointerDeviceType.Pen
                && (current.Properties.IsEraser || current.Properties.IsBarrelButtonPressed));
        bool brushHover = Tool == InkSurfaceTool.Pen
            || Tool == InkSurfaceTool.Highlighter
            || Tool == InkSurfaceTool.HiddenInk;

        // Cursor indicator follows the cursor even without contact (WPF
        // UpdateBrushIndicatorStyle parity: eraser ring while erasing, brush
        // ring while hovering in an ink mode). Input-gated surfaces (inactive
        // tab, modal block) show nothing — CancelInteraction already hid the
        // ring when the gate closed.
        if (InputEnabled && !_isDrawing && !_isErasing && !_isShapeDragging && !_isLaserDrawing)
        {
            if (eraserish)
                ShowEraserIndicatorAt(ToPointD(current.Position));
            else if (brushHover)
                ShowBrushIndicatorAt(ToPointD(current.Position));
        }

        if (_activePointerId == null || e.Pointer.PointerId != _activePointerId.Value)
            return;

        if (_isShapeDragging)
        {
            ShapeDragUpdated?.Invoke(this, new ShapeDragEventArgs(
                _shapeAnchor, ToPointD(current.Position), IsShiftHeld(e)));
            e.Handled = true;
            return;
        }

        if (_isLaserDrawing)
        {
            // Same batch semantics as the draw path — intermediate packets
            // plus the current point when it advanced.
            var batch = new List<PointD>();
            foreach (var p in e.GetIntermediatePoints(this))
            {
                var d = ToPointD(p.Position);
                if (!_lastLaserPoint.HasValue
                    || Math.Abs(_lastLaserPoint.Value.X - d.X) > 0.0001
                    || Math.Abs(_lastLaserPoint.Value.Y - d.Y) > 0.0001)
                {
                    batch.Add(d);
                    _lastLaserPoint = d;
                }
            }
            var tail = ToPointD(current.Position);
            if (!_lastLaserPoint.HasValue
                || Math.Abs(_lastLaserPoint.Value.X - tail.X) > 0.0001
                || Math.Abs(_lastLaserPoint.Value.Y - tail.Y) > 0.0001)
            {
                batch.Add(tail);
                _lastLaserPoint = tail;
            }
            if (batch.Count > 0)
                LaserStrokePointsAppended?.Invoke(this, new LaserStrokeEventArgs(batch));
            e.Handled = true;
            return;
        }

        if (_isErasing)
        {
            // One routed event can carry a whole packet batch — the batch
            // is erased in a single pass (one swept footprint over all
            // intermediate points, one candidate scan) and the side effects
            // coalesce: the indicator lands once on the freshest position
            // and InkMutated fires once per event, not per packet.
            var intermediates = e.GetIntermediatePoints(this);
            var batch = new List<PointD>(intermediates.Count + 1);
            foreach (var p in intermediates)
                batch.Add(ToPointD(p.Position));
            // GetIntermediatePoints may exclude the current point — append
            // it past the same dedup the draw path applies so the eraser
            // reaches the freshest position too.
            var currentPoint = ToPointD(current.Position);
            if (batch.Count == 0
                || Math.Abs(batch[batch.Count - 1].X - currentPoint.X) > 0.0001
                || Math.Abs(batch[batch.Count - 1].Y - currentPoint.Y) > 0.0001)
            {
                batch.Add(currentPoint);
            }
            ShowEraserIndicatorAt(batch[batch.Count - 1]);
            if (EraseAlongPoints(batch))
                InkMutated?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (_isDrawing && _liveStroke != null)
        {
            // Intermediate points carry the full packet batch — one event can
            // deliver many samples at high report rates.
            foreach (var p in e.GetIntermediatePoints(this))
            {
                if (!IsFinite(p.Position))
                    continue; // a bad packet must not poison the outline or saved ink
                float pressure = EffectivePacketPressure(p);
                _liveStroke.Points.Add(new InkPointData(
                    p.Position.X, p.Position.Y, pressure));
            }
            // Also append the current point if it advanced past the last
            // intermediate (GetIntermediatePoints may exclude it).
            var tail = _liveStroke.Points[^1];
            if (IsFinite(current.Position)
                && (Math.Abs(tail.X - current.Position.X) > 0.0001
                    || Math.Abs(tail.Y - current.Position.Y) > 0.0001))
            {
                _liveStroke.Points.Add(new InkPointData(
                    current.Position.X, current.Position.Y, EffectivePacketPressure(current)));
            }
            StrokeRenderer.UpdateStrokePath(_livePath, _liveStroke);
            e.Handled = true;
        }
    }

    private void InkSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId == null || e.Pointer.PointerId != _activePointerId.Value)
            return;

        var point = e.GetCurrentPoint(this);
        if (_isShapeDragging)
        {
            _isShapeDragging = false;
            ShapeDragEnded?.Invoke(this, new ShapeDragEventArgs(
                _shapeAnchor, ToPointD(point.Position), IsShiftHeld(e)));
            ReleaseActivePointer();
            e.Handled = true;
            return;
        }

        if (_isLaserDrawing)
        {
            _isLaserDrawing = false;
            var end = ToPointD(point.Position);
            LaserStrokeCompleted?.Invoke(this, new LaserStrokeEventArgs(new[] { end }));
            ReleaseActivePointer();
            e.Handled = true;
            return;
        }

        if (_isErasing)
        {
            if (EraseAtPoint(ToPointD(point.Position)))
                InkMutated?.Invoke(this, EventArgs.Empty);
            _isErasing = false;
            ReleaseActivePointer();
            EndEraseGesture();

            // If the pen is still inverted (hovering after lift-off) or the
            // eraser tool is armed, keep showing the indicator — WPF parity.
            bool stillEraserish =
                Tool == InkSurfaceTool.Eraser
                || (point.PointerDeviceType == PointerDeviceType.Pen
                    && (point.Properties.IsEraser || point.Properties.IsBarrelButtonPressed));
            if (!stillEraserish)
                HideEraserIndicator();
        }
        else if (_isDrawing)
        {
            // WPF parity: Shift is sampled at stylus-up (the collect
            // boundary), not at pointer-down — mid-stroke presses count.
            CompleteStroke(
                point,
                (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Shift) != 0);
            ReleaseActivePointer();
        }
        e.Handled = true;
    }

    /// <summary>
    /// The OS cancelled the active pointer (pen out of range, touch
    /// pre-empted by a system gesture, device lost). Treat it like a capture
    /// loss: roll the in-flight gesture back rather than leave a
    /// half-committed erase or a stuck live stroke.
    /// </summary>
    private void InkSurface_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId == null || e.Pointer.PointerId != _activePointerId.Value)
            return;
        CancelInteraction();
        e.Handled = true;
    }

    private void InkSurface_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId == null || e.Pointer.PointerId != _activePointerId.Value)
            return;
        if (_isDrawing)
            DiscardLiveStroke();
        if (_isErasing || HasPendingEraseGesture())
            CancelEraseGesture();
        if (_isShapeDragging)
        {
            _isShapeDragging = false;
            ShapeDragCancelled?.Invoke(this, EventArgs.Empty);
        }
        if (_isLaserDrawing)
        {
            _isLaserDrawing = false;
            LaserStrokeCompleted?.Invoke(this,
                new LaserStrokeEventArgs(
                    _lastLaserPoint.HasValue ? new[] { _lastLaserPoint.Value } : Array.Empty<PointD>()));
        }
        ReleaseActivePointer();
        HideEraserIndicator();
    }

    private void InkSurface_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId == null)
            HideEraserIndicator();
    }

    private void ReleaseActivePointer()
    {
        _activePointerId = null;
        _isDrawing = false;
        _isErasing = false;
        _isShapeDragging = false;
        _isLaserDrawing = false;
        _lastLaserPoint = null;
    }

    /// <summary>
    /// Packet pressure: real digitizer pressure when the setting is on, the
    /// WPF uniform 0.5 fallback otherwise (mouse also reports ~0.5).
    /// </summary>
    private float EffectivePacketPressure(PointerPoint point)
    {
        float p = point.Properties.Pressure;
        if (!float.IsFinite(p) || p <= 0f)
            p = 0.5f;
        if (p > 1f) p = 1f;
        return p;
    }

    private static PointD ToPointD(Point p) => new(p.X, p.Y);

    // T14-A: a non-finite coordinate reaching Polyline.Points/Canvas.Set* or
    // saved stroke data throws ArgumentException (E_INVALIDARG) on the UI
    // thread — drop it at ingest instead of letting it into geometry.
    private static bool IsFinite(PointD p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
    private static bool IsFinite(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

    // ── Drawing ──────────────────────────────────────────────────────────

    private void BeginStroke(PointerPoint point)
    {
        bool highlighter = Tool == InkSurfaceTool.Highlighter;
        var color = highlighter ? HighlighterColor : PenColor;
        _liveStroke = new InkStrokeData
        {
            Points = new List<InkPointData>
            {
                new(point.Position.X, point.Position.Y, EffectivePacketPressure(point))
            },
            R = color.R,
            G = color.G,
            B = color.B,
            A = color.A,
            Size = highlighter ? HighlighterSize : PenSize,
            IsHighlighter = highlighter,
            FitToCurve = true, // final value decided by the smoothing level at commit
            IgnorePressure = !EnablePressure,
        };
        _livePath = StrokeRenderer.CreateStrokePath(_liveStroke);
        Children.Add(_livePath);
        _isDrawing = true;
    }

    /// <summary>
    /// Commits the live stroke through the WPF post-collection pipeline —
    /// in WPF <c>InkCanvas_StrokeCollected</c> order: the release packet is
    /// appended, Shift-at-release straightens, smoothing runs, then shape
    /// recognition (a hit replaces the stroke in the store and raises
    /// <see cref="StrokeRecognized"/>), then ink simulation, and the
    /// survivor is added to the store quietly before
    /// <see cref="StrokeCollected"/> fires — the editor pushes the undo
    /// action. The store mutation already rendered the stroke; the
    /// transient live path is removed.
    /// </summary>
    private void CompleteStroke(PointerPoint point, bool shiftHeld)
    {
        var stroke = _liveStroke;
        var path = _livePath;
        _liveStroke = null;
        _livePath = null;
        _isDrawing = false;

        if (path != null)
            Children.Remove(path);

        if (stroke == null || stroke.Points.Count == 0)
            return;

        // The release packet is part of the stroke — WPF InkCanvas includes
        // the stylus-up point in StylusPoints. Append it past the same
        // dedup threshold the move handler applies.
        var tail = stroke.Points[^1];
        if (IsFinite(point.Position)
            && (Math.Abs(tail.X - point.Position.X) > 0.0001
                || Math.Abs(tail.Y - point.Position.Y) > 0.0001))
        {
            stroke.Points.Add(new InkPointData(
                point.Position.X, point.Position.Y, EffectivePacketPressure(point)));
        }

        // WPF PreserveTapStroke expands a single-point tap so the stroke
        // renders; our outline already draws a 1-point disc, so the point
        // list stays truthful instead.

        // Phase B — hidden ink: the collected stroke is converted to a mask
        // and NEVER enters the store (WPF removes the temporary ink stroke
        // in CommitHiddenInkStroke). No smoothing/recognition/simulation —
        // the mask keeps the raw collected spine.
        if (Tool == InkSurfaceTool.HiddenInk)
        {
            var maskPoints = stroke.Points
                .Select(p => new PointD(p.X, p.Y))
                .ToList();
            HiddenInkStrokeCommitted?.Invoke(this, maskPoints);
            return;
        }

        // Phase B — ruler constraint: snaps/clips the collected stroke to the
        // ruler's nearest long edge, exactly where the WPF
        // InkCanvas_StrokeCollected applies it (before shift-straighten and
        // smoothing). A null result = the stroke started inside or entered
        // the ruler body — WPF drops it silently (e.Handled, no undo entry).
        var ruler = RulerGeometryProvider?.Invoke();
        if (ruler.HasValue
            && (Tool == InkSurfaceTool.Pen || Tool == InkSurfaceTool.Highlighter))
        {
            var constrained = StrokeGeometry.ConstrainPointsToRuler(
                stroke.Points, ruler.Value.TopA, ruler.Value.TopB,
                ruler.Value.BottomA, ruler.Value.BottomB, RulerSnapTolerance);
            if (constrained == null || constrained.Count == 0)
                return; // dropped — never committed
            if (!ReferenceEquals(constrained, stroke.Points))
                stroke.Points = new List<InkPointData>(constrained);
        }

        // Task 21 parity: Shift sampled at stylus-up straightens to a
        // first→last segment (FitToCurve off — a line needs no curve fit).
        if (shiftHeld && stroke.Points.Count >= 2
            && StrokeGeometry.TryGetStraightEndpoints(stroke.Points, out var first, out var last))
        {
            stroke.Points = new List<InkPointData> { first, last };
            stroke.FitToCurve = false;
        }

        // Task 24 parity: smoothing levels 1-3 moving-average the spine and
        // keep FitToCurve on; level 0 keeps raw points but renders raw.
        if (stroke.Points.Count >= 3 && StrokeSmoothingLevel > 0)
        {
            var smoothed = StrokeGeometry.SmoothPoints(stroke.Points, StrokeSmoothingLevel);
            if (smoothed != null)
            {
                stroke.Points = new List<InkPointData>(smoothed);
                stroke.FitToCurve = true;
            }
        }
        else if (StrokeSmoothingLevel <= 0)
        {
            stroke.FitToCurve = false;
        }

        // Shape recognition runs before ink simulation (WPF ordering): a
        // recognized stroke is replaced wholesale by its ideal outline —
        // uniform width — so simulating pressure on the raw stroke would be
        // wasted. The helper adds the stroke to the store and swaps it in
        // place; a hit raises StrokeRecognized (the editor pushes
        // InkStrokeReplacedAction so undo restores the raw scribble) and
        // skips StrokeCollected entirely.
        //
        // Event order note: StrokeRecognized fires BEFORE InkMutated — the
        // undo action must land on the editor's stack before the trailing
        // "visuals changed" invalidate, matching the StrokeCollected path
        // below (collect → undo push → InkMutated last). InkMutated is
        // always the final signal on every commit path.
        if (ShapeRecognitionEnabled && !stroke.IsHighlighter
            && stroke.Points.Count >= StrokeGeometry.MinRecognizedShapePoints
            && ScribbleShapeRecognition.TryReplaceWithRecognizedStroke(
                Store, stroke, out var replacement))
        {
            StrokeRecognized?.Invoke(this, new InkStrokeRecognizedEventArgs(
                replacement.Token,
                replacement.OriginalIndex,
                replacement.OriginalSnapshot,
                replacement.IdealSnapshot));
            InkMutated?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Ink simulation synthesizes pressure from spacing; pen strokes only.
        if (InkSimulationEnabled && !stroke.IsHighlighter && stroke.Points.Count >= 3)
        {
            var simulated = StrokeGeometry.SimulateInkFlow(stroke.Points);
            if (simulated != null)
                stroke.Points = new List<InkPointData>(simulated);
        }

        Store.AddStrokeQuiet(stroke);
        StrokeCollected?.Invoke(this, stroke);
        InkMutated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops the in-flight stroke without touching the store.</summary>
    private void DiscardLiveStroke()
    {
        _isDrawing = false;
        if (_livePath != null)
            Children.Remove(_livePath);
        _livePath = null;
        _liveStroke = null;
    }

    // ── Erasing ──────────────────────────────────────────────────────────

    private void BeginEraseGesture()
    {
        // A new gesture must never inherit a cancelled gesture's payload.
        _lastErasePoint = null;
        _eraseRemovedStrokes = null;
        _eraseAddedStrokes = null;
        _eraseRemovedPlacements = null;
        _eraseAddedPlacements = null;
    }

    private bool HasPendingEraseGesture()
        => (_eraseRemovedStrokes?.Count ?? 0) > 0 || (_eraseAddedStrokes?.Count ?? 0) > 0;

    /// <summary>
    /// Applies one eraser update: the stamp at <paramref name="point"/> plus
    /// the swept segment from the previous update point. Splits are applied
    /// to the live store immediately (visible-during-drag) while the net
    /// removed/added placements accumulate into the gesture payload.
    /// Returns whether any stroke was mutated — the caller coalesces
    /// <see cref="InkMutated"/> into one raise per pointer event rather than
    /// per intermediate packet.
    /// </summary>
    private bool EraseAtPoint(PointD point)
    {
        var path = new List<PointD>(2);
        if (_lastErasePoint.HasValue)
            path.Add(_lastErasePoint.Value);
        path.Add(point);
        _lastErasePoint = point;
        return EraseAlongPath(path);
    }

    /// <summary>
    /// Batch variant of <see cref="EraseAtPoint"/> for a routed event's whole
    /// intermediate-packet batch: the swept path chains from the previous
    /// update point through every intermediate point, ONE footprint covers
    /// the batch, and every candidate stroke is evaluated once — not once
    /// per packet. Returns whether any stroke was mutated.
    /// </summary>
    private bool EraseAlongPoints(IReadOnlyList<PointD> points)
    {
        if (points == null || points.Count == 0)
            return false;
        var path = new List<PointD>(points.Count + 1);
        if (_lastErasePoint.HasValue)
            path.Add(_lastErasePoint.Value);
        path.AddRange(points);
        _lastErasePoint = points[points.Count - 1];
        return EraseAlongPath(path);
    }

    /// <summary>
    /// The shared erase pass: builds the swept square-stamp footprint for
    /// <paramref name="path"/> ONCE and evaluates every candidate stroke
    /// against it (the piece-taking Core overloads keep each stroke from
    /// rebuilding the stamps).
    /// </summary>
    private bool EraseAlongPath(IReadOnlyList<PointD> path)
    {
        // Non-stroke layers (hidden-ink masks) track the same footprint —
        // raise even when the stroke store is empty.
        EraserPathUpdated?.Invoke(this, new EraserPathEventArgs(path, EraserSize));

        if (Store.Count == 0)
            return false;

        // The swept square-stamp footprint is identical for every candidate
        // stroke — build it ONCE per update instead of letting each
        // EraserHitsStroke/SplitStrokeAtEraser call rebuild it.
        var footprintPieces = StrokeGeometry.BuildEraserFootprint(path, EraserSize);

        // Candidate prefilter: rendered spine bounds must overlap the stamp
        // footprint — mirrors the WPF rect-union prefilter before HitTest.
        // Spine bounds inflate by the stroke's max half-width (size·0.875 at
        // p=1.0; size is a safe upper bound).
        var footprint = StrokeGeometry.GetBounds(path, EraserSize * 0.5);
        var candidates = Store.Strokes
            .Where(s => StrokeGeometry.GetSpineBounds(s.Points, s.Size).IntersectsWith(footprint))
            .ToList();

        bool mutated = false;
        foreach (var stroke in candidates)
        {
            if (WholeStrokeEraser)
            {
                if (!StrokeGeometry.EraserHitsStroke(
                        stroke.Points, stroke.Size, stroke.IgnorePressure, footprintPieces))
                    continue;
                ApplyErasedStroke(stroke, new List<InkStrokeData>());
                mutated = true;
                continue;
            }

            var fragments = StrokeGeometry.SplitStrokeAtEraser(
                stroke.Points, stroke.Size, stroke.IgnorePressure, footprintPieces);
            // A miss returns a copy of the input — compare coordinates to
            // detect the no-op (GetEraseResult returns the same reference;
            // the Core splitter always allocates).
            if (fragments.Count == 1 && FragmentsEqual(fragments[0], stroke.Points))
                continue;

            var replacements = fragments
                .Select(f => CloneFragment(stroke, f))
                .ToList();
            ApplyErasedStroke(stroke, replacements);
            mutated = true;
        }

        return mutated;
    }

    /// <summary>A split fragment inherits every stroke property.</summary>
    private static InkStrokeData CloneFragment(InkStrokeData source, List<InkPointData> points)
    {
        var clone = source.Clone();
        clone.Points = points;
        return clone;
    }

    private static bool FragmentsEqual(List<InkPointData> a, List<InkPointData> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].X != b[i].X || a[i].Y != b[i].Y)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Applies one erase modification (remove original, add fragments) while
    /// accumulating the net gesture payload — a fragment re-clipped later in
    /// the same gesture cancels out, exactly like the WPF implementation.
    /// </summary>
    private void ApplyErasedStroke(InkStrokeData removedStroke, List<InkStrokeData> addedStrokes)
    {
        _eraseRemovedStrokes ??= new List<InkStrokeData>();
        _eraseAddedStrokes ??= new List<InkStrokeData>();
        _eraseRemovedPlacements ??= new List<InkStrokePlacement>();
        _eraseAddedPlacements ??= new List<InkStrokePlacement>();

        int addedIndex = _eraseAddedStrokes.IndexOf(removedStroke);
        if (addedIndex >= 0)
        {
            _eraseAddedStrokes.RemoveAt(addedIndex);
            _eraseAddedPlacements.RemoveAt(addedIndex);
        }
        else
        {
            _eraseRemovedStrokes.Add(removedStroke);
            _eraseRemovedPlacements.Add(Store.CaptureStrokePlacement(removedStroke));
        }

        Store.RemoveStrokeQuiet(removedStroke);
        foreach (var fragment in addedStrokes)
        {
            var placement = Store.AddStrokeQuiet(fragment);
            _eraseAddedStrokes.Add(fragment);
            _eraseAddedPlacements.Add(placement);
        }
    }

    /// <summary>
    /// Closes the gesture and raises <see cref="StrokesErased"/> with the
    /// net placements — one undo action per gesture.
    /// </summary>
    private void EndEraseGesture()
    {
        _lastErasePoint = null;
        var removed = _eraseRemovedPlacements;
        var added = _eraseAddedPlacements;
        _eraseRemovedStrokes = null;
        _eraseAddedStrokes = null;
        _eraseRemovedPlacements = null;
        _eraseAddedPlacements = null;

        if (removed != null && added != null && (removed.Count > 0 || added.Count > 0))
            StrokesErased?.Invoke(this, new InkStrokesErasedEventArgs(removed, added));
        EraseGestureCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Rolls a cancelled gesture back: fragments removed (desc index),
    /// originals restored (asc index). No completion events, no undo entry.
    /// </summary>
    private void CancelEraseGesture()
    {
        var removedPlacements = _eraseRemovedPlacements;
        var addedPlacements = _eraseAddedPlacements;

        _isErasing = false;
        _lastErasePoint = null;
        _eraseRemovedStrokes = null;
        _eraseAddedStrokes = null;
        _eraseRemovedPlacements = null;
        _eraseAddedPlacements = null;

        if (addedPlacements != null)
        {
            foreach (var placement in addedPlacements
                .Where(p => p != null)
                .OrderByDescending(p => p.Index))
            {
                Store.RemoveStrokeQuietExact(placement);
            }
        }
        if (removedPlacements != null)
        {
            foreach (var placement in removedPlacements
                .Where(p => p != null)
                .OrderBy(p => p.Index))
            {
                Store.AddStrokeQuiet(placement.ForOwner(Store, placement.Index));
            }
        }
        EraseGestureCancelled?.Invoke(this, EventArgs.Empty);
        InkMutated?.Invoke(this, EventArgs.Empty);
    }

    // ── Store → visual sync ──────────────────────────────────────────────

    private void Store_Mutated(object sender, InkStoreMutationEventArgs e)
    {
        switch (e.Kind)
        {
            case InkStoreMutationKind.Added:
                AddVisual(e.Stroke, e.Index);
                break;
            case InkStoreMutationKind.Removed:
                RemoveVisual(e.Stroke);
                break;
            case InkStoreMutationKind.Replaced:
                if (e.Index >= 0)
                {
                    // The old visual sits at e.Index; the replaced stroke's
                    // visual was removed with its instance — rebuild the slot.
                    RebuildAllVisuals();
                }
                break;
            case InkStoreMutationKind.Cleared:
                RebuildAllVisuals();
                break;
            case InkStoreMutationKind.GeometryChanged:
                // Selection transform (move/scale/rotate) or an undo of one —
                // rebuild the stroke's geometry in place, no z-order change.
                // Raising InkMutated keeps thumbnails fresh and lets the page
                // rebuild its selection overlay through the normal chain.
                if (e.Stroke != null
                    && _strokeVisuals.TryGetValue(e.Stroke, out var visual))
                {
                    StrokeRenderer.UpdateStrokePath(visual, e.Stroke);
                    InkMutated?.Invoke(this, EventArgs.Empty);
                }
                break;
        }
    }

    private void AddVisual(InkStrokeData stroke, int index)
    {
        if (stroke == null || _strokeVisuals.ContainsKey(stroke))
            return;
        var path = StrokeRenderer.CreateStrokePath(stroke);
        _strokeVisuals[stroke] = path;
        // The live in-progress path (not a store visual) is always the last
        // child — clamp restored/inserted strokes below it so an undo
        // mid-stroke can't paint over the stroke being drawn.
        int maxIndex = Math.Max(0, Children.Count - (_livePath != null ? 1 : 0));
        int insertAt = Math.Max(0, Math.Min(index, maxIndex));
        Children.Insert(insertAt, path);
    }

    private void RemoveVisual(InkStrokeData stroke)
    {
        if (stroke == null)
            return;
        if (_strokeVisuals.TryGetValue(stroke, out var path))
        {
            _strokeVisuals.Remove(stroke);
            Children.Remove(path);
        }
    }

    private void RebuildAllVisuals()
    {
        // Keep the live path (if any) — it is not a store visual.
        var live = _livePath;
        Children.Clear();
        _strokeVisuals.Clear();
        foreach (var stroke in Store.Strokes)
        {
            var path = StrokeRenderer.CreateStrokePath(stroke);
            _strokeVisuals[stroke] = path;
            Children.Add(path);
        }
        if (live != null)
            Children.Add(live);
    }
}
