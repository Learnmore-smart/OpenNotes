# 笔记核心功能补全（V5）Spec

## Why
代码库调研确认：Caelum 缺少多数笔记应用的标配能力（形状/直线工具、整笔擦除、Ctrl 多选、选区逐项动画反馈、墨水模拟），且存在多处体验缺陷（缩放/滚动跳帧、滚动条点击迟缓、弹窗悬浮于其他应用之上、擦除与文本操作不可撤销）。本 spec 先搭建 File Guardian（.ai/）文档镜像体系，落实用户提出的 13 项缺口（批次一）；再对照主流笔记应用（GoodNotes / Notability / OneNote / Xodo / Drawboard）补齐调研新发现的 7 项高价值缺口（批次二）与 19 项深度调研缺口（批次三，含用户点名的激光笔），并列出远期路线图。

> 状态说明（2026-08-22）：上文“现状与根因”保留为 V5 实施前的历史基线；Tasks 0–40 的当前实现状态以 `docs/tasks.md`、`docs/checklist.md` 和对应镜像为准。本轮新增 Tasks 41–49，区分代码级实现、自动化验证、人工回归和外部系统工作；当前全量测试基线为 100/100，不以旧的 29/29 结果替代。正式 workspace、solution、project 和 test 项目已使用 OpenNotes；Caelum namespace、数据目录、Codex 兼容项目名和 AppX identity 是明确兼容例外。

## 调研结论（现状与根因）

### 第一部分：用户 13 项需求的现状
1. **橡皮擦**：仅像素切割式擦除（`ClipStrokeByErasers`），无整笔模式；且擦除不注册任何 undo。
2. **直线/形状**：完全不存在。工具栏无相关按钮，`ToolType` 枚举无相关项。
3. **Frame jump 根因**（按嫌疑排序）：
   - `ZoomAroundPoint`（EditorPage.xaml.cs:1086-1115）用 `Dispatcher.BeginInvoke(Render)` 延迟校正滚动偏移 → 缩放先应用、偏移后校正，产生一帧错位跳动；
   - `ScheduleReRenderForZoom`（:1500-1529）250ms 防抖后异步替换高分辨率位图 → 清晰度跳变闪烁；
   - `PdfScrollViewer_ScrollChanged`（:1535-1581）滚动中懒渲染新页（100ms 防抖后 swap 位图）→ 滚动中途闪动；
   - 缩放时 `CancelSmoothScroll` 硬截断进行中的滚动动画。
4. **图形功能**：无（同 2）。
5. **涂鸦识别**：无任何识别逻辑。
6. **粘贴定位**：`PasteSelection`（:2091-2235）已用 `_lastClickedPage/_lastClickedPoint` 定位（基本满足），但粘贴内容**不会自动全选**（文本 `select:false`，笔迹不选中）。
7. **弹窗跨应用悬浮**：`FixPopupTopmost`（:4740-4761）已修 4 个工具 popup，但文本颜色 `colorPopup`（:3858）漏修；各 ContextMenu / ComboBox popup 也未处理。
8. **滚动条**：WPF 默认 Track 点击 = 分页步进（thumb 缓慢逐级移动），非点击即达。
9. **墨水模拟**：无速度→笔宽逻辑；且 `AppSettings.EnablePressure` 与 `PdfPageControl.PressureEnabled` 是死代码（`IgnorePressure=false` 硬编码，设置无效）。
10. **选区渲染**：`UpdateSelectionVisuals`（PdfPageControl.xaml.cs:1182-1244）只有整体包围盒的静态虚线框，无逐项描边、无动画。
11. **Ctrl 多选**：不存在。每次框选前会 `ClearSelection()`，无法累加。
12. **跨页移动**：选区跨页移动已实现（`SelectionCrossPageMoveAction`，mouseup 后按中心点判定目标页迁移）；但**单个文本框的 dragHandle 拖动写死父 Canvas**（:4435），不能跨页，且不注册 undo。
13. **撤销系统缺口**：已注册：绘制/粘贴/删除选区/选区移动/缩放/跨页移动/页面增删。**未注册**：擦除（两种模式都缺）、文本添加、文本内容编辑、文本删除、文本 dragHandle 拖动、文本字号/颜色变更。按钮颜色：图标前景固定 `#555`，禁用态无明显变灰。

### 第二部分：对照主流笔记应用的差距（批次二，7 项）
14. **中文文本导出乱码（缺陷级）**：`PdfService.CreateStandardFontResources`（:1152-1167）硬编码 `/Helv`（Helvetica，WinAnsi），中文等 CJK 字符保存到 PDF 后乱码/丢失。
15. **无法插入图片**：无图片注释模型；剪贴板仅支持自有 JSON 文本格式，粘贴图片无反应；编辑器级拖放未实现（EditorPage `AllowDrop="True"` 是死设置，无任何 Drop 处理器）。
16. **无快速复制**：无 Ctrl+D 复制选中内容。
17. **无最近颜色**：三个调色盘均无最近使用颜色记忆。
18. **无仅笔模式（防误触 / Palm Rejection）**：InkCanvas 画笔模式下触摸/手掌同样产生笔迹。
19. **无全屏沉浸模式**：工具栏常驻，无法隐藏获得整屏画布。
20. **版本历史无限增长**：`VersionControlService` 无上限、无清理；恢复旧版本前不保存当前状态，恢复不可逆。

