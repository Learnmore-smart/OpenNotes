using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Caelum.Models;
using Caelum.Pages;
using Caelum.Services;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
[NonParallelizable]
public sealed class EditorNavigationSourceTests
{
    [Test]
    public void PageJumpUsesACompactKeyboardFirstEditableField()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var utilities = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.Utilities.cs"));
        int pageJumpStart = xaml.IndexOf("x:Name=\"PageJumpGroup\"", StringComparison.Ordinal);
        int pageJumpEnd = xaml.IndexOf("<!-- Separator -->", pageJumpStart, StringComparison.Ordinal);
        string pageJumpBlock = pageJumpStart >= 0 && pageJumpEnd > pageJumpStart
            ? xaml.Substring(pageJumpStart, pageJumpEnd - pageJumpStart)
            : string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Name=\"PageNumberTextBox\""));
            Assert.That(xaml, Does.Contain("AutomationProperties.AutomationId=\"Editor.PageJump\""));
            Assert.That(xaml, Does.Contain("MinHeight=\"32\""));
            Assert.That(xaml, Does.Contain("GotKeyboardFocus=\"PageNumberTextBox_GotKeyboardFocus\""));
            Assert.That(xaml, Does.Contain("KeyDown=\"PageNumberTextBox_KeyDown\""));
            Assert.That(xaml, Does.Contain("LostFocus=\"PageNumberTextBox_LostFocus\""));
            Assert.That(xaml, Does.Not.Contain("PageJumpBorder_MouseLeftButtonDown"));
            Assert.That(pageJumpBlock, Does.Not.Contain("Cursor=\"Hand\""));
            Assert.That(source, Does.Contain("PageJumpInvalid"));
            Assert.That(source, Does.Contain("PageJumpOutOfRange"));
            Assert.That(source, Does.Contain("Math.Max(1, Math.Min(_pageControls.Count"));
            Assert.That(utilities, Does.Contain("SetToolbarMetadata(PageNumberTextBox, \"Editor.PageJump\""));
            Assert.That(utilities, Does.Not.Contain("PageJumpBorder"));
        });
    }

    [Test]
    public void SidebarUsesCustomCommandsAndKeepsDocumentNavigationControls()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        var utilities = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.Utilities.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Not.Contain("<TabControl"));
            Assert.That(xaml, Does.Not.Contain("<TabItem"));
            Assert.That(xaml, Does.Contain("x:Name=\"SidebarPagesButton\""));
            Assert.That(xaml, Does.Contain("x:Name=\"SidebarOutlineButton\""));
            Assert.That(xaml, Does.Contain("x:Name=\"SidebarBookmarksButton\""));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.Pages"));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.Outline"));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.Bookmarks"));
            Assert.That(xaml, Does.Contain("MinWidth=\"154\""));
            Assert.That(xaml, Does.Contain("MaxWidth=\"320\""));
            Assert.That(xaml, Does.Contain("SidebarResizeThumb"));
            Assert.That(xaml, Does.Contain("ThumbnailListBox"));
            Assert.That(xaml, Does.Contain("OutlineTreeView"));
            Assert.That(xaml, Does.Contain("BookmarksListBox"));
            Assert.That(source, Does.Contain("SetSidebarTab"));
            Assert.That(source, Does.Contain("SidebarResizeThumb_DragDelta"));
            Assert.That(source, Does.Contain("Editor.Sidebar.Page."));
            Assert.That(source, Does.Contain("Editor.Sidebar.Bookmark."));
            Assert.That(source, Does.Contain("Editor.Sidebar.Outline."));
            Assert.That(utilities, Does.Contain("SidebarPagesLabel"));
            Assert.That(utilities, Does.Contain("SidebarOutlineLabel"));
            Assert.That(utilities, Does.Contain("SidebarBookmarksLabel"));
            Assert.That(utilities, Does.Not.Contain("PagesTabItem"));
            Assert.That(utilities, Does.Not.Contain("OutlineTabItem"));
            Assert.That(utilities, Does.Not.Contain("BookmarksTabItem"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void PageJumpAndSidebarButtonsExposeRealAutomationPeers()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddRequiredResource(application, "ToolbarFocusVisualStyle", new Style(typeof(Control)));
        AddRequiredResource(application, "SleekScrollViewer", new Style(typeof(ScrollViewer)));
        AddRequiredResource(application, "CompactComboBox", new Style(typeof(ComboBox)));
        AddThemeResources(application);

        var previousLanguage = LocalizationService.CurrentLanguage;
        try
        {
            LocalizationService.ApplyLanguage(AppLanguage.French);
            var editor = new EditorPage();
            var pageJump = GetNamed<TextBox>(editor, "PageNumberTextBox");

            Assert.That(AutomationProperties.GetAutomationId(pageJump), Is.EqualTo("Editor.PageJump"));
            Assert.That(pageJump.MinHeight, Is.GreaterThanOrEqualTo(32));
            Assert.That(KeyboardNavigation.GetIsTabStop(pageJump), Is.True);
            var pagePeer = UIElementAutomationPeer.CreatePeerForElement(pageJump);
            Assert.That(pagePeer, Is.Not.Null);
            Assert.That(pagePeer!.GetPattern(PatternInterface.Value), Is.Not.Null);
            Assert.That(pagePeer.GetName(), Does.Contain("page").IgnoreCase.Or.Contain("page").IgnoreCase);
            Assert.That(pagePeer.GetHelpText(), Is.Not.Null.And.Not.Empty);

            var pages = FindByAutomationId<Button>(editor, "Editor.Sidebar.Pages");
            var outline = FindByAutomationId<Button>(editor, "Editor.Sidebar.Outline");
            var bookmarks = FindByAutomationId<Button>(editor, "Editor.Sidebar.Bookmarks");
            foreach (var button in new[] { pages, outline, bookmarks })
            {
                Assert.That(button.MinHeight, Is.GreaterThanOrEqualTo(32));
                Assert.That(KeyboardNavigation.GetIsTabStop(button), Is.True);
                Assert.That(UIElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke), Is.Not.Null);
            }

            var thumbnailList = GetNamed<ListBox>(editor, "ThumbnailListBox");
            var outlineTree = GetNamed<TreeView>(editor, "OutlineTreeView");
            var bookmarksList = GetNamed<ListBox>(editor, "BookmarksListBox");
            Assert.That(UIElementAutomationPeer.CreatePeerForElement(thumbnailList)?.GetPattern(PatternInterface.Selection), Is.Not.Null);
            Assert.That(UIElementAutomationPeer.CreatePeerForElement(outlineTree)?.GetPattern(PatternInterface.Selection), Is.Not.Null);
            Assert.That(UIElementAutomationPeer.CreatePeerForElement(bookmarksList)?.GetPattern(PatternInterface.Selection), Is.Not.Null);

            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(outline)!.GetPattern(PatternInterface.Invoke)!).Invoke();
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            var outlineContent = GetNamed<FrameworkElement>(editor, "OutlineSidebarContent");
            Assert.That(outlineContent.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(GetNamed<FrameworkElement>(editor, "PagesSidebarContent").Visibility, Is.EqualTo(Visibility.Collapsed));

            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(bookmarks)!.GetPattern(PatternInterface.Invoke)!).Invoke();
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            Assert.That(GetNamed<FrameworkElement>(editor, "BookmarksSidebarContent").Visibility, Is.EqualTo(Visibility.Visible));

            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(pages)!.GetPattern(PatternInterface.Invoke)!).Invoke();
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            Assert.That(GetNamed<FrameworkElement>(editor, "PagesSidebarContent").Visibility, Is.EqualTo(Visibility.Visible));

            var sidebar = GetNamed<Border>(editor, "DocumentSidebar");
            var collapse = FindByAutomationId<Button>(editor, "Editor.Sidebar.Collapse");
            var resize = FindByAutomationId<Thumb>(editor, "Editor.Sidebar.Resize");
            Assert.That(sidebar.MinWidth, Is.EqualTo(154).Within(0.1));
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(collapse)!.GetPattern(PatternInterface.Invoke)!).Invoke();
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            Assert.That(sidebar.Width, Is.EqualTo(38).Within(0.1));
            Assert.That(sidebar.MinWidth, Is.EqualTo(38).Within(0.1));
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(collapse)!.GetPattern(PatternInterface.Invoke)!).Invoke();
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            Assert.That(sidebar.MinWidth, Is.EqualTo(154).Within(0.1));

            resize.RaiseEvent(new DragDeltaEventArgs(200, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            Assert.That(sidebar.Width, Is.EqualTo(320).Within(0.1));
            resize.RaiseEvent(new DragDeltaEventArgs(-400, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            Assert.That(sidebar.Width, Is.EqualTo(154).Within(0.1));
        }
        finally
        {
            LocalizationService.ApplyLanguage(previousLanguage);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void PageJumpInitialStateIsOneBasedAndNotEditing()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddRequiredResource(application, "ToolbarFocusVisualStyle", new Style(typeof(Control)));
        AddRequiredResource(application, "SleekScrollViewer", new Style(typeof(ScrollViewer)));
        AddRequiredResource(application, "CompactComboBox", new Style(typeof(ComboBox)));
        AddThemeResources(application);

        var editor = new EditorPage();
        var pageJump = GetNamed<TextBox>(editor, "PageNumberTextBox");
        var editing = (bool)typeof(EditorPage)
            .GetField("_isPageJumpEditing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(editor)!;

        Assert.That(pageJump.Text, Is.EqualTo("1"), "The first editable value must use one-based page numbering.");
        Assert.That(editing, Is.False, "Initial XAML binding must not open a page-jump edit session.");

        SeedPageNavigationState(editor, 3);
        var value = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(pageJump)!
            .GetPattern(PatternInterface.Value)!;
        Assert.That(value.Value, Is.EqualTo("1"), "A newly seeded three-page document must expose current page 1 through UIA.");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LocalizationRefreshPreservesCollapsedAndBookmarkedSidebarMetadata()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddRequiredResource(application, "ToolbarFocusVisualStyle", new Style(typeof(Control)));
        AddRequiredResource(application, "SleekScrollViewer", new Style(typeof(ScrollViewer)));
        AddRequiredResource(application, "CompactComboBox", new Style(typeof(ComboBox)));
        AddThemeResources(application);

        var previousLanguage = LocalizationService.CurrentLanguage;
        try
        {
            LocalizationService.ApplyLanguage(AppLanguage.English);
            var editor = new EditorPage();
            var collapse = FindByAutomationId<Button>(editor, "Editor.Sidebar.Collapse");
            var bookmark = FindByAutomationId<ToggleButton>(editor, "Editor.Sidebar.BookmarkToggle");

            InvokePrivate(editor, "SetSidebarCollapsed", true);
            bookmark.IsChecked = true;
            InvokePrivate(editor, "ApplyLocalizedBookmarkLabel");

            Assert.That(bookmark.Content, Is.TypeOf<StackPanel>());

            foreach (var language in new[] { AppLanguage.English, AppLanguage.Chinese, AppLanguage.French })
            {
                LocalizationService.ApplyLanguage(language);
                editor.ApplyLocalization();

                string expand = LocalizationService.Get("Editor.SidebarExpand");
                string remove = LocalizationService.Get("Editor.UnbookmarkCurrentPage");
                Assert.Multiple(() =>
                {
                    Assert.That(AutomationProperties.GetName(collapse), Is.EqualTo(expand));
                    Assert.That(AutomationProperties.GetHelpText(collapse), Is.EqualTo(expand));
                    Assert.That(ToolTipService.GetToolTip(collapse), Is.EqualTo(expand));
                    Assert.That(AutomationProperties.GetName(bookmark), Is.EqualTo(remove));
                    Assert.That(AutomationProperties.GetHelpText(bookmark), Is.EqualTo(remove));
                    Assert.That(ToolTipService.GetToolTip(bookmark), Is.EqualTo(remove));
                });
            }
        }
        finally
        {
            LocalizationService.ApplyLanguage(previousLanguage);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void RecycledSidebarMenusRebindToCurrentPageAndBookmarkModels()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddRequiredResource(application, "ToolbarFocusVisualStyle", new Style(typeof(Control)));
        AddRequiredResource(application, "SleekScrollViewer", new Style(typeof(ScrollViewer)));
        AddRequiredResource(application, "CompactComboBox", new Style(typeof(ComboBox)));
        AddThemeResources(application);

        var editor = new EditorPage();
        var item = new ListBoxItem();
        var pageOne = new EditorPage.SidebarPageItem(0, "Page 1");
        var pageTwo = new EditorPage.SidebarPageItem(1, "Page 2");
        var bookmarkA = new EditorPage.SidebarBookmarkItem(2, "Bookmark A");
        var bookmarkB = new EditorPage.SidebarBookmarkItem(3, "Bookmark B");

        item.DataContext = pageOne;
        InvokePrivate(editor, "SidebarListBoxItem_Loaded", item,
            new RoutedEventArgs(FrameworkElement.LoadedEvent, item));
        var oldPageMenu = OpenRecycledSidebarContextMenu(editor, item, "ThumbnailListBox_ContextMenuOpening");
        var oldPageCommand = oldPageMenu.Items.OfType<MenuItem>().ElementAt(1);

        item.DataContext = pageTwo;
        InvokePrivate(editor, "SidebarListBoxItem_Loaded", item,
            new RoutedEventArgs(FrameworkElement.LoadedEvent, item));
        var currentPageMenu = OpenRecycledSidebarContextMenu(editor, item, "ThumbnailListBox_ContextMenuOpening");
        var currentPageCommand = currentPageMenu.Items.OfType<MenuItem>().ElementAt(1);

        Assert.Multiple(() =>
        {
            Assert.That(oldPageMenu, Is.Not.SameAs(currentPageMenu));
            Assert.That(oldPageMenu.Tag, Is.Null, "The recycled page menu must release its old model binding.");
            Assert.That(oldPageMenu.Items.Count, Is.EqualTo(0), "The recycled page menu must not retain old MenuItems/handlers.");
            Assert.That(currentPageMenu.Tag, Is.SameAs(pageTwo));
            Assert.That(currentPageCommand.CommandParameter, Is.SameAs(pageTwo));
            Assert.That(ResolvesCurrentSidebarModel(editor, currentPageCommand, typeof(EditorPage.SidebarPageItem), pageTwo), Is.True);
        });
        oldPageCommand.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, oldPageCommand));
        Assert.That(oldPageMenu.Items.Count, Is.EqualTo(0), "Invoking a retained old page command must be a no-op.");
        currentPageCommand.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, currentPageCommand));

        item.DataContext = bookmarkA;
        InvokePrivate(editor, "SidebarListBoxItem_Loaded", item,
            new RoutedEventArgs(FrameworkElement.LoadedEvent, item));
        var oldBookmarkMenu = OpenRecycledSidebarContextMenu(editor, item, "BookmarksListBox_ContextMenuOpening");
        var oldBookmarkCommand = oldBookmarkMenu.Items.OfType<MenuItem>().Single();

        item.DataContext = bookmarkB;
        InvokePrivate(editor, "SidebarListBoxItem_Loaded", item,
            new RoutedEventArgs(FrameworkElement.LoadedEvent, item));
        var currentBookmarkMenu = OpenRecycledSidebarContextMenu(editor, item, "BookmarksListBox_ContextMenuOpening");
        var currentBookmarkCommand = currentBookmarkMenu.Items.OfType<MenuItem>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(oldBookmarkMenu, Is.Not.SameAs(currentBookmarkMenu));
            Assert.That(oldBookmarkMenu.Tag, Is.Null, "The recycled bookmark menu must release its old model binding.");
            Assert.That(oldBookmarkMenu.Items.Count, Is.EqualTo(0), "The recycled bookmark menu must not retain old handlers.");
            Assert.That(currentBookmarkMenu.Tag, Is.SameAs(bookmarkB));
            Assert.That(currentBookmarkCommand.CommandParameter, Is.SameAs(bookmarkB));
            Assert.That(ResolvesCurrentSidebarModel(editor, currentBookmarkCommand, typeof(EditorPage.SidebarBookmarkItem), bookmarkB), Is.True);
        });
        oldBookmarkCommand.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, oldBookmarkCommand));
        Assert.That(oldBookmarkMenu.Items.Count, Is.EqualTo(0), "Invoking a retained old bookmark command must be a no-op.");
        currentBookmarkCommand.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, currentBookmarkCommand));

        InvokePrivate(editor, "SidebarListBoxItem_Unloaded", item,
            new RoutedEventArgs(FrameworkElement.UnloadedEvent, item));
        Assert.That(currentBookmarkMenu.Items.Count, Is.EqualTo(0), "Unloading must detach the current bookmark handlers too.");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void PageJumpCanReenterEditingAfterEnterEscapeInvalidAndOutOfRange()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddRequiredResource(application, "ToolbarFocusVisualStyle", new Style(typeof(Control)));
        AddRequiredResource(application, "SleekScrollViewer", new Style(typeof(ScrollViewer)));
        AddRequiredResource(application, "CompactComboBox", new Style(typeof(ComboBox)));
        AddThemeResources(application);

        var previousLanguage = LocalizationService.CurrentLanguage;
        try
        {
            LocalizationService.ApplyLanguage(AppLanguage.English);
            var editor = new EditorPage();
            SeedPageNavigationState(editor, 3);
            var pageJump = GetNamed<TextBox>(editor, "PageNumberTextBox");
            var invalidKey = CreateKeyEventArgs(pageJump, Key.Enter);
            var escapeKey = CreateKeyEventArgs(pageJump, Key.Escape);

            InvokePrivate(editor, "PageNumberTextBox_GotKeyboardFocus", pageJump, null);
            pageJump.Text = "2";
            InvokePrivate(editor, "PageNumberTextBox_KeyDown", pageJump, invalidKey);
            Assert.That(pageJump.Text, Is.EqualTo("1"), "Enter should commit and normalize the field while focus remains available.");

            pageJump.Text = "3";
            InvokePrivate(editor, "PageNumberTextBox_LostFocus", pageJump, null);
            Assert.That(pageJump.Text, Is.EqualTo("1"), "A changed value after Enter must still commit on Tab/LostFocus.");

            InvokePrivate(editor, "PageNumberTextBox_GotKeyboardFocus", pageJump, null);
            pageJump.Text = "2";
            InvokePrivate(editor, "PageNumberTextBox_KeyDown", pageJump, escapeKey);
            Assert.That(pageJump.Text, Is.EqualTo("1"), "Escape must restore the opening value.");

            pageJump.Text = "not-a-page";
            InvokePrivate(editor, "PageNumberTextBox_LostFocus", pageJump, null);
            Assert.That(AutomationProperties.GetItemStatus(pageJump), Does.Contain("whole page"));

            InvokePrivate(editor, "PageNumberTextBox_GotKeyboardFocus", pageJump, null);
            pageJump.Text = "999";
            InvokePrivate(editor, "PageNumberTextBox_KeyDown", pageJump, invalidKey);
            Assert.That(AutomationProperties.GetItemStatus(pageJump), Does.Contain("between"));

            pageJump.Text = "2";
            InvokePrivate(editor, "PageNumberTextBox_LostFocus", pageJump, null);
            Assert.That(AutomationProperties.GetItemStatus(pageJump), Is.Empty, "A valid second edit must clear the previous accessible error.");
        }
        finally
        {
            LocalizationService.ApplyLanguage(previousLanguage);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void SidebarUsesLiveThemeExpressionsAndReadablePageJumpSelectionText()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddRequiredResource(application, "ToolbarFocusVisualStyle", new Style(typeof(Control)));
        AddRequiredResource(application, "SleekScrollViewer", new Style(typeof(ScrollViewer)));
        AddRequiredResource(application, "CompactComboBox", new Style(typeof(ComboBox)));
        AddThemeResources(application);

        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("SelectionBrush=\"{DynamicResource ThemeSelectionBrush}\""));
            Assert.That(xaml, Does.Contain("SelectionTextBrush=\"{DynamicResource ThemeSelectionForegroundBrush}\""));
            Assert.That(xaml, Does.Contain("Foreground=\"{DynamicResource ThemeForegroundBrush}\""));
            Assert.That(xaml, Does.Contain("VirtualizingPanel.VirtualizationMode=\"Recycling\""));
            Assert.That(source, Does.Not.Contain("button.Background = selected"));
            Assert.That(source, Does.Contain("SetResourceReference"));
        });

        var previousLanguage = LocalizationService.CurrentLanguage;
        try
        {
            LocalizationService.ApplyLanguage(AppLanguage.English);
            var editor = new EditorPage();
            var pages = FindByAutomationId<Button>(editor, "Editor.Sidebar.Pages");
            Assert.That(pages.ReadLocalValue(Button.BackgroundProperty), Is.EqualTo(DependencyProperty.UnsetValue));
            Assert.That(pages.ReadLocalValue(Button.BorderBrushProperty), Is.EqualTo(DependencyProperty.UnsetValue));

            var pageJump = GetNamed<TextBox>(editor, "PageNumberTextBox");
            Assert.That(pageJump.ReadLocalValue(TextBox.SelectionBrushProperty), Is.Not.EqualTo(DependencyProperty.UnsetValue));
            Assert.That(pageJump.ReadLocalValue(TextBox.SelectionTextBrushProperty), Is.Not.EqualTo(DependencyProperty.UnsetValue));
        }
        finally
        {
            LocalizationService.ApplyLanguage(previousLanguage);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public async Task FallbackOutlineRowsSelectAndInvokeTheirPage()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddRequiredResource(application, "ToolbarFocusVisualStyle", new Style(typeof(Control)));
        AddRequiredResource(application, "SleekScrollViewer", new Style(typeof(ScrollViewer)));
        AddRequiredResource(application, "CompactComboBox", new Style(typeof(ComboBox)));
        AddThemeResources(application);

        var editor = new EditorPage();
        SeedPageNavigationState(editor, 3);

            // Give the scroll viewer a real document-sized surface so the
            // programmatic page jump is observable in an STA test.
            var pages = GetNamed<StackPanel>(editor, "PagesContainer");
            for (int index = 0; index < 3; index++)
                pages.Children.Add(new Border { Width = 400, Height = 500, Margin = new Thickness(0, 0, 0, 28) });
            editor.Measure(new Size(720, 540));
            editor.Arrange(new Rect(0, 0, 720, 540));
            editor.UpdateLayout();

            await (Task)InvokePrivate(editor, "RefreshOutlineAsync", CancellationToken.None, 0, null);
            var outline = GetNamed<TreeView>(editor, "OutlineTreeView");
            outline.ApplyTemplate();
            outline.Measure(new Size(300, 500));
            outline.Arrange(new Rect(0, 0, 300, 500));
            outline.UpdateLayout();
            Assert.That(outline.Items.Count, Is.EqualTo(3));
            Assert.That(outline.Items[1], Is.TypeOf<EditorPage.SidebarOutlineItem>());

            var second = (EditorPage.SidebarOutlineItem)outline.Items[1];
            var secondContainer = outline.ItemContainerGenerator.ContainerFromIndex(1) as TreeViewItem;
            Assert.That(secondContainer, Is.Not.Null);
            Assert.That(secondContainer!.GetType(), Is.EqualTo(typeof(SidebarOutlineTreeViewItem)),
                "Fallback outline rows must be custom containers so their UIA peer can expose InvokePattern.");
            var selectionPeer = UIElementAutomationPeer.CreatePeerForElement(secondContainer!);
            Assert.That(selectionPeer?.GetPattern(PatternInterface.SelectionItem), Is.AssignableTo<ISelectionItemProvider>());
            ((ISelectionItemProvider)selectionPeer!.GetPattern(PatternInterface.SelectionItem)!).Select();
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            Assert.That((int)InvokePrivate(editor, "GetCurrentPageIndex"), Is.EqualTo(1),
                "Fallback outline item 2 must invoke the same page jump as a real outline row.");

            InvokePrivate(editor, "JumpToPage", 0);
            var invokeProvider = selectionPeer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            Assert.That(invokeProvider, Is.Not.Null, "Fallback outline items must expose InvokePattern in addition to SelectionItemPattern.");
            invokeProvider!.Invoke();
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            Assert.That((int)InvokePrivate(editor, "GetCurrentPageIndex"), Is.EqualTo(1),
                "Invoking fallback outline item 2 must jump to page 2 just like selecting it.");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void SidebarResizeExposesRangeKeyboardAndNarrowCollapseContracts()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddRequiredResource(application, "ToolbarFocusVisualStyle", new Style(typeof(Control)));
        AddRequiredResource(application, "SleekScrollViewer", new Style(typeof(ScrollViewer)));
        AddRequiredResource(application, "CompactComboBox", new Style(typeof(ComboBox)));
        AddThemeResources(application);

        Window window = null;
        try
        {
            var editor = new EditorPage();
            DisablePenServiceForTest(editor);
            window = new Window { Content = editor, Width = 720, Height = 540, ShowInTaskbar = false };
            window.Show();
            window.UpdateLayout();
            var sidebar = GetNamed<Border>(editor, "DocumentSidebar");
            var resize = FindByAutomationId<Thumb>(editor, "Editor.Sidebar.Resize");
            var peer = UIElementAutomationPeer.CreatePeerForElement(resize);
            var range = (IRangeValueProvider)peer!.GetPattern(PatternInterface.RangeValue)!;
            Assert.That(range.Minimum, Is.EqualTo(154).Within(0.1));
            Assert.That(range.Maximum, Is.EqualTo(320).Within(0.1));
            range.SetValue(260);
            Assert.That(sidebar.Width, Is.EqualTo(260).Within(0.1));

            InvokePrivate(editor, "SidebarResizeThumb_KeyDown", resize, CreateKeyEventArgs(resize, Key.Right));
            Assert.That(sidebar.Width, Is.EqualTo(268).Within(0.1));
            InvokePrivate(editor, "SidebarResizeThumb_KeyDown", resize, CreateKeyEventArgs(resize, Key.Home));
            Assert.That(sidebar.Width, Is.EqualTo(154).Within(0.1));
            InvokePrivate(editor, "SidebarResizeThumb_KeyDown", resize, CreateKeyEventArgs(resize, Key.End));
            Assert.That(sidebar.Width, Is.EqualTo(320).Within(0.1));

            var collapse = FindByAutomationId<Button>(editor, "Editor.Sidebar.Collapse");
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(collapse)!
                .GetPattern(PatternInterface.Invoke)!).Invoke();
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            Assert.That(GetNamed<FrameworkElement>(editor, "SidebarNavBar").Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(resize.Visibility, Is.EqualTo(Visibility.Collapsed));

            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(collapse)!
                .GetPattern(PatternInterface.Invoke)!).Invoke();
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            Assert.That(GetNamed<FrameworkElement>(editor, "SidebarNavBar").Visibility, Is.EqualTo(Visibility.Visible));

            window.Width = 360;
            window.UpdateLayout();
            Assert.That(sidebar.Width, Is.EqualTo(38).Within(0.1), "Narrow windows should auto-collapse the overlay rail.");
        }
        finally
        {
            window?.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void SidebarAndPageJumpKeepReadableHighContrastTokens()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddRequiredResource(application, "ToolbarFocusVisualStyle", new Style(typeof(Control)));
        AddRequiredResource(application, "SleekScrollViewer", new Style(typeof(ScrollViewer)));
        AddRequiredResource(application, "CompactComboBox", new Style(typeof(ComboBox)));
        AddThemeResources(application);

        string previousTheme = ThemeService.CurrentTheme;
        Window window = null;
        try
        {
            var editor = new EditorPage();
            DisablePenServiceForTest(editor);
            window = new Window { Content = editor, Width = 720, Height = 540, ShowInTaskbar = false };
            window.Show();
            window.UpdateLayout();
            var pageJump = GetNamed<TextBox>(editor, "PageNumberTextBox");
            ThemeService.Apply("HighContrast");
            editor.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            var selection = (SolidColorBrush)application.Resources["ThemeSelectionBrush"];
            var selectionForeground = (SolidColorBrush)application.Resources["ThemeSelectionForegroundBrush"];
            Assert.That(selection.Color, Is.EqualTo(Color.FromRgb(255, 255, 0)));
            Assert.That(selectionForeground.Color, Is.EqualTo(Colors.Black));
            Assert.That(((SolidColorBrush)pageJump.SelectionBrush).Color, Is.EqualTo(selection.Color));
            Assert.That(((SolidColorBrush)pageJump.SelectionTextBrush).Color, Is.EqualTo(selectionForeground.Color));
        }
        finally
        {
            window?.Close();
            ThemeService.Apply(previousTheme);
        }
    }

    [Test]
    public void SidebarReviewContractsRequireDeferredVirtualizedItemsAndSessionGuards()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        string refresh = ExtractMethod(source, "private async Task RefreshDocumentSidebarAsync");
        string outline = ExtractMethod(source, "private async Task RefreshOutlineAsync");
        string bookmarks = ExtractMethod(source, "private void RefreshBookmarks");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("x:Key=\"SidebarPageItemTemplate\""));
            Assert.That(xaml, Does.Contain("x:Key=\"SidebarBookmarkItemTemplate\""));
            Assert.That(xaml, Does.Contain("x:Key=\"SidebarOutlineItemTemplate\""));
            Assert.That(CountOccurrences(xaml, "ItemsSource=\"{Binding"), Is.GreaterThanOrEqualTo(3));
            Assert.That(CountOccurrences(xaml, "VirtualizingPanel.VirtualizationMode=\"Recycling\""), Is.GreaterThanOrEqualTo(3));
            Assert.That(source, Does.Contain("ObservableCollection<SidebarPageItem>"));
            Assert.That(source, Does.Contain("ObservableCollection<SidebarBookmarkItem>"));
            Assert.That(source, Does.Contain("ObservableCollection<SidebarOutlineItem>"));
            Assert.That(source, Does.Contain("IsSidebarLoadCurrent"));
            Assert.That(source, Does.Contain("_loadSessionId"));
            Assert.That(refresh, Does.Not.Contain("new ListBoxItem"));
            Assert.That(refresh, Does.Not.Contain("new Image"));
            Assert.That(refresh, Does.Not.Contain("new ContextMenu"));
            Assert.That(bookmarks, Does.Not.Contain("new ListBoxItem"));
            Assert.That(bookmarks, Does.Not.Contain("new ContextMenu"));
            Assert.That(outline, Does.Not.Contain("new TreeViewItem"));
            Assert.That(source, Does.Contain("ContextMenuOpening"));
            Assert.That(source, Does.Contain("DataContextChanged += SidebarListBoxItem_DataContextChanged"));
            Assert.That(source, Does.Contain("ClearSidebarListBoxItemContextMenu"));
            Assert.That(source, Does.Contain("UnfixContextMenuTopmost"));
            Assert.That(source, Does.Contain("CommandParameter = model"));
            Assert.That(source, Does.Not.Contain("if (item.ContextMenu == null)"));
        });
    }

    [Test]
    public void SidebarReviewContractsRequireFallbackSelectionResizeBookmarkAndNarrowSemantics()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("SelectedItemChanged=\"OutlineTreeView_SelectedItemChanged\""));
            Assert.That(xaml, Does.Contain("Focusable=\"True\""));
            Assert.That(xaml, Does.Contain("KeyboardNavigation.IsTabStop=\"True\""));
            Assert.That(xaml, Does.Contain("MinWidth=\"32\""));
            Assert.That(xaml, Does.Contain("SidebarResizeThumb_KeyDown"));
            Assert.That(xaml, Does.Contain("SidebarHeaderGrid"));
            Assert.That(xaml, Does.Contain("SidebarNavBar.Visibility"));
            Assert.That(xaml, Does.Contain("ToolbarItemsScrollViewer"));
            Assert.That(xaml, Does.Contain("Editor.Sidebar.BookmarkToggle"));
            Assert.That(xaml, Does.Contain("ToggleButton x:Name=\"BookmarkToggleButton\""));
            Assert.That(xaml, Does.Contain("OutlineInvokeButton_Loaded"));
            Assert.That(xaml, Does.Contain("MinWidth=\"32\""));
            Assert.That(source, Does.Contain("ISelectionItemProvider"));
            Assert.That(source, Does.Contain("IRangeValueProvider"));
            Assert.That(source, Does.Contain("OutlineTreeView_SelectedItemChanged"));
            Assert.That(source, Does.Contain("OutlineInvokeButton_Click"));
            Assert.That(source, Does.Contain("_isSynchronizingThumbnailSelection"));
            Assert.That(source, Does.Contain("AutoCollapseSidebarForNarrowLayout"));
            Assert.That(source, Does.Contain("SidebarExpand"));
            Assert.That(source, Does.Contain("Editor.RemoveBookmark"));
            Assert.That(source, Does.Contain("SetAutomationId(BookmarkToggleButton"));
        });
    }

    [Test]
    public void SidebarLoadSessionContractRejectsStaleResultsDeterministically()
    {
        var root = FindProjectRoot();
        var source = File.ReadAllText(Path.Combine(root, "Pages", "EditorPage.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("TaskCompletionSource"));
            Assert.That(source, Does.Contain("RefreshOutlineAsync"));
            Assert.That(source, Does.Contain("sessionId != _loadSessionId"));
            Assert.That(source, Does.Contain("!string.Equals(filePath, _currentPdfPath"));
        });
    }

    [Test]
    public async Task SidebarLoadSessionTcsRejectsLateOldDocumentResults()
    {
        var gateType = typeof(EditorPage).GetNestedType("SidebarLoadSessionGate", BindingFlags.NonPublic);
        Assert.That(gateType, Is.Not.Null, "The editor must expose a deterministic session gate for async sidebar results.");
        var gate = Activator.CreateInstance(gateType!, 1, "old-document.pdf");
        var release = new TaskCompletionSource<IReadOnlyList<int>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool applied = false;

        var applyTask = release.Task.ContinueWith(_ =>
        {
            bool current = (bool)gateType!.GetMethod("IsCurrent")!.Invoke(
                gate,
                new object[] { 1, "old-document.pdf" });
            if (current)
                applied = true;
        }, TaskScheduler.Default);

        gateType!.GetMethod("Begin")!.Invoke(gate, new object[] { 2, "new-document.pdf" });
        release.SetResult(Array.Empty<int>());
        await applyTask;

        Assert.That(applied, Is.False, "A stale outline continuation must not publish after the document session changes.");
    }

    private static T GetNamed<T>(EditorPage editor, string name)
        where T : FrameworkElement
    {
        return typeof(EditorPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) as T
            ?? throw new AssertionException($"EditorPage field '{name}' was not initialized.");
    }

    private static T FindByAutomationId<T>(DependencyObject root, string id)
        where T : FrameworkElement
    {
        if (root is EditorPage editor)
        {
            var named = typeof(EditorPage).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(field => field.GetValue(editor))
                .OfType<T>()
                .FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
            if (named != null)
                return named;
        }

        foreach (var element in Descendants(root))
        {
            if (element is T typed && AutomationProperties.GetAutomationId(typed) == id)
                return typed;
        }

        throw new AssertionException($"Sidebar control '{id}' was not found.");
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;
        int next = source.IndexOf("\n        private ", start + signature.Length, StringComparison.Ordinal);
        return next > start ? source.Substring(start, next - start) : source[start..];
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static void SeedPageNavigationState(EditorPage editor, int count)
    {
        var controls = (IList)typeof(EditorPage)
            .GetField("_pageControls", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(editor)!;
        var topOffsets = (IList)typeof(EditorPage)
            .GetField("_pageTopOffsets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(editor)!;
        var heights = (IList)typeof(EditorPage)
            .GetField("_pageHeights", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(editor)!;
        for (int index = 0; index < count; index++)
        {
            controls.Add(new Caelum.Controls.PdfPageControl
            {
                PageIndex = index,
                Width = 400,
                Height = 500
            });
            topOffsets.Add(index * 528d);
            heights.Add(500d);
        }
        InvokePrivate(editor, "UpdatePageNumberIndicator");
    }

    private static KeyEventArgs CreateKeyEventArgs(Visual input, Key key)
    {
        var source = PresentationSource.FromVisual(input) ??
            new HwndSource(new HwndSourceParameters("OpenNotesNavigationTest")
            {
                Width = 1,
                Height = 1,
                WindowStyle = 0x10000000
            });
        return new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            0,
            key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
    }

    private static object InvokePrivate(EditorPage editor, string methodName, params object[] arguments)
    {
        var method = typeof(EditorPage).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException($"EditorPage method '{methodName}' was not found.");
        return method.Invoke(editor, arguments);
    }

    private static ContextMenu OpenRecycledSidebarContextMenu(EditorPage editor, ListBoxItem item, string handlerName)
    {
        var args = (ContextMenuEventArgs)Activator.CreateInstance(
            typeof(ContextMenuEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { item, true },
            culture: null)!;
        args.Source = item;
        args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
        InvokePrivate(editor, handlerName, editor, args);
        return item.ContextMenu ?? throw new AssertionException($"{handlerName} did not bind a ContextMenu.");
    }

    private static bool ResolvesCurrentSidebarModel(EditorPage editor, MenuItem command, Type modelType, object expected)
    {
        var method = typeof(EditorPage).GetMethod(
            "TryGetCurrentSidebarContextMenuModel",
            BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(modelType);
        var arguments = new object[] { command, null };
        bool resolved = (bool)method.Invoke(editor, arguments)!;
        return resolved && ReferenceEquals(arguments[1], expected);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root == null)
            yield break;

        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }
    }

    private static void AddRequiredResource(Application application, string key, object value)
    {
        if (!application.Resources.Contains(key))
            application.Resources[key] = value;
    }

    private static void AddThemeResources(Application application)
    {
        foreach (var pair in new Dictionary<string, Color>
        {
            ["ThemeWindowBackgroundBrush"] = Color.FromRgb(243, 244, 246),
            ["ThemeSurfaceBrush"] = Colors.White,
            ["ThemeSurfaceAltBrush"] = Color.FromRgb(248, 249, 250),
            ["ThemeCanvasBrush"] = Color.FromRgb(229, 231, 235),
            ["ThemeBorderBrush"] = Color.FromRgb(209, 213, 219),
            ["ThemeForegroundBrush"] = Color.FromRgb(31, 41, 55),
            ["ThemeSubtleForegroundBrush"] = Color.FromRgb(75, 85, 99),
            ["ThemeControlHoverBrush"] = Color.FromRgb(243, 244, 246),
            ["ThemeControlPressedBrush"] = Color.FromRgb(229, 231, 235),
            ["ThemeSelectionBrush"] = Color.FromRgb(219, 234, 254),
            ["ThemeSelectionForegroundBrush"] = Color.FromRgb(30, 64, 175),
            ["ThemeAccentBrush"] = Color.FromRgb(37, 99, 235),
            ["ThemeAccentHoverBrush"] = Color.FromRgb(29, 78, 216),
            ["ThemeFocusBrush"] = Color.FromRgb(21, 79, 134),
            ["ThemeMenuSeparatorBrush"] = Color.FromRgb(209, 213, 219),
            ["ThemePaperAltBrush"] = Color.FromRgb(248, 249, 250),
            ["ThemeDisabledForegroundBrush"] = Color.FromRgb(156, 163, 175)
        })
        {
            application.Resources[pair.Key] = new SolidColorBrush(pair.Value);
        }
    }

    private static void DisablePenServiceForTest(EditorPage editor)
    {
        var handler = (RoutedEventHandler)Delegate.CreateDelegate(
            typeof(RoutedEventHandler), editor, "EditorPage_Loaded");
        editor.Loaded -= handler;
    }

    private static void EnsureWpfEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows");
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenNotes.csproj")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertionException("OpenNotes project root was not found.");
    }
}
