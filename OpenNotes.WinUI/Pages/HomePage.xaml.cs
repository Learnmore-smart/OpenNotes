using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caelum.Controls;
using Caelum.Models;
using Caelum.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
#if DEBUG
            InstallDebugContextMenuSeam();
#endif
        }

#if DEBUG
        private void InstallDebugContextMenuSeam()
        {
            // The smoke session cannot deliver OS-level input (a real
            // right-click lands on the desktop site bridge, never the tile),
            // so the file-tile context menu gets a hidden invoke seam that
            // runs the same ShowFileContextMenu path ContextRequested uses.
            var seam = new Button
            {
                Width = 2,
                Height = 2,
                Opacity = 0.01,
                IsTabStop = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            AutomationProperties.SetAutomationId(seam, "Home.DebugOpenContextMenu");
            AutomationProperties.SetName(seam, "DEBUG open file context menu");
            seam.Click += (_, __) =>
            {
                var tile = VisibleTiles.FirstOrDefault(t => t.IsFile);
                if (tile == null || TilesRepeater == null)
                    return;
                ShowFileContextMenu(tile, TilesRepeater,
                    new Windows.Foundation.Point(40, 40));
            };
            (Content as Grid)?.Children.Add(seam);
        }
#endif

        // ── Hover scale animation (WPF TileScale_MouseEnter/Leave port) ─────

        private void TileButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
            {
                AnimateTileScale(button, isHovered: true);
                // T12-C card lift: only fires for a Button that IS the card
                // root (the add tile) — the inner file/folder icon buttons
                // carry no TranslateTransform and are skipped.
                button.SetValue(TileCardHoverProperty, true);
                AnimateCardLiftTo(button, TileCardHoverLift, 120);
                // File/folder tile hover is owned by the template-root
                // Border (TileCard_PointerEntered/Exited) so the tint does
                // not flicker when sliding between icon and label; the add
                // tile has no border, so its Button keeps driving it.
                if (button.Tag is HomeTile tile && tile.IsAddTile)
                    tile.IsHovered = true;
            }
        }

        private void TileButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
            {
                AnimateTileScale(button, isHovered: false);
                button.SetValue(TileCardHoverProperty, false);
                AnimateCardLiftTo(button, 0.0, 150);
                if (button.Tag is HomeTile tile && tile.IsAddTile)
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

        // ── Tile card motion (T12-C Fluent treatment) ─────────────────────
        //
        // Each template ROOT (the add-tile Button / FolderTileBorder /
        // FileTileBorder) carries a TranslateTransform. Hover lifts the card
        // a couple of px, a press settles it back to rest while held, and a
        // staggered fade+rise entrance runs on every Loaded for an
        // UNFILTERED surface (folder navigation / first load / refresh /
        // cleared query); search rebuilds skip it — Filter() runs
        // RebuildVisibleTiles per keystroke and a per-element cascade would
        // flicker while typing. Everything routes through
        // WinUiThemeService.GetAnimationDuration — under reduced motion or
        // high contrast the values land instantly instead.
        //
        // Pointer press/release is wired with handledEventsToo from Loaded:
        // the inner tile Buttons mark PointerPressed/Released handled, so
        // plain XAML handlers on the card Border would never see the press.
        // Entrance storyboards are retained per element
        // (TileCardEntranceStoryboardProperty) and Stop()ed before the next
        // re-seed — a completed HoldEnd storyboard outranks local SetValue,
        // so the Opacity=0 / Y=8 seeds would be dead sets and a recycled
        // element would silently skip its entrance.

        private const double TileCardHoverLift = -2.0;
        private const double TileEntranceRise = 8.0;
        private const int TileEntranceStaggerMs = 25;
        private const int TileEntranceStaggerCapMs = 200;

        private static readonly DependencyProperty TileCardHoverProperty =
            DependencyProperty.RegisterAttached(
                "TileCardHover", typeof(bool), typeof(HomePage), new PropertyMetadata(false));

        private static readonly DependencyProperty TileCardWiredProperty =
            DependencyProperty.RegisterAttached(
                "TileCardWired", typeof(bool), typeof(HomePage), new PropertyMetadata(false));

        private static readonly DependencyProperty TileCardEntranceStoryboardProperty =
            DependencyProperty.RegisterAttached(
                "TileCardEntranceStoryboard", typeof(Storyboard), typeof(HomePage), new PropertyMetadata(null));

        private void TileCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement card)
                return;

            if (card.GetValue(TileCardWiredProperty) is not true)
            {
                card.SetValue(TileCardWiredProperty, true);
                card.AddHandler(PointerPressedEvent,
                    new PointerEventHandler(TileCard_PointerPressed), handledEventsToo: true);
                card.AddHandler(PointerReleasedEvent,
                    new PointerEventHandler(TileCard_PointerSettled), handledEventsToo: true);
                card.AddHandler(PointerCaptureLostEvent,
                    new PointerEventHandler(TileCard_PointerSettled), handledEventsToo: true);
            }

            // A recycled element (or one re-bound to a different tile) can
            // carry a stale hover flag / held lift from its previous
            // realization — every load starts from clean rest state.
            card.SetValue(TileCardHoverProperty, false);
            if (card.Tag is HomeTile tile)
                tile.IsHovered = false;
            AnimateCardLiftTo(card, 0.0, 60);

            PlayTileCardEntrance(card);
        }

        private void TileCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement card)
                return;
            card.SetValue(TileCardHoverProperty, true);
            if (card.Tag is HomeTile tile)
                tile.IsHovered = true;
            AnimateCardLiftTo(card, TileCardHoverLift, 120);
        }

        private void TileCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement card)
                return;
            card.SetValue(TileCardHoverProperty, false);
            if (card.Tag is HomeTile tile)
                tile.IsHovered = false;
            AnimateCardLiftTo(card, 0.0, 150);
        }

        private void TileCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Press = settle: the lifted card drops back to rest while held
            // (fluent "press down" affordance; faster than the hover lift).
            if (sender is FrameworkElement card)
                AnimateCardLiftTo(card, 0.0, 60);
        }

        private void TileCard_PointerSettled(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement card)
            {
                AnimateCardLiftTo(card,
                    card.GetValue(TileCardHoverProperty) is true ? TileCardHoverLift : 0.0, 120);
            }
        }

        /// <summary>
        /// Eases the card's TranslateTransform.Y to <paramref name="targetY"/>.
        /// Cheap, per-element, fire-and-forget — the same convention as
        /// <see cref="AnimateTileScale"/> above (dependent animation +
        /// cubic ease-out; instant when animations are disabled).
        /// </summary>
        private static void AnimateCardLiftTo(FrameworkElement card, double targetY, int requestedMs)
        {
            if (card?.RenderTransform is not TranslateTransform lift || lift.Y == targetY)
                return;

            var duration = WinUiThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(requestedMs));
            if (!WinUiThemeService.ShouldAnimate || duration == TimeSpan.Zero)
            {
                lift.Y = targetY;
                return;
            }

            var animation = new DoubleAnimation
            {
                To = targetY,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, lift);
            Storyboard.SetTargetProperty(animation, nameof(TranslateTransform.Y));
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        /// <summary>
        /// First-show card entrance: fade + short rise, staggered by the
        /// element's index inside <see cref="TilesRepeater"/> (~25 ms/step,
        /// capped) so a fresh library reads as a quick cascade rather than a
        /// wall pop. Skipped while a search query is active — Filter()
        /// rebuilds VisibleTiles per keystroke and the cascade would flicker
        /// on every character. Also skipped under reduced motion — the card
        /// stays at its XAML end-state. The previous entrance storyboard is
        /// Stop()ed before re-seeding: its HoldEnd values outrank local
        /// sets, so without the Stop a recycled element would never replay.
        /// </summary>
        private void PlayTileCardEntrance(FrameworkElement card)
        {
            var duration = WinUiThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(200));
            if (!WinUiThemeService.ShouldAnimate || duration == TimeSpan.Zero ||
                _searchQuery.Length != 0)
                return;

            var index = TilesRepeater != null
                ? Math.Max(0, TilesRepeater.GetElementIndex(card))
                : 0;
            var begin = TimeSpan.FromMilliseconds(
                Math.Min(index * TileEntranceStaggerMs, TileEntranceStaggerCapMs));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            (card.GetValue(TileCardEntranceStoryboardProperty) as Storyboard)?.Stop();
            card.Opacity = 0;
            var storyboard = new Storyboard();
            var fade = new DoubleAnimation
            {
                To = 1.0,
                Duration = duration,
                EasingFunction = ease,
                BeginTime = begin
            };
            Storyboard.SetTarget(fade, card);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            storyboard.Children.Add(fade);

            if (card.RenderTransform is TranslateTransform lift)
            {
                lift.Y = TileEntranceRise;
                var rise = new DoubleAnimation
                {
                    To = 0.0,
                    Duration = duration,
                    EasingFunction = ease,
                    BeginTime = begin,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(rise, lift);
                Storyboard.SetTargetProperty(rise, nameof(TranslateTransform.Y));
                storyboard.Children.Add(rise);
            }

            storyboard.Begin();
            card.SetValue(TileCardEntranceStoryboardProperty, storyboard);
        }

        /// <summary>
        /// Selection action-bar entrance — short fade + rise when the bar
        /// appears (mirrors the tile entrance; the Visibility binding has
        /// already flipped the card visible by the time this runs). Hide
        /// stays instant: the x:Bind collapse can't be delayed. The retained
        /// storyboard is Stop()ed before re-seeding for the same HoldEnd
        /// reason as <see cref="PlayTileCardEntrance"/>.
        /// </summary>
        private void PlaySelectionBarEntrance()
        {
            if (SelectionActionBar == null || SelectionActionBar.Visibility != Visibility.Visible)
                return;

            var duration = WinUiThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(180));
            if (!WinUiThemeService.ShouldAnimate || duration == TimeSpan.Zero)
                return;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _selectionBarEntranceStoryboard?.Stop();
            SelectionActionBar.Opacity = 0;
            if (SelectionBarRiseTransform != null)
                SelectionBarRiseTransform.Y = TileEntranceRise;

            var storyboard = new Storyboard();
            var fade = new DoubleAnimation
            {
                To = 1.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(fade, SelectionActionBar);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            storyboard.Children.Add(fade);

            if (SelectionBarRiseTransform != null)
            {
                var rise = new DoubleAnimation
                {
                    To = 0.0,
                    Duration = duration,
                    EasingFunction = ease,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(rise, SelectionBarRiseTransform);
                Storyboard.SetTargetProperty(rise, nameof(TranslateTransform.Y));
                storyboard.Children.Add(rise);
            }

            storyboard.Begin();
            _selectionBarEntranceStoryboard = storyboard;
        }

        private Storyboard _selectionBarEntranceStoryboard;
        private Storyboard _overlayFadeStoryboard;

        /// <summary>
        /// Single funnel for every <see cref="DragDropOverlay"/> visibility
        /// flip (DragOver/DragLeave/Drop/DropCompleted/folder-hover). Showing
        /// fades the overlay in (~120 ms); hiding stays instant and also
        /// stops the retained fade so no HoldEnd value lingers over a
        /// collapsed element. DragOver re-fires continuously while the
        /// payload hovers, so an already-visible overlay is left alone
        /// rather than restarting the fade. The tint layer goes fully
        /// opaque under <see cref="WinUiThemeService.ReduceTransparency"/>.
        /// </summary>
        private void SetDragDropOverlayVisible(bool visible)
        {
            if (DragDropOverlay == null)
                return;

            if (!visible)
            {
                // Stop first — a mid-flight fade would keep its HoldEnd
                // opacity over the collapsed element and fight the reset.
                _overlayFadeStoryboard?.Stop();
                DragDropOverlay.Visibility = Visibility.Collapsed;
                DragDropOverlay.Opacity = 1.0;
                return;
            }

            if (DragDropOverlay.Visibility == Visibility.Visible)
                return;

            if (DragDropOverlayTint != null)
                DragDropOverlayTint.Opacity = WinUiThemeService.ReduceTransparency ? 1.0 : 0.6;

            DragDropOverlay.Visibility = Visibility.Visible;
            var duration = WinUiThemeService.GetAnimationDuration(TimeSpan.FromMilliseconds(120));
            if (!WinUiThemeService.ShouldAnimate || duration == TimeSpan.Zero)
            {
                DragDropOverlay.Opacity = 1.0;
                return;
            }

            _overlayFadeStoryboard?.Stop();
            DragDropOverlay.Opacity = 0;
            var fade = new DoubleAnimation
            {
                To = 1.0,
                Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(fade, DragDropOverlay);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            storyboard.Children.Add(fade);
            storyboard.Begin();
            _overlayFadeStoryboard = storyboard;
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

        // ── Empty state (T13-B Fluent pattern) ──────────────────────────
        // Two variants share one bound panel: library-empty (no content
        // tiles at all — icon FolderOpen + "open or create" CTA hosting the
        // same flyout the add tile shows) and search-no-results (icon Search
        // + "No matches for 'x'" + Clear search). The tiles repeater
        // collapses while the panel shows so the lone add tile can't leak
        // through under the copy.

        /// <summary>True when no folder/file tiles are visible (add tile aside).</summary>
        private bool HasContentTiles => VisibleTiles.Any(t => !t.IsAddTile);

        /// <summary>True while a trimmed search query is active.</summary>
        private bool HasActiveSearch => _searchQuery.Length != 0;

        public Visibility EmptyStateVisibility =>
            HasContentTiles ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Repeater hides whenever the empty state owns the surface.</summary>
        public Visibility TilesRepeaterVisibility =>
            HasContentTiles ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EmptyStateLibraryIconVisibility =>
            HasActiveSearch ? Visibility.Collapsed : Visibility.Visible;

        public Visibility EmptyStateSearchIconVisibility =>
            HasActiveSearch ? Visibility.Visible : Visibility.Collapsed;

        public string EmptyStateTitle => HasActiveSearch
            ? LocalizationService.Format("Home.Empty.SearchTitle", _searchQuery)
            : LocalizationService.Get("Home.Empty.LibraryTitle");

        public string EmptyStateHint => HasActiveSearch
            ? LocalizationService.Get("Home.Empty.SearchHint")
            : LocalizationService.Get("Home.Empty.LibraryHint");

        public string EmptyStateActionText =>
            LocalizationService.Get("Home.Empty.LibraryAction");

        public string EmptyStateClearText =>
            LocalizationService.Get("Home.Empty.SearchClear");

        public Visibility EmptyStateActionVisibility =>
            HasActiveSearch ? Visibility.Collapsed : Visibility.Visible;

        public Visibility EmptyStateClearVisibility =>
            HasActiveSearch ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Re-raises the bound empty-state surface after <see cref="VisibleTiles"/>
        /// or the localization catalog changes. Property-setter pattern: the
        /// panel only repaints when the underlying state actually moved.
        /// </summary>
        private void RefreshEmptyState()
        {
            OnPropertyChanged(nameof(EmptyStateVisibility));
            OnPropertyChanged(nameof(TilesRepeaterVisibility));
            OnPropertyChanged(nameof(EmptyStateLibraryIconVisibility));
            OnPropertyChanged(nameof(EmptyStateSearchIconVisibility));
            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateHint));
            OnPropertyChanged(nameof(EmptyStateActionText));
            OnPropertyChanged(nameof(EmptyStateClearText));
            OnPropertyChanged(nameof(EmptyStateActionVisibility));
            OnPropertyChanged(nameof(EmptyStateClearVisibility));
        }

        private void EmptyStateAction_Click(object sender, RoutedEventArgs e)
        {
            // Same flyout the add tile hosts — one affordance, two surfaces.
            if (sender is FrameworkElement placementTarget)
                ShowAddTileMenu(placementTarget);
        }

        private void EmptyStateClearSearch_Click(object sender, RoutedEventArgs e)
        {
            GetMainWindow()?.ClearHomeSearch();
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
            // WPF parity: every (re)navigation reloads from RecentFilesService.
            try
            {
                await RefreshCurrentFolderAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", ex.Message));
            }
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
            // HomeTiles was just rebuilt, so any folder highlights from an
            // armed choose-move-target are gone — re-derive them from the
            // flag (WPF parity: highlights were bound to the page property).
            UpdateFolderPlacementHighlights();
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
            RefreshEmptyState();
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

            var openItem = CreateMenuItem(LocalizationService.Get("Home.Menu.OpenFile"), "FileText");
            openItem.Click += async (_, _) => await PickAndOpenPdfAsync();
            menu.Items.Add(openItem);

            var createFolderItem = CreateMenuItem(LocalizationService.Get("Home.Menu.CreateFolder"), "FolderPlus");
            createFolderItem.Click += async (_, _) => await CreateFolderAsync();
            menu.Items.Add(createFolderItem);

            var createNotebookItem = CreateMenuItem(LocalizationService.Get("Home.Menu.CreateNotebook"), "FilePlus");
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

            var openItem = CreateMenuItem(LocalizationService.Get("Home.Context.Open"), "FileText");
            openItem.Click += async (_, _) => await OpenFileTileAsync(tile);
            menu.Items.Add(openItem);

            var renameItem = CreateMenuItem(LocalizationService.Get("Home.Context.Rename"), "Pencil");
            renameItem.Click += async (_, _) => await RenameTileAsync(tile);
            menu.Items.Add(renameItem);

            var selectItem = CreateMenuItem(LocalizationService.Get("Home.Context.Select"), "SquareCheck");
            selectItem.Click += (_, _) =>
            {
                if (!IsSelectionMode)
                    ToggleSelectionMode();
                ToggleTileSelection(tile);
            };
            menu.Items.Add(selectItem);

            if (IsInsideFolder)
            {
                var moveToRootItem = CreateMenuItem(LocalizationService.Get("Home.Context.MoveToLibrary"), "Move");
                moveToRootItem.Click += async (_, _) =>
                {
                    try
                    {
                        RecentFilesService.MoveToLibraryRoot(tile.Path);
                        await RefreshCurrentFolderAsync();
                    }
                    catch (Exception ex)
                    {
                        await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                            LocalizationService.Format("Home.OperationFailed", ex.Message));
                    }
                };
                menu.Items.Add(moveToRootItem);
            }

            var copyItem = CreateMenuItem(LocalizationService.Get("Home.Context.CopyPath"), "Copy");
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

            var openFolderItem = CreateMenuItem(LocalizationService.Get("Home.Context.OpenFolder"), "FolderOpen");
            openFolderItem.Click += (_, _) => OpenContainingFolder(tile);
            menu.Items.Add(openFolderItem);

            var exportItem = CreateMenuItem(LocalizationService.Get("Home.Context.Export"), "Download");
            exportItem.Click += async (_, _) => await ExportTileAsync(tile);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = CreateMenuItem(LocalizationService.Get("Home.Context.Delete"), "Trash2", foregroundResourceKey: "ThemeDangerBrush");
            deleteItem.Click += async (_, _) => await DeleteFileTileAsync(tile);
            menu.Items.Add(deleteItem);

            var removeItem = CreateMenuItem(LocalizationService.Get("Home.Context.Remove"), "FileMinus", foregroundResourceKey: "ThemeDangerBrush");
            removeItem.Click += async (_, _) => await RemoveFileTileAsync(tile);
            menu.Items.Add(removeItem);

            menu.ShowAt(placementTarget, new FlyoutShowOptions { Position = position });
        }

        private void ShowFolderContextMenu(HomeTile tile, FrameworkElement placementTarget, Windows.Foundation.Point position)
        {
            var menu = new MenuFlyout();

            var openItem = CreateMenuItem(LocalizationService.Get("Home.Context.Open"), "FolderOpen");
            openItem.Click += (_, _) =>
            {
                _currentFolderId = tile.Id;
                _currentFolderName = tile.FileName;
                _ = RefreshCurrentFolderAsync();
            };
            menu.Items.Add(openItem);

            var renameItem = CreateMenuItem(LocalizationService.Get("Home.Context.Rename"), "Pencil");
            renameItem.Click += async (_, _) => await RenameTileAsync(tile);
            menu.Items.Add(renameItem);

            // Folder color submenu (WPF nested MenuItem → MenuFlyoutSubItem).
            // T13-B: the parent glyph is a dot in the tile's current color —
            // it doubles as a state readout and matches the swatch language
            // of the submenu rows below.
            var colorItem = new MenuFlyoutSubItem
            {
                Text = LocalizationService.Get("Home.Context.Color"),
                Icon = new PathIcon
                {
                    Data = new EllipseGeometry
                    {
                        Center = new Windows.Foundation.Point(8, 8),
                        RadiusX = 7,
                        RadiusY = 7
                    },
                    Foreground = WinUiThemeService.CreateBrush(
                        string.IsNullOrWhiteSpace(tile.Color) ? FolderColorSwatches[0].Hex : tile.Color),
                    Width = 16,
                    Height = 16
                }
            };
            foreach (var swatch in FolderColorSwatches)
            {
                // T12-C: a color-dot icon per swatch (the only menu rows that
                // lacked one) — PathIcon ellipse filled with the swatch hex,
                // same glyph-icon convention as the sibling items.
                var swatchItem = new MenuFlyoutItem
                {
                    Text = LocalizeFolderColor(swatch.Key),
                    Tag = swatch.Hex,
                    Icon = new PathIcon
                    {
                        Data = new EllipseGeometry
                        {
                            Center = new Windows.Foundation.Point(8, 8),
                            RadiusX = 7,
                            RadiusY = 7
                        },
                        Foreground = WinUiThemeService.CreateBrush(swatch.Hex),
                        Width = 16,
                        Height = 16
                    }
                };
                swatchItem.Click += async (_, _) =>
                {
                    try
                    {
                        RecentFilesService.SetFolderColor(tile.Id, swatch.Hex);
                        await RefreshCurrentFolderAsync();
                    }
                    catch (Exception ex)
                    {
                        await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                            LocalizationService.Format("Home.OperationFailed", ex.Message));
                    }
                };
                colorItem.Items.Add(swatchItem);
            }
            menu.Items.Add(colorItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var removeItem = CreateMenuItem(LocalizationService.Get("Home.Context.RemoveFolder"), "FolderMinus", foregroundResourceKey: "ThemeDangerBrush");
            removeItem.Click += async (_, _) =>
            {
                try
                {
                    RecentFilesService.RemoveFolder(tile.Id);
                    await RefreshCurrentFolderAsync();
                }
                catch (Exception ex)
                {
                    await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                        LocalizationService.Format("Home.OperationFailed", ex.Message));
                }
            };
            menu.Items.Add(removeItem);

            menu.ShowAt(placementTarget, new FlyoutShowOptions { Position = position });
        }

        /// <summary>
        /// Builds a flyout item with a Lucide <see cref="PathIcon"/> + themed
        /// foreground — the WinUI port of WPF <c>CreateMenuItem</c>. The Icon
        /// slot requires an <see cref="IconElement"/>, so the shape-based
        /// <c>LucideIcon</c> control can't host it; PathIcon +
        /// <see cref="LucideIcon.GetIconGeometry"/> is the T13-A convention.
        /// Menus are built per-show, so they localize on construction and no
        /// open-menu refresh pass is needed.
        /// </summary>
        private MenuFlyoutItem CreateMenuItem(string text, string iconKind, Brush foreground = null, string foregroundResourceKey = null)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = MenuIcon(iconKind)
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

        /// <summary>
        /// T13-B shared menu glyph (T13-A convention): a 16-DIP PathIcon
        /// carrying the Lucide geometry. Foreground is left to
        /// value-inheritance so destructive items (danger Foreground) tint
        /// their icon automatically.
        /// </summary>
        private static PathIcon MenuIcon(string kind) => new()
        {
            Data = LucideIcon.GetIconGeometry(kind),
            Width = 16,
            Height = 16,
        };

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

                try
                {
                    RecentFilesService.RenameFolder(tile.Id, newName.Trim());
                    await RefreshCurrentFolderAsync();
                }
                catch (Exception ex)
                {
                    await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                        LocalizationService.Format("Home.RenameFailed", ex.Message));
                }
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

            // T13-B: prompt label rides the subtle text brush like the shared
            // dialog service body — the only body typography the chrome-free
            // ContentDialog gets.
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = prompt,
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = ResolveThemeBrush("ThemeSubtleForegroundBrush", "#6B7280")
                        },
                        inputBox
                    }
                },
                PrimaryButtonText = confirmText,
                CloseButtonText = LocalizationService.Get("Common.Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };

            // Local dialogs must share the WinUiDialogService gate — two
            // ContentDialogs on one XamlRoot throw InvalidOperationException.
            var result = await WinUiDialogService.RunUnderDialogGateAsync(() => dialog.ShowAsync().AsTask());
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
                Frame?.Navigate(typeof(EditorPage), pdfPath, GetEditorNavTransitionInfo());
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

            try
            {
                RecentFilesService.CreateFolder(name, _currentFolderId);
                await RefreshCurrentFolderAsync();
                GetMainWindow()?.ShowToast(LocalizationService.Format("Home.FolderCreated", name.Trim()), "\uE8B7");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", ex.Message));
            }
        }

        /// <summary>
        /// WPF <c>PageTemplatePickerWindow</c> notebook-creation mode via the
        /// shared <see cref="PageTemplatePickerDialog"/> (Task 9 full port —
        /// replaces the earlier compact stand-in): 3×3 card grid with
        /// localized title+hint, the "save to" folder row, and a Create
        /// button gated on a chosen folder. Returns (template, folderPath)
        /// or null when dismissed.
        /// </summary>
        private async Task<(PageInsertTemplate Template, string FolderPath)?> PickNotebookTemplateAsync()
        {
            var xamlRoot = XamlRoot;
            if (xamlRoot == null || GetMainWindow() == null)
                return null;

            var dialog = new PageTemplatePickerDialog(GetDefaultNotebookDirectory())
            {
                XamlRoot = xamlRoot,
            };
            // Gate: ContentDialog allows only one open instance per XamlRoot —
            // serialize with the WinUiDialogService dialogs.
            await WinUiDialogService.RunUnderDialogGateAsync(() => dialog.ShowAsync().AsTask());
            if (!dialog.IsConfirmed || string.IsNullOrWhiteSpace(dialog.SelectedFolderPath))
                return null;

            return (dialog.SelectedTemplate, dialog.SelectedFolderPath);
        }

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
                    Frame?.Navigate(typeof(EditorPage), notebookPath, GetEditorNavTransitionInfo());
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

            try
            {
                RecentFilesService.AddOrPromote(path, null, lastModifiedUtc, folderId, isNotebook);
                await RefreshCurrentFolderAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", ex.Message));
            }
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
                    Frame?.Navigate(typeof(EditorPage), tile.Path, GetEditorNavTransitionInfo());
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

            try
            {
                if (TryDeleteLibraryFile(tile.Path) && GetMainWindow() is MainWindow mw)
                    mw.ShowToast(LocalizationService.Format("Home.Selection.DeletedCount", 1), "Trash2");

                await RefreshCurrentFolderAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", ex.Message));
            }
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

            try
            {
                RecentFilesService.Remove(tile.Path);
                await RefreshCurrentFolderAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", ex.Message));
            }
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

        private async void NavigateUpButton_Click(object sender, RoutedEventArgs e)
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
            try
            {
                await RefreshCurrentFolderAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", ex.Message));
            }
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

            // Copy-only, matching the WPF DragDropEffects.Copy invariant pinned
            // by HomePageLibrarySourceTests: with Move advertised, a same-volume
            // Explorer drop could relocate the library PDF and dangle the entry.
            // In-app folder moves never reach this negotiation — they ride the
            // custom Caelum.LibraryTilePath(s) payloads through our own handlers.
            e.AllowedOperations = DataPackageOperation.Copy;
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

        /// <summary>
        /// Drag completion probe: the OS may only ever negotiate
        /// <see cref="DataPackageOperation.Copy"/> out of this drag
        /// (AllowedOperations is Copy-only) — logging DropResult keeps the
        /// end-to-end invariant observable in the DEBUG smoke log.
        /// </summary>
        private void FileTile_DropCompleted(UIElement sender, DropCompletedEventArgs args)
        {
            HomeSmokeLog($"drag-completed result={args.DropResult}");
            // A cancelled/Esc'd drag ends here too — re-derive folder
            // highlights (an armed move-target stays lit, drag-hover goes
            // dark) and hide the page overlay so nothing sticks.
            UpdateFolderPlacementHighlights();
            SetDragDropOverlayVisible(false);
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
            Exception failure = null;

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

                if (movedAny)
                {
                    await RefreshCurrentFolderAsync();
                    GetMainWindow()?.ShowToast(LocalizationService.Format("Home.MovedToFolder", tile.FileName), "\uE8B7");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                deferral.Complete();
            }

            if (failure != null)
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", failure.Message));

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
            SetDragDropOverlayVisible(false);
            e.Handled = true;
        }

        // ── Drop: page background (external file import / move to folder) ───

        private void HomePage_DragOver(object sender, DragEventArgs e)
        {
            if (GetFolderTileFromSource(e.OriginalSource as DependencyObject) != null)
            {
                SetDragDropOverlayVisible(false);
                return;
            }

            var hasLibraryPaths = HomePageDragDropHelper.HasLibraryTilePaths(e.DataView);
            var hasStorageItems = HomePageDragDropHelper.HasStorageItems(e.DataView);
            if (hasLibraryPaths || hasStorageItems)
            {
                e.AcceptedOperation = hasLibraryPaths ? DataPackageOperation.Move : DataPackageOperation.Copy;
                SetDragDropOverlayVisible(true);
                e.Handled = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
                SetDragDropOverlayVisible(false);
            }
        }

        private void HomePage_DragLeave(object sender, DragEventArgs e)
        {
            SetDragDropOverlayVisible(false);
        }

        private async void HomePage_Drop(object sender, DragEventArgs e)
        {
            SetDragDropOverlayVisible(false);

            if (GetFolderTileFromSource(e.OriginalSource as DependencyObject) != null)
                return;

            var deferral = e.GetDeferral();
            bool movedAny = false;
            Exception failure = null;

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

                if (movedAny)
                {
                    await RefreshCurrentFolderAsync();
                    var targetName = IsInsideFolder ? _currentFolderName : LocalizationService.Get("Home.LibraryRoot");
                    GetMainWindow()?.ShowToast(LocalizationService.Format("Home.MovedToFolder", targetName), "\uE8B7");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                deferral.Complete();
            }

            if (failure != null)
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", failure.Message));

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

        /// <summary>
        /// T12-C: home → editor navigations drill in (Fluent "open a detail"
        /// motif); reduced-motion / high-contrast users get an explicit
        /// suppress so the fallback <c>Frame?.Navigate</c> path stays instant
        /// even when it bypasses <see cref="MainWindow"/>'s transition gate.
        /// </summary>
        private static NavigationTransitionInfo GetEditorNavTransitionInfo()
        {
            return WinUiThemeService.ShouldAnimate
                ? (NavigationTransitionInfo)new DrillInNavigationTransitionInfo()
                : new SuppressNavigationTransitionInfo();
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
