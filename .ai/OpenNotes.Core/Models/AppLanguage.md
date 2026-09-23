# OpenNotes.Core/Models/AppLanguage.cs
> Last updated: 2026-09-22（V6 WinUI 3 migration — moved into OpenNotes.Core on `v6/winui3`）| Protection: STANDARD

## Purpose（一句话）
应用语言枚举：`English`/`Chinese`/`French`，由 `AppSettings.Language` 持久化并由 `LocalizationService` 驱动 UI 文案。

## What It Does（关键机制）
- `AppLanguage` 是 `Caelum.Models` 下的纯枚举：`English = 0`、`Chinese = 1`、`French = 2`。
- `LocalizationService` 把枚举映射到 `CultureInfo`（`zh-CN`/`fr-FR`）和语言选项列表；JSON 设置按枚举名序列化，新增成员向后兼容。
- 无任何 UI/WPF 依赖，已随 V6 WinUI 3 迁移进入 `OpenNotes.Core`（namespace 保持 `Caelum.Models`）。

## Public API / 关键成员
| 成员 | 说明 |
|---|---|
| `English` / `Chinese` / `French` | 三种界面语言，默认 English |

## Dependencies（上下游）
- 被 `AppSettings.Language`、`LanguageOption`、`LocalizationService` 使用；测试经 `AppSettingsCompatibilityTests`/`LocalizationServiceTests` 覆盖。

## Change History
| Date | Change | Author |
|---|---|---|
| 2026-09-22 | Moved to `OpenNotes.Core/Models/` for the V6 WinUI 3 extraction (no code change). | Devin |
