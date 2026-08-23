# Tasks

> 状态同步口径（2026-08-21）：Tasks 0–40 保留 V5 代码级完成基线；Task 41 已完成代码与自动化审计，Task 42 的正式 checkout、项目文件和可见品牌已统一为 OpenNotes 并保留 Caelum 兼容标识，Task 45/46 已完成本地与线上 Pages 验收，Task 47 的真实 Codex/Antigravity 元数据迁移已在备份、事务、哈希和最终不变量保护下完成；Hidden Ink 的真实鼠标/计时/擦除/保存重开回归和独立 Poppler/Edge 工具链检查已通过，Task 43/44/48/49 仍有触笔、跨页、主题视觉和应用导出 PDF 的第三方视觉限制。远端仓库已由 GitHub 迁移为 `Learnmore-smart/OpenNotes`，Pages URL 为 `https://learnmore-smart.github.io/OpenNotes/`。`[x]` 表示有实现或验证证据，`[ ]` 表示仍需人工验收或外部系统处理。

## 当前状态摘要

| 任务 | 状态 | 说明 |
|---|---|---|
| 0–40 | 已实现/已记录 | V5 代码级实现、Task 28 降级结论和历史 29/29 测试基线已记录；当前完整套件为 100/100 |
| 41 | 已完成（代码/自动化） | 273 条三语 catalog、386 个调用、静态扫描 0 个硬编码可见字符串；语言事件与缺 key 测试通过 |
| 42 | 已完成（代码/静态审计） | 正式 checkout、solution/test/project 文件和可见品牌已切换 OpenNotes；Caelum namespace/数据目录/AppX identity 保留 |
| 43–44 | 代码级完成 | 文本框实现与 Desk/Paper/Ink/Margin/Mark 独立 WPF 主题已在源码中；Task 48 保留设备/像素视觉回归 |
| 45 | 本地重设计与线上基线验收完成 | live-folio 官网、响应式、焦点、reduced-motion、forced-colors、404、三语、相对资源、文本移动/八向缩放和 demo 交互检查通过；既有线上首页与 `/404.html` 已验收，本轮重设计待下次 main 推送自动发布 |
| 46 | 线上部署验收完成 | workflow run `32446996825` 已成功，Pages artifact/deploy、项目路径、缓存和回滚相关检查已记录 |
| 47 | 已完成（真实迁移） | 真实迁移日志显示 82 条主库线程、35 条 catalog 线程完成关联；当前只读核对确认仅保留一个 `Caelum` 项目，唯一根目录为 OpenNotes，旧项目根为 0；备份与 manifest 位于用户 `.codex\backups\caelum-project-migration-20260820_202940_152` |
| 48 | 自动化/部分交互回归完成，外部环境待回归 | solution build 与 OpenNotes.Tests 100/100、i18n/website 检查、缺失 `WINDIR` 下的真实 WPF 启动烟雾和隔离真实 PDF 编辑器加载通过；真实 pointer smoke 已通过笔划绘制、Whole-Stroke Eraser、文本框创建、八向句柄、BottomRight 拖拽、Undo/Redo、PDF 保存和进程重启重开；设置保存重开、Hidden Ink 鼠标/计时/擦除/撤销/保存重开和独立 Poppler/Edge 工具链也已通过，跨页/触笔/主题像素视觉/应用导出 PDF 的第三方视觉仍待人工回归 |
| 49 | 代码级完成/部分交互回归完成 | Hidden Ink 已接入页面、undo、sidecar 与 PDF；真实鼠标绘制、揭示计时、擦除、撤销、保存重开已通过，独立 Poppler/Edge 工具链已通过；触笔和应用导出 PDF 的第三方视觉仍待人工验证 |

## 批次一：用户 13 项

- [x] Task 0: 搭建 File Guardian 文档镜像体系
  - [x] SubTask 0.1: 创建 `.ai/` 目录与 `PROJECT_CONTEXT.md`（架构概览、Current Work、NEVER Change）
  - [x] SubTask 0.2: 为本次涉及核心文件创建镜像文档：`EditorPage.xaml(.cs)`、`PdfPageControl.xaml(.cs)`、`App.xaml`、`AppSettings.cs`、`AppSettingsService.cs`、`WindowsPenService.cs`、`PdfService.cs`、`VersionControlService.cs`（含 Purpose、约束、Open Threads）
  - [x] SubTask 0.3: 在 `PROJECT_CONTEXT.md` 登记 Current Work = 本 spec

