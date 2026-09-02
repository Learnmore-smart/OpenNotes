# Services/PdfService.cs

## Page rotation geometry regression (2026-08-31) — GREEN

- Root cause: structural rotation persisted `/Rotate`, and Pdfium already exposed the correct rotated page size, but PdfSharpCore's `PdfPage.Width`/`Height` also became rotation-aware while `/InkList` remained in the page's raw default user space. Extraction therefore used the wrong Y basis (including negative points at 90 degrees), so the WPF drawing overlay no longer followed the rotated bitmap.
- `PdfPageDisplayGeometry` reads the raw CropBox (falling back to MediaBox), preserves non-zero box origins, normalizes `/Rotate`, and supplies exact reversible 0/90/180/270 mappings between raw PDF coordinates and top-left display DIPs.
- Owned ordinary ink, grouped shape strokes, dashed shape strokes, and Hidden Ink now use that geometry during extraction and the inverse geometry during save. Ink appearance bounds clamp against the raw page box rather than rotation-aware `Width`/`Height`.
- Preserve Pdfium's rotated display dimensions, foreign annotations, strip/rebuild ownership, DIP/PDF scaling, shape metadata, and atomic replacement.
- Verification: the original save → rotate → reload regression is GREEN for swapped aspect and clockwise points; expanded coverage is GREEN for 90/180/270, shape metadata, Hidden Ink, and repeated save/reload without drift.

## Sidebar page reorder (2026-08-30) — GREEN for focused scope

- ReorderPagesAsync is the structural write boundary for sidebar drag/drop. It receives a zero-based final destination index after source removal, preserves full page objects/content, uses the normal path/lifetime/document lease and defensive reload, and remains compatible with snapshot undo/redo in EditorPage.
- Focused forward/backward/end page-order tests pass; parent agent owns full verification and integration.

## Wave6 dual-review follow-up (2026-08-24) — GREEN closure

- Preserve owned Sticky `/NM` identity while preventing duplicate ids from entering a live
  document/page. PDF parse/load and save now maintain a case-insensitive uniqueness registry;
  duplicate/empty incoming ids get fresh GUIDs without dropping text, geometry, size or colour.
  The same stable id remains the UIA `StickyNote.{Id}` identity and sidecar payload survives
  the round trip. Focused duplicate/empty/CJK PDF tests and the full suite are green.
> Last updated: 2026-08-24（Wave6 Sticky Note identity/geometry/colour round-trip）| Protection: STANDARD

## Purpose（一句话）
PDF 核心服务：PdfiumViewer 负责加载/渲染"剥离注释后的干净流"，PdfSharpCore 负责**剥离式加载**（抽取 /Ink /FreeText /Highlight /自有 /Stamp 图片为内存模型）与**写回**（先删旧类注释再重建 + 自建外观流，FreeText 含 CJK 嵌入字体路径，图片走 /Stamp XForm 外观），并管理页面增删/空白页模板。

