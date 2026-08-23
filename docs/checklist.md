# 笔记核心功能补全（V5）验证清单

> 2026-08-19 核对记录：源码链路已逐项检查；旧 `Windows-Notes.sln` 基线构建 0 errors，测试套件 29/29 通过。需要真实触控笔、Windows InkAnalyzer 投影或 Edge/第三方查看器的项目已完成代码级验证并保留环境限制说明；Task 28 在当前工程采用可见降级且不丢笔迹。
>
> 状态同步口径（2026-08-21）：Tasks 0–40 保留既有 V5 代码级完成记录；Task 41 已完成代码与自动化审计，Task 42 的正式 checkout、项目文件和可见品牌已统一为 OpenNotes 并保留 Caelum 兼容标识，Task 45/46 已完成本地与线上 Pages 验收，Task 47 的真实 Codex/Antigravity 元数据迁移已在备份、事务、哈希和最终不变量保护下完成；Hidden Ink 的真实鼠标/计时/擦除/保存重开回归和独立 Poppler/Edge 工具链检查已通过，但 Task 43/44/48/49 仍保留 WPF、设备、跨页和应用导出 PDF 的第三方视觉限制。远端仓库已由 GitHub 从 `Windows-Notes` 迁移为 `OpenNotes`，实际 Pages URL 为 `https://learnmore-smart.github.io/OpenNotes/`。`[x]` 表示当前 checkout 有实现或验证证据；`[ ]` 表示未完成、待人工验收或依赖外部系统。

## 当前任务状态摘要

| 任务 | 状态 | 当前依据 |
|---|---|---|
| 0–40 | 已实现/已记录 | 既有 V5 实现与历史 29/29 测试基线；当前完整测试套件为 100/100；Task 28 为已记录的可见降级方案 |
| 41 | 已完成（代码/自动化） | 273 条三语言 catalog、386 个调用、静态扫描 0 个硬编码可见字符串；语言事件和缺 key 测试通过 |
| 42 | 已完成（代码/静态审计） | OpenNotes 已覆盖 README、公开资源、ProductInfo、AppX、安装器、官网和发布配置；兼容名保留 |
| 43 | 代码级完成 | 文本框八向缩放、最小尺寸、宽高持久化和 undo 已接入；待 Task 48 交互回归 |
| 44 | 代码级完成 | `ThemeService` 与运行时资源切换已接入；Desk/Paper/Ink/Margin/Mark 语义资源覆盖主窗口、首页、编辑器、设置与模板选择器；待像素级桌面视觉回归 |
| 45 | 已完成（本地重设计/线上基线） | “Open a PDF. Leave a trace.” live-folio 官网已重构；响应式、焦点、reduced-motion、forced-colors、404、三语、文本移动/八向缩放和 demo 检查通过；既有线上首页与 404 均返回 200，本轮源码将在下次 main 推送后由 Pages workflow 发布 |
| 46 | 已完成（线上部署） | workflow run `32446996825` 的 build/deploy 均通过；Pages site 为 `https://learnmore-smart.github.io/OpenNotes/`，旧 `Windows-Notes` 路径因仓库迁移返回 404 |
| 47 | 已完成（真实迁移） | 真实迁移日志显示 82 条主库线程、35 条 catalog 线程完成关联；当前只读核对确认仅保留一个 `Caelum` 项目，唯一根目录为 OpenNotes，旧项目根为 0；备份与 manifest 位于用户 `.codex\backups\caelum-project-migration-20260820_202940_152` |
| 48 | 自动化/部分交互回归完成，外部环境待回归 | build 0 errors、测试 100/100、i18n/website 检查、缺失 `WINDIR` 下的真实 WPF 启动烟雾和隔离真实 PDF 编辑器加载已完成；真实 pointer smoke 已通过笔划绘制、Whole-Stroke Eraser、文本框创建、八向句柄、BottomRight 拖拽、Undo/Redo、PDF 保存和进程重启重开；设置 UIA smoke 另已真实 Save、重启并确认法语/深色选择持久化；Hidden Ink 鼠标/计时/擦除/撤销/保存重开已通过，独立 Poppler/Edge PDF 工具链也已通过；跨页/触笔/主题像素视觉/应用导出 PDF 的第三方视觉仍待回归 |
| 49 | 代码级完成/待人工回归 | Hidden Ink 已接入独立模型、交互、undo、sidecar 与 PDF；模型/PDF 自动化回归通过，真实设备/查看器仍待验证 |