### 第三部分：深度调研差距（批次三，19 项）
21. **无激光笔**：用户点名——书写的墨迹约 1 秒后自动淡出消失，不入文档（演示讲解标配，GoodNotes/Notability 均有）。
22. **无直尺工具**：屏幕可移动/旋转直尺，笔沿边缘画直线（GoodNotes 标配）。
23. **无 Shift 直线约束**：画笔/高亮/形状拖拽时按住 Shift 不吸附 0°/45°/90°。
24. **无笔预设槽**：无法保存 2-3 组「类型+颜色+粗细」一键切换（GoodNotes/Notability 核心体验）。
25. **无笔迹平滑度设置**：`FitToCurve` 固定 true，手抖修正强度不可调。
26. **无 PDF 文本标记**：不支持对 PDF 原生文本的下划线/删除线/波浪线（PDF /Underline /StrikeOut /Squiggly 标准注释，Xodo/Drawboard 标配）。
27. **无便签注释（Sticky Note）**：无弹出式批注便签（PDF /Text 标准注释）。
28. **无区域高亮**：高亮只能跟随 PDF 原生文本选区，无法高亮任意矩形区域（图片/手写上也无高亮）。
29. **无手写转文字**：无 InkAnalyzer/OCR 集成（grep 零匹配）；GoodNotes/OneNote 核心功能。
30. **文本无富格式**：无粗体/斜体/字体族/对齐；`TextAnnotation` 模型无对应字段。
31. **无页面缩略图侧边栏**：无法缩略图导航/拖拽重排页/右键增删复制页。
32. **无大纲/书签**：不读 PDF Outline，无自定义页面书签（grep 零匹配；PdfSharpCore `PdfDocument.Outlines` 可用但未调用）。
33. **无 Ctrl+F 全文搜索**：无 PDF 内容搜索（Home 的 SearchBox 只是文件名过滤）。
34. **快捷键大量缺失**：无 PageUp/PageDown/Home/End 翻页、无 Ctrl+Tab 切换标签、无 Ctrl+A 全选注释。
35. **无适宽/适页缩放、无页面旋转**：只有 +/− 按钮与百分比输入；页面不能 90° 旋转。
36. **页面模板仅 4 种**（Blank/Notebook/Lined/Quadrille）：缺点阵、五线谱、康奈尔等常见模板。
37. **无图片导出**：不能导出页面为 PNG；`SimplePdfExporter.cs` 是从未被调用的死代码（应删除）；打印走 220DPI 位图路径（可用但不理想）。
38. **设置页形同虚设**：仅语言一项；`AppSettingsService.Sanitize/Clone`（:66-84）**丢弃 EnablePressure 字段（bug）**；自动保存间隔硬编码 60s 且失败静默；无主题设置。
39. **无深色模式**：应用与编辑器均无主题切换。

## What Changes

### 批次一：用户 13 项
- 搭建 File Guardian：创建 `.ai/` + `PROJECT_CONTEXT.md`，为本次涉及核心文件建立镜像文档（实现阶段第一项工作）。
- 橡皮擦新增「像素擦除 / 整笔擦除」模式切换（eraser popup 内 toggle UI）。
- 新增形状工具：直线、矩形、椭圆、箭头（工具栏新增按钮 + 子类型选择 + 拖拽预览）。
- 涂鸦自动识别：手绘笔画自动整形成直线/椭圆/矩形（pen popup 内开关，默认关）。
- 画笔新增「墨水模拟」开关（速度→笔宽），并接通现有「压感」设置（当前为死代码）。
- 修复缩放/滚动的 frame jump（同步校正缩放偏移、位图无闪烁替换、滚动预渲染）。
- 滚动条轨道点击即达：点击轨道后 thumb 立即跳至点击比例位置，无动画。
- 粘贴增强：粘贴位置=最后一次点击点（已有，纳入验证）；粘贴内容全部自动选中。
- 选区逐项动画描边（蚂蚁线 + 颜色流动），重叠内容可分辨选中/未选中。
- Ctrl+点击直接选中图形/文本并支持向已有选区累加。
- 文本 dragHandle 拖动接入跨页迁移机制（同选区跨页移动，含 undo）。
- 补全所有弹窗的跨应用悬浮修复（colorPopup、ContextMenu、ComboBox popup）。
- **撤销系统补全**：擦除（两种模式）、文本添加/编辑/删除/拖动/样式变更全部注册 undo；undo/redo 按钮启用=黑色、禁用=灰色。

### 批次二：调研新增 7 项
- Ctrl+D 快速复制选区（就地偏移粘贴 + 自动选中，复用批次一粘贴管线）。
- 三个调色盘新增「最近使用颜色」行（持久化）。
- 新增「仅笔绘制」开关（触摸/鼠标不产生笔迹，仅触笔绘制；防手掌误触）。
- 新增全屏沉浸模式（隐藏工具栏，快捷键/按钮切换）。
- 版本历史治理：数量上限自动清理 + 恢复前自动保存当前版本。
- **修复中文文本导出**：FreeText 注释外观流改用可嵌入 CJK 字体（如微软雅黑），中英文混排正确显示与回读。
- **图片注释**：粘贴剪贴板图片 / 拖入图片文件到页面；可选中/移动/缩放/删除/复制；保存 PDF 后重新打开可恢复。

