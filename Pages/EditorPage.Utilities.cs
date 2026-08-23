using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Caelum.Models;
using Caelum.Services;

namespace Caelum.Pages
{
    public sealed partial class EditorPage
    {
        private bool _i18nHooksInstalled;

        public bool IsSelectionMode => _currentTool == ToolType.None;

        public void ToggleSelectionMode()
        {
            if (IsSelectionMode)
            {
                var fallbackTool = _previousTool == ToolType.None ? ToolType.Pen : _previousTool;
                ActivateTool(fallbackTool);
                return;
            }

            _previousTool = _currentTool == ToolType.None ? ToolType.Pen : _currentTool;
            ActivateTool(ToolType.None);
        }

        public void ApplyLocalization()
        {
            InstallLocalizationHooks();

            if (LoadingText != null)
                LoadingText.Text = LocalizationService.Get("Editor.Loading");
            if (PrintMenuItem != null)
                PrintMenuItem.Header = LocalizationService.Get("Editor.PrintTooltip");
            if (ExportCurrentPagePng1xMenuItem != null)
                ExportCurrentPagePng1xMenuItem.Header = LocalizationService.Format("Editor.CurrentPagePng", 1);
            if (ExportCurrentPagePng2xMenuItem != null)
                ExportCurrentPagePng2xMenuItem.Header = LocalizationService.Format("Editor.CurrentPagePng", 2);
            if (ExportAllPagesPng1xMenuItem != null)
                ExportAllPagesPng1xMenuItem.Header = LocalizationService.Format("Editor.AllPagesPng", 1);
            if (ExportAllPagesPng2xMenuItem != null)
                ExportAllPagesPng2xMenuItem.Header = LocalizationService.Format("Editor.AllPagesPng", 2);
            if (InsertPdfPageMenuItem != null)
                InsertPdfPageMenuItem.Header = LocalizationService.Get("Editor.InsertPdfPage");
            if (InsertImagePageMenuItem != null)
                InsertImagePageMenuItem.Header = LocalizationService.Get("Editor.InsertImagePage");
            if (RotateCurrentPageMenuItem != null)
                RotateCurrentPageMenuItem.Header = LocalizationService.Get("Editor.RotateCurrentPage");
            if (UndoButton != null)
                UndoButton.ToolTip = LocalizationService.Get("Editor.UndoTooltip");
            if (RedoButton != null)
                RedoButton.ToolTip = LocalizationService.Get("Editor.RedoTooltip");
            if (PenToolButton != null)
                PenToolButton.ToolTip = LocalizationService.Get("Editor.PenTooltip");
            if (HighlighterToolButton != null)
                HighlighterToolButton.ToolTip = LocalizationService.Get("Editor.HighlighterTooltip");
            if (HiddenInkToolButton != null)
                HiddenInkToolButton.ToolTip = LocalizationService.Get("Editor.HiddenInkTooltip");
            if (StickyNoteToolButton != null)
                StickyNoteToolButton.ToolTip = LocalizationService.Get("Editor.StickyNoteTooltip");
            if (EraserToolButton != null)
                EraserToolButton.ToolTip = LocalizationService.Get("Editor.EraserTooltip");
            if (ShapeToolButton != null)
                ShapeToolButton.ToolTip = LocalizationService.Get("Editor.ModeShape");
            if (LaserToolButton != null)
                LaserToolButton.ToolTip = LocalizationService.Get("Editor.ModeLaser");
            if (RulerToolButton != null)
                RulerToolButton.ToolTip = LocalizationService.Get("Editor.RulerTooltip");
            if (TextToolButton != null)
                TextToolButton.ToolTip = LocalizationService.Get("Editor.TextTooltip");
            if (SelectToolButton != null)
                SelectToolButton.ToolTip = LocalizationService.Get("Editor.SelectTooltip");
            if (SavePdfButton != null)
                SavePdfButton.ToolTip = LocalizationService.Get("Editor.SaveDocumentTooltip");
            if (VersionHistoryButton != null)
                VersionHistoryButton.ToolTip = LocalizationService.Get("Editor.VersionHistoryTooltip");
            if (PenOnlyButton != null)
                PenOnlyButton.ToolTip = LocalizationService.Get("Editor.PenOnlyTooltip");
            if (ZoomOutButton != null)
                ZoomOutButton.ToolTip = LocalizationService.Get("Editor.ZoomOutTooltip");
            if (ZoomInButton != null)
                ZoomInButton.ToolTip = LocalizationService.Get("Editor.ZoomInTooltip");
            if (ZoomLabel != null)
                ZoomLabel.ToolTip = LocalizationService.Get("Editor.ZoomEditTooltip");
            if (PageJumpBorder != null)
                PageJumpBorder.ToolTip = LocalizationService.Get("Editor.PageJumpTooltip");
            if (FitWidthButton != null)
                FitWidthButton.ToolTip = LocalizationService.Get("Editor.FitWidthTooltip");
            if (FitWidthLabel != null)
                FitWidthLabel.Text = LocalizationService.Get("Editor.FitWidthTooltip");
            if (FitPageButton != null)
                FitPageButton.ToolTip = LocalizationService.Get("Editor.FitPageTooltip");
            if (FitPageLabel != null)
                FitPageLabel.Text = LocalizationService.Get("Editor.FitPageTooltip");
            if (RotatePageButton != null)
                RotatePageButton.ToolTip = LocalizationService.Get("Editor.RotateTooltip");
            if (ImmersiveModeButton != null)
                ImmersiveModeButton.ToolTip = LocalizationService.Get("Editor.ImmersiveTooltip");
            if (PagesTabItem != null)
                PagesTabItem.Header = LocalizationService.Get("Editor.PagesTab");
            if (OutlineTabItem != null)
                OutlineTabItem.Header = LocalizationService.Get("Editor.OutlineTab");
            if (BookmarksTabItem != null)
                BookmarksTabItem.Header = LocalizationService.Get("Editor.BookmarksTab");
            ApplyLocalizedBookmarkLabel();
            ApplyLocalizedSidebarLabels();
            ApplyLocalizedSearchStatus();
            RefreshTextBoxToolbarLocalization();
            RefreshTextResizeHandleTooltips();
            RefreshLocalizedDocumentSidebar();
            RefreshStickyNoteEditorLocalization();
            UpdatePresetSlotVisuals();

            CloseToolPopups();
            CreateToolPopups();
            RefreshPageDeleteButtons();
        }