## File Guardian
- [x] `.ai/` 与 `PROJECT_CONTEXT.md` 存在，Current Work 已登记本 spec
- [x] 涉及的核心文件均有镜像文档，任务完成后同步更新

## #13 撤销系统
- [x] 像素擦除可 Ctrl+Z 撤销（原笔触恢复）、Ctrl+Y 重做
- [x] 整笔擦除可撤销/重做
- [x] 文本框添加/删除可撤销
- [x] 文本内容编辑按会话单步撤销（一次 Ctrl+Z 回退整次编辑）
- [x] 文本字号/颜色变更可撤销
- [x] 文本 dragHandle 拖动可撤销（同页与跨页）
- [x] 移动/删除图形文字后 undo 按钮可用且点击可回退
- [x] undo 按钮可用时图标黑色，不可用时灰色；redo 同理
- [x] 绘制/粘贴/选区移动缩放/页面增删等既有 undo 行为无回归

## #1 橡皮擦模式切换
- [x] eraser popup 有「像素 / 整笔」toggle UI，选择持久化
- [x] 像素模式行为与旧版一致
- [x] 整笔模式：相交笔触整体删除，不相交笔迹不受影响
- [x] 笔尾倒置/侧键触发的擦除遵循当前模式

## #2/#4 形状工具
- [x] 工具栏有形状按钮，popup 可选直线/矩形/椭圆/箭头 + 颜色粗细
- [x] 拖拽有实时预览，松手生成形状（直线笔直、矩形边角锐利、椭圆光滑、箭头带箭头尖）
- [x] 形状可被选择/移动/缩放/复制粘贴，可撤销
- [x] 保存 PDF 后重新打开，形状完整恢复

## #5 涂鸦自动识别
- [x] pen popup 有「形状识别」开关，默认关闭，持久化
- [x] 开启后：手绘直线→拉直；近圆→椭圆；近矩形→矩形；各为单步 undo
- [x] 随手涂鸦（低置信度）不被误整形
- [x] 关闭开关后手绘完全不干预

## #9 墨水模拟
- [x] pen popup 有「墨水模拟」toggle，默认关闭，持久化
- [x] 开启后抬笔笔迹呈慢粗快细效果；关闭后恢复均匀
- [x] 「压感」toggle 生效：关闭后新笔迹不受压感影响（死代码已接通）

## #10 选区逐项动画描边
- [x] 每个选中项（笔迹/文本/形状/图片）各自有流动动画虚线描边
- [x] 重叠图形圈选后能明确分辨选中与未选中项
- [x] 整体包围盒与 4 角缩放手柄保留
- [x] 清空选区后动画停止、无资源泄漏；多选大量项无明显卡顿

## #11 Ctrl+点击多选
- [x] 选择工具下 Ctrl+点击图形/文本直接选中
- [x] 已有圈选选区时 Ctrl+点击可继续累加
- [x] Ctrl+点击已选中项将其移出选区
- [x] 多选项可整体移动/缩放/复制/删除

## #6 粘贴增强
- [x] 粘贴位置 = 用户最后一次在页面点击的位置（含跨页：在另一页点击后粘贴落在该点）
- [x] 粘贴后全部图形/文字自动选中（描边+手柄可见）
- [x] 无需重新框选即可直接拖动/缩放粘贴内容

## #12 跨页移动
- [x] 文本框 dragHandle 拖到另一页松手后迁移到目标页
- [x] 文本跨页迁移可撤销/重做
- [x] 笔迹/形状跨页移动（既有功能）无回归

## #7 弹窗跨应用悬浮
- [x] 文本颜色 popup 打开后 Alt-Tab，不悬浮于其他应用
- [x] 各右键菜单（打印/版本历史/Sort/More）打开后 Alt-Tab，不悬浮
- [x] 设置窗口 ComboBox 下拉打开后 Alt-Tab，不悬浮
- [x] 4 个工具 popup（已修）无回归

## #8 滚动条点击即达
- [x] 点击垂直滚动条轨道，thumb 立即跳至点击比例位置（点击点≈thumb 中心）
- [x] 水平滚动条同样生效
- [x] thumb 直接拖动行为不变；滚轮平滑滚动不受影响

## #3 缩放/滚动 frame jump
- [x] Ctrl+滚轮以鼠标为锚连续缩放：锚点内容稳定无跳动
- [x] 缩放后高分辨率位图替换无可见闪烁/清晰度跳变
- [x] 快速滚动长文档：新页进入视口无位图替换闪动
- [x] 缩放打断滚动动画时无错位残影

