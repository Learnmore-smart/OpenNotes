# App.xaml（+ App.xaml.cs）
> Last updated: 2026-08-24（Wave5 neutral semantic token GREEN） | Protection: STANDARD

## Purpose（一句话）
应用级全局样式资源字典：玻璃拟态画笔、Sleek 细滚动条、Modern ComboBox、对话框按钮、隐式 Slider/ContextMenu/MenuItem 样式。

## What It Does（关键机制，含行号引用）
- **玻璃画笔**（行 7-12）：`AppGlassPanelBrush/SurfaceBrush/SurfaceHoverBrush/SurfacePressedBrush/BorderBrush/SubtleBorderBrush`（白底不同透明度 0.08-0.24）。
- Wave5 publishes semantic aliases for window/workspace/sidebar/toolbar/surface/control/border/text/subtle/accent/focus/selection/danger, all backed by runtime-swappable `ThemeService` resources; production Home/MainWindow/Editor/Settings chrome consumes them with DynamicResource/SetResourceReference; backdrop remains workspace-only.
- Wave5 review adds `SettingsFocusVisualStyle` and explicit two-DIP focus rings to ModernComboBox/DialogPrimary/DialogSecondary and runtime page chrome; disabled states use themed opacity/subtle text. `ThemeAnimationDuration`, `ThemeSurfaceOpacity`, `ThemeShadowOpacity` and `ThemePopupAnimation` are consumed by scroll/popup/effect templates.
- **ToolbarFocusVisualStyle**（Wave 3）：共享 focus-visible adornment，使用 `ThemeFocusBrush`、2 DIP ring、负 margin，不改变 toolbar layout；EditorPage toolbar button styles add 32 DIP minimum targets and disabled tooltip support. Code-created popup controls resolve the same app-level `ToolbarButtonStyle`/`ToolbarToggleButtonStyle` resources.
- P2 keeps code-created icon/popup templates on `ThemeControlHoverBrush`, `ThemeControlPressedBrush`, `ThemeSurfaceAltBrush`, `ThemeDisabledForegroundBrush`, and `ThemeFocusBrush`; no popup state may introduce a literal hover/pressed/disabled/focus color. The implicit Slider now shares `ToolbarFocusVisualStyle` and exposes a theme-aware focus ring plus disabled trigger.
- **SleekScrollViewer**（**行 97-148**）：全局细滚动条观感。
  - 垂直/水平 ScrollBar 样式（行 47/72）：**宽/高 12px**（Min 同 12），鼠标悬停变 14（行 65-69/90-94）；Thumb CornerRadius=6、`#B4556474` 底、悬停 `#CC334155`、拖拽 `#E81F2937`（行 14-45）；Thumb MinHeight/MinWidth=38。
  - 模板只含 `Track + Thumb`（无显式 RepeatButton）——**默认 Track 点击 = 分页步进**（WPF ScrollBar 默认命令行为），拖拽走 Thumb。Task 11 起 EditorPage 的 PdfScrollViewer 两个滚动条（垂直+水平）以 `ScrollBarTrackJump_MouseLeftButtonDown`（PreviewMouseLeftButtonDown 拦截，EditorPage.xaml.cs）覆盖该默认行为为「点击轨道即达」（点击点成为 thumb 中心，无动画无步进）；**本样式文件未改**，其他 SleekScrollViewer 使用处（ComboBox 下拉/ContextMenu）保持默认行为。
  - ScrollViewer 模板（行 97-148）：两列/两行 Grid 布局 ScrollContentPresenter + 两个 ScrollBar，悬停时滚动条 Opacity 0.96→1。
- **ModernComboBox**（行 151-207）：52 高、18 字号、圆角 16、主题动态底色；下拉 Popup 圆角 16 + DropShadow 使用 `ThemeShadowOpacity`，内嵌 `SleekScrollViewer`，`ThemePopupAnimation` 在 ReduceMotion 时为 None；Chevron 用 Segoe MDL2 Assets `\uE70D`。外层有 2 DIP DynamicResource focus ring 和 disabled opacity。`ItemContainerStyle` 绑定 `ModernComboBoxItem`（行 209-232），选中/悬停/键盘焦点状态随主题资源切换；`CompactComboBox`（行 260 起）复用同一套模板供编辑器内联字体/对齐控件使用。Task 10 起该下拉 Popup 的跨应用悬浮（Alt-Tab 后仍置顶）由 `Services/PopupZOrderHelper.FixComboBoxPopupTopmost` 接入修复。
- **对话框按钮**（行 259-311）：`DialogPrimaryButton`（`ThemeAccent*Brush` 状态，文字使用 `ThemeSurfaceBrush`）和 `DialogSecondaryButton`（`ThemeSurfaceAltBrush`、`ThemeControl*Brush` 与 `ThemeBorderBrush`）均有 2 DIP focus ring 与 disabled visual。
- **GlassPopupBorder**（行 290-308）：白 0.92 底/白 0.45 边、圆角 14、投影。
- **隐式样式**（无 x:Key，全 app 生效）：Slider（行 311-369，主题轨道/Thumb、共享 `ToolbarFocusVisualStyle`、焦点 ring 与禁用 trigger）；ContextMenu（行 372-395，Win11 风：外层 12 Padding 防投影裁剪 + 近实白 `#FCFCFC` 底修 shadow bleed，内嵌 SleekScrollViewer）；MenuItem（行 398-433，SharedSizeGroup Icon/正文/InputGestureText 三列，高亮 `#0D000000`）；菜单 Separator（行 436-447）。
- **App.xaml.cs**：应用启动/资源加载，`x:Class="Caelum.App"`；`App` 类型初始化时调用 `WindowsEnvironment.NormalizeForWpf`，在首个 `Window` 初始化前为缺失 `WINDIR` 的宿主补齐进程级别别名；`OnExit` 解绑 `ThemeService` 的静态系统偏好监听。