### 批次三：深度调研新增 19 项
- **激光笔工具**（用户点名）：临时墨迹，书写后约 1s 自动淡出消失；不入文档、不置 dirty、不进 undo。
- **直尺工具**：屏幕直尺，可移动/旋转/吸附角度，笔沿边缘画直线。
- **Shift 直线约束**：画笔/高亮/形状按住 Shift 吸附 0°/45°/90°。
- **笔预设槽**：工具栏 3 个自定义笔位（类型+颜色+粗细一键切换，长按编辑）。
- **笔迹平滑度设置**：手抖修正强度可调（关/低/中/高）。
- **PDF 文本标记**：下划线 / 删除线 / 波浪线（作用于 PDF 原生文本，标准 PDF 注释，保存可回读）。
- **便签注释（Sticky Note）**：页面任意位置放置图标式便签，点击弹出编辑，保存可回读。
- **区域高亮**：任意矩形区域半透明高亮（不依赖 PDF 文本层）。
- **手写转文字**：圈选手写笔迹 → 一键转为可编辑文本框（WinRT InkAnalyzer 互操作；先 spike 验证可行性）。
- **文本富格式**：粗体/斜体/字体族（系统字体列表）/左中右对齐；模型与 PDF 导出同步扩展。
- **页面缩略图侧边栏**：缩略图导航 + 拖拽重排页 + 右键增删/复制页。
- **大纲/书签面板**：读取并展示 PDF Outline 点击跳转；支持添加/删除自定义页面书签。
- **Ctrl+F 全文搜索**：搜索框 + 全部命中列表 + 当前页高亮 + F3/Shift+F3 逐个跳转。
- **快捷键补全**：PageUp/PageDown/Home/End 翻页、Ctrl+Tab/Ctrl+Shift+Tab 切标签、Ctrl+A 全选当前页注释、Ctrl+F 搜索。
- **适宽/适页缩放按钮** + **页面 90° 旋转**。
- **页面模板扩展**：点阵（Dotted）、五线谱（Music）、康奈尔（Cornell）三种新模板。
- **导出 PNG**：当前页/全部页导出为图片；**删除死代码 `SimplePdfExporter.cs`**。
- **从 PDF/图片插入页面**：插入页对话框支持从其他 PDF 选页插入、插入图片为新页。
- **设置页扩充**：修复 EnablePressure 被 Sanitize 丢弃的 bug；新增自动保存间隔（秒）、默认笔参数、平滑度等设置；自动保存失败给出可见错误提示（不再静默吞异常）。
- **深色模式**：应用主题（浅/深）+ 编辑器背景色联动。

## Impact
- Affected specs: `overhaul-v4-ux-ui`（工具选择模型、橡皮擦、undo 行为在其基础上扩展，不推翻）。
- Affected code:
  - `Pages/EditorPage.xaml` / `EditorPage.xaml.cs`（工具栏、popup、undo 栈、缩放/滚动、粘贴、快捷键、全屏、仅笔模式、搜索、缩略图/大纲面板、拖放）
  - `Controls/PdfPageControl.xaml` / `PdfPageControl.xaml.cs`（橡皮、选区渲染、多选、形状绘制、跨页、图片层、激光笔层、直尺）
  - `Models/AnnotationModels.cs`（新增 ImageAnnotation、StickyNoteAnnotation、AreaHighlight、文本富格式字段）
  - `Models/AppSettings.cs` / `Services/AppSettingsService.cs`（大量新设置项 + Sanitize bug 修复）
  - `Models/PageInsertTemplate.cs` / `PageTemplatePickerWindow.*`（新模板）
  - `Services/PdfService.cs`（CJK 字体嵌入；图片/便签/文本标记/区域高亮注释的写入与读取；页面旋转、插入）
  - `Services/VersionControlService.cs`（上限清理、恢复前快照）
  - `SettingsWindow.*`（设置页扩充、主题）
  - `MainWindow.*`（Ctrl+Tab 标签切换、主题联动）
  - `App.xaml`（滚动条模板、按钮禁用样式、深色主题资源）
  - `Services/WindowsPenService.cs`（压感开关接通）
  - 删除 `SimplePdfExporter.cs`（死代码）
  - `.ai/`（新建）

## ADDED Requirements

## 批次一：用户需求

### Requirement: File Guardian 文档镜像
系统 SHALL 在实现开始前建立 `.ai/` 目录、`PROJECT_CONTEXT.md`，并为本次修改的核心文件创建镜像文档；每个任务完成后同步更新。

#### Scenario: 镜像文档就绪
- **WHEN** 实现阶段开始
- **THEN** `.ai/PROJECT_CONTEXT.md` 与核心文件镜像存在，含 Purpose、约束、Open Threads

### Requirement: 橡皮擦模式切换（像素 / 整笔）
橡皮擦 popup SHALL 提供两种模式的 toggle UI：「像素擦除」（现状）与「整笔擦除」（与擦除区域相交的笔触整体删除）。

#### Scenario: 切换到整笔擦除
- **WHEN** 用户选择「整笔擦除」并划过一条笔迹
- **THEN** 该笔迹整体移除，不相交笔迹不受影响，且可撤销/重做

#### Scenario: 像素擦除保持现状
- **WHEN** 模式为「像素擦除」
- **THEN** 行为与现版本一致（局部切割），且同样可撤销

### Requirement: 形状工具（直线 / 矩形 / 椭圆 / 箭头）
系统 SHALL 提供形状工具：工具栏新增「形状」toggle 按钮，popup 内选择子类型（直线、矩形、椭圆、箭头）与颜色/粗细；拖拽时显示实时预览，松开后生成对应形状。

#### Scenario: 拖拽绘制矩形
- **WHEN** 用户选择矩形子类型，在页面拖拽
- **THEN** 拖拽中显示虚线预览，松开后生成边角锐利的矩形，可选中/移动/缩放/复制，可撤销

#### Scenario: 形状持久化
- **WHEN** 保存 PDF 后重新打开
- **THEN** 直线/矩形/椭圆/箭头完整恢复（颜色、位置、大小不丢失）

### Requirement: 涂鸦形状自动识别
画笔 popup SHALL 提供「形状识别」开关（默认关闭）。开启后，手绘笔画在抬笔时若高置信度匹配直线/椭圆/矩形，自动整形为对应规则形状。

#### Scenario: 手绘直线被拉直
- **WHEN** 开关开启，用户徒手画大致直线并抬笔
- **THEN** 替换为起止点间直线，单步 undo 可恢复原始手绘

#### Scenario: 关闭时不干预
- **WHEN** 开关关闭
- **THEN** 手绘笔画保持原样

### Requirement: 墨水模拟开关
画笔 popup SHALL 提供「墨水模拟」toggle（默认关）与「压感」toggle（接通现有设置）。