- [x] Task 1: 撤销系统补全 + 按钮 enable/disable 颜色（#13，其余任务的基础）
  - [x] SubTask 1.1: 擦除 undo：像素模式按「擦除手势」（stylus down→up）记录被改动的原笔触与切割碎片，整笔模式记录移除笔触，松手时压入 undo action
  - [x] SubTask 1.2: 文本操作 undo：添加（CreateTextBox）、内容编辑（聚焦→失焦为一个会话，单步撤销）、删除、字号/颜色变更
  - [x] SubTask 1.3: 文本 dragHandle 拖动注册 undo（同页移动记录前后位置）
  - [x] SubTask 1.4: undo/redo 按钮样式触发器：IsEnabled=true 图标黑色（#1F1F1F），=false 灰色（#B0B0B0）
  - [x] SubTask 1.5: 回归验证：绘制/擦除/文本全套/选区移动缩放/粘贴/页面增删全部可撤销与重做，`UpdateUndoRedoButtons` 覆盖所有新 action

- [x] Task 2: 橡皮擦「像素 / 整笔」模式切换（#1）
  - [x] SubTask 2.1: eraser popup 新增两态 toggle UI（像素擦除=默认，整笔擦除），持久化到 AppSettings
  - [x] SubTask 2.2: 整笔擦除实现：擦除矩形与笔触包围盒/轨迹相交即整笔移除（PdfPageControl）
  - [x] SubTask 2.3: 整笔擦除接入 Task 1 的擦除 undo action；笔尾倒置/侧键切换路径同样遵循当前模式

- [x] Task 3: 形状工具：直线 / 矩形 / 椭圆 / 箭头（#2 #4）
  - [x] SubTask 3.1: `ToolType` 新增 Shape；工具栏新增「形状」toggle 按钮 + popup（子类型选择：直线/矩形/椭圆/箭头 + 颜色/粗细，复用 BuildToolPopup）
  - [x] SubTask 3.2: PdfPageControl 实现拖拽预览（半透明虚线覆盖层）与松手生成（多边形点集 Stroke，`FitToCurve=false` 保证边缘锐利；箭头=线身+两翼）
  - [x] SubTask 3.3: 生成的形状走 StrokeAddedAction undo；选择/移动/缩放/复制粘贴/保存加载（StrokeAnnotation 通道）全链路回归

- [x] Task 4: 涂鸦形状自动识别（#5）
  - [x] SubTask 4.1: pen popup 新增「形状识别」开关（默认关，持久化）
  - [x] SubTask 4.2: 抬笔时几何启发式分类：直线（弦偏差低）/ 椭圆（质心距离近恒定+闭合）/ 矩形（闭合+边方向聚类），置信度阈值过滤
  - [x] SubTask 4.3: 替换为理想形状（复用 Task 3 生成逻辑），单步 undo（原始手绘 ↔ 理想形状）；低置信度不干预

- [x] Task 5: 墨水模拟 + 压感开关接通（#9）
  - [x] SubTask 5.1: pen popup 新增「墨水模拟」toggle（默认关，持久化）；开启时抬笔后按逐点速度计算 PressureFactor（慢粗快细）后处理笔迹
  - [x] SubTask 5.2: 接通「压感」设置：PressureEnabled=false 时 `IgnorePressure=true`；同时修复 `AppSettingsService.Sanitize/Clone` 丢弃 EnablePressure 字段的 bug，设置真实持久化
  - [x] SubTask 5.3: 验证：两开关组合下新笔迹宽度行为正确，旧文档加载不受影响

- [x] Task 6: 选区逐项动画描边（#10）
  - [x] SubTask 6.1: `UpdateSelectionVisuals` 为每个选中项单独绘制虚线描边，动画 StrokeDashOffset 流动 + 颜色循环
  - [x] SubTask 6.2: 保留整体包围盒与 4 角缩放手柄；选区清空时停止动画并清理资源
  - [x] SubTask 6.3: 验证：重叠图形圈选后可分辨；性能无明显回退（多选 50+ 项不卡顿）

- [x] Task 7: Ctrl+点击多选（#11）
  - [x] SubTask 7.1: 选择工具下 Ctrl+点击命中测试（笔迹 GetBounds/文本容器 bounds），命中加入选区，已选中移出
  - [x] SubTask 7.2: 已有框选/套索选区时 Ctrl+点击累加；Ctrl+点击空白保持现选区不变
  - [x] SubTask 7.3: 多选项支持整体移动/缩放/复制/删除/跨页，回归验证

- [x] Task 8: 粘贴定位与自动全选（#6）
  - [x] SubTask 8.1: 验证/修正粘贴定位：粘贴在 `_lastClickedPoint`（最后点击页与点位），跨页复制→他页点击粘贴位置正确
  - [x] SubTask 8.2: 粘贴完成后将全部新内容置于选中状态，显示逐项描边+手柄，可直接拖动/缩放

