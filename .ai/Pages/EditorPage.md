# Pages/EditorPage.xaml.cs

> V5.1.2 dynamic page controls, bookmarks and notifications use Lucide vectors; PenOnly remains PenLine.

## Wave6 async stale-operation P2 (2026-08-24) — audit continuation

Version History, sidebar/page context, PDF structural/context operations, search,
autosave, and Undo/Redo use a shared `DocumentOperationSession` lease. Each
operation captures session/path/model identity and validates after awaits and
before any UI/model/undo/dirty/error publication. `LoadPdf`, release/unload, and
inactive tab transitions cancel the old session. Deferred Version History clicks
hold edit admission; PDF/sidebar context capture rejects blocked/inactive hosts;
thumbnail load markers are session-scoped so recycled rows cannot suppress a
replacement page. Live same-session operations retain existing save-coordinator
behavior.

- **Audit result:** RED-first contracts exposed the missing Version History
  post-toast `MarkDirty` guard, stale thumbnail exception/marker publication,
  PDF-search selection exceptions, autosave diagnostic ordering, and deferred
  context-menu admission. All are now guarded; stale callbacks return silently
  without old-document UI/model/undo/dirty/error publication.

## Wave6 Sticky/transient lifecycle (2026-08-24)

- **Status:** green for focused automated scope. Sticky Note editing now exposes explicit
  Save/Cancel/Delete controls; Save pushes one dirty-only text action, Cancel (including
  outside click, Escape, tab switch, deactivation, navigation, unload and release) restores
  original text/position, and Delete is reversible.
- `CloseTransientUi(reason)` owns a weak Popup/ContextMenu/ComboBox registry and closes
  editor search/tool/color/sticky/context surfaces plus marker menus. It is called on
  Escape/outside click, inactive-tab/navigation/release barriers and page unload; ordinary
  text sessions and save dialogs are intentionally outside the sweep. MainWindow.Deactivated
  calls it for every retained editor, and the active editor can reopen popups after resume.
- Sticky marker move/delete/keyboard/copy/paste/duplicate flows use page-owned quiet APIs
  and stable note Ids, so undo/redo never holds a stale popup reference. PDF /Text `/NM`
  stores the Id plus additive marker width/height/colour metadata; legacy PDFs still load
  through `/Rect` and default visual values. Selection resize updates sticky width/height
  and re-clamps its serialized X/Y; cross-page selection transfer/rollback also transfers
  the page-owned overlay payload so sticky text, position, size and colour survive the move.

## Wave6 dual-review follow-up (2026-08-24) — GREEN closure

- **Root cause/fix:** page/editor interactions now share `IInteractionCancellation`; LostCapture,
  Escape, inactive-tab/deactivation, navigation/reload and unload restore the opening snapshot
  before release. `CloseTransientUi` and `LoadPdf` cancel before page detach/clear, and
  `SetHostActive(false)` reaches every live page. Normal pointer/stylus-up remains the only
  completion path that records one undo/dirty action.
- **Session/popup fix:** Sticky Save/Cancel/Delete validate the live page/container/model and
  `_loadSessionId`; stale popup state cannot mutate a replacement document. All Popup, ContextMenu
  and ComboBox z-order hooks are explicitly Unfixed at close/release/unload and re-established
  idempotently for a live reopen. Save/Cancel/Delete share localized Content/Tooltip/UIA
  metadata, 32-DIP targets and the ThemeFocus/HighContrast 2-DIP focus cue.
- **Evidence:** deterministic STA/source/PDF contracts and the focused Sticky/transient filter
  pass `20/20`; full suite passes `241/241`; i18n passes `279` catalog entries/`468` calls/`0`
  hard-coded visible strings. External foreground/Alt-Tab/device/visual checks remain explicit
  blockers; ordinary text-edit sessions, save dialogs and multi-tab ownership remain outside
  the transient sweep.
> Last updated: 2026-08-24（Wave6 Sticky/transient lifecycle plus Wave5 motion/runtime chrome/PDF composite GREEN; foreground/device visual boundary remains external）| Protection: STANDARD
Wave 1 note: shape replacement undo stores only session token/index and immutable snapshots; `StrokeReplacedAction` must never retain live `Stroke` fields. Other stroke actions carry placement identity when restoring live strokes.

## Wave 1 quality follow-up（2026-08-23）

- `StrokeAddedAction`, `StrokesErasedAction`, `ItemsAddedAction`, `ItemsRemovedAction`, and `SelectionCrossPageMoveAction` store `StrokePlacement` records for token/side/index/page ownership. Their page calls resolve the current live stroke by stable token/side/owner, so shape redo can replace the historical reference without making erase/delete/move redo silently fail; repeated calls are no-op safe and cannot duplicate strokes.
- Cross-page transfer records source placements and the target placements returned by the target page. Initial multi-selection transfer, undo and redo are transactions: a source capture, target owner/token/side conflict, or counterpart add/remove failure rolls back every earlier stroke and leaves `LastOperationSucceeded=false`; callers do not push or move the undo/redo stacks on that result. Successful undo/redo uses the placement currently resolved by each page, and moves those current live strokes through `MoveItemsDirectly`. Source ownership/index and target ownership/index remain stable, while `MoveItemsDirectly` preserves pressure.
- `StrokeReplacedAction` remains the only snapshot-only shape action and now is exercised through the real private production action in `StrokeReplacementProductionTests`; shape undo after erase/delete/cross-page restore goes through `PdfPageControl.TryReplaceStrokeQuiet`, while subsequent placement redo resolves the replacement's fresh live reference by token.
（同目录 `EditorPage.xaml` 为其 UI 布局；partial 类另见 `EditorPage.Selectable.cs`（stub）与 `EditorPage.Utilities.cs`，后者另有镜像 `.ai/Pages/EditorPage.Utilities.md`）

## Wave 3 toolbar implementation（2026-08-23）

- **2026-08-24 polish result:** the explicit shape-choice checkmark is removed in favor of the existing selected tile/active bar; nine localized vector choices live in a 3×3 grid and remain session-only. Toolbar Lucide glyphs inherit owner foreground uniformly, while pen/highlighter data colors use small indicators instead of tinting/doubling glyphs. Command handlers, UIA IDs and the ink persistence pipeline are unchanged.

- Toolbar buttons use semantic vector `Path` geometry with stable `Editor.*` AutomationIds, localized ToolTip/Automation Name/HelpText, a shared non-shifting focus ring, and 32 DIP minimum targets. Checked/disabled states use theme resources and opacity/active-bar cues that remain visible across palettes.
- Shape popup choices (`Editor.Shape.Line`, `.Rectangle`, `.Ellipse`, `.Arrow`) are keyboard-selectable `ToggleButton`s with geometry previews and `IsChecked` state. Highlighter mode choices use freehand/text/underline/strikeout/squiggly/area stroke previews, and each preview refreshes from `_highlighterColor` while the popup remains open.
- Laser uses an explicit beam/dot vector; Hidden Ink keeps its card/answer vector and `HiddenInkToolButton` id. Pen/highlighter/eraser/ruler/select/text/save/history/zoom/rotate/immersive controls no longer rely on MDL2 glyph semantics.
- `ApplyToolbarAccessibilityMetadata()` reapplies localized metadata after language refresh and `CreateToolPopups()` rebuilds localized dynamic choices. `Services/LocalizationService` no longer exposes Fit Width/Fit Page or Ink Analysis-unavailable keys.
- Pen preset JSON compatibility remains in `AppSettingsService`/`AppSettings.PenPresets`; no toolbar slot is created and no empty list is populated or reset.

## Wave 3 dual-review continuation（2026-08-23）

- **Status:** complete for automated source/build/test scope. Existing source contracts cover six live highlighter previews, dynamic Button/ToggleButton peers, checked cues, marker contrast, popup lifecycle and the five editor smoke entry points.
- **Intent/result:** `PopupZOrderHelper` registration is idempotent and explicitly detachable during `ApplyLocalization()` popup replacement; the static mark shares the production highlighter alpha; and page/viewer/handle smoke IDs are centralized through `OpenNotesEditorAutomationIds.ps1`.
- **Constraints:** preserve the existing PopupZOrderHelper Win32 behavior, do not add global deactivation dismissal, do not change Hidden Ink's compatibility ID, and do not alter PDF/annotation or preset model compatibility.

## Wave 3 P2 continuation（2026-08-23）

- **Result:** the inline text-alignment array is now a localized `TextAlignmentOption` model. `RefreshTextAlignmentOptions()` replaces labels on `LanguageChanged` while restoring the selected `TextAlignment` value, and `verify-i18n.ps1` rejects the former literal alignment array.
- **Result:** shape/filter/mode choices are semantic `ToggleButton`s with real checked peers, checkmark/active-bar cues, keyboard tab/activation, 32 DIP minimum targets, and shared theme focus/disabled/hover/pressed resources. `CreateIconButtonTemplate` and page chrome templates use dynamic Theme resource keys; palette/recent markers use `ThemeFocusBrush` plus `ThemeSurfaceBrush` contrast instead of fixed white.
- **Theme-token continuation:** `SetRulerVisible` uses live `ThemeAccentBrush`/`ThemeForegroundBrush` references for visible/hidden state. Text font-group, color-indicator border, preview/separator/header, palette and recent-swatch state surfaces use semantic resources without hard-coded theme-color initializers; selected text/palette content backgrounds remain the actual user color.
- **Result:** `EditorPopupAutomationTests` constructs the production popups on an STA dispatcher and validates localized IDs/names/help text, TogglePattern state/activation, target sizes, and slider focus metadata without foreground ownership. Required Editor smoke IDs now throw/non-zero on omission and the optional list is explicit.
- **Evidence:** focused `EditorToolbarVisualSourceTests|EditorPopupAutomationTests` passed `22/22`; full `dotnet test` passed `189/189`; solution build passed with `0` errors and the existing 2 NU1701 warnings; i18n passed `268` catalog / `420` calls / `0` hard-coded visible strings; explicit-fixture Editor UIA smoke passed with all required production IDs and tool toggles, and isolated cleanup was true.
- **Constraint:** Wave6 global transient teardown remains out of scope; visual screenshots, device/foreground pointer checks and third-party viewer checks remain external.

## Wave 4 navigation continuation（2026-08-23）

- **Status:** review follow-up in_progress. The follow-up contracts are now green; the status stays open for the parent wave's final review bookkeeping and the documented foreground/device visual boundary. Ownership remains limited to the page jump and document sidebar sections in `EditorPage.xaml(.cs)`; toolbar, PDF rendering/coordinates, MainWindow lifecycle, and Sticky Note behavior remain unchanged.
- **Page jump result:** removed the mouse-only `PageJumpBorder` activation path. `PageNumberTextBox` is the compact always-visible `Editor.PageJump` TextBox, so WPF exposes ValuePattern and keyboard focus. Its XAML starts at one and construction suppresses the initial `TextChanged`, leaving `_isPageJumpEditing=false`; empty documents retain a safe `1 / 0` field. Enter commits, Escape restores the opening value, LostFocus commits valid values, invalid text restores the current page with a localized accessible validation message, and out-of-range values clamp.
- **Sidebar result:** replaced native TabControl/TabItem chrome with localized Pages/Outline/Bookmarks command buttons, explicit selected/focus cues, a shared state host, bounded resize thumb, and collapse behavior. Existing thumbnail/outline/bookmark controls and persistence handlers remain the data/interaction source of truth; dynamic items now expose stable localized metadata.
- **Constraints honored:** no MainWindow deactivation lifecycle changes, no Sticky Note popup lifecycle changes, no FitPage/FitWidth entry points, and no PDF bitmap/coordinate changes.
- **Baseline evidence:** `EditorNavigationSourceTests` failed first against the old Border/TabControl contract, then passed 3/3 with STA peer assertions. The follow-up expanded this to 12/12, with full-suite/build/i18n and three-page UIA smoke evidence recorded below; manual foreground/device/visual checks remain unclaimed.