## What It Does（关键机制，含行号引用）
- **常量/生命周期**：`PdfPointToDipScale = 96.0/72.0`；`_documentLock`（SemaphoreSlim）串行化文档操作，`_lifetimeGate` 与 `DisposeActive/DisposeStarted/DisposeCompleted` 状态机协调保存、加载和异步释放。Dispose 只有在已准入保存完成后才发布 disposing 状态，之后的等待保存会在 native reload/create 前收到 `ObjectDisposedException`。
- **剥离式加载** `LoadPdfCoreAsync`（行 238）→ `LoadPdfDocument`（行 258）：以 Modify 模式开源流，`ExtractAndStripAnnotations(sourceStream, strippedStream, ct)`（行 475-624）产出**剥离后的内存流**，`PdfiumPdfDocument.Load(strippedStream)`（行 281）加载它；剥离失败则回退直接加载原文件（行 291-301 catch，ExtractedAnnotations 置空字典）。
- **ExtractAndStripAnnotations 细节**（行 475-624，scale = 96/72，dipDpi=96）：
  - 遍历每页 `/Annots`：`/FreeText` → `TryExtractFreeTextAnnotation`（行 1404-1443：/Rect 定位（Y 翻转 `pageHeight - rect.Y1 - rect.Height`）、字号依次尝试 /DA 的 `Tf`、/DS /RC 的 CSS，默认 18、颜色 /DA rg → /DS /RC CSS → /C 数组，默认黑；文本取 /Contents → /RC 富文本转纯文本（`ConvertRichTextToPlainText` 行 1458，去 HTML 标签）→ /V）。
  - `/Ink`（当前 ExtractAndStripAnnotations 分支）：/InkList 每条 stroke；`wna_hidden_` 进入独立 HiddenInk 模型并强制 A=255，普通自有 `/Ink` 才按 `/CA` 进入 Stroke；外来 `/Ink` 不被剥离。宽度 /BS/W×scale；每点 `[x*scale, (pageHeight - y)*scale]`（**Y 翻转**）。Hidden Ink 从正值 `/WNARevealMs` 恢复 `RevealDurationMs`，缺失/非正值回退 3000ms。
  - `/Highlight`（行 557-606）：/QuadPoints 每 8 数为一四边形，取 min/max 包围盒转 `[x, pageHeight-maxY 转 y, w, h]`。
  - `/Stamp`（Task 19）：**只认 `/NM` 前缀 `wna_img_` 的自有图片注释**（严格所有权判定——外来 /Stamp 印章/签名留在页面上由 pdfium 渲染）；`TryExtractImageAnnotation`（internal static，测试直调）：/Contents base64 → 原始字节（Format 魔数嗅探 `DetectImageFormat`：PNG 89504E47 / JPEG FFD8FF），/Rect → X/Y/W/H（Y 翻转同 FreeText：`(pageHeight - rect.Y1 - rect.Height) * scale`）；命中即剥离。
  - 抽取成功的注释项从 `/Annots` 数组移除（行 610-613），随后 `document.Save(outputStream)`（行 622）——**PdfiumViewer 渲染的流里已无这些注释**，应用层自绘（剥离式架构核心）。