- [x] Task 9: 文本 dragHandle 跨页移动（#12）
  - [x] SubTask 9.1: dragHandle 松手时按文本框中心点判定目标页（复用 `PageControl_SelectionMoveCompleted` 命中逻辑，提取为共享 helper `FindPageAtContainerPoint`），跨页走 `SelectionCrossPageMoveAction`
  - [x] SubTask 9.2: 接入 Task 1 的文本拖动 undo（跨页迁移可撤销/重做）
  - [x] SubTask 9.3: 回归验证：笔迹/形状跨页移动（已有）+ 文本跨页移动 + undo/redo 全通过

- [x] Task 10: 弹窗跨应用悬浮修复（#7）
  - [x] SubTask 10.1: `FixPopupTopmost` 补全到 colorPopup（InitializeTextBoxPopup）
  - [x] SubTask 10.2: 所有 ContextMenu 与 SettingsWindow ComboBox popup 的 Opened 事件统一应用同样的 HWND 修复
  - [x] SubTask 10.3: 验证：逐一打开各弹层后 Alt-Tab，无弹层悬浮于其他应用窗口之上

- [x] Task 11: 滚动条点击即达（#8）
  - [x] SubTask 11.1: 自定义 ScrollBar Track 行为：点击轨道按比例直接 `ScrollToVerticalOffset/HorizontalOffset`（点击点成为 thumb 中心，无动画无步进），垂直+水平
  - [x] SubTask 11.2: thumb 拖动行为不变；与滚轮平滑滚动互不干扰

- [x] Task 12: 缩放/滚动 frame jump 修复（#3）
  - [x] SubTask 12.1: `ZoomAroundPoint` 消除 BeginInvoke 一帧错位：同布局 pass 同步计算并应用新偏移
  - [x] SubTask 12.2: 位图替换防闪烁：新位图就绪后原子替换 Image.Source，不改变布局尺寸，无空白帧
  - [x] SubTask 12.3: 滚动懒渲染优化：空闲时预渲染可见页相邻页
  - [x] SubTask 12.4: 验证：Ctrl+滚轮连续缩放锚点不跳；快速滚动无闪动；缩放与滚动动画衔接正常

## 批次二：调研新增

- [x] Task 13: Ctrl+D 快速复制选区
  - [x] SubTask 13.1: Ctrl+D = 复制选区并偏移（右下 +20,+20）放置于同页，副本自动全选，复用 Task 8 管线与 ItemsAddedAction undo
  - [x] SubTask 13.2: 验证：图形/文本/多选内容均可 Ctrl+D，一次 Ctrl+Z 撤销整个复制

- [x] Task 14: 最近使用颜色
  - [x] SubTask 14.1: AppSettings 持久化最近颜色列表（每调色盘最多 8 色，去重置顶）
  - [x] SubTask 14.2: 画笔/荧光笔/文本调色盘顶部渲染「最近」行，点击即应用；选色后更新并保存
  - [x] SubTask 14.3: 验证：跨会话重启后最近颜色保留

- [x] Task 15: 仅笔绘制模式（防误触）
  - [x] SubTask 15.1: 工具栏/设置新增「仅笔绘制」toggle（默认关，持久化）
  - [x] SubTask 15.2: 开启时 InkCanvas 拒绝触摸/鼠标笔迹输入（触摸走平移），仅 StylusInput 绘制
  - [x] SubTask 15.3: 验证：手掌/手指触摸不画线可滚动，鼠标不画线，触笔正常书写

- [x] Task 16: 全屏沉浸模式
  - [x] SubTask 16.1: F11 或按钮进入：隐藏工具栏与多余 chrome；ESC/F11 退出恢复
  - [x] SubTask 16.2: 沉浸模式下书写/滚动/翻页/撤销快捷键仍可用；进出不触发布局跳动
  - [x] SubTask 16.3: 验证：反复进出无 UI 状态残留（按钮态、popup 全关）

- [x] Task 17: 版本历史治理
  - [x] SubTask 17.1: VersionControlService 增加上限（默认 50）与自动清理
  - [x] SubTask 17.2: 恢复版本前先保存当前状态为新版本
  - [x] SubTask 17.3: 验证：超限自动清理；恢复后历史出现「恢复前」新版本

- [x] Task 18: 中文文本导出修复（CJK）
  - [x] SubTask 18.1: FreeText 外观流改用嵌入 CJK 字体（PdfSharpCore XFont/子集嵌入），/DA 与 /AP 一致；中文宽度改用真实测量或 CJK 系数
  - [x] SubTask 18.2: 加载路径回归：含中文 FreeText 的 PDF 重开，文字/位置/字号/颜色正确回读
  - [x] SubTask 18.3: 验证：「你好 world」保存→重开→Caelum 与 Edge 显示正常；再次编辑保存不破坏
  - [x] SubTask 18.4: 扩展 PdfServiceAnnotationSavingTests：中英文混排保存+重新解析断言

