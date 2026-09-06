# Services/RecycleBinService.cs

> Last updated: 2026-09-05 | Protection: STANDARD

## Purpose

Moves a library file into the Windows Recycle Bin via `SHFileOperation`. OpenNotes never permanently deletes from disk.

## What It Does

- `EncodeDoubleNullPath` builds the double-null-terminated `pFrom` buffer.
- `TrySendToRecycleBin` uses `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI`.
- Missing paths return false; HomePage then leaves the library index unchanged.

## Important Notes / NEVER Change

- Recycle Bin only. Do not call `File.Delete`.
- Keep `InternalsVisibleTo("OpenNotes.Tests")` coverage for encoding and missing-file failure.
