# RecentFilesService

## Purpose

Stores the OpenNotes library index and legacy text migration data under the compatible Caelum data directory.

## Important Notes / NEVER Change

- The default root remains `%LOCALAPPDATA%\Caelum`.
- `OPENNOTES_DATA_ROOT` is an explicit test/diagnostic override only; it must not rename or migrate production data.
- File contents and conversation data are unrelated to this service.

## Open Threads / Resume Context

- **Status:** ready_for_next
- The service now uses `ProductInfo.GetDataDirectory()` so isolated editor smoke can open a PDF without attempting to write the user's AppData.
- JSON/legacy-file names and current pruning/deduplication behavior remain unchanged.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-21 | Added the mirror and routed the service through the test-only data-root seam; production remains `%LOCALAPPDATA%\Caelum`. | Codex |