- **写回** `SaveAnnotationsToPdfAsync` → `SaveAnnotationsCore`：Wave 2 adds a shared `PdfSaveCoordinator` lease around the complete per-path operation, while the instance `_lifetimeGate` → `_documentLock` order coordinates disposal and Pdfium document/backing-stream ownership. Same-path callers serialize; different paths remain independent. Structural writes use the same `RunDocumentWriteAsync` helper, so Insert/Delete/Reorder/Duplicate/Rotate and PDF/image imports cannot bypass the path gate or reload after disposal.
- **写回** `SaveAnnotationsCore`（行 809-1133）：
  1. 整文件读入内存后 `PdfReader.Open(Modify)`（行 824-836，避免文件锁）。
  2. **先删**：每页只移除 OpenNotes 自有 `/FreeText` `/Ink` `/Highlight`/text-markup 和自有 `/Stamp`（分别按 `wna_text_`/`wna_ink_`/`wna_hl_`/`wna_markup_`/`wna_img_` 前缀判定；外来注释保留）——保证幂等且不破坏第三方注释。
  3. `scale = 72.0/96.0`（行 838-839），逐页重建：
     - /FreeText（行 869-939）：多行拆分、`lineHeight = fontSize*1.4`。**双路径**：
       - **CJK 路径**（行 878-883）：文本含非 ASCII 字符（`ContainsNonAscii`）且 `TryCreateCjkFreeTextAnnotation`（行 1239-1338）成功 → 走 XGraphics 路径（见下节"CJK 机制"）；任何失败（无可用 CJK 字体/XFont 异常/测量异常）自动回退 latin 路径。
       - **latin 路径**：优先通过 `XGraphics.CreateMeasureContext`/`XFont` 按真实字形宽度换行和计算对齐偏移；字体不可用时才回退到 `max(len) * fontSize * 0.55 + 12` 估算。当 `TextAnnotation.Width > 0` 时按保存矩形宽度换行，自动高度（Height=0）按换行行数重算；`Width/Height > 0` 的真实矩形使用保存位置，0 值保持旧自动布局。
     - /Ink（行 941-1021）：普通笔迹写入 `wna_ink_`；Hidden Ink 写入 `wna_hidden_`、`/CA 1`、opaque `/AP` 和自定义 `/WNARevealMs`，保留稳定 ID/颜色/宽度/点列；高亮仍使用独立透明度路径。
     - /Highlight（行 1023-1099）：/QuadPoints 按 TL/TR/BL/BR 顺序写四角、/C 颜色、/CA、外观流 `re f` 矩形填充，**/BM /Multiply 混合**（`CreateAppearanceResources` 行 1356-1375，opacity<1 或高亮时挂 /GS1 ExtGState）。
     - **/Stamp 图片（Task 19，行 1109-1156）**：每条 ImageAnnotation → base64 解码 → `XImage.FromStream(() => new MemoryStream(bytes))`（**注意：PdfSharpCore 1.3.67 该 API 收 `Func<Stream>` 工厂而非 Stream 实例**——保存期惰性重读，天然免生命周期管理）→ `XForm(document, (w,h)pt)` + `XGraphics.FromForm` + `DrawImage(ximg, 0, 0, w, h)`（scale=72/96，Y 翻转 `pageHeight - y*scale - h`）→ 反射取 PdfFormXObject（复用 Task 18 `XFormPdfFormProperty`）挂 /AP /N；**原始编码字节 base64 写 /Contents**（自有装载端无损复原的数据通道；/AP 只是给外部查看器的视觉）；/NM `wna_img_{guid}`、/F 4、AddAnnotationToPage 间接对象注册；单图解码/绘制失败仅跳过该图不中断整次保存。
      - **/Text Sticky Note（Task 26）**：`WriteStickyNoteAnnotation` 保留 `StickyNoteAnnotation.Id` 为 `/NM=wna_note_{id}`，Unicode `/Contents`，DIP 尺寸换算后的 `/Rect`，以及附加 `/WNAWidth`/`/WNAHeight` 与 `/C` 颜色元数据。`TryExtractStickyNote` 恢复 Id、DIP 位置/尺寸/RGB；旧自有 PDF 没有附加键时从 `/Rect` 读取并回退默认视觉值，外来 `/Text` 永不剥离。
      4. `AddAnnotationToPage`（行 1135-1155）：注释注册为**间接对象**（PDF 规范 §12.3.3，防 Edge 等严格查看器解析内联字典出错）、补 /Type /Annot /F=4 /P。
  5. **保存到临时文件 + `File.Move(tempPath, filePath, true)` 原子替换**（行 1102-1124）；UnauthorizedAccess/IO 异常翻译成"文件可能被其他程序占用"提示；finally 清理 tmp。
