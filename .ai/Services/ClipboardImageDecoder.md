# ClipboardImageDecoder.cs

> Last updated: 2026-09-08 | Protection: STANDARD

## Purpose（一句话人话）

从剪贴板拿出一张图（含 Excel 图表的 EMF/DIB/PNG），编码成 PNG 给现有图片注释粘贴。

## What It Does

- `ContainsImage(IDataObject)`：Bitmap / DIB / EnhancedMetafile / PNG 任一存在即为 true（不依赖 WPF `Clipboard.ContainsImage()`）。
- `TryGetPngBytes(IDataObject)`：PNG → Bitmap → DIB → EMF 栅格化；活剪贴板再尝试 Win32 `CF_ENHMETAFILE`。
- EMF 以白底栅格化，避免 Excel 透明背景在 PDF 上发黑。

## Open Threads / Resume Context

- **Status:** complete

## Agent Decisions / Thoughts

- **2026-09-08 Cursor:** Excel 图表经常只有 CF_ENHMETAFILE，WPF `ContainsImage()` 为 false，这是贴不上的根因。不做 Excel 文件导入，只修剪贴板。

## Important Notes / NEVER Change

- 仍写入现有 ImageAnnotation / `wna_img_` / 剥离式保存。
- 不要为了贴图改 PDF 坐标系。