        private void RefreshTextBoxToolbarLocalization()
        {
            if (_textDeleteButton != null)
                _textDeleteButton.ToolTip = LocalizationService.Get("Editor.DeleteTooltip");
            if (_textDecreaseFontButton != null)
                _textDecreaseFontButton.ToolTip = LocalizationService.Get("Editor.SmallerText");
            if (_textIncreaseFontButton != null)
                _textIncreaseFontButton.ToolTip = LocalizationService.Get("Editor.BiggerText");
            if (_textBoldButton != null)
                _textBoldButton.ToolTip = LocalizationService.Get("Editor.BoldTooltip");
            if (_textItalicButton != null)
                _textItalicButton.ToolTip = LocalizationService.Get("Editor.ItalicTooltip");
            if (_textFontFamilyCombo != null)
                _textFontFamilyCombo.ToolTip = LocalizationService.Get("Editor.FontFamilyTooltip");
            if (_textAlignmentCombo != null)
                _textAlignmentCombo.ToolTip = LocalizationService.Get("Editor.AlignmentTooltip");
            if (_textRecentLabel != null)
                _textRecentLabel.Text = LocalizationService.Get("Editor.Recent");
        }

        private void RefreshTextResizeHandleTooltips()
        {
            foreach (var page in _pageControls)
            {
                if (page?.TextOverlay == null)
                    continue;

                foreach (var handle in FindVisualChildren<Border>(page.TextOverlay))
                {
                    if (handle.Tag is TextResizeHandle)
                        handle.ToolTip = LocalizationService.Get("Editor.ResizeTextBox");
                }
            }
        }