#### Scenario: 开启墨水模拟
- **WHEN** 开启后书写
- **THEN** 抬笔后笔迹呈速度→粗细效果（慢粗快细）；关闭恢复均匀

#### Scenario: 关闭压感生效
- **WHEN** 关闭「压感」
- **THEN** 新笔迹不受压感影响（修复死代码，且设置真实持久化）

### Requirement: 选区逐项动画描边
选中内容（每条笔迹、每个文本框、每个形状、每张图片）SHALL 各自显示动态「蚂蚁线」描边（虚线流动、颜色循环）；整体包围盒与 4 角手柄保留。

#### Scenario: 重叠内容可分辨
- **WHEN** 两个图形重叠，圈选其中一个
- **THEN** 被选中者出现流动描边，未选中者无描边，可明确分辨

### Requirement: Ctrl+点击多选
选择工具下 Ctrl+点击 SHALL 直接选中图形/文本；已有选区时累加；点击已选中项移出选区。

#### Scenario: 向已有选区累加
- **WHEN** 已圈选两个图形，Ctrl+点击第三个文本框
- **THEN** 选区变为三项，可整体移动/缩放/复制

### Requirement: 粘贴自动全选
粘贴 SHALL 落在最后一次点击位置，且全部内容立即选中。

#### Scenario: 粘贴后直接拖动
- **WHEN** Ctrl+C 后点击另一点 Ctrl+V
- **THEN** 内容粘贴在点击点附近且自动选中，可直接拖动/缩放

### Requirement: 滚动条点击即达
点击滚动条轨道时 thumb SHALL 立即跳至点击比例位置（点击点成为 thumb 中心），无步进无动画。

#### Scenario: 点击轨道直达
- **WHEN** 点击垂直滚动条轨道某点
- **THEN** thumb 瞬间移动，内容立即显示对应位置

### Requirement: 弹窗不悬浮于其他应用
所有弹层（工具 popup、colorPopup、ContextMenu、ComboBox popup）SHALL 在 Alt-Tab 切换应用后不悬浮于其他应用窗口之上。

#### Scenario: 切换软件后弹窗不残留置顶
- **WHEN** 打开任意弹层后 Alt-Tab
- **THEN** 弹层不显示在其他应用之上

## 批次二：调研新增需求

### Requirement: Ctrl+D 快速复制
Ctrl+D SHALL 就地复制当前选区（右下偏移放置），副本自动全选；注册 undo。

#### Scenario: 复制并移动
- **WHEN** 选中图形按 Ctrl+D
- **THEN** 副本出现在原位右下并选中，一次 Ctrl+Z 撤销整个复制

### Requirement: 最近使用颜色
三个调色盘 SHALL 显示「最近使用」行（最多 8 色，持久化）。

#### Scenario: 快速复用颜色
- **WHEN** 用过某颜色后再打开调色盘
- **THEN** 该颜色出现在「最近」行，点击即应用

### Requirement: 仅笔绘制模式（防误触）
「仅笔绘制」开关开启后，触摸与鼠标 SHALL 不产生笔迹（触摸仅平移），仅触笔绘制。

#### Scenario: 手掌不误画
- **WHEN** 开启后手掌触碰屏幕
- **THEN** 不产生笔迹；触笔正常书写

### Requirement: 全屏沉浸模式
F11 或按钮 SHALL 切换全屏沉浸（隐藏工具栏，画布占满窗口），ESC/F11 退出。

#### Scenario: 进入沉浸书写
- **WHEN** 按 F11
- **THEN** 工具栏隐藏，书写/滚动/翻页仍可用；ESC 恢复无状态残留

### Requirement: 版本历史治理
版本数量 SHALL 有上限（默认 50，超出删最旧）；恢复前 SHALL 自动保存当前状态。

#### Scenario: 恢复可反悔
- **WHEN** 从历史恢复到旧版本
- **THEN** 恢复前状态已存为新版本，可再切回

### Requirement: 中文文本导出修复（CJK）
FreeText 注释 SHALL 使用嵌入 CJK 字体渲染；中文保存/回读/第三方查看器显示正确。

#### Scenario: 中文保存与回读
- **WHEN** 添加「你好 world」保存后重开
- **THEN** Caelum 与 Edge 中均正常显示，无乱码；再次编辑保存不破坏

### Requirement: 图片注释
系统 SHALL 支持图片注释：粘贴剪贴板图片 / 拖入图片文件；可选中/移动/缩放/删除/复制；保存 PDF 后恢复。

#### Scenario: 粘贴截图
- **WHEN** 截图后 Ctrl+V
- **THEN** 图片落在最后点击位置并自动选中；保存重开后完整恢复

#### Scenario: 拖入图片文件
- **WHEN** 把 PNG/JPG 拖到页面某处
- **THEN** 图片落点即松手位置，成为可编辑注释

## 批次三：深度调研新增需求

### Requirement: 激光笔工具
系统 SHALL 提供激光笔工具：书写的墨迹显示于独立临时层，约 1 秒后自动淡出消失；不写入文档、不置 dirty、不进 undo 栈、不保存。

#### Scenario: 演示时圈画重点
- **WHEN** 激光笔工具下书写
- **THEN** 墨迹呈现并约 1s 后淡出消失；保存的 PDF 与 undo 栈均不受影响

### Requirement: 直尺工具
系统 SHALL 提供屏幕直尺：可拖动位置、旋转角度（15° 步进吸附）、有刻度显示；笔尖贴附直尺边缘时沿边绘制直线。

#### Scenario: 沿直尺画线
- **WHEN** 放置直尺后沿其边缘书写
- **THEN** 生成笔直线条，方向与直尺一致；关闭直尺后恢复正常书写

### Requirement: Shift 直线约束
画笔/荧光笔/形状拖拽时按住 Shift SHALL 将方向吸附至 0°/45°/90°。