## Wave 4 review follow-up evidence（2026-08-24）

- **PageJump:** the edit state is re-entered from `TextChanged`, so a field that remains visible can be edited again after Enter, Escape, invalid input, out-of-range clamping, LostFocus or Tab. `SelectionBrush`/`SelectionTextBrush` are live theme resources, and invalid/range feedback restores a safe value while exposing localized UIA HelpText.
- **Sidebar data path:** page/bookmark/outline rows are `ObservableCollection` view models bound through recycling `ItemsControl` templates. Thumbnail loading and context-menu construction are deferred to realized containers. Outline refresh uses cancellation plus a session/path gate and a run-continuation-safe TCS, preventing a late old-document result from replacing the active document.
- **Sidebar semantics:** fallback outline rows use the same `SidebarOutlineItem` model and `TreeViewItem` metadata as real outline rows; both SelectionItem and Invoke providers reach `JumpToPage(1)` in the STA peer contract. Because WPF's external `TreeViewItem` provider retains its native TreeItem pattern set, each realized row also has a localized, keyboard/UIA-invokable 32 DIP `.Invoke` Button that reaches the identical command path. Resize is a 32 DIP focusable transparent hit target with `IRangeValueProvider`, Home/End and arrow/Shift-arrow commands. Collapse hides nav/content and restores them on expand; widths at or below 375 DIP auto-collapse and toolbar content scrolls horizontally.
- **Theme/i18n/UIA:** ListBoxItem/TreeViewItem and template labels use DynamicResource foreground/state tokens. Bookmark is a real ToggleButton with localized Add/Remove metadata and ToggleState. EN/ZH/FR labels cover validation, resize, collapse and toolbar overflow; high-contrast runtime peer checks pass.
- **Evidence:** final follow-up navigation tests passed 14/14; full suite passed 203/203; `dotnet build OpenNotes.csproj -c Debug --no-restore` passed with 0 errors (existing PdfiumViewer/HDPI warnings); `verify-i18n.ps1` passed 274 catalog entries / 454 calls / 0 hard-coded visible strings. A generated three-page PDF smoke exposed initial `Editor.PageJump` value `1`, invoked `Editor.Sidebar.Outline.Page.2.Invoke` to page 2, selected `Editor.Sidebar.Outline.Page.2` to page 2, toggled tools and cleaned its isolated data root. `Test-OpenNotesCrossPageKeyboardSmoke.ps1` was attempted but stopped before input with `REAL_SCREEN_INPUT_UNAVAILABLE` (`foregroundHwnd=0`, `foregroundPid=0`); no real-screen/device/visual pass is claimed.
- **Scope guard:** no `MainWindow` lifecycle/global popup dismiss, Sticky Note behavior, Wave5 theme/backdrop, FitPage/FitWidth entry point, or PDF canvas geometry/zoom code was changed in this follow-up.

## Wave 4 recycled sidebar menu follow-up（2026-08-24）

- **Bug:** recycling can reuse a `ListBoxItem` whose `ContextMenu` still contains handlers closed over the previous page/bookmark index. `SidebarListBoxItem_Unloaded` did not clear that menu, `DataContext` changes did not invalidate it, and both Opening handlers skipped rebuilding whenever `ContextMenu != null`.
- **GREEN:** one idempotent container cleanup path now runs on `Unloaded` and `DataContextChanged`; it unfixes the old popup hook, detaches named menu handlers, clears old menu items and nulls the container menu. Every Opening rebuilds from the current stable `SidebarPageItem`/`SidebarBookmarkItem` identity, sets `PlacementTarget`, `Tag` and `CommandParameter`, and rejects a stale binding before invoking a command. Pages and Bookmarks share the same lifecycle audit; no document/PDF geometry or Wave6 lifecycle code is involved.
- **TDD evidence:** the real STA test recycles one `ListBoxItem` through page 1 → page 2 and bookmark A → bookmark B, raises retained old and current menu commands, asserts only the current model binding remains, and verifies old handlers/items are cleared. Focused navigation tests pass 15/15; the full suite passes 204/204.

## Wave 5 review follow-up（2026-08-24）

- Home/Editor smooth scrolling now consumes `ThemeService.GetAnimationDuration` and checks `ShouldAnimate`; a zero duration removes the Rendering subscription and applies the target offset immediately. Toast fades use the same helper plus cancellation, laser fade removes immediately under ReduceMotion, and selection dash animation does not subscribe when motion is reduced.
- Runtime page insert/delete buttons and thumbnail menus use `SetResourceReference` for control, accent, danger, focus, opacity and text tokens. Text annotation selection/caret/drag/resize chrome follows live focus/accent/control/border resources; user text/annotation foreground colors remain the data colors.
- The real WPF composite contract mounts `PdfPageControl` with a non-white bitmap plus annotation rectangle under Neutral/Paper/Slate workspace borders and compares the page crop. `PdfImage`/overlay source, opacity and effects remain independent of the outer workspace.

## Purpose（一句话）
单标签页的 PDF 笔记编辑器主页：持有工具栏、undo/redo 命令栈、缩放/平滑滚动/懒渲染管线、粘贴与自动保存，并把所有页面级操作委派给 `PdfPageControl`；同时负责 Hidden Ink 的工具选择、收集/加载和撤销接线。

## What It Does（关键机制，含行号引用）
- **工具枚举与 Wave 3 toolbar（当前实现）**：保留 `ToolType` 与既有命令状态；XAML 工具栏改用 `Path` 几何图标（含显式 Laser beam/dot、当前 `_highlighterColor` 的 HighlighterIcon），移除可见 `PresetSlotsPanel`、Fit Width/Fit Page 控件与 Ink Analysis unavailable 入口。旧 `InitializePenPresetSlots`/`BuildDefaultPenPresets` 仅保留为只读兼容符号，不在构造函数调用，也不填充/写回空 `AppSettings.PenPresets`。
- **直尺工具（Task 22，#region "Task 22: on-screen ruler"）**：`RulerToolButton`/`SetRulerVisible` 提供 session-only 的全视口 overlay，不改变当前 ToolType；尺身可移动、端帽/右键可旋转并以 15° 吸附，`GetRulerEdgeEndpoints` 返回上边缘，页面通过实时 delegate 做吸附几何换算。尺本体不进入注释/undo/save 管线；沉浸模式不会隐藏 `RulerOverlayCanvas`，但会隐藏并恢复工具栏、文档侧栏和 PDF 搜索面板。完整交互与 24px 线段容差见 `PdfPageControl.md`。
- **Undo/Redo 命令栈**（行 98-545）：接口 `IUndoAction { bool LeavesDocumentDirty; Task UndoAsync(); Task RedoAsync(); }`（行 99），栈为 `List<IUndoAction> _undoStack/_redoStack`。除既有笔迹/文本/页面动作外，Hidden Ink 还提供新增和移除两类专用动作：
  1. `StrokeAddedAction`（行 106）— 单笔迹增删（RemoveStrokeQuiet/AddStrokeQuiet）
  2. `StrokesErasedAction`（行 125）— 擦除手势（移除的原笔迹 + 产生的切割碎片；Undo 移碎片还原笔迹，Redo 反向）
  3. `StrokeReplacedAction`（行 157，Task 4/Wave 1）— 形状识别替换（仅保存 token/index 与 immutable original/ideal snapshots；Undo/Redo 通过 page quiet replacement 原位互换，缺 token/side 时 no-op；订阅 PageControl.StrokeRecognized）
  4. `ItemsAddedAction`（行 184）— 批量笔迹+文本容器新增（粘贴用）；Undo 移除项前先清该页选区（Task 8 粘贴自动选中后的悬空引用防护）
  5. `ItemsRemovedAction`（行 212）— 批量删除（反向恢复）
  6. `TextBoxAddedAction`（行 240）— 文本框新增（用户点击创建；粘贴/加载路径 select:false 不推入）
  7. `TextBoxDeletedAction`（行 263）— 文本框删除（DeleteSelectedTextBox 容器分支；选区删除仍走 ItemsRemovedAction）
  8. `TextEditSessionAction`（行 286）— 文本内容编辑会话（GotFocus 捕获原文，LostFocus 不同则推入，单步撤销整段编辑）
  9. `TextStyleChangedAction`（行 316）— 字号/颜色变更（AdjustSelectedTextBoxFontSize 与调色盘选色推入，含 before/after FontSize+Foreground）
  10. `TextBoxMovedAction`（行 350）— 文本框 dragHandle 同页拖动（before/after Canvas 坐标）
  11. `TextBoxResizedAction`（行 642）— 八方向文本框缩放（before/after `TextBoxBounds`，单次拖动一个 undo action；Esc 恢复起始矩形）
  12. `SelectionMoveAction`（行 379）— 选区整体平移 ±delta
  13. `SelectionCrossPageMoveAction`（行 409）— 跨页拖动（source/target 双页 + adjust 修正，含 `ExecuteInitialTransfer`）
  14. `SelectionResizeAction`（行 490）— 选区缩放（1/scale 逆操作，anchor 锚点）
  15. `DocumentSnapshotAction`（行 520）— **页面增删等结构性操作的整文档字节快照**（before/after bytes + 焦点页索引，`LeavesDocumentDirty=false`，走 `ApplyDocumentSnapshotAsync`）