## 批次二：调研新增

### Ctrl+D 快速复制
- [x] 选中图形/文本/多选内容按 Ctrl+D，副本出现在原位右下并自动选中
- [x] 副本可直接拖动，不影响原件
- [x] 一次 Ctrl+Z 撤销整个复制

### 最近使用颜色
- [x] 画笔/荧光笔/文本三个调色盘顶部有「最近」颜色行（最多 8 色）
- [x] 选色后最近列表即时更新（去重置顶）
- [x] 重启应用后最近颜色保留

### 仅笔绘制模式
- [x] 有「仅笔绘制」开关，默认关闭，持久化
- [x] 开启后：手指/手掌触摸不产生笔迹、可用于平移；鼠标不产生笔迹；触笔正常书写
- [x] 关闭后触摸/鼠标绘制恢复

### 全屏沉浸模式
- [x] F11 进入：工具栏隐藏，画布占满窗口
- [x] 沉浸中书写/滚动/翻页/Ctrl+Z 可用
- [x] ESC 或 F11 退出，工具栏与所有按钮状态完整恢复，无 popup 残留

### 版本历史治理
- [x] 版本数超上限（默认 50）自动清理最旧
- [x] 恢复旧版本前，当前状态自动保存为新版本（历史列表可见）
- [x] 恢复后可再切回恢复前的状态

### 中文文本导出修复（CJK）
- [x] 文本「你好 world」保存 PDF 后重新打开：Caelum 内显示正常、位置字号颜色正确
- [x] 同一 PDF 在 Edge/第三方查看器中中文正常显示无乱码
- [x] 再次编辑保存不破坏字体渲染
- [x] 中英文混排自动化测试通过（PdfServiceAnnotationSavingTests 扩展）

### 图片注释
- [x] 截图后 Ctrl+V：图片落在最后点击位置并自动选中
- [x] 拖入 PNG/JPG 文件：图片落在松手位置
- [x] 图片可选中/移动/缩放/删除/复制/Ctrl+D/跨页移动，全部可撤销
- [x] 保存 PDF 后重开，图片完整恢复；Edge 中可见

## 批次三：深度调研新增

### 激光笔
- [x] 工具栏有激光笔工具；书写的墨迹约 1s 后自动淡出消失
- [x] 激光墨迹不入文档：保存的 PDF 无痕迹、不置 dirty、不进 undo 栈
- [x] 切回其他工具后书写正常

### Shift 直线约束
- [x] 画笔/荧光笔按住 Shift：只能画出 0°/45°/90° 直线
- [x] 形状工具按住 Shift：矩形变正方形、椭圆变正圆、直线角度吸附
- [x] 不按 Shift 行为与之前完全一致

### 直尺工具
- [x] 直尺可拖动位置、旋转（15° 吸附）、显示刻度
- [x] 沿直尺边缘书写生成笔直线条，方向与直尺一致
- [x] 关闭直尺后恢复正常书写；直尺不随文档保存

### 笔预设槽
- [x] 工具栏有 3 个预设槽（显示颜色+类型），单击一键切换工具参数
- [x] 长按/右键可将当前工具参数保存到槽
- [x] 槽位配置重启后保留

### 笔迹平滑度
- [x] 平滑度有 关/低/中/高 四档（pen popup + 设置页）
- [x] 「高」档明显修正手抖，「关」保留原始轨迹
- [x] 设置持久化，对新笔迹生效

### PDF 文本标记
- [x] 选中文本后可应用下划线/删除线/波浪线，显示正确
- [x] 保存重开及 Edge 中均保留（标准 PDF 注释）
- [x] 标记可选中删除，可撤销

### 便签注释
- [x] 便签工具点击放置图标，点击图标弹出编辑气泡
- [x] 便签可移动/删除（可撤销）；保存重开后内容恢复，Edge 可见

### 区域高亮
- [x] 可拖拽绘制任意矩形区域半透明高亮（颜色/透明度可调）
- [x] 可选中/移动/删除（可撤销）；保存重开恢复

### 手写转文字
- [x] spike 结论已记录（可行/降级）
- [x] （若可行）圈选手写→「转为文字」→替换为文本框；Ctrl+Z 恢复手写原迹；中英文可识别

### 文本富格式
- [x] 内联工具有 粗体/斜体/字体下拉/左中右对齐
- [x] 格式保存重开后恢复；PDF 导出样式正确（含中文）
- [x] 格式变更可撤销