- [x] Task 19: 图片注释
  - [x] SubTask 19.1: 新增 `ImageAnnotation` 模型（X/Y/W/H+图像数据）；PdfPageControl 新增图片层（InkCanvas 下、PdfImage 上）
  - [x] SubTask 19.2: 粘贴剪贴板图片落点=最后点击位置自动选中；实现 EditorPage 拖放（PNG/JPG 文件落点=松手位置，接通目前为死设置的 AllowDrop）
  - [x] SubTask 19.3: 图片接入选择管线：选中/逐项描边/移动/缩放/删除/复制粘贴/Ctrl+D/跨页/undo 全套
  - [x] SubTask 19.4: PdfService 保存/加载图片注释（PDF 内嵌图片对象）
  - [x] SubTask 19.5: 验证：截图粘贴→移动缩放→保存→重开完整恢复；Edge 兼容

## 批次三：深度调研新增

- [x] Task 20: 激光笔工具（用户点名）
  - [x] SubTask 20.1: `ToolType.Laser`；PdfPageControl 新增临时激光层（不进 InkCanvas.Strokes）
  - [x] SubTask 20.2: 墨迹生成后启动 ~1s 渐隐动画（Opacity 动画），结束移除元素；不触发 InkMutated/MarkDirty/PushUndo
  - [x] SubTask 20.3: 验证：激光书写自动消失；保存的 PDF 无激光痕迹；undo 栈不受影响

- [x] Task 21: Shift 直线约束
  - [x] SubTask 21.1: 画笔/荧光笔/形状拖拽时检测 Shift：将方向吸附至最近 0°/45°/90°（终点重投影）
  - [x] SubTask 21.2: 验证：按住 Shift 画水平/垂直/45°线笔直；不按 Shift 行为不变

- [x] Task 22: 直尺工具
  - [x] SubTask 22.1: 工具栏新增直尺 toggle；渲染可拖动/旋转（15° 吸附）的半透明直尺元素（带刻度）
  - [x] SubTask 22.2: 书写时笔尖落入直尺边缘吸附范围→输出点投影到直尺边线（生成直线）
  - [x] SubTask 22.3: 验证：沿直尺画线笔直；关闭直尺恢复正常；直尺不随文档保存

- [x] Task 23: 笔预设槽
  - [x] SubTask 23.1: 工具栏 3 个槽位按钮（显示颜色圆点+类型图标）；单击应用槽参数，长按/右键=保存当前工具参数到该槽
  - [x] SubTask 23.2: 槽位持久化（AppSettings）
  - [x] SubTask 23.3: 验证：切换即时生效；重启保留

- [x] Task 24: 笔迹平滑度设置
  - [x] SubTask 24.1: 新增平滑度设置（关/低/中/高），映射到笔迹收集后处理与 FitToCurve 组合
  - [x] SubTask 24.2: pen popup 快速切换 + 设置页入口；持久化
  - [x] SubTask 24.3: 验证：四档平滑效果可感知；「关」保留原始轨迹

- [x] Task 25: PDF 文本标记（下划线/删除线/波浪线）
  - [x] SubTask 25.1: 复用现有 PDF 文本选择管线（PdfPageTextInfo）；选中文本后右键/工具应用三种标记
  - [x] SubTask 25.2: AnnotationModels 新增 TextMarkupAnnotation（类型+Rects）；PdfService 以 /Underline /StrikeOut /Squiggly 写入与读取（含外观流）
  - [x] SubTask 25.3: 标记可选中/删除（含 undo）；验证保存重开与 Edge 可见

- [x] Task 26: 便签注释（Sticky Note）
  - [x] SubTask 26.1: `ToolType.StickyNote`；点击放置便签图标（折叠态），点击弹出编辑气泡（TextBox）
  - [x] SubTask 26.2: StickyNoteAnnotation 模型；PdfService 以 /Text 注释保存与读取（内容存 /Contents、位置 /Rect）
  - [x] SubTask 26.3: 便签可移动/删除（含 undo）；验证保存重开恢复；Edge 中显示为标准注释

- [x] Task 27: 区域高亮
  - [x] SubTask 27.1: 高亮工具扩展子模式：文本高亮（现有）/ 区域高亮（拖拽矩形半透明色块）
  - [x] SubTask 27.2: AreaHighlightAnnotation 模型（Rect+颜色+透明度）；渲染于 HighlightsCanvas；PdfService 保存/加载（/Highlight 矩形 QuadPoints）
  - [x] SubTask 27.3: 区域高亮可选中/移动/删除（含 undo）；验证保存重开恢复

- [x] Task 28: 手写转文字（含 spike）
  - [x] SubTask 28.1: Spike：验证当前 WPF 工程没有可用的 WinRT `InkAnalyzer` 投影，并将结论写入 `.ai/Task28-InkAnalysis.md`
  - [x] SubTask 28.2: 当前版本采用允许的降级路径：显示可见失败 Toast 并在 spec 记录，原笔迹保持不变
  - [x] SubTask 28.3: 验证降级路径不丢失手写原迹；未来接入平台投影后再启用中英文识别分支