## Public API / 关键成员（表）
| 资源键 | 行号 | 用途 |
|---|---|---|
| `SleekScrollViewer` | 97 | EditorPage PdfScrollViewer 等处引用 |
| `SleekVerticalScrollBar` / `SleekHorizontalScrollBar` | 47/72 | 12px 细滚动条（hover 14px） |
| `ModernComboBox` / `ModernComboBoxItem` | 151/209 | 设置/下拉 |
| `DialogPrimaryButton` / `DialogSecondaryButton` | 235/262 | 对话框按钮 |
| `GlassPopupBorder` | 290 | 弹层边框 |
| `ToolbarFocusVisualStyle` | 43 | EditorPage toolbar focus-visible adornment；动态 ThemeFocusBrush、无布局跳动 |
| `AppGlass*Brush` 系列 | 7-12 | 玻璃拟态面板 |
| （隐式）Slider / ContextMenu / MenuItem / Separator | 311/372/398/436 | 全局默认样式 |

## Dependencies
- 被 EditorPage.xaml（行 108 `Style="{StaticResource SleekScrollViewer}"`）、ModernComboBox 的 Popup 内 ScrollViewer、ContextMenu 模板等处 StaticResource 引用。
- Segoe MDL2 Assets 字体（chevron 图标）。

## Open Threads / Resume Context
- **Status:** complete for Wave 3 P2 toolbar style/lifecycle source scope; visual focus and foreground/device checks remain external.
- **Intent/result:** `ToolbarFocusVisualStyle` and app-level runtime popup styles use dynamic theme resources; dynamic popup controls receive the same focus/disabled treatment. The Slider template exposes focus/disabled visuals. Wave 5 palette/backdrop resources remain untouched.
- `PopupZOrderHelper` registration is now idempotent for tool popups, ContextMenus and ComboBox dropdowns, with explicit tool-popup detachment before localization rebuilds.
- Wave5 status: complete for automated scope; default Light aliases use the approved neutral palette and editor surround references use backdrop-aware dynamic resources. PDF image/page layers remain independent.

## Agent Decisions / Thoughts
- ContextMenu 的外层 12px Padding + 近实白底是修"投影被 Popup 边界裁剪/渗色"的双保险（注释行 377/381 自述）。
- 滚动条 12px+hover14 是触摸/笔场景的可见性折衷。

## Important Notes / NEVER Change
- `SleekScrollViewer` 模板必须保留 `PART_VerticalScrollBar`/`PART_HorizontalScrollBar` 命名部件（WPF ScrollViewer 契约），否则滚动失效。
- 隐式样式（无键）勿随意加新 TargetType——会波及全 app 同类型控件。
- 改 `SleekScrollBarThumb` 的 OverriddenDefaultStyle 结构时注意 Thumb 命名部件契约。

## V5 Completion Status

- Task 39 adds runtime-swappable ThemeWindow/Surface/Canvas/Border/Foreground resources; the main window, settings, editor and sidebar consume the palette through DynamicResource.

## Change History
- 2026-08-18: 建立镜像文档（Task 0）。
- 2026-08-18: Task 10——ModernComboBox 下拉 Popup 的 topmost 修复说明（`Services/PopupZOrderHelper.FixComboBoxPopupTopmost`，SettingsWindow 接入；App.xaml 样式未改动）。
- 2026-08-18: Task 11——滚动条点击即达：机制为 EditorPage 侧 preview 事件拦截（`ScrollBarTrackJump_MouseLeftButtonDown`），**App.xaml 样式/模板零改动**；SleekScrollViewer 段落补记该行为覆盖范围（仅 PdfScrollViewer 的两根滚动条）。
- 2026-08-20: 将 App.xaml 内控件模板的非语义固定色迁移至 `Theme*Brush` 动态资源；保留透明布局值与阴影色，并使主按钮文字随浅色/深色/高对比度表面资源切换。
- 2026-08-21: 为缺少 `WINDIR` 但存在有效 `SystemRoot` 的 WPF 宿主加入进程级启动兼容兜底；不修改用户或机器环境。
- 2026-08-23: Added the shared `ToolbarFocusVisualStyle` resource for stable, theme-aware toolbar focus rings; toolbar min-target/disabled behavior remains local to EditorPage styles.
- 2026-08-23: P2 completed the app-level runtime `ToolbarButtonStyle`/`ToolbarToggleButtonStyle` resources and implicit Slider focus/disabled template so code-created popup controls share Theme-token hover/pressed/checked/focus behavior in light, dark and high-contrast palettes.
- 2026-08-22: `ModernComboBox` 接入显式 `ModernComboBoxItem` 容器样式并新增 `CompactComboBox`；编辑器工具栏按钮、加载卡片和动态工具/选择/文本弹窗的背景、边框、标题、分隔线、筛选选中态和键盘模式切换改用 `Theme*` 动态资源。
- 2026-08-22: 增加 Desk/Paper/PaperAlt/Ink/Margin/Mark 初始语义资源，浅色默认值与 ThemeService 的纸张/墨水 palette 对齐，弹层表面改为语义 Paper；保留现有控件模板键和行为。