#### Scenario: 画水平线
- **WHEN** 画笔工具下按住 Shift 从左向右画
- **THEN** 生成严格水平直线

### Requirement: 笔预设槽
工具栏 SHALL 提供 3 个笔预设槽：每槽记忆一组「工具类型+颜色+粗细」，单击切换，长按（或右键）编辑并保存当前参数到该槽；跨会话持久化。

#### Scenario: 一键换笔
- **WHEN** 用户配置槽 1=红色细画笔、槽 2=黄色粗荧光笔后点击槽 2
- **THEN** 立即以黄色粗荧光笔状态书写；重启后槽位配置保留

### Requirement: 笔迹平滑度设置
系统 SHALL 提供笔迹平滑度（手抖修正）设置：关/低/中/高四档，影响新笔迹的曲线平滑强度，持久化。

#### Scenario: 手抖修正
- **WHEN** 平滑度设为「高」书写轻微抖动的线
- **THEN** 生成的笔迹明显平滑；设为「关」时保留原始轨迹

### Requirement: PDF 文本标记（下划线 / 删除线 / 波浪线）
系统 SHALL 支持对 PDF 原生文本的三种标记注释：下划线、删除线、波浪线；选中文本后从工具或右键应用；以标准 PDF 注释（/Underline /StrikeOut /Squiggly）保存，重载与第三方查看器可见。

#### Scenario: 给原文加删除线
- **WHEN** 选择工具选中一段 PDF 文本，应用「删除线」
- **THEN** 该段文本显示删除线；保存重开及 Edge 中均保留；可选中删除（含 undo）

### Requirement: 便签注释（Sticky Note）
系统 SHALL 支持便签：页面任意位置点击放置便签图标，点击图标弹出可编辑文本气泡；以标准 PDF /Text 注释保存，重载与第三方可见；可移动/删除（含 undo）。

#### Scenario: 添加批注便签
- **WHEN** 便签工具下点击页面某处并输入文字
- **THEN** 出现便签图标，点击可重新编辑；保存重开后便签与内容完整恢复

### Requirement: 区域高亮
系统 SHALL 支持矩形区域高亮：拖拽绘制半透明色块，不依赖 PDF 文本层；颜色/透明度可调；可选中/移动/删除（含 undo）；保存后恢复。

#### Scenario: 高亮图片区域
- **WHEN** 对页面上一张图片区域拖拽高亮
- **THEN** 该区域覆盖半透明高亮色，保存重开后保留

### Requirement: 手写转文字
系统 SHALL 支持圈选手写笔迹后一键转为可编辑文本框（替换原笔迹，单步 undo 可恢复）。技术路径为 WinRT InkAnalyzer 互操作；若 spike 验证不可行，降级为「转换失败 Toast 提示」并在 spec 记录。

#### Scenario: 手写变文本
- **WHEN** 圈选一段手写字点击「转为文字」
- **THEN** 原笔迹被识别结果文本框替换；Ctrl+Z 恢复手写原迹

### Requirement: 文本富格式
文本框 SHALL 支持粗体、斜体、字体族（系统字体下拉）、左/中/右对齐；格式随文档保存（模型与 PDF 导出扩展），重载恢复。

#### Scenario: 设置粗体与字体
- **WHEN** 选中文本设为粗体+楷体并保存重开
- **THEN** 文本以粗体楷体恢复显示

### Requirement: 页面缩略图侧边栏
系统 SHALL 提供缩略图侧边栏：显示各页缩略图；点击跳转；拖拽重排页序（含 undo）；右键菜单增删/复制页；当前页高亮。

#### Scenario: 拖拽重排
- **WHEN** 把第 3 页缩略图拖到第 1 位
- **THEN** 页序更新（文档与滚动视图同步），可 Ctrl+Z 撤销

### Requirement: 大纲 / 书签面板
系统 SHALL 读取并展示 PDF Outline（目录树，点击跳转）；SHALL 支持添加/删除自定义页面书签（星标当前页 + 书签列表快速跳转），书签存于本地（不写 PDF）。

#### Scenario: 大纲跳转
- **WHEN** 打开含目录的 PDF 并点击某章节
- **THEN** 视图滚动至对应页

#### Scenario: 收藏页面
- **WHEN** 按 Ctrl+M 或点按钮收藏当前页
- **THEN** 书签列表出现该页，点击直达；重启后保留（按文件路径记忆）

### Requirement: Ctrl+F 全文搜索
Ctrl+F SHALL 打开搜索框：搜索 PDF 文本，列出全部命中（页码+上下文片段），当前页命中高亮，Enter/F3 下一个、Shift+F3 上一个，点击列表项跳转。

#### Scenario: 搜索并跳转
- **WHEN** 搜索关键词后点击第 3 条结果
- **THEN** 跳至对应页并高亮该命中区域

### Requirement: 快捷键补全
系统 SHALL 支持：PageUp/PageDown 上/下翻页、Home/End 首/末页、Ctrl+Tab / Ctrl+Shift+Tab 切换标签页、Ctrl+A 全选当前页全部注释、Ctrl+F 搜索。

#### Scenario: 翻页与切标签
- **WHEN** 按 PageDown 后再按 Ctrl+Tab
- **THEN** 视图跳到下一页；随后切换到下一个标签页

### Requirement: 适宽/适页缩放与页面旋转
工具栏 SHALL 提供「适宽」「适页」缩放按钮；SHALL 支持单页 90° 旋转（视图+保存，含 undo），重载恢复。

#### Scenario: 一键适宽
- **WHEN** 点击「适宽」
- **THEN** 页面宽度贴合视口宽度（留边距），无需手动调百分比

#### Scenario: 旋转页面
- **WHEN** 右键页面选「旋转 90°」
- **THEN** 页面横竖转换，注释坐标同步旋转，保存重开后保持

