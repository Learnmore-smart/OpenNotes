using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Pages;
using Caelum.Services;

namespace Caelum
{
    public partial class MainWindow : Window
    {
        private const int WM_GETMINMAXINFO = 0x0024;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const string TabDragDataFormat = "Caelum.AppTab";

        private readonly List<AppTab> _tabs = new List<AppTab>();
        private AppTab _activeTab;
        private Point _tabDragStartPoint;
        private AppTab _tabDragCandidate;
        private bool _isTabDragInProgress;
        private bool _windowCloseWorkflowActive;
        private bool _allowWindowClose;
        private bool _navigationWorkflowActive;
        private readonly HashSet<AppTab> _tabCloseWorkflows = new HashSet<AppTab>();
        // A Frame navigation journal can keep an EditorPage behind a HomePage.
        // Track every editor seen by that frame so tab/window close releases
        // hidden native documents as well as the currently visible content.
        private readonly Dictionary<Frame, HashSet<EditorPage>> _frameEditors =
            new Dictionary<Frame, HashSet<EditorPage>>();
        private CancellationTokenSource _windowCloseCts;
        private CancellationTokenSource _toastCts;
        private static readonly TimeSpan CloseWorkflowTimeout = TimeSpan.FromSeconds(30);

        public MainWindow()
            : this(createHomeTab: true)
        {
        }

        internal MainWindow(bool createHomeTab)
        {
            InitializeComponent();
            LoadAppIcon();
            TabDragCoordinator.Register(this);
            SourceInitialized += MainWindow_SourceInitialized;
            StateChanged += MainWindow_StateChanged;
            Deactivated += MainWindow_Deactivated;
            KeyDown += MainWindow_KeyDown;
            TitleBarBorder.MouseLeftButtonDown += (sender, args) => DragMove();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Closed += (_, __) =>
            {
                Deactivated -= MainWindow_Deactivated;
                LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
                TabDragCoordinator.Unregister(this);
                TabBar.DragOver -= TabBar_DragOver;
                TabBar.Drop -= TabBar_Drop;
                PopupZOrderHelper.UnfixContextMenuTopmost(SortContextMenu);
                PopupZOrderHelper.UnfixContextMenuTopmost(MoreContextMenu);
            };
            var startupSettings = AppSettingsService.Load();
            ThemeService.Apply(startupSettings.Theme, workspaceBackdrop: startupSettings.WorkspaceBackdrop);
            ApplyLocalization();

            // Popups must not float above other applications after Alt-Tab (Task 10)
            PopupZOrderHelper.FixContextMenuTopmost(SortContextMenu);
            PopupZOrderHelper.FixContextMenuTopmost(MoreContextMenu);

            TabBar.AllowDrop = true;
            TabBar.DragOver += TabBar_DragOver;
            TabBar.Drop += TabBar_Drop;

            // The normal application window starts with Home.  Detached
            // windows receive an existing AppTab after construction so the
            // original Frame/editor state is preserved.
            if (createHomeTab)
                AddNewHomeTab(activate: true);
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            SortContextMenu.IsOpen = false;
            MoreContextMenu.IsOpen = false;
            // Popup HWNDs are detached from the Frame visual tree. Sweep every
            // retained editor (including journal entries hidden behind Home)
            // so an OpenNotes popup can never remain above another app.
            var editors = _frameEditors.Values
                .SelectMany(editorsForFrame => editorsForFrame)
                .Concat(_tabs.SelectMany(tab => GetFrameEditors(tab.Frame)))
                .Distinct()
                .ToList();

            foreach (var editor in editors)
            {
                editor.CancelInteraction("window deactivated");
                editor.CloseTransientUi("window deactivated");
            }
        }

