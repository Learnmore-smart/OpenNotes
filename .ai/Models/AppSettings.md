# Models/AppSettings.cs
> Last updated: 2026-08-24（Wave5 WorkspaceBackdrop persistence/normalization GREEN）| Protection: STANDARD
Wave 1 note: `PenPresets` JSON shape remains a compatibility boundary; empty/missing lists stay empty and `EditorPage` no longer writes UI defaults during initialization.

Quality follow-up note: the model keeps `PenPresets` as a nullable-safe, deep-copy-compatible JSON list; `AppSettingsService` resolves the test/production data root per operation without changing this model shape.

## Purpose（一句话）
应用设置 POCO：语言、压感、橡皮整笔模式、墨水模拟、形状识别、PenOnly 六个值字段 + 最近颜色/笔预设/平滑度 + 自动保存、默认画笔和主题设置，配合 `AppSettingsService` 持久化到 `%LOCALAPPDATA%\Caelum\settings.json`。

## What It Does（关键机制）
- `AppSettings` 是 `sealed` 快照。值字段包括 `Language`、`EnablePressure`、`WholeStrokeEraser`、`InkSimulation`、`ShapeRecognition`、`PenOnlyMode`（默认 false）、`StrokeSmoothing`（0..3，默认 2）、`AutoSaveIntervalSeconds`（15/30/60/120，默认 60）、`DefaultPenColorHex`（默认 `#000000`）、`DefaultPenSize`（0.5..24，默认 1.5）、`Theme`（`Light`/`Dark`/`System`/`HighContrast`）和 `PerformanceMode`（`BatterySaver`/`Balanced`/`BestQuality`，默认 `Balanced`）。
- `RecentPenColors`、`RecentHighlighterColors`、`RecentTextColors` 是最新在前的 `#RRGGBB` 列表；服务层过滤无效值、去重并限制 8 项。`PenPresets` 是 `PenPreset` 列表；服务层只保留受支持的 Pen/Highlighter、合法颜色和 0.5..24 尺寸，并深拷贝至最多 3 项；Wave 1 的 EditorPage 初始化只显示 fallback 槽位，不把空列表写成默认数据，Wave 3 再移除可见槽位 UI。
- `PenOnlyMode` 由工具栏按钮和 SettingsWindow 共同读写；EditorPage 在墨迹创建模式阻止鼠标/手指落墨，但不限制橡皮、选择、文本或滚动语义。
- `LanguageOption` 保存 `AppLanguage` 和本地化显示名，`ToString()` 返回显示名供 ComboBox 绑定。
- `WorkspaceBackdrop` 保存 editor workspace/canvas surround 的装饰选择（`Neutral`/`Paper`/`Slate`），缺失或非法 JSON 使用 `Neutral`；它不代表 PDF 页面的纸张颜色。

## Public API / 关键成员
| 成员 | 说明 |
|---|---|
| `AppSettings.Language` | `AppLanguage`，默认 English |
| `AppSettings.EnablePressure` | 压感开关，默认 true |
| `AppSettings.WholeStrokeEraser` | 整笔擦除开关，默认 false |
| `AppSettings.InkSimulation` / `ShapeRecognition` | 墨水模拟和形状识别开关，默认 false |
| `AppSettings.PenOnlyMode` | 仅笔绘制/防误触开关，默认 false |
| `AppSettings.RecentPenColors` / `RecentHighlighterColors` / `RecentTextColors` | 三组最近颜色列表，最多 8 项 |
| `AppSettings.PenPresets` | `PenPreset` 列表；EditorPage 维护 3 个工具栏槽位 |
| `AppSettings.StrokeSmoothing` | 0=关、1=低、2=中、3=高 |
| `AppSettings.AutoSaveIntervalSeconds` | 支持 15/30/60/120 秒 |
| `AppSettings.DefaultPenColorHex` / `.DefaultPenSize` | 默认画笔颜色和尺寸 |
| `AppSettings.Theme` | 设置文件保存的 Light/Dark/System/HighContrast 选择 |
| `AppSettings.WorkspaceBackdrop` | 编辑器 PDF 页外围的 Neutral/Paper/Slate 背景选择；缺失/非法值归一化为 Neutral |
| `AppSettings.PerformanceMode` | PDF 显示工作集/渲染预算档位；默认 Balanced |
| `PenPreset.Tool` / `.ColorHex` / `.Size` | 笔/荧光笔、颜色和尺寸三元组 |
| `LanguageOption(Language, string)` / `ToString()` | 语言下拉项及其显示文本 |

## Dependencies
- `Services/AppSettingsService`（读取、校验、深拷贝和写入）。
- `Pages/EditorPage`（工具栏、PenOnly、平滑度、默认画笔和预设槽）。
- `SettingsWindow` / `LocalizationService`（设置页控件与语言预览）。

## Open Threads / Resume Context
- **Status:** complete.
- `PerformanceMode` uses the backward-compatible `Balanced` default and supports `BatterySaver`/`BestQuality`; model, service sanitize/clone, SettingsWindow snapshot, localization and render policy are synchronized.

## Agent Decisions / Thoughts
- 旧 settings.json 缺失新增属性时依靠 C# 默认值，不引入迁移版本号。
- PenOnly 同时存在常驻工具栏入口和设置页入口；保存后 EditorPage 使用 `AppSettingsService.Save` 返回的快照，避免即时输入配置读取旧缓存。
- `Theme` 由 SettingsWindow 写入四种受支持的用户选择；`AppSettingsService` 负责归一化，`ThemeService` 负责应用对应的运行时资源。
- Backdrop 与 Theme 同属 chrome 预览设置，但由 `ThemeService` 映射到 workspace/surround resource，不能进入 PDF bitmap/render pipeline。

## Important Notes / NEVER Change
- 类为 `sealed`；扩字段必须同步模型、`Sanitize`、`Clone`、设置页克隆和 JSON 兼容行为。
- 旧 settings.json 无新字段时必须继续使用默认值，勿破坏 `%LOCALAPPDATA%\Caelum` 兼容路径。

## V5 Completion Status
- Task 15、23、24、38、39 的设置字段和 UI 入口已接入；压力、PenOnly、平滑度、自动保存、默认画笔和主题均可保存/预览。

## Wave5 Open Threads

- **Status:** complete for the automated Wave5 scope.
- `WorkspaceBackdrop` has a Neutral default, non-mutating sanitize/clone normalization, and all Neutral/Paper/Slate values round-trip through the legacy settings path.

## Change History
- 2026-08-18: 建立镜像文档并记录 Task 23 笔预设、Task 24 平滑度字段。
- 2026-08-20: Task 15/38/39——加入 PenOnly、自动保存间隔、默认画笔颜色/尺寸和主题字段；SettingsWindow 增加 PenOnly 控件并保留完整快照。
- 2026-08-21: 加入 `PerformanceMode`，旧 JSON 缺字段时自动使用 Balanced。
- 2026-08-23: Wave 1 compatibility contract：legacy three-entry `PenPresets` JSON round-trips through sanitize/clone/save/load; empty/missing lists remain empty and do not trigger UI default writes. Focused 3/3 and full 107/107 tests pass.
- 2026-08-24: Wave5 `WorkspaceBackdrop` defaults/normalization/round-trip contract is green; focused `ThemeSurfaceSourceTests` coverage includes legacy JSON and all three values.