- [x] Task 29: 文本富格式
  - [x] SubTask 29.1: TextAnnotation 扩展 Bold/Italic/FontFamily/Alignment 字段；内联工具栏增加 B/I/字体下拉/对齐按钮
  - [x] SubTask 29.2: PdfService 导出按格式渲染（字体选择联动 CJK 字体嵌入）；加载回读格式
  - [x] SubTask 29.3: 验证：格式保存重开恢复；undo 覆盖格式变更

- [x] Task 30: 页面缩略图侧边栏
  - [x] SubTask 30.1: 编辑器左侧可折叠面板：虚拟化占位项按可见项按需加载低 DPI 缩略图，当前页高亮同步
  - [x] SubTask 30.2: 拖拽重排页序（复用 DocumentSnapshotAction 模式的 undo）；右键增删/复制页（复用现有插入/删除管线）
  - [x] SubTask 30.3: 验证：重排/增删后主视图同步，undo/redo 正确

- [x] Task 31: 大纲 / 书签面板
  - [x] SubTask 31.1: PdfSharpCore 读取 PDF Outline 渲染目录树，点击跳转对应页
  - [x] SubTask 31.2: 自定义页面书签：Ctrl+M 星标当前页，书签列表面板（按文件路径持久化于本地 JSON）
  - [x] SubTask 31.3: 验证：大纲跳转准确；书签添加/删除/跳转/重启保留

- [x] Task 32: Ctrl+F 全文搜索
  - [x] SubTask 32.1: Ctrl+F 搜索框 UI（工具栏下方浮动）：输入→全文档异步搜索，命中列表（页码+片段）
  - [x] SubTask 32.2: 点击列表项/F3/Shift+F3 跳转并在页面渲染命中高亮矩形（复用 PdfPageTextInfo 区域）
  - [x] SubTask 32.3: 验证：多页文档搜索完整；跳转与高亮正确；Esc 关闭

- [x] Task 33: 快捷键补全
  - [x] SubTask 33.1: PageUp/PageDown=上/下翻页（JumpToPage），Home/End=首/末页
  - [x] SubTask 33.2: MainWindow 处理 Ctrl+Tab / Ctrl+Shift+Tab 循环切换标签
  - [x] SubTask 33.3: Ctrl+A 全选当前页全部注释（进入选择工具并全选）
  - [x] SubTask 33.4: 验证：各快捷键在编辑器/文本框聚焦时行为正确不冲突

- [x] Task 34: 适宽/适页缩放 + 页面旋转
  - [x] SubTask 34.1: 工具栏「适宽」「适页」按钮：计算 viewport/page 比例调用 ApplyCustomZoom
  - [x] SubTask 34.2: 页面 90° 旋转：PdfSharpCore 页 /Rotate 更新并通过页面快照 undo，保存重开保持
  - [x] SubTask 34.3: 验证：适宽/适页准确；旋转后渲染与注释位置正确，保存重开保持

- [x] Task 35: 页面模板扩展
  - [x] SubTask 35.1: `PageInsertTemplate` 新增 Dotted/Music/Cornell；PdfService 矢量绘制三模板
  - [x] SubTask 35.2: 模板选择器（插入页+建笔记本）新增三卡片预览
  - [x] SubTask 35.3: 验证：三种模板插入/保存/重开正确

- [x] Task 36: 导出 PNG + 清理死代码
  - [x] SubTask 36.1: 菜单「导出本页/全部页为 PNG」：含注释渲染，1x/2x 清晰度，SaveFileDialog
  - [x] SubTask 36.2: 删除 `SimplePdfExporter.cs`（全仓库零调用的死代码），确认构建
  - [x] SubTask 36.3: 验证：导出图片含全部注释，清晰度正确

- [x] Task 37: 从 PDF / 图片插入页面
  - [x] SubTask 37.1: 插入页对话框新增「从 PDF 插入」：选文件+页码范围，复制页面插入当前位置（含 undo）
  - [x] SubTask 37.2: 「从图片插入」：选图片文件→适配页面大小居中写入新页（含 undo）
  - [x] SubTask 37.3: 验证：插入的页内容完整，重开保持

- [x] Task 38: 设置页扩充
  - [x] SubTask 38.1: 修复 `AppSettingsService.Sanitize/Clone` 丢字段的 bug，改为全字段透传
  - [x] SubTask 38.2: SettingsWindow 新增自动保存间隔、压感、笔迹平滑度、默认笔颜色/粗细、主题；AppSettings 相应字段
  - [x] SubTask 38.3: AutoSaveAsync 失败显示错误 Toast
  - [x] SubTask 38.4: 验证各设置修改即生效且重启保留；自动保存失败可见

