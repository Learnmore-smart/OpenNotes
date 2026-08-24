# Services/AppSettingsService.cs
> Last updated: 2026-08-24（Wave5 WorkspaceBackdrop sanitize/clone GREEN）| Protection: STANDARD
Wave 1 note: preserve legacy `PenPresets` JSON entries, deep-copy lists, and never fill UI defaults in `Sanitize`/`Clone`.

## Purpose（一句话）
静态设置服务：把 `AppSettings` 读写到 `%LOCALAPPDATA%\Caelum\settings.json`，带缓存与锁、坏 JSON 默认回退、全字段校验和防御性深拷贝。

## What It Does（关键机制）
- 每次 `Load()`/`Save()` 都在锁内重新解析 `ProductInfo.GetDataDirectory()` 并创建目录；默认仍是 `%LOCALAPPDATA%\Caelum`，显式 `OPENNOTES_DATA_ROOT` 仅供隔离测试进程使用。缓存绑定当前绝对 settings path，切换测试 root 不会依赖静态初始化或执行顺序；JSON 使用缩进格式。
- `Load()` 在 `SyncRoot` 内按当前 path 懒加载/刷新缓存，并返回 `Clone(_cachedSettings)`；调用方不能通过修改返回对象污染缓存。
- `Save(settings)` 在同一锁内调用 `Sanitize`，绑定当前 path 写入 `settings.json`，再返回一个 clone。
- `ReadSettingsCore(path)` 对文件不存在、空内容、反序列化异常和 IO 异常回退到 `new AppSettings()`，不让坏设置阻断启动。
- `Sanitize()` 不修改传入对象，而是创建完整新快照：校验 `AppLanguage`；把 `StrokeSmoothing` 限制在 0..3；只接受 15/30/60/120 秒自动保存值；校验有限且在 0.5..24 的默认笔尺寸；把默认颜色规范化为大写 `#RRGGBB`；归一化主题和 `PerformanceMode`（无效/缺失 → `Balanced`）；并复制所有 bool、颜色列表和笔预设。
- `WorkspaceBackdrop` 由服务层归一化为 `Neutral`、`Paper` 或 `Slate`；旧 JSON 缺字段和非法手工值均回退 `Neutral`，且不修改调用方对象。
- `CopyColorList()` 过滤无效 hex、统一大写、去重并最多保留 8 项。`CopyPenPresets()` 只保留受支持的 Pen/Highlighter、合法 hex 颜色和 0.5..24 尺寸，逐项深拷贝并最多保留 3 项；预设默认槽位仍由 EditorPage 首次加载时填充，服务层不擅自生成。

## Public API / 关键成员
| 成员 | 说明 |
|---|---|
| `Load()` | 返回缓存设置的防御性 clone |
| `Save(AppSettings)` | Sanitize → 更新缓存 → 写 settings.json → 返回 clone |
| `Sanitize` / `Clone`（private） | 完整字段复制、集合隔离和值校验 |
| `GetSettingsPath()`（private static） | 每次操作解析 `%LOCALAPPDATA%\Caelum\settings.json`（测试可按操作切换 root） |

## Dependencies
- `Models/AppSettings`、`Services/ProductInfo`、`System.Text.Json`。
- 消费方：EditorPage（压力、橡皮、墨水模拟、形状识别、PenOnly、平滑、自动保存和默认画笔）、SettingsWindow（完整设置快照和预览）。

## Open Threads / Resume Context
- **Status:** complete for the automated Wave5 scope.
- `PerformanceMode` and `WorkspaceBackdrop` are normalized and defensively cloned without changing the legacy data path; old JSON missing `WorkspaceBackdrop` resolves to `Neutral`.

## Agent Decisions / Thoughts
- 采用显式字段复制而不是 MemberwiseClone/序列化 clone，便于审查新增设置是否遗漏；集合使用新列表和新 `PenPreset`，避免别名。
- 服务层只做稳定性/格式校验，不生成默认笔预设槽位；槽位初始化属于 EditorPage 的一次性用户体验逻辑。

## Important Notes / NEVER Change
- 保留静态类、`SyncRoot` 锁、缓存 clone 和坏 JSON 静默回退。
- 不要把 Sanitize 改成原地修改调用方对象；不改变 `%LOCALAPPDATA%\Caelum` 兼容路径。
- 测试覆盖必须证明未设置覆盖变量时仍使用兼容路径，并且覆盖值不会泄漏到用户 AppData。

## V5 Completion Status
- Settings fields through Task 15/23/24/38/39 are persisted without dropping values; invalid hand-edited values are normalized and collections are isolated.

## Change History
- 2026-08-18: 建立镜像并记录 Sanitize/Clone 丢弃设置字段的历史 bug。
- 2026-08-20: Sanitize 改为非变异式完整快照；加入 smoothing/autosave/pen-size/color/theme 校验、颜色列表清洗和完整 PenOnly/预设复制。
- 2026-08-21: 数据目录改由 `ProductInfo.GetDataDirectory()` 提供；显式 `OPENNOTES_DATA_ROOT` 只重定向隔离测试进程，默认仍为 `%LOCALAPPDATA%\Caelum`。
- 2026-08-21: Sanitize/Clone 接入 `PerformanceMode`，无效值回退 Balanced。
- 2026-08-23: Wave 1 compatibility regression confirms `PenPresets` JSON entries survive sanitize/clone/save/load, list entries are defensively copied, and empty/missing lists are never populated by the service. Focused 3/3 and full 107/107 tests pass.
- 2026-08-23: Quality follow-up removed static settings-path initialization; the per-operation root regression passes, while legacy JSON/clone behavior remains intact. Focused settings 4/4 and full suite 113/113 pass.
- 2026-08-24: Wave5 `WorkspaceBackdrop` sanitize/clone/save/load compatibility is green for Neutral/Paper/Slate and invalid-value fallback, with `%LOCALAPPDATA%\Caelum` compatibility preserved.