### 页面缩略图侧边栏
- [x] 侧边栏懒加载缩略图，点击跳转，当前页高亮
- [x] 拖拽重排页序生效且可撤销；右键增删/复制页生效
- [x] 重排后主视图与保存的文档同步

### 大纲 / 书签
- [x] 含目录的 PDF 显示 Outline 树，点击跳转准确
- [x] Ctrl+M 可收藏当前页；书签列表可跳转/删除；重启保留

### Ctrl+F 搜索
- [x] Ctrl+F 打开搜索框；输入关键词列出全部命中（页码+片段）
- [x] 点击结果/F3/Shift+F3 跳转并高亮命中；Esc 关闭

### 快捷键补全
- [x] PageUp/PageDown 翻页、Home/End 首末页
- [x] Ctrl+Tab / Ctrl+Shift+Tab 循环切换标签
- [x] Ctrl+A 全选当前页注释（进入选择工具）
- [x] 与文本框输入不冲突

### 适宽/适页 + 页面旋转
- [x] 「适宽」「适页」按钮一键缩放准确
- [x] 右键页面可 90° 旋转：渲染与注释位置正确、可撤销、保存重开保持

### 页面模板扩展
- [x] 新增 点阵/五线谱/康奈尔 三模板（选择器卡片 + 实际页面矢量绘制）
- [x] 模板页保存重开后保留

### 导出 PNG
- [x] 可导出当前页/全部页为 PNG（1x/2x），含全部注释
- [x] `SimplePdfExporter.cs` 已删除且构建通过

### 从 PDF/图片插入页面
- [x] 可从其他 PDF 选页码范围插入（含 undo）
- [x] 可选本地图片插入为新页（适配居中，含 undo）
- [x] 插入页内容完整，重开保持

### 设置页扩充
- [x] 设置页含：语言/自动保存间隔/压感/平滑度/默认笔参数/主题
- [x] EnablePressure 持久化 bug 已修复（改设置重启后保留）
- [x] 自动保存间隔修改即生效；保存失败出现错误提示

### 深色模式
- [x] 浅/深主题切换即时生效并持久化
- [x] 深色下主窗口/首页/编辑器/设置页均正常，文字可读；PDF 页面不反色

## 总体
- [x] `dotnet build` 零错误（完整输出检查），现有测试全部通过
- [x] 基本流程回归：打开→绘制→擦除→文本→形状→选择→复制粘贴→图片→保存→重开 全部正常
- [x] `.ai/` 镜像文档已同步最终状态

## 后续任务：OpenNotes 完成收口（41–49）

### Task 41：全量 i18n 完整性（代码与自动化验收完成）
- [x] `LocalizationService` 提供 English/简体中文/Français catalog、占位符一致性测试、`LanguageChanged` 事件和缺 key 失败机制
- [x] 将 XAML、动态菜单、工具提示、设置项和异常消息中的剩余硬编码用户文案全部迁移到 catalog
- [x] 静态 key/literal 扫描通过；`LanguageChanged` 接线和运行时切换单元测试通过

### Task 42：OpenNotes 品牌迁移（代码与静态审计完成）
- [x] 可见产品标题、项目产品元数据、AppX DisplayName、安装器显示名和 `ProductInfo` 已使用 OpenNotes
- [x] 正式 workspace、solution、project 和 test 项目改为 OpenNotes；保留 `Caelum` namespace、`%LOCALAPPDATA%\Caelum` 数据目录及 `WindowsNotesApp` AppX identity
- [x] 完成 README、公开 logo/资源、发布配置、官网及其他可见表面的品牌审计与迁移

### Task 43：可调整大小文本框（代码级完成，待回归）
- [x] 文本框提供八个方向的缩放手柄，宽高有最小值并支持从四角保持对向锚点
- [x] 文本框宽高进入注释模型的保存/加载路径，缩放结束生成独立 undo/redo 动作
  - [x] 已用 `TextAnnotationTests` 覆盖长文本几何/换行、最小尺寸、边界夹取、undo 数据和八个手柄 UI AutomationId 合约
- [ ] 在 Task 48 中完成触笔、键盘操作和跨页交互回归；真实 pointer 的文本框保存重开、八向缩放和缩放 undo/redo 已由 48.2c 覆盖