- [x] Task 39: 深色模式
  - [x] SubTask 39.1: App.xaml 主题资源字典（浅/深两套画刷）；设置切换即时应用并持久化
  - [x] SubTask 39.2: 主窗口/编辑器/设置页及侧边栏使用主题资源；编辑器背景联动（PDF 页面本身不变色）
  - [x] SubTask 39.3: 验证资源查找、即时切换、重启保持；交互视觉检查限制已记录

## 收尾

- [x] Task 40: 总体验证与文档同步
  - [x] SubTask 40.1: `dotnet build` 捕获完整输出零错误；全部测试通过（含 CJK 扩展测试）
  - [x] SubTask 40.2: 按 checklist.md 完成代码级逐项核对，交互设备/Edge 项记录为环境限制
  - [x] SubTask 40.3: 同步更新 `.ai/` 镜像文档（Change History、清理 Open Threads、PROJECT_CONTEXT Current Work）

## 后续任务：OpenNotes 完成收口

- [x] Task 41: 全量 i18n 完整性（代码与自动化验收完成）
  - [x] SubTask 41.1: `LocalizationService` 已提供 English/简体中文/Français catalog、占位符一致性测试、`LanguageChanged` 事件和缺 key 失败机制
  - [x] SubTask 41.2: 将 XAML、动态菜单、工具提示、设置项和异常消息中的剩余硬编码用户文案全部迁移到 catalog
  - [x] SubTask 41.3: 静态 key/literal 扫描通过；`LanguageChanged` 接线和运行时切换单元测试通过

- [x] Task 42: OpenNotes 品牌迁移（代码与静态审计完成）
  - [x] SubTask 42.1: 可见产品标题、项目产品元数据、AppX DisplayName、安装器显示名和 `ProductInfo` 已使用 OpenNotes
   - [x] SubTask 42.2: 正式 workspace、solution、project 和 test 项目改为 OpenNotes；保留 `Caelum` namespace、`%LOCALAPPDATA%\Caelum` 数据目录及 `WindowsNotesApp` AppX identity
  - [x] SubTask 42.3: 完成 README、公开 logo/资源、发布配置、官网及其他可见表面的品牌审计与迁移

- [x] Task 43: 可调整大小文本框（代码级完成，待回归）
  - [x] SubTask 43.1: 文本框提供八个方向的缩放手柄，宽高有最小值并支持从四角保持对向锚点
  - [x] SubTask 43.2: 文本框宽高进入注释模型的保存/加载路径，缩放结束生成独立 undo/redo 动作
   - [x] SubTask 43.3: 自动化覆盖长文本几何/换行、边界夹取、宽高持久化与 undo 数据；八个手柄的稳定 UI AutomationId 合约已加入
   - [ ] SubTask 43.4: 在 Task 48 中完成触笔、键盘操作和跨页交互回归；真实 pointer 的文本框保存重开、八向缩放和缩放 undo/redo 已由 48.2c 覆盖

- [x] Task 44: 独立 WPF 主题系统（代码级完成，待回归）
  - [x] SubTask 44.1: `ThemeService` 集中管理 Light/Dark 资源，并在启动、设置预览和保存后切换应用 chrome
  - [x] SubTask 44.2: 主题设置持久化；主窗口、首页、编辑器背景和设置页使用动态资源，PDF 页面位图不染色
   - [x] SubTask 44.3: 自动化覆盖主题资源选择、System/HighContrast 归一化、六个纸张/墨水材料 token、主要视图使用契约与持久化；`Test-OpenNotesUiAutomation.ps1 -SaveAndReopen` 已真实 Save、重启并确认 Français/深色选择仍在
  - [x] SubTask 44.3a: 主窗口、首页、编辑器工具栏、设置与模板选择器已统一为 Desk/Paper/Ink/Margin/Mark 视觉系统，保留原有命令、绑定与 PDF 位图
  - [ ] SubTask 44.4: 在 Task 48 中完成真实窗口对比度、弹层、选中态和重启视觉回归

- [x] Task 45: GitHub Pages landing page（本地与线上验收完成）
  - [x] SubTask 45.1: 以 “Open a PDF. Leave a trace.”、live folio、method、workspace anatomy、evidence drawer、principles 与 download 重构 `website/`，保留 OpenNotes 品牌、相对资源和三语入口
  - [x] SubTask 45.2: 已具备响应式、键盘焦点、减少动画/高对比度状态以及 404 fallback
  - [x] SubTask 45.3: 本地验证页面资源、链接、三语各 116 个 key、六个占位图和 demo 不依赖桌面 AppData；Playwright 覆盖多视口无溢出、绘制/撤销、文本移动、八向缩放、主题/语言、404、指针/键盘和 reduced-motion
  - [x] SubTask 45.4: 既有线上 `https://learnmore-smart.github.io/OpenNotes/` 与 `/404.html` 返回 200；本轮重设计源码将在下次 main 推送后由既有 Pages workflow 发布