- **CJK 机制（Task 18，2026-08-18）**：
  - **字体解析**：服务在第一次 CJK 导出前探测 Windows 和用户字体目录中的候选 **\*.ttf**，并在可用时安装一次受控的 `OpenNotesPdfFontResolver`；候选链为 **SimHei(simhei.ttf) → DengXian(deng.ttf) → KaiTi(simkai.ttf) → FangSong(simfang.ttf)**，无字体或 XFont 失败则回退 latin `/Helv` 路径。TTC 字体不依赖默认 resolver 的枚举结果。
  - **外观流**：CJK 路径不用手写内容流，而是 `XForm(document, measured size)` + `XGraphics.FromForm` + `DrawString` + `DrawingFinished()`（行 1301-1322）——XFont 用 `XPdfFontOptions.UnicodeDefault`（Unicode 编码）→ PdfSharpCore 自动创建 **PdfType0Font（Identity-H CID）+ 自动嵌入子集（FontFile2）**，文本以字形 ID hex Tj 写入，任何外部渲染器可显示。
  - **宽度测量和换行**：`XGraphics.CreateMeasureContext` + `MeasureString` 按真实字形宽度测量；固定宽度文本调用 `WrapMeasuredTextLines`，显式换行仍保留；自动高度根据 wrapped line count 计算。每行使用 `GetAlignedTextOffset`，因此 Left/Center/Right 在保存矩形内一致。
  - **/Contents 编码**：CJK 路径用 `SetString(key, value, PdfStringEncoding.Unicode)`（行 1295）写 /Contents——普通 `SetString` 走 RawEncoding（**低 8 位截断**，中文必乱码，旧路径的隐藏 bug）；Unicode 编码写成标准 `<FEFF...>` UTF-16BE hex 字符串，PdfSharpCore 读回时自动按 BOM 解码。
  - **/DA**：引用 form 资源里的真实字体名（`GetFormFontResourceName` 行 1340-1354，实测为 `/F0`）+ 字号 + rg 颜色；Caelum 加载端只解析 Tf 字号与 rg 颜色，资源名无影响。
  - **XForm.PdfForm 是 internal**（1.3.67）：用反射取 `PdfFormXObject`（`XFormPdfFormProperty` 行 1199-1200，缓存 PropertyInfo），其 getter 同时把 XObject 注册为间接对象，再经 `CreateAppearanceDictionary` 挂到 /AP /N。
  - **验证结论**（2026-08-18 实测）：保存后 PDF 内 CJK 注释 = Type0 `/YLUDFH+SimHei` + 18KB FontFile2 子集 + `<0596 0FB3 ...> Tj`（"你"=字形1430、"好"=字形4019，重复一致）；/Contents 与 /Rect/字号/颜色往返正确；二次保存不破坏。⚠️ pdfium 2018.4.8 本身不渲染 FreeText /AP（latin 旧路径同样不渲染，既有行为非回归）——Caelum 内部不受影响（剥离式自绘），AP 面向 Adobe/PDF.js 等外部查看器。
- **页面增删/结构写入**：`InsertPageAsync` / `DeletePageAsync` / `ReorderPagesAsync` / `DuplicatePageAsync` / `RotatePageAsync` / `InsertPdfPagesAsync` / `InsertImagePageAsync` 全部进入 `RunDocumentWriteAsync`（path coordinator → lifetime → document lock），再调用各自 Core 和防御性 `ReloadDocumentFromFileAsync`。每条 Core 都在同目录临时文件中完整保存并 flush，随后统一 `PdfAtomicFile.Replace`（底层 `File.Move(temp,target,true)`）；失败清理 temp 且保留原 target。`InsertPdfPagesAsync` 同时持有 source 读与 target 写 lease，`CreateBlankPdfAsync` 也先取得规范化路径 lease，避免新建文档与同路径替换并发。
- **模板** `ApplyPageTemplate`（行 411-431）：`XGraphics` **矢量绘制** Notebook（行 440，米色底+红线边距）/Lined（行 449）/Quadrille（行 456，18pt 网格、每 4 格主次线），非贴图。
- **渲染**：`RenderPageAsync`（行 626，192dpi PNG 走 BitmapImage）；`RenderPagePngBytesAsync`（行 663/668，带 dpiScale=192×scale）；`RenderPageBitmapSourceAsync`（行 734，**GDI LockBits → BitmapSource.Create 直转，绕过 PNG 编解码，快 5-10x**，缩放重渲染走此路径）；`GetPageSizeInDips`（行 127，纯计算不渲染）。
- **文本信息**：`GetPageTextInfoAsync`（行 153）缓存每页字符级 Bounds（`BuildPageTextInfo` 行 175，供 PDF 文本选择层）；`TryGetCachedPageTextInfo`（行 148）。

