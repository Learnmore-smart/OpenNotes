using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Services;
using PdfiumPdfDocument = PdfiumViewer.PdfDocument;
using Path = System.Windows.Shapes.Path;

namespace Caelum.Pages
{
    public sealed partial class EditorPage : Page, IInteractionCancellation
    {
        private static readonly DependencyProperty TextAnnotationAutoWidthProperty =
            DependencyProperty.RegisterAttached(
                "TextAnnotationAutoWidth", typeof(bool), typeof(EditorPage), new PropertyMetadata(false));

        private static readonly DependencyProperty TextAnnotationAutoHeightProperty =
            DependencyProperty.RegisterAttached(
                "TextAnnotationAutoHeight", typeof(bool), typeof(EditorPage), new PropertyMetadata(false));

        private enum ToolType { None, Pen, Highlighter, HiddenInk, Eraser, Shape, Laser, Text, Select, TextHighlight, AreaHighlight, StickyNote }

        /// <summary>
        /// Task 25/27: what the Highlighter toolbar button applies. Freehand is
        /// the classic ink highlighter; TextHighlight/Underline/StrikeOut/
        /// Squiggly ride the PDF text-selection pipeline; AreaHighlight drags
        /// a free-form rectangle. Session-only (not persisted).
        /// </summary>
        private enum HighlighterApplyMode { Freehand, TextHighlight, Underline, StrikeOut, Squiggly, AreaHighlight }

        // Keep popup previews aligned with the actual annotation pipelines:
        // freehand highlighter strokes are semi-transparent, text highlights
        // use PdfPageControl's 120 alpha overlay, text markup strokes remain
        // opaque, and area highlights expose an opaque edge plus a 30% fill.
        private const byte FreehandHighlighterOpacity = 140;
        private const byte TextHighlightOpacity = 120;
        private const byte AreaHighlightStrokeOpacity = 220;
        private const byte AreaHighlightFillOpacity = 76;

        private HighlighterApplyMode _highlighterApplyMode = HighlighterApplyMode.Freehand;

        private ToolType _currentTool = ToolType.None;
        private ToolType _previousTool = ToolType.Pen;
        private Color _penColor = Colors.Black;
        private Color _highlighterColor = Colors.Yellow;
        private Color _textColor = Colors.Black;
        private double _penSize = 1.5;
        private double _highlighterSize = 8.0;
        private double _eraserSize = 20.0;
        // Shape tool state — session-only by design (spec requires no persistence).
        private Color _shapeColor = Colors.Black;
        private double _shapeSize = 2.0;
        private ShapeKind _shapeKind = ShapeKind.Line;
        private double _currentFontSize = 18.0;
        private bool _textBold;
        private bool _textItalic;
        private string _textFontFamily = "Segoe UI";
        private TextAlignment _textAlignment = TextAlignment.Left;
        private bool _isRefreshingTextAlignmentOptions;
        private bool _isUpdatingToolState;
        private AppSettings _applicationSettings;

        // Popup internals are retained so runtime color/size refreshes can
        // update the live previews while a popup is open.
        private Slider _penPopupSizeSlider;
        private Line _penPopupSizePreview;
        private Slider _highlighterPopupSizeSlider;
        private Line _highlighterPopupSizePreview;
        private readonly Dictionary<HighlighterApplyMode, Path> _highlighterModePreviews = new();

        // Task 16: fullscreen immersive mode. While active the floating
        // toolbar is visually hidden (Opacity 0 + IsHitTestVisible false —
        // the toolbar is an overlay, so this never reflows the pages) and
        // all tool popups stay closed. Toggled with F11, ESC always leaves
        // it first. The previous toolbar property values are recorded so
        // repeated toggles leave no residue.
        private bool _isImmersiveMode;
        private double _preImmersiveToolbarOpacity = 1.0;
        private bool _preImmersiveToolbarHitTestVisible = true;
        private double _preImmersiveSidebarOpacity = 1.0;
        private bool _preImmersiveSidebarHitTestVisible = true;
        private double _preImmersiveSearchOpacity = 1.0;
        private bool _preImmersiveSearchHitTestVisible = true;

        // Task 22: on-screen ruler. ONE shared ruler overlaying the whole
        // editor viewport (not per-page). Session visual only — never saved
        // with the document and not persisted across sessions. It is an
        // overlay toggle INDEPENDENT of the active tool (pick Pen + ruler
        // ON and draw along the edge). Viewport-anchored: scrolling or
        // zooming the document never moves it, like a real ruler lying on
        // the screen. Pen/Highlighter strokes drawn near the ruler's edge
        // are constrained by it (see PdfPageControl.ApplyRulerConstraint);
        // the snapped strokes are ordinary strokes, so undo/save work
        // naturally and the ruler itself never touches undo/dirty.
        private bool _rulerVisible;
        private Point _rulerCenter;   // viewport (root-grid) coordinates
        private double _rulerAngle;   // degrees; always snapped to 15° steps
        private Grid _rulerVisual;    // built in code on first show
        private RotateTransform _rulerRotate;
        private bool _isDraggingRuler;
        private bool _isRotatingRuler;
        private Point _rulerDragOffset;          // pointer - center at drag start
        private double _rotateStartPointerAngle; // pointer angle around center at rotate start
        private double _rotateStartRulerAngle;

        private const double RulerLength = 360.0;
        private const double RulerHeight = 56.0;
        private const double RulerEndCapZone = 14.0;      // end zones rotate instead of move
        private const double RulerRotationSnapDegrees = 15.0;

        private readonly PdfService _pdfService;
        // Every asynchronous menu/sidebar/undo continuation captures this
        // boundary. Load/release/host transitions cancel the previous token so
        // a detached popup can never mutate a replacement document.
        private readonly DocumentOperationSession _documentOperationSession = new();
        private readonly ConditionalWeakTable<ContextMenu, ContextMenuOperationBinding> _sidebarContextMenuBindings = new();
        private CancellationTokenSource _loadCts;
        private int _loadSessionId;
        private int _completedLoadSessionId;
        private bool _isDirty;
        private long _dirtyGeneration;
        private readonly DocumentSaveCoordinator _documentSaveCoordinator = new();
        // Manual Save and the autosave timer share one task boundary. A
        // second caller reuses this task instead of collecting/writing a
        // second PDF while the first atomic replacement is in flight.
        private readonly object _saveGate = new();
        private Task<DocumentSaveResult> _autoSaveInFlight;
        private int _autoSaveTimerRunning;
        private string _currentPdfPath;
        public string CurrentPdfPath => _currentPdfPath;
        public bool IsDirty => _documentSaveCoordinator.IsDirty;

        private DocumentOperationLease CaptureDocumentOperationLease(
            object modelIdentity = null,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialDocumentOperationSession();
            return _documentOperationSession.Capture(
                _loadSessionId,
                _currentPdfPath,
                modelIdentity,
                cancellationToken);
        }

        private void EnsureInitialDocumentOperationSession()
        {
            // A newly-created draft starts at session zero. Some document
            // creation paths assign its first path before the normal LoadPdf
            // boundary runs; make that initial in-memory identity explicit so
            // save/text-session callbacks still use the shared lease contract.
            if (_loadSessionId == 0 && _completedLoadSessionId == 0 &&
                !string.IsNullOrWhiteSpace(_currentPdfPath))
                _documentOperationSession.Begin(_loadSessionId, _currentPdfPath, _pdfService);
        }

        private DocumentOperationLease CaptureDocumentOperationLease(
            int sessionId,
            string filePath,
            object modelIdentity = null,
            CancellationToken cancellationToken = default)
        {
            return _documentOperationSession.Capture(
                sessionId,
                filePath,
                modelIdentity,
                cancellationToken);
        }

        private bool ValidateDocumentOperationLease(
            DocumentOperationLease lease,
            object modelIdentity = null)
        {
            return _documentOperationSession.Validate(
                lease,
                _loadSessionId,
                _currentPdfPath,
                modelIdentity);
        }
        private bool _promptSaveAsAfterLoad;
        private bool _hasPromptedForSaveAs;
        private string _pendingLibraryFolderId;
        private bool _isNotebookDraft;

        private TextBox _selectedTextBox;
        // Text edit session tracking: text captured on GotFocus, compared on
        // LostFocus; a difference pushes one TextEditSessionAction (single-step
        // undo for the whole editing session of one text box).
        private TextBox _textEditSessionTextBox;
        private string _textEditSessionOriginalText;
        private Border _colorIndicator;
        private Button _textColorButton;
        private Border _inlineTextBoxToolbar;
        private Button _textDeleteButton;
        private Button _textDecreaseFontButton;
        private Button _textIncreaseFontButton;
        private TextBlock _textRecentLabel;
        private Popup _textColorPopup;
        private PdfPageControl _toolbarHostPage;
        private static readonly double[] TextFontSizeSteps = { 12d, 14d, 16d, 18d, 20d, 24d, 28d, 32d, 40d, 48d, 60d, 72d };

        private Popup _penPopup;
        private Popup _highlighterPopup;
        private Popup _eraserPopup;
        private Popup _shapePopup;
        private Popup _selectionPopup;
        private readonly List<(Popup Popup, EventHandler Handler)> _toolPopupOpenedHandlers = new();
        private Popup _stickyNotePopup;
        private TextBox _stickyNoteEditor;
        private Button _stickyNoteSaveButton;
        private Button _stickyNoteCancelButton;
        private Button _stickyNoteDeleteButton;
        private Border _stickyNoteDragHandle;
        private TextBlock _stickyNoteTitleTextBlock;
        private bool _isDraggingStickyNotePopup;
        private Point _stickyNotePopupDragStart;
        private double _stickyNotePopupDragStartHorizontalOffset;
        private double _stickyNotePopupDragStartVerticalOffset;
        private StickyNoteAnnotation _stickyNoteEditingModel;
        private Grid _stickyNoteEditingContainer;
        private string _stickyNoteEditingOriginalText;
        private Point _stickyNoteEditingOriginalPosition;
        private PdfPageControl _stickyNoteEditingPage;
        private int _stickyNoteEditingSessionId;
        private ToggleButton _textBoldButton;
        private ToggleButton _textItalicButton;
        private ComboBox _textFontFamilyCombo;
        private ComboBox _textAlignmentCombo;
        private PdfPageControl _activeSelectionPage;
        private bool _isDelegatingSelection;
        private PdfPageControl _selectionDelegateTarget;
        private bool _isUpdatingSelectionPopup;

        /// <summary>
        /// Weak registry for every editor-owned transient surface. WPF
        /// ContextMenu/ComboBox popups are detached from the editor visual tree,
        /// so an explicit registry is the only reliable way to close them on a
        /// window deactivation or editor unload. Weak references keep rebuilt
        /// localized controls collectible.
        /// </summary>
        private sealed class TransientUiRegistry
        {
            private readonly List<WeakReference<Popup>> _popups = new();
            private readonly List<WeakReference<ContextMenu>> _menus = new();
            private readonly List<WeakReference<ComboBox>> _comboBoxes = new();

            public void Register(Popup popup)
            {
                if (popup != null && !_popups.Any(reference => reference.TryGetTarget(out var current) && ReferenceEquals(current, popup)))
                    _popups.Add(new WeakReference<Popup>(popup));
            }

            public void Register(ContextMenu menu)
            {
                if (menu != null && !_menus.Any(reference => reference.TryGetTarget(out var current) && ReferenceEquals(current, menu)))
                    _menus.Add(new WeakReference<ContextMenu>(menu));
            }

            public void Register(ComboBox comboBox)
            {
                if (comboBox != null && !_comboBoxes.Any(reference => reference.TryGetTarget(out var current) && ReferenceEquals(current, comboBox)))
                    _comboBoxes.Add(new WeakReference<ComboBox>(comboBox));
            }

            public void CloseAll()
            {
                foreach (var reference in _popups.ToList())
                {
                    if (reference.TryGetTarget(out var popup))
                        popup.IsOpen = false;
                }

                foreach (var reference in _menus.ToList())
                {
                    if (reference.TryGetTarget(out var menu))
                        menu.IsOpen = false;
                }

                foreach (var reference in _comboBoxes.ToList())
                {
                    if (reference.TryGetTarget(out var comboBox))
                        comboBox.IsDropDownOpen = false;
                }

                _popups.RemoveAll(reference => !reference.TryGetTarget(out _));
                _menus.RemoveAll(reference => !reference.TryGetTarget(out _));
                _comboBoxes.RemoveAll(reference => !reference.TryGetTarget(out _));
            }

            public bool HasOpenSurface()
            {
                return _popups.Any(reference => reference.TryGetTarget(out var popup) && popup.IsOpen)
                    || _menus.Any(reference => reference.TryGetTarget(out var menu) && menu.IsOpen)
                    || _comboBoxes.Any(reference => reference.TryGetTarget(out var comboBox) && comboBox.IsDropDownOpen);
            }
        }

        private readonly TransientUiRegistry _transientUiRegistry = new();

        private sealed class TextAlignmentOption
        {
            public TextAlignmentOption(TextAlignment value, string label)
            {
                Value = value;
                Label = label;
            }

            public TextAlignment Value { get; }
            public string Label { get; }

            public override string ToString() => Label;
        }

        private Point _lastClickedPoint;
        private PdfPageControl _lastClickedPage;

        // Task 19: AddImage raises ImagesChanged while annotations are being
        // loaded from the PDF; suppress the dirty marking during that window
        // (loading a document must leave it clean).
        private bool _isLoadingAnnotations;

        private const double PdfTextSelectionDragThreshold = 4.0;
        private PdfPageControl _pdfTextSelectionPage;
        private PdfService.PdfPageTextInfo _pdfTextSelectionInfo;
        private Point _pdfTextSelectionPressPoint;
        private int _pdfTextSelectionAnchorOffset = -1;
        private int _pdfTextSelectionActiveOffset = -1;
        private bool _isPdfTextSelectionDragging;
        private bool _pdfTextSelectionExceededThreshold;
        private int _pdfTextSelectionRequestId;
        private string _selectedPdfText;

        private bool _isDragging;
        private bool _dragArmed;
        private Point _dragPressPointOnCanvas;
        private Grid _draggedContainer;
        private PdfPageControl _draggedContainerPage;
        private double _dragStartX;
        private double _dragStartY;
        private bool _suppressTextCaptureCancellation;

        private Grid _resizingTextContainer;
        private PdfPageControl _resizingTextPage;
        private TextResizeHandle _textResizeHandle;
        private Point _textResizeStartPoint;
        private TextBoxBounds _textResizeStartBounds;
        private bool _textResizeStartAutoWidth;
        private bool _textResizeStartAutoHeight;

        // Undo/Redo
        private interface IUndoAction
        {
            bool LeavesDocumentDirty { get; }
            Task UndoAsync();
            Task RedoAsync();
        }

        private class StrokeAddedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly StrokePlacement _placement;
            public StrokeAddedAction(PdfPageControl page, System.Windows.Ink.Stroke stroke)
            {
                _page = page;
                _placement = page.CaptureStrokePlacement(stroke);
            }

            public StrokeAddedAction(PdfPageControl page, StrokePlacement placement)
            {
                _page = page ?? throw new ArgumentNullException(nameof(page));
                _placement = placement ?? throw new ArgumentNullException(nameof(placement));
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.RemoveStrokeQuiet(_placement);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.AddStrokeQuiet(_placement.ForOwner(_page, _placement.Index));
                return Task.CompletedTask;
            }
        }

        private sealed class HiddenInkAddedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly HiddenInkAnnotation _annotation;

            public HiddenInkAddedAction(PdfPageControl page, HiddenInkAnnotation annotation)
            {
                _page = page;
                _annotation = annotation;
            }

            public bool LeavesDocumentDirty => true;

            public Task UndoAsync()
            {
                _page.RemoveHiddenInkQuiet(_annotation);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.AddHiddenInkQuiet(_annotation);
                return Task.CompletedTask;
            }
        }

        private sealed class HiddenInkRemovedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly HiddenInkAnnotation _annotation;

            public HiddenInkRemovedAction(PdfPageControl page, HiddenInkAnnotation annotation)
            {
                _page = page;
                _annotation = annotation;
            }

            public bool LeavesDocumentDirty => true;

            public Task UndoAsync()
            {
                _page.AddHiddenInkQuiet(_annotation);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.RemoveHiddenInkQuiet(_annotation);
                return Task.CompletedTask;
            }
        }

        private sealed class HiddenInksRemovedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly List<HiddenInkAnnotation> _annotations;

            public HiddenInksRemovedAction(
                PdfPageControl page,
                IReadOnlyList<HiddenInkAnnotation> annotations)
            {
                _page = page;
                _annotations = annotations == null
                    ? new List<HiddenInkAnnotation>()
                    : annotations.ToList();
            }

            public bool LeavesDocumentDirty => true;

            public Task UndoAsync()
            {
                foreach (var annotation in _annotations)
                    _page.AddHiddenInkQuiet(annotation);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                foreach (var annotation in _annotations)
                    _page.RemoveHiddenInkQuiet(annotation);
                return Task.CompletedTask;
            }
        }

        private class StrokesErasedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly List<StrokePlacement> _removedOriginals;
            private readonly List<StrokePlacement> _addedFragments;
            public StrokesErasedAction(PdfPageControl page, List<System.Windows.Ink.Stroke> removedOriginals, List<System.Windows.Ink.Stroke> addedFragments)
                : this(
                    page,
                    CapturePlacements(page, removedOriginals),
                    CapturePlacements(page, addedFragments))
            {
            }

            public StrokesErasedAction(
                PdfPageControl page,
                List<StrokePlacement> removedOriginals,
                List<StrokePlacement> addedFragments)
            {
                _page = page;
                _removedOriginals = removedOriginals ?? new List<StrokePlacement>();
                _addedFragments = addedFragments ?? new List<StrokePlacement>();
            }
            public bool LeavesDocumentDirty => true;
            public bool LastOperationSucceeded { get; private set; }
            public Task UndoAsync()
            {
                LastOperationSucceeded = false;
                var removedFragments = new List<StrokePlacement>();
                var restoredOriginals = new List<StrokePlacement>();
                try
                {
                    foreach (var fragment in _addedFragments.OrderByDescending(p => p.Index))
                    {
                        if (!TryRemoveCurrentPlacement(fragment, out var removedFragment))
                        {
                            RollbackUndo(removedFragments, restoredOriginals);
                            return Task.CompletedTask;
                        }

                        removedFragments.Add(removedFragment);
                    }

                    foreach (var original in _removedOriginals.OrderBy(p => p.Index))
                    {
                        var restored = _page.AddStrokeQuiet(original.ForOwner(_page, original.Index));
                        if (restored == null)
                        {
                            RollbackUndo(removedFragments, restoredOriginals);
                            return Task.CompletedTask;
                        }

                        restoredOriginals.Add(restored);
                    }

                    LastOperationSucceeded = true;
                }
                catch
                {
                    RollbackUndo(removedFragments, restoredOriginals);
                }

                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                LastOperationSucceeded = false;
                var removedOriginals = new List<StrokePlacement>();
                var restoredFragments = new List<StrokePlacement>();
                try
                {
                    foreach (var original in _removedOriginals.OrderByDescending(p => p.Index))
                    {
                        if (!TryRemoveCurrentPlacement(original, out var removedOriginal))
                        {
                            RollbackRedo(removedOriginals, restoredFragments);
                            return Task.CompletedTask;
                        }

                        removedOriginals.Add(removedOriginal);
                    }

                    foreach (var fragment in _addedFragments.OrderBy(p => p.Index))
                    {
                        var restored = _page.AddStrokeQuiet(fragment.ForOwner(_page, fragment.Index));
                        if (restored == null)
                        {
                            RollbackRedo(removedOriginals, restoredFragments);
                            return Task.CompletedTask;
                        }

                        restoredFragments.Add(restored);
                    }

                    LastOperationSucceeded = true;
                }
                catch
                {
                    RollbackRedo(removedOriginals, restoredFragments);
                }

                return Task.CompletedTask;
            }

            /// <summary>
            /// Erase history follows the logical token/side identity, because
            /// recognition and polishing can replace a WPF Stroke reference
            /// between the original gesture and a later redo. Capture the
            /// resolved live placement first, then remove that exact instance
            /// so rollback never targets a stale or unrelated reference.
            /// </summary>
            private bool TryRemoveCurrentPlacement(
                StrokePlacement expected,
                out StrokePlacement removed)
            {
                removed = null;
                if (!_page.TryCaptureCurrentStrokePlacement(expected, out var current)
                    || !_page.RemoveStrokeQuietExact(current))
                {
                    return false;
                }

                removed = current;
                return true;
            }

            private void RollbackUndo(
                IReadOnlyList<StrokePlacement> removedFragments,
                IReadOnlyList<StrokePlacement> restoredOriginals)
            {
                for (int index = restoredOriginals.Count - 1; index >= 0; index--)
                    _page.RemoveStrokeQuietExact(restoredOriginals[index]);

                foreach (var fragment in removedFragments.OrderBy(p => p.Index))
                    _page.AddStrokeQuiet(fragment.ForOwner(_page, fragment.Index));
            }

            private void RollbackRedo(
                IReadOnlyList<StrokePlacement> removedOriginals,
                IReadOnlyList<StrokePlacement> restoredFragments)
            {
                for (int index = restoredFragments.Count - 1; index >= 0; index--)
                    _page.RemoveStrokeQuietExact(restoredFragments[index]);

                foreach (var original in removedOriginals.OrderBy(p => p.Index))
                    _page.AddStrokeQuiet(original.ForOwner(_page, original.Index));
            }

            private static List<StrokePlacement> CapturePlacements(
                PdfPageControl page,
                List<System.Windows.Ink.Stroke> strokes)
            {
                return (strokes ?? new List<System.Windows.Ink.Stroke>())
                    .Select(page.CaptureStrokePlacement)
                    .ToList();
            }
        }

        /// <summary>
        /// A scribble shape recognition replaced one tokenized stroke in
        /// place. Only immutable snapshots are retained, so erase/other
        /// actions can make a later undo or redo a safe no-op.
        /// </summary>
        private sealed class StrokeReplacedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly Guid _token;
            private readonly int _originalIndex;
            private readonly StrokeReplacementSnapshot _originalSnapshot;
            private readonly StrokeReplacementSnapshot _idealSnapshot;

            public StrokeReplacedAction(
                PdfPageControl page,
                Guid token,
                int originalIndex,
                StrokeReplacementSnapshot originalSnapshot,
                StrokeReplacementSnapshot idealSnapshot)
            {
                _page = page;
                _token = token;
                _originalIndex = originalIndex;
                _originalSnapshot = originalSnapshot;
                _idealSnapshot = idealSnapshot;
            }

            public bool LeavesDocumentDirty => true;

            public Task UndoAsync()
            {
                _page.TryReplaceStrokeQuiet(
                    _token,
                    StrokeReplacementSide.Ideal,
                    _originalSnapshot,
                    out _);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.TryReplaceStrokeQuiet(
                    _token,
                    StrokeReplacementSide.Original,
                    _idealSnapshot,
                    out _);
                return Task.CompletedTask;
            }
        }

        private class ItemsAddedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly List<StrokePlacement> _strokes;
            private readonly List<System.Windows.Controls.Grid> _containers;

            public ItemsAddedAction(PdfPageControl page, List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
            {
                _page = page;
                _strokes = (strokes ?? new List<System.Windows.Ink.Stroke>())
                    .Select(page.CaptureStrokePlacement)
                    .ToList();
                _containers = containers ?? new List<System.Windows.Controls.Grid>();
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                // The pasted items may currently be selected (paste
                // auto-selects, Task 8.2); drop the selection before removing
                // them so it cannot reference removed strokes/containers.
                if (_page.HasSelection)
                    _page.ClearSelection();
                foreach (var stroke in _strokes.OrderByDescending(p => p.Index))
                    _page.RemoveStrokeQuiet(stroke);
                foreach (var container in _containers) _page.RemoveTextContainerQuiet(container);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                foreach (var stroke in _strokes.OrderBy(p => p.Index))
                    _page.AddStrokeQuiet(stroke.ForOwner(_page, stroke.Index));
                foreach (var container in _containers) _page.AddTextContainerQuiet(container);
                return Task.CompletedTask;
            }
        }

        private class ItemsRemovedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly List<StrokePlacement> _strokes;
            private readonly List<System.Windows.Controls.Grid> _containers;

            public ItemsRemovedAction(PdfPageControl page, List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
                : this(
                    page,
                    (strokes ?? new List<System.Windows.Ink.Stroke>())
                        .Select(page.CaptureStrokePlacement)
                        .ToList(),
                    containers)
            {
            }

            public ItemsRemovedAction(
                PdfPageControl page,
                List<StrokePlacement> placements,
                List<System.Windows.Controls.Grid> containers)
            {
                _page = page;
                _strokes = placements ?? new List<StrokePlacement>();
                _containers = containers ?? new List<System.Windows.Controls.Grid>();
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                foreach (var stroke in _strokes.OrderBy(p => p.Index))
                    _page.AddStrokeQuiet(stroke.ForOwner(_page, stroke.Index));
                foreach (var container in _containers) _page.AddTextContainerQuiet(container);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                foreach (var stroke in _strokes.OrderByDescending(p => p.Index))
                    _page.RemoveStrokeQuiet(stroke);
                foreach (var container in _containers) _page.RemoveTextContainerQuiet(container);
                return Task.CompletedTask;
            }
        }

        private sealed class StickyNoteEditAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly Grid _container;
            private readonly StickyNoteAnnotation _note;
            private readonly string _before;
            private readonly string _after;

            public StickyNoteEditAction(
                PdfPageControl page,
                Grid container,
                StickyNoteAnnotation note,
                string before,
                string after)
            {
                _page = page;
                _container = container;
                _note = note;
                _before = before;
                _after = after;
            }

            public bool LeavesDocumentDirty => true;

            public Task UndoAsync()
            {
                if (!_page.SetStickyNoteTextQuiet(_container, _before))
                    _note.Text = _before;
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                if (!_page.SetStickyNoteTextQuiet(_container, _after))
                    _note.Text = _after;
                return Task.CompletedTask;
            }
        }

        private sealed class StickyNoteMovedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly Grid _container;
            private readonly Point _before;
            private readonly Point _after;

            public StickyNoteMovedAction(
                PdfPageControl page,
                Grid container,
                Point before,
                Point after)
            {
                _page = page;
                _container = container;
                _before = before;
                _after = after;
            }

            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.SetStickyNotePositionQuiet(_container, _before);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.SetStickyNotePositionQuiet(_container, _after);
                return Task.CompletedTask;
            }
        }

        private sealed class StickyNoteAddedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly Grid _container;

            public StickyNoteAddedAction(PdfPageControl page, Grid container)
            {
                _page = page;
                _container = container;
            }

            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.RemoveTextContainerQuiet(_container);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.AddTextContainerQuiet(_container);
                return Task.CompletedTask;
            }
        }

        private sealed class StickyNoteDeletedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly Grid _container;

            public StickyNoteDeletedAction(PdfPageControl page, Grid container)
            {
                _page = page;
                _container = container;
            }

            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.AddTextContainerQuiet(_container);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.RemoveTextContainerQuiet(_container);
                return Task.CompletedTask;
            }
        }

        private class TextBoxAddedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly System.Windows.Controls.Grid _container;
            public TextBoxAddedAction(PdfPageControl page, System.Windows.Controls.Grid container)
            {
                _page = page;
                _container = container;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.RemoveTextContainerQuiet(_container);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.AddTextContainerQuiet(_container);
                return Task.CompletedTask;
            }
        }

        private class TextBoxDeletedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly System.Windows.Controls.Grid _container;
            public TextBoxDeletedAction(PdfPageControl page, System.Windows.Controls.Grid container)
            {
                _page = page;
                _container = container;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.AddTextContainerQuiet(_container);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.RemoveTextContainerQuiet(_container);
                return Task.CompletedTask;
            }
        }

        private class TextEditSessionAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly TextBox _textBox;
            private readonly string _beforeText;
            private readonly string _afterText;
            public TextEditSessionAction(PdfPageControl page, TextBox textBox, string beforeText, string afterText)
            {
                _page = page;
                _textBox = textBox;
                _beforeText = beforeText;
                _afterText = afterText;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                // Programmatic Text assignment does not raise GotFocus/LostFocus,
                // so this cannot re-enter the session tracking; the existing
                // TextChanged handler still marks the document dirty.
                _textBox.Text = _beforeText;
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _textBox.Text = _afterText;
                return Task.CompletedTask;
            }
        }

        private class TextStyleChangedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly TextBox _textBox;
            private readonly double _beforeFontSize;
            private readonly Brush _beforeForeground;
            private readonly double _afterFontSize;
            private readonly Brush _afterForeground;
            public TextStyleChangedAction(PdfPageControl page, TextBox textBox,
                double beforeFontSize, Brush beforeForeground, double afterFontSize, Brush afterForeground)
            {
                _page = page;
                _textBox = textBox;
                _beforeFontSize = beforeFontSize;
                _beforeForeground = beforeForeground;
                _afterFontSize = afterFontSize;
                _afterForeground = afterForeground;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _textBox.FontSize = _beforeFontSize;
                _textBox.Foreground = _beforeForeground;
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _textBox.FontSize = _afterFontSize;
                _textBox.Foreground = _afterForeground;
                return Task.CompletedTask;
            }
        }

        private sealed class TextFormatChangedAction : IUndoAction
        {
            private readonly TextBox _textBox;
            private readonly FontWeight _beforeWeight;
            private readonly FontStyle _beforeStyle;
            private readonly FontFamily _beforeFamily;
            private readonly TextAlignment _beforeAlignment;
            private readonly FontWeight _afterWeight;
            private readonly FontStyle _afterStyle;
            private readonly FontFamily _afterFamily;
            private readonly TextAlignment _afterAlignment;

            public TextFormatChangedAction(
                TextBox textBox,
                FontWeight beforeWeight,
                FontStyle beforeStyle,
                FontFamily beforeFamily,
                TextAlignment beforeAlignment,
                FontWeight afterWeight,
                FontStyle afterStyle,
                FontFamily afterFamily,
                TextAlignment afterAlignment)
            {
                _textBox = textBox;
                _beforeWeight = beforeWeight;
                _beforeStyle = beforeStyle;
                _beforeFamily = beforeFamily;
                _beforeAlignment = beforeAlignment;
                _afterWeight = afterWeight;
                _afterStyle = afterStyle;
                _afterFamily = afterFamily;
                _afterAlignment = afterAlignment;
            }

            public bool LeavesDocumentDirty => true;

            private void Apply(FontWeight weight, FontStyle style, FontFamily family, TextAlignment alignment)
            {
                _textBox.FontWeight = weight;
                _textBox.FontStyle = style;
                _textBox.FontFamily = family;
                _textBox.TextAlignment = alignment;
            }

            public Task UndoAsync()
            {
                Apply(_beforeWeight, _beforeStyle, _beforeFamily, _beforeAlignment);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                Apply(_afterWeight, _afterStyle, _afterFamily, _afterAlignment);
                return Task.CompletedTask;
            }
        }

        private class TextBoxMovedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly System.Windows.Controls.Grid _container;
            private readonly System.Windows.Point _beforePosition;
            private readonly System.Windows.Point _afterPosition;
            public TextBoxMovedAction(PdfPageControl page, System.Windows.Controls.Grid container, System.Windows.Point beforePosition, System.Windows.Point afterPosition)
            {
                _page = page;
                _container = container;
                _beforePosition = beforePosition;
                _afterPosition = afterPosition;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                Canvas.SetLeft(_container, _beforePosition.X);
                Canvas.SetTop(_container, _beforePosition.Y);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                Canvas.SetLeft(_container, _afterPosition.X);
                Canvas.SetTop(_container, _afterPosition.Y);
                return Task.CompletedTask;
            }
        }

        private sealed class TextBoxResizedAction : IUndoAction
        {
            private readonly Grid _container;
            private readonly TextBoxBounds _beforeBounds;
            private readonly TextBoxBounds _afterBounds;
            private readonly bool _beforeAutoWidth;
            private readonly bool _beforeAutoHeight;
            private readonly bool _afterAutoWidth;
            private readonly bool _afterAutoHeight;

            public TextBoxResizedAction(
                Grid container,
                TextBoxBounds beforeBounds,
                TextBoxBounds afterBounds,
                bool beforeAutoWidth,
                bool beforeAutoHeight,
                bool afterAutoWidth,
                bool afterAutoHeight)
            {
                _container = container;
                _beforeBounds = beforeBounds;
                _afterBounds = afterBounds;
                _beforeAutoWidth = beforeAutoWidth;
                _beforeAutoHeight = beforeAutoHeight;
                _afterAutoWidth = afterAutoWidth;
                _afterAutoHeight = afterAutoHeight;
            }

            public bool LeavesDocumentDirty => true;

            public Task UndoAsync()
            {
                ApplyTextContainerBounds(_container, _beforeBounds, _beforeAutoWidth, _beforeAutoHeight);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                ApplyTextContainerBounds(_container, _afterBounds, _afterAutoWidth, _afterAutoHeight);
                return Task.CompletedTask;
            }
        }

        private class SelectionMoveAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly double _deltaX;
            private readonly double _deltaY;
            private readonly List<System.Windows.Ink.Stroke> _strokes;
            private readonly List<System.Windows.Controls.Grid> _containers;
            public SelectionMoveAction(PdfPageControl page, double deltaX, double deltaY,
                List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
            {
                _page = page;
                _deltaX = deltaX;
                _deltaY = deltaY;
                _strokes = strokes;
                _containers = containers;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.MoveItemsDirectly(_strokes, _containers, -_deltaX, -_deltaY);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.MoveItemsDirectly(_strokes, _containers, _deltaX, _deltaY);
                return Task.CompletedTask;
            }
        }

        private class SelectionCrossPageMoveAction : IUndoAction
        {
            private readonly PdfPageControl _sourcePage;
            private readonly PdfPageControl _targetPage;
            private readonly double _deltaX;
            private readonly double _deltaY;
            private readonly double _adjustX;
            private readonly double _adjustY;
            private readonly List<StrokePlacement> _sourcePlacements;
            private readonly List<StrokePlacement> _targetPlacements = new List<StrokePlacement>();
            private readonly List<System.Windows.Controls.Grid> _containers;

            public bool LastOperationSucceeded { get; private set; }

            public SelectionCrossPageMoveAction(PdfPageControl sourcePage, PdfPageControl targetPage,
                double deltaX, double deltaY, double adjustX, double adjustY,
                List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
                : this(
                    sourcePage,
                    targetPage,
                    deltaX,
                    deltaY,
                    adjustX,
                    adjustY,
                    (strokes ?? new List<System.Windows.Ink.Stroke>())
                        .Select(sourcePage.CaptureStrokePlacement)
                        .ToList(),
                    containers)
            {
            }

            public SelectionCrossPageMoveAction(PdfPageControl sourcePage, PdfPageControl targetPage,
                double deltaX, double deltaY, double adjustX, double adjustY,
                List<StrokePlacement> sourcePlacements, List<System.Windows.Controls.Grid> containers)
            {
                _sourcePage = sourcePage;
                _targetPage = targetPage;
                _deltaX = deltaX;
                _deltaY = deltaY;
                _adjustX = adjustX;
                _adjustY = adjustY;
                _sourcePlacements = sourcePlacements ?? new List<StrokePlacement>();
                _containers = containers ?? new List<System.Windows.Controls.Grid>();
            }

            public bool LeavesDocumentDirty => true;

            public bool ExecuteInitialTransfer()
            {
                LastOperationSucceeded = false;
                _targetPlacements.Clear();
                var moved = new List<(StrokePlacement Source, StrokePlacement Target)>();
                var movedContainers = new List<System.Windows.Controls.Grid>();
                try
                {
                    foreach (var expectedSourcePlacement in _sourcePlacements.OrderBy(p => p.Index).ToList())
                    {
                        if (!_sourcePage.TryCaptureCurrentStrokePlacement(
                                expectedSourcePlacement,
                                out var sourcePlacement))
                        {
                            RollbackInitialTransfers(moved);
                            return false;
                        }

                        var targetPlacement = _targetPage.AddStrokeQuiet(
                            sourcePlacement.ForOwner(
                                _targetPage,
                                _targetPage.GetStrokes().Count));
                        if (targetPlacement == null)
                        {
                            RollbackInitialTransfers(moved);
                            return false;
                        }

                        if (!_sourcePage.RemoveStrokeQuietExact(sourcePlacement))
                        {
                            _targetPage.RemoveStrokeQuietExact(targetPlacement);
                            RollbackInitialTransfers(moved);
                            return false;
                        }

                        moved.Add((sourcePlacement, targetPlacement));
                    }

                    if (moved.Count == 0 && _containers.Count == 0)
                        return false;

                    foreach (var container in _containers)
                    {
                        if (container == null)
                        {
                            RollbackContainers(movedContainers);
                            RollbackInitialTransfers(moved);
                            return false;
                        }

                        _sourcePage.RemoveTextContainerQuiet(container);
                        _targetPage.AddTextContainerQuiet(container);
                        TransferImageData(_sourcePage, _targetPage, container);
                        TransferOverlayData(_sourcePage, _targetPage, container);
                        movedContainers.Add(container);
                    }

                    _targetPage.MoveItemsDirectly(
                        moved.Select(pair => pair.Target.Stroke).ToList(),
                        _containers,
                        _adjustX,
                        _adjustY);

                    foreach (var pair in moved)
                    {
                        var expected = _sourcePlacements.FirstOrDefault(placement =>
                            placement.Token == pair.Source.Token
                            && placement.Side == pair.Source.Side);
                        RememberCurrentSourcePlacement(expected, pair.Source);
                    }

                    _targetPlacements.AddRange(moved.Select(pair => pair.Target));
                    LastOperationSucceeded = true;
                    return true;
                }
                catch
                {
                    RollbackContainers(movedContainers);
                    RollbackInitialTransfers(moved);
                    _targetPlacements.Clear();
                    LastOperationSucceeded = false;
                    return false;
                }
            }

            public Task UndoAsync()
            {
                LastOperationSucceeded = false;
                var moved = new List<(StrokePlacement Target, StrokePlacement Source)>();
                var movedContainers = new List<System.Windows.Controls.Grid>();
                try
                {
                    foreach (var expectedTargetPlacement in _targetPlacements.OrderByDescending(p => p.Index))
                    {
                        if (!_targetPage.TryCaptureCurrentStrokePlacement(
                                expectedTargetPlacement,
                                out var currentTargetPlacement)
                            || !_targetPage.RemoveStrokeQuietExact(currentTargetPlacement))
                        {
                            RollbackUndoTransfers(moved);
                            return Task.CompletedTask;
                        }

                        var sourcePlacement = FindSourcePlacement(expectedTargetPlacement);
                        if (sourcePlacement == null)
                        {
                            _targetPage.AddStrokeQuiet(currentTargetPlacement);
                            RollbackUndoTransfers(moved);
                            return Task.CompletedTask;
                        }

                        var restored = _sourcePage.AddStrokeQuiet(
                            sourcePlacement.ForOwner(_sourcePage, sourcePlacement.Index));
                        if (restored == null)
                        {
                            _targetPage.AddStrokeQuiet(currentTargetPlacement);
                            RollbackUndoTransfers(moved);
                            return Task.CompletedTask;
                        }

                        moved.Add((currentTargetPlacement, restored));
                    }

                    foreach (var container in _containers)
                    {
                        if (container == null)
                        {
                            RollbackContainers(movedContainers);
                            RollbackUndoTransfers(moved);
                            return Task.CompletedTask;
                        }

                        _targetPage.RemoveTextContainerQuiet(container);
                        _sourcePage.AddTextContainerQuiet(container);
                        TransferImageData(_targetPage, _sourcePage, container);
                        TransferOverlayData(_targetPage, _sourcePage, container);
                        movedContainers.Add(container);
                    }

                    _sourcePage.MoveItemsDirectly(
                        moved.Select(pair => pair.Source.Stroke).ToList(),
                        _containers,
                        -_deltaX - _adjustX,
                        -_deltaY - _adjustY);

                    foreach (var pair in moved)
                    {
                        var expected = FindSourcePlacement(pair.Target);
                        RememberCurrentSourcePlacement(expected, pair.Source);
                    }

                    _targetPlacements.Clear();
                    _targetPlacements.AddRange(moved.Select(pair => pair.Target));
                    LastOperationSucceeded = true;
                }
                catch
                {
                    RollbackContainers(movedContainers);
                    RollbackUndoTransfers(moved);
                }

                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                LastOperationSucceeded = false;
                var moved = new List<(StrokePlacement Source, StrokePlacement Target)>();
                var movedContainers = new List<System.Windows.Controls.Grid>();
                var previousTargets = _targetPlacements.ToList();
                try
                {
                    foreach (var targetPlacement in previousTargets.OrderBy(p => p.Index))
                    {
                        var sourcePlacement = FindSourcePlacement(targetPlacement);
                        if (sourcePlacement == null
                            || !_sourcePage.TryCaptureCurrentStrokePlacement(
                                sourcePlacement,
                                out var currentSourcePlacement)
                            || !_sourcePage.RemoveStrokeQuietExact(currentSourcePlacement))
                        {
                            RollbackRedoTransfers(moved);
                            return Task.CompletedTask;
                        }

                        var resolvedTarget = _targetPage.AddStrokeQuiet(targetPlacement);
                        if (resolvedTarget == null)
                        {
                            _sourcePage.AddStrokeQuiet(
                                currentSourcePlacement.ForOwner(
                                    _sourcePage,
                                    currentSourcePlacement.Index));
                            RollbackRedoTransfers(moved);
                            return Task.CompletedTask;
                        }

                        moved.Add((currentSourcePlacement, resolvedTarget));
                    }

                    foreach (var container in _containers)
                    {
                        if (container == null)
                        {
                            RollbackContainers(movedContainers);
                            RollbackRedoTransfers(moved);
                            return Task.CompletedTask;
                        }

                        _sourcePage.RemoveTextContainerQuiet(container);
                        _targetPage.AddTextContainerQuiet(container);
                        TransferImageData(_sourcePage, _targetPage, container);
                        TransferOverlayData(_sourcePage, _targetPage, container);
                        movedContainers.Add(container);
                    }

                    _targetPage.MoveItemsDirectly(
                        moved.Select(pair => pair.Target.Stroke).ToList(),
                        _containers,
                        _deltaX + _adjustX,
                        _deltaY + _adjustY);

                    foreach (var sourcePlacement in moved)
                        RememberCurrentSourcePlacement(
                            FindSourcePlacement(sourcePlacement.Target),
                            sourcePlacement.Source);

                    _targetPlacements.Clear();
                    _targetPlacements.AddRange(moved.Select(pair => pair.Target));
                    LastOperationSucceeded = true;
                }
                catch
                {
                    RollbackContainers(movedContainers);
                    RollbackRedoTransfers(moved);
                    _targetPlacements.Clear();
                    _targetPlacements.AddRange(previousTargets);
                }

                return Task.CompletedTask;
            }

            private void RollbackInitialTransfers(
                IReadOnlyList<(StrokePlacement Source, StrokePlacement Target)> moved)
            {
                for (int index = moved.Count - 1; index >= 0; index--)
                {
                    var pair = moved[index];
                    _targetPage.RemoveStrokeQuietExact(pair.Target);
                    _sourcePage.AddStrokeQuiet(
                        pair.Source.ForOwner(_sourcePage, pair.Source.Index));
                }
            }

            private void RollbackUndoTransfers(
                IReadOnlyList<(StrokePlacement Target, StrokePlacement Source)> moved)
            {
                for (int index = moved.Count - 1; index >= 0; index--)
                {
                    var pair = moved[index];
                    _sourcePage.RemoveStrokeQuietExact(pair.Source);
                    _targetPage.AddStrokeQuiet(
                        pair.Target.ForOwner(_targetPage, pair.Target.Index));
                }
            }

            private void RollbackContainers(
                IReadOnlyList<System.Windows.Controls.Grid> movedContainers)
            {
                for (int index = movedContainers.Count - 1; index >= 0; index--)
                {
                    var container = movedContainers[index];
                    _targetPage.RemoveTextContainerQuiet(container);
                    _targetPage.RemoveImageData(container);
                    _sourcePage.AddTextContainerQuiet(container);
                    TransferImageData(_targetPage, _sourcePage, container);
                    TransferOverlayData(_targetPage, _sourcePage, container);
                }
            }

            private void RollbackRedoTransfers(
                IReadOnlyList<(StrokePlacement Source, StrokePlacement Target)> moved)
            {
                for (int index = moved.Count - 1; index >= 0; index--)
                {
                    var pair = moved[index];
                    _targetPage.RemoveStrokeQuietExact(pair.Target);
                    _sourcePage.AddStrokeQuiet(
                        pair.Source.ForOwner(_sourcePage, pair.Source.Index));
                }
            }

            private void RememberCurrentSourcePlacement(
                StrokePlacement expected,
                StrokePlacement current)
            {
                if (expected == null || current == null)
                    return;

                int index = _sourcePlacements.IndexOf(expected);
                if (index >= 0)
                    _sourcePlacements[index] = current;
            }

            private StrokePlacement FindSourcePlacement(StrokePlacement targetPlacement)
            {
                return _sourcePlacements.FirstOrDefault(sourcePlacement =>
                    sourcePlacement.Token == targetPlacement.Token
                    && sourcePlacement.Side == targetPlacement.Side);
            }

            /// <summary>
            /// Task 19: the image payload dict is per-page-control, so moving a
            /// container across pages must also hand the bytes to the receiving
            /// page (a no-op for text containers — GetImageData returns null).
            /// </summary>
            private static void TransferImageData(PdfPageControl source, PdfPageControl target, System.Windows.Controls.Grid container)
            {
                var data = source.GetImageData(container);
                if (data != null && target.GetImageData(container) == null)
                    target.SetImageData(container, data);
            }

            private static void TransferOverlayData(PdfPageControl source, PdfPageControl target, System.Windows.Controls.Grid container)
            {
                // Overlay payloads (markup, area highlight, and Sticky Note)
                // live in a per-page dictionary just like image bytes.  Keep
                // the same model object attached to the reparented container;
                // otherwise a cross-page move renders but loses content/colour
                // on copy, save, or a later undo/redo.
                var data = source.GetOverlayData(container);
                if (data != null)
                    target.SetOverlayData(container, data);
            }
        }

        private class SelectionResizeAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly double _totalScale;
            private readonly System.Windows.Point _anchor;
            private readonly List<System.Windows.Ink.Stroke> _strokes;
            private readonly List<System.Windows.Controls.Grid> _containers;
            public SelectionResizeAction(PdfPageControl page, double totalScale, System.Windows.Point anchor,
                List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
            {
                _page = page;
                _totalScale = totalScale;
                _anchor = anchor;
                _strokes = strokes;
                _containers = containers;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.ScaleItemsDirectly(_strokes, _containers, 1.0 / _totalScale, _anchor);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.ScaleItemsDirectly(_strokes, _containers, _totalScale, _anchor);
                return Task.CompletedTask;
            }
        }

        private sealed class DocumentSnapshotAction : IUndoAction
        {
            private readonly EditorPage _owner;
            private readonly byte[] _beforeBytes;
            private readonly byte[] _afterBytes;
            private readonly int _undoFocusPageIndex;
            private readonly int _redoFocusPageIndex;
            private readonly IReadOnlyList<PageBookmark> _beforeBookmarks;
            private readonly IReadOnlyList<PageBookmark> _afterBookmarks;
            private DocumentOperationLease _operationLease;
            private DocumentOperationLease _completedOperationLease;

            public bool LastOperationSucceeded { get; private set; }
            public DocumentOperationLease CompletedOperationLease => _completedOperationLease;

            public DocumentSnapshotAction(
                EditorPage owner,
                byte[] beforeBytes,
                byte[] afterBytes,
                int undoFocusPageIndex,
                int redoFocusPageIndex,
                IEnumerable<PageBookmark> beforeBookmarks = null,
                IEnumerable<PageBookmark> afterBookmarks = null)
            {
                _owner = owner;
                _beforeBytes = beforeBytes;
                _afterBytes = afterBytes;
                _undoFocusPageIndex = undoFocusPageIndex;
                _redoFocusPageIndex = redoFocusPageIndex;
                _beforeBookmarks = beforeBookmarks?.Select(CloneBookmark).ToList();
                _afterBookmarks = afterBookmarks?.Select(CloneBookmark).ToList();
            }

            public bool LeavesDocumentDirty => false;

            public Task UndoAsync() => ApplyAsync(_beforeBytes, _undoFocusPageIndex, _beforeBookmarks);

            public Task RedoAsync() => ApplyAsync(_afterBytes, _redoFocusPageIndex, _afterBookmarks);

            public void SetOperationLease(DocumentOperationLease operationLease)
            {
                _completedOperationLease?.Dispose();
                _completedOperationLease = null;
                _operationLease = operationLease;
                LastOperationSucceeded = false;
            }

            private async Task ApplyAsync(byte[] bytes, int focusPageIndex, IReadOnlyList<PageBookmark> bookmarks)
            {
                LastOperationSucceeded = false;
                _completedOperationLease?.Dispose();
                _completedOperationLease = await _owner.ApplyDocumentSnapshotAsync(
                    bytes,
                    focusPageIndex,
                    _operationLease);
                if (_completedOperationLease == null || bookmarks == null || string.IsNullOrWhiteSpace(_owner._currentPdfPath))
                {
                    LastOperationSucceeded = _completedOperationLease != null;
                    return;
                }

                try
                {
                    if (!_owner.ValidateDocumentOperationLease(_completedOperationLease))
                        return;
                    PageBookmarkService.Replace(_owner._currentPdfPath, bookmarks);
                    _owner.RefreshBookmarks(_owner._loadSessionId, _owner._currentPdfPath, _completedOperationLease);
                    LastOperationSucceeded = _owner.ValidateDocumentOperationLease(_completedOperationLease);
                }
                catch (Exception ex)
                {
                    if (_owner.ValidateDocumentOperationLease(_completedOperationLease))
                        System.Diagnostics.Debug.WriteLine($"[Bookmarks] Snapshot restore failed: {ex}");
                }
            }

            private static PageBookmark CloneBookmark(PageBookmark bookmark)
            {
                return new PageBookmark
                {
                    PageIndex = bookmark?.PageIndex ?? -1,
                    Label = bookmark?.Label ?? string.Empty
                };
            }
        }

        private sealed class PrintablePageImage
        {
            public BitmapSource Bitmap { get; init; }
            public double Width { get; init; }
            public double Height { get; init; }
        }

        private sealed class PdfSearchResult
        {
            public int PageIndex { get; init; }
            public int StartOffset { get; init; }
            public int Length { get; init; }
            public string DisplayText { get; init; }
        }

        // Sidebar rows are deliberately lightweight view-models.  The item
        // containers and thumbnails are created by WPF's recycling presenter;
        // document loading only publishes these rows and never builds a full
        // visual tree for every page.
        public sealed class SidebarPageItem : INotifyPropertyChanged
        {
            private BitmapSource _thumbnail;
            private string _pageLabel;

            public SidebarPageItem(int pageIndex, string pageLabel)
            {
                PageIndex = pageIndex;
                _pageLabel = pageLabel ?? string.Empty;
            }

            public int PageIndex { get; }
            public string PageLabel
            {
                get => _pageLabel;
                set
                {
                    value ??= string.Empty;
                    if (string.Equals(_pageLabel, value, StringComparison.Ordinal))
                        return;
                    _pageLabel = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PageLabel)));
                }
            }
            public BitmapSource Thumbnail
            {
                get => _thumbnail;
                set
                {
                    if (ReferenceEquals(_thumbnail, value))
                        return;
                    _thumbnail = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public sealed class SidebarBookmarkItem
        {
            private string _label;

            public SidebarBookmarkItem(int pageIndex, string label)
            {
                PageIndex = pageIndex;
                _label = label ?? string.Empty;
            }

            public int PageIndex { get; }
            public string Label
            {
                get => _label;
                set => _label = value ?? string.Empty;
            }
        }

        public sealed class SidebarOutlineItem
        {
            public SidebarOutlineItem(int pageIndex, string title, string automationId)
            {
                PageIndex = pageIndex;
                Title = title ?? string.Empty;
                AutomationId = automationId ?? string.Empty;
            }

            public int PageIndex { get; }
            public string Title { get; set; }
            public string AutomationId { get; }
            public ObservableCollection<SidebarOutlineItem> Children { get; } = new();
        }

        // A small, deterministic guard shared by all asynchronous sidebar
        // continuations.  It is intentionally independent of WPF controls so
        // a late result can be rejected in an STA test without a live window.
        private sealed class SidebarLoadSessionGate
        {
            private int _sessionId;
            private string _filePath;

            public SidebarLoadSessionGate(int sessionId, string filePath)
            {
                Begin(sessionId, filePath);
            }

            public void Begin(int sessionId, string filePath)
            {
                _sessionId = sessionId;
                _filePath = DocumentOperationSession.NormalizePath(filePath);
            }

            public bool IsCurrent(int sessionId, string filePath)
            {
                return _sessionId == sessionId &&
                    string.Equals(
                        _filePath,
                        DocumentOperationSession.NormalizePath(filePath),
                        StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class ContextMenuOperationBinding
        {
            public ContextMenuOperationBinding(object model, int sessionId, string filePath)
            {
                Model = model;
                SessionId = sessionId;
                FilePath = filePath;
            }

            public object Model { get; }
            public int SessionId { get; }
            public string FilePath { get; }
        }

        private sealed class StrokeStyleChangedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly IReadOnlyDictionary<Stroke, DrawingAttributes> _before;
            private readonly IReadOnlyDictionary<Stroke, DrawingAttributes> _after;

            public StrokeStyleChangedAction(
                PdfPageControl page,
                IReadOnlyDictionary<Stroke, DrawingAttributes> before,
                IReadOnlyDictionary<Stroke, DrawingAttributes> after)
            {
                _page = page;
                _before = before;
                _after = after;
            }

            public bool LeavesDocumentDirty => true;

            public Task UndoAsync()
            {
                Apply(_before);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                Apply(_after);
                return Task.CompletedTask;
            }

            private void Apply(IReadOnlyDictionary<Stroke, DrawingAttributes> values)
            {
                foreach (var pair in values)
                    pair.Key.DrawingAttributes = pair.Value.Clone();
                _page.RefreshSelectedDrawingStyle();
            }
        }

        // The WPF drag loop is synchronous, but the Drop callback can begin an
        // asynchronous document operation before DoDragDrop returns. Keep the
        // complete source identity in one immutable value so clearing editor
        // gesture state cannot make the drop stale by accident.
        private sealed record ThumbnailDragPayload(
            int SourceIndex,
            int SessionId,
            string FilePath,
            SidebarPageItem Source);

        private readonly List<IUndoAction> _undoStack = new List<IUndoAction>();
        private readonly List<IUndoAction> _redoStack = new List<IUndoAction>();

        // Selection tool state
        private SelectionFilter _selectionFilter = SelectionFilter.Both;
        private SelectionShape _selectionShape = SelectionShape.Rectangle;

        private double _zoomLevel = 1.0;
        private double _lastRenderedDpiScale = 1.0;
        private CancellationTokenSource _reRenderCts;
        private readonly System.Windows.Threading.DispatcherTimer _zoomRenderDebounceTimer;

        // Tracks which pages have been re-rendered at the current _lastRenderedDpiScale
        private readonly HashSet<int> _pagesRenderedAtScale = new HashSet<int>();
        private readonly List<PdfPageControl> _pageControls = new List<PdfPageControl>();
        private readonly List<double> _pageTopOffsets = new List<double>();
        private readonly List<double> _pageHeights = new List<double>();
        private readonly List<Button> _pageDeleteButtons = new List<Button>();
        private readonly List<Button> _pageInsertButtons = new List<Button>();
        private readonly List<PdfSearchResult> _pdfSearchResults = new List<PdfSearchResult>();
        private CancellationTokenSource _pdfSearchCts;
        private CancellationTokenSource _thumbnailLoadCts;
        private readonly HashSet<int> _thumbnailPagesLoading = new HashSet<int>();
        // A recycled sidebar row can finish after a reload. Keep the loading
        // marker generation-scoped so an old callback cannot block the same
        // page index in the replacement document (or clear its marker).
        private readonly Dictionary<int, int> _thumbnailPageLoadSessions = new Dictionary<int, int>();
        private readonly ThumbnailRevisionGate _thumbnailRevisionGate = new();
        private const int ThumbnailCacheCapacity = 24;
        private readonly Dictionary<int, BitmapSource> _thumbnailCache = new Dictionary<int, BitmapSource>();
        private readonly LinkedList<int> _thumbnailCacheLru = new LinkedList<int>();
        private bool _isRefreshingThumbnails;
        private bool _isSynchronizingThumbnailSelection;
        private Point _thumbnailDragStartPoint;
        private ThumbnailDragPayload _thumbnailDragPayload;
        private int _thumbnailDropSlot = -1;
        private enum SidebarTab
        {
            Pages,
            Outline,
            Bookmarks
        }

        private SidebarTab _sidebarTab = SidebarTab.Pages;
        private bool _sidebarCollapsed;
        private const double SidebarExpandedWidth = 184.0;
        private const double SidebarCollapsedWidth = 38.0;
        private readonly ObservableCollection<SidebarPageItem> _sidebarPageItems = new();
        private readonly ObservableCollection<SidebarBookmarkItem> _sidebarBookmarkItems = new();
        private readonly ObservableCollection<SidebarOutlineItem> _sidebarOutlineItems = new();
        private readonly SidebarLoadSessionGate _sidebarLoadSessionGate = new(0, string.Empty);

        public ReadOnlyObservableCollection<SidebarPageItem> SidebarPageItems { get; }
        public ReadOnlyObservableCollection<SidebarBookmarkItem> SidebarBookmarkItems { get; }
        public ReadOnlyObservableCollection<SidebarOutlineItem> SidebarOutlineItems { get; }
        private CancellationTokenSource _scrollReRenderCts;
        private readonly System.Windows.Threading.DispatcherTimer _scrollRenderDebounceTimer;
        private const double PageSpacing = 28.0;
        private bool _isHostActive = true;
        private bool _resourcesReleased;
        private bool _documentInteractionBlocked;
        private readonly DocumentEditAdmission _editAdmission = new();
        private readonly DocumentReleaseState _releaseState = new();
        private readonly object _lifecycleGate = new();
        private Task<bool> _navigationPreparationInFlight;
        private Task<bool> _closePreparationInFlight;
        private Task<bool> _releaseResourcesInFlight;

        // Smooth scrolling
        private double _targetVerticalOffset;
        private double _targetHorizontalOffset;
        private bool _smoothScrollInitialized;

        // Middle-mouse-button pan state
        private bool _isMiddleMousePanning;
        private Point _middleMouseStartPoint;
        private double _middleMouseStartVerticalOffset;
        private double _middleMouseStartHorizontalOffset;

        // Touch manipulation state
        private double _manipulationBaseZoom;
        private int _activeTouchCount;

        // Raw pinch-zoom tracking (bypasses WPF manipulation system so the
        // first finger can still reach InkCanvas while two fingers zoom)
        private readonly Dictionary<int, Point> _activeTouches = new Dictionary<int, Point>();
        private double _pinchStartDistance;
        private double _pinchStartZoom;
        private bool _isPinchActive;

        private const double ZoomMin = 0.25;
        private const double ZoomMax = 8.0;
        private const double ZoomStep = 0.1;

        // Pen scrolling state
        private bool _isPenScrolling;
        private Point _penScrollStartPoint;
        private double _penScrollStartVerticalOffset;
        private double _penScrollStartHorizontalOffset;

        // Universal pen support (Surface, Wacom, Huawei, Dell, HP, Lenovo, etc.)
        private WindowsPenService _penService;

        // Auto-save timer (every 60 seconds)
        private System.Windows.Threading.DispatcherTimer _autoSaveTimer;
        private bool _languageChangedSubscribed;

        // Horizontal mouse wheel hook for precision touchpads
        private HwndSource _hwndSource;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int MK_CONTROL = 0x0008;

        public EditorPage()
        {
            SidebarPageItems = new ReadOnlyObservableCollection<SidebarPageItem>(_sidebarPageItems);
            SidebarBookmarkItems = new ReadOnlyObservableCollection<SidebarBookmarkItem>(_sidebarBookmarkItems);
            SidebarOutlineItems = new ReadOnlyObservableCollection<SidebarOutlineItem>(_sidebarOutlineItems);
            InitializeComponent();
            // XAML assigns the one-based initial value while the TextChanged
            // handler is already wired.  Keep that binding from opening a
            // user edit session before the first document is loaded.
            if (PageNumberTextBox != null)
            {
                PageNumberTextBox.SetCurrentValue(TextBox.TextProperty, "1");
                _pageJumpOpeningValue = "1";
            }
            _isPageJumpEditing = false;
            _isPageJumpInitializing = false;
            if (OutlineTreeView != null)
                OutlineTreeView.PageInvoker = JumpToPage;
            InitializeTextBoxPopup();
            CreateToolPopups();
            ApplySettings(AppSettingsService.Load());
            ApplyLocalization();
            SetSidebarTab(SidebarTab.Pages);

            _pdfService = new PdfService();
            // Keep the empty editor's initial session usable for in-memory
            // interaction tests and pre-load command wiring. A real PDF load
            // immediately rotates this boundary to its path/session identity.
            _documentOperationSession.Begin(_loadSessionId, _currentPdfPath, _pdfService);
            ActivateTool(ToolType.None);

            FixToolPopupZOrder();

            KeyDown += EditorPage_KeyDown;

            // Task 19: image file drag-drop. The Page root has AllowDrop=true
            // (XAML) but no handlers; tunneling at the page level keeps the
            // ScrollViewer children from eating the drag events.
            PreviewDragOver += EditorPage_PreviewDragOver;
            Drop += EditorPage_Drop;

            Loaded += EditorPage_Loaded;
            Unloaded += EditorPage_Unloaded;

            // Re-render newly-visible pages when scrolling after a zoom
            _zoomRenderDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _zoomRenderDebounceTimer.Tick += ZoomRenderDebounceTimer_Tick;
            _scrollRenderDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _scrollRenderDebounceTimer.Tick += ScrollRenderDebounceTimer_Tick;
            PdfScrollViewer.ScrollChanged += PdfScrollViewer_ScrollChanged;

            EnsureAutoSaveTimer();
        }

        private void EditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_languageChangedSubscribed)
            {
                LocalizationService.LanguageChanged += EditorPage_LanguageChanged;
                _languageChangedSubscribed = true;
            }

            ApplyLocalization();
            InitializePenService();
            EnsureAutoSaveTimer();
            InstallHorizontalWheelHook();
            InstallScrollbarTrackJump();
        }

        private void EditorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            CloseTransientUi("unloaded");
            if (_languageChangedSubscribed)
            {
                LocalizationService.LanguageChanged -= EditorPage_LanguageChanged;
                _languageChangedSubscribed = false;
            }

            CancelTextBoxDrag(restoreBounds: true);
            CancelTextResize(restoreBounds: true);
            SetHostActive(false);
            UnfixTransientUiHooks();
            _autoSaveTimer?.Stop();
            _penService?.Dispose();
            _penService = null;
            RemoveHorizontalWheelHook();
            ClearPdfTextSelection();
            DetachToolPopupHandlers();
        }

        private void EnsureAutoSaveTimer()
        {
            if (_resourcesReleased || !_releaseState.CanResumeInteraction)
                return;

            if (_autoSaveTimer == null)
            {
                _autoSaveTimer = new System.Windows.Threading.DispatcherTimer();
                _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            }

            _autoSaveTimer.Interval = TimeSpan.FromSeconds(_applicationSettings?.AutoSaveIntervalSeconds ?? 60);
            _autoSaveTimer.Start();
        }

        private void EditorPage_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void ClearPdfTextSelection(bool clearCopiedText = true)
        {
            Interlocked.Increment(ref _pdfTextSelectionRequestId);

            foreach (var page in _pageControls)
                page.ClearPdfTextSelection();

            _pdfTextSelectionPage = null;
            _pdfTextSelectionInfo = null;
            _pdfTextSelectionAnchorOffset = -1;
            _pdfTextSelectionActiveOffset = -1;
            _isPdfTextSelectionDragging = false;
            _pdfTextSelectionExceededThreshold = false;

            if (clearCopiedText)
                _selectedPdfText = null;
        }

        private bool TryCopySelectedPdfTextToClipboard()
        {
            if ((_currentTool != ToolType.None && _currentTool != ToolType.Select) || string.IsNullOrEmpty(_selectedPdfText))
                return false;

            try
            {
                Clipboard.SetText(_selectedPdfText);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool RectContainsWithPadding(Rect rect, Point point, double padding)
        {
            var padded = rect;
            padded.Inflate(padding, padding);
            return padded.Contains(point);
        }

        private static double DistanceToRect(Point point, Rect rect)
        {
            double dx = 0;
            if (point.X < rect.Left)
                dx = rect.Left - point.X;
            else if (point.X > rect.Right)
                dx = point.X - rect.Right;

            double dy = 0;
            if (point.Y < rect.Top)
                dy = rect.Top - point.Y;
            else if (point.Y > rect.Bottom)
                dy = point.Y - rect.Bottom;

            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private int FindNearestTextOffset(PdfService.PdfPageTextInfo textInfo, Point point, double maxDistance)
        {
            if (textInfo?.Characters == null || textInfo.Characters.Count == 0)
                return -1;

            int bestOffset = -1;
            double bestDistance = double.MaxValue;

            foreach (var character in textInfo.Characters)
            {
                if (character.Bounds == null || character.Bounds.Count == 0 || character.UnionBounds.IsEmpty)
                    continue;

                if (RectContainsWithPadding(character.UnionBounds, point, 3.0))
                    return character.Offset;

                double distance = DistanceToRect(point, character.UnionBounds);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestOffset = character.Offset;
                }
            }

            return bestDistance <= maxDistance ? bestOffset : -1;
        }

        private static bool ShouldMergeSelectionRects(Rect current, Rect next)
        {
            double verticalOverlap = Math.Min(current.Bottom, next.Bottom) - Math.Max(current.Top, next.Top);
            bool sameLine = verticalOverlap >= Math.Min(current.Height, next.Height) * 0.35;
            bool closeEnough = next.Left <= current.Right + 8;
            return sameLine && closeEnough;
        }

        private static IReadOnlyList<Rect> BuildPdfTextSelectionRects(PdfService.PdfPageTextInfo textInfo, int startOffset, int endOffset)
        {
            var mergedRects = new List<Rect>();
            if (textInfo?.Characters == null || textInfo.Characters.Count == 0)
                return mergedRects;

            int start = Math.Max(0, Math.Min(startOffset, endOffset));
            int end = Math.Min(textInfo.Characters.Count - 1, Math.Max(startOffset, endOffset));

            for (int i = start; i <= end; i++)
            {
                var character = textInfo.Characters[i];
                if (character.Bounds == null || character.Bounds.Count == 0)
                    continue;

                foreach (var rect in character.Bounds)
                {
                    if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                        continue;

                    if (mergedRects.Count > 0 && ShouldMergeSelectionRects(mergedRects[mergedRects.Count - 1], rect))
                    {
                        var merged = mergedRects[mergedRects.Count - 1];
                        merged.Union(rect);
                        mergedRects[mergedRects.Count - 1] = merged;
                    }
                    else
                    {
                        mergedRects.Add(rect);
                    }
                }
            }

            return mergedRects;
        }

        private static TextMarkupAnnotation BuildTextMarkupAnnotation(
            IReadOnlyList<Rect> absoluteRects,
            TextMarkupKind kind,
            Color color)
        {
            var bounds = Rect.Empty;
            foreach (var rect in absoluteRects ?? Array.Empty<Rect>())
            {
                if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                    continue;
                if (bounds.IsEmpty)
                    bounds = rect;
                else
                    bounds.Union(rect);
            }

            var markup = new TextMarkupAnnotation
            {
                Kind = kind.ToString(),
                X = bounds.IsEmpty ? 0 : bounds.X,
                Y = bounds.IsEmpty ? 0 : bounds.Y,
                R = color.R,
                G = color.G,
                B = color.B
            };

            if (bounds.IsEmpty)
                return markup;

            foreach (var rect in absoluteRects)
            {
                if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                {
                    markup.Rects.Add(new[]
                    {
                        rect.X - bounds.X,
                        rect.Y - bounds.Y,
                        rect.Width,
                        rect.Height
                    });
                }
            }

            return markup;
        }

        private void UpdatePdfTextSelectionVisuals()
        {
            if (_pdfTextSelectionPage == null || _pdfTextSelectionInfo == null || _pdfTextSelectionInfo.Text == null)
            {
                ClearPdfTextSelection();
                return;
            }

            int start = Math.Min(_pdfTextSelectionAnchorOffset, _pdfTextSelectionActiveOffset);
            int end = Math.Max(_pdfTextSelectionAnchorOffset, _pdfTextSelectionActiveOffset);
            if (start < 0 || end < start || end >= _pdfTextSelectionInfo.Text.Length)
            {
                ClearPdfTextSelection();
                return;
            }

            foreach (var pageControl in _pageControls)
            {
                if (!ReferenceEquals(pageControl, _pdfTextSelectionPage))
                    pageControl.ClearPdfTextSelection();
            }

            _pdfTextSelectionPage.SetPdfTextSelectionRects(BuildPdfTextSelectionRects(_pdfTextSelectionInfo, start, end));
            _selectedPdfText = _pdfTextSelectionInfo.Text.Substring(start, end - start + 1);
        }

        private async void PageControl_PdfTextSelectionPointerPressed(object sender, PdfTextSelectionPointerEventArgs e)
        {
            if ((_currentTool != ToolType.None && _currentTool != ToolType.TextHighlight) || sender is not PdfPageControl page)
                return;

            using var operationLease = CaptureDocumentOperationLease(page);
            if (!ValidateDocumentOperationLease(operationLease, page) || !_pageControls.Contains(page))
                return;
            int requestId = Interlocked.Increment(ref _pdfTextSelectionRequestId);

            if (_selectedTextBox != null)
                DeselectTextBox();

            Keyboard.Focus(PdfScrollViewer);
            ClearPdfTextSelection();
            _pdfTextSelectionPressPoint = e.Position;

            try
            {
                var textInfo = _pdfService.TryGetCachedPageTextInfo(page.PageIndex, out var cachedTextInfo)
                    ? cachedTextInfo
                    : await _pdfService.GetPageTextInfoAsync(page.PageIndex, operationLease.Token);

                if (!ValidateDocumentOperationLease(operationLease, page) || !_pageControls.Contains(page) ||
                    requestId != _pdfTextSelectionRequestId || (_currentTool != ToolType.None && _currentTool != ToolType.TextHighlight))
                    return;

                int anchorOffset = FindNearestTextOffset(textInfo, e.Position, 24.0);
                if (anchorOffset < 0)
                    return;

                _pdfTextSelectionPage = page;
                _pdfTextSelectionInfo = textInfo;
                _pdfTextSelectionAnchorOffset = anchorOffset;
                _pdfTextSelectionActiveOffset = anchorOffset;
                _isPdfTextSelectionDragging = true;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (ValidateDocumentOperationLease(operationLease, page))
                    System.Diagnostics.Debug.WriteLine($"[PdfTextSelection] Failed to read page text: {ex}");
            }
        }

        private void PageControl_PdfTextSelectionPointerMoved(object sender, PdfTextSelectionPointerEventArgs e)
        {
            if (!_isPdfTextSelectionDragging || _pdfTextSelectionInfo == null || !ReferenceEquals(sender, _pdfTextSelectionPage))
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            int offset = FindNearestTextOffset(_pdfTextSelectionInfo, e.Position, double.PositiveInfinity);
            if (offset < 0)
                return;

            _pdfTextSelectionActiveOffset = offset;
            if (!_pdfTextSelectionExceededThreshold)
            {
                var delta = e.Position - _pdfTextSelectionPressPoint;
                _pdfTextSelectionExceededThreshold = Math.Abs(delta.X) >= PdfTextSelectionDragThreshold || Math.Abs(delta.Y) >= PdfTextSelectionDragThreshold;
            }

            if (_pdfTextSelectionExceededThreshold)
                UpdatePdfTextSelectionVisuals();
        }

        private void PageControl_PdfTextSelectionPointerReleased(object sender, PdfTextSelectionPointerEventArgs e)
        {
            Interlocked.Increment(ref _pdfTextSelectionRequestId);

            if (!_isPdfTextSelectionDragging || _pdfTextSelectionInfo == null || !ReferenceEquals(sender, _pdfTextSelectionPage))
            {
                ClearPdfTextSelection();
                return;
            }

            int offset = FindNearestTextOffset(_pdfTextSelectionInfo, e.Position, double.PositiveInfinity);
            if (offset >= 0)
                _pdfTextSelectionActiveOffset = offset;

            bool keepSelection = _pdfTextSelectionExceededThreshold
                && _pdfTextSelectionAnchorOffset >= 0
                && _pdfTextSelectionActiveOffset >= 0;

            _isPdfTextSelectionDragging = false;
            _pdfTextSelectionExceededThreshold = false;

            if (!keepSelection)
            {
                ClearPdfTextSelection();
                return;
            }

            if (_currentTool == ToolType.TextHighlight)
            {
                int start = Math.Min(_pdfTextSelectionAnchorOffset, _pdfTextSelectionActiveOffset);
                int end = Math.Max(_pdfTextSelectionAnchorOffset, _pdfTextSelectionActiveOffset);
                var rects = BuildPdfTextSelectionRects(_pdfTextSelectionInfo, start, end);
                if (rects.Count > 0)
                {
                    if (_highlighterApplyMode == HighlighterApplyMode.TextHighlight)
                    {
                        _pdfTextSelectionPage.AddHighlightAnnotation(rects, _highlighterColor);
                        MarkDirty();
                    }
                    else
                    {
                        var markup = BuildTextMarkupAnnotation(
                            rects,
                            _highlighterApplyMode switch
                            {
                                HighlighterApplyMode.StrikeOut => TextMarkupKind.StrikeOut,
                                HighlighterApplyMode.Squiggly => TextMarkupKind.Squiggly,
                                _ => TextMarkupKind.Underline
                            },
                            _highlighterColor);
                        var container = _pdfTextSelectionPage.AddTextMarkup(markup);
                        if (container != null)
                            PushUndoAction(new ItemsAddedAction(
                                _pdfTextSelectionPage,
                                new List<System.Windows.Ink.Stroke>(),
                                new List<System.Windows.Controls.Grid> { container }));
                    }
                }
                ClearPdfTextSelection();
                return;
            }

            UpdatePdfTextSelectionVisuals();
        }
        private void InstallHorizontalWheelHook()
        {
            var window = Window.GetWindow(this);
            if (window == null) return;
            _hwndSource = PresentationSource.FromVisual(window) as HwndSource;
            _hwndSource?.AddHook(WndProc);
        }

        private void RemoveHorizontalWheelHook()
        {
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
        }

        private bool IsActiveEditorPage()
        {
            return Window.GetWindow(this) is MainWindow window &&
                   window.IsActiveContent(this) &&
                   IsVisible &&
                   PdfScrollViewer != null &&
                   PdfScrollViewer.IsVisible;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!IsActiveEditorPage())
                return IntPtr.Zero;

            if (msg == WM_MOUSEWHEEL)
            {
                int keys = (int)(wParam.ToInt64() & 0xFFFF);
                if ((keys & MK_CONTROL) != 0)
                {
                    // Precision touchpad pinch-to-zoom sends Ctrl+Wheel.
                    // Handle it here so we don't rely on Keyboard.Modifiers
                    // which can miss the synthetic Ctrl from touchpad drivers.
                    int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    var mousePos = Mouse.GetPosition(PdfScrollViewer);
                    if (mousePos.X >= 0 && mousePos.Y >= 0 &&
                        mousePos.X <= PdfScrollViewer.ActualWidth &&
                        mousePos.Y <= PdfScrollViewer.ActualHeight)
                    {
                        double oldZoom = _zoomLevel;
                        double step = delta > 0 ? ZoomStep : -ZoomStep;
                        double newZoom = Math.Max(ZoomMin, Math.Min(ZoomMax, _zoomLevel + step));
                        if (Math.Abs(newZoom - oldZoom) > 0.001)
                            ZoomAroundPoint(newZoom, mousePos);
                        handled = true;
                    }
                }
            }
            else if (msg == WM_MOUSEHWHEEL)
            {
                // wParam high word = horizontal delta (positive = right, negative = left)
                int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

                // Check if mouse is over our ScrollViewer
                var mousePos = Mouse.GetPosition(PdfScrollViewer);
                if (mousePos.X >= 0 && mousePos.Y >= 0 &&
                    mousePos.X <= PdfScrollViewer.ActualWidth &&
                    mousePos.Y <= PdfScrollViewer.ActualHeight)
                {
                    if (!_smoothScrollInitialized)
                    {
                        _targetHorizontalOffset = PdfScrollViewer.HorizontalOffset;
                        _targetVerticalOffset = PdfScrollViewer.VerticalOffset;
                        _smoothScrollInitialized = true;
                    }

                    double scrollAmount = delta * 0.8;
                    _targetHorizontalOffset = Math.Max(0,
                        Math.Min(PdfScrollViewer.ScrollableWidth, _targetHorizontalOffset + scrollAmount));

                    AnimateHorizontalScroll(_targetHorizontalOffset);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private async void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            // DispatcherTimer callbacks are async void. Guard the callback
            // itself as well as AutoSaveAsync's shared task so an interval
            // tick cannot re-enter while the previous save is awaiting disk.
            if (Interlocked.Exchange(ref _autoSaveTimerRunning, 1) != 0)
                return;

            try
            {
                using var operationLease = CaptureDocumentOperationLease(_pdfService);
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                var saved = await AutoSaveAsync(operationLease);
                if (saved && ValidateDocumentOperationLease(operationLease))
                {
                    var mw = Window.GetWindow(this) as MainWindow;
                    mw?.ShowToast(LocalizationService.Get("Editor.AutoSaved"), "\uE74E", 1500);
                }
            }
            finally
            {
                Volatile.Write(ref _autoSaveTimerRunning, 0);
            }
        }

        private void InitializePenService()
        {
            if (_penService != null)
            {
                Console.WriteLine("[EditorPage] WindowsPenService already initialized, skipping");
                return;
            }

            var window = Window.GetWindow(this);
            Console.WriteLine($"[EditorPage] InitializePenService 闁?Window={window?.GetType().Name ?? "NULL"}");

            _penService = new WindowsPenService();
            _penService.ToolToggleRequested += PenService_ToolToggleRequested;
            _penService.PenDeviceDetected += PenService_PenDeviceDetected;
            _penService.Initialize(window);
            Console.WriteLine("[EditorPage] WindowsPenService.Initialize() returned");

            // Push the pen service to all existing page controls
            PushPenServiceToPages();
        }

        /// <summary>
        /// Propagate the shared <see cref="WindowsPenService"/> to every
        /// <see cref="PdfPageControl"/> currently in the pages container so
        /// they can probe devices and honour pressure/tilt settings.
        /// </summary>
        private void PushPenServiceToPages()
        {
            if (_penService == null) return;
            foreach (var page in _pageControls)
                page.SetPenService(_penService);
        }

        private void PenService_ToolToggleRequested(object sender, EventArgs e)
        {
            if (!IsActiveEditorPage())
                return;

            Console.WriteLine($"[EditorPage] ToolToggleRequested received on thread {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsActiveEditorPage())
                    return;

                Console.WriteLine($"[EditorPage] ToggleEraserMode executing, current={_currentTool}");
                ToggleEraserMode();
            }));
        }

        private void PenService_PenDeviceDetected(object sender, PenDeviceInfo info)
        {
            if (!IsActiveEditorPage())
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsActiveEditorPage())
                    return;

                string brandName = info.PenBrand == PenBrand.Generic
                    ? LocalizationService.Get("Editor.Stylus")
                    : info.PenBrand.ToString();
                var featureLabels = new List<string>();
                if (info.SupportsPressure)
                    featureLabels.Add(LocalizationService.Get("Editor.PenFeaturePressure"));
                if (info.SupportsXTilt || info.SupportsYTilt)
                    featureLabels.Add(LocalizationService.Get("Editor.PenFeatureTilt"));
                if (info.SupportsBarrelButton)
                    featureLabels.Add(LocalizationService.Get("Editor.PenFeatureBarrel"));
                string features = featureLabels.Count == 0
                    ? string.Empty
                    : $" ({string.Join(" · ", featureLabels)})";

                Console.WriteLine($"[EditorPage] Pen detected: {brandName}{features}");
                var mw = Window.GetWindow(this) as MainWindow;
                mw?.ShowToast(LocalizationService.Format("Editor.PenDetected", brandName, features), "\uEDA4", 2500);
            }));
        }

        private void ToggleEraserMode()
        {
            if (_currentTool == ToolType.Eraser)
            {
                Console.WriteLine($"[EditorPage] Eraser 闁?{_previousTool}");
                ActivateTool(_previousTool);
            }
            else
            {
                Console.WriteLine($"[EditorPage] {_currentTool} 闁?Eraser");
                _previousTool = _currentTool;
                ActivateTool(ToolType.Eraser);
            }
        }

        public EditorPage(string filePath) : this(filePath, false, null, false)
        {
        }

        public EditorPage(string filePath, bool promptSaveAsAfterLoad, string pendingLibraryFolderId, bool isNotebookDraft) : this()
        {
            _currentPdfPath = filePath;
            _promptSaveAsAfterLoad = promptSaveAsAfterLoad;
            _pendingLibraryFolderId = pendingLibraryFolderId;
            _isNotebookDraft = isNotebookDraft;
            Loaded += async (s, e) => await LoadPdfAsync(filePath);
        }

        public void UpdateCurrentPdfPath(string filePath)
        {
            _currentPdfPath = filePath;
        }

        private bool IsEditableTextInputFocused()
        {
            if (Keyboard.FocusedElement is not DependencyObject focusedElement)
                return false;

            var textBoxBase = FindAncestor<TextBoxBase>(focusedElement);
            if (textBoxBase != null)
                return textBoxBase.IsEnabled && !textBoxBase.IsReadOnly;

            var comboBox = FindAncestor<ComboBox>(focusedElement);
            return comboBox != null && comboBox.IsEnabled && comboBox.IsEditable;
        }

        private async Task<bool> TryHandleUndoRedoShortcutAsync(KeyEventArgs e)
        {
            if (IsEditableTextInputFocused())
                return false;

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                e.Handled = true;
                await PerformUndoAsync();
                return true;
            }

            if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) ||
                (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z))
            {
                e.Handled = true;
                await PerformRedoAsync();
                return true;
            }

            return false;
        }

        private async void EditorPage_KeyDown(object sender, KeyEventArgs e)
        {
            if (_documentInteractionBlocked || _resourcesReleased)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                CloseTransientUi("escape");
                ActivateTool(ToolType.None);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                e.Handled = true;
                await SaveAnnotationsToPdfAsync();
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.P)
            {
                e.Handled = true;
                await PrintPdfAsync();
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                if (_activeSelectionPage != null && _activeSelectionPage.HasSelection)
                {
                    CopySelection();
                    var mw = Window.GetWindow(this) as MainWindow;
                    mw?.ShowToast(LocalizationService.Get("Editor.Copy"), "\uE8C8", 1500);
                    e.Handled = true;
                }
                else if (TryCopySelectedPdfTextToClipboard())
                {
                    var mw = Window.GetWindow(this) as MainWindow;
                    mw?.ShowToast(LocalizationService.Get("Editor.TextCopied"), "\uE8C8", 1500);
                    e.Handled = true;
                }
                else if (_activeSelectionPage != null && _activeSelectionPage.HasSelection)
                {
                    CopySelection();
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.X)
            {
                if (_activeSelectionPage != null && _activeSelectionPage.HasSelection)
                {
                    CutSelection();
                    var mw = Window.GetWindow(this) as MainWindow;
                    mw?.ShowToast(LocalizationService.Get("Editor.Cut"), "\uE8C6", 1500); // ✂ icon roughly
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                // Task 19: a bitmap on the clipboard wins over annotation JSON.
                if (!PasteClipboardImage())
                    PasteSelection();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
            {
                // TextBoxes don't consume Ctrl+D (unlike Ctrl+C), so guard
                // explicitly: never duplicate while a text box is being edited.
                if (!IsEditableTextInputFocused() &&
                    _activeSelectionPage != null && _activeSelectionPage.HasSelection)
                {
                    DuplicateSelection();
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.D0 || e.Key == Key.NumPad0))
            {
                SetZoom(1.0);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.OemPlus || e.Key == Key.Add))
            {
                SetZoom(_zoomLevel + ZoomStep);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
            {
                SetZoom(_zoomLevel - ZoomStep);
                e.Handled = true;
            }
        }

        private void PdfScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Zoom around mouse position
                var mousePos = e.GetPosition(PdfScrollViewer);
                double oldZoom = _zoomLevel;
                double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
                double newZoom = Math.Max(ZoomMin, Math.Min(ZoomMax, _zoomLevel + delta));

                if (Math.Abs(newZoom - oldZoom) > 0.001)
                    ZoomAroundPoint(newZoom, mousePos);

                e.Handled = true;
                return;
            }

            e.Handled = true;

            if (!_smoothScrollInitialized)
            {
                _targetVerticalOffset = PdfScrollViewer.VerticalOffset;
                _targetHorizontalOffset = PdfScrollViewer.HorizontalOffset;
                _smoothScrollInitialized = true;
            }

            // Shift+Wheel 闁?horizontal scroll
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                double scrollAmount = -e.Delta * 0.8;
                _targetHorizontalOffset = Math.Max(0,
                    Math.Min(PdfScrollViewer.ScrollableWidth, _targetHorizontalOffset + scrollAmount));

                AnimateHorizontalScroll(_targetHorizontalOffset);
                return;
            }

            // Normal wheel 闁?vertical scroll
            double vScrollAmount = -e.Delta * 0.8;
            _targetVerticalOffset = Math.Max(0,
                Math.Min(PdfScrollViewer.ScrollableHeight, _targetVerticalOffset + vScrollAmount));

            AnimateScroll(_targetVerticalOffset);
        }

        private void CancelSmoothScroll()
        {
            if (_isScrollAnimating)
            {
                _isScrollAnimating = false;
                System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_ScrollRendering;
            }
            if (_isHScrollAnimating)
            {
                _isHScrollAnimating = false;
                System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_HScrollRendering;
            }
        }

        private void SyncSmoothScrollState(bool cancelAnimations = false)
        {
            if (cancelAnimations)
                CancelSmoothScroll();

            _targetVerticalOffset = PdfScrollViewer.VerticalOffset;
            _targetHorizontalOffset = PdfScrollViewer.HorizontalOffset;
            _smoothScrollInitialized = true;
        }

        // ─────────────────────────────────────────────────────────────
        // Task 11: Scrollbar track click-to-jump — 点击滚动条轨道任意
        // 位置，thumb 立即跳到点击点（点击点成为 thumb 中心）。无分页
        // 步进、无动画；thumb 拖拽保持原生行为。
        // 挂接方式：对 PdfScrollViewer 模板内的两个 ScrollBar 以
        // PreviewMouseLeftButtonDown 拦截（隧道，先于任何 Track 默认
        // 行为），不改 App.xaml 模板。
        // ─────────────────────────────────────────────────────────────

        private void InstallScrollbarTrackJump()
        {
            // Loaded 时模板已应用，模板命名部件可直接寻址；Loaded 可能
            // 反复触发（进出视觉树），Remove+Add 同一 handler 幂等，也
            // 覆盖模板被重新应用生成新 ScrollBar 实例的情况。
            var verticalBar = PdfScrollViewer.Template?.FindName("PART_VerticalScrollBar", PdfScrollViewer) as ScrollBar;
            var horizontalBar = PdfScrollViewer.Template?.FindName("PART_HorizontalScrollBar", PdfScrollViewer) as ScrollBar;

            foreach (var bar in new[] { verticalBar, horizontalBar })
            {
                if (bar == null) continue;
                var handler = new MouseButtonEventHandler(ScrollBarTrackJump_MouseLeftButtonDown);
                bar.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, handler);
                bar.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, handler, false);
            }
        }

        private void ScrollBarTrackJump_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ScrollBar bar) return;

            // PART_Track 是 WPF ScrollBar 模板契约部件（Sleek 模板已命名）
            var track = bar.Template?.FindName("PART_Track", bar) as Track;
            var thumb = track?.Thumb;
            if (track == null || thumb == null) return;

            // Thumb（含其模板子元素）上的点击走原生拖拽，不拦截
            if (e.OriginalSource is DependencyObject source && IsDescendantOf(source, thumb))
                return;

            bool vertical = bar.Orientation == Orientation.Vertical;
            double thumbLength = vertical ? thumb.ActualHeight : thumb.ActualWidth;
            double trackLength = vertical ? track.ActualHeight : track.ActualWidth;
            if (trackLength <= 0 || trackLength <= thumbLength) return; // 无可滚动空间

            // 点击点相对 Track；垂直 Track IsDirectionReversed=True（值增大
            // 方向朝上），先归一化为“值增大方向”的坐标
            double clickPos = vertical ? e.GetPosition(track).Y : e.GetPosition(track).X;
            if (vertical && track.IsDirectionReversed)
                clickPos = trackLength - clickPos;

            // 点击点成为 thumb 中心 → 换算成 thumb 中心可移动区间 [thumbLen/2,
            // trackLen-thumbLen/2] 内的比例，clamp 到 [0,1]
            double ratio = (clickPos - thumbLength / 2.0) / (trackLength - thumbLength);
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));

            // 立即跳转（绕过滚轮平滑滚动动画），并同步平滑滚动状态机，
            // 保证后续滚轮动画从新 offset 起算
            CancelSmoothScroll();
            if (vertical)
                PdfScrollViewer.ScrollToVerticalOffset(ratio * PdfScrollViewer.ScrollableHeight);
            else
                PdfScrollViewer.ScrollToHorizontalOffset(ratio * PdfScrollViewer.ScrollableWidth);
            SyncSmoothScrollState();

            // 终止事件继续路由，杜绝 Track 默认分页步进（如有）
            e.Handled = true;
        }

        // 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?Zoom around a point (keeps that point stable on screen) 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?
        private void ZoomAroundPoint(double newZoom, Point viewportPoint)
        {
            if (IsSelectablePdfSurfaceActive)
            {
                ApplySelectableViewerZoom(newZoom, viewportPoint);
                return;
            }

            CancelSmoothScroll();
            double oldZoom = _zoomLevel;

            // Convert viewport point to content coordinates.
            double contentX = (PdfScrollViewer.HorizontalOffset + viewportPoint.X) / oldZoom;
            double contentY = (PdfScrollViewer.VerticalOffset + viewportPoint.Y) / oldZoom;

            ApplyCustomZoom(newZoom);

            // Task 12.1: compute and apply the corrected scroll offsets in the
            // SAME layout pass. The old Dispatcher.BeginInvoke(Render) gap let
            // one frame render at the new zoom with the old offsets — a
            // visible jump. Forcing layout here makes the new extent available
            // (so ScrollTo* clamps against fresh ScrollableWidth/Height), and
            // a second UpdateLayout commits both offsets before the next
            // render. ScrollChanged fires synchronously during these layout
            // passes, but its handler only debounces lazy re-renders and syncs
            // animation targets — it never writes scroll offsets, so it cannot
            // fight this correction.
            PdfScrollViewer.UpdateLayout();

            double newOffsetX = Math.Max(0, contentX * _zoomLevel - viewportPoint.X);
            double newOffsetY = Math.Max(0, contentY * _zoomLevel - viewportPoint.Y);

            PdfScrollViewer.ScrollToHorizontalOffset(newOffsetX);
            PdfScrollViewer.ScrollToVerticalOffset(newOffsetY);
            PdfScrollViewer.UpdateLayout();

            SyncSmoothScrollState();
        }

        // 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?Touch Manipulation (pinch-to-zoom + pan) 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?
        private void PdfScrollViewer_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            // Cancel manipulation if user is touching the toolbar — we don't want
            // the ScrollViewer manipulation to swallow toolbar button taps.
            if (e.OriginalSource is DependencyObject touchOrigin && IsDescendantOf(touchOrigin, ToolbarBorder))
            {
                e.Cancel();
                return;
            }

            e.ManipulationContainer = PdfScrollViewer;
            e.Mode = ManipulationModes.Scale | ManipulationModes.Translate;
            _manipulationBaseZoom = _zoomLevel;
            e.Handled = true;
        }

        private void PdfScrollViewer_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // If our raw-touch pinch is active, skip manipulation-based handling
            // to avoid conflicting pan during a 2-finger zoom gesture.
            if (_isPinchActive)
            {
                e.Handled = true;
                return;
            }

            // Pan (translate) for single-finger navigation (ToolType.None)
            double panX = e.DeltaManipulation.Translation.X;
            double panY = e.DeltaManipulation.Translation.Y;

            CancelSmoothScroll();
            PdfScrollViewer.ScrollToHorizontalOffset(PdfScrollViewer.HorizontalOffset - panX);
            PdfScrollViewer.ScrollToVerticalOffset(PdfScrollViewer.VerticalOffset - panY);

            // Sync smooth scroll state
            _targetVerticalOffset = PdfScrollViewer.VerticalOffset;
            _targetHorizontalOffset = PdfScrollViewer.HorizontalOffset;
            _smoothScrollInitialized = true;

            e.Handled = true;
        }

        private void PdfScrollViewer_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
        {
            // Deceleration for flick-to-scroll
            e.TranslationBehavior.DesiredDeceleration = 0.002; // DIPs per ms^2
            e.ExpansionBehavior.DesiredDeceleration = 0.0001;
            e.Handled = true;
        }

        private void PdfScrollViewer_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            var pos = e.GetTouchPoint(PdfScrollViewer).Position;
            _activeTouches[e.TouchDevice.Id] = pos;
            _activeTouchCount++;

            // NOTE: PreviewTouchDown fires for FINGER touches only.
            // The stylus/pen arrives via PreviewStylusDown, not TouchDown.
            // So any touch event here is a finger — we can safely treat it as scroll input.
            // When a drawing tool is active, the InkCanvas has IsHitTestVisible=true, which would
            // swallow this event. By marking it Handled in this tunneling phase, we prevent InkCanvas
            // from capturing it, allowing ManipulationDelta to handle single-finger panning.

            if (_activeTouches.Count == 2)
            {
                // Second finger: always handle for pinch-zoom tracking
                var pts = new List<Point>(_activeTouches.Values);
                double dist = PinchDistance(pts[0], pts[1]);
                if (dist > 10)
                {
                    _pinchStartDistance = dist;
                    _pinchStartZoom = _zoomLevel;
                    _isPinchActive = true;
                }
                e.Handled = true; // Consume so InkCanvas doesn't see a second-finger stroke start
            }
            else if (_applicationSettings?.PenOnlyMode == true &&
                     (_currentTool == ToolType.Pen ||
                      _currentTool == ToolType.Highlighter ||
                      _currentTool == ToolType.HiddenInk ||
                      _currentTool == ToolType.Eraser ||
                      _currentTool == ToolType.Shape ||
                      _currentTool == ToolType.Laser))
            {
                // Pen-only mode turns single-finger input into navigation
                // while an immediate drawing tool is active. When the
                // setting is off, the active page receives touch normally.
                e.Handled = true;
            }
        }



        private void PdfScrollViewer_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (!_activeTouches.ContainsKey(e.TouchDevice.Id)) return;
            _activeTouches[e.TouchDevice.Id] = e.GetTouchPoint(PdfScrollViewer).Position;

            if (_isPinchActive && _activeTouches.Count >= 2)
            {
                var pts = new List<Point>(_activeTouches.Values);
                double newDist = PinchDistance(pts[0], pts[1]);
                if (_pinchStartDistance > 5 && newDist > 0)
                {
                    double newZoom = Math.Max(ZoomMin, Math.Min(ZoomMax,
                        _pinchStartZoom * (newDist / _pinchStartDistance)));
                    var center = new Point((pts[0].X + pts[1].X) / 2.0, (pts[0].Y + pts[1].Y) / 2.0);
                    ZoomAroundPoint(newZoom, center);
                }
                e.Handled = true;
            }
        }

        private void PdfScrollViewer_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            _activeTouches.Remove(e.TouchDevice.Id);
            if (_activeTouchCount > 0) _activeTouchCount--;
            if (_activeTouches.Count < 2)
                _isPinchActive = false;
        }

        private static double PinchDistance(Point a, Point b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        // 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?Smooth scroll animation (vertical) 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?
        private double _scrollAnimationTarget;
        private double _scrollAnimationStart;
        private DateTime _scrollAnimationStartTime;
        private TimeSpan _scrollAnimationDuration;
        private bool _isScrollAnimating;

        // 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?Smooth scroll animation (horizontal) 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?
        private double _hScrollAnimationTarget;
        private double _hScrollAnimationStart;
        private DateTime _hScrollAnimationStartTime;
        private TimeSpan _hScrollAnimationDuration;
        private bool _isHScrollAnimating;

        private void AnimateScroll(double toOffset)
        {
            _scrollAnimationTarget = toOffset;
            _scrollAnimationStart = PdfScrollViewer.VerticalOffset;
            _scrollAnimationStartTime = DateTime.UtcNow;
            _scrollAnimationDuration = ThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(180));

            if (_scrollAnimationDuration == TimeSpan.Zero)
            {
                _isScrollAnimating = false;
                System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_ScrollRendering;
                PdfScrollViewer.ScrollToVerticalOffset(_scrollAnimationTarget);
                return;
            }

            if (!_isScrollAnimating)
            {
                _isScrollAnimating = true;
                System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_ScrollRendering;
            }
        }

        private void AnimateHorizontalScroll(double toOffset)
        {
            _hScrollAnimationTarget = toOffset;
            _hScrollAnimationStart = PdfScrollViewer.HorizontalOffset;
            _hScrollAnimationStartTime = DateTime.UtcNow;
            _hScrollAnimationDuration = ThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(180));

            if (_hScrollAnimationDuration == TimeSpan.Zero)
            {
                _isHScrollAnimating = false;
                System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_HScrollRendering;
                PdfScrollViewer.ScrollToHorizontalOffset(_hScrollAnimationTarget);
                return;
            }

            if (!_isHScrollAnimating)
            {
                _isHScrollAnimating = true;
                System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_HScrollRendering;
            }
        }

        private void CompositionTarget_HScrollRendering(object sender, EventArgs e)
        {
            if (_hScrollAnimationDuration == TimeSpan.Zero || !ThemeService.ShouldAnimate)
            {
                PdfScrollViewer.ScrollToHorizontalOffset(_hScrollAnimationTarget);
                _isHScrollAnimating = false;
                System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_HScrollRendering;
                return;
            }

            var elapsed = DateTime.UtcNow - _hScrollAnimationStartTime;
            double progress = Math.Min(1.0, elapsed.TotalMilliseconds / _hScrollAnimationDuration.TotalMilliseconds);
            double easedProgress = 1.0 - Math.Pow(1.0 - progress, 3);

            double currentOffset = _hScrollAnimationStart + (_hScrollAnimationTarget - _hScrollAnimationStart) * easedProgress;
            PdfScrollViewer.ScrollToHorizontalOffset(currentOffset);

            if (progress >= 1.0)
            {
                _isHScrollAnimating = false;
                System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_HScrollRendering;
            }
        }

        private void CompositionTarget_ScrollRendering(object sender, EventArgs e)
        {
            if (_scrollAnimationDuration == TimeSpan.Zero || !ThemeService.ShouldAnimate)
            {
                PdfScrollViewer.ScrollToVerticalOffset(_scrollAnimationTarget);
                _isScrollAnimating = false;
                System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_ScrollRendering;
                return;
            }

            var elapsed = DateTime.UtcNow - _scrollAnimationStartTime;
            double progress = Math.Min(1.0, elapsed.TotalMilliseconds / _scrollAnimationDuration.TotalMilliseconds);
            double easedProgress = 1.0 - Math.Pow(1.0 - progress, 3);

            double currentOffset = _scrollAnimationStart + (_scrollAnimationTarget - _scrollAnimationStart) * easedProgress;
            PdfScrollViewer.ScrollToVerticalOffset(currentOffset);

            if (progress >= 1.0)
            {
                _isScrollAnimating = false;
                System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_ScrollRendering;
            }
        }

        private void PdfScrollViewer_PreviewStylusDown(object sender, StylusDownEventArgs e)
        {
            // Only handle pen (not finger touch) for stylus-drag scrolling.
            // Finger touch should go through to ManipulationDelta for pinch-to-zoom.
            // Include both Stylus and Touch tablet types 闁?some Huawei MateBook
            // digitizers report the M-Pencil as Touch rather than Stylus.
            bool isPenDevice = e.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Stylus;

            // Heuristic: single-point non-finger device is likely a pen.
            // This catches Huawei M-Pencil on MateBooks that report as Touch.
            if (!isPenDevice && e.StylusDevice != null)
            {
                var tabletDevice = e.StylusDevice.TabletDevice;
                // A real finger touch typically has TabletDeviceType.Touch.
                // But a pen-as-touch has a single StylusDevice with StylusButtons
                // (real fingers don't have buttons). Check for barrel/eraser buttons.
                if (tabletDevice != null && e.StylusDevice.StylusButtons.Count > 1)
                {
                    isPenDevice = true;
                    Console.WriteLine($"[EditorPage] Detected pen-as-touch device: {tabletDevice.Name}, buttons={e.StylusDevice.StylusButtons.Count}");
                }
            }

            // Task 11: 滚动条上的笔输入放行（轨道点击跳转 / thumb 原生拖拽），
            // 不进入笔拖动滚动模式
            if (_currentTool == ToolType.None && isPenDevice &&
                !IsOriginalSourceOverScrollbar(e.OriginalSource as DependencyObject))
            {
                _isPenScrolling = true;
                _penScrollStartPoint = e.GetPosition(PdfScrollViewer);
                _penScrollStartVerticalOffset = PdfScrollViewer.VerticalOffset;
                _penScrollStartHorizontalOffset = PdfScrollViewer.HorizontalOffset;
                PdfScrollViewer.CaptureStylus();
                e.Handled = true;
            }
        }

        private void PdfScrollViewer_PreviewStylusMove(object sender, StylusEventArgs e)
        {
            if (_isPenScrolling && _currentTool == ToolType.None)
            {
                CancelSmoothScroll();
                Point currentPoint = e.GetPosition(PdfScrollViewer);
                double deltaY = currentPoint.Y - _penScrollStartPoint.Y;
                double deltaX = currentPoint.X - _penScrollStartPoint.X;

                PdfScrollViewer.ScrollToVerticalOffset(_penScrollStartVerticalOffset - deltaY);
                PdfScrollViewer.ScrollToHorizontalOffset(_penScrollStartHorizontalOffset - deltaX);
                e.Handled = true;
            }
        }

        private void PdfScrollViewer_PreviewStylusUp(object sender, StylusEventArgs e)
        {
            if (_isPenScrolling)
            {
                _isPenScrolling = false;
                PdfScrollViewer.ReleaseStylusCapture();
                e.Handled = true;
            }
        }

        // 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?Middle mouse button panning 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋?
        private void PdfScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _isMiddleMousePanning = true;
                _middleMouseStartPoint = e.GetPosition(PdfScrollViewer);
                _middleMouseStartVerticalOffset = PdfScrollViewer.VerticalOffset;
                _middleMouseStartHorizontalOffset = PdfScrollViewer.HorizontalOffset;
                PdfScrollViewer.CaptureMouse();
                PdfScrollViewer.Cursor = Cursors.ScrollAll;
                e.Handled = true;
                return;
            }

            // Task 11: 滚动条上的左键点击（轨道跳转 / thumb 原生拖拽）放行，
            // 不进入 Select 工具的选择委托（页面 50px buffer 会覆盖滚动条区域）
            if (e.ChangedButton == MouseButton.Left &&
                IsOriginalSourceOverScrollbar(e.OriginalSource as DependencyObject))
                return;

            if (e.ChangedButton == MouseButton.Left && _currentTool == ToolType.Select)
            {
                Controls.PdfPageControl closestPage = null;
                double closestDistance = double.MaxValue;

                foreach (var page in _pageControls)
                {
                    if (page.IsSelectionMode)
                    {
                        var ptInPage = e.GetPosition(page);

                        // Check both X and Y bounds with a 50px buffer
                        if (ptInPage.X >= -50 && ptInPage.X <= page.ActualWidth + 50 &&
                            ptInPage.Y >= -50 && ptInPage.Y <= page.ActualHeight + 50)
                        {
                            // Calculate center-point distance to find the closest if overlapping
                            // (or just grab the first valid under the cursor)
                            double distance = Math.Sqrt(Math.Pow(ptInPage.X - (page.ActualWidth / 2), 2) +
                                                        Math.Pow(ptInPage.Y - (page.ActualHeight / 2), 2));

                            if (distance < closestDistance)
                            {
                                closestDistance = distance;
                                closestPage = page;
                            }
                        }
                    }
                }

                var targetPage = closestPage;

                if (targetPage == null || !targetPage.IsSelectionMode) return;

                var point = e.GetPosition(targetPage);

                // Task 7.3: Ctrl+click multi-select is same-page scoped. When
                // Ctrl+clicking a different page while another page holds the
                // selection, clear that selection first — the toggle on the
                // new page then selects the clicked item naturally.
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                    _activeSelectionPage != null && _activeSelectionPage != targetPage &&
                    _activeSelectionPage.HasSelection)
                {
                    _activeSelectionPage.ClearSelection();
                }

                targetPage.InvokeSelectionMouseDownCore(point);

                _isDelegatingSelection = true;
                _selectionDelegateTarget = targetPage;
                targetPage.SelectionOverlayCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void PdfScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isMiddleMousePanning)
            {
                Point currentPoint = e.GetPosition(PdfScrollViewer);
                double deltaY = currentPoint.Y - _middleMouseStartPoint.Y;
                double deltaX = currentPoint.X - _middleMouseStartPoint.X;

                PdfScrollViewer.ScrollToVerticalOffset(_middleMouseStartVerticalOffset - deltaY);
                PdfScrollViewer.ScrollToHorizontalOffset(_middleMouseStartHorizontalOffset - deltaX);
                e.Handled = true;
                return;
            }

            if (_isDelegatingSelection && _selectionDelegateTarget != null)
            {
                var point = e.GetPosition(_selectionDelegateTarget);
                _selectionDelegateTarget.InvokeSelectionMouseMoveCore(point);
                e.Handled = true;
            }
        }

        private void PdfScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isMiddleMousePanning && e.ChangedButton == MouseButton.Middle)
            {
                _isMiddleMousePanning = false;
                PdfScrollViewer.ReleaseMouseCapture();
                PdfScrollViewer.Cursor = Cursors.Arrow;
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left && _isDelegatingSelection && _selectionDelegateTarget != null)
            {
                _selectionDelegateTarget.InvokeSelectionMouseUpCore();
                if (_selectionDelegateTarget.SelectionOverlayCanvas.IsMouseCaptured)
                {
                    _selectionDelegateTarget.SelectionOverlayCanvas.ReleaseMouseCapture();
                }

                _isDelegatingSelection = false;
                _selectionDelegateTarget = null;
                e.Handled = true;
            }
        }

        private void SetZoom(double level)
        {
            if (IsSelectablePdfSurfaceActive)
            {
                ApplySelectableViewerZoom(level);
                return;
            }

            ApplyCustomZoom(level);
        }

        private void ApplyCustomZoom(double level)
        {
            _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax, level));
            PagesZoomTransform.ScaleX = _zoomLevel;
            PagesZoomTransform.ScaleY = _zoomLevel;

            UpdateZoomLabel();

            SyncSmoothScrollState();

            // Re-render pages at higher DPI when zoomed in (debounced)
            ScheduleReRenderForZoom();
        }

        private void ScheduleReRenderForZoom()
        {
            if (!_isHostActive || _resourcesReleased)
                return;

            _reRenderCts?.Cancel();
            SetBitmapScalingMode(GetVisiblePageControls(), BitmapScalingMode.LowQuality);
            _zoomRenderDebounceTimer.Stop();
            _zoomRenderDebounceTimer.Start();
        }

        private async void ZoomRenderDebounceTimer_Tick(object sender, EventArgs e)
        {
            _zoomRenderDebounceTimer.Stop();
            _reRenderCts?.Cancel();
            _reRenderCts?.Dispose();
            _reRenderCts = new CancellationTokenSource();
            var token = _reRenderCts.Token;

            try
            {
                token.ThrowIfCancellationRequested();
                if (!_isHostActive || _resourcesReleased)
                    return;

                // Only re-render if zoom changed significantly from last render
                var profile = PdfRenderPolicy.GetProfile(CurrentPerformanceMode);
                double neededScale = Math.Min(Math.Max(_zoomLevel, 1.0), profile.MaxRenderScale);

                // Only re-render pages currently visible in the viewport
                var visiblePages = GetVisiblePageControls();
                if (Math.Abs(neededScale - _lastRenderedDpiScale) >= 0.15)
                {
                    _lastRenderedDpiScale = neededScale;
                    _pagesRenderedAtScale.Clear();
                    await ReRenderPagesAsync(visiblePages, neededScale, token);
                }

                TrimPageBitmapWorkingSet(visiblePages);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (!token.IsCancellationRequested && _isHostActive && !_zoomRenderDebounceTimer.IsEnabled)
                    SetBitmapScalingMode(GetVisiblePageControls(), BitmapScalingMode.HighQuality);
            }
        }

        /// <summary>
        /// Called on scroll to lazily render pages that are entering the viewport and
        /// re-render visible pages at higher DPI after zooming.
        /// </summary>
        private void PdfScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdatePageNumberIndicator();
            UpdateThumbnailSelection();
            UpdateBookmarkButton();
            UpdateSelectedTextBoxPopupVisibility(forceRefresh: e.VerticalChange != 0 || e.HorizontalChange != 0);

            if (!_isScrollAnimating)
                _targetVerticalOffset = PdfScrollViewer.VerticalOffset;

            if (!_isHScrollAnimating)
                _targetHorizontalOffset = PdfScrollViewer.HorizontalOffset;

            _smoothScrollInitialized = true;

            if (!_isHostActive || _resourcesReleased)
                return;

            SetBitmapScalingMode(GetVisiblePageControls(), BitmapScalingMode.LowQuality);

            _scrollReRenderCts?.Cancel();
            _scrollRenderDebounceTimer.Stop();
            _scrollRenderDebounceTimer.Start();
        }

        private async void ScrollRenderDebounceTimer_Tick(object sender, EventArgs e)
        {
            _scrollRenderDebounceTimer.Stop();
            _scrollReRenderCts?.Cancel();
            _scrollReRenderCts?.Dispose();
            _scrollReRenderCts = new CancellationTokenSource();
            var token = _scrollReRenderCts.Token;

            try
            {
                token.ThrowIfCancellationRequested();
                if (!_isHostActive || _resourcesReleased)
                    return;

                var visiblePages = GetVisiblePageControls();
                var needsInitialRender = visiblePages
                    .Where(p => !_pagesInitiallyRendered.Contains(p.PageIndex))
                    .ToList();

                foreach (var page in needsInitialRender)
                {
                    token.ThrowIfCancellationRequested();
                    await RenderPageInitialAsync(page, token);
                }

                if (_lastRenderedDpiScale > 1.0 && _pagesRenderedAtScale.Count < _pageControls.Count)
                {
                    var needsZoomRender = visiblePages
                        .Where(p => !_pagesRenderedAtScale.Contains(p.PageIndex))
                        .ToList();

                    if (needsZoomRender.Count > 0)
                        await ReRenderPagesAsync(needsZoomRender, _lastRenderedDpiScale, token);
                }

                // Task 12.3: pre-render unrendered pages adjacent (±1) to the
                // visible range at idle priority so scrolling into them shows
                // the bitmap instantly instead of swapping it in mid-scroll.
                QueueAdjacentPagePrerender(visiblePages, token);
                TrimPageBitmapWorkingSet(visiblePages);

                UpdateSelectedTextBoxPopupVisibility(forceRefresh: true);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (!token.IsCancellationRequested && _isHostActive && !_scrollRenderDebounceTimer.IsEnabled)
                    SetBitmapScalingMode(GetVisiblePageControls(), BitmapScalingMode.HighQuality);
            }
        }

        private struct ScrollAnchor
        {
            public PdfPageControl AnchorPage;
            public double OffsetFromViewportTop;
        }

        private ScrollAnchor CaptureScrollAnchor()
        {
            var visiblePages = GetVisiblePageControls();
            if (visiblePages.Count == 0)
                return default;

            var anchorPage = visiblePages[0];
            return new ScrollAnchor
            {
                AnchorPage = anchorPage,
                OffsetFromViewportTop = GetScaledPageTop(anchorPage.PageIndex) - PdfScrollViewer.VerticalOffset
            };
        }

        private void RestoreScrollAnchor(ScrollAnchor anchor)
        {
            if (anchor.AnchorPage == null)
                return;

            double newOffset = GetScaledPageTop(anchor.AnchorPage.PageIndex) - anchor.OffsetFromViewportTop;
            PdfScrollViewer.ScrollToVerticalOffset(Math.Max(0, newOffset));
            SyncSmoothScrollState();
        }

        private async Task ReRenderPagesAsync(List<PdfPageControl> pages, double dpiScale, CancellationToken token)
        {
            foreach (var page in pages)
            {
                token.ThrowIfCancellationRequested();
                if (!_isHostActive || _resourcesReleased)
                    return;
                try
                {
                    double renderScale = PdfRenderPolicy.CalculateRenderScale(
                        CurrentPerformanceMode,
                        page.Width,
                        page.Height,
                        dpiScale);
                    var bitmapSource = await _pdfService.RenderPageBitmapSourceAsync(page.PageIndex, renderScale, token);
                    if (bitmapSource != null)
                    {
                        token.ThrowIfCancellationRequested();
                        page.PageSource = bitmapSource;
                        _pagesRenderedAtScale.Add(page.PageIndex);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }

        /// <summary>
        /// Task 12.3: queues an initial render for unrendered pages within ±1 of
        /// the visible range. RenderPageInitialAsync marks them in
        /// _pagesInitiallyRendered, so a later scroll into them shows the bitmap
        /// instantly (no mid-scroll swap). The candidate set is bounded by the
        /// ±1 window regardless of document size, so no page-count guard is
        /// needed even for very large documents.
        /// </summary>
        private void QueueAdjacentPagePrerender(List<PdfPageControl> visiblePages, CancellationToken token)
        {
            var profile = PdfRenderPolicy.GetProfile(CurrentPerformanceMode);
            if (!_isHostActive || !profile.PrefetchAdjacentPages || visiblePages.Count == 0) return;

            // GetVisiblePageControls returns pages in document order.
            int first = visiblePages[0].PageIndex;
            int last = visiblePages[visiblePages.Count - 1].PageIndex;

            var candidates = new Queue<PdfPageControl>();
            for (int i = Math.Max(0, first - 1); i <= Math.Min(_pageControls.Count - 1, last + 1); i++)
            {
                if (!_pagesInitiallyRendered.Contains(i))
                    candidates.Enqueue(_pageControls[i]);
            }

            if (candidates.Count == 0) return;
            ScheduleNextAdjacentPrerender(candidates, token);
        }

        /// <summary>
        /// Renders one adjacent candidate at a time at ApplicationIdle priority
        /// (never competes with input/render work), chaining the next page only
        /// after the previous render completes. The whole chain stops as soon as
        /// the scroll-generation token is cancelled (any new scroll event).
        /// </summary>
        private void ScheduleNextAdjacentPrerender(Queue<PdfPageControl> candidates, CancellationToken token)
        {
            if (!_isHostActive || candidates.Count == 0 || token.IsCancellationRequested) return;

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (!_isHostActive || token.IsCancellationRequested) return;

                var page = candidates.Dequeue();
                try
                {
                    await RenderPageInitialAsync(page, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                ScheduleNextAdjacentPrerender(candidates, token);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private string CurrentPerformanceMode
            => PdfRenderPolicy.NormalizeMode(_applicationSettings?.PerformanceMode);

        private static void SetBitmapScalingMode(IEnumerable<PdfPageControl> pages, BitmapScalingMode mode)
        {
            foreach (var page in pages)
                page.SetBitmapScalingMode(mode);
        }

        private void TrimPageBitmapWorkingSet(List<PdfPageControl> visiblePages)
        {
            if (!_isHostActive || visiblePages.Count == 0)
                return;

            int first = visiblePages[0].PageIndex;
            int last = visiblePages[visiblePages.Count - 1].PageIndex;
            var retained = new HashSet<int>(PdfRenderPolicy.GetRetainedPageIndices(
                first,
                last,
                _pageControls.Count,
                CurrentPerformanceMode));

            foreach (var page in _pageControls)
            {
                if (retained.Contains(page.PageIndex) || page.PageSource == null)
                    continue;

                page.PageSource = null;
                _pagesInitiallyRendered.Remove(page.PageIndex);
                _pagesRenderedAtScale.Remove(page.PageIndex);
            }
        }

        private double GetScaledPageTop(int pageIndex)
            => pageIndex >= 0 && pageIndex < _pageTopOffsets.Count ? _pageTopOffsets[pageIndex] * _zoomLevel : 0;

        private double GetScaledPageHeight(int pageIndex)
            => pageIndex >= 0 && pageIndex < _pageHeights.Count ? _pageHeights[pageIndex] * _zoomLevel : 0;

        private int FindFirstVisiblePageIndex(double viewTop)
        {
            int lo = 0;
            int hi = _pageControls.Count - 1;
            int result = _pageControls.Count - 1;

            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                double pageBottom = GetScaledPageTop(mid) + GetScaledPageHeight(mid);
                if (pageBottom >= viewTop)
                {
                    result = mid;
                    hi = mid - 1;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            return Math.Max(0, result);
        }

        private List<PdfPageControl> GetVisiblePageControls()
        {
            var result = new List<PdfPageControl>();
            if (_pageControls.Count == 0)
                return result;

            double viewportHeight = PdfScrollViewer.ViewportHeight;
            if (viewportHeight <= 0)
            {
                int initialCount = Math.Min(2, _pageControls.Count);
                for (int i = 0; i < initialCount; i++)
                    result.Add(_pageControls[i]);
                return result;
            }

            double viewTop = Math.Max(0, PdfScrollViewer.VerticalOffset - (viewportHeight * 0.5));
            double viewBottom = PdfScrollViewer.VerticalOffset + viewportHeight + (viewportHeight * 0.5);
            int startIndex = FindFirstVisiblePageIndex(viewTop);

            for (int i = startIndex; i < _pageControls.Count; i++)
            {
                double pageTop = GetScaledPageTop(i);
                if (pageTop > viewBottom)
                    break;

                double pageBottom = pageTop + GetScaledPageHeight(i);
                if (pageBottom >= viewTop)
                    result.Add(_pageControls[i]);
            }

            return result;
        }

        private async Task PerformUndoAsync()
        {
            if (_undoStack.Count == 0) return;
            if (!TryBeginDocumentEdit(out var editLease))
                return;

            using (editLease)
            {
                var action = _undoStack[_undoStack.Count - 1];
                using var operationLease = CaptureDocumentOperationLease(_pdfService);
                if (action is DocumentSnapshotAction snapshotAction)
                    snapshotAction.SetOperationLease(operationLease);
                try
                {
                    await action.UndoAsync();
                    if (action is SelectionCrossPageMoveAction crossPageMove
                        && !crossPageMove.LastOperationSucceeded)
                        return;
                    if (action is StrokesErasedAction strokesErased
                        && !strokesErased.LastOperationSucceeded)
                        return;
                    if (action is DocumentSnapshotAction snapshot
                        ? !snapshot.LastOperationSucceeded ||
                          !ValidateDocumentOperationLease(snapshot.CompletedOperationLease)
                        : !ValidateDocumentOperationLease(operationLease))
                    {
                        return;
                    }
                    _undoStack.RemoveAt(_undoStack.Count - 1);
                    _redoStack.Add(action);
                    UpdateUndoRedoButtons();
                    ApplyDirtyStateForAction(action);
                    UpdateSelectedTextBoxPopupVisibility(forceRefresh: true);
                }
                catch (Exception ex)
                {
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.UndoFailed", ex.Message), "\uE783", 3500);
                }
                finally
                {
                    if (action is DocumentSnapshotAction completedSnapshotAction)
                        completedSnapshotAction.SetOperationLease(null);
                }
            }
        }

        private async Task PerformRedoAsync()
        {
            if (_redoStack.Count == 0) return;
            if (!TryBeginDocumentEdit(out var editLease))
                return;

            using (editLease)
            {
                var action = _redoStack[_redoStack.Count - 1];
                using var operationLease = CaptureDocumentOperationLease(_pdfService);
                if (action is DocumentSnapshotAction snapshotAction)
                    snapshotAction.SetOperationLease(operationLease);
                try
                {
                    await action.RedoAsync();
                    if (action is SelectionCrossPageMoveAction crossPageMove
                        && !crossPageMove.LastOperationSucceeded)
                        return;
                    if (action is StrokesErasedAction strokesErased
                        && !strokesErased.LastOperationSucceeded)
                        return;
                    if (action is DocumentSnapshotAction snapshot
                        ? !snapshot.LastOperationSucceeded ||
                          !ValidateDocumentOperationLease(snapshot.CompletedOperationLease)
                        : !ValidateDocumentOperationLease(operationLease))
                    {
                        return;
                    }
                    _redoStack.RemoveAt(_redoStack.Count - 1);
                    _undoStack.Add(action);
                    UpdateUndoRedoButtons();
                    ApplyDirtyStateForAction(action);
                    UpdateSelectedTextBoxPopupVisibility(forceRefresh: true);
                }
                catch (Exception ex)
                {
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.RedoFailed", ex.Message), "\uE783", 3500);
                }
                finally
                {
                    if (action is DocumentSnapshotAction completedSnapshotAction)
                        completedSnapshotAction.SetOperationLease(null);
                }
            }
        }

        private void UpdateUndoRedoButtons()
        {
            UndoButton.IsEnabled = _undoStack.Count > 0;
            RedoButton.IsEnabled = _redoStack.Count > 0;
        }

        private void ApplyDirtyStateForAction(IUndoAction action)
        {
            _documentSaveCoordinator.RecordChange(action.LeavesDocumentDirty);
            SyncDirtyStateMirror();
        }

        private void ClearUndoRedoHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            UpdateUndoRedoButtons();
        }

        private void PushUndoAction(IUndoAction action)
        {
            if (action == null)
                return;

            if (!TryBeginDocumentEdit(out var editLease))
            {
                // The underlying WPF event can arrive after it has already
                // changed the model.  Retain that generation for the close
                // save loop instead of silently dropping it.
                _documentSaveCoordinator.RecordChange(action.LeavesDocumentDirty);
                SyncDirtyStateMirror();
                return;
            }

            using (editLease)
            {
                _undoStack.Add(action);
                _redoStack.Clear();
                UpdateUndoRedoButtons();
                ApplyDirtyStateForAction(action);
            }
        }

        private async void UndoButton_Click(object sender, RoutedEventArgs e) => await PerformUndoAsync();
        private async void RedoButton_Click(object sender, RoutedEventArgs e) => await PerformRedoAsync();

        private void CreateToolPopups()
        {
            // Pen popup — with size preview section
            _penPopup = BuildToolPopup(
                LocalizationService.Get("Editor.PopupSize"), 0.5, 8, _penSize, 0.25,
                v => { _penSize = v; if (_penPopupSizePreview != null) _penPopupSizePreview.StrokeThickness = v; if (_currentTool == ToolType.Pen) ApplyToolToAllPages(); },
                LocalizationService.Get("Editor.PopupColor"), _penColor,
                c => { _penColor = c; if (_penPopupSizePreview != null) _penPopupSizePreview.Stroke = new SolidColorBrush(c); UpdateToolIconColors(); if (_currentTool == ToolType.Pen) ApplyToolToAllPages(); SaveSetting(s => RecordRecentColor(s.RecentPenColors, c)); },
                out _penPopupSizeSlider,
                () => AppSettingsService.Load().RecentPenColors,
                sizeAutomationId: "Editor.Pen.Size");
            _penPopupSizePreview = AddSizePreviewSection(_penPopup, _penSize, _penColor, isHighlighter: false);
            AddPenBehaviourToggles(_penPopup);
            AddPenSmoothingSection(_penPopup);
            EnableToolPopupScrolling(_penPopup);

            // Highlighter popup — with size preview section
            _highlighterPopup = BuildToolPopup(
                LocalizationService.Get("Editor.PopupSize"), 2, 48, _highlighterSize, 0.5,
                v => { _highlighterSize = v; if (_highlighterPopupSizePreview != null) _highlighterPopupSizePreview.StrokeThickness = v; UpdateHighlighterModePreviewVisuals(); if (_currentTool == ToolType.Highlighter) ApplyToolToAllPages(); },
                LocalizationService.Get("Editor.PopupColor"), _highlighterColor,
                c => { _highlighterColor = c; if (_highlighterPopupSizePreview != null) _highlighterPopupSizePreview.Stroke = new SolidColorBrush(GetHighlighterPreviewStrokeColor(HighlighterApplyMode.Freehand, c)); UpdateHighlighterModePreviewVisuals(); UpdateToolIconColors(); if (_currentTool == ToolType.Highlighter || _currentTool == ToolType.AreaHighlight) ApplyToolToAllPages(); SaveSetting(s => RecordRecentColor(s.RecentHighlighterColors, c)); },
                out _highlighterPopupSizeSlider,
                () => AppSettingsService.Load().RecentHighlighterColors,
                sizeAutomationId: "Editor.Highlighter.Size");
            _highlighterPopupSizePreview = AddSizePreviewSection(_highlighterPopup, _highlighterSize, _highlighterColor, isHighlighter: true);
            AddHighlighterModeSection(_highlighterPopup);
            EnableToolPopupScrolling(_highlighterPopup);

            _eraserPopup = BuildToolPopup(
                LocalizationService.Get("Editor.PopupEraserSize"), 4, 80, _eraserSize, 1,
                v => { _eraserSize = v; ShowEraserSizePreview(v); ApplyToolToAllPages(); },
                null, default, null,
                out _,
                sizeAutomationId: "Editor.Eraser.Size");
            AddEraserModeSection(_eraserPopup);
            EnableToolPopupScrolling(_eraserPopup);

            // Shape popup — sub-type selector above the shared size slider
            // and color palette (session-only state, no persistence).
            _shapePopup = BuildToolPopup(
                LocalizationService.Get("Editor.PopupSize"), 1, 20, _shapeSize, 0.5,
                v => { _shapeSize = v; if (_currentTool == ToolType.Shape) ApplyToolToAllPages(); },
                LocalizationService.Get("Editor.PopupColor"), _shapeColor,
                c => { _shapeColor = c; if (_currentTool == ToolType.Shape) ApplyToolToAllPages(); },
                out _,
                sizeAutomationId: "Editor.Shape.Size");
            AddShapeSubTypeSection(_shapePopup);
            EnableToolPopupScrolling(_shapePopup);

            CreateSelectionPopup();
        }

        /// <summary>
        /// Prepends the mutually-exclusive 3×3 shape selector to the popup.
        /// Selection is session-only and re-applied to all pages immediately.
        /// </summary>
        private void AddShapeSubTypeSection(Popup popup)
        {
            if (popup?.Child is not Border border || border.Child is not StackPanel panel)
                return;

            var header = ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.ShapeHeader"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var shapeGrid = new UniformGrid { Columns = 3 };
            var buttons = new Dictionary<ShapeKind, ToggleButton>();
            var choices = new (ShapeKind Kind, string Label, string AutomationId)[]
            {
                (ShapeKind.Line, LocalizationService.Get("Editor.ShapeLine"), "Editor.Shape.Line"),
                (ShapeKind.DashedLine, LocalizationService.Get("Editor.ShapeDashedLine"), "Editor.Shape.DashedLine"),
                (ShapeKind.Rectangle, LocalizationService.Get("Editor.ShapeRectangle"), "Editor.Shape.Rectangle"),
                (ShapeKind.Ellipse, LocalizationService.Get("Editor.ShapeEllipse"), "Editor.Shape.Ellipse"),
                (ShapeKind.Arrow, LocalizationService.Get("Editor.ShapeArrow"), "Editor.Shape.Arrow"),
                (ShapeKind.Triangle, LocalizationService.Get("Editor.ShapeTriangle"), "Editor.Shape.Triangle"),
                (ShapeKind.Diamond, LocalizationService.Get("Editor.ShapeDiamond"), "Editor.Shape.Diamond"),
                (ShapeKind.Parallelogram, LocalizationService.Get("Editor.ShapeParallelogram"), "Editor.Shape.Parallelogram"),
                (ShapeKind.Pentagon, LocalizationService.Get("Editor.ShapePentagon"), "Editor.Shape.Pentagon"),
                (ShapeKind.Hexagon, LocalizationService.Get("Editor.ShapeHexagon"), "Editor.Shape.Hexagon")
            };

            foreach (var choice in choices)
            {
                var button = BuildVectorModeToggleButton(
                    choice.Label,
                    choice.AutomationId,
                    BuildShapePreview(choice.Kind),
                    new Thickness(4),
                    activated: () => SelectKind(choice.Kind));
                buttons[choice.Kind] = button;
                shapeGrid.Children.Add(button);
            }

            void ApplyVisual()
            {
                foreach (var pair in buttons)
                    StyleVectorModeToggleButton(pair.Value, _shapeKind == pair.Key);
            }

            void SelectKind(ShapeKind kind)
            {
                if (_shapeKind == kind)
                    return;
                _shapeKind = kind;
                ApplyVisual();
                if (_currentTool == ToolType.Shape)
                    ApplyToolToAllPages();
            }

            // Sub-type section sits above the size slider.
            panel.Children.Insert(0, shapeGrid);
            panel.Children.Insert(0, header);
            ApplyVisual();
        }

        /// <summary>
        /// Prepends the pixel / whole-stroke eraser mode toggle section to
        /// the eraser popup. The selection is persisted in AppSettings and
        /// applied to all pages immediately.
        /// </summary>
        private void AddEraserModeSection(Popup popup)
        {
            if (popup?.Child is not Border border || border.Child is not StackPanel panel)
                return;

            var header = ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.EraserModeHeader"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            ToggleButton pixelButton = null!;
            ToggleButton wholeButton = null!;
            pixelButton = BuildModeToggleButton(LocalizationService.Get("Editor.EraserPixel"), new Thickness(0, 0, 8, 0), activated: () => SelectMode(false), automationId: "Editor.Eraser.Pixel");
            wholeButton = BuildModeToggleButton(LocalizationService.Get("Editor.EraserStroke"), new Thickness(0), activated: () => SelectMode(true), automationId: "Editor.Eraser.WholeStroke");

            void ApplyModeVisual()
            {
                bool whole = AppSettingsService.Load().WholeStrokeEraser;
                StyleModeToggleButton(pixelButton, active: !whole);
                StyleModeToggleButton(wholeButton, active: whole);
            }

            void SelectMode(bool whole)
            {
                var settings = AppSettingsService.Load();
                if (settings.WholeStrokeEraser == whole)
                    return;
                settings.WholeStrokeEraser = whole;
                // Keep the editor cache and the page controls on the same
                // snapshot. Calling the no-argument overload here would read
                // the stale _applicationSettings value and immediately undo
                // the mode the user just selected.
                _applicationSettings = AppSettingsService.Save(settings);
                ApplyToolToAllPages(_applicationSettings);
                ApplyModeVisual();
            }

            modeRow.Children.Add(pixelButton);
            modeRow.Children.Add(wholeButton);

            // Mode section sits above the size slider.
            panel.Children.Insert(0, modeRow);
            panel.Children.Insert(0, header);
            ApplyModeVisual();
        }

        private static ToggleButton BuildModeToggleButton(
            string label,
            Thickness margin,
            double width = 116,
            Action activated = null,
            string automationId = null)
        {
            var button = new ToggleButton
            {
                Width = width,
                Height = 32,
                MinWidth = 32,
                MinHeight = 32,
                Margin = margin,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = true,
                Content = new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            ApplyToolbarPopupToggleStyle(button);
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetAutomationId(button, automationId ?? "Editor.Popup.Mode");
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetHelpText(button, label);
            KeyboardNavigation.SetIsTabStop(button, true);
            if (activated != null)
            {
                button.Click += (_, e) =>
                {
                    activated();
                    // ToggleButton's default click behavior flips IsChecked
                    // before this handler. Keep a mutually-exclusive mode
                    // selected even when the user activates the already
                    // selected option (the callback may return early).
                    button.IsChecked = true;
                    e.Handled = true;
                };
            }

            return button;
        }

        private static void ApplyToolbarPopupToggleStyle(Control button)
        {
            if (Application.Current?.TryFindResource("ToolbarToggleButtonStyle") is Style toggleStyle)
                button.Style = toggleStyle;
            ApplyToolbarFocusVisualStyle(button);
        }

        private static void ApplyToolbarPopupButtonStyle(Button button)
        {
            if (Application.Current?.TryFindResource("ToolbarButtonStyle") is Style buttonStyle)
                button.Style = buttonStyle;
            ApplyToolbarFocusVisualStyle(button);
        }

        private static void ApplyToolbarFocusVisualStyle(Control control)
        {
            if (control != null && Application.Current?.TryFindResource("ToolbarFocusVisualStyle") is Style focusStyle)
                control.FocusVisualStyle = focusStyle;
        }

        private static void StyleModeToggleButton(ToggleButton button, bool active)
        {
            button.IsChecked = active;
            button.SetResourceReference(Control.BorderBrushProperty,
                active ? "ThemeAccentBrush" : "ThemeBorderBrush");
            button.SetResourceReference(Control.BackgroundProperty,
                active ? "ThemeSelectionBrush" : "ThemeSurfaceAltBrush");
            if (button.Content is TextBlock text)
            {
                text.SetResourceReference(
                    TextElement.ForegroundProperty,
                    active ? "ThemeAccentBrush" : "ThemeForegroundBrush");
            }
        }

        private static ToggleButton BuildVectorModeToggleButton(
            string label,
            string automationId,
            Path preview,
            Thickness margin,
            Action activated)
        {
            var button = new ToggleButton
            {
                Width = 56,
                Height = 42,
                MinWidth = 32,
                MinHeight = 32,
                Margin = margin,
                Padding = new Thickness(6),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = true,
                Tag = preview,
                ToolTip = label,
                Content = preview
            };
            ApplyToolbarPopupToggleStyle(button);
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetAutomationId(button, automationId);
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetHelpText(button, label);
            KeyboardNavigation.SetIsTabStop(button, true);
            button.Click += (_, e) =>
            {
                activated?.Invoke();
                button.IsChecked = true;
                e.Handled = true;
            };
            return button;
        }

        private static void StyleVectorModeToggleButton(ToggleButton button, bool active)
        {
            button.IsChecked = active;
            button.SetResourceReference(Control.BorderBrushProperty,
                active ? "ThemeAccentBrush" : "ThemeBorderBrush");
            button.SetResourceReference(Control.BackgroundProperty,
                active ? "ThemeSelectionBrush" : "ThemeSurfaceAltBrush");
            if (button.Tag is Path preview)
                preview.SetResourceReference(Path.StrokeProperty,
                    active ? "ThemeAccentBrush" : "ThemeForegroundBrush");
        }

        private void UpdateHighlighterModePreviewVisuals()
        {
            foreach (var pair in _highlighterModePreviews)
                ApplyHighlighterPreviewColor(pair.Key, pair.Value);
        }

        private void ApplyHighlighterPreviewColor(HighlighterApplyMode mode, Path preview)
        {
            if (preview == null)
                return;

            ApplyHighlighterPreviewVisual(mode, preview, _highlighterColor, _highlighterSize);
        }

        /// <summary>
        /// Production path shared by every highlighter mode preview. Keeping
        /// size and alpha in one helper prevents a language/theme popup rebuild
        /// or a live slider/color change from leaving one mode stale.
        /// </summary>
        private static void ApplyHighlighterPreviewVisual(
            HighlighterApplyMode mode,
            Path preview,
            Color color,
            double size)
        {
            if (preview == null)
                return;

            preview.StrokeThickness = GetHighlighterPreviewStrokeThickness(mode, size);
            preview.Stroke = new SolidColorBrush(GetHighlighterPreviewStrokeColor(mode, color));
            byte fillOpacity = GetHighlighterPreviewFillOpacity(mode);
            preview.Fill = fillOpacity == 0
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromArgb(fillOpacity, color.R, color.G, color.B));
        }

        private static double GetHighlighterPreviewStrokeThickness(HighlighterApplyMode mode, double size)
        {
            _ = mode;
            return Math.Max(1.0, size);
        }

        private static byte GetHighlighterPreviewStrokeOpacity(HighlighterApplyMode mode)
        {
            return mode switch
            {
                HighlighterApplyMode.Freehand => FreehandHighlighterOpacity,
                HighlighterApplyMode.TextHighlight => TextHighlightOpacity,
                HighlighterApplyMode.AreaHighlight => AreaHighlightStrokeOpacity,
                _ => byte.MaxValue
            };
        }

        private static byte GetHighlighterPreviewFillOpacity(HighlighterApplyMode mode)
        {
            return mode == HighlighterApplyMode.AreaHighlight
                ? AreaHighlightFillOpacity
                : (byte)0;
        }

        private static Color GetHighlighterPreviewStrokeColor(HighlighterApplyMode mode, Color color)
        {
            return Color.FromArgb(GetHighlighterPreviewStrokeOpacity(mode), color.R, color.G, color.B);
        }

        private static Path BuildShapePreview(ShapeKind kind)
        {
            var data = kind switch
            {
                ShapeKind.Rectangle => "M4,4 L28,4 L28,18 L4,18 Z",
                ShapeKind.Ellipse => "M16,4 A12,7 0 1 1 15.99,4",
                ShapeKind.Arrow => "M4,11 L26,11 M19,5 L26,11 L19,17",
                ShapeKind.Triangle => "M16,3 L29,20 L3,20 Z",
                ShapeKind.Diamond => "M16,2 L29,11 L16,20 L3,11 Z",
                ShapeKind.Parallelogram => "M9,3 H29 L23,20 H3 Z",
                ShapeKind.Pentagon => "M16,2 L29,9 L24,20 L8,20 L3,9 Z",
                ShapeKind.Hexagon => "M9,3 H23 L29,11 L23,20 H9 L3,11 Z",
                ShapeKind.DashedLine => "M4,17 L9,15 M13,13 L18,11 M22,9 L27,7",
                _ => "M4,17 L27,5"
            };

            return new Path
            {
                Width = 30,
                Height = 22,
                Stretch = Stretch.Uniform,
                Fill = Brushes.Transparent,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Data = Geometry.Parse(data)
            };
        }

        private static Path BuildSelectionShapePreview(SelectionShape shape)
        {
            var data = shape == SelectionShape.Rectangle
                ? "M4,4 L28,4 L28,18 L4,18 Z"
                : "M5,16 C7,7 10,19 13,10 C16,3 18,18 22,8 C23,6 25,7 27,5";

            return new Path
            {
                Width = 30,
                Height = 22,
                Stretch = Stretch.Uniform,
                Fill = Brushes.Transparent,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Data = Geometry.Parse(data)
            };
        }

        private static Path BuildHighlighterModePreview(HighlighterApplyMode mode)
        {
            var data = mode switch
            {
                HighlighterApplyMode.TextHighlight => "M3,9 L25,9 M3,15 L25,15",
                HighlighterApplyMode.Underline => "M3,16 L25,16",
                HighlighterApplyMode.StrikeOut => "M3,6 L25,18",
                HighlighterApplyMode.Squiggly => "M3,13 C6,7 8,19 11,13 S16,7 19,13 S22,19 25,13",
                HighlighterApplyMode.AreaHighlight => "M4,5 L24,5 L24,17 L4,17 Z",
                _ => "M3,14 C6,5 8,20 12,10 C15,4 18,17 21,8 C22,6 24,7 25,6"
            };

            return new Path
            {
                Width = 28,
                Height = 22,
                Stretch = Stretch.Uniform,
                Fill = Brushes.Transparent,
                StrokeThickness = 1.0,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Data = Geometry.Parse(data)
            };
        }

        private static TextBlock ThemeSubtleHeader(TextBlock textBlock)
        {
            textBlock.SetResourceReference(
                TextElement.ForegroundProperty,
                "ThemeSubtleForegroundBrush");
            return textBlock;
        }

        private static Border ThemeDivider(Border divider)
        {
            divider.SetResourceReference(Border.BackgroundProperty, "ThemeBorderBrush");
            divider.Opacity = 0.45;
            return divider;
        }

        /// <summary>
        /// Task 25/27: prepends the highlighter apply-mode selector to the
        /// highlighter popup. Freehand = classic ink highlighter (default);
        /// 文本高亮/下划线/删除线/波浪线 ride the PDF text-selection pipeline;
        /// 区域高亮 drags a free-form rectangle. Selection is session-only and
        /// switches the active tool immediately.
        /// </summary>
        private void AddHighlighterModeSection(Popup popup)
        {
            if (popup?.Child is not Border border || border.Child is not StackPanel panel)
                return;

            var header = ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.HighlighterModeHeader"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };

            var modes = new (HighlighterApplyMode Mode, string Label, string AutomationId)[]
            {
                (HighlighterApplyMode.Freehand, LocalizationService.Get("Editor.HighlighterFreehand"), "Editor.Highlighter.Freehand"),
                (HighlighterApplyMode.TextHighlight, LocalizationService.Get("Editor.HighlighterText"), "Editor.Highlighter.Text"),
                (HighlighterApplyMode.Underline, LocalizationService.Get("Editor.HighlighterUnderline"), "Editor.Highlighter.Underline"),
                (HighlighterApplyMode.StrikeOut, LocalizationService.Get("Editor.HighlighterStrikeOut"), "Editor.Highlighter.StrikeOut"),
                (HighlighterApplyMode.Squiggly, LocalizationService.Get("Editor.HighlighterSquiggly"), "Editor.Highlighter.Squiggly"),
                (HighlighterApplyMode.AreaHighlight, LocalizationService.Get("Editor.HighlighterArea"), "Editor.Highlighter.Area"),
            };

            var buttons = new Dictionary<HighlighterApplyMode, ToggleButton>();
            _highlighterModePreviews.Clear();

            void ApplyVisual()
            {
                foreach (var pair in buttons)
                {
                    StyleVectorModeToggleButton(pair.Value, pair.Key == _highlighterApplyMode);
                    if (pair.Value.Tag is Path preview)
                    {
                        _highlighterModePreviews[pair.Key] = preview;
                        ApplyHighlighterPreviewColor(pair.Key, preview);
                    }
                }
            }

            void SelectMode(HighlighterApplyMode mode)
            {
                if (_highlighterApplyMode == mode)
                    return;
                _highlighterApplyMode = mode;
                ApplyVisual();

                // Switch the live tool immediately so the user can start
                // applying right away; the popup stays open for further
                // color/size tweaks.
                ActivateTool(ToolType.None);
                ActivateHighlighterModeTool();
            }

            for (int i = 0; i < modes.Length; i++)
            {
                var mode = modes[i].Mode;
                var button = BuildVectorModeToggleButton(
                    modes[i].Label,
                    modes[i].AutomationId,
                    BuildHighlighterModePreview(mode),
                    new Thickness(0, 0, i % 3 < 2 ? 6 : 0, 0),
                    activated: () => SelectMode(mode));
                buttons[mode] = button;
                (i < 3 ? row1 : row2).Children.Add(button);
            }

            panel.Children.Insert(0, row2);
            panel.Children.Insert(0, row1);
            panel.Children.Insert(0, header);
            ApplyVisual();
        }

        /// <summary>
        /// Task 25/27: activates the ToolType matching the current highlighter
        /// apply mode (Highlighter / TextHighlight / AreaHighlight). Used by the
        /// mode selector and the toolbar button click.
        /// </summary>
        private void ActivateHighlighterModeTool()
        {
            var tool = _highlighterApplyMode switch
            {
                HighlighterApplyMode.Freehand => ToolType.Highlighter,
                HighlighterApplyMode.AreaHighlight => ToolType.AreaHighlight,
                _ => ToolType.TextHighlight,
            };
            if (_currentTool != tool)
                ActivateTool(tool);
            else
                ApplyToolToAllPages();
        }

        /// <summary>
        /// Appends the pen behaviour toggles (pressure / ink simulation /
        /// shape recognition) to the pen popup. Each toggle persists to
        /// AppSettings and propagates via ApplyToolToAllPages.
        /// </summary>
        private void AddPenBehaviourToggles(Popup popup)
        {
            if (popup?.Child is not Border border || border.Child is not StackPanel panel)
                return;

            panel.Children.Add(ThemeDivider(new Border
            {
                Height = 1,
                Margin = new Thickness(-16, 14, -16, 10)
            }));

            var pressureRow = BuildSettingToggleRow(LocalizationService.Get("Editor.Pressure"), AppSettingsService.Load().EnablePressure, v =>
            {
                SaveSetting(s => s.EnablePressure = v);
                ApplyToolToAllPages();
            }, automationId: "Editor.Pen.Pressure");

            var inkSimRow = BuildSettingToggleRow(LocalizationService.Get("Editor.InkSimulation"), AppSettingsService.Load().InkSimulation, v =>
            {
                SaveSetting(s => s.InkSimulation = v);
                ApplyToolToAllPages();
            }, automationId: "Editor.Pen.InkSimulation");

            var shapeRecognitionRow = BuildSettingToggleRow(LocalizationService.Get("Editor.ShapeRecognition"), AppSettingsService.Load().ShapeRecognition, v =>
            {
                SaveSetting(s => s.ShapeRecognition = v);
                ApplyToolToAllPages();
            }, automationId: "Editor.Pen.ShapeRecognition");

            var behaviourGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var toggle in new[] { pressureRow, inkSimRow, shapeRecognitionRow })
            {
                toggle.Height = 58;
                toggle.MinWidth = 0;
                toggle.Margin = new Thickness(3);
                toggle.Padding = new Thickness(4);
                if (toggle.Content is StackPanel content)
                {
                    content.Orientation = Orientation.Vertical;
                    content.HorizontalAlignment = HorizontalAlignment.Center;
                    if (content.Children.OfType<Border>().FirstOrDefault() is Border indicator)
                        indicator.HorizontalAlignment = HorizontalAlignment.Center;
                    if (content.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock text)
                    {
                        text.Margin = new Thickness(0, 4, 0, 0);
                        text.FontSize = 11;
                        text.TextAlignment = TextAlignment.Center;
                        text.TextWrapping = TextWrapping.Wrap;
                    }
                }
                behaviourGrid.Children.Add(toggle);
            }
            panel.Children.Add(behaviourGrid);
        }

        /// <summary>
        /// Task 24: appends the stroke smoothing segmented selector
        /// (关 Off / 低 Low / 中 Mid / 高 High) to the pen popup. The level
        /// persists to AppSettings.StrokeSmoothing and propagates to every
        /// page via ApplyToolToAllPages (PdfPageControl consumes it in the
        /// StrokeCollected post-processing chain). Settings-page entry comes
        /// with Task 38.
        /// </summary>
        private void AddPenSmoothingSection(Popup popup)
        {
            if (popup?.Child is not Border border || border.Child is not StackPanel panel)
                return;

            panel.Children.Add(ThemeDivider(new Border
            {
                Height = 1,
                Margin = new Thickness(-16, 14, -16, 10)
            }));

            panel.Children.Add(ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.SmoothingHeader"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            }));

            var labels = new[]
            {
                LocalizationService.Get("Editor.SmoothingOff"),
                LocalizationService.Get("Editor.SmoothingLow"),
                LocalizationService.Get("Editor.SmoothingMid"),
                LocalizationService.Get("Editor.SmoothingHigh")
            };
            var buttons = new ToggleButton[labels.Length];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

            void ApplyVisual()
            {
                int current = AppSettingsService.Load().StrokeSmoothing;
                for (int i = 0; i < buttons.Length; i++)
                    StyleModeToggleButton(buttons[i], active: i == current);
            }

            for (int i = 0; i < labels.Length; i++)
            {
                int level = i;
                buttons[i] = BuildModeToggleButton(labels[i], new Thickness(0, 0, i < labels.Length - 1 ? 6 : 0, 0), width: 54, activated: () =>
                {
                    if (AppSettingsService.Load().StrokeSmoothing == level)
                        return;
                    SaveSetting(settings => settings.StrokeSmoothing = level);
                    ApplyToolToAllPages();
                    ApplyVisual();
                }, automationId: $"Editor.Pen.Smoothing.{i}");
                row.Children.Add(buttons[i]);
            }

            panel.Children.Add(row);
            ApplyVisual();
        }

        /// <summary>
        /// Builds one clickable toggle row (indicator box + label) for
        /// boolean settings in tool popups. Follows the popup look with the
        /// #2563EB accent for the on state.
        /// </summary>
        private static ToggleButton BuildSettingToggleRow(string label, bool initialState, Action<bool> toggled, string automationId = null)
        {
            var indicator = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = label,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            var row = new ToggleButton
            {
                Height = 34,
                MinWidth = 32,
                MinHeight = 32,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 0, 10, 0),
                Cursor = Cursors.Hand,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { indicator, text }
                }
            };

            row.Tag = indicator;
            ApplyToolbarPopupToggleStyle(row);
            row.Focusable = true;
            ToolTipService.SetToolTip(row, label);
            AutomationProperties.SetAutomationId(row, automationId ?? "Editor.Popup.Setting");
            AutomationProperties.SetName(row, label);
            AutomationProperties.SetHelpText(row, label);
            KeyboardNavigation.SetIsTabStop(row, true);

            row.IsChecked = initialState;

            void ApplyVisual()
            {
                bool state = row.IsChecked == true;
                row.SetResourceReference(Control.BackgroundProperty,
                    state ? "ThemeSelectionBrush" : "ThemeSurfaceAltBrush");
                indicator.SetResourceReference(Border.BorderBrushProperty,
                    state ? "ThemeAccentBrush" : "ThemeBorderBrush");
                if (state)
                    indicator.SetResourceReference(Border.BackgroundProperty, "ThemeAccentBrush");
                else
                    indicator.Background = Brushes.Transparent;
                text.SetResourceReference(
                    TextElement.ForegroundProperty,
                    state ? "ThemeAccentBrush" : "ThemeForegroundBrush");
            }

            ApplyVisual();

            row.Click += (s, e) =>
            {
                ApplyVisual();
                toggled?.Invoke(row.IsChecked == true);
                e.Handled = true;
            };

            return row;
        }

        private void SaveSetting(Action<AppSettings> mutate)
        {
            var settings = AppSettingsService.Load();
            mutate(settings);
            _applicationSettings = AppSettingsService.Save(settings);
        }

        // Task 14: recently used colors, newest first, max 8 per palette.
        private const int MaxRecentColors = 8;

        /// <summary>
        /// Task 14: inserts (or moves) the color's "#RRGGBB" hex to the front
        /// of the given recent-colors list, de-duplicating and trimming to
        /// <see cref="MaxRecentColors"/>. Callers persist via SaveSetting
        /// (the list belongs to a transient AppSettings clone from Load()).
        /// </summary>
        private void RecordRecentColor(List<string> list, Color c)
        {
            if (list == null)
                return;

            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            list.RemoveAll(x => string.Equals(x, hex, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, hex);
            if (list.Count > MaxRecentColors)
                list.RemoveRange(MaxRecentColors, list.Count - MaxRecentColors);
        }

        /// <summary>
        /// Task 14: repopulates a recent-colors swatch row from the settings
        /// list. Hidden entirely when empty; each 16x16 rounded swatch applies
        /// its color exactly like a palette cell of the owning popup.
        /// </summary>
        private void RefreshRecentColorsRow(
            StackPanel section,
            StackPanel row,
            Func<List<string>> getRecentColors,
            Action<Color> applyColor,
            Action<Color> selectedChanged = null)
        {
            row.Children.Clear();

            List<string> recent = null;
            try { recent = getRecentColors?.Invoke(); }
            catch { /* settings read failures leave the row hidden */ }

            if (recent != null)
            {
                foreach (var hex in recent)
                {
                    if (row.Children.Count >= MaxRecentColors)
                        break;
                    if (!TryParseRecentColor(hex, out var color))
                        continue;

                    int recentIndex = row.Children.Count;
                    var swatchVisual = new Border
                    {
                        Width = 22,
                        Height = 22,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(color),
                        BorderThickness = new Thickness(1),
                    };
                    swatchVisual.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
                    var swatch = new Button
                    {
                        Width = 32,
                        Height = 32,
                        Padding = new Thickness(3),
                        Margin = new Thickness(0, 0, 6, 0),
                        Cursor = Cursors.Hand,
                        Focusable = true,
                        Content = swatchVisual,
                        Tag = color
                    };
                    ApplyToolbarPopupButtonStyle(swatch);
                    ToolTipService.SetToolTip(swatch, hex);
                    AutomationProperties.SetAutomationId(swatch, $"Editor.Color.Recent.{recentIndex}");
                    AutomationProperties.SetName(swatch, hex);
                    AutomationProperties.SetHelpText(swatch, hex);
                    swatch.Click += (s, e) =>
                    {
                        if (s is Button b && b.Tag is Color picked)
                        {
                            applyColor?.Invoke(picked);
                            selectedChanged?.Invoke(picked);
                        }
                        e.Handled = true;
                    };
                    row.Children.Add(swatch);
                }
            }

            section.Visibility = row.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Task 14: parses "#RRGGBB" (recorded format) and tolerates
        /// "#AARRGGBB" for hand-edited settings.json files.
        /// </summary>
        private static bool TryParseRecentColor(string hex, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex))
                return false;

            var digits = hex.Trim().TrimStart('#');
            if (digits.Length != 6 && digits.Length != 8)
                return false;

            try
            {
                var r = byte.Parse(digits.Substring(digits.Length - 6, 2), System.Globalization.NumberStyles.HexNumber);
                var g = byte.Parse(digits.Substring(digits.Length - 4, 2), System.Globalization.NumberStyles.HexNumber);
                var b = byte.Parse(digits.Substring(digits.Length - 2, 2), System.Globalization.NumberStyles.HexNumber);
                var a = digits.Length == 8
                    ? byte.Parse(digits.Substring(0, 2), System.Globalization.NumberStyles.HexNumber)
                    : (byte)255;
                color = Color.FromArgb(a, r, g, b);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Line AddSizePreviewSection(Popup popup, double initialSize, Color initialColor, bool isHighlighter)
        {
            if (popup?.Child is not Border border || border.Child is not StackPanel panel)
                return null;

            panel.Children.Add(ThemeDivider(new Border
            {
                Height = 1,
                Margin = new Thickness(-16, 14, -16, 10)
            }));

            panel.Children.Add(ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.PopupPreview"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            }));

            var previewBorder = new Border
            {
                Height = 60,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true
            };
            previewBorder.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceAltBrush");

            Line line;
            if (isHighlighter)
            {
                // Highlighter: horizontal stroke band showing actual stroke height
                line = new Line
                {
                    X1 = 8, Y1 = 30, X2 = 212, Y2 = 30,
                    Stroke = new SolidColorBrush(GetHighlighterPreviewStrokeColor(HighlighterApplyMode.Freehand, initialColor)),
                    StrokeThickness = initialSize,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
            }
            else
            {
                // Pen: diagonal stroke line showing actual stroke width
                line = new Line
                {
                    X1 = 8, Y1 = 48, X2 = 212, Y2 = 12,
                    Stroke = new SolidColorBrush(initialColor),
                    StrokeThickness = initialSize,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
            }

            previewBorder.Child = line;
            panel.Children.Add(previewBorder);
            return line;
        }

        private CancellationTokenSource _eraserPreviewCts;

        private void ShowEraserSizePreview(double size)
        {
            EraserSizePreviewEllipse.Width = size;
            EraserSizePreviewEllipse.Height = size;
            EraserSizePreviewEllipse.Visibility = Visibility.Visible;

            _eraserPreviewCts?.Cancel();
            _eraserPreviewCts = new CancellationTokenSource();
            var token = _eraserPreviewCts.Token;

            Task.Delay(1200).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                    Dispatcher.Invoke(() => EraserSizePreviewEllipse.Visibility = Visibility.Collapsed);
            }, TaskScheduler.Default);
        }

        private void CreateSelectionPopup()
        {
            // ── Settings popup (opens when Select button is clicked) ────────────────
            _selectionPopup = new Popup { Placement = PlacementMode.Bottom, StaysOpen = true, AllowsTransparency = true, VerticalOffset = 6 };

            var settingsPanel = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            var settingsBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = settingsPanel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 20, ShadowDepth = 4, Opacity = ThemeService.GetShadowOpacity(), Color = Colors.Black
                }
            };
            settingsBorder.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceBrush");
            settingsBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");

            // ── Shape section ──────────────────────────────────────────────────────
            settingsPanel.Children.Add(ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.SelectShape"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            }));

            var shapePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

            ToggleButton MakeShapeButton(string tooltip, SelectionShape shape, Path preview)
            {
                var isActive = _selectionShape == shape;
                var btn = new ToggleButton
                {
                    Width = 36, Height = 32,
                    MinWidth = 32, MinHeight = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(0),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    ToolTip = tooltip,
                    Tag = shape
                };
                ApplyToolbarPopupToggleStyle(btn);
                btn.Content = CreateSelectionToggleContent(preview);
                string automationId = shape == SelectionShape.Rectangle
                    ? "Editor.Select.Shape.Rectangle"
                    : "Editor.Select.Shape.FreeForm";
                ToolTipService.SetToolTip(btn, tooltip);
                AutomationProperties.SetAutomationId(btn, automationId);
                AutomationProperties.SetName(btn, tooltip);
                AutomationProperties.SetHelpText(btn, tooltip);
                KeyboardNavigation.SetIsTabStop(btn, true);
                UpdateFilterButtonStyle(btn, isActive);
                btn.Checked += (_, __) =>
                {
                    if (!_isUpdatingSelectionPopup)
                        SelectShapeButton(btn);
                };
                btn.Unchecked += (_, __) =>
                {
                    if (!_isUpdatingSelectionPopup && _selectionShape == shape)
                        btn.IsChecked = true;
                };
                btn.Click += (s, ev) =>
                {
                    SelectShapeButton((ToggleButton)s);
                    ev.Handled = true;
                };
                return btn;

                void SelectShapeButton(ToggleButton selected)
                {
                    _selectionShape = (SelectionShape)selected.Tag;
                    ApplyToolToAllPages();
                    _isUpdatingSelectionPopup = true;
                    try
                    {
                        foreach (ToggleButton b in shapePanel.Children)
                            UpdateFilterButtonStyle(b, (SelectionShape)b.Tag == _selectionShape);
                    }
                    finally
                    {
                        _isUpdatingSelectionPopup = false;
                    }
                }
            }

            shapePanel.Children.Add(MakeShapeButton(
                LocalizationService.Get("Editor.SelectShapeRect"),
                SelectionShape.Rectangle,
                BuildSelectionShapePreview(SelectionShape.Rectangle)));
            shapePanel.Children.Add(MakeShapeButton(
                LocalizationService.Get("Editor.SelectShapeFree"),
                SelectionShape.FreeForm,
                BuildSelectionShapePreview(SelectionShape.FreeForm)));
            settingsPanel.Children.Add(shapePanel);

            // ── Filter section header
            settingsPanel.Children.Add(ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.SelectFilter"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            }));

            // Filter radio buttons
            var filterPanel = new StackPanel { Orientation = Orientation.Horizontal };

            ToggleButton MakeFilterButton(string label, SelectionFilter filter)
            {
                var isActive = _selectionFilter == filter;
                var btn = new ToggleButton
                {
                    Content = CreateSelectionToggleContent(new TextBlock
                    {
                        Text = label,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.NoWrap
                    }),
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 12,
                    MinWidth = 32,
                    MinHeight = 32,
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    Tag = filter
                };
                ApplyToolbarPopupToggleStyle(btn);
                string automationId = filter switch
                {
                    SelectionFilter.DrawingsOnly => "Editor.Select.Filter.Drawings",
                    SelectionFilter.TextOnly => "Editor.Select.Filter.Text",
                    _ => "Editor.Select.Filter.Both"
                };
                ToolTipService.SetToolTip(btn, label);
                AutomationProperties.SetAutomationId(btn, automationId);
                AutomationProperties.SetName(btn, label);
                AutomationProperties.SetHelpText(btn, label);
                KeyboardNavigation.SetIsTabStop(btn, true);
                UpdateFilterButtonStyle(btn, isActive);
                btn.Checked += (_, __) =>
                {
                    if (!_isUpdatingSelectionPopup)
                        SelectFilterButton(btn);
                };
                btn.Unchecked += (_, __) =>
                {
                    if (!_isUpdatingSelectionPopup && _selectionFilter == filter)
                        btn.IsChecked = true;
                };
                btn.Click += (s, ev) =>
                {
                    SelectFilterButton((ToggleButton)s);
                    ev.Handled = true;
                };
                return btn;

                void SelectFilterButton(ToggleButton selected)
                {
                    _selectionFilter = (SelectionFilter)selected.Tag;
                    ApplyToolToAllPages();
                    _isUpdatingSelectionPopup = true;
                    try
                    {
                        // Refresh all filter button styles.
                        foreach (ToggleButton b in filterPanel.Children)
                            UpdateFilterButtonStyle(b, (SelectionFilter)b.Tag == _selectionFilter);
                    }
                    finally
                    {
                        _isUpdatingSelectionPopup = false;
                    }
                }
            }

            filterPanel.Children.Add(MakeFilterButton(LocalizationService.Get("Editor.SelectFilterBoth"), SelectionFilter.Both));
            filterPanel.Children.Add(MakeFilterButton(LocalizationService.Get("Editor.SelectFilterDrawings"), SelectionFilter.DrawingsOnly));
            filterPanel.Children.Add(MakeFilterButton(LocalizationService.Get("Editor.SelectFilterText"), SelectionFilter.TextOnly));

            settingsPanel.Children.Add(filterPanel);

            settingsPanel.Children.Add(ThemeDivider(new Border
            {
                Height = 1,
                Margin = new Thickness(-14, 12, -14, 12)
            }));
            settingsPanel.Children.Add(ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.SelectedDrawingStyle"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            }));

            var widthRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            foreach (double width in new[] { 1d, 2d, 4d, 8d })
            {
                var widthButton = new Button
                {
                    Content = width.ToString("0.#", CultureInfo.InvariantCulture),
                    Width = 44,
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand
                };
                ApplyToolbarPopupButtonStyle(widthButton);
                AutomationProperties.SetAutomationId(widthButton, $"Editor.Select.DrawingWidth.{width:0.#}");
                widthButton.Click += (_, e) =>
                {
                    ApplySelectedDrawingStyle(null, width);
                    e.Handled = true;
                };
                widthRow.Children.Add(widthButton);
            }
            settingsPanel.Children.Add(widthRow);

            var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var color in new[] { Colors.Black, Colors.Red, Colors.Orange, Colors.Green, Colors.Blue, Colors.Purple })
            {
                var swatch = new Button
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Tag = color
                };
                ApplyToolbarPopupButtonStyle(swatch);
                string label = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                AutomationProperties.SetAutomationId(swatch, $"Editor.Select.DrawingColor.{label[1..]}");
                AutomationProperties.SetName(swatch, label);
                swatch.Click += (_, e) =>
                {
                    ApplySelectedDrawingStyle(color, null);
                    e.Handled = true;
                };
                colorRow.Children.Add(swatch);
            }
            settingsPanel.Children.Add(colorRow);

            _selectionPopup.Child = settingsBorder;
        }

        private void ApplySelectedDrawingStyle(Color? color, double? width)
        {
            var page = _activeSelectionPage;
            var strokes = page?.SelectedStrokes?.Distinct().ToList();
            if (page == null || strokes == null || strokes.Count == 0)
                return;

            var before = strokes.ToDictionary(stroke => stroke, stroke => stroke.DrawingAttributes.Clone());
            bool changed = false;
            foreach (var stroke in strokes)
            {
                var attributes = stroke.DrawingAttributes.Clone();
                if (color.HasValue && attributes.Color != color.Value)
                {
                    attributes.Color = color.Value;
                    changed = true;
                }
                if (width.HasValue && (Math.Abs(attributes.Width - width.Value) > 0.001 || Math.Abs(attributes.Height - width.Value) > 0.001))
                {
                    attributes.Width = width.Value;
                    attributes.Height = width.Value;
                    changed = true;
                }
                stroke.DrawingAttributes = attributes;
            }

            if (!changed)
                return;

            var after = strokes.ToDictionary(stroke => stroke, stroke => stroke.DrawingAttributes.Clone());
            page.RefreshSelectedDrawingStyle();
            PushUndoAction(new StrokeStyleChangedAction(page, before, after));
        }

        private static Grid CreateSelectionToggleContent(UIElement visual)
        {
            var activeBar = new Border
            {
                Height = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 4, 1),
                Tag = "ActiveBar",
                Visibility = Visibility.Collapsed
            };
            activeBar.SetResourceReference(Border.BackgroundProperty, "ThemeFocusBrush");

            if (visual is FrameworkElement frameworkElement)
            {
                frameworkElement.HorizontalAlignment = HorizontalAlignment.Center;
                frameworkElement.VerticalAlignment = VerticalAlignment.Center;
                if (visual is Path preview)
                    preview.Tag = "Preview";
            }

            return new Grid
            {
                Children = { visual, activeBar }
            };
        }

        private static void UpdateFilterButtonStyle(ToggleButton btn, bool isActive)
        {
            btn.IsChecked = isActive;
            btn.SetResourceReference(
                ToggleButton.BackgroundProperty,
                isActive ? "ThemeSelectionBrush" : "ThemeSurfaceAltBrush");
            btn.SetResourceReference(
                ToggleButton.BorderBrushProperty,
                isActive ? "ThemeAccentBrush" : "ThemeBorderBrush");
            btn.SetResourceReference(
                Control.ForegroundProperty,
                isActive ? "ThemeSelectionForegroundBrush" : "ThemeForegroundBrush");
            if (btn.Content is Grid grid)
            {
                foreach (var element in grid.Children)
                {
                    if (element is Border border && Equals(border.Tag, "ActiveBar"))
                    {
                        border.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                        border.SetResourceReference(Border.BackgroundProperty, "ThemeFocusBrush");
                    }
                    else if (element is Path preview)
                    {
                        preview.SetResourceReference(
                            Path.StrokeProperty,
                            isActive ? "ThemeSelectionForegroundBrush" : "ThemeForegroundBrush");
                    }
                    else if (element is TextBlock text)
                    {
                        text.SetResourceReference(
                            TextElement.ForegroundProperty,
                            isActive ? "ThemeSelectionForegroundBrush" : "ThemeForegroundBrush");
                    }
                }
            }
        }

        private void ScaleSelection(double factor)
        {
            if (_activeSelectionPage == null || !_activeSelectionPage.HasSelection)
                return;

            var bounds = _activeSelectionPage.GetSelectionBounds();
            if (bounds.IsEmpty)
                return;

            var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            _activeSelectionPage.ScaleSelection(factor, center);
            MarkDirty();
        }

        private void DeleteSelection()
        {
            if (_activeSelectionPage == null || !_activeSelectionPage.HasSelection)
                return;

            var strokes = new List<System.Windows.Ink.Stroke>(_activeSelectionPage.SelectedStrokes);
            var placements = strokes
                .Select(_activeSelectionPage.CaptureStrokePlacement)
                .ToList();
            var containers = new List<System.Windows.Controls.Grid>(_activeSelectionPage.SelectedTextContainers);

            foreach (var s in strokes) _activeSelectionPage.RemoveStrokeQuiet(s);
            foreach (var c in containers) _activeSelectionPage.RemoveTextContainerQuiet(c);

            PushUndoAction(new ItemsRemovedAction(_activeSelectionPage, placements, containers));

            _activeSelectionPage.ClearSelection();
            MarkDirty();
        }

        private void CutSelection()
        {
            if (_activeSelectionPage == null || !_activeSelectionPage.HasSelection)
                return;

            CopySelection();
            DeleteSelection();
        }

        private void CopySelection()
        {
            if (_activeSelectionPage == null || !_activeSelectionPage.HasSelection)
                return;

            try
            {
                // Create annotation data for selected items
                var annotationData = new AnnotationData();
                var pageAnnotation = new PageAnnotation();

                // Add selected strokes
                foreach (var stroke in _activeSelectionPage.SelectedStrokes)
                {
                    var shape = ShapeStrokeMetadata.Read(stroke);
                    var strokeAnnotation = new StrokeAnnotation
                    {
                        R = stroke.DrawingAttributes.Color.R,
                        G = stroke.DrawingAttributes.Color.G,
                        B = stroke.DrawingAttributes.Color.B,
                        A = stroke.DrawingAttributes.Color.A,
                        Size = stroke.DrawingAttributes.Width,
                        IsHighlighter = stroke.DrawingAttributes.IsHighlighter,
                        FitToCurve = stroke.DrawingAttributes.FitToCurve,
                        ShapeGroupId = shape.GroupId,
                        ShapeKind = shape.Kind,
                        ShapePartIndex = shape.PartIndex,
                        IsDashedShape = shape.IsDashed,
                        Points = new List<double[]>()
                    };

                    foreach (var point in stroke.StylusPoints)
                    {
                        strokeAnnotation.Points.Add(new double[] { point.X, point.Y });
                    }

                    pageAnnotation.Strokes.Add(strokeAnnotation);
                }

                // Add selected text annotations
                foreach (var container in _activeSelectionPage.SelectedTextContainers)
                {
                    if (container.Children.OfType<TextBox>().FirstOrDefault() is TextBox textBox)
                    {
                        var textAnnotation = new TextAnnotation
                        {
                            Text = textBox.Text,
                            X = Canvas.GetLeft(container),
                            Y = Canvas.GetTop(container),
                            R = ((SolidColorBrush)textBox.Foreground).Color.R,
                            G = ((SolidColorBrush)textBox.Foreground).Color.G,
                            B = ((SolidColorBrush)textBox.Foreground).Color.B,
                            FontSize = textBox.FontSize,
                            Width = GetPersistedTextWidth(container),
                            Height = GetPersistedTextHeight(container),
                            Bold = textBox.FontWeight >= FontWeights.Bold,
                            Italic = textBox.FontStyle == FontStyles.Italic,
                            FontFamily = textBox.FontFamily?.Source ?? "Segoe UI",
                            Alignment = textBox.TextAlignment.ToString()
                        };

                        pageAnnotation.Texts.Add(textAnnotation);
                    }
                    else if (PdfPageControl.IsImageContainer(container))
                    {
                        // Task 19: selected images ride along as base64 payload.
                        var imageData = _activeSelectionPage.GetImageData(container);
                        if (imageData != null)
                        {
                            pageAnnotation.Images.Add(new ImageAnnotation
                            {
                                X = Canvas.GetLeft(container),
                                Y = Canvas.GetTop(container),
                                Width = container.ActualWidth > 0 ? container.ActualWidth : container.Width,
                                Height = container.ActualHeight > 0 ? container.ActualHeight : container.Height,
                                Format = PdfService.DetectImageFormat(imageData),
                                ImageDataBase64 = Convert.ToBase64String(imageData)
                            });
                        }
                    }
                    else if (_activeSelectionPage.GetOverlayData(container) is StickyNoteAnnotation sticky)
                    {
                        pageAnnotation.StickyNotes.Add(new StickyNoteAnnotation
                        {
                            Id = sticky.Id,
                            X = Canvas.GetLeft(container),
                            Y = Canvas.GetTop(container),
                            Text = sticky.Text,
                            Width = container.ActualWidth > 0 ? container.ActualWidth : container.Width,
                            Height = container.ActualHeight > 0 ? container.ActualHeight : container.Height,
                            R = sticky.R,
                            G = sticky.G,
                            B = sticky.B
                        });
                    }
                }

                annotationData.Pages["0"] = pageAnnotation;

                // Serialize to JSON
                var json = System.Text.Json.JsonSerializer.Serialize(annotationData);

                // Copy to clipboard
                System.Windows.Clipboard.SetText(json);

                var mw = Window.GetWindow(this) as MainWindow;
                mw?.ShowToast(LocalizationService.Get("Editor.SelectionCopied"), "\uE14D", 1500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CopySelection] Error: {ex.Message}");
            }
        }

        private void PasteSelection()
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsText())
                    return;

                var json = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var annotationData = System.Text.Json.JsonSerializer.Deserialize<AnnotationData>(json);
                if (annotationData?.Pages == null || !annotationData.Pages.ContainsKey("0"))
                    return;

                var pageAnnotation = annotationData.Pages["0"];
                if (pageAnnotation == null)
                    return;

                // Find the active page - prefer _lastClickedPage, otherwise find first visible page
                var targetPage = _lastClickedPage ?? _activeSelectionPage;
                if (targetPage == null)
                {
                    targetPage = _pageControls.FirstOrDefault();
                }

                if (targetPage == null)
                    return;

                // Add a small offset to prevent pasting exactly on top of original unless we clicked somewhere
                double pasteOffsetX = 20.0;
                double pasteOffsetY = 20.0;

                // If we have a clicked page and point, offset relative to it
                if (_lastClickedPage == targetPage)
                {
                    double minX = double.MaxValue;
                    double minY = double.MaxValue;
                    bool hasBoundingBox = false;

                    if (pageAnnotation.Strokes != null)
                    {
                        foreach (var stroke in pageAnnotation.Strokes)
                        {
                            foreach (var pt in stroke.Points)
                            {
                                hasBoundingBox = true;
                                if (pt[0] < minX) minX = pt[0];
                                if (pt[1] < minY) minY = pt[1];
                            }
                        }
                    }

                    if (pageAnnotation.Texts != null)
                    {
                        foreach (var text in pageAnnotation.Texts)
                        {
                            hasBoundingBox = true;
                            if (text.X < minX) minX = text.X;
                            if (text.Y < minY) minY = text.Y;
                        }
                    }

                    if (pageAnnotation.Images != null)
                    {
                        foreach (var img in pageAnnotation.Images)
                        {
                            hasBoundingBox = true;
                            if (img.X < minX) minX = img.X;
                            if (img.Y < minY) minY = img.Y;
                        }
                    }

                    if (pageAnnotation.StickyNotes != null)
                    {
                        foreach (var sticky in pageAnnotation.StickyNotes)
                        {
                            hasBoundingBox = true;
                            if (sticky.X < minX) minX = sticky.X;
                            if (sticky.Y < minY) minY = sticky.Y;
                        }
                    }

                    if (hasBoundingBox)
                    {
                        pasteOffsetX = _lastClickedPoint.X - minX;
                        pasteOffsetY = _lastClickedPoint.Y - minY;
                    }
                }

                var pastedStrokes = new List<System.Windows.Ink.Stroke>();
                var pastedContainers = new List<System.Windows.Controls.Grid>();

                // Paste strokes
                if (pageAnnotation.Strokes != null)
                {
                    var pastedShapeGroups = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var strokeAnnotation in pageAnnotation.Strokes)
                    {
                        string pastedGroupId = string.Empty;
                        if (!string.IsNullOrWhiteSpace(strokeAnnotation.ShapeGroupId))
                        {
                            if (!pastedShapeGroups.TryGetValue(strokeAnnotation.ShapeGroupId, out pastedGroupId))
                            {
                                pastedGroupId = Guid.NewGuid().ToString("N");
                                pastedShapeGroups[strokeAnnotation.ShapeGroupId] = pastedGroupId;
                            }
                        }
                        // Apply paste offset
                        var offsetStroke = new StrokeAnnotation
                        {
                            R = strokeAnnotation.R,
                            G = strokeAnnotation.G,
                            B = strokeAnnotation.B,
                            A = strokeAnnotation.A,
                            Size = strokeAnnotation.Size,
                            IsHighlighter = strokeAnnotation.IsHighlighter,
                            FitToCurve = strokeAnnotation.FitToCurve,
                            ShapeGroupId = pastedGroupId,
                            ShapeKind = strokeAnnotation.ShapeKind,
                            ShapePartIndex = strokeAnnotation.ShapePartIndex,
                            IsDashedShape = strokeAnnotation.IsDashedShape,
                            Points = new List<double[]>()
                        };

                        foreach (var point in strokeAnnotation.Points)
                        {
                            offsetStroke.Points.Add(new double[] { point[0] + pasteOffsetX, point[1] + pasteOffsetY });
                        }

                        var s = targetPage.AddStroke(offsetStroke);
                        if (s != null)
                        {
                            pastedStrokes.Add(s);
                        }
                    }
                }

                // Paste text annotations
                if (pageAnnotation.Texts != null)
                {
                    foreach (var textAnnotation in pageAnnotation.Texts)
                    {
                        var offsetTextAnnotation = new TextAnnotation
                        {
                            Text = textAnnotation.Text,
                            X = textAnnotation.X + pasteOffsetX,
                            Y = textAnnotation.Y + pasteOffsetY,
                            R = textAnnotation.R,
                            G = textAnnotation.G,
                            B = textAnnotation.B,
                            FontSize = textAnnotation.FontSize,
                            Width = textAnnotation.Width,
                            Height = textAnnotation.Height,
                            Bold = textAnnotation.Bold,
                            Italic = textAnnotation.Italic,
                            FontFamily = textAnnotation.FontFamily,
                            Alignment = textAnnotation.Alignment
                        };

                        // Create the text box on the target page
                        var color = Color.FromRgb(offsetTextAnnotation.R, offsetTextAnnotation.G, offsetTextAnnotation.B);
                        var c = CreateTextBox(
                            targetPage,
                            new Point(offsetTextAnnotation.X, offsetTextAnnotation.Y),
                            color,
                            offsetTextAnnotation.FontSize,
                            offsetTextAnnotation.Text,
                            select: false,
                            alignToPointer: false,
                            bold: offsetTextAnnotation.Bold,
                            italic: offsetTextAnnotation.Italic,
                            fontFamily: offsetTextAnnotation.FontFamily,
                            alignment: ParseTextAlignment(offsetTextAnnotation.Alignment),
                            width: offsetTextAnnotation.Width > 0 ? offsetTextAnnotation.Width : null,
                            height: offsetTextAnnotation.Height > 0 ? offsetTextAnnotation.Height : null);
                        if (c != null)
                        {
                            pastedContainers.Add(c);
                        }
                    }
                }

                // Paste image annotations (Task 19) — the copied dimensions
                // are restored verbatim, only the position takes the offset.
                if (pageAnnotation.Images != null)
                {
                    foreach (var imageAnnotation in pageAnnotation.Images)
                    {
                        if (string.IsNullOrEmpty(imageAnnotation.ImageDataBase64))
                            continue;

                        byte[] imageBytes;
                        try { imageBytes = Convert.FromBase64String(imageAnnotation.ImageDataBase64); }
                        catch
                        {
                            continue;
                        }

                        var img = targetPage.AddImage(imageBytes,
                            new Point(imageAnnotation.X + pasteOffsetX, imageAnnotation.Y + pasteOffsetY),
                            imageAnnotation.Width, imageAnnotation.Height);
                        if (img != null)
                        {
                            pastedContainers.Add(img);
                        }
                    }
                }

                // Sticky notes keep their content, marker geometry and colour
                // in clipboard JSON; only the position and identity change.
                if (pageAnnotation.StickyNotes != null)
                {
                    foreach (var sticky in pageAnnotation.StickyNotes)
                    {
                        var pastedSticky = new StickyNoteAnnotation
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            X = sticky.X + pasteOffsetX,
                            Y = sticky.Y + pasteOffsetY,
                            Text = sticky.Text,
                            Width = sticky.Width,
                            Height = sticky.Height,
                            R = sticky.R,
                            G = sticky.G,
                            B = sticky.B
                        };
                        var pasted = targetPage.AddStickyNote(pastedSticky);
                        if (pasted != null)
                            pastedContainers.Add(pasted);
                    }
                }

                if (pastedStrokes.Count > 0 || pastedContainers.Count > 0)
                {
                    // Undo state FIRST (selection is UI state, not undoable).
                    PushUndoAction(new ItemsAddedAction(targetPage, pastedStrokes, pastedContainers));

                    // Task 8.2: auto-select the pasted content so it shows the
                    // per-item outlines + handles and is ready to drag/resize.
                    // Mirrors the Task 7 cross-page rule: a selection lingering
                    // on another page must be cleared first so only the target
                    // page holds a selection.
                    foreach (var page in _pageControls)
                    {
                        if (page != targetPage && page.HasSelection)
                            page.ClearSelection();
                    }
                    targetPage.SelectItems(pastedStrokes, pastedContainers);
                }

                MarkDirty();

                var mw = Window.GetWindow(this) as MainWindow;
                mw?.ShowToast(LocalizationService.Get("Editor.SelectionPasted"), "\uE14D", 1500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PasteSelection] Error: {ex.Message}");
                // Don't crash if paste fails - just ignore
            }
        }

        // ----- Task 19: image annotations (clipboard paste / drag-drop) -----

        /// <summary>
        /// Task 19: Ctrl+V with a bitmap on the clipboard. Encodes the bitmap
        /// to PNG, drops it on the target page (last clicked page / active
        /// selection page / first page), pushes one ItemsAddedAction and
        /// auto-selects the pasted container. Returns false when the clipboard
        /// holds no image (caller falls back to annotation JSON paste).
        /// </summary>
        private bool PasteClipboardImage()
        {
            try
            {
                if (!Clipboard.ContainsImage())
                    return false;

                BitmapSource source = null;
                try { source = Clipboard.GetImage(); }
                catch { /* some producers put a PNG stream instead of CF_BITMAP */ }

                if (source == null && Clipboard.GetData("PNG") is MemoryStream pngStream)
                {
                    // Browsers expose copied images as a "PNG" format that WPF's
                    // GetImage() does not decode.
                    pngStream.Position = 0;
                    source = BitmapFrame.Create(pngStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                }

                if (source == null)
                    return false;

                var pngBytes = EncodeBitmapSourceToPng(source);
                if (pngBytes == null || pngBytes.Length == 0)
                    return false;

                var targetPage = _lastClickedPage ?? _activeSelectionPage ?? _pageControls.FirstOrDefault();
                if (targetPage == null)
                    return false;

                Point position = _lastClickedPage == targetPage
                    ? _lastClickedPoint
                    : new Point(targetPage.ActualWidth / 2, targetPage.ActualHeight / 2);

                var container = targetPage.AddImage(pngBytes, position);
                if (container == null)
                    return false;

                if (_lastClickedPage != targetPage)
                {
                    // No click anchor: center the image on the target page.
                    Canvas.SetLeft(container, Math.Max(0, (targetPage.ActualWidth - container.Width) / 2));
                    Canvas.SetTop(container, Math.Max(0, (targetPage.ActualHeight - container.Height) / 2));
                }

                PushUndoAction(new ItemsAddedAction(targetPage,
                    new List<System.Windows.Ink.Stroke>(),
                    new List<System.Windows.Controls.Grid> { container }));

                foreach (var page in _pageControls)
                {
                    if (page != targetPage && page.HasSelection)
                        page.ClearSelection();
                }
                targetPage.SelectItems(new List<System.Windows.Ink.Stroke>(),
                    new List<System.Windows.Controls.Grid> { container });

                MarkDirty();

                var mw = Window.GetWindow(this) as MainWindow;
                mw?.ShowToast(LocalizationService.Get("Editor.ImagePasted"), "\uE8B7", 1500);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PasteClipboardImage] Error: {ex.Message}");
                return false;
            }
        }

        private static byte[] EncodeBitmapSourceToPng(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg" };

        private static bool IsSupportedImageFile(string path)
        {
            return Array.Exists(SupportedImageExtensions,
                ext => string.Equals(System.IO.Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase));
        }

        private void EditorPage_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                (e.Data.GetData(DataFormats.FileDrop) is string[] files) &&
                files.Any(IsSupportedImageFile))
            {
                e.Handled = true;
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void EditorPage_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files))
                    return;

                var imageFiles = files.Where(IsSupportedImageFile).ToList();
                if (imageFiles.Count == 0)
                    return;

                e.Handled = true;

                // Resolve the page under the cursor (PagesContainer coordinates,
                // same translate trick as FindPageAtContainerPoint).
                var pointInContainer = e.GetPosition(PagesContainer);
                PdfPageControl targetPage = null;
                Point pagePoint = default;
                foreach (var p in _pageControls)
                {
                    var ptInPage = PagesContainer.TranslatePoint(pointInContainer, p);
                    if (ptInPage.X >= 0 && ptInPage.X <= p.ActualWidth &&
                        ptInPage.Y >= 0 && ptInPage.Y <= p.ActualHeight)
                    {
                        targetPage = p;
                        pagePoint = ptInPage;
                        break;
                    }
                }

                if (targetPage == null)
                {
                    // Dropped over a gap / chrome: fall back to the first
                    // visible page, centered, stacking multiples.
                    targetPage = GetFirstVisiblePage();
                    if (targetPage == null)
                        return;
                    pagePoint = new Point(
                        targetPage.ActualWidth / 2,
                        targetPage.ActualHeight / 2);
                }

                var addedContainers = new List<System.Windows.Controls.Grid>();
                var addedStrokes = new List<System.Windows.Ink.Stroke>();
                double stackOffset = 0;
                foreach (var file in imageFiles)
                {
                    byte[] bytes;
                    try { bytes = File.ReadAllBytes(file); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EditorPage_Drop] Cannot read {file}: {ex.Message}");
                        continue;
                    }

                    var dropPoint = new Point(pagePoint.X + stackOffset, pagePoint.Y + stackOffset);
                    stackOffset += 20; // multiple files land stair-stepped

                    var container = targetPage.AddImage(bytes, dropPoint);
                    if (container != null)
                        addedContainers.Add(container);
                }

                if (addedContainers.Count == 0)
                    return;

                PushUndoAction(new ItemsAddedAction(targetPage, addedStrokes, addedContainers));

                foreach (var page in _pageControls)
                {
                    if (page != targetPage && page.HasSelection)
                        page.ClearSelection();
                }
                targetPage.SelectItems(addedStrokes, addedContainers);

                MarkDirty();

                var mw = Window.GetWindow(this) as MainWindow;
                mw?.ShowToast(LocalizationService.Get("Editor.ImageAdded"), "\uE8B7", 1500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EditorPage_Drop] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Task 13: Ctrl+D duplicates the current selection in place with a
        /// (+20,+20) offset, without touching the clipboard. Clones are built
        /// from the live objects (strokes keep their pressure data), pushed as
        /// ONE ItemsAddedAction (single Ctrl+Z removes the whole duplicate),
        /// then auto-selected — mirroring the Task 8.2 paste auto-select flow.
        /// </summary>
        private void DuplicateSelection()
        {
            var page = _activeSelectionPage;
            if (page == null || !page.HasSelection)
                return;

            try
            {
                const double offsetX = 20.0;
                const double offsetY = 20.0;

                var clonedStrokes = new List<System.Windows.Ink.Stroke>();
                var clonedContainers = new List<System.Windows.Controls.Grid>();

                // Clone strokes: fresh StylusPointCollection (offset applied,
                // PressureFactor preserved) + cloned DrawingAttributes.
                var duplicatedShapeGroups = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var stroke in page.SelectedStrokes)
                {
                    var points = new StylusPointCollection();
                    foreach (var pt in stroke.StylusPoints)
                        points.Add(new StylusPoint(pt.X + offsetX, pt.Y + offsetY, pt.PressureFactor));

                    var clone = new Stroke(points, stroke.DrawingAttributes.Clone());
                    var shape = ShapeStrokeMetadata.Read(stroke);
                    if (!string.IsNullOrWhiteSpace(shape.GroupId))
                    {
                        if (!duplicatedShapeGroups.TryGetValue(shape.GroupId, out var duplicateGroupId))
                        {
                            duplicateGroupId = Guid.NewGuid().ToString("N");
                            duplicatedShapeGroups[shape.GroupId] = duplicateGroupId;
                        }
                        ShapeStrokeMetadata.Apply(
                            clone,
                            duplicateGroupId,
                            shape.Kind,
                            shape.PartIndex,
                            shape.IsDashed);
                    }
                    page.AddStrokeQuiet(clone);
                    clonedStrokes.Add(clone);
                }

                // Clone text containers through the shared CreateTextBox path
                // (same structure/event hooks as pasted copies; select:false
                // keeps them read-only until the auto-select below).
                foreach (var container in page.SelectedTextContainers)
                {
                    if (container.Children.OfType<TextBox>().FirstOrDefault() is TextBox textBox)
                    {
                        var color = (textBox.Foreground as SolidColorBrush)?.Color ?? _textColor;
                        var clone = CreateTextBox(
                            page,
                            new Point(Canvas.GetLeft(container) + offsetX, Canvas.GetTop(container) + offsetY),
                            color, textBox.FontSize, textBox.Text,
                            select: false,
                            alignToPointer: false,
                            bold: textBox.FontWeight >= FontWeights.Bold,
                            italic: textBox.FontStyle == FontStyles.Italic,
                            fontFamily: textBox.FontFamily?.Source,
                            alignment: textBox.TextAlignment,
                            width: GetPersistedTextWidth(container),
                            height: GetPersistedTextHeight(container));
                        if (clone != null)
                            clonedContainers.Add(clone);
                    }
                    else if (PdfPageControl.IsImageContainer(container))
                    {
                        // Task 19: duplicate image annotations from their raw
                        // payload, keeping the live size.
                        var imageData = page.GetImageData(container);
                        if (imageData != null)
                        {
                            var clone = page.AddImage(imageData,
                                new Point(Canvas.GetLeft(container) + offsetX, Canvas.GetTop(container) + offsetY),
                                container.ActualWidth > 0 ? container.ActualWidth : container.Width,
                                container.ActualHeight > 0 ? container.ActualHeight : container.Height);
                            if (clone != null)
                                clonedContainers.Add(clone);
                        }
                    }
                    else if (page.GetOverlayData(container) is StickyNoteAnnotation sticky)
                    {
                        var clone = page.AddStickyNote(new StickyNoteAnnotation
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            X = Canvas.GetLeft(container) + offsetX,
                            Y = Canvas.GetTop(container) + offsetY,
                            Text = sticky.Text,
                            Width = container.ActualWidth > 0 ? container.ActualWidth : container.Width,
                            Height = container.ActualHeight > 0 ? container.ActualHeight : container.Height,
                            R = sticky.R,
                            G = sticky.G,
                            B = sticky.B
                        });
                        if (clone != null)
                            clonedContainers.Add(clone);
                    }
                }

                if (clonedStrokes.Count == 0 && clonedContainers.Count == 0)
                    return;

                // Undo state FIRST (selection is UI state, not undoable).
                PushUndoAction(new ItemsAddedAction(page, clonedStrokes, clonedContainers));

                // Cross-page rule (Task 7): clear any selection lingering on
                // other pages, then auto-select the duplicates (Task 8.2 flow).
                foreach (var other in _pageControls)
                {
                    if (other != page && other.HasSelection)
                        other.ClearSelection();
                }
                page.SelectItems(clonedStrokes, clonedContainers);

                MarkDirty();

                var mw = Window.GetWindow(this) as MainWindow;
                mw?.ShowToast(LocalizationService.Get("Editor.Duplicated"), "\uE8C8", 1500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DuplicateSelection] Error: {ex.Message}");
            }
        }

        private PdfPageControl GetFirstVisiblePage()
        {
            foreach (var page in _pageControls)
            {
                var bounds = new Rect(0, 0, PdfScrollViewer.ViewportWidth, PdfScrollViewer.ViewportHeight);
                var pageBounds = new Rect(Canvas.GetLeft(page), Canvas.GetTop(page), page.ActualWidth, page.ActualHeight);
                if (bounds.IntersectsWith(pageBounds))
                    return page;
            }
            return _pageControls.FirstOrDefault();
        }

        private static Border CreateColorSelectionIndicator(double size)
        {
            var inner = new Border
            {
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            inner.SetResourceReference(Border.BorderBrushProperty, "ThemeSurfaceBrush");

            var outer = new Border
            {
                Width = size,
                Height = size,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(2),
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = inner
            };
            outer.SetResourceReference(Border.BorderBrushProperty, "ThemeFocusBrush");
            return outer;
        }

        private void TrackToolPopupOpenedHandler(Popup popup, EventHandler handler)
        {
            if (popup == null || handler == null)
                return;

            popup.Opened += handler;
            _toolPopupOpenedHandlers.Add((popup, handler));
        }

        private void DetachToolPopupHandlers()
        {
            foreach (var registration in _toolPopupOpenedHandlers)
                registration.Popup.Opened -= registration.Handler;
            _toolPopupOpenedHandlers.Clear();

            // PopupZOrderHelper owns a separate Opened hook for each tool popup.
            // Detach those exact delegates before CreateToolPopups replaces the
            // instances during a localization refresh.
            PopupZOrderHelper.UnfixPopupTopmost(_penPopup);
            PopupZOrderHelper.UnfixPopupTopmost(_highlighterPopup);
            PopupZOrderHelper.UnfixPopupTopmost(_eraserPopup);
            PopupZOrderHelper.UnfixPopupTopmost(_shapePopup);
            PopupZOrderHelper.UnfixPopupTopmost(_selectionPopup);
            UnfixTransientUiHooks();
        }

        private void FixToolPopupZOrder()
        {
            _transientUiRegistry.Register(_penPopup);
            _transientUiRegistry.Register(_highlighterPopup);
            _transientUiRegistry.Register(_eraserPopup);
            _transientUiRegistry.Register(_shapePopup);
            _transientUiRegistry.Register(_selectionPopup);
            _transientUiRegistry.Register(_textColorPopup);
            _transientUiRegistry.Register(PdfViewerContextMenu);
            _transientUiRegistry.Register(_textFontFamilyCombo);
            _transientUiRegistry.Register(_textAlignmentCombo);
            PopupZOrderHelper.FixPopupTopmost(_penPopup);
            PopupZOrderHelper.FixPopupTopmost(_highlighterPopup);
            PopupZOrderHelper.FixPopupTopmost(_eraserPopup);
            PopupZOrderHelper.FixPopupTopmost(_shapePopup);
            PopupZOrderHelper.FixPopupTopmost(_selectionPopup);
            PopupZOrderHelper.FixPopupTopmost(_textColorPopup);
            PopupZOrderHelper.FixContextMenuTopmost(PdfViewerContextMenu);
            PopupZOrderHelper.FixComboBoxPopupTopmost(_textFontFamilyCombo);
            PopupZOrderHelper.FixComboBoxPopupTopmost(_textAlignmentCombo);
            if (_stickyNotePopup != null)
                PopupZOrderHelper.FixPopupTopmost(_stickyNotePopup);
            foreach (var page in _pageControls.ToList())
                page.EnsureTransientUiHooks();
        }

        private void UnfixTransientUiHooks()
        {
            PopupZOrderHelper.UnfixPopupTopmost(_penPopup);
            PopupZOrderHelper.UnfixPopupTopmost(_highlighterPopup);
            PopupZOrderHelper.UnfixPopupTopmost(_eraserPopup);
            PopupZOrderHelper.UnfixPopupTopmost(_shapePopup);
            PopupZOrderHelper.UnfixPopupTopmost(_selectionPopup);
            PopupZOrderHelper.UnfixPopupTopmost(_textColorPopup);
            PopupZOrderHelper.UnfixPopupTopmost(_stickyNotePopup);
            PopupZOrderHelper.UnfixComboBoxPopupTopmost(_textFontFamilyCombo);
            PopupZOrderHelper.UnfixComboBoxPopupTopmost(_textAlignmentCombo);
            PopupZOrderHelper.UnfixContextMenuTopmost(PdfViewerContextMenu);
            foreach (var page in _pageControls.ToList())
                page.UnfixTransientUiHooks();
        }

        private void EnsureTransientUiHooks()
        {
            FixToolPopupZOrder();
        }

        private Popup BuildToolPopup(
            string sizeLabel, double min, double max, double value, double step, Action<double> sizeChanged,
            string colorLabel, Color initialColor, Action<Color> colorChanged,
            out Slider sizeSlider,
            Func<List<string>> recentColors = null,
            string sizeAutomationId = null)
        {
            var popup = new Popup { Placement = PlacementMode.Bottom, StaysOpen = true, AllowsTransparency = true };
            var panel = new StackPanel { Margin = new Thickness(16) };
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 20,
                    ShadowDepth = 4,
                    Opacity = ThemeService.GetShadowOpacity(),
                    Color = Colors.Black
                }
            };
            border.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");

            // Size section
            var sizeHeader = ThemeSubtleHeader(new TextBlock
            {
                Text = sizeLabel,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                TickFrequency = step,
                Width = 240,
                Height = 32,
                MinHeight = 32,
                Focusable = true,
                IsSnapToTickEnabled = true
            };
            ApplyToolbarFocusVisualStyle(slider);
            ToolTipService.SetToolTip(slider, sizeLabel);
            AutomationProperties.SetAutomationId(slider, sizeAutomationId ?? "Editor.Popup.Size");
            AutomationProperties.SetName(slider, sizeLabel);
            AutomationProperties.SetHelpText(slider, sizeLabel);
            KeyboardNavigation.SetIsTabStop(slider, true);
            slider.ValueChanged += (s, e) => sizeChanged?.Invoke(e.NewValue);
            panel.Children.Add(sizeHeader);
            panel.Children.Add(slider);
            // Keep the slider available so runtime size changes can refresh the
            // preview without rebuilding the popup.
            sizeSlider = slider;

            if (colorLabel != null)
            {
                // Separator
                var separator = ThemeDivider(new Border
                {
                    Height = 1,
                    Margin = new Thickness(-16, 14, -16, 14)
                });
                panel.Children.Add(separator);

                    var colorHeader = ThemeSubtleHeader(new TextBlock
                    {
                        Text = colorLabel,
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 10)
                });
                panel.Children.Add(colorHeader);

                StackPanel recentRow = null;
                Grid paletteGrid = null;
                Border selectionIndicator = null;
                Color selectedColor = initialColor;

                void UpdateColorMarkers(Color selected)
                {
                    selectedColor = selected;

                    if (recentRow != null)
                    {
                        foreach (var swatch in recentRow.Children.OfType<Button>())
                        {
                            if (swatch.Content is not Border visual)
                                continue;

                            bool isSelected = swatch.Tag is Color swatchColor && swatchColor == selected;
                            visual.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                            visual.SetResourceReference(
                                Border.BorderBrushProperty,
                                isSelected ? "ThemeFocusBrush" : "ThemeBorderBrush");
                        }
                    }

                    if (paletteGrid == null || selectionIndicator == null)
                        return;

                    selectionIndicator.Visibility = Visibility.Collapsed;
                    foreach (var element in paletteGrid.Children)
                    {
                        if (element is Button cell && cell.Tag is Color cellColor && cellColor == selected)
                        {
                            selectionIndicator.Margin = cell.Margin;
                            selectionIndicator.Visibility = Visibility.Visible;
                            break;
                        }
                    }
                }

                void ApplyPickedColor(Color picked)
                {
                    UpdateColorMarkers(picked);
                    colorChanged?.Invoke(picked);
                }

                // Task 14: "最近 Recent" swatch row above the palette (hidden
                // while empty); repopulated on every popup open.
                if (recentColors != null)
                {
                    var recentSection = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
                    recentRow = new StackPanel { Orientation = Orientation.Horizontal };
                    recentSection.Children.Add(ThemeSubtleHeader(new TextBlock
                    {
                        Text = LocalizationService.Get("Editor.Recent"),
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8)
                    }));
                    recentSection.Children.Add(recentRow);
                    panel.Children.Add(recentSection);
                    EventHandler refreshRecentColors = (s, e) =>
                    {
                        RefreshRecentColorsRow(
                            recentSection,
                            recentRow,
                            recentColors,
                            ApplyPickedColor,
                            UpdateColorMarkers);
                        UpdateColorMarkers(selectedColor);
                    };
                    TrackToolPopupOpenedHandler(popup, refreshRecentColors);
                }

                // HSV color palette grid
                int cols = 12;
                int rows = 8;
                double cellSize = 32;
                paletteGrid = new Grid { Width = cols * cellSize, Height = rows * cellSize, ClipToBounds = true };

                // Two theme-driven rings keep a white or black swatch visible
                // in light, dark and high-contrast palettes.
                selectionIndicator = CreateColorSelectionIndicator(cellSize);

                for (int row = 0; row < rows; row++)
                {
                    for (int col = 0; col < cols; col++)
                    {
                        Color cellColor;
                        if (row == 0)
                        {
                            // Top row: grayscale from black to white
                            byte gray = (byte)(col * 255 / (cols - 1));
                            cellColor = Color.FromRgb(gray, gray, gray);
                        }
                        else
                        {
                            // HSV palette: hue across columns, saturation/value down rows
                            double hue = col * 360.0 / cols;
                            double saturation = 1.0;
                            double val = 1.0;
                            if (row <= rows / 2)
                            {
                                // Top half: full value, varying saturation (light 闁?saturated)
                                saturation = (double)row / (rows / 2);
                            }
                            else
                            {
                                // Bottom half: full saturation, decreasing value (saturated 闁?dark)
                                val = 1.0 - (double)(row - rows / 2) / (rows / 2);
                            }
                            cellColor = HsvToColor(hue, saturation, val);
                        }

                        var cellVisual = new Border
                        {
                            Width = cellSize - 6,
                            Height = cellSize - 6,
                            Background = new SolidColorBrush(cellColor),
                            CornerRadius = new CornerRadius(4),
                            BorderThickness = new Thickness(1)
                        };
                        cellVisual.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
                        var cell = new Button
                        {
                            Width = cellSize,
                            Height = cellSize,
                            Padding = new Thickness(3),
                            Background = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            HorizontalContentAlignment = HorizontalAlignment.Center,
                            VerticalContentAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(col * cellSize, row * cellSize, 0, 0),
                            Cursor = Cursors.Hand,
                            Focusable = true,
                            Content = cellVisual,
                            Tag = cellColor
                        };
                        ApplyToolbarPopupButtonStyle(cell);
                        string cellLabel = $"#{cellColor.R:X2}{cellColor.G:X2}{cellColor.B:X2}";
                        ToolTipService.SetToolTip(cell, cellLabel);
                        AutomationProperties.SetAutomationId(cell, $"Editor.Palette.Color.{row}.{col}");
                        AutomationProperties.SetName(cell, cellLabel);
                        AutomationProperties.SetHelpText(cell, cellLabel);

                        cell.Click += (s, e) =>
                        {
                            if (s is Button b && b.Tag is Color picked)
                                ApplyPickedColor(picked);
                            e.Handled = true;
                        };

                        paletteGrid.Children.Add(cell);
                    }
                }

                paletteGrid.Children.Add(selectionIndicator);
                UpdateColorMarkers(initialColor);
                panel.Children.Add(paletteGrid);
            }

            popup.Child = border;
            return popup;
        }

        private static void EnableToolPopupScrolling(Popup popup)
        {
            if (popup?.Child is not Border border || border.Child is not StackPanel panel)
                return;

            border.Child = new ScrollViewer
            {
                Content = panel,
                MaxHeight = Math.Max(320, Math.Min(680, SystemParameters.WorkArea.Height - 120)),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false
            };
        }

        private void OpenPdfSearch()
        {
            PdfSearchPanel.Visibility = Visibility.Visible;
            PdfSearchTextBox.Focus();
            PdfSearchTextBox.SelectAll();
        }

        private void ClosePdfSearch()
        {
            _pdfSearchCts?.Cancel();
            PdfSearchPanel.Visibility = Visibility.Collapsed;
            PdfSearchResultsListBox.Items.Clear();
            PdfSearchStatusTextBlock.Text = string.Empty;
            foreach (var page in _pageControls)
                page.ClearPdfTextSelection();
            _pdfSearchResults.Clear();
        }

        private async void PdfSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _pdfSearchCts?.Cancel();
            _pdfSearchCts = new CancellationTokenSource();
            using var operationLease = CaptureDocumentOperationLease(
                cancellationToken: _pdfSearchCts.Token);
            try
            {
                await RunPdfSearchAsync(
                    PdfSearchTextBox.Text?.Trim() ?? string.Empty,
                    operationLease.Token,
                    operationLease);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (ValidateDocumentOperationLease(operationLease))
                    System.Diagnostics.Debug.WriteLine($"[PdfSearch] Failed to search document: {ex}");
            }
        }

        private async Task RunPdfSearchAsync(
            string query,
            CancellationToken cancellationToken,
            DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(cancellationToken: cancellationToken);
            try
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                _pdfSearchResults.Clear();
                PdfSearchResultsListBox.Items.Clear();
                if (string.IsNullOrWhiteSpace(query))
                {
                    PdfSearchStatusTextBlock.Text = string.Empty;
                    return;
                }

                PdfSearchStatusTextBlock.Text = LocalizationService.Get("Editor.Searching");
                for (int pageIndex = 0; pageIndex < _pageControls.Count; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = await _pdfService.GetPageTextInfoAsync(pageIndex, cancellationToken);
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    string text = info.Text ?? string.Empty;
                    int offset = 0;
                    while (offset < text.Length)
                    {
                        int hit = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                        if (hit < 0)
                            break;

                        int snippetStart = Math.Max(0, hit - 28);
                        int snippetLength = Math.Min(text.Length - snippetStart, query.Length + 56);
                        string snippet = text.Substring(snippetStart, snippetLength).Replace('\r', ' ').Replace('\n', ' ');
                        _pdfSearchResults.Add(new PdfSearchResult
                        {
                            PageIndex = pageIndex,
                            StartOffset = hit,
                            Length = query.Length,
                            DisplayText = $"{LocalizationService.Format("Editor.PageNumber", pageIndex + 1)}  {snippet}"
                        });
                        offset = hit + Math.Max(1, query.Length);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                foreach (var result in _pdfSearchResults)
                    PdfSearchResultsListBox.Items.Add(new ListBoxItem { Content = result.DisplayText, Tag = result });
                PdfSearchStatusTextBlock.Text = LocalizationService.Format("Editor.SearchResults", _pdfSearchResults.Count);
                if (_pdfSearchResults.Count > 0)
                    PdfSearchResultsListBox.SelectedIndex = 0;
            }
            finally
            {
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private async void PdfSearchResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PdfSearchResultsListBox.SelectedItem is ListBoxItem item && item.Tag is PdfSearchResult result)
            {
                using var operationLease = CaptureDocumentOperationLease(result);
                await JumpToPdfSearchResultAsync(result, operationLease);
            }
        }

        private async Task JumpToPdfSearchResultAsync(
            PdfSearchResult result,
            DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(result);
            try
            {
                if (!ValidateDocumentOperationLease(operationLease, result))
                    return;
                if (result == null || !_pdfSearchResults.Contains(result) ||
                    result.PageIndex < 0 || result.PageIndex >= _pageControls.Count)
                    return;
                JumpToPage(result.PageIndex);
                var info = await _pdfService.GetPageTextInfoAsync(result.PageIndex, operationLease.Token);
                if (!ValidateDocumentOperationLease(operationLease, result) || !_pdfSearchResults.Contains(result))
                    return;
                var page = _pageControls[result.PageIndex];
                foreach (var other in _pageControls)
                {
                    if (!ReferenceEquals(other, page))
                        other.ClearPdfTextSelection();
                }
                page.SetPdfTextSelectionRects(BuildPdfTextSelectionRects(info, result.StartOffset, result.StartOffset + result.Length - 1));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // Search results are transient models. A replacement load or
                // closed search surface must not surface an old read failure.
                if (ValidateDocumentOperationLease(operationLease, result) &&
                    _pdfSearchResults.Contains(result))
                    System.Diagnostics.Debug.WriteLine($"[PdfSearchSelection] Failed to select result: {ex}");
            }
            finally
            {
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private async Task MovePdfSearchSelectionAsync(bool backwards)
        {
            using var operationLease = CaptureDocumentOperationLease();
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            if (PdfSearchPanel.Visibility != Visibility.Visible || _pdfSearchResults.Count == 0)
                return;
            int current = PdfSearchResultsListBox.SelectedIndex;
            int next = (current + (backwards ? -1 : 1) + _pdfSearchResults.Count) % _pdfSearchResults.Count;
            PdfSearchResultsListBox.SelectedIndex = next;
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            await JumpToPdfSearchResultAsync(_pdfSearchResults[next], operationLease);
        }

        private async void PdfSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await MovePdfSearchSelectionAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            }
            else if (e.Key == Key.Escape)
            {
                ClosePdfSearch();
                e.Handled = true;
            }
        }

        private void ClosePdfSearchButton_Click(object sender, RoutedEventArgs e) => ClosePdfSearch();

        private async void EditorPage_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Task 16: fullscreen immersive mode. F11 toggles it (never
            // while a text box is being edited); ESC always leaves it
            // first — the existing "ESC resets tool" behavior only runs
            // when not immersive (the handled preview suppresses the
            // bubbling KeyDown branch).
            if (e.Key == Key.F11 && !IsEditableTextInputFocused())
            {
                ToggleImmersiveMode();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _isImmersiveMode)
            {
                ToggleImmersiveMode();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _resizingTextContainer != null)
            {
                CancelTextResize(restoreBounds: true);
                e.Handled = true;
                return;
            }

            if (!IsEditableTextInputFocused() && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                OpenPdfSearch();
                e.Handled = true;
                return;
            }

            if (!IsEditableTextInputFocused() && e.Key == Key.F3)
            {
                await MovePdfSearchSelectionAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && PdfSearchPanel.Visibility == Visibility.Visible)
            {
                ClosePdfSearch();
                e.Handled = true;
                return;
            }

            if (!IsEditableTextInputFocused() && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (e.Key == Key.PageDown)
                {
                    JumpToPage(Math.Min(_pageControls.Count - 1, GetCurrentPageIndex() + 1));
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.PageUp)
                {
                    JumpToPage(Math.Max(0, GetCurrentPageIndex() - 1));
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Home)
                {
                    JumpToPage(0);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.End)
                {
                    JumpToPage(_pageControls.Count - 1);
                    e.Handled = true;
                    return;
                }
            }

            if (!IsEditableTextInputFocused() && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.M)
            {
                BookmarkToggleButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (!IsEditableTextInputFocused() && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
            {
                ActivateTool(ToolType.Select);
                var page = _pageControls.Count == 0 ? null : _pageControls[GetCurrentPageIndex()];
                if (page != null)
                {
                    page.SelectAllAnnotations();
                    _activeSelectionPage = page;
                }
                e.Handled = true;
                return;
            }

            if (await TryHandleUndoRedoShortcutAsync(e))
                return;

            // Resize handles own their arrow-key contract. PreviewKeyDown runs
            // before the handle's KeyDown, so let the handle receive the event
            // instead of treating the same arrow as a text-box nudge.
            if (e.OriginalSource is TextResizeHandleBorder
                || Keyboard.FocusedElement is TextResizeHandleBorder)
            {
                return;
            }

            if (_currentTool == ToolType.Text
                && _selectedTextBox != null
                && TryGetTextBoxNudge(e.Key, out double nudgeX, out double nudgeY)
                && (Keyboard.Modifiers == ModifierKeys.None
                    || Keyboard.Modifiers == ModifierKeys.Shift
                    || Keyboard.Modifiers == ModifierKeys.Alt))
            {
                bool textEditingHasFocus = IsEditableTextInputFocused();
                bool allowNudge = !textEditingHasFocus
                    || Keyboard.Modifiers == ModifierKeys.Alt;
                if (allowNudge)
                {
                    double step = Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1;
                    NudgeSelectedTextBox(nudgeX * step, nudgeY * step);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                if (_currentTool == ToolType.Select && _activeSelectionPage != null && _activeSelectionPage.HasSelection)
                {
                    DeleteSelection();
                    e.Handled = true;
                }
                else if (_currentTool == ToolType.Text && _selectedTextBox != null)
                {
                    if (string.IsNullOrEmpty(_selectedTextBox.Text) && e.Key == Key.Back)
                    {
                        DeleteSelectedTextBox();
                        e.Handled = true;
                    }
                    else if (!_selectedTextBox.IsFocused)
                    {
                        DeleteSelectedTextBox();
                        e.Handled = true;
                    }
                }
            }
        }

        private void EditorPage_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ShouldClosePopupOnPointerDown(e.OriginalSource as DependencyObject, out bool consumePointerEvent))
            {
                bool shouldArmPopupDismissal = IsActiveInkPopupOpen();
                CloseTransientUi("outside click");
                if (shouldArmPopupDismissal)
                    ArmPopupDismissalGestureIfNeeded(e.OriginalSource as DependencyObject);
                e.Handled = consumePointerEvent;
            }
        }

        private void EditorPage_PreviewStylusDown(object sender, StylusDownEventArgs e)
        {
            if (ShouldClosePopupOnPointerDown(e.OriginalSource as DependencyObject, out bool consumePointerEvent))
            {
                bool shouldArmPopupDismissal = IsActiveInkPopupOpen();
                CloseTransientUi("outside click");
                if (shouldArmPopupDismissal)
                    ArmPopupDismissalGestureIfNeeded(e.OriginalSource as DependencyObject);
                e.Handled = consumePointerEvent;
            }
        }

        private bool IsActiveInkPopupOpen()
        {
            return _currentTool switch
            {
                ToolType.Pen => _penPopup?.IsOpen == true,
                ToolType.Highlighter => _highlighterPopup?.IsOpen == true,
                _ => false
            };
        }

        private void ArmPopupDismissalGestureIfNeeded(DependencyObject originalSource)
        {
            // Only native freehand ink has a collection-time tap to suppress.
            // Shape/laser/area-highlight already own their drag threshold, and
            // eraser input is intentionally left untouched.
            if (_currentTool != ToolType.Pen && _currentTool != ToolType.Highlighter)
            {
                return;
            }

            var page = FindAncestor<PdfPageControl>(originalSource);
            if (page == null)
                return;

            // The page has several interactive overlay descendants. Hidden Ink
            // paths consume their own click and never enter native InkCanvas,
            // so arming from any PdfPageControl descendant would leave stale
            // dismissal state for a later unrelated stroke. Keep ordinary
            // native InkCanvas children eligible by resolving the routed target
            // to the page's actual InkCanvas instance.
            var nativeInkCanvas = FindAncestor<InkCanvas>(originalSource);
            if (nativeInkCanvas == null || !ReferenceEquals(nativeInkCanvas, page.InkCanvas))
                return;

            page.ArmPendingPopupDismissalGesture();
        }

        private bool ShouldClosePopupOnPointerDown(DependencyObject originalSource, out bool consumePointerEvent)
        {
            consumePointerEvent = false;
            if (originalSource == null) return false;

            var popups = new[]
            {
                _penPopup, _highlighterPopup, _eraserPopup, _shapePopup,
                _selectionPopup, _textColorPopup, _stickyNotePopup
            };
            bool anyPopupOpen = false;
            foreach (var popup in popups)
            {
                if (popup != null && popup.IsOpen)
                {
                    anyPopupOpen = true;
                    if (IsSourceInPopup(originalSource, popup))
                    {
                        return false;
                    }
                }
            }

            if (!anyPopupOpen && !_transientUiRegistry.HasOpenSurface()) return false;

            if (IsSourceInOwnedTextComboBox(originalSource))
                return false;

            if (IsSourceInToolbar(originalSource))
            {
                return false;
            }

            // Select's popup is a configuration surface for the canvas below.
            // Dismiss it, but let the same outside pointer continue to the
            // page-local selection handler so the first click is not lost.
            consumePointerEvent = _currentTool != ToolType.Select && !IsImmediateDrawingToolActive();
            return true;
        }

        private bool IsImmediateDrawingToolActive()
        {
            return _currentTool == ToolType.Pen ||
                   _currentTool == ToolType.Highlighter ||
                   _currentTool == ToolType.HiddenInk ||
                   _currentTool == ToolType.Eraser ||
                   _currentTool == ToolType.Shape ||
                   _currentTool == ToolType.Laser;
        }

        private bool IsSourceInPopup(DependencyObject source, Popup popup)
        {
            if (popup?.Child == null) return false;
            return IsDescendantOf(source, popup.Child);
        }

        private bool IsSourceInToolbar(DependencyObject source)
        {
            return IsDescendantOf(source, ToolbarBorder);
        }

        private bool IsSourceInOwnedTextComboBox(DependencyObject source)
        {
            var item = FindAncestor<ComboBoxItem>(source);
            if (item == null)
                return false;

            var owner = ItemsControl.ItemsControlFromItemContainer(item) as ComboBox;
            return ReferenceEquals(owner, _textFontFamilyCombo)
                || ReferenceEquals(owner, _textAlignmentCombo);
        }

        private bool IsDescendantOf(DependencyObject descendant, DependencyObject ancestor)
        {
            if (descendant == null || ancestor == null) return false;
            var current = descendant;
            while (current != null)
            {
                if (current == ancestor) return true;
                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }
            return false;
        }

        private static T FindAncestor<T>(DependencyObject descendant) where T : DependencyObject
        {
            var current = descendant;
            while (current != null)
            {
                if (current is T match)
                    return match;

                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }

            return null;
        }

        // Task 11: 事件源是否位于某个 ScrollBar 的可视子树内
        // （轨道 / Track / Thumb 及其模板子元素）
        private static bool IsOriginalSourceOverScrollbar(DependencyObject source)
        {
            return source != null && FindAncestor<ScrollBar>(source) != null;
        }

        private void CloseToolPopups(ToolType toolToKeepOpen = ToolType.None)
        {
            if (toolToKeepOpen != ToolType.Pen && _penPopup != null)
                _penPopup.IsOpen = false;

            if (toolToKeepOpen != ToolType.Highlighter && _highlighterPopup != null)
                _highlighterPopup.IsOpen = false;

            if (toolToKeepOpen != ToolType.Eraser && _eraserPopup != null)
                _eraserPopup.IsOpen = false;

            if (toolToKeepOpen != ToolType.Shape && _shapePopup != null)
                _shapePopup.IsOpen = false;

            if (toolToKeepOpen != ToolType.Select && _selectionPopup != null)
                _selectionPopup.IsOpen = false;
        }

        /// <summary>
        /// Closes every editor-owned transient surface. This operation is
        /// idempotent and intentionally excludes document-save dialogs and
        /// ordinary text edit sessions. Sticky Note editing follows Cancel,
        /// so switching apps can never commit half-written popup text.
        /// </summary>
        public void CloseTransientUi(string reason = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Input,
                    new Action(() => CloseTransientUi(reason)));
                return;
            }

            // Every close boundary is also a gesture boundary.  Restore any
            // in-flight text geometry and page selection snapshot before the
            // popup registry is swept, so capture-loss callbacks cannot leave
            // a stale transaction behind.
            CancelInteraction(reason);
            _pdfSearchCts?.Cancel();
            ClosePdfSearch();
            CancelStickyNoteEdit();
            CloseToolPopups();
            if (_textColorPopup != null)
                _textColorPopup.IsOpen = false;
            if (_stickyNotePopup != null)
                _stickyNotePopup.IsOpen = false;
            if (PdfViewerContextMenu != null)
                PdfViewerContextMenu.IsOpen = false;
            // Sticky marker menus are owned by the page control rather than
            // the editor's toolbar registry. Sweep those live owners too so a
            // right-click menu cannot survive tab switching or Alt-Tab.
            foreach (var page in _pageControls.ToList())
            {
                foreach (var container in page.GetOverlayContainers())
                {
                    if (container.ContextMenu != null)
                        container.ContextMenu.IsOpen = false;
                }
            }
            _transientUiRegistry.CloseAll();
            // Release every exact PopupZOrderHelper delegate before the close
            // barrier returns. Live controls stay reusable because the paired
            // ensure path is idempotent and installs one fresh hook only.
            UnfixTransientUiHooks();
            EnsureTransientUiHooks();
        }

        private void ToggleToolButton(ToolType tool, ToggleButton button, Popup popup = null)
        {
            if (_isUpdatingToolState) return;

            var isActiveTool = _currentTool == tool;
            CloseToolPopups();

            if (isActiveTool)
            {
                ActivateTool(ToolType.None);
                return;
            }

            ActivateTool(tool);

            if (tool == ToolType.Select && _selectionPopup != null)
            {
                _selectionPopup.PlacementTarget = button;
                _selectionPopup.IsOpen = true;
            }
            else if (popup != null)
            {
                popup.PlacementTarget = button;
                popup.IsOpen = true;
            }
        }

        public async Task LoadPdfAsync(string filePath)
        {
            _currentPdfPath = filePath;
            await LoadPdf(filePath);
        }

        // Tracks which pages have been rendered with their initial image
        private readonly HashSet<int> _pagesInitiallyRendered = new HashSet<int>();

        private async Task LoadPdf(string filePath)
        {
            ClearThumbnailDropIndicator();
            ResetThumbnailDragState();
            CloseTransientUi("load");
            CancelRenderWork();
            _thumbnailLoadCts?.Cancel();
            _thumbnailLoadCts?.Dispose();
            _thumbnailLoadCts = null;
            _thumbnailPagesLoading.Clear();
            _thumbnailPageLoadSessions.Clear();
            _isRefreshingThumbnails = false;
            CancelActiveLoad();
            _loadCts = new CancellationTokenSource();
            var sessionId = Interlocked.Increment(ref _loadSessionId);
            _thumbnailRevisionGate.BeginSession(sessionId);
            _documentOperationSession.Begin(sessionId, filePath, _pdfService);
            var loadLease = CaptureDocumentOperationLease(
                sessionId,
                filePath,
                cancellationToken: _loadCts.Token);
            var token = loadLease.Token;
            _sidebarLoadSessionGate.Begin(sessionId, filePath);

            ShowLoadingOverlay();
            DetachAllPageControlEvents();
            PagesContainer.Children.Clear();
            _pageControls.Clear();
            // Reset the paste anchor so a stale reference to a detached page
            // control can't become the paste target in the new document.
            _lastClickedPage = null;
            _pageTopOffsets.Clear();
            _pageHeights.Clear();
            _pageDeleteButtons.Clear();
            _pageInsertButtons.Clear();
            DeselectTextBox();
            _documentSaveCoordinator.Reset();
            SyncDirtyStateMirror();
            _lastRenderedDpiScale = 1.0;
            _pagesRenderedAtScale.Clear();
            _pagesInitiallyRendered.Clear();
            ClearThumbnailCache();
            DisposeSelectablePdfDocument();
            UpdatePdfSurfaceVisibility();
            ClearUndoRedoHistory();

            try
            {
                await _pdfService.LoadPdfAsync(filePath, token);
                if (!ValidateDocumentOperationLease(loadLease))
                    return;
                await LoadSelectablePdfDocumentAsync(filePath, token);
                if (!ValidateDocumentOperationLease(loadLease))
                    return;
                RecentFilesService.UpdateMetadata(filePath, _pdfService.PageCount, File.GetLastWriteTimeUtc(filePath));

                int pageCount = _pdfService.PageCount;
                double currentTop = 0;

                for (int i = 0; i < pageCount; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!ValidateDocumentOperationLease(loadLease))
                        return;

                    var (w, h) = _pdfService.GetPageSizeInDips(i);
                    if (w <= 0 || h <= 0)
                    {
                        w = 1584;
                        h = 2245;
                    }

                    var pageControl = new PdfPageControl
                    {
                        PageIndex = i,
                        Width = w,
                        Height = h
                    };
                    // Runtime page controls are created in code, so expose a
                    // stable non-visible UI Automation id for desktop tests
                    // and assistive tooling to distinguish page bounds.
                    AutomationProperties.SetAutomationId(pageControl, $"PdfPageControl.{i}");
                    pageControl.SetHostActive(_isHostActive);
                    pageControl.SetDocumentInputEnabled(_isHostActive && !_documentInteractionBlocked && !_resourcesReleased);

                    pageControl.TextOverlayPointerPressed += PageControl_TextOverlayPointerPressed;
                    pageControl.BackgroundPointerPressed += PageControl_BackgroundPointerPressed;
                    // handledEventsToo: PdfScrollViewer_PreviewMouseDown marks the
                    // Select-tool delegate path as handled at an ancestor level,
                    // which would stop a plain += tunnel handler from firing.
                    pageControl.AddHandler(
                        UIElement.PreviewMouseDownEvent,
                        new MouseButtonEventHandler(PageControl_PreviewMouseDown),
                        handledEventsToo: true);
                    pageControl.PdfTextSelectionPointerPressed += PageControl_PdfTextSelectionPointerPressed;
                    pageControl.PdfTextSelectionPointerMoved += PageControl_PdfTextSelectionPointerMoved;
                    pageControl.PdfTextSelectionPointerReleased += PageControl_PdfTextSelectionPointerReleased;
                    pageControl.InkMutated += PageControl_InkMutated;
                    pageControl.QuietStrokeMutation += PageControl_QuietStrokeMutation;
                    pageControl.StrokeCollectedUndoable += PageControl_StrokeCollectedUndoable;
                    pageControl.StrokesErased += PageControl_StrokesErased;
                    pageControl.StrokeRecognized += PageControl_StrokeRecognized;
                    pageControl.ImagesChanged += PageControl_ImagesChanged;
                    pageControl.AreaHighlightCreated += PageControl_AreaHighlightCreated;
                    pageControl.StickyNoteActivated += PageControl_StickyNoteActivated;
                    pageControl.StickyNoteMoved += PageControl_StickyNoteMoved;
                    pageControl.StickyNoteDeleteRequested += PageControl_StickyNoteDeleteRequested;
                    pageControl.StickyNoteContextMenuCreated += PageControl_StickyNoteContextMenuCreated;
                    pageControl.HiddenInkCreated += PageControl_HiddenInkCreated;
                    pageControl.HiddenInkRemoved += PageControl_HiddenInkRemoved;
                    pageControl.HiddenInksRemoved += PageControl_HiddenInksRemoved;
                    pageControl.ModeChanged += PageControl_ModeChanged;
                    pageControl.SelectionChanged += PageControl_SelectionChanged;
                    pageControl.SelectionMoveCompleted += PageControl_SelectionMoveCompleted;
                    pageControl.SelectionResizeCompleted += PageControl_SelectionResizeCompleted;

                    // Task 22: ruler snap provider. The page queries the
                    // active ruler edge at stroke-collection time; the
                    // viewport→page translation happens per query via
                    // TranslatePoint, so scrolling/zooming (or moving the
                    // ruler) never serves a stale segment. Null while the
                    // ruler is hidden. The edge lives in the overlay, never
                    // in the document — no dirty flag, no undo, no save.
                    pageControl.GetRulerGeometryInPageCoords = () =>
                    {
                        var geometry = GetRulerGeometryEndpoints();
                        if (geometry == null) return null;
                        return (
                            TranslatePoint(geometry.Value.TopA, pageControl),
                            TranslatePoint(geometry.Value.TopB, pageControl),
                            TranslatePoint(geometry.Value.BottomA, pageControl),
                            TranslatePoint(geometry.Value.BottomB, pageControl));
                    };

                    if (_penService != null)
                        pageControl.SetPenService(_penService);

                    _pageControls.Add(pageControl);
                    _pageTopOffsets.Add(currentTop);
                    _pageHeights.Add(h);
                    currentTop += h + PageSpacing;

                    if (i > 0)
                        PagesContainer.Children.Add(CreatePageInsertGap(i));

                    PagesContainer.Children.Add(CreatePageHost(pageControl));
                }

                if (pageCount > 0)
                    PagesContainer.Children.Add(CreatePageInsertGap(pageCount));

                ApplyToolToAllPages();
                RefreshPageDeleteButtons();

                CancelSmoothScroll();
                PdfScrollViewer.ScrollToVerticalOffset(0);
                PdfScrollViewer.ScrollToHorizontalOffset(0);
                SyncSmoothScrollState();

                var visiblePages = GetVisiblePageControls();
                foreach (var page in visiblePages)
                {
                    token.ThrowIfCancellationRequested();
                    await RenderPageInitialAsync(page, token);
                    if (!ValidateDocumentOperationLease(loadLease))
                        return;
                }
                TrimPageBitmapWorkingSet(visiblePages);

                if (!string.IsNullOrEmpty(_currentPdfPath))
                {
                    await LoadAnnotationsFromPdfServiceAsync(loadLease);
                    if (!ValidateDocumentOperationLease(loadLease))
                        return;
                }

                if (!ValidateDocumentOperationLease(loadLease))
                    return;
                UpdatePageNumberIndicator();
                SyncSelectableViewerFromCustomView();
                await RefreshDocumentSidebarAsync(token, sessionId, filePath, loadLease);
                if (!ValidateDocumentOperationLease(loadLease))
                    return;

                if (_promptSaveAsAfterLoad && !_hasPromptedForSaveAs)
                {
                    var refreshedLoadLease = await PromptSaveAsForDraftAsync(loadLease);
                    if (refreshedLoadLease != null)
                    {
                        loadLease.Dispose();
                        loadLease = refreshedLoadLease;
                    }
                    if (!ValidateDocumentOperationLease(loadLease))
                        return;
                }

                _completedLoadSessionId = sessionId;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                var errorMsg = LocalizationService.Format("Editor.LoadPdfFailed", ex.Message);
                if (ex.InnerException != null)
                    errorMsg += $"\n\n{LocalizationService.Format("Editor.ErrorDetails", ex.InnerException.Message)}";

                if (ValidateDocumentOperationLease(loadLease))
                {
                    var mw = GetMainWindow();
                    if (mw != null)
                        await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), errorMsg);
                    else
                        MessageBox.Show(errorMsg, LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                loadLease.Dispose();
                if (sessionId == _loadSessionId)
                    HideLoadingOverlay();
            }
        }

        /// <summary>
        /// Reloads as part of a live structural/undo transaction. The reload
        /// necessarily rotates the document session, so the returned lease is
        /// the only lease allowed to publish the transaction's post-reload
        /// focus/bookmark/undo state. A competing load leaves no replacement
        /// lease and the caller must silently abort.
        /// </summary>
        private async Task<DocumentOperationLease> ReloadDocumentForOperationAsync(
            string filePath,
            DocumentOperationLease operationLease)
        {
            if (!ValidateDocumentOperationLease(operationLease))
                return null;

            int previousSessionId = _loadSessionId;
            try
            {
                await LoadPdf(filePath);
            }
            finally
            {
                // LoadPdf begins the replacement session and cancels this
                // pre-reload lease. It must not remain the owner of an
                // operation while callers publish only the fresh lease below.
                operationLease.Dispose();
            }

            int expectedSessionId = previousSessionId + 1;
            if (_completedLoadSessionId != expectedSessionId ||
                _loadSessionId != expectedSessionId ||
                !string.Equals(
                    DocumentOperationSession.NormalizePath(filePath),
                    DocumentOperationSession.NormalizePath(_currentPdfPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var refreshedLease = CaptureDocumentOperationLease(
                expectedSessionId,
                filePath,
                _pdfService);
            return ValidateDocumentOperationLease(refreshedLease)
                ? refreshedLease
                : null;
        }

        private bool IsSidebarLoadCurrent(int sessionId, string filePath)
        {
            if (sessionId != _loadSessionId)
                return false;
            if (!string.Equals(filePath, _currentPdfPath, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                        DocumentOperationSession.NormalizePath(filePath),
                        DocumentOperationSession.NormalizePath(_currentPdfPath),
                        StringComparison.OrdinalIgnoreCase))
                return false;
            return _sidebarLoadSessionGate.IsCurrent(sessionId, filePath);
        }

        private async Task RefreshDocumentSidebarAsync(
            CancellationToken cancellationToken,
            int sessionId,
            string filePath,
            DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(
                sessionId,
                filePath,
                cancellationToken: cancellationToken);

            try
            {
                if (ThumbnailListBox == null || _pdfService == null ||
                    !IsSidebarLoadCurrent(sessionId, filePath) ||
                    !ValidateDocumentOperationLease(operationLease))
                    return;

                _thumbnailLoadCts?.Cancel();
                _thumbnailLoadCts?.Dispose();
                _thumbnailLoadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _thumbnailPagesLoading.Clear();
                _thumbnailPageLoadSessions.Clear();
                ClearThumbnailCache();
                _isRefreshingThumbnails = true;
                _sidebarPageItems.Clear();
                for (int pageIndex = 0; pageIndex < _pageControls.Count; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    _sidebarPageItems.Add(new SidebarPageItem(
                        pageIndex,
                        LocalizationService.Format("Editor.PageNumber", pageIndex + 1)));
                }

                UpdateThumbnailSelection();
                if (PagesEmptyState != null)
                    PagesEmptyState.Visibility = _pageControls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (!IsSidebarLoadCurrent(sessionId, filePath) ||
                    !ValidateDocumentOperationLease(operationLease))
                    return;

                RefreshBookmarks(sessionId, filePath, operationLease);
                await RefreshOutlineCoreAsync(cancellationToken, sessionId, filePath, operationLease);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ValidateDocumentOperationLease(operationLease) &&
                    IsSidebarLoadCurrent(sessionId, filePath))
                    _isRefreshingThumbnails = false;
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private async void ThumbnailListBoxItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListBoxItem item || item.DataContext is not SidebarPageItem model ||
                model.Thumbnail != null || !_isHostActive || _resourcesReleased)
                return;

            int sessionId = _loadSessionId;
            string filePath = _currentPdfPath;
            if (_thumbnailPageLoadSessions.TryGetValue(model.PageIndex, out int loadingSessionId) &&
                loadingSessionId == sessionId)
                return;
            var externalToken = _thumbnailLoadCts?.Token ?? CancellationToken.None;
            int thumbnailRevision = _thumbnailRevisionGate.CaptureRevision(model.PageIndex);
            using var operationLease = CaptureDocumentOperationLease(
                sessionId,
                filePath,
                model,
                externalToken);
            if (!ValidateDocumentOperationLease(operationLease, model))
                return;

            if (TryGetCachedThumbnail(model.PageIndex, out var cached))
            {
                model.Thumbnail = cached;
                return;
            }

            _thumbnailPagesLoading.Add(model.PageIndex);
            _thumbnailPageLoadSessions[model.PageIndex] = sessionId;
            try
            {
                var token = operationLease.Token;
                var bitmap = await _pdfService.RenderPageBitmapSourceAsync(model.PageIndex, 0.22, token);
                token.ThrowIfCancellationRequested();
                bool liveModel = ReferenceEquals(item.DataContext, model) && _sidebarPageItems.Contains(model);
                if (!ValidateDocumentOperationLease(operationLease, model) || !_isHostActive || _resourcesReleased || !liveModel
                    || !_thumbnailRevisionGate.IsCurrent(model.PageIndex, sessionId, thumbnailRevision))
                    return;

                var pageControl = GetThumbnailPageControl(model.PageIndex);
                bitmap = ThumbnailCompositor.Composite(
                    bitmap,
                    pageControl?.GetStrokeData(),
                    pageControl?.Width ?? 0,
                    pageControl?.Height ?? 0);
                if (!ValidateDocumentOperationLease(operationLease, model) || !_isHostActive || _resourcesReleased
                    || !ReferenceEquals(item.DataContext, model) || !_sidebarPageItems.Contains(model)
                    || !_thumbnailRevisionGate.IsCurrent(model.PageIndex, sessionId, thumbnailRevision))
                    return;

                CacheThumbnail(model.PageIndex, bitmap);
                if (ValidateDocumentOperationLease(operationLease, model) &&
                    ReferenceEquals(item.DataContext, model) && _sidebarPageItems.Contains(model)
                    && _thumbnailRevisionGate.IsCurrent(model.PageIndex, sessionId, thumbnailRevision))
                    model.Thumbnail = bitmap;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // A recycled sidebar item can outlive the document that
                // started its render. Stale failures are intentionally silent:
                // never report an old PDF error against the replacement model.
                if (ValidateDocumentOperationLease(operationLease, model) &&
                    ReferenceEquals(item.DataContext, model) && _sidebarPageItems.Contains(model))
                    System.Diagnostics.Debug.WriteLine($"[Thumbnail] Failed to render page {model.PageIndex}: {ex}");
            }
            finally
            {
                bool shouldReloadAfterMutation =
                    !_thumbnailRevisionGate.IsCurrent(model.PageIndex, sessionId, thumbnailRevision);
                if (_thumbnailPageLoadSessions.TryGetValue(model.PageIndex, out int ownedSessionId) &&
                    ownedSessionId == sessionId)
                {
                    _thumbnailPageLoadSessions.Remove(model.PageIndex);
                    _thumbnailPagesLoading.Remove(model.PageIndex);
                }

                if (shouldReloadAfterMutation && _isHostActive && !_resourcesReleased
                    && IsSidebarLoadCurrent(sessionId, filePath)
                    && item.IsLoaded && ReferenceEquals(item.DataContext, model)
                    && _sidebarPageItems.Contains(model))
                {
                    ThumbnailListBoxItem_Loaded(
                        item,
                        new RoutedEventArgs(FrameworkElement.LoadedEvent, item));
                }
            }
        }

        private void ThumbnailListBoxItem_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListBoxItem item || item.DataContext is not SidebarPageItem model)
                return;

            if (!_thumbnailCache.ContainsKey(model.PageIndex))
                model.Thumbnail = null;
        }

        private void SidebarListBoxItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListBoxItem item)
                return;

            item.DataContextChanged -= SidebarListBoxItem_DataContextChanged;
            item.DataContextChanged += SidebarListBoxItem_DataContextChanged;
            ClearSidebarListBoxItemContextMenu(item);
            KeyboardNavigation.SetIsTabStop(item, true);
            item.MinHeight = Math.Max(32, item.MinHeight);
            if (item.DataContext is SidebarPageItem page)
            {
                string label = page.PageLabel;
                item.Tag = page.PageIndex;
                SetSidebarItemMetadata(item, $"Editor.Sidebar.Page.{page.PageIndex + 1}", label,
                    LocalizationService.Format("Editor.PageNumber", page.PageIndex + 1));
                ThumbnailListBoxItem_Loaded(item, e);
            }
            else if (item.DataContext is SidebarBookmarkItem bookmark)
            {
                item.Tag = bookmark.PageIndex;
                SetSidebarItemMetadata(item, $"Editor.Sidebar.Bookmark.{bookmark.PageIndex + 1}",
                    bookmark.Label, LocalizationService.Format("Editor.PageNumber", bookmark.PageIndex + 1));
            }
        }

        private void SidebarListBoxItem_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListBoxItem item)
                return;

            item.DataContextChanged -= SidebarListBoxItem_DataContextChanged;
            ClearSidebarListBoxItemContextMenu(item);
        }

        private void SidebarListBoxItem_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ListBoxItem item)
                ClearSidebarListBoxItemContextMenu(item);
        }

        private void ClearSidebarListBoxItemContextMenu(ListBoxItem item)
        {
            if (item?.ContextMenu == null)
                return;

            var menu = item.ContextMenu;
            menu.IsOpen = false;
            PopupZOrderHelper.UnfixContextMenuTopmost(menu);
            foreach (var menuItem in menu.Items.OfType<MenuItem>().ToList())
            {
                menuItem.Click -= ThumbnailContextMenu_InsertPage_Click;
                menuItem.Click -= ThumbnailContextMenu_DuplicatePage_Click;
                menuItem.Click -= ThumbnailContextMenu_DeletePage_Click;
                menuItem.Click -= BookmarkContextMenu_Remove_Click;
            }
            menu.Items.Clear();
            _sidebarContextMenuBindings.Remove(menu);
            menu.Tag = null;
            menu.PlacementTarget = null;
            item.ContextMenu = null;
        }

        private static void SetSidebarItemMetadata(ListBoxItem item, string automationId, string name, string help)
        {
            AutomationProperties.SetAutomationId(item, automationId);
            AutomationProperties.SetName(item, name ?? string.Empty);
            AutomationProperties.SetHelpText(item, help ?? name ?? string.Empty);
            ToolTipService.SetToolTip(item, name ?? string.Empty);
        }

        private static bool HasSidebarSelectionItemPattern(AutomationPeer peer)
        {
            return peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider;
        }

        private bool TryGetCachedThumbnail(int pageIndex, out BitmapSource bitmap)
        {
            if (!_thumbnailCache.TryGetValue(pageIndex, out bitmap))
                return false;

            _thumbnailCacheLru.Remove(pageIndex);
            _thumbnailCacheLru.AddLast(pageIndex);
            return true;
        }

        private void CacheThumbnail(int pageIndex, BitmapSource bitmap)
        {
            if (bitmap == null)
                return;

            _thumbnailCache[pageIndex] = bitmap;
            _thumbnailCacheLru.Remove(pageIndex);
            _thumbnailCacheLru.AddLast(pageIndex);

            while (_thumbnailCacheLru.Count > ThumbnailCacheCapacity)
            {
                int evictedPage = _thumbnailCacheLru.First.Value;
                _thumbnailCacheLru.RemoveFirst();
                if (!_thumbnailCache.Remove(evictedPage, out var evictedBitmap))
                    continue;

                var evictedModel = _sidebarPageItems.FirstOrDefault(page => page.PageIndex == evictedPage);
                if (evictedModel != null && ReferenceEquals(evictedModel.Thumbnail, evictedBitmap))
                    evictedModel.Thumbnail = null;
            }
        }

        private PdfPageControl GetThumbnailPageControl(int pageIndex)
        {
            return _pageControls.FirstOrDefault(page => page != null && page.PageIndex == pageIndex);
        }

        private void InvalidateThumbnailForPage(int pageIndex)
        {
            if (pageIndex < 0)
                return;

            _thumbnailRevisionGate.InvalidatePage(pageIndex);
            if (_thumbnailCache.Remove(pageIndex, out var oldBitmap))
                _thumbnailCacheLru.Remove(pageIndex);

            var model = _sidebarPageItems.FirstOrDefault(page => page.PageIndex == pageIndex);
            if (model != null && (oldBitmap == null || ReferenceEquals(model.Thumbnail, oldBitmap)))
                model.Thumbnail = null;

            // Leave an in-flight marker in place. Its finally block observes
            // the revision mismatch, removes only its own session marker, and
            // starts one fresh render for the realized row. Clearing the
            // marker here would let the old callback remove a newer marker.
            if (_thumbnailPageLoadSessions.ContainsKey(pageIndex)
                || ThumbnailListBox == null || !_isHostActive || _resourcesReleased
                || model == null)
                return;

            int itemIndex = _sidebarPageItems.IndexOf(model);
            if (itemIndex < 0
                || ThumbnailListBox.ItemContainerGenerator.ContainerFromIndex(itemIndex) is not ListBoxItem item
                || !item.IsLoaded
                || !ReferenceEquals(item.DataContext, model))
                return;

            ThumbnailListBoxItem_Loaded(
                item,
                new RoutedEventArgs(FrameworkElement.LoadedEvent, item));
        }

        private void ClearThumbnailCache()
        {
            _thumbnailCache.Clear();
            _thumbnailCacheLru.Clear();
            foreach (var model in _sidebarPageItems)
                model.Thumbnail = null;
        }

        private void LoadVisibleThumbnails()
        {
            if (ThumbnailListBox == null)
                return;

            for (int index = 0; index < ThumbnailListBox.Items.Count; index++)
            {
                if (ThumbnailListBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item && item.IsLoaded)
                    ThumbnailListBoxItem_Loaded(item, new RoutedEventArgs(FrameworkElement.LoadedEvent, item));
            }
        }

        private ContextMenu BuildThumbnailContextMenu(SidebarPageItem model)
        {
            var binding = new ContextMenuOperationBinding(model, _loadSessionId, _currentPdfPath);
            var menu = new ContextMenu
            {
                // Keep Tag compatible with existing callers/UIA probes while
                // the weak table retains the full session/path lease binding.
                Tag = model
            };
            _sidebarContextMenuBindings.Add(menu, binding);
            menu.SetResourceReference(Control.BackgroundProperty, "ThemeSurfaceBrush");
            menu.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush");
            menu.SetResourceReference(UIElement.OpacityProperty, "ThemeSurfaceOpacity");
            var insert = new MenuItem { Header = LocalizationService.Get("Editor.InsertBlankPageBefore") };
            insert.CommandParameter = model;
            insert.Click += ThumbnailContextMenu_InsertPage_Click;
            var duplicate = new MenuItem { Header = LocalizationService.Get("Editor.DuplicatePage") };
            duplicate.CommandParameter = model;
            duplicate.Click += ThumbnailContextMenu_DuplicatePage_Click;
            var delete = new MenuItem { Header = LocalizationService.Get("Editor.DeletePage") };
            delete.CommandParameter = model;
            delete.Click += ThumbnailContextMenu_DeletePage_Click;
            insert.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush");
            duplicate.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush");
            delete.SetResourceReference(Control.ForegroundProperty, "ThemeDangerBrush");
            menu.Items.Add(insert);
            menu.Items.Add(duplicate);
            menu.Items.Add(new Separator());
            menu.Items.Add(delete);
            _transientUiRegistry.Register(menu);
            PopupZOrderHelper.FixContextMenuTopmost(menu);
            return menu;
        }

        private async void ThumbnailContextMenu_InsertPage_Click(object sender, RoutedEventArgs e)
        {
            if (TryCaptureSidebarContextMenuModel<SidebarPageItem>(sender, out var model, out var operationLease))
            {
                using (operationLease)
                await InsertPageAtAsync(model.PageIndex, operationLease);
            }
        }

        private async void ThumbnailContextMenu_DuplicatePage_Click(object sender, RoutedEventArgs e)
        {
            if (TryCaptureSidebarContextMenuModel<SidebarPageItem>(sender, out var model, out var operationLease))
            {
                using (operationLease)
                await DuplicatePageAtAsync(model.PageIndex, operationLease);
            }
        }

        private async void ThumbnailContextMenu_DeletePage_Click(object sender, RoutedEventArgs e)
        {
            if (TryCaptureSidebarContextMenuModel<SidebarPageItem>(sender, out var model, out var operationLease))
            {
                using (operationLease)
                await DeletePageAtAsync(model.PageIndex, operationLease);
            }
        }

        private bool TryCaptureSidebarContextMenuModel<T>(
            object source,
            out T model,
            out DocumentOperationLease operationLease)
            where T : class
        {
            model = null;
            operationLease = null;
            if (!_isHostActive || _resourcesReleased || _documentInteractionBlocked)
                return false;
            var menuItem = source as MenuItem;
            var menu = FindAncestor<ContextMenu>(source as DependencyObject) ?? menuItem?.Parent as ContextMenu;
            if (menu == null || !_sidebarContextMenuBindings.TryGetValue(menu, out var binding) ||
                binding.Model is not T boundModel ||
                menu.PlacementTarget is not ListBoxItem item ||
                !ReferenceEquals(item.DataContext, boundModel))
                return false;

            if (boundModel is SidebarPageItem page && !_sidebarPageItems.Contains(page))
                return false;
            if (boundModel is SidebarBookmarkItem bookmark && !_sidebarBookmarkItems.Contains(bookmark))
                return false;

            operationLease = CaptureDocumentOperationLease(
                binding.SessionId,
                binding.FilePath,
                _pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
            {
                operationLease.Dispose();
                operationLease = null;
                return false;
            }

            model = boundModel;
            return true;
        }

        // Compatibility shim for existing STA/UIA probes that resolve the
        // current model without needing to own an async lease. Production
        // async handlers use TryCaptureSidebarContextMenuModel above.
        private bool TryGetCurrentSidebarContextMenuModel<T>(
            object source,
            out T model)
            where T : class
        {
            model = null;
            if (!_isHostActive || _resourcesReleased || _documentInteractionBlocked)
                return false;
            var menuItem = source as MenuItem;
            var menu = FindAncestor<ContextMenu>(source as DependencyObject) ?? menuItem?.Parent as ContextMenu;
            if (menu == null || !_sidebarContextMenuBindings.TryGetValue(menu, out var binding) ||
                binding.Model is not T boundModel ||
                menu.PlacementTarget is not ListBoxItem item ||
                !ReferenceEquals(item.DataContext, boundModel))
                return false;

            model = boundModel;
            return true;
        }

        private static void RefreshThumbnailContextMenu(ContextMenu menu)
        {
            if (menu == null)
                return;

            var menuItems = menu.Items.OfType<MenuItem>().ToList();
            if (menuItems.Count > 0)
                menuItems[0].Header = LocalizationService.Get("Editor.InsertBlankPageBefore");
            if (menuItems.Count > 1)
                menuItems[1].Header = LocalizationService.Get("Editor.DuplicatePage");
            if (menuItems.Count > 2)
                menuItems[2].Header = LocalizationService.Get("Editor.DeletePage");
        }

        private void ThumbnailListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingThumbnails || _isSynchronizingThumbnailSelection ||
                ThumbnailListBox.SelectedItem is not SidebarPageItem item)
                return;
            JumpToPage(item.PageIndex);
        }

        private void ThumbnailListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not ListBoxItem item ||
                item.DataContext is not SidebarPageItem page)
            {
                e.Handled = true;
                return;
            }

            ClearSidebarListBoxItemContextMenu(item);
            item.ContextMenu = BuildThumbnailContextMenu(page);
            item.ContextMenu.PlacementTarget = item;
        }

        private void ThumbnailListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item &&
                item.DataContext is SidebarPageItem page &&
                _sidebarPageItems.Contains(page) &&
                !string.IsNullOrWhiteSpace(_currentPdfPath))
            {
                _thumbnailDragStartPoint = e.GetPosition(ThumbnailListBox);
                _thumbnailDragPayload = new ThumbnailDragPayload(
                    page.PageIndex,
                    _loadSessionId,
                    DocumentOperationSession.NormalizePath(_currentPdfPath),
                    page);
            }
            else
                ResetThumbnailDragState();
        }

        private void ThumbnailListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_thumbnailDragPayload == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(ThumbnailListBox);
            if (Math.Abs(current.X - _thumbnailDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _thumbnailDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var payload = _thumbnailDragPayload;
            try
            {
                DragDrop.DoDragDrop(
                    ThumbnailListBox,
                    new DataObject(typeof(ThumbnailDragPayload), payload),
                    DragDropEffects.Move);
            }
            finally
            {
                ResetThumbnailDragState();
                ClearThumbnailDropIndicator();
            }
        }

        private bool IsCurrentThumbnailDragPayload(ThumbnailDragPayload payload)
        {
            return payload != null && payload.Source != null &&
                _isHostActive && !_resourcesReleased && !_documentInteractionBlocked &&
                payload.SessionId == _loadSessionId &&
                string.Equals(
                    DocumentOperationSession.NormalizePath(payload.FilePath),
                    DocumentOperationSession.NormalizePath(_currentPdfPath),
                    StringComparison.OrdinalIgnoreCase) &&
                payload.SourceIndex >= 0 &&
                payload.SourceIndex < _sidebarPageItems.Count &&
                ReferenceEquals(_sidebarPageItems[payload.SourceIndex], payload.Source);
        }

        private bool TryGetThumbnailDragPayload(
            IDataObject data,
            out ThumbnailDragPayload payload)
        {
            payload = null;
            if (data == null || !data.GetDataPresent(typeof(ThumbnailDragPayload)))
                return false;

            payload = data.GetData(typeof(ThumbnailDragPayload)) as ThumbnailDragPayload;
            return IsCurrentThumbnailDragPayload(payload);
        }

        private bool TryResolveThumbnailDropSlot(
            DragEventArgs e,
            int pageCount,
            out int slot,
            out double indicatorTop)
        {
            slot = -1;
            indicatorTop = 0;
            if (ThumbnailListBox == null || pageCount <= 0)
                return false;

            var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (targetItem?.DataContext is SidebarPageItem target &&
                _sidebarPageItems.Contains(target))
            {
                Point pointer = e.GetPosition(ThumbnailListBox);
                Point origin = targetItem.TranslatePoint(new Point(0, 0), ThumbnailListBox);
                double height = targetItem.ActualHeight > 0
                    ? targetItem.ActualHeight
                    : Math.Max(1, targetItem.DesiredSize.Height);
                bool before = pointer.Y < origin.Y + (height / 2.0);
                slot = before ? target.PageIndex : target.PageIndex + 1;
                indicatorTop = before ? origin.Y : origin.Y + height;
            }
            else
            {
                slot = pageCount;
                var lastItem = Enumerable.Range(0, _sidebarPageItems.Count)
                    .Reverse()
                    .Select(index => ThumbnailListBox.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem)
                    .FirstOrDefault(item => item != null && item.IsLoaded);
                if (lastItem != null)
                {
                    Point origin = lastItem.TranslatePoint(new Point(0, 0), ThumbnailListBox);
                    double height = lastItem.ActualHeight > 0
                        ? lastItem.ActualHeight
                        : Math.Max(1, lastItem.DesiredSize.Height);
                    indicatorTop = origin.Y + height;
                }
                else
                    indicatorTop = Math.Max(0, ThumbnailListBox.ActualHeight);
            }

            slot = Math.Clamp(slot, 0, pageCount);
            indicatorTop = Math.Max(0, indicatorTop);
            return true;
        }

        private void ShowThumbnailDropIndicator(double top)
        {
            if (ThumbnailDropIndicator == null)
                return;

            ThumbnailDropIndicator.Margin = new Thickness(0, top, 0, 0);
            ThumbnailDropIndicator.Visibility = Visibility.Visible;
        }

        private void ClearThumbnailDropIndicator()
        {
            _thumbnailDropSlot = -1;
            if (ThumbnailDropIndicator == null)
                return;

            ThumbnailDropIndicator.Visibility = Visibility.Collapsed;
            ThumbnailDropIndicator.Margin = new Thickness(0);
        }

        private void ResetThumbnailDragState()
        {
            _thumbnailDragPayload = null;
        }

        private void ThumbnailListBox_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetThumbnailDragPayload(e.Data, out var payload) ||
                !TryResolveThumbnailDropSlot(e, _sidebarPageItems.Count, out int slot, out double top))
            {
                ClearThumbnailDropIndicator();
                e.Effects = DragDropEffects.None;
                return;
            }

            _thumbnailDropSlot = slot;
            ShowThumbnailDropIndicator(top);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void ThumbnailListBox_DragLeave(object sender, DragEventArgs e)
        {
            ClearThumbnailDropIndicator();
        }

        private void ThumbnailListBox_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            if (e.Action == DragAction.Cancel)
            {
                ClearThumbnailDropIndicator();
                ResetThumbnailDragState();
            }
        }

        private async void ThumbnailListBox_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            int indicatorSlot = _thumbnailDropSlot;
            ClearThumbnailDropIndicator();

            if (!TryGetThumbnailDragPayload(e.Data, out var payload))
                return;
            ResetThumbnailDragState();

            DocumentOperationLease currentLease = CaptureDocumentOperationLease(
                payload.SessionId,
                payload.FilePath,
                payload.Source);
            if (!ValidateDocumentOperationLease(currentLease, payload.Source))
            {
                currentLease.Dispose();
                return;
            }

            if (!TryBeginDocumentEdit(out var editLease))
            {
                currentLease.Dispose();
                return;
            }

            using (editLease)
            {
                try
                {
                    int pageCount = _sidebarPageItems.Count;
                    int slot = TryResolveThumbnailDropSlot(
                        e,
                        pageCount,
                        out int resolvedSlot,
                        out _)
                        ? resolvedSlot
                        : indicatorSlot;
                    if (pageCount <= 0 || payload.SourceIndex < 0 ||
                        payload.SourceIndex >= pageCount || slot < 0)
                        return;

                    int finalIndex = ThumbnailDropPlacement.ResolveFinalIndex(
                        payload.SourceIndex,
                        slot,
                        pageCount);
                    if (finalIndex < 0 || finalIndex == payload.SourceIndex)
                        return;

                    string filePath = _currentPdfPath;
                    if (!ValidateDocumentOperationLease(currentLease, payload.Source))
                        return;
                    if (_documentSaveCoordinator.IsDirty &&
                        (!await AutoSaveAsync(currentLease) ||
                         !ValidateDocumentOperationLease(currentLease, payload.Source)))
                        return;

                    byte[] before = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease, payload.Source))
                        return;
                    int focusBefore = GetCurrentPageIndex();
                    var beforeBookmarks = PageBookmarkService.Load(filePath).ToList();
                    await _pdfService.ReorderPagesAsync(filePath, payload.SourceIndex, finalIndex);
                    if (!ValidateDocumentOperationLease(currentLease, payload.Source))
                        return;
                    byte[] after = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease, payload.Source))
                        return;

                    currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                    if (currentLease == null)
                        return;
                    int focused = Math.Max(0, Math.Min(finalIndex, _pageControls.Count - 1));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    JumpToPage(focused);
                    var afterBookmarks = PageBookmarkService.RemapForMove(
                        beforeBookmarks,
                        payload.SourceIndex,
                        finalIndex).ToList();
                    PageBookmarkService.Replace(filePath, afterBookmarks);
                    RefreshBookmarks(_loadSessionId, filePath, currentLease);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    PushUndoAction(new DocumentSnapshotAction(
                        this,
                        before,
                        after,
                        focusBefore,
                        focused,
                        beforeBookmarks,
                        afterBookmarks));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (ValidateDocumentOperationLease(currentLease))
                        GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.PageReorderFailed", ex.Message), "\uE783", 3500);
                }
                finally
                {
                    currentLease?.Dispose();
                    ClearThumbnailDropIndicator();
                }
            }
        }

        private async Task DuplicatePageAtAsync(
            int pageIndex,
            DocumentOperationLease operationLease = null)
        {
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using (editLease)
            {
                if (string.IsNullOrWhiteSpace(_currentPdfPath))
                {
                    if (operationLease == null)
                        GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                    return;
                }

                string filePath = _currentPdfPath;
                DocumentOperationLease currentLease = operationLease ?? CaptureDocumentOperationLease(_pdfService);
                try
                {
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    if (_documentSaveCoordinator.IsDirty &&
                        (!await AutoSaveAsync(currentLease) || !ValidateDocumentOperationLease(currentLease)))
                        return;

                    byte[] before = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    int focusBefore = GetCurrentPageIndex();
                    var beforeBookmarks = PageBookmarkService.Load(filePath).ToList();
                    await _pdfService.DuplicatePageAsync(filePath, pageIndex);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    byte[] after = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;

                    currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                    if (currentLease == null)
                        return;
                    int focused = Math.Max(0, Math.Min(pageIndex + 1, _pageControls.Count - 1));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    JumpToPage(focused);
                    var afterBookmarks = PageBookmarkService.ApplyPageInsert(filePath, pageIndex + 1).ToList();
                    RefreshBookmarks(_loadSessionId, filePath, currentLease);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    PushUndoAction(new DocumentSnapshotAction(this, before, after, focusBefore, focused, beforeBookmarks, afterBookmarks));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (ValidateDocumentOperationLease(currentLease))
                        GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.PageDuplicateFailed", ex.Message), "\uE783", 3500);
                }
                finally
                {
                    currentLease?.Dispose();
                }
            }
        }

        private void UpdateThumbnailSelection(bool forceCenter = false)
        {
            if (ThumbnailListBox == null || ThumbnailListBox.Items.Count == 0)
                return;
            int current = GetCurrentPageIndex();
            if (current >= 0 && current < ThumbnailListBox.Items.Count)
            {
                bool indexChanged = ThumbnailListBox.SelectedIndex != current;
                _isSynchronizingThumbnailSelection = true;
                try
                {
                    ThumbnailListBox.SelectedIndex = current;
                }
                finally
                {
                    _isSynchronizingThumbnailSelection = false;
                }

                if (indexChanged || forceCenter)
                {
                    ScrollThumbnailItemToCenter(current);
                }
            }
        }

        private void ScrollThumbnailItemToCenter(int index)
        {
            if (ThumbnailListBox == null || index < 0 || index >= ThumbnailListBox.Items.Count)
                return;

            if (_sidebarTab != SidebarTab.Pages || _sidebarCollapsed)
                return;

            var itemData = ThumbnailListBox.Items[index];
            ThumbnailListBox.ScrollIntoView(itemData);

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (ThumbnailListBox == null || index < 0 || index >= ThumbnailListBox.Items.Count)
                    return;

                var scrollViewer = FindVisualChildren<ScrollViewer>(ThumbnailListBox).FirstOrDefault();
                if (scrollViewer == null)
                    return;

                if (ThumbnailListBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container && container.IsLoaded)
                {
                    try
                    {
                        var transform = container.TransformToAncestor(scrollViewer);
                        var pos = transform.Transform(new Point(0, 0));
                        double containerCenter = pos.Y + container.ActualHeight / 2.0;
                        double viewportCenter = scrollViewer.ViewportHeight / 2.0;
                        double delta = containerCenter - viewportCenter;
                        if (Math.Abs(delta) > 2.0)
                        {
                            double targetOffset = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + delta));
                            scrollViewer.ScrollToVerticalOffset(targetOffset);
                        }
                    }
                    catch
                    {
                        // Ignore any visual tree detachment during layout transitions
                    }
                }
                else if (scrollViewer.ScrollableHeight > 0 && ThumbnailListBox.Items.Count > 0)
                {
                    double avgItemHeight = scrollViewer.ExtentHeight / ThumbnailListBox.Items.Count;
                    double estimatedTarget = (index + 0.5) * avgItemHeight - (scrollViewer.ViewportHeight / 2.0);
                    scrollViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, estimatedTarget)));
                }
            }));
        }

        private void RefreshBookmarks()
        {
            RefreshBookmarks(_loadSessionId, _currentPdfPath, null);
        }

        private void RefreshBookmarks(
            int sessionId,
            string filePath,
            DocumentOperationLease operationLease = null)
        {
            if (BookmarksListBox == null || !IsSidebarLoadCurrent(sessionId, filePath) ||
                (operationLease != null && !ValidateDocumentOperationLease(operationLease)))
                return;

            _sidebarBookmarkItems.Clear();
            foreach (var bookmark in PageBookmarkService.Load(filePath ?? string.Empty))
            {
                _sidebarBookmarkItems.Add(new SidebarBookmarkItem(
                    bookmark.PageIndex,
                    PageBookmarkService.GetDisplayLabel(bookmark)));
            }
            if (BookmarksEmptyState != null)
                BookmarksEmptyState.Visibility = _sidebarBookmarkItems.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
            UpdateBookmarkButton();
        }

        private void BookmarksListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not ListBoxItem item ||
                item.DataContext is not SidebarBookmarkItem bookmark)
            {
                e.Handled = true;
                return;
            }

            ClearSidebarListBoxItemContextMenu(item);
            item.ContextMenu = BuildBookmarkContextMenu(bookmark);
            item.ContextMenu.PlacementTarget = item;
        }

        private ContextMenu BuildBookmarkContextMenu(SidebarBookmarkItem model)
        {
            var binding = new ContextMenuOperationBinding(model, _loadSessionId, _currentPdfPath);
            var menu = new ContextMenu
            {
                Tag = model
            };
            _sidebarContextMenuBindings.Add(menu, binding);
            var removeItem = new MenuItem { Header = LocalizationService.Get("Editor.RemoveBookmark") };
            removeItem.CommandParameter = model;
            removeItem.Click += BookmarkContextMenu_Remove_Click;
            menu.Items.Add(removeItem);
            _transientUiRegistry.Register(menu);
            PopupZOrderHelper.FixContextMenuTopmost(menu);
            return menu;
        }

        private void BookmarkContextMenu_Remove_Click(object sender, RoutedEventArgs e)
        {
            if (!TryCaptureSidebarContextMenuModel<SidebarBookmarkItem>(sender, out var model, out var operationLease) ||
                string.IsNullOrWhiteSpace(_currentPdfPath))
                return;

            using (operationLease)
            {
                string filePath = _currentPdfPath;
                PageBookmarkService.Toggle(filePath, model.PageIndex);
                RefreshBookmarks(_loadSessionId, filePath, operationLease);
            }
        }

        private static void RefreshBookmarkContextMenu(ContextMenu menu)
        {
            if (menu?.Items.OfType<MenuItem>().FirstOrDefault() is MenuItem removeItem)
                removeItem.Header = LocalizationService.Get("Editor.RemoveBookmark");
        }

        private void UpdateBookmarkButton()
        {
            if (BookmarkToggleButton == null)
                return;
            bool bookmarked = PageBookmarkService.Load(_currentPdfPath).Any(bookmark => bookmark.PageIndex == GetCurrentPageIndex());
            BookmarkToggleButton.IsChecked = bookmarked;
            SetBookmarkButtonContent(bookmarked);
            ApplyStateAwareSidebarMetadata();
            SetAutomationId(BookmarkToggleButton, "Editor.Sidebar.BookmarkToggle");
        }

        private void SetBookmarkButtonContent(bool bookmarked)
        {
            var icon = new LucideIcon
            {
                Kind = "Bookmark",
                Width = 15,
                Height = 15,
                Fill = Brushes.Transparent,
                Stroke = SystemColors.HighlightBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetResourceReference(Shape.StrokeProperty, "ThemeAccentBrush");
            if (bookmarked)
                icon.SetResourceReference(Shape.FillProperty, "ThemeAccentBrush");
            var label = new TextBlock
            {
                Text = bookmarked
                    ? LocalizationService.Get("Editor.UnbookmarkCurrentPage")
                    : LocalizationService.Get("Editor.BookmarkCurrentPage"),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ThemeForegroundBrush");
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(label);
            BookmarkToggleButton.Content = content;
        }

        private static void SetAutomationId(DependencyObject control, string automationId)
        {
            if (control != null)
                AutomationProperties.SetAutomationId(control, automationId);
        }

        private void BookmarkToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath) || _pageControls.Count == 0)
                return;
            PageBookmarkService.Toggle(_currentPdfPath, GetCurrentPageIndex());
            RefreshBookmarks();
        }

        private void SidebarPagesButton_Click(object sender, RoutedEventArgs e)
        {
            SetSidebarTab(SidebarTab.Pages);
        }

        private void SidebarOutlineButton_Click(object sender, RoutedEventArgs e)
        {
            SetSidebarTab(SidebarTab.Outline);
        }

        private void SidebarBookmarksButton_Click(object sender, RoutedEventArgs e)
        {
            SetSidebarTab(SidebarTab.Bookmarks);
        }

        private void SetSidebarTab(SidebarTab tab)
        {
            _sidebarTab = tab;
            if (PagesSidebarContent == null || OutlineSidebarContent == null || BookmarksSidebarContent == null)
                return;

            PagesSidebarContent.Visibility = tab == SidebarTab.Pages && !_sidebarCollapsed
                ? Visibility.Visible : Visibility.Collapsed;
            OutlineSidebarContent.Visibility = tab == SidebarTab.Outline && !_sidebarCollapsed
                ? Visibility.Visible : Visibility.Collapsed;
            BookmarksSidebarContent.Visibility = tab == SidebarTab.Bookmarks && !_sidebarCollapsed
                ? Visibility.Visible : Visibility.Collapsed;

            ApplySidebarButtonState(SidebarPagesButton, tab == SidebarTab.Pages, LocalizationService.Get("Editor.PagesTab"));
            ApplySidebarButtonState(SidebarOutlineButton, tab == SidebarTab.Outline, LocalizationService.Get("Editor.OutlineTab"));
            ApplySidebarButtonState(SidebarBookmarksButton, tab == SidebarTab.Bookmarks, LocalizationService.Get("Editor.BookmarksTab"));

            if (tab == SidebarTab.Pages && !_sidebarCollapsed)
            {
                UpdateThumbnailSelection(forceCenter: true);
            }
        }

        private void ApplySidebarButtonState(Button button, bool selected, string label)
        {
            if (button == null)
                return;

            // Selection is a live style expression.  Clearing the local values
            // lets the Tag trigger re-resolve theme/high-contrast resources.
            button.ClearValue(Button.BackgroundProperty);
            button.ClearValue(Button.BorderBrushProperty);
            button.ClearValue(Button.BorderThicknessProperty);
            button.ClearValue(Button.FontWeightProperty);
            button.SetResourceReference(Button.ForegroundProperty, "ThemeForegroundBrush");
            button.Tag = selected ? "Selected" : null;
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetHelpText(button, label);
            AutomationProperties.SetItemStatus(button, selected ? LocalizationService.Get("Editor.SidebarSelected") : string.Empty);
            ToolTipService.SetToolTip(button, label);
        }

        private void SidebarCollapseButton_Click(object sender, RoutedEventArgs e)
        {
            SetSidebarCollapsed(!_sidebarCollapsed);
        }

        private void SetSidebarCollapsed(bool collapsed)
        {
            _sidebarCollapsed = collapsed;
            if (SidebarContentHost != null)
                SidebarContentHost.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (SidebarNavBar != null)
                SidebarNavBar.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (SidebarTitleLabel != null)
                SidebarTitleLabel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (SidebarPagesLabel != null)
                SidebarPagesLabel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (SidebarOutlineLabel != null)
                SidebarOutlineLabel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (SidebarBookmarksLabel != null)
                SidebarBookmarksLabel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (SidebarHeaderGrid != null)
                SidebarHeaderGrid.Margin = _sidebarCollapsed
                    ? new Thickness(3)
                    : new Thickness(8, 8, 8, 2);

            if (DocumentSidebar != null)
            {
                DocumentSidebar.Width = _sidebarCollapsed
                    ? SidebarCollapsedWidth
                    : SidebarExpandedWidth;
            }

            if (SidebarCollapseIcon != null)
            {
                SidebarCollapseIcon.Kind = _sidebarCollapsed ? "PanelLeftOpen" : "PanelLeftClose";
            }

            // ApplyStateAwareSidebarMetadata resolves Editor.SidebarExpand /
            // Editor.SidebarCollapse after every state transition.
            ApplyStateAwareSidebarMetadata();
            SetSidebarTab(_sidebarTab);
        }

        private void EditorPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ToolbarBorder != null)
                ToolbarBorder.MaxWidth = Math.Max(220, ActualWidth - 24);
            if (ToolbarItemsScrollViewer != null)
            {
                ToolbarItemsScrollViewer.MaxWidth = Math.Max(220, ActualWidth - 24);
                SetToolbarMetadata(ToolbarItemsScrollViewer, "Editor.ToolbarOverflow",
                    LocalizationService.Get("Editor.ToolbarScroll"));
            }
            AutoCollapseSidebarForNarrowLayout();
        }

        private void AutoCollapseSidebarForNarrowLayout()
        {
            if (ActualWidth > 0 && ActualWidth <= 375 && !_sidebarCollapsed)
                SetSidebarCollapsed(true);
        }

        private void BookmarksListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BookmarksListBox.SelectedItem is SidebarBookmarkItem item && _sidebarBookmarkItems.Contains(item))
                JumpToPage(item.PageIndex);
        }

        private void OutlineTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is SidebarOutlineItem item && _sidebarOutlineItems.Contains(item) && item.PageIndex >= 0)
                JumpToPage(item.PageIndex);
        }

        private void OutlineInvokeButton_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not SidebarOutlineItem model)
                return;

            string label = LocalizationService.Format("Editor.PageNumber", model.PageIndex + 1);
            SetToolbarMetadata(button, $"{model.AutomationId}.Invoke", label);
            button.MinWidth = Math.Max(32, button.MinWidth);
            button.MinHeight = Math.Max(32, button.MinHeight);
        }

        private void OutlineInvokeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is SidebarOutlineItem model &&
                _sidebarOutlineItems.Contains(model) && model.PageIndex >= 0)
                JumpToPage(model.PageIndex);
            e.Handled = true;
        }

        // Three-argument compatibility entry point retained for the existing
        // STA/sidebar probes; asynchronous production callers use the core
        // overload so they can transfer an already-captured document lease.
        private Task RefreshOutlineAsync(
            CancellationToken cancellationToken,
            int sessionId,
            string filePath)
        {
            return RefreshOutlineCoreAsync(cancellationToken, sessionId, filePath);
        }

        private async Task RefreshOutlineCoreAsync(
            CancellationToken cancellationToken,
            int sessionId,
            string filePath,
            DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(
                sessionId,
                filePath,
                cancellationToken: cancellationToken);

            if (OutlineTreeView == null || !IsSidebarLoadCurrent(sessionId, filePath) ||
                !ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }

            try
            {
                IReadOnlyList<PdfService.PdfOutlineEntry> outline;
                try
                {
                    // The completion source gives the async boundary an explicit
                    // continuation that can be rejected after a newer document
                    // session begins (including deterministic TCS tests).
                    var completion = new TaskCompletionSource<IReadOnlyList<PdfService.PdfOutlineEntry>>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        completion.SetResult(await _pdfService.GetOutlineAsync(cancellationToken));
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }

                    outline = await completion.Task;
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A late outline read must not report an old document
                    // failure after a replacement document has taken over.
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    System.Diagnostics.Debug.WriteLine($"[Outline] Failed to read outline: {ex}");
                    outline = Array.Empty<PdfService.PdfOutlineEntry>();
                }

                if (!IsSidebarLoadCurrent(sessionId, filePath) ||
                    !ValidateDocumentOperationLease(operationLease))
                    return;

                _sidebarOutlineItems.Clear();
                if (outline.Count == 0)
                {
                    for (int i = 0; i < _pageControls.Count; i++)
                    {
                        string label = LocalizationService.Format("Editor.PageNumber", i + 1);
                        _sidebarOutlineItems.Add(new SidebarOutlineItem(
                            i, label, $"Editor.Sidebar.Outline.Page.{i + 1}"));
                    }
                    if (OutlineEmptyState != null)
                        OutlineEmptyState.Visibility = _pageControls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    return;
                }

                for (int index = 0; index < outline.Count; index++)
                {
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    _sidebarOutlineItems.Add(BuildOutlineModel(outline[index], (index + 1).ToString()));
                }
                if (OutlineEmptyState != null)
                    OutlineEmptyState.Visibility = Visibility.Collapsed;
            }
            finally
            {
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private SidebarOutlineItem BuildOutlineModel(PdfService.PdfOutlineEntry entry, string automationPath)
        {
            string title = string.IsNullOrWhiteSpace(entry.Title)
                ? LocalizationService.Format("Editor.PageNumber", entry.PageIndex + 1)
                : entry.Title;
            var item = new SidebarOutlineItem(
                entry.PageIndex,
                title,
                $"Editor.Sidebar.Outline.{automationPath}");
            for (int index = 0; index < entry.Children.Count; index++)
                item.Children.Add(BuildOutlineModel(entry.Children[index], $"{automationPath}.{index + 1}"));
            return item;
        }

        private void OutlineTreeViewItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem item || item.DataContext is not SidebarOutlineItem model)
                return;

            item.Tag = model.PageIndex;
            item.MinHeight = Math.Max(32, item.MinHeight);
            KeyboardNavigation.SetIsTabStop(item, true);
            AutomationProperties.SetAutomationId(item, model.AutomationId);
            AutomationProperties.SetName(item, model.Title);
            AutomationProperties.SetHelpText(item,
                LocalizationService.Format("Editor.PageNumber", model.PageIndex + 1));
            ToolTipService.SetToolTip(item, model.Title);
            if (item is SidebarOutlineTreeViewItem outlineItem)
                outlineItem.InvokeAction = () =>
                {
                    if (_sidebarOutlineItems.Contains(model))
                        JumpToPage(model.PageIndex);
                };
        }

        private FrameworkElement CreatePageHost(PdfPageControl pageControl)
        {
            var host = new Grid
            {
                Width = pageControl.Width,
                Height = pageControl.Height,
                HorizontalAlignment = HorizontalAlignment.Center,
                ClipToBounds = false
            };

            host.Children.Add(pageControl);

            var deleteButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 14, 14, 0),
                MinHeight = 34,
                Padding = new Thickness(10, 6, 10, 6),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Hidden,
                ToolTip = LocalizationService.Get("Editor.DeletePageTooltip"),
                Template = CreatePageChromeButtonTemplate(
                    "ThemeControlHoverBrush", "ThemeControlPressedBrush")
            };
            deleteButton.SetResourceReference(Control.BackgroundProperty, "ThemeControlBrush");
            deleteButton.SetResourceReference(Control.BorderBrushProperty, "ThemeDangerBrush");
            deleteButton.SetResourceReference(Control.ForegroundProperty, "ThemeDangerBrush");
            deleteButton.SetResourceReference(Control.FocusVisualStyleProperty, "SettingsFocusVisualStyle");

            deleteButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new LucideIcon
                    {
                        Kind = "Trash2",
                        Width = 14,
                        Height = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = LocalizationService.Get("Editor.DeletePageTooltip"),
                        Margin = new Thickness(6, 0, 0, 0),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            foreach (var label in ((StackPanel)deleteButton.Content).Children.OfType<TextBlock>())
                label.SetResourceReference(TextBlock.ForegroundProperty, "ThemeDangerBrush");
            foreach (var icon in ((StackPanel)deleteButton.Content).Children.OfType<LucideIcon>())
                icon.SetResourceReference(Shape.StrokeProperty, "ThemeDangerBrush");

            deleteButton.Click += async (sender, args) =>
            {
                args.Handled = true;
                using var operationLease = CaptureDocumentOperationLease(_pdfService);
                await DeletePageAtAsync(pageControl.PageIndex, operationLease);
            };

            host.MouseEnter += (_, __) =>
            {
                if (_pageControls.Count > 1)
                    deleteButton.Visibility = Visibility.Visible;
            };
            host.MouseLeave += (_, __) =>
            {
                if (!deleteButton.IsMouseOver)
                    deleteButton.Visibility = Visibility.Hidden;
            };
            deleteButton.MouseLeave += (_, __) =>
            {
                if (!host.IsMouseOver)
                    deleteButton.Visibility = Visibility.Hidden;
            };

            _pageDeleteButtons.Add(deleteButton);
            host.Children.Add(deleteButton);
            return host;
        }

        private FrameworkElement CreatePageInsertGap(int insertIndex)
        {
            var zone = new Grid
            {
                Height = PageSpacing,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ClipToBounds = false
            };

            var guideLine = new Border
            {
                Width = 150,
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            guideLine.SetResourceReference(Border.BackgroundProperty, "ThemeAccentBrush");

            var insertButton = new Button
            {
                Width = 78,
                Height = 32,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                ToolTip = LocalizationService.Get("Editor.InsertPageHereTooltip"),
                Template = CreatePageChromeButtonTemplate(
                    "ThemeSelectionBrush", "ThemeControlHoverBrush")
            };
            insertButton.SetResourceReference(Control.BackgroundProperty, "ThemeControlBrush");
            insertButton.SetResourceReference(Control.BorderBrushProperty, "ThemeAccentBrush");
            insertButton.SetResourceReference(Control.ForegroundProperty, "ThemeAccentBrush");
            insertButton.SetResourceReference(Control.FocusVisualStyleProperty, "SettingsFocusVisualStyle");

            insertButton.Content = new LucideIcon
            {
                Kind = "Plus",
                Width = 17,
                Height = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ((LucideIcon)insertButton.Content).SetResourceReference(Shape.StrokeProperty, "ThemeAccentBrush");

            insertButton.Click += async (_, __) =>
            {
                using var operationLease = CaptureDocumentOperationLease(_pdfService);
                await InsertPageAtAsync(insertIndex, operationLease);
            };

            zone.MouseEnter += (_, __) =>
            {
                guideLine.Visibility = Visibility.Visible;
                insertButton.Visibility = Visibility.Visible;
            };
            zone.MouseLeave += (_, __) =>
            {
                if (!insertButton.IsMouseOver)
                {
                    guideLine.Visibility = Visibility.Collapsed;
                    insertButton.Visibility = Visibility.Collapsed;
                }
            };
            insertButton.MouseLeave += (_, __) =>
            {
                if (!zone.IsMouseOver)
                {
                    guideLine.Visibility = Visibility.Collapsed;
                    insertButton.Visibility = Visibility.Collapsed;
                }
            };

            _pageInsertButtons.Add(insertButton);
            zone.Children.Add(guideLine);
            zone.Children.Add(insertButton);
            return zone;
        }

        private void RefreshPageDeleteButtons()
        {
            var visibility = _pageControls.Count > 1 ? Visibility.Hidden : Visibility.Collapsed;
            foreach (var button in _pageDeleteButtons)
            {
                button.Visibility = visibility;
                button.ToolTip = LocalizationService.Get("Editor.DeletePageTooltip");
                if (button.Content is StackPanel panel && panel.Children.Count > 1 && panel.Children[1] is TextBlock label)
                    label.Text = LocalizationService.Get("Editor.DeletePageTooltip");
            }

            foreach (var button in _pageInsertButtons)
                button.ToolTip = LocalizationService.Get("Editor.InsertPageHereTooltip");
        }

        private async Task InsertPageAtAsync(
            int insertIndex,
            DocumentOperationLease operationLease = null)
        {
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using (editLease)
            {
                if (string.IsNullOrWhiteSpace(_currentPdfPath))
                {
                    if (operationLease == null)
                        GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                    return;
                }

                string filePath = _currentPdfPath;
                DocumentOperationLease currentLease = operationLease ?? CaptureDocumentOperationLease(_pdfService);
                var owner = GetMainWindow();
                var picker = new PageTemplatePickerWindow();
                if (owner != null)
                    picker.Owner = owner;

                if (picker.ShowDialog() != true)
                {
                    currentLease.Dispose();
                    return;
                }

                try
                {
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    if (_documentSaveCoordinator.IsDirty &&
                        (!await AutoSaveAsync(currentLease) || !ValidateDocumentOperationLease(currentLease)))
                        return;

                    byte[] beforeBytes = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    int undoFocusIndex = Math.Max(0, Math.Min(insertIndex, Math.Max(_pageControls.Count - 1, 0)));
                    var beforeBookmarks = PageBookmarkService.Load(filePath).ToList();

                    await _pdfService.InsertPageAsync(filePath, insertIndex, picker.SelectedTemplate);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;

                    byte[] afterBytes = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                    if (currentLease == null)
                        return;

                    int insertedPageIndex = Math.Max(0, Math.Min(insertIndex, _pageControls.Count - 1));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    JumpToPage(insertedPageIndex);
                    RecentFilesService.UpdateMetadata(filePath, _pageControls.Count, File.GetLastWriteTimeUtc(filePath));
                    var afterBookmarks = PageBookmarkService.ApplyPageInsert(filePath, insertedPageIndex).ToList();
                    RefreshBookmarks(_loadSessionId, filePath, currentLease);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    PushUndoAction(new DocumentSnapshotAction(this, beforeBytes, afterBytes, undoFocusIndex, insertedPageIndex, beforeBookmarks, afterBookmarks));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageAdded"), "\uE710");
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    var mw = GetMainWindow();
                    if (mw != null)
                        await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Editor.AddPageFailed", ex.Message));
                    else
                        MessageBox.Show(LocalizationService.Format("Editor.AddPageFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    currentLease?.Dispose();
                }
            }
        }

        private async Task DeletePageAtAsync(
            int pageIndex,
            DocumentOperationLease operationLease = null)
        {
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using (editLease)
            {
                if (string.IsNullOrWhiteSpace(_currentPdfPath))
                {
                    if (operationLease == null)
                        GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                    return;
                }

                if (_pageControls.Count <= 1)
                {
                    if (operationLease == null)
                        GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageDeleteBlocked"), "\uE783");
                    return;
                }

                string filePath = _currentPdfPath;
                DocumentOperationLease currentLease = operationLease ?? CaptureDocumentOperationLease(_pdfService);
                try
                {
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    if (_documentSaveCoordinator.IsDirty &&
                        (!await AutoSaveAsync(currentLease) || !ValidateDocumentOperationLease(currentLease)))
                        return;

                    byte[] beforeBytes = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    var beforeBookmarks = PageBookmarkService.Load(filePath).ToList();
                    await _pdfService.DeletePageAsync(filePath, pageIndex);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;

                    byte[] afterBytes = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                    if (currentLease == null)
                        return;

                    int focusAfterDelete = Math.Max(0, Math.Min(pageIndex, _pageControls.Count - 1));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    JumpToPage(focusAfterDelete);
                    RecentFilesService.UpdateMetadata(filePath, _pageControls.Count, File.GetLastWriteTimeUtc(filePath));
                    var afterBookmarks = PageBookmarkService.ApplyPageDelete(filePath, pageIndex).ToList();
                    RefreshBookmarks(_loadSessionId, filePath, currentLease);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    PushUndoAction(new DocumentSnapshotAction(this, beforeBytes, afterBytes, pageIndex, focusAfterDelete, beforeBookmarks, afterBookmarks));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageDeleted"), "\uE74D");
                }
                catch (OperationCanceledException)
                {
                }
                catch (InvalidOperationException)
                {
                    if (ValidateDocumentOperationLease(currentLease))
                        GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageDeleteBlocked"), "\uE783");
                }
                catch (Exception ex)
                {
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    var mw = GetMainWindow();
                    if (mw != null)
                        await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Editor.DeletePageFailed", ex.Message));
                    else
                        MessageBox.Show(LocalizationService.Format("Editor.DeletePageFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    currentLease?.Dispose();
                }
            }
        }

        private async Task<DocumentOperationLease> ApplyDocumentSnapshotAsync(
            byte[] snapshotBytes,
            int focusPageIndex,
            DocumentOperationLease operationLease)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath) ||
                !ValidateDocumentOperationLease(operationLease))
                return null;

            string filePath = _currentPdfPath;
            await WriteDocumentBytesAsync(filePath, snapshotBytes, operationLease.Token);
            if (!ValidateDocumentOperationLease(operationLease))
                return null;

            var refreshedLease = await ReloadDocumentForOperationAsync(filePath, operationLease);
            if (refreshedLease == null)
                return null;

            if (_pageControls.Count > 0)
            {
                if (!ValidateDocumentOperationLease(refreshedLease))
                {
                    refreshedLease.Dispose();
                    return null;
                }
                JumpToPage(Math.Max(0, Math.Min(focusPageIndex, _pageControls.Count - 1)));
            }

            if (!ValidateDocumentOperationLease(refreshedLease))
            {
                refreshedLease.Dispose();
                return null;
            }
            RecentFilesService.UpdateMetadata(filePath, _pageControls.Count, File.GetLastWriteTimeUtc(filePath));
            return refreshedLease;
        }

        private static Task WriteDocumentBytesAsync(
            string filePath,
            byte[] snapshotBytes,
            CancellationToken cancellationToken = default)
        {
            // DocumentSnapshotAction is an editor-owned structural write, so
            // it must use the same process-wide PDF path lease as PdfService
            // saves before replacing bytes. The subsequent LoadPdfAsync also
            // joins that lease for its native reload.
            return PdfSaveCoordinator.RunExclusiveAsync(
                filePath,
                () => WriteDocumentBytesCoreAsync(filePath, snapshotBytes, cancellationToken));
        }

        private static async Task WriteDocumentBytesCoreAsync(
            string filePath,
            byte[] snapshotBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string tempPath = PdfAtomicFile.CreateTempPath(filePath);

            try
            {
                await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await output.WriteAsync(snapshotBytes ?? Array.Empty<byte>(), cancellationToken);
                    output.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                PdfAtomicFile.Replace(tempPath, filePath);
            }
            finally
            {
                PdfAtomicFile.TryDelete(tempPath);
            }
        }

        /// <summary>
        /// Renders a single page's initial image using the fast BitmapSource path.
        /// Only renders if the page hasn't been rendered yet.
        /// Does NOT adjust scroll 闁?the caller is responsible for anchor save/restore.
        /// </summary>
        private async Task RenderPageInitialAsync(PdfPageControl page, CancellationToken token)
        {
            if (!_isHostActive || _resourcesReleased || _pagesInitiallyRendered.Contains(page.PageIndex)) return;

            try
            {
                double renderScale = PdfRenderPolicy.CalculateRenderScale(
                    CurrentPerformanceMode,
                    page.Width,
                    page.Height,
                    1.0);
                var bitmapSource = await _pdfService.RenderPageBitmapSourceAsync(page.PageIndex, renderScale, token);
                if (bitmapSource != null)
                {
                    token.ThrowIfCancellationRequested();
                    page.PageSource = bitmapSource;
                    _pagesInitiallyRendered.Add(page.PageIndex);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RenderPageInitialAsync page {page.PageIndex} failed: {ex.Message}");
            }
        }

        private void PageControl_ModeChanged(object sender, CustomInkInputProcessingMode mode)
        {
            // Update UI when mode changes from double tap
            if (mode == CustomInkInputProcessingMode.Erasing && _currentTool != ToolType.Eraser)
            {
                _previousTool = _currentTool;
                ActivateTool(ToolType.Eraser);
            }
            else if (mode == CustomInkInputProcessingMode.Inking && _currentTool == ToolType.Eraser)
            {
                ActivateTool(_previousTool);
            }
        }

        private void PageControl_SelectionChanged(object sender, AnnotationSelectionChangedEventArgs e)
        {
            if (_currentTool != ToolType.Select) return;

            if (sender is PdfPageControl page)
            {
                if (e.HasSelection)
                    _activeSelectionPage = page;
                else if (_activeSelectionPage == page)
                    _activeSelectionPage = null;
            }
        }

        private void PageControl_SelectionMoveCompleted(object sender, SelectionMoveCompletedEventArgs e)
        {
            if (sender is not PdfPageControl page) return;

            Rect bounds = page.GetSelectionBounds();
            if (bounds.IsEmpty)
            {
                var action = new SelectionMoveAction(page, e.DeltaX, e.DeltaY, e.SelectedStrokes, e.SelectedTextContainers);
                PushUndoAction(action);
                return;
            }

            Point centerInPage = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            PdfPageControl targetPage = FindPageAtContainerPoint(page, centerInPage);

            if (targetPage != null && targetPage != page)
            {
                Point targetOriginInPage = targetPage.TranslatePoint(new Point(0, 0), page);
                double adjustX = -targetOriginInPage.X;
                double adjustY = -targetOriginInPage.Y;

                page.ClearSelection();

                var moveAction = new SelectionCrossPageMoveAction(
                    page, targetPage,
                    e.DeltaX, e.DeltaY,
                    adjustX, adjustY,
                    e.SelectedStrokes
                        .Select(page.CaptureStrokePlacement)
                        .ToList(),
                    e.SelectedTextContainers);

                if (moveAction.ExecuteInitialTransfer())
                    PushUndoAction(moveAction);
            }
            else
            {
                var action = new SelectionMoveAction(page, e.DeltaX, e.DeltaY, e.SelectedStrokes, e.SelectedTextContainers);
                PushUndoAction(action);
            }
        }

        /// <summary>
        /// Task 9: page hit-test shared by the selection cross-page move and the
        /// text-box dragHandle cross-page move. Finds the page whose bounds contain
        /// the given point (expressed in the source page's coordinate system,
        /// translated through PagesContainer). Returns null when the point lands in
        /// a gap between pages or outside the document — callers keep the items on
        /// the source page in that case.
        /// </summary>
        private PdfPageControl FindPageAtContainerPoint(PdfPageControl source, Point centerInSource)
        {
            Point centerInContainer = source.TranslatePoint(centerInSource, PagesContainer);

            foreach (var p in _pageControls)
            {
                Point ptInPage = PagesContainer.TranslatePoint(centerInContainer, p);
                if (ptInPage.X >= 0 && ptInPage.X <= p.ActualWidth &&
                    ptInPage.Y >= 0 && ptInPage.Y <= p.ActualHeight)
                {
                    return p;
                }
            }
            return null;
        }

        private void PageControl_SelectionResizeCompleted(object sender, SelectionResizeCompletedEventArgs e)
        {
            if (sender is not PdfPageControl page) return;
            var action = new SelectionResizeAction(page, e.TotalScale, e.Anchor, e.SelectedStrokes, e.SelectedTextContainers);
            PushUndoAction(action);
        }

        private void PenToolButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleToolButton(ToolType.Pen, PenToolButton, _penPopup);
        }

        private void HighlighterToolButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleToolButton(ToolType.Highlighter, HighlighterToolButton, _highlighterPopup);
        }

        private void HiddenInkToolButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleToolButton(ToolType.HiddenInk, HiddenInkToolButton);
        }

        private void StickyNoteToolButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleToolButton(ToolType.StickyNote, StickyNoteToolButton);
        }



        private void EraserToolButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleToolButton(ToolType.Eraser, EraserToolButton, _eraserPopup);
        }

        private void ShapeToolButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleToolButton(ToolType.Shape, ShapeToolButton, _shapePopup);
        }

        // Task 20: laser pointer — ephemeral ink that fades away ~1s after
        // writing. No popup (no options), so the popup argument stays null
        // (ToggleToolButton is null-safe for it).
        private void LaserToolButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleToolButton(ToolType.Laser, LaserToolButton);
        }

        private void TextToolButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleToolButton(ToolType.Text, TextToolButton);
        }

        private void SelectToolButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleToolButton(ToolType.Select, SelectToolButton);
        }



        private void ActivateTool(ToolType tool)
        {
            if (_resizingTextContainer != null && tool != ToolType.Text)
                CancelTextResize(restoreBounds: true);

            if (_currentTool == tool) return;

            if (_currentTool == ToolType.Select && tool != ToolType.Select)
            {
                _activeSelectionPage?.ClearSelection();
                _activeSelectionPage = null;
            }

            bool wasSelectableSurfaceActive = IsSelectablePdfSurfaceActive;

            if (tool != ToolType.Text)
                DeselectTextBox();

            _isUpdatingToolState = true;
            _currentTool = tool;
            CloseToolPopups(tool);

            PenToolButton.IsChecked = tool == ToolType.Pen;
            HighlighterToolButton.IsChecked = tool == ToolType.Highlighter
                || tool == ToolType.TextHighlight
                || tool == ToolType.AreaHighlight;
            HiddenInkToolButton.IsChecked = tool == ToolType.HiddenInk;
            StickyNoteToolButton.IsChecked = tool == ToolType.StickyNote;
            EraserToolButton.IsChecked = tool == ToolType.Eraser;
            ShapeToolButton.IsChecked = tool == ToolType.Shape;
            LaserToolButton.IsChecked = tool == ToolType.Laser;
            TextToolButton.IsChecked = tool == ToolType.Text;
            SelectToolButton.IsChecked = tool == ToolType.Select;
            _isUpdatingToolState = false;

            UpdateToolIconColors();
            ApplyToolToAllPages();
            UpdatePdfSurfaceVisibility();

            if (wasSelectableSurfaceActive && !IsSelectablePdfSurfaceActive)
            {
                SyncCustomSurfaceFromSelectableViewer();
            }
            else if (!wasSelectableSurfaceActive && IsSelectablePdfSurfaceActive)
            {
                SyncSelectableViewerFromCustomView();
            }
        }

        private void UpdateToolIconColors()
        {
            // Keep every Lucide glyph monochrome and theme-owned. Data colors
            // remain visible as small bars, so black ink no longer needs a
            // doubled icon/backplate and bright highlighter colors do not make
            // one toolbar glyph visually heavier than the others.
            if (PenColorIndicator != null)
                PenColorIndicator.Background = new SolidColorBrush(_penColor);
            if (HighlighterColorIndicator != null)
                HighlighterColorIndicator.Background = new SolidColorBrush(
                    GetHighlighterPreviewStrokeColor(HighlighterApplyMode.Freehand, _highlighterColor));

            UpdateHighlighterModePreviewVisuals();
        }

        // Task 15: pen-only drawing (palm rejection) toolbar toggle.
        private void PenOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            // Persist and retain the same snapshot so the touch gate and all
            // page controls observe the new value immediately. Loading a
            // separate snapshot here would leave _applicationSettings stale.
            var settings = AppSettingsService.Load();
            settings.PenOnlyMode = PenOnlyButton.IsChecked == true;
            AppSettingsService.Save(settings);
            _applicationSettings = settings;
            ApplyToolToAllPages(settings);
        }

        private void UpdatePenOnlyButtonVisual()
        {
            // Distinct checked tint uses theme resources so it remains
            // legible in light, dark and high-contrast palettes.
            PenOnlyIcon.SetResourceReference(
                Path.StrokeProperty,
                PenOnlyButton.IsChecked == true
                    ? "ThemeAccentBrush"
                    : "ThemeForegroundBrush");
        }

        #region Legacy pen preset JSON compatibility

        // The toolbar no longer creates visible preset slots, but retain the
        // old private entry points so legacy settings can still be read and
        // written by the existing AppSettingsService contract. This method is
        // deliberately read-only: it never fills or resets an empty list.
        private void InitializePenPresetSlots()
        {
            _ = AppSettingsService.Load().PenPresets;
        }

        private static List<PenPreset> BuildDefaultPenPresets()
        {
            return new List<PenPreset>
            {
                new PenPreset { Tool = "Pen", ColorHex = "#000000", Size = 2 },
                new PenPreset { Tool = "Highlighter", ColorHex = "#FFFF00", Size = 8 },
                new PenPreset { Tool = "Pen", ColorHex = "#FF0000", Size = 3 }
            };
        }

        #endregion


        #region Task 22: on-screen ruler

        // Task 22: on-screen ruler toggle. This is an OVERLAY toggle, not a
        // ToolType — it stays active alongside the current tool (e.g. Pen +
        // ruler ON). Session-only: neither persisted to settings nor saved
        // with the document. v1: button only (no shortcut, no ESC binding).
        private void RulerToolButton_Click(object sender, RoutedEventArgs e)
        {
            SetRulerVisible(RulerToolButton.IsChecked == true);
        }

        private void SetRulerVisible(bool visible)
        {
            _rulerVisible = visible;
            RulerToolButton.IsChecked = visible;
            RulerIcon?.SetResourceReference(
                Path.StrokeProperty,
                visible ? "ThemeAccentBrush" : "ThemeForegroundBrush");

            if (visible)
            {
                EnsureRulerVisual();
                _rulerVisual.Visibility = Visibility.Visible;
            }
            else if (_rulerVisual != null)
            {
                _rulerVisual.Visibility = Visibility.Collapsed;
                // Drop any in-flight manipulation so a stale capture can't
                // keep dragging an invisible ruler.
                _isDraggingRuler = false;
                _isRotatingRuler = false;
            }
        }

        /// <summary>
        /// Builds the ruler visual once (Grid 360×56: semi-transparent
        /// rounded body, tick marks every 10px with longer ticks every
        /// 50px, a centre handle dot, and transparent end-cap rectangles
        /// that afford rotation). Rotation snaps to 15° increments.
        /// </summary>
        private void EnsureRulerVisual()
        {
            if (_rulerVisual != null) return;

            _rulerRotate = new RotateTransform(0, RulerLength / 2, RulerHeight / 2);

            var ruler = new Grid
            {
                Width = RulerLength,
                Height = RulerHeight,
                // Keep the ruler body draggable even though its visual
                // children are intentionally non-hit-testable. The full
                // overlay canvas remains background-free so empty space
                // still passes through to the document surface.
                Background = Brushes.Transparent,
                RenderTransform = _rulerRotate,
                Cursor = Cursors.SizeAll
            };

            // Semi-transparent body. Keep the ruler itself in application
            // chrome resources; only PDF/page annotations retain content
            // colours.
            var rulerBody = new Rectangle
            {
                Fill = Brushes.Transparent,
                Stroke = Brushes.Transparent,
                StrokeThickness = 1,
                RadiusX = 6,
                RadiusY = 6,
                IsHitTestVisible = false
            };
            rulerBody.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ThemeToolbarBrush");
            rulerBody.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ThemeBorderBrush");
            rulerBody.Opacity = 0.92;
            ruler.Children.Add(rulerBody);

            // Tick marks along the top edge: minor every 10px, major every
            // 50px (labels intentionally skipped in v1).
            for (double x = 10; x < RulerLength; x += 10)
            {
                bool major = Math.Abs(x % 50) < 0.01;
                var tick = new Line
                {
                    X1 = x, Y1 = 0,
                    X2 = x, Y2 = major ? 12 : 6,
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                tick.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ThemeSubtleTextBrush");
                tick.Opacity = 0.72;
                ruler.Children.Add(tick);
            }

            // Centre rotation handle dot.
            var rulerCenterDot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            rulerCenterDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ThemeAccentBrush");
            rulerCenterDot.Opacity = 0.82;
            ruler.Children.Add(rulerCenterDot);

            // Transparent end-cap zones: hitting them starts a rotation
            // drag instead of a move (they sit above the body in z-order).
            // Non-null Fill is required for hit testing.
            var capFill = Brushes.Transparent;
            var leftCap = new Rectangle { Width = RulerEndCapZone, Height = RulerHeight, Fill = capFill, Cursor = Cursors.SizeNESW, HorizontalAlignment = HorizontalAlignment.Left };
            var rightCap = new Rectangle { Width = RulerEndCapZone, Height = RulerHeight, Fill = capFill, Cursor = Cursors.SizeNESW, HorizontalAlignment = HorizontalAlignment.Right };
            ruler.Children.Add(leftCap);
            ruler.Children.Add(rightCap);

            // Interactions: left-drag the body = move; left-drag either end
            // cap OR right-drag anywhere = rotate. Stylus/touch input is
            // promoted to mouse events, so pen/finger drags work too
            // (manipulating the ruler with the pen is allowed, GoodNotes
            // style). The ruler never creates ink, so pen-only mode does
            // not apply to it.
            ruler.MouseLeftButtonDown += Ruler_MouseLeftButtonDown;
            ruler.MouseRightButtonDown += Ruler_MouseRightButtonDown;
            ruler.MouseMove += Ruler_MouseMove;
            ruler.MouseLeftButtonUp += Ruler_MouseButtonUp;
            ruler.MouseRightButtonUp += Ruler_MouseButtonUp;
            ruler.LostMouseCapture += Ruler_LostMouseCapture;

            _rulerVisual = ruler;

            // First show: default to the middle of the viewport so the
            // ruler can never appear off-screen.
            double vw = ActualWidth > 0 ? ActualWidth : PdfScrollViewer.ViewportWidth;
            double vh = ActualHeight > 0 ? ActualHeight : PdfScrollViewer.ViewportHeight;
            if (vw <= 0) vw = 800;
            if (vh <= 0) vh = 600;
            _rulerCenter = new Point(vw / 2, vh / 2);
            UpdateRulerPosition();

            RulerOverlayCanvas.Children.Add(ruler);
        }

        private void Ruler_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_rulerVisible || _rulerVisual == null) return;

            // GetPosition applies the ruler's RenderTransform, so local
            // coordinates are the ruler's own (unrotated) frame — the end
            // zones stay the first/last 14px of the body at any angle.
            var local = e.GetPosition(_rulerVisual);
            bool inEndZone = local.X < RulerEndCapZone || local.X > RulerLength - RulerEndCapZone;

            StartRulerManipulation(e.GetPosition(RulerOverlayCanvas), rotating: inEndZone);
            _rulerVisual.CaptureMouse();
            e.Handled = true;
        }

        // Right-drag anywhere on the ruler rotates it (alternative affordance
        // when the end caps are hard to hit at steep angles).
        private void Ruler_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_rulerVisible || _rulerVisual == null) return;

            StartRulerManipulation(e.GetPosition(RulerOverlayCanvas), rotating: true);
            _rulerVisual.CaptureMouse();
            e.Handled = true;
        }

        private void StartRulerManipulation(Point viewportPoint, bool rotating)
        {
            _isDraggingRuler = !rotating;
            _isRotatingRuler = rotating;
            _rulerDragOffset = new Point(viewportPoint.X - _rulerCenter.X, viewportPoint.Y - _rulerCenter.Y);
            _rotateStartPointerAngle = Math.Atan2(viewportPoint.Y - _rulerCenter.Y, viewportPoint.X - _rulerCenter.X) * 180.0 / Math.PI;
            _rotateStartRulerAngle = _rulerAngle;
        }

        private void Ruler_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingRuler && !_isRotatingRuler) return;

            var p = e.GetPosition(RulerOverlayCanvas);

            if (_isDraggingRuler)
            {
                _rulerCenter = new Point(p.X - _rulerDragOffset.X, p.Y - _rulerDragOffset.Y);
                ClampRulerCenter();
                UpdateRulerPosition();
            }
            else if (_isRotatingRuler)
            {
                double pointerAngle = Math.Atan2(p.Y - _rulerCenter.Y, p.X - _rulerCenter.X) * 180.0 / Math.PI;
                _rulerAngle = SnapRulerAngle(_rotateStartRulerAngle + pointerAngle - _rotateStartPointerAngle);
                _rulerRotate.Angle = _rulerAngle;
            }
        }

        private void Ruler_MouseButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingRuler && !_isRotatingRuler) return;

            _rulerVisual?.ReleaseMouseCapture();
            _isDraggingRuler = false;
            _isRotatingRuler = false;
            e.Handled = true;
        }

        private void Ruler_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _isDraggingRuler = false;
            _isRotatingRuler = false;
        }

        private void UpdateRulerPosition()
        {
            Canvas.SetLeft(_rulerVisual, _rulerCenter.X - RulerLength / 2);
            Canvas.SetTop(_rulerVisual, _rulerCenter.Y - RulerHeight / 2);
        }

        // Keeps the ruler reachable: clamping the CENTRE inside the viewport
        // guarantees at least the centre point of the body stays grabbable,
        // no matter how the ruler is rotated (a full bounding-box clamp
        // would need re-clamping on every rotation; v1 keeps it simple).
        private void ClampRulerCenter()
        {
            double vw = ActualWidth > 0 ? ActualWidth : PdfScrollViewer.ViewportWidth;
            double vh = ActualHeight > 0 ? ActualHeight : PdfScrollViewer.ViewportHeight;
            if (vw <= 0 || vh <= 0) return;

            _rulerCenter = new Point(
                Math.Max(0, Math.Min(_rulerCenter.X, vw)),
                Math.Max(0, Math.Min(_rulerCenter.Y, vh)));
        }

        // v1: rotation ALWAYS snaps to 15° increments (simple + predictable).
        private static double SnapRulerAngle(double angle)
        {
            double snapped = Math.Round(angle / RulerRotationSnapDegrees) * RulerRotationSnapDegrees;
            snapped %= 360.0;
            if (snapped < 0) snapped += 360.0;
            return snapped;
        }

        /// <summary>
        /// Task 22: endpoints of the ruler's TOP edge (the drawing edge) in
        /// viewport (root-grid) coordinates, or null while the ruler is
        /// hidden. The edge — not the centre line — is the snap target:
        /// users draw along the visible edge of the ruler, and a centre-line
        /// snap would visually jump the stroke half the ruler height (28px)
        /// away from where it was drawn. Rotating the ruler 180° swaps which
        /// physical edge is "top", so every direction stays usable.
        /// </summary>
        private (Point TopA, Point TopB, Point BottomA, Point BottomB)? GetRulerGeometryEndpoints()
        {
            if (!_rulerVisible || _rulerVisual == null) return null;

            double rad = _rulerAngle * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            // Unit vector along the ruler, and its "up" normal
            // (pre-rotation -Y rotated by the ruler angle).
            double dirX = cos, dirY = sin;
            double upX = sin, upY = -cos;
            double halfLen = RulerLength / 2;
            double halfHeight = RulerHeight / 2;

            var topA = new Point(
                    _rulerCenter.X - halfLen * dirX + halfHeight * upX,
                    _rulerCenter.Y - halfLen * dirY + halfHeight * upY);
            var topB = new Point(
                    _rulerCenter.X + halfLen * dirX + halfHeight * upX,
                    _rulerCenter.Y + halfLen * dirY + halfHeight * upY);
            var bottomA = new Point(
                    _rulerCenter.X - halfLen * dirX - halfHeight * upX,
                    _rulerCenter.Y - halfLen * dirY - halfHeight * upY);
            var bottomB = new Point(
                    _rulerCenter.X + halfLen * dirX - halfHeight * upX,
                    _rulerCenter.Y + halfLen * dirY - halfHeight * upY);
            return (topA, topB, bottomA, bottomB);
        }

        #endregion

        /// <summary>
        /// Task 16: toggles fullscreen immersive mode. Entering closes all
        /// tool popups and visually hides the floating toolbar via
        /// Opacity=0 + IsHitTestVisible=false (preferred over
        /// Visibility=Collapsed: purely visual, guaranteed zero layout
        /// shift even though the toolbar is an overlay that doesn't affect
        /// the page layout either). Writing, scrolling, page jumping and
        /// Ctrl+Z all keep working — none of them need the toolbar. Leaving
        /// restores the recorded toolbar state; repeated toggles leave no
        /// residue.
        /// </summary>
        private void ToggleImmersiveMode()
        {
            CloseToolPopups();
            _isImmersiveMode = !_isImmersiveMode;

            if (_isImmersiveMode)
            {
                _preImmersiveToolbarOpacity = ToolbarBorder.Opacity;
                _preImmersiveToolbarHitTestVisible = ToolbarBorder.IsHitTestVisible;
                _preImmersiveSidebarOpacity = DocumentSidebar.Opacity;
                _preImmersiveSidebarHitTestVisible = DocumentSidebar.IsHitTestVisible;
                _preImmersiveSearchOpacity = PdfSearchPanel.Opacity;
                _preImmersiveSearchHitTestVisible = PdfSearchPanel.IsHitTestVisible;
                ToolbarBorder.Opacity = 0;
                ToolbarBorder.IsHitTestVisible = false;
                DocumentSidebar.Opacity = 0;
                DocumentSidebar.IsHitTestVisible = false;
                PdfSearchPanel.Opacity = 0;
                PdfSearchPanel.IsHitTestVisible = false;
            }
            else
            {
                ToolbarBorder.Opacity = _preImmersiveToolbarOpacity;
                ToolbarBorder.IsHitTestVisible = _preImmersiveToolbarHitTestVisible;
                DocumentSidebar.Opacity = _preImmersiveSidebarOpacity;
                DocumentSidebar.IsHitTestVisible = _preImmersiveSidebarHitTestVisible;
                PdfSearchPanel.Opacity = _preImmersiveSearchOpacity;
                PdfSearchPanel.IsHitTestVisible = _preImmersiveSearchHitTestVisible;
            }
        }

        public void ApplySettings(AppSettings settings)
        {
            string previousPerformanceMode = CurrentPerformanceMode;
            _applicationSettings = settings ?? new AppSettings();

            try
            {
                _penColor = (Color)ColorConverter.ConvertFromString(_applicationSettings.DefaultPenColorHex ?? "#000000");
            }
            catch
            {
                _penColor = Colors.Black;
            }

            _penSize = Math.Max(0.5, Math.Min(24, _applicationSettings.DefaultPenSize));
            UpdateToolIconColors();
            if (_autoSaveTimer != null)
                _autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(15, _applicationSettings.AutoSaveIntervalSeconds));

            ApplyToolToAllPages(_applicationSettings);

            if (!string.Equals(previousPerformanceMode, CurrentPerformanceMode, StringComparison.Ordinal))
            {
                var profile = PdfRenderPolicy.GetProfile(CurrentPerformanceMode);
                _lastRenderedDpiScale = Math.Min(Math.Max(_zoomLevel, 1.0), profile.MaxRenderScale);
                _pagesInitiallyRendered.Clear();
                _pagesRenderedAtScale.Clear();

                var visiblePages = GetVisiblePageControls();
                TrimPageBitmapWorkingSet(visiblePages);
                if (_isHostActive && !_resourcesReleased)
                    _ = RenderVisibleWorkingSetAsync();
            }
        }

        /// <summary>
        /// True while this editor owns a text drag/resize transaction. Ordinary
        /// TextBox focus/edit sessions intentionally do not count as transient
        /// interactions and are never cancelled by this boundary.
        /// </summary>
        public bool HasActiveInteraction =>
            _draggedContainer != null
            || _dragArmed
            || _isDragging
            || _resizingTextContainer != null;

        /// <summary>
        /// Cancels editor-owned text gestures and then delegates the same
        /// idempotent boundary to every live page. Only a normal pointer/stylus
        /// release is allowed to publish an undo action or dirty state.
        /// </summary>
        public void CancelInteraction(string reason = null)
        {
            CancelTextBoxDrag(restoreBounds: true);
            CancelTextResize(restoreBounds: true);
            ResetThumbnailDragState();
            ClearThumbnailDropIndicator();
            InteractionCancellation.CancelAll(_pageControls, reason);
            _isDelegatingSelection = false;
            _selectionDelegateTarget = null;
        }

        /// <summary>
        /// Pauses native rendering and releases display-only bitmaps when this
        /// editor is behind another tab. Annotation state remains in memory.
        /// </summary>
        public void SetHostActive(bool isActive)
        {
            if (!isActive)
            {
                CloseTransientUi("inactive editor");
                ResetThumbnailDragState();
                ClearThumbnailDropIndicator();
                _documentOperationSession.Cancel();
            }
            else
                EnsureTransientUiHooks();
            if (_resourcesReleased || (isActive && !_releaseState.CanResumeInteraction) || _isHostActive == isActive)
                return;

            if (isActive)
                _documentOperationSession.Begin(_loadSessionId, _currentPdfPath, _pdfService);
            _isHostActive = isActive;
            foreach (var page in _pageControls)
            {
                page.SetHostActive(isActive);
                page.SetDocumentInputEnabled(isActive && !_documentInteractionBlocked);
            }

            if (!isActive)
            {
                CancelRenderWork();
                _thumbnailLoadCts?.Cancel();
                _thumbnailLoadCts?.Dispose();
                _thumbnailLoadCts = null;
                _thumbnailPagesLoading.Clear();
                _thumbnailPageLoadSessions.Clear();
                _isRefreshingThumbnails = false;
                CancelSmoothScroll();

                foreach (var page in _pageControls)
                    page.PageSource = null;
                _pagesInitiallyRendered.Clear();
                _pagesRenderedAtScale.Clear();
                ClearThumbnailCache();
                return;
            }

            _thumbnailLoadCts = new CancellationTokenSource();
            LoadVisibleThumbnails();
            _ = RenderVisibleWorkingSetAsync();
        }

        private void SetDocumentInteractionBlocked(bool blocked)
        {
            bool effectiveBlocked = blocked || !_releaseState.CanResumeInteraction;
            _documentInteractionBlocked = effectiveBlocked;
            // Disable the complete editor command/input subtree, not only the
            // page controls.  Toolbar commands and routed keyboard handlers
            // can otherwise still mutate the model while close/navigation is
            // waiting for the final persistence barrier. Popup editors are
            // committed explicitly by the close/navigation protocol above.
            IsEnabled = !effectiveBlocked;
            foreach (var page in _pageControls)
                page.SetDocumentInputEnabled(!effectiveBlocked && _isHostActive && !_resourcesReleased);
        }

        private async Task BeginDocumentInteractionBlockAsync(
            CancellationToken cancellationToken,
            DocumentOperationLease operationLease = null)
        {
            if (operationLease != null && !ValidateDocumentOperationLease(operationLease))
                throw new OperationCanceledException(operationLease.Token);

            _editAdmission.BeginClose();
            SetDocumentInteractionBlocked(true);
            await _editAdmission.WaitForQuiescenceAsync(cancellationToken)
                .ConfigureAwait(true);
            if (operationLease != null && !ValidateDocumentOperationLease(operationLease))
                throw new OperationCanceledException(operationLease.Token);

            // WPF input routed before IsEnabled was flipped can still be in
            // the dispatcher queue and may mutate the live model before its
            // event callback calls MarkDirty/PushUndoAction.  Let already
            // queued input callbacks drain before the final generation check;
            // this is an async dispatcher barrier, never a UI-thread wait.
                await Dispatcher.InvokeAsync(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.Input)
                .Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(true);
            if (operationLease != null && !ValidateDocumentOperationLease(operationLease))
                throw new OperationCanceledException(operationLease.Token);
        }

        private bool TryBeginDocumentEdit(out IDisposable lease)
        {
            lease = null;
            if (_resourcesReleased || _documentInteractionBlocked || !_releaseState.CanResumeInteraction)
                return false;

            return _editAdmission.TryEnter(out lease);
        }

        /// <summary>
        /// Reopens a document that was safely persisted for navigation and is
        /// now the active frame again. Navigation preparation deliberately
        /// leaves the editor blocked while it is in the back stack; rendering
        /// activation alone must not silently reopen model mutations.
        /// </summary>
        public void ResumeDocumentInteraction()
        {
            if (_resourcesReleased || !_releaseState.CanResumeInteraction)
                return;

            // Navigation uses the same final-generation close state as tab
            // closing. Reopen both state machines when the editor becomes
            // active again; otherwise the coordinator would keep
            // `_closeCompleted` and silently discard the first edit after
            // returning through the frame journal.
            _documentSaveCoordinator.CancelCloseRequest();
            _editAdmission.CancelClose();
            SetDocumentInteractionBlocked(false);
            EnsureAutoSaveTimer();
        }

        private async Task RenderVisibleWorkingSetAsync()
        {
            if (!_isHostActive || _resourcesReleased || _pageControls.Count == 0)
                return;

            _scrollReRenderCts?.Cancel();
            _scrollReRenderCts?.Dispose();
            _scrollReRenderCts = new CancellationTokenSource();
            var token = _scrollReRenderCts.Token;

            try
            {
                var visiblePages = GetVisiblePageControls();
                foreach (var page in visiblePages.Where(page => !_pagesInitiallyRendered.Contains(page.PageIndex)))
                    await RenderPageInitialAsync(page, token);

                if (_lastRenderedDpiScale > 1.0)
                {
                    var zoomPages = visiblePages
                        .Where(page => !_pagesRenderedAtScale.Contains(page.PageIndex))
                        .ToList();
                    if (zoomPages.Count > 0)
                        await ReRenderPagesAsync(zoomPages, _lastRenderedDpiScale, token);
                }

                TrimPageBitmapWorkingSet(visiblePages);
                SetBitmapScalingMode(visiblePages, BitmapScalingMode.HighQuality);
                QueueAdjacentPagePrerender(visiblePages, token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void CancelRenderWork()
        {
            _zoomRenderDebounceTimer.Stop();
            _scrollRenderDebounceTimer.Stop();
            _reRenderCts?.Cancel();
            _reRenderCts?.Dispose();
            _reRenderCts = null;
            _scrollReRenderCts?.Cancel();
            _scrollReRenderCts?.Dispose();
            _scrollReRenderCts = null;
        }

        /// <summary>
        /// Persists the newest generation before a navigation transition. A
        /// failed save keeps the editor alive and restarts its timer.
        /// </summary>
        public Task<bool> PrepareForNavigationAsync(CancellationToken cancellationToken = default)
        {
            lock (_lifecycleGate)
            {
                if (_resourcesReleased)
                    return Task.FromResult(true);
                if (!_releaseState.CanResumeInteraction)
                    return Task.FromResult(false);
                if (_closePreparationInFlight != null)
                    return Task.FromResult(false);
                if (_navigationPreparationInFlight != null)
                    return _navigationPreparationInFlight;

                var task = PrepareForNavigationCoreAsync(cancellationToken);
                _navigationPreparationInFlight = task;
                if (task.IsCompleted)
                    _navigationPreparationInFlight = null;
                return task;
            }
        }

        private async Task<bool> PrepareForNavigationCoreAsync(CancellationToken cancellationToken)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            bool succeeded = false;
            try
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                // TextBox.TextChanged mutates the live model before the focus
                // event commits its undo action.  Flush that session before
                // the coordinator captures a generation.
                CommitTextEditSession();
                CloseTransientUi("navigation");
                // Sticky-note editing lives in a Popup and therefore is not
                // covered by disabling the EditorPage subtree. The transient
                // contract is Cancel; the compatibility shim is now a no-op.
                CommitStickyNoteEdit();
                await BeginDocumentInteractionBlockAsync(cancellationToken, operationLease).ConfigureAwait(true);
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                // A queued Popup activation can run at the dispatcher barrier
                // after the first flush. Close/cancel once more so a detached
                // Sticky Note editor cannot remain interactive during save.
                CloseTransientUi("navigation barrier");
                CommitStickyNoteEdit();
                _autoSaveTimer?.Stop();

                cancellationToken.ThrowIfCancellationRequested();
                await _documentSaveCoordinator.SaveUntilCleanAsync(
                    generation => SaveCurrentDocumentCoreAsync(generation, operationLease),
                    // Navigation has the same atomic admission requirement
                    // as final close: a queued model callback must either be
                    // retained for a retry or be rejected after the clean
                    // generation is completed. ResumeDocumentInteraction()
                    // reopens this request when the frame becomes active.
                    finalClose: true,
                    cancellationToken).ConfigureAwait(true);
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                SyncDirtyStateMirror();
                succeeded = !_documentSaveCoordinator.IsDirty;
                return succeeded;
            }
            catch (OperationCanceledException ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                SyncDirtyStateMirror();
                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.AutoSaveFailed", ex.Message),
                    "\uE783",
                    3500);
                return false;
            }
            catch (Exception ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                SyncDirtyStateMirror();
                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.AutoSaveFailed", ex.Message),
                    "\uE783",
                    3500);
                return false;
            }
            finally
            {
                if (!succeeded && ValidateDocumentOperationLease(operationLease))
                {
                    _editAdmission.CancelClose();
                    SetDocumentInteractionBlocked(false);
                    EnsureAutoSaveTimer();
                }

                lock (_lifecycleGate)
                {
                    _navigationPreparationInFlight = null;
                }
            }
        }

        /// <summary>
        /// Final close protocol: stop timer, join/coalesce any active save,
        /// retry a generation mismatch, and only report success once the
        /// newest snapshot is persisted. Callers must not release resources
        /// or remove a tab when this returns false.
        /// </summary>
        public Task<bool> PrepareForCloseAsync(CancellationToken cancellationToken = default)
        {
            lock (_lifecycleGate)
            {
                if (_resourcesReleased)
                    return Task.FromResult(true);
                if (!_releaseState.CanResumeInteraction
                    && !_releaseState.IsReleaseInFlight
                    && !_releaseState.HasFailed)
                    return Task.FromResult(false);
                if (_closePreparationInFlight != null)
                    return _closePreparationInFlight;
                if (_navigationPreparationInFlight != null)
                    return Task.FromResult(false);

                var task = PrepareForCloseCoreAsync(cancellationToken);
                _closePreparationInFlight = task;
                if (task.IsCompleted)
                    _closePreparationInFlight = null;
                return task;
            }
        }

        private async Task<bool> PrepareForCloseCoreAsync(CancellationToken cancellationToken)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            bool succeeded = false;
            try
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                CommitTextEditSession();
                CloseTransientUi("release");
                // Popup content does not inherit the page's IsEnabled state;
                // keep the historical compatibility call after cancellation.
                CommitStickyNoteEdit();
                await BeginDocumentInteractionBlockAsync(cancellationToken, operationLease).ConfigureAwait(true);
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                // The input barrier may have delivered a queued activation;
                // close/cancel any Popup created by that callback too.
                CloseTransientUi("release barrier");
                CommitStickyNoteEdit();
                _autoSaveTimer?.Stop();
                await _documentSaveCoordinator.SaveUntilCleanAsync(
                    generation => SaveCurrentDocumentCoreAsync(generation, operationLease),
                    finalClose: true,
                    cancellationToken).ConfigureAwait(true);
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                SyncDirtyStateMirror();
                succeeded = !_documentSaveCoordinator.IsDirty;
                if (succeeded)
                    _editAdmission.CompleteClose();
                return succeeded;
            }
            catch (OperationCanceledException ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                SyncDirtyStateMirror();
                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.AutoSaveFailed", ex.Message),
                    "\uE783",
                    3500);
                return false;
            }
            catch (Exception ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                SyncDirtyStateMirror();
                var mw = GetMainWindow();
                if (mw != null)
                    await DialogService.ShowErrorAsync(
                        mw,
                        LocalizationService.Get("Common.Error"),
                        LocalizationService.Format("Editor.SaveFailed", ex.Message));
                else
                    MessageBox.Show(
                        LocalizationService.Format("Editor.SaveFailed", ex.Message),
                        LocalizationService.Get("Common.Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                return false;
            }
            finally
            {
                if (!succeeded && _releaseState.CanResumeInteraction)
                {
                    if (ValidateDocumentOperationLease(operationLease))
                    {
                        _documentSaveCoordinator.CancelCloseRequest();
                        _editAdmission.CancelClose();
                        SetDocumentInteractionBlocked(false);
                        EnsureAutoSaveTimer();
                    }
                }

                lock (_lifecycleGate)
                {
                    _closePreparationInFlight = null;
                }
            }
        }

        /// <summary>Resume editing after a failed/non-destructive close attempt.</summary>
        public void CancelClosePreparation()
        {
            if (!_releaseState.CanResumeInteraction)
            {
                // A timed-out/failed native release owns the editor until its
                // tracked task settles and a retry succeeds.  Re-entry must
                // not detach/rebind events or admit late model mutations.
                SetDocumentInteractionBlocked(true);
                return;
            }

            _documentSaveCoordinator.CancelCloseRequest();
            _editAdmission.CancelClose();
            SetDocumentInteractionBlocked(false);
            SyncDirtyStateMirror();
            EnsureAutoSaveTimer();
        }

        /// <summary>Final tab-close cleanup for timers, hooks, bitmaps and the native PDF document.</summary>
        public Task<bool> ReleaseResourcesAsync()
        {
            lock (_lifecycleGate)
            {
                if (_resourcesReleased)
                    return Task.FromResult(true);
                if (_releaseResourcesInFlight != null)
                    return _releaseResourcesInFlight;
                if (!_releaseState.TryBeginRelease())
                    return Task.FromResult(false);

                var task = ReleaseResourcesCoreAsync();
                _releaseResourcesInFlight = task;
                if (task.IsCompleted)
                    _releaseResourcesInFlight = null;
                return task;
            }
        }

        private async Task<bool> ReleaseResourcesCoreAsync()
        {
            bool cleanupStarted = false;
            try
            {
                CloseTransientUi("release");
                if (!await PrepareForCloseAsync().ConfigureAwait(true))
                {
                    _releaseState.ResetAfterPreReleaseFailure();
                    CancelClosePreparation();
                    return false;
                }

                _releaseState.MarkCleanupStarted();
                cleanupStarted = true;
                SetHostActive(false);
                CancelActiveLoad();
                CancelRenderWork();

                _thumbnailLoadCts?.Cancel();
                _thumbnailLoadCts?.Dispose();
                _thumbnailLoadCts = null;
                _pdfSearchCts?.Cancel();
                _pdfSearchCts?.Dispose();
                _pdfSearchCts = null;

                _autoSaveTimer?.Stop();
                if (_autoSaveTimer != null)
                    _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
                _autoSaveTimer = null;

                _zoomRenderDebounceTimer.Stop();
                _zoomRenderDebounceTimer.Tick -= ZoomRenderDebounceTimer_Tick;
                _scrollRenderDebounceTimer.Stop();
                _scrollRenderDebounceTimer.Tick -= ScrollRenderDebounceTimer_Tick;

                if (_languageChangedSubscribed)
                {
                    LocalizationService.LanguageChanged -= EditorPage_LanguageChanged;
                    _languageChangedSubscribed = false;
                }

                _penService?.Dispose();
                _penService = null;
                RemoveHorizontalWheelHook();
                PdfScrollViewer.ScrollChanged -= PdfScrollViewer_ScrollChanged;
                DetachAllPageControlEvents();
                DetachToolPopupHandlers();
                DisposeSelectablePdfDocument();
                ClearThumbnailCache();
                await _pdfService.DisposeAsync().ConfigureAwait(true);

                // Mark released only after every resource owner has completed.
                // A failure leaves the editor/tab recoverable for a retry.
                _resourcesReleased = true;
                _releaseState.MarkSucceeded();
                SetDocumentInteractionBlocked(true);
                return true;
            }
            catch
            {
                _resourcesReleased = false;
                if (cleanupStarted)
                {
                    // Keep the editor non-interactive until a later explicit
                    // retry completes.  In particular, a timeout/catch must
                    // not make ActivateTab resume a service whose events or
                    // native owners were only partially released.
                    _releaseState.MarkFailed();
                    SetDocumentInteractionBlocked(true);
                }
                else
                {
                    _releaseState.ResetAfterPreReleaseFailure();
                    CancelClosePreparation();
                }
                throw;
            }
            finally
            {
                lock (_lifecycleGate)
                {
                    _releaseResourcesInFlight = null;
                }
            }
        }

        private void ApplyToolToAllPages(AppSettings settings = null)
        {
            settings ??= _applicationSettings ?? AppSettingsService.Load();

            // Task 15: pen-only mode — keep the toolbar toggle in sync with
            // the persisted setting (also covers startup via the ctor's
            // ActivateTool(None)) and propagate to every page below.
            PenOnlyButton.IsChecked = settings.PenOnlyMode;
            UpdatePenOnlyButtonVisual();

            // Task 24: stroke smoothing level (clamped defensively; Sanitize
            // already bounds it, hand-rolled AppSettings instances may not).
            int smoothingLevel = Math.Clamp(settings.StrokeSmoothing, 0, 3);

            foreach (var page in _pageControls)
            {
                page.PressureEnabled = settings.EnablePressure;
                page.WholeStrokeEraser = settings.WholeStrokeEraser;
                page.InkSimulationEnabled = settings.InkSimulation;
                page.ShapeRecognitionEnabled = settings.ShapeRecognition;
                page.PenOnlyMode = settings.PenOnlyMode;
                page.StrokeSmoothingLevel = smoothingLevel;
                page.SetMode(_currentTool == ToolType.Text);
                page.SetPdfTextSelectionEnabled(_currentTool == ToolType.None || _currentTool == ToolType.TextHighlight);
                page.SetSelectionMode(_currentTool == ToolType.Select);
                page.SetSelectionFilter(_selectionFilter);
                page.SetSelectionShape(_selectionShape);
                page.ShapeMode = _currentTool == ToolType.Shape;
                var atts = page.CopyDefaultDrawingAttributes();

                switch (_currentTool)
                {
                    case ToolType.None:
                        page.SetInputMode(CustomInkInputProcessingMode.None);
                        break;
                    case ToolType.Pen:
                        page.SetInputMode(CustomInkInputProcessingMode.Inking);
                        atts.Color = _penColor;
                        atts.Width = _penSize;
                        atts.Height = _penSize;
                        atts.IsHighlighter = false;
                        page.SetInkAttributes(atts);
                        break;
                    case ToolType.Highlighter:
                        page.SetInputMode(CustomInkInputProcessingMode.Inking);
                        atts.Color = Color.FromArgb(
                            FreehandHighlighterOpacity,
                            _highlighterColor.R,
                            _highlighterColor.G,
                            _highlighterColor.B);
                        atts.Width = _highlighterSize;
                        atts.Height = _highlighterSize;
                        atts.IsHighlighter = true;
                        page.SetInkAttributes(atts);
                        break;
                    case ToolType.HiddenInk:
                        // New masks use a neutral gray cover. Existing masks
                        // loaded from annotations retain their serialized
                        // color; this assignment only configures new input.
                        page.HiddenInkMaskColor = Color.FromRgb(199, 205, 212);
                        page.HiddenInkSize = 28.0;
                        page.HiddenInkRevealDurationMs = HiddenInkRevealState.DefaultRevealDurationMs;
                        atts.Color = page.HiddenInkMaskColor;
                        atts.Width = page.HiddenInkSize;
                        atts.Height = page.HiddenInkSize;
                        atts.IsHighlighter = false;
                        page.SetInkAttributes(atts);
                        page.SetInputMode(CustomInkInputProcessingMode.HiddenInk);
                        break;
                    case ToolType.Eraser:
                        page.SetInputMode(CustomInkInputProcessingMode.Erasing);
                        break;
                    case ToolType.Shape:
                        page.CurrentShape = _shapeKind;
                        page.ShapeColor = _shapeColor;
                        page.ShapeStrokeSize = _shapeSize;
                        page.SetInputMode(CustomInkInputProcessingMode.Shape);
                        break;
                    case ToolType.Laser:
                        // Task 20: pure visual layer on the page control —
                        // no ink attributes, no undo, no dirty flag.
                        page.SetInputMode(CustomInkInputProcessingMode.Laser);
                        break;
                    case ToolType.AreaHighlight:
                        page.AreaHighlightColor = _highlighterColor;
                        page.AreaHighlightOpacity = AreaHighlightFillOpacity;
                        page.SetInputMode(CustomInkInputProcessingMode.AreaHighlight);
                        break;
                    case ToolType.StickyNote:
                        page.SetInputMode(CustomInkInputProcessingMode.None);
                        break;
                    case ToolType.Text:
                        page.SetInputMode(CustomInkInputProcessingMode.None);
                        break;
                    case ToolType.Select:
                        page.SetInputMode(CustomInkInputProcessingMode.None);
                        break;
                }

                page.SetEraserSize(_eraserSize);
            }
        }

        private void UpdatePageNumberIndicator()
        {
            if (PageNumberTextBox == null || PageCountText == null) return;

            if (_pageControls.Count == 0)
            {
                if (!_isPageJumpEditing)
                    SetPageJumpText("1");
                if (PageNumberLabel != null)
                    PageNumberLabel.Text = "1";
                PageCountText.Text = "/ 0";
                if (PreviousPageButton != null)
                    PreviousPageButton.IsEnabled = false;
                if (NextPageButton != null)
                    NextPageButton.IsEnabled = false;
                return;
            }

            int currentPageIndex = GetCurrentPageIndex();
            int currentPageNumber = currentPageIndex + 1;
            if (!_isPageJumpEditing)
                SetPageJumpText(currentPageNumber.ToString());
            if (PageNumberLabel != null)
                PageNumberLabel.Text = currentPageNumber.ToString();
            PageCountText.Text = $"/ {_pageControls.Count}";
            if (PreviousPageButton != null)
                PreviousPageButton.IsEnabled = currentPageIndex > 0;
            if (NextPageButton != null)
                NextPageButton.IsEnabled = currentPageIndex < _pageControls.Count - 1;
            UpdateThumbnailSelection();
            UpdateBookmarkButton();
        }

        private int GetCurrentPageIndex()
        {
            if (_pageControls.Count == 0)
                return 0;

            double viewportHeight = PdfScrollViewer.ViewportHeight;
            if (viewportHeight <= 0)
                return 0;

            double centerOffset = PdfScrollViewer.VerticalOffset + (viewportHeight / 2);
            int currentPageIndex = 0;

            for (int i = 0; i < _pageControls.Count; i++)
            {
                double pageTop = GetScaledPageTop(i);
                if (pageTop > centerOffset)
                    break;

                currentPageIndex = i;
            }

            return currentPageIndex;
        }

        private async void Back_Click(object sender, RoutedEventArgs e)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            bool wasDirty = IsDirty;
            if (!await PrepareForNavigationAsync())
                return;
            if (!ValidateDocumentOperationLease(operationLease))
                return;

            if (wasDirty)
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.AutoSaved"));
            NavigateBackCore();
        }

        private void ClearAllAnnotations()
        {
            foreach (var page in _pageControls)
            {
                page.ClearAllAnnotations();
            }
        }

        private async void SavePdf_Click(object sender, RoutedEventArgs e)
        {
            await SaveAnnotationsToPdfAsync();
        }

        private void VersionHistory_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPdfPath) || !_isHostActive ||
                _resourcesReleased || _documentInteractionBlocked)
                return;

            int menuSessionId = _loadSessionId;
            string menuPath = _currentPdfPath;
            var versions = Services.VersionControlService.GetVersions(_currentPdfPath);
            if (versions.Count == 0)
            {
            GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoVersionHistory"), "\uE946");
                return;
            }

            var menu = new ContextMenu
            {
                MaxHeight = 300
            };
            ScrollViewer.SetVerticalScrollBarVisibility(menu, ScrollBarVisibility.Auto);

            foreach (var vFile in versions)
            {
                var dt = System.IO.File.GetCreationTime(vFile);
                var item = new MenuItem { Header = dt.ToString("yyyy-MM-dd HH:mm:ss") };
                item.Click += async (s, args) =>
                {
                    using var operationLease = CaptureDocumentOperationLease(
                        menuSessionId,
                        menuPath,
                        _pdfService);
                    if (!ValidateDocumentOperationLease(operationLease) ||
                        !TryBeginDocumentEdit(out var editLease))
                        return;

                    using (editLease)
                    try
                    {
                        var data = await Services.VersionControlService.LoadVersionAsync(vFile, operationLease.Token);
                        if (data == null || !ValidateDocumentOperationLease(operationLease))
                            return;

                            // 恢复前先把当前注释快照为新版本（最新），使恢复可逆
                            var current = CollectAnnotations();
                            if (!ValidateDocumentOperationLease(operationLease))
                                return;
                            await Services.VersionControlService.SaveVersionAsync(menuPath, current, operationLease.Token);
                            if (!ValidateDocumentOperationLease(operationLease))
                                return;

                            ClearAllAnnotations();
                            if (!ValidateDocumentOperationLease(operationLease))
                                return;
                            // A restored snapshot is a new document state;
                            // actions from the previous snapshot must not be
                            // able to reinsert its annotations via Ctrl+Z.
                            ClearUndoRedoHistory();
                            if (!ValidateDocumentOperationLease(operationLease))
                                return;
                            _pdfService.ExtractedAnnotations = data;
                            await LoadAnnotationsFromPdfServiceAsync(operationLease);
                            if (!ValidateDocumentOperationLease(operationLease))
                                return;
                            if (!ValidateDocumentOperationLease(operationLease))
                                return;
            GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.RestoredVersion", dt.ToString("g", LocalizationService.CurrentCulture)));
                            if (!ValidateDocumentOperationLease(operationLease))
                                return;
                            MarkDirty();
                    }
                    catch (OperationCanceledException)
                    {
                        // A reload/tab release intentionally cancels old menu
                        // continuations without surfacing an error in the new doc.
                    }
                    catch (Exception ex)
                    {
                        if (!ValidateDocumentOperationLease(operationLease))
                            return;
            GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.VersionLoadFailed"));
                        Console.WriteLine($"[VersionHistory] Error: {ex.Message}");
                    }
                };
                menu.Items.Add(item);
            }

            _transientUiRegistry.Register(menu);
            PopupZOrderHelper.FixContextMenuTopmost(menu);
            menu.PlacementTarget = VersionHistoryButton;
            menu.IsOpen = true;
        }

        private async void PrintPdf_Click(object sender, RoutedEventArgs e)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            await PrintPdfAsync(operationLease);
        }

        private void PdfScrollViewer_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
        }

        private void PdfViewerContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu)
            {
                // Bind the menu instance to the document that was active when
                // it opened.  CloseTransientUi intentionally leaves this tag
                // intact so a deferred routed Click can still be rejected as
                // stale after a load/tab transition.
                menu.Tag = new ContextMenuOperationBinding(null, _loadSessionId, _currentPdfPath);
            }
        }

        private bool TryCapturePdfContextMenuLease(out DocumentOperationLease operationLease)
        {
            operationLease = null;
            if (!_isHostActive || _resourcesReleased || _documentInteractionBlocked)
                return false;
            if (PdfViewerContextMenu?.Tag is not ContextMenuOperationBinding binding)
                return false;

            operationLease = CaptureDocumentOperationLease(
                binding.SessionId,
                binding.FilePath,
                _pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
            {
                operationLease.Dispose();
                operationLease = null;
                return false;
            }

            return true;
        }

        private async void ContextMenu_PrintClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!TryCapturePdfContextMenuLease(out var operationLease))
                return;
            using (operationLease)
                await PrintPdfAsync(operationLease);
        }

        private async void ExportCurrentPagePng1x_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!TryCapturePdfContextMenuLease(out var operationLease))
                return;
            using (operationLease)
                await ExportPngAsync(false, 1.0, operationLease);
        }

        private async void ExportCurrentPagePng2x_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!TryCapturePdfContextMenuLease(out var operationLease))
                return;
            using (operationLease)
                await ExportPngAsync(false, 2.0, operationLease);
        }

        private async void ExportAllPagesPng1x_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!TryCapturePdfContextMenuLease(out var operationLease))
                return;
            using (operationLease)
                await ExportPngAsync(true, 1.0, operationLease);
        }

        private async void ExportAllPagesPng2x_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!TryCapturePdfContextMenuLease(out var operationLease))
                return;
            using (operationLease)
                await ExportPngAsync(true, 2.0, operationLease);
        }

        private async Task ExportPngAsync(
            bool allPages,
            double dpiScale,
            DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(_pdfService);
            if (string.IsNullOrWhiteSpace(_currentPdfPath) || _pageControls.Count == 0 ||
                !ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }

            string filePath = _currentPdfPath;

            try
            {
                string folder = null;
                string singlePath = null;
                string baseName = System.IO.Path.GetFileNameWithoutExtension(_currentPdfPath);
                if (allPages)
                {
                    using var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description = LocalizationService.Get("Editor.PngFolderDescription"),
                        SelectedPath = System.IO.Path.GetDirectoryName(_currentPdfPath) ?? string.Empty
                    };
                    if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return;
                    folder = folderDialog.SelectedPath;
                }
                else
                {
                    var dialog = new SaveFileDialog
                    {
                        Filter = LocalizationService.Get("Editor.PngFileFilter"),
                        FileName = $"{baseName}_page_{GetCurrentPageIndex() + 1}.png",
                        AddExtension = true,
                        DefaultExt = ".png",
                        OverwritePrompt = true
                    };
                    if (dialog.ShowDialog() != true)
                        return;
                    singlePath = dialog.FileName;
                    folder = System.IO.Path.GetDirectoryName(singlePath);
                }

                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                var pages = await BuildPrintablePagesAsync(true, dpiScale, operationLease, filePath);
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                IEnumerable<int> indexes = allPages
                    ? Enumerable.Range(0, pages.Count)
                    : new[] { Math.Max(0, Math.Min(GetCurrentPageIndex(), pages.Count - 1)) };
                foreach (int index in indexes)
                {
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    string outputPath = allPages
                        ? System.IO.Path.Combine(folder, $"{baseName}_page_{index + 1:000}.png")
                        : singlePath;
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(pages[index].Bitmap));
                    using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    encoder.Save(stream);
                }

                if (ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.PngExported", allPages ? pages.Count : 1, dpiScale.ToString("0.#", LocalizationService.CurrentCulture)),
                    "\uE74E",
                    2500);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.PngExportFailed", ex.Message), "\uE783", 3500);
            }
            finally
            {
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private async void InsertPdfPages_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!TryCapturePdfContextMenuLease(out var operationLease) ||
                string.IsNullOrWhiteSpace(_currentPdfPath))
                return;
            string filePath = _currentPdfPath;
            using (operationLease)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                var dialog = new OpenFileDialog { Filter = LocalizationService.Get("Editor.PdfFileFilter"), Multiselect = false };
                if (dialog.ShowDialog() != true)
                    return;

                int sourcePageCount;
                try
                {
                    using var source = PdfiumPdfDocument.Load(dialog.FileName);
                    sourcePageCount = source.PageCount;
                }
                catch (Exception ex)
                {
                    if (ValidateDocumentOperationLease(operationLease))
                        GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.SourcePdfReadFailed", ex.Message), "\uE783", 3500);
                    return;
                }

                if (!TryPromptPageRange(sourcePageCount, out int startPage, out int endPage))
                    return;
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                int insertPageIndex = Math.Max(0, GetCurrentPageIndex());
                await InsertExternalDocumentAsync(() => _pdfService.InsertPdfPagesAsync(
                    filePath, dialog.FileName, insertPageIndex, startPage, endPage),
                    insertPageIndex,
                    endPage - startPage + 1,
                    LocalizationService.Get("Editor.PdfPagesInserted"),
                    operationLease);
            }
        }

        private async void InsertImagePage_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!TryCapturePdfContextMenuLease(out var operationLease) ||
                string.IsNullOrWhiteSpace(_currentPdfPath))
                return;
            string filePath = _currentPdfPath;
            using (operationLease)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                var dialog = new OpenFileDialog { Filter = LocalizationService.Get("Editor.ImageFileFilter"), Multiselect = false };
                if (dialog.ShowDialog() != true)
                    return;
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                int insertPageIndex = Math.Max(0, GetCurrentPageIndex());
                await InsertExternalDocumentAsync(() => _pdfService.InsertImagePageAsync(
                    filePath, dialog.FileName, insertPageIndex),
                    insertPageIndex,
                    1,
                    LocalizationService.Get("Editor.ImagePageInserted"),
                    operationLease);
            }
        }

        private bool TryPromptPageRange(int pageCount, out int startPage, out int endPage)
        {
            startPage = 0;
            endPage = Math.Max(0, pageCount - 1);
            var dialog = new Window
            {
                Title = LocalizationService.Get("Editor.PageRangeTitle"),
                Width = 330,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = GetMainWindow(),
                ResizeMode = ResizeMode.NoResize
            };
            var panel = new StackPanel { Margin = new Thickness(18) };
            var prompt = new TextBlock { Text = LocalizationService.Format("Editor.PageRangePrompt", pageCount) };
            panel.Children.Add(prompt);
            var input = new TextBox { Text = pageCount > 0 ? $"1-{pageCount}" : "1", Margin = new Thickness(0, 10, 0, 10) };
            panel.Children.Add(input);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = LocalizationService.Get("Common.Cancel"), Width = 70, Margin = new Thickness(0, 0, 8, 0) };
            var ok = new Button { Content = LocalizationService.Get("Common.OK"), Width = 70, IsDefault = true };
            EventHandler languageChanged = (_, __) =>
            {
                dialog.Title = LocalizationService.Get("Editor.PageRangeTitle");
                prompt.Text = LocalizationService.Format("Editor.PageRangePrompt", pageCount);
                cancel.Content = LocalizationService.Get("Common.Cancel");
                ok.Content = LocalizationService.Get("Common.OK");
            };
            LocalizationService.LanguageChanged += languageChanged;
            dialog.Closed += (_, __) => LocalizationService.LanguageChanged -= languageChanged;
            cancel.Click += (_, __) => dialog.DialogResult = false;
            ok.Click += (_, __) => dialog.DialogResult = true;
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            if (dialog.ShowDialog() != true)
                return false;

            var parts = input.Text.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!int.TryParse(parts[0], out int first))
                return false;
            int last = parts.Length > 1 && int.TryParse(parts[1], out int parsedLast) ? parsedLast : first;
            if (first < 1 || last < first || last > pageCount)
                return false;
            startPage = first - 1;
            endPage = last - 1;
            return true;
        }

        private async Task InsertExternalDocumentAsync(
            Func<Task> operation,
            int insertPageIndex,
            int insertedPageCount,
            string successMessage,
            DocumentOperationLease operationLease = null)
        {
            byte[] before = null;
            int focusBefore = 0;
            List<PageBookmark> beforeBookmarks = null;
            bool operationMayHaveChangedDocument = false;
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using (editLease)
            {
                if (string.IsNullOrWhiteSpace(_currentPdfPath))
                    return;

                string filePath = _currentPdfPath;
                DocumentOperationLease currentLease = operationLease ?? CaptureDocumentOperationLease(_pdfService);
                try
                {
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    if (_documentSaveCoordinator.IsDirty &&
                        (!await AutoSaveAsync(currentLease) || !ValidateDocumentOperationLease(currentLease)))
                        return;
                    before = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    focusBefore = GetCurrentPageIndex();
                    beforeBookmarks = PageBookmarkService.Load(filePath).ToList();
                    operationMayHaveChangedDocument = true;
                    await operation();
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    byte[] after = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                    if (currentLease == null)
                        return;
                    int focused = Math.Max(0, Math.Min(insertPageIndex, _pageControls.Count - 1));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    JumpToPage(focused);
                    var afterBookmarks = PageBookmarkService.ApplyPageInsert(
                        filePath,
                        insertPageIndex,
                        insertedPageCount).ToList();
                    RefreshBookmarks(_loadSessionId, filePath, currentLease);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    PushUndoAction(new DocumentSnapshotAction(
                        this,
                        before,
                        after,
                        focusBefore,
                        focused,
                        beforeBookmarks,
                        afterBookmarks));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    GetMainWindow()?.ShowToast(successMessage, "\uE710", 2000);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    // A stale import must not roll back or report against the
                    // replacement document. Only the still-live transaction
                    // may restore its before-bytes and sidecar.
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;

                    if (operationMayHaveChangedDocument && before != null)
                    {
                        try
                        {
                            await WriteDocumentBytesAsync(filePath, before, currentLease.Token);
                            if (!ValidateDocumentOperationLease(currentLease))
                                return;
                            PageBookmarkService.Replace(filePath, beforeBookmarks ?? new List<PageBookmark>());
                            currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                            if (currentLease == null)
                                return;
                            if (!ValidateDocumentOperationLease(currentLease))
                                return;
                            JumpToPage(Math.Max(0, Math.Min(focusBefore, _pageControls.Count - 1)));
                            RefreshBookmarks(_loadSessionId, filePath, currentLease);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception rollbackException)
                        {
                            if (ValidateDocumentOperationLease(currentLease))
                                System.Diagnostics.Debug.WriteLine($"[Import] Rollback failed: {rollbackException}");
                        }
                    }
                    if (ValidateDocumentOperationLease(currentLease))
                        GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.ImportFailed", ex.Message), "\uE783", 3500);
                }
                finally
                {
                    currentLease?.Dispose();
                }
            }
        }

        private async void RotateCurrentPage_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            DocumentOperationLease operationLease;
            if (sender is MenuItem)
            {
                if (!TryCapturePdfContextMenuLease(out operationLease))
                    return;
            }
            else
            {
                operationLease = CaptureDocumentOperationLease(_pdfService);
                if (!ValidateDocumentOperationLease(operationLease))
                {
                    operationLease.Dispose();
                    return;
                }
            }

            if (!TryBeginDocumentEdit(out var editLease))
            {
                operationLease.Dispose();
                return;
            }

            using (operationLease)
            using (editLease)
            {
                if (string.IsNullOrWhiteSpace(_currentPdfPath) || _pageControls.Count == 0)
                    return;

                string filePath = _currentPdfPath;
                try
                {
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    if (_documentSaveCoordinator.IsDirty &&
                        (!await AutoSaveAsync(operationLease) || !ValidateDocumentOperationLease(operationLease)))
                        return;
                    int pageIndex = GetCurrentPageIndex();
                    byte[] before = await File.ReadAllBytesAsync(filePath, operationLease.Token);
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    await _pdfService.RotatePageAsync(filePath, pageIndex, 1);
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    byte[] after = await File.ReadAllBytesAsync(filePath, operationLease.Token);
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    var refreshedLease = await ReloadDocumentForOperationAsync(filePath, operationLease);
                    if (refreshedLease == null)
                        return;
                    using (refreshedLease)
                    {
                        if (!ValidateDocumentOperationLease(refreshedLease))
                            return;
                        JumpToPage(pageIndex);
                        PushUndoAction(new DocumentSnapshotAction(this, before, after, pageIndex, pageIndex));
                        if (!ValidateDocumentOperationLease(refreshedLease))
                            return;
                        GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageRotated"), "\uE7AD", 1800);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (ValidateDocumentOperationLease(operationLease))
                        GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.RotateFailed", ex.Message), "\uE783", 3500);
                }
            }
        }

        private async Task PrintPdfAsync(DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(_pdfService);
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
            {
                if (ownsLease)
                    GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }

            string filePath = _currentPdfPath;
            if (!ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
            {
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }
            if (!ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }

            string originalLoadingText = LoadingText.Text;
            ShowLoadingOverlay();
            LoadingText.Text = LocalizationService.Get("Editor.PreparingPrint");

            try
            {
                var pages = await BuildPrintablePagesAsync(
                    includeAnnotations: true,
                    operationLease: operationLease,
                    filePath: filePath);
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                if (pages.Count == 0)
                    throw new InvalidOperationException(LocalizationService.Get("Editor.NoPagesToPrint"));

                var printDocument = CreatePrintDocument(pages, dialog);
                dialog.PrintDocument(printDocument.DocumentPaginator, System.IO.Path.GetFileName(filePath));
                if (ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PrintSent"), "\uE749", 1500);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                var mw = GetMainWindow();
                if (mw != null)
                    await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Editor.PrintFailed", ex.Message));
                else
                    MessageBox.Show(LocalizationService.Format("Editor.PrintFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (ValidateDocumentOperationLease(operationLease))
                {
                    LoadingText.Text = originalLoadingText;
                    HideLoadingOverlay();
                }
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private async Task<IReadOnlyList<PrintablePageImage>> BuildPrintablePagesAsync(
            bool includeAnnotations,
            double dpiScale = 1.0,
            DocumentOperationLease operationLease = null,
            string filePath = null)
        {
            string tempPrintPath = null;
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(_pdfService);
            filePath ??= _currentPdfPath;

            try
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return Array.Empty<PrintablePageImage>();
                string renderPath = filePath;
                if (includeAnnotations)
                {
                    string tempDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Caelum", "Print");
                    Directory.CreateDirectory(tempDirectory);
                    tempPrintPath = System.IO.Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.pdf");
                    PdfAtomicFile.CopyFile(filePath, tempPrintPath);
                    await _pdfService.SaveAnnotationsToPdfAsync(tempPrintPath, CollectAnnotations());
                    if (!ValidateDocumentOperationLease(operationLease))
                        return Array.Empty<PrintablePageImage>();
                    renderPath = tempPrintPath;
                }

                var pages = await Task.Run(() => RenderPrintablePages(renderPath, includeAnnotations, dpiScale));
                if (!ValidateDocumentOperationLease(operationLease))
                    return Array.Empty<PrintablePageImage>();
                return pages;
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<PrintablePageImage>();
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPrintPath) && File.Exists(tempPrintPath))
                {
                    try { File.Delete(tempPrintPath); } catch { }
                }
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private static IReadOnlyList<PrintablePageImage> RenderPrintablePages(string filePath, bool includeAnnotations, double dpiScale = 1.0)
        {
            using var document = PdfiumPdfDocument.Load(filePath);
            var pages = new List<PrintablePageImage>(document.PageCount);
            int renderDpi = Math.Max(72, (int)Math.Round(220 * Math.Max(1.0, dpiScale)));
            var renderFlags = includeAnnotations ? PdfiumViewer.PdfRenderFlags.Annotations : (PdfiumViewer.PdfRenderFlags)0;

            for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
            {
                var pageSize = document.PageSizes[pageIndex];
                int width = Math.Max(1, (int)Math.Ceiling(pageSize.Width * renderDpi / 72.0));
                int height = Math.Max(1, (int)Math.Ceiling(pageSize.Height * renderDpi / 72.0));

                using var gdiBitmap = (System.Drawing.Bitmap)document.Render(pageIndex, width, height, renderDpi, renderDpi, renderFlags);
                var bitmapData = gdiBitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, width, height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    var bitmapSource = BitmapSource.Create(
                        width,
                        height,
                        renderDpi,
                        renderDpi,
                        PixelFormats.Bgra32,
                        null,
                        bitmapData.Scan0,
                        bitmapData.Stride * height,
                        bitmapData.Stride);
                    bitmapSource.Freeze();

                    pages.Add(new PrintablePageImage
                    {
                        Bitmap = bitmapSource,
                        Width = pageSize.Width * 96.0 / 72.0,
                        Height = pageSize.Height * 96.0 / 72.0
                    });
                }
                finally
                {
                    gdiBitmap.UnlockBits(bitmapData);
                }
            }

            return pages;
        }

        private static FixedDocument CreatePrintDocument(IReadOnlyList<PrintablePageImage> pages, PrintDialog printDialog)
        {
            double printableWidth = printDialog.PrintableAreaWidth > 0 ? printDialog.PrintableAreaWidth : 816;
            double printableHeight = printDialog.PrintableAreaHeight > 0 ? printDialog.PrintableAreaHeight : 1056;

            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(printableWidth, printableHeight);

            foreach (var page in pages)
            {
                var fixedPage = new FixedPage
                {
                    Width = printableWidth,
                    Height = printableHeight,
                    Background = Brushes.White
                };

                double scale = Math.Min(printableWidth / page.Width, printableHeight / page.Height);
                double imageWidth = page.Width * scale;
                double imageHeight = page.Height * scale;

                var image = new Image
                {
                    Source = page.Bitmap,
                    Width = imageWidth,
                    Height = imageHeight,
                    Stretch = Stretch.Fill
                };

                FixedPage.SetLeft(image, Math.Max(0, (printableWidth - imageWidth) / 2));
                FixedPage.SetTop(image, Math.Max(0, (printableHeight - imageHeight) / 2));
                fixedPage.Children.Add(image);

                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(fixedPage);
                document.Pages.Add(pageContent);
            }

            return document;
        }

        private async Task<DocumentOperationLease> PromptSaveAsForDraftAsync(
            DocumentOperationLease operationLease)
        {
            if (_hasPromptedForSaveAs || string.IsNullOrWhiteSpace(_currentPdfPath))
                return null;
            if (!ValidateDocumentOperationLease(operationLease))
                return null;

            var initialName = System.IO.Path.GetFileName(_currentPdfPath);
            var dialog = new SaveFileDialog
            {
                Filter = LocalizationService.Get("Home.PdfFilter"),
                Title = LocalizationService.Get("Home.SaveNotebookTitle"),
                FileName = initialName,
                AddExtension = true,
                DefaultExt = ".pdf",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog() != true)
            {
                if (ValidateDocumentOperationLease(operationLease))
                {
                    _hasPromptedForSaveAs = true;
                    _promptSaveAsAfterLoad = false;
                }
                return null;
            }

            if (!ValidateDocumentOperationLease(operationLease))
                return null;

            _hasPromptedForSaveAs = true;
            _promptSaveAsAfterLoad = false;

            var oldPath = _currentPdfPath;
            var newPath = dialog.FileName;
            DocumentOperationLease refreshedLease = null;
            bool leaseHandedOff = false;

            try
            {
                if (_documentSaveCoordinator.IsDirty && !await AutoSaveAsync(operationLease))
                    return null;
                if (!ValidateDocumentOperationLease(operationLease))
                    return null;

                bool samePdfPath = string.Equals(
                    PdfSaveCoordinator.NormalizePath(oldPath),
                    PdfSaveCoordinator.NormalizePath(newPath),
                    StringComparison.OrdinalIgnoreCase);
                if (!samePdfPath)
                {
                    // Save-As reads the old PDF and writes the destination.
                    // Admit both paths in deterministic order so a concurrent
                    // writer cannot change the source while it is copied.
                    await PdfSaveCoordinator.RunExclusiveAsync(
                        new[] { oldPath, newPath },
                        () => Task.Run(() =>
                        {
                            string directory = System.IO.Path.GetDirectoryName(newPath);
                            if (!string.IsNullOrWhiteSpace(directory))
                                Directory.CreateDirectory(directory);
                            PdfAtomicFile.CopyFile(oldPath, newPath);
                        })).ConfigureAwait(true);
                    if (!ValidateDocumentOperationLease(operationLease))
                        return null;
                }

                if (!ValidateDocumentOperationLease(operationLease))
                    return null;
                RecentFilesService.UpdatePath(oldPath, newPath);
                RecentFilesService.AddOrPromote(newPath, _pageControls.Count, File.GetLastWriteTimeUtc(newPath), _pendingLibraryFolderId, true);
                UpdateCurrentPdfPath(newPath);
                _isNotebookDraft = false;
                _documentOperationSession.Begin(_loadSessionId, newPath, _pdfService);
                refreshedLease = CaptureDocumentOperationLease(_loadSessionId, newPath, _pdfService);
                if (!ValidateDocumentOperationLease(refreshedLease))
                    return null;

                GetMainWindow()?.HandleActiveTabFilePathChanged(this, oldPath, newPath);
                if (ValidateDocumentOperationLease(refreshedLease))
                    GetMainWindow()?.ShowToast(LocalizationService.Get("Home.NotebookSaved"), "\uE74E");

                if (!ValidateDocumentOperationLease(refreshedLease))
                    return null;

                if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) &&
                    oldPath.IndexOf(System.IO.Path.Combine("Caelum", "Drafts"), StringComparison.OrdinalIgnoreCase) >= 0 &&
                    File.Exists(oldPath))
                {
                    try
                    {
                        File.Delete(oldPath);
                    }
                    catch
                    {
                    }
                }
                leaseHandedOff = true;
                return refreshedLease;
            }
            catch (Exception ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return null;
                var mw = GetMainWindow();
                if (mw != null)
                    await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Home.CreateNotebookFailed", ex.Message));
                else
                    MessageBox.Show(LocalizationService.Format("Home.CreateNotebookFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            finally
            {
                if (!leaseHandedOff)
                    refreshedLease?.Dispose();
            }
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            var center = new Point(PdfScrollViewer.ViewportWidth / 2, PdfScrollViewer.ViewportHeight / 2);
            double newZoom = Math.Max(ZoomMin, Math.Min(ZoomMax, _zoomLevel + ZoomStep));
            ZoomAroundPoint(newZoom, center);
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            var center = new Point(PdfScrollViewer.ViewportWidth / 2, PdfScrollViewer.ViewportHeight / 2);
            double newZoom = Math.Max(ZoomMin, Math.Min(ZoomMax, _zoomLevel - ZoomStep));
            ZoomAroundPoint(newZoom, center);
        }

        private void ZoomLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Show editable text box with current percentage
            ZoomLabel.Visibility = Visibility.Collapsed;
            ZoomTextBox.Text = $"{(int)Math.Round(_zoomLevel * 100)}";
            ZoomTextBox.Visibility = Visibility.Visible;
            ZoomTextBox.Focus();
            ZoomTextBox.SelectAll();
        }

        private void ZoomTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyZoomFromTextBox();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HideZoomTextBox();
                e.Handled = true;
            }
        }

        private void ZoomTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyZoomFromTextBox();
        }

        private void ApplyZoomFromTextBox()
        {
            var text = ZoomTextBox.Text.Trim().TrimEnd('%');
            if (int.TryParse(text, out int pct) && pct >= (int)(ZoomMin * 100) && pct <= (int)(ZoomMax * 100))
            {
                var center = new Point(PdfScrollViewer.ViewportWidth / 2, PdfScrollViewer.ViewportHeight / 2);
                ZoomAroundPoint(pct / 100.0, center);
            }
            HideZoomTextBox();
        }

        private void HideZoomTextBox()
        {
            ZoomTextBox.Visibility = Visibility.Collapsed;
            ZoomLabel.Visibility = Visibility.Visible;
        }

        private bool _isPageJumpInitializing = true;
        private bool _isPageJumpEditing;
        private bool _suppressPageJumpTextChanged;
        private string _pageJumpOpeningValue = "1";
        private string _pageJumpValidationMessage;

        private void PageNumberTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (PageNumberTextBox == null)
                return;

            _isPageJumpEditing = true;
            _pageJumpOpeningValue = PageNumberTextBox.Text;
            ClearPageJumpValidationMessage();
            PageNumberTextBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (PageNumberTextBox.IsKeyboardFocusWithin)
                    PageNumberTextBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void PageNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Enter/Escape/indicator synchronization writes the field while it
            // remains visible and focused.  Only user edits reopen the session;
            // this makes every subsequent Enter, Escape or Tab deterministic.
            if (!_isPageJumpInitializing && !_suppressPageJumpTextChanged)
                _isPageJumpEditing = true;
        }

        private void PageNumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyPageJumpFromTextBox();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (PageNumberTextBox != null && !string.IsNullOrWhiteSpace(_pageJumpOpeningValue))
                    SetPageJumpText(_pageJumpOpeningValue);
                ClearPageJumpValidationMessage();
                HidePageNumberTextBox();
                e.Handled = true;
            }
        }

        private void PageNumberTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isPageJumpEditing)
                ApplyPageJumpFromTextBox();
        }

        private void ApplyPageJumpFromTextBox()
        {
            if (_pageControls.Count == 0)
            {
                HidePageNumberTextBox();
                return;
            }

            string rawValue = PageNumberTextBox.Text?.Trim() ?? string.Empty;
            if (!int.TryParse(rawValue, out int requestedPage))
            {
                SetPageJumpText((GetCurrentPageIndex() + 1).ToString());
                ShowPageJumpValidationMessage(LocalizationService.Get("Editor.PageJumpInvalid"));
                _isPageJumpEditing = false;
                return;
            }

            int unclampedPage = requestedPage;
            requestedPage = Math.Max(1, Math.Min(_pageControls.Count, requestedPage));
            if (unclampedPage != requestedPage)
            {
                ShowPageJumpValidationMessage(LocalizationService.Format(
                    "Editor.PageJumpOutOfRange", _pageControls.Count));
            }
            else
            {
                ClearPageJumpValidationMessage();
            }

            JumpToPage(requestedPage - 1);
            HidePageNumberTextBox();
        }

        private void HidePageNumberTextBox()
        {
            _isPageJumpEditing = false;
            if (PageNumberTextBox != null && _pageControls.Count > 0)
                SetPageJumpText((GetCurrentPageIndex() + 1).ToString());
            if (PageNumberLabel != null)
                PageNumberLabel.Text = PageNumberTextBox?.Text ?? "0";
        }

        private void SetPageJumpText(string value)
        {
            if (PageNumberTextBox == null)
                return;

            _suppressPageJumpTextChanged = true;
            try
            {
                PageNumberTextBox.Text = value ?? string.Empty;
            }
            finally
            {
                _suppressPageJumpTextChanged = false;
            }
        }

        private void ShowPageJumpValidationMessage(string message)
        {
            _pageJumpValidationMessage = message ?? string.Empty;
            if (PageNumberTextBox == null)
                return;

            ToolTipService.SetToolTip(PageNumberTextBox, _pageJumpValidationMessage);
            AutomationProperties.SetHelpText(PageNumberTextBox, _pageJumpValidationMessage);
            AutomationProperties.SetItemStatus(PageNumberTextBox, _pageJumpValidationMessage);
        }

        private void ClearPageJumpValidationMessage()
        {
            _pageJumpValidationMessage = null;
            if (PageNumberTextBox == null)
                return;

            string label = LocalizationService.Get("Editor.PageJumpTooltip");
            ToolTipService.SetToolTip(PageNumberTextBox, label);
            AutomationProperties.SetName(PageNumberTextBox, label);
            AutomationProperties.SetHelpText(PageNumberTextBox, label);
            AutomationProperties.SetItemStatus(PageNumberTextBox, string.Empty);
        }

        private void JumpToPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _pageControls.Count)
                return;

            CancelSmoothScroll();
            double targetOffset = Math.Max(0, GetScaledPageTop(pageIndex) - 12);
            PdfScrollViewer.ScrollToVerticalOffset(targetOffset);
            PdfScrollViewer.UpdateLayout();
            SyncSmoothScrollState();
            UpdatePageNumberIndicator();
            UpdateThumbnailSelection(forceCenter: true);
            UpdateBookmarkButton();
            UpdateSelectedTextBoxPopupVisibility(forceRefresh: true);
        }

        private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pageControls.Count > 0)
                JumpToPage(Math.Max(0, GetCurrentPageIndex() - 1));
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pageControls.Count > 0)
                JumpToPage(Math.Min(_pageControls.Count - 1, GetCurrentPageIndex() + 1));
        }

        private void UpdateZoomLabel()
        {
            if (ZoomLabel != null)
                ZoomLabel.Text = $"{(int)Math.Round(_zoomLevel * 100)}%";
        }

        private void InitializeTextBoxPopup()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = ThemeService.GetShadowOpacity(), Color = Colors.Black }
            };
            border.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");

            var deleteButton = new Button
            {
                Width = 32,
                Height = 32,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = LocalizationService.Get("Editor.DeleteTooltip"),
                Margin = new Thickness(0)
            };
            _textDeleteButton = deleteButton;
            deleteButton.Template = CreateIconButtonTemplate();
            deleteButton.Content = new Path
            {
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Fill = Brushes.Transparent,
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Data = Geometry.Parse("M5,5 L19,5 M8,5 L8,3 L16,3 L16,5 M7,7 L8,19 L16,19 L17,7")
            };
            ((Path)deleteButton.Content).SetResourceReference(Path.StrokeProperty, "ThemeMarginBrush");
            deleteButton.Click += (s, e) => DeleteSelectedTextBox();

            var sep1 = ThemeDivider(new Border
            {
                Width = 1,
                Height = 18,
                Margin = new Thickness(6, 5, 6, 5),
                VerticalAlignment = VerticalAlignment.Center
            });

            var decreaseFontButton = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = LocalizationService.Get("Editor.SmallerText"),
                Content = CreateTextSizeButtonContent(increase: false)
            };
            _textDecreaseFontButton = decreaseFontButton;
            decreaseFontButton.Template = CreateIconButtonTemplate();
            decreaseFontButton.Click += (s, e) => AdjustSelectedTextBoxFontSize(increase: false);

            var increaseFontButton = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = LocalizationService.Get("Editor.BiggerText"),
                Content = CreateTextSizeButtonContent(increase: true)
            };
            _textIncreaseFontButton = increaseFontButton;
            increaseFontButton.Template = CreateIconButtonTemplate();
            increaseFontButton.Click += (s, e) => AdjustSelectedTextBoxFontSize(increase: true);

            var fontButtonGroup = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(2, 0, 2, 0),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        decreaseFontButton,
                        ThemeDivider(new Border
                        {
                            Width = 1,
                            Height = 16,
                            Margin = new Thickness(1, 0, 1, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        }),
                        increaseFontButton
                    }
                }
            };
            fontButtonGroup.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceAltBrush");

            var sep2 = ThemeDivider(new Border
            {
                Width = 1,
                Height = 18,
                Margin = new Thickness(6, 5, 6, 5),
                VerticalAlignment = VerticalAlignment.Center
            });

            _colorIndicator = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(_textColor),
                BorderThickness = new Thickness(1)
            };
            _colorIndicator.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
            var colorButton = new Button
            {
                Content = _colorIndicator,
                Width = 32,
                Height = 32,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };
            _textColorButton = colorButton;
            colorButton.Template = CreateIconButtonTemplate();
            var colorPopup = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            _textColorPopup = colorPopup;
            _transientUiRegistry.Register(colorPopup);
            PopupZOrderHelper.FixPopupTopmost(colorPopup);

            int cols = 12;
            int rows = 8;
            double cellSize = 32;
            var paletteGrid = new Grid { Width = cols * cellSize, Height = rows * cellSize, ClipToBounds = true };
            var selectionIndicator = CreateColorSelectionIndicator(cellSize);
            StackPanel recentRow = null;

            void UpdateTextColorMarkers(Color selected)
            {
                foreach (var swatch in recentRow?.Children.OfType<Button>() ?? Enumerable.Empty<Button>())
                {
                    if (swatch.Content is not Border visual)
                        continue;
                    bool isSelected = swatch.Tag is Color swatchColor && swatchColor == selected;
                    visual.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                    visual.SetResourceReference(
                        Border.BorderBrushProperty,
                        isSelected ? "ThemeFocusBrush" : "ThemeBorderBrush");
                }

                selectionIndicator.Visibility = Visibility.Collapsed;
                foreach (var element in paletteGrid.Children)
                {
                    if (element is Button cell && cell.Tag is Color cellColor && cellColor == selected)
                    {
                        selectionIndicator.Margin = cell.Margin;
                        selectionIndicator.Visibility = Visibility.Visible;
                        break;
                    }
                }
            }

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    Color cellColor;
                    if (row == 0)
                    {
                        byte gray = (byte)(col * 255 / (cols - 1));
                        cellColor = Color.FromRgb(gray, gray, gray);
                    }
                    else
                    {
                        double hue = col * 360.0 / cols;
                        double saturation = row <= rows / 2 ? (double)row / (rows / 2) : 1.0;
                        double val = row <= rows / 2 ? 1.0 : 1.0 - (double)(row - rows / 2) / (rows / 2);
                        cellColor = HsvToColor(hue, saturation, val);
                    }

                    var cellVisual = new Border
                    {
                        Width = cellSize - 6, Height = cellSize - 6,
                        Background = new SolidColorBrush(cellColor),
                        CornerRadius = new CornerRadius(4),
                        BorderThickness = new Thickness(1)
                    };
                    cellVisual.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
                    var cell = new Button
                    {
                        Width = cellSize,
                        Height = cellSize,
                        Padding = new Thickness(3),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(col * cellSize, row * cellSize, 0, 0),
                        Cursor = Cursors.Hand,
                        Focusable = true,
                        Content = cellVisual,
                        Tag = cellColor
                    };
                    ApplyToolbarPopupButtonStyle(cell);
                    string cellLabel = $"#{cellColor.R:X2}{cellColor.G:X2}{cellColor.B:X2}";
                    ToolTipService.SetToolTip(cell, cellLabel);
                    AutomationProperties.SetAutomationId(cell, $"Editor.TextPalette.Color.{row}.{col}");
                    AutomationProperties.SetName(cell, cellLabel);
                    AutomationProperties.SetHelpText(cell, cellLabel);

                    cell.Click += (s, ev) =>
                    {
                        if (s is Button b && b.Tag is Color picked)
                        {
                            UpdateTextColorMarkers(picked);
                            ApplyTextColor(picked);
                        }
                        ev.Handled = true;
                    };

                    paletteGrid.Children.Add(cell);
                }
            }

            foreach (var element in paletteGrid.Children)
            {
                if (element is Button cell && cell.Tag is Color c && c == _textColor)
                {
                    selectionIndicator.Margin = cell.Margin;
                    selectionIndicator.Visibility = Visibility.Visible;
                    break;
                }
            }

            paletteGrid.Children.Add(selectionIndicator);

            // Shared apply path for palette cells and recent-color swatches
            // (Task 14): applies to the selected text box with an undoable
            // TextStyleChangedAction, records the color, closes the popup.
            void ApplyTextColor(Color picked)
            {
                if (_selectedTextBox != null)
                {
                    var beforeBrush = _selectedTextBox.Foreground;
                    var beforeFontSize = _selectedTextBox.FontSize;
                    var page = GetPageByTextContainer(_selectedTextBox.Parent as Grid);

                    _selectedTextBox.Foreground = new SolidColorBrush(picked);
                    _textColor = picked;
                    _colorIndicator.Background = new SolidColorBrush(picked);
                    if (page != null)
                        PushUndoAction(new TextStyleChangedAction(page, _selectedTextBox, beforeFontSize, beforeBrush, _selectedTextBox.FontSize, _selectedTextBox.Foreground));
                    MarkDirty();
                    SaveSetting(s => RecordRecentColor(s.RecentTextColors, picked));
                }
                colorPopup.IsOpen = false;
            }

            // Task 14: "最近 Recent" swatch row above the palette (hidden
            // while empty); repopulated on every popup open.
            var recentSection = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
            recentRow = new StackPanel { Orientation = Orientation.Horizontal };
            _textRecentLabel = ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.Recent"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            recentSection.Children.Add(_textRecentLabel);
            recentSection.Children.Add(recentRow);

            var colorPopupBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = new StackPanel { Margin = new Thickness(16), Children = { recentSection, paletteGrid } },
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = ThemeService.GetShadowOpacity(), Color = Colors.Black }
            };
            colorPopupBorder.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceBrush");
            colorPopupBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
            colorPopup.Child = colorPopupBorder;
            colorPopup.Opened += (s, e) =>
            {
                RefreshRecentColorsRow(
                    recentSection,
                    recentRow,
                    () => AppSettingsService.Load().RecentTextColors,
                    ApplyTextColor,
                    UpdateTextColorMarkers);
                UpdateTextColorMarkers(_textColor);
            };
            colorButton.Click += (s, e) =>
            {
                colorPopup.PlacementTarget = colorButton;
                colorPopup.IsOpen = true;
            };

            panel.Children.Add(deleteButton);
            panel.Children.Add(sep1);
            panel.Children.Add(fontButtonGroup);
            panel.Children.Add(sep2);
            panel.Children.Add(colorButton);

            var formatSeparator = ThemeDivider(new Border
            {
                Width = 1,
                Height = 18,
                Margin = new Thickness(6, 5, 6, 5),
                VerticalAlignment = VerticalAlignment.Center
            });

            _textBoldButton = new ToggleButton
            {
                Content = "B",
                Width = 32,
                Height = 32,
                MinWidth = 32,
                MinHeight = 32,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = LocalizationService.Get("Editor.BoldTooltip")
            };
            _textItalicButton = new ToggleButton
            {
                Content = "I",
                Width = 32,
                Height = 32,
                MinWidth = 32,
                MinHeight = 32,
                FontStyle = FontStyles.Italic,
                Cursor = Cursors.Hand,
                ToolTip = LocalizationService.Get("Editor.ItalicTooltip")
            };
            ApplyToolbarPopupToggleStyle(_textBoldButton);
            ApplyToolbarPopupToggleStyle(_textItalicButton);
            _textBoldButton.Click += (_, __) => ApplySelectedTextFormat(tb =>
                tb.FontWeight = _textBoldButton.IsChecked == true ? FontWeights.Bold : FontWeights.Normal);
            _textItalicButton.Click += (_, __) => ApplySelectedTextFormat(tb =>
                tb.FontStyle = _textItalicButton.IsChecked == true ? FontStyles.Italic : FontStyles.Normal);

            _textFontFamilyCombo = new ComboBox
            {
                Width = 104,
                Height = 32,
                Margin = new Thickness(4, 0, 0, 0),
                ItemsSource = new[] { "Segoe UI", "Arial", "Times New Roman", "Consolas" },
                Style = (Style)Application.Current.FindResource("CompactComboBox"),
                ToolTip = LocalizationService.Get("Editor.FontFamilyTooltip")
            };
            _textFontFamilyCombo.SelectionChanged += (_, __) =>
            {
                if (_textFontFamilyCombo.SelectedItem is string family)
                    ApplySelectedTextFormat(tb => tb.FontFamily = new FontFamily(family));
            };

            _textAlignmentCombo = new ComboBox
            {
                Width = 86,
                Height = 32,
                MinHeight = 32,
                Margin = new Thickness(4, 0, 0, 0),
                ItemsSource = BuildTextAlignmentOptions(),
                DisplayMemberPath = nameof(TextAlignmentOption.Label),
                SelectedValuePath = nameof(TextAlignmentOption.Value),
                SelectedValue = _textAlignment,
                Style = (Style)Application.Current.FindResource("CompactComboBox"),
                ToolTip = LocalizationService.Get("Editor.AlignmentTooltip")
            };
            PopupZOrderHelper.FixComboBoxPopupTopmost(_textFontFamilyCombo);
            PopupZOrderHelper.FixComboBoxPopupTopmost(_textAlignmentCombo);
            _transientUiRegistry.Register(_textFontFamilyCombo);
            _transientUiRegistry.Register(_textAlignmentCombo);
            _textAlignmentCombo.SelectionChanged += (_, __) =>
            {
                if (_isRefreshingTextAlignmentOptions)
                    return;

                if (_textAlignmentCombo.SelectedItem is TextAlignmentOption alignment)
                {
                    _textAlignment = alignment.Value;
                    ApplySelectedTextFormat(tb => tb.TextAlignment = alignment.Value);
                }
            };

            panel.Children.Add(formatSeparator);
            panel.Children.Add(_textBoldButton);
            panel.Children.Add(_textItalicButton);
            panel.Children.Add(_textFontFamilyCombo);
            panel.Children.Add(_textAlignmentCombo);

            _inlineTextBoxToolbar = border;
        }

        private static IReadOnlyList<TextAlignmentOption> BuildTextAlignmentOptions()
        {
            return new[]
            {
                new TextAlignmentOption(TextAlignment.Left, LocalizationService.Get("Editor.AlignmentLeft")),
                new TextAlignmentOption(TextAlignment.Center, LocalizationService.Get("Editor.AlignmentCenter")),
                new TextAlignmentOption(TextAlignment.Right, LocalizationService.Get("Editor.AlignmentRight"))
            };
        }

        private void RefreshTextAlignmentOptions()
        {
            if (_textAlignmentCombo == null)
                return;

            var selectedAlignment = _textAlignmentCombo.SelectedItem is TextAlignmentOption selected
                ? selected.Value
                : _selectedTextBox?.TextAlignment ?? _textAlignment;

            _isRefreshingTextAlignmentOptions = true;
            try
            {
                _textAlignmentCombo.ItemsSource = BuildTextAlignmentOptions();
                _textAlignmentCombo.SelectedValue = selectedAlignment;
            }
            finally
            {
                _isRefreshingTextAlignmentOptions = false;
            }
        }

        // Popup no longer auto-deselects. Deselection happens via:
        // - Clicking on canvas background
        // - Switching tools
        // - Clicking outside in PageControl_BackgroundPointerPressed

        private void DeleteSelectedTextBox()
        {
            if (_selectedTextBox == null) return;
            var tb = _selectedTextBox;
            DeselectTextBox();

            if (tb.Parent is Grid container && container.Parent is Panel panel1)
            {
                panel1.Children.Remove(container);
                var page = GetPageByTextContainer(container);
                if (page != null)
                    PushUndoAction(new TextBoxDeletedAction(page, container));
                MarkDirty();
            }
            else if (tb.Parent is Panel panel2)
            {
                panel2.Children.Remove(tb);
                MarkDirty();
            }
        }

        /// <summary>
        /// Resolves the PdfPageControl whose TextOverlay hosts the given text
        /// container, or null if the container is not attached to a page.
        /// </summary>
        private PdfPageControl GetPageByTextContainer(Grid container)
        {
            if (container?.Parent is Canvas canvas)
                return _pageControls.FirstOrDefault(p => p.TextOverlay == canvas);
            return null;
        }

        private static bool TryGetTextBoxNudge(Key key, out double deltaX, out double deltaY)
        {
            deltaX = 0;
            deltaY = 0;
            switch (key)
            {
                case Key.Left:
                    deltaX = -1;
                    return true;
                case Key.Right:
                    deltaX = 1;
                    return true;
                case Key.Up:
                    deltaY = -1;
                    return true;
                case Key.Down:
                    deltaY = 1;
                    return true;
                default:
                    return false;
            }
        }

        private void NudgeSelectedTextBox(double deltaX, double deltaY)
        {
            var container = _selectedTextBox?.Parent as Grid;
            var page = GetPageByTextContainer(container);
            if (container == null || page == null)
                return;

            var before = GetTextContainerBounds(container);
            double pageWidth = page.TextOverlay.ActualWidth > 0
                ? page.TextOverlay.ActualWidth
                : page.ActualWidth;
            double pageHeight = page.TextOverlay.ActualHeight > 0
                ? page.TextOverlay.ActualHeight
                : page.ActualHeight;
            var after = TextAnnotationGeometry.ClampToPage(
                before with { X = before.X + deltaX, Y = before.Y + deltaY },
                pageWidth,
                pageHeight);

            if (Math.Abs(before.X - after.X) <= 0.5
                && Math.Abs(before.Y - after.Y) <= 0.5)
            {
                return;
            }

            ApplyTextContainerBounds(
                container,
                after,
                autoWidth: IsTextAnnotationAutoWidth(container),
                autoHeight: IsTextAnnotationAutoHeight(container));
            PushUndoAction(new TextBoxMovedAction(
                page,
                container,
                new Point(before.X, before.Y),
                new Point(after.X, after.Y)));
            MarkDirty();
            PositionInlineTextBoxToolbar(container);
        }

        private void BeginTextEditSession(TextBox textBox)
        {
            _textEditSessionTextBox = textBox;
            _textEditSessionOriginalText = textBox.Text;
        }

        private void CommitTextEditSession()
        {
            var textBox = _textEditSessionTextBox;
            if (textBox == null) return;
            _textEditSessionTextBox = null;

            string beforeText = _textEditSessionOriginalText;
            string afterText = textBox.Text;
            if (string.Equals(beforeText, afterText))
                return;

            var page = GetPageByTextContainer(textBox.Parent as Grid);
            if (page == null)
                return;

            PushUndoAction(new TextEditSessionAction(page, textBox, beforeText, afterText));
        }

        private void SelectTextBox(TextBox textBox, bool focusTextBox = true, bool refreshPopupPlacement = false)
        {
            if (textBox == null || _currentTool != ToolType.Text)
                return;

            bool selectionChanged = !ReferenceEquals(_selectedTextBox, textBox);

            if (_selectedTextBox != null && selectionChanged)
            {
                ApplyTextBoxChrome(_selectedTextBox, isSelected: false);
                _selectedTextBox.IsReadOnly = true;
            }

            _selectedTextBox = textBox;
            textBox.IsReadOnly = false;
            ApplyTextBoxChrome(textBox, isSelected: true);
            SyncPopupToSelectedTextBox();
            PositionInlineTextBoxToolbar(textBox.Parent as UIElement ?? textBox);

            if (focusTextBox && !textBox.IsKeyboardFocusWithin)
                textBox.Focus();
        }

        private void SuppressTextAnnotationStylusInteraction(object sender, StylusDownEventArgs e)
        {
            Keyboard.Focus(PdfScrollViewer);
            e.Handled = true;
        }


        private void DeselectTextBox()
        {
            if (_selectedTextBox == null) return;
            ApplyTextBoxChrome(_selectedTextBox, isSelected: false);
            _selectedTextBox.IsReadOnly = true;
            _selectedTextBox = null;
            RemoveInlineTextBoxToolbar();
        }

        private void ApplyTextBoxChrome(TextBox textBox, bool isSelected)
        {
            textBox.BorderThickness = new Thickness(0);
            textBox.Background = Brushes.Transparent;

            if (textBox.Parent is Grid container)
            {
                foreach (UIElement child in container.Children)
                {
                    if (child is Border b && !b.IsHitTestVisible && b.Tag is string tag && tag == "chrome")
                    {
                        b.BorderThickness = isSelected ? new Thickness(1.5) : new Thickness(0);
                        b.ClearValue(Border.BackgroundProperty);
                        b.Background = Brushes.Transparent;
                        if (isSelected)
                        {
                            b.SetResourceReference(Border.BorderBrushProperty, "ThemeFocusBrush");
                        }
                        else
                        {
                            b.ClearValue(Border.BorderBrushProperty);
                            b.BorderBrush = Brushes.Transparent;
                        }
                    }
                    else if (child is Border resizeHandle && resizeHandle.Tag is TextResizeHandle)
                    {
                        resizeHandle.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
        }

        private void SyncPopupToSelectedTextBox()
        {
            if (_selectedTextBox == null) return;

            _currentFontSize = _selectedTextBox.FontSize;
            var current = (_selectedTextBox.Foreground as SolidColorBrush)?.Color ?? Colors.Black;
            _colorIndicator.Background = new SolidColorBrush(current);
            _textBoldButton?.SetCurrentValue(ToggleButton.IsCheckedProperty, _selectedTextBox.FontWeight >= FontWeights.Bold);
            _textItalicButton?.SetCurrentValue(ToggleButton.IsCheckedProperty, _selectedTextBox.FontStyle == FontStyles.Italic);
            if (_textFontFamilyCombo != null)
                _textFontFamilyCombo.SelectedItem = _selectedTextBox.FontFamily?.Source ?? "Segoe UI";
            if (_textAlignmentCombo != null)
            {
                _textAlignment = _selectedTextBox.TextAlignment;
                _textAlignmentCombo.SelectedValue = _selectedTextBox.TextAlignment;
            }
        }

        private void ApplySelectedTextFormat(Action<TextBox> apply)
        {
            var textBox = _selectedTextBox;
            if (textBox == null || apply == null)
                return;

            var beforeWeight = textBox.FontWeight;
            var beforeStyle = textBox.FontStyle;
            var beforeFamily = textBox.FontFamily;
            var beforeAlignment = textBox.TextAlignment;

            apply(textBox);

            bool changed = beforeWeight != textBox.FontWeight
                || beforeStyle != textBox.FontStyle
                || !string.Equals(beforeFamily?.Source, textBox.FontFamily?.Source, StringComparison.OrdinalIgnoreCase)
                || beforeAlignment != textBox.TextAlignment;
            if (!changed)
                return;

            var page = GetPageByTextContainer(textBox.Parent as Grid);
            if (page != null)
            {
                PushUndoAction(new TextFormatChangedAction(
                    textBox,
                    beforeWeight,
                    beforeStyle,
                    beforeFamily,
                    beforeAlignment,
                    textBox.FontWeight,
                    textBox.FontStyle,
                    textBox.FontFamily,
                    textBox.TextAlignment));
            }

            _textBold = textBox.FontWeight >= FontWeights.Bold;
            _textItalic = textBox.FontStyle == FontStyles.Italic;
            _textFontFamily = textBox.FontFamily?.Source ?? "Segoe UI";
            _textAlignment = textBox.TextAlignment;
            SyncPopupToSelectedTextBox();
            MarkDirty();
        }

        private void AdjustSelectedTextBoxFontSize(bool increase)
        {
            if (_selectedTextBox == null)
                return;

            double currentSize = _selectedTextBox.FontSize;
            double nextSize = GetSteppedFontSize(currentSize, increase);
            if (Math.Abs(nextSize - currentSize) < 0.01)
                return;

            var beforeBrush = _selectedTextBox.Foreground;
            var page = GetPageByTextContainer(_selectedTextBox.Parent as Grid);

            _selectedTextBox.FontSize = nextSize;
            _currentFontSize = nextSize;
            if (page != null)
                PushUndoAction(new TextStyleChangedAction(page, _selectedTextBox, currentSize, beforeBrush, nextSize, _selectedTextBox.Foreground));
            MarkDirty();
            PositionInlineTextBoxToolbar(_selectedTextBox.Parent as UIElement ?? _selectedTextBox);
            _selectedTextBox.Focus();
        }

        private static double GetSteppedFontSize(double currentSize, bool increase)
        {
            if (TextFontSizeSteps.Length == 0)
                return currentSize;

            if (increase)
            {
                foreach (double size in TextFontSizeSteps)
                {
                    if (size > currentSize + 0.1)
                        return size;
                }

                return TextFontSizeSteps[^1];
            }

            for (int i = TextFontSizeSteps.Length - 1; i >= 0; i--)
            {
                if (TextFontSizeSteps[i] < currentSize - 0.1)
                    return TextFontSizeSteps[i];
            }

            return TextFontSizeSteps[0];
        }

        private static UIElement CreateTextSizeButtonContent(bool increase)
        {
            var sizeGlyph = new TextBlock
            {
                Text = "A",
                FontSize = increase ? 15 : 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            sizeGlyph.SetResourceReference(TextElement.ForegroundProperty, "ThemeTextBrush");

            var directionGlyph = new TextBlock
            {
                Text = increase ? "^" : "v",
                FontSize = 8,
                Margin = new Thickness(1, 0, 0, 0),
                VerticalAlignment = increase ? VerticalAlignment.Top : VerticalAlignment.Bottom
            };
            directionGlyph.SetResourceReference(TextElement.ForegroundProperty, "ThemeSubtleTextBrush");

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    sizeGlyph,
                    directionGlyph
                }
            };
        }

        private void UpdateSelectedTextBoxPopupVisibility(bool forceRefresh)
        {
            if (_selectedTextBox == null)
                return;

            var placementTarget = _selectedTextBox.Parent as UIElement ?? _selectedTextBox;
            if (!IsElementVisibleInPdfViewport(placementTarget))
            {
                if (_inlineTextBoxToolbar != null)
                    _inlineTextBoxToolbar.Visibility = Visibility.Collapsed;
                return;
            }

            PositionInlineTextBoxToolbar(placementTarget);
        }

        private bool IsElementVisibleInPdfViewport(UIElement element)
        {
            if (element == null || PdfScrollViewer == null || !element.IsVisible)
                return false;

            double viewportWidth = PdfScrollViewer.ViewportWidth > 0 ? PdfScrollViewer.ViewportWidth : PdfScrollViewer.ActualWidth;
            double viewportHeight = PdfScrollViewer.ViewportHeight > 0 ? PdfScrollViewer.ViewportHeight : PdfScrollViewer.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0 || element.RenderSize.Width <= 0 || element.RenderSize.Height <= 0)
                return false;

            try
            {
                var bounds = element.TransformToAncestor(PdfScrollViewer)
                    .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));
                var viewportBounds = new Rect(0, 0, viewportWidth, viewportHeight);
                return bounds.IntersectsWith(viewportBounds);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void PositionInlineTextBoxToolbar(UIElement placementTarget)
        {
            if (_inlineTextBoxToolbar == null || _selectedTextBox == null)
                return;

            if (placementTarget is not Grid container)
                return;

            var canvas = container.Parent as Canvas;
            if (canvas == null)
                return;

            if (_toolbarHostPage != null && _toolbarHostPage.TextOverlay != canvas)
                RemoveInlineTextBoxToolbar();

            _toolbarHostPage = _pageControls.FirstOrDefault(p => p.TextOverlay == canvas);
            if (_toolbarHostPage == null)
                return;

            _inlineTextBoxToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double containerLeft = Canvas.GetLeft(container);
            double containerTop = Canvas.GetTop(container);

            double toolbarLeft = containerLeft;
            double toolbarHeight = _inlineTextBoxToolbar.DesiredSize.Height > 0
                ? _inlineTextBoxToolbar.DesiredSize.Height
                : 42;
            double toolbarTop = containerTop - toolbarHeight;

            if (toolbarTop < 0)
                toolbarTop = 0;

            Canvas.SetLeft(_inlineTextBoxToolbar, toolbarLeft);
            Canvas.SetTop(_inlineTextBoxToolbar, toolbarTop);
            Panel.SetZIndex(_inlineTextBoxToolbar, 2000);

            if (_inlineTextBoxToolbar.Parent == null)
                canvas.Children.Add(_inlineTextBoxToolbar);

            _inlineTextBoxToolbar.Visibility = Visibility.Visible;
        }

        private void RemoveInlineTextBoxToolbar()
        {
            if (_inlineTextBoxToolbar == null)
                return;

            _inlineTextBoxToolbar.Visibility = Visibility.Collapsed;

            if (_inlineTextBoxToolbar.Parent is Canvas canvas)
                canvas.Children.Remove(_inlineTextBoxToolbar);

            _toolbarHostPage = null;
        }

        private static void RefreshPopupPlacement(Popup popup)
        {
            if (popup == null || !popup.IsOpen)
                return;

            double horizontalOffset = popup.HorizontalOffset;
            double verticalOffset = popup.VerticalOffset;

            popup.HorizontalOffset = horizontalOffset + 0.001;
            popup.VerticalOffset = verticalOffset + 0.001;
            popup.HorizontalOffset = horizontalOffset;
            popup.VerticalOffset = verticalOffset;
        }

        private void PageControl_TextOverlayPointerPressed(object sender, MouseButtonEventArgs e)
        {
            if (_currentTool != ToolType.Text) return;

            var page = sender as PdfPageControl;
            if (page == null) return;
            var point = e.GetPosition(page.TextOverlay);

            if (_selectedTextBox != null)
            {
                DeselectTextBox();
                return; // Clicking outside simply deselects and stops
            }

            CreateTextBox(page, point, alignToPointer: true);
            e.Handled = true;
        }

        private Grid CreateTextBox(
            PdfPageControl page,
            Point position,
            Color? color = null,
            double? fontSize = null,
            string text = null,
            bool select = true,
            bool alignToPointer = false,
            bool? bold = null,
            bool? italic = null,
            string fontFamily = null,
            TextAlignment? alignment = null,
            double? width = null,
            double? height = null)
        {
            var textPadding = new Thickness(10, 8, 10, 8);
            bool useDefaultSize = text == null && (!width.HasValue || width.Value <= 0) && (!height.HasValue || height.Value <= 0);
            double? initialWidth = width.HasValue && width.Value > 0
                ? width.Value
                : useDefaultSize ? TextAnnotationGeometry.DefaultWidth : null;
            double? initialHeight = height.HasValue && height.Value > 0
                ? height.Value
                : useDefaultSize ? TextAnnotationGeometry.DefaultHeight : null;

            var container = new Grid
            {
                Background = Brushes.Transparent,
                Tag = "text-annotation"
            };
            bool autoWidth = !width.HasValue || width.Value <= 0;
            bool autoHeight = !height.HasValue || height.Value <= 0;
            if (useDefaultSize)
            {
                // Newly created text boxes use the product default rectangle;
                // the legacy automatic-size sentinel is reserved for loaded
                // annotations that explicitly carried zero dimensions.
                autoWidth = false;
                autoHeight = false;
            }
            container.SetValue(TextAnnotationAutoWidthProperty, autoWidth);
            container.SetValue(TextAnnotationAutoHeightProperty, autoHeight);
            if (initialWidth.HasValue || initialHeight.HasValue)
            {
                var initialBounds = TextAnnotationGeometry.Normalize(new TextBoxBounds(
                    position.X,
                    position.Y,
                    initialWidth ?? TextAnnotationGeometry.DefaultWidth,
                    initialHeight ?? TextAnnotationGeometry.DefaultHeight));
                if (initialWidth.HasValue)
                    container.Width = initialBounds.Width;
                if (initialHeight.HasValue)
                    container.Height = initialBounds.Height;
            }

            container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Visual chrome border spanning both columns
            var chrome = new TextAnnotationDragHandleBorder
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = select ? new Thickness(1.5) : new Thickness(0),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Tag = "chrome"
            };
            if (select)
                chrome.SetResourceReference(Border.BorderBrushProperty, "ThemeFocusBrush");
            AutomationProperties.SetAutomationId(chrome, "TextAnnotationMoveBorder");
            AutomationProperties.SetName(chrome, LocalizationService.Get("Editor.MoveTextBox"));

            double availableWidth = page.ActualWidth - Math.Max(0, position.X);
            double maxTextBoxWidth = Math.Max(100, availableWidth - textPadding.Left - textPadding.Right - 40);

            var textBox = new TextBox
            {
                Text = text ?? LocalizationService.Get("Editor.ModeText"),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinWidth = 100,
                MaxWidth = double.IsNaN(container.Width) ? maxTextBoxWidth : double.PositiveInfinity,
                MinHeight = 30,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = fontSize ?? _currentFontSize,
                Foreground = new SolidColorBrush(color ?? _textColor),
                FontWeight = (bold ?? _textBold) ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = (italic ?? _textItalic) ? FontStyles.Italic : FontStyles.Normal,
                FontFamily = new FontFamily(string.IsNullOrWhiteSpace(fontFamily) ? _textFontFamily : fontFamily),
                TextAlignment = alignment ?? _textAlignment,
                IsReadOnly = !select,
                Padding = textPadding,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                CaretBrush = Brushes.Transparent
            };
            textBox.SetResourceReference(TextBox.CaretBrushProperty, "ThemeAccentBrush");

            page.SizeChanged += (s, e) =>
            {
                if (textBox.Parent is Grid containerGrid)
                {
                    if (double.IsNaN(containerGrid.Width))
                    {
                        double newAvailableWidth = page.ActualWidth - Canvas.GetLeft(containerGrid);
                        textBox.MaxWidth = Math.Max(100, newAvailableWidth - textPadding.Left - textPadding.Right - 40);
                    }
                }
            };

            Grid.SetColumn(textBox, 0);

            container.Children.Add(chrome);
            container.Children.Add(textBox);

            var resizeHandleDefinitions = new[]
            {
                (TextResizeHandle.TopLeft, HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE),
                (TextResizeHandle.Top, HorizontalAlignment.Center, VerticalAlignment.Top, Cursors.SizeNS),
                (TextResizeHandle.TopRight, HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW),
                (TextResizeHandle.Left, HorizontalAlignment.Left, VerticalAlignment.Center, Cursors.SizeWE),
                (TextResizeHandle.Right, HorizontalAlignment.Right, VerticalAlignment.Center, Cursors.SizeWE),
                (TextResizeHandle.BottomLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW),
                (TextResizeHandle.Bottom, HorizontalAlignment.Center, VerticalAlignment.Bottom, Cursors.SizeNS),
                (TextResizeHandle.BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE),
            };

            foreach (var definition in resizeHandleDefinitions)
            {
                var resizeHandle = new TextResizeHandleBorder
                {
                    Width = 10,
                    Height = 10,
                    Margin = new Thickness(-5),
                    HorizontalAlignment = definition.Item2,
                    VerticalAlignment = definition.Item3,
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Cursor = definition.Item4,
                    Focusable = true,
                    Visibility = select ? Visibility.Visible : Visibility.Collapsed,
                    Tag = definition.Item1,
                    ToolTip = LocalizationService.Get("Editor.ResizeTextBox")
                };
                resizeHandle.SetResourceReference(Border.BackgroundProperty, "ThemeAccentBrush");
                resizeHandle.SetResourceReference(Border.BorderBrushProperty, "ThemeFocusBrush");
                AutomationProperties.SetAutomationId(
                    resizeHandle,
                    TextAnnotationGeometry.GetResizeHandleAutomationId(definition.Item1));
                AutomationProperties.SetName(
                    resizeHandle,
                    LocalizationService.Get("Editor.ResizeTextBox"));
                KeyboardNavigation.SetIsTabStop(resizeHandle, true);
                Panel.SetZIndex(resizeHandle, 20);
                resizeHandle.MouseLeftButtonDown += TextResizeHandle_MouseLeftButtonDown;
                resizeHandle.MouseMove += TextResizeHandle_MouseMove;
                resizeHandle.MouseLeftButtonUp += TextResizeHandle_MouseLeftButtonUp;
                resizeHandle.LostMouseCapture += TextResizeHandle_LostMouseCapture;
                resizeHandle.KeyDown += TextResizeHandle_KeyDown;
                resizeHandle.StylusDown += TextResizeHandle_StylusDown;
                resizeHandle.StylusMove += TextResizeHandle_StylusMove;
                resizeHandle.StylusUp += TextResizeHandle_StylusUp;
                resizeHandle.LostStylusCapture += TextResizeHandle_LostStylusCapture;
                container.Children.Add(resizeHandle);
            }

            var initialLeft = position.X;
            var initialTop = position.Y;
            if (alignToPointer)
            {
                initialLeft -= textPadding.Left;
                initialTop -= textPadding.Top;
            }

            Canvas.SetLeft(container, Math.Max(0, initialLeft));
            Canvas.SetTop(container, Math.Max(0, initialTop));
            Panel.SetZIndex(container, 1000);

            container.PreviewMouseLeftButtonDown += TextContainerBorder_MouseLeftButtonDown;
            container.PreviewMouseMove += TextContainerBorder_MouseMove;
            container.PreviewMouseLeftButtonUp += TextContainerBorder_MouseLeftButtonUp;
            container.LostMouseCapture += TextContainerBorder_LostMouseCapture;
            container.PreviewStylusDown += TextContainerBorder_StylusDown;
            container.PreviewStylusMove += TextContainerBorder_StylusMove;
            container.PreviewStylusUp += TextContainerBorder_StylusUp;
            container.LostStylusCapture += TextContainerBorder_LostStylusCapture;
            container.QueryCursor += TextContainerBorder_QueryCursor;

            textBox.TextChanged += (s, e) => MarkDirty();
            textBox.PreviewMouseLeftButtonDown += (s, e) =>
            {
                // Let the native TextBox click logic place the caret.
                // We only switch selection/read-only state before WPF handles the click.
                SelectTextBox((TextBox)s, focusTextBox: false);
            };
            textBox.PreviewStylusDown += SuppressTextAnnotationStylusInteraction;
            textBox.GotFocus += (s, e) =>
            {
                BeginTextEditSession((TextBox)s);
                SelectTextBox((TextBox)s);
            };
            textBox.LostFocus += (s, e) => CommitTextEditSession();

            page.TextOverlay.Children.Add(container);

            if (select)
            {
                SelectTextBox(textBox);
                textBox.SelectAll();
                textBox.Focus();
                // Only user-created boxes (select: true) push an undo action here;
                // PasteSelection pushes a batched ItemsAddedAction and loading
                // existing annotations must not touch the undo stack.
                PushUndoAction(new TextBoxAddedAction(page, container));
                MarkDirty();
            }

            return container;
        }

        private static TextBoxBounds GetTextContainerBounds(Grid container)
        {
            if (container == null)
                return new TextBoxBounds(0, 0, TextAnnotationGeometry.DefaultWidth, TextAnnotationGeometry.DefaultHeight);

            double left = Canvas.GetLeft(container);
            double top = Canvas.GetTop(container);
            double width = !double.IsNaN(container.Width) && container.Width > 0
                ? container.Width
                : container.ActualWidth > 0 ? container.ActualWidth : container.RenderSize.Width;
            double height = !double.IsNaN(container.Height) && container.Height > 0
                ? container.Height
                : container.ActualHeight > 0 ? container.ActualHeight : container.RenderSize.Height;

            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            return TextAnnotationGeometry.Normalize(new TextBoxBounds(left, top, width, height));
        }

        private static bool IsTextAnnotationAutoWidth(Grid container)
        {
            return container?.GetValue(TextAnnotationAutoWidthProperty) is bool value && value;
        }

        private static bool IsTextAnnotationAutoHeight(Grid container)
        {
            return container?.GetValue(TextAnnotationAutoHeightProperty) is bool value && value;
        }

        private static double GetPersistedTextWidth(Grid container)
        {
            return IsTextAnnotationAutoWidth(container) ? 0 : GetTextContainerBounds(container).Width;
        }

        private static double GetPersistedTextHeight(Grid container)
        {
            return IsTextAnnotationAutoHeight(container) ? 0 : GetTextContainerBounds(container).Height;
        }

        private static void ApplyTextContainerBounds(
            Grid container,
            TextBoxBounds bounds,
            bool? autoWidth = null,
            bool? autoHeight = null)
        {
            if (container == null)
                return;

            var normalized = TextAnnotationGeometry.Normalize(bounds);
            if (autoWidth.HasValue)
                container.SetValue(TextAnnotationAutoWidthProperty, autoWidth.Value);
            if (autoHeight.HasValue)
                container.SetValue(TextAnnotationAutoHeightProperty, autoHeight.Value);
            Canvas.SetLeft(container, normalized.X);
            Canvas.SetTop(container, normalized.Y);
            container.Width = IsTextAnnotationAutoWidth(container)
                ? double.NaN
                : normalized.Width;
            container.Height = IsTextAnnotationAutoHeight(container)
                ? double.NaN
                : normalized.Height;

            if (container.Children.OfType<TextBox>().FirstOrDefault() is TextBox textBox)
            {
                // The first grid column is star-sized, so the editor fills the
                // resized rectangle while retaining its own padding and wrapping.
                textBox.MaxWidth = double.PositiveInfinity;
                textBox.Width = double.NaN;
                textBox.Height = double.NaN;
            }

            container.InvalidateMeasure();
            container.UpdateLayout();
        }

        private void TextResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentTool != ToolType.Text || sender is not Border handle || handle.Tag is not TextResizeHandle resizeHandle)
                return;

            var container = handle.Parent as Grid;
            var page = GetPageByTextContainer(container);
            if (container == null || page == null)
                return;

            BeginTextResize(handle, container, page, resizeHandle, e.GetPosition(page));
            handle.CaptureMouse();
            e.Handled = true;
        }

        private void TextResizeHandle_KeyDown(object sender, KeyEventArgs e)
        {
            if (_currentTool != ToolType.Text
                || sender is not Border handle
                || handle.Tag is not TextResizeHandle resizeHandle
                || !TryGetTextBoxNudge(e.Key, out double nudgeX, out double nudgeY)
                || (Keyboard.Modifiers != ModifierKeys.None && Keyboard.Modifiers != ModifierKeys.Shift))
            {
                return;
            }

            var container = handle.Parent as Grid;
            var page = GetPageByTextContainer(container);
            if (container == null || page == null)
                return;

            double step = Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1;
            var before = GetTextContainerBounds(container);
            var after = TextAnnotationGeometry.Resize(
                before,
                resizeHandle,
                nudgeX * step,
                nudgeY * step);
            double pageWidth = page.TextOverlay.ActualWidth > 0
                ? page.TextOverlay.ActualWidth
                : page.ActualWidth;
            double pageHeight = page.TextOverlay.ActualHeight > 0
                ? page.TextOverlay.ActualHeight
                : page.ActualHeight;
            after = TextAnnotationGeometry.ClampToPage(after, pageWidth, pageHeight);

            bool geometryChanged = Math.Abs(before.X - after.X) > 0.5
                || Math.Abs(before.Y - after.Y) > 0.5
                || Math.Abs(before.Width - after.Width) > 0.5
                || Math.Abs(before.Height - after.Height) > 0.5;
            if (!geometryChanged)
                return;

            bool beforeAutoWidth = IsTextAnnotationAutoWidth(container);
            bool beforeAutoHeight = IsTextAnnotationAutoHeight(container);
            ApplyTextContainerBounds(container, after, autoWidth: false, autoHeight: false);
            PushUndoAction(new TextBoxResizedAction(
                container,
                before,
                after,
                beforeAutoWidth,
                beforeAutoHeight,
                afterAutoWidth: false,
                afterAutoHeight: false));
            MarkDirty();
            PositionInlineTextBoxToolbar(container);
            e.Handled = true;
        }

        private void TextResizeHandle_StylusDown(object sender, StylusEventArgs e)
        {
            if (_currentTool != ToolType.Text || sender is not Border handle || handle.Tag is not TextResizeHandle resizeHandle)
                return;

            var container = handle.Parent as Grid;
            var page = GetPageByTextContainer(container);
            if (container == null || page == null)
                return;

            BeginTextResize(handle, container, page, resizeHandle, e.GetPosition(page));
            handle.CaptureStylus();
            e.Handled = true;
        }

        private void BeginTextResize(
            Border handle,
            Grid container,
            PdfPageControl page,
            TextResizeHandle resizeHandle,
            Point startPoint)
        {
            if (container.Children.OfType<TextBox>().FirstOrDefault() is TextBox textBox)
                SelectTextBox(textBox, focusTextBox: false);

            handle.Focus();

            _resizingTextContainer = container;
            _resizingTextPage = page;
            _textResizeHandle = resizeHandle;
            _textResizeStartPoint = startPoint;
            _textResizeStartBounds = GetTextContainerBounds(container);
            _textResizeStartAutoWidth = IsTextAnnotationAutoWidth(container);
            _textResizeStartAutoHeight = IsTextAnnotationAutoHeight(container);
        }

        private void TextResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_resizingTextContainer == null || _resizingTextPage == null)
                return;

            UpdateTextResize(e.GetPosition(_resizingTextPage));
            e.Handled = true;
        }

        private void TextResizeHandle_StylusMove(object sender, StylusEventArgs e)
        {
            if (_resizingTextContainer == null || _resizingTextPage == null)
                return;

            UpdateTextResize(e.GetPosition(_resizingTextPage));
            e.Handled = true;
        }

        private void UpdateTextResize(Point point)
        {
            if (_resizingTextContainer == null || _resizingTextPage == null)
                return;

            var resized = TextAnnotationGeometry.Resize(
                _textResizeStartBounds,
                _textResizeHandle,
                point.X - _textResizeStartPoint.X,
                point.Y - _textResizeStartPoint.Y);
            double pageWidth = _resizingTextPage.TextOverlay.ActualWidth > 0
                ? _resizingTextPage.TextOverlay.ActualWidth
                : _resizingTextPage.ActualWidth;
            double pageHeight = _resizingTextPage.TextOverlay.ActualHeight > 0
                ? _resizingTextPage.TextOverlay.ActualHeight
                : _resizingTextPage.ActualHeight;
            resized = TextAnnotationGeometry.ClampToPage(resized, pageWidth, pageHeight);
            ApplyTextContainerBounds(_resizingTextContainer, resized, autoWidth: false, autoHeight: false);
        }

        private void TextResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_resizingTextContainer == null)
                return;

            _suppressTextCaptureCancellation = true;
            try
            {
                if (sender is UIElement handle && handle.IsMouseCaptured)
                    handle.ReleaseMouseCapture();

                CompleteTextResize();
            }
            finally
            {
                _suppressTextCaptureCancellation = false;
            }
            e.Handled = true;
        }

        private void TextResizeHandle_StylusUp(object sender, StylusEventArgs e)
        {
            if (_resizingTextContainer == null)
                return;

            _suppressTextCaptureCancellation = true;
            try
            {
                if (sender is UIElement handle && handle.IsStylusCaptured)
                    handle.ReleaseStylusCapture();

                CompleteTextResize();
            }
            finally
            {
                _suppressTextCaptureCancellation = false;
            }
            e.Handled = true;
        }

        private void TextResizeHandle_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!_suppressTextCaptureCancellation)
                CancelTextResize(restoreBounds: true);
        }

        private void TextResizeHandle_LostStylusCapture(object sender, StylusEventArgs e)
        {
            if (!_suppressTextCaptureCancellation)
                CancelTextResize(restoreBounds: true);
        }

        private void CompleteTextResize()
        {
            var resizedContainer = _resizingTextContainer;
            if (resizedContainer == null)
                return;

            var before = _textResizeStartBounds;
            var after = GetTextContainerBounds(resizedContainer);
            bool afterAutoWidth = IsTextAnnotationAutoWidth(resizedContainer);
            bool afterAutoHeight = IsTextAnnotationAutoHeight(resizedContainer);
            _resizingTextContainer = null;
            _resizingTextPage = null;
            _textResizeHandle = default;

            bool geometryChanged = Math.Abs(before.X - after.X) > 0.5
                || Math.Abs(before.Y - after.Y) > 0.5
                || Math.Abs(before.Width - after.Width) > 0.5
                || Math.Abs(before.Height - after.Height) > 0.5;
            bool layoutModeChanged = _textResizeStartAutoWidth != afterAutoWidth
                || _textResizeStartAutoHeight != afterAutoHeight;

            if (!geometryChanged && layoutModeChanged)
            {
                // A click or a sub-pixel pointer jitter must not silently
                // convert an automatic-size annotation into a fixed box.
                ApplyTextContainerBounds(
                    resizedContainer,
                    before,
                    _textResizeStartAutoWidth,
                    _textResizeStartAutoHeight);
            }
            else if (geometryChanged || layoutModeChanged)
            {
                PushUndoAction(new TextBoxResizedAction(
                    resizedContainer,
                    before,
                    after,
                    _textResizeStartAutoWidth,
                    _textResizeStartAutoHeight,
                    afterAutoWidth,
                    afterAutoHeight));
                MarkDirty();
            }
        }

        private void CancelTextResize(bool restoreBounds)
        {
            var resizingContainer = _resizingTextContainer;
            if (resizingContainer == null)
                return;

            _suppressTextCaptureCancellation = true;
            try
            {
                if (restoreBounds)
                    ApplyTextContainerBounds(
                        resizingContainer,
                        _textResizeStartBounds,
                        _textResizeStartAutoWidth,
                        _textResizeStartAutoHeight);

                Mouse.Capture(null);
                Stylus.Capture(null);
            }
            finally
            {
                _suppressTextCaptureCancellation = false;
                _resizingTextContainer = null;
                _resizingTextPage = null;
                _textResizeHandle = default;
                _textResizeStartBounds = default;
            }
        }

        private static bool IsTextContainerBorderGesture(Grid container, Point point, object originalSource)
        {
            return FindAncestor<TextResizeHandleBorder>(originalSource as DependencyObject) == null
                && TextAnnotationGeometry.IsMoveBorderHit(
                    point.X,
                    point.Y,
                    container.ActualWidth,
                    container.ActualHeight);
        }

        private void TextContainerBorder_QueryCursor(object sender, QueryCursorEventArgs e)
        {
            if (sender is Grid container &&
                _currentTool == ToolType.Text &&
                IsTextContainerBorderGesture(container, e.GetPosition(container), e.OriginalSource))
            {
                e.Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        private void TextContainerBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentTool != ToolType.Text ||
                sender is not Grid container ||
                container.Parent is not Canvas canvas ||
                !IsTextContainerBorderGesture(container, e.GetPosition(container), e.OriginalSource))
            {
                return;
            }

            BeginTextBoxDrag(container, e.GetPosition(canvas));
            e.Handled = true;
        }

        private void TextContainerBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedContainer?.Parent is Canvas canvas)
                UpdateTextBoxDrag(e.GetPosition(canvas), () => _draggedContainer.CaptureMouse());
            e.Handled = _isDragging || _dragArmed;
        }

        private void TextContainerBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var container = sender as Grid;
            _suppressTextCaptureCancellation = true;
            bool wasDragging;
            try
            {
                container?.ReleaseMouseCapture();
                wasDragging = CompleteTextBoxDrag();
            }
            finally
            {
                _suppressTextCaptureCancellation = false;
            }
            e.Handled = wasDragging;
        }

        private void TextContainerBorder_StylusDown(object sender, StylusEventArgs e)
        {
            if (_currentTool != ToolType.Text ||
                sender is not Grid container ||
                container.Parent is not Canvas canvas ||
                !IsTextContainerBorderGesture(container, e.GetPosition(container), e.OriginalSource))
                return;

            BeginTextBoxDrag(container, e.GetPosition(canvas));
            container.CaptureStylus();
            e.Handled = true;
        }

        private void TextContainerBorder_StylusMove(object sender, StylusEventArgs e)
        {
            if (_draggedContainer?.Parent is Canvas canvas)
                UpdateTextBoxDrag(e.GetPosition(canvas), () => _draggedContainer.CaptureStylus());
            e.Handled = _isDragging || _dragArmed;
        }

        private void TextContainerBorder_StylusUp(object sender, StylusEventArgs e)
        {
            _suppressTextCaptureCancellation = true;
            bool wasDragging;
            try
            {
                (sender as Grid)?.ReleaseStylusCapture();
                wasDragging = CompleteTextBoxDrag();
            }
            finally
            {
                _suppressTextCaptureCancellation = false;
            }
            e.Handled = wasDragging;
        }

        private void TextContainerBorder_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!_suppressTextCaptureCancellation)
                CancelTextBoxDrag(restoreBounds: true);
        }

        private void TextContainerBorder_LostStylusCapture(object sender, StylusEventArgs e)
        {
            if (!_suppressTextCaptureCancellation)
                CancelTextBoxDrag(restoreBounds: true);
        }

        private void BeginTextBoxDrag(Grid container, Point pressPoint)
        {
            if (_currentTool != ToolType.Text || container == null)
                return;

            if (container.Children.OfType<TextBox>().FirstOrDefault() is TextBox textBox)
                SelectTextBox(textBox, focusTextBox: false);

            _dragArmed = true;
            _draggedContainer = container;
            _dragPressPointOnCanvas = pressPoint;
            _draggedContainerPage = GetPageByTextContainer(container);
            _dragStartX = Canvas.GetLeft(container);
            _dragStartY = Canvas.GetTop(container);
        }

        private void UpdateTextBoxDrag(Point currentPoint, Action capture)
        {
            if ((!_isDragging && !_dragArmed) || _draggedContainer == null)
                return;

            if (_dragArmed && !_isDragging)
            {
                var dx = currentPoint.X - _dragPressPointOnCanvas.X;
                var dy = currentPoint.Y - _dragPressPointOnCanvas.Y;
                if (Math.Abs(dx) > 4 || Math.Abs(dy) > 4)
                {
                    _isDragging = true;
                    _dragArmed = false;
                    capture?.Invoke();
                    if (_inlineTextBoxToolbar != null)
                        _inlineTextBoxToolbar.Visibility = Visibility.Collapsed;
                }
            }

            if (_isDragging)
            {
                var dx = currentPoint.X - _dragPressPointOnCanvas.X;
                var dy = currentPoint.Y - _dragPressPointOnCanvas.Y;
                // Task 9: no clamping while dragging — the container must be free to
                // follow the pointer beyond the source page bounds so a cross-page
                // drop can be detected on pointer-up. When no other page is hit,
                // pointer-up clamps the container back into the source page.
                Canvas.SetLeft(_draggedContainer, _dragStartX + dx);
                Canvas.SetTop(_draggedContainer, _dragStartY + dy);
            }
        }

        private bool CompleteTextBoxDrag()
        {
            if (!_isDragging && !_dragArmed)
                return false;

            var wasDragging = _isDragging;
            _dragArmed = false;
            _isDragging = false;

            if (_draggedContainer != null)
            {
                var endX = Canvas.GetLeft(_draggedContainer);
                var endY = Canvas.GetTop(_draggedContainer);
                if (Math.Abs(endX - _dragStartX) > 0.5 || Math.Abs(endY - _dragStartY) > 0.5)
                {
                    var sourcePage = _draggedContainerPage ?? GetPageByTextContainer(_draggedContainer);
                    var targetPage = sourcePage != null
                        ? FindPageAtContainerPoint(sourcePage, new Point(
                            endX + _draggedContainer.ActualWidth / 2,
                            endY + _draggedContainer.ActualHeight / 2))
                        : null;

                    if (sourcePage != null && targetPage != null && targetPage != sourcePage)
                    {
                        // Task 9: cross-page drop — reuse the selection cross-page
                        // mechanism with a single text container and no strokes.
                        // adjust = -targetOriginInSourcePage so that after the
                        // quiet re-parent the container lands at the same visual
                        // position inside the target page.
                        Point targetOriginInSource = targetPage.TranslatePoint(new Point(0, 0), sourcePage);
                        var moveAction = new SelectionCrossPageMoveAction(
                            sourcePage, targetPage,
                            endX - _dragStartX, endY - _dragStartY,
                            -targetOriginInSource.X, -targetOriginInSource.Y,
                            new List<StrokePlacement>(),
                            new List<System.Windows.Controls.Grid> { _draggedContainer });

                        if (sourcePage.HasSelection && sourcePage.SelectedTextContainers.Contains(_draggedContainer))
                            sourcePage.ClearSelection();

                        if (moveAction.ExecuteInitialTransfer())
                            PushUndoAction(moveAction);
                    }
                    else
                    {
                        // Same page, or a miss into a gap between pages / beyond the
                        // document: clamp back into the source page bounds (previous
                        // in-drag clamp behaviour) and record a same-page move.
                        if (sourcePage != null && _draggedContainer.Parent is Canvas canvas)
                        {
                            endX = Math.Max(0, Math.Min(endX, Math.Max(0, canvas.ActualWidth - _draggedContainer.ActualWidth)));
                            endY = Math.Max(0, Math.Min(endY, Math.Max(0, canvas.ActualHeight - _draggedContainer.ActualHeight)));
                            Canvas.SetLeft(_draggedContainer, endX);
                            Canvas.SetTop(_draggedContainer, endY);
                        }
                        if (sourcePage != null)
                            PushUndoAction(new TextBoxMovedAction(sourcePage, _draggedContainer, new Point(_dragStartX, _dragStartY), new Point(endX, endY)));
                    }
                    MarkDirty();
                }
                if (wasDragging)
                {
                    var tb = _draggedContainer.Children.OfType<TextBox>().FirstOrDefault();
                    if (tb != null)
                    {
                        PositionInlineTextBoxToolbar(_draggedContainer);
                        tb.Focus();
                    }
                }
            }
            _draggedContainer = null;
            _draggedContainerPage = null;
            _dragStartX = 0;
            _dragStartY = 0;
            return wasDragging;
        }

        private void CancelTextBoxDrag(bool restoreBounds)
        {
            var container = _draggedContainer;
            bool active = container != null || _dragArmed || _isDragging;
            if (!active)
                return;

            _suppressTextCaptureCancellation = true;
            try
            {
                if (restoreBounds && container != null)
                {
                    Canvas.SetLeft(container, _dragStartX);
                    Canvas.SetTop(container, _dragStartY);
                }

                if (Mouse.Captured is UIElement mouseOwner)
                    mouseOwner.ReleaseMouseCapture();
                if (Stylus.Captured is UIElement stylusOwner)
                    stylusOwner.ReleaseStylusCapture();
            }
            finally
            {
                _suppressTextCaptureCancellation = false;
                _isDragging = false;
                _dragArmed = false;
                _draggedContainer = null;
                _draggedContainerPage = null;
                _dragStartX = 0;
                _dragStartY = 0;
            }
        }

        private void PageControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Task 8.1: ANY left click on ANY page refreshes the paste anchor,
            // regardless of the active tool. PreviewMouseDown tunnels through
            // before the child layers (InkCanvas / SelectionOverlayCanvas /
            // TextBox) can mark the event handled, so pen / select / shape /
            // eraser clicks — which the bubbling BackgroundPointerPressed
            // never sees — all update the anchor too. Coordinates are
            // page-relative.
            if (e.ChangedButton == MouseButton.Left && sender is PdfPageControl page)
            {
                _lastClickedPoint = e.GetPosition(page);
                _lastClickedPage = page;
            }
        }

        private void PageControl_BackgroundPointerPressed(object sender, MouseButtonEventArgs e)
        {
            if (_selectedTextBox != null) DeselectTextBox();

            if (_currentTool == ToolType.StickyNote && sender is PdfPageControl page)
            {
                var note = new StickyNoteAnnotation
                {
                    X = e.GetPosition(page).X,
                    Y = e.GetPosition(page).Y,
                    Text = string.Empty
                };
                var container = page.AddStickyNote(note);
                if (container != null)
                {
                    PushUndoAction(new StickyNoteAddedAction(page, container));
                    OpenStickyNoteEditor(page, container, note);
                }
                e.Handled = true;
            }
        }

        private async Task SaveAnnotationsToPdfAsync()
        {
            if (string.IsNullOrEmpty(_currentPdfPath))
            {
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                return;
            }

            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            try
            {
                // Manual save joins an in-flight autosave, so the user can
                // press Ctrl+S without racing the timer or writing a second
                // annotation snapshot.
                if (await SaveCurrentDocumentWithLeaseAsync(operationLease) &&
                    ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.SavedSuccessfully"));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                var mw = GetMainWindow();
                if (mw != null)
                    await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Editor.SaveFailed", ex.Message));
                else
                    MessageBox.Show(LocalizationService.Format("Editor.SaveFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task<bool> AutoSaveAsync(DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(_pdfService);
            // Do not short-circuit on IsDirty: a successful callback clears
            // the flag before its in-flight task finishes. SaveCurrentDocumentWithLeaseAsync
            // must still join that task during the completion window.
            try
            {
                if (string.IsNullOrEmpty(_currentPdfPath) ||
                    !ValidateDocumentOperationLease(operationLease))
                    return false;
                return await SaveCurrentDocumentWithLeaseAsync(operationLease) &&
                    ValidateDocumentOperationLease(operationLease);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (ValidateDocumentOperationLease(operationLease))
                {
                    System.Diagnostics.Debug.WriteLine($"[AutoSave] Failed: {ex}");
                    GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.AutoSaveFailed", ex.Message), "\uE783", 3500);
                }
                return false;
            }
            finally
            {
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        /// <summary>
        /// Returns the one current save task for this editor. Manual and
        /// automatic callers intentionally share this boundary; a later
        /// timer tick retries if the task observed a newer dirty generation.
        /// </summary>
        private Task<bool> SaveCurrentDocumentAsync() => SaveCurrentDocumentWithLeaseAsync();

        private async Task<bool> SaveCurrentDocumentWithLeaseAsync(DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(_pdfService);
            if (_resourcesReleased || !_releaseState.CanResumeInteraction)
            {
                if (ownsLease)
                    operationLease.Dispose();
                return false;
            }
            if (!ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                return false;
            }

            // Capture/commit the active text session before SaveAsync captures
            // its generation.  Committing inside the persistence callback
            // would make the first save appear stale and cause an unnecessary
            // second PDF/version write.
            CommitTextEditSession();

            Task<DocumentSaveResult> saveTask;
            lock (_saveGate)
            {
                saveTask = _autoSaveInFlight;
                if (saveTask == null)
                {
                    saveTask = _documentSaveCoordinator.SaveAsync(
                        generation => SaveCurrentDocumentCoreAsync(generation, operationLease));
                    _autoSaveInFlight = saveTask;
                }
            }

            try
            {
                var result = await saveTask;
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                SyncDirtyStateMirror();
                return result.Succeeded && result.GenerationIsCurrent;
            }
            finally
            {
                lock (_saveGate)
                {
                    if (ReferenceEquals(_autoSaveInFlight, saveTask))
                        _autoSaveInFlight = null;
                }
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private async Task SaveCurrentDocumentCoreAsync(
            long saveGeneration,
            DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                throw new OperationCanceledException(operationLease.Token);
            }
            // DocumentSaveCoordinator deliberately does not capture a WPF
            // synchronization context. A generation mismatch can therefore
            // retry its persistence callback on a thread-pool continuation;
            // collect the live DependencyObjects only on this page's
            // dispatcher, while the PDF/version I/O remains asynchronous.
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(
                        () => SaveCurrentDocumentCoreAsync(saveGeneration, operationLease),
                        System.Windows.Threading.DispatcherPriority.Normal)
                    .Task
                    .Unwrap()
                    .ConfigureAwait(false);
                if (!ValidateDocumentOperationLease(operationLease))
                {
                    if (ownsLease)
                        operationLease.Dispose();
                    throw new OperationCanceledException(operationLease.Token);
                }
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }

            var annotations = CollectAnnotations();
            if (!ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                throw new OperationCanceledException(operationLease.Token);
            }
            string filePath = _currentPdfPath;

            // The PDF is the source of truth. Only create a history sidecar
            // after the atomic PDF save succeeds, otherwise a failed save
            // would leave a misleading "ghost" version behind.
            await _pdfService.SaveAnnotationsToPdfAsync(_currentPdfPath, annotations);
            if (!ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                throw new OperationCanceledException(operationLease.Token);
            }
            await Services.VersionControlService.SaveVersionAsync(filePath, annotations, operationLease.Token);
            if (!ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                throw new OperationCanceledException(operationLease.Token);
            }
            // DocumentSaveCoordinator compares saveGeneration with the
            // latest generation atomically after this callback returns.
            SyncDirtyStateMirror();
            if (ownsLease)
                operationLease.Dispose();
        }

        private Dictionary<int, PageAnnotation> CollectAnnotations()
        {
            var annotations = new Dictionary<int, PageAnnotation>();
            foreach (var page in _pageControls)
            {
                var pa = new PageAnnotation();
                pa.Strokes = page.GetStrokeData();
                pa.Highlights = page.GetHighlights().ToList();
                pa.HiddenInks = page.GetHiddenInkData().ToList();

                foreach (var element in page.TextOverlay.Children)
                {
                    var containerTb = (element is Grid container) ? container.Children.OfType<TextBox>().FirstOrDefault() : null;
                    if (containerTb != null)
                    {
                        var color = (containerTb.Foreground as SolidColorBrush)?.Color ?? Colors.Black;
                        pa.Texts.Add(new TextAnnotation
                        {
                            Text = containerTb.Text,
                            X = Canvas.GetLeft((Grid)containerTb.Parent),
                            Y = Canvas.GetTop((Grid)containerTb.Parent),
                            R = color.R, G = color.G, B = color.B,
                            FontSize = containerTb.FontSize,
                            Width = GetPersistedTextWidth((Grid)containerTb.Parent),
                            Height = GetPersistedTextHeight((Grid)containerTb.Parent),
                            Bold = containerTb.FontWeight >= FontWeights.Bold,
                            Italic = containerTb.FontStyle == FontStyles.Italic,
                            FontFamily = containerTb.FontFamily?.Source ?? "Segoe UI",
                            Alignment = containerTb.TextAlignment.ToString()
                        });
                    }
                    else if (element is TextBox tb)
                    {
                        var color = (tb.Foreground as SolidColorBrush)?.Color ?? Colors.Black;
                        pa.Texts.Add(new TextAnnotation
                        {
                            Text = tb.Text,
                            X = Canvas.GetLeft(tb),
                            Y = Canvas.GetTop(tb),
                            R = color.R, G = color.G, B = color.B,
                            FontSize = tb.FontSize,
                            Width = GetTextContainerBounds(tb.Parent as Grid).Width,
                            Height = GetTextContainerBounds(tb.Parent as Grid).Height,
                            Bold = tb.FontWeight >= FontWeights.Bold,
                            Italic = tb.FontStyle == FontStyles.Italic,
                            FontFamily = tb.FontFamily?.Source ?? "Segoe UI",
                            Alignment = tb.TextAlignment.ToString()
                        });
                    }
                }

                // Task 19: image annotations (raw encoded bytes + geometry).
                foreach (var imageContainer in page.ImageContainers)
                {
                    var imageData = page.GetImageData(imageContainer);
                    if (imageData == null)
                        continue;

                    pa.Images.Add(new ImageAnnotation
                    {
                        X = Canvas.GetLeft(imageContainer),
                        Y = Canvas.GetTop(imageContainer),
                        Width = imageContainer.ActualWidth > 0 ? imageContainer.ActualWidth : imageContainer.Width,
                        Height = imageContainer.ActualHeight > 0 ? imageContainer.ActualHeight : imageContainer.Height,
                        Format = PdfService.DetectImageFormat(imageData),
                        ImageDataBase64 = Convert.ToBase64String(imageData)
                    });
                }

                // Task 25/26/27: overlay annotations share the image
                // selection/move/resize pipeline. Rebuild their saved models
                // from the live container geometry so a move or scale is
                // preserved even when the user saves before another redraw.
                foreach (var container in page.GetOverlayContainers())
                {
                    var data = page.GetOverlayData(container);
                    double x = Canvas.GetLeft(container);
                    double y = Canvas.GetTop(container);
                    if (double.IsNaN(x)) x = 0;
                    if (double.IsNaN(y)) y = 0;

                    if (data is TextMarkupAnnotation markup)
                    {
                        var copy = new TextMarkupAnnotation
                        {
                            Kind = markup.Kind,
                            X = x,
                            Y = y,
                            R = markup.R,
                            G = markup.G,
                            B = markup.B
                        };
                        double originalWidth = markup.Rects
                            .Where(r => r != null && r.Length >= 4)
                            .Select(r => r[0] + r[2])
                            .DefaultIfEmpty(1)
                            .Max();
                        double originalHeight = markup.Rects
                            .Where(r => r != null && r.Length >= 4)
                            .Select(r => r[1] + r[3])
                            .DefaultIfEmpty(1)
                            .Max();
                        double width = container.ActualWidth > 0 ? container.ActualWidth : container.Width;
                        double height = container.ActualHeight > 0 ? container.ActualHeight : container.Height;
                        double scaleX = originalWidth > 0 ? width / originalWidth : 1;
                        double scaleY = originalHeight > 0 ? height / originalHeight : 1;
                        foreach (var rect in markup.Rects)
                        {
                            if (rect != null && rect.Length >= 4)
                                copy.Rects.Add(new[] { rect[0] * scaleX, rect[1] * scaleY, rect[2] * scaleX, rect[3] * scaleY });
                        }
                        pa.TextMarkups.Add(copy);
                    }
                    else if (data is AreaHighlightAnnotation area)
                    {
                        pa.AreaHighlights.Add(new AreaHighlightAnnotation
                        {
                            X = x,
                            Y = y,
                            Width = container.ActualWidth > 0 ? container.ActualWidth : container.Width,
                            Height = container.ActualHeight > 0 ? container.ActualHeight : container.Height,
                            R = area.R,
                            G = area.G,
                            B = area.B,
                            A = area.A
                        });
                    }
                    else if (data is StickyNoteAnnotation note)
                    {
                        pa.StickyNotes.Add(new StickyNoteAnnotation
                        {
                            Id = note.Id,
                            X = x,
                            Y = y,
                            Text = note.Text,
                            Width = container.ActualWidth > 0 ? container.ActualWidth : container.Width,
                            Height = container.ActualHeight > 0 ? container.ActualHeight : container.Height,
                            R = note.R,
                            G = note.G,
                            B = note.B
                        });
                    }
                }

                if (pa.Strokes.Count > 0 || pa.Texts.Count > 0 || pa.Highlights.Count > 0 || pa.Images.Count > 0
                    || pa.TextMarkups.Count > 0 || pa.AreaHighlights.Count > 0 || pa.StickyNotes.Count > 0
                    || pa.HiddenInks.Count > 0)
                    annotations[page.PageIndex] = pa;
            }
            return annotations;
        }

        private static TextAlignment ParseTextAlignment(string value)
        {
            return Enum.TryParse<TextAlignment>(value, true, out var alignment)
                ? alignment
                : TextAlignment.Left;
        }

        private MainWindow GetMainWindow()
        {
            return Window.GetWindow(this) as MainWindow
                ?? TabDragCoordinator.GetRegisteredWindows().FirstOrDefault(window => window.IsActiveContent(this))
                ?? Application.Current?.MainWindow as MainWindow;
        }

        private async Task LoadAnnotationsFromPdfServiceAsync(DocumentOperationLease operationLease = null)
        {
            if (_pdfService.ExtractedAnnotations == null || _pdfService.ExtractedAnnotations.Count == 0) return;
            if (operationLease != null && !ValidateDocumentOperationLease(operationLease))
                return;

            try
            {
                _isLoadingAnnotations = true;
                foreach (var page in _pageControls)
                {
                    if (operationLease != null && !ValidateDocumentOperationLease(operationLease))
                        return;
                    if (_pdfService.ExtractedAnnotations.TryGetValue(page.PageIndex, out var pa))
                    {
                        foreach (var sa in pa.Strokes)
                        {
                            page.AddStroke(sa);
                        }

                        foreach (var hiddenInk in pa.HiddenInks ?? new List<HiddenInkAnnotation>())
                        {
                            page.AddHiddenInk(hiddenInk);
                        }

                        foreach (var ta in pa.Texts)
                        {
                            var color = Color.FromRgb(ta.R, ta.G, ta.B);
                            CreateTextBox(
                                page,
                                new Point(ta.X, ta.Y),
                                color: color,
                                fontSize: ta.FontSize,
                                text: ta.Text,
                                select: false,
                                width: ta.Width > 0 ? ta.Width : null,
                                height: ta.Height > 0 ? ta.Height : null,
                                bold: ta.Bold,
                                italic: ta.Italic,
                                fontFamily: ta.FontFamily,
                                alignment: ParseTextAlignment(ta.Alignment));
                        }

                        foreach (var hl in pa.Highlights)
                        {
                            page.AddHighlight(hl);
                        }

                        // Task 19: restored image annotations keep their saved
                        // geometry verbatim.
                        foreach (var ia in pa.Images)
                        {
                            if (string.IsNullOrEmpty(ia.ImageDataBase64))
                                continue;

                            byte[] imageBytes;
                            try { imageBytes = Convert.FromBase64String(ia.ImageDataBase64); }
                            catch { continue; }

                            page.AddImage(imageBytes, new Point(ia.X, ia.Y), ia.Width, ia.Height);
                        }

                        foreach (var markup in pa.TextMarkups)
                            page.AddTextMarkup(markup);

                        foreach (var area in pa.AreaHighlights)
                            page.AddAreaHighlight(area);

                        foreach (var note in pa.StickyNotes)
                            page.AddStickyNote(note);
                    }
                }
            }
            catch (Exception ex)
            {
                if (operationLease == null || ValidateDocumentOperationLease(operationLease))
                    System.Diagnostics.Debug.WriteLine($"LoadAnnotationsFromPdfServiceAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // Do not let a stale load clear the guard owned by a newer
                // document's annotation injection.
                if (operationLease == null || ValidateDocumentOperationLease(operationLease))
                    _isLoadingAnnotations = false;
            }
            await Task.CompletedTask;
        }

        private void PageControl_InkMutated(object sender, EventArgs e)
        {
            MarkDirty();
            if (sender is PdfPageControl page)
                InvalidateThumbnailForPage(page.PageIndex);
        }

        private void PageControl_QuietStrokeMutation(object sender, EventArgs e)
        {
            if (sender is PdfPageControl page)
                InvalidateThumbnailForPage(page.PageIndex);
        }

        private void PageControl_HiddenInkCreated(object sender, HiddenInkAnnotation annotation)
        {
            if (_isLoadingAnnotations || annotation == null || sender is not PdfPageControl page)
                return;

            PushUndoAction(new HiddenInkAddedAction(page, annotation));
        }

        private void PageControl_HiddenInkRemoved(object sender, HiddenInkAnnotation annotation)
        {
            if (_isLoadingAnnotations || annotation == null || sender is not PdfPageControl page)
                return;

            PushUndoAction(new HiddenInkRemovedAction(page, annotation));
        }

        private void PageControl_HiddenInksRemoved(
            object sender,
            HiddenInksRemovedEventArgs e)
        {
            if (_isLoadingAnnotations || e?.Annotations == null
                || e.Annotations.Count == 0 || sender is not PdfPageControl page)
                return;

            PushUndoAction(new HiddenInksRemovedAction(page, e.Annotations));
        }

        private void PageControl_ImagesChanged(object sender, EventArgs e)
        {
            if (!_isLoadingAnnotations)
                MarkDirty();
        }

        private void PageControl_AreaHighlightCreated(object sender, Grid container)
        {
            if (sender is PdfPageControl page && container != null)
            {
                PushUndoAction(new ItemsAddedAction(
                    page,
                    new List<System.Windows.Ink.Stroke>(),
                    new List<Grid> { container }));
            }
        }

        private void PageControl_StickyNoteActivated(object sender, Grid container)
        {
            if (sender is not PdfPageControl page || container == null)
                return;

            if (IsLiveStickyContainer(page, container)
                && page.GetOverlayData(container) is StickyNoteAnnotation note)
                OpenStickyNoteEditor(page, container, note);
        }

        private void PageControl_StickyNoteMoved(object sender, StickyNoteMovedEventArgs e)
        {
            if (_isLoadingAnnotations || e?.Container == null || sender is not PdfPageControl page
                || !IsLiveStickyContainer(page, e.Container))
                return;

            PushUndoAction(new StickyNoteMovedAction(
                page,
                e.Container,
                e.OldPosition,
                e.NewPosition));
            MarkDirty();
        }

        private void PageControl_StickyNoteDeleteRequested(object sender, Grid container)
        {
            if (_isLoadingAnnotations || container == null || sender is not PdfPageControl page
                || !IsLiveStickyContainer(page, container)
                || page.GetOverlayData(container) is not StickyNoteAnnotation note)
                return;

            // A marker-level Delete always wins over an open editor bubble. It
            // uses the same reversible action as the explicit popup button.
            if (ReferenceEquals(_stickyNoteEditingContainer, container))
                CancelStickyNoteEdit();

            if (page.RemoveTextContainerQuiet(container))
            {
                PushUndoAction(new StickyNoteDeletedAction(page, container));
                MarkDirty();
            }
        }

        private void PageControl_StickyNoteContextMenuCreated(object sender, ContextMenu menu)
        {
            if (sender is PdfPageControl page && _pageControls.Contains(page))
                _transientUiRegistry.Register(menu);
        }

        private void OpenStickyNoteEditor(PdfPageControl page, Grid container, StickyNoteAnnotation note)
        {
            CancelStickyNoteEdit();

            _stickyNoteEditingPage = page;
            _stickyNoteEditingContainer = container;
            _stickyNoteEditingModel = note;
            _stickyNoteEditingOriginalText = note.Text ?? string.Empty;
            _stickyNoteEditingOriginalPosition = new Point(note.X, note.Y);
            _stickyNoteEditingSessionId = _loadSessionId;
            _stickyNoteEditor = new TextBox
            {
                Text = _stickyNoteEditingOriginalText,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinWidth = 220,
                MaxWidth = 320,
                MinHeight = 76,
                MaxHeight = 180,
                FontSize = 14,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10, 8, 10, 8),
                BorderThickness = new Thickness(1)
            };
            _stickyNoteEditor.SetResourceReference(Control.BackgroundProperty, "ThemeControlBrush");
            _stickyNoteEditor.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush");
            _stickyNoteEditor.SetResourceReference(Control.BorderBrushProperty, "ThemeBorderBrush");

            var saveButton = new Button
            {
                Style = TryFindResource("DialogPrimaryButton") as Style,
                MinWidth = 84,
                MinHeight = 34,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _stickyNoteSaveButton = saveButton;
            ApplyStickyNoteButtonMetadata(saveButton, LocalizationService.Get("Common.Save"), "Sticky.Save");

            var cancelButton = new Button
            {
                Style = TryFindResource("DialogSecondaryButton") as Style,
                MinWidth = 84,
                MinHeight = 34
            };
            _stickyNoteCancelButton = cancelButton;
            ApplyStickyNoteButtonMetadata(cancelButton, LocalizationService.Get("Common.Cancel"), "Sticky.Cancel");

            var deleteStyle = new Style(typeof(Button), TryFindResource("DialogSecondaryButton") as Style);
            deleteStyle.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension("ThemeDangerBrush")));
            var deleteButton = new Button
            {
                Style = deleteStyle,
                MinWidth = 76,
                MinHeight = 34
            };
            _stickyNoteDeleteButton = deleteButton;
            ApplyStickyNoteButtonMetadata(deleteButton, LocalizationService.Get("Editor.DeleteTooltip"), "Sticky.Delete");

            _stickyNoteTitleTextBlock = new TextBlock
            {
                Text = LocalizationService.Get("Editor.StickyNoteTooltip"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            _stickyNoteTitleTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextBrush");
            var gripIcon = new LucideIcon
            {
                Kind = "GripVertical",
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0)
            };
            gripIcon.SetResourceReference(Shape.StrokeProperty, "ThemeSubtleTextBrush");
            var dragHeaderContent = new DockPanel { LastChildFill = true };
            dragHeaderContent.Children.Add(gripIcon);
            dragHeaderContent.Children.Add(_stickyNoteTitleTextBlock);
            _stickyNoteDragHandle = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(-4, -4, -4, 10),
                Cursor = Cursors.SizeAll,
                Child = dragHeaderContent
            };
            AutomationProperties.SetAutomationId(_stickyNoteDragHandle, "Sticky.Editor.DragHandle");
            ApplyStickyNoteDragHandleMetadata();
            _stickyNoteDragHandle.MouseLeftButtonDown += StickyNoteDragHandle_MouseLeftButtonDown;
            _stickyNoteDragHandle.MouseMove += StickyNoteDragHandle_MouseMove;
            _stickyNoteDragHandle.MouseLeftButtonUp += StickyNoteDragHandle_MouseLeftButtonUp;
            _stickyNoteDragHandle.LostMouseCapture += StickyNoteDragHandle_LostMouseCapture;

            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(_stickyNoteDragHandle);
            panel.Children.Add(_stickyNoteEditor);
            var actionRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionRow.Children.Add(deleteButton);
            Grid.SetColumn(deleteButton, 0);
            var confirmActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            confirmActions.Children.Add(cancelButton);
            confirmActions.Children.Add(saveButton);
            Grid.SetColumn(confirmActions, 1);
            actionRow.Children.Add(confirmActions);
            panel.Children.Add(actionRow);

            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(2),
                Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = ThemeService.GetShadowOpacity(),
                    Color = Colors.Black
                }
            };
            border.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");

            _stickyNotePopup = new Popup
            {
                PlacementTarget = container,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = border
            };
            AutomationProperties.SetAutomationId(border, $"Sticky.Editor.{note.Id}");
            PopupZOrderHelper.FixPopupTopmost(_stickyNotePopup);
            _transientUiRegistry.Register(_stickyNotePopup);
            _stickyNotePopup.Closed += StickyNotePopup_Closed;
            saveButton.Click += (_, __) => SaveStickyNoteEdit();
            cancelButton.Click += (_, __) => CancelStickyNoteEdit();
            deleteButton.Click += (_, __) => DeleteStickyNoteEdit();
            _stickyNotePopup.IsOpen = true;
            _stickyNoteEditor.Focus();
            _stickyNoteEditor.SelectAll();
        }

        private void StickyNotePopup_Closed(object sender, EventArgs e)
        {
            // Clicking outside, Escape, deactivation and unloading all follow
            // the explicit Cancel contract; only the Save button commits.
            CancelStickyNoteEdit();
        }

        private void CloseStickyNotePopup(Popup popup)
        {
            if (popup == null)
                return;

            EndStickyNotePopupDrag();
            popup.Closed -= StickyNotePopup_Closed;
            PopupZOrderHelper.UnfixPopupTopmost(popup);
            if (popup.IsOpen)
                popup.IsOpen = false;
        }

        private void ResetStickyNoteEditorState()
        {
            _stickyNotePopup = null;
            _stickyNoteEditor = null;
            _stickyNoteSaveButton = null;
            _stickyNoteCancelButton = null;
            _stickyNoteDeleteButton = null;
            _stickyNoteDragHandle = null;
            _stickyNoteTitleTextBlock = null;
            _isDraggingStickyNotePopup = false;
            _stickyNoteEditingModel = null;
            _stickyNoteEditingContainer = null;
            _stickyNoteEditingPage = null;
            _stickyNoteEditingOriginalText = null;
            _stickyNoteEditingOriginalPosition = default;
            _stickyNoteEditingSessionId = 0;
        }

        private static void ApplyStickyNoteButtonMetadata(Button button, string label, string automationId)
        {
            if (button == null)
                return;

            button.Content = label;
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetAutomationId(button, automationId);
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetHelpText(button, label);
            button.Focusable = true;
            KeyboardNavigation.SetIsTabStop(button, true);
            if (double.IsNaN(button.MinHeight) || button.MinHeight < 32)
                button.MinHeight = 32;
            ApplyToolbarFocusVisualStyle(button);
            button.SetResourceReference(Control.BorderBrushProperty, "ThemeFocusBrush");
            button.SetResourceReference(Control.FocusVisualStyleProperty, "ToolbarFocusVisualStyle");
        }

        private void ApplyStickyNoteDragHandleMetadata()
        {
            if (_stickyNoteDragHandle == null)
                return;

            string label = LocalizationService.Get("Editor.MoveStickyNoteEditor");
            ToolTipService.SetToolTip(_stickyNoteDragHandle, label);
            AutomationProperties.SetName(_stickyNoteDragHandle, label);
            AutomationProperties.SetHelpText(_stickyNoteDragHandle, label);
            if (_stickyNoteTitleTextBlock != null)
                _stickyNoteTitleTextBlock.Text = LocalizationService.Get("Editor.StickyNoteTooltip");
        }

        private void StickyNoteDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _stickyNotePopup == null)
                return;

            BeginStickyNotePopupDrag(Mouse.GetPosition(this));
            _stickyNoteDragHandle?.CaptureMouse();
            e.Handled = true;
        }

        private void StickyNoteDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingStickyNotePopup && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateStickyNotePopupDrag(Mouse.GetPosition(this));
                e.Handled = true;
            }
        }

        private void StickyNoteDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingStickyNotePopup)
            {
                EndStickyNotePopupDrag();
                e.Handled = true;
            }
        }

        private void StickyNoteDragHandle_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndStickyNotePopupDrag();
        }

        private void BeginStickyNotePopupDrag(Point pointerPosition)
        {
            if (_stickyNotePopup == null)
                return;

            _isDraggingStickyNotePopup = true;
            _stickyNotePopupDragStart = pointerPosition;
            _stickyNotePopupDragStartHorizontalOffset = _stickyNotePopup.HorizontalOffset;
            _stickyNotePopupDragStartVerticalOffset = _stickyNotePopup.VerticalOffset;
        }

        private void UpdateStickyNotePopupDrag(Point pointerPosition)
        {
            if (!_isDraggingStickyNotePopup || _stickyNotePopup == null)
                return;

            Vector delta = pointerPosition - _stickyNotePopupDragStart;
            _stickyNotePopup.HorizontalOffset = _stickyNotePopupDragStartHorizontalOffset + delta.X;
            _stickyNotePopup.VerticalOffset = _stickyNotePopupDragStartVerticalOffset + delta.Y;
        }

        private void EndStickyNotePopupDrag()
        {
            if (!_isDraggingStickyNotePopup)
                return;

            _isDraggingStickyNotePopup = false;
            if (_stickyNoteDragHandle?.IsMouseCaptured == true)
                _stickyNoteDragHandle.ReleaseMouseCapture();
        }

        private bool IsLiveStickyNoteEdit(
            PdfPageControl page,
            Grid container,
            StickyNoteAnnotation note,
            int sessionId)
        {
            return sessionId == _loadSessionId && IsLiveStickyContainer(page, container, note);
        }

        private bool IsLiveStickyContainer(
            PdfPageControl page,
            Grid container,
            StickyNoteAnnotation note = null)
        {
            return page != null
                && _pageControls.Contains(page)
                && container != null
                && page.GetOverlayContainers().Contains(container)
                && (note == null || ReferenceEquals(page.GetOverlayData(container), note));
        }

        private void SaveStickyNoteEdit()
        {
            if (_stickyNotePopup == null)
                return;

            var popup = _stickyNotePopup;
            var page = _stickyNoteEditingPage;
            var container = _stickyNoteEditingContainer;
            var note = _stickyNoteEditingModel;
            var sessionId = _stickyNoteEditingSessionId;
            var before = _stickyNoteEditingOriginalText ?? string.Empty;
            var after = _stickyNoteEditor?.Text ?? string.Empty;
            CloseStickyNotePopup(popup);
            if (IsLiveStickyNoteEdit(page, container, note, sessionId)
                && !string.Equals(before, after, StringComparison.Ordinal))
            {
                if (page.SetStickyNoteTextQuiet(container, after))
                {
                    PushUndoAction(new StickyNoteEditAction(page, container, note, before, after));
                    MarkDirty();
                }
            }
            ResetStickyNoteEditorState();
        }

        private void CancelStickyNoteEdit()
        {
            if (_stickyNotePopup == null)
                return;

            var popup = _stickyNotePopup;
            var page = _stickyNoteEditingPage;
            var container = _stickyNoteEditingContainer;
            var note = _stickyNoteEditingModel;
            var sessionId = _stickyNoteEditingSessionId;
            var originalText = _stickyNoteEditingOriginalText ?? string.Empty;
            var originalPosition = _stickyNoteEditingOriginalPosition;
            CloseStickyNotePopup(popup);
            if (IsLiveStickyNoteEdit(page, container, note, sessionId))
            {
                page.SetStickyNoteTextQuiet(container, originalText);
                page.SetStickyNotePositionQuiet(container, originalPosition);
            }
            ResetStickyNoteEditorState();
        }

        private void DeleteStickyNoteEdit()
        {
            if (_stickyNotePopup == null)
                return;

            var popup = _stickyNotePopup;
            var page = _stickyNoteEditingPage;
            var container = _stickyNoteEditingContainer;
            var note = _stickyNoteEditingModel;
            var sessionId = _stickyNoteEditingSessionId;
            CloseStickyNotePopup(popup);
            ResetStickyNoteEditorState();
            if (IsLiveStickyNoteEdit(page, container, note, sessionId)
                && page.RemoveTextContainerQuiet(container))
            {
                PushUndoAction(new StickyNoteDeletedAction(page, container));
                MarkDirty();
            }
        }

        // Kept as a compatibility shim for existing close/save barriers. New
        // transient lifecycle paths use CancelStickyNoteEdit explicitly.
        private void CommitStickyNoteEdit()
        {
            SaveStickyNoteEdit();
        }

        private void PageControl_StrokeCollectedUndoable(object sender, System.Windows.Ink.Stroke stroke)
        {
            if (sender is PdfPageControl page)
                PushUndoAction(new StrokeAddedAction(page, stroke));
        }

        private void PageControl_StrokesErased(object sender, StrokesErasedEventArgs e)
        {
            if (sender is PdfPageControl page)
            {
                var removedPlacements = e.RemovedPlacements.Count > 0
                    ? e.RemovedPlacements.ToList()
                    : e.RemovedStrokes.Select(page.CaptureStrokePlacement).ToList();
                var addedPlacements = e.AddedPlacements.Count > 0
                    ? e.AddedPlacements.ToList()
                    : e.AddedStrokes.Select(page.CaptureStrokePlacement).ToList();
                PushUndoAction(new StrokesErasedAction(
                    page,
                    removedPlacements,
                    addedPlacements));
            }
        }

        private void PageControl_StrokeRecognized(object sender, StrokeRecognizedEventArgs e)
        {
            if (sender is PdfPageControl page)
            {
                // InkCanvas recognition is a fresh user gesture. The page has
                // already replaced its raw/smoothed stroke with the Ideal live
                // stroke, so one Undo must remove the whole gesture instead
                // of exposing the polishing snapshot as an extra history step.
                // Keep the existing snapshot action for any future true
                // replacement event, and only accept a live placement after
                // token + Ideal-side validation.
                if (e.IsFreshStroke
                    && page.TryCaptureCurrentStrokePlacement(
                        e.Token,
                        StrokeReplacementSide.Ideal,
                        out var idealPlacement))
                {
                    PushUndoAction(new StrokeAddedAction(page, idealPlacement));
                    return;
                }

                PushUndoAction(new StrokeReplacedAction(
                    page,
                    e.Token,
                    e.OriginalIndex,
                    e.OriginalSnapshot,
                    e.IdealSnapshot));
            }
        }

        private void MarkDirty()
        {
            _documentSaveCoordinator.MarkDirty();
            SyncDirtyStateMirror();
        }

        private void SyncDirtyStateMirror()
        {
            _dirtyGeneration = _documentSaveCoordinator.DirtyGeneration;
            _isDirty = _documentSaveCoordinator.IsDirty;
        }

        private void NavigateBackCore()
        {
            CloseTransientUi("navigation");
            SetHostActive(false);
            if (NavigationService != null && NavigationService.CanGoBack)
                NavigationService.GoBack();
            else if (NavigationService != null)
                NavigationService.Navigate(new HomePage());
        }

        private void ShowLoadingOverlay()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            UpdateLoadingAnimation();
            UpdatePdfSurfaceVisibility();
        }

        private void HideLoadingOverlay()
        {
            StopLoadingAnimation();
            LoadingOverlay.Visibility = Visibility.Collapsed;
            UpdatePdfSurfaceVisibility();
        }

        private void UpdateLoadingAnimation()
        {
            if (LoadingRotate == null)
                return;

            LoadingRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            LoadingRotate.Angle = 0;
            if (!ThemeService.ShouldAnimate)
                return;

            var duration = ThemeService.GetAnimationDuration(TimeSpan.FromSeconds(1));
            if (duration == TimeSpan.Zero)
                return;

            var animation = new DoubleAnimation(0, 360, duration)
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            LoadingRotate.BeginAnimation(RotateTransform.AngleProperty, animation);
        }

        private void StopLoadingAnimation()
        {
            if (LoadingRotate == null)
                return;

            LoadingRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            LoadingRotate.Angle = 0;
        }

        private void CancelActiveLoad()
        {
            _documentOperationSession.Cancel();
            try { Interlocked.Increment(ref _loadSessionId); _loadCts?.Cancel(); _loadCts?.Dispose(); } catch { }
            _loadCts = null;
        }

        private void DetachAllPageControlEvents()
        {
            foreach (var pageControl in _pageControls)
            {
                pageControl.TextOverlayPointerPressed -= PageControl_TextOverlayPointerPressed;
                pageControl.BackgroundPointerPressed -= PageControl_BackgroundPointerPressed;
                pageControl.RemoveHandler(
                    UIElement.PreviewMouseDownEvent,
                    new MouseButtonEventHandler(PageControl_PreviewMouseDown));
                pageControl.InkMutated -= PageControl_InkMutated;
                pageControl.QuietStrokeMutation -= PageControl_QuietStrokeMutation;
                pageControl.StrokeCollectedUndoable -= PageControl_StrokeCollectedUndoable;
                pageControl.StrokesErased -= PageControl_StrokesErased;
                pageControl.StrokeRecognized -= PageControl_StrokeRecognized;
                pageControl.ImagesChanged -= PageControl_ImagesChanged;
                pageControl.AreaHighlightCreated -= PageControl_AreaHighlightCreated;
                pageControl.StickyNoteActivated -= PageControl_StickyNoteActivated;
                pageControl.StickyNoteMoved -= PageControl_StickyNoteMoved;
                pageControl.StickyNoteDeleteRequested -= PageControl_StickyNoteDeleteRequested;
                pageControl.StickyNoteContextMenuCreated -= PageControl_StickyNoteContextMenuCreated;
                pageControl.HiddenInkCreated -= PageControl_HiddenInkCreated;
                pageControl.HiddenInkRemoved -= PageControl_HiddenInkRemoved;
                pageControl.HiddenInksRemoved -= PageControl_HiddenInksRemoved;
                pageControl.SelectionChanged -= PageControl_SelectionChanged;
                pageControl.SelectionMoveCompleted -= PageControl_SelectionMoveCompleted;
                pageControl.SelectionResizeCompleted -= PageControl_SelectionResizeCompleted;
                pageControl.UnfixTransientUiHooks();
            }
        }

        private static async Task<BitmapImage> CreateBitmapImageAsync(byte[] pngBytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var image = new BitmapImage();
                using (var stream = new MemoryStream(pngBytes))
                {
                    stream.Position = 0;
                    image.BeginInit();
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                }
                return image;
            });
        }

        private static Color HsvToColor(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb(
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255));
        }

        private static ControlTemplate CreateIconButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var rootGrid = new FrameworkElementFactory(typeof(Grid));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(4));
            borderFactory.Name = "Root";

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.Name = "Content";
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);
            rootGrid.AppendChild(borderFactory);

            var focusRingFactory = new FrameworkElementFactory(typeof(Border));
            focusRingFactory.Name = "FocusRing";
            focusRingFactory.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeFocusBrush"));
            focusRingFactory.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            focusRingFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            focusRingFactory.SetValue(Border.MarginProperty, new Thickness(-2));
            focusRingFactory.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            rootGrid.AppendChild(focusRingFactory);

            template.VisualTree = rootGrid;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension("ThemeControlHoverBrush"), "Root"));
            template.Triggers.Add(hoverTrigger);

            var pressTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension("ThemeControlPressedBrush"), "Root"));
            template.Triggers.Add(pressTrigger);

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "FocusRing"));
            template.Triggers.Add(focusTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55, "Root"));
            disabledTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension("ThemeSurfaceAltBrush"), "Root"));
            disabledTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty,
                new DynamicResourceExtension("ThemeDisabledForegroundBrush"), "Content"));
            disabledTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "FocusRing"));
            template.Triggers.Add(disabledTrigger);

            return template;
        }

        private static ControlTemplate CreatePageChromeButtonTemplate(
            string hoverResourceKey,
            string pressedResourceKey)
        {
            var template = new ControlTemplate(typeof(Button));
            var rootGrid = new FrameworkElementFactory(typeof(Grid));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "Root";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);
            rootGrid.AppendChild(borderFactory);

            var focusRingFactory = new FrameworkElementFactory(typeof(Border));
            focusRingFactory.Name = "FocusRing";
            focusRingFactory.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeFocusBrush"));
            focusRingFactory.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            focusRingFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            focusRingFactory.SetValue(Border.MarginProperty, new Thickness(-2));
            focusRingFactory.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            focusRingFactory.SetValue(UIElement.IsHitTestVisibleProperty, false);
            rootGrid.AppendChild(focusRingFactory);

            template.VisualTree = rootGrid;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension(hoverResourceKey), "Root"));
            template.Triggers.Add(hoverTrigger);

            var pressTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension(pressedResourceKey), "Root"));
            template.Triggers.Add(pressTrigger);

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "FocusRing"));
            template.Triggers.Add(focusTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55, "Root"));
            disabledTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "FocusRing"));
            template.Triggers.Add(disabledTrigger);

            return template;
        }
    }
}

namespace Caelum.Pages
{
    /// <summary>
    /// Keeps the outline tree's normal TreeView selection semantics while
    /// supplying a custom item container whose peer can also be invoked.
    /// </summary>
    public sealed class SidebarOutlineTreeView : TreeView
    {
        private Action<int> _pageInvoker;

        internal Action<int> PageInvoker
        {
            get => _pageInvoker;
            set => _pageInvoker = value;
        }

        protected override DependencyObject GetContainerForItemOverride()
        {
            return new SidebarOutlineTreeViewItem { PageInvoker = PageInvoker };
        }

        protected override bool IsItemItsOwnContainerOverride(object item) => item is SidebarOutlineTreeViewItem;
    }

    public sealed class SidebarOutlineTreeViewItem : TreeViewItem
    {
        internal Action InvokeAction { get; set; }
        internal Action<int> PageInvoker { get; set; }

        internal void InvokeFromAutomation()
        {
            if (InvokeAction != null)
            {
                InvokeAction();
                return;
            }

            if (DataContext is EditorPage.SidebarOutlineItem model && PageInvoker != null)
            {
                PageInvoker(model.PageIndex);
                return;
            }

            // A virtualized/recycled item may be invoked before its Loaded
            // metadata callback runs.  Preserve the ordinary TreeView
            // selection route as a final safe fallback in that case.
            IsSelected = true;
        }

        protected override DependencyObject GetContainerForItemOverride()
        {
            return new SidebarOutlineTreeViewItem { PageInvoker = PageInvoker };
        }

        protected override bool IsItemItsOwnContainerOverride(object item) => item is SidebarOutlineTreeViewItem;

        protected override AutomationPeer OnCreateAutomationPeer() => new SidebarOutlineTreeViewItemAutomationPeer(this);
    }

    internal sealed class SidebarOutlineTreeViewItemAutomationPeer : TreeViewItemAutomationPeer, IInvokeProvider
    {
        private new SidebarOutlineTreeViewItem Owner => (SidebarOutlineTreeViewItem)base.Owner;

        public SidebarOutlineTreeViewItemAutomationPeer(SidebarOutlineTreeViewItem owner)
            : base(owner)
        {
        }

        public override object GetPattern(PatternInterface patternInterface)
        {
            return patternInterface == PatternInterface.Invoke
                ? this
                : base.GetPattern(patternInterface);
        }

        void IInvokeProvider.Invoke()
        {
            if (!Owner.IsEnabled)
                throw new ElementNotEnabledException();
            Owner.InvokeFromAutomation();
        }
    }
}
