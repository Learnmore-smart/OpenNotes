# Models/AnnotationModels.cs

## Wave6 dual-review follow-up (2026-08-24) — plan before code

- Sticky Ids must be unique within the active document/page payload. On load or AddStickyNote,
  empty/duplicate ids receive a fresh GUID; copy/duplicate already generate a new identity and
  must preserve all other payload fields. Add a RED malicious-duplicate roundtrip contract.
> Last updated: 2026-08-24（Wave6 Sticky Note identity/geometry/colour persistence）| Protection: STANDARD

## Purpose（一句话）
注释持久化数据模型：内存中的笔迹/文本/高亮与 PDF /Ink /FreeText /Highlight 注释互转的中间形态，也是剪贴板 JSON、版本快照 JSON 和学习模式 Hidden Ink 遮罩的序列化对象。

## What It Does（关键机制，含行号引用）
- `AnnotationData`（行 5-9）：`Version=1` + `Dictionary<string, PageAnnotation> Pages`（**键为字符串页号**，如 "0"；粘贴路径 `PasteSelection` 取 `Pages["0"]`）。
- `PageAnnotation`：`Strokes` / `Texts` / `Highlights` / `Images` / `StickyNotes`，以及独立的 `HiddenInks` List。Hidden Ink 不混入普通笔迹，避免普通橡皮或选择操作误改学习遮罩。
- `StrokeAnnotation`（行 35-48）：`R/G/B`（byte）+ `A=255`、`Size=2.0`（DIP 宽度）、`IsHighlighter`、`FitToCurve`、`Points` 为 `List<double[]>`——**每个点仅 [x, y] 两个元素**（无压力/时间戳，压感只在渲染层用 StylusPoints，不落盘）。缺失的旧 JSON 字段默认 `true`，保持历史曲线渲染。
- `TextAnnotation`（行 67-86）：`Text`、`X/Y`（DIP 左上角）、`R/G/B`、`FontSize=18`、`Width/Height` 和 `Bold/Italic/FontFamily/Alignment`。Width/Height 为 0 表示旧文档的自动尺寸；正值表示可保存、可换行的真实文本框矩形。
- `HighlightAnnotation`（行 40-48）：`Rects` 每项 `[X, Y, Width, Height]`（DIP，Y 为顶部）、默认色 `R=255,G=255,B=0`（黄）、`A=128` 半透明。
- `ImageAnnotation`（Task 19）：`X/Y/Width/Height`（DIP，左上角 + 显式尺寸——装载/粘贴副本按此复原不重新适配）、`Format`（"png"|"jpeg"，仅信息性——解码与 PDF 嵌入都靠魔数嗅探）、`ImageDataBase64`（**原始编码字节** base64——保存 PDF 时原样进 /Stamp /Contents，免重编码无损往返）。
- `HiddenInkAnnotation`（学习模式）：`Id`、`R/G/B/A`（新对象默认不透明中性灰 `#C7CDD4`；已序列化的显式白色 `255/255/255` 原样保留）、`Size=28`（DIP）、`RevealDurationMs`（默认 3000ms）和 `Points`（DIP `[x,y]` 点列）。Reveal 的临时显示状态不序列化，文档重开时遮罩始终恢复为隐藏状态。
- `StickyNoteAnnotation`（Task 26）：稳定 `Id`、`X/Y/Text`、`Width/Height`（默认 36 DIP，旧 JSON 缺失时回退）和 `R/G/B`（默认浅黄）。这些字段同时服务 sidecar、版本/剪贴板 JSON、duplicate/cross-page selection 和 PDF `/Text` round-trip。
- 序列化：`System.Text.Json` 默认行为（属性名 PascalCase）；`VersionControlService` 直接序列化 `Dictionary<int, PageAnnotation>`（**注意键是 int**，与 AnnotationData 的 string 键不同）；剪贴板走 `AnnotationData`。

## Public API / 关键成员（表）
| 类型 | 成员 | 说明 |
|---|---|---|
| `AnnotationData` | `Version` / `Pages` | 版本号=1；Pages 键为 string 页号 |
| `PageAnnotation` | `Strokes/Texts/Highlights/Images/StickyNotes/HiddenInks` | 注释容器；`HiddenInks` 是独立的学习遮罩集合 |
| `StrokeAnnotation` | `R/G/B/A/Size/IsHighlighter/FitToCurve/Points` | Points 元素仅 [x,y]；FitToCurve 旧 JSON 默认 true |
| `TextAnnotation` | `Text/X/Y/R/G/B/FontSize/Width/Height/Bold/Italic/FontFamily/Alignment` | 0 尺寸保持旧文档自动布局；正值保存真实矩形 |
| `HighlightAnnotation` | `Rects([X,Y,W,H])/R/G/B/A` | 默认黄色 A=128 |
| `ImageAnnotation` | `X/Y/Width/Height/Format/ImageDataBase64` | Task 19：原始编码字节 base64（无损往返） |
| `HiddenInkAnnotation` | `Id/R/G/B/A/Size/RevealDurationMs/Points` | 不透明自由手绘遮罩；Points 为 DIP；临时 reveal 状态不落盘 |
| `StickyNoteAnnotation` | `Id/X/Y/Text/Width/Height/R/G/B` | 可编辑便签；稳定身份、DIP 位置/尺寸/颜色，旧 JSON 兼容默认值 |