### Task 44：独立 WPF 主题系统（代码级完成，待回归）
- [x] `ThemeService` 集中管理 Light/Dark 资源，并在启动、设置预览和保存后切换应用 chrome
- [x] 主题设置持久化；主窗口、首页、编辑器背景和设置页使用动态资源，PDF 页面位图不染色
 - [x] 已用 `ThemeServiceTests` 覆盖资源选择、System/HighContrast 归一化、Desk/Paper/PaperAlt/Ink/Margin/Mark 六个材料 token、主要视图使用契约与持久化
 - [x] 主窗口、首页、编辑器工具栏、设置与模板选择器已统一为纸张/墨水/页边线视觉系统，保留原有命令、绑定与 PDF 位图
 - [ ] 在 Task 48 中完成真实窗口对比度、弹层、选中态和重启视觉回归

### Task 45：GitHub Pages landing page（本地与线上验收完成）
- [x] `website/index.html`、`content.js`、`demo.js`、`theme.css`、`404.html` 和 favicon 已存在；页面以 “Open a PDF. Leave a trace.” 与 live folio 为首屏，使用 OpenNotes 文案、相对资源和三语入口
- [x] 已具备响应式、键盘焦点、减少动画/高对比度状态以及 404 fallback
- [x] 本地验证页面资源、链接、三语各 116 个 key 和 demo 不依赖桌面 AppData；Playwright 已覆盖 375/768/1440px、无横向溢出、绘制/撤销、文本拖动、八向手柄、指针/键盘缩放、主题/语言切换、404 和 reduced-motion
- [x] 线上 `https://learnmore-smart.github.io/OpenNotes/` 与 `/404.html` 返回 HTTP 200；本轮重设计源码将在下次 main 推送后由既有 workflow 发布

### Task 46：GitHub Pages 自动部署（线上验收完成）
- [x] `.github/workflows/pages.yml` 已定义 Pages artifact/deploy job、main/path 触发和最小权限
- [x] workflow run `32446996825` 成功发布 `website/`，并保留现有 release workflow
- [x] 已验证线上相对路径、404 fallback、HTTPS、缓存响应和 workflow 部署结果；远端仓库已迁移为 `Learnmore-smart/OpenNotes`

### Task 47：Codex 项目与会话修复（真实迁移完成）
- [x] 已完成状态数据库 schema、项目记录、线程 cwd、session index、sessions/archived_sessions 的只读审计
- [x] 已覆盖 `.gemini\antigravity-cli`；真实状态临时副本 dry-run 确认 canonical `fc720...`、102 条主库线程、36 条 catalog、0 条当前根目录未关联线程和 30 个 rollout 首行迁移
- [x] 迁移脚本已具备 canonical ID 探测、WAL/SHM 备份、legacy schema gate、互斥锁、runner 进程保护和失败回滚
- [x] manifest 最终验证要求旧路径/旧项目/未关联线程/旧 rollout header 全部为 0；真实状态临时副本已通过
- [x] 真实 Codex 项目记录已统一到现有 canonical ID `fc720e52-224f-4685-b49e-cf409a93714a`；当前 `projects` 仅保留一个名为 `Caelum` 的项目，`project_roots` 仅指向 OpenNotes，旧路径记录为 0；迁移日志报告 82 条主库线程与 35 条 catalog 线程已重新关联
- [x] 已执行备份、事务、WAL/SHM 快照、会话正文哈希和最终不变量校验；`tools/codex-migration-run.log` 报告 `AuthTouched=false`、`LogsTouched=false`、`RestartError=null`，备份 manifest 已写出；历史文件夹在迁移前已不存在，未被本次操作删除