## Public API / 关键成员（表）
| 成员 | 行号 | 说明 |
|---|---|---|
| `PageCount` | 68 | Pdfium 页数 |
| `ExtractedAnnotations` | 69 | 剥离出的 `Dictionary<int, PageAnnotation>`（EditorPage 装载自绘） |
| `LoadPdfAsync(path, ct)` | 143 | 剥离式加载入口 |
| `SaveAnnotationsToPdfAsync(path, annotations)` | 784 | 写回（删旧重建 + 原子替换；FreeText 含 CJK 嵌入字体路径） |
| `CreateBlankPdfAsync(path, w, h, template)` | 71 | 新建空白/模板 PDF |
| `InsertPageAsync` / `DeletePageAsync` / `AppendBlankPageAsync` | 95/109/90 | 页面增删 |
| `GetPageSizeInDips(i)` | 127 | 页尺寸（DIP，192dpi 渲染等效换算） |
| `RenderPageAsync` / `RenderPagePngBytesAsync` / `RenderPageBitmapSourceAsync` | 626/663/734 | 三条渲染路径 |
| `GetPageTextInfoAsync` / `TryGetCachedPageTextInfo` | 153/148 | 页文本+字符 Bounds 缓存 |

## Dependencies
- PdfiumViewer（渲染/文本）、PdfSharpCore 1.3.67（注释与页面修改；XForm/XFont/XGraphics 用于 CJK 外观与页面模板）、`Models/AnnotationModels`。
- 被 EditorPage（加载/保存/渲染/分页）与 DocumentSnapshotAction（经 ApplyDocumentSnapshotAsync 重载字节流）使用。

## Open Threads / Resume Context
- **Edge compatibility fix (2026-09-02, GREEN):** never read `PdfPage.CropBox` unless `/CropBox` actually exists. PdfSharpCore's getter creates an empty rectangle entry for an inherited/missing box, and saving that entry produces `/CropBox [0 0 0 0]`, which Microsoft Edge 152 displays as a blank page. Geometry reads explicit boxes through `Elements`, removes zero-area boxes, falls back to `/MediaBox`, and every direct PdfSharpCore save path applies the same repair. Valid explicit CropBoxes, rotations, raw origins, strip-and-rebuild annotation ownership, and atomic replacement remain preserved. Real desktop Edge render, 381/381 tests, and a 0-error Release build are green.
- v5.2.6 owned `/Ink` persists optional logical-shape metadata in private `/WNA*` keys. Missing keys remain ordinary legacy ink; foreign ink is never modified or interpreted as an OpenNotes shape.
- **v5.2.6 shape-integrity fix (2026-08-30):** owned `/Ink` annotations persist `StrokeAnnotation.FitToCurve` in `/WNAFitToCurve`. Loading honors that value and conservatively recovers legacy five-point perpendicular rectangles as crisp, non-smoothed shapes so a reload cannot visually turn a square into a circle. Foreign `/Ink` remains untouched. Full suite: 354/354; Release build: 0 errors.
- Checklist renders repeated checkbox rows and TwoColumn renders a central divider with parallel writing rules. Both use the existing vector page-template path; annotation/save/coordinate/atomic replacement behavior is unchanged.
- **Status:** ready_for_next — Wave 2 final save/dispose/structural-write review is green for automated scope.
- `SaveAnnotationsToPdfAsync` and every structural write acquire `PdfSaveCoordinator` before the `_lifetimeGate` → `_documentLock` pair; optional Pdfium reload remains inside both leases and checks `ThrowIfDisposed()` before/after native load. `DisposeAsync` joins admitted work; a path/lifetime waiter fails before reload/create after disposing. A failed disposal restores the active state and replaces its completion source so editor resource release can retry. Focused/expanded save, load/reopen, legacy-white, missing-`/C`, stream-ownership, structural-gate and disposal-race tests are green.
- Public `LoadPdfAsync` also joins the normalized path lease, so external reloads cannot observe a concurrent structural/annotation replacement halfway through. Internal write helpers continue to use the already-held path/lifetime/document order without recursive coordinator acquisition.
- `DisposeCurrentDocument` retains any owner whose native/stream `Dispose` failed while still attempting the other owner, so a retry can release the failed resource instead of losing the reference; editor release marks `_resourcesReleased` only after the service succeeds.
- Both bitmap render paths use `PdfRenderPolicy.CalculateRenderDpi`, so 0.22× thumbnails render at 42 DPI instead of being clamped to 192 DPI. `PdfService` implements idempotent `IAsyncDisposable`; final editor cleanup awaits `_documentLock`, disposes the current Pdfium document/backing stream, and clears extracted state. The direct frozen BitmapSource path and all PDF invariants below are preserved.
- Hidden Ink PDF round-trip tests cover opaque `/CA 1`, `/WNARevealMs`, stable ownership prefixes, geometry, and preservation of foreign `/Ink` annotations.
- Fixed-width Latin and CJK FreeText export now wraps within persisted `TextAnnotation.Width`, derives automatic height when needed, and aligns each rendered line; zero dimensions preserve legacy automatic sizing.
- PDF FreeText now needs explicit `/WNAutoWidth` and `/WNAutoHeight` metadata so zero dimensions survive a save/reload; owned legacy annotations without the metadata remain automatic. External annotations and the strip-and-rebuild ownership rules stay unchanged.
- Preserve strip-and-rebuild, annotation ownership, coordinate conversion, CJK Unicode `/Contents`, and atomic replacement in future changes.

