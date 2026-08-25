# Shared production AutomationId aliases for editor/pointer smoke scripts.
# Keep this file data-only so every smoke entry point resolves the same IDs as
# Pages/EditorPage.xaml; Hidden Ink retains its historical compatibility id.
$EditorAutomationIds = [ordered]@{
    Undo = 'Editor.UndoButton'
    Redo = 'Editor.RedoButton'
    Pen = 'Editor.PenToolButton'
    Highlighter = 'Editor.HighlighterToolButton'
    HiddenInk = 'HiddenInkToolButton'
    Sticky = 'Editor.StickyNoteToolButton'
    Eraser = 'Editor.EraserToolButton'
    Shape = 'Editor.ShapeToolButton'
    Laser = 'Editor.LaserToolButton'
    Ruler = 'Editor.RulerToolButton'
    Select = 'Editor.SelectToolButton'
    Text = 'Editor.TextToolButton'
    Save = 'Editor.SavePdfButton'
    VersionHistory = 'Editor.VersionHistoryButton'
    PageJump = 'Editor.PageJump'
    SidebarPages = 'Editor.Sidebar.Pages'
    SidebarOutline = 'Editor.Sidebar.Outline'
    SidebarBookmarks = 'Editor.Sidebar.Bookmarks'
    SidebarCollapse = 'Editor.Sidebar.Collapse'
    SidebarResize = 'Editor.Sidebar.Resize'
    SidebarPagePrefix = 'Editor.Sidebar.Page.'
    SidebarBookmarkPrefix = 'Editor.Sidebar.Bookmark.'
    SidebarOutlinePrefix = 'Editor.Sidebar.Outline.'
    PenOnly = 'Editor.PenOnlyButton'
    ZoomOut = 'Editor.ZoomOutButton'
    ZoomIn = 'Editor.ZoomInButton'
    Rotate = 'Editor.RotatePageButton'
    PdfScrollViewer = 'PdfScrollViewer'
    PdfPageControlPrefix = 'PdfPageControl.'
    TextResizeHandlePrefix = 'TextResizeHandle.'
    TextResizeHandleBottomRight = 'TextResizeHandle.BottomRight'
    TextAnnotationDragHandle = 'TextAnnotationDragHandle'
}

function Get-EditorAutomationId([string]$name) {
    if (-not $EditorAutomationIds.Contains($name)) {
        throw "Unknown editor AutomationId alias: $name"
    }
    return $EditorAutomationIds[$name]
}

function Get-EditorPageAutomationId([int]$pageIndex) {
    if ($pageIndex -lt 0) {
        throw "Page index must be non-negative: $pageIndex"
    }
    return "$($EditorAutomationIds.PdfPageControlPrefix)$pageIndex"
}

function Get-EditorTextResizeHandleAutomationId([string]$direction) {
    if ([string]::IsNullOrWhiteSpace($direction)) {
        throw 'Text resize handle direction is required.'
    }
    return "$($EditorAutomationIds.TextResizeHandlePrefix)$direction"
}