- [x] Task 46: GitHub Pages 自动部署（线上验收完成）
  - [x] SubTask 46.1: `.github/workflows/pages.yml` 已定义 Pages artifact/deploy job、main/path 触发和最小权限
  - [x] SubTask 46.2: workflow run `32446996825` 成功发布 `website/` 静态产物，并保留现有 release workflow
  - [x] SubTask 46.3: 已验证线上相对路径、404 fallback、HTTPS、缓存响应和 workflow 部署结果

- [x] Task 47: Codex 项目与会话修复（真实迁移完成）
   - [x] SubTask 47.1: 已完成状态数据库 schema、项目记录、线程 cwd、session index、sessions/archived_sessions 的只读审计
   - [x] SubTask 47.1a: 已覆盖 `antigravity-cli`，并在真实状态临时副本 dry-run 验证 canonical ID、102 条主库线程、36 条 catalog、0 条当前根目录未关联线程、30 个 rollout 首行和 rollback manifest
   - [x] SubTask 47.1b: manifest 写出前执行最终不变量验证，旧路径/旧项目/未关联线程/旧 rollout header 必须全部为 0，否则进入回滚
    - [x] SubTask 47.2: 真实 Codex 项目记录已统一到现有 canonical ID `fc720e52-224f-4685-b49e-cf409a93714a`；当前 `projects` 仅保留一个名为 `Caelum` 的项目，`project_roots` 仅指向 OpenNotes，旧路径记录为 0；迁移日志报告 82 条主库线程与 35 条 catalog 线程已重新关联
    - [x] SubTask 47.3: 已执行备份、事务、WAL/SHM 快照、会话正文哈希和最终不变量校验；`tools/codex-migration-run.log` 报告 `AuthTouched=false`、`LogsTouched=false`、`RestartError=null`，备份 manifest 已写出；历史文件夹在迁移前已不存在，未被本次操作删除

 - [ ] Task 48: 全功能回归验证（自动化与启动烟雾完成，桌面/外部环境待回归）
  - [x] SubTask 48.1: 当前 checkout 已重新执行完整 `OpenNotes.sln` build（0 errors）与 `OpenNotes.Tests`（100/100）
    - [ ] SubTask 48.2: 回归打开/绘制/擦除/文本缩放/形状/选择/复制/图片/保存/重开、语言切换和浅深主题
  - [x] SubTask 48.2a: `tools/Test-OpenNotesUiAutomation.ps1` 在临时 `LOCALAPPDATA`/`APPDATA` 与 `OPENNOTES_DATA_ROOT` 环境值下真实打开主窗口、More 菜单和设置窗口；UIA 选择 Français 与深色预览后，设置页实时刷新并通过取消关闭；`-SaveAndReopen` 变体另行真实 Save、重启进程并确认两项选择持久化
     - [x] SubTask 48.2b: `tools/Test-OpenNotesEditorSmoke.ps1` 在临时三目录中预置真实 PDF 最近文件并经主页文件卡片打开；真实 `EditorPage` 加载成功，UIA 找到主要工具、`SavePdfButton` 与 `PdfScrollViewer`，并通过 `TogglePattern` 触发 Pen/Highlighter/Hidden Ink/Eraser/Shape/Laser/Ruler/Select/Text，输出 `EDITOR_SMOKE_RESULT=PASS`，2 个 sidecar 在结束后清理
     - [x] SubTask 48.2c: `tools/Test-OpenNotesPointerSmoke.ps1` 已在隔离临时环境的真实交互桌面通过：物理 pointer 点击切换 Pen/Text/Eraser，笔划保存后 PDF `/Ink` 从 `0` 增至 `1`，Whole-Stroke Eraser 保存后回到 `0`；随后创建文本框，八向手柄全部进入 UIA，BottomRight 实际拖拽使矩形从 `508×168` 变为 `628×240`，Undo/Redo/再次 Undo 均恢复正确；输入文本、保存 PDF、关闭并重启 OpenNotes，从最近文件重开后文本值仍为 `Pointer smoke persistence`。脚本仍保留 `WM_MOUSE*` 回退，但回退路径不冒充 WPF 命中测试
    - [x] SubTask 48.3a: GitHub Pages workflow 与线上首页/`404.html` 已在 Tasks 45/46 完成验证
    - [ ] SubTask 48.3: 回归触控笔/弹窗跨应用/Edge 或第三方 PDF 等环境相关项，并明确记录无法在当前环境验证的项目；独立 `Test-OpenNotesThirdPartyViewerSmoke.ps1` 已用 Poppler 与 Edge headless 验证输入 PDF 的页数、PNG 渲染、截图和哈希不变，但新生成 OpenNotes Hidden Ink PDF 的第三方视觉仍需一次实际产物交叉检查；本轮跨页/Hidden Ink 保留产物重试时 Windows 前台属于其他进程或 `LockApp`，物理 pointer 无法注入，均不计为产品失败
    - [x] SubTask 48.3b: 在刻意缺失 `WINDIR`、保留有效 `SystemRoot` 的宿主环境中启动构建产物，真实窗口枚举确认 `OpenNotes` 主窗口可见、标题正确且为 1280×720；应用通过进程级兜底规避 WPF 字体初始化失败
   - [x] SubTask 48.4: 已同步 0–49 的任务、清单、spec 与 `.ai` 镜像状态；未验证的外部项保留为未完成

