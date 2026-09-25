# OpenNotes.Core/Services/RecentColors.cs
> Last updated: 2026-09-25（G4 port — WPF RecordRecentColor/TryParseRecentColor moved to Core for headless tests）| Protection: STANDARD

## Purpose（一句话）
每调色板"最近使用颜色"列表的去重/上限/解析原语 —— WPF `EditorPage.RecordRecentColor`/`TryParseRecentColor` 的 Core 移植，供 WinUI shell（WPF 侧仍保留其内置实现 — Pages/EditorPage.xaml.cs:4816+，本文件为移植版） 共用并可直接无头测试。

## What It Does（关键机制）
- `MaxRecentColors = 8`：WPF `EditorPage` 常量的WinUI 侧权威来源（WPF 仍用自己的私有副本）。
- `Record(list, hex)`：`OrdinalIgnoreCase` 去重后插到索引 0，超出 8 项的尾部截断；list/hex 为空时静默返回。列表属于 `AppSettingsService.Load()` 返回的瞬态克隆 —— 调用方负责 `Save()`。
- `TryParse(hex, out a, r, g, b)`：严格 `#RRGGBB`（记录格式）+ 容忍 `#AARRGGBB`（手工编辑 settings.json），非法输入返回 false 不抛异常。

## Public API / 关键成员
| 成员 | 说明 |
|---|---|
| `MaxRecentColors` | 8，与 `AppSettingsService.CopyColorList` 的截断上限一致 |
| `Record(List<string>, string)` | 去重 + 最新在前 + 截断 |
| `TryParse(string, out byte×4)` | `#RRGGBB`/`#AARRGGBB` → ARGB 通道 |

## Dependencies
- `Models/AppSettings`（`RecentPenColors`/`RecentHighlighterColors`/`RecentTextColors` —— Shape 颜色刻意保持 session-only，无列表）。
- 消费方：WPF `EditorPage`（笔/荧光笔/文本调色板），WinUI `EditorPage`（`ShowPenFlyout`/`ShowHighlighterFlyout`/文本颜色浮层）。

## Open Threads / Resume Context
- **Status:** complete.

## Important Notes / NEVER Change
- 保持无 UI 依赖（不引用 `System.Windows.Media`/`Windows.UI.Color`）——颜色→hex 格式化留在各 shell。
- 不要在此生成默认色或写入 settings；持久化一律经 `AppSettingsService.Save`。
