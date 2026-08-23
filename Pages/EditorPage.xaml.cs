using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Services;
using PdfiumPdfDocument = PdfiumViewer.PdfDocument;

namespace Caelum.Pages
{
    public sealed partial class EditorPage : Page
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
        private bool _isUpdatingToolState;
        private AppSettings _applicationSettings;

        // Task 23: pen preset slots — 3 toolbar circles between the
        // Highlighter and Eraser buttons. Left-click applies the preset
        // (tool + color + size); right-click captures the CURRENT
        // Pen/Highlighter state into the slot. Persisted in
        // AppSettings.PenPresets (defaults filled on first load).
        private const int PenPresetSlotCount = 3;
        private readonly Border[] _presetSlots = new Border[PenPresetSlotCount];
        // Popup internals kept as fields so ApplyPenPreset can resync the
        // size slider + preview line after changing _penSize/_highlighterSize
        // outside the popup's own handlers.
        private Slider _penPopupSizeSlider;
        private Line _penPopupSizePreview;
        private Slider _highlighterPopupSizeSlider;
        private Line _highlighterPopupSizePreview;

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
        // are projected onto it (see PdfPageControl.SnapStrokeToRuler);
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
        private CancellationTokenSource _loadCts;
        private int _loadSessionId;
        private bool _isDirty;
        private long _dirtyGeneration;
        private string _currentPdfPath;
        public string CurrentPdfPath => _currentPdfPath;
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
        private Popup _stickyNotePopup;
        private TextBox _stickyNoteEditor;
        private Button _stickyNoteSaveButton;
        private StickyNoteAnnotation _stickyNoteEditingModel;
        private string _stickyNoteEditingOriginalText;
        private ToggleButton _textBoldButton;
        private ToggleButton _textItalicButton;
        private ComboBox _textFontFamilyCombo;
        private ComboBox _textAlignmentCombo;
        private PdfPageControl _activeSelectionPage;
        private bool _isDelegatingSelection;
        private PdfPageControl _selectionDelegateTarget;

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
            private readonly System.Windows.Ink.Stroke _stroke;
            public StrokeAddedAction(PdfPageControl page, System.Windows.Ink.Stroke stroke) { _page = page; _stroke = stroke; }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.RemoveStrokeQuiet(_stroke);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.AddStrokeQuiet(_stroke);
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
            private readonly List<System.Windows.Ink.Stroke> _removedOriginals;
            private readonly List<System.Windows.Ink.Stroke> _addedFragments;
            public StrokesErasedAction(PdfPageControl page, List<System.Windows.Ink.Stroke> removedOriginals, List<System.Windows.Ink.Stroke> addedFragments)
            {
                _page = page;
                _removedOriginals = removedOriginals;
                _addedFragments = addedFragments;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                foreach (var fragment in _addedFragments) _page.RemoveStrokeQuiet(fragment);
                foreach (var original in _removedOriginals) _page.AddStrokeQuiet(original);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                foreach (var original in _removedOriginals) _page.RemoveStrokeQuiet(original);
                foreach (var fragment in _addedFragments) _page.AddStrokeQuiet(fragment);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// A scribble shape recognition replaced the original freehand
        /// stroke with an ideal shape stroke in place. Undo restores the
        /// freehand original, redo re-applies the ideal shape.
        /// </summary>
        private class StrokeReplacedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly System.Windows.Ink.Stroke _originalStroke;
            private readonly System.Windows.Ink.Stroke _idealStroke;
            public StrokeReplacedAction(PdfPageControl page, System.Windows.Ink.Stroke originalStroke, System.Windows.Ink.Stroke idealStroke)
            {
                _page = page;
                _originalStroke = originalStroke;
                _idealStroke = idealStroke;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                _page.RemoveStrokeQuiet(_idealStroke);
                _page.AddStrokeQuiet(_originalStroke);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _page.RemoveStrokeQuiet(_originalStroke);
                _page.AddStrokeQuiet(_idealStroke);
                return Task.CompletedTask;
            }
        }

        private class ItemsAddedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly List<System.Windows.Ink.Stroke> _strokes;
            private readonly List<System.Windows.Controls.Grid> _containers;

            public ItemsAddedAction(PdfPageControl page, List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
            {
                _page = page;
                _strokes = strokes;
                _containers = containers;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                // The pasted items may currently be selected (paste
                // auto-selects, Task 8.2); drop the selection before removing
                // them so it cannot reference removed strokes/containers.
                if (_page.HasSelection)
                    _page.ClearSelection();
                foreach (var stroke in _strokes) _page.RemoveStrokeQuiet(stroke);
                foreach (var container in _containers) _page.RemoveTextContainerQuiet(container);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                foreach (var stroke in _strokes) _page.AddStrokeQuiet(stroke);
                foreach (var container in _containers) _page.AddTextContainerQuiet(container);
                return Task.CompletedTask;
            }
        }

        private class ItemsRemovedAction : IUndoAction
        {
            private readonly PdfPageControl _page;
            private readonly List<System.Windows.Ink.Stroke> _strokes;
            private readonly List<System.Windows.Controls.Grid> _containers;

            public ItemsRemovedAction(PdfPageControl page, List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
            {
                _page = page;
                _strokes = strokes;
                _containers = containers;
            }
            public bool LeavesDocumentDirty => true;
            public Task UndoAsync()
            {
                foreach (var stroke in _strokes) _page.AddStrokeQuiet(stroke);
                foreach (var container in _containers) _page.AddTextContainerQuiet(container);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                foreach (var stroke in _strokes) _page.RemoveStrokeQuiet(stroke);
                foreach (var container in _containers) _page.RemoveTextContainerQuiet(container);
                return Task.CompletedTask;
            }
        }

        private sealed class StickyNoteEditAction : IUndoAction
        {
            private readonly StickyNoteAnnotation _note;
            private readonly string _before;
            private readonly string _after;

            public StickyNoteEditAction(StickyNoteAnnotation note, string before, string after)
            {
                _note = note;
                _before = before;
                _after = after;
            }

            public bool LeavesDocumentDirty => true;

            public Task UndoAsync()
            {
                _note.Text = _before;
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                _note.Text = _after;
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
            private readonly List<System.Windows.Ink.Stroke> _strokes;
            private readonly List<System.Windows.Controls.Grid> _containers;

            public SelectionCrossPageMoveAction(PdfPageControl sourcePage, PdfPageControl targetPage,
                double deltaX, double deltaY, double adjustX, double adjustY,
                List<System.Windows.Ink.Stroke> strokes, List<System.Windows.Controls.Grid> containers)
            {
                _sourcePage = sourcePage;
                _targetPage = targetPage;
                _deltaX = deltaX;
                _deltaY = deltaY;
                _adjustX = adjustX;
                _adjustY = adjustY;
                _strokes = strokes;
                _containers = containers;
            }

            public bool LeavesDocumentDirty => true;

            public void ExecuteInitialTransfer()
            {
                foreach (var stroke in _strokes)
                {
                    _sourcePage.RemoveStrokeQuiet(stroke);
                    _targetPage.AddStrokeQuiet(stroke);
                }

                foreach (var container in _containers)
                {
                    _sourcePage.RemoveTextContainerQuiet(container);
                    _targetPage.AddTextContainerQuiet(container);
                    TransferImageData(_sourcePage, _targetPage, container);
                }

                _targetPage.MoveItemsDirectly(_strokes, _containers, _adjustX, _adjustY);
            }

            public Task UndoAsync()
            {
                foreach (var stroke in _strokes)
                {
                    _targetPage.RemoveStrokeQuiet(stroke);
                    _sourcePage.AddStrokeQuiet(stroke);
                }

                foreach (var container in _containers)
                {
                    _targetPage.RemoveTextContainerQuiet(container);
                    _sourcePage.AddTextContainerQuiet(container);
                    TransferImageData(_targetPage, _sourcePage, container);
                }

                _sourcePage.MoveItemsDirectly(_strokes, _containers, -_deltaX - _adjustX, -_deltaY - _adjustY);
                return Task.CompletedTask;
            }

            public Task RedoAsync()
            {
                foreach (var stroke in _strokes)
                {
                    _sourcePage.RemoveStrokeQuiet(stroke);
                    _targetPage.AddStrokeQuiet(stroke);
                }

                foreach (var container in _containers)
                {
                    _sourcePage.RemoveTextContainerQuiet(container);
                    _targetPage.AddTextContainerQuiet(container);
                    TransferImageData(_sourcePage, _targetPage, container);
                }

                _targetPage.MoveItemsDirectly(_strokes, _containers, _deltaX + _adjustX, _deltaY + _adjustY);
                return Task.CompletedTask;
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

            private async Task ApplyAsync(byte[] bytes, int focusPageIndex, IReadOnlyList<PageBookmark> bookmarks)
            {
                await _owner.ApplyDocumentSnapshotAsync(bytes, focusPageIndex);
                if (bookmarks == null || string.IsNullOrWhiteSpace(_owner._currentPdfPath))
                    return;

                try
                {
                    PageBookmarkService.Replace(_owner._currentPdfPath, bookmarks);
                    _owner.RefreshBookmarks();
                }
                catch (Exception ex)
                {
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
        private const int ThumbnailCacheCapacity = 24;
        private readonly Dictionary<int, BitmapSource> _thumbnailCache = new Dictionary<int, BitmapSource>();
        private readonly LinkedList<int> _thumbnailCacheLru = new LinkedList<int>();
        private bool _isRefreshingThumbnails;
        private Point _thumbnailDragStartPoint;
        private int _thumbnailDragIndex = -1;
        private CancellationTokenSource _scrollReRenderCts;
        private readonly System.Windows.Threading.DispatcherTimer _scrollRenderDebounceTimer;
        private const double PageSpacing = 28.0;
        private bool _isHostActive = true;
        private bool _resourcesReleased;

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
            InitializeComponent();
            InitializeTextBoxPopup();
            CreateToolPopups();
            InitializePenPresetSlots();
            ApplySettings(AppSettingsService.Load());
            ApplyLocalization();

            _pdfService = new PdfService();
            ActivateTool(ToolType.None);

            PopupZOrderHelper.FixPopupTopmost(_penPopup);
            PopupZOrderHelper.FixPopupTopmost(_highlighterPopup);
            PopupZOrderHelper.FixPopupTopmost(_eraserPopup);
            PopupZOrderHelper.FixPopupTopmost(_shapePopup);
            PopupZOrderHelper.FixPopupTopmost(_selectionPopup);
            PopupZOrderHelper.FixContextMenuTopmost(PdfViewerContextMenu);

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
            if (_languageChangedSubscribed)
            {
                LocalizationService.LanguageChanged -= EditorPage_LanguageChanged;
                _languageChangedSubscribed = false;
            }

            CancelTextResize(restoreBounds: false);
            SetHostActive(false);
            _autoSaveTimer?.Stop();
            _penService?.Dispose();
            _penService = null;
            RemoveHorizontalWheelHook();
            ClearPdfTextSelection();
        }

        private void EnsureAutoSaveTimer()
        {
            if (_resourcesReleased)
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

            if (_selectedTextBox != null)
                DeselectTextBox();

            Keyboard.Focus(PdfScrollViewer);
            ClearPdfTextSelection();
            _pdfTextSelectionPressPoint = e.Position;

            int requestId = Interlocked.Increment(ref _pdfTextSelectionRequestId);

            try
            {
                var textInfo = _pdfService.TryGetCachedPageTextInfo(page.PageIndex, out var cachedTextInfo)
                    ? cachedTextInfo
                    : await _pdfService.GetPageTextInfoAsync(page.PageIndex);

                if (requestId != _pdfTextSelectionRequestId || (_currentTool != ToolType.None && _currentTool != ToolType.TextHighlight))
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
            var saved = await AutoSaveAsync();
            if (saved)
            {
                var mw = Window.GetWindow(this) as MainWindow;
                mw?.ShowToast(LocalizationService.Get("Editor.AutoSaved"), "\uE74E", 1500);
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
            if (e.Key == Key.Escape)
            {
                ActivateTool(ToolType.None);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                await SaveAnnotationsToPdfAsync();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.P)
            {
                await PrintPdfAsync();
                e.Handled = true;
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
            _scrollAnimationDuration = TimeSpan.FromMilliseconds(180);

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
            _hScrollAnimationDuration = TimeSpan.FromMilliseconds(180);

            if (!_isHScrollAnimating)
            {
                _isHScrollAnimating = true;
                System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_HScrollRendering;
            }
        }

        private void CompositionTarget_HScrollRendering(object sender, EventArgs e)
        {
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
            var action = _undoStack[_undoStack.Count - 1];
            try
            {
                await action.UndoAsync();
                _undoStack.RemoveAt(_undoStack.Count - 1);
                _redoStack.Add(action);
                UpdateUndoRedoButtons();
                ApplyDirtyStateForAction(action);
                UpdateSelectedTextBoxPopupVisibility(forceRefresh: true);
            }
            catch (Exception ex)
            {
                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.UndoFailed", ex.Message), "\uE783", 3500);
            }
        }

        private async Task PerformRedoAsync()
        {
            if (_redoStack.Count == 0) return;
            var action = _redoStack[_redoStack.Count - 1];
            try
            {
                await action.RedoAsync();
                _redoStack.RemoveAt(_redoStack.Count - 1);
                _undoStack.Add(action);
                UpdateUndoRedoButtons();
                ApplyDirtyStateForAction(action);
                UpdateSelectedTextBoxPopupVisibility(forceRefresh: true);
            }
            catch (Exception ex)
            {
                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.RedoFailed", ex.Message), "\uE783", 3500);
            }
        }

        private void UpdateUndoRedoButtons()
        {
            UndoButton.IsEnabled = _undoStack.Count > 0;
            RedoButton.IsEnabled = _redoStack.Count > 0;
        }

        private void ApplyDirtyStateForAction(IUndoAction action)
        {
            _dirtyGeneration++;
            _isDirty = action.LeavesDocumentDirty;
        }

        private void ClearUndoRedoHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            UpdateUndoRedoButtons();
        }

        private void PushUndoAction(IUndoAction action)
        {
            _undoStack.Add(action);
            _redoStack.Clear();
            UpdateUndoRedoButtons();
            ApplyDirtyStateForAction(action);
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
                c => { _penColor = c; if (_penPopupSizePreview != null) _penPopupSizePreview.Stroke = new SolidColorBrush(c); UpdateToolIconColors(); if (_currentTool == ToolType.Pen) ApplyToolToAllPages(); SaveSetting(s => RecordRecentColor(s.RecentPenColors, c)); UpdatePresetSlotVisuals(); },
                out _penPopupSizeSlider,
                () => AppSettingsService.Load().RecentPenColors);
            _penPopupSizePreview = AddSizePreviewSection(_penPopup, _penSize, _penColor, isHighlighter: false);
            AddPenBehaviourToggles(_penPopup);
            AddPenSmoothingSection(_penPopup);

            // Highlighter popup — with size preview section
            _highlighterPopup = BuildToolPopup(
                LocalizationService.Get("Editor.PopupSize"), 2, 48, _highlighterSize, 0.5,
                v => { _highlighterSize = v; if (_highlighterPopupSizePreview != null) _highlighterPopupSizePreview.StrokeThickness = v; if (_currentTool == ToolType.Highlighter) ApplyToolToAllPages(); },
                LocalizationService.Get("Editor.PopupColor"), _highlighterColor,
                c => { _highlighterColor = c; if (_highlighterPopupSizePreview != null) _highlighterPopupSizePreview.Stroke = new SolidColorBrush(Color.FromArgb(140, c.R, c.G, c.B)); UpdateToolIconColors(); if (_currentTool == ToolType.Highlighter) ApplyToolToAllPages(); SaveSetting(s => RecordRecentColor(s.RecentHighlighterColors, c)); UpdatePresetSlotVisuals(); },
                out _highlighterPopupSizeSlider,
                () => AppSettingsService.Load().RecentHighlighterColors);
            _highlighterPopupSizePreview = AddSizePreviewSection(_highlighterPopup, _highlighterSize, _highlighterColor, isHighlighter: true);
            AddHighlighterModeSection(_highlighterPopup);

            _eraserPopup = BuildToolPopup(
                LocalizationService.Get("Editor.PopupEraserSize"), 4, 80, _eraserSize, 1,
                v => { _eraserSize = v; ShowEraserSizePreview(v); ApplyToolToAllPages(); },
                null, default, null,
                out _);
            AddEraserModeSection(_eraserPopup);

            // Shape popup — sub-type selector above the shared size slider
            // and color palette (session-only state, no persistence).
            _shapePopup = BuildToolPopup(
                LocalizationService.Get("Editor.PopupSize"), 1, 20, _shapeSize, 0.5,
                v => { _shapeSize = v; if (_currentTool == ToolType.Shape) ApplyToolToAllPages(); },
                LocalizationService.Get("Editor.PopupColor"), _shapeColor,
                c => { _shapeColor = c; if (_currentTool == ToolType.Shape) ApplyToolToAllPages(); },
                out _);
            AddShapeSubTypeSection(_shapePopup);

            CreateSelectionPopup();
        }

        /// <summary>
        /// Prepends the mutually-exclusive shape sub-type selector (直线 /
        /// 矩形 / 椭圆 / 箭头) to the shape popup. Selection is session-only
        /// and re-applied to all pages immediately.
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
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 10)
            });

            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            Border lineButton = null!;
            Border rectButton = null!;
            Border ellipseButton = null!;
            Border arrowButton = null!;
            lineButton = BuildModeToggleButton(LocalizationService.Get("Editor.ShapeLine"), new Thickness(0, 0, 8, 0), activated: () => SelectKind(ShapeKind.Line));
            rectButton = BuildModeToggleButton(LocalizationService.Get("Editor.ShapeRectangle"), new Thickness(0), activated: () => SelectKind(ShapeKind.Rectangle));
            ellipseButton = BuildModeToggleButton(LocalizationService.Get("Editor.ShapeEllipse"), new Thickness(0, 0, 8, 0), activated: () => SelectKind(ShapeKind.Ellipse));
            arrowButton = BuildModeToggleButton(LocalizationService.Get("Editor.ShapeArrow"), new Thickness(0), activated: () => SelectKind(ShapeKind.Arrow));

            void ApplyVisual()
            {
                StyleModeToggleButton(lineButton, _shapeKind == ShapeKind.Line);
                StyleModeToggleButton(rectButton, _shapeKind == ShapeKind.Rectangle);
                StyleModeToggleButton(ellipseButton, _shapeKind == ShapeKind.Ellipse);
                StyleModeToggleButton(arrowButton, _shapeKind == ShapeKind.Arrow);
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

            row1.Children.Add(lineButton);
            row1.Children.Add(rectButton);
            row2.Children.Add(ellipseButton);
            row2.Children.Add(arrowButton);

            // Sub-type section sits above the size slider.
            panel.Children.Insert(0, row2);
            panel.Children.Insert(0, row1);
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
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 10)
            });

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            Border pixelButton = null!;
            Border wholeButton = null!;
            pixelButton = BuildModeToggleButton(LocalizationService.Get("Editor.EraserPixel"), new Thickness(0, 0, 8, 0), activated: () => SelectMode(false));
            wholeButton = BuildModeToggleButton(LocalizationService.Get("Editor.EraserStroke"), new Thickness(0), activated: () => SelectMode(true));

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
                AppSettingsService.Save(settings);
                ApplyToolToAllPages();
                ApplyModeVisual();
            }

