using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Caelum.Services;

namespace Caelum.Pages
{
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

            OnPropertyChanged(nameof(IsSelectionMode));
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
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(HomeTiles);
            return view.Cast<object>()
                .OfType<HomeTile>()
                .Where(tile => tile.IsFile);
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

            RefreshSelectionState();
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
            RefreshOpenContextMenus();
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

            foreach (var tile in selectedTiles)
            {
                if (!string.IsNullOrWhiteSpace(tile.Path))
                    RecentFilesService.Remove(tile.Path);
            }

            await RefreshCurrentFolderAsync();
            RefreshSelectionState();

            if (Window.GetWindow(this) is MainWindow mw)
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

            var dialog = new SaveFileDialog
            {
                Filter = LocalizationService.Get("Home.PdfFilter"),
                Title = LocalizationService.Get("Home.ExportTitle"),
                FileName = Path.GetFileName(tile.Path),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                PdfAtomicFile.CopyFile(tile.Path, dialog.FileName);
                if (Window.GetWindow(this) is MainWindow mw)
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

            foreach (var tile in selectedTiles)
                RecentFilesService.MoveToFolder(tile.Path, folderId);

            IsChoosingMoveTarget = false;
            ClearFolderPlacementHighlights();
            await RefreshCurrentFolderAsync();
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

            var owner = Window.GetWindow(this);
            bool? confirmed = await DialogService.ShowDangerConfirmAsync(
                owner,
                LocalizationService.Get("Home.Selection.DeleteTitle"),
                LocalizationService.Format("Home.Selection.DeleteMessage", selectedTiles.Count),
                LocalizationService.Get("Common.Cancel"),
                LocalizationService.Get("Home.Selection.Delete"));
            if (confirmed != true)
                return;

            int deleted = 0;
            foreach (var tile in selectedTiles)
            {
                if (TryDeleteLibraryFile(tile.Path))
                    deleted++;
            }

            await RefreshCurrentFolderAsync();
            RefreshSelectionState();
            if (Window.GetWindow(this) is MainWindow mw && deleted > 0)
                mw.ShowToast(LocalizationService.Format("Home.Selection.DeletedCount", deleted), "Trash2");
        }

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
