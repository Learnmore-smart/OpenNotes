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
/// Which Phase-A ink tool the surface currently applies. The editor's
/// wider tool enum maps onto this — selection/shapes/hidden-ink/laser are
/// Phase-B and simply map to <see cref="None"/> for now.
/// </summary>
public enum InkSurfaceTool
{
    None,
    Pen,
    Highlighter,
    Eraser,
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
/// - Touch is never ink input in Phase A (it pans the ScrollViewer).
/// - An inverted pen (<see cref="PointerPointProperties.IsEraser"/>) or a
///   held barrel button erases regardless of the active tool. The
///   draw-vs-erase decision is sampled once at pointer-down and held for
///   the whole gesture — a mid-gesture barrel/inversion change takes
///   effect on the NEXT stroke (WPF arbitrates the same way at
///   stroke-collect boundaries). The WPF double-barrel press-to-toggle
///   is not ported.
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
    public double HighlighterSize { get; set; } = 6.0;
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

    /// <summary>
    /// The eraser cursor element (host-owned Ellipse in the same coordinate
    /// space). The surface moves/sizes it; null disables the indicator.
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
        ReleaseActivePointer();
        HideEraserIndicator();
    }

    /// <summary>
    /// Shared pen service — the surface applies its PressureEnabled flag and
    /// feeds it pen packets for capability probing (WPF SetPenService parity).
    /// </summary>
    public void SetPenService(Caelum.Services.PenService service)
    {
        _penService = service;
        if (service != null)
            EnablePressure = service.PressureEnabled;
    }

    /// <summary>Positions and shows the eraser indicator over a page point.</summary>
    private void ShowEraserIndicatorAt(PointD pagePoint)
    {
        var indicator = EraserIndicator;
        if (indicator == null)
            return;
        indicator.Visibility = Visibility.Visible;
        if (indicator is FrameworkElement fe)
        {
            fe.Width = EraserSize;
            fe.Height = EraserSize;
        }
        SetLeft(indicator, pagePoint.X - EraserSize / 2.0);
        SetTop(indicator, pagePoint.Y - EraserSize / 2.0);
    }

    private void HideEraserIndicator()
    {
        if (EraserIndicator != null)
            EraserIndicator.Visibility = Visibility.Collapsed;
    }

    // ── Pointer pipeline ─────────────────────────────────────────────────

    private void InkSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!InputEnabled || _activePointerId.HasValue)
            return;

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

        // WPF parity: pen-only blocks non-pen INK creation; erasing is not
        // an ink-creation mode, so a mouse still erases under pen-only.
        bool inkCreation = !wantsErase && (Tool == InkSurfaceTool.Pen || Tool == InkSurfaceTool.Highlighter);
        if (PenOnlyMode && inkCreation && !isPen)
            return;
        if (!wantsErase && !inkCreation)
            return; // Tool == None (or a Phase-B tool)

        _activePointerId = e.Pointer.PointerId;
        CapturePointer(e.Pointer);

        if (wantsErase)
        {
            BeginEraseGesture();
            _isErasing = true;
            ShowEraserIndicatorAt(ToPointD(point.Position));
            EraseAtPoint(ToPointD(point.Position));
        }
        else
        {
            BeginStroke(point);
        }
        e.Handled = true;
    }

    private void InkSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var current = e.GetCurrentPoint(this);
        var device = current.PointerDeviceType;
        bool penErasingHover = device == PointerDeviceType.Pen
            && (current.Properties.IsEraser || current.Properties.IsBarrelButtonPressed
                || Tool == InkSurfaceTool.Eraser);
        bool mouseEraserHover = device == PointerDeviceType.Mouse
            && Tool == InkSurfaceTool.Eraser;

        // Eraser indicator follows the cursor even without contact (WPF shows
        // it while hovering with an inverted pen / in eraser mode).
        if (!_isDrawing && !_isErasing && (penErasingHover || mouseEraserHover))
            ShowEraserIndicatorAt(ToPointD(current.Position));

        if (_activePointerId == null || e.Pointer.PointerId != _activePointerId.Value)
            return;

        if (_isErasing)
        {
            var points = e.GetIntermediatePoints(this);
            foreach (var p in points)
            {
                ShowEraserIndicatorAt(ToPointD(p.Position));
                EraseAtPoint(ToPointD(p.Position));
            }
            e.Handled = true;
            return;
        }

        if (_isDrawing && _liveStroke != null)
        {
            // Intermediate points carry the full packet batch — one event can
            // deliver many samples at high report rates.
            foreach (var p in e.GetIntermediatePoints(this))
            {
                float pressure = EffectivePacketPressure(p);
                _liveStroke.Points.Add(new InkPointData(
                    p.Position.X, p.Position.Y, pressure));
            }
            // Also append the current point if it advanced past the last
            // intermediate (GetIntermediatePoints may exclude it).
            var tail = _liveStroke.Points[^1];
            if (Math.Abs(tail.X - current.Position.X) > 0.0001
                || Math.Abs(tail.Y - current.Position.Y) > 0.0001)
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
        if (_isErasing)
        {
            EraseAtPoint(ToPointD(point.Position));
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

    private void InkSurface_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId == null || e.Pointer.PointerId != _activePointerId.Value)
            return;
        if (_isDrawing)
            DiscardLiveStroke();
        if (_isErasing || HasPendingEraseGesture())
            CancelEraseGesture();
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
        if (Math.Abs(tail.X - point.Position.X) > 0.0001
            || Math.Abs(tail.Y - point.Position.Y) > 0.0001)
        {
            stroke.Points.Add(new InkPointData(
                point.Position.X, point.Position.Y, EffectivePacketPressure(point)));
        }

        // WPF PreserveTapStroke expands a single-point tap so the stroke
        // renders; our outline already draws a 1-point disc, so the point
        // list stays truthful instead.

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
    /// </summary>
    private void EraseAtPoint(PointD point)
    {
        var path = new List<PointD>();
        if (_lastErasePoint.HasValue)
            path.Add(_lastErasePoint.Value);
        path.Add(point);
        _lastErasePoint = point;

        if (Store.Count == 0)
            return;

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
                        stroke.Points, stroke.Size, stroke.IgnorePressure, path, EraserSize))
                    continue;
                ApplyErasedStroke(stroke, new List<InkStrokeData>());
                mutated = true;
                continue;
            }

            var fragments = StrokeGeometry.SplitStrokeAtEraser(
                stroke.Points, stroke.Size, stroke.IgnorePressure, path, EraserSize);
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

        if (mutated)
            InkMutated?.Invoke(this, EventArgs.Empty);
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
        }
    }

    private void AddVisual(InkStrokeData stroke, int index)
    {
        if (stroke == null || _strokeVisuals.ContainsKey(stroke))
            return;
        var path = StrokeRenderer.CreateStrokePath(stroke);
        _strokeVisuals[stroke] = path;
        int insertAt = Math.Max(0, Math.Min(index, Children.Count));
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