            modeRow.Children.Add(pixelButton);
            modeRow.Children.Add(wholeButton);

            // Mode section sits above the size slider.
            panel.Children.Insert(0, modeRow);
            panel.Children.Insert(0, header);
            ApplyModeVisual();
        }

        private static Border BuildModeToggleButton(
            string label,
            Thickness margin,
            double width = 116,
            Action activated = null)
        {
            var button = new Border
            {
                Width = width,
                Height = 32,
                Margin = margin,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = true,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            AutomationProperties.SetName(button, label);
            KeyboardNavigation.SetIsTabStop(button, true);
            if (activated != null)
            {
                button.MouseLeftButtonDown += (_, e) =>
                {
                    activated();
                    e.Handled = true;
                };
                button.KeyDown += (_, e) =>
                {
                    if (e.Key != Key.Enter && e.Key != Key.Space)
                        return;
                    activated();
                    e.Handled = true;
                };
            }

            return button;
        }

        private static void StyleModeToggleButton(Border button, bool active)
        {
            button.SetResourceReference(Border.BorderBrushProperty,
                active ? "ThemeAccentBrush" : "ThemeBorderBrush");
            button.SetResourceReference(Border.BackgroundProperty,
                active ? "ThemeSelectionBrush" : "ThemeSurfaceAltBrush");
            if (button.Child is TextBlock text)
            {
                text.SetResourceReference(
                    TextElement.ForegroundProperty,
                    active ? "ThemeAccentBrush" : "ThemeForegroundBrush");
            }
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
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 10)
            });

            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };

            var modes = new (HighlighterApplyMode Mode, string Label)[]
            {
                (HighlighterApplyMode.Freehand, LocalizationService.Get("Editor.HighlighterFreehand")),
                (HighlighterApplyMode.TextHighlight, LocalizationService.Get("Editor.HighlighterText")),
                (HighlighterApplyMode.Underline, LocalizationService.Get("Editor.HighlighterUnderline")),
                (HighlighterApplyMode.StrikeOut, LocalizationService.Get("Editor.HighlighterStrikeOut")),
                (HighlighterApplyMode.Squiggly, LocalizationService.Get("Editor.HighlighterSquiggly")),
                (HighlighterApplyMode.AreaHighlight, LocalizationService.Get("Editor.HighlighterArea")),
            };

            var buttons = new Dictionary<HighlighterApplyMode, Border>();

            void ApplyVisual()
            {
                foreach (var pair in buttons)
                    StyleModeToggleButton(pair.Value, pair.Key == _highlighterApplyMode);
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
                var button = BuildModeToggleButton(modes[i].Label,
                    new Thickness(0, 0, i % 3 < 2 ? 6 : 0, 0), width: 82,
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
                Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                Margin = new Thickness(-16, 14, -16, 10)
            }));

            var pressureRow = BuildSettingToggleRow(LocalizationService.Get("Editor.Pressure"), AppSettingsService.Load().EnablePressure, v =>
            {
                SaveSetting(s => s.EnablePressure = v);
                ApplyToolToAllPages();
            });

            var inkSimRow = BuildSettingToggleRow(LocalizationService.Get("Editor.InkSimulation"), AppSettingsService.Load().InkSimulation, v =>
            {
                SaveSetting(s => s.InkSimulation = v);
                ApplyToolToAllPages();
            });

            var shapeRecognitionRow = BuildSettingToggleRow(LocalizationService.Get("Editor.ShapeRecognition"), AppSettingsService.Load().ShapeRecognition, v =>
            {
                SaveSetting(s => s.ShapeRecognition = v);
                ApplyToolToAllPages();
            });

            panel.Children.Add(pressureRow);
            panel.Children.Add(inkSimRow);
            panel.Children.Add(shapeRecognitionRow);
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
                Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                Margin = new Thickness(-16, 14, -16, 10)
            }));

            panel.Children.Add(ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.SmoothingHeader"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 10)
            }));

            var labels = new[]
            {
                LocalizationService.Get("Editor.SmoothingOff"),
                LocalizationService.Get("Editor.SmoothingLow"),
                LocalizationService.Get("Editor.SmoothingMid"),
                LocalizationService.Get("Editor.SmoothingHigh")
            };
            var buttons = new Border[labels.Length];
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
                });
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
        private static Border BuildSettingToggleRow(string label, bool initialState, Action<bool> toggled)
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

            var row = new Border
            {
                Height = 34,
                Margin = new Thickness(0, 0, 0, 6),
                Background = new SolidColorBrush(Color.FromArgb(6, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 0, 10, 0),
                Cursor = Cursors.Hand,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { indicator, text }
                }
            };

            row.Focusable = true;
            AutomationProperties.SetName(row, label);
            KeyboardNavigation.SetIsTabStop(row, true);

            bool state = initialState;

            void ApplyVisual()
            {
                row.SetResourceReference(Border.BackgroundProperty,
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

            void Toggle()
            {
                state = !state;
                ApplyVisual();
                toggled?.Invoke(state);
            }

            row.MouseLeftButtonDown += (s, e) =>
            {
                Toggle();
                e.Handled = true;
            };
            row.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Space)
                    return;
                Toggle();
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
        private void RefreshRecentColorsRow(StackPanel section, StackPanel row, Func<List<string>> getRecentColors, Action<Color> applyColor)
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

                    var swatch = new Border
                    {
                        Width = 16,
                        Height = 16,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(color),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        Margin = new Thickness(0, 0, 6, 0),
                        ToolTip = hex,
                        Tag = color
                    };
                    swatch.MouseLeftButtonDown += (s, e) =>
                    {
                        if (s is Border b && b.Tag is Color picked)
                            applyColor?.Invoke(picked);
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
                Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                Margin = new Thickness(-16, 14, -16, 10)
            }));

            panel.Children.Add(ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.PopupPreview"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 8)
            }));

            var previewBorder = new Border
            {
                Height = 60,
                Background = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true
            };

            Line line;
            if (isHighlighter)
            {
                // Highlighter: horizontal stroke band showing actual stroke height
                line = new Line
                {
                    X1 = 8, Y1 = 30, X2 = 212, Y2 = 30,
                    Stroke = new SolidColorBrush(Color.FromArgb(140, initialColor.R, initialColor.G, initialColor.B)),
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
                    BlurRadius = 20, ShadowDepth = 4, Opacity = 0.12, Color = Colors.Black
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
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 8)
            }));

            var shapePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

            Button MakeShapeButton(string icon, string tooltip, SelectionShape shape)
            {
                var isActive = _selectionShape == shape;
                var btn = new Button
                {
                    Width = 36, Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(0),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    ToolTip = tooltip,
                    Tag = shape
                };
                btn.Template = CreateIconButtonTemplate("#E8E8E8", "#DCDCDC");
                btn.Content = new TextBlock
                {
                    Text = icon,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                UpdateFilterButtonStyle(btn, isActive);
                btn.Click += (s, ev) =>
                {
                    _selectionShape = (SelectionShape)((Button)s).Tag;
                    ApplyToolToAllPages();
                    foreach (Button b in shapePanel.Children)
                        UpdateFilterButtonStyle(b, (SelectionShape)b.Tag == _selectionShape);
                };
                return btn;
            }

            shapePanel.Children.Add(MakeShapeButton("\uE73F", LocalizationService.Get("Editor.SelectShapeRect"), SelectionShape.Rectangle));
            shapePanel.Children.Add(MakeShapeButton("\uED63", LocalizationService.Get("Editor.SelectShapeFree"), SelectionShape.FreeForm));
            settingsPanel.Children.Add(shapePanel);

            // ── Filter section header
            settingsPanel.Children.Add(ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.SelectFilter"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 8)
            }));

            // Filter radio buttons
            var filterPanel = new StackPanel { Orientation = Orientation.Horizontal };

            Button MakeFilterButton(string label, SelectionFilter filter)
            {
                var isActive = _selectionFilter == filter;
                var btn = new Button
                {
                    Content = label,
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 12,
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    Tag = filter
                };
                btn.Template = CreateIconButtonTemplate("#E8E8E8", "#DCDCDC");
                UpdateFilterButtonStyle(btn, isActive);
                btn.Click += (s, ev) =>
                {
                    _selectionFilter = (SelectionFilter)((Button)s).Tag;
                    ApplyToolToAllPages();
                    // Refresh all filter button styles
                    foreach (Button b in filterPanel.Children)
                        UpdateFilterButtonStyle(b, (SelectionFilter)b.Tag == _selectionFilter);
                };
                return btn;
            }

            filterPanel.Children.Add(MakeFilterButton(LocalizationService.Get("Editor.SelectFilterBoth"), SelectionFilter.Both));
            filterPanel.Children.Add(MakeFilterButton(LocalizationService.Get("Editor.SelectFilterDrawings"), SelectionFilter.DrawingsOnly));
            filterPanel.Children.Add(MakeFilterButton(LocalizationService.Get("Editor.SelectFilterText"), SelectionFilter.TextOnly));

            settingsPanel.Children.Add(filterPanel);

            // Task 28: the current WPF build has no WinRT InkAnalyzer
            // projection. Keep the action visible and fail safely without
            // mutating the selected strokes; the spike conclusion is recorded
            // in .ai/Task28-InkAnalysis.md.
            var recognizeButton = new Button
            {
                Content = LocalizationService.Get("Editor.InkAnalysisUnavailable"),
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(10, 6, 10, 6),
                Cursor = Cursors.Hand,
                ToolTip = LocalizationService.Get("Editor.InkAnalysisTooltip")
            };
            recognizeButton.Click += (_, __) =>
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.InkAnalysisUnavailable"), "\uE783", 2600);
            settingsPanel.Children.Add(recognizeButton);
            _selectionPopup.Child = settingsBorder;
        }

        private void UpdateFilterButtonStyle(Button btn, bool isActive)
        {
            btn.SetResourceReference(
                Button.BackgroundProperty,
                isActive ? "ThemeSelectionBrush" : "ThemeSurfaceAltBrush");
            btn.SetResourceReference(
                Button.BorderBrushProperty,
                isActive ? "ThemeAccentBrush" : "ThemeBorderBrush");
            btn.SetResourceReference(
                Control.ForegroundProperty,
                isActive ? "ThemeSelectionForegroundBrush" : "ThemeForegroundBrush");
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
            var containers = new List<System.Windows.Controls.Grid>(_activeSelectionPage.SelectedTextContainers);

            foreach (var s in strokes) _activeSelectionPage.RemoveStrokeQuiet(s);
            foreach (var c in containers) _activeSelectionPage.RemoveTextContainerQuiet(c);

            PushUndoAction(new ItemsRemovedAction(_activeSelectionPage, strokes, containers));

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
                    var strokeAnnotation = new StrokeAnnotation
                    {
                        R = stroke.DrawingAttributes.Color.R,
                        G = stroke.DrawingAttributes.Color.G,
                        B = stroke.DrawingAttributes.Color.B,
                        A = stroke.DrawingAttributes.Color.A,
                        Size = stroke.DrawingAttributes.Width,
                        IsHighlighter = stroke.DrawingAttributes.IsHighlighter,
                        FitToCurve = stroke.DrawingAttributes.FitToCurve,
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
                    foreach (var strokeAnnotation in pageAnnotation.Strokes)
                    {
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
                foreach (var stroke in page.SelectedStrokes)
                {
                    var points = new StylusPointCollection();
                    foreach (var pt in stroke.StylusPoints)
                        points.Add(new StylusPoint(pt.X + offsetX, pt.Y + offsetY, pt.PressureFactor));

                    var clone = new Stroke(points, stroke.DrawingAttributes.Clone());
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

        private Popup BuildToolPopup(
            string sizeLabel, double min, double max, double value, double step, Action<double> sizeChanged,
            string colorLabel, Color initialColor, Action<Color> colorChanged,
            out Slider sizeSlider,
            Func<List<string>> recentColors = null)
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
                    Opacity = 0.12,
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
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 10)
            });
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                TickFrequency = step,
                Width = 240,
                IsSnapToTickEnabled = true
            };
            slider.ValueChanged += (s, e) => sizeChanged?.Invoke(e.NewValue);
            panel.Children.Add(sizeHeader);
            panel.Children.Add(slider);
            // Task 23: expose the slider so preset applications can resync
            // it when _penSize/_highlighterSize change outside the popup.
            sizeSlider = slider;

            if (colorLabel != null)
            {
                // Separator
                var separator = ThemeDivider(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                    Margin = new Thickness(-16, 14, -16, 14)
                });
                panel.Children.Add(separator);

                var colorHeader = ThemeSubtleHeader(new TextBlock
                {
                    Text = colorLabel,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    Margin = new Thickness(0, 0, 0, 10)
                });
                panel.Children.Add(colorHeader);

                // Task 14: "最近 Recent" swatch row above the palette (hidden
                // while empty); repopulated on every popup open.
                if (recentColors != null)
                {
                    var recentSection = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
                    var recentRow = new StackPanel { Orientation = Orientation.Horizontal };
                    recentSection.Children.Add(ThemeSubtleHeader(new TextBlock
                    {
                        Text = LocalizationService.Get("Editor.Recent"),
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                        Margin = new Thickness(0, 0, 0, 8)
                    }));
                    recentSection.Children.Add(recentRow);
                    panel.Children.Add(recentSection);
                    popup.Opened += (s, e) => RefreshRecentColorsRow(recentSection, recentRow, recentColors, colorChanged);
                }

                // HSV color palette grid
                int cols = 12;
                int rows = 8;
                double cellSize = 20;
                var paletteGrid = new Grid { Width = cols * cellSize, Height = rows * cellSize, ClipToBounds = true };

                // Selection indicator border (drawn on top of palette)
                var selectionIndicator = new Border
                {
                    Width = cellSize,
                    Height = cellSize,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(2),
                    Background = Brushes.Transparent,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };

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

                        var cell = new Border
                        {
                            Width = cellSize,
                            Height = cellSize,
                            Background = new SolidColorBrush(cellColor),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(col * cellSize, row * cellSize, 0, 0),
                            Cursor = Cursors.Hand,
                            Tag = cellColor
                        };

                        cell.MouseLeftButtonDown += (s, e) =>
                        {
                            var b = s as Border;
                            var picked = (Color)b.Tag;
                            selectionIndicator.Margin = b.Margin;
                            selectionIndicator.Visibility = Visibility.Visible;
                            colorChanged?.Invoke(picked);
                            e.Handled = true;
                        };

                        paletteGrid.Children.Add(cell);
                    }
                }

                // Place selection indicator on initial color if found
                foreach (Border cell in paletteGrid.Children)
                {
                    if (cell.Tag is Color c && c == initialColor)
                    {
                        selectionIndicator.Margin = cell.Margin;
                        selectionIndicator.Visibility = Visibility.Visible;
                        break;
                    }
                }

                paletteGrid.Children.Add(selectionIndicator);
                panel.Children.Add(paletteGrid);
            }

            popup.Child = border;
            return popup;
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
            try
            {
                await RunPdfSearchAsync(PdfSearchTextBox.Text?.Trim() ?? string.Empty, _pdfSearchCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task RunPdfSearchAsync(string query, CancellationToken cancellationToken)
        {
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
            foreach (var result in _pdfSearchResults)
                PdfSearchResultsListBox.Items.Add(new ListBoxItem { Content = result.DisplayText, Tag = result });
            PdfSearchStatusTextBlock.Text = LocalizationService.Format("Editor.SearchResults", _pdfSearchResults.Count);
            if (_pdfSearchResults.Count > 0)
                PdfSearchResultsListBox.SelectedIndex = 0;
        }

        private async void PdfSearchResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PdfSearchResultsListBox.SelectedItem is ListBoxItem item && item.Tag is PdfSearchResult result)
                await JumpToPdfSearchResultAsync(result);
        }

        private async Task JumpToPdfSearchResultAsync(PdfSearchResult result)
        {
            if (result == null || result.PageIndex < 0 || result.PageIndex >= _pageControls.Count)
                return;
            JumpToPage(result.PageIndex);
            var info = await _pdfService.GetPageTextInfoAsync(result.PageIndex);
            var page = _pageControls[result.PageIndex];
            foreach (var other in _pageControls)
            {
                if (!ReferenceEquals(other, page))
                    other.ClearPdfTextSelection();
            }
            page.SetPdfTextSelectionRects(BuildPdfTextSelectionRects(info, result.StartOffset, result.StartOffset + result.Length - 1));
        }

        private async Task MovePdfSearchSelectionAsync(bool backwards)
        {
            if (PdfSearchPanel.Visibility != Visibility.Visible || _pdfSearchResults.Count == 0)
                return;
            int current = PdfSearchResultsListBox.SelectedIndex;
            int next = (current + (backwards ? -1 : 1) + _pdfSearchResults.Count) % _pdfSearchResults.Count;
            PdfSearchResultsListBox.SelectedIndex = next;
            await JumpToPdfSearchResultAsync(_pdfSearchResults[next]);
        }

        private async void PdfSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await MovePdfSearchSelectionAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
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
                CloseToolPopups();
                e.Handled = consumePointerEvent;
            }
        }

        private void EditorPage_PreviewStylusDown(object sender, StylusDownEventArgs e)
        {
            if (ShouldClosePopupOnPointerDown(e.OriginalSource as DependencyObject, out bool consumePointerEvent))
            {
                CloseToolPopups();
                e.Handled = consumePointerEvent;
            }
        }

        private bool ShouldClosePopupOnPointerDown(DependencyObject originalSource, out bool consumePointerEvent)
        {
            consumePointerEvent = false;
            if (originalSource == null) return false;

            var popups = new[] { _penPopup, _highlighterPopup, _eraserPopup, _shapePopup, _selectionPopup };
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

            if (!anyPopupOpen) return false;

            if (IsSourceInToolbar(originalSource))
            {
                return false;
            }

            consumePointerEvent = !IsImmediateDrawingToolActive();
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
            CancelActiveLoad();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;
            var sessionId = Interlocked.Increment(ref _loadSessionId);

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
            _isDirty = false;
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
                await LoadSelectablePdfDocumentAsync(filePath, token);
                RecentFilesService.UpdateMetadata(filePath, _pdfService.PageCount, File.GetLastWriteTimeUtc(filePath));

                int pageCount = _pdfService.PageCount;
                double currentTop = 0;

                for (int i = 0; i < pageCount; i++)
                {
                    token.ThrowIfCancellationRequested();

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
                    pageControl.StrokeCollectedUndoable += PageControl_StrokeCollectedUndoable;
                    pageControl.StrokesErased += PageControl_StrokesErased;
                    pageControl.StrokeRecognized += PageControl_StrokeRecognized;
                    pageControl.ImagesChanged += PageControl_ImagesChanged;
                    pageControl.AreaHighlightCreated += PageControl_AreaHighlightCreated;
                    pageControl.StickyNoteActivated += PageControl_StickyNoteActivated;
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
                    pageControl.GetRulerEdgeInPageCoords = () =>
                    {
                        var edge = GetRulerEdgeEndpoints();
                        if (edge == null) return null;
                        return (TranslatePoint(edge.Value.A, pageControl), TranslatePoint(edge.Value.B, pageControl));
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
                }
                TrimPageBitmapWorkingSet(visiblePages);

                if (!string.IsNullOrEmpty(_currentPdfPath))
                    await LoadAnnotationsFromPdfServiceAsync();

                UpdatePageNumberIndicator();
                SyncSelectableViewerFromCustomView();
                await RefreshDocumentSidebarAsync(token);

                if (_promptSaveAsAfterLoad && !_hasPromptedForSaveAs)
                    await PromptSaveAsForDraftAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                var errorMsg = LocalizationService.Format("Editor.LoadPdfFailed", ex.Message);
                if (ex.InnerException != null)
                    errorMsg += $"\n\n{LocalizationService.Format("Editor.ErrorDetails", ex.InnerException.Message)}";

                if (sessionId == _loadSessionId)
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
                if (sessionId == _loadSessionId)
                    HideLoadingOverlay();
            }
        }

        private async Task RefreshDocumentSidebarAsync(CancellationToken cancellationToken)
        {
            if (ThumbnailListBox == null || _pdfService == null)
                return;

            _thumbnailLoadCts?.Cancel();
            _thumbnailLoadCts?.Dispose();
            _thumbnailLoadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _thumbnailPagesLoading.Clear();
            ClearThumbnailCache();
            _isRefreshingThumbnails = true;
            try
            {
                ThumbnailListBox.Items.Clear();
                for (int pageIndex = 0; pageIndex < _pageControls.Count; pageIndex++)
                {
                    var image = new Image
                    {
                        Width = 132,
                        Height = 170,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(4)
                    };
                    var panel = new StackPanel();
                    panel.Children.Add(image);
                    panel.Children.Add(new TextBlock
                    {
                Text = LocalizationService.Format("Editor.PageNumber", pageIndex + 1),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 4),
                        Foreground = (Brush)FindResource("ThemeSubtleForegroundBrush")
                    });

                    var item = new ListBoxItem { Content = panel, Tag = pageIndex, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    item.ContextMenu = BuildThumbnailContextMenu(pageIndex);
                    item.Loaded += ThumbnailListBoxItem_Loaded;
                    item.Unloaded += ThumbnailListBoxItem_Unloaded;
                    ThumbnailListBox.Items.Add(item);
                }

                UpdateThumbnailSelection();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _isRefreshingThumbnails = false;
            }

            RefreshBookmarks();
            await RefreshOutlineAsync(cancellationToken);
        }

        private async void ThumbnailListBoxItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListBoxItem item || item.Tag is not int pageIndex ||
                item.Content is not StackPanel panel ||
                panel.Children.OfType<Image>().FirstOrDefault() is not Image image ||
                image.Source != null || _thumbnailPagesLoading.Contains(pageIndex) ||
                !_isHostActive || _resourcesReleased)
                return;

            if (TryGetCachedThumbnail(pageIndex, out var cached))
            {
                image.Source = cached;
                return;
            }

            _thumbnailPagesLoading.Add(pageIndex);
            try
            {
                var token = _thumbnailLoadCts?.Token ?? CancellationToken.None;
                var bitmap = await _pdfService.RenderPageBitmapSourceAsync(pageIndex, 0.22, token);
                token.ThrowIfCancellationRequested();
                if (!_isHostActive || _resourcesReleased)
                    return;
                CacheThumbnail(pageIndex, bitmap);
                image.Source = bitmap;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Thumbnail] Failed to render page {pageIndex}: {ex}");
            }
            finally
            {
                _thumbnailPagesLoading.Remove(pageIndex);
            }
        }

        private void ThumbnailListBoxItem_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListBoxItem item || item.Tag is not int pageIndex ||
                item.Content is not StackPanel panel ||
                panel.Children.OfType<Image>().FirstOrDefault() is not Image image)
                return;

            if (!_thumbnailCache.ContainsKey(pageIndex))
                image.Source = null;
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

                if (evictedPage < ThumbnailListBox.Items.Count &&
                    ThumbnailListBox.Items[evictedPage] is ListBoxItem evictedItem &&
                    evictedItem.Content is StackPanel evictedPanel &&
                    evictedPanel.Children.OfType<Image>().FirstOrDefault() is Image evictedImage &&
                    ReferenceEquals(evictedImage.Source, evictedBitmap))
                {
                    evictedImage.Source = null;
                }
            }
        }

        private void ClearThumbnailCache()
        {
            _thumbnailCache.Clear();
            _thumbnailCacheLru.Clear();
            if (ThumbnailListBox == null)
                return;

            foreach (var item in ThumbnailListBox.Items.OfType<ListBoxItem>())
            {
                if (item.Content is StackPanel panel &&
                    panel.Children.OfType<Image>().FirstOrDefault() is Image image)
                {
                    image.Source = null;
                }
            }
        }

        private void LoadVisibleThumbnails()
        {
            if (ThumbnailListBox == null)
                return;

            foreach (var item in ThumbnailListBox.Items.OfType<ListBoxItem>().Where(item => item.IsLoaded))
                ThumbnailListBoxItem_Loaded(item, new RoutedEventArgs(FrameworkElement.LoadedEvent, item));
        }

        private ContextMenu BuildThumbnailContextMenu(int pageIndex)
        {
            var menu = new ContextMenu();
            var insert = new MenuItem { Header = LocalizationService.Get("Editor.InsertBlankPageBefore") };
            insert.Click += async (_, __) => await InsertPageAtAsync(pageIndex);
            var duplicate = new MenuItem { Header = LocalizationService.Get("Editor.DuplicatePage") };
            duplicate.Click += async (_, __) => await DuplicatePageAtAsync(pageIndex);
            var delete = new MenuItem { Header = LocalizationService.Get("Editor.DeletePage") };
            delete.Click += async (_, __) => await DeletePageAtAsync(pageIndex);
            menu.Items.Add(insert);
            menu.Items.Add(duplicate);
            menu.Items.Add(new Separator());
            menu.Items.Add(delete);
            PopupZOrderHelper.FixContextMenuTopmost(menu);
            return menu;
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
            if (_isRefreshingThumbnails || ThumbnailListBox.SelectedItem is not ListBoxItem item || item.Tag is not int pageIndex)
                return;
            JumpToPage(pageIndex);
        }

        private void ThumbnailListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item && item.Tag is int pageIndex)
            {
                _thumbnailDragStartPoint = e.GetPosition(ThumbnailListBox);
                _thumbnailDragIndex = pageIndex;
            }
        }

        private void ThumbnailListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_thumbnailDragIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(ThumbnailListBox);
            if (Math.Abs(current.X - _thumbnailDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _thumbnailDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            int sourceIndex = _thumbnailDragIndex;
            _thumbnailDragIndex = -1;
            DragDrop.DoDragDrop(ThumbnailListBox, new DataObject(typeof(int), sourceIndex), DragDropEffects.Move);
        }

        private async void ThumbnailListBox_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(int)) || string.IsNullOrWhiteSpace(_currentPdfPath))
                return;

            int fromIndex = (int)e.Data.GetData(typeof(int));
            var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            int toIndex = targetItem?.Tag is int target ? target : _pageControls.Count - 1;
            if (fromIndex == toIndex || fromIndex < 0 || toIndex < 0)
                return;

            try
            {
                if (_isDirty && !await AutoSaveAsync())
                    return;

                byte[] before = await File.ReadAllBytesAsync(_currentPdfPath);
                int focusBefore = GetCurrentPageIndex();
                var beforeBookmarks = PageBookmarkService.Load(_currentPdfPath).ToList();
                await _pdfService.ReorderPagesAsync(_currentPdfPath, fromIndex, toIndex);
                byte[] after = await File.ReadAllBytesAsync(_currentPdfPath);
                await LoadPdf(_currentPdfPath);
                int focused = Math.Max(0, Math.Min(toIndex, _pageControls.Count - 1));
                JumpToPage(focused);
                var afterBookmarks = PageBookmarkService.ApplyPageMove(_currentPdfPath, fromIndex, toIndex).ToList();
                RefreshBookmarks();
                PushUndoAction(new DocumentSnapshotAction(this, before, after, focusBefore, focused, beforeBookmarks, afterBookmarks));
            }
            catch (Exception ex)
            {
                GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.PageReorderFailed", ex.Message), "\uE783", 3500);
            }
        }

        private async Task DuplicatePageAtAsync(int pageIndex)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
                return;
            try
            {
                if (_isDirty && !await AutoSaveAsync())
                    return;
                byte[] before = await File.ReadAllBytesAsync(_currentPdfPath);
                int focusBefore = GetCurrentPageIndex();
                var beforeBookmarks = PageBookmarkService.Load(_currentPdfPath).ToList();
                await _pdfService.DuplicatePageAsync(_currentPdfPath, pageIndex);
                byte[] after = await File.ReadAllBytesAsync(_currentPdfPath);
                await LoadPdf(_currentPdfPath);
                int focused = Math.Max(0, Math.Min(pageIndex + 1, _pageControls.Count - 1));
                JumpToPage(focused);
                var afterBookmarks = PageBookmarkService.ApplyPageInsert(_currentPdfPath, pageIndex + 1).ToList();
                RefreshBookmarks();
                PushUndoAction(new DocumentSnapshotAction(this, before, after, focusBefore, focused, beforeBookmarks, afterBookmarks));
            }
            catch (Exception ex)
            {
                GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.PageDuplicateFailed", ex.Message), "\uE783", 3500);
            }
        }

        private void UpdateThumbnailSelection()
        {
            if (ThumbnailListBox == null || ThumbnailListBox.Items.Count == 0)
                return;
            int current = GetCurrentPageIndex();
            if (current >= 0 && current < ThumbnailListBox.Items.Count)
                ThumbnailListBox.SelectedIndex = current;
        }

        private void RefreshBookmarks()
        {
            if (BookmarksListBox == null)
                return;
            BookmarksListBox.Items.Clear();
            foreach (var bookmark in PageBookmarkService.Load(_currentPdfPath))
            {
                var item = new ListBoxItem
                {
                    Tag = bookmark.PageIndex,
                    Content = PageBookmarkService.GetDisplayLabel(bookmark)
                };
                var removeMenu = new ContextMenu();
                var removeItem = new MenuItem { Header = LocalizationService.Get("Editor.RemoveBookmark") };
                removeItem.Click += (_, __) =>
                {
                    PageBookmarkService.Toggle(_currentPdfPath, bookmark.PageIndex);
                    RefreshBookmarks();
                };
                removeMenu.Items.Add(removeItem);
                PopupZOrderHelper.FixContextMenuTopmost(removeMenu);
                item.ContextMenu = removeMenu;
                BookmarksListBox.Items.Add(item);
            }
            UpdateBookmarkButton();
        }

        private void UpdateBookmarkButton()
        {
            if (BookmarkToggleButton == null)
                return;
            bool bookmarked = PageBookmarkService.Load(_currentPdfPath).Any(bookmark => bookmark.PageIndex == GetCurrentPageIndex());
            BookmarkToggleButton.Content = bookmarked
                ? $"★ {LocalizationService.Get("Editor.UnbookmarkCurrentPage")}"
                : $"☆ {LocalizationService.Get("Editor.BookmarkCurrentPage")}";
        }

        private void BookmarkToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath) || _pageControls.Count == 0)
                return;
            PageBookmarkService.Toggle(_currentPdfPath, GetCurrentPageIndex());
            RefreshBookmarks();
        }

        private void SidebarCollapseButton_Click(object sender, RoutedEventArgs e)
        {
            bool isCollapsed = DocumentSidebarTabs.Visibility == Visibility.Collapsed;
            DocumentSidebarTabs.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
            DocumentSidebar.Width = isCollapsed ? 184 : 38;
            SidebarCollapseButton.Content = isCollapsed
                ? $"‹  {LocalizationService.Get("Editor.SidebarCollapse")}"
                : $"›  {LocalizationService.Get("Editor.SidebarExpand")}";
        }

        private void BookmarksListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BookmarksListBox.SelectedItem is ListBoxItem item && item.Tag is int pageIndex)
                JumpToPage(pageIndex);
        }

        private async Task RefreshOutlineAsync(CancellationToken cancellationToken)
        {
            if (OutlineTreeView == null)
                return;
            OutlineTreeView.Items.Clear();
            IReadOnlyList<PdfService.PdfOutlineEntry> outline;
            try
            {
                outline = await _pdfService.GetOutlineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Outline] Failed to read outline: {ex}");
                outline = Array.Empty<PdfService.PdfOutlineEntry>();
            }
            if (outline.Count == 0)
            {
                for (int i = 0; i < _pageControls.Count; i++)
                OutlineTreeView.Items.Add(new TreeViewItem
                {
                    Header = LocalizationService.Format("Editor.PageNumber", i + 1),
                    Tag = i
                });
                return;
            }

            foreach (var entry in outline)
                OutlineTreeView.Items.Add(BuildOutlineTreeItem(entry));
        }

        private TreeViewItem BuildOutlineTreeItem(PdfService.PdfOutlineEntry entry)
        {
            var item = new TreeViewItem { Header = entry.Title, Tag = entry.PageIndex };
            item.Selected += (_, __) =>
            {
                if (item.Tag is int pageIndex && pageIndex >= 0)
                    JumpToPage(pageIndex);
            };
            foreach (var child in entry.Children)
                item.Items.Add(BuildOutlineTreeItem(child));
            return item;
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
                Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Hidden,
                ToolTip = LocalizationService.Get("Editor.DeletePageTooltip"),
                Template = CreatePageChromeButtonTemplate("#FEE2E2", "#FECACA")
            };

            deleteButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = "\uE74D",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28)),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = LocalizationService.Get("Editor.DeletePageTooltip"),
                        Margin = new Thickness(6, 0, 0, 0),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(127, 29, 29)),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };

            deleteButton.Click += async (sender, args) =>
            {
                args.Handled = true;
                await DeletePageAtAsync(pageControl.PageIndex);
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
                Background = new SolidColorBrush(Color.FromRgb(191, 219, 254)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var insertButton = new Button
            {
                Width = 78,
                Height = 32,
                Background = new SolidColorBrush(Color.FromArgb(250, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                ToolTip = LocalizationService.Get("Editor.InsertPageHereTooltip"),
                Template = CreatePageChromeButtonTemplate("#EFF6FF", "#DBEAFE")
            };

            insertButton.Content = new TextBlock
            {
                Text = "\uE710",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            insertButton.Click += async (_, __) => await InsertPageAtAsync(insertIndex);

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

        private async Task InsertPageAtAsync(int insertIndex)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
            {
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                return;
            }

            var owner = GetMainWindow();
            var picker = new PageTemplatePickerWindow();
            if (owner != null)
                picker.Owner = owner;

            if (picker.ShowDialog() != true)
                return;

            try
            {
                if (_isDirty && !await AutoSaveAsync())
                    return;

                byte[] beforeBytes = await File.ReadAllBytesAsync(_currentPdfPath);
                int undoFocusIndex = Math.Max(0, Math.Min(insertIndex, Math.Max(_pageControls.Count - 1, 0)));
                var beforeBookmarks = PageBookmarkService.Load(_currentPdfPath).ToList();

                await _pdfService.InsertPageAsync(_currentPdfPath, insertIndex, picker.SelectedTemplate);

                byte[] afterBytes = await File.ReadAllBytesAsync(_currentPdfPath);
                await LoadPdf(_currentPdfPath);

                int insertedPageIndex = Math.Max(0, Math.Min(insertIndex, _pageControls.Count - 1));
                JumpToPage(insertedPageIndex);
                RecentFilesService.UpdateMetadata(_currentPdfPath, _pageControls.Count, File.GetLastWriteTimeUtc(_currentPdfPath));
                var afterBookmarks = PageBookmarkService.ApplyPageInsert(_currentPdfPath, insertedPageIndex).ToList();
                RefreshBookmarks();
                PushUndoAction(new DocumentSnapshotAction(this, beforeBytes, afterBytes, undoFocusIndex, insertedPageIndex, beforeBookmarks, afterBookmarks));
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageAdded"), "\uE710");
            }
            catch (Exception ex)
            {
                var mw = GetMainWindow();
                if (mw != null)
                    await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Editor.AddPageFailed", ex.Message));
                else
                    MessageBox.Show(LocalizationService.Format("Editor.AddPageFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeletePageAtAsync(int pageIndex)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
            {
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                return;
            }

            if (_pageControls.Count <= 1)
            {
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageDeleteBlocked"), "\uE783");
                return;
            }

            try
            {
                if (_isDirty && !await AutoSaveAsync())
                    return;

                byte[] beforeBytes = await File.ReadAllBytesAsync(_currentPdfPath);
                var beforeBookmarks = PageBookmarkService.Load(_currentPdfPath).ToList();
                await _pdfService.DeletePageAsync(_currentPdfPath, pageIndex);

                byte[] afterBytes = await File.ReadAllBytesAsync(_currentPdfPath);
                await LoadPdf(_currentPdfPath);

                int focusAfterDelete = Math.Max(0, Math.Min(pageIndex, _pageControls.Count - 1));
                JumpToPage(focusAfterDelete);
                RecentFilesService.UpdateMetadata(_currentPdfPath, _pageControls.Count, File.GetLastWriteTimeUtc(_currentPdfPath));
                var afterBookmarks = PageBookmarkService.ApplyPageDelete(_currentPdfPath, pageIndex).ToList();
                RefreshBookmarks();
                PushUndoAction(new DocumentSnapshotAction(this, beforeBytes, afterBytes, pageIndex, focusAfterDelete, beforeBookmarks, afterBookmarks));
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageDeleted"), "\uE74D");
            }
            catch (InvalidOperationException)
            {
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageDeleteBlocked"), "\uE783");
            }
            catch (Exception ex)
            {
                var mw = GetMainWindow();
                if (mw != null)
                    await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Editor.DeletePageFailed", ex.Message));
                else
                    MessageBox.Show(LocalizationService.Format("Editor.DeletePageFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ApplyDocumentSnapshotAsync(byte[] snapshotBytes, int focusPageIndex)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
                return;

            await WriteDocumentBytesAsync(_currentPdfPath, snapshotBytes);
            await LoadPdf(_currentPdfPath);

            if (_pageControls.Count > 0)
                JumpToPage(Math.Max(0, Math.Min(focusPageIndex, _pageControls.Count - 1)));

            RecentFilesService.UpdateMetadata(_currentPdfPath, _pageControls.Count, File.GetLastWriteTimeUtc(_currentPdfPath));
        }

        private static async Task WriteDocumentBytesAsync(string filePath, byte[] snapshotBytes)
        {
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(filePath) ?? string.Empty,
                $"{System.IO.Path.GetFileName(filePath)}.{Guid.NewGuid():N}.snapshot");

            try
            {
                await File.WriteAllBytesAsync(tempPath, snapshotBytes);
                File.Copy(tempPath, filePath, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
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
                    e.SelectedStrokes, e.SelectedTextContainers);

                moveAction.ExecuteInitialTransfer();
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
            PenIcon.Foreground = new SolidColorBrush(_penColor);
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
            // Distinct checked tint: blue pen icon while pen-only drawing
            // is active, neutral gray otherwise.
            PenOnlyIcon.Foreground = new SolidColorBrush(
                PenOnlyButton.IsChecked == true
                    ? Color.FromRgb(0x00, 0x78, 0xD4)
                    : Color.FromRgb(0x55, 0x55, 0x55));
        }

        #region Task 23: pen preset slots

        /// <summary>
        /// Task 23: builds the 3 preset slot circles into
        /// <see cref="PresetSlotsPanel"/> (XAML placeholder between the
        /// Highlighter and Eraser buttons) and fills the 3 default presets
        /// on first use (empty persisted list). Defaults are filled HERE on
        /// load — AppSettingsService.Sanitize only deep-copies (spec).
        /// </summary>
        private void InitializePenPresetSlots()
        {
            var settings = AppSettingsService.Load();
            if (settings.PenPresets == null || settings.PenPresets.Count == 0)
            {
                settings.PenPresets = BuildDefaultPenPresets();
                AppSettingsService.Save(settings);
            }

            for (int i = 0; i < PenPresetSlotCount; i++)
            {
                int slotIndex = i;
                var slot = new Border
                {
                    Width = 22,
                    Height = 22,
                    VerticalAlignment = VerticalAlignment.Center,
                    CornerRadius = new CornerRadius(11),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, i < PenPresetSlotCount - 1 ? 6 : 0, 0),
                    Child = new TextBlock
                    {
                        Text = "P",
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                // Left-click applies the preset; right-click captures the
                // current Pen/Highlighter state into the slot.
                slot.MouseLeftButtonUp += (s, e) => { ApplyPenPreset(slotIndex); e.Handled = true; };
                slot.MouseRightButtonUp += (s, e) => { CapturePenPreset(slotIndex); e.Handled = true; };

                _presetSlots[i] = slot;
                PresetSlotsPanel.Children.Add(slot);
            }

            UpdatePresetSlotVisuals();
        }

        /// <summary>Task 23: first-use defaults — Pen black 2, Highlighter yellow 8, Pen red 3.</summary>
        private static List<PenPreset> BuildDefaultPenPresets()
        {
            return new List<PenPreset>
            {
                new PenPreset { Tool = "Pen", ColorHex = "#000000", Size = 2 },
                new PenPreset { Tool = "Highlighter", ColorHex = "#FFFF00", Size = 8 },
                new PenPreset { Tool = "Pen", ColorHex = "#FF0000", Size = 3 }
            };
        }

        /// <summary>
        /// Task 23: exactly <see cref="PenPresetSlotCount"/> presets from the
        /// persisted list, padding missing/null entries with the defaults
        /// (hand-trimmed settings.json stays UI-safe; capture persists the
        /// normalized list).
        /// </summary>
        private static List<PenPreset> NormalizePenPresets(List<PenPreset> presets)
        {
            var defaults = BuildDefaultPenPresets();
            var result = new List<PenPreset>(PenPresetSlotCount);
            for (int i = 0; i < PenPresetSlotCount; i++)
                result.Add(i < presets?.Count && presets[i] != null ? presets[i] : defaults[i]);
            return result;
        }

        /// <summary>
        /// Task 23: applies a preset slot — switches the active tool to the
        /// preset's (Pen/Highlighter), loads its color/size into the session
        /// fields and re-applies to all pages immediately. The popup size
        /// sliders + preview lines are resynced so reopening a popup shows
        /// the applied state.
        /// </summary>
        private void ApplyPenPreset(int slotIndex)
        {
            var preset = NormalizePenPresets(AppSettingsService.Load().PenPresets)[slotIndex];
            bool isHighlighter = string.Equals(preset.Tool, "Highlighter", StringComparison.OrdinalIgnoreCase);
            var color = TryParseRecentColor(preset.ColorHex, out var parsed) ? parsed : (isHighlighter ? Colors.Yellow : Colors.Black);

            if (isHighlighter)
            {
                _highlighterColor = color;
                _highlighterSize = preset.Size;
            }
            else
            {
                _penColor = color;
                _penSize = preset.Size;
            }

            CloseToolPopups();
            var tool = isHighlighter ? ToolType.Highlighter : ToolType.Pen;
            if (_currentTool != tool)
                ActivateTool(tool); // button states + ApplyToolToAllPages
            else
                ApplyToolToAllPages();
            UpdateToolIconColors();
            UpdatePresetSlotVisuals();

            // Resync the popups' size slider + preview line so they reflect
            // the applied preset (setting slider.Value re-fires the popup's
            // own size-changed handler — idempotent for the same value).
            if (_penPopupSizeSlider != null)
                _penPopupSizeSlider.Value = _penSize;
            if (_penPopupSizePreview != null)
                _penPopupSizePreview.Stroke = new SolidColorBrush(_penColor);
            if (_highlighterPopupSizeSlider != null)
                _highlighterPopupSizeSlider.Value = _highlighterSize;
            if (_highlighterPopupSizePreview != null)
                _highlighterPopupSizePreview.Stroke = new SolidColorBrush(Color.FromArgb(140, _highlighterColor.R, _highlighterColor.G, _highlighterColor.B));
        }

        /// <summary>
        /// Task 23: captures the CURRENT Pen/Highlighter tool state (tool
        /// kind, color, size) into a preset slot and persists it. Capturing
        /// from any other tool is rejected with a toast hint.
        /// </summary>
        private void CapturePenPreset(int slotIndex)
        {
            if (_currentTool != ToolType.Pen && _currentTool != ToolType.Highlighter)
            {
            GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.SelectPenFirst"), "\uED63", 1500);
                return;
            }

            bool isHighlighter = _currentTool == ToolType.Highlighter;
            var color = isHighlighter ? _highlighterColor : _penColor;
            var captured = new PenPreset
            {
                Tool = isHighlighter ? "Highlighter" : "Pen",
                ColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
                Size = isHighlighter ? _highlighterSize : _penSize
            };

            SaveSetting(s =>
            {
                var presets = NormalizePenPresets(s.PenPresets);
                presets[slotIndex] = captured;
                s.PenPresets = presets;
            });

            UpdatePresetSlotVisuals();
            GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.PresetSaved", slotIndex + 1), "\uE74E", 1500);
        }

        /// <summary>
        /// Task 23: refreshes every slot circle — preset color fill, P/H
        /// tool letter (contrast-aware) and the accent ring on the slot
        /// matching the CURRENT tool + color + size. Called from
        /// ApplyToolToAllPages so every state change path stays in sync.
        /// </summary>
        private void UpdatePresetSlotVisuals()
        {
            var presets = NormalizePenPresets(AppSettingsService.Load().PenPresets);
            for (int i = 0; i < PenPresetSlotCount; i++)
            {
                var slot = _presetSlots[i];
                if (slot == null)
                    continue;

                var preset = presets[i];
                bool isHighlighter = string.Equals(preset.Tool, "Highlighter", StringComparison.OrdinalIgnoreCase);
                var color = TryParseRecentColor(preset.ColorHex, out var parsed)
                    ? parsed
                    : (isHighlighter ? Colors.Yellow : Colors.Black);

                bool isActive = isHighlighter
                    ? _currentTool == ToolType.Highlighter && _highlighterColor == color && Math.Abs(_highlighterSize - preset.Size) < 0.001
                    : _currentTool == ToolType.Pen && _penColor == color && Math.Abs(_penSize - preset.Size) < 0.001;

                slot.Background = new SolidColorBrush(color);
                slot.BorderBrush = new SolidColorBrush(isActive
                    ? Color.FromRgb(0x25, 0x63, 0xEB)
                    : Color.FromArgb(60, 0, 0, 0));
                slot.BorderThickness = new Thickness(isActive ? 2 : 1);

                if (slot.Child is TextBlock letter)
                {
                    letter.Text = isHighlighter ? "H" : "P";
                    double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                    letter.Foreground = new SolidColorBrush(luminance > 140 ? Colors.Black : Colors.White);
                }

                var toolName = isHighlighter
                    ? LocalizationService.Get("Editor.ModeHighlighter")
                    : LocalizationService.Get("Editor.ModePen");
                slot.ToolTip = LocalizationService.Format(
                    "Editor.PresetTooltip",
                    i + 1,
                    toolName,
                    preset.ColorHex,
                    preset.Size.ToString("0.##", LocalizationService.CurrentCulture),
                    LocalizationService.Get("Editor.PresetClickApply"),
                    LocalizationService.Get("Editor.PresetRightClickSave"));
            }
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
            RulerIcon.Foreground = new SolidColorBrush(
                visible
                    ? Color.FromRgb(0x00, 0x78, 0xD4)
                    : Color.FromRgb(0x55, 0x55, 0x55));

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

            // Semi-transparent body.
            ruler.Children.Add(new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0x66, 0x66, 0x66)),
                StrokeThickness = 1,
                RadiusX = 6,
                RadiusY = 6,
                IsHitTestVisible = false
            });

            // Tick marks along the top edge: minor every 10px, major every
            // 50px (labels intentionally skipped in v1).
            var tickBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x33, 0x33, 0x33));
            for (double x = 10; x < RulerLength; x += 10)
            {
                bool major = Math.Abs(x % 50) < 0.01;
                ruler.Children.Add(new Line
                {
                    X1 = x, Y1 = 0,
                    X2 = x, Y2 = major ? 12 : 6,
                    Stroke = tickBrush,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                });
            }

            // Centre rotation handle dot.
            ruler.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0x78, 0xD4)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });

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
        private (Point A, Point B)? GetRulerEdgeEndpoints()
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

            return (
                new Point(
                    _rulerCenter.X - halfLen * dirX + halfHeight * upX,
                    _rulerCenter.Y - halfLen * dirY + halfHeight * upY),
                new Point(
                    _rulerCenter.X + halfLen * dirX + halfHeight * upX,
                    _rulerCenter.Y + halfLen * dirY + halfHeight * upY));
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

        private void ImmersiveModeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleImmersiveMode();
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
        /// Pauses native rendering and releases display-only bitmaps when this
        /// editor is behind another tab. Annotation state remains in memory.
        /// </summary>
        public void SetHostActive(bool isActive)
        {
            if (_resourcesReleased || _isHostActive == isActive)
                return;

            _isHostActive = isActive;
            foreach (var page in _pageControls)
                page.SetHostActive(isActive);

            if (!isActive)
            {
                CancelRenderWork();
                _thumbnailLoadCts?.Cancel();
                _thumbnailLoadCts?.Dispose();
                _thumbnailLoadCts = null;
                _thumbnailPagesLoading.Clear();
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

        /// <summary>Final tab-close cleanup for timers, hooks, bitmaps and the native PDF document.</summary>
        public async Task ReleaseResourcesAsync()
        {
            if (_resourcesReleased)
                return;

            SetHostActive(false);
            _resourcesReleased = true;
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
            DisposeSelectablePdfDocument();
            ClearThumbnailCache();
            await _pdfService.DisposeAsync();
        }

        private void ApplyToolToAllPages(AppSettings settings = null)
        {
            settings ??= _applicationSettings ?? AppSettingsService.Load();

            // Task 15: pen-only mode — keep the toolbar toggle in sync with
            // the persisted setting (also covers startup via the ctor's
            // ActivateTool(None)) and propagate to every page below.
            PenOnlyButton.IsChecked = settings.PenOnlyMode;
            UpdatePenOnlyButtonVisual();

            // Task 23: preset slot active rings follow the current tool,
            // color and size (every state-change path funnels through here).
            UpdatePresetSlotVisuals();

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
                        atts.Color = _highlighterColor;
                        atts.Width = _highlighterSize;
                        atts.Height = _highlighterSize;
                        atts.IsHighlighter = true;
                        page.SetInkAttributes(atts);
                        break;
                    case ToolType.HiddenInk:
                        page.HiddenInkMaskColor = Colors.White;
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
            if (PageNumberLabel == null || PageCountText == null) return;

            if (_pageControls.Count == 0)
            {
                PageNumberLabel.Text = "0";
                PageCountText.Text = "/ 0";
                return;
            }

            int currentPageNumber = GetCurrentPageIndex() + 1;
            PageNumberLabel.Text = currentPageNumber.ToString();
            PageCountText.Text = $"/ {_pageControls.Count}";
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
            if (_isDirty && !string.IsNullOrEmpty(_currentPdfPath))
            {
                await AutoSaveAsync();
            GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.AutoSaved"));
            }
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
            if (string.IsNullOrEmpty(_currentPdfPath)) return;

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
                    try
                    {
                        var data = await Services.VersionControlService.LoadVersionAsync(vFile);
                        if (data != null)
                        {
                            // 恢复前先把当前注释快照为新版本（最新），使恢复可逆
                            var current = CollectAnnotations();
                            await Services.VersionControlService.SaveVersionAsync(_currentPdfPath, current);

                            ClearAllAnnotations();
                            // A restored snapshot is a new document state;
                            // actions from the previous snapshot must not be
                            // able to reinsert its annotations via Ctrl+Z.
                            ClearUndoRedoHistory();
                            _pdfService.ExtractedAnnotations = data;
                            await LoadAnnotationsFromPdfServiceAsync();
            GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.RestoredVersion", dt.ToString("g", LocalizationService.CurrentCulture)));
                            MarkDirty();
                        }
                    }
                    catch (Exception ex)
                    {
            GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.VersionLoadFailed"));
                        Console.WriteLine($"[VersionHistory] Error: {ex.Message}");
                    }
                };
                menu.Items.Add(item);
            }

            PopupZOrderHelper.FixContextMenuTopmost(menu);
            menu.PlacementTarget = VersionHistoryButton;
            menu.IsOpen = true;
        }

        private async void PrintPdf_Click(object sender, RoutedEventArgs e)
        {
            await PrintPdfAsync();
        }

        private void PdfScrollViewer_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
        }

        private async void ContextMenu_PrintClick(object sender, RoutedEventArgs e)
        {
            await PrintPdfAsync();
        }

        private async void ExportCurrentPagePng1x_Click(object sender, RoutedEventArgs e) => await ExportPngAsync(false, 1.0);
        private async void ExportCurrentPagePng2x_Click(object sender, RoutedEventArgs e) => await ExportPngAsync(false, 2.0);
        private async void ExportAllPagesPng1x_Click(object sender, RoutedEventArgs e) => await ExportPngAsync(true, 1.0);
        private async void ExportAllPagesPng2x_Click(object sender, RoutedEventArgs e) => await ExportPngAsync(true, 2.0);

        private async Task ExportPngAsync(bool allPages, double dpiScale)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath) || _pageControls.Count == 0)
                return;

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

                var pages = await BuildPrintablePagesAsync(true, dpiScale);
                IEnumerable<int> indexes = allPages
                    ? Enumerable.Range(0, pages.Count)
                    : new[] { Math.Max(0, Math.Min(GetCurrentPageIndex(), pages.Count - 1)) };
                foreach (int index in indexes)
                {
                    string outputPath = allPages
                        ? System.IO.Path.Combine(folder, $"{baseName}_page_{index + 1:000}.png")
                        : singlePath;
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(pages[index].Bitmap));
                    using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    encoder.Save(stream);
                }

                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.PngExported", allPages ? pages.Count : 1, dpiScale.ToString("0.#", LocalizationService.CurrentCulture)),
                    "\uE74E",
                    2500);
            }
            catch (Exception ex)
            {
                GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.PngExportFailed", ex.Message), "\uE783", 3500);
            }
        }

        private async void InsertPdfPages_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
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
                GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.SourcePdfReadFailed", ex.Message), "\uE783", 3500);
                return;
            }

            if (!TryPromptPageRange(sourcePageCount, out int startPage, out int endPage))
                return;
            int insertPageIndex = Math.Max(0, GetCurrentPageIndex());
            await InsertExternalDocumentAsync(() => _pdfService.InsertPdfPagesAsync(
                _currentPdfPath, dialog.FileName, insertPageIndex, startPage, endPage),
                insertPageIndex,
                endPage - startPage + 1,
                LocalizationService.Get("Editor.PdfPagesInserted"));
        }

        private async void InsertImagePage_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
                return;
            var dialog = new OpenFileDialog { Filter = LocalizationService.Get("Editor.ImageFileFilter"), Multiselect = false };
            if (dialog.ShowDialog() != true)
                return;
            int insertPageIndex = Math.Max(0, GetCurrentPageIndex());
            await InsertExternalDocumentAsync(() => _pdfService.InsertImagePageAsync(
                _currentPdfPath, dialog.FileName, insertPageIndex),
                insertPageIndex,
                1,
                LocalizationService.Get("Editor.ImagePageInserted"));
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
            string successMessage)
        {
            byte[] before = null;
            int focusBefore = 0;
            List<PageBookmark> beforeBookmarks = null;
            bool operationMayHaveChangedDocument = false;
            try
            {
                if (_isDirty && !await AutoSaveAsync())
                    return;
                before = await File.ReadAllBytesAsync(_currentPdfPath);
                focusBefore = GetCurrentPageIndex();
                beforeBookmarks = PageBookmarkService.Load(_currentPdfPath).ToList();
                operationMayHaveChangedDocument = true;
                await operation();
                byte[] after = await File.ReadAllBytesAsync(_currentPdfPath);
                await LoadPdf(_currentPdfPath);
                int focused = Math.Max(0, Math.Min(insertPageIndex, _pageControls.Count - 1));
                JumpToPage(focused);
                var afterBookmarks = PageBookmarkService.ApplyPageInsert(
                    _currentPdfPath,
                    insertPageIndex,
                    insertedPageCount).ToList();
                RefreshBookmarks();
                PushUndoAction(new DocumentSnapshotAction(
                    this,
                    before,
                    after,
                    focusBefore,
                    focused,
                    beforeBookmarks,
                    afterBookmarks));
                GetMainWindow()?.ShowToast(successMessage, "\uE710", 2000);
            }
            catch (Exception ex)
            {
                if (operationMayHaveChangedDocument && before != null && !string.IsNullOrWhiteSpace(_currentPdfPath))
                {
                    try
                    {
                        // The PDF write and bookmark sidecar update are separate files. If
                        // either half fails before the undo action is registered, restore
                        // both snapshots so a failed import cannot leave page indices stale.
                        await WriteDocumentBytesAsync(_currentPdfPath, before);
                        PageBookmarkService.Replace(_currentPdfPath, beforeBookmarks ?? new List<PageBookmark>());
                        await LoadPdf(_currentPdfPath);
                        JumpToPage(Math.Max(0, Math.Min(focusBefore, _pageControls.Count - 1)));
                        RefreshBookmarks();
                    }
                    catch (Exception rollbackException)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Import] Rollback failed: {rollbackException}");
                    }
                }
                GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.ImportFailed", ex.Message), "\uE783", 3500);
            }
        }

        private async void RotateCurrentPage_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath) || _pageControls.Count == 0)
                return;
            try
            {
                if (_isDirty && !await AutoSaveAsync())
                    return;
                int pageIndex = GetCurrentPageIndex();
                byte[] before = await File.ReadAllBytesAsync(_currentPdfPath);
                await _pdfService.RotatePageAsync(_currentPdfPath, pageIndex, 1);
                byte[] after = await File.ReadAllBytesAsync(_currentPdfPath);
                await LoadPdf(_currentPdfPath);
                JumpToPage(pageIndex);
                PushUndoAction(new DocumentSnapshotAction(this, before, after, pageIndex, pageIndex));
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageRotated"), "\uE7AD", 1800);
            }
            catch (Exception ex)
            {
                GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.RotateFailed", ex.Message), "\uE783", 3500);
            }
        }

        private async Task PrintPdfAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
            {
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                return;
            }

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            string originalLoadingText = LoadingText.Text;
            ShowLoadingOverlay();
            LoadingText.Text = LocalizationService.Get("Editor.PreparingPrint");

            try
            {
                var pages = await BuildPrintablePagesAsync(includeAnnotations: true);
                if (pages.Count == 0)
                    throw new InvalidOperationException(LocalizationService.Get("Editor.NoPagesToPrint"));

                var printDocument = CreatePrintDocument(pages, dialog);
                dialog.PrintDocument(printDocument.DocumentPaginator, System.IO.Path.GetFileName(_currentPdfPath));
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PrintSent"), "\uE749", 1500);
            }
            catch (Exception ex)
            {
                var mw = GetMainWindow();
                if (mw != null)
                    await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Editor.PrintFailed", ex.Message));
                else
                    MessageBox.Show(LocalizationService.Format("Editor.PrintFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingText.Text = originalLoadingText;
                HideLoadingOverlay();
            }
        }

        private async Task<IReadOnlyList<PrintablePageImage>> BuildPrintablePagesAsync(bool includeAnnotations, double dpiScale = 1.0)
        {
            string tempPrintPath = null;

            try
            {
                string renderPath = _currentPdfPath;
                if (includeAnnotations)
                {
                    string tempDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Caelum", "Print");
                    Directory.CreateDirectory(tempDirectory);
                    tempPrintPath = System.IO.Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.pdf");
                    File.Copy(_currentPdfPath, tempPrintPath, true);
                    await _pdfService.SaveAnnotationsToPdfAsync(tempPrintPath, CollectAnnotations());
                    renderPath = tempPrintPath;
                }

                return await Task.Run(() => RenderPrintablePages(renderPath, includeAnnotations, dpiScale));
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPrintPath) && File.Exists(tempPrintPath))
                {
                    try { File.Delete(tempPrintPath); } catch { }
                }
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

        private async Task PromptSaveAsForDraftAsync()
        {
            if (_hasPromptedForSaveAs || string.IsNullOrWhiteSpace(_currentPdfPath))
                return;

            _hasPromptedForSaveAs = true;
            _promptSaveAsAfterLoad = false;

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
                return;

            var oldPath = _currentPdfPath;
            var newPath = dialog.FileName;

            try
            {
                if (_isDirty && !await AutoSaveAsync())
                    return;

                if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(newPath) ?? string.Empty);
                    File.Copy(oldPath, newPath, true);
                }

                RecentFilesService.UpdatePath(oldPath, newPath);
                RecentFilesService.AddOrPromote(newPath, _pageControls.Count, File.GetLastWriteTimeUtc(newPath), _pendingLibraryFolderId, true);
                UpdateCurrentPdfPath(newPath);
                _isNotebookDraft = false;
                GetMainWindow()?.HandleFilePathChanged(oldPath, newPath);
                GetMainWindow()?.ShowToast(LocalizationService.Get("Home.NotebookSaved"), "\uE74E");

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
            }
            catch (Exception ex)
            {
                var mw = GetMainWindow();
                if (mw != null)
                    await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Home.CreateNotebookFailed", ex.Message));
                else
                    MessageBox.Show(LocalizationService.Format("Home.CreateNotebookFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void FitWidthButton_Click(object sender, RoutedEventArgs e) => ApplyFitZoom(fitPage: false);

        private void FitPageButton_Click(object sender, RoutedEventArgs e) => ApplyFitZoom(fitPage: true);

        private void ApplyFitZoom(bool fitPage)
        {
            if (_pageControls.Count == 0 || IsSelectablePdfSurfaceActive)
                return;

            var page = _pageControls[Math.Max(0, Math.Min(GetCurrentPageIndex(), _pageControls.Count - 1))];
            double viewportWidth = Math.Max(200, PdfScrollViewer.ViewportWidth - 72);
            double viewportHeight = Math.Max(200, PdfScrollViewer.ViewportHeight - 72);
            double widthRatio = viewportWidth / Math.Max(1, page.Width);
            double heightRatio = viewportHeight / Math.Max(1, page.Height);
            double level = fitPage ? Math.Min(widthRatio, heightRatio) : widthRatio;
            ApplyCustomZoom(Math.Max(ZoomMin, Math.Min(ZoomMax, level)));
            JumpToPage(page.PageIndex);
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

        private void PageJumpBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_pageControls.Count == 0)
                return;

            if (PageNumberTextBox.Visibility == Visibility.Visible)
                return;

            PageNumberLabel.Visibility = Visibility.Collapsed;
            PageNumberTextBox.Text = (GetCurrentPageIndex() + 1).ToString();
            PageNumberTextBox.Visibility = Visibility.Visible;
            PageNumberTextBox.Focus();
            PageNumberTextBox.SelectAll();
            e.Handled = true;
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
                HidePageNumberTextBox();
                e.Handled = true;
            }
        }

        private void PageNumberTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (PageNumberTextBox.Visibility == Visibility.Visible)
                ApplyPageJumpFromTextBox();
        }

        private void ApplyPageJumpFromTextBox()
        {
            if (_pageControls.Count == 0)
            {
                HidePageNumberTextBox();
                return;
            }

            if (int.TryParse(PageNumberTextBox.Text.Trim(), out int requestedPage))
            {
                requestedPage = Math.Max(1, Math.Min(_pageControls.Count, requestedPage));
                JumpToPage(requestedPage - 1);
            }

            HidePageNumberTextBox();
        }

        private void HidePageNumberTextBox()
        {
            PageNumberTextBox.Visibility = Visibility.Collapsed;
            PageNumberLabel.Visibility = Visibility.Visible;
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
            UpdateSelectedTextBoxPopupVisibility(forceRefresh: true);
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
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.10, Color = Colors.Black }
            };
            border.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");

            var deleteButton = new Button
            {
                Width = 28,
                Height = 28,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = LocalizationService.Get("Editor.DeleteTooltip"),
                Margin = new Thickness(0)
            };
            _textDeleteButton = deleteButton;
            deleteButton.Template = CreateIconButtonTemplate("#FEE2E2", "#FECACA");
            deleteButton.Content = new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteButton.Click += (s, e) => DeleteSelectedTextBox();

            var sep1 = ThemeDivider(new Border
            {
                Width = 1,
                Height = 18,
                Background = new SolidColorBrush(Color.FromArgb(24, 15, 23, 42)),
                Margin = new Thickness(6, 5, 6, 5),
                VerticalAlignment = VerticalAlignment.Center
            });

            var decreaseFontButton = new Button
            {
                Width = 30,
                Height = 28,
                Margin = new Thickness(0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = LocalizationService.Get("Editor.SmallerText"),
                Content = CreateTextSizeButtonContent(increase: false)
            };
            _textDecreaseFontButton = decreaseFontButton;
            decreaseFontButton.Template = CreateIconButtonTemplate("#E5E7EB", "#D1D5DB");
            decreaseFontButton.Click += (s, e) => AdjustSelectedTextBoxFontSize(increase: false);

            var increaseFontButton = new Button
            {
                Width = 30,
                Height = 28,
                Margin = new Thickness(0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = LocalizationService.Get("Editor.BiggerText"),
                Content = CreateTextSizeButtonContent(increase: true)
            };
            _textIncreaseFontButton = increaseFontButton;
            increaseFontButton.Template = CreateIconButtonTemplate("#E5E7EB", "#D1D5DB");
            increaseFontButton.Click += (s, e) => AdjustSelectedTextBoxFontSize(increase: true);

            var fontButtonGroup = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(24, 15, 23, 42)),
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
                            VerticalAlignment = VerticalAlignment.Center,
                            Background = new SolidColorBrush(Color.FromArgb(30, 15, 23, 42))
                        }),
                        increaseFontButton
                    }
                }
            };

            var sep2 = ThemeDivider(new Border
            {
                Width = 1,
                Height = 18,
                Background = new SolidColorBrush(Color.FromArgb(24, 15, 23, 42)),
                Margin = new Thickness(6, 5, 6, 5),
                VerticalAlignment = VerticalAlignment.Center
            });

            _colorIndicator = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(_textColor),
                BorderBrush = new SolidColorBrush(Color.FromArgb(36, 15, 23, 42)),
                BorderThickness = new Thickness(1)
            };
            var colorButton = new Button
            {
                Content = _colorIndicator,
                Width = 28,
                Height = 28,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };
            colorButton.Template = CreateIconButtonTemplate("#E0E7FF", "#DBEAFE");
            var colorPopup = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            _textColorPopup = colorPopup;
            PopupZOrderHelper.FixPopupTopmost(colorPopup);

            int cols = 12;
            int rows = 8;
            double cellSize = 20;
            var paletteGrid = new Grid { Width = cols * cellSize, Height = rows * cellSize, ClipToBounds = true };

            var selectionIndicator = new Border
            {
                Width = cellSize, Height = cellSize,
                BorderBrush = Brushes.White, BorderThickness = new Thickness(2),
                Background = Brushes.Transparent, IsHitTestVisible = false,
                Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
            };

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

                    var cell = new Border
                    {
                        Width = cellSize, Height = cellSize,
                        Background = new SolidColorBrush(cellColor),
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(col * cellSize, row * cellSize, 0, 0),
                        Cursor = Cursors.Hand, Tag = cellColor
                    };

                    cell.MouseLeftButtonDown += (s, ev) =>
                    {
                        var b = s as Border;
                        var picked = (Color)b.Tag;
                        selectionIndicator.Margin = b.Margin;
                        selectionIndicator.Visibility = Visibility.Visible;
                        ApplyTextColor(picked);
                        ev.Handled = true;
                    };

                    paletteGrid.Children.Add(cell);
                }
            }

            foreach (Border cell in paletteGrid.Children)
            {
                if (cell.Tag is Color c && c == _textColor)
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
            var recentRow = new StackPanel { Orientation = Orientation.Horizontal };
            _textRecentLabel = ThemeSubtleHeader(new TextBlock
            {
                Text = LocalizationService.Get("Editor.Recent"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 8)
            });
            recentSection.Children.Add(_textRecentLabel);
            recentSection.Children.Add(recentRow);

            var colorPopupBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(250, 248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(28, 15, 23, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = new StackPanel { Margin = new Thickness(16), Children = { recentSection, paletteGrid } },
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.18, Color = Colors.Black }
            };
            colorPopupBorder.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceBrush");
            colorPopupBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
            colorPopup.Child = colorPopupBorder;
            colorPopup.Opened += (s, e) => RefreshRecentColorsRow(recentSection, recentRow, () => AppSettingsService.Load().RecentTextColors, ApplyTextColor);
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
                Background = new SolidColorBrush(Color.FromArgb(24, 15, 23, 42)),
                Margin = new Thickness(6, 5, 6, 5),
                VerticalAlignment = VerticalAlignment.Center
            });

            _textBoldButton = new ToggleButton
            {
                Content = "B",
                Width = 28,
                Height = 28,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = LocalizationService.Get("Editor.BoldTooltip")
            };
            _textItalicButton = new ToggleButton
            {
                Content = "I",
                Width = 28,
                Height = 28,
                FontStyle = FontStyles.Italic,
                Cursor = Cursors.Hand,
                ToolTip = LocalizationService.Get("Editor.ItalicTooltip")
            };
            _textBoldButton.Click += (_, __) => ApplySelectedTextFormat(tb =>
                tb.FontWeight = _textBoldButton.IsChecked == true ? FontWeights.Bold : FontWeights.Normal);
            _textItalicButton.Click += (_, __) => ApplySelectedTextFormat(tb =>
                tb.FontStyle = _textItalicButton.IsChecked == true ? FontStyles.Italic : FontStyles.Normal);

            _textFontFamilyCombo = new ComboBox
            {
                Width = 104,
                Height = 28,
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
                Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
                ItemsSource = new[] { "Left", "Center", "Right" },
                Style = (Style)Application.Current.FindResource("CompactComboBox"),
                ToolTip = LocalizationService.Get("Editor.AlignmentTooltip")
            };
            PopupZOrderHelper.FixComboBoxPopupTopmost(_textFontFamilyCombo);
            PopupZOrderHelper.FixComboBoxPopupTopmost(_textAlignmentCombo);
            _textAlignmentCombo.SelectionChanged += (_, __) =>
            {
                if (_textAlignmentCombo.SelectedItem is string alignment)
                    ApplySelectedTextFormat(tb => tb.TextAlignment = ParseTextAlignment(alignment));
            };

            panel.Children.Add(formatSeparator);
            panel.Children.Add(_textBoldButton);
            panel.Children.Add(_textItalicButton);
            panel.Children.Add(_textFontFamilyCombo);
            panel.Children.Add(_textAlignmentCombo);

            _inlineTextBoxToolbar = border;
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
                        b.BorderBrush = isSelected
                            ? new SolidColorBrush(Color.FromArgb(90, 0, 120, 212))
                            : Brushes.Transparent;
                        b.BorderThickness = isSelected ? new Thickness(1.5) : new Thickness(0);
                        b.Background = isSelected
                            ? new SolidColorBrush(Color.FromArgb(10, 0, 120, 212))
                            : Brushes.Transparent;
                    }
                    else if (child is Border handle && handle.Cursor == Cursors.SizeAll)
                    {
                        handle.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
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
                _textAlignmentCombo.SelectedItem = _selectedTextBox.TextAlignment.ToString();
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
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                VerticalAlignment = VerticalAlignment.Center
            };
            sizeGlyph.SetResourceReference(TextElement.ForegroundProperty, "ThemeForegroundBrush");

            var directionGlyph = new TextBlock
            {
                Text = increase ? "^" : "v",
                FontSize = 8,
                Margin = new Thickness(1, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
                VerticalAlignment = increase ? VerticalAlignment.Top : VerticalAlignment.Bottom
            };
            directionGlyph.SetResourceReference(TextElement.ForegroundProperty, "ThemeSubtleForegroundBrush");

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
            container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Visual chrome border spanning both columns
            var chrome = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = select ? new Thickness(1.5) : new Thickness(0),
                BorderBrush = select ? new SolidColorBrush(Color.FromArgb(90, 0, 120, 212)) : Brushes.Transparent,
                Background = select ? new SolidColorBrush(Color.FromArgb(10, 0, 120, 212)) : Brushes.Transparent,
                IsHitTestVisible = false,
                Tag = "chrome"
            };
            Grid.SetColumnSpan(chrome, 2);

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
                CaretBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212))
            };

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

            var dragHandle = new TextAnnotationDragHandleBorder
            {
                Width = 18,
                Height = 36,
                Margin = new Thickness(8, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(9),
                Visibility = select ? Visibility.Visible : Visibility.Collapsed,
                Cursor = Cursors.SizeAll,
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 1,
                    Opacity = 0.10,
                    Color = Colors.Black
                }
            };

            var dragIcon = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            for (int column = 0; column < 2; column++)
            {
                var dotColumn = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(column == 0 ? 0 : 2, 0, 0, 0)
                };

                for (int row = 0; row < 3; row++)
                {
                    dotColumn.Children.Add(new Ellipse
                    {
                        Width = 3,
                        Height = 3,
                        Fill = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                        Margin = new Thickness(0, 1.5, 0, 1.5)
                    });
                }

                dragIcon.Children.Add(dotColumn);
            }
            dragHandle.Child = dragIcon;
            AutomationProperties.SetAutomationId(dragHandle, "TextAnnotationDragHandle");
            AutomationProperties.SetName(dragHandle, LocalizationService.Get("Editor.MoveTextBox"));

            Grid.SetColumn(textBox, 0);
            Grid.SetColumn(dragHandle, 1);

            container.Children.Add(chrome);
            container.Children.Add(textBox);
            container.Children.Add(dragHandle);

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
                    Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Cursor = definition.Item4,
                    Focusable = true,
                    Visibility = select ? Visibility.Visible : Visibility.Collapsed,
                    Tag = definition.Item1,
                    ToolTip = LocalizationService.Get("Editor.ResizeTextBox")
                };
                AutomationProperties.SetAutomationId(
                    resizeHandle,
                    TextAnnotationGeometry.GetResizeHandleAutomationId(definition.Item1));
                AutomationProperties.SetName(
                    resizeHandle,
                    LocalizationService.Get("Editor.ResizeTextBox"));
                KeyboardNavigation.SetIsTabStop(resizeHandle, true);
                Grid.SetColumnSpan(resizeHandle, 2);
                Panel.SetZIndex(resizeHandle, 20);
                resizeHandle.MouseLeftButtonDown += TextResizeHandle_MouseLeftButtonDown;
                resizeHandle.MouseMove += TextResizeHandle_MouseMove;
                resizeHandle.MouseLeftButtonUp += TextResizeHandle_MouseLeftButtonUp;
                resizeHandle.KeyDown += TextResizeHandle_KeyDown;
                resizeHandle.StylusDown += TextResizeHandle_StylusDown;
                resizeHandle.StylusMove += TextResizeHandle_StylusMove;
                resizeHandle.StylusUp += TextResizeHandle_StylusUp;
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

            dragHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
            dragHandle.MouseMove += DragHandle_MouseMove;
            dragHandle.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
            dragHandle.StylusDown += DragHandle_StylusDown;
            dragHandle.StylusMove += DragHandle_StylusMove;
            dragHandle.StylusUp += DragHandle_StylusUp;

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

            if (sender is UIElement handle && handle.IsMouseCaptured)
                handle.ReleaseMouseCapture();

            CompleteTextResize();
            e.Handled = true;
        }

        private void TextResizeHandle_StylusUp(object sender, StylusEventArgs e)
        {
            if (_resizingTextContainer == null)
                return;

            if (sender is UIElement handle && handle.IsStylusCaptured)
                handle.ReleaseStylusCapture();

            CompleteTextResize();
            e.Handled = true;
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

            if (restoreBounds)
                ApplyTextContainerBounds(
                    resizingContainer,
                    _textResizeStartBounds,
                    _textResizeStartAutoWidth,
                    _textResizeStartAutoHeight);

            Mouse.Capture(null);
            Stylus.Capture(null);
            _resizingTextContainer = null;
            _resizingTextPage = null;
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border handle && handle.Parent is Grid container && container.Parent is Canvas canvas)
                BeginTextBoxDrag(handle, e.GetPosition(canvas));
            e.Handled = true;
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedContainer?.Parent is Canvas canvas)
                UpdateTextBoxDrag(e.GetPosition(canvas), () =>
                {
                    var handle = _draggedContainer.Children.OfType<Border>().FirstOrDefault(b => b.Cursor == Cursors.SizeAll);
                    handle?.CaptureMouse();
                });
            e.Handled = _isDragging || _dragArmed;
        }

        private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var handle = sender as Border;
            handle?.ReleaseMouseCapture();
            var wasDragging = CompleteTextBoxDrag();
            e.Handled = wasDragging;
        }

        private void DragHandle_StylusDown(object sender, StylusEventArgs e)
        {
            if (sender is Border handle && handle.Parent is Grid container && container.Parent is Canvas canvas)
            {
                BeginTextBoxDrag(handle, e.GetPosition(canvas));
                handle.CaptureStylus();
                e.Handled = true;
            }
        }

        private void DragHandle_StylusMove(object sender, StylusEventArgs e)
        {
            if (_draggedContainer?.Parent is Canvas canvas)
                UpdateTextBoxDrag(e.GetPosition(canvas), () =>
                {
                    var handle = _draggedContainer.Children.OfType<Border>().FirstOrDefault(b => b.Cursor == Cursors.SizeAll);
                    handle?.CaptureStylus();
                });
            e.Handled = _isDragging || _dragArmed;
        }

        private void DragHandle_StylusUp(object sender, StylusEventArgs e)
        {
            (sender as Border)?.ReleaseStylusCapture();
            var wasDragging = CompleteTextBoxDrag();
            e.Handled = wasDragging;
        }

        private void BeginTextBoxDrag(Border handle, Point pressPoint)
        {
            if (_currentTool != ToolType.Text || handle?.Parent is not Grid container)
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
                            new List<System.Windows.Ink.Stroke>(),
                            new List<System.Windows.Controls.Grid> { _draggedContainer });

                        if (sourcePage.HasSelection && sourcePage.SelectedTextContainers.Contains(_draggedContainer))
                            sourcePage.ClearSelection();

                        moveAction.ExecuteInitialTransfer();
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
            return wasDragging;
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
                    PushUndoAction(new ItemsAddedAction(
                        page,
                        new List<System.Windows.Ink.Stroke>(),
                        new List<Grid> { container }));
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

            try
            {
                // Flush an active TextBox edit before collecting annotations so
                // Ctrl+S cannot save clean and then create a new dirty undo
                // action when focus eventually leaves the editor.
                CommitTextEditSession();
                var annotations = CollectAnnotations();
                long saveGeneration = _dirtyGeneration;

                // The PDF is the source of truth. Only create a history sidecar
                // after the atomic PDF save succeeds, otherwise a failed save
                // would leave a misleading "ghost" version behind.
                await _pdfService.SaveAnnotationsToPdfAsync(_currentPdfPath, annotations);
                await Services.VersionControlService.SaveVersionAsync(_currentPdfPath, annotations);
                if (_dirtyGeneration == saveGeneration)
                    _isDirty = false;

                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.SavedSuccessfully"));
            }
            catch (Exception ex)
            {
                var mw = GetMainWindow();
                if (mw != null)
                    await DialogService.ShowErrorAsync(mw, LocalizationService.Get("Common.Error"), LocalizationService.Format("Editor.SaveFailed", ex.Message));
                else
                    MessageBox.Show(LocalizationService.Format("Editor.SaveFailed", ex.Message), LocalizationService.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task<bool> AutoSaveAsync()
        {
            if (!_isDirty || string.IsNullOrEmpty(_currentPdfPath)) return false;
            try
            {
                CommitTextEditSession();
                var annotations = CollectAnnotations();
                long saveGeneration = _dirtyGeneration;

                // Keep autosave history transactional with the document save.
                await _pdfService.SaveAnnotationsToPdfAsync(_currentPdfPath, annotations);
                await Services.VersionControlService.SaveVersionAsync(_currentPdfPath, annotations);
                if (_dirtyGeneration != saveGeneration)
                {
                    _isDirty = true;
                    return false;
                }

                _isDirty = false;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoSave] Failed: {ex}");
                GetMainWindow()?.ShowToast(LocalizationService.Format("Editor.AutoSaveFailed", ex.Message), "\uE783", 3500);
                return false;
            }
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
                            X = x,
                            Y = y,
                            Text = note.Text
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
            return Application.Current.MainWindow as MainWindow;
        }

        private async Task LoadAnnotationsFromPdfServiceAsync()
        {
            if (_pdfService.ExtractedAnnotations == null || _pdfService.ExtractedAnnotations.Count == 0) return;

            try
            {
                _isLoadingAnnotations = true;
                foreach (var page in _pageControls)
                {
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
                System.Diagnostics.Debug.WriteLine($"LoadAnnotationsFromPdfServiceAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _isLoadingAnnotations = false;
            }
            await Task.CompletedTask;
        }

        private void PageControl_InkMutated(object sender, EventArgs e) => MarkDirty();

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

            if (page.GetOverlayData(container) is StickyNoteAnnotation note)
                OpenStickyNoteEditor(page, container, note);
        }

        private void OpenStickyNoteEditor(PdfPageControl page, Grid container, StickyNoteAnnotation note)
        {
            CommitStickyNoteEdit();

            _stickyNoteEditingModel = note;
            _stickyNoteEditingOriginalText = note.Text ?? string.Empty;
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
                Margin = new Thickness(0, 0, 0, 8)
            };

            var saveButton = new Button
            {
                Content = LocalizationService.Get("Common.Save"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(12, 5, 12, 5),
                MinWidth = 72
            };
            _stickyNoteSaveButton = saveButton;

            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(_stickyNoteEditor);
            panel.Children.Add(saveButton);

            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = panel
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
            PopupZOrderHelper.FixPopupTopmost(_stickyNotePopup);
            _stickyNotePopup.Closed += StickyNotePopup_Closed;
            saveButton.Click += (_, __) => _stickyNotePopup.IsOpen = false;
            _stickyNotePopup.IsOpen = true;
            _stickyNoteEditor.Focus();
            _stickyNoteEditor.SelectAll();
        }

        private void StickyNotePopup_Closed(object sender, EventArgs e)
        {
            CommitStickyNoteEdit();
        }

        private void CommitStickyNoteEdit()
        {
            if (_stickyNotePopup == null)
                return;

            var popup = _stickyNotePopup;
            var note = _stickyNoteEditingModel;
            var before = _stickyNoteEditingOriginalText ?? string.Empty;
            var after = _stickyNoteEditor?.Text ?? string.Empty;

            _stickyNotePopup = null;
            _stickyNoteEditor = null;
            _stickyNoteSaveButton = null;
            _stickyNoteEditingModel = null;
            _stickyNoteEditingOriginalText = null;

            if (note != null && !string.Equals(before, after, StringComparison.Ordinal))
            {
                note.Text = after;
                PushUndoAction(new StickyNoteEditAction(note, before, after));
                MarkDirty();
            }

            popup.Closed -= StickyNotePopup_Closed;
        }

        private void PageControl_StrokeCollectedUndoable(object sender, System.Windows.Ink.Stroke stroke)
        {
            if (sender is PdfPageControl page)
                PushUndoAction(new StrokeAddedAction(page, stroke));
        }

        private void PageControl_StrokesErased(object sender, StrokesErasedEventArgs e)
        {
            if (sender is PdfPageControl page)
                PushUndoAction(new StrokesErasedAction(page, e.RemovedStrokes, e.AddedStrokes));
        }

        private void PageControl_StrokeRecognized(object sender, StrokeRecognizedEventArgs e)
        {
            if (sender is PdfPageControl page)
                PushUndoAction(new StrokeReplacedAction(page, e.OriginalStroke, e.IdealStroke));
        }

        private void MarkDirty()
        {
            _dirtyGeneration++;
            _isDirty = true;
        }

        private void NavigateBackCore()
        {
            SetHostActive(false);
            if (NavigationService != null && NavigationService.CanGoBack)
                NavigationService.GoBack();
            else if (NavigationService != null)
                NavigationService.Navigate(new HomePage());
        }

        private void ShowLoadingOverlay()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            UpdatePdfSurfaceVisibility();
        }

        private void HideLoadingOverlay()
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            UpdatePdfSurfaceVisibility();
        }

        private void CancelActiveLoad()
        {
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
                pageControl.StrokeCollectedUndoable -= PageControl_StrokeCollectedUndoable;
                pageControl.StrokesErased -= PageControl_StrokesErased;
                pageControl.StrokeRecognized -= PageControl_StrokeRecognized;
                pageControl.ImagesChanged -= PageControl_ImagesChanged;
                pageControl.AreaHighlightCreated -= PageControl_AreaHighlightCreated;
                pageControl.StickyNoteActivated -= PageControl_StickyNoteActivated;
                pageControl.HiddenInkCreated -= PageControl_HiddenInkCreated;
                pageControl.HiddenInkRemoved -= PageControl_HiddenInkRemoved;
                pageControl.HiddenInksRemoved -= PageControl_HiddenInksRemoved;
                pageControl.SelectionChanged -= PageControl_SelectionChanged;
                pageControl.SelectionMoveCompleted -= PageControl_SelectionMoveCompleted;
                pageControl.SelectionResizeCompleted -= PageControl_SelectionResizeCompleted;
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

        private static ControlTemplate CreateIconButtonTemplate(string hoverColor, string pressedColor)
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(4));
            borderFactory.Name = "Root";

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(hoverColor)), "Root"));
            template.Triggers.Add(hoverTrigger);

            var pressTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(pressedColor)), "Root"));
            template.Triggers.Add(pressTrigger);

            return template;
        }

        private static ControlTemplate CreatePageChromeButtonTemplate(string hoverColor, string pressedColor)
        {
            var template = new ControlTemplate(typeof(Button));
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

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(hoverColor)), "Root"));
            template.Triggers.Add(hoverTrigger);

            var pressTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(pressedColor)), "Root"));
            template.Triggers.Add(pressTrigger);

            return template;
        }
    }
}
