using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caelum.Models;
using Caelum.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using PdfService = Caelum.Pdf.PdfService;

namespace Caelum.Pages
{
    /// <summary>
    /// WinUI port of the WPF <c>Pages/HomePage.xaml.cs</c> — the library home
    /// surface: tile grid (add/folder/file), search + sort, per-folder colors,
    /// selection mode, recycle-bin delete, export, Word→PDF import, tile
    /// drag-into-folder and external file-drop import.
    ///
    /// Platform swaps vs WPF (see also the XAML header comment):
    /// <list type="bullet">
    /// <item><c>ContextMenu</c> → per-show <see cref="MenuFlyout"/> built at
    /// runtime (auto-localizes; no stale open-menu refresh — the WPF
    /// <c>TrackOpenContextMenu</c>/<c>RefreshOpenContextMenus</c> machinery is
    /// intentionally not ported).</item>
    /// <item><c>OpenFileDialog</c>/<c>SaveFileDialog</c> →
    /// <see cref="FileOpenPicker"/>/<see cref="FileSavePicker"/> +
    /// <see cref="InitializeWithWindow"/> on the host hwnd.</item>
    /// <item><c>PromptForInput</c> borderless window →
    /// <see cref="ContentDialog"/> (<see cref="PromptForInputAsync"/>).</item>
    /// <item><c>DragDrop.DoDragDrop</c> → <c>CanDrag</c> +
    /// <c>DragStarting</c> on the file tile button; payload carries the
    /// <c>Caelum.LibraryTilePath[s]</c> custom formats plus
    /// <see cref="StandardDataFormats.StorageItems"/> for Explorer
    /// drag-out.</item>
    /// <item><c>CollectionViewSource</c> filter/sort →
    /// <see cref="VisibleTiles"/> rebuilt by <see cref="RebuildVisibleTiles"/>
    /// (WinUI has no collection-view filter/sort).</item>
    /// <item>Wheel animation rides <see cref="CompositionTarget.Rendering"/>
    /// + <see cref="ScrollViewer.ChangeView"/>, same math as WPF.</item>
    /// </list>
    /// </summary>
    public sealed partial class HomePage : Page
    {
        private enum HomeSortMode
        {
            Date,
            Name
        }

        /// <summary>All tiles in the current folder (unfiltered, unsorted).</summary>
        public ObservableCollection<HomeTile> HomeTiles { get; } = new ObservableCollection<HomeTile>();

        /// <summary>Filtered + sorted view bound by the repeater.</summary>
        public ObservableCollection<HomeTile> VisibleTiles { get; } = new ObservableCollection<HomeTile>();

        private bool _libraryLoaded;
        private string _currentFolderId = string.Empty;
        private string _currentFolderName = string.Empty;
        private string _searchQuery = string.Empty;
        private HomeSortMode _currentSortMode = HomeSortMode.Date;
        private bool _languageChangedSubscribed;

        public bool IsSelectionMode { get; private set; }

        public HomePage()
        {
            InitializeComponent();
            ApplyLocalization();
            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;
        }

        // ── Hover scale animation (WPF TileScale_MouseEnter/Leave port) ─────

        private void TileButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
            {
                AnimateTileScale(button, isHovered: true);
                if (button.Tag is HomeTile tile)
                    tile.IsHovered = true;
            }
        }