### Requirement: 页面模板扩展
页面模板 SHALL 新增：点阵（Dotted）、五线谱（Music）、康奈尔（Cornell）；矢量绘制写入 PDF，模板选择器与插入页对话框同步提供。

#### Scenario: 插入点阵页
- **WHEN** 插入页时选择 Dotted
- **THEN** 新页为点阵底纹，保存重开后保留

### Requirement: 导出 PNG 与死代码清理
系统 SHALL 支持导出当前页/全部页为 PNG（透明背景可选）；SHALL 删除从未被调用的死代码 `SimplePdfExporter.cs`。

#### Scenario: 导出当前页
- **WHEN** 菜单选择「导出本页为 PNG」并选保存位置
- **THEN** 生成含全部注释的页面图片，清晰度可选（1x/2x）

### Requirement: 从 PDF / 图片插入页面
插入页对话框 SHALL 支持从其他 PDF 文件选择页码范围插入，以及选择本地图片插入为新页（图片适配页面大小居中）。

#### Scenario: 合并另一 PDF 的页
- **WHEN** 选择 another.pdf 的第 2-3 页插入当前位置
- **THEN** 两页连同其原有内容插入文档，可撤销

### Requirement: 设置页扩充
设置页 SHALL 提供：语言（现有）、自动保存间隔（15/30/60/120s）、压感（修复 Sanitize 丢弃字段的 bug）、笔迹平滑度、默认笔颜色/粗细、主题（浅/深）；自动保存失败 SHALL 显示可见错误提示（不再静默吞异常）。

#### Scenario: 修改自动保存间隔
- **WHEN** 把间隔从 60s 改为 30s 并重启
- **THEN** 设置生效并持久化；保存失败时出现错误 Toast

### Requirement: 深色模式
系统 SHALL 支持浅色/深色主题切换：应用 chrome（标题栏/工具栏/设置/首页）与编辑器背景联动；PDF 页面本身不变色（可选未来加反色滤镜）。

#### Scenario: 切换深色
- **WHEN** 设置中切到深色主题
- **THEN** 全应用 UI 立即切换为深色，重启保持；编辑器背景同步加深

## 后续收口规格（Tasks 41–49）

### Requirement: 全量 i18n 完整性（Task 41）
所有面向用户的动态文字、XAML 文本、工具提示、菜单项、设置项、错误/成功消息和 landing page copy SHALL 有 English、简体中文、Français 三语来源；格式化占位符 SHALL 在三语之间保持一致。语言切换 SHALL 刷新已打开的窗口、页面、弹层和菜单；缺失 key SHALL 在验证阶段失败，而不是静默显示 key。

#### 当前状态
- `LocalizationService` 已有三语 catalog、占位符测试、`LanguageChanged` 事件和缺 key 失败机制。
- 当前 checkout 已完成 catalog、调用 key、占位符和硬编码可见字符串审计：273 条 catalog、386 个调用、0 个硬编码可见字符串；语言切换事件和缺 key 测试通过。真实窗口的逐项视觉刷新仍属于桌面人工回归范围。

### Requirement: OpenNotes 品牌迁移（Task 42）
用户可见的产品名、标题、产品元数据、AppX DisplayName、安装器显示名和公开文案 SHALL 使用 `OpenNotes`；正式 workspace、solution、project、程序集和测试项目 SHALL 使用 OpenNotes；迁移 SHALL 保留既有兼容身份：GitHub 仓库/Pages URL、C# namespace `Caelum`、`%LOCALAPPDATA%\Caelum` 数据目录、文件格式标识和 `WindowsNotesApp` AppX identity 不得改名或搬迁。

#### 当前状态
- 可见应用/安装器、`ProductInfo`、README、公开 logo/资源、发布配置和官网已完成 OpenNotes 审计；正式 checkout、solution/project/test 文件已使用 OpenNotes。
- `Caelum` namespace、数据目录和 `WindowsNotesApp` identity 保持兼容。

### Requirement: 可调整大小文本框（Task 43）
文本注释 SHALL 支持八向缩放手柄、最小宽高约束、长文本自动换行，以及宽高随注释保存/加载；一次缩放手势 SHALL 形成一个可撤销/重做动作，并不得破坏既有位置、样式或跨页移动。

#### 当前状态
当前 checkout 已有 `TextAnnotationGeometry`、八个手柄、最小尺寸、宽高持久化和 `TextBoxResizedAction`；鼠标/触笔和保存重开等交互由 Task 48 验收。

### Requirement: 独立 WPF 主题系统（Task 44）
主题系统 SHALL 由独立 `ThemeService` 管理 Light/Dark 资源；启动、设置预览、保存和重启 SHALL 保持一致；主窗口、首页、编辑器背景和设置页 SHALL 使用动态资源；PDF 页面位图 SHALL 不被主题染色。

#### 当前状态
当前 checkout 已有运行时主题资源与 `ThemeService` 接线，并覆盖 Light/Dark/System/HighContrast 归一化；Desk/Paper/PaperAlt/Ink/Margin/Mark 六个材料 token 已用于主窗口、首页、编辑器工具栏、设置与模板选择器。自动化测试覆盖资源选择、主要表面契约与持久化；UIA 保存重启通过，对比度、弹层和选中态仍需 Task 48 像素级桌面视觉验收。

### Requirement: GitHub Pages landing page（Task 45）
仓库 SHALL 提供项目页路径安全的静态 landing page，使用 OpenNotes 品牌、截图/演示、相对资源路径和三语 copy；页面 SHALL 包含响应式布局、键盘焦点、减少动画/高对比度支持和 404 fallback，且 demo 不得访问桌面 AppData。

