# OpenNotes.WinUI/Services/Win32Print.cs
> Last updated: 2026-09-26 (G1 print pipeline port) | Protection: STANDARD

## Purpose

Win32 GDI print path — the unpackaged-WinUI stand-in for WPF's
`System.Windows.Controls.PrintDialog` + `dialog.PrintDocument(paginator, name)`
pair. The WinRT `Windows.Graphics.Printing` task pipeline requires packaged
CoreWindow plumbing that is fragile unpackaged, so this shell prints the way
WPF's dialog did underneath: `PrintDlgEx` on the MainWindow HWND picks the
printer / page range / copies, then a printer DC spools each pdfium-rendered
BGRA page through `StretchDIBits` (`Caelum.Services`, all `internal`).

## What it does

- `Win32Print.TryShowPrintDialog(ownerHwnd, pageCount)` → `Win32PrintJob?` —
  `PRINTDLGEXW` with `PD_ALLPAGES|PD_USEDEVMODECOPIESANDCOLLATE|PD_COLLATE`,
  32-slot `PRINTPAGERANGE` buffer, `nMinPage=1`/`nMaxPage=pageCount`. Returns
  null on Cancel/Apply; throws on HRESULT failure (e.g. no default printer —
  surfaces through `Editor.PrintFailed`). No `PD_RETURNDC`: the DC is created
  manually below so copies/collate are deterministic.
- `CreateJob` — reads DEVNAMES strings (driver/device/output), then **lifts
  `dmCopies`/`dmCollate` out of the DEVMODE** (byte offsets 86/100 — stable
  DEVMODEW ABI) into the job and resets them to 1/false before `CreateDCW` —
  the managed loop owns replication so a driver can never double-print.
  `CreateDCW(driver, device, output, devMode)` with a `("WINSPOOL", device)`
  fallback. Reads `LOGPIXELSX/Y`, `HORZRES`/`VERTRES` (the printable rect on
  real printer DCs; `PHYSICALWIDTH/HEIGHT` fallback), sets `HALFTONE`
  stretch mode. `PD_PAGENUMS` ranges clamp into `[1, pageCount]`.
- `Win32Print.PrintPages(job, pages, jobName, ct)` — off-UI-thread spool:
  `StartDocW` → per `PrintablePageImage`, `StartPage` + `StretchDIBits`
  (32bpp BI_RGB, **negative `biHeight`** — pdfium buffers are top-down) into
  the `PrintPageGeometry.FitPageToPrintableArea` rect + `EndPage` → `EndDoc`;
  `AbortDoc` on mid-job failure. Collated = copy-major loop, uncollated =
  page-major. `ct.ThrowIfCancellationRequested()` per page.
- `Win32PrintJob : IDisposable` — owns the printer DC + DEVMODE/DEVNAMES
  handles (`DeleteDC`/`GlobalFree`); `OrderedPageIndexes()` expands ranges
  to 0-based indexes (range order, duplicates kept); `PrinterDpi` = min axis.
- `PrintablePageImage` — raw BGRA + pixel dims/stride + page size in points
  (the WPF twin wrapped a frozen `BitmapSource`; GDI needs the bytes).

## Constraints / NEVER Change

- Do **not** set `PD_RETURNDC` or leave `dmCopies>1` in the DEVMODE given to
  `CreateDC` — the driver-side copy multiplier must stay off; the loop owns it.
- The printer DC coordinate origin is the printable-area corner (GDI
  semantics) — fit math lives in Core `PrintPageGeometry`, never recompute here.
- Keep every entry point `[SupportedOSPlatform]`-guarded and `internal`.
- Spool only off the UI thread (callers wrap in `Task.Run`); pdfium + GDI are
  both serialized there.

## Dependencies

- Consumed by `Pages/EditorPage.xaml.cs` `PrintPdfAsync` (lease-guarded
  overlay/toast/dialog flow). Math: `OpenNotes.Core/Services/PrintPageGeometry`.
- Rasters: `Caelum.Pdf.PdfiumRasterizerFactory` BGRA buffers.

## Open Threads / Resume Context

- `[manual]`: needs a real printer / Print-to-PDF desktop check — headless CI
  covers source contracts (`WinUiPrintSourceTests`) + the Core math only.
- No `AbortProc` dialog (WPF had none either); user cancel = pre-page token.
