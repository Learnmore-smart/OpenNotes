# WordToPdfConverter.cs

> Last updated: 2026-09-08 | Protection: STANDARD

## Purpose（一句话人话）

把 Word 转成旁边的 PDF，失败时原 Word 必须还在。

## What It Does

- `Import(wordPath)`：校验 Word 存在 → 计算 sibling PDF → 调用 `IWordPdfExporter` → 校验 `%PDF` 头。
- 失败删除不完整 PDF，不改 Word 字节/时间戳。
- 默认导出器：Microsoft Word COM（只读打开、`ExportAsFixedFormat`），否则 LibreOffice `soffice --headless`。
- 两者都没有则 `WordConverterNotFoundException`。
- Word COM 在独立 STA 线程上跑，避免卡住 WPF UI 线程。

## Public API / Exports

| Name | Type | Description |
|------|------|-------------|
| `Default` | static | Word COM → LibreOffice |
| `Import` | string | 同步转换，返回 PDF 路径 |
| `ImportAsync` | Task<string> | STA 后台转换 |

## Open Threads / Resume Context

- **Status:** complete

## Agent Decisions / Thoughts

- **2026-09-08 Cursor:** 不用商业 Word SDK。CI/GitHub Actions 没有 Word，生产路径用 ProgID + soffice，测试只打 fake exporter。拒绝在 PDF 编辑器里编辑 .docx。

## Important Notes / NEVER Change

- 原 Word 只读打开，Close/Quit 不保存。
- 剥离式 PDF 注释架构不变：转换后的 PDF 走现有 EditorPage。