#### 当前状态
当前 checkout 已围绕 “Open a PDF. Leave a trace.” 与首屏 live folio 完整重构，保留六个可替换占位资源，并通过 `tools/check-website.ps1` 与本地 Playwright 验收：覆盖 375/768/1440px 无横向溢出、三语各 116 个 key、绘制/撤销、文本移动、八向缩放、指针/键盘焦点、主题/语言切换、reduced-motion、404 和相对资源。workflow run `32446996825` 证明既有发布链路可用，线上 Pages 首页与 `/404.html` 返回 HTTP 200；本轮重设计将在下次 main 推送后自动发布。

### Requirement: GitHub Pages 自动部署（Task 46）
仓库 SHALL 提供最小权限的 GitHub Actions workflow，将 `website/` 构建并部署到项目 Pages；部署 SHALL 保留现有 release workflow，并验证项目路径下的相对资源、404、缓存和回滚说明。

#### 当前状态
 当前 checkout 的 `.github/workflows/pages.yml` 已通过本地静态结构检查，包含 Pages artifact/deploy job、main/path 触发、最小权限和 `website/` 相对来源；workflow run `32446996825` 的 build/deploy、线上 404、缓存和项目路径已验收。

### Requirement: Codex 项目与会话修复（Task 47）
Codex 项目记录 SHALL 指向当前正式 checkout，相关会话历史正文 SHALL 保持不变；任何 AppData 修复 SHALL 在 Codex 关闭后进行，先备份、可回滚，并继续使用 Caelum 数据目录兼容约束。仓库文档不得把未执行的外部修复写成已完成。

#### 当前状态
当前正式 checkout 是 `D:\Noah\文档\Coding\1. Open-Source\OpenNotes`；安全迁移 fixture 已覆盖两条历史 Caelum 路径、正文不变、边界/前缀变体、多行 Antigravity 数据和失败回滚。真实迁移已完成：当前只读核对确认 canonical project `fc720e52-224f-4685-b49e-cf409a93714a` 的唯一根目录为 OpenNotes，旧项目根为 0；迁移日志报告 82 条主库线程、35 条 catalog 线程已重新关联，并保留备份/manifest 供回滚审计。

### Requirement: 全功能回归验证（Task 48）
在 Tasks 41–49 收口后， SHALL 重新执行 solution build、完整 `OpenNotes.Tests`、核心编辑流程回归、语言/主题回归，并对触控笔、弹窗跨应用、Edge/第三方 PDF 和 Pages 部署等环境项分别记录证据或限制。最终文档 SHALL 让 0–49 的任务、清单和规格状态一致。

#### 当前状态
Task 48 的自动化与启动烟雾部分已执行：`OpenNotes.sln` build 0 errors，`OpenNotes.Tests` 100/100；i18n、website 静态/浏览器验证也通过。在刻意缺失 `WINDIR`、保留有效 `SystemRoot` 的宿主环境中，构建产物真实创建了可见的 1280×720 `OpenNotes` 主窗口；`WindowsEnvironment` 以进程级兜底规避 WPF 字体初始化失败。`tools/Test-OpenNotesUiAutomation.ps1` 又在临时 `OPENNOTES_DATA_ROOT` 下真实打开 More/设置，验证 Français 与深色主题预览的实时刷新，并通过取消关闭；其 `-SaveAndReopen` 变体进一步真实保存设置、重启进程，并在重新打开设置窗口后确认 Français 与深色主题仍被选中；`tools/Test-OpenNotesEditorSmoke.ps1` 进一步在隔离最近文件 sidecar 中打开真实 PDF 文件卡片，确认 `EditorPage` 加载、主要工具/保存/滚动控件暴露并通过 UIA `TogglePattern` 触发九个工具，随后清理 2 个 sidecar 文件；在获准的交互桌面上，`tools/Test-OpenNotesPointerSmoke.ps1` 已真实点击 Pen/Text/Eraser 和 PDF 页面，笔划保存后 PDF `/Ink` 从 0 增至 1，Whole-Stroke Eraser 保存后回到 0，再创建文本框，发现八个 UIA 手柄，执行 BottomRight 拖拽（`508×168` → `628×240`）以及 Undo/Redo/再次 Undo，输入文本后保存 PDF、重启 OpenNotes 并确认文本值在最近文件重开后保留；新增 `tools/Test-OpenNotesHiddenInkSmoke.ps1` 已真实验证鼠标遮罩、揭示/计时恢复、擦除、Undo、保存标记和重开后的再次计时；`tools/Test-OpenNotesThirdPartyViewerSmoke.ps1` 已用 Poppler 与 Edge headless 对独立 PDF 验证页数、PNG 渲染、截图和输入哈希不变。触控笔、跨页移动、跨应用弹窗、主题视觉和刚由 OpenNotes 保存的 Hidden Ink PDF 的第三方视觉仍需人工或外部环境验证。

### Requirement: Hidden Ink 学习遮罩（Task 49）
系统 SHALL 提供独立的 Hidden Ink 学习工具：用户可用笔或鼠标自由手绘不透明纸张色遮罩覆盖关键词；点击单个遮罩 SHALL 仅临时揭示其覆盖内容，默认 3 秒后自动重新遮挡。遮罩 SHALL 与普通笔迹分离，普通选择/擦除不得意外修改它；Eraser 模式点击遮罩 SHALL 删除它，并支持 undo/redo。

Hidden Ink SHALL 保留稳定 ID、颜色/alpha、宽度、reveal 时长和 DIP 点列。sidecar SHALL 直接保存 `PageAnnotation.HiddenInks`；PDF 导出 SHALL 写出不透明 `/Ink`，使用 `wna_hidden_` `/NM` 前缀，剥离式加载 SHALL 按该前缀恢复到 HiddenInks 而不是普通 Strokes。reveal 状态是临时 UI 状态，保存、导出和重开 SHALL 仍以隐藏状态为准。

