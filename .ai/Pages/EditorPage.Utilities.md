# Pages/EditorPage.Utilities.cs（合并说明：含 EditorPage.Selectable.cs）
> Last updated: 2026-08-21（Hidden Ink and dynamic i18n synchronization pass） | Protection: STANDARD
本镜像按 Task 0 要求合并说明 `EditorPage.Utilities.cs` 与 `EditorPage.Selectable.cs` 两个 partial 文件（均为 `EditorPage` 的 partial class）。

## Purpose（一句话）
`EditorPage.Utilities.cs` 提供 EditorPage 的本地化字符串应用、Hidden Ink 工具提示刷新与"无工具模式"切换等轻量入口；`EditorPage.Selectable.cs` 是尚未实现的"可选 PDF 表面"（PdfiumViewer 原生文本选择层）stub。

## What It Does（关键机制，含行号引用）
### EditorPage.Utilities.cs
- `IsSelectionMode => _currentTool == ToolType.None`（行 7）：注意这里语义是 **None 工具 = 手势/滚动模式**，与 `ToolType.Select`（框选工具）是两个概念。
- `ToggleSelectionMode()`（行 9-20）：在 None 与 `_previousTool`（None 时回退 Pen）之间切换，供外部（如笔的双击切换）调用。
- `ApplyLocalization()`（行 22-54）：为工具栏全部控件（Undo/Redo/Pen/Highlighter/HiddenInk/Eraser/Text/Select/Save/Zoom/PageJump 等）设置 `LocalizationService.Get(...)` 的 ToolTip，并刷新已经创建的文本工具栏、文本框缩放手柄、缩略图/书签/大纲和 Sticky Note 编辑控件，再重建工具 Popup、刷新页面删除按钮文案。Hidden Ink 使用 `Editor.HiddenInkTooltip`，解释点击后显示 3 秒。
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
- **Status:** in_progress
- Every live EditorPage subscribes to `LocalizationService.LanguageChanged` while loaded, unsubscribes while unloaded, and refreshes once when it is loaded again. The current synchronization pass is closing remaining dynamic-control and duplicate-event gaps.
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
