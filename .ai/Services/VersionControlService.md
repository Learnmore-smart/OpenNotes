# Services/VersionControlService.cs
> Last updated: 2026-08-21（test data-root override implemented）| Protection: STANDARD

## Purpose（一句话）
本地版本历史 sidecar：每次成功保存后把注释字典序列化为 JSON 快照存到 `%LOCALAPPDATA%\Caelum\VersionHistory\{文件SHA256}\`，供版本回滚。

## What It Does（关键机制）
- `GetVersionDir(filePath)` 继续使用绝对路径小写字符串的 SHA256 作为目录名，保持既有历史数据寻址兼容；目录不存在时创建。
- `SaveVersionAsync(filePath, annotations)` 序列化 `Dictionary<int, PageAnnotation>`（版本历史键仍为 int，与剪贴板 `AnnotationData.Pages` 的 string 键不同），文件名为 UTC `yyyyMMdd_HHmmss_fff` 加 GUID 后缀。毫秒和随机后缀共同保证同一时钟 tick 的多次保存不会覆盖。
- 写入完成后调用 `PruneVersions`，保留最新 `MaxVersions=50` 个 JSON；删除旧文件是 best-effort。
- `GetVersions(filePath)` 读取 `*.json` 并按 `File.GetLastWriteTimeUtc` 倒序排列。使用最后写入时间而不是创建时间，避免复制/恢复文件后历史顺序错乱。
- `LoadVersionAsync(versionFilePath)` 反序列化注释字典。EditorPage 在恢复旧版本前先 await 保存当前快照，因此回滚本身可逆；普通保存/自动保存也在 PDF 原子写入成功后再 await sidecar。

## Public API / 关键成员
| 成员 | 说明 |
|---|---|
| `MaxVersions` | 历史上限 50 |
| `SaveVersionAsync(filePath, annotations)` | 写唯一文件名的 UTC JSON 快照并剪枝 |
| `GetVersions(filePath)` | 返回最新在前的历史路径列表 |
| `LoadVersionAsync(versionFilePath)` | 读取 `Dictionary<int, PageAnnotation>` 快照 |

## Dependencies
- `Models/AnnotationModels`、`System.Text.Json`、`System.Security.Cryptography`。
- 调用方：EditorPage 的保存、自动保存和版本历史恢复 UI。

## Open Threads / Resume Context
- **Status:** ready_for_next
- 同 tick 文件覆盖问题已修复；保留路径哈希布局和旧 `.json` 文件可读性。仍需外部 UI 走查版本菜单和真实恢复体验。

## Agent Decisions / Thoughts
- 快照只存注释字典，不复制整份 PDF，以保持历史轻量；页面结构变更后的页号语义仍由 EditorPage 的结构快照/恢复流程负责。
- 时间排序采用 last-write time，文件名增加 GUID；不要以创建时间或秒级文件名重新引入同 tick 碰撞。

## Important Notes / NEVER Change
- 目录布局 `{SHA256(path)}\*.json` 是既有历史寻址方式，不能随 OpenNotes 品牌迁移改为新数据目录。
- 目录根通过 `ProductInfo.GetDataDirectory()` 获取；默认布局和兼容路径不变，只有显式 `OPENNOTES_DATA_ROOT` 测试进程才会重定向。
- `GetVersions` 必须保持最新在前；序列化形状仍是 `Dictionary<int, PageAnnotation>`。

## V5 Completion Status
- Task 17 的 50 条上限、剪枝、恢复前快照和同 tick 唯一文件名已实现；自动化构建/测试通过。

## Change History
- 2026-08-18: 建立镜像并记录历史无上限/秒级覆盖的旧行为。
- 2026-08-20: 文件名增加毫秒与 GUID，列表改按最后写入时间排序；EditorPage 保存/自动保存改为 PDF 成功后 await 版本写入。
- 2026-08-21: 版本历史根目录改由 `ProductInfo.GetDataDirectory()` 提供；显式 `OPENNOTES_DATA_ROOT` 可隔离测试 sidecar，默认路径和哈希布局不变。
