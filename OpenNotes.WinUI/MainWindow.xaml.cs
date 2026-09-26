using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caelum.Models;
using Caelum.Pages;
using Caelum.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace Caelum
{
    /// <summary>
    /// V6 WinUI main window. Ports the chrome + tab model slice of the WPF
    /// <c>MainWindow.xaml.cs</c> plus the Task 5 toolbar slice: title-bar
    /// SearchBox/SelectButton/SortButton/MoreButton wired to the active
    /// <see cref="HomePage"/>, <see cref="ShowToast"/> overlay toasts,
    /// <see cref="NavigateActiveTabToFile"/> → EditorPage navigation,
    /// check-for-updates and the about dialog. Editor save/close workflows
    /// land in later tasks.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly ObservableCollection<AppTab> _tabs = new ObservableCollection<AppTab>();
        private AppTab _activeTab;
        private bool _syncingTabSelection;

        private readonly UpdateCheckService _updateCheckService = new UpdateCheckService();
        private bool _isUpdateCheckInProgress;
        private CancellationTokenSource _updateCheckCts;
        private CancellationTokenSource _toastCts;

        // ── T9 close/dirty workflow markers (WPF MainWindow parity) ──────
        // A tab/window close is a save-then-release workflow: while any of
        // these markers is set, tab activation, navigation and file-open
        // transitions are refused so the protocol cannot race itself.
        private bool _windowCloseWorkflowActive;
        private bool _allowWindowClose;
        private bool _navigationWorkflowActive;
        private readonly HashSet<AppTab> _tabCloseWorkflows = new HashSet<AppTab>();
        private CancellationTokenSource _windowCloseCts;
        private static readonly TimeSpan CloseWorkflowTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The live window (single-window shell) — the WinUI stand-in for
        /// <c>Window.GetWindow(this)</c>/<c>Application.Current.MainWindow</c>
        /// that pages and services anchor pickers/dialogs/toasts to.
        /// </summary>
        internal static new MainWindow Current { get; private set; }

        private AppWindow _appWindow;
        private OverlappedPresenter _presenter;
        private double _appliedScale = 1.0;

        // DIP intents — converted to physical px by the rasterization scale.
        private const int StartupWidthDips = 1280;
        private const int StartupHeightDips = 720;
        private const int MinWidthDips = 560;
        private const int MinHeightDips = 360;

        public MainWindow()
        {
            this.InitializeComponent();
            Current = this;

            InitializeAppWindow();
            ApplyStartupSettings();
            ApplyLocalization();

            WinUiThemeService.RegisterWindow(this);
            WinUiThemeService.ThemeApplied += WinUiThemeService_ThemeApplied;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            this.Closed += MainWindow_Closed;
            // WPF MainWindow_Deactivated parity: sweep transient UI + cancel
            // in-flight gestures when the window loses activation (Alt-Tab,
            // minimize) so an OpenNotes flyout/armed drag can't outlive focus.
            this.Activated += MainWindow_Activated;

            // The normal application window starts with Home (WPF parity).
            AddNewHomeTab(activate: true);
            MaybeRunTabSmoke();
        }

        // ── Window chrome (AppWindow / OverlappedPresenter) ─────────────────

        private void InitializeAppWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _presenter = _appWindow?.Presenter as OverlappedPresenter;
            ApplyCustomChrome(_presenter);

            // WPF: 1280x720, WindowStartupLocation=CenterScreen.
            // MoveAndResize AND PreferredMinimum* take PHYSICAL pixels under
            // PerMonitorV2 while the WPF sizes were DIPs. GetDpiForWindow
            // reads the monitor's real scale BEFORE the visual tree exists,
            // so the first paint lands at the right size — no
            // scale-1.0-then-rescale flicker. RootGrid_Loaded stays as the
            // safety net and only re-sizes if the real XamlRoot scale
            // differs (first-rasterization race).
            SizeAndCenter(GetWindowRasterizationScale(hWnd));
            RootGrid.Loaded += RootGrid_Loaded;

            if (_appWindow != null)
            {
                _appWindow.Changed += AppWindow_Changed;
                // WPF OnClosing parity — the first close attempt is always
                // cancelled; CompleteWindowCloseAsync saves+releases every
                // editor and reissues Close() once the protocol settles.
                _appWindow.Closing += AppWindow_Closing;
            }

            // Full client area; the XAML grid below draws the 40px title row
            // and AppTitleBar becomes the real caption/drag rect.
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= RootGrid_Loaded;
            var xamlRoot = RootGrid.XamlRoot;
            if (xamlRoot == null)
                return;

            // Cross-monitor DPI moves fire XamlRoot.Changed: the platform
            // rescales the window itself but does NOT rescale
            // PreferredMinimum* — re-apply the minimum there. NEVER
            // re-size or re-center after first layout: the user's window
            // size stays theirs.
            xamlRoot.Changed += XamlRoot_Changed;

            // GetDpiForWindow normally made this a no-op; re-run only when
            // the real rasterization scale differs (first-paint race).
            if (Math.Abs(xamlRoot.RasterizationScale - _appliedScale) > 0.001)
                SizeAndCenter(xamlRoot.RasterizationScale);
        }

        private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            // Fires on size/visibility changes too — ApplyMinimumSize is
            // idempotent, so only the minimum floor is re-asserted here.
            ApplyMinimumSize(sender.RasterizationScale);
        }

        private void SizeAndCenter(double rasterizationScale)
        {
            if (_appWindow == null)
                return;
            ApplyMinimumSize(rasterizationScale);
            double scale = _appliedScale;

            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
            if (displayArea == null)
                return;

            var workArea = displayArea.WorkArea;
            int width = (int)Math.Round(StartupWidthDips * scale);
            int height = (int)Math.Round(StartupHeightDips * scale);
            int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
            _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }

        /// <summary>
        /// <see cref="OverlappedPresenter.PreferredMinimumWidth"/>/Height are
        /// PHYSICAL pixels — an unscaled 560x360 would only enforce a
        /// 280x180 DIP floor at 200% DPI. Re-applied on XamlRoot.Changed
        /// (cross-monitor DPI moves) and on presenter swaps; idempotent.
        /// </summary>
        private void ApplyMinimumSize(double rasterizationScale)
        {
            double scale = NormalizeScale(rasterizationScale);
            _appliedScale = scale;
            if (_presenter == null)
                return;

            int minWidth = (int)Math.Round(MinWidthDips * scale);
            int minHeight = (int)Math.Round(MinHeightDips * scale);
            if (_presenter.PreferredMinimumWidth != minWidth)
                _presenter.PreferredMinimumWidth = minWidth;
            if (_presenter.PreferredMinimumHeight != minHeight)
                _presenter.PreferredMinimumHeight = minHeight;
        }

        private static double NormalizeScale(double rasterizationScale)
            => rasterizationScale > 0 ? rasterizationScale : 1.0;

        /// <summary>
        /// True monitor scale before the visual tree exists —
        /// <c>GetDpiForWindow</c>/96 (Win10 1607+; our floor is 17763).
        /// A non-DPI-aware process gets 96 → scale 1.0, which is also
        /// correct for its virtualized coordinates.
        /// </summary>
        private static double GetWindowRasterizationScale(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return 1.0;
            uint dpi = GetDpiForWindow(hWnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        /// <summary>
        /// NEVER-remove chrome rule: <c>ExtendsContentIntoTitleBar</c> alone
        /// only extends our content under the title bar — the system
        /// Min/Max/Close buttons still render topmost over the right edge and
        /// occlude the custom caption buttons (verified by UIA: both button
        /// sets coexisted at the same rects). Keeping the border but dropping
        /// the system title bar makes our XAML buttons the only caption UI.
        /// </summary>
        private static void ApplyCustomChrome(OverlappedPresenter presenter)
        {
            presenter?.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPresenterChange)
            {
                // A presenter swap drops the SetBorderAndTitleBar config —
                // re-apply or the system caption buttons come back over the
                // custom chrome.
                _presenter = sender.Presenter as OverlappedPresenter;
                ApplyCustomChrome(_presenter);
                // A presenter swap also resets PreferredMinimum* to defaults.
                ApplyMinimumSize(_appliedScale);
            }
            UpdateMaximizeGlyph();

            // WPF MainWindow_StateChanged parity: minimize gates the ACTIVE
            // editor (window-global pen hotkeys still arrive while
            // minimized); restore resumes interaction.
            if (_activeTab?.Frame?.Content is EditorPage activeEditor)
            {
                bool minimized = _appWindow?.Presenter is OverlappedPresenter p
                    && p.State == OverlappedPresenterState.Minimized;
                activeEditor.SetHostActive(!minimized);
                if (!minimized)
                    activeEditor.ResumeDocumentInteraction();
            }
        }

        private void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
                return;
            // Sweep every tab's editor (hidden tabs are already input-gated,
            // but an armed thumbnail drag / flyout on the ACTIVE editor must
            // not survive an app switch — WPF swept all retained editors).
            foreach (var tab in _tabs)
                (tab.Frame?.Content as EditorPage)?.OnWindowDeactivated();
        }

        private void UpdateMaximizeGlyph()
        {
            bool maximized = _presenter?.State == OverlappedPresenterState.Maximized;
            // ChromeMaximize / ChromeRestore stand-ins for the Lucide
            // Square/Restore icons until the icon port lands.
            MaximizeIcon.Glyph = maximized ? "\uE923" : "\uE922";
        }

        /// <summary>
        /// Window-side half of the editor's F11 immersive mode (WPF
        /// <c>ToggleImmersiveMode</c> hid page chrome; the WPF window itself
        /// was already borderless so no presenter swap existed there).
        /// <see cref="AppWindowPresenterKind.FullScreen"/> covers the
        /// taskbar; Default restores the pre-immersive placement.
        /// <see cref="AppWindow_Changed"/> re-applies the custom chrome +
        /// minimum size on each presenter swap (<c>DidPresenterChange</c>).
        /// </summary>
        internal void SetImmersiveFullscreen(bool immersive)
        {
            _appWindow?.SetPresenter(immersive
                ? AppWindowPresenterKind.FullScreen
                : AppWindowPresenterKind.Default);
        }

        /// <summary>
        /// WPF OnClosing parity: the first close attempt is cancelled and the
        /// save/release protocol completes before Close() is requested again
        /// — the process cannot exit while a snapshot is still in flight.
        /// </summary>
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_allowWindowClose)
                return;

            args.Cancel = true;
            if (_windowCloseWorkflowActive)
                return;

            _windowCloseWorkflowActive = true;
            _windowCloseCts?.Dispose();
            _windowCloseCts = new CancellationTokenSource(CloseWorkflowTimeout);
            _ = CompleteWindowCloseAsync(_windowCloseCts.Token);
        }

        /// <summary>
        /// Saves (dirty generations included) and releases every live editor,
        /// then reissues the real window close. A failed prepare/release
        /// keeps the window open with the editor interactive again.
        /// </summary>
        private async Task CompleteWindowCloseAsync(CancellationToken cancellationToken)
        {
            var preparedEditors = new List<EditorPage>();
            var releasesStarted = new HashSet<EditorPage>();
            var releasedEditors = new HashSet<EditorPage>();
            bool releaseHandoff = false;
            bool closeDispatched = false;
            try
            {
                var allEditors = _tabs
                    .Select(tab => tab.Frame?.Content as EditorPage)
                    .Where(editor => editor != null)
                    .Distinct()
                    .ToList();

                foreach (var editor in allEditors)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!await editor.PrepareForCloseAsync(cancellationToken).WaitAsync(cancellationToken))
                        return;
                    preparedEditors.Add(editor);
                }

                for (int releaseIndex = 0; releaseIndex < preparedEditors.Count; releaseIndex++)
                {
                    var editor = preparedEditors[releaseIndex];
                    cancellationToken.ThrowIfCancellationRequested();
                    releasesStarted.Add(editor);
                    Task<bool> releaseTask = editor.ReleaseResourcesAsync();
                    try
                    {
                        if (!await releaseTask.WaitAsync(cancellationToken))
                            return;
                        releasedEditors.Add(editor);
                    }
                    catch (OperationCanceledException) when (!releaseTask.IsCompleted)
                    {
                        // The underlying release is deliberately not aborted
                        // mid-disposal. The window workflow stays active
                        // while the native release settles in the background.
                        releaseHandoff = true;
                        _ = ContinueTimedOutWindowCloseAsync(
                            preparedEditors.ToList(),
                            releaseIndex,
                            releaseTask,
                            releasedEditors);
                        return;
                    }
                }

                // _allowWindowClose is a single-shot latch armed inside the
                // queued re-close itself: if TryEnqueue fails (dispatcher
                // shutting down) the latch must NOT stay armed, or the next
                // user close would bypass the save/release protocol entirely.
                // Close() must run on the UI dispatcher — the continuation
                // may be finishing on a thread-pool thread.
                if (!DispatcherQueue.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () =>
                        {
                            _allowWindowClose = true;
                            Close();
                        }))
                {
                    throw new InvalidOperationException(
                        "The window re-close could not be queued on the dispatcher.");
                }
                closeDispatched = true;
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "", 3500);
            }
            finally
            {
                if (!closeDispatched)
                {
                    foreach (var editor in preparedEditors)
                    {
                        if (releasesStarted.Contains(editor))
                            continue;
                        // Per-item guard: a faulted cancel must not strand
                        // _windowCloseWorkflowActive (and its close gate).
                        // This also runs during a release handoff so the
                        // not-yet-released suffix stays interactive while
                        // the native release settles in the background.
                        try
                        {
                            editor.CancelClosePreparation();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[MainWindow] CancelClosePreparation faulted: {ex}");
                        }
                    }

                    // Editors whose release already settled are dead
                    // objects — a partial close failure must not leave them
                    // displayed as usable zombie tabs.
                    foreach (var tab in _tabs.ToList())
                    {
                        if (releasedEditors.Contains(GetTabEditor(tab)))
                            RemoveTabAfterResourcesReleased(tab);
                    }
                }

                if (!releaseHandoff)
                {
                    _windowCloseWorkflowActive = false;
                    _windowCloseCts?.Dispose();
                    _windowCloseCts = null;
                }
            }
        }

        /// <summary>
        /// Finishes a window close after its bounded UI wait elapsed. The
        /// guard and all prepared editors remain busy until this task
        /// settles (WPF parity).
        /// </summary>
        private async Task ContinueTimedOutWindowCloseAsync(
            IReadOnlyList<EditorPage> preparedEditors,
            int releaseIndex,
            Task<bool> releaseTask,
            HashSet<EditorPage> releasedEditors)
        {
            try
            {
                if (!await releaseTask.ConfigureAwait(true))
                    throw new InvalidOperationException("The document release did not complete.");
                releasedEditors.Add(preparedEditors[releaseIndex]);

                for (int i = releaseIndex + 1; i < preparedEditors.Count; i++)
                {
                    if (!await preparedEditors[i].ReleaseResourcesAsync().ConfigureAwait(true))
                        throw new InvalidOperationException("The document release did not complete.");
                    releasedEditors.Add(preparedEditors[i]);
                }

                _allowWindowClose = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "", 3500);
                // The suffix was prepared but never released — a
                // timeout/failure must not strand those editors in a
                // close-preparation state or leave them half-detached.
                // Per-item guard: one faulted cancel must not skip the rest
                // or strand _windowCloseWorkflowActive.
                for (int i = releaseIndex + 1; i < preparedEditors.Count; i++)
                {
                    if (releasedEditors.Contains(preparedEditors[i]))
                        continue;
                    try
                    {
                        preparedEditors[i].CancelClosePreparation();
                    }
                    catch (Exception cancelEx)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[MainWindow] CancelClosePreparation faulted: {cancelEx}");
                    }
                }
                // Editors whose release already settled are dead objects —
                // never leave them displayed as usable zombie tabs.
                foreach (var tab in _tabs.ToList())
                {
                    if (releasedEditors.Contains(GetTabEditor(tab)))
                        RemoveTabAfterResourcesReleased(tab);
                }
                _windowCloseWorkflowActive = false;
                _windowCloseCts?.Dispose();
                _windowCloseCts = null;
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => _presenter?.Minimize();

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_presenter == null)
                return;
            if (_presenter.State == OverlappedPresenterState.Maximized)
                _presenter.Restore();
            else
                _presenter.Maximize();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // ── Startup settings / localization ─────────────────────────────────

        private void ApplyStartupSettings()
        {
            var startupSettings = AppSettingsService.Load();
            WinUiThemeService.Apply(startupSettings.Theme, workspaceBackdrop: startupSettings.WorkspaceBackdrop);
            LocalizationService.ApplyLanguage(startupSettings.Language);
        }

        private void ApplyLocalization()
        {
            Title = ProductInfo.DisplayName;
            if (ProductNameTextBlock != null)
                ProductNameTextBlock.Text = ProductInfo.DisplayName;
            ToolTipService.SetToolTip(NewTabButton, LocalizationService.Get("Main.NewTabTooltip"));
            if (SearchBox != null)
                SearchBox.PlaceholderText = LocalizationService.Get("Main.SearchPlaceholder");
            if (SelectButtonLabel != null)
                SelectButtonLabel.Text = LocalizationService.Get("Main.Select");
            if (SortByNameMenuItem != null)
                SortByNameMenuItem.Text = LocalizationService.Get("Main.SortByName");
            if (SortByDateMenuItem != null)
                SortByDateMenuItem.Text = LocalizationService.Get("Main.SortByDate");
            if (SettingsMenuItem != null)
                SettingsMenuItem.Text = LocalizationService.Get("Main.Settings");
            if (CheckForUpdatesMenuItem != null && !_isUpdateCheckInProgress)
                CheckForUpdatesMenuItem.Text = LocalizationService.Get("Main.CheckForUpdates");
            if (AboutMenuItem != null)
                AboutMenuItem.Text = LocalizationService.Get("Main.About");
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
            // Pages built at navigation time re-localize on their own; the
            // live HomePage rebinds through its ApplyLocalization too.
            (ActiveFrame?.Content as HomePage)?.ApplyLocalization();
        }

        private void WinUiThemeService_ThemeApplied(object sender, EventArgs e)
        {
            // Tab chrome binds to brush values computed by AppTab; re-resolve
            // them so a palette swap cannot leave stale brushes painted.
            foreach (var tab in _tabs)
                tab.RefreshVisualState();
            // Theme-resolved brushes on tiles are plain objects — re-raise
            // them the way the WPF DynamicResource bindings did implicitly.
            foreach (var tab in _tabs)
                (tab.Frame?.Content as HomePage)?.RefreshTileVisualState();
            RefreshSelectButtonVisualState();
        }

        // ── Window-scoped pen service (WPF WindowsPenService parity) ──────
        //
        // Exactly ONE PenService per window: it subclasses this HWND once
        // and owns the Win+F19/F20 RegisterHotKey pair. The earlier
        // per-EditorPage construction raced every page onto the same HWND —
        // duplicate hotkey ids fail for tabs 2+ (leaving them without
        // hotkeys, and the owning tab's teardown killed the registration
        // app-wide) while every subclass proc still observed the shared
        // WM_HOTKEY, so the eraser toggle landed on inactive editors.

        private PenService _penService;

        /// <summary>
        /// The single window-owned <see cref="PenService"/> — created and
        /// HWND-initialized on first access (<c>Initialize</c> is
        /// idempotent, so a HWND that wasn't ready yet simply retries on the
        /// next call), disposed in <see cref="MainWindow_Closed"/>. Editor
        /// pages pull it to feed pen probing; this window routes its events
        /// to the ACTIVE editor only.
        /// </summary>
        internal PenService GetOrCreatePenService()
        {
            if (_penService == null)
            {
                _penService = new PenService();
                _penService.ToolToggleRequested += PenService_ToolToggleRequested;
                _penService.PenDeviceDetected += PenService_PenDeviceDetected;
            }
            _penService.Initialize(this);
            return _penService;
        }

        /// <summary>
        /// Huawei M-Pencil double-tap → eraser toggle on the ACTIVE editor
        /// only — the window-level half of WPF's IsActiveEditorPage gate
        /// (the editor re-checks its host-active/released state inside the
        /// queued callback).
        /// </summary>
        private void PenService_ToolToggleRequested(object sender, EventArgs e)
            => (_activeTab?.Frame?.Content as EditorPage)?.HandlePenToolToggle();

        /// <summary>
        /// First-pen-packet toast — routed to the ACTIVE editor only. When
        /// no editor tab is active (Home tab) WPF's IsActiveEditorPage gate
        /// dropped the toast entirely; same here.
        /// </summary>
        private void PenService_PenDeviceDetected(object sender, PenDeviceInfo info)
            => (_activeTab?.Frame?.Content as EditorPage)?.HandlePenDeviceDetected(info);

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            WinUiThemeService.ThemeApplied -= WinUiThemeService_ThemeApplied;
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            _updateCheckCts?.Cancel();
            _toastCts?.Cancel();
            // The window owns the single PenService — drop the HWND
            // subclass + Win+F19/F20 registrations with the window.
            _penService?.Dispose();
            _penService = null;
            _windowCloseCts?.Dispose();
            _windowCloseCts = null;
            if (ReferenceEquals(Current, this))
                Current = null;
            if (_appWindow != null)
            {
                _appWindow.Changed -= AppWindow_Changed;
                _appWindow.Closing -= AppWindow_Closing;
            }
            var xamlRoot = RootGrid?.XamlRoot;
            if (xamlRoot != null)
                xamlRoot.Changed -= XamlRoot_Changed;
            // RegisterWindow removes this window itself via its Closed hook.
        }

        // ── Tab Management (WPF MainWindow parity, minimal slice) ───────────

        private Frame ActiveFrame => _activeTab?.Frame;

        /// <summary>Debug/smoke seam: number of live tabs.</summary>
        internal int TabCount => _tabs.Count;

        /// <summary>Debug/smoke seam: ordered tab list.</summary>
        internal IReadOnlyList<AppTab> Tabs => _tabs;

        internal AppTab ActiveTab => _activeTab;

        public void AddNewHomeTab(bool activate = true)
        {
            // Workflow gate (WPF parity): while a close/navigation workflow
            // holds an editor's lifecycle, no new surface may appear —
            // ActivateTab is already gated, so an un-gated creation would
            // strand a dead, never-activated tab mid-close.
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;
            var tab = new AppTab { Title = GetHomeTabTitle(), Icon = "Home" };
            var frame = new Frame();
            frame.Navigated += Frame_Navigated;
            tab.Frame = frame;
            TabContentArea.Children.Add(frame);
            frame.Visibility = Visibility.Collapsed;
            _tabs.Add(tab);

            frame.Navigate(typeof(HomePage));

            if (activate)
                ActivateTab(tab);

            UpdateCloseButtonVisibility();
        }

        private void ActivateTab(AppTab tab)
        {
            // Workflow gate (WPF parity): while a close/navigation workflow
            // holds an editor's lifecycle, activation must not re-enter it.
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;
            if (tab == null || _activeTab == tab)
                return;

            foreach (var t in _tabs)
            {
                t.IsActive = false;
                if (t.Frame != null)
                    t.Frame.Visibility = Visibility.Collapsed;
                // Hidden editors gate page input + stop the marching-ants
                // timer (WPF SetHostActive parity).
                (t.Frame?.Content as EditorPage)?.SetHostActive(false);
            }

            tab.IsActive = true;
            if (tab.Frame != null)
                tab.Frame.Visibility = Visibility.Visible;
            if (tab.Frame?.Content is EditorPage activeEditor)
            {
                activeEditor.SetHostActive(true);
                // WPF ActivateTab parity: an editor that was prepared for a
                // navigation that never materialized stays admission-closed
                // — reopen it when it becomes the active surface again.
                activeEditor.ResumeDocumentInteraction();
            }
            _activeTab = tab;

            _syncingTabSelection = true;
            try
            {
                TabStrip.SelectedItem = tab;
            }
            finally
            {
                _syncingTabSelection = false;
            }

            UpdateNavButtons();
            UpdateToolbarForActivePage();
        }

        /// <summary>
        /// WinUI has no journal-resident pages: a tab's only live editor is
        /// its Frame.Content.
        /// </summary>
        private static EditorPage GetTabEditor(AppTab tab) => tab?.Frame?.Content as EditorPage;

        /// <summary>
        /// T9 close protocol (WPF CloseTab parity): the tab is not removed
        /// until the editor has persisted its newest generation and released
        /// its native resources. A failed save keeps the tab/document alive
        /// for recovery; a timed-out release continues in the background
        /// while the tab stays guarded.
        /// </summary>
        private async void CloseTab(AppTab tab)
        {
            if (tab == null || !_tabs.Contains(tab))
                return;
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || !_tabCloseWorkflows.Add(tab))
                return;

            EditorPage activeEditor = null;
            var preparedEditors = new List<EditorPage>();
            bool releaseStarted = false;
            bool releaseHandoff = false;
            try
            {
                using var timeout = new CancellationTokenSource(CloseWorkflowTimeout);
                var editor = GetTabEditor(tab);
                if (editor != null)
                {
                    activeEditor = editor;
                    bool wasDirty = editor.IsDirty;
                    if (!await editor.PrepareForCloseAsync(timeout.Token).WaitAsync(timeout.Token))
                    {
                        foreach (var prepared in preparedEditors)
                            TryCancelClosePreparation(prepared);
                        return;
                    }
                    if (wasDirty)
                        ShowToast(LocalizationService.Get("Main.FileAutoSaved"));
                    preparedEditors.Add(editor);
                }

                for (int releaseIndex = 0; releaseIndex < preparedEditors.Count; releaseIndex++)
                {
                    var prepared = preparedEditors[releaseIndex];
                    activeEditor = prepared;
                    releaseStarted = true;
                    Task<bool> releaseTask = prepared.ReleaseResourcesAsync();
                    bool releaseCompleted;
                    try
                    {
                        releaseCompleted = await releaseTask.WaitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException) when (!releaseTask.IsCompleted)
                    {
                        // The underlying release is deliberately not aborted
                        // mid-disposal. Leave the editor admitted as busy so
                        // a later close attempt can join and finish it.
                        releaseHandoff = true;
                        _ = ContinueTimedOutTabCloseAsync(
                            tab,
                            preparedEditors.ToList(),
                            releaseIndex,
                            releaseTask);
                        ShowToast(LocalizationService.Get("Editor.CloseTimedOut"), "", 3500);
                        return;
                    }
                    if (!releaseCompleted)
                    {
                        TryCancelClosePreparation(prepared);
                        return;
                    }
                }

                RemoveTabAfterResourcesReleased(tab);
            }
            catch (Exception ex)
            {
                if (!releaseStarted)
                {
                    foreach (var prepared in preparedEditors)
                        TryCancelClosePreparation(prepared);
                    if (activeEditor != null)
                        TryCancelClosePreparation(activeEditor);
                }
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "", 3500);
            }
            finally
            {
                if (!releaseHandoff)
                    _tabCloseWorkflows.Remove(tab);
            }
        }

        /// <summary>
        /// Completes a tab close after the UI timeout stopped waiting. The
        /// workflow marker remains installed until every native release task
        /// has actually settled, so ActivateTab/re-close cannot re-enter a
        /// partially disposed editor (WPF parity).
        /// </summary>
        private async Task ContinueTimedOutTabCloseAsync(
            AppTab tab,
            IReadOnlyList<EditorPage> preparedEditors,
            int releaseIndex,
            Task<bool> releaseTask)
        {
            try
            {
                if (!await releaseTask.ConfigureAwait(true))
                    throw new InvalidOperationException("The document release did not complete.");

                for (int i = releaseIndex + 1; i < preparedEditors.Count; i++)
                {
                    if (!await preparedEditors[i].ReleaseResourcesAsync().ConfigureAwait(true))
                        throw new InvalidOperationException("The document release did not complete.");
                }

                RemoveTabAfterResourcesReleased(tab);
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "", 3500);
                // Editors after the failed release were only prepared,
                // never admitted to native cleanup — re-open their
                // input/autosave admission while the failed/current
                // release remains blocked for an explicit retry.
                for (int i = releaseIndex + 1; i < preparedEditors.Count; i++)
                    TryCancelClosePreparation(preparedEditors[i]);
                _tabCloseWorkflows.Remove(tab);
            }
        }

        /// <summary>
        /// Per-item close-preparation cancel: a faulted cancel inside a
        /// workflow's cleanup loop must not skip the remaining editors or
        /// escape an async-void caller (no App.UnhandledException backstop).
        /// </summary>
        private static void TryCancelClosePreparation(EditorPage editor)
        {
            try
            {
                editor?.CancelClosePreparation();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MainWindow] CancelClosePreparation faulted: {ex}");
            }
        }

        /// <summary>
        /// Removes a tab only after its editor resources have settled —
        /// never while a release may still be running (WPF parity).
        /// </summary>
        private void RemoveTabAfterResourcesReleased(AppTab tab)
        {
            if (tab?.Frame == null || !_tabs.Contains(tab))
                return;

            tab.Frame.Navigated -= Frame_Navigated;
            TabContentArea.Children.Remove(tab.Frame);
            tab.Frame = null;
            _tabCloseWorkflows.Remove(tab);
            bool wasActive = ReferenceEquals(tab, _activeTab);
            // Suppress the ListView's auto-selection while the item leaves
            // the collection; the explicit activate-below decides what is next.
            _syncingTabSelection = true;
            try
            {
                _tabs.Remove(tab);
            }
            finally
            {
                _syncingTabSelection = false;
            }

            if (_tabs.Count == 0)
            {
                // Always keep at least one tab (WPF parity).
                _activeTab = null;
                AddNewHomeTab(activate: true);
            }
            else if (wasActive)
            {
                _activeTab = null;
                ActivateTab(_tabs[_tabs.Count - 1]);
            }

            UpdateCloseButtonVisibility();
        }

        /// <summary>
        /// Reorders <paramref name="draggedTab"/> relative to
        /// <paramref name="targetTab"/> (WPF MoveTab parity). The ListView's
        /// built-in reorder mutates the collection itself; this helper covers
        /// programmatic moves and the drag-completed verification path.
        /// </summary>
        private bool MoveTab(AppTab draggedTab, AppTab targetTab, bool insertAfter)
        {
            if (draggedTab == null || targetTab == null || ReferenceEquals(draggedTab, targetTab))
                return false;

            int sourceIndex = _tabs.IndexOf(draggedTab);
            int targetIndex = _tabs.IndexOf(targetTab);
            if (sourceIndex < 0 || targetIndex < 0)
                return false;

            // Suppress ListView auto-selection while the collection mutates —
            // otherwise removing the item lets the strip select a neighbor
            // mid-move and SelectionChanged would flip _activeTab.
            _syncingTabSelection = true;
            try
            {
                _tabs.RemoveAt(sourceIndex);
                if (sourceIndex < targetIndex)
                    targetIndex--;

                int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
                insertIndex = Math.Max(0, Math.Min(insertIndex, _tabs.Count));

                _tabs.Insert(insertIndex, draggedTab);
                TabStrip.SelectedItem = _activeTab;
            }
            finally
            {
                _syncingTabSelection = false;
            }

            return true;
        }

        private void UpdateCloseButtonVisibility()
        {
            bool visible = _tabs.Count > 1;
            foreach (var tab in _tabs)
                tab.IsCloseButtonVisible = visible;
        }

        // ── Tab strip events ────────────────────────────────────────────────

        private void TabStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingTabSelection)
                return;
            if (TabStrip.SelectedItem is AppTab tab && !ReferenceEquals(tab, _activeTab))
                ActivateTab(tab);
        }

        private void TabStrip_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