        private void LoadAppIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app-icon.ico");
                if (!File.Exists(iconPath)) return;
                using var fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read);
                var decoder = new IconBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                // Pick the largest frame 鈥?preserves 32-bit ARGB transparency
                var best = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                Icon = best;
            }
            catch
            {
                // Fall back silently 鈥?window will use default icon
            }
        }

        // 鈹€鈹€鈹€ Drag & Drop 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        private static readonly string[] SupportedDropExtensions = { ".pdf" };

        private bool HasSupportedFiles(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            return files != null && files.Any(f =>
                SupportedDropExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        }

        private bool ShouldDeferWindowFileDrop(DragEventArgs e)
        {
            return ActiveFrame?.Content is HomePage home &&
                   home.ShouldDeferWindowFileDrop(e.OriginalSource as DependencyObject, e.Data);
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (IsTabDragOverTabStrip(e))
                return;
            if (TabDragCoordinator.TryGetPayload(e.Data, out _))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (ShouldDeferWindowFileDrop(e))
                return;

            e.Effects = HasSupportedFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (IsTabDragOverTabStrip(e))
                return;
            if (TabDragCoordinator.TryGetPayload(e.Data, out _))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (ShouldDeferWindowFileDrop(e))
                return;

            e.Effects = HasSupportedFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (IsTabDragOverTabStrip(e) || TabDragCoordinator.TryGetPayload(e.Data, out _))
                return;

            if (ShouldDeferWindowFileDrop(e))
                return;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null) return;

            var pdfFiles = files.Where(f =>
                SupportedDropExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();

            if (pdfFiles.Count == 0) return;

            // If the active tab is on the Home page, open the first file in-place
            bool isHomePage = ActiveFrame?.Content is HomePage;
            bool first = true;

            foreach (var file in pdfFiles)
            {
                if (first && isHomePage)
                {
                    // Open directly in the current Home tab
                    NavigateActiveTabToFile(file);
                    first = false;
                }
                else
                {
                    OpenFileInNewTab(file);
                    first = false;
                }
            }
            e.Handled = true;
        }

        private bool IsTabDragOverTabStrip(DragEventArgs e)
        {
            return TabDragCoordinator.TryGetPayload(e.Data, out _) &&
                   IsDescendantOf(e.OriginalSource as DependencyObject, TabBar);
        }

        // 鈹€鈹€鈹€ Tab Management 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        private Frame ActiveFrame => _activeTab?.Frame;
        internal bool IsActiveContent(object content) => ReferenceEquals(ActiveFrame?.Content, content);

        private IReadOnlyList<EditorPage> GetFrameEditors(Frame frame)
        {
            return frame != null && _frameEditors.TryGetValue(frame, out var editors)
                ? editors.ToList()
                : Array.Empty<EditorPage>();
        }

        public void AddNewHomeTab(bool activate = true)
        {
            var tab = new AppTab { Title = GetHomeTabTitle(), Icon = "\uE80F" };
            var frame = new Frame
            {
                NavigationUIVisibility = NavigationUIVisibility.Hidden,
                AllowDrop = true,
                Background = Brushes.Transparent
            };
            frame.Navigated += Frame_Navigated;
            tab.Frame = frame;
            TabContentArea.Children.Add(frame);
            _tabs.Add(tab);
            RebuildTabBar();

            frame.Navigate(new HomePage());

            if (activate)
                ActivateTab(tab);
        }

        private sealed class TabTransferState
        {
            public AppTab Tab { get; set; }
            public Frame Frame { get; set; }
            public HashSet<EditorPage> Editors { get; set; }
            public int SourceIndex { get; set; }
            public bool WasActive { get; set; }
        }

        /// <summary>
        /// Removes a tab from this window without closing or recreating its
        /// Frame.  The editor map and Navigated subscription travel with the
        /// same transfer transaction and are each moved exactly once.
        /// </summary>
        private bool TransferTabTo(
            MainWindow destination,
            AppTab tab,
            AppTab targetTab,
            bool insertAfter,
            TabDragPayload payload)
        {
            if (destination == null || destination == this || tab == null ||
                !_tabs.Contains(tab) || tab.Frame == null ||
                _windowCloseWorkflowActive || _navigationWorkflowActive ||
                _tabCloseWorkflows.Count > 0 ||
                destination._windowCloseWorkflowActive || destination._navigationWorkflowActive ||
                destination._tabCloseWorkflows.Count > 0)
            {
                return false;
            }

            if (payload != null && !ReferenceEquals(payload.Tab, tab))
                return false;

            if (ReferenceEquals(tab, _activeTab) && tab.Frame.Content is EditorPage activeEditor)
            {
                activeEditor.CancelInteraction("tab transfer");
                activeEditor.CloseTransientUi("tab transfer");
                activeEditor.SetHostActive(false);
            }

            var state = RemoveTabForTransfer(tab);
            if (state == null)
                return false;

            try
            {
                if (!destination.AttachTransferredTab(state, targetTab, insertAfter))
                {
                    RestoreTabAfterTransferFailure(state);
                    return false;
                }

                CompleteSourceTransfer(state);
                if (payload != null)
                    TabDragCoordinator.AcceptDrop(payload);
                return true;
            }
            catch
            {
                RestoreTabAfterTransferFailure(state);
                return false;
            }
        }

        private TabTransferState RemoveTabForTransfer(AppTab tab)
        {
            int sourceIndex = _tabs.IndexOf(tab);
            if (sourceIndex < 0 || tab.Frame == null)
                return null;

            var frame = tab.Frame;
            var editors = _frameEditors.TryGetValue(frame, out var knownEditors)
                ? knownEditors
                : new HashSet<EditorPage>();
            _frameEditors.Remove(frame);
            frame.Navigated -= Frame_Navigated;
            TabContentArea.Children.Remove(frame);

            bool wasActive = ReferenceEquals(tab, _activeTab);
            _tabs.RemoveAt(sourceIndex);
            tab.IsActive = false;
            frame.Visibility = Visibility.Collapsed;
            if (wasActive)
                _activeTab = null;

            RebuildTabBar();
            return new TabTransferState
            {
                Tab = tab,
                Frame = frame,
                Editors = editors,
                SourceIndex = sourceIndex,
                WasActive = wasActive
            };
        }

        private bool AttachTransferredTab(TabTransferState state, AppTab targetTab, bool insertAfter)
        {
            if (state?.Tab == null || state.Frame == null || _tabs.Contains(state.Tab) ||
                _windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
            {
                return false;
            }

            int insertIndex = _tabs.Count;
            if (targetTab != null)
            {
                int targetIndex = _tabs.IndexOf(targetTab);
                if (targetIndex < 0)
                    return false;
                insertIndex = targetIndex + (insertAfter ? 1 : 0);
            }

            state.Frame.Navigated += Frame_Navigated;
            try
            {
                TabContentArea.Children.Add(state.Frame);
                // This assignment is the destination half of the one-time
                // map move performed by RemoveTabForTransfer.
                _frameEditors[state.Frame] = state.Editors ?? new HashSet<EditorPage>();
                _tabs.Insert(Math.Min(insertIndex, _tabs.Count), state.Tab);
                state.Tab.IsActive = false;
                state.Frame.Visibility = Visibility.Collapsed;
                RebuildTabBar();
                ActivateTab(state.Tab);
                return true;
            }
            catch
            {
                state.Frame.Navigated -= Frame_Navigated;
                _frameEditors.Remove(state.Frame);
                TabContentArea.Children.Remove(state.Frame);
                _tabs.Remove(state.Tab);
                return false;
            }
        }

        private void CompleteSourceTransfer(TabTransferState state)
        {
            if (_tabs.Count == 0)
            {
                // A source window remains usable after its last tab moves.
                AddNewHomeTab(activate: true);
            }
            else if (state.WasActive)
            {
                int nextIndex = Math.Min(state.SourceIndex, _tabs.Count - 1);
                ActivateTab(_tabs[nextIndex]);
            }
            else
            {
                RebuildTabBar();
                UpdateNavButtons();
            }
        }

        private void RestoreTabAfterTransferFailure(TabTransferState state)
        {
            if (state?.Tab == null || state.Frame == null || _tabs.Contains(state.Tab))
                return;

            state.Frame.Navigated += Frame_Navigated;
            TabContentArea.Children.Add(state.Frame);
            _frameEditors[state.Frame] = state.Editors ?? new HashSet<EditorPage>();
            int insertIndex = Math.Max(0, Math.Min(state.SourceIndex, _tabs.Count));
            _tabs.Insert(insertIndex, state.Tab);
            state.Frame.Visibility = Visibility.Collapsed;
            state.Tab.IsActive = false;
            RebuildTabBar();

            if (state.WasActive)
            {
                _activeTab = null;
                ActivateTab(state.Tab);
            }
            else
            {
                UpdateNavButtons();
            }
        }

        private void DetachTabToNewWindow(AppTab tab)
        {
            if (tab == null || !_tabs.Contains(tab))
                return;

            var detached = new MainWindow(createHomeTab: false)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = Width,
                Height = Height,
                Left = Left + 32,
                Top = Top + 32
            };

            detached.Show();
            if (!TransferTabTo(detached, tab, targetTab: null, insertAfter: true, payload: null))
            {
                detached._allowWindowClose = true;
                detached.Close();
                return;
            }

            detached.Activate();
        }

        public void OpenFileInNewTab(string filePath, bool promptSaveAsAfterLoad = false, string pendingLibraryFolderId = null, bool isNotebookDraft = false)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;
            RecentFilesService.AddOrPromote(filePath);

            var name = Path.GetFileNameWithoutExtension(filePath);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            string icon = ext == ".pdf" ? "\uEA90" : "\uE7C3"; // PDF icon or generic document

            var tab = new AppTab { Title = name, Icon = icon, FilePath = filePath };
            var frame = new Frame
            {
                NavigationUIVisibility = NavigationUIVisibility.Hidden,
                AllowDrop = true,
                Background = Brushes.Transparent
            };
            frame.Navigated += Frame_Navigated;
            tab.Frame = frame;
            TabContentArea.Children.Add(frame);
            _tabs.Add(tab);
            RebuildTabBar();

            frame.Navigate(new EditorPage(filePath, promptSaveAsAfterLoad, pendingLibraryFolderId, isNotebookDraft));
            ActivateTab(tab);
        }

        private void ActivateTab(AppTab tab)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;
            if (_activeTab == tab) return;

            if (_activeTab?.Frame?.Content is EditorPage previousEditor)
            {
                previousEditor.CloseTransientUi("tab switch");
                previousEditor.SetHostActive(false);
            }

            foreach (var t in _tabs)
            {
                t.IsActive = false;
                if (t.Frame != null)
                    t.Frame.Visibility = Visibility.Collapsed;
            }

            tab.IsActive = true;
            tab.Frame.Visibility = Visibility.Visible;
            _activeTab = tab;

            if (tab.Frame.Content is EditorPage activeEditor)
            {
                activeEditor.SetHostActive(WindowState != WindowState.Minimized);
                if (WindowState != WindowState.Minimized)
                    activeEditor.ResumeDocumentInteraction();
            }

            UpdateNavButtons();
            RebuildTabBar();
            RefreshSelectButtonVisualState();
        }

        private async void CloseTab(AppTab tab)
        {
            if (tab == null || !_tabs.Contains(tab))
                return;
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || !_tabCloseWorkflows.Add(tab))
                return;

            // A tab is not removed until the editor has persisted its newest
            // generation and released native resources. A failed save keeps
            // the tab/document alive for recovery.
            EditorPage activeEditor = null;
            var preparedEditors = new List<EditorPage>();
            bool releaseStarted = false;
            bool releaseHandoff = false;
            try
            {
                using var timeout = new CancellationTokenSource(CloseWorkflowTimeout);
                var editors = GetFrameEditors(tab.Frame).ToList();
                if (tab.Frame?.Content is EditorPage currentEditor && !editors.Contains(currentEditor))
                    editors.Add(currentEditor);

                foreach (var editor in editors)
                {
                    activeEditor = editor;
                    bool wasDirty = editor.IsDirty;
                    if (!await editor.PrepareForCloseAsync(timeout.Token))
                    {
                        foreach (var prepared in preparedEditors)
                            prepared.CancelClosePreparation();
                        return;
                    }
                    if (wasDirty)
                        ShowToast(LocalizationService.Get("Main.FileAutoSaved"));
                    preparedEditors.Add(editor);
                }

                for (int releaseIndex = 0; releaseIndex < preparedEditors.Count; releaseIndex++)
                {
                    var editor = preparedEditors[releaseIndex];
                    activeEditor = editor;
                    releaseStarted = true;
                    Task<bool> releaseTask = editor.ReleaseResourcesAsync();
                    bool releaseCompleted;
                    try
                    {
                        releaseCompleted = await releaseTask.WaitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException) when (!releaseTask.IsCompleted)
                    {
                        // The underlying release is deliberately not aborted
                        // mid-disposal.  Leave the editor admitted as busy so
                        // a later close attempt can join and finish it.
                        releaseHandoff = true;
                        _ = ContinueTimedOutTabCloseAsync(
                            tab,
                            preparedEditors.ToList(),
                            releaseIndex,
                            releaseTask);
                        ShowToast(LocalizationService.Get("Editor.SaveFailed"), "\uE783", 3500);
                        return;
                    }
                    if (!releaseCompleted)
                    {
                        editor.CancelClosePreparation();
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
                        prepared.CancelClosePreparation();
                    activeEditor?.CancelClosePreparation();
                }
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "\uE783", 3500);
            }
            finally
            {
                if (!releaseHandoff)
                    _tabCloseWorkflows.Remove(tab);
            }
        }

        /// <summary>
        /// Completes a tab close after the UI timeout stopped waiting.  The
        /// workflow marker remains installed until every native release task
        /// has actually settled, so ActivateTab/re-close cannot re-enter a
        /// partially disposed editor.
        /// </summary>
        private async Task ContinueTimedOutTabCloseAsync(
            AppTab tab,
            IReadOnlyList<EditorPage> preparedEditors,
            int releaseIndex,
            Task<bool> releaseTask)
        {
            try
            {
                if (!await releaseTask.ConfigureAwait(false))
                    throw new InvalidOperationException("The document release did not complete.");

                for (int i = releaseIndex + 1; i < preparedEditors.Count; i++)
                {
                    if (!await preparedEditors[i].ReleaseResourcesAsync().ConfigureAwait(false))
                        throw new InvalidOperationException("The document release did not complete.");
                }

                await Dispatcher.InvokeAsync(
                    () => RemoveTabAfterResourcesReleased(tab),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "\uE783", 3500);
                        // Editors after the failed release were only prepared,
                        // never admitted to native cleanup.  Re-open their
                        // input/autosave admission, while the failed/current
                        // release remains blocked for an explicit retry.
                        for (int i = releaseIndex + 1; i < preparedEditors.Count; i++)
                            preparedEditors[i].CancelClosePreparation();
                    },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                // ReleaseResourcesAsync retains the editor's blocked/failed
                // state.  Removing only the workflow marker permits an
                // explicit retry without enabling the editor implicitly.
                await Dispatcher.InvokeAsync(
                    () => _tabCloseWorkflows.Remove(tab),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private void RemoveTabAfterResourcesReleased(AppTab tab)
        {
            if (tab?.Frame == null || !_tabs.Contains(tab))
                return;

            tab.Frame.Navigated -= Frame_Navigated;
            TabContentArea.Children.Remove(tab.Frame);
            _tabs.Remove(tab);
            _frameEditors.Remove(tab.Frame);
            _tabCloseWorkflows.Remove(tab);

            if (_tabs.Count == 0)
            {
                // Always keep at least one tab.
                AddNewHomeTab(activate: true);
            }
            else if (tab == _activeTab)
            {
                ActivateTab(_tabs.Last());
            }

            RebuildTabBar();
        }

        private void RebuildTabBar()
        {
            TabBar.Children.Clear();

            foreach (var tab in _tabs)
            {
                var tabButton = CreateTabButton(tab);
                TabBar.Children.Add(tabButton);
            }
        }

        private static Brush GetThemeBrush(string key, Brush fallback)
        {
            return Application.Current?.TryFindResource(key) as Brush ?? fallback;
        }

        private static void UseThemeBrush(FrameworkElement element, DependencyProperty property, string key)
        {
            if (element == null || Application.Current?.TryFindResource(key) == null)
                return;

            // Keep a DynamicResource expression so an in-place settings preview
            // updates existing tab chrome when ThemeService swaps the palette.
            element.SetResourceReference(property, key);
        }

        private Border CreateTabButton(AppTab tab)
        {
            bool isActive = tab == _activeTab;
            var activeForeground = GetThemeBrush("ThemeForegroundBrush", SystemColors.ControlTextBrush);
            var inactiveForeground = GetThemeBrush("ThemeSubtleForegroundBrush", SystemColors.GrayTextBrush);
            var activeBackground = GetThemeBrush("ThemeSurfaceAltBrush", SystemColors.WindowBrush);
            var activeBorderBrush = GetThemeBrush("ThemeBorderBrush", SystemColors.ActiveBorderBrush);
            var transparentBackground = Brushes.Transparent;

            // Tab content: icon + title + close button
            var icon = new LucideIcon
            {
                Kind = tab.Icon,
                Width = 14,
                Height = 14,
                Stroke = isActive ? activeForeground : inactiveForeground,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            UseThemeBrush(icon, System.Windows.Shapes.Shape.StrokeProperty, isActive ? "ThemeForegroundBrush" : "ThemeSubtleForegroundBrush");

            var title = new TextBlock
            {
                Text = tab.Title.Length > 20 ? tab.Title.Substring(0, 17) + "..." : tab.Title,
                FontSize = 13,
                Foreground = isActive ? activeForeground : inactiveForeground,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 132,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = isActive ? FontWeights.Medium : FontWeights.Normal
            };
            UseThemeBrush(title, TextBlock.ForegroundProperty, isActive ? "ThemeForegroundBrush" : "ThemeSubtleForegroundBrush");

            var closeIcon = new LucideIcon
            {
                Kind = "X",
                Width = 12,
                Height = 12,
                Stroke = inactiveForeground
            };
            UseThemeBrush(closeIcon, System.Windows.Shapes.Shape.StrokeProperty, "ThemeSubtleForegroundBrush");

            var closeBtn = new Button
            {
                Content = closeIcon,
                Width = 20,
                Height = 20,
                Background = transparentBackground,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = _tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed,
                Opacity = isActive ? 1 : 0.72,
                ToolTip = LocalizationService.Get("Main.CloseTabTooltip")
            };

            // Close button template with hover
            var closeBtnTemplate = new ControlTemplate(typeof(Button));
            var closeBorder = new FrameworkElementFactory(typeof(Border));
            closeBorder.SetValue(Border.BackgroundProperty, transparentBackground);
            closeBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            closeBorder.Name = "CloseBg";
            var closeContent = new FrameworkElementFactory(typeof(ContentPresenter));
            closeContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            closeContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            closeBorder.AppendChild(closeContent);
            closeBtnTemplate.VisualTree = closeBorder;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                new DynamicResourceExtension("ThemeControlHoverBrush"),
                "CloseBg"));
            closeBtnTemplate.Triggers.Add(hoverTrigger);

            closeBtn.Template = closeBtnTemplate;

            var capturedTab = tab;
            closeBtn.Click += (s, e) => { e.Handled = true; CloseTab(capturedTab); };

            var panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.Margin = new Thickness(10, 0, 8, 0);
            panel.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(icon);
            panel.Children.Add(title);
            panel.Children.Add(closeBtn);

            var border = new Border
            {
                Child = panel,
                Background = isActive ? activeBackground : transparentBackground,
                BorderBrush = isActive ? activeBorderBrush : transparentBackground,
                BorderThickness = isActive ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 4, 0),
                Height = 32,
                MinWidth = 72,
                AllowDrop = true,
                Focusable = true,
                ToolTip = tab.Title,
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };
            KeyboardNavigation.SetIsTabStop(border, true);

            if (isActive)
            {
                UseThemeBrush(border, Border.BackgroundProperty, "ThemeSurfaceAltBrush");
                UseThemeBrush(border, Border.BorderBrushProperty, "ThemeBorderBrush");
            }

            border.MouseEnter += (s, e) =>
            {
                if (capturedTab != _activeTab)
                {
                    UseThemeBrush(border, Border.BackgroundProperty, "ThemeControlHoverBrush");
                }

                closeBtn.Opacity = 1;
            };

            border.MouseLeave += (s, e) =>
            {
                if (capturedTab != _activeTab)
                {
                    border.Background = transparentBackground;
                    border.BorderBrush = transparentBackground;
                }

                closeBtn.Opacity = capturedTab == _activeTab ? 1 : 0.72;
            };

            border.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject source && IsDescendantOf(source, closeBtn))
                    return;

                ActivateTab(capturedTab);
                _tabDragCandidate = capturedTab;
                _tabDragStartPoint = e.GetPosition(TabBar);
            };

            border.PreviewMouseMove += (s, e) =>
            {
                if (_isTabDragInProgress ||
                    _tabDragCandidate != capturedTab ||
                    e.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }

                var currentPosition = e.GetPosition(TabBar);
                if (!HasExceededDragThreshold(_tabDragStartPoint, currentPosition))
                    return;

                _isTabDragInProgress = true;
                TabDragPayload dragPayload = null;
                DragDropEffects dragResult = DragDropEffects.None;
                QueryContinueDragEventHandler queryContinueDrag = null;
                try
                {
                    dragPayload = TabDragCoordinator.BeginDrag(this, capturedTab);
                    var dragData = new DataObject();
                    dragData.SetData(TabDragCoordinator.DragDataFormat, dragPayload);
                    // Keep the legacy ID format for same-window callers and
                    // older automation payload readers.
                    dragData.SetData(TabDragDataFormat, capturedTab.Id);
                    queryContinueDrag = (querySender, queryArgs) =>
                    {
                        if (queryArgs.EscapePressed || queryArgs.Action == DragAction.Cancel)
                            TabDragCoordinator.CancelDrag(dragPayload);
                    };
                    border.QueryContinueDrag += queryContinueDrag;
                    dragResult = DragDrop.DoDragDrop(border, dragData, DragDropEffects.Move);
                }
                finally
                {
                    if (queryContinueDrag != null)
                        border.QueryContinueDrag -= queryContinueDrag;
                    _isTabDragInProgress = false;
                    if (_tabDragCandidate == capturedTab)
                        _tabDragCandidate = null;
                }

                if (dragPayload != null && TabDragCoordinator.CompleteDrag(dragPayload, dragResult))
                    DetachTabToNewWindow(capturedTab);

                e.Handled = true;
            };

            border.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_tabDragCandidate == capturedTab)
                    _tabDragCandidate = null;
            };

            border.GotKeyboardFocus += (s, e) =>
            {
                UseThemeBrush(border, Border.BorderBrushProperty, "ThemeFocusBrush");
                border.BorderThickness = new Thickness(2);
            };

            border.LostKeyboardFocus += (s, e) =>
            {
                border.BorderThickness = capturedTab == _activeTab ? new Thickness(1) : new Thickness(0);
                if (capturedTab == _activeTab)
                    UseThemeBrush(border, Border.BorderBrushProperty, "ThemeBorderBrush");
                else
                    border.BorderBrush = transparentBackground;
            };

            border.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    ActivateTab(capturedTab);
                    e.Handled = true;
                }
            };

            // Middle-click to close
            border.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle && _tabs.Count > 1)
                {
                    e.Handled = true;
                    CloseTab(capturedTab);
                }
            };

            border.DragOver += (s, e) =>
            {
                if (TryGetDraggedTab(e.Data, out var draggedTab, out var sourceWindow, out _)
                    && draggedTab != capturedTab && sourceWindow != null)
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }

                e.Effects = DragDropEffects.None;
                e.Handled = true;
            };

            border.Drop += (s, e) =>
            {
                if (!TryGetDraggedTab(e.Data, out var draggedTab, out var sourceWindow, out var payload))
                    return;

                if (draggedTab == capturedTab)
                {
                    if (payload != null)
                        TabDragCoordinator.AcceptDrop(payload);
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }

                bool insertAfter = e.GetPosition(border).X >= border.ActualWidth / 2;
                bool moved = sourceWindow == this
                    ? MoveTab(draggedTab, capturedTab, insertAfter)
                    : sourceWindow?.TransferTabTo(this, draggedTab, capturedTab, insertAfter, payload) == true;
                if (moved)
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            };

            return border;
        }

        private void TabBar_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetDraggedTab(e.Data, out var draggedTab, out var sourceWindow, out _))
                return;

            if (draggedTab == null || sourceWindow == null)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void TabBar_Drop(object sender, DragEventArgs e)
        {
            if (!TryGetDraggedTab(e.Data, out var draggedTab, out var sourceWindow, out var payload))
                return;

            if (sourceWindow == this)
            {
                // A blank portion of the same strip is a valid no-op target,
                // so the source must not mistake it for an outside drop.
                if (payload != null)
                    TabDragCoordinator.AcceptDrop(payload);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            if (sourceWindow?.TransferTabTo(this, draggedTab, targetTab: null, insertAfter: true, payload: payload) == true)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private bool TryGetDraggedTab(IDataObject data, out AppTab tab)
            => TryGetDraggedTab(data, out tab, out _, out _);

        private bool TryGetDraggedTab(
            IDataObject data,
            out AppTab tab,
            out MainWindow sourceWindow,
            out TabDragPayload payload)
        {
            tab = null;
            sourceWindow = this;
            payload = null;

            if (TabDragCoordinator.TryGetPayload(data, out payload))
            {
                tab = payload.Tab;
                sourceWindow = payload.SourceWindow;
                return tab != null;
            }

            if (data == null || !data.GetDataPresent(TabDragDataFormat))
                return false;

            var tabId = data.GetData(TabDragDataFormat) as string;
            if (string.IsNullOrWhiteSpace(tabId))
                return false;

            tab = _tabs.FirstOrDefault(candidate => string.Equals(candidate.Id, tabId, StringComparison.Ordinal));
            return tab != null;
        }

        private bool MoveTab(AppTab draggedTab, AppTab targetTab, bool insertAfter)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return false;
            if (draggedTab == null || targetTab == null || draggedTab == targetTab)
                return false;

            int sourceIndex = _tabs.IndexOf(draggedTab);
            int targetIndex = _tabs.IndexOf(targetTab);
            if (sourceIndex < 0 || targetIndex < 0)
                return false;

            _tabs.RemoveAt(sourceIndex);
            if (sourceIndex < targetIndex)
                targetIndex--;

            int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
            insertIndex = Math.Max(0, Math.Min(insertIndex, _tabs.Count));

            _tabs.Insert(insertIndex, draggedTab);
            RebuildTabBar();
            return true;
        }

        private static bool HasExceededDragThreshold(Point startPoint, Point currentPoint)
        {
            return Math.Abs(currentPoint.X - startPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                   Math.Abs(currentPoint.Y - startPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
        }

        private static bool IsDescendantOf(DependencyObject descendant, DependencyObject ancestor)
        {
            var current = descendant;
            while (current != null)
            {
                if (current == ancestor)
                    return true;

                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }

            return false;
        }

        // 鈹€鈹€鈹€ Navigation (operates on active tab's frame) 鈹€鈹€

        private void Frame_Navigated(object sender, NavigationEventArgs e)
        {
            if (e.Content is EditorPage navigatedEditor)
            {
                if (sender is Frame navigatedFrame)
                {
                    if (!_frameEditors.TryGetValue(navigatedFrame, out var editors))
                    {
                        editors = new HashSet<EditorPage>();
                        _frameEditors[navigatedFrame] = editors;
                    }

                    editors.Add(navigatedEditor);
                }

                bool isActiveEditor = sender == _activeTab?.Frame && WindowState != WindowState.Minimized;
                navigatedEditor.SetHostActive(isActiveEditor);
                if (isActiveEditor)
                    navigatedEditor.ResumeDocumentInteraction();
            }

            if (sender == _activeTab?.Frame)
            {
                UpdateNavButtons();
                UpdateActiveTabInfo();
                RefreshSelectButtonVisualState();
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
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "\uE783", 3500);
                if (ActiveFrame?.Content is EditorPage editor)
                    editor.CancelClosePreparation();
            }
            finally
            {
                _navigationWorkflowActive = false;
            }
        }

        private void NavForward_Click(object sender, RoutedEventArgs e)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;
            if (ActiveFrame?.CanGoForward == true)
            {
                ActiveFrame.GoForward();
            }
        }

        private async void NavHome_Click(object sender, RoutedEventArgs e)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0 || ActiveFrame == null)
                return;

            _navigationWorkflowActive = true;
            try
            {
                using var timeout = new CancellationTokenSource(CloseWorkflowTimeout);
                if (ActiveFrame.Content is EditorPage editor)
                {
                    bool wasDirty = editor.IsDirty;
                    if (!await editor.PrepareForNavigationAsync(timeout.Token))
                        return;
                    if (wasDirty)
                        ShowToast(LocalizationService.Get("Main.FileAutoSaved"));
                    editor.SetHostActive(false);
                }
                ActiveFrame.Navigate(new HomePage());
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "\uE783", 3500);
                if (ActiveFrame?.Content is EditorPage editor)
                    editor.CancelClosePreparation();
            }
            finally
            {
                _navigationWorkflowActive = false;
            }
        }

        private void NewTab_Click(object sender, RoutedEventArgs e)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                return;
            AddNewHomeTab(activate: true);
        }

        private void UpdateActiveTabInfo()
        {
            if (_activeTab == null) return;
            if (ActiveFrame?.Content is HomePage)
            {
                _activeTab.Title = GetHomeTabTitle();
                _activeTab.Icon = "\uE80F";
                _activeTab.FilePath = null;
            }
            else if (ActiveFrame?.Content is EditorPage ep && !string.IsNullOrEmpty(ep.CurrentPdfPath))
            {
                _activeTab.Title = Path.GetFileNameWithoutExtension(ep.CurrentPdfPath);
                _activeTab.FilePath = ep.CurrentPdfPath;
                _activeTab.Icon = Path.GetExtension(ep.CurrentPdfPath).ToLowerInvariant() == ".pdf" ? "\uEA90" : "\uE7C3";
            }
            RebuildTabBar();
        }

        public void RefreshActiveTabInfo()
        {
            UpdateActiveTabInfo();
        }

        // Called by HomePage/EditorPage to navigate the current tab to a file
        public void NavigateActiveTabToFile(string filePath, bool promptSaveAsAfterLoad = false, string pendingLibraryFolderId = null, bool isNotebookDraft = false)
        {
            if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0 || _activeTab == null)
                return;

            RecentFilesService.AddOrPromote(filePath);

            var name = Path.GetFileNameWithoutExtension(filePath);
            _activeTab.Title = name;
            _activeTab.FilePath = filePath;
            _activeTab.Icon = Path.GetExtension(filePath).ToLowerInvariant() == ".pdf" ? "\uEA90" : "\uE7C3";
            if (ActiveFrame?.Content is EditorPage currentEditor)
                currentEditor.SetHostActive(false);
            ActiveFrame?.Navigate(new EditorPage(filePath, promptSaveAsAfterLoad, pendingLibraryFolderId, isNotebookDraft));
            RebuildTabBar();
        }

        public void HandleFilePathChanged(string oldPath, string newPath)
        {
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
                return;

            foreach (var tab in _tabs.Where(tab =>
                         !string.IsNullOrWhiteSpace(tab.FilePath) &&
                         string.Equals(tab.FilePath, oldPath, StringComparison.OrdinalIgnoreCase)))
            {
                tab.FilePath = newPath;
                tab.Title = Path.GetFileNameWithoutExtension(newPath);
                tab.Icon = Path.GetExtension(newPath).ToLowerInvariant() == ".pdf" ? "\uEA90" : "\uE7C3";

                if (tab.Frame?.Content is EditorPage editor)
                    editor.UpdateCurrentPdfPath(newPath);
            }

            if (_activeTab != null &&
                !string.IsNullOrWhiteSpace(_activeTab.FilePath) &&
                string.Equals(_activeTab.FilePath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateActiveTabInfo();
            }
            else
            {
                RebuildTabBar();
            }
        }

        public void HandleActiveTabFilePathChanged(EditorPage sourceEditor, string oldPath, string newPath)
        {
            if (sourceEditor == null || string.IsNullOrWhiteSpace(newPath))
                return;

            var tab = _tabs.FirstOrDefault(candidate =>
                ReferenceEquals(candidate.Frame?.Content, sourceEditor) ||
                GetFrameEditors(candidate.Frame).Contains(sourceEditor));
            if (tab == null)
                return;

            tab.FilePath = newPath;
            tab.Title = Path.GetFileNameWithoutExtension(newPath);
            tab.Icon = Path.GetExtension(newPath).ToLowerInvariant() == ".pdf" ? "\uEA90" : "\uE7C3";
            if (ReferenceEquals(tab, _activeTab))
                UpdateActiveTabInfo();
            else
                RebuildTabBar();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.T)
                {
                    if (_windowCloseWorkflowActive || _navigationWorkflowActive || _tabCloseWorkflows.Count > 0)
                        return;
                    AddNewHomeTab(activate: true);
                    e.Handled = true;
                }
                else if (e.Key == Key.W && _tabs.Count > 0)
                {
                    CloseTab(_activeTab);
                    e.Handled = true;
                }
                else if (e.Key == Key.Tab && _tabs.Count > 1)
                {
                    int currentIndex = Math.Max(0, _tabs.IndexOf(_activeTab));
                    bool backwards = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                    int nextIndex = (currentIndex + (backwards ? -1 : 1) + _tabs.Count) % _tabs.Count;
                    ActivateTab(_tabs[nextIndex]);
                    e.Handled = true;
                }
            }
        }

        // 鈹€鈹€鈹€ Window State 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            MaximizeIcon.Kind = WindowState == WindowState.Maximized ? "Restore" : "Square";
            if (ActiveFrame?.Content is EditorPage activeEditor)
            {
                activeEditor.SetHostActive(WindowState != WindowState.Minimized);
                if (WindowState != WindowState.Minimized)
                    activeEditor.ResumeDocumentInteraction();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_allowWindowClose)
            {
                base.OnClosing(e);
                return;
            }

            // WPF does not await an async override. Cancel this attempt and
            // complete the save/release protocol before requesting Close()
            // again, otherwise the process can exit while an older snapshot
            // is still in flight.
            e.Cancel = true;
            if (_windowCloseWorkflowActive)
                return;

            _windowCloseWorkflowActive = true;
            _windowCloseCts?.Dispose();
            _windowCloseCts = new CancellationTokenSource(CloseWorkflowTimeout);
            _ = CompleteWindowCloseAsync(_windowCloseCts.Token);
        }

        private async Task CompleteWindowCloseAsync(CancellationToken cancellationToken)
        {
            var preparedEditors = new List<EditorPage>();
            var releasesStarted = new HashSet<EditorPage>();
            bool releaseHandoff = false;
            try
            {
                var allEditors = _tabs
                    .SelectMany(tab => GetFrameEditors(tab.Frame))
                    .Distinct()
                    .ToList();
                foreach (var tab in _tabs)
                {
                    if (tab.Frame?.Content is EditorPage currentEditor && !allEditors.Contains(currentEditor))
                        allEditors.Add(currentEditor);
                }

                foreach (var editor in allEditors)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!await editor.PrepareForCloseAsync(cancellationToken))
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
                    }
                    catch (OperationCanceledException) when (!releaseTask.IsCompleted)
                    {
                        // Keep the window workflow active while the native
                        // release continues in the background.  A second
                        // OnClosing attempt is cancelled and cannot race a
                        // second DisposeAsync call.
                        releaseHandoff = true;
                        _ = ContinueTimedOutWindowCloseAsync(
                            preparedEditors.ToList(),
                            releaseIndex,
                            releaseTask);
                        return;
                    }
                }

                _allowWindowClose = true;
                await Dispatcher.InvokeAsync(
                    Close,
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "\uE783", 3500);
            }
            finally
            {
                if (!_allowWindowClose)
                {
                    foreach (var editor in preparedEditors)
                    {
                        if (!releasesStarted.Contains(editor))
                            editor.CancelClosePreparation();
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
        /// Finishes a window close after its bounded UI wait elapsed.  The
        /// guard and all prepared editors remain busy until this task settles.
        /// </summary>
        private async Task ContinueTimedOutWindowCloseAsync(
            IReadOnlyList<EditorPage> preparedEditors,
            int releaseIndex,
            Task<bool> releaseTask)
        {
            try
            {
                if (!await releaseTask.ConfigureAwait(false))
                    throw new InvalidOperationException("The document release did not complete.");

                for (int i = releaseIndex + 1; i < preparedEditors.Count; i++)
                {
                    if (!await preparedEditors[i].ReleaseResourcesAsync().ConfigureAwait(false))
                        throw new InvalidOperationException("The document release did not complete.");
                }

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        _allowWindowClose = true;
                        Close();
                    },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        ShowToast(LocalizationService.Format("Editor.SaveFailed", ex.Message), "\uE783", 3500);
                        // The suffix was prepared but not yet released.  A
                        // timeout/failure must not strand those editors in a
                        // close-preparation state or leave them half-detached.
                        for (int i = releaseIndex + 1; i < preparedEditors.Count; i++)
                            preparedEditors[i].CancelClosePreparation();
                        _windowCloseWorkflowActive = false;
                        _windowCloseCts?.Dispose();
                        _windowCloseCts = null;
                    },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);

            // Enforce transparent icon at Win32 level
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app-icon.ico");
                if (File.Exists(iconPath))
                {
                    const int IMAGE_ICON = 1;
                    const int LR_LOADFROMFILE = 0x00000010;
                    const int LR_SHARED = 0x00008000;
                    const int WM_SETICON = 0x0080;
                    const int ICON_SMALL = 0;
                    const int ICON_BIG = 1;

                    // Fetch the absolute maximum icons internally to force extreme clarity and size
                    IntPtr hIconSmall = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 64, 64, LR_LOADFROMFILE | LR_SHARED);
                    IntPtr hIconBig = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 256, 256, LR_LOADFROMFILE | LR_SHARED);

                    if (hIconSmall != IntPtr.Zero) SendMessage(handle, WM_SETICON, (IntPtr)ICON_SMALL, hIconSmall);
                    if (hIconBig != IntPtr.Zero) SendMessage(handle, WM_SETICON, (IntPtr)ICON_BIG, hIconBig);
                }
            }
            catch { }

            EnableAcrylicBlur(handle);

            // Allow drag-drop messages through UIPI (fixes blocked drops when running elevated)
            ChangeWindowMessageFilterEx(handle, 0x0233, 1, IntPtr.Zero); // WM_DROPFILES
            ChangeWindowMessageFilterEx(handle, 0x004A, 1, IntPtr.Zero); // WM_COPYDATA
            ChangeWindowMessageFilterEx(handle, 0x0049, 1, IntPtr.Zero); // WM_COPYGLOBALDATA
            DragDrop.AddPreviewDropHandler(this, Window_Drop);
            DragDrop.AddPreviewDragOverHandler(this, Window_DragOver);
            DragDrop.AddPreviewDragEnterHandler(this, Window_DragEnter);
        }



        // 鈹€鈹€鈹€ Header Toolbar Buttons 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                UpdateMaximizedBounds(hwnd, lParam);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private static void UpdateMaximizedBounds(IntPtr hwnd, IntPtr lParam)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return;

            var monitorInfo = new MONITORINFO
            {
                cbSize = Marshal.SizeOf<MONITORINFO>()
            };

            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return;

            var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var workArea = monitorInfo.rcWork;
            var monitorArea = monitorInfo.rcMonitor;

            minMaxInfo.ptMaxPosition.x = workArea.left - monitorArea.left;
            minMaxInfo.ptMaxPosition.y = workArea.top - monitorArea.top;
            minMaxInfo.ptMaxSize.x = workArea.right - workArea.left;
            minMaxInfo.ptMaxSize.y = workArea.bottom - workArea.top;
            minMaxInfo.ptMaxTrackSize.x = minMaxInfo.ptMaxSize.x;
            minMaxInfo.ptMaxTrackSize.y = minMaxInfo.ptMaxSize.y;

            Marshal.StructureToPtr(minMaxInfo, lParam, false);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ActiveFrame?.Content is HomePage home)
            {
                home.Filter(SearchBox.Text);
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveFrame?.Content is HomePage home)
            {
                home.ToggleSelectionMode();
                RefreshSelectButtonVisualState();
                ShowToast(home.IsSelectionMode ? LocalizationService.Get("Main.SelectionEnabled") : LocalizationService.Get("Main.SelectionDisabled"), "\uE762");
                return;
            }

            if (ActiveFrame?.Content is EditorPage editor)
            {
                editor.ToggleSelectionMode();
                RefreshSelectButtonVisualState();
                ShowToast(editor.IsSelectionMode ? LocalizationService.Get("Main.SelectionEnabled") : LocalizationService.Get("Main.SelectionDisabled"), "\uE762");
            }
        }

        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void SortByName_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveFrame?.Content is HomePage home)
            {
                home.SortByName();
            }
        }

        private void SortByDate_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveFrame?.Content is HomePage home)
            {
                home.SortByDate();
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsDialog();
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            await DialogService.ShowInfoAsync(this, LocalizationService.Get("Main.AboutTitle"), LocalizationService.Get("Main.AboutMessage"));
        }

        // 鈹€鈹€鈹€ Toast 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        private enum ToastIconKind
        {
            Check
        }

        public async void ShowToast(string message, string icon = null, int durationMs = 2500)
        {
            _toastCts?.Cancel();
            _toastCts?.Dispose();
            _toastCts = new CancellationTokenSource();
            CancellationToken toastToken = _toastCts.Token;

            ToastIcon.Kind = string.IsNullOrWhiteSpace(icon) ? nameof(ToastIconKind.Check) : icon;
            ToastText.Text = message;
            ToastBorder.Visibility = Visibility.Visible;

            TimeSpan fadeInDuration = ThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(220));
            if (fadeInDuration == TimeSpan.Zero)
            {
                ToastBorder.BeginAnimation(OpacityProperty, null);
                ToastBorder.Opacity = 1;
            }
            else
            {
                var fadeIn = new DoubleAnimation(0, 1, fadeInDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ToastBorder.BeginAnimation(OpacityProperty, fadeIn);
            }

            try
            {
                await Task.Delay(durationMs, toastToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            TimeSpan fadeOutDuration = ThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(350));
            if (fadeOutDuration == TimeSpan.Zero)
            {
                ToastBorder.BeginAnimation(OpacityProperty, null);
                ToastBorder.Opacity = 0;
                ToastBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                var fadeOut = new DoubleAnimation(1, 0, fadeOutDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += (s, ev) => ToastBorder.Visibility = Visibility.Collapsed;
                ToastBorder.BeginAnimation(OpacityProperty, fadeOut);
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, int uType, int cxDesired, int cyDesired, int fuLoad);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        // 鈹€鈹€鈹€ Acrylic Blur (Glassmorphism) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        private void EnableAcrylicBlur(IntPtr handle)
        {
            // Solid white background — no acrylic/DWM backdrop needed
            int backdropType = 1; // DWMWCP_DEFAULT
            DwmSetWindowAttribute(handle, 38, ref backdropType, Marshal.SizeOf(typeof(int)));
        }
    }
}