## Agent Decisions / Thoughts
- 剥离式的收益：PdfiumViewer 永远只画"底子"，应用层注释有完整 WPF 交互（选中/擦除/缩放），往返 PDF 不产生双重渲染。
- 外观流手写（StringBuilder 拼 PDF 内容流）而非 XGraphics.MeasureDriver，是为了精确控制 /CA 与 /Multiply——改外观时注意坐标是**注释局部坐标系**（相对 min/min 平移）。CJK FreeText 是例外：它必须走 XGraphics（需要 CID 字形编码），且不需要 /CA 控制。
- CJK 字体选 SimHei 而非微软雅黑：雅黑/宋体是 .ttc，1.3.67 默认 FontResolver 只索引 .ttf；SimHei 是 Windows 中文语言包里最常见的 .ttf CJK 字体（黑体也贴合注释场景）。
- `SetString` 的 RawEncoding 低 8 位截断是 PdfSharpCore 的坑：**任何写含非 ASCII 的 /Contents 都必须用 `PdfStringEncoding.Unicode`**。
- `RenderPageBitmapSourceAsync` 是缩放流畅度的关键路径，勿退回 PNG 往返。

## Important Notes / NEVER Change
- **NEVER**：剥离式注释加载架构（ExtractAndStripAnnotations 抽出 + Pdfium 渲染干净流）。
- **NEVER**：坐标系 DIP↔PDF 换算（加载 scale=96/72 + Y 翻转；保存 scale=72/96 + Y 翻转）。
- **NEVER**：保存"先删三类注释再写入"幂等顺序 + 临时文件 `File.Move` 原子替换。
- **NEVER**：让 `DisposeAsync` 与保存绕过 `_lifetimeGate` → `_documentLock` 顺序；否则等待保存可能在 disposing 后重新创建 native document/stream。
- **NEVER**：注释必须注册为间接对象（AddAnnotationToPage）。
- **NEVER**：CJK /Contents 必须用 `PdfStringEncoding.Unicode` 写（RawEncoding 截断必乱码）。
- 页面增删 Core 与 SaveAnnotationsCore 统一使用同目录 temp + flush + `PdfAtomicFile.Replace`；不要退回直接覆盖 target，否则失败可能留下半文件。
- CJK 字体回退链保持 SimHei 优先；若未来升级 PdfSharpCore（FailsafeFontResolver/TTC 支持），可重估候选链。

## Wave 2 final acceptance follow-up（2026-08-23）

