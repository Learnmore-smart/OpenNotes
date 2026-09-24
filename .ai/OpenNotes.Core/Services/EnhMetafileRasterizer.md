# OpenNotes.Core/Services/EnhMetafileRasterizer.cs
> Last updated: 2026-09-24 | Protection: STANDARD

## Purpose

Pure-Win32 Enhanced Metafile helpers shared by every UI shell (`Caelum.Services`,
Task 8 Phase B). EMF is a GDI record format — the WPF decoder resolved it via
System.Drawing and WinUI 3 must play the record back into a memory DC instead,
so the rasterizer lives in Core (no UI-framework dependency, headless-friendly)
and returns raw 32bpp BGRA pixels each shell wraps in its own bitmap type.

## API surface

- `IsClipboardEnhMetafileAvailable()` — `IsClipboardFormatAvailable(CF_ENHMETAFILE=14)`.
  WinUI's `DataPackageView` does not surface metafiles, so PowerPoint/Word/CAD
  copies that only publish EMF are invisible without this Win32 check.
- `TryReadClipboardEnhMetafileBytes(out byte[] emfBytes)` — `OpenClipboard` →
  `GetClipboardData` → `GetEnhMetaFileBits` into a self-contained byte[];
  always `CloseClipboard` in `finally`.
- `TryRasterizeToBgra(emfBytes, maxEdge, out bgra, out w, out h)` — dims come
  from `ENHMETAHEADER.rclBounds`, falling back to `rclFrame` (.01 mm → 96 DIP)
  for boundsless records; `maxEdge` (0 = uncapped) scales a hostile metafile
  down. Plays the record into a `CreateDIBSection` top-down 32bpp DIB inside a
  memory DC, `PatBlt(WHITENESS)` white backing first (records that never paint
  a background must not come through transparent black), then normalizes the
  GDI alpha channel to 0xFF. All GDI handles released in `finally`
  (`DeleteObject`/`DeleteDC`/`ReleaseDC`/`DeleteEnhMetaFile`).

## Constraints / NEVER Change

- Win32-only by nature (user32/gdi32) but UI-framework-free — the Core
  assembly still compiles for both WPF tests and WinUI.
- The WinUI caller (`ClipboardImageDecoder`) wraps the BGRA buffer in a
  `SoftwareBitmap` and PNG-encodes; the rasterizer itself never encodes.
