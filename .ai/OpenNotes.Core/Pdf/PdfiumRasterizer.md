# OpenNotes.Core/Pdf/PdfiumRasterizer.cs

> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

Direct P/Invoke implementation of `IPdfRasterizer` against `pdfium.dll` —
replaces the `PdfiumViewer` managed wrapper inside Core so the WPF app and the
future WinUI host share one UI-free raster backend.

## What It Does

- `PdfiumRasterizerFactory.Shared` — `IPdfRasterizerFactory`; `LoadFromFile`
  opens a `FileStream` and feeds the stream loader (mirrors
  `PdfDocument.Load(path)`).
- `PdfiumRasterizer(Stream)` — pins a `GCHandle` for the stream, loads via
  `FPDF_LoadCustomDocument` with an `FPDF_FILEACCESS` struct (cdecl
  `m_GetBlock` delegate reads from the managed stream; static delegate field
  prevents GC). Source stream stays open for the document lifetime and is
  disposed in `Dispose`.
- `InitializeFormEnvironment` — probes `FPDFDOC_InitFormFillEnvironment` for
  versions 1→2 over a pinned `FpdfFormFillInfo`, sets the same highlight
  color/alpha (`0xFFE4DD`, 100), then `FORM_DoDocumentJSAction` +
  `FORM_DoDocumentOpenAction`.
- `FpdfFormFillInfo` — `int Version` + 31 `IntPtr` slots in fpdf_formfill.h
  order. `CreateFormFillInfo()` populates all 16 v1 slots with real
  stateless/no-op cdecl delegates (shared static array so they never get
  collected; `m_pJsPlatform` stays null). `FFI_GetLocalTime` returns a pinned
  process-lifetime zeroed `FPDF_SYSTEMTIME`. Never hand pdfium a null
  function pointer — documents with page/document actions may invoke them.
- `RenderPageBgra(pageIndex, w, h, renderAnnotations = true)` — `byte[]`
  pinned + `FPDFBitmap_CreateEx(w, h, FPDFBitmapFormatBgra=4, scan0, w*4)` +
  `FPDFBitmap_FillRect(0xFFFFFFFF)` + `FPDF_RenderPageBitmap` +
  `FPDF_FFLDraw` (annotations via form env, same as
  `PdfRenderFlags.Annotations`; `renderAnnotations == false` skips FFLDraw —
  the old `(PdfRenderFlags)0` path, used by print without annotations).
- `GetPageText`/`GetTextBounds` — `FPDFText_*` calls inside a `PageData`
  scope; char bounds use `FPDFText_GetCharBox` with the same horizontal-run
  merge (top=max, bottom=min, `AreClose < 4f`).
- `RectangleFromPdf` — two `FPDF_PageToDevice` calls at natural page size.
- `PageData` (private) — `FPDF_LoadPage` → `FPDFText_LoadPage` →
  `FORM_OnAfterLoadPage` → `FORM_DoPageAAction(Open)`; dispose reverses:
  `FORM_DoPageAAction(Close)` → `FORM_OnBeforeClosePage` →
  `FPDFText_ClosePage` → `FPDF_ClosePage`.
- `Dispose` — `FORM_DoDocumentAAction(WC)` → `FPDFDOC_ExitFormFillEnvironment`
  → `FPDF_CloseDocument` → free forminfo/stream handles → dispose stream.
  Idempotent; all public methods throw `ObjectDisposedException` after.
- `PdfiumException` — wraps `FPDF_GetLastError` on load failure.

## Dependencies

- `pdfium.dll` resolution: `EnsureLoaded` probes
  `AppDomain.RelativeSearchPath` then the assembly directory, appending
  `x64`/`x86` (same order as PdfiumViewer's NativeMethods static ctor), then
  `LoadLibraryW` + `FPDF_AddRef`.
- **Thread safety:** every native entry point locks `PdfiumNative.SyncRoot`
  — the interned string `e362349b-001d-4cb2-bf55-a71606a3e36f`, the *same*
  monitor PdfiumViewer uses — so both backends serialize on one process-wide
  pdfium lock (pdfium is not thread-safe).
- Structs/imports: `FPDF_FILEACCESS` (uint FileLen + GetBlock fnptr + Param),
  `FpdfFormFillInfo` sequential layout; all imports declared
  `CallingConvention.Cdecl`.

## Important Notes / NEVER Change

- **NEVER** change `LockString` — parity with PdfiumViewer's lock object is
  what prevents the two backends from entering pdfium concurrently.
- **NEVER** let a managed exception escape `FpdfGetBlock` — return 0.
- Keep disposal order identical to `PdfiumViewer.PdfFile.Dispose`.
- Keep `CreateFormFillInfo` callback count in sync with the struct slots.

## Change History

- 2026-09-22: Created for Task 3. Parity-verified byte-for-byte against
  `PdfiumViewer.PdfDocument.Render` (3-page fixture × 96/144/192 dpi),
  identical `GetPdfText`/`GetTextBounds`/`RectangleFromPdf` answers —
  `OpenNotes.Tests/PdfiumRasterizerParityTests.cs`. Also serves
  `EditorPage.RenderPrintablePages` and the insert-pages page-count probe —
  the last two production `PdfiumViewer` call sites (WPF `EditorPage` no
  longer references `PdfiumViewer`; only the parity test does, deliberately).
