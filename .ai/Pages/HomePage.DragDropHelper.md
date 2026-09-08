# HomePage.DragDropHelper.cs

> Last updated: 2026-09-08 | Protection: STANDARD

## Purpose

解析首页拖放载荷：库内磁贴路径，以及外部可导入文件（PDF 和 Word）。

## Open Threads / Resume Context

- **Status:** complete
- `GetDroppedImportablePaths` accepts PDF and Word. Folder/home drop converts Word then `AddOrPromote`s the PDF.

## Agent Decisions / Thoughts

- **2026-09-08 Cursor:** 内部库磁贴拖动仍只搬 PDF 路径。外部 FileDrop 才接受 Word。
