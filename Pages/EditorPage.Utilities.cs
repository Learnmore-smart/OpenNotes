using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
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
            {
                string hiddenInkLabel = LocalizationService.Get("Editor.HiddenInkTooltip");
                HiddenInkToolButton.ToolTip = hiddenInkLabel;
                AutomationProperties.SetName(HiddenInkToolButton, hiddenInkLabel);
                AutomationProperties.SetHelpText(HiddenInkToolButton, hiddenInkLabel);
            }
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
            if (PageNumberTextBox != null)
                PageNumberTextBox.ToolTip = LocalizationService.Get("Editor.PageJumpTooltip");
            if (RotatePageButton != null)
                RotatePageButton.ToolTip = LocalizationService.Get("Editor.RotateTooltip");
            if (ImmersiveModeButton != null)
                ImmersiveModeButton.ToolTip = LocalizationService.Get("Editor.ImmersiveTooltip");
            ApplyLocalizedBookmarkLabel();
            ApplyLocalizedSidebarLabels();
            ApplyLocalizedSearchStatus();
            RefreshTextBoxToolbarLocalization();
            RefreshTextResizeHandleTooltips();
            RefreshLocalizedDocumentSidebar();
            RefreshStickyNoteEditorLocalization();
            ApplyToolbarAccessibilityMetadata();

            CloseToolPopups();
            DetachToolPopupHandlers();
            CreateToolPopups();
            FixToolPopupZOrder();
            RefreshPageDeleteButtons();
        }

        /// <summary>
        /// Keeps the static toolbar UIA contract in one place. XAML carries
        /// the stable ids for smoke discovery; this refresh reapplies the
        /// localized name/help/tooltip after every language change.
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
            SetToolbarMetadata(SidebarPagesButton, "Editor.Sidebar.Pages", LocalizationService.Get("Editor.PagesTab"));
            SetToolbarMetadata(SidebarOutlineButton, "Editor.Sidebar.Outline", LocalizationService.Get("Editor.OutlineTab"));
            SetToolbarMetadata(SidebarBookmarksButton, "Editor.Sidebar.Bookmarks", LocalizationService.Get("Editor.BookmarksTab"));
            SetToolbarMetadata(SidebarCollapseButton, "Editor.Sidebar.Collapse", LocalizationService.Get("Editor.SidebarCollapse"));
            SetToolbarMetadata(SidebarResizeThumb, "Editor.Sidebar.Resize", LocalizationService.Get("Editor.SidebarResize"));
            SetToolbarMetadata(BookmarkToggleButton, "Editor.Sidebar.BookmarkToggle",
                LocalizationService.Get("Editor.BookmarkCurrentPage"));
            SetToolbarMetadata(ToolbarItemsScrollViewer, "Editor.ToolbarOverflow",
                LocalizationService.Get("Editor.ToolbarScroll"));
            SetToolbarMetadata(ZoomOutButton, "Editor.ZoomOutButton", LocalizationService.Get("Editor.ZoomOutTooltip"));
            SetToolbarMetadata(ZoomInButton, "Editor.ZoomInButton", LocalizationService.Get("Editor.ZoomInTooltip"));
            SetToolbarMetadata(ZoomLabel, "Editor.ZoomLabel", LocalizationService.Get("Editor.ZoomEditTooltip"));
            SetToolbarMetadata(ZoomTextBox, "Editor.ZoomInput", LocalizationService.Get("Editor.ZoomEditTooltip"));
            SetToolbarMetadata(RotatePageButton, "Editor.RotatePageButton", LocalizationService.Get("Editor.RotateTooltip"));
            SetToolbarMetadata(ImmersiveModeButton, "Editor.ImmersiveModeButton", LocalizationService.Get("Editor.ImmersiveTooltip"));
            SetToolbarMetadata(_textDeleteButton, "Editor.Text.Delete", LocalizationService.Get("Editor.DeleteTooltip"));
            SetToolbarMetadata(_textDecreaseFontButton, "Editor.Text.Smaller", LocalizationService.Get("Editor.SmallerText"));
            SetToolbarMetadata(_textIncreaseFontButton, "Editor.Text.Bigger", LocalizationService.Get("Editor.BiggerText"));
            SetToolbarMetadata(_textBoldButton, "Editor.Text.Bold", LocalizationService.Get("Editor.BoldTooltip"));
            SetToolbarMetadata(_textItalicButton, "Editor.Text.Italic", LocalizationService.Get("Editor.ItalicTooltip"));
            SetToolbarMetadata(_textFontFamilyCombo, "Editor.Text.FontFamily", LocalizationService.Get("Editor.FontFamilyTooltip"));
            SetToolbarMetadata(_textAlignmentCombo, "Editor.Text.Alignment", LocalizationService.Get("Editor.AlignmentTooltip"));
            SetToolbarMetadata(_textColorButton, "Editor.Text.Color", LocalizationService.Get("Editor.TextColorTooltip"));
            // These two controls encode live state, so their metadata must be
            // the final writes in every localization refresh.  A static Add /
            // Collapse assignment here would overwrite Remove / Expand.
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
                SetAutomationId(BookmarkToggleButton, "Editor.Sidebar.BookmarkToggle");
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
            RefreshTextAlignmentOptions();
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

            foreach (var page in _sidebarPageItems)
                page.PageLabel = LocalizationService.Format("Editor.PageNumber", page.PageIndex + 1);
            RefreshRealizedSidebarContextMenus();

            if (!string.IsNullOrWhiteSpace(_currentPdfPath))
            {
                RefreshBookmarks();
                if (_pdfService != null && OutlineTreeView != null)
                    _ = RefreshOutlineAsync(CancellationToken.None, _loadSessionId, _currentPdfPath);
            }
            SetSidebarTab(_sidebarTab);
        }

        private void RefreshRealizedSidebarContextMenus()
        {
            if (ThumbnailListBox != null)
            {
                for (int index = 0; index < ThumbnailListBox.Items.Count; index++)
                {
                    if (ThumbnailListBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item &&
                        item.ContextMenu != null)
                        RefreshThumbnailContextMenu(item.ContextMenu);
                }
            }

            if (BookmarksListBox != null)
            {
                for (int index = 0; index < BookmarksListBox.Items.Count; index++)
                {
                    if (BookmarksListBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item &&
                        item.ContextMenu != null)
                        RefreshBookmarkContextMenu(item.ContextMenu);
                }
            }
        }

        private void RefreshStickyNoteEditorLocalization()
        {
            ApplyStickyNoteButtonMetadata(_stickyNoteSaveButton, LocalizationService.Get("Common.Save"), "Sticky.Save");
            ApplyStickyNoteButtonMetadata(_stickyNoteCancelButton, LocalizationService.Get("Common.Cancel"), "Sticky.Cancel");
            ApplyStickyNoteButtonMetadata(_stickyNoteDeleteButton, LocalizationService.Get("Editor.DeleteTooltip"), "Sticky.Delete");
            foreach (var page in _pageControls)
                page.RefreshStickyNoteContextMenuLocalization();
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
            var localizedLabel = marker == "★"
                ? LocalizationService.Get("Editor.UnbookmarkCurrentPage")
                : LocalizationService.Get("Editor.BookmarkCurrentPage");
            var localizedContent = $"{marker}  {localizedLabel}";
            if (!string.Equals(currentContent, localizedContent, StringComparison.Ordinal))
                BookmarkToggleButton.Content = localizedContent;
            BookmarkToggleButton.IsChecked = marker == "★";
            ApplyStateAwareSidebarMetadata();
        }

        private void ApplyLocalizedSidebarLabels()
        {
            string pages = LocalizationService.Get("Editor.PagesTab");
            string outline = LocalizationService.Get("Editor.OutlineTab");
            string bookmarks = LocalizationService.Get("Editor.BookmarksTab");
            if (SidebarPagesLabel != null)
                SidebarPagesLabel.Text = pages;
            if (SidebarOutlineLabel != null)
                SidebarOutlineLabel.Text = outline;
            if (SidebarBookmarksLabel != null)
                SidebarBookmarksLabel.Text = bookmarks;
            if (SidebarTitleLabel != null)
                SidebarTitleLabel.Text = pages;
            if (PagesEmptyState != null)
                PagesEmptyState.Text = LocalizationService.Get("Editor.NoDocumentLoaded");
            if (OutlineEmptyState != null)
                OutlineEmptyState.Text = LocalizationService.Get("Editor.NoDocumentLoaded");
            if (BookmarksEmptyState != null)
                BookmarksEmptyState.Text = LocalizationService.Get("Editor.SidebarNoBookmarks");
            if (SidebarResizeThumb != null)
                SetToolbarMetadata(SidebarResizeThumb, "Editor.Sidebar.Resize", LocalizationService.Get("Editor.SidebarResize"));
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