#if DEBUG
            // Smoke seam: proves the pointer drag actually became an item drag
            // (DragItemsCompleted only fires if the gesture got this far).
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opennotes_winui_tabsmoke.log"),
                    $"drag-items-starting count={_tabs.Count}{Environment.NewLine}");
            }
            catch
            {
                // Smoke logging is best-effort only.
            }
#endif
        }

        private void TabStrip_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            // CanReorderItems reorders the bound ObservableCollection in place,
            // so _tabs already matches the visual order here.
#if DEBUG
            // Smoke seam: lets tools/winui-uia-pointer-smoke.ps1 prove a REAL
            // pointer drag reordered the collection (UIA can't distinguish
            // same-titled tabs).
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opennotes_winui_tabsmoke.log"),
                    $"drag-items-completed count={_tabs.Count} order={string.Join(":", _tabs.Select(t => t.Id.Substring(0, 6)))} active={_activeTab?.Id?.Substring(0, 6)}{Environment.NewLine}");
            }
            catch
            {
                // Smoke logging is best-effort only.
            }
#endif
        }

        private void TabItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Middle-click to close (WPF border.MouseDown parity).
            var point = e.GetCurrentPoint(sender as UIElement);
            if (!point.Properties.IsMiddleButtonPressed || _tabs.Count <= 1)
                return;
            if ((sender as FrameworkElement)?.DataContext is not AppTab tab)
                return;

            e.Handled = true;
            CloseTab(tab);
        }

        private void TabItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            // WPF hover: inactive tabs get the control-hover brush.
            if (sender is not Border border ||
                border.DataContext is not AppTab tab ||
                ReferenceEquals(tab, _activeTab))
                return;

            border.Background = ResolveThemeBrush("ThemeControlHoverBrush", "#EEF0F2");
        }

        private void TabItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // ClearValue would null the pill (x:Bind has no BindingExpression
            // to restore) and strip the ACTIVE tab's surface brush after one
            // hover. Re-apply the computed brush — for the active tab that is
            // ThemeSurfaceAltBrush, for inactive ones the inactive brush.
            if (sender is Border border && border.DataContext is AppTab tab)
                border.SetValue(Border.BackgroundProperty, tab.TabBackground);
        }

        private void TabCloseButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // WPF PreviewMouseLeftButtonDown parity: consume the press so the
            // ListView never turns a close click into an activate-then-close.
            var point = e.GetCurrentPoint(sender as UIElement);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            e.Handled = true;
            if (sender is FrameworkElement element && element.DataContext is AppTab tab)
            {
                // Tag the press-target so a trailing Click (if one ever
                // arrives on this same button) cannot re-close a DIFFERENT
                // tab after the ListView recycled the container.
                element.Tag = tab;
                CloseTab(tab);
            }
        }

        private void TabCloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Keyboard activation path (Enter/Space on a focused close button)
            // and a safety net if the press path was skipped. When a press did
            // close a tab, the tag tells us which tab the press targeted —
            // ignore the Click if the container was recycled onto another tab.
            if (sender is not FrameworkElement element || element.DataContext is not AppTab tab)
                return;
            if (element.Tag is AppTab pressedTab)
            {
                element.Tag = null;
                if (!ReferenceEquals(pressedTab, tab))
                    return;
            }
            CloseTab(tab);
        }

        private void NewTab_Click(object sender, RoutedEventArgs e)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;
            AddNewHomeTab(activate: true);
        }

        // ── Navigation (operates on active tab's frame) ─────────────────────

        private void Frame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            if (e.Content is EditorPage navigatedEditor &&
                ReferenceEquals(sender, _activeTab?.Frame))
            {
                // WPF Frame_Navigated parity: a freshly hosted editor on the
                // active tab is host-active; ResumeDocumentInteraction is a
                // no-op for new instances but reopens admission if the same
                // editor ever survives a cancelled navigation.
                navigatedEditor.SetHostActive(true);
                navigatedEditor.ResumeDocumentInteraction();
            }

            if (ReferenceEquals(sender, _activeTab?.Frame))
            {
                UpdateNavButtons();
                UpdateActiveTabInfo();
            }
        }

        private void UpdateNavButtons()
        {
            var frame = ActiveFrame;
            NavBackButton.IsEnabled = frame?.CanGoBack == true;
            NavForwardButton.IsEnabled = frame?.CanGoForward == true;
        }

        private async void NavBack_Click(object sender, RoutedEventArgs e)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;

            _navigationWorkflowActive = true;
            try
            {
                using var timeout = new CancellationTokenSource(CloseWorkflowTimeout);
                EditorPage preparedEditor = ActiveFrame?.Content as EditorPage;
                bool wasDirty = preparedEditor?.IsDirty == true;
                // WinUI destroys the outgoing page (NavigationCacheMode is
                // disabled), so the prepare/save barrier is required even
                // though the WPF journal kept the editor alive.
                bool navigated = await NavigationCloseCoordinator.TryNavigateBackAsync(
                    () => preparedEditor == null
                        ? Task.FromResult(true)
                        : preparedEditor.PrepareForNavigationAsync(timeout.Token),
                    () => ActiveFrame?.CanGoBack == true,
                    () => preparedEditor?.CancelClosePreparation(),
                    () =>
                    {
                        if (ActiveFrame?.Content is EditorPage currentEditor)
                            currentEditor.SetHostActive(false);
                        ActiveFrame?.GoBack();
                        return Task.CompletedTask;
                    });
                if (navigated && wasDirty)
                    ShowToast(LocalizationService.Get("Main.FileAutoSaved"));
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "", 3500);
                if (ActiveFrame?.Content is EditorPage editor)
                    editor.CancelClosePreparation();
            }
            finally
            {
                _navigationWorkflowActive = false;
            }
        }

        private async void NavForward_Click(object sender, RoutedEventArgs e)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;
            if (ActiveFrame?.CanGoForward != true)
                return;

            _navigationWorkflowActive = true;
            try
            {
                // The outgoing editor is destroyed on navigation — persist
                // its newest generation first (see NavBack_Click).
                if (ActiveFrame?.Content is EditorPage editor)
                {
                    using var timeout = new CancellationTokenSource(CloseWorkflowTimeout);
                    bool wasDirty = editor.IsDirty;
                    if (!await editor.PrepareForNavigationAsync(timeout.Token))
                        return;
                    if (wasDirty)
                        ShowToast(LocalizationService.Get("Main.FileAutoSaved"));
                    editor.SetHostActive(false);
                }
                ActiveFrame.GoForward();
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "", 3500);
                if (ActiveFrame?.Content is EditorPage editor)
                    editor.CancelClosePreparation();
            }
            finally
            {
                _navigationWorkflowActive = false;
            }
        }

        private async void NavHome_Click(object sender, RoutedEventArgs e)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0 || ActiveFrame == null)
                return;

            _navigationWorkflowActive = true;
            try
            {
                if (ActiveFrame.Content is EditorPage editor)
                {
                    using var timeout = new CancellationTokenSource(CloseWorkflowTimeout);
                    bool wasDirty = editor.IsDirty;
                    if (!await editor.PrepareForNavigationAsync(timeout.Token))
                        return;
                    if (wasDirty)
                        ShowToast(LocalizationService.Get("Main.FileAutoSaved"));
                    editor.SetHostActive(false);
                }
                ActiveFrame.Navigate(typeof(HomePage));
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "", 3500);
                if (ActiveFrame?.Content is EditorPage failedEditor)
                    failedEditor.CancelClosePreparation();
            }
            finally
            {
                _navigationWorkflowActive = false;
            }
        }

        private void UpdateActiveTabInfo()
        {
            if (_activeTab == null)
                return;
            if (ActiveFrame?.Content is HomePage)
            {
                _activeTab.Title = GetHomeTabTitle();
                _activeTab.Icon = "Home";
                _activeTab.FilePath = null;
            }
            else if (ActiveFrame?.Content is EditorPage editorPage)
            {
                var pdfPath = editorPage.CurrentPdfPath;
                if (!string.IsNullOrWhiteSpace(pdfPath))
                {
                    _activeTab.Title = Path.GetFileNameWithoutExtension(pdfPath);
                    _activeTab.Icon = "FileText";
                    _activeTab.FilePath = pdfPath;
                }
            }
            UpdateToolbarForActivePage();
        }

        // ── Home toolbar (WPF title-bar cluster port) ──────────────────────

        /// <summary>
        /// SearchBox + SelectButton + SortButton live on Home only — the
        /// cluster collapses on editor tabs (WPF showed the cluster but the
        /// buttons are no-ops off-Home; collapsing keeps the chrome honest).
        /// </summary>
        private void UpdateToolbarForActivePage()
        {
            if (HomeToolbarPanel != null)
                HomeToolbarPanel.Visibility =
                    ActiveFrame?.Content is HomePage ? Visibility.Visible : Visibility.Collapsed;
            RefreshSelectButtonVisualState();
        }

        private HomePage GetActiveHomePage() => ActiveFrame?.Content as HomePage;

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            GetActiveHomePage()?.Filter(SearchBox?.Text ?? string.Empty);
        }

        private void SortByName_Click(object sender, RoutedEventArgs e)
        {
            GetActiveHomePage()?.SortByName();
        }

        private void SortByDate_Click(object sender, RoutedEventArgs e)
        {
            GetActiveHomePage()?.SortByDate();
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            var homePage = GetActiveHomePage();
            if (homePage == null)
            {
                // The editor selection-mode branch lands with the Task 6
                // editor port — WPF's EditorPage.IsSelectionMode counterpart.
                return;
            }

            homePage.ToggleSelectionMode();
            ShowToast(homePage.IsSelectionMode
                ? LocalizationService.Get("Main.SelectionEnabled")
                : LocalizationService.Get("Main.SelectionDisabled"), "\uE762");
            RefreshSelectButtonVisualState();
        }

        /// <summary>
        /// Active-state pill on SelectButton — the WinUI stand-in for the WPF
        /// <c>SelectToolbarButtonStyle</c> DataTrigger on IsSelectionMode.
        /// Foregrounds are set on the inner icon/label explicitly so the
        /// accent color lands regardless of the NavButtonStyle hover setters.
        /// </summary>
        public void RefreshSelectButtonVisualState()
        {
            if (SelectButton == null)
                return;

            bool isActive = GetActiveHomePage()?.IsSelectionMode == true;
            SelectButton.Background = isActive
                ? ResolveThemeBrush("ThemeSelectionBrush", "#DBEAFE")
                : new SolidColorBrush(Colors.Transparent);
            var foreground = isActive
                ? ResolveThemeBrush("ThemeAccentBrush", "#2563EB")
                : ResolveThemeBrush("ThemeSubtleForegroundBrush", "#4B5563");
            if (SelectButtonIcon != null)
                SelectButtonIcon.Foreground = foreground;
            if (SelectButtonLabel != null)
                SelectButtonLabel.Foreground = foreground;
        }

        // ── Toast (WPF ShowToast parity) ───────────────────────────────────

        /// <summary>
        /// Transient overlay toast — same contract as the WPF version:
        /// a Lucide icon name OR a raw Segoe MDL2 glyph string, ~2.2 s hold,
        /// fade in/out on Border.Opacity. A new toast cancels the pending
        /// dismissal of the previous one.
        /// </summary>
        public void ShowToast(string message, string iconGlyph = null, int durationMs = 2200)
        {
            if (ToastBorder == null || ToastText == null || ToastIcon == null)
                return;

            _toastCts?.Cancel();
            _toastCts?.Dispose();
            var cts = new CancellationTokenSource();
            _toastCts = cts;

            ToastText.Text = message ?? string.Empty;
            ToastIcon.Glyph = MapToastIconGlyph(iconGlyph);
            ToastBorder.Visibility = Visibility.Visible;

            var fadeIn = WinUiThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(140));
            var fadeOut = WinUiThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(200));
            if (!WinUiThemeService.ShouldAnimate || fadeIn == TimeSpan.Zero)
                ToastBorder.Opacity = 1.0;
            else
                AnimateToastOpacity(1.0, fadeIn, EasingMode.EaseOut);

            _ = DismissToastAfterDelayAsync(cts, durationMs, fadeOut);
        }

        private async Task DismissToastAfterDelayAsync(CancellationTokenSource cts, int durationMs, TimeSpan fadeOut)
        {
            try
            {
                await Task.Delay(durationMs, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!WinUiThemeService.ShouldAnimate || fadeOut == TimeSpan.Zero)
            {
                ToastBorder.Opacity = 0.0;
                ToastBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                AnimateToastOpacity(0.0, fadeOut, EasingMode.EaseIn);
            }
        }

        private void AnimateToastOpacity(double to, TimeSpan duration, EasingMode easingMode)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            };
            Storyboard.SetTarget(animation, ToastBorder);
            Storyboard.SetTargetProperty(animation, "Opacity");
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            if (to == 0.0)
            {
                storyboard.Completed += (_, _) =>
                {
                    if (ToastBorder != null && ToastBorder.Opacity == 0.0)
                        ToastBorder.Visibility = Visibility.Collapsed;
                };
            }
            storyboard.Begin();
        }

        /// <summary>
        /// HomePage toasts pass Lucide icon names (WPF parity) or raw Segoe
        /// MDL2 glyph strings — map the names to glyphs until the icon port
        /// lands. Single/double-char strings are already glyphs and pass
        /// through untouched.
        /// </summary>
        private static string MapToastIconGlyph(string icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
                return "\uE73E"; // Check
            if (icon.Length <= 2)
                return icon;
            return icon switch
            {
                "Trash2" => "\uE74D",
                "Folder" or "FolderOpen" => "\uE8B7",
                "Check" or "CheckCircle" => "\uE73E",
                _ => "\uE73E"
            };
        }

        // ── Editor navigation + rename flow (WPF ports) ────────────────────

        /// <summary>
        /// Opens <paramref name="filePath"/> in the active tab — WPF parity:
        /// promote in recents, retitle the tab, then navigate its Frame to
        /// the editor. T9: the outgoing editor is destroyed by the
        /// navigation (no journal-resident pages), so a dirty document is
        /// persisted through the same prepare barrier as Back/Home first.
        /// </summary>
        public async void NavigateActiveTabToFile(string filePath)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0 ||
                string.IsNullOrWhiteSpace(filePath) || _activeTab == null)
                return;

            _navigationWorkflowActive = true;
            try
            {
                if (ActiveFrame?.Content is EditorPage editor)
                {
                    using var timeout = new CancellationTokenSource(CloseWorkflowTimeout);
                    bool wasDirty = editor.IsDirty;
                    if (!await editor.PrepareForNavigationAsync(timeout.Token))
                        return;
                    if (wasDirty)
                        ShowToast(LocalizationService.Get("Main.FileAutoSaved"));
                    editor.SetHostActive(false);
                }

                RecentFilesService.AddOrPromote(filePath);
                _activeTab.Title = Path.GetFileNameWithoutExtension(filePath);
                _activeTab.Icon = "FileText";
                _activeTab.FilePath = filePath;
                ActiveFrame?.Navigate(typeof(EditorPage), filePath);
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "", 3500);
                if (ActiveFrame?.Content is EditorPage editor)
                    editor.CancelClosePreparation();
            }
            finally
            {
                _navigationWorkflowActive = false;
            }
        }

        /// <summary>
        /// Library-rename flow: retitle/repath every tab whose file moved so
        /// the tab strip and the open editor track the new path (WPF
        /// <c>HandleFilePathChanged</c> parity).
        /// </summary>
        public void HandleFilePathChanged(string oldPath, string newPath)
        {
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
                return;

            foreach (var tab in _tabs)
            {
                if (string.IsNullOrWhiteSpace(tab.FilePath) ||
                    !string.Equals(tab.FilePath, oldPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                tab.FilePath = newPath;
                tab.Title = Path.GetFileNameWithoutExtension(newPath);
                tab.Icon = "FileText";
                if (tab.Frame?.Content is EditorPage editor)
                    editor.UpdateCurrentPdfPath(newPath);
            }

            UpdateActiveTabInfo();
        }

        /// <summary>
        /// WPF <c>OpenFileInNewTab</c>: drop-onto-window and future "open
        /// externally" flows create a file tab rather than hijacking the
        /// active one.
        /// </summary>
        public void OpenFileInNewTab(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;
            // Workflow gate (WPF parity): refuse new tabs while a
            // close/navigation workflow owns the editor lifecycle.
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;

            RecentFilesService.AddOrPromote(filePath);
            var tab = new AppTab
            {
                Title = Path.GetFileNameWithoutExtension(filePath),
                Icon = "FileText",
                FilePath = filePath
            };
            var frame = new Frame();
            frame.Navigated += Frame_Navigated;
            tab.Frame = frame;
            TabContentArea.Children.Add(frame);
            frame.Visibility = Visibility.Collapsed;
            _tabs.Add(tab);
            frame.Navigate(typeof(EditorPage), filePath);
            ActivateTab(tab);
            UpdateCloseButtonVisibility();
        }

        // ── Window-level file drop (WPF Window_Drop parity) ────────────────
        // When the Home page is active it owns file drops on its own surface
        // (WPF ShouldDeferWindowFileDrop); on non-Home pages the window
        // imports each dropped file and opens it in a NEW tab. WinUI drag
        // events bubble: HomePage's root Grid marks accepted drops handled,
        // so these handlers only ever see drops HomePage declined or drops
        // that land while a non-Home page is active.
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (GetActiveHomePage() != null)
                return;

            e.AcceptedOperation = HomePageDragDropHelper.HasStorageItems(e.DataView)
                ? DataPackageOperation.Copy
                : DataPackageOperation.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (GetActiveHomePage() != null)
                return;
            // Workflow gate: a drop that arrives while a close/navigation
            // workflow owns the lifecycle is refused — OpenFileInNewTab is
            // gated anyway, and the conversion work would be wasted.
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;

            var deferral = e.GetDeferral();
            string[] paths;
            try
            {
                paths = await HomePageDragDropHelper.GetDroppedImportablePathsAsync(e.DataView);
            }
            catch (Exception ex)
            {
                // async-void: the deferral must still complete (finally
                // runs on return) and nothing may escape.
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Drop path collection failed: {ex}");
                return;
            }
            finally
            {
                deferral.Complete();
            }

            if (paths.Length == 0)
                return;

            try
            {
                foreach (var path in paths)
                {
                    var pdfPath = await TryImportDroppedDocumentAsync(path);
                    if (!string.IsNullOrWhiteSpace(pdfPath))
                        OpenFileInNewTab(pdfPath);
                }
                e.Handled = true;
            }
            catch (Exception ex)
            {
                // async-void last-resort guard: no App.UnhandledException
                // backstop exists, so nothing may escape.
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Drop processing failed: {ex}");
            }
        }

        /// <summary>WPF <c>TryImportDroppedDocumentAsync</c>: PDFs pass
        /// through untouched; Word docs convert to a sibling PDF first.</summary>
        private async Task<string> TryImportDroppedDocumentAsync(string path)
        {
            if (WordDocumentImport.IsPdfPath(path))
                return path;
            if (!WordDocumentImport.IsWordPath(path))
                return null;

            ShowToast(LocalizationService.Get("Home.ConvertingWord"), "");
            // WPF swapped in a wait cursor via Mouse.OverrideCursor; WinUI's
            // only cursor hook is UIElement.ProtectedCursor — protected, so a
            // Window can't reach it on RootGrid. The toast carries the
            // progress signal instead.
            try
            {
                return await WordToPdfConverter.Default.ImportAsync(path);
            }
            catch (WordConverterNotFoundException)
            {
                await WinUiDialogService.ShowErrorAsync(
                    RootGrid?.XamlRoot,
                    LocalizationService.Get("Common.Error"),
                    LocalizationService.Get("Home.WordConverterMissing"));
                return null;
            }
            catch (Exception ex)
            {
                await WinUiDialogService.ShowErrorAsync(
                    RootGrid?.XamlRoot,
                    LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.WordConvertFailed", Path.GetFileName(path), ex.Message));
                return null;
            }
        }

        // ── More menu: settings + updates + about (WPF ports) ──────────────

        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await OpenSettingsDialogAsync();
            }
            catch (Exception ex)
            {
                // async-void menu handler: no App.UnhandledException backstop.
                Debug.WriteLine($"[MainWindow] Settings dialog faulted: {ex}");
            }
        }

        /// <summary>
        /// WPF <c>PreviewSettings</c> parity: pushes a staged settings
        /// snapshot live — language, theme/backdrop and every open editor's
        /// tool state — without persisting. The settings dialog calls this on
        /// each control change and again with the original snapshot when it
        /// closes unconfirmed.
        /// </summary>
        public void PreviewSettings(AppSettings settings)
        {
            if (settings == null)
                return;
            LocalizationService.ApplyLanguage(settings.Language);
            WinUiThemeService.Apply(settings.Theme, workspaceBackdrop: settings.WorkspaceBackdrop);
            foreach (var editor in _tabs
                .Select(tab => tab?.Frame?.Content)
                .OfType<EditorPage>())
            {
                try
                {
                    editor.ApplySettings(settings);
                }
                catch (Exception ex)
                {
                    // One faulting editor must not fault the preview for the
                    // remaining tabs (or escape into the dialog's handlers).
                    Debug.WriteLine($"[MainWindow] PreviewSettings editor apply faulted: {ex}");
                }
            }
        }

        /// <summary>WPF <c>ApplySettings</c> parity: persist → live-apply.</summary>
        private void ApplySettings(AppSettings settings)
        {
            var savedSettings = AppSettingsService.Save(settings);
            PreviewSettings(savedSettings);
        }

        /// <summary>
        /// WPF <c>OpenSettingsDialog</c> parity — the WPF borderless
        /// <c>SettingsWindow</c> becomes a gated <see cref="SettingsDialog"/>
        /// (ContentDialog); confirm persists + toasts, cancel/dismiss was
        /// already reverted by the dialog's Closed handler and the final
        /// <see cref="PreviewSettings"/> keeps the post-cancel restore
        /// ordering identical to WPF.
        /// </summary>
        private async Task OpenSettingsDialogAsync()
        {
            var xamlRoot = RootGrid?.XamlRoot;
            if (xamlRoot == null)
                return;

            var originalSettings = AppSettingsService.Load();
            var dialog = new SettingsDialog(originalSettings) { XamlRoot = xamlRoot };
            await WinUiDialogService.RunUnderDialogGateAsync(
                () => dialog.ShowAsync().AsTask());

            if (dialog.SelectedSettings != null)
            {
                ApplySettings(dialog.SelectedSettings);
                ShowToast(LocalizationService.Get("Main.SettingsSaved"), "");
                return;
            }

            PreviewSettings(originalSettings);
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdateCheckInProgress)
                return;

            _isUpdateCheckInProgress = true;
            if (CheckForUpdatesMenuItem != null)
            {
                CheckForUpdatesMenuItem.IsEnabled = false;
                CheckForUpdatesMenuItem.Text = LocalizationService.Get("Main.CheckingForUpdates");
            }

            var requestCts = new CancellationTokenSource();
            _updateCheckCts = requestCts;
            try
            {
                Version installedVersion = typeof(App).Assembly.GetName().Version
                    ?? Version.Parse(ProductInfo.Version);
                UpdateCheckResult result = await _updateCheckService.CheckAsync(installedVersion, requestCts.Token);
                string installedDisplay = FormatDisplayVersion(result.InstalledVersion);
                var xamlRoot = RootGrid?.XamlRoot;

                if (!result.IsUpdateAvailable)
                {
                    await WinUiDialogService.ShowInfoAsync(
                        xamlRoot,
                        LocalizationService.Get("Main.UpToDateTitle"),
                        string.Format(LocalizationService.CurrentCulture,
                            LocalizationService.Get("Main.UpToDateMessage"), installedDisplay));
                    return;
                }

                bool? openRelease = await WinUiDialogService.ShowDialogAsync(
                    xamlRoot,
                    LocalizationService.Get("Main.UpdateAvailableTitle"),
                    string.Format(LocalizationService.CurrentCulture,
                        LocalizationService.Get("Main.UpdateAvailableMessage"),
                        installedDisplay, FormatDisplayVersion(result.LatestVersion)),
                    LocalizationService.Get("Common.Cancel"),
                    LocalizationService.Get("Main.ViewRelease"));
                if (openRelease == true)
                {
                    try
                    {
                        OpenTrustedReleasePage(result.ReleaseUri);
                    }
                    catch (UpdateCheckException)
                    {
                        throw; // untrusted release URI — still a check failure
                    }
                    catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
                    {
                        // The update CHECK succeeded — Process.Start found no
                        // browser/association for the release page.
                        Debug.WriteLine($"[MainWindow] Release page launch failed: {ex}");
                        await WinUiDialogService.ShowErrorAsync(
                            xamlRoot,
                            LocalizationService.Get("Common.Error"),
                            LocalizationService.Get("Main.OpenReleaseFailed"));
                    }
                }
            }
            catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
            {
                // Window closed mid-check — nothing to report.
            }
            catch (Exception ex) when (ex is UpdateCheckException or Win32Exception)
            {
                await WinUiDialogService.ShowErrorAsync(
                    RootGrid?.XamlRoot,
                    LocalizationService.Get("Main.UpdateCheckFailedTitle"),
                    GetUpdateCheckFailureMessage(ex));
            }
            catch (Exception ex)
            {
                // async-void last-resort guard: an unexpected failure must
                // not escape (no App.UnhandledException backstop). Surface
                // the generic update-check error where the dialog allows.
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Update check faulted: {ex}");
                try
                {
                    await WinUiDialogService.ShowErrorAsync(
                        RootGrid?.XamlRoot,
                        LocalizationService.Get("Main.UpdateCheckFailedTitle"),
                        ex.Message);
                }
                catch (Exception dialogEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Update-check error dialog faulted: {dialogEx}");
                }
            }
            finally
            {
                requestCts.Dispose();
                if (ReferenceEquals(_updateCheckCts, requestCts))
                    _updateCheckCts = null;
                _isUpdateCheckInProgress = false;
                if (CheckForUpdatesMenuItem != null)
                {
                    CheckForUpdatesMenuItem.IsEnabled = true;
                    CheckForUpdatesMenuItem.Text = LocalizationService.Get("Main.CheckForUpdates");
                }
            }
        }

        private static string FormatDisplayVersion(Version version)
        {
            if (version == null)
                return "0.0.0";
            return version.Revision > 0 ? version.ToString(4) : version.ToString(3);
        }

        private static string GetUpdateCheckFailureMessage(Exception ex)
        {
            if (ex is UpdateCheckException update)
            {
                return update.Kind switch
                {
                    UpdateCheckFailureKind.Network => LocalizationService.Get("Main.UpdateCheckFailedNetwork"),
                    UpdateCheckFailureKind.Timeout => LocalizationService.Get("Main.UpdateCheckFailedTimeout"),
                    UpdateCheckFailureKind.HttpStatus => LocalizationService.Get("Main.UpdateCheckFailedHttp"),
                    UpdateCheckFailureKind.InvalidResponse => LocalizationService.Get("Main.UpdateCheckFailedInvalid"),
                    _ => LocalizationService.Get("Main.UpdateCheckFailedMessage")
                };
            }

            return LocalizationService.Get("Main.UpdateCheckFailedMessage");
        }

        private static void OpenTrustedReleasePage(Uri releaseUri)
        {
            if (!UpdateCheckService.IsTrustedReleaseUri(releaseUri))
            {
                throw new UpdateCheckException(
                    UpdateCheckFailureKind.InvalidResponse,
                    "The release URL is not trusted.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = releaseUri.AbsoluteUri,
                UseShellExecute = true
            });
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await WinUiDialogService.ShowInfoAsync(
                    RootGrid?.XamlRoot,
                    LocalizationService.Get("Main.AboutTitle"),
                    LocalizationService.Get("Main.AboutMessage"));
            }
            catch (System.Exception ex)
            {
                // async-void click handler: no App.UnhandledException backstop.
                System.Diagnostics.Debug.WriteLine($"[MainWindow] About faulted: {ex}");
            }
        }

        // ── Keyboard shortcuts (WPF MainWindow_KeyDown parity) ──────────────

        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            if (!ctrl.HasFlag(CoreVirtualKeyStates.Down))
                return;

            if (e.Key == VirtualKey.T)
            {
                if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                    return;
                AddNewHomeTab(activate: true);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.W && _tabs.Count > 0)
            {
                CloseTab(_activeTab);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.F && ActiveFrame?.Content is EditorPage editor)
            {
                // Ctrl+F must reach the editor regardless of which chrome
                // element currently holds focus (the WPF shell treats it as a
                // window-level accelerator).
                editor.OpenSearchPanel();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Tab && _tabs.Count > 1)
            {
                // ActivateTab refuses during close/navigation workflows —
                // skip the arithmetic too so the shortcut stays consistent.
                if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                    return;
                int currentIndex = Math.Max(0, _tabs.IndexOf(_activeTab));
                var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
                bool backwards = shift.HasFlag(CoreVirtualKeyStates.Down);
                int nextIndex = (currentIndex + (backwards ? -1 : 1) + _tabs.Count) % _tabs.Count;
                ActivateTab(_tabs[nextIndex]);
                e.Handled = true;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private string GetHomeTabTitle() => LocalizationService.Get("Main.HomeTabTitle");

        private static Brush ResolveThemeBrush(string key, string fallbackHex)
        {
            if (Application.Current?.Resources?.TryGetValue(key, out var value) == true &&
                value is Brush brush)
                return brush;
            return WinUiThemeService.CreateBrush(fallbackHex);
        }

        // ── Debug smoke hook (Debug builds only) ────────────────────────────

#if DEBUG
        /// <summary>
        /// Code-driven verification seam for environments without UI
        /// automation: `OpenNotes.WinUI.exe --tabsmoke` exercises the tab
        /// model (add / activate / reorder / close) ~800ms after activation
        /// and appends a line-delimited state log to
        /// %TEMP%\opennotes_winui_tabsmoke.log. No production behavior change.
        /// </summary>
        private void MaybeRunTabSmoke()
        {
            var args = Environment.GetCommandLineArgs();
            if (args == null || !args.Any(a => string.Equals(a, "--tabsmoke", StringComparison.Ordinal)))
                return;

            var timer = this.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(800);
            timer.IsRepeating = false;
            timer.Tick += (s, e) => RunTabSmoke();
            timer.Start();
        }

        private void RunTabSmoke()
        {
            var lines = new List<string>
            {
                $"tabs-start={_tabs.Count}",
                $"active-start={_activeTab?.Title}"
            };

            AddNewHomeTab(activate: true);
            AddNewHomeTab(activate: true);
            lines.Add($"after-add={_tabs.Count}");

            ActivateTab(_tabs[0]);
            lines.Add($"active-after-activate-first={_tabs.IndexOf(_activeTab)}");

            // Exercise the same reorder semantics as ListView drag reorder.
            var first = _tabs[0];
            var last = _tabs[_tabs.Count - 1];
            MoveTab(first, last, insertAfter: true);
            lines.Add($"after-move-first-to-end={string.Join(":", _tabs.Select(t => t.Title))}");
            lines.Add($"first-now-at={_tabs.IndexOf(first)}");

            CloseTab(_tabs[0]);
            lines.Add($"after-close={_tabs.Count}");
            lines.Add($"active-final={_activeTab?.Title}");

            try
            {
                System.IO.File.AppendAllLines(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opennotes_winui_tabsmoke.log"),
                    lines);
            }
            catch
            {
                // Smoke logging is best-effort only.
            }
        }
#else
        private void MaybeRunTabSmoke() { }
#endif
    }
}
