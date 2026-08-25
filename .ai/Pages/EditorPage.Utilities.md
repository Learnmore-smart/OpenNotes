# Pages/EditorPage.Utilities.cs（合并说明：含 EditorPage.Selectable.cs）

> 2026-08-25 GREEN: removed the retired sidebar-resize tooltip/UIA metadata calls; collapse/expand, toolbar-overflow, bookmark, and page-jump metadata remain unchanged.

> V5.1.2 localized bookmark content is rebuilt from checked state with a Lucide bookmark instead of text stars.
> The obsolete Content-property localization observer was removed because rebuilding the visual from that observer recursively re-entered itself; language refresh already calls the localized rebuild directly.
> Last updated: 2026-08-24（Wave6 transient/Sticky localization refresh plus Wave4 sidebar/page-jump UIA evidence green; foreground/device visual boundary remains external） | Protection: STANDARD
本镜像按 Task 0 要求合并说明 `EditorPage.Utilities.cs` 与 `EditorPage.Selectable.cs` 两个 partial 文件（均为 `EditorPage` 的 partial class）。

## Purpose（一句话）
`EditorPage.Utilities.cs` 提供 EditorPage 的本地化字符串应用、Hidden Ink 工具提示刷新与"无工具模式"切换等轻量入口；`EditorPage.Selectable.cs` 是尚未实现的"可选 PDF 表面"（PdfiumViewer 原生文本选择层）stub。

## What It Does（关键机制，含行号引用）
### EditorPage.Utilities.cs
- `IsSelectionMode => _currentTool == ToolType.None`（行 7）：注意这里语义是 **None 工具 = 手势/滚动模式**，与 `ToolType.Select`（框选工具）是两个概念。
- `ToggleSelectionMode()`（行 9-20）：在 None 与 `_previousTool`（None 时回退 Pen）之间切换，供外部（如笔的双击切换）调用。
- `ApplyLocalization()`（行 34 起）：为工具栏全部控件（Undo/Redo/Pen/Highlighter/HiddenInk/Eraser/Shape/Laser/Ruler/Text/Select/Save/History/Zoom/PageJump 等）设置本地化 ToolTip，并刷新已经创建的文本工具栏、文本框缩放手柄、缩略图/书签/大纲和 Sticky Note 编辑控件，再重建工具 Popup、刷新页面删除按钮文案。`ApplyToolbarAccessibilityMetadata()` 是统一静态 metadata helper：设置 ToolTipService.ToolTip、AutomationProperties.Name/HelpText/AutomationId，并在每次语言刷新后重跑；Hidden Ink 保持 `HiddenInkToolButton` id。文本对齐 ComboBox 由 `RefreshTextAlignmentOptions()` 重建本地化模型项，同时恢复选中的 `TextAlignment` 值。Wave4 的同一路径还刷新页面跳转验证、侧栏折叠/展开、可调整宽度、工具栏 overflow 和书签 Add/Remove 状态，避免语言切换留下旧文案；其末尾统一调用 `ApplyStateAwareSidebarMetadata()`，按当前 `_sidebarCollapsed` 与 `BookmarkToggleButton.IsChecked` 最后写入 Expand/Collapse、Bookmark/Unbookmark 的 Name/HelpText/Tooltip；Wave6 还刷新已存在 Sticky marker context-menu 的 Delete 文案。
- The same metadata helper covers code-created text formatting controls (`Editor.Text.*`) and the color button; popup palette swatches and selection shape/filter choices receive stable ids and localized names at construction.
- `GetLocalizedToolName(ToolType)`（行 56 起）：工具名本地化 switch，包含 `ToolType.HiddenInk` 对应的 `Editor.ModeHiddenInk`。
### EditorPage.Selectable.cs
- `IsSelectablePdfSurfaceActive => false`（行 22）：**恒为 false**，stub 未实现。
- 其余 `LoadSelectablePdfDocumentAsync` / `DisposeSelectablePdfDocument` / `UpdatePdfSurfaceVisibility` / `ApplySelectableViewerZoom` / `SyncSelectableViewerFromCustomView` / `SyncCustomSurfaceFromSelectableViewer`（行 27-76）全部为 no-op / `Task.CompletedTask`。
- 主文件中的调用点（`ZoomAroundPoint` 行 1088、`ActivateTool` 行 3159-3186、`LoadPdf` 行 2601-2602）都已预留分支，一旦实现即自动生效。

## Public API / 关键成员（表）
| 成员 | 文件:行 | 说明 |
|---|---|---|
| `IsSelectionMode` | Utilities:7 | `_currentTool == ToolType.None`（手势模式，≠ Select 工具） |
| `ToggleSelectionMode()` | Utilities:9 | None ↔ 上一工具切换 |
| `ApplyLocalization()` | Utilities:22 | 全工具栏本地化 |
| `IsSelectablePdfSurfaceActive` | Selectable:22 | 恒 false（stub） |
| `ApplySelectableViewerZoom` 等 6 个 | Selectable:27-76 | 全部 no-op stub |

## Dependencies
- `LocalizationService`（键：Editor.Loading / Editor.UndoTooltip / Editor.PopupSize 等）。
- Hidden Ink 相关键：`Editor.ModeHiddenInk`、`Editor.HiddenInkTooltip`；缺失 key 应继续让验证失败，不要在此处添加硬编码 fallback。
- 主文件 `EditorPage.xaml.cs` 的 `_currentTool`/`_previousTool`/`ActivateTool`/`CloseToolPopups`/`CreateToolPopups`/`RefreshPageDeleteButtons`。

