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
        private bool _isPageJumpInitializing = true;
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
            try { InitializeComponent(); }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "editorpage_ctor_error.txt"), ex.ToString()); } catch { }
                throw;
            }
            _pdfService = new PdfService(PdfiumRasterizerFactory.Shared);
            _applicationSettings = AppSettingsService.Load();

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
            _documentOperationSession.Begin(_loadSessionId, newPath, _pdfService);
        }

        private void EditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_languageChangedSubscribed)
            {
                LocalizationService.LanguageChanged += EditorPage_LanguageChanged;
                _languageChangedSubscribed = true;
            }

            AutoCollapseSidebarForNarrowLayout();
            if (_completedLoadSessionId != 0 && _pageControls.Count > 0)
                KickViewportRender();
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
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            // Retire every background pipeline from the previous document.
            _reRenderCts?.Cancel();
            _scrollReRenderCts?.Cancel();
            _thumbnailLoadCts?.Cancel();
            _thumbnailLoadCts?.Dispose();
            _thumbnailLoadCts = new CancellationTokenSource();
            _pdfSearchCts?.Cancel();
            _lastRenderedDpiScale = 1.0;
            _pagesRenderedAtScale.Clear();
            _isPageJumpEditing = false;
            _isPageJumpInitializing = true;

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
                // Retained for the annotation tasks: ExtractedAnnotations is
                // page-indexed markup harvested during LoadPdfAsync.
                _ = _pdfService.ExtractedAnnotations;

                double currentTop = 0;
                for (int i = 0; i < pageCount; i++)
                {
                    var size = _pdfService.GetPageSizeInDips(i);
                    AddPdfPage(i, size, ref currentTop, pageCount);
                }

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
                double renderScale = PdfRenderPolicy.CalculateRenderScale(
                    CurrentPerformanceMode,
                    page.Width,
                    page.Height,
                    1.0);
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
            _reRenderCts?.Dispose();
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
            if (!_isPageJumpInitializing && !_suppressPageJumpTextChanged)
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

        // ── Toolbar (visual-state only until T7–T9) ─────────────────────────

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            // T7/T8: undo pipeline not ported.
        }

        private void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            // T7/T8: redo pipeline not ported.
        }

        /// <summary>
        /// T7/T8 annotation tools are inert; the toggle visuals still track a
        /// single active tool so the chrome behaves like the WPF toolbar
        /// (click to check, re-click to clear, mutually exclusive).
        /// </summary>
        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked)
                return;

            var toolButtons = new[]
            {
                PenToolButton, HighlighterToolButton, HiddenInkToolButton,
                StickyNoteToolButton, EraserToolButton, ShapeToolButton,
                LaserToolButton, SelectToolButton, TextToolButton
            };
            foreach (var button in toolButtons)
            {
                if (!ReferenceEquals(button, clicked))
                    button.IsChecked = false;
            }
        }

        private void PenOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            // T7: palm-rejection toggle — visual state only.
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
            foreach (var bookmark in PageBookmarkService.Load(filePath ?? string.Empty))
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

        private void UpdateBookmarkButton()
        {
            if (BookmarkToggleButton == null)
                return;
            int current = GetCurrentPageIndex();
            bool bookmarked = PageBookmarkService.Load(_currentPdfPath)
                .Any(bookmark => bookmark.PageIndex == current);
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

        private async void PdfSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                    .HasFlag(Microsoft.UI.Input.VirtualKeyStates.Down);
                await MovePdfSearchSelectionAsync(shift);
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
                    _ = MovePdfSearchSelectionAsync(backwards: shift);
                    e.Handled = true;
                    break;
                case VirtualKey.Escape:
                    if (PdfSearchPanel.Visibility == Visibility.Visible)
                    {
                        ClosePdfSearch();
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

        private void ApplyLocalizedSidebarLabels()
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
            if (SidebarPagesLabel != null)
                SidebarPagesLabel.Text = LocalizationService.Get("Editor.PagesTab");
            if (SidebarOutlineLabel != null)
                SidebarOutlineLabel.Text = LocalizationService.Get("Editor.OutlineTab");
            if (SidebarBookmarksLabel != null)
                SidebarBookmarksLabel.Text = LocalizationService.Get("Editor.BookmarksTab");
            if (SidebarTitleLabel != null)
                SidebarTitleLabel.Text = LocalizationService.Get("Editor.PagesTab");
            if (PagesEmptyState != null)
                PagesEmptyState.Text = LocalizationService.Get("Editor.NoDocumentLoaded");
            if (OutlineEmptyState != null)
                OutlineEmptyState.Text = LocalizationService.Get("Editor.NoDocumentLoaded");
            if (BookmarksEmptyState != null)
                BookmarksEmptyState.Text = LocalizationService.Get("Editor.SidebarNoBookmarks");

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

            // PdfService owns the rasterizer/document; async-dispose is
            // fire-and-forget on teardown (the tab is leaving the tree).
            var service = _pdfService;
            _ = service.DisposeAsync().AsTask();

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