        private void RefreshLocalizedDocumentSidebar()
        {
            if (ThumbnailListBox != null)
            {
                foreach (var item in ThumbnailListBox.Items.OfType<ListBoxItem>())
                {
                    if (item.Tag is not int pageIndex)
                        continue;

                    if (item.Content is StackPanel panel)
                    {
                        var label = panel.Children.OfType<TextBlock>().FirstOrDefault();
                        if (label != null)
                            label.Text = LocalizationService.Format("Editor.PageNumber", pageIndex + 1);
                    }

                    if (item.ContextMenu == null)
                        item.ContextMenu = BuildThumbnailContextMenu(pageIndex);
                    else
                        RefreshThumbnailContextMenu(item.ContextMenu);
                }
            }

            if (!string.IsNullOrWhiteSpace(_currentPdfPath))
            {
                RefreshBookmarks();
                if (_pdfService != null && OutlineTreeView != null)
                    _ = RefreshOutlineAsync(CancellationToken.None);
            }
        }

        private void RefreshStickyNoteEditorLocalization()
        {
            if (_stickyNoteSaveButton != null)
                _stickyNoteSaveButton.Content = LocalizationService.Get("Common.Save");
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null)
                yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;

                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private void InstallLocalizationHooks()
        {
            if (_i18nHooksInstalled)
                return;

            _i18nHooksInstalled = true;

            if (PdfSearchStatusTextBlock != null)
            {
                var textDescriptor = DependencyPropertyDescriptor.FromProperty(
                    TextBlock.TextProperty, typeof(TextBlock));
                textDescriptor?.AddValueChanged(PdfSearchStatusTextBlock,
                    (_, __) => ApplyLocalizedSearchStatus());
            }

            if (BookmarkToggleButton != null)
            {
                var contentDescriptor = DependencyPropertyDescriptor.FromProperty(
                    ContentControl.ContentProperty, typeof(ContentControl));
                contentDescriptor?.AddValueChanged(BookmarkToggleButton,
                    (_, __) => ApplyLocalizedBookmarkLabel());
            }

            if (SidebarCollapseButton != null)
            {
                var contentDescriptor = DependencyPropertyDescriptor.FromProperty(
                    ContentControl.ContentProperty, typeof(ContentControl));
                contentDescriptor?.AddValueChanged(SidebarCollapseButton,
                    (_, __) => ApplyLocalizedSidebarLabels());
            }
        }

        private void ApplyLocalizedBookmarkLabel()
        {
            if (BookmarkToggleButton == null)
                return;

            var currentContent = BookmarkToggleButton.Content as string;
            var marker = currentContent?.StartsWith("★", StringComparison.Ordinal) == true ? "★" : "☆";
            var localizedContent = $"{marker}  {LocalizationService.Get("Editor.BookmarkCurrentPage")}";
            if (!string.Equals(currentContent, localizedContent, StringComparison.Ordinal))
                BookmarkToggleButton.Content = localizedContent;
        }

        private void ApplyLocalizedSidebarLabels()
        {
            if (SidebarCollapseButton == null)
                return;

            var currentContent = SidebarCollapseButton.Content as string;
            var marker = currentContent?.StartsWith("›", StringComparison.Ordinal) == true ? "›" : "‹";
            var localizedContent = $"{marker}  {LocalizationService.Get("Editor.SidebarCollapse")}";
            if (!string.Equals(currentContent, localizedContent, StringComparison.Ordinal))
                SidebarCollapseButton.Content = localizedContent;
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

        private string GetLocalizedToolName(ToolType tool)
        {
            return tool switch
            {
                ToolType.Pen => LocalizationService.Get("Editor.ModePen"),
                ToolType.Highlighter => LocalizationService.Get("Editor.ModeHighlighter"),
                ToolType.HiddenInk => LocalizationService.Get("Editor.ModeHiddenInk"),
                ToolType.Eraser => LocalizationService.Get("Editor.ModeEraser"),
                ToolType.Shape => LocalizationService.Get("Editor.ModeShape"),
                ToolType.Laser => LocalizationService.Get("Editor.ModeLaser"),
                ToolType.Text => LocalizationService.Get("Editor.ModeText"),
                ToolType.Select => LocalizationService.Get("Editor.ModeSelect"),
                _ => LocalizationService.Get("Editor.ModeSelect")
            };
        }
    }
}
