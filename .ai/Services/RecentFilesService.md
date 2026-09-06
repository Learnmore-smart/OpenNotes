# RecentFilesService

## Purpose

Stores the OpenNotes library index and legacy text migration data under the compatible Caelum data directory.

- **2026-09-05:** `RecentFileEntry.Color` is an optional hex string. `SetFolderColor` / `NormalizeFolderColor` persist it; missing JSON fields stay empty so the home UI keeps the default amber.

## Important Notes / NEVER Change

- The default root remains `%LOCALAPPDATA%\Caelum`.
- `OPENNOTES_DATA_ROOT` is an explicit test/diagnostic override only; it must not rename or migrate production data.
- File contents and conversation data are unrelated to this service.

## Open Threads / Resume Context

- **Status:** complete
- Missing PDFs stay in `recent_files.json`. Empty index restores from `.bak` then existing `bookmarks.json` PDF keys. `GetLibraryDisplayName` falls back to the file name. Save refuses to replace a non-empty file list with zero files.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-06 | Keep missing library PDFs, restore empty index from bak/bookmarks, `GetLibraryDisplayName`. | Cursor |
| 2026-08-21 | Added the mirror and routed the service through the test-only data-root seam; production remains `%LOCALAPPDATA%\Caelum`. | Codex |