- **Undo 执行**（行 1867-1913）：`PerformUndoAsync`/`PerformRedoAsync` 弹栈执行并互推；`PushUndoAction`（行 1907）会清空 `_redoStack`；`UpdateUndoRedoButtons`（行 1889）按栈深启停按钮；`ApplyDirtyStateForAction` 用 `action.LeavesDocumentDirty` 覆盖 `_isDirty`（快照类操作撤销后文档算"干净"）。按钮图标颜色由 `UndoRedoButtonStyle`（EditorPage.xaml）控制：IsEnabled=true → #1F1F1F，=false → #B0B0B0（TextBlock 无本地 Foreground，靠 TextElement 继承传播）。
- `HiddenInkAddedAction`：新建遮罩的 undo/redo（quiet remove/add）。
- `HiddenInkRemovedAction`：擦除遮罩的 undo/redo（quiet add/remove）。两者均 `LeavesDocumentDirty=true`，由统一命令栈提供 Ctrl+Z/Ctrl+Y。
- **快捷键** `EditorPage_KeyDown`（行 1162-1246）：Esc→None 工具；Ctrl+S 保存；Ctrl+P 打印；Ctrl+C 复制选区（优先注释选区，其次 PDF 文本）；Ctrl+X 剪切；Ctrl+V **先 `PasteClipboardImage()`（Task 19：剪贴板位图优先），无图才 `PasteSelection`**；**Ctrl+D `DuplicateSelection`（Task 13，行 1215）**——`IsEditableTextInputFocused()` 守卫（TextBox 不消费 Ctrl+D，事件会冒泡上来，编辑中必须显式跳过）+ `_activeSelectionPage.HasSelection` 才执行；Ctrl+0/± 缩放。Ctrl+Z/Y/Ctrl+Shift+Z 在 `TryHandleUndoRedoShortcutAsync`（行 1139，输入框聚焦时放行给 TextBox）。**Task 16 沉浸模式快捷键在 `EditorPage_PreviewKeyDown` 顶部（先于 undo/redo 处理）**：F11（文本编辑中跳过）→ `ToggleImmersiveMode()`；ESC 且 `_isImmersiveMode` → 退出沉浸（preview 置 Handled 抑制冒泡 KeyDown 的"ESC 重置工具"分支——沉浸时 ESC 恒先退沉浸）。
- **仅笔绘制（Task 15）**：工具栏右侧 ZoomOut 前的 `PenOnlyButton`（E7C9 笔图标，`ToolbarToggleButtonStyle` + 选中态图标蓝色 #0078D4 tint，tooltip「仅笔绘制 Pen-only」）；点击 → `SaveSetting(s.PenOnlyMode)` 持久化 → `ApplyToolToAllPages`（其内重读设置、回写 `PenOnlyButton.IsChecked` + `UpdatePenOnlyButtonVisual` 图标 tint + `page.PenOnlyMode` 全页同步；ctor 的 `ActivateTool(None)` 即首次同步，启动即恢复按钮态）。阻断机制见 PdfPageControl 镜像"仅笔绘制"段。
- **全屏沉浸模式（Task 16）**：`_isImmersiveMode` + `ToggleImmersiveMode()`——进入：`CloseToolPopups()` 关闭全部工具弹窗，记录并将 `ToolbarBorder`、`DocumentSidebar`、`PdfSearchPanel` 的 Opacity 置 0 且不可命中（**纯视觉隐藏而非 Visibility=Collapsed**，不重排页面）；退出：逐一恢复记录值。F11、沉浸中的 ESC 和新增 `ImmersiveModeButton` 都走同一切换函数；反复进出无残留。沉浸期间书写/滚动/Ctrl+Z 均不依赖工具栏照常工作；不做窗口无边框化。
- **定点缩放** `ZoomAroundPoint`（行 1369-1406，Task 12.1 重写）：先把视口点换算为内容坐标（`(Offset+viewportPoint)/oldZoom`），`ApplyCustomZoom` 后**同步**修正偏移——`UpdateLayout()`（新 scale 下 extent 立即重算，`ScrollTo*` 按新 Scrollable* 正确 clamp）→ 计算新偏移 → `ScrollToHorizontal/VerticalOffset` → 再 `UpdateLayout()` 提交 → `SyncSmoothScrollState()`。偏移修正与新缩放落在**同一布局 pass**，无中间帧。原 `Dispatcher.BeginInvoke(Render)` 延迟一帧方案本身就是跳帧根因（一帧以新缩放+旧偏移渲染），Task 12 已移除。`UpdateLayout` 期间 ScrollChanged 同步触发，但其 handler 只做懒渲染防抖/动画基准同步，**不写偏移**，不会打架。全部 6 个入口共享此方法：Ctrl+滚轮（`PdfScrollViewer_PreviewMouseWheel` 行 1243）、WndProc 精密触摸板 pinch（合成 Ctrl+Wheel 消息，行 975）、触摸双指 pinch（`PreviewTouchMove` 行 1511）、工具栏缩放 ±钮（行 4308/4315）与缩放百分比输入框（行 4353，均视口中心锚点）。
- **缩放防抖重渲染 + 模式预算** `ScheduleReRenderForZoom`：复用单个 250ms `DispatcherTimer`（连续输入只 restart，不再为每个事件分配 `Task.Delay`）并取消上一代 native work；`PdfRenderPolicy` 按 BatterySaver/Balanced/BestQuality 把请求缩放限制到 1.35x/2x/3x，并按页尺寸继续压到 32/64/128 MiB 的单页预算。移动中用 LowQuality 插值，稳定后恢复 HighQuality；只渲染当前可见工作集。
- **滚动懒渲染 + 有界工作集** `PdfScrollViewer_ScrollChanged`：复用单个 100ms `DispatcherTimer` 做 restartable debounce；首次进入视口的页做初始渲染，高清补渲染只覆盖可见页。`TrimPageBitmapWorkingSet` 通过 `PdfRenderPolicy.GetRetainedPageIndices` 保留可见范围及模式 padding，其他页清 `PageSource` 和渲染标记。Balanced/BestQuality 仍以 `ApplicationIdle` 预取相邻页，BatterySaver 禁止预取。页面控件/注释仍非虚拟化并保留，释放的是占用最大的 display-only BitmapSource。
- **滚动条点击即达**（Task 11，行 1810-1865）：`InstallScrollbarTrackJump`（EditorPage_Loaded 调用）经 `PdfScrollViewer.Template.FindName` 取 `PART_VerticalScrollBar`/`PART_HorizontalScrollBar`，为每根滚动条创建显式 `MouseButtonEventHandler` 实例后再 Remove/Add `PreviewMouseLeftButtonDown`，避免 WPF 路由事件的委托类型不匹配，同时覆盖 Loaded 反复触发与模板重应用；handler `ScrollBarTrackJump_MouseLeftButtonDown`：OriginalSource 在 Thumb 子树内→跳过（原生拖拽）；否则经 `PART_Track` 取 thumb/track 几何，`ratio = (clickPos − thumbLen/2)/(trackLen − thumbLen)` clamp 0..1（垂直 Track `IsDirectionReversed=True`，clickPos 先归一化为值增大方向），`CancelSmoothScroll()` → `ScrollToVerticalOffset/HorizontalOffset(ratio × Scrollable*)`（同步直跳，无动画无分页步进）→ `SyncSmoothScrollState()` 同步滚轮动画基准 → `e.Handled=true`。配套放行：`PdfScrollViewer_PreviewMouseDown` 与 `PdfScrollViewer_PreviewStylusDown` 开头以 `IsOriginalSourceOverScrollbar`（FindAncestor\<ScrollBar\>，行 3123）放行滚动条子树的左键/笔输入——否则 Select 工具选择委托（页面 50px buffer 覆盖滚动条）会在祖先层 handle 掉事件、None 工具笔点击会被 pen-scroll capture 吞掉。App.xaml 滚动条模板零改动。
- **粘贴** `PasteSelection`（行 2850）：从剪贴板 JSON 反序列化 `AnnotationData`（只取 Pages["0"]）；目标页优先 `_lastClickedPage`，否则 `_activeSelectionPage`/首页；有点击点时按内容包围盒 minX/minY 把粘贴内容**对齐到 `_lastClickedPoint`**（Task 19 起包围盒含 Images），否则偏移 (20,20)；重建 Stroke/Text/**Image（Task 19：AddImage 显式宽高复原副本尺寸，仅位置吃偏移）** 后 `PushUndoAction(new ItemsAddedAction(...))` → 清其它页残留选区 → `targetPage.SelectItems(pastedStrokes, pastedContainers)` **自动全选粘贴内容**（Task 8.2）→ `MarkDirty()`；异常仅 Console 吞掉。
- **图片注释（Task 19）**：
  - **剪贴板粘贴** `PasteClipboardImage`：Ctrl+V 先于 PasteSelection 执行——`Clipboard.ContainsImage()` → GetImage()（失败再取 "PNG" 自定义格式 MemoryStream→BitmapFrame，浏览器复制走此路径）→ `EncodeBitmapSourceToPng`（PngBitmapEncoder）→ `page.AddImage(bytes, pos)`（落点：`_lastClickedPage==目标页` 用 `_lastClickedPoint`，否则页中心并二次居中）→ 单容器 `ItemsAddedAction` → 清他页选区 → `SelectItems` 自动选中 → MarkDirty → Toast「图片已粘贴」。返回 false（剪贴板无图）则回落 PasteSelection。
  - **拖放** ctor 接线 `PreviewDragOver`/`Drop`（Page 根 XAML 已有 AllowDrop=True 此前是死设置）：DragOver 对 FileDrop 含 .png/.jpg/.jpeg → e.Handled + Effects=Copy；Drop → `e.GetPosition(PagesContainer)` + TranslatePoint 逐页命中（与 FindPageAtContainerPoint 同款换算）找落点页，未命中（页间隙/chrome）回落 GetFirstVisiblePage 页中心；逐文件 `File.ReadAllBytes` → `AddImage`（多文件 +20px 阶梯落点）→ 批量 `ItemsAddedAction` + `SelectItems` + MarkDirty + Toast「图片已插入」。`IsSupportedImageFile`/`SupportedImageExtensions` 静态辅助（注意 `System.IO.Path` 全限定——本文件有 Shapes.Path 歧义）。
  - **收集/装载** `CollectAnnotations` 遍历 `page.ImageContainers`+`GetImageData` 产出 `pa.Images`（base64 原始字节 + Format 魔数嗅探 `PdfService.DetectImageFormat`）；`LoadAnnotationsFromPdfServiceAsync` 以保存的 X/Y/W/H **显式尺寸** `AddImage` 复原，全程 `_isLoadingAnnotations=true` 抑制 `ImagesChanged`→MarkDirty（装载不得置脏文档），finally 复位。
  - **复制/剪切/Ctrl+D** CopySelection 容器循环加 image else-if 分支（base64 + 实际宽高入 Images）；CutSelection=Copy+Delete 零改动即通；DuplicateSelection image 分支用原始字节 AddImage 保活体尺寸。
  - **跨页移动** `SelectionCrossPageMoveAction` 三处（ExecuteInitialTransfer/Undo/Redo）容器循环后 `TransferImageData(source, target, container)`——payload 字典 per-control，Grid 换父页后必须显式搬字节（文本容器 GetImageData 为 null 天然 no-op）。dragHandle 跨页（Task 9）复用同一 action 自动覆盖。
  - **事件** LoadPdf 订阅/DetachAllPageControlEvents 退订 `ImagesChanged` → `PageControl_ImagesChanged`（`_isLoadingAnnotations` 抑制外 → MarkDirty）。
- **就地复制** `DuplicateSelection`（Task 13，行 3016）：Ctrl+D 触发，**不碰剪贴板**，直接克隆当前选区（同页偏移 +20,+20）——笔迹走活对象克隆（新 `StylusPointCollection` 逐点加偏移且**保留 PressureFactor** + `DrawingAttributes.Clone()`，经 `AddStrokeQuiet` 入页；比粘贴的 JSON 往返保真度更高）；文本容器复用 `CreateTextBox(select:false)`（与粘贴副本同一条创建路径，事件钩子/只读 chrome 完全一致，复制 Text/FontSize/Foreground/位置）；随后**单个** `ItemsAddedAction` 入栈（一次 Ctrl+Z 撤销整个副本，UndoAsync 先清选区防悬空引用——Task 8 已处理）→ 清其它页残留选区 → `SelectItems(克隆)` 副本自动全选 → `MarkDirty()` → Toast "Duplicated"。
- **文本框创建/尺寸** `CreateTextBox`：Grid 容器（chrome 边框 + TextBox + 移动/八方向缩放把手），旧注释 `Width/Height=0` 走自动尺寸，新/调整后的矩形保存正值；`TextAnnotationGeometry.ClampToPage` 将 live resize 限制在页面表面和最小 120×48 DIP 内。`select:true` 创建后推 `TextBoxAddedAction`；GotFocus→`BeginTextEditSession`，LostFocus/保存前→`CommitTextEditSession`；resize 的 mouse/stylus/keyboard 路径共用边界和 undo 语义，Esc 恢复起始矩形，卸载释放 capture。`EditorPage_PreviewKeyDown` 在文本框 nudge 分支前让 `TextResizeHandleBorder` 的方向键继续路由到 handle 自身，避免页面级预览事件抢先消费键盘缩放。
- **运行时页面/文本可访问性**：`LoadPdfAsync` 创建每个 `PdfPageControl` 后设置非可见稳定 AutomationId `PdfPageControl.{i}`，桌面 UIA smoke 可以获取真实页面控件 bounds，不必从 outer ScrollViewer 或内部位图 bounds 猜测页面坐标；`CreateTextBox` 的移动手柄设置稳定 `TextAnnotationDragHandle` AutomationId 和 `Editor.MoveTextBox` 本地化名称，跨页拖动 smoke 无需猜右侧手柄像素。
- **保存** `SaveAnnotationsToPdfAsync`：保存前提交活动文本编辑会话并收集 `Stroke/Highlight/Text/Image/HiddenInk` 等模型；先 await PDF 原子写入成功，再 await `VersionControlService.SaveVersionAsync`，成功后清 dirty，避免失败 PDF 留下误导性的历史快照。
- **自动保存** `AutoSaveAsync`：无路径直接 false；不再先用 coordinator clean state 短路，因为成功回调会在 task 完成前清 dirty，必须让 `SaveCurrentDocumentAsync` 加入该 completion window。Wave 2 uses `DocumentSaveCoordinator` plus the shared `_autoSaveInFlight` task/gate for both manual save and autosave, so timer re-entry/coincident manual saves coalesce. The core receives the captured dirty generation, commits text, saves PDF then version sidecar, and leaves the coordinator dirty when a new edit arrives during the operation; exceptions remain observable through the existing manual dialog/autosave toast paths.
- **关闭/导航保存协议**：`PrepareForNavigationAsync` 停止 timer、以 final-close admission 等待最新 generation，并在禁用页面输入后用异步 Dispatcher barrier 排空已排队的 WPF 输入；`PrepareForCloseAsync` 同样以 final-close 模式阻止 late edits，失败时恢复 timer/编辑状态。`ReleaseResourcesAsync` 只有在协议成功后才停钩子、释放 PdfService。MainWindow 的 CloseTab/NavBack/NavHome 和同步 `OnClosing` 都检查 false，不移除 tab 或提前退出。
- **Popup 焦点修复**（Task 10 起迁至 `Services/PopupZOrderHelper`，EditorPage 处仅委派调用）：`FixPopupTopmost(popup)` 在 Popup.Opened 时 `SetWindowPos(HWND_NOTOPMOST=-2)` 去掉 WPF 透明 Popup 的 topmost（Alt-Tab 后不再悬浮于其他应用之上），并 `SetWindowLong` 加 `WS_EX_NOACTIVATE`（0x08000000）防止 Popup 抢主窗口焦点导致工具栏"要点两下"。Task 10 覆盖：5 个工具 popup + colorPopup（InitializeTextBoxPopup 内创建处，行 4415-4416）+ PdfViewerContextMenu（XAML 命名，构造函数接入）与版本历史菜单（VersionHistory_Click 代码构建，IsOpen=true 前接入），后两者走 `FixContextMenuTopmost`（Opened 后 Dispatcher(Render) 一帧再取菜单 hwnd）。MainWindow Sort/More 菜单与 SettingsWindow LanguageComboBox 下拉见各自文件。
- **工具切换** `ToggleToolButton`（再次点击同工具→None）→ `ActivateTool`（同步各 ToggleButton.IsChecked 含 LaserToolButton、清选区/文本焦点）→ `ApplyToolToAllPages`（行 4164）：每页先同步五开关（`PressureEnabled`/`WholeStrokeEraser`/`InkSimulationEnabled`/`ShapeRecognitionEnabled`/`PenOnlyMode`，均来自 `AppSettingsService.Load()`，Sanitize bug 已修复、真实持久化）、`SetMode(Text)`、`SetPdfTextSelectionEnabled(None||TextHighlight)`、`SetSelectionMode(Select)`、`ShapeMode=(tool==Shape)`、按工具写 `DrawingAttributes`（Pen/Highlighter 颜色宽度、IsHighlighter，经 `SetInkAttributes` 应用 `IgnorePressure=!PressureEnabled`；Shape 分支写 `CurrentShape/ShapeColor/ShapeStrokeSize` + `SetInputMode(Shape)`；**Laser 分支（Task 20）仅 `SetInputMode(Laser)`**，笔迹生成逻辑在 PdfPageControl）。
- **工具弹窗** `CreateToolPopups`（行 2130）→ `BuildToolPopup`（行 3099，尺寸滑条+HSV 调色盘通用模板，Task 14 起带可选 `Func<List<string>> recentColors` 参数）：pen popup 追加 `AddPenBehaviourToggles`（「压感 Pressure」/「墨水模拟 Ink sim」/「形状识别 Shape recogn」三行 toggle，`BuildSettingToggleRow` 构建，#2563EB 高亮态，点击 → `SaveSetting` 持久化 + `ApplyToolToAllPages`）；eraser popup 顶部插入 `AddEraserModeSection`（「像素擦除」/「整笔擦除」互斥两钮，`BuildModeToggleButton`/`StyleModeToggleButton`，选中态 #2563EB 边框，选择持久化到 `AppSettings.WholeStrokeEraser` 并立即应用全页）；shape popup 复用 BuildToolPopup（尺寸 1-20 步进 0.5 + HSV 调色盘，**不传 recentColors**——形状颜色会话级）+ 顶部插入 `AddShapeSubTypeSection`（直线/矩形/椭圆/箭头 2×2 互斥钮，复用 BuildModeToggleButton/StyleModeToggleButton，会话级选择立即 ApplyToolToAllPages，不持久化）。`ShapeToolButton_Click` 走标准 ToggleToolButton(Shape, ShapeToolButton, _shapePopup)；`_shapePopup` 已纳入 CloseToolPopups/ShouldClosePopupOnPointerDown popup 数组/FixPopupTopmost/`IsImmediateDrawingToolActive`（popup 打开时点击页面关闭弹窗且事件穿透，与 Pen 行为一致）。
- **最近使用颜色**（Task 14）：pen/highlighter/文本三调色盘顶部各一「最近 Recent」行（16×16 圆角 swatch 横排 ≤8 个，tooltip=hex，空则整行隐藏）。机制：`BuildToolPopup` 的 recentColors 参数非空时在 colorHeader 与调色盘之间插入 recentSection，订阅 `popup.Opened → RefreshRecentColorsRow`（行 2459，每次打开重读设置重填——popup 内容 ctor 一次性构建，最近行是唯一动态区）；文本 colorPopup（InitializeTextBoxPopup，行 4654）同构，swatch 点击走与调色盘单元格**共享的局部函数 `ApplyTextColor`**（行 4851：应用选中 TextBox + TextStyleChangedAction undo + 记录 + 关弹窗；pen/highlighter 的 swatch 点击则直接调各自的 colorChanged 回调）。**记录链路**：三处选色应用点（pen/highlighter 的 colorChanged 回调、ApplyTextColor）各追加 `SaveSetting(s => RecordRecentColor(s.RecentXxxColors, c))`——`RecordRecentColor`（行 2442）只做纯列表变异（"#RRGGBB" 去重置顶 + 截到 `MaxRecentColors=8`），持久化由 SaveSetting 承担（Load 返回的是克隆，必须同一 settings 对象 Load→mutate→Save）；`TryParseRecentColor`（行 2506）解析 "#RRGGBB"（兼容手改的 "#AARRGGBB"）。点最近 swatch = 应用该色 = 重新记录置顶（自我维持 MRU 语义）。
- **加载** `LoadPdf`（行 2581）：取消上次加载（`_loadCts`/`_loadSessionId`），清空容器/undo 历史，`GetPageSizeInDips` 逐页建 `PdfPageControl`。

## Hidden Ink 学习工具（Task 49）

- `ToolType` 当前包含 `HiddenInk`；Hidden Ink 的撤销模型区分新增、单击移除和拖动擦除手势批量移除，批量手势由 `HiddenInksRemovedAction` 作为一个命令整体撤销/重做。
- 工具栏通过 `HiddenInkToolButton`/`HiddenInkToolButton_Click` 激活 `ToolType.HiddenInk`，随后 `ApplyToolToAllPages` 为每页设置新的中性灰 `#C7CDD4` 不透明遮罩、28 DIP 宽度和 `HiddenInkRevealState.DefaultRevealDurationMs`（3000ms），并切入 `CustomInkInputProcessingMode.HiddenInk`；已加载显式颜色不被覆盖。
- 页面抬笔后 `PdfPageControl` 发出 `HiddenInkCreated`，EditorPage 以 `HiddenInkAddedAction` 推入 undo 栈；擦除模式点击遮罩发出 `HiddenInkRemoved`，以 `HiddenInkRemovedAction` 推入 undo 栈。加载与 undo/redo 使用 quiet API，不会重复生成动作。
- `CollectAnnotations`/`LoadAnnotationsFromPdfServiceAsync` 分别保存和恢复 `PageAnnotation.HiddenInks`。点击 reveal 只折叠页面视觉并启动 3 秒计时器，不改变模型；保存时仍写入遮罩，重开后默认再次隐藏。

## Public API / 关键成员（表）
| 成员 | 行号 | 说明 |
|---|---|---|
| `CurrentPdfPath` | 47 | 当前 PDF 路径（只读属性） |
| `LoadPdfAsync(string)` | 2572 | 外部入口（标签打开文件） |
| `UpdateCurrentPdfPath(string)` | 905 | 另存后更新路径 |
| `AutoSaveAsync()` | 4539 | 自动保存，失败静默 |
| `ToolType`（private enum） | 29 | None/Pen/Highlighter/HiddenInk/Eraser/Shape/Laser/Text/Select/TextHighlight（HiddenInk=Task 49 学习遮罩） |
| `_undoStack` / `_redoStack` | 512-513 | List\<IUndoAction\> 命令栈 |
| `_textEditSessionTextBox` / `_textEditSessionOriginalText` | 57-58 | 文本编辑会话追踪（GotFocus 捕获/LostFocus 提交） |
| `GetPageByTextContainer(Grid)` | 4165 | 由文本容器反查所属 PdfPageControl（文本 undo action 落页用） |
| `FindPageAtContainerPoint(PdfPageControl, Point)` | 3638 | Task 9：共享页命中测试（点在源页坐标 → 经 PagesContainer 找包含它的页；间隙/文档外 → null）。选区跨页与 dragHandle 跨页共用 |
| `_draggedContainerPage` | 95 | dragHandle 拖动起始页（Down 捕获，Up 清空）——跨页转移前容器 Parent 仍是源页，但显式捕获更稳 |
| `_lastClickedPage` / `_lastClickedPoint` | 67-68 | 粘贴/上下文定位锚点；Task 8.1 起由 `PageControl_PreviewMouseDown`（AddHandler handledEventsToo:true 挂每页 UIElement.PreviewMouseDownEvent）在**任意工具任意页的左键点击**时更新（页相对坐标）；LoadPdf 换文档时重置为 null |
| `DuplicateSelection()` | 3016 | Task 13：Ctrl+D 就地复制选区（+20,+20，不碰剪贴板，单个 ItemsAddedAction + 副本自动全选） |
| `HiddenInkToolButton_Click` | Task 49 | 激活 Hidden Ink 工具并传播页面遮罩输入配置 |
| `PageControl_HiddenInkCreated` / `PageControl_HiddenInkRemoved` / `PageControl_HiddenInksRemoved` | Task 49 | 用户创建、单击移除或拖动手势批量移除遮罩时分别推入新增/移除 undo action；加载与 undo/redo 使用 quiet API |
| `CollectAnnotations()` / `LoadAnnotationsFromPdfServiceAsync()` | Task 49 | 收集/加载 `PageAnnotation.HiddenInks`；加载期抑制重复 dirty/undo |
| `RecordRecentColor(list, c)` | 2442 | Task 14：最近颜色纯变异（去重置顶 "#RRGGBB" + 截 8）；持久化由调用方 SaveSetting 承担 |
| `RefreshRecentColorsRow(section, row, getter, apply)` | 2459 | Task 14：popup.Opened 时重填最近色 swatch 行（空则隐藏） |
| `TryParseRecentColor(hex, out color)` | 2506 | Task 14：解析 "#RRGGBB"/"#AARRGGBB"（静态） |
| `MaxRecentColors` | 2434 | 每调色盘最近颜色上限（8） |
| `PasteClipboardImage()` | Task 19 | Ctrl+V 剪贴板位图粘贴（无图返回 false 回落 JSON 粘贴） |
| `EditorPage_PreviewDragOver` / `EditorPage_Drop` | Task 19 | 图片文件拖放（ctor 接线；落点页命中 + 多文件阶梯） |
| `_isLoadingAnnotations` | Task 19 | 装载期抑制 ImagesChanged→MarkDirty |
| `ToggleImmersiveMode()` / `ImmersiveModeButton_Click` | ~6070 | Task 16：切换沉浸模式（F11/ESC/工具栏按钮；隐藏并恢复工具栏、文档侧栏和 PDF 搜索面板的 Opacity/命中状态） |
| `PenOnlyButton_Click` / `UpdatePenOnlyButtonVisual` | ~4115 | Task 15：仅笔绘制 toggle（持久化 + 全页同步；图标选中态 #0078D4 tint） |
| `RulerToolButton_Click` / `SetRulerVisible` / `EnsureRulerVisual` / `GetRulerEdgeEndpoints` | #region Task 22 | 直尺 overlay 开关（非 ToolType，与当前工具正交；session-only）；尺视觉/交互/吸附几何全在此 region，`RulerLength=360`/`RulerHeight=56`/`RulerEndCapZone=14`/`RulerRotationSnapDegrees=15` 常量 |
| `_zoomLevel` / `_lastRenderedDpiScale` | 348-349 | 当前缩放与上次渲染 DPI 档 |
| `_pageControls` / `_pageTopOffsets` / `_pageHeights` | 354-356 | 页面控件与几何缓存（二分找可见页） |

## Dependencies
- `Controls/PdfPageControl`（页面交互层）、`Services/PdfService`、`Services/VersionControlService`、`Services/AppSettingsService`、`Services/LocalizationService`、`Services/DialogService`、`MainWindow`（Toast）。
- WPF Ink（Stroke/DrawingAttributes）、PdfiumViewer（别名 `PdfiumPdfDocument`）。

## Open Threads / Resume Context
- **Status:** complete for Wave 3 P2 automated scope; visual screenshots and foreground/device checks remain external.
- **Intent/result:** highlighter previews, dynamic popup UIA/keyboard, marker contrast, production smoke IDs, popup rebuild z-order/handler lifecycle, and high-contrast pen visuals are covered by source/runtime contracts and explicit-fixture smoke evidence without changing later-wave ownership.
- **Next steps:** keep Wave6 global transient teardown and later-wave sidebar/theme work out of this scope; perform visual/device/third-party checks only with their own evidence.
- **Constraints:** keep PenPresets JSON-only compatibility, single-frame editor architecture, existing PopupZOrderHelper contract, and no Wave 4+ sidebar/theme/transient redesign.
**Status:** complete for Wave 3 source/build/test scope — toolbar XAML/runtime popup construction and metadata are implemented. Visible preset slots, Fit Width/Fit Page and Ink Analysis unavailable entry points are removed while `AppSettings.PenPresets`, zoom core and supported selection actions remain. Semantic vector Paths, live `_highlighterColor` previews, localized ToolTip/Name/HelpText/AutomationId metadata, theme-token state colors, semantic Toggle peers, and focus/min-target styling are verified; explicit-fixture editor UIA smoke is green, while screenshots/device/foreground checks remain external.

## Recent i18n synchronization

- `EditorPage_Loaded` subscribes to `LocalizationService.LanguageChanged`; `EditorPage_Unloaded` removes the handler so inactive tab pages do not remain rooted by the static event.
- The handler calls `ApplyLocalization()`, which updates XAML labels, refreshes already-created text toolbars, resize-handle tooltips, thumbnails, bookmarks, outline nodes and Sticky Note controls, and rebuilds dynamic tool popups in the current culture. Loading a navigation-history page applies the same refresh once on re-entry.

原已知小瑕疵已修复（2026-08-18，随 Task 6）：`DetachAllPageControlEvents` 漏退订 `StrokesErased`（Task 1 遗漏）——已补 `pageControl.StrokesErased -= PageControl_StrokesErased;`，现与订阅侧（LoadPdf）完全对称。

2026-08-20 completion sync：Task 23-40 implementations and the new Hidden Ink/text-resize/i18n/theme additions are connected through `CollectAnnotations`, load, selection, movement, undo and save paths. Keep PDF text selection separate from self-rendered annotation layers.

2026-08-20 Hidden Ink EditorPage 接线完成：工具栏激活、全页传播、收集/加载、`HiddenInkCreated`/`HiddenInkRemoved`/`HiddenInksRemoved` 订阅和新增/单个/批量移除 undo action 均已存在；自动化 build/test 已通过，触笔行为、PDF 第三方查看器和保存重开 UI 回归仍是外部验收。

## Agent Decisions / Thoughts
- **2026-08-20:** External document imports insert before the current page. The PDF range is inclusive, so the import path must remap the persisted bookmark list by the exact inserted-page count and pass the before/after lists into `DocumentSnapshotAction`; restoring only PDF bytes would leave the sidecar indices stale after undo/redo.
- `DocumentSnapshotAction` 用整文档字节快照而非细粒度操作，是为页面增删这类难以增量 undo 的结构变更；代价是内存与 `LeavesDocumentDirty=false` 的语义（撤销快照操作后不算 dirty）。
- Task 12 决策：`ZoomAroundPoint` 偏移修正改为**同步双 `UpdateLayout` 夹 `ScrollTo*`**（新 scale 下先 layout 再算偏移、提交后再 layout），原 BeginInvoke(Render)「延迟一帧补偿」方案废弃——该一帧空窗正是跳帧根因（新缩放+旧偏移渲染一帧）。`ApplyCustomZoom` 内的 `ScheduleReRenderForZoom` 调用保留（250ms 防抖，无害）。位图替换闪烁不在渲染侧节流，而在 PdfPageControl 内两层交换修（详见其镜像 Task 12 决策）。相邻页预渲染选 `ApplicationIdle` 逐页链式而非批量并行：绝不与输入/渲染竞争，且任一新滚动事件（新 token）即整链作废。
- `ApplyToolToAllPages` 每次调用都重新读设置并全页应用——新增工具时在此 switch 扩展。
- Task 2/5 决策：橡皮模式/压感/墨水模拟三开关均走「popup 即时改 → `AppSettingsService.Save` → `ApplyToolToAllPages` 全页重应用」链路（与橡皮尺寸滑条同模式）；toggle UI 用 Border+TextBlock 自绘（#2563EB 激活态）而非系统 CheckBox，标签用中英双语裸字符串（其余 popup 标签走 LocalizationService，此两处按 spec 从简）。
- Task 3 决策：形状子类型/颜色/粗细**会话级不持久化**（spec 无要求，跳过 AppSettings）；子类型选择器复用 Task 2 的 BuildModeToggleButton/StyleModeToggleButton（2×2 布局）；工具栏图标用 Segoe MDL2 &#xE8A9;（E7A6 已被 Redo 占用）；箭头 = 2 Stroke 2 步 undo（详见 PdfPageControl.md 决策）。
- Task 4/Wave 1 决策：形状识别开关走 Task 2/5 同款链路（popup 即时改 → SaveSetting → ApplyToolToAllPages，持久化 `AppSettings.ShapeRecognition` 默认关）；识别成功后 `PdfPageControl` 以 session token 和 immutable snapshots 原位替换并发 `StrokeRecognized`，`StrokeReplacedAction` 只持有 token/index/snapshots；undo/redo 找不到 token 或 side 已被其它动作改变时安静 no-op，普通擦除仍可删除还原后的原笔迹；dirty 由既有 `InkMutated` 承担。
- **Wave 1 quality follow-up:** `StrokesErasedAction`, `ItemsRemovedAction`, `ItemsAddedAction` and `SelectionCrossPageMoveAction` must carry `StrokePlacement` records rather than reconstructing identity from a live stroke after removal. A shape action remains snapshot-only; placement records are only for ordinary live-stroke restoration and page ownership transfer.
- Task 9 决策：文本 dragHandle 跨页复用 `SelectionCrossPageMoveAction`（单容器 + 空笔迹列表），delta=拖动实际位移（end−start，源页坐标），adjust=−targetOriginInSource，undo 数学与选区路径同构（Undo→start，Redo→目标页视觉同位）。**关键前提**：`DragHandle_MouseMove` 原有 clamp（限制在源页 Canvas 内）必须去掉——否则容器中心永不出源页、跨页永不触发；选区跨页可行的原因正是 `MoveItemsDirectly` 无 clamp。副作用处理：拖动中容器可溢出源页（视觉上被相邻页覆盖，与选区拖动一致）；松手无目标页命中（页间隙/文档外）或命中源页自身时，**clamp 回源页边界**再推 TextBoxMovedAction（保底文本框永不丢页外——比 spec 的"留在当前位置"更稳，避免页外坐标被保存）。防御性选区清除用 `HasSelection && SelectedTextContainers.Contains(container)` 精确判断（Text 工具下本不该有选区，无条件 ClearSelection 会误清）。
- Task 13 决策：**活对象克隆而非剪贴板/JSON 往返**——`CopySelection→PasteSelection` 路径会丢 PressureFactor（StrokeAnnotation.Points 只存 X,Y），Ctrl+D 直接 `new StylusPoint(x+20, y+20, pt.PressureFactor)` + `DrawingAttributes.Clone()` 保真度更高；文本容器不新写克隆构造器，直接复用 `CreateTextBox(select:false)`（它本就带 text/fontSize/color/position 模板参数，与粘贴副本同路径，事件钩子逐字一致——任务建议的"提取 helper"已被现有参数化签名满足）。undo 单步性靠把全部克隆塞进一个 `ItemsAddedAction`；选区切换顺序与 PasteSelection 完全同构（先 push、再清他页、再 SelectItems）。
- Task 14 决策：**RecordRecentColor 只做纯列表变异、持久化由调用方 SaveSetting 承担**——`AppSettingsService.Load()` 返回克隆（Sanitize+CopyColorList 每次新列表），helper 内部无法凭 list 引用反查所属 settings 对象落盘；调用点统一 `SaveSetting(s => RecordRecentColor(s.RecentXxxColors, c))`（复用 Task 2 模式）。最近行刷新选 **popup.Opened 重填**（任务给的二选一）而非持有引用增量维护——popup 内容静态一次性构建，Opened 重读设置最简单且天然覆盖跨会话。swatch 点击 = 该 popup 调色盘单元格的同一 apply 回调（pen/highlighter 传 colorChanged 本身、文本传共享局部函数 ApplyTextColor），点击最近色会再次记录置顶（MRU 自洽）。shape popup 不接最近行（颜色会话级，与三个持久化列表语义不符）。
- Task 15 决策：**PenOnlyMode 走「toolbar 按钮（非 popup 内 toggle）+ ApplyToolToAllPages 全页同步」链路**——按钮与 popup 内 BuildSettingToggleRow 不同：它是模式开关而非笔工具附属设置，放工具栏 ZoomOut 前常驻可点；状态同步收口在 ApplyToolToAllPages（按钮 IsChecked + 图标 tint + 全页属性三合一），启动经 ctor ActivateTool(None) 首次同步，之后任何切换路径（点击/工具切换）都过同一收口，无分叉。设备判别不用任务原文的 Type!=Stylus 一刀切而用 IsTouchFinger（华为 pen-as-touch 兼容），详见 PdfPageControl.md 决策段。
- Task 16 决策：**工具栏隐藏用 Opacity=0 + IsHitTestVisible=false 而非 Visibility=Collapsed**——ToolbarBorder 是根 Grid 中的悬浮覆盖（非固定行高 row），两法都不重排页面，但 Opacity 方案零布局失效、零视觉树变更（ShouldClosePopupOnPointerDown/IsSourceInToolbar 等子树查询不受影响），且记录-恢复 Opacity/IsHitTestVisible 两属性即可保证反复进出无残留。**ESC 退沉浸不放 IsEditableTextInputFocused 守卫**（任务原文"ESC 恒先退沉浸"；现有 KeyDown 的 ESC→None 本就无文本框守卫，preview 拦截后冒泡分支被抑制）；F11 放守卫（任务原文"文本框聚焦时不触发"）。**不动键盘焦点**——若把焦点移走（ClearFocus/Focus(null)），键盘事件路由树变化可能令 Page 的 PreviewKeyDown 收不到键、F11 自锁；焦点留在原处（哪怕是不可见工具栏按钮）事件仍沿可视树隧道经过 Page，F11/ESC 必可达。**不做窗口无边框化**（MainWindow 自定义 chrome 保持；"画布占满窗口"由隐藏悬浮工具栏达成）。
- Task 20 决策（激光笔，EditorPage 侧）：**popup=null 走 ToggleToolButton 默认参数**（签名 `Popup popup = null` + 内部判空，激光无选项故不建弹窗）；`IsImmediateDrawingToolActive` 纳入 Laser——语义上激光也是即时绘制工具；`GetLocalizedToolName` 用裸字符串「激光笔 Laser」（沿 Task 3 Shape 先例，不入 LocalizationService）；tooltip 同款裸双语直接写 XAML。
- Task 21 决策（Shift 约束，EditorPage 侧零改动）：全部逻辑落 PdfPageControl（形状约束 helper + StrokeCollected 拉直），EditorPage 不感知。
- Task 22 决策（直尺，EditorPage 侧）：
  - **overlay toggle 而非 ToolType**（任务设计明示）：尺与当前工具正交（Pen+尺 ON 才有意义），若做成工具会强迫"选尺=退出画笔"；按钮点击直连 `SetRulerVisible`，绕开 ToggleToolButton/ActivateTool/ApplyToolToAllPages 全部管线——尺不切工具、不清选区、不碰页面输入模式。v1 无快捷键/ESC 绑定（button only），双击尺身关闭等交互不做。
  - **单尺共享 + viewport 锚定**（任务设计）：尺在 `RulerOverlayCanvas`（根 Grid 内、ScrollViewer 之上、ToolbarBorder 之下）——z 序保证尺悬浮于所有页面之上（跨页可用）且工具栏可点；**尺不随内容滚动/缩放移动**（像真实尺子摆在屏幕上，v1 有意为之），吸附 delegate 每次查询实时 TranslatePoint，坐标系恒正确。空 Canvas 无 Background → 命中透明，滚动/平移手势在尺外区域不受影响。
  - **代码构建视觉而非 XAML**：尺是一次性静态视觉（body/刻度/端帽/把手 40+ 子元素）且只在首次显示时创建（懒加载），XAML 声明会让 EditorPage.xaml 膨胀；交互全用鼠标事件（stylus/touch 提升为 mouse 自动覆盖，笔/手指拖尺可用——尺不产生墨迹，PenOnlyMode 的设备过滤不适用；GoodNotes 同款交互）。
  - **旋转交互双入口**（端帽拖拽 + 右键拖拽任意处）：端帽（14px 区）在陡峭角度下难点，右键拖拽是兜底；旋转=指针绕尺心 atan2 角度增量叠加起点角，**恒 15° 吸附**（v1 简单可预期，不做"按住 Shift 关吸附"类修饰）。右键拖拽期间鼠标被 capture，ScrollViewer 右键菜单自然不弹（可接受副作用——尺上右键单击无菜单）。
  - **中心 clamp 而非 bbox clamp**：`ClampRulerCenter` 只把尺心留在视口内——无论什么角度，尺心附近总有可抓的尺身，尺永不会拖丢；bbox clamp 需在每次旋转后重算（旋转后 bbox 尺寸变化），v1 不值。首次显示默认视口中心。
  - **`GetRulerEdgeEndpoints` 返回尺上边缘**（偏离任务原文的"过中心线"）：中线 snap 会把沿边画的线跳到 28px 外且 24px 容差 < 28px 半高互相矛盾——详见 PdfPageControl 镜像 Task 22 决策段。旋转 180° 后"上边缘"自动换到另一侧物理边，全方向可用。
  - **delegate per-page 注入（LoadPdf 建页处）而非静态查询**：闭包捕获具体 pageControl，`TranslatePoint` 每次调用实时换算 viewport→页坐标——滚动/缩放/移尺/转尺后无缓存过期问题；尺隐藏时返回 null，PdfPageControl 零成本跳过。换文档（LoadPdf 清空重建）时新页自动获得新 delegate。

## Important Notes / NEVER Change
- **NEVER**：undo 必须保持 IUndoAction 命令栈（结构性操作用 DocumentSnapshotAction 字节快照）。
- **NEVER**：注释坐标系 DIP 96dpi（保存换算见 PdfService）。
- **NEVER**：单窗口 Frame 标签架构（EditorPage 由 MainWindow 的 AppTab.Frame 承载）。
- `AutoSaveAsync` 静默失败是有意设计（自动保存不打扰用户），勿改为抛异常。
- `PopupZOrderHelper`（原 EditorPage.FixPopupTopmost）的 WS_EX_NOACTIVATE 是修"按钮要点两下"bug 的关键，勿删；HWND_NOTOPMOST(-2) 常量与 flags（NOSIZE|NOMOVE|NOACTIVATE）勿改。

## OpenNotes Completion Pass

- **Status:** ready_for_next
- i18n, Hidden Ink, resizable text boxes, theme/popup coverage and static website implementation are synchronized and pass automated checks.
- The current solution verification is 0 build errors with 2 documented NU1701 warnings and the latest full test count recorded in `.ai/PROJECT_CONTEXT.md`; see that file for complete command evidence.
- Popup coverage includes the editor popups, MainWindow menus, all four SettingsWindow ComboBoxes and the three dynamically created HomePage menus; desktop Alt-Tab behavior remains manual.
- Immersive mode currently hides/restores `ToolbarBorder`, `DocumentSidebar` and `PdfSearchPanel`; the older ruler note that mentioned only the toolbar is superseded.
 - Page bookmark persistence is wired for toggle/jump, structural page edits, and external PDF/image imports; the remaining checks are real WPF import and undo/redo interaction.
- Keep the single-window Frame-tab architecture and page coordinate conversions unchanged.

## Current Narrow Fix

- **Status:** complete for the current UI/theme pass
- **Intent:** Keep the stable UI Automation identity for each code-created text resize handle and preserve the real pointer-input smoke for creation, resize, Undo/Redo geometry and save/reopen persistence.
- **Notes:** The eight handles use `TextResizeHandleBorder`, a Border-compatible control with a `Thumb` UI Automation peer; this preserves the existing mouse/stylus handlers while making live handles discoverable. The interactive smoke passed tool switching, text-box creation, all eight handles, BottomRight drag and Undo/Redo. The toolbar now uses semantic Paper/Ink/Mark/Margin resources while preserving all commands and bindings. The code-built ruler root Grid retains the transparent hit-test background; the full-screen `RulerOverlayCanvas` intentionally remains without one so empty overlay space passes through to the ScrollViewer. Single-finger suppression is gated by `_applicationSettings?.PenOnlyMode == true`. The Codex migration is already complete and is outside this UI pass.

## Agent Decisions / Thoughts

- **2026-08-20:** Put the transparent brush on the interactive ruler Grid, not on the full-screen overlay Canvas. This makes the ruler body draggable while preserving scrolling and panning outside the ruler.
- **2026-08-20:** Gate finger suppression with `_applicationSettings?.PenOnlyMode == true`; when PenOnly is off, the existing input mode is allowed to receive single-finger input. Shape and Laser are included because they are immediate drawing tools, while Text/Select remain touch-interactive.
- **2026-08-20:** `PenOnlyButton_Click` now keeps the loaded settings snapshot after saving, so the new touch gate observes the button state immediately instead of being reset from a stale `_applicationSettings` object.
- **2026-08-20 correction:** The older ruler paragraph saying immersive mode only hides `ToolbarBorder` is superseded: current `ToggleImmersiveMode` also hides/restores `DocumentSidebar` and `PdfSearchPanel`; the ruler overlay remains independently interactive.
- **2026-08-20:** Text resize uses shared mouse/stylus handlers, page clamping, Escape cancellation and `TextBoxResizedAction`; save/autosave now commit text edits and await PDF-before-version ordering.
- **2026-08-23:** Wave 2 save gate decision: one `_autoSaveInFlight` task is the per-editor coalescing boundary shared by manual save and autosave; timer re-entry is ignored while a tick is already awaiting that task. A generation mismatch is a normal dirty result, not a second concurrent write or a success claim.
- **2026-08-23:** Wave 2 implementation: `SaveCurrentDocumentCoreAsync` captures the generation before PDF write, writes the version sidecar only after PDF success, and returns false/keeps dirty on concurrent edits; manual errors still use the dialog and autosave errors still use the toast.
- **2026-08-23 revision:** `DocumentSaveCoordinator` provides executable manual/autosave coalescing and latest-generation retry; `PrepareForCloseAsync`/`PrepareForNavigationAsync` are awaited by MainWindow before tab/content/resource transitions, and PdfService disposal joins the coordinated save path.
- **2026-08-23 final review:** `DocumentEditAdmission` blocks page input and commands during close/navigation, waits already-admitted edits to quiesce, and reopens on a failed/timeout release. `CommitTextEditSession` runs before `SaveAsync` captures a generation, while late WPF model notifications are retained as a dirty generation for a final retry. `ReleaseResourcesAsync` coalesces callers and only sets `_resourcesReleased` after every owner, including PdfService, succeeds; a failed release remains retryable.
- **2026-08-23 final review follow-up:** Sticky Note Popup sessions are committed and closed before the admission barrier and flushed again after the dispatcher barrier, so a queued activation cannot leave a detached editor interactive; whole-page `IsEnabled` blocks toolbar/routed commands alongside page input. Autosave joins an active task even when its completion has already cleared the dirty bit. Active-frame navigation calls `ResumeDocumentInteraction()`, which reopens both the edit admission and coordinator close state so the first edit after returning is persisted.
- **2026-08-23 structural reload follow-up:** Editor-owned `DocumentSnapshotAction` byte replacements acquire `PdfSaveCoordinator`; `PdfService.LoadPdfAsync` acquires the same path lease for the native reload, closing the direct snapshot/annotation-save race without changing the strip/rebuild pipeline.
- **2026-08-23 WPF retry follow-up:** `SaveCurrentDocumentCoreAsync` marshals `CollectAnnotations()` back to the editor Dispatcher when a generation-retry callback resumes on the thread pool; PDF and version I/O remain asynchronous, and the real close/navigation STA regressions persist late text safely.
- **2026-08-23 navigation re-entry:** a successfully prepared editor stays blocked while in the frame back stack; `ResumeDocumentInteraction()` reopens the admission and autosave timer only when `Frame_Navigated` makes that editor active again.
- **2026-08-23 destination/source-write follow-up:** draft Save-As copies now acquire sorted normalized leases for both the old/source and new/target path around directory creation and atomic `PdfAtomicFile.CopyFile`, so neither a source rewrite nor a destination replacement can race the copy.
- **2026-08-20:** Immersive mode now hides/restores the toolbar, document sidebar and search panel and is reachable from the localized toolbar button as well as F11/ESC.

## Change History
- 2026-08-24 Sticky editor UI repair: replaced the runtime flat action row with primary/secondary/destructive rounded buttons and added a Lucide grip header that moves only the transient editor popup. The persisted marker remains the page-bounded/undoable movement surface; popup dragging does not dirty the document or alter annotation DIP coordinates.

- **2026-08-20:** External PDF/image imports now capture and restore bookmark sidecar snapshots and remap subsequent page indices for the full inserted range.
- **2026-08-21:** Completed the scoped popup/ruler/PenOnly input fixes plus text resize, save ordering, immersive-surface synchronization, external-import bookmark snapshots and Hidden Ink input boundary; text boxes now expose keyboard nudge and focusable keyboard resize handles; added the isolated `OPENNOTES_DATA_ROOT` test seam, stable resize-handle AutomationIds and the live UIA peer contract; solution build has 0 errors with two known NU1701 warnings and tests pass 96/96 after the theme contract was added.

- **2026-08-21 runtime smoke fix:** The first isolated real-PDF editor launch reached `InstallScrollbarTrackJump` and exposed a WPF `RemoveHandler` delegate-type mismatch. The handler is now created as an explicit `MouseButtonEventHandler` for both removal and registration; the corrected real editor load and tool-control smoke now pass.
- **2026-08-21 pointer harness:** Text resize handles now expose localized names and stable `TextResizeHandle.{direction}` AutomationIds through `TextResizeHandleBorder`/`TextResizeHandleAutomationPeer`. The escalated isolated pointer smoke reached the real PDF editor, created a text box, discovered all eight handles, resized BottomRight from `508×168` to `628×240`, then passed Undo, Redo and a final Undo; it then changed the text, saved the temporary PDF, restarted OpenNotes and confirmed the text survived reopen. The script still labels the `WM_MOUSE*` fallback separately when a host cannot accept physical cursor input.

## V5 Completion Status

- Tasks 25-40 are connected to the existing page, undo and PDF pipeline: text markups, sticky notes, area highlights, rich text, sidebar navigation/search, page operations, PNG export, page insertion, settings and theme.
- Task 28 is intentionally degraded when InkAnalyzer is unavailable; the editor shows a visible bilingual fallback and preserves selected strokes.
- Search now checks cancellation before publishing result rows, and malformed/unreadable PDF outlines degrade to the page list instead of aborting document load.
- Thumbnail sidebar items are created as lightweight placeholders; realized/visible ListBoxItems request their true 0.22x bitmap asynchronously. A 24-entry LRU clears evicted item sources, so long documents retain neither every thumbnail nor full-DPI thumbnail backing stores.
  - Open threads: no required code implementation remains in this page; real isolated PDF editor loading, text resize/Undo/Redo pointer interaction and text save/reopen are verified, while drawing/eraser, cross-page movement, device/Edge visual checks, third-party viewers, live Pages and Codex AppData migration remain environment-dependent. The final suite has 96 passing tests.

- Hidden Ink 已达到代码级实现：遮罩 reveal 是临时视觉状态，保存/加载始终以隐藏状态重建；本文不把完整回归或真实设备验收标为通过。
- Wave 2 final-close admission now flushes both inline text sessions and the Sticky Note Popup before blocking input; the popup is closed explicitly because Popup content is outside the page `IsEnabled` subtree. `SetDocumentInteractionBlocked` disables the whole editor command/input subtree, and active-frame/window restore calls `ResumeDocumentInteraction` so a prepared navigation can be edited again.

- **2026-08-23 final timeout/atomic follow-up:** `DocumentReleaseState` remains non-resumable through a timed-out or partially failed release; MainWindow keeps tab/window workflow guards installed while a background release task settles, and only then removes the tab or requests `Close()`. Failed continuations cancel only unreleased prepared suffix editors, leaving the failed editor blocked for explicit retry. Snapshot bytes, print copies, and Save-As use same-directory temp/flush/atomic replacement; Save-As holds source+destination path leases.
- **2026-08-24 Wave5 review:** Home/Editor smooth scroll, loading, and PdfPageControl visual animation paths consume `ThemeService.GetAnimationDuration`/`ShouldAnimate`. The loading spinner is started/stopped in code so reduced motion disables it cleanly; no fixed-duration Editor storyboard remains. Runtime page chrome continues to use DynamicResource semantic aliases while annotation/data colors remain explicit.
- The review follow-up also routes the ruler body/ticks/center cue, text selection border/fill, resize-handle dots, and eraser preview through live accent/focus/surface/subtle resources. Text/document colors and annotation colors remain explicit data colors.
- **2026-08-23 Wave 2 transactional follow-up plan:** a failed multi-selection cross-page transfer must roll back every stroke already moved, preserve exact source/target identity and indexes, and expose unsuccessful initial/undo/redo results so callers leave the undo/redo stacks unchanged. Release preparation recovery must only re-enable interaction for a true pre-cleanup failure.

## Change History
- 2026-08-18: 建立镜像文档（Task 0，基于当日源码阅读，行号以当时文件为准）。
- 2026-08-18: Task 1 undo 系统补全——新增 StrokesErasedAction（订阅 PageControl.StrokesErased）+ TextBoxAdded/TextBoxDeleted/TextEditSession/TextStyleChanged/TextBoxMoved 五类文本 action；文本编辑会话追踪（BeginTextEditSession/CommitTextEditSession）；GetPageByTextContainer helper；UndoRedoButtonStyle 按钮启用/禁用配色（#1F1F1F/#B0B0B0）。
- 2026-08-18: Task 2+5——eraser popup 顶部新增像素/整笔模式互斥切换（AddEraserModeSection，持久化 AppSettings.WholeStrokeEraser）；pen popup 新增压感/墨水模拟 toggle 行（AddPenBehaviourToggles/BuildSettingToggleRow/SaveSetting）；ApplyToolToAllPages 改为一次性读设置并同步 PressureEnabled/WholeStrokeEraser/InkSimulationEnabled 三开关到全页。
- 2026-08-18: Task 3 形状工具——ToolType 增 Shape；工具栏 ShapeToolButton（E8A9 图标，橡皮与选择之间）；_shapePopup = BuildToolPopup（1-20 滑条 + HSV 调色盘）+ AddShapeSubTypeSection（直线/矩形/椭圆/箭头 2×2 互斥，会话级）；ShapeToolButton_Click/ActivateTool IsChecked 同步/CloseToolPopups/popup 数组/IsImmediateDrawingToolActive/ApplyToolToAllPages Shape 分支（ShapeMode+CurrentShape+ShapeColor+ShapeStrokeSize+SetInputMode(Shape) 全页传播）；GetLocalizedToolName 补 Shape（EditorPage.Utilities.cs，裸字符串"形状 Shape"）。
- 2026-08-18: Task 4 涂鸦形状识别——pen popup 第三个 toggle「形状识别 Shape recogn」（默认关，持久化 AppSettings.ShapeRecognition）；新增 StrokeReplacedAction（第 14 个 undo action）；LoadPdf 订阅/DetachAllPageControlEvents 退订 PageControl.StrokeRecognized + PageControl_StrokeRecognized 处理器；ApplyToolToAllPages 同步第四开关 ShapeRecognitionEnabled。识别算法与阈值决策详见 PdfPageControl.md。
- 2026-08-18: Task 6（EditorPage 侧仅一行）——DetachAllPageControlEvents 补 `pageControl.StrokesErased -= PageControl_StrokesErased;`（Task 1 遗漏的退订，修复删页/换文档后的潜在幽灵事件）。逐项动画描边本体全部落在 PdfPageControl（详见其镜像）。
- 2026-08-18: Task 8 粘贴定位与自动全选——新增 `PageControl_PreviewMouseDown`（AddHandler(UIElement.PreviewMouseDownEvent, handledEventsToo:true)，LoadPdf 订阅/DetachAllPageControlEvents 退订）：任意工具任意页左键点击均更新 _lastClickedPage/_lastClickedPoint（页相对坐标）；**handledEventsToo 必需**——Select 工具下 PdfScrollViewer_PreviewMouseDown 委托路径在祖先层 e.Handled=true，普通 += 隧道订阅收不到事件。TextOverlayPointerPressed/BackgroundPointerPressed 中的旧赋值删除（PreviewMouseDown 已全覆盖，后者保留 DeselectTextBox 逻辑）。PasteSelection：PushUndoAction(ItemsAddedAction) 后清其它页残留选区（镜像 Task 7 跨页规则）+ `targetPage.SelectItems(pastedStrokes, pastedContainers)` 自动全选（SelectItems 为 PdfPageControl 新 API，见其镜像）；粘贴项收集链路（AddStroke 返回 Stroke、CreateTextBox 返回 Grid）系 Task 1 已有。ItemsAddedAction.UndoAsync 移除项前 `if (_page.HasSelection) _page.ClearSelection();`（粘贴自动选中后 undo 不得悬空引用被移除项）。LoadPdf 清理区补 `_lastClickedPage = null;`（防换文档后粘贴到游离页控件）。
- 2026-08-18: Task 9 文本 dragHandle 跨页移动——`DragHandle_MouseMove` 去掉源页内 clamp（跨页拖动前提，与选区拖动自由度一致）；新增字段 `_draggedContainerPage`（Down 捕获/Up 清空）；`DragHandle_MouseLeftButtonUp` 按容器中心（left+w/2, top+h/2）经 `FindPageAtContainerPoint` 命中目标页：跨页 → `SelectionCrossPageMoveAction`（source/target/delta/adjust/空笔迹/单容器）+ ExecuteInitialTransfer + PushUndoAction，跳过 TextBoxMovedAction；同页/无命中 → clamp 回源页边界后走原 TextBoxMovedAction。`PageControl_SelectionMoveCompleted` 的命中循环提取为共享 helper `FindPageAtContainerPoint(source, centerInSource)`（行为逐字保留，选区跨页回归无变化）。防御：跨页转移前 `sourcePage.HasSelection && SelectedTextContainers.Contains(container)` 时 ClearSelection。构建 0 错误 + 15/15 测试通过。
- 2026-08-18: Task 10 弹窗跨应用悬浮修复——`FixPopupTopmost` 私有实现+3 个 user32 DllImport 从本文件迁出至新静态类 `Services/PopupZOrderHelper`（逻辑逐字保留），本文件改为委派调用并补全覆盖：ctor 中 5 个工具 popup + `PdfViewerContextMenu`（EditorPage.xaml 命名，`FixContextMenuTopmost`）；InitializeTextBoxPopup 的 colorPopup（行 4416，`StaysOpen=false` 弹层此前漏修）；VersionHistory_Click 代码构建的版本历史菜单（IsOpen=true 前接入）。PopupZOrderHelper 另含 `FixContextMenuTopmost`（Opened → Dispatcher(Render) 一帧 → PresentationSource.FromVisual(menu) 取 hwnd）与 `FixComboBoxPopupTopmost`（DropDownOpened → Render 一帧 → FindVisualChild\<Popup\> → popup.Child hwnd），MainWindow Sort/More 菜单与 SettingsWindow LanguageComboBox 分别接入。行为不变式：HWND_NOTOPMOST(-2) 只脱离 topmost 链（owned 窗口仍恒在 owner 主窗口之上），WS_EX_NOACTIVATE 不影响 StaysOpen=false 的失焦关闭（WPF 走 capture 丢失而非激活丢失）。构建 0 错误 + 15/15 测试通过；Alt-Tab 运行时回归待用户确认。
- 2026-08-18: Task 11 滚动条点击即达——**机制为 preview 事件拦截，未改 App.xaml 模板**（spec 的 least-invasive 选项）。新增 `InstallScrollbarTrackJump`/`ScrollBarTrackJump_MouseLeftButtonDown`（行 1301-1366）：EditorPage_Loaded 时经 `Template.FindName` 取两根 `PART_*ScrollBar`，AddHandler `PreviewMouseLeftButtonDown`（Remove+Add 幂等防 Loaded 重入）；handler 内 Thumb 子树点击跳过（原生拖拽），轨道点击按 `ratio=(clickPos−thumbLen/2)/(trackLen−thumbLen)`（垂直 Track IsDirectionReversed=True 需坐标翻转）直跳 `ScrollTo*Offset` 并 `e.Handled=true`（无动画无分页）；跳转前 `CancelSmoothScroll()`、跳转后 `SyncSmoothScrollState()` 保证滚轮动画基准同步。配套两处祖先放行（必要，否则拦截不到）：`PdfScrollViewer_PreviewMouseDown`（Select 选择委托页面 50px buffer 覆盖滚动条区域，会在祖先层 handle）与 `PdfScrollViewer_PreviewStylusDown`（None 工具 pen-scroll capture 会吞掉笔点击并抑制 mouse 提升）开头加 `IsOriginalSourceOverScrollbar`（新 static helper，FindAncestor\<ScrollBar\>）判断——副作用为正面修复：Select 工具下点页面边缘 50px 内 thumb 恢复可拖拽、笔点击轨道即跳转。构建 0 错误 + 15/15 测试通过。
- 2026-08-18: Task 12 缩放/滚动 frame jump 修复（三处根因）——**12.1** `ZoomAroundPoint`（行 1369）重写：删 `Dispatcher.BeginInvoke(Render)` 延迟回调，改为 `ApplyCustomZoom` → `UpdateLayout()` → 同步计算并 `ScrollTo*Offset` → `UpdateLayout()` → `SyncSmoothScrollState()`（偏移修正与新缩放同布局 pass，消除"新缩放+旧偏移"的一帧跳动；ScrollChanged 在 layout 中同步触发但只做防抖/基准同步，无偏移写入）；**12.3** `PdfScrollViewer_ScrollChanged` 末尾新增 `QueueAdjacentPagePrerender`/`ScheduleNextAdjacentPrerender`（行 1958/1983）：可见范围 ±1 未初始渲染页 → `ApplicationIdle` 优先级逐页链式 `RenderPageInitialAsync`（`_scrollReRenderCts` token 随新滚动事件取消整链；±1 窗口有界，无页数守卫）；**12.2** 落在 PdfPageControl（两层位图交换，见其镜像）。回归走查：pinch（WndProc 合成 Ctrl+Wheel + 触摸双指）、Ctrl+滚轮、缩放 ±钮/百分比框 6 入口共享 ZoomAroundPoint 均受益；JumpToPage、Task 11 滚动条跳转（ScrollTo*+SyncSmoothScrollState 直调）与 180ms 平滑滚轮动画（AnimateScroll/CompositionTarget_ScrollRendering）未触碰不受影响；SetZoom（Ctrl+0/±）无锚点行为不变。构建 0 错误 + 15/15 测试通过。
- 2026-08-18: Task 13 Ctrl+D 快速复制选区——`EditorPage_KeyDown` 新增 Key.D 分支（`IsEditableTextInputFocused()` 守卫：TextBox 不消费 Ctrl+D，编辑中显式跳过）；新增 `DuplicateSelection()`（行 3016）：活对象克隆（笔迹 StylusPoint 逐点 +20,+20 且保留 PressureFactor + DrawingAttributes.Clone，经 AddStrokeQuiet 入页；文本容器复用 CreateTextBox(select:false) 复制 Text/FontSize/Foreground/位置）→ 单个 ItemsAddedAction → 清他页残留选区 → SelectItems(克隆) 副本自动全选 → MarkDirty → Toast "Duplicated"。与 ItemsAddedAction.UndoAsync（Task 8 加的先清选区）交叉验证：副本被选中时 Ctrl+Z 一步撤销整个复制无悬空引用。构建 0 错误 + 15/15 测试通过。
- 2026-08-18: Task 14 最近使用颜色——AppSettings 新增 `RecentPenColors`/`RecentHighlighterColors`/`RecentTextColors` 三列表（hex "#RRGGBB"，上限 8，Sanitize/Clone 经新 `CopyColorList` null 兜底 ToList 透传，旧 settings.json 兼容）；EditorPage 新增 `MaxRecentColors=8`/`RecordRecentColor`（纯变异：去重置顶+截断）/`RefreshRecentColorsRow`（重填 swatch 行，空则隐藏）/`TryParseRecentColor`（解析 RRGGBB/AARRGGBB）；`BuildToolPopup` 增可选 `recentColors` 参数（pen/highlighter 传入，shape/eraser 不传），recentSection 插在 colorHeader 与调色盘之间，`popup.Opened` 时重填；InitializeTextBoxPopup 的 colorPopup 同构接入 + 调色盘单元格与最近 swatch 共享局部函数 `ApplyTextColor`（应用+TextStyleChangedAction undo+记录+关弹窗）；三处选色应用点各追加 `SaveSetting(s => RecordRecentColor(s.RecentXxxColors, c))` 持久化。构建 0 错误 + 15/15 测试通过；重启保留（14.3）经持久化链路成立，运行时确认待用户。
- 2026-08-18: Task 15 仅笔绘制（防误触）——AppSettings 新增 `PenOnlyMode`（默认 false，Sanitize/Clone 透传）；EditorPage.xaml 工具栏 ZoomOut 前新增 `PenOnlyButton`（ToolbarToggleButtonStyle + E7C9 笔图标 + 选中态 #0078D4 图标 tint，tooltip「仅笔绘制 Pen-only」）；`PenOnlyButton_Click`（SaveSetting 持久化 + ApplyToolToAllPages）；`ApplyToolToAllPages` 开头同步按钮 IsChecked/图标 tint、循环内第五开关 `page.PenOnlyMode = settings.PenOnlyMode`；PdfPageControl 侧阻断实现见其镜像。构建 0 错误 + 15/15 测试通过。
- 2026-08-18: Task 16 全屏沉浸模式——新增字段 `_isImmersiveMode`/`_preImmersiveToolbarOpacity`/`_preImmersiveToolbarHitTestVisible`（行 45-53）；`ToggleImmersiveMode()`（CloseToolPopups + ToolbarBorder Opacity/IsHitTestVisible 记录-隐藏/恢复，纯视觉无布局变化）；`EditorPage_PreviewKeyDown` 顶部新增 F11（IsEditableTextInputFocused 守卫）与 ESC-沉浸优先两分支（preview Handled 抑制既有 KeyDown 的 ESC→None，非沉浸时 ESC 行为不变）。决策（Opacity 而非 Collapsed、ESC 无守卫、不动键盘焦点、不做无边框化）见决策段。构建 0 错误 + 15/15 测试通过。
- 2026-08-18: Task 17 版本历史治理（EditorPage 侧）——`VersionHistory_Click` 恢复闭包（行 4334-4353）在 `LoadVersionAsync` 成功后、`ClearAllAnnotations()` 前插入恢复前快照：`CollectAnnotations()` + `await SaveVersionAsync(_currentPdfPath, current)`（行 4341-4343，await 保证快照先落盘再清空），恢复可逆——重开菜单（每次点击重新 GetVersions 重建）第一条即"恢复前"状态。上限/剪枝本体在 VersionControlService（见其镜像）。构建 0 错误 + 15/15 测试通过。
- 2026-08-18: Task 19 图片注释——Ctrl+V 分支改先 `PasteClipboardImage`（位图优先，GetImage + "PNG" 格式兜底 + PngBitmapEncoder）；ctor 接线 `PreviewDragOver`/`Drop`（FileDrop .png/.jpg/.jpeg，落点页 TranslatePoint 命中 + GetFirstVisiblePage 回落 + 多文件 +20 阶梯）；CopySelection/PasteSelection/DuplicateSelection/CollectAnnotations/LoadAnnotationsFromPdfServiceAsync 全部接入 Images（显式宽高复原、base64 原始字节、Format 嗅探）；`SelectionCrossPageMoveAction` 三处 TransferImageData（payload per-control 显式搬运）；`_isLoadingAnnotations` 装载期抑制脏标记；LoadPdf/Detach 订阅退订 ImagesChanged。构建 0 错误 + 21/21 测试通过。
- 2026-08-18: Task 20 激光笔 + Task 21 Shift 约束——EditorPage 侧：ToolType 增 `Laser`；EditorPage.xaml 工具栏 Shape 后新增 `LaserToolButton`（E790，#FF3B30 图标色）；`LaserToolButton_Click`（popup 省略=null）；`ActivateTool` IsChecked 同步 + `ApplyToolToAllPages` Laser 分支（仅 SetInputMode(Laser)）+ `IsImmediateDrawingToolActive` 含 Laser；GetLocalizedToolName 补 Laser（裸双语字符串）。激光层/渐隐/隔离与 Shift 约束全部落 PdfPageControl（见其镜像）。构建 0 错误 + 21/21 测试通过。
- 2026-08-18: Task 22 直尺工具——EditorPage.xaml 工具栏 Laser 后新增 `RulerToolButton`（E770，选中态 #0078D4 图标 tint）+ 根 Grid 新增 `RulerOverlayCanvas`（ScrollViewer 与 ToolbarBorder 之间，无 Background 命中透明）；EditorPage.xaml.cs 新增 ruler 字段组（`_rulerVisible/_rulerCenter/_rulerAngle/_rulerVisual/_rulerRotate` + 拖拽/旋转状态）与 `#region Task 22: on-screen ruler`（RulerToolButton_Click/SetRulerVisible/EnsureRulerVisual/Ruler 六个输入 handler/StartRulerManipulation/UpdateRulerPosition/ClampRulerCenter/SnapRulerAngle/GetRulerEdgeEndpoints + 4 常量）；LoadPdf 建页处注入 `pageControl.GetRulerEdgeInPageCoords` 闭包（查询时 TranslatePoint 实时换算）。吸附执行在 PdfPageControl（见其镜像）。构建 0 错误 + 21/21 测试通过。
- 2026-08-20: Hidden Ink（Task 49）——新增 Hidden Ink 工具、3 秒点击 reveal、擦除/undo/redo 接线，以及 sidecar 收集/加载；隐藏状态由 PdfService 负责写为带 `wna_hidden_` 前缀的 PDF `/Ink`。
- 2026-08-20: Hidden Ink 批量擦除接线——`HiddenInksRemoved` 事件按拖动手势聚合多个遮罩，EditorPage 以一个 `HiddenInksRemovedAction` 支持整体撤销/重做；清理/加载/undo/redo 使用 quiet API。
- 2026-08-21: `tools/Test-OpenNotesEditorSmoke.ps1` 预置真实 PDF 最近文件并经主页文件卡片打开，真实加载 `EditorPage`，UIA 暴露主要工具、保存和滚动控件；临时 sidecar 2 个并在 PASS 后清理。
- 2026-08-22: 编辑器动态控件主题收口：模式/设置行支持焦点与 Enter/Space，动态工具/选择/文本弹窗的根面、标题、分隔线、筛选选中态、文本颜色面板和字体/对齐 ComboBox 使用 `Theme*` 资源；Sticky Note popup 注册 `PopupZOrderHelper`，并保留八向文本框 resize UIA peer。构建 0 错误、95 个测试通过。
- 2026-08-22: 键盘缩放路由修复：`EditorPage_PreviewKeyDown` 对焦点/原始源为 `TextResizeHandleBorder` 的方向键提前放行，使八向句柄的 `KeyDown` 能执行尺寸变化，而不是被页面级文本框 nudge 分支抢先消费；新增源级回归合约。
- 2026-08-23: Wave 1 shape-recognition/settings compatibility remains green; Wave 3 removes the visible preset-slot fallback and leaves only the uncalled read-only legacy initializer symbols so `AppSettings.PenPresets` JSON round-trip tests remain intact. Focused toolbar/settings/theme 20/20, full suite 170/170; pointer/editor visual smoke remains external.
- 2026-08-23: Wave 1 quality follow-up——ordinary stroke undo actions now store `StrokePlacement` records and cross-page transfer tracks target ownership/index; real `StrokeReplacedAction` plus erase/delete/cross-page STA tests pass 5/5, shape/settings focused pass 13/13, full suite passes 113/113. Pointer smoke remains blocked by foreground ownership; no dedicated shape smoke exists.
- 2026-08-24: Editor chrome refresh replaces mixed glyph/one-off toolbar art with named font-independent Lucide vectors, adds state-aware Previous/Next navigation around the editable page field, and changes the sidebar selector to a three-column strip so labels cannot vertically collide.
- 2026-08-24: Bookmark toggle localization now selects between two literal catalog lookups, preserving live bookmark state text while satisfying strict i18n call-site validation.
- 2026-08-24: Toolbar/shape polish removes the shape checkmark, adds nine localized choices, normalizes icon weight/color and supplies the themed ToolTip path; focused editor coverage and the full 277-test suite pass.
