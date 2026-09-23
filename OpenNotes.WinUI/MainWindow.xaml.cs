using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace Caelum
{
    /// <summary>
    /// V6 WinUI main window. This step ports the chrome + tab model slice of
    /// the WPF <c>MainWindow.xaml.cs</c>: the <c>_tabs</c> list, new/activate/
    /// close/reorder semantics, caption buttons through
    /// <see cref="AppWindow"/>/<see cref="OverlappedPresenter"/>, and the theme
    /// apply on startup. Editor-tab navigation, save/close workflows, toasts,
    /// and the toolbar clusters land in later tasks.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly ObservableCollection<AppTab> _tabs = new ObservableCollection<AppTab>();
        private AppTab _activeTab;
        private bool _syncingTabSelection;

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

            InitializeAppWindow();
            ApplyStartupSettings();
            ApplyLocalization();

            WinUiThemeService.RegisterWindow(this);
            WinUiThemeService.ThemeApplied += WinUiThemeService_ThemeApplied;
            this.Closed += MainWindow_Closed;

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
                _appWindow.Changed += AppWindow_Changed;

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
        }

        private void UpdateMaximizeGlyph()
        {
            bool maximized = _presenter?.State == OverlappedPresenterState.Maximized;
            // ChromeMaximize / ChromeRestore stand-ins for the Lucide
            // Square/Restore icons until the icon port lands.
            MaximizeIcon.Glyph = maximized ? "\uE923" : "\uE922";
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
        }

        private void WinUiThemeService_ThemeApplied(object sender, EventArgs e)
        {
            // Tab chrome binds to brush values computed by AppTab; re-resolve
            // them so a palette swap cannot leave stale brushes painted.
            foreach (var tab in _tabs)
                tab.RefreshVisualState();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            WinUiThemeService.ThemeApplied -= WinUiThemeService_ThemeApplied;
            if (_appWindow != null)
                _appWindow.Changed -= AppWindow_Changed;
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
            var tab = new AppTab { Title = GetHomeTabTitle(), Icon = "Home" };
            var frame = new Frame();
            frame.Navigated += Frame_Navigated;
            tab.Frame = frame;
            TabContentArea.Children.Add(frame);
            frame.Visibility = Visibility.Collapsed;
            _tabs.Add(tab);

            frame.Navigate(typeof(HomePlaceholderPage));

            if (activate)
                ActivateTab(tab);

            UpdateCloseButtonVisibility();
        }

        private void ActivateTab(AppTab tab)
        {
            if (tab == null || _activeTab == tab)
                return;

            foreach (var t in _tabs)
            {
                t.IsActive = false;
                if (t.Frame != null)
                    t.Frame.Visibility = Visibility.Collapsed;
            }

            tab.IsActive = true;
            if (tab.Frame != null)
                tab.Frame.Visibility = Visibility.Visible;
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
        }

        /// <summary>
        /// Closes a tab and drops its Frame. The WPF version runs the editor
        /// save/release workflow first; that protocol arrives with the editor
        /// port (Task 9) — a tab close here can never strand a dirty document
        /// because editor tabs do not exist yet.
        /// </summary>
        private void CloseTab(AppTab tab)
        {
            if (tab == null || !_tabs.Contains(tab))
                return;

            if (tab.Frame != null)
            {
                tab.Frame.Navigated -= Frame_Navigated;
                TabContentArea.Children.Remove(tab.Frame);
                // Release the page tree now — the WPF port keeps the Frame
                // only while the tab lives.
                tab.Frame = null;
            }
            bool wasActive = ReferenceEquals(tab, _activeTab);
            // Suppress the ListView's auto-selection while the item leaves the
            // collection; the explicit activate-below decides what is next.
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
            AddNewHomeTab(activate: true);
        }

        // ── Navigation (operates on active tab's frame) ─────────────────────

        private void Frame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
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

        private void NavBack_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveFrame?.CanGoBack == true)
                ActiveFrame.GoBack();
        }

        private void NavForward_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveFrame?.CanGoForward == true)
                ActiveFrame.GoForward();
        }

        private void NavHome_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveFrame == null)
                return;
            ActiveFrame.Navigate(typeof(HomePlaceholderPage));
        }

        private void UpdateActiveTabInfo()
        {
            if (_activeTab == null)
                return;
            if (ActiveFrame?.Content is HomePlaceholderPage)
            {
                _activeTab.Title = GetHomeTabTitle();
                _activeTab.Icon = "Home";
                _activeTab.FilePath = null;
            }
            // EditorPage branch arrives with the editor port.
        }

        // ── Keyboard shortcuts (WPF MainWindow_KeyDown parity) ──────────────

        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            if (!ctrl.HasFlag(CoreVirtualKeyStates.Down))
                return;

            if (e.Key == VirtualKey.T)
            {
                AddNewHomeTab(activate: true);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.W && _tabs.Count > 0)
            {
                CloseTab(_activeTab);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Tab && _tabs.Count > 1)
            {
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