## 新增学习功能：Hidden Ink

- [x] Task 49: Hidden Ink 学习遮罩（代码级完成，待全功能回归）
  - [x] SubTask 49.1: 增加独立 `HiddenInkAnnotation` 与 `PageAnnotation.HiddenInks`，不混入普通 `StrokeAnnotation`，并保留稳定 ID、颜色、alpha、宽度、reveal 时长和 DIP 点列
  - [x] SubTask 49.2: 增加专用 Hidden Ink 工具；笔/鼠标自由手绘产生不透明纸张色遮罩，默认宽度 28 DIP
  - [x] SubTask 49.3: 点击单个遮罩后临时揭示其覆盖内容，默认 3 秒后自动恢复；reveal 只存在于当前控件会话，保存/加载后仍从隐藏状态开始
  - [x] SubTask 49.4: Eraser 模式点击遮罩会移除该遮罩，并通过 `HiddenInkRemoved` 事件接入删除路径
  - [x] SubTask 49.5: 新建与移除分别注册 `HiddenInkAddedAction`/`HiddenInkRemovedAction`，支持 undo/redo；加载和重放使用 quiet API 避免重复命令
  - [x] SubTask 49.6: sidecar 收集/加载保留 Hidden Ink；PDF 写出不透明 `/Ink`，使用 `wna_hidden_` 前缀，剥离式加载按前缀恢复到 `HiddenInks` 而非普通笔迹
    - [x] SubTask 49.7a: `tools/Test-OpenNotesHiddenInkSmoke.ps1` 真实鼠标回归通过：遮罩绘制、遮罩/揭示/3 秒恢复、擦除、Undo、PDF `wna_hidden_` 标记、进程重启重开和重开后的再次计时均通过；临时隔离目录已清理
    - [x] SubTask 49.7b: `tools/Test-OpenNotesThirdPartyViewerSmoke.ps1` 独立 Poppler/Edge 工具链通过：`pdfinfo` 识别页数、`pdftoppm` 生成非空 PNG、Edge headless 生成截图、输入哈希保持不变
    - [ ] SubTask 49.7: 真实触控笔、跨应用弹窗和对“刚由 OpenNotes 保存的 Hidden Ink PDF”的第三方查看器视觉复核；当前 Windows 前台限制导致应用导出产物交叉检查尚未完成

# Task Dependencies
- Task 1 是 Task 2/4/9 的前置（擦除 undo / 文本 undo 基础）
- Task 4 依赖 Task 3（理想形状生成复用）
- Task 6 建议先于 Task 7/8（逐项描边是视觉反馈基础）
- Task 8 是 Task 13 的前置（Ctrl+D 复用粘贴管线）
- Task 9 依赖 Task 1；Task 19.3 依赖 Task 6/7
- Task 25 依赖现有 PDF 文本选择管线；Task 29 依赖 Task 18（字体嵌入）；Task 28 spike 独立可先行
- Task 34.2、37、35 均改 PdfService/页面结构，串行执行
- Task 38 依赖各功能引入的设置项落地后统一接线（Task 5/24 已先行各自持久化，38 收口 + 修 bug）
- Task 39 依赖 Task 38（主题设置项）
- Task 0 最先；Task 40 是 V5 基线收尾；Task 48 是本轮后续收尾
- 同文件冲突规避：EditorPage.xaml.cs / PdfPageControl.xaml.cs / PdfService.cs 为热点文件，全部按编号串行，不并行执行
- 批次二/三可在批次一验收后独立裁剪或延后，互不阻塞批次一交付
- Task 41/42 收口后再做 Task 48；Task 43/44 的交互和视觉验收由 Task 48 统一完成
- Task 45 是 Task 46 的前置；Task 47 依赖 Codex 外部项目/AppData 操作，不能以仓库文档代替
- Task 49 的代码接线已完成；其交互、保存重开和第三方查看器验收与 Task 48 一起执行，不能以本节文档代替验证证据
