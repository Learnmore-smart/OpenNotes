# OpenNotes.WinUI/Services/ClipboardImageDecoder.cs
> Last updated: 2026-09-24 | Protection: STANDARD

## Purpose

WinUI port of the WPF `ClipboardImageDecoder` (Task 19/8 Phase B): turns
whatever image payload a source app put on the clipboard into PNG bytes that
`PdfService` persists unchanged (`Caelum.Services`).

## API surface

- `ContainsImage(content, includeWin32Clipboard = true)` — true when the
  `DataPackageView` advertises `"PNG"`/`StandardDataFormats.Bitmap`, or the
  raw Win32 clipboard carries `CF_ENHMETAFILE` (checked through
  `EnhMetafileRasterizer.IsClipboardEnhMetafileAvailable` — `DataPackageView`
  cannot see metafiles).
- `TryGetPngBytesAsync(content, includeWin32Clipboard = true)` — three legs in
  WPF priority order, never throwing on a malformed payload:
  1. `"PNG"` format — bytes returned as-is when the 0x89 PNG magic matches
     (`GetDataAsync` handles stream/buffer/storage-item payloads).
  2. `StandardDataFormats.Bitmap` — `BitmapDecoder` WIC-decode → BGRA
     `SoftwareBitmap` → `BitmapEncoder` PNG re-encode (the WPF DIB leg needs
     no equivalent — `DataPackageView` merges CF_DIB/CF_BITMAP publishers
     into Bitmap).
  3. `CF_ENHMETAFILE` — Core `EnhMetafileRasterizer` reads + rasterizes the
     Win32 handle to BGRA, wrapped in `SoftwareBitmap` and PNG-encoded here;
     the whole leg is try/catch-wrapped (WPF `TryFromWin32EnhMetafile`).
- `MaxDecodeEdge = 16384` caps the WIC Bitmap leg only (a WinUI safety cap —
  WPF's Bitmap/DIB legs are uncapped); `MaxEmfDecodeEdge = 4096` caps EMF
  playback (WPF `RasterizeMetafile` parity — tighter than the WIC leg).
- All image reads are exception-swallowing by contract — a bad clipboard must
  never kill Ctrl+V (`ex is not OutOfMemoryException` filter everywhere).

## Constraints / NEVER Change

- Priority order is load-bearing: PNG > Bitmap > EMF — a bitmap paste wins
  over annotation JSON at the `PasteClipboardImageOrSelection` call site;
  EMF is the last-chance leg.
- `includeWin32Clipboard` exists so tests/non-interactive callers can skip
  the raw-clipboard probe — do not remove the flag.
