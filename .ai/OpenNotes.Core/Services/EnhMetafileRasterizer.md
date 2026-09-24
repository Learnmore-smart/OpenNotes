# OpenNotes.Core/Services/EnhMetafileRasterizer.cs
> Last updated: 2026-09-24 | Protection: STANDARD

## Purpose

Pure-Win32 Enhanced Metafile helpers shared by every UI shell (`Caelum.Services`,
Task 8 Phase B). EMF is a GDI record format — the WPF decoder resolved it via
System.Drawing and WinUI 3 must play the record back into a memory DC instead,
so the rasterizer lives in Core (no UI-framework dependency, headless-friendly)
and returns raw 32bpp BGRA pixels each shell wraps in its own bitmap type.

## API surface

- `IsClipboardEnhMetafileAvailable()` — `IsClipboardFormatAvailable(CF_ENHMETAFILE=14)`
  behind a `RuntimeInformation.IsOSPlatform(Windows)` guard + try/catch
  (WPF `IsWin32EnhMetafileAvailable` — a throwing clipboard API just means
  "no EMF"; probing must never kill Ctrl+V).
  WinUI's `DataPackageView` does not surface metafiles, so PowerPoint/Word/CAD
  copies that only publish EMF are invisible without this Win32 check.
- `TryReadClipboardEnhMetafileBytes(out byte[] emfBytes)` — OS-guarded;
  `OpenClipboard` → `GetClipboardData` → `GetEnhMetaFileBits` into a
  self-contained byte[]; `MaxEmfByteSize` (256 MB) caps the record stream
  BEFORE `new byte[size]` so a hostile size field can't even attempt the
  alloc; the whole body sits in try/catch → false and always
  `CloseClipboard` in `finally`.
- `TryRasterizeToBgra(emfBytes, maxEdge, out bgra, out w, out h)` — OS-guarded;
  dims come from `ENHMETAHEADER.rclBounds`, falling back to `rclFrame`
  (.01 mm → 96 DIP) for boundsless records; `maxEdge` (0 = uncapped) scales a
  hostile metafile down BEFORE the DIB/pixel-buffer alloc. Plays the record
  into a `CreateDIBSection` top-down 32bpp DIB inside a memory DC,
  `PatBlt(WHITENESS)` white backing first (records that never paint a
  background must not come through transparent black), then normalizes the
  GDI alpha channel to 0xFF. Whole body is try/catch → false (malformed
  record, GDI failure, oversized alloc = "no image", never a throw); all GDI
  handles + `hEmf` released in `finally`.

## Constraints / NEVER Change

- Win32-only by nature (user32/gdi32) but UI-framework-free — the Core
  assembly still compiles for both WPF tests and WinUI. Non-Windows early
  returns ride `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` (the
  `PdfiumRasterizer` pattern).
- The WinUI caller (`ClipboardImageDecoder`) wraps the BGRA buffer in a
  `SoftwareBitmap` and PNG-encodes; the rasterizer itself never encodes.
- The EMF leg's effective edge cap is the CALLER's `maxEdge` argument — the
  WinUI decoder passes 4096 (WPF `RasterizeMetafile` parity), not the 16384
  WIC-leg cap.