#### Scenario: 点击遮罩短暂显示
- **WHEN** 用户在 Hidden Ink 工具下画过遮罩，并点击该遮罩
- **THEN** 只有被点击的遮罩暂时消失，覆盖内容立即可见；默认 3 秒计时结束后遮罩重新出现

#### Scenario: Eraser 移除遮罩并可撤销
- **WHEN** 用户切换 Eraser 后点击 Hidden Ink 遮罩
- **THEN** 该遮罩被移除，Ctrl+Z 恢复、Ctrl+Y 再移除，其他普通笔迹不受影响

#### Scenario: 保存后仍保持隐藏
- **WHEN** 用户在 reveal 窗口内保存 PDF 或 sidecar，并重新打开
- **THEN** Hidden Ink 仍以不透明遮罩存在；PDF 中的 `wna_hidden_` 注释被识别并恢复到独立 HiddenInks 集合

#### 当前状态
- 当前 checkout 已有 `HiddenInkAnnotation`/`PageAnnotation.HiddenInks`、Hidden Ink 工具、3 秒 `DispatcherTimer` reveal、Eraser 移除、专用 undo actions，以及 sidecar/PDF `wna_hidden_` 持久化接线。
- 代码级实现证据已在源码、`OpenNotes.Tests/HiddenInkTests.cs` 和 `PdfServiceAnnotationSavingTests.cs` 中出现；自动化 build/test 已通过，覆盖 opaque mask、reveal 时长、foreign `/Ink` 保留和 PDF 往返。鼠标/触笔真实交互、保存重开 UI、第三方 PDF 查看器和全功能桌面回归仍待人工验证。

## 兼容性不变量（适用于 Tasks 41–49）
- 正式 workspace、solution、project、程序集和 test 项目名称为 `OpenNotes`；C# namespace、文件格式兼容标识和历史数据目录继续为 `Caelum`。
- 现有 `%LOCALAPPDATA%\Caelum` 数据文件布局继续可读写。
- AppX identity 继续为 `WindowsNotesApp`；OpenNotes 仅为可见产品品牌迁移。
- 本轮已完成的实现同时涉及生产代码、测试、官网、项目文件、工具脚本和真实 Codex/Antigravity 元数据迁移；会话正文、认证信息、日志和附件不在迁移范围内，旧历史文件夹在迁移前已不存在。

## MODIFIED Requirements

### Requirement: 撤销/重做系统注册全部动作
在现有命令栈基础上扩展：擦除（两种模式）、文本框添加、内容编辑（会话合并单步）、删除、dragHandle 拖动（同页+跨页）、字号/颜色/富格式变更、图片注释操作、页面重排/旋转、便签/区域高亮/文本标记的增删改，全部 SHALL 注册为可撤销/重做动作。

#### Scenario: 擦除可撤销
- **WHEN** 擦除后按 Ctrl+Z
- **THEN** 笔迹恢复原状；Ctrl+Y 重新擦除

#### Scenario: 文本编辑可撤销
- **WHEN** 文本框内输入多字后点击空白处，再按 Ctrl+Z
- **THEN** 该会话全部输入一次回退

#### Scenario: 按钮状态清晰
- **WHEN** undo 栈非空/为空
- **THEN** undo 按钮图标分别为黑色/灰色；redo 同理

### Requirement: 缩放与滚动稳定性
`ZoomAroundPoint` SHALL 同布局 pass 同步完成缩放与偏移校正；位图替换 SHALL 无可见闪烁；滚动懒渲染 SHALL 预渲染相邻页。

#### Scenario: Ctrl+滚轮缩放无跳动
- **WHEN** 以鼠标为锚点连续缩放
- **THEN** 锚点稳定，无偏移跳动与清晰度闪烁

#### Scenario: 快速滚动无闪动
- **WHEN** 快速滚动多页文档
- **THEN** 页面进入视口无位图替换闪动

### Requirement: 文本跨页移动
文本框 dragHandle 拖动 SHALL 支持跨页（复用 `SelectionCrossPageMoveAction`），并注册 undo。笔迹/形状/图片跨页纳入回归验证。

#### Scenario: 拖动文本到下一页
- **WHEN** dragHandle 拖到下一页松手
- **THEN** 文本迁移至目标页，Ctrl+Z 可撤销

## REMOVED Requirements
### Requirement: SimplePdfExporter 死代码
**Reason**: `SimplePdfExporter.cs` 全仓库零调用，功能被 PdfService 保存路径完全取代，维护成本为负资产。
**Migration**: 直接删除文件与（若有）引用；无行为影响。

## 远期 Roadmap（P2，本轮不实现）
- 录音笔记与回放同步（Notability 式）
- 文档密码保护/加密
- 双页并排视图、多文档分屏对照
- 无限画布文档类型（非 PDF 页式）
- 手写内容搜索（依赖手写识别索引化）
- 便签贴纸/素材库（Elements）
- 测量工具（距离/面积，工程 PDF）
- 链接注释（URL/内部跳转）
- PDF 反色夜间滤镜、打印矢量路径优化
- 云同步/自动备份到文件夹

## 已知限制记录（不在本轮范围）
- 压感/墨水模拟数据不随 PDF 持久化（/Ink 标准仅存 X/Y）。
- 文本宽度估算在 CJK 修复任务中改善，但极端长文本仍可能偏移。
- 手写转文字依赖 WinRT InkAnalyzer 互操作。2026-08-19 spike 结论：当前 WPF `net8.0-windows` 工程没有 Windows App SDK/CsWinRT InkAnalyzer 投影；本轮不引入未经验证的平台依赖，编辑器提供可见的双语“当前版本不可用”降级提示并保留原笔迹。
- 页面缩略图与搜索的渲染性能：缩略图已采用虚拟化占位项和可见项按需渲染；极长文档仍可继续优化搜索结果分页。
