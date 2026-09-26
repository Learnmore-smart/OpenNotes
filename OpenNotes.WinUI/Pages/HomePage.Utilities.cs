using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caelum.Models;
using Caelum.Services;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Caelum.Pages
{
    /// <summary>
    /// WinUI port of the WPF <c>Pages/HomePage.Utilities.cs</c>: INPC surface
    /// for x:Bind, selection-mode state/actions, move-selection-to-folder
    /// flow, remove/delete-selected flows, recycle-bin delete helper, export,
    /// and open-containing-folder. Semantics are 1:1 with the WPF version;
    /// dialogs go through <see cref="WinUiDialogService"/> (ContentDialog) and
    /// <see cref="FileSavePicker"/> replaces <c>SaveFileDialog</c>.
    /// </summary>
    public sealed partial class HomePage : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsChoosingMoveTarget { get; private set; }

        public bool CanChooseMoveTarget =>
            HasSelectedTiles && (IsInsideFolder || HomeTiles.Any(tile => tile.IsFolder));

        public int SelectedTileCount => HomeTiles.Count(tile => tile.IsFile && tile.IsSelected);

        public bool HasSelectedTiles => SelectedTileCount > 0;

        public bool CanSelectAllTiles => GetVisibleFileTiles().Any(tile => !tile.IsSelected);

        public string SelectionSummary => HasSelectedTiles
            ? LocalizationService.Format("Home.Selection.Count", SelectedTileCount)
            : LocalizationService.Get("Home.Selection.None");

        public string SelectionHint => IsChoosingMoveTarget
            ? LocalizationService.Get("Home.Selection.MoveHint")
            : LocalizationService.Get("Home.Selection.Hint");

        public string SelectionClearText => LocalizationService.Get("Home.Selection.Clear");

        public string SelectionDoneText => LocalizationService.Get("Home.Selection.Done");

        public string SelectionMoveText => LocalizationService.Get("Home.Selection.Move");

        public string SelectionRemoveText => LocalizationService.Get("Home.Selection.Remove");

        public string SelectionDeleteText => LocalizationService.Get("Home.Selection.Delete");

        public string SelectionSelectAllText => LocalizationService.Get("Home.Selection.SelectAll");

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void RefreshSelectionState()
        {
            if (!HasSelectedTiles && IsChoosingMoveTarget)
            {
                IsChoosingMoveTarget = false;
                ClearFolderPlacementHighlights();
            }

            // Push the page-level flag onto every tile — the WinUI stand-in
            // for the WPF RelativeSource=Page binding on the check badge.
            foreach (var tile in HomeTiles)
                tile.IsSelectionModeUi = IsSelectionMode;

            OnPropertyChanged(nameof(IsSelectionMode));
            OnPropertyChanged(nameof(SelectionBarVisibility));
            OnPropertyChanged(nameof(IsChoosingMoveTarget));
            OnPropertyChanged(nameof(CanChooseMoveTarget));
            OnPropertyChanged(nameof(SelectedTileCount));
            OnPropertyChanged(nameof(HasSelectedTiles));
            OnPropertyChanged(nameof(CanSelectAllTiles));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(SelectionHint));
            OnPropertyChanged(nameof(SelectionClearText));
            OnPropertyChanged(nameof(SelectionDoneText));
            OnPropertyChanged(nameof(SelectionMoveText));
            OnPropertyChanged(nameof(SelectionRemoveText));
            OnPropertyChanged(nameof(SelectionDeleteText));
            OnPropertyChanged(nameof(SelectionSelectAllText));
            GetMainWindow()?.RefreshSelectButtonVisualState();
        }

        private IEnumerable<HomeTile> GetVisibleFileTiles()
        {
            // VisibleTiles is the filtered/sorted view the repeater shows —
            // the WinUI stand-in for WPF's GetDefaultView(HomeTiles).
            return VisibleTiles.Where(tile => tile.IsFile);
        }

        private void SetSelectionMode(bool isEnabled)
        {
            if (IsSelectionMode == isEnabled)
                return;

            IsSelectionMode = isEnabled;
            if (!IsSelectionMode)
            {
                IsChoosingMoveTarget = false;
                ClearSelectedTiles(refreshState: false);
                ClearFolderPlacementHighlights();
            }

            // The add tile is hidden while selecting (WPF collapsed it via a
            // DataTrigger; here it is filtered out of the visible list).
            RebuildVisibleTiles();
            RefreshSelectionState();
            if (isEnabled)
                PlaySelectionBarEntrance();
        }

        private void ToggleTileSelection(HomeTile tile)
        {
            if (tile == null || !tile.IsFile)
                return;

            tile.IsSelected = !tile.IsSelected;
            RefreshSelectionState();
        }

        private void ClearSelectedTiles(bool refreshState = true)
        {
            foreach (var tile in HomeTiles)
            {
                if (tile.IsFile && tile.IsSelected)
                    tile.IsSelected = false;
            }

            if (refreshState)
                RefreshSelectionState();
        }

        public void ApplyLocalization()
        {
            if (DragDropOverlayText != null)
                DragDropOverlayText.Text = LocalizationService.Get("Home.OpenDocumentTitle");

            foreach (var tile in HomeTiles)
                tile.RefreshDisplay();

            UpdateHeaderText();
            RefreshSelectionState();
            // No RefreshOpenContextMenus port: menus are MenuFlyouts built
            // per-show, so they always open already localized.
        }

        /// <summary>Re-raises every computed brush/text binding after a theme apply.</summary>
        internal void RefreshTileVisualState()
        {
            foreach (var tile in HomeTiles)
                tile.RefreshDisplay();
        }

        public void ToggleSelectionMode()
        {
            SetSelectionMode(!IsSelectionMode);
        }

        private void SelectAllVisibleTiles()
        {
            foreach (var tile in GetVisibleFileTiles())
            {
                tile.IsSelected = true;
            }

            RefreshSelectionState();
        }

        private async Task RemoveSelectedTilesAsync()
        {
            var selectedTiles = HomeTiles
                .Where(tile => tile.IsFile && tile.IsSelected)
                .ToList();

            if (selectedTiles.Count == 0)
                return;

            try
            {
                foreach (var tile in selectedTiles)
                {
                    if (!string.IsNullOrWhiteSpace(tile.Path))
                        RecentFilesService.Remove(tile.Path);
                }

                await RefreshCurrentFolderAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", ex.Message));
            }

            RefreshSelectionState();

            if (GetMainWindow() is MainWindow mw)
                mw.ShowToast(LocalizationService.Format("Home.Selection.RemovedCount", selectedTiles.Count), "\uE74D");

            await Task.CompletedTask;
        }

        private void SelectAllSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAllVisibleTiles();
        }

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            ClearSelectedTiles();
        }

        private async void RemoveSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            await RemoveSelectedTilesAsync();
        }

        private void DoneSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectionMode(false);
        }

        private async Task ExportTileAsync(HomeTile tile)
        {
            if (tile == null || tile.IsAddTile || string.IsNullOrWhiteSpace(tile.Path) || !File.Exists(tile.Path))
                return;

            var mainWindow = GetMainWindow();
            if (mainWindow == null)
                return;

            // FileSavePicker replaces the WPF SaveFileDialog. The
            // "PDF Files (*.pdf)|*.pdf" catalog string is a WinForms filter —
            // FileTypeChoices takes a plain display name + extension list.
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = Path.GetFileNameWithoutExtension(tile.Path),
                DefaultFileExtension = ".pdf",
                CommitButtonText = LocalizationService.Get("Home.ExportTitle")
            };
            picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(mainWindow));

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            try
            {
                PdfAtomicFile.CopyFile(tile.Path, file.Path);
                if (GetMainWindow() is MainWindow mw)
                    mw.ShowToast(LocalizationService.Get("Home.ExportSucceeded"), "\uEDE1");
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"), LocalizationService.Format("Home.ExportFailed", ex.Message));
            }
        }

        private void MoveSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!HasSelectedTiles)
                return;

            if (!CanChooseMoveTarget)
                return;

            IsChoosingMoveTarget = !IsChoosingMoveTarget;
            UpdateFolderPlacementHighlights();
            RefreshSelectionState();
        }

        private async Task MoveSelectedTilesToFolderAsync(string folderId)
        {
            var selectedTiles = HomeTiles
                .Where(candidate => candidate.IsFile && candidate.IsSelected)
                .ToList();
            if (selectedTiles.Count == 0)
                return;

            try
            {
                foreach (var tile in selectedTiles)
                    RecentFilesService.MoveToFolder(tile.Path, folderId);

                IsChoosingMoveTarget = false;
                ClearFolderPlacementHighlights();
                await RefreshCurrentFolderAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", ex.Message));
            }

            ClearSelectedTiles();
        }

        private void UpdateFolderPlacementHighlights()
        {
            foreach (var tile in HomeTiles.Where(candidate => candidate.IsFolder))
                tile.IsDropTarget = IsChoosingMoveTarget;
        }

        private void ClearFolderPlacementHighlights()
        {
            foreach (var tile in HomeTiles.Where(candidate => candidate.IsFolder))
                tile.IsDropTarget = false;
        }

        private async void DeleteSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            await DeleteSelectedTilesAsync();
        }

        private async Task DeleteSelectedTilesAsync()
        {
            var selectedTiles = HomeTiles
                .Where(tile => tile.IsFile && tile.IsSelected)
                .ToList();
            if (selectedTiles.Count == 0)
                return;

            bool? confirmed = await WinUiDialogService.ShowDangerConfirmAsync(
                XamlRoot,
                LocalizationService.Get("Home.Selection.DeleteTitle"),
                LocalizationService.Format("Home.Selection.DeleteMessage", selectedTiles.Count),
                LocalizationService.Get("Common.Cancel"),
                LocalizationService.Get("Home.Selection.Delete"));
            if (confirmed != true)
                return;

            int deleted = 0;
            try
            {
                foreach (var tile in selectedTiles)
                {
                    if (TryDeleteLibraryFile(tile.Path))
                        deleted++;
                }

                await RefreshCurrentFolderAsync();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(LocalizationService.Get("Common.Error"),
                    LocalizationService.Format("Home.OperationFailed", ex.Message));
            }

            RefreshSelectionState();
            if (GetMainWindow() is MainWindow mw && deleted > 0)
                mw.ShowToast(LocalizationService.Format("Home.Selection.DeletedCount", deleted), "Trash2");
        }

        /// <summary>Recycle-bin delete + library remove (WPF parity).</summary>
        internal static bool TryDeleteLibraryFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            bool recycled = !File.Exists(path) || RecycleBinService.TrySendToRecycleBin(path);
            if (!recycled)
                return false;

            RecentFilesService.Remove(path);
            return true;
        }

        private void OpenContainingFolder(HomeTile tile)
        {
            if (tile == null || tile.IsAddTile || string.IsNullOrWhiteSpace(tile.Path) || !File.Exists(tile.Path))
                return;

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{tile.Path}\"")
            {
                UseShellExecute = true
            });
        }
    }
}