## Dependencies
- 被 `Services/PdfService.cs`（读写 PDF 注释）、`Services/VersionControlService.cs`（快照 JSON）、`Pages/EditorPage.xaml.cs`（CollectAnnotations/加载/undo）、`Controls/PdfPageControl.xaml.cs`（普通笔迹与 Hidden Ink 渲染）引用。
- `HiddenInkAnnotation.RevealDurationMs` 的默认规则由 `Models/HiddenInkRevealState.cs` 集中定义。
- 无外部依赖（纯 POCO）。

## Open Threads / Resume Context
- v5.2.6 ordinary stroke annotations gain optional logical-shape metadata (`ShapeGroupId`, `ShapeKind`, `ShapePartIndex`, `IsDashedShape`). Empty/default values remain legacy freehand and must round-trip through owned PDF ink, clipboard, undo, and eraser fragments.
- **Status:** ready_for_next
- Wave 2 changes the default only for newly constructed Hidden Ink masks. Existing serialized RGB values (including explicit pure white) remain data, and reveal timers/state remain runtime-only. Focused Hidden Ink/PDF tests pass.

## Agent Decisions / Thoughts
- Stroke 点位不存压力是有意的体积/兼容折衷；压感宽度在收集后由 `PressureEnabled` 后处理进 StylusPoints 宽度，落盘仅存几何。
- AnnotationData（剪贴板，string 键）与 VersionControlService（int 键字典）是**两套不同的序列化形状**，勿混用。

## Important Notes / NEVER Change
- **NEVER**：坐标一律 DIP 96dpi（保存时由 PdfService 以 scale=72/96 + Y 翻转转 PDF 点）。
- `Version=1` 字段是向前兼容锚点，勿删。
- 修改字段名会破坏已存在的版本历史 JSON 与剪贴板格式——改名需迁移逻辑。

## V5 Completion Status

- Tasks 25-29 add `TextMarkups`, `AreaHighlights`, `StickyNotes` and rich `TextAnnotation` fields (`Bold`, `Italic`, `FontFamily`, `Alignment`) with defaults that keep old JSON readable.

## Hidden Ink code-level status（2026-08-20）

- `PageAnnotation.HiddenInks` 与 `HiddenInkAnnotation` 已实现并保持旧 JSON 向后兼容：缺失字段由空 List/default 值补齐。
- Hidden Ink 使用自由手绘点列覆盖关键词；颜色和 alpha 由模型保存，新对象默认不透明中性灰 `#C7CDD4`，显式 legacy white 仍按原值往返，alpha 强制为不透明。
- Sidecar 与 PDF 读写路径均保留稳定 ID、颜色、宽度和点位；PDF 侧使用 `wna_hidden_` 名称前缀与普通 `/Ink` 区分。
- 自动化 solution build/test 已通过；真实交互设备和完整保存重开 UI 回归仍由 Task 48 做人工验收。

## Change History
- 2026-08-18: 建立镜像文档（Task 0）。
- 2026-08-18: Task 19——新增 `ImageAnnotation`（X/Y/Width/Height/Format/ImageDataBase64 原始字节 base64）；`PageAnnotation` 增 `Images` 列表。旧 JSON（版本快照/剪贴板）兼容：新字段默认空列表/null，反序列化旧数据不受影响。
- 2026-08-20: Hidden Ink——新增独立 `HiddenInks`/`HiddenInkAnnotation` 模型，记录不透明遮罩点列、颜色、宽度、稳定 ID 与 3 秒 reveal 配置；临时显示状态不落盘。
- 2026-08-20: 文本框/笔迹持久化——`TextAnnotation.Width/Height`（0=旧自动尺寸）与 `StrokeAnnotation.FitToCurve` 接入现有 JSON、剪贴板、版本快照和 PDF 导出链路；旧文档字段缺失时保持兼容默认值。
- 2026-08-23: Wave 2 新 Hidden Ink 模型默认改为不透明 `#C7CDD4`；显式 legacy RGB（含纯白）继续按原值往返，reveal 状态仍不进入序列化。
- 2026-08-24: Wave6 Sticky Note 增加稳定 Id、36-DIP 默认尺寸和颜色字段；PDF/sidecar/clipboard/undo flows 使用同一模型，缺失字段仍兼容旧便签。