- `PdfAtomicFile.SaveDocument` writes a complete same-directory temp stream and calls `Flush(true)` before replacement. Blank create, annotation snapshot, all page/image structural operations, and editor Save-As use this contract; Save-As admits both old/source and new/target paths in sorted order.
- `PdfSaveCoordinator.RunExclusiveAsync(IReadOnlyCollection<string>)` sorts normalized case-insensitive paths and releases all entries in reverse order. Source reads in page import therefore cannot race a writer, and crossed imports cannot deadlock.
- `RunDocumentWriteAsync` is the single structural lifetime admission helper. `ThrowIfDisposed()` is checked before/after every awaited gate and around reload, so a queued write cannot create/reload a native document after disposal begins.

## V5 Completion Status

- Tasks 25-29 preserve owned standard annotations and rich FreeText styling through the existing strip-and-rebuild path; Tasks 30-37 add outline, page reorder/duplicate/rotate, PDF/image insertion and low-DPI thumbnail rendering.
- Structural page operations remain before/after byte-snapshot operations in EditorPage; atomic save, DIP↔PDF conversion and foreign-annotation preservation are unchanged.
- Open threads: no required V5/Hidden Ink service implementation remains; third-party viewer and online/manual verification are external.

## Change History
- 2026-08-18: 建立镜像文档（Task 0）。
- 2026-08-18: Task 18 —— FreeText CJK 导出修复：XGraphics+XForm 外观流、Type0/Identity-H 嵌入 SimHei 子集、MeasureString 真实宽度、/Contents Unicode 编码（修 RawEncoding 截断乱码）、字体回退链探测；新增 6 个测试（嵌入结构/往返解析/混排零回归/二次保存）。
- 2026-08-18: Task 19 图片注释——保存：SaveAnnotationsCore 先删循环扩自有 /Stamp（IsOwnImageStamp，/NM 前缀 wna_img_；外来 /Stamp 保留）+ 逐 ImageAnnotation 建 /Stamp（XImage.FromStream(Func<Stream>) → XForm/DrawImage /AP + /Contents base64 原始字节，单图失败跳过）；装载：ExtractAndStripAnnotations 增 /Stamp 分支 → TryExtractImageAnnotation（internal static：NM 严格判定 + base64 + /Rect 反变换）→ Images + 剥离；新增 DetectImageFormat 魔数嗅探（internal，EditorPage 复用）；新增 2 个测试（1x1 红 PNG 往返 + 二次保存幂等/外来 Stamp 保留），21/21 通过。
- 2026-08-20: Hidden Ink/PDF text completion——加载恢复 `/WNARevealMs`；固定宽度 FreeText 的 Latin/CJK 路径分别使用 `WrapTextLines`/`WrapMeasuredTextLines`，按真实矩形重算自动高度并应用文本对齐；CJK 字体使用受控候选 resolver；自动化验证通过，桌面/第三方查看器检查仍属外部验收。
- 2026-08-23: Wave 2 save coordination planned: same normalized PDF path serializes across PdfService instances; different paths retain independent concurrency; PDF stream ownership/disposal and strip/rebuild/atomic replacement are protected invariants.
- 2026-08-23: Wave 2 implementation complete for automated scope: shared path lease covers SaveAnnotationsCore plus reload, preserving strip/rebuild, stream ownership, DIP transforms, foreign annotation retention and atomic Move. Eight concurrent service instances produce a readable final PDF.
- 2026-08-23: Wave 2 final review: Insert/Delete/Reorder/Duplicate/Rotate/PDF/image import now use the same path/lifetime/document helper; public loads and `CreateBlankPdfAsync` also join the normalized path lease; defensive reload checks close the native-load window and dispose a rejected native document/backing stream independently; failed disposal is retryable. Structural queued-before/after-dispose and same-path gate tests are green.
- 2026-08-24: Wave6 Sticky Note `/NM` now carries the stable model Id and additive size/colour metadata; owned legacy notes still parse through `/Rect` and default values, while foreign `/Text` annotations remain preserved.