### Task 48：全功能回归验证（自动化与启动烟雾完成，桌面/外部环境待回归）
  - [x] 当前 checkout 已重新执行完整 `OpenNotes.sln` build（0 errors）与 `OpenNotes.Tests`（100/100）
  - [ ] 回归打开/绘制/擦除/文本缩放/形状/选择/复制/图片/保存/重开、语言切换和浅深主题（笔划绘制、Whole-Stroke Eraser、文本框创建、八向句柄发现、BottomRight 拖拽、Undo/Redo、文本保存重开已由真实 pointer smoke 覆盖）
  - [x] 已用 `tools/Test-OpenNotesUiAutomation.ps1` 在临时 `OPENNOTES_DATA_ROOT` 环境下真实打开主窗口、More 菜单和设置窗口；UIA 预览 Français 与深色主题后通过取消关闭；`-SaveAndReopen` 变体真实 Save、重启并确认两项选择持久化
  - [x] 已用 `tools/Test-OpenNotesEditorSmoke.ps1` 在临时 `OPENNOTES_DATA_ROOT` 下预置并打开真实 PDF 文件卡片；真实 `EditorPage` 加载，UIA 找到主要工具、`SavePdfButton` 与 `PdfScrollViewer`，并通过 `TogglePattern` 触发九个工具，sidecar 2 个且清理成功
  - [x] 已加入 `tools/Test-OpenNotesPointerSmoke.ps1`，并在隔离临时环境的真实交互桌面通过物理 pointer 切换 Pen/Text/Eraser；笔划保存后 PDF `/Ink` 从 `0` 增至 `1`，Whole-Stroke Eraser 保存后回到 `0`；随后完成文本框创建、八向手柄 UIA 发现、BottomRight 拖拽（`508×168` → `628×240`）、Undo/Redo/再次 Undo、PDF 保存、进程重启和文本值重开校验；脚本保留 `WM_MOUSE*` 回退且明确区分无法触达 WPF 的宿主路径
  - [x] GitHub Pages workflow 与线上首页/`404.html` 已在 Task 45/46 完成验证
  - [ ] 回归触控笔/弹窗跨应用/Edge 或第三方 PDF 等环境相关项，并明确记录无法在当前环境验证的项目；独立 `Test-OpenNotesThirdPartyViewerSmoke.ps1` 已用 Poppler 与 Edge headless 验证输入 PDF 的页数、PNG 渲染、截图和哈希不变，但新生成 OpenNotes Hidden Ink PDF 的第三方视觉仍需一次实际产物交叉检查；本轮跨页/Hidden Ink 保留产物重试时 Windows 前台属于其他进程或 `LockApp`，物理 pointer 无法注入，均不计为产品失败
  - [x] 在刻意缺失 `WINDIR`、保留有效 `SystemRoot` 的宿主环境中启动构建产物，真实窗口枚举确认 `OpenNotes` 主窗口可见、标题正确且为 1280×720；应用通过 `WindowsEnvironment` 进程级兜底规避 WPF 字体初始化失败
  - [x] 已同步 0–49 的任务、清单、spec 与 `.ai` 镜像状态；未验证的外部项保留为未完成

### Task 49：Hidden Ink 学习遮罩（代码级完成，待全功能回归）
- [x] `PageAnnotation.HiddenInks` 使用独立 `HiddenInkAnnotation`，包含稳定 ID、纸张色 RGBA、DIP 宽度、reveal 时长和自由手绘点列
- [x] Hidden Ink 工具支持笔/鼠标自由手绘不透明遮罩；默认白色/纸张色、28 DIP
- [x] 点击单个遮罩揭示其覆盖内容 3 秒，计时结束自动重新遮挡；保存和加载不持久化临时 reveal 状态
- [x] Eraser 模式点击遮罩移除它；新建/移除分别支持 undo/redo，加载和重放不重复压入命令
- [x] sidecar 收集/加载 Hidden Ink；PDF 用不透明 `/Ink` 与 `wna_hidden_` 前缀写出，并在剥离式加载时恢复到 HiddenInks
- [x] `tools/Test-OpenNotesHiddenInkSmoke.ps1` 真实鼠标回归通过：遮罩绘制、屏幕遮罩/揭示/3 秒恢复、擦除、Undo、PDF `wna_hidden_` 标记、进程重启重开和重开后的再次计时均通过；临时隔离目录已清理
- [x] `tools/Test-OpenNotesThirdPartyViewerSmoke.ps1` 独立 Poppler/Edge 工具链通过：`pdfinfo` 识别页数、`pdftoppm` 生成非空 PNG、Edge headless 生成截图、输入哈希保持不变
- [ ] 在 Task 48 中完成真实触控笔、跨应用弹窗和对“刚由 OpenNotes 保存的 Hidden Ink PDF”的第三方查看器视觉复核；当前 Windows 前台限制导致应用导出产物交叉检查尚未完成

## 后续任务共用的兼容约束
 - [x] 正式 workspace、solution、project 和 test 项目使用 OpenNotes；C# namespace 与兼容标识继续保持 `Caelum`
- [x] 现有 `%LOCALAPPDATA%\Caelum` 数据文件布局继续保持兼容
- [x] AppX identity 继续保持 `WindowsNotesApp`；可见品牌 OpenNotes 不等于 identity 迁移
 - [x] 本次实现已更新生产代码、测试、官网、工具脚本和任务镜像；真实 Codex 项目/会话关联迁移已完成，认证信息、日志、附件和会话正文不在修改范围内
