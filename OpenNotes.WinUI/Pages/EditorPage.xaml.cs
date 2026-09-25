using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caelum.Controls;
using Caelum.Ink;
using Caelum.InkGeometry;
using Caelum.Models;
using Caelum.Pdf;
using Caelum.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Text;

namespace Caelum.Pages
{
    /// <summary>
    /// V6 WinUI port of WPF <c>Pages/EditorPage.xaml.cs</c> — Task 6 shell
    /// (PDF load/render, scroll/zoom/page navigation, three-tab document
    /// sidebar, toolbar chrome + page-jump navigator, full-text search,
    /// page context menu, loading overlay, tab-close disposal) plus the
    /// complete Task 7 ink toolset: pen/highlighter/eraser with pressure +
    /// scribble recognition (Phase A), and selection lasso/marquee with
    /// move/rotate/scale incl. cross-page moves, shape tools, hidden-ink
    /// masks, ephemeral laser and the viewport-anchored ruler (Phase B),
    /// all driven through the token/snapshot undo/redo pipeline.
    ///
    /// Deliberately deferred, preserving element names + AutomationIds:
    /// T8 — text/select/sticky annotations, search-bound text highlights,
    /// selectable-PDF surface; T9 — save/autosave/dirty-close pipeline,
    /// version history, thumbnail drag-reorder, insert-gap affordances,
    /// page delete buttons, print.
    /// </summary>
    public sealed partial class EditorPage : Page
    {
        // ── Sidebar geometry: the 228/32 DIP content-offset contract. ───────
        private const double SidebarExpandedWidth = 184.0;
        private const double SidebarCollapsedWidth = 38.0;
        private const double SidebarNarrowAutoCollapseWidth = 375.0;
        private static readonly Thickness PagesContainerDefaultMargin = new(32, 20, 32, 32);
        private static readonly Thickness PagesContainerSidebarMargin =
            new(32 + 12 + SidebarExpandedWidth, 20, 32, 32);
        private const double PageSpacing = 28.0;

        // Zoom contract (WPF ZoomMin/ZoomMax/ZoomStep).
        private const double ZoomMin = 0.25;
        private const double ZoomMax = 8.0;
        private const double ZoomStep = 0.1;

        // Thumbnail contract (WPF ThumbnailCacheCapacity + render scale).
        private const int ThumbnailCacheCapacity = 24;
        private const double ThumbnailRenderScale = 0.22;

        private enum SidebarTab
        {
            Pages,
            Outline,
            Bookmarks
        }

        // ── Document/session state ──────────────────────────────────────────
        private readonly PdfService _pdfService;
        private string _currentPdfPath;
        public string CurrentPdfPath => _currentPdfPath;
        private readonly DocumentOperationSession _documentOperationSession = new();
        private CancellationTokenSource _loadCts;
        private int _loadSessionId;
        private int _completedLoadSessionId;
        private bool _isHostActive = true;
        private bool _resourcesReleased;
        private AppSettings _applicationSettings;
        private string CurrentPerformanceMode
            => PdfRenderPolicy.NormalizeMode(_applicationSettings?.PerformanceMode);

        // ── Ink tools / undo (Task 7 Phase A) ──────────────────────────────

        /// <summary>
        /// Toolbar tool set — mirrors the WPF ToolType order minus Ruler,
        /// which is an OVERLAY toggle in WPF (RulerToolButton_Click →
        /// SetRulerVisible), never a ToolType: it stays active alongside the
        /// current tool (e.g. Pen + ruler ON).
        /// </summary>
        private enum ToolType
        {
            None,
            Pen,
            Highlighter,
            HiddenInk,
            StickyNote,
            Eraser,
            Shape,
            Laser,
            Select,
            Text,
            // Task 8 Phase B: the two non-button tools the highlighter popup
            // activates — a drag over PDF text, a drag-to-rect anywhere.
            TextHighlight,
            AreaHighlight,
        }

        /// <summary>
        /// Task 25/27: what the Highlighter toolbar button applies. Freehand
        /// is the classic ink highlighter; TextHighlight/Underline/StrikeOut/
        /// Squiggly ride the PDF text-selection pipeline; AreaHighlight drags
        /// a free-form rectangle. Session-only (not persisted) — WPF parity.
        /// </summary>
        private enum HighlighterApplyMode { Freehand, TextHighlight, Underline, StrikeOut, Squiggly, AreaHighlight }

        private ToolType _currentTool = ToolType.None;
        private ToolType _previousTool = ToolType.None;
        private HighlighterApplyMode _highlighterApplyMode = HighlighterApplyMode.Freehand;

        // WPF defaults: pen black @1.5 DIP (settings-driven), highlighter
        // yellow at the fixed 140-alpha translucency, eraser 20 DIP.
        // Mode-specific opacities keep the popup previews aligned with the
        // real annotation pipelines (WPF parity).
        private const byte FreehandHighlighterOpacity = 140;
        private const byte TextHighlightOpacity = 120;
        private const byte AreaHighlightStrokeOpacity = 220;
        private const byte AreaHighlightFillOpacity = 76;
        private Windows.UI.Color _penColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        private Windows.UI.Color _highlighterColor = Windows.UI.Color.FromArgb(255, 255, 255, 0);
        private double _penSize = 1.5;
        private double _highlighterSize = 8.0; // WPF default (was 6.0 here)
        private double _eraserSize = 20.0;

        // ── Task 7 Phase B: shape tool + selection state ─────────────────
        // Shape tool attributes are session-only in WPF (never persisted).
        private InkShapeKind _shapeKind = InkShapeKind.Line;
        private bool _shapeIsDashed;
        private Windows.UI.Color _shapeColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        private double _shapeSize = 2.0;
        private Caelum.Controls.SelectionShape _selectionShape = Caelum.Controls.SelectionShape.Rectangle;
        private Caelum.Controls.SelectionFilter _selectionFilter = Caelum.Controls.SelectionFilter.Both;
        // The page currently owning an annotation selection — set/cleared by
        // PageControl_SelectionChanged; used by Delete/Ctrl+A/Esc paths.
        private PdfPageControl _activeSelectionPage;
        // Last page-background click — WPF _lastClickedPage/_lastClickedPoint;
        // the paste anchor prefers it over the selection page.
        private PdfPageControl _lastClickedPage;
        private Point _lastClickedPoint;

        // ── Task 8 Phase B: PDF text selection state (WPF parity) ────────
        // One page owns the active selection at a time; offsets are
        // PdfTextCharacterInfo indices, not Text string indices.
        private PdfPageControl _pdfTextSelectionPage;
        private PdfService.PdfPageTextInfo _pdfTextSelectionInfo;
        private Point _pdfTextSelectionPressPoint;
        private int _pdfTextSelectionAnchorOffset = -1;
        private int _pdfTextSelectionActiveOffset = -1;
        private bool _isPdfTextSelectionDragging;
        private bool _pdfTextSelectionExceededThreshold;
        private int _pdfTextSelectionRequestId;
        private string _selectedPdfText;
        private const double PdfTextSelectionDragThreshold = 4.0;

        // ── Task 8 Phase A: text boxes + sticky notes ─────────────────
        // Session defaults for newly created text boxes (WPF fields; the
        // inline toolbar syncs them from the selected box).
        private Windows.UI.Color _textColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        private double _currentFontSize = 18.0;
        private bool _textBold;
        private bool _textItalic;
        private string _textFontFamily = "Segoe UI";
        private TextAlignment _textAlignment = TextAlignment.Left;
        private static readonly double[] TextFontSizeSteps =
            { 12d, 14d, 16d, 18d, 20d, 24d, 28d, 32d, 40d, 48d, 60d, 72d };

        // The selected box and its chrome; GotFocus captures the original
        // text so LostFocus can push ONE TextEditSessionAction on change
        // (WPF _selectedTextBox / _textEditSessionTextBox parity).
        private TextBox _selectedTextBox;
        private TextBox _textEditSessionTextBox;
        private string _textEditSessionOriginalText;
        private PdfPageControl _textEditSessionPage;
        private int _textEditSessionId;

        // Floating inline toolbar hosted on the page's TextOverlay canvas.
        private Border _inlineTextBoxToolbar;
        private PdfPageControl _toolbarHostPage;
        private ToggleButton _textBoldButton;
        private ToggleButton _textItalicButton;
        private ComboBox _textFontFamilyCombo;
        private ComboBox _textAlignmentCombo;
        private Border _colorIndicator;
        private Flyout _textColorFlyout;
        // The last-shown tool/options/context flyout — tracked so
        // CloseTransientUi can sweep it (WPF transient-registry parity).
        private FlyoutBase _transientFlyout;
        // The open tool-options flyout + its owning tool (highlighter
        // bucket) — WPF CloseToolPopups parity: only one tool flyout lives
        // at a time and a tool switch sweeps the previous one.
        private FlyoutBase _toolFlyout;
        private ToolType _toolFlyoutTool = ToolType.None;
        // WPF _penPopupSizePreview — the live pen size/colour preview line
        // inside the pen flyout (rebuilt per show; field mirrors the WPF
        // popup's persistent-child shape).
        private Microsoft.UI.Xaml.Shapes.Line _penFlyoutSizePreview;
        // WPF _highlighterPopupSizePreview — the live size/colour stroke
        // inside the highlighter flyout's preview well (rebuilt per show;
        // the field mirrors the WPF popup's persistent-child shape).
        private Microsoft.UI.Xaml.Shapes.Line _highlighterFlyoutSizePreview;
        private CancellationTokenSource _eraserPreviewCts;
        private bool _isRefreshingTextAlignmentOptions;

        // Border-band drag state (arm on press, start past 4 DIP, cross-page
        // drop resolves on release — WPF _draggedContainer et al.).
        private Grid _draggedContainer;
        private PdfPageControl _draggedContainerPage;
        private Point _dragPressPointOnCanvas;
        private bool _dragArmed;
        private bool _isDragging;
        private double _dragStartX;
        private double _dragStartY;
        private uint? _dragPointerId;
        private bool _suppressTextCaptureCancellation;

        // Eight-handle resize state.
        private Grid _resizingTextContainer;
        private PdfPageControl _resizingTextPage;
        private TextResizeHandle _textResizeHandle;
        private TextResizeHandleElement _resizingTextHandleElement;
        private uint? _textResizePointerId;
        private Point _textResizeStartPoint;
        private TextBoxBounds _textResizeStartBounds;
        private bool _textResizeStartAutoWidth;
        private bool _textResizeStartAutoHeight;

        // Sticky-note editor popup state (WPF _stickyNotePopup et al.).
        private Popup _stickyNotePopup;
        private TextBox _stickyNoteEditor;
        private Border _stickyNoteDragHandle;
        private TextBlock _stickyNoteTitleTextBlock;
        private Button _stickyNoteSaveButton;
        private Button _stickyNoteCancelButton;
        private Button _stickyNoteDeleteButton;
        private PdfPageControl _stickyNoteEditingPage;
        private Grid _stickyNoteEditingContainer;
        private StickyNoteAnnotation _stickyNoteEditingModel;
        private string _stickyNoteEditingOriginalText;
        private PointD _stickyNoteEditingOriginalPosition;
        private int _stickyNoteEditingSessionId;
        private bool _isDraggingStickyNotePopup;
        private Point _stickyNotePopupDragStart;
        private double _stickyNotePopupDragStartHorizontalOffset;
        private double _stickyNotePopupDragStartVerticalOffset;

        // ── T9 save/autosave + close/dirty protocol (WPF parity) ──────────
        // DocumentSaveCoordinator coalesces manual/auto saves into one
        // in-flight task and tracks dirty generations; DocumentEditAdmission
        // and DocumentReleaseState bound the close/navigation protocol. All
        // three live in Core, byte-identical to the WPF implementation.
        private readonly DocumentSaveCoordinator _documentSaveCoordinator = new();
        private readonly DocumentEditAdmission _editAdmission = new();
        private readonly DocumentReleaseState _releaseState = new();
        private readonly object _lifecycleGate = new();
        private readonly object _saveGate = new();
        private Task<bool> _navigationPreparationInFlight;
        private Task<bool> _closePreparationInFlight;
        private Task<bool> _releaseResourcesInFlight;
        private Task<DocumentSaveResult> _autoSaveInFlight;
        private bool _documentInteractionBlocked;
        // Structural-op latch: DocumentEditAdmission is a counter, not an
        // exclusion — two concurrent structural ops (insert/delete/duplicate/
        // rotate/import) would interleave byte snapshots + reloads against
        // stale page indices. BeginStructuralOperation() refuses the second
        // one quietly.
        private int _structuralOperationInFlight;
        // Debug mirror of the coordinator state — WPF carries the identical
        // write-only pair (EditorPage :158-159) so the pending-save state is
        // inspectable in a debugger without evaluating the locked coordinator.
        // Live reads go through IsDirty / _documentSaveCoordinator directly.
        private bool _isDirty;
        private long _dirtyGeneration;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer _autoSaveTimer;
        private int _autoSaveTimerRunning;
        internal bool IsDirty => _documentSaveCoordinator.IsDirty;

        private bool _isLoadingAnnotations;
        private readonly Stack<IUndoAction> _undoStack = new();
        private readonly Stack<IUndoAction> _redoStack = new();

        // Pen hardware service (Huawei hotkey toggle + capability probing)
        // — a reference to the single WINDOW-scoped instance owned by
        // MainWindow (see InitializePenService); never disposed here.
        private Caelum.Services.PenService _penService;

        // ── Pages/rendering ─────────────────────────────────────────────────
        private readonly List<PdfPageControl> _pageControls = new();
        private readonly List<double> _pageTopOffsets = new();
        private readonly List<double> _pageHeights = new();
        // T9-B page chrome (WPF parity): hover-only per-page delete buttons
        // and the insert-gap "+" affordances between pages.
        private readonly List<Button> _pageDeleteButtons = new();
        private readonly List<Button> _pageInsertButtons = new();
        private readonly HashSet<int> _pagesInitiallyRendered = new();
        private readonly HashSet<int> _pagesRenderedAtScale = new();
        private double _zoomLevel = 1.0;
        private double _lastRenderedDpiScale = 1.0;
        private CancellationTokenSource _reRenderCts;
        private CancellationTokenSource _scrollReRenderCts;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _zoomRenderDebounceTimer;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _scrollRenderDebounceTimer;

        // ── Sidebar ─────────────────────────────────────────────────────────
        private SidebarTab _sidebarTab = SidebarTab.Pages;
        private bool _sidebarCollapsed;
        internal ObservableCollection<SidebarPageItem> SidebarPageItems { get; } = new();
        internal ObservableCollection<SidebarBookmarkItem> SidebarBookmarkItems { get; } = new();
        private readonly ObservableCollection<SidebarOutlineItem> _sidebarOutlineItems = new();
        private readonly Dictionary<int, SoftwareBitmapSource> _thumbnailCache = new();
        private readonly LinkedList<int> _thumbnailCacheLru = new();
        private readonly HashSet<int> _thumbnailPagesLoading = new();
        private CancellationTokenSource _thumbnailLoadCts = new();
        private bool _isRefreshingThumbnails;
        private bool _isSynchronizingThumbnailSelection;

        // ── Search ──────────────────────────────────────────────────────────
        private readonly List<PdfSearchResult> _pdfSearchResults = new();
        private CancellationTokenSource _pdfSearchCts;

        private bool _languageChangedSubscribed;

        // ── Page jump ───────────────────────────────────────────────────────
        private string _bookmarksCachePath;
        private HashSet<int> _bookmarkPageIndexes = new();
        private bool _isPageJumpEditing;
        private bool _suppressPageJumpTextChanged;
        private string _pageJumpOpeningValue = "1";
        private string _pageJumpValidationMessage;

        // ── Context menu (built in code for localization refresh) ───────────
        private MenuFlyout _pageContextMenu;
        private MenuFlyoutItem PrintMenuItem;
        private MenuFlyoutItem ExportCurrentPagePng1xMenuItem;
        private MenuFlyoutItem ExportCurrentPagePng2xMenuItem;
        private MenuFlyoutItem ExportAllPagesPng1xMenuItem;
        private MenuFlyoutItem ExportAllPagesPng2xMenuItem;
        private MenuFlyoutItem InsertPdfPageMenuItem;
        private MenuFlyoutItem InsertImagePageMenuItem;
        private MenuFlyoutItem RotateCurrentPageMenuItem;

        public EditorPage()
        {
            InitializeComponent();
            _pdfService = new PdfService(PdfiumRasterizerFactory.Shared);
            _applicationSettings = AppSettingsService.Load();
            ApplySettingsToToolState();

            // Undo/redo shortcuts — the accelerators live on the buttons so
            // they share the buttons' enabled state (empty stack = inert).
            UndoButton.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Key = VirtualKey.Z,
                Modifiers = VirtualKeyModifiers.Control,
            });
            RedoButton.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Key = VirtualKey.Y,
                Modifiers = VirtualKeyModifiers.Control,
            });
            // WPF parity: Ctrl+Shift+Z is the second REDO chord, not undo
            // (EditorPage_KeyDown → PerformRedoAsync).
            RedoButton.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Key = VirtualKey.Z,
                Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            });

            _zoomRenderDebounceTimer = DispatcherQueue.CreateTimer();
            _zoomRenderDebounceTimer.Interval = TimeSpan.FromMilliseconds(250);
            _zoomRenderDebounceTimer.IsRepeating = false;
            _zoomRenderDebounceTimer.Tick += ZoomRenderDebounceTimer_Tick;

            _scrollRenderDebounceTimer = DispatcherQueue.CreateTimer();
            _scrollRenderDebounceTimer.Interval = TimeSpan.FromMilliseconds(100);
            _scrollRenderDebounceTimer.IsRepeating = false;
            _scrollRenderDebounceTimer.Tick += ScrollRenderDebounceTimer_Tick;

            BuildPageContextMenu();
            // Task 19: Explorer image files can drop anywhere on the editor
            // surface (WPF PreviewDragOver/Drop on the page root). The
            // handlers resolve the page under the cursor themselves; drops
            // over chrome land on the first visible page.
            EditorRootGrid.AllowDrop = true;
            EditorRootGrid.DragOver += EditorPage_DragOver;
            EditorRootGrid.Drop += EditorPage_Drop;
            ApplyLocalization();
            // Expanded is the default state — same as the WPF shell — so the
            // pages margin starts at the 228 DIP offset.
            SetSidebarCollapsed(false);
            SetSidebarTab(SidebarTab.Pages);
            UpdatePageNumberIndicator();
            UpdateZoomLabel();
            Loaded += EditorPage_Loaded;
            Unloaded += EditorPage_Unloaded;
            // WPF ctor tail: the autosave timer is armed immediately — the
            // tick itself gates on dirty/host-active so an idle editor
            // costs nothing but a no-op callback.
            EnsureAutoSaveTimer();
#if DEBUG
            InstallDebugNarrowLayoutToggle();
#endif
        }

        // ── Navigation surface (MainWindow contract) ────────────────────────

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var path = e.Parameter as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                _ = LoadPdfAsync(path);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ReleaseResources();
        }

        /// <summary>
        /// WPF parity: a rename/move keeps the loaded document — only the
        /// display path changes (the rasterizer reads from memory).
        /// </summary>
        public void UpdateCurrentPdfPath(string newPath)
        {
            if (string.IsNullOrWhiteSpace(newPath))
                return;
            _currentPdfPath = newPath;
            SetCompatProbeText(newPath);
            // Begin rebases every future lease to the renamed path but also
            // cancels the previous session token — thumbnails, the outline
            // refresh and an in-flight search die silently mid-flight. The
            // document bytes are identical, so restart the pending pipelines
            // against the new path.
            _documentOperationSession.Begin(_loadSessionId, newPath, _pdfService);
            RestartPendingDocumentPipelines();
        }

        /// <summary>
        /// Re-kicks every pipeline whose session lease was cancelled by a
        /// path rebase (rename/move of the open document).
        /// </summary>
        private void RestartPendingDocumentPipelines()
        {
            if (!_isHostActive || _resourcesReleased)
                return;

            if (_pageControls.Count == 0)
            {
                // The rename raced the initial load: the in-flight load's
                // lease is dead, so its post-await validation exits early
                // and LoadingOverlay would stay up forever. Re-kick the
                // load against the renamed path — LoadPdfAsync increments
                // the session (retiring the stale load) and PdfService
                // serializes document swaps on _lifetimeGate.
                if (_completedLoadSessionId != _loadSessionId &&
                    !string.IsNullOrWhiteSpace(_currentPdfPath))
                {
                    _ = LoadPdfAsync(_currentPdfPath);
                }
                return;
            }

            // All thumbnail leases were cancelled — the tracking set entries
            // are stale (dead continuations remove them again in finally,
            // which only risks a benign duplicate load).
            _thumbnailPagesLoading.Clear();
            foreach (var item in SidebarPageItems)
                if (item.Thumbnail == null)
                    TryLoadThumbnail(item);

            KickViewportRender();
            InvalidateBookmarkCache();
            RefreshBookmarks();
            _ = RefreshOutlineCoreAsync(CancellationToken.None, _loadSessionId, _currentPdfPath);

            if (PdfSearchPanel?.Visibility == Visibility.Visible &&
                !string.IsNullOrWhiteSpace(PdfSearchTextBox?.Text))
            {
                PdfSearchTextBox_TextChanged(PdfSearchTextBox, null);
            }
        }

        private void EditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_languageChangedSubscribed)
            {
                LocalizationService.LanguageChanged += EditorPage_LanguageChanged;
                _languageChangedSubscribed = true;
            }

            InitializePenService();
            AutoCollapseSidebarForNarrowLayout();
            if (_completedLoadSessionId != 0 && _pageControls.Count > 0)
                KickViewportRender();
            EnsureAutoSaveTimer();
        }

        /// <summary>
        /// WinUI port of the WPF pen-service init: the service itself is
        /// WINDOW-scoped — <see cref="MainWindow"/> owns exactly one
        /// <see cref="Caelum.Services.PenService"/> per HWND (one subclass +
        /// one Win+F19/F20 registration pair however many editor tabs are
        /// open; duplicate RegisterHotKey ids on the same HWND fail, so
        /// per-page services left later tabs without hotkeys AND every
        /// subclass proc saw the shared WM_HOTKEY). This page only grabs
        /// the shared instance to feed pen-probing on its ink surfaces;
        /// MainWindow routes the events to the ACTIVE editor through
        /// <see cref="HandlePenToolToggle"/>/<see cref="HandlePenDeviceDetected"/>.
        /// </summary>
        private void InitializePenService()
        {
            if (_penService != null)
                return;

            _penService = GetMainWindow()?.GetOrCreatePenService();
            PushPenServiceToPages();
        }

        private void PushPenServiceToPages()
        {
            if (_penService == null)
                return;
            foreach (var page in _pageControls)
                page.Ink.SetPenService(_penService);
        }

        /// <summary>
        /// Huawei M-Pencil double-tap (Win+F19/F20 via the window HWND
        /// subclass) → eraser toggle, marshalled to the UI thread.
        /// <see cref="MainWindow"/> owns the single PenService and routes
        /// the event to the ACTIVE editor only — the host-active/released
        /// rechecks are the WPF IsActiveEditorPage() gates (pre-dispatch
        /// and inside the callback), so a queued toggle can't land on a
        /// hidden or released editor.
        /// </summary>
        internal void HandlePenToolToggle()
        {
            if (!_isHostActive || _resourcesReleased)
                return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isHostActive || _resourcesReleased)
                    return;
                ToggleEraserMode();
            });
        }

        /// <summary>
        /// First-pen-packet toast, routed from the window-scoped PenService
        /// by <see cref="MainWindow"/> to the ACTIVE editor — WPF shows it
        /// only on the active editor page.
        /// </summary>
        internal void HandlePenDeviceDetected(Caelum.Services.PenDeviceInfo info)
        {
            if (!_isHostActive || _resourcesReleased)
                return;
            // Packet probing fires on the UI thread already; the guard keeps
            // the toast honest if the service ever moves off it.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isHostActive || _resourcesReleased || info == null)
                    return;
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
                GetMainWindow()?.ShowToast(
                    LocalizationService.Get("Editor.Stylus") + features, "🖊", 2500);
            });
        }

        /// <summary>
        /// Barrel/hotkey eraser toggle: eraser → back to the previous tool,
        /// otherwise → eraser (current becomes previous). WPF parity.
        /// </summary>
        private void ToggleEraserMode()
        {
            ActivateTool(_currentTool == ToolType.Eraser ? _previousTool : ToolType.Eraser);
        }

        /// <summary>
        /// Sets the active tool and syncs the toolbar toggle visuals —
        /// programmatic equivalent of clicking the tool button.
        /// </summary>
        private void ActivateTool(ToolType tool)
        {
            var toolButtons = new (ToggleButton Button, ToolType Tool)[]
            {
                (PenToolButton, ToolType.Pen),
                (HighlighterToolButton, ToolType.Highlighter),
                (HiddenInkToolButton, ToolType.HiddenInk),
                (StickyNoteToolButton, ToolType.StickyNote),
                (EraserToolButton, ToolType.Eraser),
                (ShapeToolButton, ToolType.Shape),
                (LaserToolButton, ToolType.Laser),
                (SelectToolButton, ToolType.Select),
                (TextToolButton, ToolType.Text),
            };
            foreach (var (button, t) in toolButtons)
            {
                if (button == null)
                    continue;
                // WPF parity: TextHighlight/AreaHighlight keep the
                // highlighter button checked — they're its apply modes.
                button.IsChecked = t == ToolType.Highlighter
                    ? IsHighlighterTool(tool)
                    : t == tool;
            }
            // Leaving Select abandons the selection (WPF ActivateTool clears
            // _activeSelectionPage before switching; _currentTool still holds
            // the outgoing tool at this point in the method).
            if (_currentTool == ToolType.Select && tool != ToolType.Select
                && _activeSelectionPage != null)
            {
                _activeSelectionPage.ClearSelection();
                _activeSelectionPage = null;
            }
            // WPF UpdateToolButtonStates parity: switching away from Text
            // restores in-flight drag/resize and drops the text-box
            // selection. The drag half was missing — a pointer captured on
            // a container could outlive the Text tool.
            if (tool != ToolType.Text)
            {
                CancelTextBoxDrag(restoreBounds: true);
                if (_resizingTextContainer != null)
                    CancelTextResize(restoreBounds: true);
                if (tool != _currentTool)
                {
                    CommitTextEditSession();
                    DeselectTextBox();
                }
            }
            if (tool != _currentTool)
            {
                _previousTool = _currentTool;
                _currentTool = tool;
            }
            // WPF ActivateTool → CloseToolPopups(tool): a programmatic tool
            // switch (pen-service toggle, Ctrl+A, Escape) sweeps every other
            // tool flyout while the incoming tool's stays open.
            CloseToolFlyouts(tool);
            ApplyToolToAllPages();
        }

        private void EditorPage_Unloaded(object sender, RoutedEventArgs e) => ReleaseResources();

        private void EditorPage_LanguageChanged(object sender, EventArgs e)
        {
            // LanguageChanged is raised on the UI thread by ApplyLanguage;
            // re-localize sidebar labels, empty states, toolbar metadata and
            // the context menu in place (WPF EditorPage parity).
            ApplyLocalization();
        }

        /// <summary>
        /// Task 5 stub UIA contract kept for winui-home-smoke: the probes are
        /// realized but invisible, carrying the document name + full path.
        /// </summary>
        private void SetCompatProbeText(string filePath)
        {
            if (EditorPageTitleCompat != null)
                EditorPageTitleCompat.Text = Path.GetFileName(filePath) ?? string.Empty;
            if (EditorPagePathCompat != null)
                EditorPagePathCompat.Text = filePath ?? string.Empty;
        }

#if DEBUG
        private bool _debugForceNarrowLayout;

        /// <summary>
        /// DEBUG-only smoke seam: the window presenter floors at 560 DIP, so
        /// the <=375 auto-collapse rule is unreachable by resizing. The smoke
        /// invokes this hidden button to simulate the narrow layout instead.
        /// </summary>
        private void InstallDebugNarrowLayoutToggle()
        {
            var toggle = new Button
            {
                Width = 2,
                Height = 2,
                Opacity = 0.01,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsTabStop = false
            };
            AutomationProperties.SetAutomationId(toggle, "Editor.DebugSidebarNarrow");
            AutomationProperties.SetName(toggle, "DEBUG narrow-layout toggle");
            toggle.Click += (_, __) =>
            {
                _debugForceNarrowLayout = !_debugForceNarrowLayout;
                if (_debugForceNarrowLayout && !_sidebarCollapsed)
                    SetSidebarCollapsed(true);
                else if (!_debugForceNarrowLayout && _sidebarCollapsed)
                    SetSidebarCollapsed(false);
            };
            EditorRootGrid.Children.Add(toggle);

            // The smoke session cannot deliver OS-level input (SendKeys /
            // physical clicks) to the window, so input-driven editor paths get
            // hidden invoke seams that call the same handlers the real input
            // path uses.
            var commitJump = CreateDebugSeamButton("Editor.DebugCommitJump", "DEBUG commit page jump");
            commitJump.Click += (_, __) => ApplyPageJumpFromTextBox();
            EditorRootGrid.Children.Add(commitJump);

            var openSearch = CreateDebugSeamButton("Editor.DebugOpenSearch", "DEBUG open search panel");
            openSearch.Click += (_, __) => OpenPdfSearch();
            EditorRootGrid.Children.Add(openSearch);

            var openContext = CreateDebugSeamButton("Editor.DebugOpenContextMenu", "DEBUG open page context menu");
            openContext.Click += (_, __) =>
            {
                var position = new Point(
                    Math.Max(0, PdfScrollViewer.ViewportWidth / 2),
                    Math.Max(0, PdfScrollViewer.ViewportHeight / 2));
                _pageContextMenu?.ShowAt(PdfScrollViewer, position);
            };
            EditorRootGrid.Children.Add(openContext);
        }

        private static Button CreateDebugSeamButton(string automationId, string name)
        {
            var button = new Button
            {
                Width = 2,
                Height = 2,
                Opacity = 0.01,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsTabStop = false
            };
            AutomationProperties.SetAutomationId(button, automationId);
            AutomationProperties.SetName(button, name);
            return button;
        }
#endif

        // ── Document load ───────────────────────────────────────────────────

        private async Task LoadPdfAsync(string filePath)
        {
            var sessionId = Interlocked.Increment(ref _loadSessionId);
            _documentOperationSession.Begin(sessionId, filePath, _pdfService);
            using var operationLease = _documentOperationSession.Capture(sessionId, filePath, _pdfService);
            // Cancel only — a cancelled-but-unreferenced CTS is collectible,
            // while disposing it races in-flight continuations that still
            // read or register on the token (ObjectDisposedException).
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            // Retire every background pipeline from the previous document.
            _reRenderCts?.Cancel();
            _scrollReRenderCts?.Cancel();
            _thumbnailLoadCts?.Cancel();
            _thumbnailLoadCts = new CancellationTokenSource();
            _pdfSearchCts?.Cancel();
            _lastRenderedDpiScale = 1.0;
            _pagesRenderedAtScale.Clear();
            _isPageJumpEditing = false;
            InvalidateBookmarkCache();

            try
            {
                // Join any in-flight save before the reload mutates state —
                // a structural op can reach this path while the autosave
                // pipeline is mid-write (e.g. snapshot undo racing a tick),
                // and Reset() below throws on an active save. Aborting after
                // _pageControls.Clear() would leave the file/UI diverged, so
                // the drain runs before any teardown. It covers the FULL
                // save task — the version sidecar write runs outside the PDF
                // path lease — and a faulted save is only observed: the
                // reload discards the coordinator's generation state anyway.
                await DrainInFlightDocumentSaveAsync();
                _currentPdfPath = filePath;
                SetCompatProbeText(filePath);
                PagesContainer.Children.Clear();
                _pageControls.Clear();
                _pageDeleteButtons.Clear();
                _pageInsertButtons.Clear();
                _pageTopOffsets.Clear();
                _pageHeights.Clear();
                _pagesInitiallyRendered.Clear();
                _pagesRenderedAtScale.Clear();
                ReleaseThumbnailCache();
                ClearUndoRedoHistory();
                // WPF LoadPdfAsync parity: a new document starts a fresh
                // generation space. Reset() throws if a save is in flight —
                // every mutating boundary (close/tab/doc-op) flushes or
                // blocks before it can reach a reload, and the drain above
                // covers the save that was already in flight.
                _documentSaveCoordinator.Reset();
                SyncDirtyStateMirror();
                // Task 8: retire any live text/sticky edit session before the
                // page controls are replaced — their containers die with the
                // old document.
                CancelStickyNoteEdit();
                DeselectTextBox();
                RemoveInlineTextBoxToolbar();
                _lastClickedPage = null;
                SidebarPageItems.Clear();
                SidebarBookmarkItems.Clear();
                _sidebarOutlineItems.Clear();
                OutlineTreeView.RootNodes.Clear();
                _pdfSearchResults.Clear();
                PdfSearchResultsListBox.Items.Clear();
                PdfSearchPanel.Visibility = Visibility.Collapsed;
                PagesEmptyState.Visibility = Visibility.Visible;

                LoadingOverlay.Visibility = Visibility.Visible;
                await _pdfService.LoadPdfAsync(filePath, token);
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                _completedLoadSessionId = sessionId;
                // WPF LoadPdf parity: refresh the library entry's page count
                // and timestamp once the new document is known good.
                RecentFilesService.UpdateMetadata(
                    filePath, _pdfService.PageCount, File.GetLastWriteTimeUtc(filePath));

                int pageCount = _pdfService.PageCount;

                double currentTop = 0;
                for (int i = 0; i < pageCount; i++)
                {
                    var size = _pdfService.GetPageSizeInDips(i);
                    AddPdfPage(i, size, ref currentTop, pageCount);
                }
                // WPF parity: the trailing gap is the "insert at end" drop
                // zone (index == pageCount).
                if (pageCount > 0)
                    PagesContainer.Children.Add(CreatePageInsertGap(pageCount));
                ApplyToolToAllPages();
                RefreshPageDeleteButtons();
                await LoadAnnotationsIntoPagesAsync();

                _zoomLevel = 1.0;
                PdfScrollViewer.ChangeView(0, 0, 1.0f, disableAnimation: true);
                UpdateZoomLabel();
                UpdatePageNumberIndicator();

                _ = RefreshDocumentSidebarAsync(sessionId, filePath);
                RefreshBookmarks(sessionId, filePath, operationLease);
                _ = RefreshOutlineCoreAsync(token, sessionId, filePath, operationLease);

                await RenderInitialPagesAsync(token);
                UpdatePageNumberIndicator();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.LoadPdfFailed", ex.Message), "", 3500);
            }
            finally
            {
                if (IsSidebarLoadCurrent(sessionId, filePath))
                    LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void AddPdfPage(int index, (double Width, double Height) size, ref double currentTop, int pageCount)
        {
            var pageControl = new PdfPageControl
            {
                PageIndex = index,
                Width = size.Width,
                Height = size.Height,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            AutomationProperties.SetAutomationId(pageControl, $"PdfPageControl.{index}");
            AutomationProperties.SetName(pageControl, LocalizationService.Format("Editor.PageNumber", index + 1));

            pageControl.StrokeCollected += PageControl_StrokeCollected;
            pageControl.StrokeRecognized += PageControl_StrokeRecognized;
            pageControl.StrokesErased += PageControl_StrokesErased;
            pageControl.InkMutated += PageControl_InkMutated;
            pageControl.ShapeCommittedUndoable += PageControl_ShapeCommittedUndoable;
            pageControl.HiddenInkCreated += PageControl_HiddenInkCreated;
            pageControl.HiddenInksRemoved += PageControl_HiddenInksRemoved;
            pageControl.SelectionChanged += PageControl_SelectionChanged;
            pageControl.SelectionMoveCompleted += PageControl_SelectionMoveCompleted;
            pageControl.SelectionResizeCompleted += PageControl_SelectionResizeCompleted;
            pageControl.SelectionRotateCompleted += PageControl_SelectionRotateCompleted;
            pageControl.BlankContextRequested += PageControl_BlankContextRequested;
            // Task 8 Phase A: text-box creation/deselection on the text
            // overlay, sticky-note placement on the page background and the
            // marker's activate/move/delete lifecycle.
            pageControl.TextOverlayPointerPressed += PageControl_TextOverlayPointerPressed;
            pageControl.BackgroundPointerPressed += PageControl_BackgroundPointerPressed;
            pageControl.StickyNoteActivated += PageControl_StickyNoteActivated;
            pageControl.StickyNoteMoved += PageControl_StickyNoteMoved;
            pageControl.StickyNoteDeleteRequested += PageControl_StickyNoteDeleteRequested;
            // Task 8 Phase B: overlay-set changes (images/markups/areas) mark
            // dirty; committed area-highlight drags push one undo action;
            // the PDF text-selection layer forwards pointer traffic.
            pageControl.ImagesChanged += PageControl_ImagesChanged;
            pageControl.AreaHighlightCreated += PageControl_AreaHighlightCreated;
            pageControl.PdfTextSelectionPointerPressed += PageControl_PdfTextSelectionPointerPressed;
            pageControl.PdfTextSelectionPointerMoved += PageControl_PdfTextSelectionPointerMoved;
            pageControl.PdfTextSelectionPointerReleased += PageControl_PdfTextSelectionPointerReleased;

            // Task 22 parity: the page queries the active ruler edge at
            // stroke-collect/shape-commit time; the viewport→page transform
            // runs per query so scrolling/zooming/ruler moves never serve a
            // stale segment. Null while the ruler is hidden.
            pageControl.GetRulerGeometryInPageCoords = () =>
            {
                var geometry = GetRulerGeometryEndpoints();
                if (geometry == null)
                    return null;
                var transform = RulerOverlayCanvas.TransformToVisual(pageControl);
                PointD Map(PointD p)
                {
                    var mapped = transform.TransformPoint(new Point(p.X, p.Y));
                    return new PointD(mapped.X, mapped.Y);
                }
                return (
                    Map(geometry.Value.TopA),
                    Map(geometry.Value.TopB),
                    Map(geometry.Value.BottomA),
                    Map(geometry.Value.BottomB));
            };

            // Late-loading docs must inherit the tab's host-active gate —
            // a page created while the tab is hidden must not come up
            // input-enabled (same pair SetHostActive propagates).
            pageControl.SetHostActive(_isHostActive);
            pageControl.SetDocumentInputEnabled(_isHostActive);

            _pageTopOffsets.Add(currentTop);
            _pageHeights.Add(size.Height);

            // WPF page chrome parity: an insert-gap zone sits BEFORE each
            // page after the first (insertIndex == the following page index),
            // and the host grid carries the hover-only delete button.
            if (index > 0)
                PagesContainer.Children.Add(CreatePageInsertGap(index));

            PagesContainer.Children.Add(CreatePageHost(pageControl));
            _pageControls.Add(pageControl);

            currentTop += size.Height + PageSpacing;
        }

        // ── Per-page chrome (WPF CreatePageHost / CreatePageInsertGap) ────

        /// <summary>
        /// WPF <c>CreatePageHost</c> parity: wraps the page in a Grid that
        /// carries a hover-only delete button (top-right overlay, visible on
        /// pointer hover while more than one page exists). WinUI has no
        /// <c>IsMouseOver</c>/<c>Visibility.Hidden</c>, so explicit hover
        /// flags + Collapsed stand in for WPF's enter/leave/Hidden trio; the
        /// Button template's built-in hover/press/focus visuals replace WPF's
        /// CreatePageChromeButtonTemplate triggers.
        /// </summary>
        private FrameworkElement CreatePageHost(PdfPageControl pageControl)
        {
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            host.Children.Add(pageControl);

            var deleteButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 14, 14, 0),
                MinHeight = 34,
                Padding = new Thickness(10, 6, 10, 6),
                Background = ResolveThemeBrush("ThemeControlBrush", Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6)),
                BorderBrush = ResolveThemeBrush("ThemeDangerBrush", Color.FromArgb(0xFF, 0xB4, 0x23, 0x18)),
                Foreground = ResolveThemeBrush("ThemeDangerBrush", Color.FromArgb(0xFF, 0xB4, 0x23, 0x18)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Visibility = Visibility.Collapsed,
            };
            var deleteLabel = new TextBlock
            {
                Text = LocalizationService.Get("Editor.DeletePageTooltip"),
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var deleteIcon = new LucideIcon { Kind = "Trash2", Width = 14, Height = 14 };
            deleteIcon.Stroke = ResolveThemeBrush("ThemeDangerBrush", Color.FromArgb(0xFF, 0xB4, 0x23, 0x18));
            deleteLabel.Foreground = ResolveThemeBrush("ThemeDangerBrush", Color.FromArgb(0xFF, 0xB4, 0x23, 0x18));
            var deleteContent = new StackPanel { Orientation = Orientation.Horizontal };
            deleteContent.Children.Add(deleteIcon);
            deleteContent.Children.Add(deleteLabel);
            deleteButton.Content = deleteContent;
            ToolTipService.SetToolTip(deleteButton, LocalizationService.Get("Editor.DeletePageTooltip"));
            AutomationProperties.SetAutomationId(deleteButton, $"Editor.PageDeleteButton.{pageControl.PageIndex}");

            bool hostHovered = false;
            bool buttonHovered = false;
            void UpdateDeleteVisibility()
            {
                // WPF parity: Hidden(→Collapsed) while >1 page and not
                // hovered, Collapsed outright on single-page documents.
                bool show = (hostHovered || buttonHovered) && _pageControls.Count > 1;
                deleteButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }

            host.PointerEntered += (_, _) => { hostHovered = true; UpdateDeleteVisibility(); };
            host.PointerExited += (_, _) => { hostHovered = false; UpdateDeleteVisibility(); };
            deleteButton.PointerEntered += (_, _) => { buttonHovered = true; UpdateDeleteVisibility(); };
            deleteButton.PointerExited += (_, _) => { buttonHovered = false; UpdateDeleteVisibility(); };

            deleteButton.Click += async (_, _) =>
            {
                try
                {
                    using var operationLease = CaptureDocumentOperationLease(_pdfService);
                    await DeletePageAtAsync(pageControl.PageIndex, operationLease);
                }
                catch (Exception ex)
                {
                    // async-void click handler: no App.UnhandledException backstop.
                    System.Diagnostics.Debug.WriteLine($"[PageChrome] Delete click faulted: {ex}");
                }
            };

            _pageDeleteButtons.Add(deleteButton);
            host.Children.Add(deleteButton);
            return host;
        }

        /// <summary>
        /// WPF <c>CreatePageInsertGap</c> parity: the page-spacing strip
        /// between (and after) pages carries a hover-only accent guide line +
        /// "+" button that opens the template picker and inserts a page at
        /// <paramref name="insertIndex"/>.
        /// </summary>
        private FrameworkElement CreatePageInsertGap(int insertIndex)
        {
            var zone = new Grid
            {
                Height = PageSpacing,
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            var guideLine = new Border
            {
                Width = 150,
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            var insertButton = new Button
            {
                Width = 78,
                Height = 32,
                Background = ResolveThemeBrush("ThemeControlBrush", Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6)),
                BorderBrush = ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)),
                Foreground = ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Padding = new Thickness(0),
            };
            var plusIcon = new LucideIcon { Kind = "Plus", Width = 17, Height = 17 };
            plusIcon.Stroke = ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB));
            insertButton.Content = plusIcon;
            ToolTipService.SetToolTip(insertButton, LocalizationService.Get("Editor.InsertPageHereTooltip"));
            AutomationProperties.SetAutomationId(insertButton, $"Editor.PageInsertButton.{insertIndex}");

            bool zoneHovered = false;
            bool buttonHovered = false;
            void UpdateInsertVisibility()
            {
                bool show = zoneHovered || buttonHovered;
                guideLine.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                    guideLine.Background = ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB));
                insertButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }

            zone.PointerEntered += (_, _) => { zoneHovered = true; UpdateInsertVisibility(); };
            zone.PointerExited += (_, _) => { zoneHovered = false; UpdateInsertVisibility(); };
            insertButton.PointerEntered += (_, _) => { buttonHovered = true; UpdateInsertVisibility(); };
            insertButton.PointerExited += (_, _) => { buttonHovered = false; UpdateInsertVisibility(); };

            insertButton.Click += async (_, _) =>
            {
                try
                {
                    using var operationLease = CaptureDocumentOperationLease(_pdfService);
                    await InsertPageAtAsync(insertIndex, operationLease);
                }
                catch (Exception ex)
                {
                    // async-void click handler: no App.UnhandledException backstop.
                    System.Diagnostics.Debug.WriteLine($"[PageChrome] Insert click faulted: {ex}");
                }
            };

            _pageInsertButtons.Add(insertButton);
            zone.Children.Add(guideLine);
            zone.Children.Add(insertButton);
            return zone;
        }

        /// <summary>
        /// WPF <c>RefreshPageDeleteButtons</c> parity — the name is WPF's and
        /// stays for parity even though the method re-stamps BOTH page-chrome
        /// sets: delete-button tooltips/labels AND insert-gap "+" tooltips.
        /// (Single-page delete collapse is driven by the hover-visibility
        /// handlers in <see cref="CreatePageHost"/>, not here.)
        /// </summary>
        private void RefreshPageDeleteButtons()
        {
            foreach (var button in _pageDeleteButtons)
            {
                ToolTipService.SetToolTip(button, LocalizationService.Get("Editor.DeletePageTooltip"));
                if (button.Content is StackPanel panel
                    && panel.Children.Count > 1
                    && panel.Children[1] is TextBlock label)
                    label.Text = LocalizationService.Get("Editor.DeletePageTooltip");
            }

            foreach (var button in _pageInsertButtons)
                ToolTipService.SetToolTip(button, LocalizationService.Get("Editor.InsertPageHereTooltip"));
        }

        /// <summary>
        /// Quiet sidecar load: ExtractedAnnotations is page-indexed markup
        /// harvested during LoadPdfAsync. Strokes enter through the store's
        /// quiet path under the _isLoadingAnnotations guard so loading never
        /// creates undo actions — the same contract as the WPF loader
        /// (strokes → hidden inks → texts → highlights → images → markups →
        /// areas → stickies).
        /// </summary>
        private async Task LoadAnnotationsIntoPagesAsync()
        {
            var annotations = _pdfService.ExtractedAnnotations;
            if (annotations == null || annotations.Count == 0)
                return;

            _isLoadingAnnotations = true;
            try
            {
                foreach (var page in _pageControls)
                {
                    if (!annotations.TryGetValue(page.PageIndex, out var pageAnnotation)
                        || pageAnnotation == null)
                    {
                        continue;
                    }
                    if (pageAnnotation.Strokes != null)
                    {
                        foreach (var stroke in pageAnnotation.Strokes)
                            page.AddStroke(stroke);
                    }
                    // Task 7 Phase B: study-mode masks load quietly alongside
                    // the strokes (WPF loader parity — no undo entries).
                    if (pageAnnotation.HiddenInks != null)
                    {
                        foreach (var mask in pageAnnotation.HiddenInks)
                            page.AddHiddenInk(mask);
                    }
                    // Task 8 Phase A: text boxes restore quietly —
                    // CreateTextBox(select:false) pushes no undo action and
                    // keeps the serialized auto-size sentinels.
                    if (pageAnnotation.Texts != null)
                    {
                        foreach (var ta in pageAnnotation.Texts)
                        {
                            var color = Windows.UI.Color.FromArgb(255, ta.R, ta.G, ta.B);
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
                                alignment: ParseTextAlignment(ta.Alignment),
                                rotationDegrees: ta.RotationDegrees);
                        }
                    }
                    // Task 8 Phase B: persisted text-quad highlights repaint
                    // through the same render path as a fresh selection.
                    if (pageAnnotation.Highlights != null)
                    {
                        foreach (var hl in pageAnnotation.Highlights)
                            page.AddHighlight(hl);
                    }
                    // Task 19: restored image annotations keep their saved
                    // geometry + rotation verbatim.
                    if (pageAnnotation.Images != null)
                    {
                        foreach (var ia in pageAnnotation.Images)
                        {
                            if (string.IsNullOrEmpty(ia.ImageDataBase64))
                                continue;

                            byte[] imageBytes;
                            try { imageBytes = Convert.FromBase64String(ia.ImageDataBase64); }
                            catch { continue; }

                            var imageContainer = await page.AddImageAsync(
                                imageBytes, new Point(ia.X, ia.Y), ia.Width, ia.Height);
                            if (imageContainer != null)
                                PdfPageControl.ApplyAnnotationRotation(imageContainer, ia.RotationDegrees);
                        }
                    }
                    if (pageAnnotation.TextMarkups != null)
                    {
                        foreach (var markup in pageAnnotation.TextMarkups)
                            page.AddTextMarkup(markup);
                    }
                    if (pageAnnotation.AreaHighlights != null)
                    {
                        foreach (var area in pageAnnotation.AreaHighlights)
                            page.AddAreaHighlight(area);
                    }
                    if (pageAnnotation.StickyNotes != null)
                    {
                        foreach (var note in pageAnnotation.StickyNotes)
                            page.AddStickyNote(note);
                    }
                }
            }
            finally
            {
                _isLoadingAnnotations = false;
            }
        }

        private async Task RenderInitialPagesAsync(CancellationToken token)
        {
            // First render covers the viewport set; the scroll debounce then
            // fills adjacent pages at idle priority.
            var visiblePages = GetVisiblePageControls();
            foreach (var page in visiblePages)
            {
                token.ThrowIfCancellationRequested();
                if (!_isHostActive || _resourcesReleased)
                    return;
                await RenderPageInitialAsync(page, token);
            }
            QueueAdjacentPagePrerender(visiblePages, token);
        }

        private void KickViewportRender()
        {
            if (!_isHostActive || _resourcesReleased)
                return;
            _scrollReRenderCts?.Cancel();
            _scrollRenderDebounceTimer.Stop();
            _scrollRenderDebounceTimer.Start();
        }

        // ── Rendering ───────────────────────────────────────────────────────

        private async Task RenderPageInitialAsync(PdfPageControl page, CancellationToken token)
        {
            if (!_isHostActive || _resourcesReleased || _pagesInitiallyRendered.Contains(page.PageIndex))
                return;

            try
            {
                // DPI-aware baseline: match the zoom re-render path
                // (ZoomRenderDebounceTimer_Tick) so a >100% monitor does not
                // get a soft 1.0 raster until the first zoom.
                double rasterScale = Math.Max(XamlRoot?.RasterizationScale ?? 1.0, 1.0);
                double renderScale = PdfRenderPolicy.CalculateRenderScale(
                    CurrentPerformanceMode,
                    page.Width,
                    page.Height,
                    Math.Max(_zoomLevel * rasterScale, 1.0));
                var source = await RenderPageImageSourceAsync(page.PageIndex, renderScale, token);
                if (source != null)
                {
                    token.ThrowIfCancellationRequested();
                    page.PageSource = source;
                    _pagesInitiallyRendered.Add(page.PageIndex);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RenderPageInitialAsync page {page.PageIndex} failed: {ex.Message}");
            }
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
                    var source = await RenderPageImageSourceAsync(page.PageIndex, renderScale, token);
                    if (source != null)
                    {
                        token.ThrowIfCancellationRequested();
                        page.PageSource = source;
                        _pagesRenderedAtScale.Add(page.PageIndex);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }

        private async Task<SoftwareBitmapSource> RenderPageImageSourceAsync(
            int pageIndex, double renderScale, CancellationToken token)
        {
            var bitmap = await _pdfService.RenderPageBgraAsync(pageIndex, renderScale, token);
            if (bitmap == null)
                return null;
            token.ThrowIfCancellationRequested();
            return await CreateImageSourceAsync(bitmap);
        }

        /// <summary>
        /// Converts a Core BGRA page bitmap into a WinUI
        /// <see cref="SoftwareBitmapSource"/>. The rasterizer emits premultiplied
        /// BGRA — the same layout SoftwareBitmap expects.
        /// </summary>
        private static async Task<SoftwareBitmapSource> CreateImageSourceAsync(PdfPageBitmap bitmap)
        {
            if (bitmap?.Bgra == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                return null;
            var softwareBitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Premultiplied);
            softwareBitmap.CopyFromBuffer(bitmap.Bgra.AsBuffer());
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(softwareBitmap);
            return source;
        }

        private void QueueAdjacentPagePrerender(List<PdfPageControl> visiblePages, CancellationToken token)
        {
            var profile = PdfRenderPolicy.GetProfile(CurrentPerformanceMode);
            if (!_isHostActive || !profile.PrefetchAdjacentPages || visiblePages.Count == 0)
                return;

            int first = visiblePages[0].PageIndex;
            int last = visiblePages[visiblePages.Count - 1].PageIndex;

            var candidates = new Queue<PdfPageControl>();
            for (int i = Math.Max(0, first - 1); i <= Math.Min(_pageControls.Count - 1, last + 1); i++)
            {
                if (!_pagesInitiallyRendered.Contains(i))
                    candidates.Enqueue(_pageControls[i]);
            }
            if (candidates.Count == 0)
                return;
            ScheduleNextAdjacentPrerender(candidates, token);
        }

        /// <summary>
        /// WPF chains adjacent renders at ApplicationIdle priority;
        /// <see cref="DispatcherQueuePriority.Low"/> is the WinUI equivalent.
        /// </summary>
        private void ScheduleNextAdjacentPrerender(Queue<PdfPageControl> candidates, CancellationToken token)
        {
            if (!_isHostActive || candidates.Count == 0 || token.IsCancellationRequested)
                return;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                if (!_isHostActive || token.IsCancellationRequested)
                    return;
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
            });
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

        // ── Viewport geometry ───────────────────────────────────────────────

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

        // ── Scroll / view tracking ──────────────────────────────────────────

        private void PdfScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            // Sync native zoom (pinch / ScrollViewer-owned zoom gestures)
            // back into the shell's zoom contract.
            double factor = PdfScrollViewer.ZoomFactor;
            if (Math.Abs(factor - _zoomLevel) > 0.0001)
            {
                _zoomLevel = factor;
                UpdateZoomLabel();
                ScheduleReRenderForZoom();
            }

            UpdatePageNumberIndicator();
            UpdateBookmarkButton();

            // Task 8: the floating text toolbar tracks the selected box —
            // hide it while the box is scrolled out of the viewport and
            // re-position it once visible again (WPF parity).
            UpdateSelectedTextBoxPopupVisibility(false);

            if (!_isHostActive || _resourcesReleased)
                return;

            _scrollReRenderCts?.Cancel();
            _scrollRenderDebounceTimer.Stop();
            _scrollRenderDebounceTimer.Start();
        }

        private async void ScrollRenderDebounceTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            _scrollRenderDebounceTimer.Stop();
            _scrollReRenderCts?.Cancel();
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

                QueueAdjacentPagePrerender(visiblePages, token);
                TrimPageBitmapWorkingSet(visiblePages);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // async-void timer callback: no App.UnhandledException
                // backstop exists, so nothing may escape.
                System.Diagnostics.Debug.WriteLine($"[EditorPage] Scroll re-render tick faulted: {ex}");
            }
        }

        // ── Zoom ────────────────────────────────────────────────────────────

        private void SetZoom(double level)
        {
            ZoomAroundPoint(level, GetViewportCenter());
        }

        private Point GetViewportCenter()
            => new(PdfScrollViewer.ViewportWidth / 2.0, PdfScrollViewer.ViewportHeight / 2.0);

        private void AdjustZoom(double delta)
        {
            ZoomAroundPoint(_zoomLevel + delta, GetViewportCenter());
        }

        /// <summary>
        /// Zoom-around-viewport-point: keeps the content point under
        /// <paramref name="viewportPoint"/> stationary. With WinUI's
        /// ScrollViewer zoom, offsets are expressed in scaled content
        /// coordinates, so one <see cref="ScrollViewer.ChangeView"/> commits
        /// the zoom + corrected offsets atomically — the equivalent of WPF's
        /// UpdateLayout + ScrollTo pair.
        /// </summary>
        private void ZoomAroundPoint(double newZoom, Point viewportPoint)
        {
            double oldZoom = _zoomLevel;
            _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax, newZoom));
            if (Math.Abs(_zoomLevel - oldZoom) < 0.0001)
                return;

            double contentX = (PdfScrollViewer.HorizontalOffset + viewportPoint.X) / oldZoom;
            double contentY = (PdfScrollViewer.VerticalOffset + viewportPoint.Y) / oldZoom;
            double newOffsetX = Math.Max(0, contentX * _zoomLevel - viewportPoint.X);
            double newOffsetY = Math.Max(0, contentY * _zoomLevel - viewportPoint.Y);
            PdfScrollViewer.ChangeView(newOffsetX, newOffsetY, (float)_zoomLevel, disableAnimation: true);

            UpdateZoomLabel();
            ScheduleReRenderForZoom();
        }

        private void ScheduleReRenderForZoom()
        {
            if (!_isHostActive || _resourcesReleased)
                return;

            _reRenderCts?.Cancel();
            _zoomRenderDebounceTimer.Stop();
            _zoomRenderDebounceTimer.Start();
        }

        private async void ZoomRenderDebounceTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            _zoomRenderDebounceTimer.Stop();
            _reRenderCts?.Cancel();
            _reRenderCts = new CancellationTokenSource();
            var token = _reRenderCts.Token;

            try
            {
                token.ThrowIfCancellationRequested();
                if (!_isHostActive || _resourcesReleased)
                    return;

                var profile = PdfRenderPolicy.GetProfile(CurrentPerformanceMode);
                // Render scale follows zoom × monitor DPI so text stays crisp;
                // the render-policy MaxRenderScale caps memory at 800% zoom.
                double rasterScale = XamlRoot?.RasterizationScale ?? 1.0;
                double neededScale = Math.Min(Math.Max(_zoomLevel * rasterScale, 1.0), profile.MaxRenderScale);

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
            catch (Exception ex)
            {
                // async-void timer callback: no App.UnhandledException
                // backstop exists, so nothing may escape.
                System.Diagnostics.Debug.WriteLine($"[EditorPage] Zoom re-render tick faulted: {ex}");
            }
        }

        private void UpdateZoomLabel()
        {
            if (ZoomLabel == null)
                return;
            ZoomLabel.Text = $"{(int)Math.Round(_zoomLevel * 100)}%";
            // UIA parity with the WPF shell: the label's accessible name IS
            // the current percentage (the edit hint rides on the tooltip).
            AutomationProperties.SetName(ZoomLabel, ZoomLabel.Text);
        }

        private void ZoomLabel_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (ZoomTextBox == null || ZoomLabel == null)
                return;
            ZoomTextBox.Text = $"{(int)Math.Round(_zoomLevel * 100)}";
            // WPF parity: the label hides while the inline editor is open
            // (they share one grid cell — the textbox overlays it).
            ZoomLabel.Visibility = Visibility.Collapsed;
            ZoomTextBox.Visibility = Visibility.Visible;
            ZoomTextBox.Focus(FocusState.Programmatic);
            ZoomTextBox.SelectAll();
        }

        private void ZoomTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                ApplyZoomFromTextBox();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                // Discard: the label keeps the pre-edit value and reappears.
                HideZoomTextBox();
                e.Handled = true;
            }
        }

        private void ZoomTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // WPF parity: losing focus commits, same as Enter.
            ApplyZoomFromTextBox();
        }

        private void ApplyZoomFromTextBox()
        {
            if (ZoomTextBox == null)
                return;
            var text = ZoomTextBox.Text.Trim().TrimEnd('%');
            if (int.TryParse(text, out int pct) &&
                pct >= (int)(ZoomMin * 100) && pct <= (int)(ZoomMax * 100))
            {
                ZoomAroundPoint(pct / 100.0, GetViewportCenter());
            }
            HideZoomTextBox();
        }

        private void HideZoomTextBox()
        {
            if (ZoomTextBox != null)
                ZoomTextBox.Visibility = Visibility.Collapsed;
            if (ZoomLabel != null)
                ZoomLabel.Visibility = Visibility.Visible;
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => AdjustZoom(-ZoomStep);
        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            AdjustZoom(ZoomStep);
        }

        private void PagesContainer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            if (!state.HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down))
                return;

            // Drive ctrl+wheel ourselves so the 0.1 step + zoom-label contract
            // stays exact (the ScrollViewer's native ctrl+wheel uses its own
            // step). e.Handled suppresses the native zoom.
            e.Handled = true;
            var point = e.GetCurrentPoint(PdfScrollViewer);
            double newZoom = _zoomLevel + (point.Properties.MouseWheelDelta > 0 ? ZoomStep : -ZoomStep);
            ZoomAroundPoint(newZoom, point.Position);
        }

        // ── Page navigation / jump ──────────────────────────────────────────

        private void JumpToPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _pageControls.Count)
                return;

            double targetOffset = Math.Max(0, GetScaledPageTop(pageIndex) - 12);
            PdfScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
            UpdatePageNumberIndicator();
            UpdateThumbnailSelection(forceCenter: true);
            UpdateBookmarkButton();
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

        private void UpdatePageNumberIndicator()
        {
            if (PageNumberTextBox == null || PageCountText == null)
                return;

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
#if DEBUG
            // DEBUG-only smoke seam: the WinUI TextBox UIA Value can lag a
            // programmatic Text rewrite, so the current page is mirrored onto
            // the group's HelpText for tools/winui-editor-smoke.ps1.
            AutomationProperties.SetHelpText(PageJumpGroup, $"current-page={currentPageNumber}");
#endif
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

        private void PageNumberTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (PageNumberTextBox == null)
                return;

            _isPageJumpEditing = true;
            _pageJumpOpeningValue = PageNumberTextBox.Text;
            ClearPageJumpValidationMessage();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), PageNumberTextBox))
                    PageNumberTextBox.SelectAll();
            });
        }

        private void PageNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_suppressPageJumpTextChanged)
                _isPageJumpEditing = true;
        }

        private void PageNumberTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                ApplyPageJumpFromTextBox();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                if (PageNumberTextBox != null && !string.IsNullOrWhiteSpace(_pageJumpOpeningValue))
                    SetPageJumpText(_pageJumpOpeningValue);
                ClearPageJumpValidationMessage();
                EndPageJumpEdit();
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
                EndPageJumpEdit();
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
            EndPageJumpEdit();
        }

        /// <summary>
        /// Ends an in-progress page-jump edit and re-syncs the jump textbox
        /// (and compat label) to the live current page. Historical name was
        /// HidePageNumberTextBox — nothing is hidden; the box stays visible.
        /// </summary>
        private void EndPageJumpEdit()
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

        // ── Toolbar + ink tools (Task 7 Phase A) ────────────────────────────

        /// <summary>
        /// Settings → tool state. Runs at construction; the pen colour/size
        /// follow AppSettings defaults exactly like the WPF shell.
        /// </summary>
        private void ApplySettingsToToolState()
        {
            if (_applicationSettings == null)
                return;

            try
            {
                _penColor = ParseHexColor(_applicationSettings.DefaultPenColorHex);
            }
            catch
            {
                _penColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
            }
            _penSize = Math.Clamp(_applicationSettings.DefaultPenSize, 0.5, 24.0);
            if (PenColorIndicator != null)
                PenColorIndicator.Background = new SolidColorBrush(_penColor);
            if (PenOnlyButton != null)
                PenOnlyButton.IsChecked = _applicationSettings.PenOnlyMode;
        }

        /// <summary>#RRGGBB / #AARRGGBB → Color. Throws on bad input.</summary>
        private static Windows.UI.Color ParseHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                throw new FormatException("empty color");
            var s = hex.Trim().TrimStart('#');
            if (s.Length == 6)
            {
                return Windows.UI.Color.FromArgb(
                    255,
                    Convert.ToByte(s.Substring(0, 2), 16),
                    Convert.ToByte(s.Substring(2, 2), 16),
                    Convert.ToByte(s.Substring(4, 2), 16));
            }
            if (s.Length == 8)
            {
                return Windows.UI.Color.FromArgb(
                    Convert.ToByte(s.Substring(0, 2), 16),
                    Convert.ToByte(s.Substring(2, 2), 16),
                    Convert.ToByte(s.Substring(4, 2), 16),
                    Convert.ToByte(s.Substring(6, 2), 16));
            }
            throw new FormatException($"bad color: {hex}");
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e) => _ = PerformUndoAsync();
        private void RedoButton_Click(object sender, RoutedEventArgs e) => _ = PerformRedoAsync();

        /// <summary>
        /// Tool toggles stay mutually exclusive like the WPF toolbar —
        /// every Phase-B tool (Select/Shape/HiddenInk/Laser included) arms a
        /// real <see cref="CustomInkInputProcessingMode"/> on each page via
        /// <see cref="ApplyToolToAllPages"/>; only the ruler sits outside
        /// this set (it is an overlay toggle, not a ToolType). Re-clicking
        /// the armed tool unchecks it and deactivates to
        /// <see cref="ToolType.None"/> (WPF parity).
        /// </summary>
        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked)
                return;

            var toolButtons = new (ToggleButton Button, ToolType Tool)[]
            {
                (PenToolButton, ToolType.Pen),
                (HighlighterToolButton, ToolType.Highlighter),
                (HiddenInkToolButton, ToolType.HiddenInk),
                (StickyNoteToolButton, ToolType.StickyNote),
                (EraserToolButton, ToolType.Eraser),
                (ShapeToolButton, ToolType.Shape),
                (LaserToolButton, ToolType.Laser),
                (SelectToolButton, ToolType.Select),
                (TextToolButton, ToolType.Text),
            };

            ToolType next = ToolType.None;
            foreach (var (button, tool) in toolButtons)
            {
                if (ReferenceEquals(button, clicked))
                {
                    if (clicked.IsChecked == true)
                        next = tool;
                }
                else
                {
                    button.IsChecked = false;
                }
            }

            // The Highlighter button arms whichever apply mode is armed in
            // its flyout (WPF HighlighterToolButton_Click →
            // GetActiveHighlighterToolType).
            if (next == ToolType.Highlighter)
                next = GetActiveHighlighterToolType();

            // Leaving Select abandons the selection (WPF ActivateTool parity).
            if (_currentTool == ToolType.Select && next != ToolType.Select
                && _activeSelectionPage != null)
            {
                _activeSelectionPage.ClearSelection();
                _activeSelectionPage = null;
            }

            // WPF UpdateToolButtonStates parity: switching away from Text
            // restores an in-flight resize and drops the text-box selection.
            if (next != ToolType.Text)
            {
                if (_resizingTextContainer != null)
                    CancelTextResize(restoreBounds: true);
                if (next != _currentTool)
                {
                    CommitTextEditSession();
                    DeselectTextBox();
                }
            }

            if (next != _currentTool)
            {
                _previousTool = _currentTool;
                _currentTool = next;
            }
            ApplyToolToAllPages();

            // WPF ToggleToolButton → CloseToolPopups() runs first: only one
            // tool flyout can be open at a time, and deactivating back to
            // None sweeps it too. The incoming tool's flyout (if any) then
            // opens under its button — arming Pen/Eraser/Shape/Select or any
            // highlighter mode opens the matching options flyout (WPF
            // _penPopup/_eraserPopup/_shapePopup/_selectionPopup/
            // _highlighterPopup parity).
            CloseToolFlyouts(next);
            if (next == ToolType.Pen)
                ShowPenFlyout(clicked);
            else if (next == ToolType.Eraser)
                ShowEraserFlyout(clicked);
            else if (next == ToolType.Shape)
                ShowShapeFlyout(clicked);
            else if (next == ToolType.Select)
                ShowSelectionFlyout(clicked);
            else if (IsHighlighterTool(next))
                ShowHighlighterFlyout(clicked);
        }

        /// <summary>WPF GetActiveHighlighterToolType — apply mode → ToolType.</summary>
        private ToolType GetActiveHighlighterToolType() => _highlighterApplyMode switch
        {
            HighlighterApplyMode.Freehand => ToolType.Highlighter,
            HighlighterApplyMode.AreaHighlight => ToolType.AreaHighlight,
            _ => ToolType.TextHighlight,
        };

        private static bool IsHighlighterTool(ToolType tool) =>
            tool == ToolType.Highlighter
            || tool == ToolType.TextHighlight
            || tool == ToolType.AreaHighlight;

        /// <summary>
        /// Activates the ToolType matching the current highlighter apply
        /// mode — used by the flyout mode selector and the toolbar click
        /// (WPF ActivateHighlighterModeTool).
        /// </summary>
        private void ActivateHighlighterModeTool()
        {
            var tool = GetActiveHighlighterToolType();
            if (_currentTool != tool)
                ActivateTool(tool);
            else
                ApplyToolToAllPages();
        }

        /// <summary>
        /// Pushes the current tool + ink settings into every page surface —
        /// the ApplyToolToAllPages port. The page-level SetInputMode owns the
        /// ink-surface tool; Pen/Highlighter pre-seed <see cref="InkSurface.Tool"/>
        /// so the Inking mode keeps the right nib (WPF SetInkAttributes parity).
        /// </summary>
        private void ApplyToolToAllPages()
        {
            var settings = _applicationSettings;
            foreach (var page in _pageControls)
            {
                var ink = page.Ink;
                ink.SetPenService(_penService);
                ink.PenColor = _penColor;
                ink.PenSize = _penSize;
                ink.HighlighterColor = Windows.UI.Color.FromArgb(
                    FreehandHighlighterOpacity,
                    _highlighterColor.R, _highlighterColor.G, _highlighterColor.B);
                ink.HighlighterSize = _highlighterSize;
                ink.EraserSize = _eraserSize;
                if (settings != null)
                {
                    ink.PenOnlyMode = settings.PenOnlyMode;
                    ink.EnablePressure = settings.EnablePressure;
                    ink.WholeStrokeEraser = settings.WholeStrokeEraser;
                    ink.InkSimulationEnabled = settings.InkSimulation;
                    ink.ShapeRecognitionEnabled = settings.ShapeRecognition;
                    ink.StrokeSmoothingLevel = settings.StrokeSmoothing;
                }

                page.SetSelectionMode(_currentTool == ToolType.Select);
                page.SetSelectionFilter(_selectionFilter);
                page.SetSelectionShape(_selectionShape);
                // WPF SetMode: text containers only take direct input while
                // the Text tool is armed.
                page.SetMode(_currentTool == ToolType.Text);
                // WPF parity: the PDF text-selection layer arms only when no
                // other tool owns the pointer (None) or when a text-markup
                // highlighter mode is armed (TextHighlight).
                page.SetPdfTextSelectionEnabled(
                    _currentTool == ToolType.None || _currentTool == ToolType.TextHighlight);

                switch (_currentTool)
                {
                    case ToolType.Pen:
                        ink.Tool = InkSurfaceTool.Pen;
                        page.SetInputMode(CustomInkInputProcessingMode.Inking);
                        break;
                    case ToolType.Highlighter:
                        ink.Tool = InkSurfaceTool.Highlighter;
                        page.SetInputMode(CustomInkInputProcessingMode.Inking);
                        break;
                    case ToolType.HiddenInk:
                        // WPF parity: new masks use the neutral gray cover;
                        // loaded masks keep their serialized colour.
                        page.HiddenInkMaskColor = Windows.UI.Color.FromArgb(255, 199, 205, 212);
                        page.HiddenInkSize = 28.0;
                        page.HiddenInkRevealDurationMs = HiddenInkRevealState.DefaultRevealDurationMs;
                        page.SetInputMode(CustomInkInputProcessingMode.HiddenInk);
                        break;
                    case ToolType.Eraser:
                        page.SetInputMode(CustomInkInputProcessingMode.Erasing);
                        break;
                    case ToolType.Shape:
                        page.CurrentShape = _shapeKind;
                        page.ShapeIsDashed = _shapeIsDashed;
                        page.ShapeColor = _shapeColor;
                        page.ShapeStrokeSize = _shapeSize;
                        page.SetInputMode(CustomInkInputProcessingMode.Shape);
                        break;
                    case ToolType.Laser:
                        page.SetInputMode(CustomInkInputProcessingMode.Laser);
                        break;
                    case ToolType.AreaHighlight:
                        // Drag-to-rect over any content (text, images, ink);
                        // colour + fill alpha ride the highlighter colour.
                        page.AreaHighlightColor = _highlighterColor;
                        page.AreaHighlightOpacity = AreaHighlightFillOpacity;
                        page.SetInputMode(CustomInkInputProcessingMode.AreaHighlight);
                        break;
                    default:
                        // None/Select/StickyNote/Text/TextHighlight — ink
                        // surface idles; Select already armed its overlay
                        // above and TextHighlight armed the PDF
                        // text-selection layer above (WPF parity: those
                        // modes share SetInputMode(None)).
                        page.SetInputMode(CustomInkInputProcessingMode.None);
                        break;
                }

                page.CancelInteraction();
            }
        }

        private void PenOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_applicationSettings == null)
                return;
            _applicationSettings.PenOnlyMode = PenOnlyButton.IsChecked == true;
            AppSettingsService.Save(_applicationSettings);
            foreach (var page in _pageControls)
                page.Ink.PenOnlyMode = _applicationSettings.PenOnlyMode;
        }

        // ── Task 22: on-screen ruler (viewport overlay, NOT a ToolType) ───
        //
        // WPF parity: the ruler is an overlay toggle that stays active
        // alongside the current tool (e.g. Pen + ruler ON). Session-only —
        // neither persisted to settings nor saved with the document.
        // Pen/Highlighter strokes drawn near a long edge are constrained by
        // it (Core StrokeGeometry.ConstrainPointsToRuler via each page's
        // GetRulerGeometryInPageCoords); snapped strokes are ordinary ink so
        // undo/save work naturally, and the ruler itself never touches
        // undo/dirty.

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
        private uint? _rulerPointerId;

        private const double DefaultRulerLength = 360.0;
        private const double MinRulerLength = 80.0;
        private double _rulerLength = DefaultRulerLength;
        private const double RulerHeight = 56.0;
        private const double RulerEndCapZone = 14.0;      // end zones rotate instead of move
        private const double RulerRotationSnapDegrees = 15.0;
        private bool _isResizingRuler;
        private bool _rulerResizeFromLeft;
        private Canvas _rulerTickCanvas;
        private FrameworkElement _rulerLeftLengthHandle;
        private FrameworkElement _rulerRightLengthHandle;

        // WinUI keeps UIElement.ProtectedCursor protected, so per-element
        // hover cursors (WPF Cursor=... parity) need these tiny derived
        // types instead of property assignment from the page.
        private sealed class CursorGrid : Grid
        {
            internal void SetCursor(InputSystemCursorShape shape) =>
                ProtectedCursor = InputSystemCursor.Create(shape);
        }

        /// <summary>
        /// The eight-point text-resize handle square — WPF
        /// <c>TextResizeHandleBorder</c> parity. A Grid subclass gives the
        /// handle a UI Automation peer (plain panels have none in WinUI 3)
        /// and a settable ProtectedCursor; the Tag carries the
        /// <see cref="TextResizeHandle"/> role.
        /// </summary>
        private sealed class TextResizeHandleElement : Grid
        {
            internal void SetCursor(InputSystemCursorShape shape) =>
                ProtectedCursor = InputSystemCursor.Create(shape);

            protected override Microsoft.UI.Xaml.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
                => new Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer(this);
        }

        /// <summary>Alignment combo row — label + <see cref="TextAlignment"/> value (WPF TextAlignmentOption).</summary>
        private sealed record TextAlignmentOption(TextAlignment Value, string Label);

        /// <summary>
        /// WPF DocumentSnapshotAction parity — a structural PDF edit (insert
        /// pages, rotate page) is undone/redone by atomically restoring the
        /// captured before/after document bytes and the persisted bookmark
        /// list, then reloading the document. The undo/redo loop hands this
        /// action the caller's operation lease via <see cref="SetOperationLease"/>;
        /// the reload swaps the session, so the action publishes the lease of
        /// the reloaded document on <see cref="CompletedOperationLease"/> for
        /// the caller's staleness check.
        /// </summary>
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

            public string Description => "Structural document edit";

            // The snapshot write replaces the PDF bytes itself — the applied
            // state already matches disk, so this transition is not dirty.
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

        /// <summary>Overlay toggle — the button is NOT in the exclusive tool set.</summary>
        private void RulerToolButton_Click(object sender, RoutedEventArgs e)
        {
            SetRulerVisible(RulerToolButton.IsChecked == true);
        }

        private void SetRulerVisible(bool visible)
        {
            _rulerVisible = visible;
            RulerToolButton.IsChecked = visible;
            if (RulerIcon != null)
            {
                RulerIcon.Stroke = ResolveThemeBrush(
                    visible ? "ThemeAccentBrush" : "ThemeForegroundBrush",
                    visible ? Color.FromArgb(0xFF, 0x25, 0x63, 0xEB) : Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B));
            }

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
                _isResizingRuler = false;
                _rulerPointerId = null;
            }
        }

        /// <summary>
        /// Builds the ruler visual once (Grid 360×56: semi-transparent
        /// rounded body, tick marks every 10px with longer ticks every
        /// 50px, a centre handle dot, transparent end-cap rectangles that
        /// afford rotation, and edge length handles). Rotation snaps to
        /// 15° increments — WPF EnsureRulerVisual parity.
        /// </summary>
        private void EnsureRulerVisual()
        {
            if (_rulerVisual != null)
                return;

            _rulerRotate = new RotateTransform { Angle = 0, CenterX = _rulerLength / 2, CenterY = RulerHeight / 2 };

            var ruler = new CursorGrid
            {
                Width = _rulerLength,
                Height = RulerHeight,
                // Keep the ruler body draggable even though its visual
                // children are intentionally non-hit-testable. The full
                // overlay canvas stays background-free so empty space
                // still passes through to the document surface.
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                RenderTransform = _rulerRotate,
            };
            ruler.SetCursor(InputSystemCursorShape.SizeAll);
            AutomationProperties.SetAutomationId(ruler, "Editor.RulerVisual");
            AutomationProperties.SetName(ruler, LocalizationService.Get("Editor.RulerTooltip"));

            // Semi-transparent body — chrome resources only (the ruler is
            // application chrome, not document content).
            var rulerBody = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                StrokeThickness = 1,
                RadiusX = 6,
                RadiusY = 6,
                IsHitTestVisible = false,
                Opacity = 0.92,
                Fill = ResolveThemeBrush("ThemeToolbarBrush", Color.FromArgb(0xF2, 0xF8, 0xF9, 0xFC)),
                Stroke = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
            };
            ruler.Children.Add(rulerBody);

            _rulerTickCanvas = new Canvas { IsHitTestVisible = false };
            RebuildRulerTicks();
            ruler.Children.Add(_rulerTickCanvas);

            // Centre rotation handle dot.
            var rulerCenterDot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Opacity = 0.82,
                Fill = ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)),
            };
            ruler.Children.Add(rulerCenterDot);

            // Transparent end-cap zones: hitting them starts a rotation
            // drag instead of a move (non-null Fill required for hit tests).
            var capFill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            var leftCap = new CursorGrid
            {
                Width = RulerEndCapZone,
                Height = RulerHeight,
                Background = capFill,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var rightCap = new CursorGrid
            {
                Width = RulerEndCapZone,
                Height = RulerHeight,
                Background = capFill,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            leftCap.SetCursor(InputSystemCursorShape.SizeNortheastSouthwest);
            rightCap.SetCursor(InputSystemCursorShape.SizeNortheastSouthwest);
            ruler.Children.Add(leftCap);
            ruler.Children.Add(rightCap);

            _rulerLeftLengthHandle = CreateRulerLengthHandle(HorizontalAlignment.Left);
            _rulerRightLengthHandle = CreateRulerLengthHandle(HorizontalAlignment.Right);
            ruler.Children.Add(_rulerLeftLengthHandle);
            ruler.Children.Add(_rulerRightLengthHandle);

            // Pointer interactions: left-drag the body = move; left-drag
            // either end cap OR right-drag anywhere = rotate; the edge
            // handles resize. Pen/touch drags are allowed (GoodNotes
            // style); the ruler never creates ink so pen-only mode does
            // not apply to it.
            ruler.PointerPressed += Ruler_PointerPressed;
            ruler.PointerMoved += Ruler_PointerMoved;
            ruler.PointerReleased += Ruler_PointerReleased;
            ruler.PointerCanceled += Ruler_PointerCanceled;
            ruler.PointerCaptureLost += Ruler_PointerCaptureLost;

            _rulerVisual = ruler;

            // First show: default to the middle of the viewport so the
            // ruler can never appear off-screen.
            double vw = RulerOverlayCanvas.ActualWidth > 0
                ? RulerOverlayCanvas.ActualWidth
                : PdfScrollViewer.ViewportWidth;
            double vh = RulerOverlayCanvas.ActualHeight > 0
                ? RulerOverlayCanvas.ActualHeight
                : PdfScrollViewer.ViewportHeight;
            if (vw <= 0) vw = 800;
            if (vh <= 0) vh = 600;
            _rulerCenter = new Point(vw / 2, vh / 2);
            UpdateRulerPosition();

            RulerOverlayCanvas.Children.Add(ruler);
        }

        private void Ruler_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_rulerVisible || _rulerVisual == null || _rulerPointerId != null)
                return;

            var props = e.GetCurrentPoint(_rulerVisual).Properties;
            bool isLeft = props.IsLeftButtonPressed;
            bool isRight = props.IsRightButtonPressed;
            if (!isLeft && !isRight)
                return;

            // GetCurrentPoint applies the ruler's RenderTransform, so local
            // coordinates are the ruler's own (unrotated) frame — the end
            // zones stay the first/last 14px of the body at any angle.
            var local = e.GetCurrentPoint(_rulerVisual).Position;
            var viewport = e.GetCurrentPoint(RulerOverlayCanvas).Position;

            if (isLeft && IsRulerLengthHandle(e.OriginalSource as DependencyObject, out bool fromLeft))
            {
                StartRulerLengthResize(viewport, fromLeft);
            }
            else
            {
                bool inEndZone = local.X < RulerEndCapZone || local.X > _rulerLength - RulerEndCapZone;
                // Right-drag anywhere rotates (alternative affordance when
                // the end caps are hard to hit at steep angles).
                StartRulerManipulation(viewport, rotating: isRight || inEndZone);
            }

            _rulerVisual.CapturePointer(e.Pointer);
            _rulerPointerId = e.Pointer.PointerId;
            e.Handled = true;
        }

        private void Ruler_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_rulerPointerId == null || e.Pointer.PointerId != _rulerPointerId.Value)
                return;
            if (!_isDraggingRuler && !_isRotatingRuler && !_isResizingRuler)
                return;

            var p = e.GetCurrentPoint(RulerOverlayCanvas).Position;

            if (_isResizingRuler)
            {
                UpdateRulerLengthFromPointer(p);
            }
            else if (_isDraggingRuler)
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
            e.Handled = true;
        }

        private void Ruler_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_rulerPointerId == null || e.Pointer.PointerId != _rulerPointerId.Value)
                return;
            _rulerPointerId = null;
            _rulerVisual?.ReleasePointerCapture(e.Pointer);
            _isDraggingRuler = false;
            _isRotatingRuler = false;
            _isResizingRuler = false;
            e.Handled = true;
        }

        private void Ruler_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_rulerPointerId == null || e.Pointer.PointerId != _rulerPointerId.Value)
                return;
            _rulerPointerId = null;
            _isDraggingRuler = false;
            _isRotatingRuler = false;
            _isResizingRuler = false;
        }

        private void Ruler_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _rulerPointerId = null;
            _isDraggingRuler = false;
            _isRotatingRuler = false;
            _isResizingRuler = false;
        }

        private void StartRulerManipulation(Point viewportPoint, bool rotating)
        {
            _isDraggingRuler = !rotating;
            _isRotatingRuler = rotating;
            _isResizingRuler = false;
            _rulerDragOffset = new Point(viewportPoint.X - _rulerCenter.X, viewportPoint.Y - _rulerCenter.Y);
            _rotateStartPointerAngle = Math.Atan2(viewportPoint.Y - _rulerCenter.Y, viewportPoint.X - _rulerCenter.X) * 180.0 / Math.PI;
            _rotateStartRulerAngle = _rulerAngle;
        }

        private void StartRulerLengthResize(Point viewportPoint, bool fromLeft)
        {
            _isResizingRuler = true;
            _rulerResizeFromLeft = fromLeft;
            _isDraggingRuler = false;
            _isRotatingRuler = false;
            UpdateRulerLengthFromPointer(viewportPoint);
        }

        private static FrameworkElement CreateRulerLengthHandle(HorizontalAlignment alignment)
        {
            var handle = new CursorGrid
            {
                Width = 12,
                Height = 12,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(alignment == HorizontalAlignment.Left ? 2 : 0, 0, alignment == HorizontalAlignment.Right ? 2 : 0, 0),
                Tag = alignment == HorizontalAlignment.Left ? "ruler-length-left" : "ruler-length-right",
                // Non-null Background required for the wrapper to hit-test.
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            };
            handle.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
                Fill = ResolveThemeBrush("ThemeSurfaceBrush", Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                Stroke = ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)),
            });
            handle.SetCursor(InputSystemCursorShape.SizeWestEast);
            return handle;
        }

        private bool IsRulerLengthHandle(DependencyObject source, out bool fromLeft)
        {
            fromLeft = false;
            while (source != null && !ReferenceEquals(source, _rulerVisual))
            {
                if (source is FrameworkElement element)
                {
                    if (ReferenceEquals(element, _rulerLeftLengthHandle)
                        || (element.Tag as string) == "ruler-length-left")
                    {
                        fromLeft = true;
                        return true;
                    }
                    if (ReferenceEquals(element, _rulerRightLengthHandle)
                        || (element.Tag as string) == "ruler-length-right")
                    {
                        fromLeft = false;
                        return true;
                    }
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private void RebuildRulerTicks()
        {
            if (_rulerTickCanvas == null)
                return;

            _rulerTickCanvas.Children.Clear();
            var tickBrush = ResolveThemeBrush("ThemeSubtleTextBrush", Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));
            for (double x = 10; x < _rulerLength; x += 10)
            {
                bool major = Math.Abs(x % 50) < 0.01;
                var tick = new Microsoft.UI.Xaml.Shapes.Line
                {
                    X1 = x, Y1 = 0,
                    X2 = x, Y2 = major ? 12 : 6,
                    StrokeThickness = 1,
                    IsHitTestVisible = false,
                    Opacity = 0.72,
                    Stroke = tickBrush,
                };
                _rulerTickCanvas.Children.Add(tick);
            }
        }

        private void ApplyRulerLengthToVisual()
        {
            if (_rulerVisual == null)
                return;

            _rulerVisual.Width = _rulerLength;
            if (_rulerRotate != null)
            {
                _rulerRotate.CenterX = _rulerLength / 2;
                _rulerRotate.CenterY = RulerHeight / 2;
            }
            RebuildRulerTicks();
            UpdateRulerPosition();
        }

        private void UpdateRulerLengthFromPointer(Point viewportPoint)
        {
            double rad = _rulerAngle * Math.PI / 180.0;
            double dirX = Math.Cos(rad);
            double dirY = Math.Sin(rad);
            double half = _rulerLength / 2;
            var left = new Point(_rulerCenter.X - half * dirX, _rulerCenter.Y - half * dirY);
            var right = new Point(_rulerCenter.X + half * dirX, _rulerCenter.Y + half * dirY);

            double t = ((viewportPoint.X - _rulerCenter.X) * dirX) + ((viewportPoint.Y - _rulerCenter.Y) * dirY);
            var projected = new Point(_rulerCenter.X + t * dirX, _rulerCenter.Y + t * dirY);

            if (_rulerResizeFromLeft)
                left = projected;
            else
                right = projected;

            double dx = right.X - left.X;
            double dy = right.Y - left.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < MinRulerLength)
            {
                double scale = MinRulerLength / Math.Max(length, 0.001);
                if (_rulerResizeFromLeft)
                    left = new Point(right.X - dx * scale, right.Y - dy * scale);
                else
                    right = new Point(left.X + dx * scale, left.Y + dy * scale);
                length = MinRulerLength;
            }

            _rulerLength = length;
            _rulerCenter = new Point((left.X + right.X) / 2, (left.Y + right.Y) / 2);
            ClampRulerCenter();
            ApplyRulerLengthToVisual();
        }

        private void UpdateRulerPosition()
        {
            Canvas.SetLeft(_rulerVisual, _rulerCenter.X - _rulerLength / 2);
            Canvas.SetTop(_rulerVisual, _rulerCenter.Y - RulerHeight / 2);
        }

        // Keeps the ruler reachable: clamping the CENTRE inside the viewport
        // guarantees at least the centre point of the body stays grabbable,
        // no matter how the ruler is rotated (WPF v1 keeps this simple).
        private void ClampRulerCenter()
        {
            double vw = RulerOverlayCanvas.ActualWidth > 0
                ? RulerOverlayCanvas.ActualWidth
                : PdfScrollViewer.ViewportWidth;
            double vh = RulerOverlayCanvas.ActualHeight > 0
                ? RulerOverlayCanvas.ActualHeight
                : PdfScrollViewer.ViewportHeight;
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
        /// users draw along the visible edge of the ruler. Rotating the
        /// ruler 180° swaps which physical edge is "top", so every
        /// direction stays usable.
        /// </summary>
        private (PointD TopA, PointD TopB, PointD BottomA, PointD BottomB)? GetRulerGeometryEndpoints()
        {
            if (!_rulerVisible || _rulerVisual == null)
                return null;

            double rad = _rulerAngle * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            // Unit vector along the ruler, and its "up" normal
            // (pre-rotation -Y rotated by the ruler angle).
            double dirX = cos, dirY = sin;
            double upX = sin, upY = -cos;
            double halfLen = _rulerLength / 2;
            double halfHeight = RulerHeight / 2;

            var topA = new PointD(
                    _rulerCenter.X - halfLen * dirX + halfHeight * upX,
                    _rulerCenter.Y - halfLen * dirY + halfHeight * upY);
            var topB = new PointD(
                    _rulerCenter.X + halfLen * dirX + halfHeight * upX,
                    _rulerCenter.Y + halfLen * dirY + halfHeight * upY);
            var bottomA = new PointD(
                    _rulerCenter.X - halfLen * dirX - halfHeight * upX,
                    _rulerCenter.Y - halfLen * dirY - halfHeight * upY);
            var bottomB = new PointD(
                    _rulerCenter.X + halfLen * dirX - halfHeight * upX,
                    _rulerCenter.Y + halfLen * dirY - halfHeight * upY);
            return (topA, topB, bottomA, bottomB);
        }

        // ── Undo/redo (Core IUndoAction over InkStrokeStore) ──────────────

        private void PushUndoAction(IUndoAction action)
        {
            if (action == null)
                return;

            if (!TryBeginDocumentEdit(out var editLease))
            {
                // The underlying event can arrive after it has already
                // changed the model. Retain that generation for the close
                // save loop instead of silently dropping it (WPF parity).
                _documentSaveCoordinator.RecordChange(action.LeavesDocumentDirty);
                SyncDirtyStateMirror();
                return;
            }

            using (editLease)
            {
                _undoStack.Push(action);
                _redoStack.Clear();
                UpdateUndoRedoButtons();
                ApplyDirtyStateForAction(action);
            }
        }

        /// <summary>
        /// WPF ApplyDirtyStateForAction parity — the ledger push and its
        /// dirty record are one transition. Every mutation action reports
        /// <see cref="IUndoAction.LeavesDocumentDirty"/> (all Core
        /// annotation actions return true; <see cref="DocumentSnapshotAction"/>
        /// returns false because it rewrites the PDF bytes itself).
        /// </summary>
        private void ApplyDirtyStateForAction(IUndoAction action)
        {
            _documentSaveCoordinator.RecordChange(action.LeavesDocumentDirty);
            SyncDirtyStateMirror();
        }

        private async Task PerformUndoAsync()
        {
            if (_undoStack.Count == 0)
                return;
            // T9: an undo is a document mutation — hold an admission lease
            // so close/navigation quiescence covers the in-flight action.
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using var _ = editLease;
            // Cancel live gestures first (ApplyToolToAllPages contract):
            // a selection-drag snapshot restore would re-apply the undone
            // transform, and an erase undo would insert duplicates over the
            // still-running gesture.
            foreach (var p in _pageControls)
                p.CancelInteraction();
            // Restore in-flight geometry first — an uncommitted drag/resize
            // must not survive as phantom layout over the restored state.
            CancelTextBoxDrag(restoreBounds: true);
            CancelTextResize(restoreBounds: true);
            var action = _undoStack.Peek();
            // A DocumentSnapshotAction reloads the document, which swaps the
            // operation session — hand it this lease so it can publish the
            // reloaded session's lease for the staleness check (WPF parity).
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (action is DocumentSnapshotAction snapshotAction)
                snapshotAction.SetOperationLease(operationLease);
            try
            {
                await action.UndoAsync();
                // A failed token-resolution undo stays on the stack as a
                // no-op — same contract as the WPF StrokesErasedAction path
                // (cross-page moves carry the same LastOperationSucceeded
                // contract, WPF :3815-3817 parity).
                if (action is InkStrokesErasedAction erased && !erased.LastOperationSucceeded)
                    return;
                if (action is InkSelectionCrossPageMoveAction crossPage
                    && !crossPage.LastOperationSucceeded)
                    return;
                if (action is AnnotationSelectionCrossPageMoveAction annotationCrossPage
                    && !annotationCrossPage.LastOperationSucceeded)
                    return;
                if (action is AnnotationItemsAddedAction itemsAdded
                    && !itemsAdded.LastOperationSucceeded)
                    return;
                if (action is AnnotationItemsRemovedAction itemsRemoved
                    && !itemsRemoved.LastOperationSucceeded)
                    return;
                // A snapshot undo ran against the pre-reload session — only
                // the lease it captured after reloading proves freshness.
                if (action is DocumentSnapshotAction snapshot
                    ? !snapshot.LastOperationSucceeded ||
                      !ValidateDocumentOperationLease(snapshot.CompletedOperationLease)
                    : !ValidateDocumentOperationLease(operationLease))
                {
                    return;
                }
                _undoStack.Pop();
                _redoStack.Push(action);
                UpdateUndoRedoButtons();
                // WPF parity: an applied undo records a dirty generation so
                // the T9 save pipeline persists the reverted state.
                ApplyDirtyStateForAction(action);
            }
            catch (Exception ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.UndoFailed", ex.Message), "", 3500);
            }
            finally
            {
                if (action is DocumentSnapshotAction completedSnapshotAction)
                    completedSnapshotAction.SetOperationLease(null);
            }
        }
        private async Task PerformRedoAsync()
        {
            if (_redoStack.Count == 0)
                return;
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using var _ = editLease;
            foreach (var p in _pageControls)
                p.CancelInteraction();
            CancelTextBoxDrag(restoreBounds: true);
            CancelTextResize(restoreBounds: true);
            var action = _redoStack.Peek();
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (action is DocumentSnapshotAction snapshotAction)
                snapshotAction.SetOperationLease(operationLease);
            try
            {
                await action.RedoAsync();
                if (action is InkStrokesErasedAction erased && !erased.LastOperationSucceeded)
                    return;
                if (action is InkSelectionCrossPageMoveAction crossPage
                    && !crossPage.LastOperationSucceeded)
                    return;
                if (action is AnnotationSelectionCrossPageMoveAction annotationCrossPage
                    && !annotationCrossPage.LastOperationSucceeded)
                    return;
                if (action is AnnotationItemsAddedAction itemsAdded
                    && !itemsAdded.LastOperationSucceeded)
                    return;
                if (action is AnnotationItemsRemovedAction itemsRemoved
                    && !itemsRemoved.LastOperationSucceeded)
                    return;
                if (action is DocumentSnapshotAction snapshot
                    ? !snapshot.LastOperationSucceeded ||
                      !ValidateDocumentOperationLease(snapshot.CompletedOperationLease)
                    : !ValidateDocumentOperationLease(operationLease))
                {
                    return;
                }
                _redoStack.Pop();
                _undoStack.Push(action);
                UpdateUndoRedoButtons();
                ApplyDirtyStateForAction(action);
            }
            catch (Exception ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.RedoFailed", ex.Message), "", 3500);
            }
            finally
            {
                if (action is DocumentSnapshotAction completedSnapshotAction)
                    completedSnapshotAction.SetOperationLease(null);
            }
        }
        private void UpdateUndoRedoButtons()
        {
            if (UndoButton != null)
                UndoButton.IsEnabled = _undoStack.Count > 0;
            if (RedoButton != null)
                RedoButton.IsEnabled = _redoStack.Count > 0;
        }

        private void ClearUndoRedoHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            UpdateUndoRedoButtons();
        }

        /// <summary>
        /// Records an annotation edit in the save coordinator — WPF
        /// MarkDirty parity. The T9 pipeline consumes the generation when
        /// autosave/manual-save arrives; until then the mirror exposes the
        /// flag for tests and navigation guards.
        /// </summary>
        private void MarkDirty()
        {
            _documentSaveCoordinator.MarkDirty();
            SyncDirtyStateMirror();
        }

        private void SyncDirtyStateMirror()
        {
            _isDirty = _documentSaveCoordinator.IsDirty;
            _dirtyGeneration = _documentSaveCoordinator.DirtyGeneration;
        }

        // ── Page ink events ────────────────────────────────────────────────

        private void PageControl_StrokeCollected(object sender, InkStrokeData stroke)
        {
            if (_isLoadingAnnotations || sender is not PdfPageControl page)
                return;
            PushUndoAction(new InkStrokeAddedAction(page.Ink.Store, stroke));
        }

        /// <summary>
        /// Shape recognition swapped the collected stroke for its ideal
        /// outline in place. WPF pushed a StrokeAddedAction on the ideal
        /// placement (undo dropped the whole gesture); the WinUI editor
        /// instead pushes <see cref="InkStrokeReplacedAction"/> so undo
        /// restores the user's raw scribble — a deliberate spec change.
        /// </summary>
        private void PageControl_StrokeRecognized(object sender, InkStrokeRecognizedEventArgs e)
        {
            if (_isLoadingAnnotations || sender is not PdfPageControl page)
                return;
            PushUndoAction(new InkStrokeReplacedAction(
                page.Ink.Store,
                e.Token,
                e.OriginalIndex,
                e.OriginalSnapshot,
                e.IdealSnapshot));
        }

        private void PageControl_StrokesErased(object sender, InkStrokesErasedEventArgs e)
        {
            if (_isLoadingAnnotations || sender is not PdfPageControl page)
                return;
            PushUndoAction(new InkStrokesErasedAction(
                page.Ink.Store,
                e.RemovedPlacements.ToList(),
                e.AddedPlacements.ToList()));
        }

        private void PageControl_InkMutated(object sender, EventArgs e)
        {
            // WPF marks dirty unconditionally here because its quiet load
            // path raises QuietStrokeMutation instead. WinUI quiet mutators
            // (AddStroke/AddHiddenInk/…) raise InkMutated as well, so the
            // load-time sweep under _isLoadingAnnotations must suppress the
            // dirty mark — thumbnail invalidation stays unconditional.
            if (!_isLoadingAnnotations)
                MarkDirty();
            if (sender is PdfPageControl page)
                InvalidateThumbnail(page.PageIndex);
        }

        // ── Phase B page events: selection, shapes, hidden ink ──────────

        private void PageControl_SelectionChanged(object sender, AnnotationSelectionChangedEventArgs e)
        {
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
            if (sender is not PdfPageControl page)
                return;

            // WPF cross-page parity: when the selection's post-drag bounds
            // centre lands on a DIFFERENT page, the strokes transfer stores
            // inside one undoable action instead of a plain move.
            Rect bounds = page.GetSelectionBounds();
            if (bounds.IsEmpty)
            {
                if (e.SelectedTextContainers.Count > 0)
                {
                    PushUndoAction(new AnnotationSelectionMoveAction(
                        page, e.Strokes, e.SelectedTextContainers, e.DeltaX, e.DeltaY));
                }
                else
                {
                    PushUndoAction(new InkSelectionMoveAction(
                        page.Ink.Store, e.Strokes, e.DeltaX, e.DeltaY));
                }
                MarkDirty();
                return;
            }

            var centerInPage = new PointD(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            PdfPageControl targetPage = FindPageAtContainerPoint(page, centerInPage);

            if (targetPage != null && targetPage != page)
            {
                var targetOriginInPage = targetPage.TransformToVisual(page)
                    .TransformPoint(new Point(0, 0));
                double adjustX = -targetOriginInPage.X;
                double adjustY = -targetOriginInPage.Y;

                page.ClearSelection();

                if (e.SelectedTextContainers.Count > 0)
                {
                    // Mixed selection crosses pages: containers ride along
                    // through the host transfer (WPF SelectionCrossPageMove
                    // with containers).
                    var moveAction = new AnnotationSelectionCrossPageMoveAction(
                        page.Ink.Store,
                        targetPage.Ink.Store,
                        page,
                        targetPage,
                        e.Strokes,
                        e.SelectedTextContainers,
                        e.DeltaX, e.DeltaY,
                        adjustX, adjustY,
                        e.Strokes.Select(s => page.Ink.Store.CaptureStrokePlacement(s)).ToList());
                    bool transferred;
                    try
                    {
                        transferred = moveAction.ExecuteInitialTransfer();
                    }
                    catch
                    {
                        // A host failure mid-gesture must not corrupt the
                        // editor — the action guards its own legs, this is
                        // the last-resort net.
                        transferred = false;
                    }
                    if (transferred)
                        PushUndoAction(moveAction);
                }
                else
                {
                    var moveAction = new InkSelectionCrossPageMoveAction(
                        page.Ink.Store,
                        targetPage.Ink.Store,
                        e.Strokes,
                        e.DeltaX, e.DeltaY,
                        adjustX, adjustY,
                        e.Strokes.Select(s => page.Ink.Store.CaptureStrokePlacement(s)).ToList());

                    if (moveAction.ExecuteInitialTransfer())
                        PushUndoAction(moveAction);
                }
            }
            else if (e.SelectedTextContainers.Count > 0)
            {
                PushUndoAction(new AnnotationSelectionMoveAction(
                    page, e.Strokes, e.SelectedTextContainers, e.DeltaX, e.DeltaY));
            }
            else
            {
                PushUndoAction(new InkSelectionMoveAction(
                    page.Ink.Store, e.Strokes, e.DeltaX, e.DeltaY));
            }
            MarkDirty();
        }

        /// <summary>
        /// Finds the page whose bounds contain the given point expressed in
        /// the SOURCE page's coordinate system (translated through
        /// PagesContainer). WPF FindPageAtContainerPoint parity — null when
        /// the point lands in a page gap or outside the document.
        /// </summary>
        private PdfPageControl FindPageAtContainerPoint(PdfPageControl source, PointD centerInSource)
        {
            var centerInContainer = source.TransformToVisual(PagesContainer)
                .TransformPoint(new Point(centerInSource.X, centerInSource.Y));

            foreach (var p in _pageControls)
            {
                var ptInPage = PagesContainer.TransformToVisual(p)
                    .TransformPoint(centerInContainer);
                if (ptInPage.X >= 0 && ptInPage.X <= p.Width &&
                    ptInPage.Y >= 0 && ptInPage.Y <= p.Height)
                {
                    return p;
                }
            }
            return null;
        }

        private void PageControl_SelectionResizeCompleted(object sender, SelectionResizeCompletedEventArgs e)
        {
            if (sender is not PdfPageControl page)
                return;
            if (e.SelectedTextContainers.Count > 0)
            {
                PushUndoAction(new AnnotationSelectionResizeAction(
                    page, e.Strokes, e.SelectedTextContainers, e.TotalScale, e.Anchor));
            }
            else
            {
                PushUndoAction(new InkSelectionResizeAction(
                    page.Ink.Store, e.Strokes, e.TotalScale, e.Anchor));
            }
            MarkDirty();
        }

        private void PageControl_SelectionRotateCompleted(object sender, SelectionRotateCompletedEventArgs e)
        {
            if (sender is not PdfPageControl page)
                return;
            if (e.SelectedTextContainers.Count > 0)
            {
                PushUndoAction(new AnnotationSelectionRotateAction(
                    page, e.Strokes, e.SelectedTextContainers, e.TotalDegrees, e.Center));
            }
            else
            {
                PushUndoAction(new InkSelectionRotateAction(
                    page.Ink.Store, e.Strokes, e.TotalDegrees, e.Center));
            }
            MarkDirty();
        }

        /// <summary>
        /// A shape drag committed N strokes as ONE undoable unit (arrow
        /// shaft+head, baked dash segments share the ShapeGroupId). WPF then
        /// arms Select and selects the group — same here.
        /// </summary>
        private void PageControl_ShapeCommittedUndoable(object sender, IReadOnlyList<InkStrokeData> strokes)
        {
            if (_isLoadingAnnotations || sender is not PdfPageControl page
                || strokes == null || strokes.Count == 0)
                return;

            var placements = strokes
                .Select(s => page.Ink.Store.CaptureStrokePlacement(s))
                .ToList();
            PushUndoAction(new InkStrokesAddedAction(page.Ink.Store, placements));

            ActivateTool(ToolType.Select);
            page.SelectItems(strokes);
            _activeSelectionPage = page;
        }

        private void PageControl_HiddenInkCreated(object sender, HiddenInkAnnotation annotation)
        {
            if (_isLoadingAnnotations || annotation == null || sender is not PdfPageControl page)
                return;
            PushUndoAction(new HiddenInkAddedAction(page.HiddenInkStore, annotation));
        }

        private void PageControl_HiddenInksRemoved(object sender, HiddenInksRemovedEventArgs e)
        {
            if (_isLoadingAnnotations || e?.Entries == null
                || e.Entries.Count == 0 || sender is not PdfPageControl page)
                return;
            PushUndoAction(new HiddenInksRemovedAction(page.HiddenInkStore, e.Entries));
        }

        // ==================================================================
        // Task 8 Phase B: overlay annotations + PDF text selection
        // ==================================================================

        /// <summary>WPF PageControl_ImagesChanged — overlay-set edits mark dirty (load is guarded).</summary>
        private void PageControl_ImagesChanged(object sender, EventArgs e)
        {
            if (!_isLoadingAnnotations)
                MarkDirty();
        }

        /// <summary>
        /// WPF PageControl_AreaHighlightCreated — the page already committed
        /// the container; the editor only pushes the undo action.
        /// </summary>
        private void PageControl_AreaHighlightCreated(object sender, Grid container)
        {
            if (sender is PdfPageControl page && container != null)
            {
                PushUndoAction(new AnnotationItemsAddedAction(
                    page.Ink.Store,
                    new List<InkStrokePlacement>(),
                    page,
                    new List<Grid> { container }));
            }
        }

        /// <summary>
        /// Clears the active PDF text selection — painted rects on every
        /// page plus the editor's offset state (WPF ClearPdfTextSelection).
        /// </summary>
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

        /// <summary>
        /// WPF TryCopySelectedPdfTextToClipboard — annotation selection wins;
        /// the pdf-text copy only fires while the surface tool is passive.
        /// </summary>
        private bool TryCopySelectedPdfTextToClipboard()
        {
            if ((_currentTool != ToolType.None && _currentTool != ToolType.Select)
                || string.IsNullOrEmpty(_selectedPdfText))
                return false;

            try
            {
                var package = new DataPackage();
                package.SetText(_selectedPdfText);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Repaints the selection rects on the owning page and refreshes
        /// <see cref="_selectedPdfText"/> (WPF UpdatePdfTextSelectionVisuals).
        /// </summary>
        private void UpdatePdfTextSelectionVisuals()
        {
            if (_pdfTextSelectionPage == null
                || _pdfTextSelectionInfo?.Text == null)
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

            _pdfTextSelectionPage.SetPdfTextSelectionRects(
                BuildPdfTextSelectionRects(_pdfTextSelectionInfo, start, end));
            _selectedPdfText = _pdfTextSelectionInfo.Text.Substring(start, end - start + 1);
        }

        private async void PageControl_PdfTextSelectionPointerPressed(
            object sender, PdfTextSelectionPointerEventArgs e)
        {
            if ((_currentTool != ToolType.None && _currentTool != ToolType.TextHighlight)
                || sender is not PdfPageControl page)
                return;

            using var operationLease = CaptureDocumentOperationLease(page);
            if (!ValidateDocumentOperationLease(operationLease, page) || !_pageControls.Contains(page))
                return;
            if (_selectedTextBox != null)
                DeselectTextBox();

            // WPF Keyboard.Focus(PdfScrollViewer): pull focus off chrome so
            // Escape/arrows reach the page-level key handlers mid-drag.
            PdfScrollViewer.Focus(FocusState.Programmatic);
            ClearPdfTextSelection();

            // Capture the request generation AFTER ClearPdfTextSelection —
            // the clear itself increments the field, so a pre-clear capture
            // is always stale and the handler returned before arming the
            // drag. WPF has the identical ordering bug (increment at :2374
            // then clear at :2379) — its selection silently dead-arms too;
            // fixed here so the port actually works.
            int requestId = Interlocked.Increment(ref _pdfTextSelectionRequestId);
            _pdfTextSelectionPressPoint = e.Position;

            try
            {
                var textInfo = _pdfService.TryGetCachedPageTextInfo(page.PageIndex, out var cached)
                    ? cached
                    : await _pdfService.GetPageTextInfoAsync(page.PageIndex, operationLease.Token);

                if (!ValidateDocumentOperationLease(operationLease, page)
                    || !_pageControls.Contains(page)
                    || requestId != _pdfTextSelectionRequestId
                    || (_currentTool != ToolType.None && _currentTool != ToolType.TextHighlight))
                    return;

                int anchorOffset = PdfTextSelectionGeometry.FindNearestTextOffset(
                    textInfo, new PointD(e.Position.X, e.Position.Y), 24.0);
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

        private void PageControl_PdfTextSelectionPointerMoved(
            object sender, PdfTextSelectionPointerEventArgs e)
        {
            if (!_isPdfTextSelectionDragging || _pdfTextSelectionInfo == null
                || !ReferenceEquals(sender, _pdfTextSelectionPage))
                return;
            if (!e.IsLeftButtonPressed)
                return;

            int offset = PdfTextSelectionGeometry.FindNearestTextOffset(
                _pdfTextSelectionInfo,
                new PointD(e.Position.X, e.Position.Y),
                double.PositiveInfinity);
            if (offset < 0)
                return;

            _pdfTextSelectionActiveOffset = offset;
            if (!_pdfTextSelectionExceededThreshold)
            {
                _pdfTextSelectionExceededThreshold =
                    Math.Abs(e.Position.X - _pdfTextSelectionPressPoint.X) >= PdfTextSelectionDragThreshold
                    || Math.Abs(e.Position.Y - _pdfTextSelectionPressPoint.Y) >= PdfTextSelectionDragThreshold;
            }
            if (_pdfTextSelectionExceededThreshold)
                UpdatePdfTextSelectionVisuals();
        }

        private void PageControl_PdfTextSelectionPointerReleased(
            object sender, PdfTextSelectionPointerEventArgs e)
        {
            Interlocked.Increment(ref _pdfTextSelectionRequestId);

            if (!_isPdfTextSelectionDragging || _pdfTextSelectionInfo == null
                || !ReferenceEquals(sender, _pdfTextSelectionPage))
            {
                ClearPdfTextSelection();
                return;
            }

            int offset = PdfTextSelectionGeometry.FindNearestTextOffset(
                _pdfTextSelectionInfo,
                new PointD(e.Position.X, e.Position.Y),
                double.PositiveInfinity);
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

            // WPF parity: under a text-markup highlighter mode the release
            // commits the persistent annotation and clears the selection.
            if (_currentTool == ToolType.TextHighlight)
            {
                int start = Math.Min(_pdfTextSelectionAnchorOffset, _pdfTextSelectionActiveOffset);
                int end = Math.Max(_pdfTextSelectionAnchorOffset, _pdfTextSelectionActiveOffset);
                var rects = BuildPdfTextSelectionRects(_pdfTextSelectionInfo, start, end);
                if (rects.Count > 0)
                {
                    if (_highlighterApplyMode == HighlighterApplyMode.TextHighlight)
                    {
                        var highlight = _pdfTextSelectionPage.AddHighlightAnnotation(rects, _highlighterColor);
                        if (highlight != null)
                            PushUndoAction(new HighlightAddedAction(_pdfTextSelectionPage, highlight));
                        MarkDirty();
                    }
                    else
                    {
                        var markup = PdfTextSelectionGeometry.BuildTextMarkupAnnotation(
                            rects.Select(r => new RectD(r.X, r.Y, r.Width, r.Height)).ToList(),
                            _highlighterApplyMode switch
                            {
                                HighlighterApplyMode.StrikeOut => TextMarkupKind.StrikeOut,
                                HighlighterApplyMode.Squiggly => TextMarkupKind.Squiggly,
                                _ => TextMarkupKind.Underline
                            },
                            _highlighterColor.R, _highlighterColor.G, _highlighterColor.B);
                        var container = _pdfTextSelectionPage.AddTextMarkup(markup);
                        if (container != null)
                            PushUndoAction(new AnnotationItemsAddedAction(
                                _pdfTextSelectionPage.Ink.Store,
                                new List<InkStrokePlacement>(),
                                _pdfTextSelectionPage,
                                new List<Grid> { container }));
                    }
                }
                ClearPdfTextSelection();
                return;
            }

            UpdatePdfTextSelectionVisuals();
        }

        private void PageControl_BlankContextRequested(object sender, EventArgs e)
        {
            ShowBlankContextMenu();
        }

        /// <summary>
        /// Delete-key selection removal: capture placements first so undo
        /// restores z-order, then quietly remove and push the batch action —
        /// WPF DeleteSelection → ItemsRemovedAction parity. When the
        /// selection holds text/sticky containers the combined
        /// <see cref="AnnotationItemsRemovedAction"/> keeps strokes and
        /// containers in ONE undo step.
        /// </summary>
        private void DeleteSelection()
        {
            if (_activeSelectionPage == null || !_activeSelectionPage.HasSelection)
                return;

            // Capture FIRST — ClearSelection() fires SelectionChanged(false)
            // synchronously and PageControl_SelectionChanged nulls
            // _activeSelectionPage, so post-clear dereferences would NRE.
            var page = _activeSelectionPage;
            var strokes = page.SelectedStrokes.ToList();
            var containers = page.SelectedTextContainers.ToList();
            var placements = strokes
                .Select(s => page.Ink.Store.CaptureStrokePlacement(s))
                .ToList();

            foreach (var stroke in strokes)
                page.Ink.Store.RemoveStrokeQuiet(stroke);
            foreach (var container in containers)
                page.RemoveTextContainerQuiet(container);

            if (containers.Count > 0)
            {
                PushUndoAction(new AnnotationItemsRemovedAction(
                    page.Ink.Store, placements, page, containers));
            }
            else
            {
                PushUndoAction(new InkStrokesRemovedAction(page.Ink.Store, placements));
            }
            page.ClearSelection();
            InvalidateThumbnail(page.PageIndex);
            MarkDirty();
        }

        // ==================================================================
        // Selection clipboard (WPF CopySelection/CutSelection/PasteSelection)
        // ==================================================================

        /// <summary>
        /// Ctrl+X — copy then delete through the normal selection path so the
        /// removal lands in undo history (WPF CutSelection parity).
        /// </summary>
        private void CutSelection()
        {
            if (_activeSelectionPage == null || !_activeSelectionPage.HasSelection)
                return;

            CopySelection();
            DeleteSelection();
        }

        /// <summary>
        /// Serializes the live selection — strokes, text boxes, images and
        /// sticky notes — as AnnotationData JSON on the clipboard (WPF
        /// CopySelection parity).
        /// </summary>
        private void CopySelection()
        {
            if (_activeSelectionPage == null || !_activeSelectionPage.HasSelection)
                return;

            try
            {
                var annotationData = new AnnotationData();
                var pageAnnotation = new PageAnnotation();

                foreach (var stroke in _activeSelectionPage.SelectedStrokes)
                    pageAnnotation.Strokes.Add(stroke.ToAnnotation());

                foreach (var container in _activeSelectionPage.SelectedTextContainers)
                {
                    if (_activeSelectionPage.TryGetTextAnnotation(container) is TextAnnotation text)
                    {
                        pageAnnotation.Texts.Add(text);
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
                                ImageDataBase64 = Convert.ToBase64String(imageData),
                                RotationDegrees = PdfPageControl.ReadAnnotationRotation(container),
                            });
                        }
                    }
                    else if (_activeSelectionPage.GetOverlayData(container) is StickyNoteAnnotation sticky)
                    {
                        double left = Canvas.GetLeft(container);
                        double top = Canvas.GetTop(container);
                        pageAnnotation.StickyNotes.Add(new StickyNoteAnnotation
                        {
                            Id = sticky.Id,
                            X = double.IsNaN(left) ? sticky.X : left,
                            Y = double.IsNaN(top) ? sticky.Y : top,
                            Text = sticky.Text,
                            Width = container.ActualWidth > 0 ? container.ActualWidth : container.Width,
                            Height = container.ActualHeight > 0 ? container.ActualHeight : container.Height,
                            R = sticky.R,
                            G = sticky.G,
                            B = sticky.B,
                            RotationDegrees = PdfPageControl.ReadAnnotationRotation(container),
                        });
                    }
                }

                annotationData.Pages["0"] = pageAnnotation;
                var json = JsonSerializer.Serialize(annotationData);

                var package = new DataPackage();
                package.SetText(json);
                Clipboard.SetContent(package);
                Clipboard.Flush();

                GetMainWindow()?.ShowToast(
                    LocalizationService.Get("Editor.SelectionCopied"), "\uE14D", 1500);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CopySelection] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Ctrl+V — rebuilds strokes + text boxes + images + sticky markers
        /// from clipboard JSON on the anchor page, offset either to the last
        /// clicked point or by (+20,+20), then pushes ONE
        /// <see cref="AnnotationItemsAddedAction"/> and auto-selects the
        /// pasted items (WPF PasteSelection parity). Sticky ids are
        /// regenerated; shape groups remap to fresh group ids.
        /// </summary>
        private async void PasteSelection()
        {
            try
            {
                var content = Clipboard.GetContent();
                if (content == null || !content.Contains(StandardDataFormats.Text))
                    return;

                var json = await content.GetTextAsync();
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var annotationData = JsonSerializer.Deserialize<AnnotationData>(json);
                if (annotationData?.Pages == null || !annotationData.Pages.ContainsKey("0"))
                    return;

                var pageAnnotation = annotationData.Pages["0"];
                if (pageAnnotation == null)
                    return;

                // The clicked anchor is only valid while that page is live —
                // a reload swaps the controls out from under it.
                var anchorPage = _lastClickedPage != null && _pageControls.Contains(_lastClickedPage)
                    ? _lastClickedPage
                    : null;
                var targetPage = anchorPage ?? _activeSelectionPage
                    ?? _pageControls.FirstOrDefault();
                if (targetPage == null)
                    return;

                double pasteOffsetX = 20.0;
                double pasteOffsetY = 20.0;

                if (anchorPage == targetPage)
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

                var pastedStrokes = new List<InkStrokeData>();
                var pastedContainers = new List<Grid>();

                if (pageAnnotation.Strokes != null)
                {
                    // Shape groups remap to one fresh group id per source
                    // group so a paste never merges with the originals.
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
                            Points = new List<double[]>(),
                        };
                        foreach (var point in strokeAnnotation.Points)
                            offsetStroke.Points.Add(new[] { point[0] + pasteOffsetX, point[1] + pasteOffsetY });

                        var stroke = targetPage.AddStroke(offsetStroke);
                        if (stroke != null)
                            pastedStrokes.Add(stroke);
                    }
                }

                if (pageAnnotation.Texts != null)
                {
                    // Paste lands inside the TARGET page bounds — the same
                    // clamp the drag path applies on release (P3s.8).
                    double pasteW = targetPage.TextOverlay.ActualWidth > 0
                        ? targetPage.TextOverlay.ActualWidth : targetPage.ActualWidth;
                    double pasteH = targetPage.TextOverlay.ActualHeight > 0
                        ? targetPage.TextOverlay.ActualHeight : targetPage.ActualHeight;
                    foreach (var textAnnotation in pageAnnotation.Texts)
                    {
                        var color = Windows.UI.Color.FromArgb(255,
                            textAnnotation.R, textAnnotation.G, textAnnotation.B);
                        var pastedBounds = TextAnnotationGeometry.ClampToPage(
                            new TextBoxBounds(
                                textAnnotation.X + pasteOffsetX,
                                textAnnotation.Y + pasteOffsetY,
                                textAnnotation.Width > 0
                                    ? textAnnotation.Width
                                    : TextAnnotationGeometry.DefaultWidth,
                                textAnnotation.Height > 0
                                    ? textAnnotation.Height
                                    : TextAnnotationGeometry.DefaultHeight),
                            pasteW,
                            pasteH);
                        var container = CreateTextBox(
                            targetPage,
                            new Point(pastedBounds.X, pastedBounds.Y),
                            color: color,
                            fontSize: textAnnotation.FontSize,
                            text: textAnnotation.Text,
                            select: false,
                            bold: textAnnotation.Bold,
                            italic: textAnnotation.Italic,
                            fontFamily: textAnnotation.FontFamily,
                            alignment: ParseTextAlignment(textAnnotation.Alignment),
                            width: textAnnotation.Width > 0 ? textAnnotation.Width : null,
                            height: textAnnotation.Height > 0 ? textAnnotation.Height : null,
                            rotationDegrees: textAnnotation.RotationDegrees);
                        if (container != null)
                            pastedContainers.Add(container);
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

                        var img = await targetPage.AddImageAsync(
                            imageBytes,
                            new Point(imageAnnotation.X + pasteOffsetX, imageAnnotation.Y + pasteOffsetY),
                            imageAnnotation.Width,
                            imageAnnotation.Height);
                        if (img != null)
                        {
                            PdfPageControl.ApplyAnnotationRotation(img, imageAnnotation.RotationDegrees);
                            pastedContainers.Add(img);
                        }
                    }
                }

                if (pageAnnotation.StickyNotes != null)
                {
                    foreach (var sticky in pageAnnotation.StickyNotes)
                    {
                        // Content, marker geometry and colour ride along —
                        // only the position and identity change (WPF parity).
                        var pasted = targetPage.AddStickyNote(new StickyNoteAnnotation
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            X = sticky.X + pasteOffsetX,
                            Y = sticky.Y + pasteOffsetY,
                            Text = sticky.Text,
                            Width = sticky.Width,
                            Height = sticky.Height,
                            R = sticky.R,
                            G = sticky.G,
                            B = sticky.B,
                            RotationDegrees = sticky.RotationDegrees,
                        });
                        if (pasted != null)
                            pastedContainers.Add(pasted);
                    }
                }

                if (pastedStrokes.Count > 0 || pastedContainers.Count > 0)
                {
                    // Undo state FIRST (selection is UI state, not undoable).
                    var placements = pastedStrokes
                        .Select(s => targetPage.Ink.Store.CaptureStrokePlacement(s))
                        .ToList();
                    PushUndoAction(new AnnotationItemsAddedAction(
                        targetPage.Ink.Store, placements, targetPage, pastedContainers));

                    // Auto-select the pasted content (WPF Task 8.2): a
                    // selection lingering on another page clears first so
                    // only the target page holds one.
                    foreach (var page in _pageControls)
                    {
                        if (page != targetPage && page.HasSelection)
                            page.ClearSelection();
                    }
                    targetPage.SelectItems(pastedStrokes, pastedContainers);
                    InvalidateThumbnail(targetPage.PageIndex);
                }

                MarkDirty();
                GetMainWindow()?.ShowToast(
                    LocalizationService.Get("Editor.SelectionPasted"), "\uE14D", 1500);
            }
            catch (Exception ex)
            {
                // WPF parity: a malformed clipboard payload is ignored, it
                // never takes the editor down.
                System.Diagnostics.Debug.WriteLine($"[PasteSelection] Error: {ex.Message}");
            }
        }

        // ----- Task 19 port: image annotations (clipboard paste / drag-drop) -----

        /// <summary>
        /// WPF Ctrl+V ordering: a bitmap on the clipboard wins over
        /// annotation JSON — the decoder returns false when nothing
        /// decodable is present so the JSON path still runs.
        /// </summary>
        private async void PasteClipboardImageOrSelection()
        {
            if (!await PasteClipboardImageAsync())
                PasteSelection();
        }

        /// <summary>
        /// Task 19: Ctrl+V with a bitmap/PNG/EMF on the clipboard. Decodes
        /// through <see cref="ClipboardImageDecoder"/>, drops the container
        /// on the target page (last clicked → selection → first), pushes one
        /// AnnotationItemsAddedAction and auto-selects — WPF
        /// PasteClipboardImage parity.
        /// </summary>
        private async Task<bool> PasteClipboardImageAsync()
        {
            try
            {
                DataPackageView content;
                try { content = Clipboard.GetContent(); }
                catch { return false; }

                var pngBytes = await ClipboardImageDecoder.TryGetPngBytesAsync(
                    content, includeWin32Clipboard: true);
                if (pngBytes == null || pngBytes.Length == 0)
                    return false;

                var targetPage = _lastClickedPage != null && _pageControls.Contains(_lastClickedPage)
                    ? _lastClickedPage
                    : null;
                targetPage ??= _activeSelectionPage ?? _pageControls.FirstOrDefault();
                if (targetPage == null)
                    return false;

                var position = ReferenceEquals(_lastClickedPage, targetPage)
                    ? _lastClickedPoint
                    : new Point(targetPage.ActualWidth / 2, targetPage.ActualHeight / 2);

                var container = await targetPage.AddImageAsync(pngBytes, position);
                if (container == null)
                    return false;

                if (!ReferenceEquals(_lastClickedPage, targetPage))
                {
                    // No click anchor: centre the image on the target page.
                    Canvas.SetLeft(container,
                        Math.Max(0, (targetPage.ActualWidth - container.Width) / 2));
                    Canvas.SetTop(container,
                        Math.Max(0, (targetPage.ActualHeight - container.Height) / 2));
                }

                PushUndoAction(new AnnotationItemsAddedAction(
                    targetPage.Ink.Store,
                    new List<InkStrokePlacement>(),
                    targetPage,
                    new List<Grid> { container }));

                foreach (var page in _pageControls)
                {
                    if (!ReferenceEquals(page, targetPage) && page.HasSelection)
                        page.ClearSelection();
                }
                targetPage.SelectItems(Array.Empty<InkStrokeData>(), new List<Grid> { container });
                InvalidateThumbnail(targetPage.PageIndex);
                MarkDirty();
                GetMainWindow()?.ShowToast(
                    LocalizationService.Get("Editor.ImagePasted"), "\uE8B7", 1500);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PasteClipboardImage] Error: {ex.Message}");
                return false;
            }
        }

        private static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg" };

        private static bool IsSupportedImageFile(string path)
            => Array.Exists(SupportedImageExtensions,
                ext => string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// WPF EditorPage_PreviewDragOver — advertise Copy while storage
        /// items are over the document surface. Extension filtering is async
        /// in WinRT, so it happens on Drop instead (anything undecodable is
        /// ignored there).
        /// </summary>
        private void EditorPage_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.Handled = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }

        /// <summary>
        /// WPF EditorPage_Drop — Explorer image files land on the page under
        /// the cursor (centre of the first visible page when over chrome),
        /// stair-stepped by 20 DIP for multiples, one undo action for all.
        /// </summary>
        private async void EditorPage_Drop(object sender, DragEventArgs e)
        {
            var deferral = e.GetDeferral();
            try
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                    return;
                var items = await e.DataView.GetStorageItemsAsync();
                var imageFiles = items?
                    .OfType<Windows.Storage.StorageFile>()
                    .Where(f => !string.IsNullOrEmpty(f.Path) && IsSupportedImageFile(f.Path))
                    .ToList();
                if (imageFiles == null || imageFiles.Count == 0)
                    return;
                e.Handled = true;

                // Resolve the page under the cursor (PagesContainer
                // coordinates — same translate trick as WPF).
                var pointInContainer = e.GetPosition(PagesContainer);
                PdfPageControl targetPage = null;
                Point pagePoint = default;
                foreach (var p in _pageControls)
                {
                    var ptInPage = PagesContainer.TransformToVisual(p).TransformPoint(pointInContainer);
                    if (ptInPage.X >= 0 && ptInPage.X <= p.ActualWidth
                        && ptInPage.Y >= 0 && ptInPage.Y <= p.ActualHeight)
                    {
                        targetPage = p;
                        pagePoint = ptInPage;
                        break;
                    }
                }

                if (targetPage == null)
                {
                    // Over a gap/chrome: first visible page, centred.
                    targetPage = GetVisiblePageControls().FirstOrDefault()
                        ?? _pageControls.FirstOrDefault();
                    if (targetPage == null)
                        return;
                    pagePoint = new Point(targetPage.ActualWidth / 2, targetPage.ActualHeight / 2);
                }

                var addedContainers = new List<Grid>();
                double stackOffset = 0;
                foreach (var file in imageFiles)
                {
                    byte[] bytes;
                    try { bytes = (await Windows.Storage.FileIO.ReadBufferAsync(file)).ToArray(); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EditorPage_Drop] Cannot read {file.Path}: {ex.Message}");
                        continue;
                    }

                    var container = await targetPage.AddImageAsync(
                        bytes, new Point(pagePoint.X + stackOffset, pagePoint.Y + stackOffset));
                    stackOffset += 20; // multiple files land stair-stepped
                    if (container != null)
                        addedContainers.Add(container);
                }

                if (addedContainers.Count == 0)
                    return;

                PushUndoAction(new AnnotationItemsAddedAction(
                    targetPage.Ink.Store,
                    new List<InkStrokePlacement>(),
                    targetPage,
                    addedContainers));

                foreach (var page in _pageControls)
                {
                    if (!ReferenceEquals(page, targetPage) && page.HasSelection)
                        page.ClearSelection();
                }
                targetPage.SelectItems(Array.Empty<InkStrokeData>(), addedContainers);
                InvalidateThumbnail(targetPage.PageIndex);
                MarkDirty();
                GetMainWindow()?.ShowToast(
                    LocalizationService.Get("Editor.ImageAdded"), "\uE8B7", 1500);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorPage_Drop] Error: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        /// <summary>
        /// WPF DuplicateSelection (Ctrl+D): clone the selection in place at
        /// (+20,+20) without touching the clipboard — strokes keep pressure
        /// data, images rebuild from their raw payload, one undo action for
        /// the whole duplicate, then auto-select.
        /// </summary>
        private async void DuplicateSelection()
        {
            var page = _activeSelectionPage;
            if (page == null || !page.HasSelection)
                return;

            try
            {
                const double offsetX = 20.0;
                const double offsetY = 20.0;

                var clonedStrokes = new List<InkStrokeData>();
                var clonedContainers = new List<Grid>();

                // Clone strokes: fresh point lists (offset applied, pressure
                // preserved — WPF Clone parity) + fresh shape group ids so
                // duplicates never merge with the originals.
                var duplicatedShapeGroups = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var stroke in page.SelectedStrokes)
                {
                    var clone = stroke.Clone();
                    clone.Points = clone.Points
                        .Select(p => new InkPointData(p.X + offsetX, p.Y + offsetY, p.Pressure))
                        .ToList();
                    var shape = stroke.GetShapeIdentity();
                    if (!string.IsNullOrWhiteSpace(shape.GroupId))
                    {
                        if (!duplicatedShapeGroups.TryGetValue(shape.GroupId, out var duplicateGroupId))
                        {
                            duplicateGroupId = Guid.NewGuid().ToString("N");
                            duplicatedShapeGroups[shape.GroupId] = duplicateGroupId;
                        }
                        clone.ApplyShapeIdentity(new ShapeStrokeIdentity(
                            duplicateGroupId, shape.Kind, shape.PartIndex, shape.IsDashed));
                    }
                    page.Ink.Store.AddStrokeQuiet(clone);
                    clonedStrokes.Add(clone);
                }

                foreach (var container in page.SelectedTextContainers.ToList())
                {
                    if (page.TryGetTextAnnotation(container) is TextAnnotation text)
                    {
                        var clone = CreateTextBox(
                            page,
                            new Point(text.X + offsetX, text.Y + offsetY),
                            color: Windows.UI.Color.FromArgb(255, text.R, text.G, text.B),
                            fontSize: text.FontSize,
                            text: text.Text,
                            select: false,
                            bold: text.Bold,
                            italic: text.Italic,
                            fontFamily: text.FontFamily,
                            alignment: ParseTextAlignment(text.Alignment),
                            width: text.Width > 0 ? text.Width : null,
                            height: text.Height > 0 ? text.Height : null,
                            rotationDegrees: text.RotationDegrees);
                        if (clone != null)
                            clonedContainers.Add(clone);
                    }
                    else if (PdfPageControl.IsImageContainer(container))
                    {
                        // Task 19: duplicate image annotations from their raw
                        // payload, keeping the live size + rotation.
                        var imageData = page.GetImageData(container);
                        if (imageData != null)
                        {
                            double left = Canvas.GetLeft(container);
                            double top = Canvas.GetTop(container);
                            var clone = await page.AddImageAsync(
                                imageData,
                                new Point(
                                    (double.IsNaN(left) ? 0 : left) + offsetX,
                                    (double.IsNaN(top) ? 0 : top) + offsetY),
                                container.ActualWidth > 0 ? container.ActualWidth : container.Width,
                                container.ActualHeight > 0 ? container.ActualHeight : container.Height);
                            if (clone != null)
                            {
                                PdfPageControl.ApplyAnnotationRotation(
                                    clone, PdfPageControl.ReadAnnotationRotation(container));
                                clonedContainers.Add(clone);
                            }
                        }
                    }
                    else if (page.GetOverlayData(container) is StickyNoteAnnotation sticky)
                    {
                        double left = Canvas.GetLeft(container);
                        double top = Canvas.GetTop(container);
                        var clone = page.AddStickyNote(new StickyNoteAnnotation
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            X = (double.IsNaN(left) ? sticky.X : left) + offsetX,
                            Y = (double.IsNaN(top) ? sticky.Y : top) + offsetY,
                            Text = sticky.Text,
                            Width = container.ActualWidth > 0 ? container.ActualWidth : container.Width,
                            Height = container.ActualHeight > 0 ? container.ActualHeight : container.Height,
                            R = sticky.R,
                            G = sticky.G,
                            B = sticky.B,
                            RotationDegrees = PdfPageControl.ReadAnnotationRotation(container),
                        });
                        if (clone != null)
                            clonedContainers.Add(clone);
                    }
                }

                if (clonedStrokes.Count == 0 && clonedContainers.Count == 0)
                    return;

                // Undo state FIRST (selection is UI state, not undoable).
                var placements = clonedStrokes
                    .Select(s => page.Ink.Store.CaptureStrokePlacement(s))
                    .ToList();
                PushUndoAction(new AnnotationItemsAddedAction(
                    page.Ink.Store, placements, page, clonedContainers));

                // Cross-page rule: clear any selection lingering on other
                // pages, then auto-select the duplicates.
                foreach (var other in _pageControls)
                {
                    if (!ReferenceEquals(other, page) && other.HasSelection)
                        other.ClearSelection();
                }
                page.SelectItems(clonedStrokes, clonedContainers);
                InvalidateThumbnail(page.PageIndex);
                MarkDirty();
                GetMainWindow()?.ShowToast(
                    LocalizationService.Get("Editor.Duplicated"), "\uE8C8", 1500);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DuplicateSelection] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clipboard holds annotation JSON text or any decodable image
        /// payload — WPF HasPasteableClipboard (text || image || EMF).
        /// </summary>
        private static bool HasPasteableClipboard()
        {
            try
            {
                var content = Clipboard.GetContent();
                return content != null
                    && (content.Contains(StandardDataFormats.Text)
                        || ClipboardImageDecoder.ContainsImage(content));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// WPF EnsureBlankContextMenu parity for the Phase A surface: Copy /
        /// Paste / Select-all / Delete — copy+delete only with a live
        /// selection, paste only when the clipboard carries text.
        /// </summary>
        private void ShowBlankContextMenu()
        {
            bool hasSelection = _activeSelectionPage != null && _activeSelectionPage.HasSelection;
            bool canPaste = HasPasteableClipboard();

            var flyout = new MenuFlyout();

            if (hasSelection)
            {
                var copyItem = new MenuFlyoutItem
                {
                    Text = LocalizationService.Get("Editor.Action.Copy"),
                };
                AutomationProperties.SetAutomationId(copyItem, "Editor.Action.Copy");
                copyItem.Click += (_, __) => CopySelection();
                flyout.Items.Add(copyItem);
            }

            if (canPaste)
            {
                var pasteItem = new MenuFlyoutItem
                {
                    Text = LocalizationService.Get("Editor.Action.Paste"),
                };
                AutomationProperties.SetAutomationId(pasteItem, "Editor.Action.Paste");
                pasteItem.Click += (_, __) => PasteClipboardImageOrSelection();
                flyout.Items.Add(pasteItem);
            }

            var selectAll = new MenuFlyoutItem
            {
                Text = LocalizationService.Get("Editor.Action.SelectAll"),
            };
            AutomationProperties.SetAutomationId(selectAll, "Editor.Action.SelectAll");
            selectAll.Click += (_, __) =>
            {
                ActivateTool(ToolType.Select);
                var page = _pageControls.Count == 0 ? null : _pageControls[GetCurrentPageIndex()];
                if (page != null)
                {
                    page.SelectAllAnnotations();
                    _activeSelectionPage = page;
                }
            };
            flyout.Items.Add(selectAll);

            // WPF EnsureBlankContextMenu parity: Refresh sits between
            // SelectAll and Delete and is always present.
            var refreshItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Get("Editor.Action.RefreshPage"),
            };
            AutomationProperties.SetAutomationId(refreshItem, "Editor.Action.RefreshPage");
            refreshItem.Click += async (_, __) =>
            {
                try
                {
                    await RefreshCurrentDocumentPreservingEditsAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RefreshPage] Failed: {ex.Message}");
                }
            };
            flyout.Items.Add(refreshItem);

            if (hasSelection)
            {
                var deleteItem = new MenuFlyoutItem
                {
                    Text = LocalizationService.Get("Editor.Action.Delete"),
                };
                AutomationProperties.SetAutomationId(deleteItem, "Editor.Action.Delete");
                deleteItem.Click += (_, __) => DeleteSelection();
                flyout.Items.Add(deleteItem);
            }

            _transientFlyout = flyout;
            flyout.ShowAt(PdfScrollViewer);
        }

        /// <summary>
        /// WPF RefreshCurrentDocumentPreservingEditsAsync parity for the
        /// blank-context "Refresh page" item: flush the live edits through
        /// the shared save pipeline, then reload under a fresh-session lease
        /// so every pipeline (pages, thumbnails, outline, search) re-reads
        /// persisted state. Deviation: a failed save aborts the reload —
        /// WPF reloaded unconditionally, which would discard the in-memory
        /// edits this command exists to preserve.
        /// </summary>
        private async Task RefreshCurrentDocumentPreservingEditsAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
                return;

            var operationLease = CaptureDocumentOperationLease(_pdfService);
            try
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                if (!await AutoSaveAsync(operationLease) ||
                    !ValidateDocumentOperationLease(operationLease))
                    return;

                // ReloadDocumentForOperationAsync retires the incoming lease
                // inside its own finally — the extra Dispose below is a
                // documented no-op (lease Dispose is idempotent).
                var refreshedLease = await ReloadDocumentForOperationAsync(
                    _currentPdfPath, operationLease);
                refreshedLease?.Dispose();
            }
            finally
            {
                operationLease.Dispose();
            }
        }

        /// <summary>
        /// Selection drawing-style undo: capture the before/after colour+size
        /// of every selected stroke, apply the new values in place and push
        /// one batch action — WPF ApplySelectedDrawingStyle →
        /// StrokeStyleChangedAction parity.
        /// </summary>
        private void ApplySelectedDrawingStyle(Windows.UI.Color? color, double? width)
        {
            var page = _activeSelectionPage;
            var strokes = page?.SelectedStrokes?.Distinct().ToList();
            if (page == null || strokes == null || strokes.Count == 0)
                return;

            var before = strokes.ToDictionary(
                stroke => stroke,
                stroke => (stroke.R, stroke.G, stroke.B, stroke.A, stroke.Size));
            bool changed = false;
            foreach (var stroke in strokes)
            {
                if (color.HasValue
                    && (stroke.R != color.Value.R || stroke.G != color.Value.G
                        || stroke.B != color.Value.B || stroke.A != color.Value.A))
                {
                    stroke.R = color.Value.R;
                    stroke.G = color.Value.G;
                    stroke.B = color.Value.B;
                    stroke.A = color.Value.A;
                    changed = true;
                }
                if (width.HasValue && Math.Abs(stroke.Size - width.Value) > 0.001)
                {
                    stroke.Size = width.Value;
                    changed = true;
                }
            }

            if (!changed)
                return;

            var after = strokes.ToDictionary(
                stroke => stroke,
                stroke => (stroke.R, stroke.G, stroke.B, stroke.A, stroke.Size));
            page.Ink.Store.NotifyGeometryChanged(strokes);
            PushUndoAction(new InkStrokesStyleChangedAction(page.Ink.Store, before, after));
        }

        // ==================================================================
        // Task 8 Phase A — text annotation boxes (WPF CreateTextBox et al.)
        // ==================================================================

        /// <summary>
        /// Press on the TextOverlay background while the Text tool is armed —
        /// a click outside any container deselects the current box, an empty
        /// spot creates a new one (WPF PageControl_TextOverlayPointerPressed).
        /// </summary>
        private void PageControl_TextOverlayPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_currentTool != ToolType.Text || sender is not PdfPageControl page)
                return;

            var point = e.GetCurrentPoint(page.TextOverlay).Position;
            if (_selectedTextBox != null)
            {
                DeselectTextBox();
                e.Handled = true;
                return;
            }

            CreateTextBox(page, point, alignToPointer: true);
            e.Handled = true;
        }

        /// <summary>
        /// Creates a text-annotation container on the page's TextOverlay:
        /// transparent Grid + decorative chrome Border + wrapped TextBox +
        /// eight <see cref="TextResizeHandleElement"/> squares (WPF
        /// CreateTextBox parity, pointer events in place of Mouse/Stylus).
        /// <paramref name="select"/> is false for sidecar loads and pastes —
        /// those paths must not push a TextBoxAddedAction or mark dirty.
        /// </summary>
        private Grid CreateTextBox(
            PdfPageControl page,
            Point position,
            Windows.UI.Color? color = null,
            double? fontSize = null,
            string text = null,
            bool select = true,
            bool alignToPointer = false,
            bool? bold = null,
            bool? italic = null,
            string fontFamily = null,
            TextAlignment? alignment = null,
            double? width = null,
            double? height = null,
            double rotationDegrees = 0)
        {
            var textPadding = new Thickness(10, 8, 10, 8);
            bool useDefaultSize = text == null
                && (!width.HasValue || width.Value <= 0)
                && (!height.HasValue || height.Value <= 0);
            double? initialWidth = width.HasValue && width.Value > 0
                ? width.Value
                : useDefaultSize ? TextAnnotationGeometry.DefaultWidth : null;
            double? initialHeight = height.HasValue && height.Value > 0
                ? height.Value
                : useDefaultSize ? TextAnnotationGeometry.DefaultHeight : null;

            var container = new CursorGrid
            {
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Tag = "text-annotation",
            };
            container.SetCursor(InputSystemCursorShape.SizeAll);
            bool autoWidth = !width.HasValue || width.Value <= 0;
            bool autoHeight = !height.HasValue || height.Value <= 0;
            if (useDefaultSize)
            {
                // Newly created boxes use the product default rectangle; the
                // persist-as-auto sentinel is reserved for loaded annotations
                // that explicitly carried zero dimensions (WPF parity).
                autoWidth = false;
                autoHeight = false;
            }
            page.SetTextAutoSize(container, autoWidth, autoHeight);
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

            // Visual chrome border spanning both columns (hit-test
            // transparent; shown only while the box is selected).
            var chrome = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(select ? 1.5 : 0),
                BorderBrush = select
                    ? ResolveThemeBrush("ThemeFocusBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB))
                    : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                IsHitTestVisible = false,
                Tag = "chrome",
            };
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
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                FontSize = fontSize ?? _currentFontSize,
                Foreground = new SolidColorBrush(color ?? _textColor),
                FontWeight = (bold ?? _textBold)
                    ? Microsoft.UI.Text.FontWeights.Bold
                    : Microsoft.UI.Text.FontWeights.Normal,
                FontStyle = (italic ?? _textItalic)
                    ? Windows.UI.Text.FontStyle.Italic
                    : Windows.UI.Text.FontStyle.Normal,
                FontFamily = new FontFamily(
                    string.IsNullOrWhiteSpace(fontFamily) ? _textFontFamily : fontFamily),
                TextAlignment = alignment ?? _textAlignment,
                IsReadOnly = !select,
                Padding = textPadding,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            // Auto-width tracking follows the hosting page. The handler is
            // owned by the container's Loaded/Unloaded lifecycle so EVERY
            // removal path (delete, undo/redo, cross-page transfer,
            // clipboard, page teardown) unsubscribes — and reparenting
            // re-binds to the new page instead of leaking the old one.
            PdfPageControl sizeTrackingPage = null;
            SizeChangedEventHandler sizeTrackingHandler = null;
            sizeTrackingHandler = (s, e) =>
            {
                if (textBox.Parent is Grid containerGrid && double.IsNaN(containerGrid.Width))
                {
                    var hostPage = GetPageByTextContainer(containerGrid) ?? sizeTrackingPage;
                    if (hostPage == null)
                        return;
                    double newAvailableWidth = hostPage.ActualWidth - Canvas.GetLeft(containerGrid);
                    textBox.MaxWidth = Math.Max(
                        100, newAvailableWidth - textPadding.Left - textPadding.Right - 40);
                }
            };
            void SubscribeSizeTracking(PdfPageControl hostPage)
            {
                if (hostPage != null && !ReferenceEquals(sizeTrackingPage, hostPage))
                {
                    if (sizeTrackingPage != null)
                        sizeTrackingPage.SizeChanged -= sizeTrackingHandler;
                    hostPage.SizeChanged += sizeTrackingHandler;
                    sizeTrackingPage = hostPage;
                }
            }
            void UnsubscribeSizeTracking()
            {
                if (sizeTrackingPage != null)
                {
                    sizeTrackingPage.SizeChanged -= sizeTrackingHandler;
                    sizeTrackingPage = null;
                }
            }
            container.Loaded += (s, e) => SubscribeSizeTracking(GetPageByTextContainer(container));
            container.Unloaded += (s, e) => UnsubscribeSizeTracking();
            SubscribeSizeTracking(page);

            Grid.SetColumn(textBox, 0);

            container.Children.Add(chrome);
            container.Children.Add(textBox);

            var resizeHandleDefinitions = new[]
            {
                (TextResizeHandle.TopLeft, HorizontalAlignment.Left, VerticalAlignment.Top, InputSystemCursorShape.SizeNorthwestSoutheast),
                (TextResizeHandle.Top, HorizontalAlignment.Center, VerticalAlignment.Top, InputSystemCursorShape.SizeNorthSouth),
                (TextResizeHandle.TopRight, HorizontalAlignment.Right, VerticalAlignment.Top, InputSystemCursorShape.SizeNortheastSouthwest),
                (TextResizeHandle.Left, HorizontalAlignment.Left, VerticalAlignment.Center, InputSystemCursorShape.SizeWestEast),
                (TextResizeHandle.Right, HorizontalAlignment.Right, VerticalAlignment.Center, InputSystemCursorShape.SizeWestEast),
                (TextResizeHandle.BottomLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNortheastSouthwest),
                (TextResizeHandle.Bottom, HorizontalAlignment.Center, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNorthSouth),
                (TextResizeHandle.BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNorthwestSoutheast),
            };

            foreach (var definition in resizeHandleDefinitions)
            {
                var resizeHandle = new TextResizeHandleElement
                {
                    Width = 10,
                    Height = 10,
                    Margin = new Thickness(-5),
                    HorizontalAlignment = definition.Item2,
                    VerticalAlignment = definition.Item3,
                    Visibility = select ? Visibility.Visible : Visibility.Collapsed,
                    Tag = definition.Item1,
                    IsTabStop = true,
                    // Panels only hit-test through a non-null Background —
                    // without this the handle is pointer-dead and presses
                    // fall through to the container drag/deselect paths.
                    Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                };
                resizeHandle.SetCursor(definition.Item4);
                // Visual square: accent fill + focus ring inside a 10 DIP
                // transparent host (the 5 DIP negative margin keeps the WPF
                // grab zone centered on the container edge).
                resizeHandle.Children.Add(new Border
                {
                    Background = ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)),
                    BorderBrush = ResolveThemeBrush("ThemeFocusBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    IsHitTestVisible = false,
                });
                string resizeLabel = LocalizationService.Get("Editor.ResizeTextBox");
                ToolTipService.SetToolTip(resizeHandle, resizeLabel);
                AutomationProperties.SetAutomationId(
                    resizeHandle,
                    TextAnnotationGeometry.GetResizeHandleAutomationId(definition.Item1));
                AutomationProperties.SetName(resizeHandle, resizeLabel);
                AutomationProperties.SetHelpText(resizeHandle, resizeLabel);
                Canvas.SetZIndex(resizeHandle, 20);
                resizeHandle.PointerPressed += TextResizeHandle_PointerPressed;
                resizeHandle.PointerMoved += TextResizeHandle_PointerMoved;
                resizeHandle.PointerReleased += TextResizeHandle_PointerReleased;
                resizeHandle.PointerCanceled += TextResizeHandle_PointerCanceled;
                resizeHandle.PointerCaptureLost += TextResizeHandle_PointerCaptureLost;
                resizeHandle.KeyDown += TextResizeHandle_KeyDown;
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
            PdfPageControl.ApplyAnnotationRotation(container, rotationDegrees);
            Canvas.SetZIndex(container, 1000);

            // Border-band drag: handledEventsToo mirrors WPF's Preview*
            // tunneling — the TextBox child marks its presses handled, yet
            // the 8 DIP move-border band inside its padding must still arm
            // the container drag.
            container.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler(TextContainerBorder_PointerPressed),
                handledEventsToo: true);
            container.AddHandler(
                UIElement.PointerMovedEvent,
                new PointerEventHandler(TextContainerBorder_PointerMoved),
                handledEventsToo: true);
            container.AddHandler(
                UIElement.PointerReleasedEvent,
                new PointerEventHandler(TextContainerBorder_PointerReleased),
                handledEventsToo: true);
            container.PointerCanceled += TextContainerBorder_PointerCanceled;
            container.PointerCaptureLost += TextContainerBorder_PointerCaptureLost;

            textBox.TextChanged += (s, e) => MarkDirty();
            textBox.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler((s, e) =>
                {
                    // WPF PreviewStylusDown parity: pen presses on a text box
                    // belong to the ink surface — drop focus to the scroller
                    // and swallow the event instead of placing the caret.
                    if (e.Pointer.PointerDeviceType == PointerDeviceType.Pen)
                    {
                        PdfScrollViewer.Focus(FocusState.Programmatic);
                        e.Handled = true;
                        return;
                    }
                    // Let the native TextBox click logic place the caret; only
                    // the selection/read-only state switches first (WPF
                    // PreviewMouseLeftButtonDown parity).
                    SelectTextBox((TextBox)s, focusTextBox: false);
                }),
                handledEventsToo: true);
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
                textBox.Focus(FocusState.Programmatic);
                // Only user-created boxes push an undo action here; loads and
                // pastes take their own batch path (WPF parity).
                PushUndoAction(new TextBoxAddedAction(page, container));
                MarkDirty();
            }

            return container;
        }

        private static TextBoxBounds GetTextContainerBounds(Grid container)
        {
            if (container == null)
            {
                return new TextBoxBounds(
                    0, 0,
                    TextAnnotationGeometry.DefaultWidth,
                    TextAnnotationGeometry.DefaultHeight);
            }

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

        private static double GetPersistedTextWidth(PdfPageControl page, Grid container)
            => page.IsTextAnnotationAutoWidth(container) ? 0 : GetTextContainerBounds(container).Width;

        private static double GetPersistedTextHeight(PdfPageControl page, Grid container)
            => page.IsTextAnnotationAutoHeight(container) ? 0 : GetTextContainerBounds(container).Height;

        /// <summary>
        /// Applies normalized bounds + auto-size flags to a text container —
        /// the interactive counterpart of the page's quiet bounds setter
        /// (WPF ApplyTextContainerBounds).
        /// </summary>
        private void ApplyTextContainerBounds(
            Grid container,
            TextBoxBounds bounds,
            bool? autoWidth = null,
            bool? autoHeight = null)
        {
            if (container == null)
                return;

            var page = GetPageByTextContainer(container);
            var normalized = TextAnnotationGeometry.Normalize(bounds);
            bool aw = autoWidth ?? page?.IsTextAnnotationAutoWidth(container) == true;
            bool ah = autoHeight ?? page?.IsTextAnnotationAutoHeight(container) == true;
            page?.SetTextAutoSize(container, aw, ah);

            Canvas.SetLeft(container, normalized.X);
            Canvas.SetTop(container, normalized.Y);
            container.Width = aw ? double.NaN : normalized.Width;
            container.Height = ah ? double.NaN : normalized.Height;

            if (container.Children.OfType<TextBox>().FirstOrDefault() is TextBox textBox)
            {
                // The first grid column is star-sized, so the editor fills
                // the resized rectangle while retaining padding + wrapping.
                textBox.MaxWidth = double.PositiveInfinity;
                textBox.Width = double.NaN;
                textBox.Height = double.NaN;
            }

            // InvalidateMeasure only — the resize handles live INSIDE the
            // container and track its size on the next layout pass, so a
            // synchronous layout pass per pointer packet would only burn
            // frames (the quiet setter relies on the same pass).
            container.InvalidateMeasure();
        }

        // ── Eight-handle resize ────────────────────────────────────────────

        private void TextResizeHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_currentTool != ToolType.Text
                || sender is not TextResizeHandleElement handle
                || handle.Tag is not TextResizeHandle resizeHandle
                || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var container = handle.Parent as Grid;
            var page = GetPageByTextContainer(container);
            if (container == null || page == null)
                return;

            BeginTextResize(handle, container, page, resizeHandle, e.GetCurrentPoint(page).Position);
            handle.CapturePointer(e.Pointer);
            _textResizePointerId = e.Pointer.PointerId;
            e.Handled = true;
        }

        private void TextResizeHandle_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);
            bool alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
                .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);
            bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);
            if (_currentTool != ToolType.Text
                || sender is not TextResizeHandleElement handle
                || handle.Tag is not TextResizeHandle resizeHandle
                || !TryGetTextBoxNudge(e.Key, out double nudgeX, out double nudgeY)
                || ctrl || alt)
            {
                return;
            }

            var container = handle.Parent as Grid;
            var page = GetPageByTextContainer(container);
            if (container == null || page == null)
                return;

            double step = shift ? 10 : 1;
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

            bool beforeAutoWidth = page.IsTextAnnotationAutoWidth(container);
            bool beforeAutoHeight = page.IsTextAnnotationAutoHeight(container);
            ApplyTextContainerBounds(container, after, autoWidth: false, autoHeight: false);
            PushUndoAction(new TextBoxResizedAction(
                page,
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

        private void BeginTextResize(
            TextResizeHandleElement handle,
            Grid container,
            PdfPageControl page,
            TextResizeHandle resizeHandle,
            Point startPoint)
        {
            if (container.Children.OfType<TextBox>().FirstOrDefault() is TextBox textBox)
                SelectTextBox(textBox, focusTextBox: false);

            handle.Focus(FocusState.Programmatic);

            _resizingTextContainer = container;
            _resizingTextPage = page;
            _textResizeHandle = resizeHandle;
            // The handle element OWNS the pointer capture — cancel paths
            // must release it, not the container (P2s.1).
            _resizingTextHandleElement = handle;
            _textResizeStartPoint = startPoint;
            _textResizeStartBounds = GetTextContainerBounds(container);
            _textResizeStartAutoWidth = page.IsTextAnnotationAutoWidth(container);
            _textResizeStartAutoHeight = page.IsTextAnnotationAutoHeight(container);
        }

        private void TextResizeHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_resizingTextContainer == null || _resizingTextPage == null)
                return;
            if (_textResizePointerId != null && e.Pointer.PointerId != _textResizePointerId.Value)
                return;

            UpdateTextResize(e.GetCurrentPoint(_resizingTextPage).Position);
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

        private void TextResizeHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_resizingTextContainer == null)
                return;
            if (_textResizePointerId != null && e.Pointer.PointerId != _textResizePointerId.Value)
                return;

            _suppressTextCaptureCancellation = true;
            try
            {
                if (sender is UIElement handle)
                    handle.ReleasePointerCaptures();
                CompleteTextResize();
            }
            finally
            {
                _suppressTextCaptureCancellation = false;
            }
            e.Handled = true;
        }

        private void TextResizeHandle_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_resizingTextContainer != null
                && (_textResizePointerId == null
                    || e.Pointer.PointerId == _textResizePointerId.Value))
            {
                CancelTextResize(restoreBounds: true);
            }
        }

        private void TextResizeHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!_suppressTextCaptureCancellation
                && (_textResizePointerId == null
                    || e.Pointer.PointerId == _textResizePointerId.Value))
            {
                CancelTextResize(restoreBounds: true);
            }
        }

        private void CompleteTextResize()
        {
            var resizedContainer = _resizingTextContainer;
            if (resizedContainer == null || _resizingTextPage == null)
                return;

            var page = _resizingTextPage;
            var before = _textResizeStartBounds;
            var after = GetTextContainerBounds(resizedContainer);
            bool afterAutoWidth = page.IsTextAnnotationAutoWidth(resizedContainer);
            bool afterAutoHeight = page.IsTextAnnotationAutoHeight(resizedContainer);
            _resizingTextContainer = null;
            _resizingTextPage = null;
            _textResizeHandle = default;

            _resizingTextHandleElement = null;
            _textResizePointerId = null;

            bool geometryChanged = Math.Abs(before.X - after.X) > 0.5
                || Math.Abs(before.Y - after.Y) > 0.5
                || Math.Abs(before.Width - after.Width) > 0.5
                || Math.Abs(before.Height - after.Height) > 0.5;
            bool layoutModeChanged = _textResizeStartAutoWidth != afterAutoWidth
                || _textResizeStartAutoHeight != afterAutoHeight;

            if (!geometryChanged && layoutModeChanged)
            {
                // A click or sub-pixel jitter must not silently convert an
                // automatic-size annotation into a fixed box (WPF parity).
                ApplyTextContainerBounds(
                    resizedContainer,
                    before,
                    _textResizeStartAutoWidth,
                    _textResizeStartAutoHeight);
            }
            else if (geometryChanged || layoutModeChanged)
            {
                PushUndoAction(new TextBoxResizedAction(
                    page,
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
                {
                    ApplyTextContainerBounds(
                        resizingContainer,
                        _textResizeStartBounds,
                        _textResizeStartAutoWidth,
                        _textResizeStartAutoHeight);
                }
                // The capture lives on the handle element, not the
                // container — releasing the container left the dead
                // gesture holding the pointer.
                _resizingTextHandleElement?.ReleasePointerCaptures();
            }
            finally
            {
                _suppressTextCaptureCancellation = false;
                _resizingTextContainer = null;
                _resizingTextPage = null;
                _textResizeHandle = default;
                _resizingTextHandleElement = null;
                _textResizePointerId = null;
                _textResizeStartBounds = default;
            }
        }

        // ── Border-band drag ───────────────────────────────────────────────

        private static bool IsTextContainerBorderGesture(
            Grid container, Point point, object originalSource)
        {
            return FindAncestor<TextResizeHandleElement>(originalSource as DependencyObject) == null
                && TextAnnotationGeometry.IsMoveBorderHit(
                    point.X,
                    point.Y,
                    container.ActualWidth,
                    container.ActualHeight);
        }

        private void TextContainerBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_currentTool != ToolType.Text
                || sender is not Grid container
                || container.Parent is not Canvas canvas
                || !e.GetCurrentPoint(container).Properties.IsLeftButtonPressed
                || !IsTextContainerBorderGesture(
                    container, e.GetCurrentPoint(container).Position, e.OriginalSource))
            {
                return;
            }

            BeginTextBoxDrag(container, e.GetCurrentPoint(canvas).Position);
            _dragPointerId = e.Pointer.PointerId;
            e.Handled = true;
        }

        private void TextContainerBorder_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_dragPointerId != null && e.Pointer.PointerId != _dragPointerId.Value)
                return;
            if (_draggedContainer?.Parent is Canvas canvas)
            {
                UpdateTextBoxDrag(
                    e.GetCurrentPoint(canvas).Position,
                    () => _draggedContainer.CapturePointer(e.Pointer));
            }
            e.Handled = _isDragging || _dragArmed;
        }

        private void TextContainerBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_dragPointerId != null && e.Pointer.PointerId != _dragPointerId.Value)
                return;
            var container = sender as Grid;
            _suppressTextCaptureCancellation = true;
            bool wasDragging;
            try
            {
                container?.ReleasePointerCaptures();
                wasDragging = CompleteTextBoxDrag();
            }
            finally
            {
                _suppressTextCaptureCancellation = false;
            }
            e.Handled = wasDragging;
        }

        private void TextContainerBorder_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (!_suppressTextCaptureCancellation
                && (_dragPointerId == null
                    || e.Pointer.PointerId == _dragPointerId.Value))
            {
                CancelTextBoxDrag(restoreBounds: true);
            }
        }

        private void TextContainerBorder_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!_suppressTextCaptureCancellation
                && (_dragPointerId == null
                    || e.Pointer.PointerId == _dragPointerId.Value))
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
                // No clamping while dragging — the container follows the
                // pointer beyond the source page so a cross-page drop can be
                // detected on release (WPF Task 9 parity).
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
                        ? FindPageAtContainerPoint(sourcePage, new PointD(
                            endX + _draggedContainer.ActualWidth / 2,
                            endY + _draggedContainer.ActualHeight / 2))
                        : null;

                    if (sourcePage != null && targetPage != null && targetPage != sourcePage)
                    {
                        // Cross-page drop — the selection cross-page
                        // mechanism with a single text container and no
                        // strokes (WPF parity).
                        Point targetOriginInSource = targetPage.TransformToVisual(sourcePage)
                            .TransformPoint(new Point(0, 0));
                        double adjustX = -targetOriginInSource.X;
                        double adjustY = -targetOriginInSource.Y;

                        // Clamp the landing point into the TARGET page — a
                        // drop near the edge must not strand the box
                        // off-canvas. The clamp is folded into the action's
                        // delta so initial transfer AND redo land the same.
                        var landing = new TextBoxBounds(
                            endX + adjustX, endY + adjustY,
                            _draggedContainer.ActualWidth,
                            _draggedContainer.ActualHeight);
                        double targetW = targetPage.TextOverlay.ActualWidth > 0
                            ? targetPage.TextOverlay.ActualWidth : targetPage.ActualWidth;
                        double targetH = targetPage.TextOverlay.ActualHeight > 0
                            ? targetPage.TextOverlay.ActualHeight : targetPage.ActualHeight;
                        var clamped = TextAnnotationGeometry.ClampToPage(
                            landing, targetW, targetH);
                        double effDx = clamped.X - _dragStartX - adjustX;
                        double effDy = clamped.Y - _dragStartY - adjustY;

                        var moveAction = new AnnotationSelectionCrossPageMoveAction(
                            sourcePage.Ink.Store,
                            targetPage.Ink.Store,
                            sourcePage,
                            targetPage,
                            Array.Empty<InkStrokeData>(),
                            new List<Grid> { _draggedContainer },
                            effDx,
                            effDy,
                            adjustX,
                            adjustY,
                            new List<InkStrokePlacement>());

                        if (sourcePage.HasSelection
                            && sourcePage.SelectedTextContainers.Contains(_draggedContainer))
                        {
                            sourcePage.ClearSelection();
                        }

                        bool transferred;
                        try
                        {
                            transferred = moveAction.ExecuteInitialTransfer();
                        }
                        catch
                        {
                            transferred = false;
                        }
                        if (transferred)
                            PushUndoAction(moveAction);
                    }
                    else
                    {
                        // Same page or a miss into a gap: clamp back into the
                        // source page bounds and record a same-page move.
                        if (sourcePage != null && _draggedContainer.Parent is Canvas canvas)
                        {
                            endX = Math.Max(0, Math.Min(
                                endX, Math.Max(0, canvas.ActualWidth - _draggedContainer.ActualWidth)));
                            endY = Math.Max(0, Math.Min(
                                endY, Math.Max(0, canvas.ActualHeight - _draggedContainer.ActualHeight)));
                            Canvas.SetLeft(_draggedContainer, endX);
                            Canvas.SetTop(_draggedContainer, endY);
                        }
                        if (sourcePage != null)
                        {
                            PushUndoAction(new TextBoxMovedAction(
                                sourcePage,
                                _draggedContainer,
                                new PointD(_dragStartX, _dragStartY),
                                new PointD(endX, endY)));
                        }
                    }
                    MarkDirty();
                }
                if (wasDragging)
                {
                    var tb = _draggedContainer.Children.OfType<TextBox>().FirstOrDefault();
                    if (tb != null)
                    {
                        PositionInlineTextBoxToolbar(_draggedContainer);
                        tb.Focus(FocusState.Programmatic);
                    }
                }
            }
            _draggedContainer = null;
            _draggedContainerPage = null;
            _dragStartX = 0;
            _dragStartY = 0;
            _dragPointerId = null;
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
                container?.ReleasePointerCaptures();
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
                _dragPointerId = null;
            }
        }

        /// <summary>
        /// Press on the page background (outside the text overlay) — the
        /// Text tool deselects; the StickyNote tool places a marker and opens
        /// its editor (WPF PageControl_BackgroundPointerPressed parity).
        /// </summary>
        private void PageControl_BackgroundPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_selectedTextBox != null)
                DeselectTextBox();

            // WPF tracks the click so Ctrl+V anchors the paste offset at the
            // clicked point rather than a fixed (+20,+20) nudge.
            if (sender is PdfPageControl clickedPage)
            {
                _lastClickedPage = clickedPage;
                _lastClickedPoint = e.GetCurrentPoint(clickedPage).Position;
            }

            if (_currentTool == ToolType.StickyNote && sender is PdfPageControl page)
            {
                var pos = e.GetCurrentPoint(page).Position;
                var note = new StickyNoteAnnotation
                {
                    X = pos.X,
                    Y = pos.Y,
                    Text = string.Empty,
                };
                var container = page.AddStickyNote(note);
                if (container != null)
                {
                    PushUndoAction(new StickyNoteAddedAction(page, container));
                    MarkDirty();
                    OpenStickyNoteEditor(page, container, note);
                }
                e.Handled = true;
            }
        }

        // ── Text-box selection / focus-session undo ───────────────────────

        private void SelectTextBox(TextBox textBox, bool focusTextBox = true)
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

            if (focusTextBox && textBox.FocusState == FocusState.Unfocused)
                textBox.Focus(FocusState.Programmatic);
        }

        private void DeselectTextBox()
        {
            if (_selectedTextBox == null)
                return;
            ApplyTextBoxChrome(_selectedTextBox, isSelected: false);
            _selectedTextBox.IsReadOnly = true;
            _selectedTextBox = null;
            RemoveInlineTextBoxToolbar();
        }

        /// <summary>
        /// Toggles the selected chrome + resize handles around a text box
        /// (WPF ApplyTextBoxChrome).
        /// </summary>
        private void ApplyTextBoxChrome(TextBox textBox, bool isSelected)
        {
            textBox.BorderThickness = new Thickness(0);
            textBox.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

            if (textBox.Parent is not Grid container)
                return;

            foreach (var child in container.Children)
            {
                if (child is Border chrome && !chrome.IsHitTestVisible
                    && chrome.Tag is string tag && tag == "chrome")
                {
                    chrome.BorderThickness = new Thickness(isSelected ? 1.5 : 0);
                    chrome.BorderBrush = isSelected
                        ? ResolveThemeBrush("ThemeFocusBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB))
                        : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                    chrome.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                }
                else if (child is TextResizeHandleElement handle && handle.Tag is TextResizeHandle)
                {
                    handle.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void DeleteSelectedTextBox()
        {
            if (_selectedTextBox == null)
                return;
            var tb = _selectedTextBox;
            DeselectTextBox();

            if (tb.Parent is Grid container && container.Parent is Panel panel)
            {
                // Resolve the page BEFORE detaching — Parent is null after
                // the remove (WPF looked it up afterwards and silently
                // skipped the undo push).
                var page = GetPageByTextContainer(container);
                panel.Children.Remove(container);
                if (page != null)
                    PushUndoAction(new TextBoxDeletedAction(page, container));
                MarkDirty();
            }
            else if (tb.Parent is Panel orphanPanel)
            {
                orphanPanel.Children.Remove(tb);
                MarkDirty();
            }
        }

        /// <summary>
        /// The page whose TextOverlay hosts the container (WPF
        /// GetPageByTextContainer) — null while the container is detached.
        /// </summary>
        private PdfPageControl GetPageByTextContainer(Grid container)
        {
            if (container?.Parent is Canvas canvas)
                return _pageControls.FirstOrDefault(p => ReferenceEquals(p.TextOverlay, canvas));
            return null;
        }

        /// <summary>
        /// True when focus sits inside interactive editor chrome — the
        /// inline text toolbar or any control that owns a keyboard contract
        /// (buttons, combo boxes, sliders, list/menu items, other TextBoxes,
        /// the sticky marker button). The selected annotation TextBox is
        /// the only TextBox allowed to fall through so its caret keys stay
        /// native while the unfocused nudge/Delete branches still apply.
        /// Plain panels, canvases and the page controls themselves are NOT
        /// chrome — selection shortcuts keep working while they hold focus.
        /// </summary>
        private bool IsInteractiveEditorChrome(DependencyObject focused)
        {
            for (var current = focused; current != null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, _inlineTextBoxToolbar))
                    return true;
                switch (current)
                {
                    case ButtonBase:
                    case ComboBox:
                    case SelectorItem:
                    case Slider:
                    case MenuFlyoutItem:
                        return true;
                    case TextBox textBox when !ReferenceEquals(textBox, _selectedTextBox):
                        // Search/zoom/page-jump/sticky-editor boxes own
                        // their keys; the annotation box is carved out so
                        // unfocused Delete/Back + Alt-nudge still work.
                        return true;
                }
            }
            return false;
        }

        private static bool TryGetTextBoxNudge(VirtualKey key, out double deltaX, out double deltaY)
        {
            deltaX = 0;
            deltaY = 0;
            switch (key)
            {
                case VirtualKey.Left:
                    deltaX = -1;
                    return true;
                case VirtualKey.Right:
                    deltaX = 1;
                    return true;
                case VirtualKey.Up:
                    deltaY = -1;
                    return true;
                case VirtualKey.Down:
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
                autoWidth: page.IsTextAnnotationAutoWidth(container),
                autoHeight: page.IsTextAnnotationAutoHeight(container));
            PushUndoAction(new TextBoxMovedAction(
                page,
                container,
                new PointD(before.X, before.Y),
                new PointD(after.X, after.Y)));
            MarkDirty();
            PositionInlineTextBoxToolbar(container);
        }

        private void BeginTextEditSession(TextBox textBox)
        {
            _textEditSessionTextBox = textBox;
            _textEditSessionOriginalText = textBox.Text;
            // Liveness anchors (WPF sticky-session parity): the hosting
            // page + load generation are captured so a stale LostFocus
            // can't mutate a dead or reparented container.
            _textEditSessionPage = GetPageByTextContainer(textBox?.Parent as Grid);
            _textEditSessionId = _loadSessionId;
        }

        /// <summary>
        /// Commits the open focus session — one TextEditSessionAction per
        /// net change, never for no-op sessions (WPF CommitTextEditSession).
        /// </summary>
        private void CommitTextEditSession()
        {
            var textBox = _textEditSessionTextBox;
            if (textBox == null)
                return;
            var sessionPage = _textEditSessionPage;
            int sessionId = _textEditSessionId;
            _textEditSessionTextBox = null;
            _textEditSessionPage = null;
            _textEditSessionId = 0;

            string beforeText = _textEditSessionOriginalText;
            string afterText = textBox.Text;
            if (string.Equals(beforeText, afterText))
                return;

            // Stale-session guard: the container was removed, reparented
            // across pages (GetPageByTextContainer then reports the NEW
            // page), or the document reloaded mid-session — skip the action
            // instead of recording an undo for a dead/gone box.
            var container = textBox.Parent as Grid;
            var page = GetPageByTextContainer(container);
            if (page == null || container == null
                || sessionId != _loadSessionId
                || !ReferenceEquals(page, sessionPage))
            {
                return;
            }

            PushUndoAction(new TextEditSessionAction(page, container, beforeText, afterText));
            MarkDirty();
        }

        // ── Inline text toolbar ────────────────────────────────────────────

        /// <summary>
        /// Builds the floating text toolbar once (WPF InitializeTextBoxPopup):
        /// delete | font −/+ | colour palette | B I | family | alignment.
        /// Hosted on the page's TextOverlay canvas above the selected box.
        /// </summary>
        private void EnsureInlineTextBoxToolbar()
        {
            if (_inlineTextBoxToolbar != null)
                return;

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = panel,
                Background = ResolveThemeBrush("ThemeSurfaceBrush", Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                BorderBrush = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
                Visibility = Visibility.Collapsed,
            };

            var deleteButton = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0),
                Content = new Microsoft.UI.Xaml.Shapes.Path
                {
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Stroke = ResolveThemeBrush("ThemeMarginBrush", Color.FromArgb(0xFF, 0x6B, 0x72, 0x80)),
                    Data = LucideIcon.ParseIconGeometry(
                        "M5,5 L19,5 M8,5 L8,3 L16,3 L16,5 M7,7 L8,19 L16,19 L17,7"),
                },
            };
            string deleteLabel = LocalizationService.Get("Editor.DeleteTooltip");
            ToolTipService.SetToolTip(deleteButton, deleteLabel);
            AutomationProperties.SetAutomationId(deleteButton, "Editor.TextToolbar.Delete");
            AutomationProperties.SetName(deleteButton, deleteLabel);
            deleteButton.Click += (s, e) => DeleteSelectedTextBox();

            var decreaseFontButton = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Content = CreateTextSizeButtonContent(increase: false),
            };
            string smallerLabel = LocalizationService.Get("Editor.SmallerText");
            ToolTipService.SetToolTip(decreaseFontButton, smallerLabel);
            AutomationProperties.SetAutomationId(decreaseFontButton, "Editor.TextToolbar.FontSizeDown");
            AutomationProperties.SetName(decreaseFontButton, smallerLabel);
            decreaseFontButton.Click += (s, e) => AdjustSelectedTextBoxFontSize(increase: false);

            var increaseFontButton = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Content = CreateTextSizeButtonContent(increase: true),
            };
            string biggerLabel = LocalizationService.Get("Editor.BiggerText");
            ToolTipService.SetToolTip(increaseFontButton, biggerLabel);
            AutomationProperties.SetAutomationId(increaseFontButton, "Editor.TextToolbar.FontSizeUp");
            AutomationProperties.SetName(increaseFontButton, biggerLabel);
            increaseFontButton.Click += (s, e) => AdjustSelectedTextBoxFontSize(increase: true);

            var fontButtonGroup = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(2, 0, 2, 0),
                Background = ResolveThemeBrush("ThemeSurfaceAltBrush", Color.FromArgb(0xFF, 0xF2, 0xF5, 0xF7)),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        decreaseFontButton,
                        new Border
                        {
                            Width = 1,
                            Height = 16,
                            Margin = new Thickness(1, 0, 1, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Background = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
                        },
                        increaseFontButton,
                    },
                },
            };

            _colorIndicator = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(_textColor),
                BorderThickness = new Thickness(1),
                BorderBrush = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
            };
            var colorButton = new Button
            {
                Content = _colorIndicator,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0),
            };
            string colorLabel = LocalizationService.Get("Editor.TextColorTooltip");
            if (string.IsNullOrWhiteSpace(colorLabel))
                colorLabel = LocalizationService.Get("Editor.ColorTooltip");
            ToolTipService.SetToolTip(colorButton, colorLabel);
            AutomationProperties.SetAutomationId(colorButton, "Editor.TextToolbar.Color");
            AutomationProperties.SetName(colorButton, colorLabel);
            // G4: WPF's text-colour popup shows the "最近 Recent" swatch row
            // above the palette; repopulated on every open (WPF popup.Opened
            // parity) and persisted via AppSettings.RecentTextColors.
            var textRecentSection = BuildRecentColorsSection(out var textRecentRow);
            Action<Windows.UI.Color> markTextPalette = null;
            var textPalette = BuildColorPalette(
                _textColor, ApplyTextColor, textRecentRow, out markTextPalette);
            var textColorPanel = new StackPanel { Margin = new Thickness(4) };
            textColorPanel.Children.Add(textRecentSection);
            textColorPanel.Children.Add(textPalette);
            _textColorFlyout = new Flyout { Content = textColorPanel };
            _textColorFlyout.Opening += (_, __) =>
            {
                RefreshRecentColorsRow(
                    textRecentSection,
                    textRecentRow,
                    () => AppSettingsService.Load().RecentTextColors,
                    ApplyTextColor,
                    markTextPalette);
                markTextPalette?.Invoke(_textColor);
            };
            colorButton.Flyout = _textColorFlyout;

            _textBoldButton = new ToggleButton
            {
                Content = "B",
                Width = 32,
                Height = 32,
                MinWidth = 32,
                MinHeight = 32,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            };
            string boldLabel = LocalizationService.Get("Editor.BoldTooltip");
            ToolTipService.SetToolTip(_textBoldButton, boldLabel);
            AutomationProperties.SetAutomationId(_textBoldButton, "Editor.TextToolbar.Bold");
            AutomationProperties.SetName(_textBoldButton, boldLabel);
            _textItalicButton = new ToggleButton
            {
                Content = "I",
                Width = 32,
                Height = 32,
                MinWidth = 32,
                MinHeight = 32,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
            };
            string italicLabel = LocalizationService.Get("Editor.ItalicTooltip");
            ToolTipService.SetToolTip(_textItalicButton, italicLabel);
            AutomationProperties.SetAutomationId(_textItalicButton, "Editor.TextToolbar.Italic");
            AutomationProperties.SetName(_textItalicButton, italicLabel);
            _textBoldButton.Click += (_, __) => ApplySelectedTextFormat(tb =>
                tb.FontWeight = _textBoldButton.IsChecked == true
                    ? Microsoft.UI.Text.FontWeights.Bold
                    : Microsoft.UI.Text.FontWeights.Normal);
            _textItalicButton.Click += (_, __) => ApplySelectedTextFormat(tb =>
                tb.FontStyle = _textItalicButton.IsChecked == true
                    ? Windows.UI.Text.FontStyle.Italic
                    : Windows.UI.Text.FontStyle.Normal);

            _textFontFamilyCombo = new ComboBox
            {
                Width = 104,
                Height = 32,
                Margin = new Thickness(4, 0, 0, 0),
                ItemsSource = new[] { "Segoe UI", "Arial", "Times New Roman", "Consolas" },
            };
            string familyLabel = LocalizationService.Get("Editor.FontFamilyTooltip");
            ToolTipService.SetToolTip(_textFontFamilyCombo, familyLabel);
            AutomationProperties.SetAutomationId(_textFontFamilyCombo, "Editor.TextToolbar.FontFamily");
            AutomationProperties.SetName(_textFontFamilyCombo, familyLabel);
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
            };
            string alignmentLabel = LocalizationService.Get("Editor.AlignmentTooltip");
            ToolTipService.SetToolTip(_textAlignmentCombo, alignmentLabel);
            AutomationProperties.SetAutomationId(_textAlignmentCombo, "Editor.TextToolbar.Alignment");
            AutomationProperties.SetName(_textAlignmentCombo, alignmentLabel);
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

            panel.Children.Add(deleteButton);
            panel.Children.Add(new Border
            {
                Width = 1,
                Height = 18,
                Margin = new Thickness(6, 5, 6, 5),
                VerticalAlignment = VerticalAlignment.Center,
                Background = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
            });
            panel.Children.Add(fontButtonGroup);
            panel.Children.Add(new Border
            {
                Width = 1,
                Height = 18,
                Margin = new Thickness(6, 5, 6, 5),
                VerticalAlignment = VerticalAlignment.Center,
                Background = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
            });
            panel.Children.Add(colorButton);
            panel.Children.Add(new Border
            {
                Width = 1,
                Height = 18,
                Margin = new Thickness(6, 5, 6, 5),
                VerticalAlignment = VerticalAlignment.Center,
                Background = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
            });
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
                new TextAlignmentOption(TextAlignment.Right, LocalizationService.Get("Editor.AlignmentRight")),
            };
        }

        /// <summary>
        /// Re-applies localized tooltips/UIA names on the live inline text
        /// toolbar after a language change — matched by AutomationId so the
        /// local-only buttons (delete/font −/+/colour) refresh too.
        /// </summary>
        private void ApplyInlineTextBoxToolbarLocalization()
        {
            if (_inlineTextBoxToolbar == null)
                return;

            ApplyToolbarDescendantLabel("Editor.TextToolbar.Delete",
                LocalizationService.Get("Editor.DeleteTooltip"));
            ApplyToolbarDescendantLabel("Editor.TextToolbar.FontSizeDown",
                LocalizationService.Get("Editor.SmallerText"));
            ApplyToolbarDescendantLabel("Editor.TextToolbar.FontSizeUp",
                LocalizationService.Get("Editor.BiggerText"));
            string colorLabel = LocalizationService.Get("Editor.TextColorTooltip");
            if (string.IsNullOrWhiteSpace(colorLabel))
                colorLabel = LocalizationService.Get("Editor.ColorTooltip");
            ApplyToolbarDescendantLabel("Editor.TextToolbar.Color", colorLabel);
            ApplyToolbarDescendantLabel("Editor.TextToolbar.Bold",
                LocalizationService.Get("Editor.BoldTooltip"));
            ApplyToolbarDescendantLabel("Editor.TextToolbar.Italic",
                LocalizationService.Get("Editor.ItalicTooltip"));
            ApplyToolbarDescendantLabel("Editor.TextToolbar.FontFamily",
                LocalizationService.Get("Editor.FontFamilyTooltip"));
            ApplyToolbarDescendantLabel("Editor.TextToolbar.Alignment",
                LocalizationService.Get("Editor.AlignmentTooltip"));
        }

        private void ApplyToolbarDescendantLabel(string automationId, string label)
        {
            var element = FindDescendantByAutomationId(_inlineTextBoxToolbar, automationId);
            if (element == null)
                return;

            ToolTipService.SetToolTip(element, label);
            AutomationProperties.SetName(element, label);
            AutomationProperties.SetHelpText(element, label);
        }

        private static DependencyObject FindDescendantByAutomationId(DependencyObject root, string automationId)
        {
            if (root == null)
                return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (AutomationProperties.GetAutomationId(child) == automationId)
                    return child;
                var found = FindDescendantByAutomationId(child, automationId);
                if (found != null)
                    return found;
            }
            return null;
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

        /// <summary>
        /// Palette cell commit — foreground change on the selected box with
        /// a TextStyleChangedAction (WPF ApplyTextColor parity).
        /// </summary>
        private void ApplyTextColor(Windows.UI.Color picked)
        {
            if (_selectedTextBox != null)
            {
                var beforeBrush = _selectedTextBox.Foreground;
                var beforeFontSize = _selectedTextBox.FontSize;
                var container = _selectedTextBox.Parent as Grid;
                var page = GetPageByTextContainer(container);

                _selectedTextBox.Foreground = new SolidColorBrush(picked);
                _textColor = picked;
                if (_colorIndicator != null)
                    _colorIndicator.Background = new SolidColorBrush(picked);
                if (page != null && container != null)
                {
                    var before = (beforeBrush as SolidColorBrush)?.Color
                        ?? Windows.UI.Color.FromArgb(255, 0, 0, 0);
                    PushUndoAction(new TextStyleChangedAction(
                        page,
                        container,
                        new TextStyleSnapshot(beforeFontSize, before.R, before.G, before.B),
                        new TextStyleSnapshot(_selectedTextBox.FontSize, picked.R, picked.G, picked.B)));
                }
                MarkDirty();
                // Task 14/G4 parity: the applied colour joins the
                // text-palette recents (WPF records inside the
                // _selectedTextBox guard, same as here).
                SaveSetting(s => RecordRecentColor(s.RecentTextColors, picked));
            }
            _textColorFlyout?.Hide();
        }

        /// <summary>
        /// Bold/italic/family/alignment change on the selected box as one
        /// TextFormatChangedAction (WPF ApplySelectedTextFormat parity).
        /// </summary>
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

            bool changed = beforeWeight.Weight != textBox.FontWeight.Weight
                || beforeStyle != textBox.FontStyle
                || !string.Equals(beforeFamily?.Source, textBox.FontFamily?.Source, StringComparison.OrdinalIgnoreCase)
                || beforeAlignment != textBox.TextAlignment;
            if (!changed)
                return;

            var container = textBox.Parent as Grid;
            var page = GetPageByTextContainer(container);
            if (page != null && container != null)
            {
                PushUndoAction(new TextFormatChangedAction(
                    page,
                    container,
                    new TextFormatSnapshot(
                        beforeWeight.Weight >= Microsoft.UI.Text.FontWeights.Bold.Weight,
                        beforeStyle == Windows.UI.Text.FontStyle.Italic,
                        beforeFamily?.Source ?? "Segoe UI",
                        beforeAlignment.ToString()),
                    new TextFormatSnapshot(
                        textBox.FontWeight.Weight >= Microsoft.UI.Text.FontWeights.Bold.Weight,
                        textBox.FontStyle == Windows.UI.Text.FontStyle.Italic,
                        textBox.FontFamily?.Source ?? "Segoe UI",
                        textBox.TextAlignment.ToString())));
            }

            _textBold = textBox.FontWeight.Weight >= Microsoft.UI.Text.FontWeights.Bold.Weight;
            _textItalic = textBox.FontStyle == Windows.UI.Text.FontStyle.Italic;
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
            var container = _selectedTextBox.Parent as Grid;
            var page = GetPageByTextContainer(container);

            _selectedTextBox.FontSize = nextSize;
            _currentFontSize = nextSize;
            if (page != null && container != null)
            {
                var beforeColor = (beforeBrush as SolidColorBrush)?.Color
                    ?? Windows.UI.Color.FromArgb(255, 0, 0, 0);
                var afterColor = (_selectedTextBox.Foreground as SolidColorBrush)?.Color
                    ?? beforeColor;
                PushUndoAction(new TextStyleChangedAction(
                    page,
                    container,
                    new TextStyleSnapshot(currentSize, beforeColor.R, beforeColor.G, beforeColor.B),
                    new TextStyleSnapshot(nextSize, afterColor.R, afterColor.G, afterColor.B)));
            }
            MarkDirty();
            PositionInlineTextBoxToolbar(_selectedTextBox.Parent as UIElement ?? _selectedTextBox);
            _selectedTextBox.Focus(FocusState.Programmatic);
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
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ResolveThemeBrush("ThemeForegroundBrush", Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B)),
            };
            var directionGlyph = new TextBlock
            {
                Text = increase ? "^" : "v",
                FontSize = 8,
                Margin = new Thickness(1, 0, 0, 0),
                VerticalAlignment = increase ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Foreground = ResolveThemeBrush("ThemeSubtleTextBrush", Color.FromArgb(0xFF, 0x6B, 0x72, 0x80)),
            };
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { sizeGlyph, directionGlyph },
            };
        }

        private void SyncPopupToSelectedTextBox()
        {
            if (_selectedTextBox == null)
                return;

            _currentFontSize = _selectedTextBox.FontSize;
            var current = (_selectedTextBox.Foreground as SolidColorBrush)?.Color
                ?? Windows.UI.Color.FromArgb(255, 0, 0, 0);
            if (_colorIndicator != null)
                _colorIndicator.Background = new SolidColorBrush(current);
            if (_textBoldButton != null)
            {
                _textBoldButton.IsChecked =
                    _selectedTextBox.FontWeight.Weight >= Microsoft.UI.Text.FontWeights.Bold.Weight;
            }
            if (_textItalicButton != null)
            {
                _textItalicButton.IsChecked =
                    _selectedTextBox.FontStyle == Windows.UI.Text.FontStyle.Italic;
            }
            if (_textFontFamilyCombo != null)
            {
                _textFontFamilyCombo.SelectedItem =
                    _selectedTextBox.FontFamily?.Source ?? "Segoe UI";
            }
            if (_textAlignmentCombo != null)
            {
                _textAlignment = _selectedTextBox.TextAlignment;
                _textAlignmentCombo.SelectedValue = _selectedTextBox.TextAlignment;
            }
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
            if (element == null || PdfScrollViewer == null)
                return false;

            double viewportWidth = PdfScrollViewer.ViewportWidth > 0
                ? PdfScrollViewer.ViewportWidth
                : PdfScrollViewer.ActualWidth;
            double viewportHeight = PdfScrollViewer.ViewportHeight > 0
                ? PdfScrollViewer.ViewportHeight
                : PdfScrollViewer.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0
                || element.RenderSize.Width <= 0 || element.RenderSize.Height <= 0)
            {
                return false;
            }

            try
            {
                var bounds = element.TransformToVisual(PdfScrollViewer)
                    .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));
                var viewportBounds = new Rect(0, 0, viewportWidth, viewportHeight);
                return bounds.Left <= viewportBounds.Right
                    && bounds.Right >= viewportBounds.Left
                    && bounds.Top <= viewportBounds.Bottom
                    && bounds.Bottom >= viewportBounds.Top;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Positions the floating toolbar on the host page's TextOverlay
        /// directly above the selected container (WPF
        /// PositionInlineTextBoxToolbar parity).
        /// </summary>
        private void PositionInlineTextBoxToolbar(UIElement placementTarget)
        {
            EnsureInlineTextBoxToolbar();
            if (_inlineTextBoxToolbar == null || _selectedTextBox == null)
                return;

            if (placementTarget is not Grid container)
                return;

            if (container.Parent is not Canvas canvas)
                return;

            if (_toolbarHostPage != null && !ReferenceEquals(_toolbarHostPage.TextOverlay, canvas))
                RemoveInlineTextBoxToolbar();

            _toolbarHostPage = _pageControls.FirstOrDefault(p => ReferenceEquals(p.TextOverlay, canvas));
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
            Canvas.SetZIndex(_inlineTextBoxToolbar, 2000);

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

        // ==================================================================
        // Task 8 Phase A — sticky-note editor popup + marker events
        // ==================================================================

        private void PageControl_StickyNoteActivated(object sender, Grid container)
        {
            if (sender is not PdfPageControl page || container == null)
                return;

            if (IsLiveStickyContainer(page, container)
                && page.GetOverlayData(container) is StickyNoteAnnotation note)
            {
                OpenStickyNoteEditor(page, container, note);
            }
        }

        private void PageControl_StickyNoteMoved(object sender, StickyNoteMovedEventArgs e)
        {
            if (_isLoadingAnnotations || e?.Container == null || sender is not PdfPageControl page
                || !IsLiveStickyContainer(page, e.Container))
            {
                return;
            }

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
                || page.GetOverlayData(container) is not StickyNoteAnnotation)
            {
                return;
            }

            // A marker-level Delete always wins over an open editor bubble —
            // same reversible action as the popup's delete button (WPF).
            if (ReferenceEquals(_stickyNoteEditingContainer, container))
                CancelStickyNoteEdit();

            if (page.RemoveTextContainerQuiet(container))
            {
                PushUndoAction(new StickyNoteDeletedAction(page, container));
                MarkDirty();
            }
        }

        /// <summary>
        /// Opens the sticky-note editor bubble under the marker — a
        /// light-dismiss <see cref="Popup"/> carrying a wrapped TextBox,
        /// Save/Cancel/Delete actions and a draggable grip header (WPF
        /// OpenStickyNoteEditor parity; Popup replaces the WPF placement
        /// popup with explicit root-space offsets).
        /// </summary>
        private void OpenStickyNoteEditor(PdfPageControl page, Grid container, StickyNoteAnnotation note)
        {
            CancelStickyNoteEdit();

            _stickyNoteEditingPage = page;
            _stickyNoteEditingContainer = container;
            _stickyNoteEditingModel = note;
            _stickyNoteEditingOriginalText = note.Text ?? string.Empty;
            _stickyNoteEditingOriginalPosition = new PointD(note.X, note.Y);
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
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10, 8, 10, 8),
                BorderThickness = new Thickness(1),
                Background = ResolveThemeBrush("ThemeControlBrush", Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                Foreground = ResolveThemeBrush("ThemeForegroundBrush", Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B)),
                BorderBrush = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
            };
            ScrollViewer.SetVerticalScrollBarVisibility(_stickyNoteEditor, ScrollBarVisibility.Auto);

            var saveButton = new Button { MinWidth = 84, MinHeight = 34, Margin = new Thickness(8, 0, 0, 0) };
            _stickyNoteSaveButton = saveButton;
            ApplyStickyNoteButtonMetadata(saveButton, LocalizationService.Get("Common.Save"), "Sticky.Save");

            var cancelButton = new Button { MinWidth = 84, MinHeight = 34 };
            _stickyNoteCancelButton = cancelButton;
            ApplyStickyNoteButtonMetadata(cancelButton, LocalizationService.Get("Common.Cancel"), "Sticky.Cancel");

            var deleteButton = new Button
            {
                MinWidth = 76,
                MinHeight = 34,
                Foreground = ResolveThemeBrush("ThemeDangerBrush", Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C)),
            };
            _stickyNoteDeleteButton = deleteButton;
            ApplyStickyNoteButtonMetadata(deleteButton, LocalizationService.Get("Editor.DeleteTooltip"), "Sticky.Delete");

            _stickyNoteTitleTextBlock = new TextBlock
            {
                Text = LocalizationService.Get("Editor.StickyNoteTooltip"),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ResolveThemeBrush("ThemeForegroundBrush", Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B)),
            };
            var gripIcon = new LucideIcon
            {
                Kind = "GripVertical",
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0),
                Stroke = ResolveThemeBrush("ThemeSubtleTextBrush", Color.FromArgb(0xFF, 0x6B, 0x72, 0x80)),
            };
            var dragHeaderContent = new StackPanel { Orientation = Orientation.Horizontal };
            dragHeaderContent.Children.Add(gripIcon);
            dragHeaderContent.Children.Add(_stickyNoteTitleTextBlock);
            _stickyNoteDragHandle = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(-4, -4, -4, 10),
                Child = dragHeaderContent,
            };
            AutomationProperties.SetAutomationId(_stickyNoteDragHandle, "Sticky.Editor.DragHandle");
            ApplyStickyNoteDragHandleMetadata();
            _stickyNoteDragHandle.PointerPressed += StickyNoteDragHandle_PointerPressed;
            _stickyNoteDragHandle.PointerMoved += StickyNoteDragHandle_PointerMoved;
            _stickyNoteDragHandle.PointerReleased += StickyNoteDragHandle_PointerReleased;
            _stickyNoteDragHandle.PointerCaptureLost += StickyNoteDragHandle_PointerCaptureLost;

            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(_stickyNoteDragHandle);
            panel.Children.Add(_stickyNoteEditor);
            var actionRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(deleteButton, 0);
            actionRow.Children.Add(deleteButton);
            var confirmActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
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
                Background = ResolveThemeBrush("ThemeSurfaceBrush", Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                BorderBrush = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
            };
            AutomationProperties.SetAutomationId(border, $"Sticky.Editor.{note.Id}");

            // WPF Placement=Bottom under the marker → explicit root-space
            // offsets (the drag handle then adjusts them).
            _stickyNotePopup = new Popup
            {
                XamlRoot = XamlRoot,
                IsLightDismissEnabled = true,
                Child = border,
            };
            double markerHeight = container.ActualHeight > 0
                ? container.ActualHeight
                : container.Height > 0 ? container.Height : 36;
            var anchor = container.TransformToVisual(null)
                .TransformPoint(new Point(0, markerHeight + 6));
            _stickyNotePopup.HorizontalOffset = anchor.X;
            _stickyNotePopup.VerticalOffset = anchor.Y;

            _stickyNotePopup.Closed += StickyNotePopup_Closed;
            saveButton.Click += (_, __) => SaveStickyNoteEdit();
            cancelButton.Click += (_, __) => CancelStickyNoteEdit();
            deleteButton.Click += (_, __) => DeleteStickyNoteEdit();
            _stickyNotePopup.IsOpen = true;
            _stickyNoteEditor.Focus(FocusState.Programmatic);
            _stickyNoteEditor.SelectAll();
        }

        private void StickyNotePopup_Closed(object sender, object e)
        {
            // Clicking outside, Escape and teardown all follow the explicit
            // Cancel contract; only the Save button commits (WPF parity).
            CancelStickyNoteEdit();
        }

        private void CloseStickyNotePopup(Popup popup)
        {
            if (popup == null)
                return;

            EndStickyNotePopupDrag();
            popup.Closed -= StickyNotePopup_Closed;
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
            button.IsTabStop = true;
            if (button.MinHeight < 32)
                button.MinHeight = 32;
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

        private void StickyNoteDragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(_stickyNoteDragHandle).Properties.IsLeftButtonPressed
                || _stickyNotePopup == null)
            {
                return;
            }

            BeginStickyNotePopupDrag(e.GetCurrentPoint(this).Position);
            _stickyNoteDragHandle?.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void StickyNoteDragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingStickyNotePopup
                && e.GetCurrentPoint(_stickyNoteDragHandle).Properties.IsLeftButtonPressed)
            {
                UpdateStickyNotePopupDrag(e.GetCurrentPoint(this).Position);
                e.Handled = true;
            }
        }

        private void StickyNoteDragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingStickyNotePopup)
            {
                EndStickyNotePopupDrag();
                e.Handled = true;
            }
        }

        private void StickyNoteDragHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
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

            double deltaX = pointerPosition.X - _stickyNotePopupDragStart.X;
            double deltaY = pointerPosition.Y - _stickyNotePopupDragStart.Y;
            _stickyNotePopup.HorizontalOffset = _stickyNotePopupDragStartHorizontalOffset + deltaX;
            _stickyNotePopup.VerticalOffset = _stickyNotePopupDragStartVerticalOffset + deltaY;
        }

        private void EndStickyNotePopupDrag()
        {
            if (!_isDraggingStickyNotePopup)
                return;

            _isDraggingStickyNotePopup = false;
            _stickyNoteDragHandle?.ReleasePointerCaptures();
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

        // Compatibility shim for the close/save barriers the T9 pipeline
        // adds — WPF CommitStickyNoteEdit → SaveStickyNoteEdit.
        private void CommitStickyNoteEdit()
        {
            SaveStickyNoteEdit();
        }

        private static TextAlignment ParseTextAlignment(string value)
        {
            return Enum.TryParse<TextAlignment>(value, true, out var alignment)
                ? alignment
                : TextAlignment.Left;
        }

        /// <summary>
        /// Live annotation collector for the T9 save pipeline — rebuilds
        /// <see cref="PageAnnotation"/> per page from the current container
        /// state so a save always reflects what the user sees (WPF
        /// CollectAnnotations parity).
        /// </summary>
        internal Dictionary<int, PageAnnotation> CollectAnnotations()
        {
            var annotations = new Dictionary<int, PageAnnotation>();
            foreach (var page in _pageControls)
            {
                var pa = new PageAnnotation
                {
                    Strokes = page.Ink.Store.Strokes.Select(s => s.ToAnnotation()).ToList(),
                    HiddenInks = page.GetHiddenInkData(),
                    Texts = page.GetTextData(),
                    StickyNotes = page.GetStickyNoteData(),
                    Highlights = page.GetHighlights().ToList(),
                };

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
                        Width = imageContainer.ActualWidth > 0
                            ? imageContainer.ActualWidth : imageContainer.Width,
                        Height = imageContainer.ActualHeight > 0
                            ? imageContainer.ActualHeight : imageContainer.Height,
                        Format = PdfService.DetectImageFormat(imageData),
                        ImageDataBase64 = Convert.ToBase64String(imageData),
                        RotationDegrees = PdfPageControl.ReadAnnotationRotation(imageContainer),
                    });
                }

                // Task 25/27: overlay annotations share the image
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
                            B = markup.B,
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
                        double width = container.ActualWidth > 0
                            ? container.ActualWidth : container.Width;
                        double height = container.ActualHeight > 0
                            ? container.ActualHeight : container.Height;
                        double scaleX = originalWidth > 0 ? width / originalWidth : 1;
                        double scaleY = originalHeight > 0 ? height / originalHeight : 1;
                        foreach (var rect in markup.Rects)
                        {
                            if (rect != null && rect.Length >= 4)
                            {
                                copy.Rects.Add(new[]
                                {
                                    rect[0] * scaleX, rect[1] * scaleY,
                                    rect[2] * scaleX, rect[3] * scaleY,
                                });
                            }
                        }
                        pa.TextMarkups.Add(copy);
                    }
                    else if (data is AreaHighlightAnnotation area)
                    {
                        pa.AreaHighlights.Add(new AreaHighlightAnnotation
                        {
                            X = x,
                            Y = y,
                            Width = container.ActualWidth > 0
                                ? container.ActualWidth : container.Width,
                            Height = container.ActualHeight > 0
                                ? container.ActualHeight : container.Height,
                            R = area.R,
                            G = area.G,
                            B = area.B,
                            A = area.A,
                        });
                    }
                }

                if (pa.Strokes.Count > 0 || pa.Texts.Count > 0
                    || pa.StickyNotes.Count > 0 || pa.HiddenInks.Count > 0
                    || pa.Highlights.Count > 0 || pa.Images.Count > 0
                    || pa.TextMarkups.Count > 0 || pa.AreaHighlights.Count > 0)
                {
                    annotations[page.PageIndex] = pa;
                }
            }
            return annotations;
        }

        // ── Tool flyouts (WPF popups → WinUI Flyout) ────────────────────

        /// <summary>
        /// The Shape tool's options flyout — the WPF _shapePopup port:
        /// 3×3 shape-kind grid, Solid/Dashed line style, size slider
        /// (1–20, 0.5 steps) and the shared 12×8 HSV palette. Selection is
        /// session-only exactly like WPF and applies to all pages at once.
        /// </summary>
        private void ShowShapeFlyout(FrameworkElement anchor)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };

            panel.Children.Add(PopupSectionHeader(LocalizationService.Get("Editor.ShapeHeader")));

            var shapeGrid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
            for (int col = 0; col < 3; col++)
                shapeGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var buttons = new Dictionary<InkShapeKind, ToggleButton>();
            var choices = new (InkShapeKind Kind, string Label, string AutomationId)[]
            {
                (InkShapeKind.Line, LocalizationService.Get("Editor.ShapeLine"), "Editor.Shape.Line"),
                (InkShapeKind.Rectangle, LocalizationService.Get("Editor.ShapeRectangle"), "Editor.Shape.Rectangle"),
                (InkShapeKind.Ellipse, LocalizationService.Get("Editor.ShapeEllipse"), "Editor.Shape.Ellipse"),
                (InkShapeKind.Arrow, LocalizationService.Get("Editor.ShapeArrow"), "Editor.Shape.Arrow"),
                (InkShapeKind.Triangle, LocalizationService.Get("Editor.ShapeTriangle"), "Editor.Shape.Triangle"),
                (InkShapeKind.Diamond, LocalizationService.Get("Editor.ShapeDiamond"), "Editor.Shape.Diamond"),
                (InkShapeKind.Parallelogram, LocalizationService.Get("Editor.ShapeParallelogram"), "Editor.Shape.Parallelogram"),
                (InkShapeKind.Pentagon, LocalizationService.Get("Editor.ShapePentagon"), "Editor.Shape.Pentagon"),
                (InkShapeKind.Hexagon, LocalizationService.Get("Editor.ShapeHexagon"), "Editor.Shape.Hexagon"),
            };

            foreach (var choice in choices)
            {
                var button = BuildGlyphToggleButton(
                    choice.Label, choice.AutomationId, BuildShapePreviewGlyph(choice.Kind));
                button.Click += (_, __) =>
                {
                    if (_shapeKind == choice.Kind)
                        return;
                    _shapeKind = choice.Kind;
                    ApplyShapeKindVisuals(buttons);
                    if (_currentTool == ToolType.Shape)
                        ApplyToolToAllPages();
                };
                buttons[choice.Kind] = button;
                int slot = shapeGrid.Children.Count;
                while (shapeGrid.RowDefinitions.Count <= slot / 3)
                    shapeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(button, slot % 3);
                Grid.SetRow(button, slot / 3);
                shapeGrid.Children.Add(button);
            }
            panel.Children.Add(shapeGrid);

            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.ShapeLineStyleHeader"), topMargin: 12));

            var styleGrid = new Grid { ColumnSpacing = 4 };
            for (int col = 0; col < 2; col++)
                styleGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var solidButton = BuildGlyphToggleButton(
                LocalizationService.Get("Editor.ShapeSolid"),
                "Editor.Shape.Style.Solid",
                BuildShapePreviewGlyph(InkShapeKind.Line));
            var dashedButton = BuildGlyphToggleButton(
                LocalizationService.Get("Editor.ShapeDashed"),
                "Editor.Shape.Style.Dashed",
                BuildShapePreviewGlyph(InkShapeKind.DashedLine));
            solidButton.Click += (_, __) =>
            {
                if (_shapeIsDashed)
                {
                    _shapeIsDashed = false;
                    ApplyShapeStyleVisuals(solidButton, dashedButton);
                    if (_currentTool == ToolType.Shape)
                        ApplyToolToAllPages();
                }
            };
            dashedButton.Click += (_, __) =>
            {
                if (!_shapeIsDashed)
                {
                    _shapeIsDashed = true;
                    ApplyShapeStyleVisuals(solidButton, dashedButton);
                    if (_currentTool == ToolType.Shape)
                        ApplyToolToAllPages();
                }
            };
            Grid.SetColumn(dashedButton, 1);
            styleGrid.Children.Add(solidButton);
            styleGrid.Children.Add(dashedButton);
            panel.Children.Add(styleGrid);

            ApplyShapeKindVisuals(buttons);
            ApplyShapeStyleVisuals(solidButton, dashedButton);

            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.PopupSize"), topMargin: 12));
            var slider = new Slider
            {
                Minimum = 1,
                Maximum = 20,
                Value = _shapeSize,
                StepFrequency = 0.5,
                Width = 240,
            };
            AutomationProperties.SetAutomationId(slider, "Editor.Shape.Size");
            AutomationProperties.SetName(slider, LocalizationService.Get("Editor.PopupSize"));
            slider.ValueChanged += (_, args) =>
            {
                _shapeSize = args.NewValue;
                if (_currentTool == ToolType.Shape)
                    ApplyToolToAllPages();
            };
            panel.Children.Add(slider);

            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.PopupColor"), topMargin: 12));
            panel.Children.Add(BuildColorPalette(_shapeColor, color =>
            {
                _shapeColor = color;
                if (_currentTool == ToolType.Shape)
                    ApplyToolToAllPages();
            }));

            var flyout = new Flyout { Content = WrapToolFlyoutContent(panel) };
            ShowToolFlyout(flyout, ToolType.Shape, anchor);
        }

        /// <summary>
        /// The Highlighter tool's options flyout — the WPF _highlighterPopup
        /// port: the six apply modes (Freehand / Text / Underline /
        /// StrikeOut / Squiggly / Area) as a 3×2 grid, the size slider
        /// (2–48, 0.5 steps) and the shared 12×8 HSV palette. Mode choice is
        /// session-only and switches the live tool immediately (WPF
        /// SelectMode → ActivateHighlighterModeTool).
        /// </summary>
        private void ShowHighlighterFlyout(FrameworkElement anchor)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };

            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.HighlighterModeHeader")));

            var modeGrid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
            for (int col = 0; col < 3; col++)
                modeGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            modeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
            var previews = new Dictionary<HighlighterApplyMode, Microsoft.UI.Xaml.Shapes.Path>();

            void ApplyVisual()
            {
                foreach (var pair in buttons)
                {
                    bool active = pair.Key == _highlighterApplyMode;
                    StylePopupToggle(pair.Value, active);
                    if (previews.TryGetValue(pair.Key, out var preview))
                        ApplyHighlighterPreviewVisual(pair.Key, preview, _highlighterColor, _highlighterSize);
                    if (pair.Value.Content is StackPanel content
                        && content.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock text)
                    {
                        text.Foreground = ResolveThemeBrush(
                            active ? "ThemeAccentBrush" : "ThemeForegroundBrush",
                            Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B));
                        text.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                    }
                }
            }

            for (int i = 0; i < modes.Length; i++)
            {
                var mode = modes[i].Mode;
                var preview = BuildHighlighterModePreview(mode);
                var label = new TextBlock
                {
                    Text = modes[i].Label,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                    Foreground = ResolveThemeBrush("ThemeForegroundBrush", Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B)),
                };
                var contentPanel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                contentPanel.Children.Add(preview);
                contentPanel.Children.Add(label);

                var button = new ToggleButton
                {
                    Height = 54,
                    MinWidth = 32,
                    MinHeight = 32,
                    Margin = new Thickness(2),
                    Padding = new Thickness(4),
                    Content = contentPanel,
                };
                ToolTipService.SetToolTip(button, modes[i].Label);
                AutomationProperties.SetAutomationId(button, modes[i].AutomationId);
                AutomationProperties.SetName(button, modes[i].Label);
                AutomationProperties.SetHelpText(button, modes[i].Label);
                button.Click += (_, __) =>
                {
                    if (_highlighterApplyMode != mode)
                    {
                        _highlighterApplyMode = mode;
                        ApplyVisual();
                        // WPF SelectMode parity: switch the live tool
                        // immediately; the flyout stays open for further
                        // colour/size tweaks.
                        ActivateHighlighterModeTool();
                    }
                    button.IsChecked = true;
                };

                buttons[mode] = button;
                previews[mode] = preview;
                Grid.SetColumn(button, i % 3);
                Grid.SetRow(button, i / 3);
                modeGrid.Children.Add(button);
            }
            panel.Children.Add(modeGrid);
            ApplyVisual();

            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.PopupSize"), topMargin: 12));
            var slider = new Slider
            {
                Minimum = 2,
                Maximum = 48,
                Value = _highlighterSize,
                StepFrequency = 0.5,
                Width = 240,
            };
            AutomationProperties.SetAutomationId(slider, "Editor.Highlighter.Size");
            AutomationProperties.SetName(slider, LocalizationService.Get("Editor.PopupSize"));
            slider.ValueChanged += (_, args) =>
            {
                _highlighterSize = args.NewValue;
                if (_highlighterFlyoutSizePreview != null)
                    _highlighterFlyoutSizePreview.StrokeThickness = args.NewValue;
                ApplyVisual();
                if (_currentTool == ToolType.Highlighter)
                    ApplyToolToAllPages();
            };
            panel.Children.Add(slider);

            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.PopupColor"), topMargin: 12));
            // G4: the "最近 Recent" swatch row sits between the colour header
            // and the palette (WPF BuildToolPopup recentColors ordering);
            // repopulated on every show since the flyout is rebuilt.
            var recentSection = BuildRecentColorsSection(out var recentRow);
            panel.Children.Add(recentSection);

            Action<Windows.UI.Color> markPalette = null;
            void ApplyPickedColor(Windows.UI.Color color)
            {
                markPalette?.Invoke(color);
                _highlighterColor = color;
                if (_highlighterFlyoutSizePreview != null)
                    _highlighterFlyoutSizePreview.Stroke = new SolidColorBrush(
                        GetHighlighterPreviewStrokeColor(HighlighterApplyMode.Freehand, color));
                ApplyVisual();
                if (HighlighterColorIndicator != null)
                {
                    HighlighterColorIndicator.Background = new SolidColorBrush(
                        GetHighlighterPreviewStrokeColor(HighlighterApplyMode.Freehand, color));
                }
                // Freehand strokes + area-highlight drags both consume the
                // colour live (WPF ApplyToolToAllPages gate parity).
                if (_currentTool == ToolType.Highlighter || _currentTool == ToolType.AreaHighlight)
                    ApplyToolToAllPages();
                SaveSetting(s => RecordRecentColor(s.RecentHighlighterColors, color));
            }

            var palette = BuildColorPalette(
                _highlighterColor, ApplyPickedColor, recentRow, out markPalette);
            panel.Children.Add(palette);
            RefreshRecentColorsRow(
                recentSection,
                recentRow,
                () => AppSettingsService.Load().RecentHighlighterColors,
                ApplyPickedColor,
                markPalette);
            markPalette?.Invoke(_highlighterColor);

            // Preview section (WPF AddSizePreviewSection, isHighlighter
            // branch): a horizontal band drawn at the real stroke thickness
            // inside the alt-surface well — it follows the size slider and
            // palette/recents picks live and is painted at the freehand
            // alpha (WPF GetHighlighterPreviewStrokeColor(Freehand) parity).
            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.PopupPreview"), topMargin: 12));
            var previewBorder = new Border
            {
                Height = 60,
                CornerRadius = new CornerRadius(8),
                Background = ResolveThemeBrush(
                    "ThemeSurfaceAltBrush", Color.FromArgb(0xFF, 0xF1, 0xF3, 0xF5)),
            };
            // WPF ClipToBounds parity — a thick stroke cannot bleed past
            // the rounded well corners.
            previewBorder.SizeChanged += (_, args) =>
                previewBorder.Clip = new RectangleGeometry
                {
                    Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height),
                };
            _highlighterFlyoutSizePreview = new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = 8,
                Y1 = 30,
                X2 = 212,
                Y2 = 30,
                Stroke = new SolidColorBrush(GetHighlighterPreviewStrokeColor(
                    HighlighterApplyMode.Freehand, _highlighterColor)),
                StrokeThickness = _highlighterSize,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            previewBorder.Child = _highlighterFlyoutSizePreview;
            panel.Children.Add(previewBorder);

            var flyout = new Flyout { Content = WrapToolFlyoutContent(panel) };
            ShowToolFlyout(flyout, ToolType.Highlighter, anchor);
        }

        /// <summary>
        /// The Pen tool's options flyout — the WPF _penPopup port: size
        /// slider (0.5–8, 0.25 steps, "Editor.Pen.Size"), the "最近 Recent"
        /// swatch row over the shared 12×8 HSV palette, the live diagonal
        /// preview stroke ("Editor.PopupPreview"), the Pressure / Ink
        /// Simulation / Shape Recognition toggles (persisted to AppSettings
        /// via SaveSetting) and the Off/Low/Mid/High smoothing selector
        /// ("Editor.SmoothingHeader", "Editor.Pen.Smoothing.{i}").
        /// </summary>
        private void ShowPenFlyout(FrameworkElement anchor)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };

            // Size section (WPF BuildToolPopup order: size → colour →
            // preview → behaviour toggles → smoothing).
            string sizeLabel = LocalizationService.Get("Editor.PopupSize");
            panel.Children.Add(PopupSectionHeader(sizeLabel));
            var slider = new Slider
            {
                Minimum = 0.5,
                Maximum = 8,
                Value = _penSize,
                StepFrequency = 0.25,
                Width = 240,
            };
            AutomationProperties.SetAutomationId(slider, "Editor.Pen.Size");
            AutomationProperties.SetName(slider, sizeLabel);
            AutomationProperties.SetHelpText(slider, sizeLabel);
            slider.ValueChanged += (_, args) =>
            {
                _penSize = args.NewValue;
                if (_penFlyoutSizePreview != null)
                    _penFlyoutSizePreview.StrokeThickness = args.NewValue;
                if (_currentTool == ToolType.Pen)
                    ApplyToolToAllPages();
            };
            panel.Children.Add(slider);

            // Colour section: header → recents row → shared palette.
            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.PopupColor"), topMargin: 12));
            var recentSection = BuildRecentColorsSection(out var recentRow);
            panel.Children.Add(recentSection);

            Action<Windows.UI.Color> markPalette = null;
            void ApplyPickedPenColor(Windows.UI.Color color)
            {
                markPalette?.Invoke(color);
                _penColor = color;
                if (_penFlyoutSizePreview != null)
                    _penFlyoutSizePreview.Stroke = new SolidColorBrush(color);
                // WPF UpdateToolIconColors parity — the pen glyph's colour
                // bar follows the picked colour.
                if (PenColorIndicator != null)
                    PenColorIndicator.Background = new SolidColorBrush(color);
                if (_currentTool == ToolType.Pen)
                    ApplyToolToAllPages();
                SaveSetting(s => RecordRecentColor(s.RecentPenColors, color));
            }

            var palette = BuildColorPalette(
                _penColor, ApplyPickedPenColor, recentRow, out markPalette);
            panel.Children.Add(palette);
            RefreshRecentColorsRow(
                recentSection,
                recentRow,
                () => AppSettingsService.Load().RecentPenColors,
                ApplyPickedPenColor,
                markPalette);
            markPalette?.Invoke(_penColor);

            // Preview section: diagonal stroke inside the alt-surface well —
            // the stroke follows size slider + palette picks live.
            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.PopupPreview"), topMargin: 12));
            var previewBorder = new Border
            {
                Height = 60,
                CornerRadius = new CornerRadius(8),
                Background = ResolveThemeBrush(
                    "ThemeSurfaceAltBrush", Color.FromArgb(0xFF, 0xF1, 0xF3, 0xF5)),
            };
            // WPF ClipToBounds parity — a fat stroke cannot bleed past the
            // rounded well corners.
            previewBorder.SizeChanged += (_, args) =>
                previewBorder.Clip = new RectangleGeometry
                {
                    Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height),
                };
            _penFlyoutSizePreview = new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = 8,
                Y1 = 48,
                X2 = 212,
                Y2 = 12,
                Stroke = new SolidColorBrush(_penColor),
                StrokeThickness = _penSize,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            previewBorder.Child = _penFlyoutSizePreview;
            panel.Children.Add(previewBorder);

            // Behaviour toggles (WPF AddPenBehaviourToggles): a 3-column
            // grid of vertical setting toggles. UniformGrid has no WinUI
            // equivalent — star columns produce the same thirds.
            var pressureToggle = BuildSettingToggleButton(
                LocalizationService.Get("Editor.Pressure"),
                AppSettingsService.Load().EnablePressure,
                v => { SaveSetting(s => s.EnablePressure = v); ApplyToolToAllPages(); },
                "Editor.Pen.Pressure");
            var inkSimToggle = BuildSettingToggleButton(
                LocalizationService.Get("Editor.InkSimulation"),
                AppSettingsService.Load().InkSimulation,
                v => { SaveSetting(s => s.InkSimulation = v); ApplyToolToAllPages(); },
                "Editor.Pen.InkSimulation");
            var shapeRecognitionToggle = BuildSettingToggleButton(
                LocalizationService.Get("Editor.ShapeRecognition"),
                AppSettingsService.Load().ShapeRecognition,
                v => { SaveSetting(s => s.ShapeRecognition = v); ApplyToolToAllPages(); },
                "Editor.Pen.ShapeRecognition");

            var behaviourGrid = new Grid
            {
                ColumnSpacing = 6,
                Margin = new Thickness(0, 0, 0, 6),
            };
            for (int col = 0; col < 3; col++)
                behaviourGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int column = 0;
            foreach (var toggle in new[] { pressureToggle, inkSimToggle, shapeRecognitionToggle })
            {
                // WPF cell adjustments: taller vertical tile, centred
                // indicator over a wrapped caption.
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
                Grid.SetColumn(toggle, column++);
                behaviourGrid.Children.Add(toggle);
            }
            // WPF AddPenBehaviourToggles parity — the toggle grid carries
            // no section header, so the rule line goes in by hand.
            panel.Children.Add(PopupSectionDivider());
            panel.Children.Add(behaviourGrid);

            // Smoothing selector (WPF AddPenSmoothingSection): Off/Low/
            // Mid/High segmented row persisted to AppSettings.StrokeSmoothing.
            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.SmoothingHeader"), topMargin: 12));
            var smoothingLabels = new[]
            {
                LocalizationService.Get("Editor.SmoothingOff"),
                LocalizationService.Get("Editor.SmoothingLow"),
                LocalizationService.Get("Editor.SmoothingMid"),
                LocalizationService.Get("Editor.SmoothingHigh"),
            };
            var smoothingButtons = new ToggleButton[smoothingLabels.Length];
            var smoothingRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6),
            };

            void ApplySmoothingVisual()
            {
                int current = AppSettingsService.Load().StrokeSmoothing;
                for (int i = 0; i < smoothingButtons.Length; i++)
                    StylePopupToggle(smoothingButtons[i], active: i == current);
            }

            for (int i = 0; i < smoothingLabels.Length; i++)
            {
                int level = i;
                var button = BuildTextToggleButton(
                    smoothingLabels[i], $"Editor.Pen.Smoothing.{i}");
                button.Width = 54;
                button.Margin = new Thickness(0, 0, i < smoothingLabels.Length - 1 ? 6 : 0, 0);
                button.Click += (_, __) =>
                {
                    if (AppSettingsService.Load().StrokeSmoothing == level)
                    {
                        // Mutually-exclusive mode semantics: re-clicking the
                        // armed level must not leave it unchecked.
                        button.IsChecked = true;
                        return;
                    }
                    SaveSetting(settings => settings.StrokeSmoothing = level);
                    ApplyToolToAllPages();
                    ApplySmoothingVisual();
                    button.IsChecked = true;
                };
                smoothingButtons[i] = button;
                smoothingRow.Children.Add(button);
            }
            panel.Children.Add(smoothingRow);
            ApplySmoothingVisual();

            var flyout = new Flyout { Content = WrapToolFlyoutContent(panel) };
            ShowToolFlyout(flyout, ToolType.Pen, anchor);
        }

        /// <summary>
        /// The Eraser tool's options flyout — the WPF _eraserPopup port:
        /// the pixel / whole-stroke mode row ("Editor.Eraser.Pixel" /
        /// "Editor.Eraser.WholeStroke", persisted to
        /// AppSettings.WholeStrokeEraser) above the 4–80 size slider
        /// ("Editor.Eraser.Size"). No palette — the eraser owns no colour.
        /// </summary>
        private void ShowEraserFlyout(FrameworkElement anchor)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };

            // WPF AddEraserModeSection inserts the mode row above the size
            // section — same visual order here.
            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.EraserModeHeader")));
            var modeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 14),
            };
            var pixelButton = BuildTextToggleButton(
                LocalizationService.Get("Editor.EraserPixel"), "Editor.Eraser.Pixel");
            var wholeButton = BuildTextToggleButton(
                LocalizationService.Get("Editor.EraserStroke"), "Editor.Eraser.WholeStroke");
            pixelButton.Width = 116;
            wholeButton.Width = 116;
            pixelButton.Margin = new Thickness(0, 0, 8, 0);
            wholeButton.Margin = new Thickness(0);

            void ApplyModeVisual()
            {
                bool whole = AppSettingsService.Load().WholeStrokeEraser;
                StylePopupToggle(pixelButton, active: !whole);
                StylePopupToggle(wholeButton, active: whole);
            }

            void SelectMode(bool whole)
            {
                var settings = AppSettingsService.Load();
                if (settings.WholeStrokeEraser == whole)
                    return;
                settings.WholeStrokeEraser = whole;
                // Keep the editor cache and the page controls on the same
                // snapshot — a stale _applicationSettings would undo the
                // mode the user just selected (WPF comment parity).
                _applicationSettings = AppSettingsService.Save(settings);
                ApplyToolToAllPages();
                ApplyModeVisual();
            }

            pixelButton.Click += (_, __) =>
            {
                SelectMode(false);
                // Mutually-exclusive modes stay armed even when the
                // already-selected option is re-clicked.
                pixelButton.IsChecked = true;
            };
            wholeButton.Click += (_, __) =>
            {
                SelectMode(true);
                wholeButton.IsChecked = true;
            };
            modeRow.Children.Add(pixelButton);
            modeRow.Children.Add(wholeButton);
            panel.Children.Add(modeRow);
            ApplyModeVisual();

            string sizeLabel = LocalizationService.Get("Editor.PopupEraserSize");
            panel.Children.Add(PopupSectionHeader(sizeLabel));
            var slider = new Slider
            {
                Minimum = 4,
                Maximum = 80,
                Value = _eraserSize,
                StepFrequency = 1,
                Width = 240,
            };
            AutomationProperties.SetAutomationId(slider, "Editor.Eraser.Size");
            AutomationProperties.SetName(slider, sizeLabel);
            AutomationProperties.SetHelpText(slider, sizeLabel);
            slider.ValueChanged += (_, args) =>
            {
                _eraserSize = args.NewValue;
                ShowEraserSizePreview(args.NewValue);
                ApplyToolToAllPages();
            };
            panel.Children.Add(slider);

            var flyout = new Flyout { Content = WrapToolFlyoutContent(panel) };
            ShowToolFlyout(flyout, ToolType.Eraser, anchor);
        }

        /// <summary>
        /// WPF ShowEraserSizePreview parity: flashes the eraser footprint as
        /// a centred ellipse for ~1.2 s so slider drags show the real stamp
        /// size. Each new value re-arms the self-hide timer.
        /// </summary>
        private void ShowEraserSizePreview(double size)
        {
            if (EraserSizePreviewEllipse == null)
                return;
            EraserSizePreviewEllipse.Width = size;
            EraserSizePreviewEllipse.Height = size;
            EraserSizePreviewEllipse.Visibility = Visibility.Visible;

            _eraserPreviewCts?.Cancel();
            _eraserPreviewCts = new CancellationTokenSource();
            var token = _eraserPreviewCts.Token;

            _ = Task.Delay(1200).ContinueWith(
                _ =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (EraserSizePreviewEllipse != null)
                                EraserSizePreviewEllipse.Visibility = Visibility.Collapsed;
                        });
                    }
                },
                TaskScheduler.Default);
        }

        /// <summary>
        /// The "最近 Recent" section shared by every colour flyout — a
        /// collapsed-until-populated StackPanel with the Editor.Recent
        /// header above the swatch row (WPF recentSection parity).
        /// </summary>
        private StackPanel BuildRecentColorsSection(out StackPanel row)
        {
            row = new StackPanel { Orientation = Orientation.Horizontal };
            var header = PopupSectionHeader(LocalizationService.Get("Editor.Recent"));
            header.Margin = new Thickness(0, 0, 0, 8);
            return new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 12),
                Visibility = Visibility.Collapsed,
                Children = { header, row },
            };
        }

        /// <summary>
        /// Repopulates a recent-colors swatch row from the settings list —
        /// the WPF RefreshRecentColorsRow port. Hidden entirely while empty;
        /// each 22×22 rounded swatch inside a 32×32 button applies its
        /// colour exactly like a palette cell of the owning flyout and
        /// carries the "Editor.Color.Recent.{i}" id.
        /// </summary>
        private void RefreshRecentColorsRow(
            StackPanel section,
            StackPanel row,
            Func<List<string>> getRecentColors,
            Action<Windows.UI.Color> applyColor,
            Action<Windows.UI.Color> selectedChanged = null)
        {
            row.Children.Clear();

            List<string> recent = null;
            try { recent = getRecentColors?.Invoke(); }
            catch { /* settings read failures leave the row hidden */ }

            if (recent != null)
            {
                foreach (var hex in recent)
                {
                    if (row.Children.Count >= RecentColors.MaxRecentColors)
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
                        BorderBrush = ResolveThemeBrush(
                            "ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
                    };
                    var swatch = new Button
                    {
                        Width = 32,
                        Height = 32,
                        Padding = new Thickness(3),
                        Margin = new Thickness(0, 0, 6, 0),
                        Content = swatchVisual,
                        Tag = color,
                    };
                    ToolTipService.SetToolTip(swatch, hex);
                    AutomationProperties.SetAutomationId(swatch, $"Editor.Color.Recent.{recentIndex}");
                    AutomationProperties.SetName(swatch, hex);
                    AutomationProperties.SetHelpText(swatch, hex);
                    swatch.Click += (_, __) =>
                    {
                        if (swatch.Tag is Windows.UI.Color picked)
                        {
                            applyColor?.Invoke(picked);
                            selectedChanged?.Invoke(picked);
                        }
                    };
                    row.Children.Add(swatch);
                }
            }

            section.Visibility = row.Children.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// "#RRGGBB" parse (tolerates "#AARRGGBB" hand-edits) — the UI-type
        /// half of <see cref="RecentColors.TryParse"/>.
        /// </summary>
        private static bool TryParseRecentColor(string hex, out Windows.UI.Color color)
        {
            if (RecentColors.TryParse(hex, out byte a, out byte r, out byte g, out byte b))
            {
                color = Windows.UI.Color.FromArgb(a, r, g, b);
                return true;
            }
            color = default;
            return false;
        }

        /// <summary>
        /// WPF RecordRecentColor parity: newest-first, deduped, capped at
        /// <see cref="RecentColors.MaxRecentColors"/> — the list lives on a
        /// transient AppSettings clone, persisted by the caller's
        /// <see cref="SaveSetting"/>.
        /// </summary>
        private void RecordRecentColor(List<string> list, Windows.UI.Color color)
            => RecentColors.Record(list, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

        /// <summary>
        /// WPF SaveSetting parity: load → mutate → save → refresh the editor
        /// settings cache so tool application reads the same snapshot the
        /// popup just wrote.
        /// </summary>
        private void SaveSetting(Action<AppSettings> mutate)
        {
            var settings = AppSettingsService.Load();
            mutate(settings);
            _applicationSettings = AppSettingsService.Save(settings);
        }

        /// <summary>
        /// WPF BuildSettingToggleRow parity: one clickable toggle row
        /// (indicator box + label) for boolean settings inside tool flyouts
        /// — checked state paints the accent indicator + tinted surface.
        /// </summary>
        private static ToggleButton BuildSettingToggleButton(
            string label, bool initialState, Action<bool> toggled, string automationId = null)
        {
            var indicator = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var text = new TextBlock
            {
                Text = label,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };
            var row = new ToggleButton
            {
                Height = 34,
                MinWidth = 32,
                MinHeight = 32,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 0, 10, 0),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { indicator, text },
                },
                Tag = indicator,
            };
            ToolTipService.SetToolTip(row, label);
            AutomationProperties.SetAutomationId(row, automationId ?? "Editor.Popup.Setting");
            AutomationProperties.SetName(row, label);
            AutomationProperties.SetHelpText(row, label);

            row.IsChecked = initialState;

            void ApplyVisual()
            {
                bool state = row.IsChecked == true;
                row.Background = ResolveThemeBrush(
                    state ? "ThemeSelectionBrush" : "ThemeSurfaceAltBrush",
                    state ? Color.FromArgb(0x3C, 0x25, 0x63, 0xEB)
                          : Color.FromArgb(0xFF, 0xF1, 0xF3, 0xF5));
                indicator.BorderBrush = ResolveThemeBrush(
                    state ? "ThemeAccentBrush" : "ThemeBorderBrush",
                    state ? Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)
                          : Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6));
                indicator.Background = state
                    ? ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB))
                    : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                text.Foreground = ResolveThemeBrush(
                    state ? "ThemeAccentBrush" : "ThemeForegroundBrush",
                    state ? Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)
                          : Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B));
            }

            ApplyVisual();
            row.Click += (_, __) =>
            {
                ApplyVisual();
                toggled?.Invoke(row.IsChecked == true);
            };
            return row;
        }

        /// <summary>
        /// WPF EnableToolPopupScrolling parity: tool flyout content scrolls
        /// when taller than the work-area-derived cap
        /// (Math.Max(320, Math.Min(680, height − 120))). WinUI has no
        /// SystemParameters.WorkArea — the XamlRoot content height stands in
        /// for it (flyouts cannot overflow the window anyway).
        /// </summary>
        private ScrollViewer WrapToolFlyoutContent(UIElement content)
        {
            double workHeight = XamlRoot != null && XamlRoot.Size.Height > 0
                ? XamlRoot.Size.Height
                : 680;
            return new ScrollViewer
            {
                Content = content,
                MaxHeight = Math.Max(320, Math.Min(680, workHeight - 120)),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
        }

        /// <summary>
        /// Shows a tool-options flyout under its toolbar button and records
        /// ownership so the mutual-exclusion sweep (<see cref="CloseToolFlyouts"/>)
        /// and the transient-UI sweep (<see cref="CloseTransientUi"/>) can
        /// both reach it. <paramref name="owner"/> is the bucket tool — all
        /// three highlighter apply modes share
        /// <see cref="ToolType.Highlighter"/> (WPF _highlighterPopup).
        /// </summary>
        private void ShowToolFlyout(Flyout flyout, ToolType owner, FrameworkElement anchor)
        {
            _toolFlyout?.Hide();
            _toolFlyout = flyout;
            _toolFlyoutTool = ToolFlyoutOwner(owner);
            _transientFlyout = flyout;
            // Light-dismiss self-closes the flyout — drop the stale refs so the
            // next CloseToolFlyouts doesn't see ghost ownership and the dead
            // flyout tree can collect.
            flyout.Closed += (_, __) =>
            {
                if (ReferenceEquals(_toolFlyout, flyout))
                {
                    _toolFlyout = null;
                    _toolFlyoutTool = ToolType.None;
                }
                if (ReferenceEquals(_transientFlyout, flyout))
                    _transientFlyout = null;
            };
            flyout.ShowAt(anchor);
        }

        /// <summary>Highlighter apply modes share one flyout owner bucket.</summary>
        private static ToolType ToolFlyoutOwner(ToolType tool)
            => IsHighlighterTool(tool) ? ToolType.Highlighter : tool;

        /// <summary>
        /// WPF CloseToolPopups parity: sweeps the open tool flyout unless it
        /// belongs to <paramref name="keepOpen"/> — the highlighter bucket
        /// keeps the flyout alive across apply-mode switches (WPF
        /// IsHighlighterTool(toolToKeepOpen)).
        /// </summary>
        private void CloseToolFlyouts(ToolType keepOpen = ToolType.None)
        {
            if (_toolFlyout == null)
                return;
            if (keepOpen != ToolType.None
                && _toolFlyoutTool == ToolFlyoutOwner(keepOpen))
                return;

            if (ReferenceEquals(_transientFlyout, _toolFlyout))
                _transientFlyout = null;
            _toolFlyout.Hide();
            _toolFlyout = null;
            _toolFlyoutTool = ToolType.None;
        }

        /// <summary>
        /// The six mode-preview glyphs — identical markup to the WPF
        /// BuildHighlighterModePreview table. The squiggly path spells the
        /// smooth-cubic S segments as explicit C curves (the shared mini
        /// parser has no S command).
        /// </summary>
        private Microsoft.UI.Xaml.Shapes.Path BuildHighlighterModePreview(HighlighterApplyMode mode)
        {
            var data = mode switch
            {
                HighlighterApplyMode.Freehand => "M3,16 C8,8 12,18 17,10 C20,5 23,12 26,6",
                HighlighterApplyMode.TextHighlight => "M3,7 H25 M3,14 H25",
                HighlighterApplyMode.Underline => "M4,6 H24 M4,11 H18 M3,16 H25",
                HighlighterApplyMode.StrikeOut => "M4,6 H24 M3,11 H25 M4,16 H20",
                // WPF "S16,10 19,14 S22,18 25,14" expanded: each S control is
                // the reflection of the previous second control point.
                HighlighterApplyMode.Squiggly => "M4,6 H24 M3,14 C6,10 8,18 11,14 C14,10 16,10 19,14 C22,18 22,18 25,14",
                _ => "M4,4 L24,4 L24,18 L4,18 Z", // AreaHighlight
            };
            var preview = new Microsoft.UI.Xaml.Shapes.Path
            {
                Width = 30,
                Height = 22,
                Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Data = LucideIcon.ParseIconGeometry(data),
            };
            ApplyHighlighterPreviewVisual(mode, preview, _highlighterColor, _highlighterSize);
            return preview;
        }

        /// <summary>
        /// Mode-aware preview styling — the stroke keeps the highlighter
        /// colour at the mode's own alpha so previews match the real
        /// pipelines (WPF ApplyHighlighterPreviewVisual parity).
        /// </summary>
        private static void ApplyHighlighterPreviewVisual(
            HighlighterApplyMode mode,
            Microsoft.UI.Xaml.Shapes.Path preview,
            Windows.UI.Color color,
            double size)
        {
            if (preview == null)
                return;
            preview.StrokeThickness = GetHighlighterPreviewStrokeThickness(mode, size);
            preview.Stroke = new SolidColorBrush(GetHighlighterPreviewStrokeColor(mode, color));
            byte fillOpacity = GetHighlighterPreviewFillOpacity(mode);
            preview.Fill = fillOpacity == 0
                ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
                : new SolidColorBrush(Color.FromArgb(fillOpacity, color.R, color.G, color.B));
        }

        private static double GetHighlighterPreviewStrokeThickness(HighlighterApplyMode mode, double size)
            => mode switch
            {
                HighlighterApplyMode.Freehand => Math.Clamp(1.6 + (size - 2.0) * (1.2 / 46.0), 1.6, 2.8),
                HighlighterApplyMode.AreaHighlight => 1.4,
                _ => 1.8,
            };

        private static byte GetHighlighterPreviewStrokeOpacity(HighlighterApplyMode mode)
            => mode switch
            {
                HighlighterApplyMode.Freehand => FreehandHighlighterOpacity,
                HighlighterApplyMode.TextHighlight => TextHighlightOpacity,
                HighlighterApplyMode.AreaHighlight => AreaHighlightStrokeOpacity,
                _ => byte.MaxValue,
            };

        private static byte GetHighlighterPreviewFillOpacity(HighlighterApplyMode mode)
            => mode == HighlighterApplyMode.AreaHighlight ? AreaHighlightFillOpacity : (byte)0;

        private static Windows.UI.Color GetHighlighterPreviewStrokeColor(
            HighlighterApplyMode mode, Windows.UI.Color color)
            => Windows.UI.Color.FromArgb(
                GetHighlighterPreviewStrokeOpacity(mode), color.R, color.G, color.B);

        /// <summary>
        /// The Select tool's options flyout — the WPF _selectionPopup port:
        /// marquee shape (Rectangle/Freehand), the Both/Drawings/Text filter
        /// and the "Selected drawing" restyle row (widths + swatches →
        /// InkStrokesStyleChangedAction).
        /// </summary>
        private void ShowSelectionFlyout(FrameworkElement anchor)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };

            panel.Children.Add(PopupSectionHeader(LocalizationService.Get("Editor.SelectShape")));

            var shapePanel = new StackPanel { Orientation = Orientation.Horizontal };
            var rectButton = BuildGlyphToggleButton(
                LocalizationService.Get("Editor.SelectShapeRect"),
                "Editor.Select.Shape.Rectangle",
                BuildSelectionShapePreviewGlyph(Caelum.Controls.SelectionShape.Rectangle));
            var freeButton = BuildGlyphToggleButton(
                LocalizationService.Get("Editor.SelectShapeFree"),
                "Editor.Select.Shape.FreeForm",
                BuildSelectionShapePreviewGlyph(Caelum.Controls.SelectionShape.FreeForm));
            void SelectShape(Caelum.Controls.SelectionShape shape)
            {
                _selectionShape = shape;
                StylePopupToggle(rectButton, shape == Caelum.Controls.SelectionShape.Rectangle);
                StylePopupToggle(freeButton, shape == Caelum.Controls.SelectionShape.FreeForm);
                ApplyToolToAllPages();
            }
            rectButton.Click += (_, __) => SelectShape(Caelum.Controls.SelectionShape.Rectangle);
            freeButton.Click += (_, __) => SelectShape(Caelum.Controls.SelectionShape.FreeForm);
            shapePanel.Children.Add(rectButton);
            shapePanel.Children.Add(freeButton);
            panel.Children.Add(shapePanel);

            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.SelectFilter"), topMargin: 12));

            var filterPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var bothButton = BuildTextToggleButton(
                LocalizationService.Get("Editor.SelectFilterBoth"), "Editor.Select.Filter.Both");
            var drawingsButton = BuildTextToggleButton(
                LocalizationService.Get("Editor.SelectFilterDrawings"), "Editor.Select.Filter.Drawings");
            var textButton = BuildTextToggleButton(
                LocalizationService.Get("Editor.SelectFilterText"), "Editor.Select.Filter.Text");
            void SelectFilter(Caelum.Controls.SelectionFilter filter)
            {
                _selectionFilter = filter;
                StylePopupToggle(bothButton, filter == Caelum.Controls.SelectionFilter.Both);
                StylePopupToggle(drawingsButton, filter == Caelum.Controls.SelectionFilter.DrawingsOnly);
                StylePopupToggle(textButton, filter == Caelum.Controls.SelectionFilter.TextOnly);
                ApplyToolToAllPages();
            }
            bothButton.Click += (_, __) => SelectFilter(Caelum.Controls.SelectionFilter.Both);
            drawingsButton.Click += (_, __) => SelectFilter(Caelum.Controls.SelectionFilter.DrawingsOnly);
            textButton.Click += (_, __) => SelectFilter(Caelum.Controls.SelectionFilter.TextOnly);
            filterPanel.Children.Add(bothButton);
            filterPanel.Children.Add(drawingsButton);
            filterPanel.Children.Add(textButton);
            panel.Children.Add(filterPanel);

            StylePopupToggle(rectButton, _selectionShape == Caelum.Controls.SelectionShape.Rectangle);
            StylePopupToggle(freeButton, _selectionShape == Caelum.Controls.SelectionShape.FreeForm);
            StylePopupToggle(bothButton, _selectionFilter == Caelum.Controls.SelectionFilter.Both);
            StylePopupToggle(drawingsButton, _selectionFilter == Caelum.Controls.SelectionFilter.DrawingsOnly);
            StylePopupToggle(textButton, _selectionFilter == Caelum.Controls.SelectionFilter.TextOnly);

            // ── Selected drawing style (restyles the live selection) ────
            panel.Children.Add(PopupSectionHeader(
                LocalizationService.Get("Editor.SelectedDrawingStyle"), topMargin: 12));

            var widthRow = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (double width in new[] { 1d, 2d, 4d, 8d })
            {
                var widthButton = new Button
                {
                    Content = width.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                    Width = 44,
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                AutomationProperties.SetAutomationId(
                    widthButton, $"Editor.Select.DrawingWidth.{width:0.#}");
                double w = width;
                widthButton.Click += (_, __) => ApplySelectedDrawingStyle(null, w);
                widthRow.Children.Add(widthButton);
            }
            panel.Children.Add(widthRow);

            var colorRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
            };
            var swatchColors = new[]
            {
                Windows.UI.Color.FromArgb(255, 0, 0, 0),
                Windows.UI.Color.FromArgb(255, 255, 0, 0),
                Windows.UI.Color.FromArgb(255, 255, 165, 0),
                Windows.UI.Color.FromArgb(255, 0, 128, 0),
                Windows.UI.Color.FromArgb(255, 0, 0, 255),
                Windows.UI.Color.FromArgb(255, 128, 0, 128),
            };
            foreach (var color in swatchColors)
            {
                var swatch = new Button
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(1),
                    Tag = color,
                };
                string label = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                AutomationProperties.SetAutomationId(swatch, $"Editor.Select.DrawingColor.{label[1..]}");
                AutomationProperties.SetName(swatch, label);
                var picked = color;
                swatch.Click += (_, __) => ApplySelectedDrawingStyle(picked, null);
                colorRow.Children.Add(swatch);
            }
            panel.Children.Add(colorRow);

            // WPF parity note: the selection popup never got
            // EnableToolPopupScrolling — it stays unwrapped here too.
            var flyout = new Flyout { Content = panel };
            ShowToolFlyout(flyout, ToolType.Select, anchor);
        }

        /// <summary>
        /// WPF ThemeDivider parity — a 1px rule in the theme border brush
        /// at 45% opacity, bled -4 horizontally to the flyout edge like the
        /// WPF -16 popup margins. <see cref="PopupSectionHeader"/> emits it
        /// before every non-first section; headerless sections (the pen
        /// behaviour toggles) add it by hand.
        /// </summary>
        private static Border PopupSectionDivider(double topMargin = 12, double bottomMargin = 12) => new()
        {
            Height = 1,
            Margin = new Thickness(-4, topMargin, -4, bottomMargin),
            Opacity = 0.45,
            Background = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
        };

        /// <summary>
        /// Section header text. A non-first section (<paramref name="topMargin"/>
        /// &gt; 0 — the marker every flyout already uses) is preceded by the
        /// WPF-style rule line via <see cref="PopupSectionDivider"/>, so the
        /// pen, highlighter, eraser, shape and selection flyouts all get the
        /// separators uniformly.
        /// </summary>
        private static FrameworkElement PopupSectionHeader(string text, double topMargin = 0)
        {
            var header = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = ResolveThemeBrush("ThemeSubtleForegroundBrush", Color.FromArgb(0xFF, 0x6B, 0x72, 0x80)),
            };
            if (topMargin <= 0)
                return header;
            return new StackPanel
            {
                Children =
                {
                    PopupSectionDivider(topMargin),
                    header,
                },
            };
        }

        private static ToggleButton BuildGlyphToggleButton(
            string tooltip, string automationId, UIElement glyph)
        {
            var button = new ToggleButton
            {
                Width = 44,
                Height = 36,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                Content = glyph,
            };
            ToolTipService.SetToolTip(button, tooltip);
            AutomationProperties.SetAutomationId(button, automationId);
            AutomationProperties.SetName(button, tooltip);
            return button;
        }

        private static ToggleButton BuildTextToggleButton(string label, string automationId)
        {
            var button = new ToggleButton
            {
                Content = label,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 12,
                MinHeight = 32,
            };
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetAutomationId(button, automationId);
            AutomationProperties.SetName(button, label);
            return button;
        }

        /// <summary>
        /// WPF UpdateFilterButtonStyle parity: checked tint + accent border
        /// when active, quiet surface otherwise.
        /// </summary>
        private static void StylePopupToggle(ToggleButton button, bool active)
        {
            button.IsChecked = active;
            button.Background = ResolveThemeBrush(
                active ? "ThemeSelectionBrush" : "ThemeSurfaceAltBrush",
                active ? Color.FromArgb(0x3C, 0x25, 0x63, 0xEB) : Color.FromArgb(0xFF, 0xF1, 0xF3, 0xF5));
            button.BorderBrush = ResolveThemeBrush(
                active ? "ThemeAccentBrush" : "ThemeBorderBrush",
                active ? Color.FromArgb(0xFF, 0x25, 0x63, 0xEB) : Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6));
            button.Foreground = ResolveThemeBrush(
                active ? "ThemeSelectionForegroundBrush" : "ThemeForegroundBrush",
                Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B));
            if (button.Content is Microsoft.UI.Xaml.Shapes.Path path)
            {
                path.Stroke = ResolveThemeBrush(
                    active ? "ThemeSelectionForegroundBrush" : "ThemeForegroundBrush",
                    Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B));
            }
        }

        private void ApplyShapeKindVisuals(Dictionary<InkShapeKind, ToggleButton> buttons)
        {
            foreach (var pair in buttons)
                StylePopupToggle(pair.Value, _shapeKind == pair.Key);
        }

        private void ApplyShapeStyleVisuals(ToggleButton solid, ToggleButton dashed)
        {
            StylePopupToggle(solid, !_shapeIsDashed);
            StylePopupToggle(dashed, _shapeIsDashed);
        }

        /// <summary>
        /// The shape-option glyph previews — identical markup to the WPF
        /// BuildShapePreview table, parsed by the shared mini parser.
        /// </summary>
        private static Microsoft.UI.Xaml.Shapes.Path BuildShapePreviewGlyph(InkShapeKind kind)
        {
            var data = kind switch
            {
                InkShapeKind.Rectangle => "M4,4 L28,4 L28,18 L4,18 Z",
                InkShapeKind.Ellipse => "M16,4 A12,7 0 1 1 15.99,4",
                InkShapeKind.Arrow => "M4,11 L26,11 M19,5 L26,11 L19,17",
                InkShapeKind.Triangle => "M16,3 L29,20 L3,20 Z",
                InkShapeKind.Diamond => "M16,2 L29,11 L16,20 L3,11 Z",
                InkShapeKind.Parallelogram => "M9,3 H29 L23,20 H3 Z",
                InkShapeKind.Pentagon => "M16,2 L29,9 L24,20 L8,20 L3,9 Z",
                InkShapeKind.Hexagon => "M9,3 H23 L29,11 L23,20 H9 L3,11 Z",
                InkShapeKind.DashedLine => "M4,17 L9,15 M13,13 L18,11 M22,9 L27,7",
                _ => "M4,17 L27,5",
            };
            return new Microsoft.UI.Xaml.Shapes.Path
            {
                Width = 30,
                Height = 22,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stroke = ResolveThemeBrush("ThemeForegroundBrush", Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B)),
                Data = LucideIcon.ParseIconGeometry(data),
            };
        }

        private static Microsoft.UI.Xaml.Shapes.Path BuildSelectionShapePreviewGlyph(
            Caelum.Controls.SelectionShape shape)
        {
            var data = shape == Caelum.Controls.SelectionShape.Rectangle
                ? "M4,4 L28,4 L28,18 L4,18 Z"
                : "M5,16 C7,7 10,19 13,10 C16,3 18,18 22,8 C23,6 25,7 27,5";
            return new Microsoft.UI.Xaml.Shapes.Path
            {
                Width = 30,
                Height = 22,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stroke = ResolveThemeBrush("ThemeForegroundBrush", Color.FromArgb(0xFF, 0x1F, 0x24, 0x2B)),
                Data = LucideIcon.ParseIconGeometry(data),
            };
        }

        /// <summary>
        /// The shared 12×8 HSV palette from WPF BuildToolPopup — grayscale
        /// row on top, hue columns × saturation/value rows below, accent-ring
        /// selection marker and Editor.Palette.Color.{row}.{col} ids.
        /// </summary>
        private static Grid BuildColorPalette(
            Windows.UI.Color initialColor, Action<Windows.UI.Color> colorChanged)
            => BuildColorPalette(initialColor, colorChanged, null, out _);

        /// <summary>
        /// Palette overload carrying the WPF shared UpdateColorMarkers
        /// surface: when <paramref name="recentRow"/> is supplied the recent
        /// swatches' focus rings move with the pick (WPF recentRow loop
        /// parity), and <paramref name="markSelected"/> exposes the combined
        /// marker so recent-swatch clicks re-mark the palette too.
        /// </summary>
        private static Grid BuildColorPalette(
            Windows.UI.Color initialColor,
            Action<Windows.UI.Color> colorChanged,
            StackPanel recentRow,
            out Action<Windows.UI.Color> markSelected)
        {
            int cols = 12;
            int rows = 8;
            double cellSize = 32;
            var paletteGrid = new Grid
            {
                Width = cols * cellSize,
                Height = rows * cellSize,
            };

            var selectionIndicator = CreateColorSelectionIndicator(cellSize);

            void UpdateColorMarkers(Windows.UI.Color selected)
            {
                if (recentRow != null)
                {
                    foreach (var element in recentRow.Children)
                    {
                        if (element is not Button swatch || swatch.Content is not Border visual)
                            continue;

                        bool isSelected = swatch.Tag is Windows.UI.Color swatchColor
                            && swatchColor.R == selected.R && swatchColor.G == selected.G
                            && swatchColor.B == selected.B;
                        visual.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                        visual.BorderBrush = ResolveThemeBrush(
                            isSelected ? "ThemeFocusBrush" : "ThemeBorderBrush",
                            isSelected ? Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)
                                       : Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6));
                    }
                }

                selectionIndicator.Visibility = Visibility.Collapsed;
                foreach (var element in paletteGrid.Children)
                {
                    if (element is Button cell && cell.Tag is Windows.UI.Color cellColor
                        && cellColor.R == selected.R && cellColor.G == selected.G && cellColor.B == selected.B)
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
                    Windows.UI.Color cellColor;
                    if (row == 0)
                    {
                        byte gray = (byte)(col * 255 / (cols - 1));
                        cellColor = Windows.UI.Color.FromArgb(255, gray, gray, gray);
                    }
                    else
                    {
                        double hue = col * 360.0 / cols;
                        double saturation = 1.0;
                        double val = 1.0;
                        if (row <= rows / 2)
                            saturation = (double)row / (rows / 2);
                        else
                            val = 1.0 - (double)(row - rows / 2) / (rows / 2);
                        cellColor = HsvToColor(hue, saturation, val);
                    }

                    var cellVisual = new Border
                    {
                        Width = cellSize - 6,
                        Height = cellSize - 6,
                        Background = new SolidColorBrush(cellColor),
                        CornerRadius = new CornerRadius(4),
                        BorderThickness = new Thickness(1),
                        BorderBrush = ResolveThemeBrush("ThemeBorderBrush", Color.FromArgb(0xFF, 0xC9, 0xCE, 0xD6)),
                    };
                    var cell = new Button
                    {
                        Width = cellSize,
                        Height = cellSize,
                        Padding = new Thickness(3),
                        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                        BorderThickness = new Thickness(0),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(col * cellSize, row * cellSize, 0, 0),
                        Content = cellVisual,
                        Tag = cellColor,
                    };
                    string cellLabel = $"#{cellColor.R:X2}{cellColor.G:X2}{cellColor.B:X2}";
                    ToolTipService.SetToolTip(cell, cellLabel);
                    AutomationProperties.SetAutomationId(cell, $"Editor.Palette.Color.{row}.{col}");
                    AutomationProperties.SetName(cell, cellLabel);
                    cell.Click += (_, __) =>
                    {
                        UpdateColorMarkers(cellColor);
                        colorChanged?.Invoke(cellColor);
                    };
                    paletteGrid.Children.Add(cell);
                }
            }

            paletteGrid.Children.Add(selectionIndicator);
            UpdateColorMarkers(initialColor);
            markSelected = UpdateColorMarkers;
            return paletteGrid;
        }

        private static Border CreateColorSelectionIndicator(double size)
        {
            var inner = new Border
            {
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                IsHitTestVisible = false,
                BorderBrush = ResolveThemeBrush("ThemeSurfaceBrush", Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            };
            return new Border
            {
                Width = size,
                Height = size,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                BorderBrush = ResolveThemeBrush("ThemeFocusBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB)),
                Child = inner,
            };
        }

        private static Windows.UI.Color HsvToColor(double h, double s, double v)
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
            return Windows.UI.Color.FromArgb(255,
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255));
        }

        /// <summary>
        /// Evicts a page's cached sidebar thumbnail so the next realization
        /// re-renders with current ink. (Ink is not composited into the
        /// thumbnail yet — that is the T9 save/render pipeline's job — but
        /// eviction is the correct invalidation seam.)
        /// </summary>
        private void InvalidateThumbnail(int pageIndex)
        {
            if (pageIndex < 0)
                return;
            _thumbnailCache.Remove(pageIndex);
            _thumbnailCacheLru.Remove(pageIndex);
            _thumbnailPagesLoading.Remove(pageIndex);
        }

        // ── Sidebar: collapse geometry (228/32 DIP contract) ────────────────

        private void SidebarCollapseButton_Click(object sender, RoutedEventArgs e)
            => SetSidebarCollapsed(!_sidebarCollapsed);

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

            UpdatePagesContainerMarginForSidebar();

            if (SidebarCollapseIcon != null)
                SidebarCollapseIcon.Kind = _sidebarCollapsed ? "PanelLeftOpen" : "PanelLeftClose";

            ApplyStateAwareSidebarMetadata();
            SetSidebarTab(_sidebarTab);
        }

        /// <summary>
        /// The 5.2.15 content-offset fix: expanded sidebar shifts the page
        /// canvas right by 228 DIP so a 184-wide rail at margin 12 never
        /// overlaps the first page column; collapsed restores the centered
        /// 32 DIP margin. Regression-critical — verified by the smoke.
        /// </summary>
        private void UpdatePagesContainerMarginForSidebar()
        {
            if (PagesContainer == null)
                return;
            PagesContainer.Margin = _sidebarCollapsed
                ? PagesContainerDefaultMargin
                : PagesContainerSidebarMargin;
#if DEBUG
            // DEBUG-only smoke seam: UIA HelpText exposes the live margin so
            // tools/winui-editor-smoke.ps1 can assert the 228/32 contract
            // without a renderer probe.
            AutomationProperties.SetHelpText(PagesContainer,
                $"pages-margin-left={PagesContainer.Margin.Left:0.##}");
#endif
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
            // A shrinking viewport can strand the ruler outside the canvas —
            // re-clamp so its centre (and grab handle) stays reachable.
            if (_rulerVisible && _rulerVisual != null)
            {
                ClampRulerCenter();
                UpdateRulerPosition();
            }
            AutoCollapseSidebarForNarrowLayout();
        }

        private void AutoCollapseSidebarForNarrowLayout()
        {
#if DEBUG
            if (_debugForceNarrowLayout && !_sidebarCollapsed)
            {
                SetSidebarCollapsed(true);
                return;
            }
#endif
            if (ActualWidth > 0 && ActualWidth <= SidebarNarrowAutoCollapseWidth && !_sidebarCollapsed)
                SetSidebarCollapsed(true);
        }

        // ── Sidebar: tabs ───────────────────────────────────────────────────

        private void SidebarPagesButton_Click(object sender, RoutedEventArgs e)
            => SetSidebarTab(SidebarTab.Pages);

        private void SidebarOutlineButton_Click(object sender, RoutedEventArgs e)
            => SetSidebarTab(SidebarTab.Outline);

        private void SidebarBookmarksButton_Click(object sender, RoutedEventArgs e)
            => SetSidebarTab(SidebarTab.Bookmarks);

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

            ApplySidebarButtonState(SidebarPagesButton, tab == SidebarTab.Pages,
                LocalizationService.Get("Editor.PagesTab"), PagesNavSelectionCue);
            ApplySidebarButtonState(SidebarOutlineButton, tab == SidebarTab.Outline,
                LocalizationService.Get("Editor.OutlineTab"), OutlineNavSelectionCue);
            ApplySidebarButtonState(SidebarBookmarksButton, tab == SidebarTab.Bookmarks,
                LocalizationService.Get("Editor.BookmarksTab"), BookmarksNavSelectionCue);

            if (tab == SidebarTab.Pages && !_sidebarCollapsed)
                UpdateThumbnailSelection(forceCenter: true);
        }

        private void ApplySidebarButtonState(Button button, bool selected, string label, Border selectionCue)
        {
            if (button == null)
                return;

            // The WPF Tag trigger becomes explicit property writes: selected
            // buttons get selection background + accent border + semibold +
            // the bottom cue bar.
            button.Background = selected
                ? ResolveThemeBrush("ThemeSelectionBrush", Color.FromArgb(0xFF, 0xDB, 0xEA, 0xFE))
                : new SolidColorBrush(Colors.Transparent);
            button.BorderBrush = selected
                ? ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB))
                : new SolidColorBrush(Colors.Transparent);
            button.BorderThickness = new Thickness(1);
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            if (selectionCue != null)
                selectionCue.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;

            AutomationProperties.SetName(button, label);
            AutomationProperties.SetHelpText(button, label);
            AutomationProperties.SetItemStatus(button, selected
                ? LocalizationService.Get("Editor.SidebarSelected")
                : string.Empty);
            ToolTipService.SetToolTip(button, label);
        }

        // ── Sidebar: pages / thumbnails ─────────────────────────────────────

        private async Task RefreshDocumentSidebarAsync(int sessionId, string filePath)
        {
            if (_isRefreshingThumbnails)
                return;

            _isRefreshingThumbnails = true;
            try
            {
                SidebarPageItems.Clear();
                for (int index = 0; index < _pageControls.Count; index++)
                {
                    if (!IsSidebarLoadCurrent(sessionId, filePath))
                        return;
                    AddSidebarPage(index);
                }
                PagesEmptyState.Visibility = _pageControls.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                UpdateThumbnailSelection(forceCenter: true);
                await Task.CompletedTask;
            }
            finally
            {
                _isRefreshingThumbnails = false;
            }
        }

        private void AddSidebarPage(int pageIndex)
        {
            var item = new SidebarPageItem(
                pageIndex,
                LocalizationService.Format("Editor.PageNumber", pageIndex + 1),
                $"Editor.Sidebar.Page.{pageIndex + 1}");
            SidebarPageItems.Add(item);
        }

        /// <summary>
        /// ListBox has no ContainerContentChanging on WinUI; the template's
        /// Image raises Loaded on realization, which is the lazy-thumbnail
        /// trigger (WPF SidebarListBoxItem_Loaded parity). The per-page
        /// AutomationId lives on the template root via x:Bind.
        /// </summary>
        private void ThumbnailImage_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.DataContext is SidebarPageItem item &&
                item.Thumbnail == null)
            {
                TryLoadThumbnail(item);
            }
        }

        private void TryLoadThumbnail(SidebarPageItem item)
        {
            if (item == null || item.Thumbnail != null || _resourcesReleased || !_isHostActive)
                return;
            if (TryGetCachedThumbnail(item.PageIndex, out var cached))
            {
                item.Thumbnail = cached;
                return;
            }
            if (!_thumbnailPagesLoading.Add(item.PageIndex))
                return;

            _ = LoadThumbnailIntoItemAsync(item);
        }

        private async Task LoadThumbnailIntoItemAsync(SidebarPageItem item)
        {
            try
            {
                using var lease = CaptureDocumentOperationLease(item, _thumbnailLoadCts.Token);
                var thumbnail = await LoadThumbnailAsync(item.PageIndex, lease.Token);
                if (thumbnail == null || !ValidateDocumentOperationLease(lease, item))
                    return;
                item.Thumbnail = thumbnail;
                AddThumbnailToCache(item.PageIndex, thumbnail);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Thumbnail] Page {item.PageIndex + 1} failed: {ex}");
            }
            finally
            {
                _thumbnailPagesLoading.Remove(item.PageIndex);
            }
        }

        private async Task<SoftwareBitmapSource> LoadThumbnailAsync(int pageIndex, CancellationToken token)
        {
            if (_pdfService == null || pageIndex < 0 || pageIndex >= _pageControls.Count)
                return null;

            // T7: the WPF ThumbnailCompositor also folds in page ink strokes;
            // the rasterizer bitmap is the shell-correct base.
            var bitmap = await _pdfService.RenderPageBgraAsync(pageIndex, ThumbnailRenderScale, token);
            if (bitmap == null || token.IsCancellationRequested)
                return null;
            return await CreateImageSourceAsync(bitmap);
        }

        private bool TryGetCachedThumbnail(int pageIndex, out SoftwareBitmapSource thumbnail)
        {
            if (_thumbnailCache.TryGetValue(pageIndex, out thumbnail))
            {
                TouchThumbnailCacheEntry(pageIndex);
                return true;
            }
            thumbnail = null;
            return false;
        }

        private void AddThumbnailToCache(int pageIndex, SoftwareBitmapSource thumbnail)
        {
            if (thumbnail == null)
                return;
            _thumbnailCache[pageIndex] = thumbnail;
            TouchThumbnailCacheEntry(pageIndex);
            while (_thumbnailCache.Count > ThumbnailCacheCapacity && _thumbnailCacheLru.Last != null)
            {
                int evict = _thumbnailCacheLru.Last.Value;
                _thumbnailCacheLru.RemoveLast();
                _thumbnailCache.Remove(evict);
            }
        }

        private void TouchThumbnailCacheEntry(int pageIndex)
        {
            var node = _thumbnailCacheLru.Find(pageIndex);
            if (node != null)
                _thumbnailCacheLru.Remove(node);
            _thumbnailCacheLru.AddFirst(pageIndex);
        }

        private void ReleaseThumbnailCache()
        {
            _thumbnailCache.Clear();
            _thumbnailCacheLru.Clear();
            _thumbnailPagesLoading.Clear();
            foreach (var item in SidebarPageItems)
                item.Thumbnail = null;
        }

        private void ThumbnailListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSynchronizingThumbnailSelection)
                return;
            if (ThumbnailListBox.SelectedItem is SidebarPageItem item && item.PageIndex >= 0)
                JumpToPage(item.PageIndex);
        }

        /// <summary>
        /// WPF <c>ThumbnailListBox_ContextMenuOpening</c> +
        /// <c>BuildThumbnailContextMenu</c> parity: right-tapping a page
        /// thumbnail offers Insert-blank-before / Duplicate / Delete. Session
        /// and path are captured at open time (WPF
        /// <c>ContextMenuOperationBinding</c> parity) so a document swap while
        /// the menu is up can't act on the replacement document; the shared
        /// structural ops re-validate the lease at every await anyway.
        /// </summary>
        private void ThumbnailListBox_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
        {
            var itemElement = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (itemElement?.DataContext is not SidebarPageItem model ||
                !SidebarPageItems.Contains(model) ||
                string.IsNullOrWhiteSpace(_currentPdfPath))
                return;

            int menuSessionId = _loadSessionId;
            string menuPath = _currentPdfPath;

            var flyout = new MenuFlyout();
            var insertItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Get("Editor.InsertBlankPageBefore"),
            };
            AutomationProperties.SetAutomationId(insertItem, "Editor.Sidebar.Page.InsertBefore");
            insertItem.Click += (_, _) => RunThumbnailMenuOperationAsync(
                model, menuSessionId, menuPath, ThumbnailMenuOperation.InsertBlankBefore);

            var duplicateItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Get("Editor.DuplicatePage"),
            };
            AutomationProperties.SetAutomationId(duplicateItem, "Editor.Sidebar.Page.Duplicate");
            duplicateItem.Click += (_, _) => RunThumbnailMenuOperationAsync(
                model, menuSessionId, menuPath, ThumbnailMenuOperation.Duplicate);

            var deleteItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Get("Editor.DeletePage"),
                // WPF parity: the destructive item keeps the danger brush.
                Foreground = ResolveThemeBrush("ThemeDangerBrush", Color.FromArgb(0xFF, 0xB4, 0x23, 0x18)),
            };
            AutomationProperties.SetAutomationId(deleteItem, "Editor.Sidebar.Page.Delete");
            deleteItem.Click += (_, _) => RunThumbnailMenuOperationAsync(
                model, menuSessionId, menuPath, ThumbnailMenuOperation.Delete);

            flyout.Items.Add(insertItem);
            flyout.Items.Add(duplicateItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(deleteItem);

            _transientFlyout = flyout;
            flyout.ShowAt(itemElement);
            e.Handled = true;
        }

        private enum ThumbnailMenuOperation
        {
            InsertBlankBefore,
            Duplicate,
            Delete,
        }

        /// <summary>
        /// Runs one thumbnail context-menu op under a session/path-bound
        /// lease (captured at menu-open; WPF
        /// <c>ThumbnailContextMenu_*_Click</c> parity). async-void — the
        /// last-resort guard is inside (no App.UnhandledException backstop).
        /// </summary>
        private async void RunThumbnailMenuOperationAsync(
            SidebarPageItem model,
            int sessionId,
            string filePath,
            ThumbnailMenuOperation operation)
        {
            try
            {
                using var operationLease = CaptureDocumentOperationLease(sessionId, filePath, _pdfService);
                if (!ValidateDocumentOperationLease(operationLease) ||
                    !SidebarPageItems.Contains(model))
                    return;

                switch (operation)
                {
                    case ThumbnailMenuOperation.InsertBlankBefore:
                        await InsertBlankPageBeforeAsync(model.PageIndex, operationLease);
                        break;
                    case ThumbnailMenuOperation.Duplicate:
                        await DuplicatePageAtAsync(model.PageIndex, operationLease);
                        break;
                    case ThumbnailMenuOperation.Delete:
                        await DeletePageAtAsync(model.PageIndex, operationLease);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ThumbnailMenu] {operation} faulted: {ex}");
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

                foreach (var item in SidebarPageItems)
                    item.IsSelected = item.PageIndex == current;

                if (indexChanged || forceCenter)
                    ScrollThumbnailItemToCenter(current);
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
        }

        // ── Sidebar: outline ────────────────────────────────────────────────

        private async Task RefreshOutlineCoreAsync(
            CancellationToken cancellationToken,
            int sessionId,
            string filePath,
            DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(
                sessionId, filePath, cancellationToken: cancellationToken);

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
                    outline = await _pdfService.GetOutlineAsync(cancellationToken);
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
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
                    // WPF parity: an outline-less document still lists every
                    // page so the rail stays useful.
                    for (int i = 0; i < _pageControls.Count; i++)
                    {
                        string label = LocalizationService.Format("Editor.PageNumber", i + 1);
                        _sidebarOutlineItems.Add(new SidebarOutlineItem(
                            i, label, $"Editor.Sidebar.Outline.Page.{i + 1}"));
                    }
                    if (OutlineEmptyState != null)
                        OutlineEmptyState.Visibility = _pageControls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    for (int index = 0; index < outline.Count; index++)
                    {
                        if (!ValidateDocumentOperationLease(operationLease))
                            return;
                        _sidebarOutlineItems.Add(BuildOutlineModel(outline[index], (index + 1).ToString()));
                    }
                    if (OutlineEmptyState != null)
                        OutlineEmptyState.Visibility = Visibility.Collapsed;
                }
                RebuildOutlineTree();
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

        private void RebuildOutlineTree()
        {
            OutlineTreeView.RootNodes.Clear();
            foreach (var item in _sidebarOutlineItems)
                OutlineTreeView.RootNodes.Add(BuildOutlineNode(item));
        }

        private static TreeViewNode BuildOutlineNode(SidebarOutlineItem item)
        {
            var node = new TreeViewNode { Content = item };
            foreach (var child in item.Children)
                node.Children.Add(BuildOutlineNode(child));
            return node;
        }

        private void OutlineTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode node &&
                node.Content is SidebarOutlineItem item &&
                item.PageIndex >= 0 &&
                IsSidebarOutlineItemCurrent(item))
            {
                JumpToPage(item.PageIndex);
            }
        }

        private void OutlineInvokeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is int pageIndex && pageIndex >= 0 &&
                pageIndex < _pageControls.Count)
            {
                JumpToPage(pageIndex);
            }
        }

        private bool IsSidebarOutlineItemCurrent(SidebarOutlineItem item)
        {
            foreach (var root in _sidebarOutlineItems)
            {
                if (ReferenceEquals(root, item) || ContainsOutlineDescendant(root, item))
                    return true;
            }
            return false;
        }

        private static bool ContainsOutlineDescendant(SidebarOutlineItem parent, SidebarOutlineItem item)
        {
            foreach (var child in parent.Children)
            {
                if (ReferenceEquals(child, item) || ContainsOutlineDescendant(child, item))
                    return true;
            }
            return false;
        }

        // ── Sidebar: bookmarks ──────────────────────────────────────────────

        private void RefreshBookmarks()
            => RefreshBookmarks(_loadSessionId, _currentPdfPath, null);

        private void RefreshBookmarks(
            int sessionId,
            string filePath,
            DocumentOperationLease operationLease = null)
        {
            if (BookmarksListBox == null || !IsSidebarLoadCurrent(sessionId, filePath) ||
                (operationLease != null && !ValidateDocumentOperationLease(operationLease)))
                return;

            SidebarBookmarkItems.Clear();
            var bookmarks = PageBookmarkService.Load(filePath ?? string.Empty);
            // Warm the scroll-path cache — UpdateBookmarkButton reads it on
            // every ViewChanged, so it must never hit the disk there.
            _bookmarksCachePath = filePath ?? string.Empty;
            _bookmarkPageIndexes = bookmarks.Select(b => b.PageIndex).ToHashSet();
            foreach (var bookmark in bookmarks)
            {
                SidebarBookmarkItems.Add(new SidebarBookmarkItem(
                    bookmark.PageIndex,
                    PageBookmarkService.GetDisplayLabel(bookmark)));
            }
            if (BookmarksEmptyState != null)
                BookmarksEmptyState.Visibility = SidebarBookmarkItems.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
            UpdateBookmarkButton();
        }

        /// <summary>
        /// Memoized bookmark page-index set for the current document path.
        /// ViewChanged calls this on every scroll frame — the underlying
        /// PageBookmarkService.Load is synchronous file I/O, so it must not
        /// run per scroll tick. RefreshBookmarks (toggle, insert, language
        /// change) and InvalidateBookmarkCache (load/rename) keep it fresh.
        /// </summary>
        private HashSet<int> GetBookmarkPageIndexes()
        {
            string path = _currentPdfPath ?? string.Empty;
            if (_bookmarksCachePath == null ||
                !string.Equals(_bookmarksCachePath, path, StringComparison.OrdinalIgnoreCase))
            {
                _bookmarksCachePath = path;
                _bookmarkPageIndexes = PageBookmarkService.Load(path)
                    .Select(b => b.PageIndex).ToHashSet();
            }
            return _bookmarkPageIndexes;
        }

        private void InvalidateBookmarkCache() => _bookmarksCachePath = null;

        private void UpdateBookmarkButton()
        {
            if (BookmarkToggleButton == null)
                return;
            int current = GetCurrentPageIndex();
            bool bookmarked = !string.IsNullOrWhiteSpace(_currentPdfPath) &&
                GetBookmarkPageIndexes().Contains(current);
            BookmarkToggleButton.IsChecked = bookmarked;
            SetBookmarkButtonContent(bookmarked);
            ApplyStateAwareSidebarMetadata();
            AutomationProperties.SetAutomationId(BookmarkToggleButton, "Editor.Sidebar.BookmarkToggle");
        }

        private void SetBookmarkButtonContent(bool bookmarked)
        {
            var accent = ResolveThemeBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB));
            var icon = new LucideIcon
            {
                Kind = "Bookmark",
                Width = 15,
                Height = 15,
                Fill = bookmarked ? accent : new SolidColorBrush(Colors.Transparent),
                Stroke = accent,
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new TextBlock
            {
                Text = bookmarked
                    ? LocalizationService.Get("Editor.UnbookmarkCurrentPage")
                    : LocalizationService.Get("Editor.BookmarkCurrentPage"),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ResolveThemeBrush("ThemeForegroundBrush", Color.FromArgb(0xFF, 0x1F, 0x29, 0x37))
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(label);
            BookmarkToggleButton.Content = content;
        }

        private void BookmarkToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath) || _pageControls.Count == 0)
                return;
            PageBookmarkService.Toggle(_currentPdfPath, GetCurrentPageIndex());
            RefreshBookmarks();
        }

        private void BookmarksListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BookmarksListBox.SelectedItem is SidebarBookmarkItem item &&
                SidebarBookmarkItems.Contains(item))
            {
                JumpToPage(item.PageIndex);
            }
        }

        private void BookmarksListBox_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
        {
            // Locate the bookmark row under the pointer; the remove command is
            // the WPF per-item context menu.
            var itemElement = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (itemElement?.DataContext is not SidebarBookmarkItem model ||
                !SidebarBookmarkItems.Contains(model))
                return;

            var flyout = new MenuFlyout();
            var removeItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Get("Editor.RemoveBookmark"),
                Tag = model
            };
            AutomationProperties.SetAutomationId(removeItem, "Editor.Sidebar.Bookmark.Remove");
            removeItem.Click += BookmarkContextMenu_Remove_Click;
            flyout.Items.Add(removeItem);
            _transientFlyout = flyout;
            flyout.ShowAt(itemElement);
            e.Handled = true;
        }

        private void BookmarkContextMenu_Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not SidebarBookmarkItem model ||
                string.IsNullOrWhiteSpace(_currentPdfPath))
                return;

            string filePath = _currentPdfPath;
            PageBookmarkService.Toggle(filePath, model.PageIndex);
            RefreshBookmarks(_loadSessionId, filePath, null);
        }

        // ── Search ──────────────────────────────────────────────────────────

        /// <summary>
        /// Window-level Ctrl+F forward: MainWindow's root PreviewKeyDown calls
        /// this when the active tab hosts this editor.
        /// </summary>
        public void OpenSearchPanel() => OpenPdfSearch();

        private void OpenPdfSearch()
        {
            PdfSearchPanel.Visibility = Visibility.Visible;
            PdfSearchTextBox.Focus(FocusState.Programmatic);
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
                    PdfSearchResultsListBox.Items.Add(new ListViewItem { Content = result.DisplayText, Tag = result });
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
            if (PdfSearchResultsListBox.SelectedItem is ListViewItem item && item.Tag is PdfSearchResult result)
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
                // Task 8 Phase B: text-bound highlight rectangles paint on
                // the selection canvas — visual only, never persisted (WPF
                // search-jump parity).
                page.SetPdfTextSelectionRects(BuildPdfTextSelectionRects(info, result.StartOffset, result.StartOffset + result.Length - 1));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
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

        /// <summary>
        /// Converts a character-offset range into merged selection
        /// rectangles on PdfTextSelectionCanvas — the search-jump path and
        /// the interactive drag both paint through this (WPF
        /// BuildPdfTextSelectionRects, Core PdfTextSelectionGeometry).
        /// </summary>
        private static IReadOnlyList<Rect> BuildPdfTextSelectionRects(
            PdfService.PdfPageTextInfo info, int startOffset, int endOffset)
            => PdfTextSelectionGeometry.BuildSelectionRects(info, startOffset, endOffset)
                .Select(r => new Rect(r.X, r.Y, r.Width, r.Height))
                .ToList();

        /// <summary>
        /// SelectionChanged fires synchronously on SelectedIndex and performs
        /// the jump itself — an explicit second jump here navigated twice.
        /// </summary>
        private void MovePdfSearchSelection(bool backwards)
        {
            using var operationLease = CaptureDocumentOperationLease();
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            if (PdfSearchPanel.Visibility != Visibility.Visible || _pdfSearchResults.Count == 0)
                return;
            int current = PdfSearchResultsListBox.SelectedIndex;
            int next = (current + (backwards ? -1 : 1) + _pdfSearchResults.Count) % _pdfSearchResults.Count;
            PdfSearchResultsListBox.SelectedIndex = next;
        }

        private void PdfSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                    .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);
                MovePdfSearchSelection(shift);
            }
            else if (e.Key == VirtualKey.Escape)
            {
                ClosePdfSearch();
                e.Handled = true;
            }
        }

        private void ClosePdfSearchButton_Click(object sender, RoutedEventArgs e) => ClosePdfSearch();

        // ── Page context menu (MenuFlyout on the scroll surface) ────────────

        private void BuildPageContextMenu()
        {
            _pageContextMenu = new MenuFlyout();

            PrintMenuItem = new MenuFlyoutItem
            {
                Icon = new PathIcon { Data = LucideIcon.GetIconGeometry("Printer") },
                KeyboardAcceleratorTextOverride = "Ctrl+P",
            };
            AutomationProperties.SetAutomationId(PrintMenuItem, "Editor.ContextMenu.Print");
            PrintMenuItem.Click += PrintMenuItem_Click;
            _pageContextMenu.Items.Add(PrintMenuItem);

            _pageContextMenu.Items.Add(new MenuFlyoutSeparator());

            ExportCurrentPagePng1xMenuItem = AddMenuItem("Editor.ContextMenu.ExportCurrentPagePng1x", ExportCurrentPagePng1x_Click);
            ExportCurrentPagePng2xMenuItem = AddMenuItem("Editor.ContextMenu.ExportCurrentPagePng2x", ExportCurrentPagePng2x_Click);
            ExportAllPagesPng1xMenuItem = AddMenuItem("Editor.ContextMenu.ExportAllPagesPng1x", ExportAllPagesPng1x_Click);
            ExportAllPagesPng2xMenuItem = AddMenuItem("Editor.ContextMenu.ExportAllPagesPng2x", ExportAllPagesPng2x_Click);

            _pageContextMenu.Items.Add(new MenuFlyoutSeparator());

            InsertPdfPageMenuItem = AddMenuItem("Editor.ContextMenu.InsertPdfPage", InsertPdfPages_Click);
            InsertImagePageMenuItem = AddMenuItem("Editor.ContextMenu.InsertImagePage", InsertImagePage_Click);
            RotateCurrentPageMenuItem = AddMenuItem("Editor.ContextMenu.RotateCurrentPage", RotateCurrentPage_Click);

            PdfScrollViewer.ContextFlyout = _pageContextMenu;
        }

        private MenuFlyoutItem AddMenuItem(string automationId, RoutedEventHandler onClick)
        {
            var item = new MenuFlyoutItem();
            AutomationProperties.SetAutomationId(item, automationId);
            item.Click += onClick;
            _pageContextMenu.Items.Add(item);
            return item;
        }

        private async void ExportCurrentPagePng1x_Click(object sender, RoutedEventArgs e)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            await ExportPngAsync(false, 1.0, operationLease);
        }

        private async void ExportCurrentPagePng2x_Click(object sender, RoutedEventArgs e)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            await ExportPngAsync(false, 2.0, operationLease);
        }

        private async void ExportAllPagesPng1x_Click(object sender, RoutedEventArgs e)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            await ExportPngAsync(true, 1.0, operationLease);
        }

        private async void ExportAllPagesPng2x_Click(object sender, RoutedEventArgs e)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            await ExportPngAsync(true, 2.0, operationLease);
        }

        /// <summary>
        /// PNG export through the rasterizer + <see cref="BitmapEncoder"/> —
        /// the WinUI stand-in for WPF's BuildPrintablePagesAsync +
        /// PngBitmapEncoder path.
        /// </summary>
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

            var hwnd = GetWindowHandle();

            try
            {
                string folder = null;
                string singlePath = null;
                string baseName = Path.GetFileNameWithoutExtension(_currentPdfPath);
                if (allPages)
                {
                    var folderPicker = new FolderPicker
                    {
                        SuggestedStartLocation = PickerLocationId.DocumentsLibrary
                    };
                    WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
                    var pickedFolder = await folderPicker.PickSingleFolderAsync();
                    if (pickedFolder == null)
                        return;
                    folder = pickedFolder.Path;
                }
                else
                {
                    var picker = new FileSavePicker
                    {
                        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                        SuggestedFileName = $"{baseName}_page_{GetCurrentPageIndex() + 1}"
                    };
                    picker.FileTypeChoices.Add(
                        LocalizationService.Get("Editor.PngFileFilter").Split('|')[0],
                        new List<string> { ".png" });
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                    var file = await picker.PickSaveFileAsync();
                    if (file == null)
                        return;
                    singlePath = file.Path;
                    folder = Path.GetDirectoryName(singlePath);
                }

                if (!ValidateDocumentOperationLease(operationLease))
                    return;

                IEnumerable<int> indexes = allPages
                    ? Enumerable.Range(0, _pageControls.Count)
                    : new[] { Math.Max(0, Math.Min(GetCurrentPageIndex(), _pageControls.Count - 1)) };

                int exported = 0;
                foreach (int index in indexes)
                {
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    string outputPath = allPages
                        ? Path.Combine(folder, $"{baseName}_page_{index + 1:000}.png")
                        : singlePath;

                    var bitmap = await _pdfService.RenderPageBgraAsync(index, dpiScale, operationLease.Token);
                    if (bitmap == null || !ValidateDocumentOperationLease(operationLease))
                        return;
                    await SavePngAsync(bitmap, outputPath);
                    exported++;
                }

                if (ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.PngExported", exported,
                            dpiScale.ToString("0.#", LocalizationService.CurrentCulture)),
                        "", 2500);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.PngExportFailed", ex.Message), "", 3500);
            }
            finally
            {
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private static async Task SavePngAsync(PdfPageBitmap bitmap, string outputPath)
        {
            var softwareBitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Premultiplied);
            softwareBitmap.CopyFromBuffer(bitmap.Bgra.AsBuffer());
            using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var encoder = await BitmapEncoder.CreateAsync(
                    BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
                encoder.SetSoftwareBitmap(softwareBitmap);
                await encoder.FlushAsync();
            }
        }

        // ── Print pipeline (WPF PrintPdfAsync — Win32 GDI backend) ─────────

        /// <summary>
        /// Page-context-menu Print (WPF ContextMenu_PrintClick parity). The
        /// WinUI MenuFlyout has no open-time lease binding, so — like the
        /// export/insert handlers — it captures a fresh lease at click time.
        /// </summary>
        private async void PrintMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var operationLease = CaptureDocumentOperationLease(_pdfService);
                await PrintPdfAsync(operationLease);
            }
            catch (Exception ex)
            {
                // PrintPdfAsync owns the PrintFailed UX for its own failures;
                // this only catches plumbing faults — an async-void handler
                // must never escape an exception.
                System.Diagnostics.Debug.WriteLine($"[Print] Menu handler failed: {ex}");
            }
        }

        /// <summary>
        /// WPF PrintPdfAsync parity: lease capture/validation → print sheet
        /// → PreparingPrint overlay → annotation-baked temp PDF rasterized at
        /// printer DPI → spool → PrintSent toast, PrintFailed dialog, OCE
        /// swallow. The WinRT PrintManager task pipeline needs packaged
        /// CoreWindow plumbing, so the print sheet is the classic Win32
        /// PrintDlgEx on the MainWindow HWND and pages spool through GDI
        /// (<see cref="Win32Print"/>) — the same underlying stack WPF's
        /// PrintDialog sits on. Read-only against the document: an operation
        /// lease, not the structural latch, is the concurrency boundary.
        /// </summary>
        private async Task PrintPdfAsync(DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(_pdfService);
            if (string.IsNullOrWhiteSpace(_currentPdfPath))
            {
                if (ownsLease)
                {
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                    operationLease.Dispose();
                }
                return;
            }

            string filePath = _currentPdfPath;
            if (!ValidateDocumentOperationLease(operationLease))
            {
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }

            Win32PrintJob printJob;
            try
            {
                printJob = Win32Print.TryShowPrintDialog(
                    GetWindowHandle(), Math.Max(1, _pdfService.PageCount));
            }
            catch (Exception ex)
            {
                // PrintDlgEx surfaces "no default printer" etc. as HRESULTs —
                // same PrintFailed UX as a mid-spool failure.
                if (ValidateDocumentOperationLease(operationLease))
                    await ShowPrintFailureAsync(ex);
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }

            if (printJob == null || !ValidateDocumentOperationLease(operationLease))
            {
                printJob?.Dispose();
                if (ownsLease)
                    operationLease.Dispose();
                return;
            }

            string originalLoadingText = LoadingText.Text;
            LoadingText.Text = LocalizationService.Get("Editor.PreparingPrint");
            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                var pages = await BuildPrintablePagesAsync(
                    includeAnnotations: true,
                    printerDpi: printJob.PrinterDpi,
                    pageIndexes: printJob.OrderedPageIndexes(),
                    operationLease: operationLease,
                    filePath: filePath);
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                if (pages.Count == 0)
                    throw new InvalidOperationException(
                        LocalizationService.Get("Editor.NoPagesToPrint"));

                // Spool off the UI thread — StartDoc/StretchDIBits block while
                // the driver consumes each page.
                await Task.Run(() => Win32Print.PrintPages(
                    printJob, pages, Path.GetFileName(filePath), operationLease.Token));
                if (ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Get("Editor.PrintSent"), "\uE749", 1500);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                await ShowPrintFailureAsync(ex);
            }
            finally
            {
                if (ValidateDocumentOperationLease(operationLease))
                {
                    LoadingText.Text = originalLoadingText;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
                printJob?.Dispose();
                if (ownsLease)
                    operationLease.Dispose();
            }
        }

        private async Task ShowPrintFailureAsync(Exception ex)
        {
            string message = LocalizationService.Format("Editor.PrintFailed", ex.Message);
            if (XamlRoot != null)
            {
                await WinUiDialogService.ShowErrorAsync(
                    XamlRoot, LocalizationService.Get("Common.Error"), message);
            }
            else
            {
                GetMainWindow()?.ShowToast(message, "\uE783", 3500);
            }
        }

        /// <summary>
        /// WPF BuildPrintablePagesAsync parity: atomically copies the live
        /// PDF into %TEMP%\Caelum\Print, bakes the in-memory annotation set
        /// in via PdfService.SaveAnnotationsToPdfAsync, then rasterizes the
        /// selected pages off the UI thread. The temp copy keeps the printed
        /// bytes stable against a mid-print autosave; it is always deleted
        /// in the finally.
        /// </summary>
        private async Task<IReadOnlyList<PrintablePageImage>> BuildPrintablePagesAsync(
            bool includeAnnotations,
            int printerDpi,
            IReadOnlyList<int> pageIndexes,
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
                    string tempDirectory = Path.Combine(
                        Path.GetTempPath(), "Caelum", "Print");
                    Directory.CreateDirectory(tempDirectory);
                    tempPrintPath = Path.Combine(
                        tempDirectory, $"{Guid.NewGuid():N}.pdf");
                    PdfAtomicFile.CopyFile(filePath, tempPrintPath);
                    await _pdfService.SaveAnnotationsToPdfAsync(
                        tempPrintPath, CollectAnnotations());
                    if (!ValidateDocumentOperationLease(operationLease))
                        return Array.Empty<PrintablePageImage>();
                    renderPath = tempPrintPath;
                }

                var pages = await Task.Run(() => RenderPrintablePages(
                    renderPath, includeAnnotations, printerDpi, pageIndexes));
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

        /// <summary>
        /// WPF RenderPrintablePages parity — pdfium BGRA rasterization of the
        /// annotation-baked print source at the resolved print DPI. The WPF
        /// twin wrapped each buffer in a frozen BitmapSource; the GDI spool
        /// path consumes the raw BGRA (a <see cref="PrintablePageImage"/>).
        /// </summary>
        private static IReadOnlyList<PrintablePageImage> RenderPrintablePages(
            string filePath,
            bool includeAnnotations,
            int printerDpi,
            IReadOnlyList<int> pageIndexes)
        {
            using var rasterizer = PdfiumRasterizerFactory.Shared.LoadFromFile(filePath);
            var sizes = rasterizer.PageSizes;

            // The DPI resolver needs the total selected-page area in points.
            double totalAreaPoints = 0;
            foreach (int index in pageIndexes)
            {
                if (index >= 0 && index < sizes.Count)
                {
                    var s = sizes[index];
                    totalAreaPoints += (double)s.Width * s.Height;
                }
            }
            int renderDpi = PrintPageGeometry.ResolvePrintRenderDpi(
                printerDpi, totalAreaPoints);

            var pages = new List<PrintablePageImage>(pageIndexes.Count);
            foreach (int index in pageIndexes)
            {
                if (index < 0 || index >= rasterizer.PageCount)
                    continue;
                var pageSize = sizes[index];
                int width = Math.Max(1, (int)Math.Ceiling(pageSize.Width * renderDpi / 72.0));
                int height = Math.Max(1, (int)Math.Ceiling(pageSize.Height * renderDpi / 72.0));

                // FFLDraw annotation pass — WPF's PdfRenderFlags.Annotations
                // equivalent (includeAnnotations == true).
                var rendered = rasterizer.RenderPageBgra(index, width, height, includeAnnotations);
                pages.Add(new PrintablePageImage
                {
                    PageIndex = index,
                    Bgra = rendered.Bgra,
                    PixelWidth = rendered.Width,
                    PixelHeight = rendered.Height,
                    Stride = rendered.Stride,
                    WidthPoints = pageSize.Width,
                    HeightPoints = pageSize.Height,
                });
            }

            return pages;
        }

        // ── Insert pages / rotate ───────────────────────────────────────────

        private async void InsertPdfPages_Click(object sender, RoutedEventArgs e)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease) ||
                string.IsNullOrWhiteSpace(_currentPdfPath))
                return;
            string filePath = _currentPdfPath;

            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".pdf");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, GetWindowHandle());
            var file = await picker.PickSingleFileAsync();
            if (file == null || !ValidateDocumentOperationLease(operationLease))
                return;

            int sourcePageCount;
            try
            {
                using var source = PdfiumRasterizerFactory.Shared.LoadFromFile(file.Path);
                sourcePageCount = source.PageCount;
            }
            catch (Exception ex)
            {
                if (ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.SourcePdfReadFailed", ex.Message), "", 3500);
                return;
            }

            var range = await TryPromptPageRangeAsync(sourcePageCount);
            if (range == null || !ValidateDocumentOperationLease(operationLease))
                return;

            int insertPageIndex = Math.Max(0, GetCurrentPageIndex());
            await InsertExternalDocumentAsync(
                () => _pdfService.InsertPdfPagesAsync(
                    filePath, file.Path, insertPageIndex, range.Value.Start, range.Value.End),
                insertPageIndex,
                range.Value.End - range.Value.Start + 1,
                LocalizationService.Get("Editor.PdfPagesInserted"),
                operationLease);
        }

        private async void InsertImagePage_Click(object sender, RoutedEventArgs e)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease) ||
                string.IsNullOrWhiteSpace(_currentPdfPath))
                return;
            string filePath = _currentPdfPath;

            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp" })
                picker.FileTypeFilter.Add(ext);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, GetWindowHandle());
            var file = await picker.PickSingleFileAsync();
            if (file == null || !ValidateDocumentOperationLease(operationLease))
                return;

            int insertPageIndex = Math.Max(0, GetCurrentPageIndex());
            await InsertExternalDocumentAsync(
                () => _pdfService.InsertImagePageAsync(filePath, file.Path, insertPageIndex),
                insertPageIndex,
                1,
                LocalizationService.Get("Editor.ImagePageInserted"),
                operationLease);
        }

        /// <summary>
        /// Page-range prompt (WPF TryPromptPageRange). Runs under the shared
        /// dialog gate so it can never overlap another ContentDialog.
        /// </summary>
        private async Task<(int Start, int End)?> TryPromptPageRangeAsync(int pageCount)
        {
            if (XamlRoot == null)
                return null;

            var input = new TextBox
            {
                Text = pageCount > 0 ? $"1-{pageCount}" : "1",
                Margin = new Thickness(0, 10, 0, 0)
            };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Format("Editor.PageRangePrompt", pageCount),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(input);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = LocalizationService.Get("Editor.PageRangeTitle"),
                Content = panel,
                PrimaryButtonText = LocalizationService.Get("Common.OK"),
                CloseButtonText = LocalizationService.Get("Common.Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await WinUiDialogService.RunUnderDialogGateAsync(() => dialog.ShowAsync().AsTask());
            if (result != ContentDialogResult.Primary)
                return null;

            var parts = input.Text.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || !int.TryParse(parts[0], out int first))
                return null;
            int last = parts.Length > 1 && int.TryParse(parts[1], out int parsedLast) ? parsedLast : first;
            if (first < 1 || last < first || last > pageCount)
                return null;
            return (first - 1, last - 1);
        }

        /// <summary>
        /// Shared insert-document boundary (WPF InsertExternalDocumentAsync):
        /// flush a dirty document first, snapshot the before/after PDF bytes
        /// and the persisted bookmark list, run the Core op, reload under a
        /// refreshed session lease, refocus, and push a DocumentSnapshotAction
        /// so the structural edit participates in undo/redo. A mid-operation
        /// failure rolls the file bytes and bookmark sidecar back before the
        /// failure toast (WPF parity).
        /// </summary>
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
            // T9: a structural document op is a mutation — hold the edit
            // admission lease so close/navigation quiescence covers it.
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using (editLease)
            {
                // Structural latch: imports share the byte-snapshot/reload
                // pipeline with page insert/delete — never overlap them.
                using var structuralScope = BeginStructuralOperation();
                if (structuralScope == null)
                    return;

                if (string.IsNullOrWhiteSpace(_currentPdfPath))
                    return;

                string filePath = _currentPdfPath;
                DocumentOperationLease currentLease = operationLease ?? CaptureDocumentOperationLease(_pdfService);
                try
                {
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    // A dirty document must hit disk BEFORE the binary PDF is
                    // rewritten — otherwise the insert bakes a stale base and
                    // the pending annotations are lost (WPF parity).
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
                    {
                        // Post-mutation reload failure — the file bytes moved
                        // but the loaded document didn't. TryRollback re-leases
                        // when the failed reload retired the lease; a swapped
                        // document refuses silently (WPF parity), otherwise the
                        // failure is surfaced instead of silently returning.
                        var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                            filePath, before, beforeBookmarks, focusBefore,
                            currentLease, "Import");
                        if (!stillOurs)
                            return;
                        currentLease = rolledBack;
                        GetMainWindow()?.ShowToast(
                            LocalizationService.Format(
                                "Editor.ImportFailed",
                                LocalizationService.Get("Editor.DocumentReloadFailed")),
                            "\uE783", 3500);
                        return;
                    }
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
                    GetMainWindow()?.ShowToast(successMessage, "", 2000);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    // A stale import must not roll back or report against the
                    // replacement document. Only the still-live transaction
                    // may restore its before-bytes and sidecar — TryRollback
                    // re-leases when the failed reload retired currentLease.
                    if (operationMayHaveChangedDocument && before != null)
                    {
                        var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                            filePath, before, beforeBookmarks, focusBefore,
                            currentLease, "Import");
                        if (!stillOurs)
                            return;
                        currentLease = rolledBack;
                    }
                    else if (!ValidateDocumentOperationLease(currentLease))
                    {
                        return;
                    }

                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.ImportFailed", ex.Message), "", 3500);
                }
                finally
                {
                    currentLease?.Dispose();
                }
            }
        }

        private async void RotateCurrentPage_Click(object sender, RoutedEventArgs e)
        {
            DocumentOperationLease operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
            {
                operationLease.Dispose();
                return;
            }

            // T9: hold the admission lease across the rewrite + flush a
            // dirty document first, or the rotated PDF bakes a stale base
            // and pending annotations are lost (WPF parity).
            if (!TryBeginDocumentEdit(out var editLease))
            {
                operationLease.Dispose();
                return;
            }

            using (operationLease)
            using (editLease)
            {
                // Structural latch: rapid toolbar clicks could otherwise run
                // two rotate rewrites against the same before-bytes.
                using var structuralScope = BeginStructuralOperation();
                if (structuralScope == null)
                    return;

                if (string.IsNullOrWhiteSpace(_currentPdfPath) || _pageControls.Count == 0)
                    return;

                string filePath = _currentPdfPath;
                byte[] before = null;
                int pageIndex = 0;
                // Rotation doesn't move bookmarks, but the shared rollback
                // helper restores the persisted sidecar — snapshot it so the
                // restore is a byte-identical no-op, not a wipe.
                List<PageBookmark> beforeBookmarks = null;
                bool operationMayHaveChangedDocument = false;
                try
                {
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    if (_documentSaveCoordinator.IsDirty &&
                        (!await AutoSaveAsync(operationLease) || !ValidateDocumentOperationLease(operationLease)))
                        return;
                    pageIndex = GetCurrentPageIndex();
                    before = await File.ReadAllBytesAsync(filePath, operationLease.Token);
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    beforeBookmarks = PageBookmarkService.Load(filePath).ToList();
                    operationMayHaveChangedDocument = true;
                    await _pdfService.RotatePageAsync(filePath, pageIndex, 1);
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    byte[] after = await File.ReadAllBytesAsync(filePath, operationLease.Token);
                    if (!ValidateDocumentOperationLease(operationLease))
                        return;
                    var refreshedLease = await ReloadDocumentForOperationAsync(filePath, operationLease);
                    if (refreshedLease == null)
                    {
                        // Post-mutation reload failure — restore the before
                        // bytes + sidecar (the failed reload retired
                        // operationLease; TryRollback re-leases while the
                        // session still owns this path, refuses on swap).
                        var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                            filePath, before, beforeBookmarks, pageIndex,
                            operationLease, "RotatePage");
                        if (!stillOurs)
                            return;
                        rolledBack?.Dispose();
                        GetMainWindow()?.ShowToast(
                            LocalizationService.Format(
                                "Editor.RotateFailed",
                                LocalizationService.Get("Editor.DocumentReloadFailed")),
                            "\uE783", 3500);
                        return;
                    }
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
                    if (operationMayHaveChangedDocument && before != null)
                    {
                        var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                            filePath, before, beforeBookmarks, pageIndex,
                            operationLease, "RotatePage");
                        if (!stillOurs)
                            return;
                        rolledBack?.Dispose();
                    }
                    else if (!ValidateDocumentOperationLease(operationLease))
                    {
                        return;
                    }

                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.RotateFailed", ex.Message), "\uE783", 3500);
                }
            }
        }

        // ── Page structure: template insert / delete / version history ────

        /// <summary>
        /// WPF <c>InsertPageAtAsync</c> parity — template pick through
        /// <see cref="PageTemplatePickerDialog"/> behind the shared dialog
        /// gate (replacing the WPF borderless <c>PageTemplatePickerWindow</c>),
        /// then <see cref="InsertPageCoreAsync"/> runs the lease-guarded
        /// insert sequence. A cancelled pick returns before any document
        /// state moves. The lease is required (every call site captures one
        /// before invoking) — the earlier <c>= null</c> "capture + toast"
        /// fallback was dead code: no caller omitted it, and its side-paths
        /// hid the lease-lifetime rules.
        /// </summary>
        private async Task InsertPageAtAsync(
            int insertIndex,
            DocumentOperationLease operationLease)
        {
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using (editLease)
            {
                // Structural latch — the picker sits inside it so a second
                // structural gesture can't interleave its snapshots while
                // the user is still choosing a template.
                using var structuralScope = BeginStructuralOperation();
                if (structuralScope == null)
                    return;

                if (string.IsNullOrWhiteSpace(_currentPdfPath))
                {
                    GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                    return;
                }

                var xamlRoot = XamlRoot ?? GetMainWindow()?.Content?.XamlRoot;
                if (xamlRoot == null)
                    return;

                var picker = new PageTemplatePickerDialog { XamlRoot = xamlRoot };
                await WinUiDialogService.RunUnderDialogGateAsync(
                    () => picker.ShowAsync().AsTask());
                if (!picker.IsConfirmed)
                    return;

                await InsertPageCoreAsync(insertIndex, picker.SelectedTemplate, operationLease);
            }
        }

        /// <summary>
        /// Thumbnail context-menu "Insert blank page before" — the
        /// label-accurate direct blank insert. (WPF routed this item through
        /// <c>InsertPageAtAsync</c>'s template picker despite the label; the
        /// picker stays reachable via the insert-gap "+" affordances, so the
        /// menu item does what it says.) Holds the same admission +
        /// structural latches, then shares <see cref="InsertPageCoreAsync"/>.
        /// </summary>
        private async Task InsertBlankPageBeforeAsync(
            int pageIndex,
            DocumentOperationLease operationLease)
        {
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using (editLease)
            {
                using var structuralScope = BeginStructuralOperation();
                if (structuralScope == null)
                    return;

                if (string.IsNullOrWhiteSpace(_currentPdfPath))
                {
                    GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                    return;
                }

                await InsertPageCoreAsync(pageIndex, PageInsertTemplate.Blank, operationLease);
            }
        }

        /// <summary>
        /// Shared insert sequence for <see cref="InsertPageAtAsync"/> (picked
        /// template) and <see cref="InsertBlankPageBeforeAsync"/> (blank).
        /// Callers hold the admission + structural latches; the sequence is
        /// lease-guarded dirty flush → before-snapshot →
        /// <c>InsertPageAsync</c> → reload under a refreshed session lease →
        /// jump + recent-files metadata + bookmark remap → undo snapshot →
        /// toast. A mid-operation failure rolls the file bytes + bookmark
        /// sidecar back before the error surface (import-op rollback parity).
        /// </summary>
        private async Task InsertPageCoreAsync(
            int insertIndex,
            PageInsertTemplate template,
            DocumentOperationLease operationLease)
        {
            string filePath = _currentPdfPath;
            DocumentOperationLease currentLease = operationLease;
            byte[] beforeBytes = null;
            int undoFocusIndex = 0;
            List<PageBookmark> beforeBookmarks = null;
            bool operationMayHaveChangedDocument = false;
            try
            {
                if (!ValidateDocumentOperationLease(currentLease))
                    return;
                // A dirty document must hit disk BEFORE the binary PDF is
                // rewritten — otherwise the insert bakes a stale base and
                // the pending annotations are lost (WPF parity).
                if (_documentSaveCoordinator.IsDirty &&
                    (!await AutoSaveAsync(currentLease) ||
                        !ValidateDocumentOperationLease(currentLease)))
                    return;

                beforeBytes = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                if (!ValidateDocumentOperationLease(currentLease))
                    return;
                undoFocusIndex = Math.Max(0,
                    Math.Min(insertIndex, Math.Max(_pageControls.Count - 1, 0)));
                beforeBookmarks = PageBookmarkService.Load(filePath).ToList();

                operationMayHaveChangedDocument = true;
                await _pdfService.InsertPageAsync(filePath, insertIndex, template);
                if (!ValidateDocumentOperationLease(currentLease))
                    return;

                byte[] afterBytes = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                if (!ValidateDocumentOperationLease(currentLease))
                    return;
                currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                if (currentLease == null)
                {
                    // Post-mutation reload failure — the file bytes moved but
                    // the loaded document didn't. Restore via TryRollback
                    // (re-leases when the failed reload retired currentLease);
                    // a swapped document refuses silently, otherwise report.
                    var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                        filePath, beforeBytes, beforeBookmarks, undoFocusIndex,
                        currentLease, "InsertPage");
                    if (!stillOurs)
                        return;
                    currentLease = rolledBack;
                    await WinUiDialogService.ShowErrorAsync(
                        XamlRoot ?? GetMainWindow()?.Content?.XamlRoot,
                        LocalizationService.Get("Common.Error"),
                        LocalizationService.Format(
                            "Editor.AddPageFailed",
                            LocalizationService.Get("Editor.DocumentReloadFailed")));
                    return;
                }

                int insertedPageIndex = Math.Max(0, Math.Min(insertIndex, _pageControls.Count - 1));
                if (!ValidateDocumentOperationLease(currentLease))
                    return;
                JumpToPage(insertedPageIndex);
                RecentFilesService.UpdateMetadata(
                    filePath, _pageControls.Count, File.GetLastWriteTimeUtc(filePath));
                var afterBookmarks = PageBookmarkService
                    .ApplyPageInsert(filePath, insertedPageIndex).ToList();
                RefreshBookmarks(_loadSessionId, filePath, currentLease);
                if (!ValidateDocumentOperationLease(currentLease))
                    return;
                PushUndoAction(new DocumentSnapshotAction(
                    this, beforeBytes, afterBytes,
                    undoFocusIndex, insertedPageIndex,
                    beforeBookmarks, afterBookmarks));
                if (!ValidateDocumentOperationLease(currentLease))
                    return;
                GetMainWindow()?.ShowToast(
                    LocalizationService.Get("Editor.PageAdded"), "\uE710");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (operationMayHaveChangedDocument && beforeBytes != null)
                {
                    // The post-op read/reload faulted — restore the before
                    // bytes + bookmark sidecar so the file and the loaded
                    // document agree again (WPF import-op rollback parity).
                    // A failed reload retires currentLease, so TryRollback
                    // re-leases when the session still owns the path; a
                    // swapped document refuses silently (stale → no report).
                    var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                        filePath, beforeBytes, beforeBookmarks, undoFocusIndex,
                        currentLease, "InsertPage");
                    if (!stillOurs)
                        return;
                    currentLease = rolledBack;
                }
                else if (!ValidateDocumentOperationLease(currentLease))
                {
                    return;
                }
                await WinUiDialogService.ShowErrorAsync(
                    XamlRoot ?? GetMainWindow()?.Content?.XamlRoot,
                    LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Editor.AddPageFailed", ex.Message));
            }
            finally
            {
                // Method-owned: ReloadDocumentForOperationAsync retires the
                // incoming lease and publishes a fresh one — disposing here
                // covers both (lease Dispose is idempotent, so a caller's
                // own `using` on the passed lease stays safe).
                currentLease?.Dispose();
            }
        }

        /// <summary>
        /// WPF <c>DeletePageAtAsync</c> parity (invoked by the hover-only page
        /// delete button and the thumbnail context menu): admission lease →
        /// structural latch → dirty flush → byte snapshot →
        /// <c>DeletePageAsync</c> → reload → focus + metadata + bookmark
        /// remap → undo snapshot → toast. Single-page documents are blocked
        /// before any state moves; an <see cref="InvalidOperationException"/>
        /// from the Core service maps to the same blocked toast. A
        /// mid-operation failure rolls the file bytes + bookmark sidecar
        /// back before the error surface (import-op rollback parity).
        /// </summary>
        private async Task DeletePageAtAsync(
            int pageIndex,
            DocumentOperationLease operationLease)
        {
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using (editLease)
            {
                using var structuralScope = BeginStructuralOperation();
                if (structuralScope == null)
                    return;

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

                string filePath = _currentPdfPath;
                DocumentOperationLease currentLease = operationLease;
                byte[] beforeBytes = null;
                List<PageBookmark> beforeBookmarks = null;
                bool operationMayHaveChangedDocument = false;
                try
                {
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    if (_documentSaveCoordinator.IsDirty &&
                        (!await AutoSaveAsync(currentLease) ||
                            !ValidateDocumentOperationLease(currentLease)))
                        return;

                    beforeBytes = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    beforeBookmarks = PageBookmarkService.Load(filePath).ToList();
                    operationMayHaveChangedDocument = true;
                    await _pdfService.DeletePageAsync(filePath, pageIndex);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;

                    byte[] afterBytes = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                    if (currentLease == null)
                    {
                        // Post-mutation reload failure — restore the before
                        // bytes + sidecar via TryRollback (re-leases when the
                        // failed reload retired currentLease); swapped →
                        // silent, otherwise surface instead of returning.
                        var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                            filePath, beforeBytes, beforeBookmarks, pageIndex,
                            currentLease, "DeletePage");
                        if (!stillOurs)
                            return;
                        currentLease = rolledBack;
                        await WinUiDialogService.ShowErrorAsync(
                            XamlRoot ?? GetMainWindow()?.Content?.XamlRoot,
                            LocalizationService.Get("Common.Error"),
                            LocalizationService.Format(
                                "Editor.DeletePageFailed",
                                LocalizationService.Get("Editor.DocumentReloadFailed")));
                        return;
                    }

                    int focusAfterDelete = Math.Max(0,
                        Math.Min(pageIndex, _pageControls.Count - 1));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    JumpToPage(focusAfterDelete);
                    RecentFilesService.UpdateMetadata(
                        filePath, _pageControls.Count, File.GetLastWriteTimeUtc(filePath));
                    var afterBookmarks = PageBookmarkService
                        .ApplyPageDelete(filePath, pageIndex).ToList();
                    RefreshBookmarks(_loadSessionId, filePath, currentLease);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    PushUndoAction(new DocumentSnapshotAction(
                        this, beforeBytes, afterBytes,
                        pageIndex, focusAfterDelete,
                        beforeBookmarks, afterBookmarks));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Get("Editor.PageDeleted"), "\uE74D");
                }
                catch (OperationCanceledException)
                {
                }
                catch (InvalidOperationException)
                {
                    if (operationMayHaveChangedDocument && beforeBytes != null)
                    {
                        // Even the "blocked" fault can land after a partial
                        // write — restore bytes + sidecar first. TryRollback
                        // re-leases when a failed reload retired currentLease;
                        // a swapped document refuses silently.
                        var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                            filePath, beforeBytes, beforeBookmarks, pageIndex,
                            currentLease, "DeletePage");
                        if (!stillOurs)
                            return;
                        currentLease = rolledBack;
                    }
                    else if (!ValidateDocumentOperationLease(currentLease))
                    {
                        return;
                    }
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Get("Editor.PageDeleteBlocked"), "\uE783");
                }
                catch (Exception ex)
                {
                    if (operationMayHaveChangedDocument && beforeBytes != null)
                    {
                        var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                            filePath, beforeBytes, beforeBookmarks, pageIndex,
                            currentLease, "DeletePage");
                        if (!stillOurs)
                            return;
                        currentLease = rolledBack;
                    }
                    else if (!ValidateDocumentOperationLease(currentLease))
                    {
                        return;
                    }
                    await WinUiDialogService.ShowErrorAsync(
                        XamlRoot ?? GetMainWindow()?.Content?.XamlRoot,
                        LocalizationService.Get("Common.Error"),
                        LocalizationService.Format("Editor.DeletePageFailed", ex.Message));
                }
                finally
                {
                    // Method-owned: the reloaded lease published by
                    // ReloadDocumentForOperationAsync must be disposed here
                    // (lease Dispose is idempotent for the caller's own).
                    currentLease?.Dispose();
                }
            }
        }

        /// <summary>
        /// WPF <c>DuplicatePageAtAsync</c> parity (thumbnail context menu —
        /// Core <c>PdfService.DuplicatePageAsync</c> copies the page in place
        /// right after itself): admission lease → structural latch → dirty
        /// flush → byte snapshot → duplicate → reload → focus the copy
        /// (<paramref name="pageIndex"/> + 1) → metadata + bookmark remap →
        /// undo snapshot. A mid-operation failure rolls the file bytes +
        /// bookmark sidecar back (import-op rollback parity); failures
        /// surface as the WPF <c>Editor.PageDuplicateFailed</c> toast.
        /// </summary>
        private async Task DuplicatePageAtAsync(
            int pageIndex,
            DocumentOperationLease operationLease)
        {
            if (!TryBeginDocumentEdit(out var editLease))
                return;
            using (editLease)
            {
                using var structuralScope = BeginStructuralOperation();
                if (structuralScope == null)
                    return;

                if (string.IsNullOrWhiteSpace(_currentPdfPath))
                {
                    GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "\uE783");
                    return;
                }

                string filePath = _currentPdfPath;
                DocumentOperationLease currentLease = operationLease;
                byte[] before = null;
                int focusBefore = 0;
                List<PageBookmark> beforeBookmarks = null;
                bool operationMayHaveChangedDocument = false;
                try
                {
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    if (_documentSaveCoordinator.IsDirty &&
                        (!await AutoSaveAsync(currentLease) ||
                            !ValidateDocumentOperationLease(currentLease)))
                        return;

                    before = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    focusBefore = GetCurrentPageIndex();
                    beforeBookmarks = PageBookmarkService.Load(filePath).ToList();
                    operationMayHaveChangedDocument = true;
                    await _pdfService.DuplicatePageAsync(filePath, pageIndex);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    byte[] after = await File.ReadAllBytesAsync(filePath, currentLease.Token);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;

                    currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                    if (currentLease == null)
                    {
                        // Post-mutation reload failure — restore the before
                        // bytes + sidecar via TryRollback (re-leases when the
                        // failed reload retired currentLease); swapped →
                        // silent, otherwise surface instead of returning.
                        var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                            filePath, before, beforeBookmarks, focusBefore,
                            currentLease, "DuplicatePage");
                        if (!stillOurs)
                            return;
                        currentLease = rolledBack;
                        GetMainWindow()?.ShowToast(
                            LocalizationService.Format(
                                "Editor.PageDuplicateFailed",
                                LocalizationService.Get("Editor.DocumentReloadFailed")),
                            "\uE783", 3500);
                        return;
                    }
                    int focused = Math.Max(0, Math.Min(pageIndex + 1, _pageControls.Count - 1));
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    JumpToPage(focused);
                    // WPF omits this; the WinUI insert/delete already refresh
                    // it and a duplicate changes the page count the same way.
                    RecentFilesService.UpdateMetadata(
                        filePath, _pageControls.Count, File.GetLastWriteTimeUtc(filePath));
                    var afterBookmarks = PageBookmarkService
                        .ApplyPageInsert(filePath, pageIndex + 1).ToList();
                    RefreshBookmarks(_loadSessionId, filePath, currentLease);
                    if (!ValidateDocumentOperationLease(currentLease))
                        return;
                    PushUndoAction(new DocumentSnapshotAction(
                        this, before, after, focusBefore, focused,
                        beforeBookmarks, afterBookmarks));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (operationMayHaveChangedDocument && before != null)
                    {
                        var (rolledBack, stillOurs) = await TryRollbackStructuralOperationAsync(
                            filePath, before, beforeBookmarks, focusBefore,
                            currentLease, "DuplicatePage");
                        if (!stillOurs)
                            return;
                        currentLease = rolledBack;
                    }
                    else if (!ValidateDocumentOperationLease(currentLease))
                    {
                        return;
                    }
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.PageDuplicateFailed", ex.Message), "\uE783", 3500);
                }
                finally
                {
                    currentLease?.Dispose();
                }
            }
        }

        /// <summary>
        /// WPF <c>VersionHistory_Click</c> parity: a <see cref="MenuFlyout"/>
        /// anchored to the toolbar button lists the version sidecars
        /// (creation-time stamped, newest first); the handler captures the
        /// menu's session/path so a mid-menu document swap can't act on the
        /// replacement document. Selecting a version snapshots the CURRENT
        /// annotations as a new version first (restore stays reversible),
        /// then clears every layer + the undo ledger, repaints from the
        /// sidecar and marks dirty (WPF restore sequence).
        /// </summary>
        private void VersionHistory_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPdfPath) || !_isHostActive ||
                _resourcesReleased || _documentInteractionBlocked)
                return;

            int menuSessionId = _loadSessionId;
            string menuPath = _currentPdfPath;
            var versions = VersionControlService.GetVersions(_currentPdfPath);
            if (versions.Count == 0)
            {
                GetMainWindow()?.ShowToast(
                    LocalizationService.Get("Editor.NoVersionHistory"), "");
                return;
            }

            var flyout = new MenuFlyout();
            if (s_versionHistoryPresenterStyle == null)
            {
                var style = new Style(typeof(MenuFlyoutPresenter));
                style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 300.0));
                style.Setters.Add(new Setter(
                    ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto));
                s_versionHistoryPresenterStyle = style;
            }
            flyout.MenuFlyoutPresenterStyle = s_versionHistoryPresenterStyle;

            for (int i = 0; i < versions.Count; i++)
            {
                string versionFilePath = versions[i];
                var dt = File.GetCreationTime(versionFilePath);
                var item = new MenuFlyoutItem
                {
                    Text = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                };
                AutomationProperties.SetAutomationId(item, $"Editor.VersionHistoryItem.{i}");
                item.Click += async (s, args) =>
                {
                    using var operationLease = CaptureDocumentOperationLease(
                        menuSessionId, menuPath, _pdfService);
                    if (!ValidateDocumentOperationLease(operationLease) ||
                        !TryBeginDocumentEdit(out var editLease))
                        return;

                    using (editLease)
                    try
                    {
                        var data = await VersionControlService.LoadVersionAsync(
                            versionFilePath, operationLease.Token);
                        if (data == null || !ValidateDocumentOperationLease(operationLease))
                            return;

                        // Snapshot the current annotations as a new version
                        // first so the restore stays reversible (WPF parity).
                        var current = CollectAnnotations();
                        if (!ValidateDocumentOperationLease(operationLease))
                            return;
                        await VersionControlService.SaveVersionAsync(
                            menuPath, current, operationLease.Token);
                        if (!ValidateDocumentOperationLease(operationLease))
                            return;

                        // The sticky-note bubble + inline text chrome are
                        // editor-level UI referencing containers the sweep is
                        // about to detach — close them first (the sweep
                        // cannot reach a root-level Popup the way the WPF
                        // canvas clear implicitly did).
                        DeselectTextBox();
                        CancelStickyNoteEdit();
                        ClearAllAnnotations();
                        if (!ValidateDocumentOperationLease(operationLease))
                            return;
                        // A restored snapshot is a new document state; undo
                        // entries from the previous snapshot must not be able
                        // to reinsert its annotations via Ctrl+Z (WPF parity).
                        ClearUndoRedoHistory();
                        if (!ValidateDocumentOperationLease(operationLease))
                            return;
                        _pdfService.ExtractedAnnotations = data;
                        await LoadAnnotationsIntoPagesAsync();
                        if (!ValidateDocumentOperationLease(operationLease))
                            return;
                        GetMainWindow()?.ShowToast(LocalizationService.Format(
                            "Editor.RestoredVersion",
                            dt.ToString("g", LocalizationService.CurrentCulture)));
                        if (!ValidateDocumentOperationLease(operationLease))
                            return;
                        MarkDirty();
                    }
                    catch (OperationCanceledException)
                    {
                        // A reload/tab release intentionally cancels old menu
                        // continuations without surfacing an error in the
                        // new doc (WPF parity).
                    }
                    catch (Exception ex)
                    {
                        if (!ValidateDocumentOperationLease(operationLease))
                            return;
                        GetMainWindow()?.ShowToast(
                            LocalizationService.Get("Editor.VersionLoadFailed"));
                        System.Diagnostics.Debug.WriteLine(
                            $"[VersionHistory] Error: {ex.Message}");
                    }
                };
                flyout.Items.Add(item);
            }

            _transientFlyout = flyout;
            flyout.ShowAt(VersionHistoryButton);
        }

        private static Style s_versionHistoryPresenterStyle;

        /// <summary>
        /// WPF <c>ClearAllAnnotations</c> parity: sweep every annotation
        /// layer on every page (version-restore paints a fresh snapshot next).
        /// </summary>
        private void ClearAllAnnotations()
        {
            foreach (var page in _pageControls)
            {
                page.ClearAllAnnotations();
            }
        }

        // ── Keyboard ────────────────────────────────────────────────────────

        private void EditorPage_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // T9: the interaction block swallows every shortcut — the
            // close/navigate protocol already committed and flushed any
            // open edit session before blocking input.
            if (_documentInteractionBlocked || _resourcesReleased)
            {
                e.Handled = true;
                return;
            }

            // WPF guards each branch with !IsEditableTextInputFocused instead
            // of returning early — the Escape-resize and Delete/Back branches
            // below intentionally run regardless of focus.
            bool textInputFocused = FocusManager.GetFocusedElement(XamlRoot) is TextBox;

            // WPF: Escape restores an in-flight text resize before any other
            // branch (including while the embedded TextBox owns focus).
            if (e.Key == VirtualKey.Escape && _resizingTextContainer != null)
            {
                CancelTextResize(restoreBounds: true);
                e.Handled = true;
                return;
            }

            // WPF Escape parity — the routed KeyDown runs regardless of text
            // focus or modifiers: sweep transient UI (covers the search
            // panel) and drop the active tool back to None. ActivateTool
            // commits any live edit session via the tool-switch path —
            // nothing is discarded.
            if (e.Key == VirtualKey.Escape)
            {
                CloseTransientUi("escape");
                ActivateTool(ToolType.None);
                e.Handled = true;
                return;
            }

            bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);
            bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);
            bool alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
                .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);

            // WPF Ctrl+S runs ahead of the text-focus gate — a save while a
            // TextBox is focused commits the live edit session first.
            if (ctrl && e.Key == VirtualKey.S)
            {
                e.Handled = true;
                _ = SaveAnnotationsToPdfAsync();
                return;
            }

            // WPF Ctrl+P — document print, ahead of the text-focus gate like
            // Ctrl+S (WPF ran it from the same ungated else-if chain; the
            // context-menu item advertises the same accelerator).
            if (ctrl && e.Key == VirtualKey.P)
            {
                e.Handled = true;
                _ = PrintPdfAsync();
                return;
            }

            if (ctrl && !textInputFocused)
            {
                switch (e.Key)
                {
                    case VirtualKey.F:
                        OpenPdfSearch();
                        e.Handled = true;
                        return;
                    case VirtualKey.M:
                        if (_pageControls.Count > 0 && !string.IsNullOrWhiteSpace(_currentPdfPath))
                        {
                            PageBookmarkService.Toggle(_currentPdfPath, GetCurrentPageIndex());
                            RefreshBookmarks();
                        }
                        e.Handled = true;
                        return;
                    case VirtualKey.A:
                        // WPF Ctrl+A: arm Select and select all ink on the
                        // current page.
                        ActivateTool(ToolType.Select);
                        if (_pageControls.Count > 0)
                        {
                            var page = _pageControls[GetCurrentPageIndex()];
                            page.SelectAllAnnotations();
                            _activeSelectionPage = page;
                        }
                        e.Handled = true;
                        return;
                    case VirtualKey.C:
                        // WPF Ctrl+C: a live annotation selection serializes
                        // to clipboard JSON; otherwise a PDF text selection
                        // copies its text (WPF TryCopySelectedPdfText).
                        if (_activeSelectionPage != null && _activeSelectionPage.HasSelection)
                        {
                            CopySelection();
                            e.Handled = true;
                        }
                        else if (TryCopySelectedPdfTextToClipboard())
                        {
                            // WPF: a successful pdf-text copy confirms with
                            // the "Text copied" toast.
                            GetMainWindow()?.ShowToast(
                                LocalizationService.Get("Editor.TextCopied"), "\uE8C8", 1500);
                            e.Handled = true;
                        }
                        return;
                    case VirtualKey.X:
                        if (_activeSelectionPage != null && _activeSelectionPage.HasSelection)
                        {
                            CutSelection();
                            e.Handled = true;
                        }
                        return;
                    case VirtualKey.V:
                        // WPF Task 19: a bitmap on the clipboard wins over
                        // annotation JSON.
                        PasteClipboardImageOrSelection();
                        e.Handled = true;
                        return;
                    case VirtualKey.D:
                        // WPF Ctrl+D: duplicate the selection in place.
                        if (_activeSelectionPage != null && _activeSelectionPage.HasSelection)
                        {
                            DuplicateSelection();
                            e.Handled = true;
                        }
                        return;
                    case VirtualKey.Add:

                        AdjustZoom(ZoomStep);
                        e.Handled = true;
                        return;
                    case VirtualKey.Subtract:

                        AdjustZoom(-ZoomStep);
                        e.Handled = true;
                        return;
                    case VirtualKey.Number0:
                    case VirtualKey.NumberPad0:
                        SetZoom(1.0);
                        e.Handled = true;
                        return;
                }
                return;
            }
            if (ctrl)
                return;

            // WPF: the resize handles own their arrow-key contract — the
            // focused handle receives the event instead of the same arrow
            // becoming a text-box nudge.
            if (e.OriginalSource is TextResizeHandleElement
                || FocusManager.GetFocusedElement(XamlRoot) is TextResizeHandleElement)
            {
                return;
            }

            // PreviewKeyDown TUNNELS — it fires before the focused control's
            // own KeyDown. Interactive chrome (inline toolbar buttons/combo,
            // sticky marker button, the sticky editor TextBox, sidebar
            // lists) must own its keys: arrows navigate, Delete deletes
            // characters — the nudge/delete branches below would otherwise
            // also move the box or kill the annotation. Only the selected
            // annotation TextBox itself (or a non-control page element)
            // falls through.
            if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focusedElement
                && IsInteractiveEditorChrome(focusedElement))
            {
                return;
            }

            // WPF text-tool nudge: arrows move the selected box 1 DIP (Shift
            // ×10); while the embedded TextBox owns focus only Alt+Arrow
            // still nudges (plain arrows move the caret).
            if (_currentTool == ToolType.Text
                && _selectedTextBox != null
                && TryGetTextBoxNudge(e.Key, out double nudgeX, out double nudgeY)
                && (!shift || !alt))
            {
                bool allowNudge = !textInputFocused || (alt && !shift);
                if (allowNudge)
                {
                    double step = shift ? 10 : 1;
                    NudgeSelectedTextBox(nudgeX * step, nudgeY * step);
                    e.Handled = true;
                    return;
                }
            }

            // WPF Delete/Back branch — unguarded by focus: an empty selected
            // box is deleted even while its TextBox holds the caret, and the
            // Select-tool branch removes the live selection.
            if (e.Key == VirtualKey.Delete || e.Key == VirtualKey.Back)
            {
                if (_currentTool == ToolType.Select && _activeSelectionPage != null
                    && _activeSelectionPage.HasSelection)
                {
                    DeleteSelection();
                    e.Handled = true;
                    return;
                }
                if (_currentTool == ToolType.Text && _selectedTextBox != null)
                {
                    if (string.IsNullOrEmpty(_selectedTextBox.Text) && e.Key == VirtualKey.Back)
                    {
                        DeleteSelectedTextBox();
                        e.Handled = true;
                        return;
                    }
                    if (_selectedTextBox.FocusState == FocusState.Unfocused)
                    {
                        DeleteSelectedTextBox();
                        e.Handled = true;
                        return;
                    }
                }
            }

            if (textInputFocused)
                return;

            switch (e.Key)
            {
                case VirtualKey.PageUp:
                    if (_pageControls.Count > 0)
                        JumpToPage(Math.Max(0, GetCurrentPageIndex() - 1));
                    e.Handled = true;
                    break;
                case VirtualKey.PageDown:
                    if (_pageControls.Count > 0)
                        JumpToPage(Math.Min(_pageControls.Count - 1, GetCurrentPageIndex() + 1));
                    e.Handled = true;
                    break;
                case VirtualKey.Home:
                    if (_pageControls.Count > 0)
                        JumpToPage(0);
                    e.Handled = true;
                    break;
                case VirtualKey.End:
                    if (_pageControls.Count > 0)
                        JumpToPage(_pageControls.Count - 1);
                    e.Handled = true;
                    break;
                case VirtualKey.F3:
                    MovePdfSearchSelection(backwards: shift);
                    e.Handled = true;
                    break;
            }
        }


        // -- Localization / accessibility metadata --------------------------

        /// <summary>
        /// Reapplies every localized string + UIA name/tooltip -- the WinUI
        /// port of WPF ApplyLocalization (annotation-tool and save/version
        /// entries that belong to T7-T9 are covered by the same ids but stay
        /// inert).
        /// </summary>
        public void ApplyLocalization()
        {
            if (LoadingText != null)
                LoadingText.Text = LocalizationService.Get("Editor.Loading");
            if (PrintMenuItem != null)
                PrintMenuItem.Text = LocalizationService.Get("Editor.PrintTooltip");
            if (ExportCurrentPagePng1xMenuItem != null)
                ExportCurrentPagePng1xMenuItem.Text = LocalizationService.Format("Editor.CurrentPagePng", 1);
            if (ExportCurrentPagePng2xMenuItem != null)
                ExportCurrentPagePng2xMenuItem.Text = LocalizationService.Format("Editor.CurrentPagePng", 2);
            if (ExportAllPagesPng1xMenuItem != null)
                ExportAllPagesPng1xMenuItem.Text = LocalizationService.Format("Editor.AllPagesPng", 1);
            if (ExportAllPagesPng2xMenuItem != null)
                ExportAllPagesPng2xMenuItem.Text = LocalizationService.Format("Editor.AllPagesPng", 2);
            if (InsertPdfPageMenuItem != null)
                InsertPdfPageMenuItem.Text = LocalizationService.Get("Editor.InsertPdfPage");
            if (InsertImagePageMenuItem != null)
                InsertImagePageMenuItem.Text = LocalizationService.Get("Editor.InsertImagePage");
            if (RotateCurrentPageMenuItem != null)
                RotateCurrentPageMenuItem.Text = LocalizationService.Get("Editor.RotateCurrentPage");

            ApplyLocalizedSidebarLabels();
            ApplyLocalizedBookmarkLabel();
            ApplyLocalizedSearchStatus();
            ApplyToolbarAccessibilityMetadata();
            RefreshLocalizedDocumentSidebar();

            // Task 8: text toolbar + sticky-note chrome re-localize live.
            RefreshTextAlignmentOptions();
            foreach (var page in _pageControls)
                page.RefreshStickyNoteContextMenuLocalization();
            if (_stickyNotePopup != null)
            {
                ApplyStickyNoteDragHandleMetadata();
                ApplyStickyNoteButtonMetadata(_stickyNoteSaveButton,
                    LocalizationService.Get("Common.Save"), "Sticky.Save");
                ApplyStickyNoteButtonMetadata(_stickyNoteCancelButton,
                    LocalizationService.Get("Common.Cancel"), "Sticky.Cancel");
                ApplyStickyNoteButtonMetadata(_stickyNoteDeleteButton,
                    LocalizationService.Get("Editor.DeleteTooltip"), "Sticky.Delete");
            }
            if (_inlineTextBoxToolbar != null)
                ApplyInlineTextBoxToolbarLocalization();

            // Task 9-B: page-chrome tooltips (per-page delete + insert-gap
            // buttons) re-localize live too (WPF ApplyLocalization parity).
            RefreshPageDeleteButtons();
        }

        /// <summary>
        /// Keeps the static toolbar UIA contract in one place -- stable ids for
        /// smoke discovery with localized name/help/tooltip re-applied on every
        /// language change.
        /// </summary>
        private void ApplyToolbarAccessibilityMetadata()
        {
            SetToolbarMetadata(UndoButton, "Editor.UndoButton", LocalizationService.Get("Editor.UndoTooltip"));
            SetToolbarMetadata(RedoButton, "Editor.RedoButton", LocalizationService.Get("Editor.RedoTooltip"));
            SetToolbarMetadata(PenToolButton, "Editor.PenToolButton", LocalizationService.Get("Editor.PenTooltip"));
            SetToolbarMetadata(HighlighterToolButton, "Editor.HighlighterToolButton", LocalizationService.Get("Editor.HighlighterTooltip"));
            SetToolbarMetadata(HiddenInkToolButton, "HiddenInkToolButton", LocalizationService.Get("Editor.HiddenInkTooltip"));
            SetToolbarMetadata(StickyNoteToolButton, "Editor.StickyNoteToolButton", LocalizationService.Get("Editor.StickyNoteTooltip"));
            SetToolbarMetadata(EraserToolButton, "Editor.EraserToolButton", LocalizationService.Get("Editor.EraserTooltip"));
            SetToolbarMetadata(ShapeToolButton, "Editor.ShapeToolButton", LocalizationService.Get("Editor.ModeShape"));
            SetToolbarMetadata(LaserToolButton, "Editor.LaserToolButton", LocalizationService.Get("Editor.ModeLaser"));
            SetToolbarMetadata(RulerToolButton, "Editor.RulerToolButton", LocalizationService.Get("Editor.RulerTooltip"));
            SetToolbarMetadata(SelectToolButton, "Editor.SelectToolButton", LocalizationService.Get("Editor.SelectTooltip"));
            SetToolbarMetadata(TextToolButton, "Editor.TextToolButton", LocalizationService.Get("Editor.TextTooltip"));
            SetToolbarMetadata(SavePdfButton, "Editor.SavePdfButton", LocalizationService.Get("Editor.SaveDocumentTooltip"));
            SetToolbarMetadata(VersionHistoryButton, "Editor.VersionHistoryButton", LocalizationService.Get("Editor.VersionHistoryTooltip"));
            SetToolbarMetadata(PenOnlyButton, "Editor.PenOnlyButton", LocalizationService.Get("Editor.PenOnlyTooltip"));
            SetToolbarMetadata(PageNumberTextBox, "Editor.PageJump", LocalizationService.Get("Editor.PageJumpTooltip"));
            SetToolbarMetadata(PreviousPageButton, "Editor.PreviousPageButton", LocalizationService.Get("Editor.PreviousPage"));
            SetToolbarMetadata(NextPageButton, "Editor.NextPageButton", LocalizationService.Get("Editor.NextPage"));
            SetToolbarMetadata(SidebarPagesButton, "Editor.Sidebar.Pages", LocalizationService.Get("Editor.PagesTab"));
            SetToolbarMetadata(SidebarOutlineButton, "Editor.Sidebar.Outline", LocalizationService.Get("Editor.OutlineTab"));
            SetToolbarMetadata(SidebarBookmarksButton, "Editor.Sidebar.Bookmarks", LocalizationService.Get("Editor.BookmarksTab"));
            SetToolbarMetadata(SidebarCollapseButton, "Editor.Sidebar.Collapse", LocalizationService.Get("Editor.SidebarCollapse"));
            SetToolbarMetadata(BookmarkToggleButton, "Editor.Sidebar.BookmarkToggle",
                LocalizationService.Get("Editor.BookmarkCurrentPage"));
            SetToolbarMetadata(ToolbarItemsScrollViewer, "Editor.ToolbarOverflow",
                LocalizationService.Get("Editor.ToolbarScroll"));
            SetToolbarMetadata(ZoomOutButton, "Editor.ZoomOutButton", LocalizationService.Get("Editor.ZoomOutTooltip"));
            SetToolbarMetadata(ZoomInButton, "Editor.ZoomInButton", LocalizationService.Get("Editor.ZoomInTooltip"));
            SetToolbarMetadata(ZoomLabel, "Editor.ZoomLabel", LocalizationService.Get("Editor.ZoomEditTooltip"));
            SetToolbarMetadata(ZoomTextBox, "Editor.ZoomInput", LocalizationService.Get("Editor.ZoomEditTooltip"));
            SetToolbarMetadata(RotatePageButton, "Editor.RotatePageButton", LocalizationService.Get("Editor.RotateTooltip"));
            SetToolbarMetadata(PdfSearchTextBox, "PdfSearchTextBox", LocalizationService.Get("Editor.Searching"));
            SetToolbarMetadata(PdfSearchResultsListBox, "PdfSearchResultsListBox", LocalizationService.Format("Editor.SearchResults", 0));
            SetToolbarMetadata(PdfSearchStatusTextBlock, "PdfSearchStatus", string.Empty);
            // These two controls encode live state; their metadata must be
            // the final writes in every localization refresh.
            ApplyStateAwareSidebarMetadata();
        }

        private void ApplyStateAwareSidebarMetadata()
        {
            if (SidebarCollapseButton != null)
            {
                string collapseLabel = _sidebarCollapsed
                    ? LocalizationService.Get("Editor.SidebarExpand")
                    : LocalizationService.Get("Editor.SidebarCollapse");
                SetToolbarMetadata(SidebarCollapseButton, "Editor.Sidebar.Collapse", collapseLabel);
            }

            if (BookmarkToggleButton != null)
            {
                bool bookmarked = BookmarkToggleButton.IsChecked == true;
                string bookmarkLabel = bookmarked
                    ? LocalizationService.Get("Editor.UnbookmarkCurrentPage")
                    : LocalizationService.Get("Editor.BookmarkCurrentPage");
                SetToolbarMetadata(BookmarkToggleButton, "Editor.Sidebar.BookmarkToggle", bookmarkLabel);
                AutomationProperties.SetItemStatus(BookmarkToggleButton, bookmarkLabel);
            }
        }

        private static void SetToolbarMetadata(DependencyObject control, string automationId, string label)
        {
            if (control == null)
                return;

            ToolTipService.SetToolTip(control, label);
            AutomationProperties.SetAutomationId(control, automationId);
            AutomationProperties.SetName(control, label);
            AutomationProperties.SetHelpText(control, label);
        }

        private void ApplyLocalizedBookmarkLabel()
        {
            if (BookmarkToggleButton == null)
                return;

            bool bookmarked = BookmarkToggleButton.IsChecked == true;
            SetBookmarkButtonContent(bookmarked);
            ApplyStateAwareSidebarMetadata();
        }

        /// <summary>
        /// Shared sidebar label block — both ApplyLocalizedSidebarLabels and
        /// RefreshLocalizedDocumentSidebar need the same 7 assignments.
        /// </summary>
        private void ApplyLocalizedSidebarLabelText()
        {
            string pages = LocalizationService.Get("Editor.PagesTab");
            if (SidebarPagesLabel != null)
                SidebarPagesLabel.Text = pages;
            if (SidebarOutlineLabel != null)
                SidebarOutlineLabel.Text = LocalizationService.Get("Editor.OutlineTab");
            if (SidebarBookmarksLabel != null)
                SidebarBookmarksLabel.Text = LocalizationService.Get("Editor.BookmarksTab");
            if (SidebarTitleLabel != null)
                SidebarTitleLabel.Text = pages;
            if (PagesEmptyState != null)
                PagesEmptyState.Text = LocalizationService.Get("Editor.NoDocumentLoaded");
            if (OutlineEmptyState != null)
                OutlineEmptyState.Text = LocalizationService.Get("Editor.NoDocumentLoaded");
            if (BookmarksEmptyState != null)
                BookmarksEmptyState.Text = LocalizationService.Get("Editor.SidebarNoBookmarks");
        }

        private void ApplyLocalizedSidebarLabels()
        {
            ApplyLocalizedSidebarLabelText();
            SetToolbarMetadata(ToolbarItemsScrollViewer, "Editor.ToolbarOverflow", LocalizationService.Get("Editor.ToolbarScroll"));
            SetSidebarTab(_sidebarTab);
            ApplyStateAwareSidebarMetadata();
        }

        private void ApplyLocalizedSearchStatus()
        {
            if (PdfSearchStatusTextBlock == null)
                return;

            if (PdfSearchPanel == null || PdfSearchPanel.Visibility != Visibility.Visible ||
                PdfSearchTextBox == null || string.IsNullOrWhiteSpace(PdfSearchTextBox.Text))
            {
                if (!string.IsNullOrEmpty(PdfSearchStatusTextBlock.Text))
                    PdfSearchStatusTextBlock.Text = string.Empty;
                return;
            }

            var currentStatus = PdfSearchStatusTextBlock.Text ?? string.Empty;
            var localizedStatus = PdfSearchResultsListBox.Items.Count == 0
                ? LocalizationService.Get("Editor.Searching")
                : LocalizationService.Format("Editor.SearchResults", _pdfSearchResults.Count);

            if (!string.Equals(currentStatus, localizedStatus, StringComparison.Ordinal))
                PdfSearchStatusTextBlock.Text = localizedStatus;
        }

        private void RefreshLocalizedDocumentSidebar()
        {
            ApplyLocalizedSidebarLabelText();

            foreach (var page in SidebarPageItems)
                page.PageLabel = LocalizationService.Format("Editor.PageNumber", page.PageIndex + 1);

            if (!string.IsNullOrWhiteSpace(_currentPdfPath))
            {
                RefreshBookmarks();
                if (OutlineTreeView != null)
                    _ = RefreshOutlineCoreAsync(CancellationToken.None, _loadSessionId, _currentPdfPath);
            }
            SetSidebarTab(_sidebarTab);
        }

        /// <summary>
        /// Called by MainWindow.CloseTab before the Frame leaves the tree --
        /// the same teardown Unloaded performs, idempotent.
        /// </summary>
        public void ShutdownEditor() => ReleaseResources();

        /// <summary>
        /// Closes every editor-owned transient surface — WPF
        /// <c>CloseTransientUi(reason)</c> parity. Idempotent and null-safe
        /// when nothing is transient. The ordinary text-edit session is
        /// intentionally excluded: it commits later via LostFocus / the tool
        /// switch, exactly as WPF leaves it open across a deactivate.
        /// </summary>
        private void CloseTransientUi(string reason = null)
        {
            // Gesture boundary first (WPF CancelInteraction): restore
            // in-flight text geometry + selection snapshots before the
            // popup sweep so capture-loss callbacks cannot leave a stale
            // transaction behind. Page CancelInteraction now also cancels
            // any captured sticky-marker drag.
            CancelTextBoxDrag(restoreBounds: true);
            CancelTextResize(restoreBounds: true);
            foreach (var page in _pageControls)
                page.CancelInteraction();

            _pdfSearchCts?.Cancel();
            ClosePdfSearch();
            CancelStickyNoteEdit();
            _textColorFlyout?.Hide();
            _transientFlyout?.Hide();
            _transientFlyout = null;
            if (_toolFlyout != null)
            {
                _toolFlyout.Hide();
                _toolFlyout = null;
            }
            _toolFlyoutTool = ToolType.None;
            // The eraser-size preview ellipse is transient chrome too —
            // cancel its self-hide timer and drop it immediately.
            _eraserPreviewCts?.Cancel();
            if (EraserSizePreviewEllipse != null)
                EraserSizePreviewEllipse.Visibility = Visibility.Collapsed;
            _pageContextMenu?.Hide();
            RemoveInlineTextBoxToolbar();

            // Sticky marker menus live on the page overlay containers —
            // sweep them so a right-click menu cannot survive tab
            // switching or Alt-Tab (WPF container.ContextMenu.IsOpen=false).
            foreach (var page in _pageControls)
            {
                foreach (var container in page.GetOverlayContainers())
                    container.ContextFlyout?.Hide();
            }
        }

        /// <summary>
        /// WPF SetHostActive parity: MainWindow calls this on tab switches so
        /// hidden tabs sweep transient UI, gate page input AND stop the
        /// selection marching-ants timer via
        /// <see cref="PdfPageControl.SetHostActive"/> → ApplyInputGate.
        /// Rendering/scroll state stays warm — the tab is hidden, not torn
        /// down.
        /// </summary>
        public void SetHostActive(bool isActive)
        {
            // WPF runs the transient sweep BEFORE the no-op early return —
            // repeated SetHostActive(false) calls still close anything that
            // opened since the last gate.
            if (!isActive)
                CloseTransientUi("inactive editor");

            // A releasing/failed editor stays non-interactive — the T9
            // close protocol owns its input state until a retry succeeds.
            if (_resourcesReleased ||
                (isActive && !_releaseState.CanResumeInteraction) ||
                _isHostActive == isActive)
                return;
            _isHostActive = isActive;
            foreach (var page in _pageControls)
            {
                page.SetHostActive(isActive);
                page.SetDocumentInputEnabled(isActive && !_documentInteractionBlocked);
            }

            // The sweep hid the text chrome — re-show it over the still-
            // selected box when the tab comes back.
            if (isActive && _selectedTextBox != null && _currentTool == ToolType.Text)
                PositionInlineTextBoxToolbar(_selectedTextBox.Parent as UIElement ?? _selectedTextBox);
        }

        // ── Session/lease plumbing ──────────────────────────────────────────

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

        private bool IsSidebarLoadCurrent(int sessionId, string filePath)
        {
            if (_resourcesReleased || sessionId != _loadSessionId || string.IsNullOrWhiteSpace(filePath))
                return false;
            return string.Equals(
                DocumentOperationSession.NormalizePath(filePath),
                DocumentOperationSession.NormalizePath(_currentPdfPath ?? string.Empty),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reloads the document after a structural file write and returns a
        /// lease bound to the NEW session (WPF ReloadDocumentForOperationAsync).
        /// LoadPdfAsync cancels the pre-reload session, so the incoming lease
        /// is always retired here and must not be reused by the caller.
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
                await LoadPdfAsync(filePath);
            }
            finally
            {
                // LoadPdfAsync begins the replacement session and cancels this
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

        /// <summary>
        /// Mid-operation rollback for the structural page ops — the same
        /// sequence <see cref="InsertExternalDocumentAsync"/> runs inline for
        /// document imports: restore the pre-op PDF bytes + the persisted
        /// bookmark list, reload under a fresh session lease, refocus, and
        /// repaint the bookmark rail. Returns the lease the caller must keep
        /// validating against (<see cref="ReloadDocumentForOperationAsync"/>
        /// retires the incoming one), or null when the session was replaced
        /// mid-rollback and the caller must bail silently. A rollback failure
        /// hands the (possibly dead) lease back — callers re-validate before
        /// surfacing the original error.
        /// </summary>
        private async Task<DocumentOperationLease> RollbackStructuralOperationAsync(
            string filePath,
            byte[] beforeBytes,
            List<PageBookmark> beforeBookmarks,
            int focusBefore,
            DocumentOperationLease currentLease,
            string operationName)
        {
            try
            {
                await WriteDocumentBytesAsync(filePath, beforeBytes, currentLease.Token);
                if (!ValidateDocumentOperationLease(currentLease))
                    return null;
                PageBookmarkService.Replace(filePath, beforeBookmarks ?? new List<PageBookmark>());
                currentLease = await ReloadDocumentForOperationAsync(filePath, currentLease);
                if (currentLease == null)
                    return null;
                if (!ValidateDocumentOperationLease(currentLease))
                    return null;
                JumpToPage(Math.Max(0, Math.Min(focusBefore, _pageControls.Count - 1)));
                RefreshBookmarks(_loadSessionId, filePath, currentLease);
                return currentLease;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception rollbackException)
            {
                if (ValidateDocumentOperationLease(currentLease))
                    System.Diagnostics.Debug.WriteLine(
                        $"[{operationName}] Rollback failed: {rollbackException}");
                return currentLease;
            }
        }

        /// <summary>
        /// Rollback entry point that survives a dead
        /// <paramref name="currentLease"/>: the failed reload retires the
        /// incoming lease inside <see cref="ReloadDocumentForOperationAsync"/>'s
        /// finally, so a caller holding only that lease would skip both the
        /// restore AND the failure report (the old <c>if (!Validate) return</c>
        /// guard fired on the dead lease). When the incoming lease no longer
        /// validates, a fresh lease is captured — but ONLY while the live
        /// session still owns <paramref name="filePath"/>; a genuinely swapped
        /// document returns (null, false) and the caller stays silent (WPF
        /// stale-import parity). When the path is still ours but no lease can
        /// be established, (null, true) lets the caller surface the failure
        /// even though no restore could run.
        /// </summary>
        private async Task<(DocumentOperationLease Lease, bool StillOwnsDocument)> TryRollbackStructuralOperationAsync(
            string filePath,
            byte[] beforeBytes,
            List<PageBookmark> beforeBookmarks,
            int focusBefore,
            DocumentOperationLease currentLease,
            string operationName)
        {
            if (!ValidateDocumentOperationLease(currentLease))
            {
                // Dead/stale incoming lease — a path match is the only honest
                // "still our document" proof a re-leased capture can offer.
                currentLease?.Dispose();
                if (!string.Equals(
                        DocumentOperationSession.NormalizePath(filePath),
                        DocumentOperationSession.NormalizePath(_currentPdfPath ?? string.Empty),
                        StringComparison.OrdinalIgnoreCase))
                    return (null, false);
                try
                {
                    currentLease = CaptureDocumentOperationLease(_pdfService);
                }
                catch (Exception)
                {
                    // Session disposed/inactive — path still ours → report.
                    return (null, true);
                }
                if (!ValidateDocumentOperationLease(currentLease))
                {
                    // The captured lease can't validate against the live
                    // session — the session is mid-transition; the path is
                    // still ours, so surface the failure even though the
                    // restore can't run.
                    currentLease?.Dispose();
                    return (null, true);
                }
            }

            var rolledBack = await RollbackStructuralOperationAsync(
                filePath, beforeBytes, beforeBookmarks, focusBefore,
                currentLease, operationName);
            return (rolledBack, true);
        }

        /// <summary>
        /// DocumentSnapshotAction undo/redo boundary (WPF
        /// ApplyDocumentSnapshotAsync): atomically restore the snapshot bytes,
        /// reload the document, restore focus and publish the fresh lease +
        /// recent-file metadata for the new page count/timestamp.
        /// </summary>
        private async Task<DocumentOperationLease> ApplyDocumentSnapshotAsync(
            byte[] snapshotBytes,
            int focusPageIndex,
            DocumentOperationLease operationLease)
        {
            if (string.IsNullOrWhiteSpace(_currentPdfPath) ||
                !ValidateDocumentOperationLease(operationLease))
                return null;

            // Structural latch — undo/redo rides the same byte-write + reload
            // pipeline as the live structural ops, so never overlap them. The
            // null refusal flows through DocumentSnapshotAction.ApplyAsync as
            // LastOperationSucceeded=false: PerformUndo/Redo keep the action on
            // its stack (honest, retryable refusal — never drop it silently).
            using var structuralScope = BeginStructuralOperation();
            if (structuralScope == null)
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

        private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static Brush ResolveThemeBrush(string key, Color fallback)
        {
            if (Application.Current?.Resources?.TryGetValue(key, out var value) == true &&
                value is Brush brush)
                return brush;
            return new SolidColorBrush(fallback);
        }

        private static MainWindow GetMainWindow() => MainWindow.Current;

        private IntPtr GetWindowHandle()
        {
            var window = GetMainWindow();
            return window == null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(window);
        }

        // ── T9 save/autosave pipeline + close/dirty protocol (WPF parity) ────

        /// <summary>
        /// Reloads persisted settings and re-arms the autosave interval
        /// (WPF ApplySettings parity; the settings window itself is T9-B,
        /// this entry point is what it will call).
        /// </summary>
        public void ApplySettings() => ApplySettings(AppSettingsService.Load());

        /// <summary>
        /// WPF <c>ApplySettings(AppSettings)</c> parity: applies a staged
        /// settings snapshot (the settings dialog's live preview calls this
        /// on every control change), re-arms autosave, pushes tool settings
        /// to every page, and — when the performance mode changed — drops
        /// the rendered-page caches so the visible working set re-rasters at
        /// the new policy's scale.
        /// </summary>
        public void ApplySettings(AppSettings settings)
        {
            string previousPerformanceMode = CurrentPerformanceMode;
            _applicationSettings = settings ?? new AppSettings();
            ApplySettingsToToolState();
            ApplyToolToAllPages();
            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.Interval = TimeSpan.FromSeconds(
                    Math.Max(15, _applicationSettings.AutoSaveIntervalSeconds));
            }

            if (!string.Equals(previousPerformanceMode, CurrentPerformanceMode, StringComparison.Ordinal))
            {
                var profile = PdfRenderPolicy.GetProfile(CurrentPerformanceMode);
                _lastRenderedDpiScale = Math.Min(Math.Max(_zoomLevel, 1.0), profile.MaxRenderScale);
                _pagesInitiallyRendered.Clear();
                _pagesRenderedAtScale.Clear();
                var visiblePages = GetVisiblePageControls();
                TrimPageBitmapWorkingSet(visiblePages);
                // KickViewportRender debounces the scroll re-render tick,
                // which re-runs RenderPageInitialAsync for the now-empty
                // _pagesInitiallyRendered set (WPF RenderVisibleWorkingSetAsync).
                if (_isHostActive && !_resourcesReleased)
                    KickViewportRender();
            }
        }

        private void EnsureAutoSaveTimer()
        {
            // A released/releasing editor is inert — never re-arm its timers.
            if (_resourcesReleased || !_releaseState.CanResumeInteraction)
                return;

            if (_autoSaveTimer == null)
            {
                _autoSaveTimer = DispatcherQueue.CreateTimer();
                _autoSaveTimer.IsRepeating = true;
                _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            }

            // WPF parity: the stored value is sanitized to {15,30,60,120};
            // the Math.Max keeps the floor for hand-edited settings files.
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(
                Math.Max(15, _applicationSettings?.AutoSaveIntervalSeconds ?? 60));
            _autoSaveTimer.Start();
        }

        private async void AutoSaveTimer_Tick(
            Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            // DispatcherQueueTimer callbacks are async void. Guard the
            // callback itself as well as AutoSaveAsync's shared task so an
            // interval tick cannot re-enter while the previous save is
            // awaiting disk.
            if (Interlocked.Exchange(ref _autoSaveTimerRunning, 1) != 0)
                return;

            try
            {
                // Host-active gate: in WPF a hidden tab's session is
                // cancelled outright so its tick dies at lease validation;
                // WinUI keeps hidden sessions alive for fast re-activation,
                // so the gate lives on the tick. Close/guard paths call
                // AutoSaveAsync directly and bypass this gate.
                if (!_isHostActive || _resourcesReleased || !_releaseState.CanResumeInteraction)
                    return;
                using var operationLease = CaptureDocumentOperationLease(_pdfService);
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                var saved = await AutoSaveAsync(operationLease);
                if (saved && ValidateDocumentOperationLease(operationLease))
                {
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Get("Editor.AutoSaved"), "\uE74E", 1500);
                }
            }
            catch (Exception ex)
            {
                // async-void timer callback: no App.UnhandledException
                // backstop exists, so nothing may escape (AutoSaveAsync
                // already reports save failures — this is the last-resort
                // guard for the lease/toast plumbing around it).
                System.Diagnostics.Debug.WriteLine($"[AutoSave] Tick faulted: {ex}");
            }
            finally
            {
                Volatile.Write(ref _autoSaveTimerRunning, 0);
            }
        }

        /// <summary>
        /// One autosave attempt for the current generation. Returns false on
        /// a stale lease, cancellation or save failure; failures surface the
        /// localized toast and the timer stays armed for the next interval.
        /// </summary>
        public async Task<bool> AutoSaveAsync(DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            operationLease ??= CaptureDocumentOperationLease(_pdfService);
            // Do not short-circuit on IsDirty: a successful callback clears
            // the flag before its in-flight task finishes — the shared task
            // must still be joined during that completion window.
            try
            {
                if (string.IsNullOrEmpty(_currentPdfPath) ||
                    !ValidateDocumentOperationLease(operationLease))
                    return false;
                return await SaveCurrentDocumentWithLeaseAsync(operationLease).ConfigureAwait(true) &&
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
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.AutoSaveFailed", ex.Message), "", 3500);
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

            // Capture/commit the active text session before SaveAsync
            // captures its generation — committing inside the persistence
            // callback would make the first save appear stale and force an
            // unnecessary second PDF/version write.
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
                var result = await saveTask.ConfigureAwait(true);
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

        /// <summary>
        /// Joins the save pipeline's in-flight task(s) before a document
        /// reload resets the coordinator's generation space — the
        /// <c>SaveUntilCleanAsync</c> join semantics applied to quiescence
        /// rather than persistence. The awaited task is the FULL save
        /// (including the version sidecar write, which runs outside the PDF
        /// path lease), and the loop repeats until no tracked save remains.
        /// A faulted save is only observed: the reload discards the
        /// coordinator's state regardless, and the owning workflow already
        /// surfaced the failure.
        /// </summary>
        private async Task DrainInFlightDocumentSaveAsync()
        {
            while (true)
            {
                Task<DocumentSaveResult> pending;
                lock (_saveGate)
                    pending = _autoSaveInFlight;
                pending ??= _documentSaveCoordinator.InFlightSave;
                if (pending == null)
                    return;
                try
                {
                    await pending.ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[EditorPage] In-flight save faulted during reload drain: {ex}");
                }
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
            // DocumentSaveCoordinator deliberately does not capture a UI
            // synchronization context. A generation mismatch can therefore
            // retry its persistence callback on a thread-pool continuation;
            // collect the live DependencyObjects only on this page's
            // dispatcher, while the PDF/version I/O remains asynchronous.
            var dispatcherQueue = DispatcherQueue;
            if (dispatcherQueue != null && !dispatcherQueue.HasThreadAccess)
            {
                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                if (!dispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        await SaveCurrentDocumentCoreAsync(saveGeneration, operationLease)
                            .ConfigureAwait(true);
                        completion.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                }))
                {
                    if (ownsLease)
                        operationLease.Dispose();
                    throw new OperationCanceledException(operationLease.Token);
                }
                await completion.Task.ConfigureAwait(false);
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
            await VersionControlService.SaveVersionAsync(filePath, annotations, operationLease.Token);
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

        private async void SavePdf_Click(object sender, RoutedEventArgs e)
        {
            await SaveAnnotationsToPdfAsync();
        }

        /// <summary>
        /// Manual save (toolbar + Ctrl+S). Joins an in-flight autosave, so
        /// Ctrl+S cannot race the timer or write a second annotation
        /// snapshot (WPF SaveAnnotationsToPdfAsync parity).
        /// </summary>
        private async Task SaveAnnotationsToPdfAsync()
        {
            if (string.IsNullOrEmpty(_currentPdfPath))
            {
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.NoDocumentLoaded"), "");
                return;
            }

            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease))
                return;
            try
            {
                if (await SaveCurrentDocumentWithLeaseAsync(operationLease).ConfigureAwait(true) &&
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
                await WinUiDialogService.ShowErrorAsync(
                    XamlRoot ?? GetMainWindow()?.Content?.XamlRoot,
                    LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Editor.SaveFailed", ex.Message));
            }
        }

        // ── Edit admission / interaction block (WPF parity) ─────────────

        private bool TryBeginDocumentEdit(out IDisposable lease)
        {
            lease = null;
            if (_resourcesReleased || _documentInteractionBlocked || !_releaseState.CanResumeInteraction)
                return false;

            return _editAdmission.TryEnter(out lease);
        }

        /// <summary>
        /// Structural-operation latch (stronger than WPF needed — its modal
        /// pickers serialized these gestures for free). The edit admission is
        /// a shared counter, so two overlapping structural ops (insert /
        /// delete / duplicate / rotate / external import) could interleave
        /// their on-disk byte snapshots and reloads and act on stale page
        /// indices. Returns a scope while the latch is free; null when one is
        /// already in flight — callers refuse quietly.
        /// </summary>
        private IDisposable BeginStructuralOperation()
        {
            return Interlocked.CompareExchange(ref _structuralOperationInFlight, 1, 0) == 0
                ? new StructuralOperationScope(this)
                : null;
        }

        private void ExitStructuralOperation()
        {
            Interlocked.Exchange(ref _structuralOperationInFlight, 0);
        }

        private sealed class StructuralOperationScope : IDisposable
        {
            private EditorPage _owner;

            public StructuralOperationScope(EditorPage owner) => _owner = owner;

            public void Dispose() =>
                Interlocked.Exchange(ref _owner, null)?.ExitStructuralOperation();
        }

        private void SetDocumentInteractionBlocked(bool blocked)
        {
            bool effectiveBlocked = blocked || !_releaseState.CanResumeInteraction;
            _documentInteractionBlocked = effectiveBlocked;
            // Disable the complete editor command/input subtree, not only
            // the page controls — toolbar commands and routed keyboard
            // handlers could otherwise still mutate the model while
            // close/navigation waits for the final persistence barrier.
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

            // Input routed before IsEnabled flipped can still sit in the
            // dispatcher queue and mutate the live model before its event
            // callback calls MarkDirty/PushUndoAction. Let already-queued
            // input callbacks drain before the final generation check —
            // an async dispatcher barrier, never a UI-thread wait.
            await DispatcherQueueBarrierAsync(cancellationToken).ConfigureAwait(true);
            if (operationLease != null && !ValidateDocumentOperationLease(operationLease))
                throw new OperationCanceledException(operationLease.Token);
        }

        /// <summary>
        /// Drains already-queued input work — the WinUI stand-in for WPF's
        /// <c>Dispatcher.InvokeAsync(() => {}, DispatcherPriority.Input)</c>.
        /// DispatcherQueue input callbacks run at Normal priority, so a
        /// Normal-priority enqueue lands behind everything already queued.
        /// </summary>
        private Task DispatcherQueueBarrierAsync(CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var queue = DispatcherQueue;
            if (queue == null ||
                !queue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
                    () => completion.TrySetResult(true)))
            {
                completion.TrySetResult(false);
            }
            return completion.Task.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Reopens a document that was safely persisted for navigation and
        /// is now the active frame again. Navigation preparation deliberately
        /// leaves the editor blocked while it is in the back stack;
        /// rendering activation alone must not silently reopen model
        /// mutations (WPF ResumeDocumentInteraction parity — kept for the
        /// re-activation paths that survive a torn-down frame).
        /// </summary>
        public void ResumeDocumentInteraction()
        {
            if (_resourcesReleased || !_releaseState.CanResumeInteraction)
                return;

            // Navigation uses the same final-generation close state as tab
            // closing. Reopen both state machines when the editor becomes
            // active again; otherwise the coordinator would keep
            // _closeCompleted and silently discard the first edit.
            _documentSaveCoordinator.CancelCloseRequest();
            _editAdmission.CancelClose();
            SetDocumentInteractionBlocked(false);
            EnsureAutoSaveTimer();
        }

        /// <summary>Resume editing after a failed/non-destructive close attempt.</summary>
        public void CancelClosePreparation()
        {
            if (!_releaseState.CanResumeInteraction)
            {
                // A timed-out/failed native release owns the editor until
                // its tracked task settles and a retry succeeds. Re-entry
                // must not detach/rebind events or admit late mutations.
                SetDocumentInteractionBlocked(true);
                return;
            }

            _documentSaveCoordinator.CancelCloseRequest();
            _editAdmission.CancelClose();
            SetDocumentInteractionBlocked(false);
            SyncDirtyStateMirror();
            EnsureAutoSaveTimer();
        }

        // ── Close/navigation protocol (WPF PrepareFor*/ReleaseResourcesAsync) ──

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
                // TextBox.TextChanged mutates the live model before the
                // focus event commits its undo action — flush that session
                // before the coordinator captures a generation.
                CommitTextEditSession();
                CloseTransientUi("navigation");
                // Sticky-note editing lives in a Popup and therefore is not
                // covered by disabling the EditorPage subtree.
                CommitStickyNoteEdit();
                await BeginDocumentInteractionBlockAsync(cancellationToken, operationLease)
                    .ConfigureAwait(true);
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                // A queued Popup activation can run at the dispatcher
                // barrier after the first flush — close/cancel once more.
                CloseTransientUi("navigation barrier");
                CommitStickyNoteEdit();
                _autoSaveTimer?.Stop();

                cancellationToken.ThrowIfCancellationRequested();
                await _documentSaveCoordinator.SaveUntilCleanAsync(
                    generation => SaveCurrentDocumentCoreAsync(generation, operationLease),
                    // Navigation has the same atomic admission requirement
                    // as final close: a queued model callback must either be
                    // retained for a retry or be rejected after the clean
                    // generation is completed.
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
                    "",
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
                    "",
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
        /// Final close protocol: stop the timer, join/coalesce any active
        /// save, retry a generation mismatch, and only report success once
        /// the newest snapshot is persisted. Callers must not release
        /// resources or remove a tab when this returns false.
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
                await BeginDocumentInteractionBlockAsync(cancellationToken, operationLease)
                    .ConfigureAwait(true);
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
                    "",
                    3500);
                return false;
            }
            catch (Exception ex)
            {
                if (!ValidateDocumentOperationLease(operationLease))
                    return false;
                SyncDirtyStateMirror();
                await WinUiDialogService.ShowErrorAsync(
                    XamlRoot ?? GetMainWindow()?.Content?.XamlRoot,
                    LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Editor.SaveFailed", ex.Message));
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

        /// <summary>
        /// Final tab-close cleanup for timers, hooks, bitmaps and the native
        /// PDF document. Returns an already-running release task to
        /// concurrent callers instead of disposing twice (WPF parity).
        /// </summary>
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
                _loadCts?.Cancel();
                _reRenderCts?.Cancel();
                _scrollReRenderCts?.Cancel();
                _thumbnailLoadCts?.Cancel();
                _pdfSearchCts?.Cancel();
                // CancelActiveLoad parity: invalidate every outstanding
                // lease so late continuations cannot touch dead pages.
                _documentOperationSession.Cancel();

                _autoSaveTimer?.Stop();
                if (_autoSaveTimer != null)
                    _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
                _autoSaveTimer = null;

                _zoomRenderDebounceTimer.Stop();
                _scrollRenderDebounceTimer.Stop();

                if (_languageChangedSubscribed)
                {
                    LocalizationService.LanguageChanged -= EditorPage_LanguageChanged;
                    _languageChangedSubscribed = false;
                }

                // Window-owned shared service — release the reference
                // only; MainWindow disposes the single instance on Closed.
                _penService = null;

                DeselectTextBox();
                ClearPdfTextSelection();
                foreach (var page in _pageControls)
                    page.ReleaseResources();
                ReleaseThumbnailCache();
                _pageControls.Clear();

                // PdfService owns the rasterizer/document — await the async
                // dispose so a release failure can still be reported (and
                // retried) instead of escaping as a fire-and-forget fault.
                await _pdfService.DisposeAsync().AsTask().ConfigureAwait(true);

                // Mark released only after every resource owner has
                // completed. A failure leaves the editor/tab recoverable
                // for a retry.
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
                    // retry completes — a timeout/catch must not make
                    // ActivateTab resume a service whose native owners were
                    // only partially released.
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

        /// <summary>
        /// When the page leaves the tree mid-prepare (a Frame removal the
        /// workflow gates did not own), finish the pending save first, then
        /// run the protocol teardown — never wipe the annotation collectors
        /// under a pending final save.
        /// </summary>
        private async Task DeferredTeardownAsync()
        {
            Task pending;
            lock (_lifecycleGate)
            {
                pending = _releaseResourcesInFlight
                    ?? (Task)_closePreparationInFlight
                    ?? _navigationPreparationInFlight;
            }
            if (pending != null)
            {
                try
                {
                    await pending.ConfigureAwait(true);
                }
                catch
                {
                    // The owning workflow reports the failure; teardown
                    // still has to run.
                }
            }
            bool released;
            try
            {
                released = await ReleaseResourcesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorPage] Deferred release failed: {ex}");
                released = false;
            }
            if (released)
                return;
            // A refused/failed release on a detached page has no retry
            // path — nobody calls ReleaseResourcesAsync again for this
            // instance. Record the failure so a queued activation can never
            // silently resume the editor, and hold the input block + timer
            // stop that a successful release would have set (a pre-cleanup
            // failure may have re-armed both through CancelClosePreparation).
            _releaseState.MarkFailed();
            _autoSaveTimer?.Stop();
            SetDocumentInteractionBlocked(true);
        }

        // ── Lifecycle ───────────────────────────────────────────────────────

        private void ReleaseResources()
        {
            if (_resourcesReleased)
                return;
            // Every unload funnels through the tracked async release — even
            // when no protocol task is in flight. The old synchronous
            // teardown set _resourcesReleased BEFORE its sweep, so a
            // mid-teardown throw both escaped through the Unloaded /
            // OnNavigatedFrom handlers (there is no App.UnhandledException
            // backstop) and left a false "released" marker that made a later
            // ReleaseResourcesAsync report success without releasing
            // anything. DeferredTeardownAsync joins any pending protocol
            // task first, then runs the same awaited teardown a managed
            // close uses — including the save barrier a dirty forced-unload
            // still needs and the awaited PdfService dispose.
            _ = DeferredTeardownAsync();
        }
    }


    // ── Sidebar view-models (WPF EditorPage nested types, hoisted to
    // namespace level so XAML x:DataType can resolve them) ─────────────────

    /// <summary>One row in the Pages rail: label + lazy thumbnail.</summary>
    public sealed class SidebarPageItem : INotifyPropertyChanged
    {
        private SoftwareBitmapSource _thumbnail;
        private string _pageLabel;
        private bool _isSelected;

        public SidebarPageItem(int pageIndex, string pageLabel, string automationId)
        {
            PageIndex = pageIndex;
            _pageLabel = pageLabel ?? string.Empty;
            AutomationId = automationId ?? string.Empty;
        }

        public int PageIndex { get; }
        public string AutomationId { get; }

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

        public SoftwareBitmapSource Thumbnail
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

        /// <summary>Selection visual state driven by UpdateThumbnailSelection.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LabelForeground)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LabelFontWeight)));
            }
        }

        public Brush LabelForeground => _isSelected
            ? ResolveBrush("ThemeAccentBrush", Color.FromArgb(0xFF, 0x25, 0x63, 0xEB))
            : ResolveBrush("ThemeSubtleForegroundBrush", Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));

        public FontWeight LabelFontWeight => _isSelected ? FontWeights.SemiBold : FontWeights.Normal;

        private static Brush ResolveBrush(string key, Color fallback)
        {
            if (Application.Current?.Resources?.TryGetValue(key, out var value) == true &&
                value is Brush brush)
                return brush;
            return new SolidColorBrush(fallback);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>One row in the Bookmarks rail.</summary>
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

    /// <summary>One node in the Outline rail; children mirror the PDF outline.</summary>
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
        public string InvokeAutomationId => AutomationId + ".Invoke";
        public string PageLabel =>
            LocalizationService.Format("Editor.PageNumber", PageIndex + 1);
        public ObservableCollection<SidebarOutlineItem> Children { get; } = new();
    }

    /// <summary>One full-text hit produced by the document search pass.</summary>
    public sealed class PdfSearchResult
    {
        public int PageIndex { get; init; }
        public int StartOffset { get; init; }
        public int Length { get; init; }
        public string DisplayText { get; init; }
    }
}
