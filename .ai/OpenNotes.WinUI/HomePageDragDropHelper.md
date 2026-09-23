# OpenNotes.WinUI/HomePageDragDropHelper.cs
> Last updated: 2026-09-23 (V6 Task 5 — library drag/drop payload helper) | Protection: STANDARD

## Purpose
`internal static class HomePageDragDropHelper` — marshals library drag/drop payloads for the WinUI `HomePage`. Replaces WPF `DataObject`/`DataFormats` usage: WinUI uses `DataPackage`/`DataPackageView`, where storage items arrive asynchronously but format presence is synchronous.

## What It Does
- **Custom formats:** `Caelum.LibraryTilePath` (single path) and `Caelum.LibraryTilePaths` (newline-joined multi-selection) — internal library moves only.
- `PackLibraryTilePaths` / `GetLibraryTilePathsAsync` — join/split, trim, dedupe (ordinal-ignore-case), drop blanks.
- `HasLibraryTilePaths` / `HasStorageItems` / `HasSupportedFolderDropPayload` — synchronous `DataPackageView.Contains` probes for `DragEnter`/`DragOver` accept/reject decisions (async checks are impossible inside those events).
- `GetDroppedImportablePathsAsync` — reads `StandardDataFormats.StorageItems`, filters to `WordDocumentImport.IsImportablePath` (pdf/doc/docx/docm), dedupes against the library's existing paths where the caller asks.

## Important Notes / NEVER Change
- NEVER move the `Contains` probes to async — `DragEventArgs` must be answered synchronously; WinUI gives no deferrable format query.
- The custom-format payload is newline-separated plain text (no JSON) — keep the format names stable; a rename silently breaks internal drag.
- External import stays routed through `WordDocumentImport.IsImportablePath` — the library remains PDF-only; Word files convert on import, matching WPF behavior.
