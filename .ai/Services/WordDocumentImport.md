# WordDocumentImport.cs

> Last updated: 2026-09-08 | Protection: STANDARD

## Purpose（一句话人话）

判断一个路径是不是 Word，并算出同目录下不覆盖已有文件的 PDF 目标路径。

## What It Does

- 识别 `.doc` / `.docx` / `.docm`（大小写不敏感）。
- 识别 `.pdf`。
- `BuildSiblingPdfPath`：`作业.docx` → 同目录 `作业.pdf`；若已存在则 `作业 (1).pdf`、`作业 (2).pdf`。
- 不移动、不删除、不改写 Word 原文件。

## Public API / Exports

| Name | Type | Description |
|------|------|-------------|
| `IsWordPath` | static bool | Word 扩展名 |
| `IsPdfPath` | static bool | PDF 扩展名 |
| `IsImportablePath` | static bool | PDF 或 Word |
| `BuildSiblingPdfPath` | static string | 同目录唯一 PDF 路径 |

## Open Threads / Resume Context

- **Status:** complete

## Agent Decisions / Thoughts

- **2026-09-08 Cursor:** 用户选 B（原 Word 不动，资料库只收 PDF）+ A（PDF 和 Word 同目录）。不做 Word 原生编辑器。库索引仍只收 `.pdf`。

## Important Notes / NEVER Change

- 不要把 `.docx` 写进 `RecentFilesService`。
- 不要覆盖已有同名 PDF。