## Open Threads / Resume Context
- **Wave6 dual-review result:** `ApplyLocalization` now refreshes Sticky Save/Cancel/Delete
  Content plus Automation Name/HelpText/Tooltip through one shared focus/metadata helper;
  live EN/ZH/FR popup controls retain their IDs and 32-DIP targets. Every editor Popup,
  ContextMenu and text ComboBox z-order registration has an explicit Unfix path on close,
  release and unload, with idempotent re-fix only for live reopen. Ordinary text/save modal
  sessions remain outside `CloseTransientUi`; deterministic STA/source coverage is green.
- **Status:** complete for the Wave6 automated scope; external pointer/device/visual checks
  remain environment-dependent.
- **Intent:** preserve localized metadata after popup rebuilds while rebuilt tool popups re-register z-order and detach old Opened handlers.
- **Lifecycle contract:** localization refresh order is explicit (close → detach old row/z-order handlers → rebuild → reattach). `PopupZOrderHelper` registrations are idempotent for tool popups, ContextMenus and ComboBox dropdowns; Wave6 adds a global `CloseTransientUi`/MainWindow.Deactivated dismissal without closing save dialogs or ordinary text sessions.
- Every live EditorPage subscribes to `LocalizationService.LanguageChanged` while loaded, unsubscribes while unloaded, and refreshes once when it is loaded again. The current synchronization pass closes dynamic-control and duplicate-event gaps.
- `Selectable` remains an intentional no-op stub for the optional native PdfiumViewer text-selection surface. If it is implemented later, update this mirror and `EditorPage.md`; the existing custom PDF text-selection path is separate.

## Agent Decisions / Thoughts
- Utilities 的 `IsSelectionMode`（None 工具）与主文件的 `ToolType.Select` 命名易混淆——扩展功能前先确认语义。
- Selectable stub 保留是为了让主文件的分支调用安全存在；实现时可参考 `SyncCustomSurfaceFromSelectableViewer` 注释的双向同步意图。

## Important Notes / NEVER Change
- **NEVER**：勿把 Selectable stub 的 no-op 方法直接删除——主文件存在对其的调用点（会编译错误），且这是预留架构位。
- 本地化键集中在 `LocalizationService`，勿硬编码文案。

## Change History
- 2026-08-18: 建立镜像文档（Task 0，合并 Utilities + Selectable 说明）。
- 2026-08-20: Hidden Ink——补充工具提示和工具名本地化的镜像说明，确认 reveal 时长文案来自 `LocalizationService`。
- 2026-08-21: Added the lifecycle-bound language-change subscription; `ApplyLocalization()` remains the single refresh path for dynamic popups and menus.
- 2026-08-21: Audited dynamic i18n surfaces and identified the refresh path for existing text toolbars, resize handles, document sidebars, outline nodes, and Sticky Note controls.
- 2026-08-23: Added localized Hidden Ink Automation Name/HelpText beside the stable `HiddenInkToolButton` AutomationId so UIA does not depend on Tooltip exposure.
- 2026-08-23: Added `ApplyToolbarAccessibilityMetadata()` and rerun it after every localization/popup rebuild; dynamic shape/highlighter controls now receive stable localized UIA metadata.
- 2026-08-23: Dual-review continuation records explicit popup z-order detachment before tool popup replacement; no global deactivation sweep is introduced in this Wave 3 slice.
- 2026-08-23: Added idempotent ContextMenu/ComboBox z-order registration coverage after auditing repeated `ApplyLocalization()` calls; focused toolbar contracts are green.
- 2026-08-23: P2 alignment refresh now uses localized `TextAlignmentOption` values without losing the enum selection; final focused source/runtime filter passed `20/20` and the explicit-fixture Editor UIA smoke passed.
- 2026-08-23: Wave 4 metadata refresh now covers the compact `Editor.PageJump` field, custom sidebar command rail, collapse/resize affordances and dynamic page/bookmark/outline items; page-jump and sidebar UIA smoke passed with localized metadata.
- 2026-08-24: Wave4 review follow-up refresh now reapplies localized PageJump validation, resize/collapse/overflow labels and bookmark Toggle status to realized controls. The final P2 pass keeps the one-based initial field out of edit mode and makes the state-aware sidebar metadata helper the final localization write; EN/ZH/FR STA coverage keeps collapsed/bookmarked labels correct. The three-page editor smoke committed page 2 and invoked fallback outline page 2; the focused navigation class and full suite passed. Cross-page physical input remains explicitly blocked by the foreground environment.
- 2026-08-24: Wave6 Sticky marker context menus now refresh localized Delete labels alongside the explicit Save/Cancel/Delete popup; transient closure is centralized in EditorPage and invoked by MainWindow deactivation/tab lifecycle.
- 2026-08-24: Added localized metadata/tooltips for the real Previous/Next page buttons; the editable page number and its stable UIA id remain unchanged.
- 2026-08-24: Sticky localization refresh now includes the editor title and draggable-header tooltip/Automation metadata in addition to Save/Cancel/Delete.
