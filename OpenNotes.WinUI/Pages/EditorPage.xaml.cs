using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
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
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Text;

namespace Caelum.Pages
{
    /// <summary>
    /// V6 WinUI port of WPF <c>Pages/EditorPage.xaml.cs</c>, scoped to the
    /// Task 6 shell: PDF load/render, scroll/zoom/page navigation, the
    /// three-tab document sidebar (pages / outline / bookmarks), the toolbar
    /// chrome + page-jump navigator, full-text search, the page context menu
    /// (PNG export, insert, rotate), the loading overlay and tab-close
    /// disposal.
    ///
    /// Deliberately deferred, preserving element names + AutomationIds:
    /// T7 — pen/highlighter/eraser/shape/laser/ruler/hidden-ink input
    /// surfaces (toolbar toggles are visual-only); T8 — text/select/sticky
    /// annotations, search-bound text highlights, selectable-PDF surface;
    /// T9 — undo/redo pipeline, save/version history, thumbnail
    /// drag-reorder, insert-gap affordances, page delete buttons, print.
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
        }

        private ToolType _currentTool = ToolType.None;
        private ToolType _previousTool = ToolType.None;

        // WPF defaults: pen black @1.5 DIP (settings-driven), highlighter
        // yellow at the fixed 140-alpha translucency, eraser 20 DIP.
        private const byte FreehandHighlighterOpacity = 140;
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

        private bool _isLoadingAnnotations;
        private readonly Stack<IUndoAction> _undoStack = new();
        private readonly Stack<IUndoAction> _redoStack = new();

        // Pen hardware service (Huawei hotkey toggle + capability probing).
        private Caelum.Services.PenService _penService;

        // ── Pages/rendering ─────────────────────────────────────────────────
        private readonly List<PdfPageControl> _pageControls = new();
        private readonly List<double> _pageTopOffsets = new();
        private readonly List<double> _pageHeights = new();
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
            ApplyLocalization();
            // Expanded is the default state — same as the WPF shell — so the
            // pages margin starts at the 228 DIP offset.
            SetSidebarCollapsed(false);
            SetSidebarTab(SidebarTab.Pages);
            UpdatePageNumberIndicator();
            UpdateZoomLabel();
            Loaded += EditorPage_Loaded;
            Unloaded += EditorPage_Unloaded;
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
        }

        /// <summary>
        /// WinUI port of the WPF pen-service init: one service per editor
        /// page (the WPF window-scoped instance shared the HWND subclass —
        /// here each page subclasses its own window; duplicate hotkey
        /// registrations on the same HWND are idempotent per id).
        /// </summary>
        private void InitializePenService()
        {
            if (_penService != null)
                return;

            var window = GetMainWindow();
            if (window == null)
                return;

            _penService = new Caelum.Services.PenService();
            _penService.ToolToggleRequested += PenService_ToolToggleRequested;
            _penService.PenDeviceDetected += PenService_PenDeviceDetected;
            _penService.Initialize(window);
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
        /// Huawei M-Pencil double-tap (Win+F19/F20 via the HWND subclass) →
        /// eraser toggle, marshalled to the UI thread. WPF parity.
        /// </summary>
        private void PenService_ToolToggleRequested(object sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(ToggleEraserMode);
        }

        private void PenService_PenDeviceDetected(object sender, Caelum.Services.PenDeviceInfo info)
        {
            // Packet probing fires on the UI thread already; the guard keeps
            // the toast honest if the service ever moves off it.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (info == null)
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
                if (button != null)
                    button.IsChecked = t == tool;
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
            if (tool != _currentTool)
            {
                _previousTool = _currentTool;
                _currentTool = tool;
            }
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
                _currentPdfPath = filePath;
                SetCompatProbeText(filePath);
                PagesContainer.Children.Clear();
                _pageControls.Clear();
                _pageTopOffsets.Clear();
                _pageHeights.Clear();
                _pagesInitiallyRendered.Clear();
                _pagesRenderedAtScale.Clear();
                ReleaseThumbnailCache();
                ClearUndoRedoHistory();
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

                int pageCount = _pdfService.PageCount;

                double currentTop = 0;
                for (int i = 0; i < pageCount; i++)
                {
                    var size = _pdfService.GetPageSizeInDips(i);
                    AddPdfPage(i, size, ref currentTop, pageCount);
                }
                ApplyToolToAllPages();
                LoadAnnotationsIntoPages();

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
            pageControl.HiddenInkRemoved += PageControl_HiddenInkRemoved;
            pageControl.HiddenInksRemoved += PageControl_HiddenInksRemoved;
            pageControl.SelectionChanged += PageControl_SelectionChanged;
            pageControl.SelectionMoveCompleted += PageControl_SelectionMoveCompleted;
            pageControl.SelectionResizeCompleted += PageControl_SelectionResizeCompleted;
            pageControl.SelectionRotateCompleted += PageControl_SelectionRotateCompleted;
            pageControl.BlankContextRequested += PageControl_BlankContextRequested;

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

            _pageTopOffsets.Add(currentTop);
            _pageHeights.Add(size.Height);

            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            host.Children.Add(pageControl);
            PagesContainer.Children.Add(host);
            _pageControls.Add(pageControl);

            // T9: the WPF insert-gap affordance lives in this slot; the spacer
            // grid keeps identical page-top offsets until it is ported.
            if (index < pageCount - 1)
                PagesContainer.Children.Add(new Grid { Height = PageSpacing });

            currentTop += size.Height + PageSpacing;
        }

        /// <summary>
        /// Quiet sidecar load: ExtractedAnnotations is page-indexed markup
        /// harvested during LoadPdfAsync. Strokes enter through the store's
        /// quiet path under the _isLoadingAnnotations guard so loading never
        /// creates undo actions — the same contract as the WPF loader.
        /// </summary>
        private void LoadAnnotationsIntoPages()
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
        /// Tool toggles stay mutually exclusive like the WPF toolbar; Phase A
        /// maps Pen/Highlighter/Eraser onto the ink surface and leaves the
        /// Phase-B tools visual-only (their InkSurfaceTool is None).
        /// Re-clicking the active tool keeps it armed (WPF: the tool stays
        /// selected — a second click doesn't drop it to None).
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

            // Leaving Select abandons the selection (WPF ActivateTool parity).
            if (_currentTool == ToolType.Select && next != ToolType.Select
                && _activeSelectionPage != null)
            {
                _activeSelectionPage.ClearSelection();
                _activeSelectionPage = null;
            }

            if (next != _currentTool)
            {
                _previousTool = _currentTool;
                _currentTool = next;
            }
            ApplyToolToAllPages();

            // WPF ToggleToolButton parity: arming the Shape or Select tool
            // opens its options flyout under the button.
            if (next == ToolType.Shape)
                ShowShapeFlyout(clicked);
            else if (next == ToolType.Select)
                ShowSelectionFlyout(clicked);
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
                    default:
                        // None/Select/StickyNote/Text — ink surface idles;
                        // Select already armed its overlay above.
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
            _undoStack.Push(action);
            _redoStack.Clear();
            UpdateUndoRedoButtons();
        }

        private async Task PerformUndoAsync()
        {
            if (_undoStack.Count == 0)
                return;
            var action = _undoStack.Peek();
            try
            {
                await action.UndoAsync();
                // A failed token-resolution undo stays on the stack as a
                // no-op — same contract as the WPF StrokesErasedAction path.
                if (action is InkStrokesErasedAction erased && !erased.LastOperationSucceeded)
                    return;
                _undoStack.Pop();
                _redoStack.Push(action);
                UpdateUndoRedoButtons();
            }
            catch (Exception ex)
            {
                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.UndoFailed", ex.Message), "", 3500);
            }
        }

        private async Task PerformRedoAsync()
        {
            if (_redoStack.Count == 0)
                return;
            var action = _redoStack.Peek();
            try
            {
                await action.RedoAsync();
                if (action is InkStrokesErasedAction erased && !erased.LastOperationSucceeded)
                    return;
                _redoStack.Pop();
                _undoStack.Push(action);
                UpdateUndoRedoButtons();
            }
            catch (Exception ex)
            {
                GetMainWindow()?.ShowToast(
                    LocalizationService.Format("Editor.RedoFailed", ex.Message), "", 3500);
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
                PushUndoAction(new InkSelectionMoveAction(
                    page.Ink.Store, e.Strokes, e.DeltaX, e.DeltaY));
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
            else
            {
                PushUndoAction(new InkSelectionMoveAction(
                    page.Ink.Store, e.Strokes, e.DeltaX, e.DeltaY));
            }
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
            PushUndoAction(new InkSelectionResizeAction(
                page.Ink.Store, e.Strokes, e.TotalScale, e.Anchor));
        }

        private void PageControl_SelectionRotateCompleted(object sender, SelectionRotateCompletedEventArgs e)
        {
            if (sender is not PdfPageControl page)
                return;
            PushUndoAction(new InkSelectionRotateAction(
                page.Ink.Store, e.Strokes, e.TotalDegrees, e.Center));
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

        private void PageControl_HiddenInkRemoved(object sender, HiddenInkAnnotation annotation)
        {
            if (_isLoadingAnnotations || annotation == null || sender is not PdfPageControl page)
                return;
            int index = page.HiddenInkStore.IndexOf(annotation);
            PushUndoAction(new HiddenInkRemovedAction(
                page.HiddenInkStore, annotation, Math.Max(0, index)));
        }

        private void PageControl_HiddenInksRemoved(object sender, HiddenInksRemovedEventArgs e)
        {
            if (_isLoadingAnnotations || e?.Entries == null
                || e.Entries.Count == 0 || sender is not PdfPageControl page)
                return;
            PushUndoAction(new HiddenInksRemovedAction(page.HiddenInkStore, e.Entries));
        }

        private void PageControl_BlankContextRequested(object sender, EventArgs e)
        {
            ShowBlankContextMenu();
        }

        /// <summary>
        /// Delete-key selection removal: capture placements first so undo
        /// restores z-order, then quietly remove and push the batch action —
        /// WPF DeleteSelection → ItemsRemovedAction parity (strokes only;
        /// text containers are T8).
        /// </summary>
        private void DeleteSelection()
        {
            if (_activeSelectionPage == null || !_activeSelectionPage.HasSelection)
                return;

            var strokes = _activeSelectionPage.SelectedStrokes.ToList();
            var placements = strokes
                .Select(s => _activeSelectionPage.Ink.Store.CaptureStrokePlacement(s))
                .ToList();

            foreach (var stroke in strokes)
                _activeSelectionPage.Ink.Store.RemoveStrokeQuiet(stroke);

            PushUndoAction(new InkStrokesRemovedAction(_activeSelectionPage.Ink.Store, placements));
            _activeSelectionPage.ClearSelection();
            InvalidateThumbnail(_activeSelectionPage.PageIndex);
        }

        /// <summary>
        /// WPF EnsureBlankContextMenu minus clipboard/save entries (T8/T9):
        /// Select-all and Delete (visible only with a live selection).
        /// </summary>
        private void ShowBlankContextMenu()
        {
            bool hasSelection = _activeSelectionPage != null && _activeSelectionPage.HasSelection;

            var flyout = new MenuFlyout();
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

            flyout.ShowAt(PdfScrollViewer);
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

            var flyout = new Flyout { Content = panel };
            flyout.ShowAt(anchor);
        }

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

            var flyout = new Flyout { Content = panel };
            flyout.ShowAt(anchor);
        }

        private static TextBlock PopupSectionHeader(string text, double topMargin = 0) => new()
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, topMargin, 0, 10),
            Foreground = ResolveThemeBrush("ThemeSubtleForegroundBrush", Color.FromArgb(0xFF, 0x6B, 0x72, 0x80)),
        };

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
                // T8: real text-bound highlight rectangles land with the text
                // overlay; SetPdfTextSelectionRects is a shell stub for now.
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
        /// T8 placeholder: WPF converts text-hit glyph bounds into selection
        /// rectangles on PdfTextSelectionCanvas. The shell keeps the call
        /// shape; the highlight draw arrives with the text overlay.
        /// </summary>
        private static IReadOnlyList<Rect> BuildPdfTextSelectionRects(
            PdfService.PdfPageTextInfo info, int startOffset, int endOffset)
            => Array.Empty<Rect>();

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
                IsEnabled = false // T9: print pipeline.
            };
            AutomationProperties.SetAutomationId(PrintMenuItem, "Editor.ContextMenu.Print");
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
        /// Shared insert-document boundary (WPF InsertExternalDocumentAsync,
        /// minus the undo/save pipeline that arrives with T9): run the Core
        /// operation, remap persisted bookmarks, reload, refocus and toast.
        /// </summary>
        private async Task InsertExternalDocumentAsync(
            Func<Task> operation,
            int insertPageIndex,
            int insertedPageCount,
            string successMessage,
            DocumentOperationLease operationLease = null)
        {
            bool ownsLease = operationLease == null;
            DocumentOperationLease currentLease = operationLease ?? CaptureDocumentOperationLease(_pdfService);
            try
            {
                if (string.IsNullOrWhiteSpace(_currentPdfPath) || !ValidateDocumentOperationLease(currentLease))
                    return;
                string filePath = _currentPdfPath;
                await operation();
                if (!ValidateDocumentOperationLease(currentLease))
                    return;
                PageBookmarkService.ApplyPageInsert(filePath, insertPageIndex, insertedPageCount);
                await LoadPdfAsync(filePath);
                // LoadPdfAsync swaps the session; a stale continuation must
                // not touch the new document.
                if (!IsSidebarLoadCurrent(_loadSessionId, filePath))
                    return;
                int focused = Math.Max(0, Math.Min(insertPageIndex, _pageControls.Count - 1));
                JumpToPage(focused);
                RefreshBookmarks(_loadSessionId, filePath, null);
                GetMainWindow()?.ShowToast(successMessage, "", 2000);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (ValidateDocumentOperationLease(currentLease))
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.ImportFailed", ex.Message), "", 3500);
            }
            finally
            {
                if (ownsLease)
                    currentLease?.Dispose();
            }
        }

        private async void RotateCurrentPage_Click(object sender, RoutedEventArgs e)
        {
            using var operationLease = CaptureDocumentOperationLease(_pdfService);
            if (!ValidateDocumentOperationLease(operationLease) ||
                string.IsNullOrWhiteSpace(_currentPdfPath) || _pageControls.Count == 0)
                return;
            string filePath = _currentPdfPath;
            int pageIndex = GetCurrentPageIndex();
            try
            {
                await _pdfService.RotatePageAsync(filePath, pageIndex, 1);
                if (!ValidateDocumentOperationLease(operationLease))
                    return;
                await LoadPdfAsync(filePath);
                if (!IsSidebarLoadCurrent(_loadSessionId, filePath))
                    return;
                JumpToPage(Math.Min(pageIndex, _pageControls.Count - 1));
                GetMainWindow()?.ShowToast(LocalizationService.Get("Editor.PageRotated"), "", 1800);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (ValidateDocumentOperationLease(operationLease))
                    GetMainWindow()?.ShowToast(
                        LocalizationService.Format("Editor.RotateFailed", ex.Message), "", 3500);
            }
        }

        // ── Keyboard ────────────────────────────────────────────────────────

        private void EditorPage_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Text inputs own their keys (page jump box, search box, zoom box).
            if (FocusManager.GetFocusedElement(XamlRoot) is TextBox)
                return;

            bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);
            bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);

            if (ctrl)
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
                case VirtualKey.Escape:
                    if (PdfSearchPanel.Visibility == Visibility.Visible)
                    {
                        ClosePdfSearch();
                        e.Handled = true;
                    }
                    else
                    {
                        // WPF Esc parity: dismiss the active tool back to
                        // None (which also drops any live selection).
                        ActivateTool(ToolType.None);
                        e.Handled = true;
                    }
                    break;
                case VirtualKey.Delete:
                case VirtualKey.Back:
                    if (_currentTool == ToolType.Select
                        && _activeSelectionPage != null
                        && _activeSelectionPage.HasSelection)
                    {
                        DeleteSelection();
                        e.Handled = true;
                    }
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

        // ── Lifecycle ───────────────────────────────────────────────────────

        private void ReleaseResources()
        {
            if (_resourcesReleased)
                return;
            _resourcesReleased = true;
            _isHostActive = false;

            if (_languageChangedSubscribed)
            {
                LocalizationService.LanguageChanged -= EditorPage_LanguageChanged;
                _languageChangedSubscribed = false;
            }

            _loadCts?.Cancel();
            _reRenderCts?.Cancel();
            _scrollReRenderCts?.Cancel();
            _thumbnailLoadCts?.Cancel();
            _pdfSearchCts?.Cancel();
            _zoomRenderDebounceTimer.Stop();
            _scrollRenderDebounceTimer.Stop();
            _documentOperationSession.Cancel();

            _penService?.Dispose();
            _penService = null;

            foreach (var page in _pageControls)
                page.CancelInteraction();

            // PdfService owns the rasterizer/document; async-dispose is
            // fire-and-forget on teardown (the tab is leaving the tree) but
            // failures must still be observed — an unobserved fault can take
            // down the process on a GC pass.
            var service = _pdfService;
            _ = service.DisposeAsync().AsTask().ContinueWith(
                t => System.Diagnostics.Debug.WriteLine(
                    $"[EditorPage] PdfService.DisposeAsync faulted: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);

            ReleaseThumbnailCache();
            _pageControls.Clear();
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