        private void TileButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
            {
                AnimateTileScale(button, isHovered: false);
                if (button.Tag is HomeTile tile)
                    tile.IsHovered = false;
            }
        }

        /// <summary>
        /// Same eased scale-in/out as WPF <c>AnimateTileScale</c>: folder icons
        /// grow 1.06×, add/file tiles 1.08×, cubic ease-out, honoring
        /// <see cref="WinUiThemeService.ShouldAnimate"/> /
        /// <see cref="WinUiThemeService.GetAnimationDuration"/>. The icon grid
        /// is the Button's direct content here (WPF dug it out of a custom
        /// ControlTemplate; the simplified template keeps it as content).
        /// </summary>
        private static void AnimateTileScale(Button button, bool isHovered)
        {
            if (button?.Content is not FrameworkElement target ||
                target.RenderTransform is not ScaleTransform scale)
                return;

            double targetScale = isHovered
                ? (target.Name == "FolderIconGrid" ? 1.06 : 1.08)
                : 1.0;

            var duration = WinUiThemeService.GetAnimationDuration(
                TimeSpan.FromMilliseconds(isHovered ? 200 : 300));
            if (!WinUiThemeService.ShouldAnimate || duration == TimeSpan.Zero)
            {
                scale.ScaleX = targetScale;
                scale.ScaleY = targetScale;
                return;
            }

            var storyboard = new Storyboard();
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            foreach (var property in new[] { "ScaleX", "ScaleY" })
            {
                var animation = new DoubleAnimation
                {
                    To = targetScale,
                    Duration = duration,
                    EasingFunction = easing,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(animation, scale);
                Storyboard.SetTargetProperty(animation, property);
                storyboard.Children.Add(animation);
            }
            storyboard.Begin();
        }

        // ── Folder navigation state ─────────────────────────────────────────

        public bool IsInsideFolder => !string.IsNullOrWhiteSpace(_currentFolderId);

        /// <summary>Breadcrumb row visibility (WPF BooleanToVisibilityConverter site).</summary>
        public Visibility BreadcrumbBarVisibility =>
            IsInsideFolder ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Selection action-bar visibility.</summary>
        public Visibility SelectionBarVisibility =>
            IsSelectionMode ? Visibility.Visible : Visibility.Collapsed;

        public string NavigateUpText => LocalizationService.Get("Home.NavigateUp");

        public string FolderBreadcrumb
        {
            get
            {
                if (!IsInsideFolder)
                    return string.Empty;

                var names = new List<string>();
                var cursor = RecentFilesService.GetFolder(_currentFolderId);
                while (cursor != null)
                {
                    names.Add(cursor.DisplayName);
                    cursor = string.IsNullOrWhiteSpace(cursor.ParentFolderId)
                        ? null
                        : RecentFilesService.GetFolder(cursor.ParentFolderId);
                }

                names.Reverse();
                names.Insert(0, LocalizationService.Get("Home.LibraryRoot"));
                return string.Join(" / ", names);
            }
        }


        /// <summary>
        /// Debug-only diagnostic log for the home smoke driver — the
        /// [Conditional] attribute strips every call site from Release builds,
        /// matching the repo's tabsmoke logging convention.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        internal static void HomeSmokeLog(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opennotes_winui_home.log"),
                    $"{msg}{Environment.NewLine}");
            }
            catch
            {
                // Best-effort only.
            }
        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_languageChangedSubscribed)
            {
                LocalizationService.LanguageChanged += HomePage_LanguageChanged;
                _languageChangedSubscribed = true;
            }

            ApplyLocalization();
            await EnsureLibraryLoadedAsync();
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_languageChangedSubscribed)
            {
                LocalizationService.LanguageChanged -= HomePage_LanguageChanged;
                _languageChangedSubscribed = false;
            }
        }

        private void HomePage_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private async Task EnsureLibraryLoadedAsync()
        {
            if (_libraryLoaded)
            {
                await RefreshCurrentFolderAsync();
                return;
            }

            await RefreshCurrentFolderAsync();
            _libraryLoaded = true;
        }

        public void Filter(string query)
        {
            _searchQuery = query?.Trim() ?? string.Empty;
            RebuildVisibleTiles();
            RefreshSelectionState();
        }

        public void SortByName()
        {
            _currentSortMode = HomeSortMode.Name;
            RebuildVisibleTiles();
        }

        public void SortByDate()
        {
            _currentSortMode = HomeSortMode.Date;
            RebuildVisibleTiles();
        }

        private async Task RefreshCurrentFolderAsync()
        {
            var selectionState = HomeTiles
                .Where(tile => tile.IsFile && tile.IsSelected)
                .ToDictionary(tile => tile.Path, tile => tile.IsSelected, StringComparer.OrdinalIgnoreCase);

            var activeFolder = string.IsNullOrWhiteSpace(_currentFolderId) ? null : RecentFilesService.GetFolder(_currentFolderId);
            if (!string.IsNullOrWhiteSpace(_currentFolderId) && activeFolder == null)
            {
                _currentFolderId = string.Empty;
                _currentFolderName = string.Empty;
            }
            else
            {
                _currentFolderName = activeFolder?.DisplayName ?? string.Empty;
            }

            HomeTiles.Clear();
            HomeTiles.Add(HomeTile.CreateAddTile());

            var libraryEntries = RecentFilesService.GetLibraryEntries(_currentFolderId);
            HomeSmokeLog($"refresh folder={_currentFolderId} entries={libraryEntries.Count}");
            foreach (var entry in libraryEntries)
            {
                if (entry.IsFolder)
                {
                    HomeTiles.Add(HomeTile.CreateFolderTile(entry, RecentFilesService.GetDirectChildCount(entry.Id)));
                    continue;
                }

                if (!string.Equals(Path.GetExtension(entry.Path), ".pdf", StringComparison.OrdinalIgnoreCase))
                    continue;

                var tile = HomeTile.CreateFileTile(entry);
                if (selectionState.TryGetValue(tile.Path, out var isSelected))
                    tile.IsSelected = isSelected;

                HomeTiles.Add(tile);
            }

            UpdateHeaderText();
            RebuildVisibleTiles();
            RefreshSelectionState();
            await Task.CompletedTask;
        }

        private void UpdateHeaderText()
        {
            HomeTitleTextBlock.Text = IsInsideFolder
                ? _currentFolderName
                : LocalizationService.Get("Home.Title");
            HomeSubtitleTextBlock.Text = IsInsideFolder
                ? LocalizationService.Format("Home.FolderSubtitle", _currentFolderName)
                : LocalizationService.Get("Home.Subtitle");

            OnPropertyChanged(nameof(IsInsideFolder));
            OnPropertyChanged(nameof(BreadcrumbBarVisibility));
            OnPropertyChanged(nameof(NavigateUpText));
            OnPropertyChanged(nameof(FolderBreadcrumb));
        }

        /// <summary>
        /// WinUI replacement for the WPF CollectionViewSource filter +
        /// SortDescriptions. Rebuilds <see cref="VisibleTiles"/> as
        /// [add tile (hidden in selection mode)] + folders + files matching
        /// <see cref="_searchQuery"/>, ordered by SortPriority then name /
        /// last-modified-desc per <see cref="_currentSortMode"/>.
        /// </summary>
        private void RebuildVisibleTiles()
        {
            var query = _searchQuery;
            var filtered = HomeTiles.Where(tile =>
            {
                if (tile.IsAddTile)
                    return !IsSelectionMode;

                if (string.IsNullOrWhiteSpace(query))
                    return true;

                return tile.FileName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
            });

            IEnumerable<HomeTile> ordered = _currentSortMode == HomeSortMode.Name
                ? filtered
                    .OrderBy(tile => tile.SortPriority)
                    .ThenBy(tile => tile.FileName, Comparer<string>.Default)
                : filtered
                    .OrderBy(tile => tile.SortPriority)
                    .ThenByDescending(tile => tile.LastModified)
                    .ThenBy(tile => tile.FileName, Comparer<string>.Default);

            VisibleTiles.Clear();
            foreach (var tile in ordered)
                VisibleTiles.Add(tile);
        }

        // ── Tile activation + context menus ─────────────────────────────────

        private void AddTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement placementTarget)
                ShowAddTileMenu(placementTarget);
        }

        private void ShowAddTileMenu(FrameworkElement placementTarget)
        {
            var menu = new MenuFlyout();

            var openItem = CreateMenuItem(LocalizationService.Get("Home.Menu.OpenFile"), "\uE8E5");
            openItem.Click += async (_, _) => await PickAndOpenPdfAsync();
            menu.Items.Add(openItem);

            var createFolderItem = CreateMenuItem(LocalizationService.Get("Home.Menu.CreateFolder"), "\uE8B7");
            createFolderItem.Click += async (_, _) => await CreateFolderAsync();
            menu.Items.Add(createFolderItem);

            var createNotebookItem = CreateMenuItem(LocalizationService.Get("Home.Menu.CreateNotebook"), "\uE70B");
            createNotebookItem.Click += async (_, _) => await CreateEmptyNotebookAsync();
            menu.Items.Add(createNotebookItem);

            menu.ShowAt(placementTarget);
        }

        private void FileTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not HomeTile tile || !tile.IsFile)
                return;

            if (IsSelectionMode)
            {
                ToggleTileSelection(tile);
                return;
            }

            _ = OpenFileTileAsync(tile).ContinueWith(
                t => HomeSmokeLog($"open-fault {t.Exception?.Flatten().InnerException}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        private void FolderTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not HomeTile tile || !tile.IsFolder)
            {
                HomeSmokeLog($"folder-click-ignored ctx={((sender as FrameworkElement)?.Tag?.GetType().Name ?? "null")}");
                return;
            }

            if (IsChoosingMoveTarget)
            {
                _ = MoveSelectedTilesToFolderAsync(tile.Id);
                return;
            }

            HomeSmokeLog($"folder-click id={tile.Id} name={tile.FileName}");
            _currentFolderId = tile.Id;
            _currentFolderName = tile.FileName;
            _ = RefreshCurrentFolderAsync().ContinueWith(
                t => HomeSmokeLog($"refresh-fault {t.Exception?.Flatten().InnerException}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        // ContextRequested (not RightTapped): ButtonBase marks RightTapped
        // handled, which would swallow the gesture on the tile surface.
        // ContextRequested is the WinUI context-menu event — it fires for
        // right-click, the keyboard Menu key, and touch press-and-hold.
        private void FileTile_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not HomeTile tile || !tile.IsFile)
                return;

            var position = e.TryGetPosition(element, out var p)
                ? p
                : new Windows.Foundation.Point(element.ActualWidth / 2, element.ActualHeight / 2);
            ShowFileContextMenu(tile, element, position);
            e.Handled = true;
        }

        private void FolderTile_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not HomeTile tile || !tile.IsFolder)
                return;

            var position = e.TryGetPosition(element, out var p)
                ? p
                : new Windows.Foundation.Point(element.ActualWidth / 2, element.ActualHeight / 2);
            ShowFolderContextMenu(tile, element, position);
            e.Handled = true;
        }

        private void ShowFileContextMenu(HomeTile tile, FrameworkElement placementTarget, Windows.Foundation.Point position)
        {
            var menu = new MenuFlyout();

            var openItem = CreateMenuItem(LocalizationService.Get("Home.Context.Open"), "\uE7C3");
            openItem.Click += async (_, _) => await OpenFileTileAsync(tile);
            menu.Items.Add(openItem);

            var renameItem = CreateMenuItem(LocalizationService.Get("Home.Context.Rename"), "\uE70F");
            renameItem.Click += async (_, _) => await RenameTileAsync(tile);
            menu.Items.Add(renameItem);

            var selectItem = CreateMenuItem(LocalizationService.Get("Home.Context.Select"), "\uE762");
            selectItem.Click += (_, _) =>
            {
                if (!IsSelectionMode)
                    ToggleSelectionMode();
                ToggleTileSelection(tile);
            };
            menu.Items.Add(selectItem);

            if (IsInsideFolder)
            {
                var moveToRootItem = CreateMenuItem(LocalizationService.Get("Home.Context.MoveToLibrary"), "\uE8DE");
                moveToRootItem.Click += async (_, _) =>
                {
                    RecentFilesService.MoveToLibraryRoot(tile.Path);
                    await RefreshCurrentFolderAsync();
                };
                menu.Items.Add(moveToRootItem);
            }

            var copyItem = CreateMenuItem(LocalizationService.Get("Home.Context.CopyPath"), "\uE8C8");
            copyItem.Click += (_, _) =>
            {
                try
                {
                    var package = new DataPackage();
                    package.SetText(tile.Path);
                    Clipboard.SetContent(package);
                    Clipboard.Flush();
                }
                catch
                {
                    // Clipboard is best-effort (WPF parity: silent catch).
                }
            };
            menu.Items.Add(copyItem);

            var openFolderItem = CreateMenuItem(LocalizationService.Get("Home.Context.OpenFolder"), "\uE838");
            openFolderItem.Click += (_, _) => OpenContainingFolder(tile);
            menu.Items.Add(openFolderItem);

            var exportItem = CreateMenuItem(LocalizationService.Get("Home.Context.Export"), "\uEDE1");
            exportItem.Click += async (_, _) => await ExportTileAsync(tile);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = CreateMenuItem(LocalizationService.Get("Home.Context.Delete"), "\uE74D", foregroundResourceKey: "ThemeDangerBrush");
            deleteItem.Click += async (_, _) => await DeleteFileTileAsync(tile);
            menu.Items.Add(deleteItem);

            var removeItem = CreateMenuItem(LocalizationService.Get("Home.Context.Remove"), "\uE74D", foregroundResourceKey: "ThemeDangerBrush");
            removeItem.Click += async (_, _) => await RemoveFileTileAsync(tile);
            menu.Items.Add(removeItem);

            menu.ShowAt(placementTarget, new FlyoutShowOptions { Position = position });
        }

        private void ShowFolderContextMenu(HomeTile tile, FrameworkElement placementTarget, Windows.Foundation.Point position)
        {
            var menu = new MenuFlyout();

            var openItem = CreateMenuItem(LocalizationService.Get("Home.Context.Open"), "\uE8B7");
            openItem.Click += (_, _) =>
            {
                _currentFolderId = tile.Id;
                _currentFolderName = tile.FileName;
                _ = RefreshCurrentFolderAsync();
            };
            menu.Items.Add(openItem);

            var renameItem = CreateMenuItem(LocalizationService.Get("Home.Context.Rename"), "\uE70F");
            renameItem.Click += async (_, _) => await RenameTileAsync(tile);
            menu.Items.Add(renameItem);

            // Folder color submenu (WPF nested MenuItem → MenuFlyoutSubItem).
            var colorItem = new MenuFlyoutSubItem
            {
                Text = LocalizationService.Get("Home.Context.Color")
            };
            foreach (var swatch in FolderColorSwatches)
            {
                var swatchItem = new MenuFlyoutItem { Text = LocalizeFolderColor(swatch.Key), Tag = swatch.Hex };
                swatchItem.Click += async (_, _) =>
                {
                    RecentFilesService.SetFolderColor(tile.Id, swatch.Hex);
                    await RefreshCurrentFolderAsync();
                };
                colorItem.Items.Add(swatchItem);
            }
            menu.Items.Add(colorItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var removeItem = CreateMenuItem(LocalizationService.Get("Home.Context.RemoveFolder"), "\uE74D", foregroundResourceKey: "ThemeDangerBrush");
            removeItem.Click += async (_, _) =>
            {
                RecentFilesService.RemoveFolder(tile.Id);
                await RefreshCurrentFolderAsync();
            };
            menu.Items.Add(removeItem);

            menu.ShowAt(placementTarget, new FlyoutShowOptions { Position = position });
        }

        /// <summary>
        /// Builds a flyout item with a Segoe glyph icon + themed foreground —
        /// the WinUI stand-in for WPF <c>CreateMenuItem</c> (Lucide icons land
        /// with the Task 6 icon port; the glyph stand-ins keep AutomationIds
        /// and layout stable). Menus are built per-show, so they localize on
        /// construction and no open-menu refresh pass is needed.
        /// </summary>
        private MenuFlyoutItem CreateMenuItem(string text, string iconGlyph, Brush foreground = null, string foregroundResourceKey = null)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = new FontIcon { Glyph = iconGlyph, FontSize = 14 }
            };

            if (foreground != null)
            {
                item.Foreground = foreground;
            }
            else
            {
                string resourceKey = string.IsNullOrWhiteSpace(foregroundResourceKey)
                    ? "ThemeForegroundBrush"
                    : foregroundResourceKey;
                if (Application.Current?.Resources?.TryGetValue(resourceKey, out var value) == true &&
                    value is Brush brush)
                {
                    item.Foreground = brush;
                }
            }

            return item;
        }

        // ── Rename / create / open flows ────────────────────────────────────

        private async Task RenameTileAsync(HomeTile tile)
        {
            if (tile == null || tile.IsAddTile)
                return;

            if (tile.IsFolder)
            {
                var newName = await PromptForInputAsync(
                    LocalizationService.Get("Home.RenameFolderTitle"),
                    LocalizationService.Get("Home.RenameFolderPrompt"),
                    tile.FileName,
                    LocalizationService.Get("Home.RenameAction"));

                if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName.Trim(), tile.FileName, StringComparison.Ordinal))
                    return;

                RecentFilesService.RenameFolder(tile.Id, newName.Trim());
                await RefreshCurrentFolderAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(tile.Path) || !File.Exists(tile.Path))
                return;

            var oldPath = tile.Path;
            var newBaseName = await PromptForInputAsync(
                LocalizationService.Get("Home.RenameTitle"),
                LocalizationService.Get("Home.RenamePrompt"),
                Path.GetFileNameWithoutExtension(oldPath),
                LocalizationService.Get("Home.RenameAction"));

            if (string.IsNullOrWhiteSpace(newBaseName))
                return;

            var directory = Path.GetDirectoryName(oldPath) ?? string.Empty;
            var extension = Path.GetExtension(oldPath);
            var newPath = Path.Combine(directory, newBaseName.Trim() + extension);

            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                File.Move(oldPath, newPath);
                RecentFilesService.UpdatePath(oldPath, newPath);
                tile.SetPath(newPath);
                tile.LastModified = File.GetLastWriteTime(newPath);
                GetMainWindow()?.HandleFilePathChanged(oldPath, newPath);
                await RefreshCurrentFolderAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"), LocalizationService.Format("Home.RenameFailed", ex.Message));
            }
        }

        /// <summary>
        /// ContentDialog port of the WPF <c>PromptForInput</c> borderless
        /// window: title + prompt + TextBox (text pre-selected) + confirm /
        /// cancel. Returns the trimmed input, or null on cancel/dismiss.
        /// </summary>
        private async Task<string> PromptForInputAsync(string title, string prompt, string initialValue, string confirmText)
        {
            var xamlRoot = XamlRoot;
            if (xamlRoot == null)
                return null;

            var inputBox = new TextBox
            {
                Text = initialValue ?? string.Empty,
                FontSize = 14,
                MinWidth = 320
            };
            inputBox.Loaded += (_, _) => inputBox.SelectAll();

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = prompt,
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 10)
                        },
                        inputBox
                    }
                },
                PrimaryButtonText = confirmText,
                CloseButtonText = LocalizationService.Get("Common.Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? inputBox.Text.Trim() : null;
        }

        private async Task PickAndOpenPdfAsync()
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
                return;

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
                CommitButtonText = LocalizationService.Get("Home.OpenDocumentTitle")
            };
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".doc");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".docm");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(mainWindow));

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            var folderId = IsInsideFolder ? _currentFolderId : null;
            var pdfPath = await TryImportAsLibraryPdfAsync(file.Path);
            if (string.IsNullOrWhiteSpace(pdfPath))
                return;

            await AddFileToLibraryAsync(pdfPath, folderId, false);

            if (GetMainWindow() is MainWindow mw)
                mw.NavigateActiveTabToFile(pdfPath);
            else
                Frame?.Navigate(typeof(EditorPage), pdfPath);
        }

        private async Task CreateFolderAsync()
        {
            var name = await PromptForInputAsync(
                LocalizationService.Get("Home.CreateFolderTitle"),
                LocalizationService.Get("Home.CreateFolderPrompt"),
                string.Empty,
                LocalizationService.Get("Home.CreateFolderAction"));

            if (string.IsNullOrWhiteSpace(name))
                return;

            RecentFilesService.CreateFolder(name, _currentFolderId);
            await RefreshCurrentFolderAsync();
            GetMainWindow()?.ShowToast(LocalizationService.Format("Home.FolderCreated", name.Trim()), "\uE8B7");
        }

        /// <summary>
        /// Compact ContentDialog stand-in for the WPF
        /// <c>PageTemplatePickerWindow</c> notebook-creation mode (the full
        /// picker port is scheduled for Task 9): 9 template radio cards with
        /// localized title+hint, a "save to" folder row driven by
        /// <see cref="FolderPicker"/>, and Create/Cancel. Returns
        /// (template, folderPath) or null when dismissed.
        /// </summary>
        private async Task<(PageInsertTemplate Template, string FolderPath)?> PickNotebookTemplateAsync()
        {
            var xamlRoot = XamlRoot;
            var mainWindow = GetMainWindow();
            if (xamlRoot == null || mainWindow == null)
                return null;

            var selectedTemplate = PageInsertTemplate.Blank;
            string folderPath = GetDefaultNotebookDirectory();

            var radioPanel = new StackPanel { Spacing = 4 };
            foreach (var (template, titleKey, hintKey) in NotebookTemplateOptions)
            {
                var option = template;
                var radio = new RadioButton
                {
                    GroupName = "NotebookTemplate",
                    IsChecked = template == selectedTemplate,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = LocalizationService.Get(titleKey), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                            new TextBlock { Text = LocalizationService.Get(hintKey), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                                            Foreground = ResolveThemeBrush("ThemeSubtleForegroundBrush", "#4B5563") }
                        }
                    }
                };
                radio.Checked += (_, _) => selectedTemplate = option;
                radioPanel.Children.Add(radio);
            }

            var folderPathText = new TextBlock
            {
                Text = folderPath,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ResolveThemeBrush("ThemeSubtleForegroundBrush", "#4B5563"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var browseButton = new Button
            {
                Content = LocalizationService.Get("Home.CreateNotebookBrowseFolder"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            browseButton.Click += async (_, _) =>
            {
                var folderPicker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    CommitButtonText = LocalizationService.Get("Home.CreateNotebookBrowseFolder")
                };
                folderPicker.FileTypeFilter.Add("*");
                InitializeWithWindow.Initialize(folderPicker, WindowNative.GetWindowHandle(mainWindow));
                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    folderPath = folder.Path;
                    folderPathText.Text = folder.Path;
                }
            };

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = LocalizationService.Get("Home.CreateNotebookDialogTitle"),
                PrimaryButtonText = LocalizationService.Get("Home.CreateNotebookAction"),
                CloseButtonText = LocalizationService.Get("Common.Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                Content = new ScrollViewer
                {
                    MaxHeight = 520,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = LocalizationService.Get("Home.CreateNotebookDialogSubtitle"),
                                FontSize = 13,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = ResolveThemeBrush("ThemeSubtleForegroundBrush", "#4B5563")
                            },
                            radioPanel,
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = LocalizationService.Get("Home.CreateNotebookPathLabel"),
                                        FontSize = 13,
                                        VerticalAlignment = VerticalAlignment.Center
                                    },
                                    folderPathText,
                                    browseButton
                                }
                            }
                        }
                    }
                }
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(folderPath))
                return null;

            return (selectedTemplate, folderPath);
        }

        private static readonly (PageInsertTemplate Template, string TitleKey, string HintKey)[] NotebookTemplateOptions =
        {
            (PageInsertTemplate.Blank, "Editor.PageTemplateBlank", "Editor.PageTemplateBlankHint"),
            (PageInsertTemplate.Notebook, "Editor.PageTemplateNotebook", "Editor.PageTemplateNotebookHint"),
            (PageInsertTemplate.Lined, "Editor.PageTemplateLined", "Editor.PageTemplateLinedHint"),
            (PageInsertTemplate.Quadrille, "Editor.PageTemplateQuadrille", "Editor.PageTemplateQuadrilleHint"),
            (PageInsertTemplate.Dotted, "PageTemplate.DottedTitle", "PageTemplate.DottedHint"),
            (PageInsertTemplate.Music, "PageTemplate.MusicTitle", "PageTemplate.MusicHint"),
            (PageInsertTemplate.Cornell, "PageTemplate.CornellTitle", "PageTemplate.CornellHint"),
            (PageInsertTemplate.Checklist, "PageTemplate.ChecklistTitle", "PageTemplate.ChecklistHint"),
            (PageInsertTemplate.TwoColumn, "PageTemplate.TwoColumnTitle", "PageTemplate.TwoColumnHint")
        };

        private async Task CreateEmptyNotebookAsync()
        {
            var picked = await PickNotebookTemplateAsync();
            if (picked == null)
                return;

            var (template, folderPath) = picked.Value;
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            try
            {
                Directory.CreateDirectory(folderPath);
                var notebookPath = BuildNotebookFilePath(folderPath);
                await PdfService.CreateBlankPdfAsync(notebookPath, template: template);
                await AddFileToLibraryAsync(notebookPath, IsInsideFolder ? _currentFolderId : null, true);

                if (GetMainWindow() is MainWindow mw)
                    mw.NavigateActiveTabToFile(notebookPath);
                else
                    Frame?.Navigate(typeof(EditorPage), notebookPath);
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"), LocalizationService.Format("Home.CreateNotebookFailed", ex.Message));
            }
        }

        private async Task AddFileToLibraryAsync(string path, string folderId, bool isNotebook)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                return;

            DateTime? lastModifiedUtc = null;
            if (File.Exists(path))
                lastModifiedUtc = File.GetLastWriteTimeUtc(path);

            RecentFilesService.AddOrPromote(path, null, lastModifiedUtc, folderId, isNotebook);
            await RefreshCurrentFolderAsync();
        }

        private async Task OpenFileTileAsync(HomeTile tile)
        {
            if (tile == null || !tile.IsFile || string.IsNullOrWhiteSpace(tile.Path))
                return;

            if (!string.Equals(Path.GetExtension(tile.Path), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await RemoveFileTileAsync(tile);
                await ShowDialogAsync(LocalizationService.Get("Common.Error"), LocalizationService.Get("Home.ErrorUnsupportedType"));
                return;
            }

            try
            {
                if (!File.Exists(tile.Path))
                {
                    await RemoveFileTileAsync(tile);
                    await ShowDialogAsync(LocalizationService.Get("Common.Error"), LocalizationService.Get("Home.ErrorFileNotFound"));
                    return;
                }

                RecentFilesService.AddOrPromote(tile.Path);
                if (GetMainWindow() is MainWindow mw)
                    mw.NavigateActiveTabToFile(tile.Path);
                else
                    Frame?.Navigate(typeof(EditorPage), tile.Path);
            }
            catch
            {
                await RemoveFileTileAsync(tile);
                await ShowDialogAsync(LocalizationService.Get("Common.Error"), LocalizationService.Get("Home.ErrorAccessDenied"));
            }
        }

        private async Task DeleteFileTileAsync(HomeTile tile)
        {
            if (tile == null || !tile.IsFile)
                return;

            bool? confirmed = await WinUiDialogService.ShowDangerConfirmAsync(
                XamlRoot,
                LocalizationService.Get("Home.DeleteTitle"),
                LocalizationService.Format("Home.DeleteMessage", tile.FileName),
                LocalizationService.Get("Common.Cancel"),
                LocalizationService.Get("Home.Context.Delete"));
            if (confirmed != true)
                return;

            if (TryDeleteLibraryFile(tile.Path) && GetMainWindow() is MainWindow mw)
                mw.ShowToast(LocalizationService.Format("Home.Selection.DeletedCount", 1), "Trash2");

            await RefreshCurrentFolderAsync();
        }

        private static readonly (string Key, string Hex)[] FolderColorSwatches =
        {
            ("Home.FolderColor.Amber", "#F59E0B"),
            ("Home.FolderColor.Red", "#EF4444"),
            ("Home.FolderColor.Orange", "#F97316"),
            ("Home.FolderColor.Green", "#22C55E"),
            ("Home.FolderColor.Blue", "#3B82F6"),
            ("Home.FolderColor.Purple", "#8B5CF6"),
            ("Home.FolderColor.Pink", "#EC4899"),
            ("Home.FolderColor.Slate", "#64748B")
        };

        private static string LocalizeFolderColor(string key)
        {
            return key switch
            {
                "Home.FolderColor.Amber" => LocalizationService.Get("Home.FolderColor.Amber"),
                "Home.FolderColor.Red" => LocalizationService.Get("Home.FolderColor.Red"),
                "Home.FolderColor.Orange" => LocalizationService.Get("Home.FolderColor.Orange"),
                "Home.FolderColor.Green" => LocalizationService.Get("Home.FolderColor.Green"),
                "Home.FolderColor.Blue" => LocalizationService.Get("Home.FolderColor.Blue"),
                "Home.FolderColor.Purple" => LocalizationService.Get("Home.FolderColor.Purple"),
                "Home.FolderColor.Pink" => LocalizationService.Get("Home.FolderColor.Pink"),
                "Home.FolderColor.Slate" => LocalizationService.Get("Home.FolderColor.Slate"),
                _ => key
            };
        }

        private async Task RemoveFileTileAsync(HomeTile tile)
        {
            if (tile == null || !tile.IsFile)
                return;

            RecentFilesService.Remove(tile.Path);
            await RefreshCurrentFolderAsync();
        }

        private async Task ShowDialogAsync(string title, string content)
        {
            var xamlRoot = XamlRoot;
            if (xamlRoot != null)
                await WinUiDialogService.ShowInfoAsync(xamlRoot, title, content);
        }

        private async Task<string> TryImportAsLibraryPdfAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (WordDocumentImport.IsPdfPath(path))
                return path;

            if (!WordDocumentImport.IsWordPath(path))
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"), LocalizationService.Get("Home.ErrorUnsupportedType"));
                return null;
            }

            GetMainWindow()?.ShowToast(LocalizationService.Get("Home.ConvertingWord"), "\uE8B7");
            var previousCursor = ProtectedCursor;
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Wait);
            try
            {
                return await WordToPdfConverter.Default.ImportAsync(path);
            }
            catch (WordConverterNotFoundException)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"), LocalizationService.Get("Home.WordConverterMissing"));
                return null;
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.WordConvertFailed", Path.GetFileName(path), ex.Message));
                return null;
            }
            finally
            {
                ProtectedCursor = previousCursor;
            }
        }

        private void NavigateUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsInsideFolder)
                return;

            if (IsChoosingMoveTarget)
            {
                var parentFolder = RecentFilesService.GetFolder(_currentFolderId)?.ParentFolderId;
                _ = MoveSelectedTilesToFolderAsync(string.IsNullOrWhiteSpace(parentFolder) ? null : parentFolder);
                return;
            }

            var parent = RecentFilesService.GetFolder(_currentFolderId)?.ParentFolderId;
            _currentFolderId = parent ?? string.Empty;
            _currentFolderName = RecentFilesService.GetFolder(_currentFolderId)?.DisplayName ?? string.Empty;
            _ = RefreshCurrentFolderAsync();
        }

        // ── Drag: file tile drag-out (WPF DoDragDrop → CanDrag/DragStarting) ─

        /// <summary>
        /// Starts a tile drag. Payload: <c>Caelum.LibraryTilePaths</c>
        /// (newline-joined) + <c>Caelum.LibraryTilePath</c> (single path) for
        /// in-app folder moves, and <see cref="StandardDataFormats.StorageItems"/>
        /// for the Explorer drag-out — the WinUI counterpart of the WPF
        /// <c>DataFormats.FileDrop</c> payload. In selection mode only the
        /// selected set drags; an unselected tile cancels the drag.
        /// </summary>
        private async void FileTile_DragStarting(UIElement sender, DragStartingEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not HomeTile tile || !tile.IsFile)
            {
                e.Cancel = true;
                return;
            }

            var paths = GetDragCandidatePaths(tile);
            if (paths.Length == 0)
            {
                e.Cancel = true;
                return;
            }

            e.AllowedOperations = DataPackageOperation.Copy | DataPackageOperation.Move;
            e.Data.RequestedOperation = DataPackageOperation.Copy;
            e.Data.SetData(HomePageDragDropHelper.LibraryTilePathsDataFormat,
                HomePageDragDropHelper.PackLibraryTilePaths(paths));
            if (paths.Length == 1)
                e.Data.SetData(HomePageDragDropHelper.LibraryTilePathDataFormat, paths[0]);

            // Explorer drag-out: attach the real files as StorageItems so the
            // OS shell receives CF_HDROP equivalents. Storage enumeration is
            // async — the deferral holds the drag open while we resolve them.
            var deferral = e.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (var path in paths)
                {
                    if (!File.Exists(path))
                        continue;
                    try
                    {
                        items.Add(await StorageFile.GetFileFromPathAsync(path));
                    }
                    catch
                    {
                        // Skip files that can't be surfaced as StorageFiles.
                    }
                }

                if (items.Count > 0)
                    e.Data.SetStorageItems(items);
            }
            catch
            {
                // In-app folder drop still works via the custom formats above.
            }
            finally
            {
                deferral.Complete();
            }
        }

        private string[] GetDragCandidatePaths(HomeTile tile)
        {
            if (tile == null || !tile.IsFile)
                return Array.Empty<string>();

            if (!IsSelectionMode)
                return string.IsNullOrWhiteSpace(tile.Path)
                    ? Array.Empty<string>()
                    : new[] { tile.Path };

            if (!tile.IsSelected)
                return Array.Empty<string>();

            return HomeTiles
                .Where(candidate => candidate.IsFile && candidate.IsSelected && !string.IsNullOrWhiteSpace(candidate.Path))
                .Select(candidate => candidate.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // ── Drop: folder tile targets ────────────────────────────────────────

        private void FolderTile_DragEnter(object sender, DragEventArgs e)
        {
            UpdateFolderDropState(sender, e, true);
        }

        private void FolderTile_DragOver(object sender, DragEventArgs e)
        {
            UpdateFolderDropState(sender, e, true);
        }

        private void FolderTile_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is HomeTile tile)
                tile.IsDropTarget = false;
        }

        private async void FolderTile_Drop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not HomeTile tile || !tile.IsFolder)
                return;

            tile.IsDropTarget = false;
            var deferral = e.GetDeferral();
            var movedAny = false;

            try
            {
                var libraryTilePaths = await HomePageDragDropHelper.GetLibraryTilePathsAsync(e.DataView);
                if (libraryTilePaths.Length > 0)
                {
                    foreach (var filePath in libraryTilePaths)
                        movedAny = RecentFilesService.MoveToFolder(filePath, tile.Id) || movedAny;
                }
                else
                {
                    foreach (var file in await HomePageDragDropHelper.GetDroppedImportablePathsAsync(e.DataView))
                    {
                        var pdfPath = await TryImportAsLibraryPdfAsync(file);
                        if (string.IsNullOrWhiteSpace(pdfPath))
                            continue;

                        RecentFilesService.AddOrPromote(
                            pdfPath,
                            null,
                            File.Exists(pdfPath) ? File.GetLastWriteTimeUtc(pdfPath) : null,
                            tile.Id,
                            false);
                        movedAny = true;
                    }
                }
            }
            finally
            {
                deferral.Complete();
            }

            if (movedAny)
            {
                await RefreshCurrentFolderAsync();
                GetMainWindow()?.ShowToast(LocalizationService.Format("Home.MovedToFolder", tile.FileName), "\uE8B7");
            }

            e.Handled = true;
        }

        private void UpdateFolderDropState(object sender, DragEventArgs e, bool isActive)
        {
            if (sender is not FrameworkElement element || element.Tag is not HomeTile tile || !tile.IsFolder)
                return;

            // DragOver cannot enumerate storage items synchronously; the
            // Contains probes mirror WPF's payload check closely enough for
            // the highlight, and Drop does the real importable-path filtering.
            var hasLibraryPaths = HomePageDragDropHelper.HasLibraryTilePaths(e.DataView);
            var hasStorageItems = HomePageDragDropHelper.HasStorageItems(e.DataView);
            var canAcceptDrop = hasLibraryPaths || hasStorageItems;

            tile.IsDropTarget = isActive && canAcceptDrop;
            e.AcceptedOperation = canAcceptDrop
                ? (hasLibraryPaths ? DataPackageOperation.Move : DataPackageOperation.Copy)
                : DataPackageOperation.None;

            // Hovering a folder must hide the page-level overlay even though
            // the routed DragOver may be handled before it reaches the page.
            DragDropOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }

        // ── Drop: page background (external file import / move to folder) ───

        private void HomePage_DragOver(object sender, DragEventArgs e)
        {
            if (GetFolderTileFromSource(e.OriginalSource as DependencyObject) != null)
            {
                DragDropOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            var hasLibraryPaths = HomePageDragDropHelper.HasLibraryTilePaths(e.DataView);
            var hasStorageItems = HomePageDragDropHelper.HasStorageItems(e.DataView);
            if (hasLibraryPaths || hasStorageItems)
            {
                e.AcceptedOperation = hasLibraryPaths ? DataPackageOperation.Move : DataPackageOperation.Copy;
                DragDropOverlay.Visibility = Visibility.Visible;
                e.Handled = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
                DragDropOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void HomePage_DragLeave(object sender, DragEventArgs e)
        {
            DragDropOverlay.Visibility = Visibility.Collapsed;
        }

        private async void HomePage_Drop(object sender, DragEventArgs e)
        {
            DragDropOverlay.Visibility = Visibility.Collapsed;

            if (GetFolderTileFromSource(e.OriginalSource as DependencyObject) != null)
                return;

            var deferral = e.GetDeferral();
            bool movedAny = false;

            try
            {
                var libraryPaths = await HomePageDragDropHelper.GetLibraryTilePathsAsync(e.DataView);
                var pdfPaths = await HomePageDragDropHelper.GetDroppedImportablePathsAsync(e.DataView);

                if (libraryPaths.Length > 0 && IsInsideFolder)
                {
                    foreach (var filePath in libraryPaths)
                        movedAny = RecentFilesService.MoveToFolder(filePath, _currentFolderId) || movedAny;
                }
                else if (pdfPaths.Length > 0)
                {
                    foreach (var file in pdfPaths)
                    {
                        var pdfPath = await TryImportAsLibraryPdfAsync(file);
                        if (string.IsNullOrWhiteSpace(pdfPath))
                            continue;

                        RecentFilesService.AddOrPromote(
                            pdfPath,
                            null,
                            File.Exists(pdfPath) ? File.GetLastWriteTimeUtc(pdfPath) : null,
                            IsInsideFolder ? _currentFolderId : null,
                            false);
                        movedAny = true;
                    }
                }
            }
            finally
            {
                deferral.Complete();
            }

            if (movedAny)
            {
                await RefreshCurrentFolderAsync();
                var targetName = IsInsideFolder ? _currentFolderName : LocalizationService.Get("Home.LibraryRoot");
                GetMainWindow()?.ShowToast(LocalizationService.Format("Home.MovedToFolder", targetName), "\uE8B7");
            }

            e.Handled = true;
        }

        /// <summary>
        /// Walks the visual ancestors of a drag-event source looking for a
        /// folder tile (the folder Grid carries <c>Tag={x:Bind}</c> = its
        /// <see cref="HomeTile"/>). Same contract as the WPF
        /// <c>GetFolderTileFromSource</c>.
        /// </summary>
        private HomeTile GetFolderTileFromSource(DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                if (current is FrameworkElement element &&
                    element.Tag is HomeTile tile &&
                    tile.IsFolder)
                {
                    return tile;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static bool IsPdfFile(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string((name ?? string.Empty).Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(sanitized)
                ? LocalizationService.Get("Home.NewNotebookName")
                : sanitized;
        }

        private static string GetDefaultNotebookDirectory()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documentsPath))
                return documentsPath;

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return string.IsNullOrWhiteSpace(desktopPath)
                ? Path.Combine(ProductInfo.GetDataDirectory(), "Notebooks")
                : desktopPath;
        }

        private static string BuildNotebookFilePath(string directory)
        {
            var notebookName = SanitizeFileName(LocalizationService.Get("Home.NewNotebookName"));
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", LocalizationService.CurrentCulture);
            var baseName = $"{notebookName} {timestamp}";
            var filePath = Path.Combine(directory, $"{baseName}.pdf");
            var counter = 1;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(directory, $"{baseName} ({counter}).pdf");
                counter++;
            }

            return filePath;
        }

        private MainWindow GetMainWindow()
        {
            // Single-window shell — the static is the WinUI stand-in for
            // Window.GetWindow(this)/Application.Current.MainWindow.
            return MainWindow.Current;
        }

        private static Brush ResolveThemeBrush(string key, string fallbackHex)
        {
            if (Application.Current?.Resources?.TryGetValue(key, out var value) == true &&
                value is Brush brush)
                return brush;
            return WinUiThemeService.CreateBrush(fallbackHex);
        }

        // ── Smooth wheel scroll (WPF CompositionTarget.Rendering port) ──────

        private double _targetVerticalOffset;
        private bool _smoothScrollInitialized;
        private double _scrollAnimationTarget;
        private double _scrollAnimationStart;
        private DateTime _scrollAnimationStartTime;
        private TimeSpan _scrollAnimationDuration;
        private bool _isScrollAnimating;

        private void HomeScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            // Same contract as WPF PreviewMouseWheel: we own the wheel, the
            // ScrollViewer's built-in scroll is suppressed, and offset changes
            // run through the eased animation below.
            e.Handled = true;

            if (!_smoothScrollInitialized)
            {
                _targetVerticalOffset = HomeScrollViewer.VerticalOffset;
                _smoothScrollInitialized = true;
            }

            var wheelDelta = e.GetCurrentPoint(HomeScrollViewer).Properties.MouseWheelDelta;
            double scrollAmount = -wheelDelta * 0.8;
            _targetVerticalOffset = Math.Max(0,
                Math.Min(HomeScrollViewer.ScrollableHeight, _targetVerticalOffset + scrollAmount));

            _scrollAnimationTarget = _targetVerticalOffset;
            _scrollAnimationStart = HomeScrollViewer.VerticalOffset;
            _scrollAnimationStartTime = DateTime.UtcNow;
            _scrollAnimationDuration = WinUiThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(180));

            if (_scrollAnimationDuration == TimeSpan.Zero)
            {
                _isScrollAnimating = false;
                CompositionTarget.Rendering -= HomeCompositionTarget_Rendering;
                HomeScrollViewer.ChangeView(null, _scrollAnimationTarget, null, disableAnimation: true);
                return;
            }

            if (!_isScrollAnimating)
            {
                _isScrollAnimating = true;
                CompositionTarget.Rendering += HomeCompositionTarget_Rendering;
            }
        }

        private void HomeCompositionTarget_Rendering(object sender, object e)
        {
            if (_scrollAnimationDuration == TimeSpan.Zero || !WinUiThemeService.ShouldAnimate)
            {
                HomeScrollViewer.ChangeView(null, _scrollAnimationTarget, null, disableAnimation: true);
                _isScrollAnimating = false;
                CompositionTarget.Rendering -= HomeCompositionTarget_Rendering;
                return;
            }

            var elapsed = DateTime.UtcNow - _scrollAnimationStartTime;
            double progress = Math.Min(1.0, elapsed.TotalMilliseconds / _scrollAnimationDuration.TotalMilliseconds);
            double easedProgress = 1.0 - Math.Pow(1.0 - progress, 3);

            double currentOffset = _scrollAnimationStart + (_scrollAnimationTarget - _scrollAnimationStart) * easedProgress;
            HomeScrollViewer.ChangeView(null, currentOffset, null, disableAnimation: true);

            if (progress >= 1.0)
            {
                _isScrollAnimating = false;
                CompositionTarget.Rendering -= HomeCompositionTarget_Rendering;
            }
        }
    }
}
