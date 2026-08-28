# Controls/PdfPageControl.xaml(.cs)

## v5.2.4 ruler constraint follow-up (2026-08-27) — IN PROGRESS

- Replace the one-edge/all-points-near-only snap contract with live four-corner ruler geometry. A stroke approaching the ruler body must end at its first boundary intersection; a stroke beginning inside is rejected; an outside stroke drawn alongside either long edge snaps to the nearer edge.
- Run the ruler constraint before Shift, smoothing, recognition and ink simulation so the final constrained stroke remains one ordinary placement/history action.
> Last updated: 2026-08-24（Wave6 Sticky/transient dual-review GREEN closure）| Protection: STANDARD

## Task 1 selection regression fix (2026-08-26) — GREEN

- **Root cause/fix:** the 290ade1 `HitStroke` tightening kept path/closed-polygon checks but limited its final bounds fallback to strokes with one dimension ≤16 DIP. Broad/open drawings therefore missed interior clicks. The final fallback is now the stroke's own bounds for open strokes only; closed shapes return false after polygon containment so bounding-box corners do not select outside the shape.
- **Same-page semantics:** `HandleCtrlClickToggle` remains the page-local owner. The RED test exercises normal click, Ctrl add, Ctrl empty-click retention, and Ctrl removal on one page; no cross-page accumulation was enabled.
- **Evidence:** focused `ShapeSelectionTests` command was RED 3/7 before the production edit; the routed first-gesture review test was RED 2/8 with the popup consume behavior reverted; final focused GREEN is `8/8`. The integration path selects a real open stroke through the `PdfPageControl` seam. Layer ordering, custom stroke collection, placement/token metadata, and PDF DIP coordinates remain unchanged.
- **Open threads:** none for Task 1.

## Task 2 pending popup dismissal and recognition history (2026-08-26) — GREEN for issue 6

- **Pending input boundary:** when an ink-producing tool popup closes on a page
  pointer, the page may receive the same gesture. Suppress only a stationary
  native ink tap; a move beyond SystemParameters.MinimumHorizontalDragDistance /
  MinimumVerticalDragDistance must keep the full drawing path. Eraser input is
  intentionally outside this guard.
- **Recognition:** InkCanvas_StrokeCollected still owns smoothing and shape
  recognition. A recognized fresh stroke must publish one normal mutation and a
  token/placement-safe add/remove history boundary at the editor layer; the
  intermediate smoothed snapshot must not become a visible Undo step.
- **Compatibility guard:** `StrokeRecognizedEventArgs` defaults to the
  snapshot-replacement path for legacy four-argument callers. Only the real
  `InkCanvas_StrokeCollected` fresh-gesture event opts into the add/remove path.
- **Evidence:** the reviewer regression was intentionally RED with the default
  discriminator set to true; after defaulting it to false and passing true only
  from `InkCanvas_StrokeCollected`, focused production coverage is `16/16` and
  combined shape coverage is `20/20`. Popup dismissal remains outside this slice.
- **Open threads:** none for issue 6; the external pointer smoke boundary remains
  unclaimed.

## Issue 3 outside pen-popup gesture (2026-08-26) — GREEN for focused scope

- **Intent:** consume only a stationary native Pen/Highlighter tap after an outside popup dismissal; preserve the full stroke when its path exceeds WPF's system drag thresholds.
- **Guard boundary:** the pending flag is page-local and is armed by `EditorPage` only for native inking tools. It must be cleared on collection, pointer-up/lost-capture, cancellation, and mode changes. Eraser and custom shape/laser/area-highlight gestures remain untouched.
- **Reviewer follow-up:** a production-path regression covers a PenOnly-blocked mouse down/up that never raises `StrokeCollected`; the next unrelated short stroke is retained. The pending flag is cleared at the end of normal `InkCanvas_MouseUp` and `InkCanvas_StylusUp` paths, while collection-time tap suppression remains unchanged.
- **Overlay boundary:** `EditorPage` arms this page-local flag only when the routed source resolves to the page's own native `InkCanvas`; Hidden Ink and other interactive overlay descendants cannot leave pending state behind.
- **Evidence:** the lifecycle regression was RED at `1 failure / 2 passes` before the pointer-up fix, and the Hidden Ink overlay regression was RED at `1 failure / 3 passes` before the target gate. Focused `EditorPopupDismissalTests` pass `4/4`; `HiddenInkTests` pass `10/10`; relevant `PenOnlyInputTests` pass `1/1`. Expected Pdfium NU1701 and WPF high-DPI WFAC010 warnings remain.
- **Active-popup boundary:** `EditorPage` snapshots whether `_penPopup`/`_highlighterPopup` was open for the active tool before closing transient surfaces; unrelated popup dismissal cannot arm this page-local guard.
- **Evidence:** the unrelated-surface regression was RED at `1 failure / 4 passes` before the active-popup gate. Focused `EditorPopupDismissalTests` pass `5/5`; `HiddenInkTests` pass `10/10`; relevant `PenOnlyInputTests` pass `1/1`. Expected Pdfium NU1701 and WPF high-DPI WFAC010 warnings remain.
- **Open threads:** none for this focused issue-3 scope; parent integration may run the broader suite/build.

## Wave6 Open Thread

- **2026-08-24 shape result:** `ShapeKind` and `BuildShapeOutline` now include Triangle, Diamond, Parallelogram, Pentagon and Hexagon. Each produces one closed, bounded ordinary ink stroke, so existing undo, selection, copy/paste and save behavior is inherited unchanged; Shift applies equal width/height bounds. Production geometry tests pass for all five shapes.

- **Status:** green for focused automated scope. `ImageOverlayCanvas` has a null
  background and only Sticky marker containers are hit-testable; image/markup/area
  visuals remain non-hit-testable so PDF drawing is not swallowed.
- Sticky markers use mouse/stylus capture, page-bounded DIP coordinates, arrow-key
  nudging (Shift large step), explicit move/delete events, a 36-DIP minimum marker,
  UIA name/help/id, and a localized Delete context menu. Quiet setters keep undo/redo
  and selection moves from retaining stale UI references; unload detaches handlers and
  PopupZOrder hooks before Loaded reattaches them.

## Wave6 dual-review follow-up (2026-08-24) — GREEN closure

- **Root cause/fix:** `PdfPageControl` implements `IInteractionCancellation`; Sticky,
  selection/resize, PDF text-selection and page-local drawing captures are cancelled
  idempotently on LostCapture, Escape/deactivation/navigation/reload/unload and inactive host.
  Selection snapshots and Sticky opening positions restore before capture release; normal
  pointer/stylus-up alone emits completion events, so cancellation never adds undo/dirty.
- **Reopen/ownership:** marker handlers remain guarded by one per-container registration;
  exact PopupZOrder ContextMenu hooks have explicit Unfix/Ensure methods used by close/release/
  unload. The null `ImageOverlayCanvas` background and semantic-only Sticky hit-test invariant
  remain unchanged. Focused deterministic STA/source contracts are green; external foreground
  and device checks remain unclaimed.

## Wave 1 quality follow-up（2026-08-23）

- `StrokePlacement` is the ordinary live-stroke undo boundary: it carries the owning `PdfPageControl`, stable token, replacement side, immutable snapshot, live reference, and original collection index. `AddStrokeQuiet`/`RemoveStrokeQuiet` preserve that identity across erase/delete/cross-page undo and insert at the recorded index instead of appending.
- `StrokesErasedEventArgs` now carries removed/added placement lists alongside its legacy stroke lists. `ApplyErasedStroke` records placements before removal and for each fragment after insertion, including net cancellation when a fragment is re-clipped in the same gesture.
- `TryReplaceStrokeQuiet` synchronizes and delegates token/index/side lookup to the production `StrokeReplacementState`; a missing/stale token or side returns `false` without changing the collection. Replacement snapshots copy every point's `PressureFactor` and `DrawingAttributes.IgnorePressure`, so original pressure remains variable while ideal replacements can remain uniform.
- `RemoveStrokeQuiet(StrokePlacement)` resolves the current live `Stroke` by page owner, stable token, and replacement side before removing it; `AddStrokeQuiet(StrokePlacement)` requires the same owning page and matching token/side, then resolves an existing current stroke on that page so shape undo/redo can create a new live reference without duplication. A foreign owner, side conflict, or empty token is a no-op; neither path relies only on the historical `Stroke` reference or appends a duplicate token.
- Cross-page transfer adds an identity-specific `RemoveStrokeQuietExact` and `TryCaptureCurrentStrokePlacement`: before removing a source after shape replacement, the action captures the current live reference; target add/remove and rollback require the expected owner/token/side/reference. An unrelated same-token/same-side target therefore leaves the source intact and cannot be treated as a successful transfer.
- `GetStrokes()` keeps its historical `StrokeCollection` return type for existing read callers but returns a defensive collection copy; mutating that copy cannot add/remove live page strokes or bypass metadata.
- `MoveItemsDirectly` preserves point pressure during page movement. `ClearInk`/`ClearStrokes` clear placement history and the replacement ledger together.

## Purpose（一句话）
单个 PDF 页面的交互控件：以多层 Canvas 承载墨迹（InkCanvas）、文本、高亮、PDF 文本选择、选区变换和学习模式 Hidden Ink，实现点级切割橡皮、框选/套索、拖拽/缩放、拖拽画形状、激光笔渐隐墨迹，以及可点击短暂揭示的遮罩。

## Wave5 surface invariant

Keep `PageGrid`, `PdfImage`, and `PdfImageOverlay` opaque and independent from `WorkspaceBackdrop`; no tint/effect/color matrix/overlay brush may be introduced on PDF image layers. Pixel comparisons across Neutral/Paper/Slate must be identical.

## What It Does（关键机制，含行号引用）
- **层级结构**（PdfPageControl.xaml，自底向上）：`PdfImage`（位图）→ `PdfImageOverlay`（Task 12.2 位图交换覆盖层，与 PdfImage 同布局槽、Opacity 0、不参与命中，仅 SwapPageSource 过程中短暂可见）→ `ImageOverlayCanvas`（Task 19/26 图片与 Sticky 注释层，背景为 null；仅 Sticky marker 子容器命中，图片/markup/area visuals 不命中，避免吞 PDF 绘制）→ `InkCanvas`（墨迹）→ `ShapePreviewCanvas`（形状拖拽虚线预览，不参与命中）→ `TextOverlayCanvas`（文本框）→ `HighlightsCanvas`（持久高亮，不参与命中）→ `PdfTextSelectionCanvas`（PDF 文本选择，默认 Collapsed）→ `SelectionOverlayCanvas`（选区变换层）→ `HiddenInkCanvas`（学习遮罩，仅其 Polyline 子项命中）→ `EraserCanvas`（橡皮光圈指示器 Ellipse）→ `LaserInkCanvas`（**Task 20 激光层，最顶层视觉**；IsHitTestVisible=false，输入由 Laser 模式下 InkCanvas 的 handler 捕获）。外层 RootGrid 有独立投影 Border。
- **Sticky Note marker（Task 26）**：`AddStickyNote` creates a semantic 36-DIP-or-larger note icon with stable Id, editable text tooltip, marker size/colour, AutomationId/Name/HelpText and a right-click Delete menu. Mouse/stylus gestures capture on the marker itself, clamp to page bounds in DIP, and emit one `StickyNoteMoved` event on release; Enter/Space reopens the editor, Delete/context-menu raises `StickyNoteDeleteRequested`, and arrows nudge 4 DIP (Shift=16 DIP). Quiet position/text setters are used by undo/redo, selection moves, copy/paste and duplicate; unloading removes handlers and z-order hooks before reattachment. `ScaleItemsDirectly` persists marker width/height and re-clamps X/Y after resizing; localized marker Automation Name/HelpText and Delete-menu metadata are refreshed with the active catalog.
- **Hidden Ink（学习遮罩）**：`CustomInkInputProcessingMode.HiddenInk` 复用 InkCanvas 的自由手绘采样作为临时预览；抬笔后立即把 Stroke 从普通 `InkCanvas.Strokes` 移除，转换为 `HiddenInkAnnotation`，再在独立 `HiddenInkCanvas` 上渲染为圆头、圆角、纯色 Polyline。新遮罩默认使用不透明中性灰 `#C7CDD4`，已加载的显式颜色（包括 legacy white）保持原值，默认宽度为 28 DIP，alpha 为不透明，因此关键词下方内容在隐藏状态不可见；空白 Canvas 没有背景，不会吞掉普通页面点击。
- **Hidden Ink reveal / eraser**：每个 Polyline 自己接收鼠标与触控笔点击；Hidden Ink 工具或普通模式点击只折叠该遮罩并启动独立 `DispatcherTimer`，使用 `RevealDurationMs`（默认 3000ms）到期后重新显示。重复点击会先停止旧 timer 再重新计时。切换到 Erasing 后由底层 InkCanvas 接管拖拽擦除，擦除命中用线段与膨胀矩形相交测试，避免仅用轴对齐 bounds 误删斜线遮罩；一整个拖拽手势累积所有移除项，抬笔/抬鼠标时一次性发出 `HiddenInksRemoved`。清空页面时同时停止所有 reveal timer、移除视觉和模型项。Reveal 仅为会话态，不改变保存数据。
- **位图两层交换**（Task 12.2，`SwapPageSource`，OnPageSourceChanged 唯一入口）：直接 `PdfImage.Source = 新位图` 会闪（旧位图释放/新位图尚在解码合成 + HighQuality 重插值瞬间可见）。两层交换：新位图先入 `PdfImageOverlay`（Opacity 0.001——全透明视觉可能被渲染走查跳过，必须保证预热帧真实合成位图；0.1% alpha 视觉不可见）→ 链式两次 `BeginInvoke(Render)`（每次在该帧渲染 pass 前执行，即预热 2 帧）→ 同帧 `Overlay.Opacity=1` + `PdfImage.Source=新位图`（覆盖层全遮盖，底下换源不可见）→ 下一帧清 overlay（Source=null/Opacity=0；主图已完整渲染新位图一帧）。**generation 计数器**（`_pageSourceSwapGeneration`）每次 PageSource 变更自增，旧回调比对失配即 no-op（快速缩放/滚动连发时旧交换链安全作废，主图始终持有最后**已提交**位图）。布局零变化：两 Image 均 Stretch=Uniform 于固定尺寸页内。初始渲染（LoadPdf/滚动懒渲染）与缩放重渲染（ReRenderPagesAsync）共用此 DP 回调路径。`PageSource=null`（卸载清空）直接清两层。
- **自定义墨迹集合**（.cs 行 150-159）：`private readonly StrokeCollection _strokes` 直接赋给 `InkCanvas.Strokes`——**防止 WPF 在 EditingMode/可见性切换时清空笔迹**（注释行 150-151 明示）。构造器把 `EditingMode/EditingModeInverted` 均设为 `None`（行 177-178），禁用原生倒置橡皮，走自定义逻辑。
- **压力**：`PressureEnabled` 属性（行 166，默认 true）控制 `SetInkAttributes`（行 1033-1042）克隆属性后的 `IgnorePressure = !PressureEnabled`——关闭压感=均匀线宽，开启=设备压感+墨水模拟均可产生线宽变化；构造器初始 `_drawingAttributes` 仍 `IgnorePressure=false`（行 205 附近）。
- **墨水模拟**（行 263-326）：`InkSimulationEnabled`（行 187）开启时 `InkCanvas_StrokeCollected` 对**非高亮、点数≥3** 的笔迹先跑 `ApplyInkSimulation` 再发事件——用相邻点距离作速度代理（StylusPoint 无时间戳，采样率近似恒定），按笔迹内最大速度归一，PressureFactor 映射：慢→1.0（粗，上限）、快→0.25（细，下限）；重建 StylusPointCollection + 克隆 DrawingAttributes 生成替换 Stroke，`_strokes[index] = replacement` 原位换入，并把**替换后的 Stroke** 传给 StrokeCollectedUndoable（undo 引用与集合内对象一致）。
- **Shift 直线约束（Task 21）**：`InkCanvas_StrokeCollected` 最前（PreserveTapStroke 之后、形状识别/墨水模拟之前）若 `IsShiftHeld()`（`(Keyboard.Modifiers & Shift)==Shift`，"含 Shift"语义）→ `StraightenShiftStroke`：整笔替换为首尾两点直线 Stroke（attrs 克隆保 Color/Width/IsHighlighter，`FitToCurve=false`），`_strokes[index]=replacement` 原位换入（ApplyInkSimulation 同款模式，**单步 undo 覆盖、无额外 undo 事件**）；2 点笔迹天然跳过形状识别（<8 点门）与墨水模拟（<3 点门）——用户显式意图优先于启发式。**已知限制（有意为之，per-stroke 而非 live）**：拉直与否由抬笔瞬间 Shift 状态决定，不追踪笔中途松/按 Shift；WPF InkCanvas 逐点收集无法低成本逐点拦截。
- **直尺吸附（Task 22）**：`InkCanvas_StrokeCollected` 最前（PreserveTapStroke 之后、Shift 拉直/形状识别/墨水模拟之前）且 `_currentMode == Inking`（仅 Pen/Highlighter 自由手写；Shape/Laser 手动提交不走此事件、橡皮不收集，gate 显式化契约）时跑 `SnapStrokeToRuler`：查询 `GetRulerEdgeInPageCoords` delegate（EditorPage 在 LoadPdf 建页时注入，闭包内 `TranslatePoint` 实时把 viewport 坐标的尺边换算到该页 RootGrid 坐标——滚动/缩放/移尺后永不过期；null=尺隐藏）；非 null 时先 `RootGrid.TranslatePoint` 转成 InkCanvas 坐标（stylus points 的度量系），**两遍扫描**：第一遍算每点到线段的距离（t clamp 到 [0,1]，超出尺端的行程计入距离）取 max，≥24px（`RulerSnapTolerancePx`）整笔不动；第二遍把每点投影到线段（t clamp 保 PressureFactor），克隆 DrawingAttributes 生成替换 Stroke，`_strokes[index]=replacement` 原位换入（ApplyInkSimulation 同款模式）→ 沿尺的完美直线；替换后笔迹继续走既有管线（InkMutated dirty + StrokeCollectedUndoable 单步 undo + GetStrokeData 保存全自动）。**吸附目标是尺的上边缘而非中线**（决策见下）。退化为单点的 tap（PreserveTapStroke 补点后 2 点）靠近尺边也会吸附为贴边短线段，行为合理。
- **笔迹平滑（Task 24）**：`StrokeSmoothingLevel` 属性（0=关/1=低/2=中/3=高，默认 2，EditorPage 每次 ApplyToolToAllPages 从 AppSettings.StrokeSmoothing 同步并 Math.Clamp）。`InkCanvas_StrokeCollected` 处理链位：**尺吸附 → Shift 拉直 → 平滑 → 形状识别 → 墨水模拟**（平滑在尺吸附/Shift 拉直之后——它们的输出是用户意图轨迹（共线点均值仍共线，无害）；在识别/墨水模拟之前——降噪后的点对识别的几何门更有利，模拟读最终点集）。`ApplySmoothing(stroke)`：**档 0（关）**点集原样，但把笔迹原位替换为 attrs 克隆 `FitToCurve=false` 的副本（WPF 的 FitToCurve 是渲染期曲线拟合，不关它"关"档看到的仍是平滑曲线；已 false 的笔迹（如 Shift 拉直线）跳过替换）；**档 1-3** 滑动窗口均值 w=1/2/4（每侧邻居数，索引端点 clamp），newPts[i]=avg(pts[i-w..i+w])，**PressureFactor 取原中心点**，attrs 克隆 FitToCurve=true 原位换入（ApplyInkSimulation 同款 `_strokes[index]=replacement` 模式——单步 undo、保存管线全自动）。防护：count<3（tap/拉直线）不动；**短笔迹有效窗口钳制 w≤(count-1)/2**（否则窗口≥笔长时所有输出点坍缩到质心把笔画缩成点）。平滑对 Pen/Highlighter 均生效（无 IsHighlighter 门——平滑的是轨迹而非墨水）；高亮笔迹同样平滑。设置入口在 pen popup 分段行（EditorPage），设置页入口 Task 38。
- **涂鸦形状识别**（Task 4，#region 行 1108-1445）：`ShapeRecognitionEnabled`（行 219，EditorPage 每次 ApplyToolToAllPages 从设置同步）开启时 `InkCanvas_StrokeCollected`（行 320）对**非高亮、点数≥8** 的笔迹**先于墨水模拟**跑 `TryRecognizeShape`（命中则整体替换、跳过墨水模拟）。流程：点集→纯点 bounds（非 GetBounds，后者含笔宽膨胀）→ diag<24px 拒 → 周长与闭合判定（首尾距 <0.15·perimeter）→ 开口走 `LooksLikeLine`（到首尾弦的平均垂距 <6%·diag）；闭合走 `LooksLikeRectangle`（方向桶游程法，**先于椭圆**）再 `LooksLikeEllipse`（质心距离 circularity>0.82 + 排序 atan2 角覆盖 ≥300°）。命中→ 复用 Task 3 的 `BuildShapeOutline` 生成理想形状（line=原始首尾点；rect/ellipse=bounds 对角点，椭圆中心取 bounds 中心）→ 克隆原 DrawingAttributes 改 FitToCurve=false/IgnorePressure=true → `ReplaceRecognizedStroke` 以同一 session `Guid` token 和不可变 original/ideal snapshots 原位替换，通过 `TryReplaceStrokeQuiet` 验证 token/side，成功才发 `InkMutated` + **`StrokeRecognized`**（事件只携带 token/index/snapshots，代替 `StrokeCollectedUndoable`）；缺 token 永不追加 ideal。矩形识别（`LooksLikeRectangle`）：逐点 5 点窗方向→`DirectionBucket` 量化为 4 个 45° 桶（mod 180°，上下同桶、垂直差恒为 2 桶）→ 连续同桶游程 → 丢弃 <6% 点数的噪声游程并合并同桶邻居 → **恰 4 条主导边**（闭合笔迹从边中起笔时首尾两条同桶游程 wrap 合并算 1 边）→ 相邻边桶号差恒为 2（垂直）→ 游程覆盖 ≥80% 点数 → 每边平均垂距 <6%·边弦长 → 检测角（相邻边过渡区中点）与 bounds 四角**双向**就近匹配 <12%·diag（拒菱形/梯形/旋转矩形）。
- **形状工具**（Task 3，#region 行 869-1064）：`CustomInkInputProcessingMode.Shape`（枚举行 15）时 InkCanvas `EditingMode=None` + 命中开启 + Cross 光标，由控件自处理输入——StylusDown（行 691，倒置笔/侧键 shouldErase 优先仍走擦除）→ BeginShapeDrag（CaptureStylus），StylusMove/Up 更新/提交；鼠标路径走 MouseLeftButtonDown（行 784）/MouseMove/MouseUp（CaptureMouse），且鼠标处理器以 `e.StylusDevice != null` 守卫跳过笔触提升合成的鼠标事件（触控提升走 Stylus 分支，与自由手写行为一致）。预览：`ShapePreviewCanvas` 上的 Polyline（DashArray 4,2、颜色=ShapeColor、Opacity 0.6、StrokeThickness=max(1,ShapeStrokeSize)），箭头为 2 条（线身+头部 V）；拖拽中只重赋 Points 不重建元素。提交 `CommitShape`（行 1021）：Line=2 点 / Rect=5 点闭合 / 椭圆=64 段参数多边形 / 箭头=线身 2 点 + 头部 V 3 点共 **2 个 Stroke**；DrawingAttributes `{FitToCurve=false, IgnorePressure=true, IsHighlighter=false}`（每笔 Clone），手动 `InkCanvas.Strokes.Add` + `InkMutated` + 逐笔 `StrokeCollectedUndoable`——形状笔迹即普通 Stroke，选区/undo/复制粘贴/GetStrokeData 保存加载管线全自动。拖拽 <4px（ShapeDragThreshold）视为点击不提交；SetInputMode 离开 Shape 模式时清拖拽状态与预览层。**Shift 约束（Task 21）**：`UpdateShapeDrag`/`EndShapeDrag` 两个入口统一经 `ConstrainShapeEndpoints(start, end, kind, isShift)`（静态 helper）——Line/Arrow：方向吸附最近 45° 倍数（`round(atan2/45°)·45°`，终点按原长度重投影）；Rect/Ellipse：正方/正圆（side=max(|dx|,|dy|)，拖拽方向符号保留，sign(0) 取 +）；isShift=false 原样返回。**preview 与 commit 共用同一 helper（一处计算）**，所见即所得；阈值判定用原始 position（约束前）。
- **激光笔（Task 20，#region "Laser pointer"）**：`CustomInkInputProcessingMode.Laser` 时 InkCanvas `EditingMode=None` + **命中开启**（捕获手势）+ Cross 光标——与 Shape 同款输入契约。输入处理镜像 Shape 的管线：StylusDown/Move/Up + MouseDown/Move/Up（鼠标处理器 `e.StylusDevice != null` 守卫），`BeginLaserStroke`（CaptureStylus/CaptureMouse）/`UpdateLaserStroke`/`EndLaserStroke`。**无 pen-only 守卫（有意）**：鼠标激光是主用例，ShouldBlockNonPenInk 不适用。绘制：普通 Polyline（激光红 #FF3B30、粗 3、Round caps/join、IsHitTestVisible=false）落在 `LaserInkCanvas`，live 追加点（<0.5px 重复点跳过）；抬笔（EndLaserStroke）→ `DoubleAnimation` Opacity 1→0（900ms，BeginTime 150ms 延迟）→ Completed 移除 Polyline。**live 上限 60 条**（`MaxLiveLaserPolylines`，超出立即移除最旧）。**关键隔离（20.2）**：激光笔迹纯视觉——不碰 `InkCanvas.Strokes`、不发 `InkMutated`/`StrokeCollectedUndoable`/`StrokeRecognized`、不 MarkDirty、不推 undo；保存路径 `GetStrokeData`（只读 InkCanvas.Strokes）/CollectAnnotations 天然不含激光层（20.3 已 code-reading 验证）。SetInputMode 离开 Laser 时清 in-flight 拖拽状态（`_isLaserDrawing=false`），**已在渐隐的 polyline 不动**（自行移除）；ClearAllAnnotations 亦不清激光层（自愈式设计，最大隔离）。
- **点切割橡皮**：`EraseStrokesAtPoints(StylusPointCollection)`——先 `CreateEraserRects`（每触点生成 `_eraserSize` 见方矩形）取并集预筛候选笔迹（`GetBounds().IntersectsWith`），再分模式：**像素模式**（`WholeStrokeEraser=false`，默认）用 `PointHitsEraser` 确认有采样点落入橡皮，命中则 `ClipStrokeByErasers` 按**采样点级切割**（被覆盖点丢弃，剩余连续段 >1 点重建为带克隆 DrawingAttributes 的新 Stroke）；**整笔模式**（`WholeStrokeEraser=true`，行 180）只要 `stroke.GetBounds()` 与任一**单个**橡皮矩形相交即整笔移除（`ApplyErasedStroke(stroke, 空列表)`，无碎片）。倒置笔/侧键路径同走此方法，自动遵循模式。
- **擦除手势累积（undo 数据源）**：每次修改走 `ApplyErasedStroke`（行 852）——按手势（stylus/mouse down→up）懒初始化累积 `_eraseGestureRemovedStrokes/_eraseGestureAddedStrokes`；同手势内被再次切割的碎片会从 added 列表**抵消**（净变化语义）；手势结束（StylusUp 或 MouseUp）由 `EndEraseGesture`（行 876）触发 `StrokesErased` 事件（payload=移除原笔迹+新增碎片；事件为橡皮模式无关设计，整笔擦除=removed 无 fragments）。
- **倒置笔擦除**：`_isStylusInverted`（行 129-134）——笔倒置时无视当前工具直接进入擦除（华为 M-Pencil 走此信号路径）。
- **单点补线** `PreserveTapStroke`（行 232-243）：单击只产生 1 个 StylusPoint 时补一个 +0.1 的点，否则 WPF 不显示"点"。
- **手指识别** `IsTouchFinger`（行 196-208）：`TabletDeviceType.Touch` 且 stylus 按钮 ≤1 视为手指，放行给 WPF manipulation 做 pan/zoom（笔迹只响应真笔）。
- **仅笔绘制（Task 15，防误触）**：`PenOnlyMode` 属性（EditorPage 每次 ApplyToolToAllPages 从设置同步）开启且当前模式为**普通墨迹创建**（`IsInkCreationModeActive` = Inking 或 Shape；Hidden Ink 明确保留鼠标/笔双输入，橡皮/选区/文本/PDF 文本选择不受限，鼠标擦除仍可用）时，非笔设备禁止落墨。判定 `ShouldBlockNonPenInk(device)`：device==null（纯鼠标，WPF 中鼠标不会提升为 stylus 事件）或 `IsTouchFinger`（真手指：Touch 型 ≤1 按钮）→ 阻断；真笔（含华为 M-Pencil 类 Touch 型多按钮 pen-as-touch）→ 放行（与笔滚动/PDF 文本选择的设备判别一致）。阻断技术（构造器 AddHandler，handledEventsToo:true，与 InkCanvas 同生命周期免退订）：① `InkCanvas_PreviewMouseLeftButtonDown_PenOnly`——WPF InkCanvas 鼠标落墨始于 MouseLeftButtonDown，preview 置 Handled 即抑制冒泡事件、集合不启动（笔接触提升的鼠标事件 StylusDevice 非空，放行）；② `InkCanvas_PreviewStylusDown_PenOnly`——InkCanvas 原生从 StylusDown 收集含触摸提升在内的 stylus 输入，须在冒泡前拦（自由手写的单指触摸本已被 EditorPage 上游 `PdfScrollViewer_PreviewTouchDown` 拦为平移，此处主要覆盖 Shape 工具的触摸 + 兜底）；③ 双保险显式守卫：`InkCanvas_StylusDown` / `InkCanvas_MouseLeftButtonDown` 的 Shape 分支各加 `PenOnlyMode && ShouldBlockNonPenInk` 早退（preview 拦截失效时块仍成立）。触摸平移不受影响：scroll manipulation 由 EditorPage 层驱动，stylus 事件 Handled 不取消 touch 驱动的 manipulation。
- **选区**：
  - 过滤器 `SelectionFilter { Both, DrawingsOnly, TextOnly }` 与形状 `SelectionShape { Rectangle, FreeForm }`（行 17-18）。
  - 判定（行 1500-1568）：矩形选区要求 `selRect.Contains(stroke.GetBounds())` / `selRect.Contains(containerRect)`——**GetBounds 完全包含才选中**（行 1542、1554）；自由套索用 `IsRectInsidePolygon`（行 1585，四角均在多边形内）。
  - **Ctrl+点击多选**（Task 7）：`SelectionOverlayCanvas_MouseLeftButtonDownCore(point, fromStylus)` 开头检测 `Keyboard.Modifiers` 含 Ctrl（仅鼠标路径；StylusDown 传 `fromStylus:true` 绕过）且置于手柄/拖拽判定**之前**（Ctrl+点已选 bbox 内部=切换下层项而非开始拖拽）→ `HandleCtrlClickToggle`：文本优先（TextOverlayCanvas.Children **倒序 z 序**，bounds 含点击点）→ 未中文本再查笔迹（InkCanvas.Strokes 倒序，`GetBounds().Contains(point) || stroke.HitTest(point, 2)`）；命中已选项→移除（清空则 ClearSelection），未选中→追加 + UpdateSelectionVisuals；点击空白→保持现选区不变；不启动框选、不捕获鼠标。跨页 Ctrl+点击由 EditorPage.PdfScrollViewer_PreviewMouseDown 委托路径先清旧页选区再切换新页命中项。
  - 视觉 `UpdateSelectionVisuals`：**静态虚线整体框**（`StrokeDashArray {3,2}`，accent #2563EB，半透明填充 18 alpha）+ 4 角 12px 白心手柄（TL/TR/BL/BR，光标 SizeNWSE/SizeNESW），`_resizeHandleIndex` 0-3 对应；**逐项 marching-ants 描边**（Task 6，`AddPerItemOutline`）：每个选中 stroke（`GetBounds()` 外扩 3px）/文本容器（Canvas.GetLeft/GetTop + ActualWidth/Height，fallback RenderSize，外扩 3px）一个虚线 Rectangle（StrokeThickness 1.2、DashArray {3,2}、无填充、`IsHitTestVisible=false`、`Tag="perItemOutline"`），当前实现不再以 200 项截断，选择集合中的每个条目都有独立描边；动画由**单一 `CompositionTarget.Rendering` 驱动**（`StartSelectionDashAnimation`/`StopSelectionDashAnimation`/`SelectionDashAnimation_Tick`，仅选区存在期间订阅）：每帧推进共享 `_selectionDashOffset`（15 units/s，mod 5 周期）写回所有逐项 rect 的 StrokeDashOffset（render-only，无布局失效），颜色按 1.5s 半周期硬切换 accent #2563EB ↔ cyan #0891B2（冻结静态画刷，`ReferenceEquals` 比较避免重复赋值）；tick 内 `IsLoaded` 兜底退订防静态事件泄漏，选区清空（ClearSelection / UpdateSelectionVisuals 空选区 / 框选落空自动清空）即停止并退订。
  - 变换：拖拽整体移动 + 四角等比缩放；完成后触发 `SelectionMoveCompleted`/`SelectionResizeCompleted` 事件（EditorPage 据此建 undo action）。`MoveItemsDirectly`（行 1072）/`ScaleItemsDirectly`（行 1107）是 undo 重放用的直接变换入口（缩放同时调 TextBox FontSize）。
  - 鼠标/触摸笔事件均有 `Core` 版本，供 EditorPage 跨页委托转发（`InvokeSelectionMouseMoveCore` 等）。
- **PDF 文本选择层**：`SetPdfTextSelectionEnabled`（行 804-823）启用时禁 InkCanvas 命中并 IBeam 光标；`SetPdfTextSelectionRects`（行 825）绘制 80 alpha 蓝色圆角矩形。
- **图片注释（Task 19）**：图片以 **Grid 容器（Tag="imageContainer"）** 落在 `ImageOverlayCanvas`（InkCanvas 之下），**注册进既有选区管线**（进入 `_selectedTextContainers` 列表，与文本容器同型）。`AddImage(byte[], Point, double? explicitWidth, double? explicitHeight)`：BitmapImage（CacheOption.OnLoad 解码后脱流+Freeze）→ 显式尺寸（装载/粘贴副本路径）或按 40% 页宽高保纵横比自适应 → Grid 包 Image（Stretch=Uniform，IsHitTestVisible=false）→ 顶层左角落点并 clamp 页内 → 入 `_imageContainers` 列表 + `_imageDataById`（Dictionary<Grid, byte[]> 原始编码字节，保存时免重编码）→ 发 `ImagesChanged`（EditorPage → MarkDirty；装载期由 EditorPage `_isLoadingAnnotations` 抑制）。**集中式容器移除**：`RemoveTextContainerQuiet`/`AddTextContainerQuiet` 感知层级（image 容器 ↔ ImageOverlayCanvas，文本 ↔ TextOverlayCanvas）——所有 undo/删除/跨页动作走同一 API；quiet 移除只从 `_imageContainers` 删列表项、**字典保留 payload**（同页 undo 重加免数据搬家）；跨页转移由 EditorPage `SelectionCrossPageMoveAction.TransferImageData` 显式搬运字节（字典 per-control）。`ClearAllAnnotations` 整体清层+列表+字典。选区集成：框选完成（矩形/套索两分支）与 Ctrl+点击（HandleCtrlClickToggle，文本→图片→笔迹的 z 序）都迭代 `ImageOverlayCanvas.Children`；`ScaleItemsDirectly` 对 image 容器缩放 **Grid Width/Height**（非 FontSize——image 容器无 TextBox）；移动（Canvas.SetLeft/Top）、逐项描边、删除、GetSelectionBounds 均容器通用（bounds 用 ActualWidth 失效时回退显式 Width，粘贴即选时首帧 bbox 正确）。
- **高亮**：`AddHighlightAnnotation`（行 1643，A=120 固定半透明）/`AddHighlight`（行 1662）→ `RenderHighlightVisual` 自绘到 HighlightsCanvas。
- **区域高亮预览**：`BeginAreaHighlightDrag` 的虚线边框/填充都使用当前 `AreaHighlightColor` 与 `AreaHighlightOpacity`；因此主拖拽预览与 EditorPage 的 6 模式 preview（fill alpha 76、stroke alpha 220）共享生产 opacity，不再保留独立的 48 alpha。
- **模式**：`SetMode(bool isTextMode)`（行 795-802）仅文本工具下 TextOverlayCanvas 参与命中；`SetInputMode(CustomInkInputProcessingMode)`（行 1327）切换 None/Inking/Erasing/Shape/Laser 并更新橡皮光圈样式（行 640-665）；离开 Shape 模式时清形状拖拽状态与预览层；离开 Laser 模式时清激光拖拽状态（渐隐中的 polyline 不动）；Laser case 与 Shape 同款（命中开启 + EditingMode=None + Cross 光标）。
- **事件**（行 107-118）：`InkMutated`、`StrokeCollectedUndoable`、`StrokesErased`（擦除手势完成，带 StrokesErasedEventArgs：RemovedStrokes/AddedStrokes）、`StrokeRecognized`（形状识别命中，带 `StrokeRecognizedEventArgs` 的 token/index/original snapshot/ideal snapshot，**代替** `StrokeCollectedUndoable` 发出；EditorPage 据此推仅保存 token/snapshot/index 的 `StrokeReplacedAction`）、`ModeChanged`、`SelectionChanged`、`SelectionMoveCompleted`、`SelectionResizeCompleted`、文本/PDF 文本选择的 Pointer 系列。

## Public API / 关键成员（表）
| 成员 | 行号 | 说明 |
|---|---|---|
| `PageSource`（DP）/ `PageIndex` | 76-85 | 位图源与页索引（PageSource 赋值走 SwapPageSource 两层交换防闪，见机制段） |
| `TextOverlay` | 87 | TextOverlayCanvas 直通 |
| `SetInputMode(mode)` | 1327 | None/Inking/Erasing/Shape/Laser/HiddenInk（HiddenInk=独立学习遮罩输入模式） |
| `SetInkAttributes(attrs)` | 1033 | 克隆并强制 FitToCurve，IgnorePressure=!PressureEnabled |
| `WholeStrokeEraser` | 180 | 整笔擦除模式开关（EditorPage 每次 ApplyToolToAllPages 从设置同步；倒置笔/侧键路径自动遵循） |
| `ShapeMode` / `CurrentShape` / `ShapeColor` / `ShapeStrokeSize` | 198-207 | 形状工具配置（EditorPage ApplyToolToAllPages 全页同步；会话级不持久化） |
| `ShapeKind`（enum） | 22 | Line/Rectangle/Ellipse/Arrow（命名空间级） |
| `InkSimulationEnabled` | 187 | 墨水模拟开关（StrokeCollected 后处理 PressureFactor，见"墨水模拟"段） |
| `ShapeRecognitionEnabled` | 219 | 涂鸦形状识别开关（EditorPage 每次 ApplyToolToAllPages 从设置同步；见"涂鸦形状识别"段） |
| `PenOnlyMode` | ~247 | Task 15 仅笔绘制开关（同上从设置同步；见"仅笔绘制"段——设备过滤 + preview AddHandler 阻断技术） |
| `StrokeSmoothingLevel` | Task 24 | int 平滑档 0=关/1=低/2=中(默认)/3=高（同上从设置同步；见"笔迹平滑"段——ApplySmoothing 滑动窗口均值 + Off 档 FitToCurve=false） |
| `GetRulerEdgeInPageCoords` | Task 22 | `Func<(Point A, Point B)?>` delegate：返回当前直尺画边（该页 RootGrid 坐标），null=尺隐藏；EditorPage LoadPdf 注入（闭包内 TranslatePoint 实时换算），StrokeCollected 时查询做吸附 |
| `SetMode(isTextMode)` | 795 | 文本层命中开关 |
| `SetSelectionMode/SetSelectionFilter/SetSelectionShape` | 1016 起 | 选区配置 |
| `SetEraserSize` | — | 橡皮直径（EditorPage 每次工具切换同步） |
| `AddStroke(StrokeAnnotation)` / `AddStrokeQuiet` / `RemoveStrokeQuiet` | 996-1016 | 笔迹装载（加载注释 / undo 静默版） |
| `AddHighlightAnnotation(rects,color)` / `AddHighlight` | 1643/1662 | 高亮 |
| `AddImage(bytes, pos, w?, h?)` | Task 19 | 图片注释装载（返回 Grid 容器；显式尺寸或 40% 页自适应；Tag="imageContainer"） |
| `ImageContainers` / `GetImageData(Grid)` / `SetImageData(Grid, bytes)` / `RemoveImageData(Grid)` | Task 19 | 图片容器列表与原始字节存取（保存收集/跨页转移与事务回滚用）；`IsImageContainer(Grid)` internal 判定 |
| `ImagesChanged` 事件 | Task 19 | AddImage 时发出（EditorPage→MarkDirty，装载期抑制） |
| `MoveItemsDirectly` / `ScaleItemsDirectly` | 1072/1107 | undo 重放变换 |
| `GetSelectionBounds()` / `HasSelection` / `SelectedStrokes` / `SelectedTextContainers` | 1147-1180 | 选区查询 |
| `SelectItems(IEnumerable<Stroke>, IEnumerable<Grid>)` | 1938 | 批量选区入口（Task 8.2 粘贴自动选中）：清空后整批填入双列表，空则走 ClearSelection，非空走 RefreshSelectionAfterToggle（= 框选完成路径：重建 bbox+逐项描边+手柄 + SelectionChanged） |
| `GetStrokeData()` / `GetHighlights()` | — | 收集保存数据；`GetStrokeData` 保留 `DrawingAttributes.FitToCurve`，`AddStroke` 按该字段还原 |
| `AddHiddenInk` / `AddHiddenInkQuiet` / `RemoveHiddenInkQuiet` | Task 49 | 装载与 undo 重放使用的静默路径；重复 ID 会生成新 ID |
| `GetHiddenInkData()` | Task 49 | 返回 Hidden Ink 的深拷贝列表；临时 reveal 状态不包含在结果中 |
| `HiddenInkCreated` / `HiddenInkRemoved` | Task 49 | 新遮罩与擦除移除事件；EditorPage 用于 dirty/undo 接线 |
| `HiddenInksRemoved` | Task 49 | 一次擦除手势结束时批量报告被移除的 Hidden Ink 深拷贝；EditorPage 为整个手势推一个 undo action |
| `HiddenInkMaskColor` / `HiddenInkSize` / `HiddenInkRevealDurationMs` | Task 49/Wave 2 | 新遮罩的纯色、DIP 宽度和 reveal 时长（默认中性灰 `#C7CDD4`、28 DIP、3000ms；已加载显式颜色不被覆盖） |
| `SetPenService(WindowsPenService)` | 214 | 注入笔服务，同步 Pressure/TiltEnabled |
| `PressureEnabled` / `TiltEnabled` | 143/148 | 压感/倾斜开关（属性，默认 true） |

## Dependencies
- `Services/WindowsPenService`（设备探测/能力）、`Models/AnnotationModels`。
- 被 `Pages/EditorPage.xaml.cs` 大量使用（工具应用、undo 重放、注释装载）。

## Open Threads / Resume Context

 - **Wave 2 complete for automated scope:** New Hidden Ink masks use opaque `#C7CDD4` in model/control/editor defaults. Existing loaded annotation RGB values remain untouched; reveal is still visibility/timer-only. `RenderHiddenInkVisual` retains round caps and isolated HiddenInkCanvas hit testing. Expanded coordinator/Hidden Ink/PDF tests pass 32/32 and full suite passes 127/127 with no ownership/coordinate changes.
 - **Wave 1 quality follow-up:** Shape recognition uses session `Guid` tokens plus immutable snapshots, but erase/delete/cross-page undo must also preserve placement identity (token, side, original index and page owner), pressure/IgnorePressure, and protected stroke access. `ReplaceRecognizedStroke` must continue replacing in place and return failure without appending when the token is absent.
- **Status:** performance lifecycle complete.
- `SetHostActive(false)` stops the selection `CompositionTarget.Rendering` subscription, Hidden Ink reveal timers, and transient laser visuals without mutating annotations; reactivation rebuilds selection visuals. `SetBitmapScalingMode` lets EditorPage use cheaper interpolation during motion. First/eviction renders assign the bitmap directly, while replacements keep the guarded two-layer swap. `Unloaded` performs the same transient cleanup to avoid static-event retention.
- Control-side implementation for ordinary ink, selection, ruler, PenOnly, laser, smoothing, text/image containers and Hidden Ink is complete; the current solution builds with 0 errors and 5 documented warnings.
- Manual WPF/device interaction and third-party viewer behavior remain external checks; do not reintroduce the old area-highlight/PdfService compile-error note.

## Agent Decisions / Thoughts
- 橡皮选择"采样点级切割 + 段重建"而非 InkCanvas 原生 `EraseByPoint`：可控且保留 DrawingAttributes（含高亮属性）。
- `_strokes` 自定义集合是为了绕开 InkCanvas 在 EditingMode 切换时替换 Strokes 引用的 WPF 行为（历史 bug 修复）。
- 选区"完全包含"语义意味着画一半的笔迹不会被选中——如未来要改"相交即选"，需评估对拖拽语义的影响。
- Task 2/5 决策：整笔擦除的命中判定用 `stroke.GetBounds()` 与**单个**橡皮矩形相交（spec 语义，比采样点命中宽松——包围盒角碰到橡皮也算）；墨水模拟的速度代理用相邻点距离（StylusPoint 无时间戳），且在 StrokeCollectedUndoable 事件前完成 Stroke 替换，保证 undo action 引用集合内真实对象。
- Task 3 决策：**箭头 = 2 个 Stroke（线身 + 头部 V）→ 2 步 undo**。单 Stroke 连续折线会把两翼起点连线画出错误横线，故拆分；不做 50ms 批量合并（复杂度不值）。Line/Rect/Ellipse = 1 Stroke 1 步 undo。指针抬起即提交（<4px 忽略），不做 ESC/右键取消（从简）。形状配置（子类型/颜色/粗细）会话级不持久化（spec 无要求）。新输入模式走 `CustomInkInputProcessingMode.Shape` 而非复用 None/Inking：需要"InkCanvas 命中开启 + EditingMode=None"的组合，现有两值都做不到。
- Task 4 决策（阈值与事件设计）：
  - **矩形先于椭圆检测**：近正方形的矩形 circularity ≈0.89 > 0.82 也能过椭圆门，若先查椭圆会把方块吸成圆；而圆/椭圆的方向桶游程 ≥8 条不可能过矩形的"恰 4 条主导边"门，先查矩形互不误伤（spec 未定顺序，此为实现决策）。
  - **方向桶 mod 180°**（上/下同桶）：垂直方向恒差 2 桶，"相邻边桶号差=2"即严格垂直判定；同时"上下往返描同一条线"合并为单游程被"恰 4 边"自然拒绝。
  - 阈值取值：闭合 0.15·perimeter、线平均垂距 6%·diag、椭圆 circularity 0.82 + 300° 覆盖、噪声游程 <6% 点数、覆盖 ≥80%、边直度 6%·弦长、角容差 12%·diag（双向匹配）。调参目标（spec）：随手直线/毛糙圆/认真矩形 → 识别；乱涂/锯齿 → 不动。锯齿波幅 <12% 长度时会被拉直（此时视觉上本就近直线，可接受）。
  - **理想形状几何**：line=原始首尾点；rect/ellipse=原始**纯点 bounds**（非 GetBounds——后者含笔宽膨胀会放大形状）；椭圆中心取 bounds 中心而非 centroid（与 rx/ry=bounds/2 自洽，spec 允许 v1 近似）。轴对齐 v1：旋转矩形/菱形/梯形被角匹配门有意拒绝（理想形状是 bounds 拟合，非旋转拟合）。
  - **事件设计**：命中时发带 token/snapshot/index 的 `StrokeRecognized` **代替** `StrokeCollectedUndoable`（否则 undo 只删 ideal 不还原 original）；InkMutated 照发（dirty）。undo 语义在 EditorPage 的 snapshot-only `StrokeReplacedAction`，其 quiet replacement 在用户擦除/其它动作已移除 token 后安全 no-op。识别先于墨水模拟（命中即整体替换，模拟无意义）。
  - 识别入口即 InkCanvas_StrokeCollected 既有后处理位（与墨水模拟同位），鼠标绘制笔迹同样生效。
- Task 6 决策（逐项动画描边）：
  - **单一 CompositionTarget.Rendering 驱动**而非每 rect 一个 Storyboard/DoubleAnimation：N 个动画钟在 50+ 项时开销与 GC 压力不可控；复用 EditorPage 滚动动画的既有 tick 模式（DateTime.UtcNow 计时）。每 tick 只写 StrokeDashOffset（render-only DP，无布局失效）。
  - **颜色循环走 LIGHT 版硬切换**：spec「颜色循环」的完整色相旋转复杂度高，按任务指示降级为 accent #2563EB ↔ cyan #0891B2 每 1.5s 半周期硬切换（ColorAnimation-free）；两色为**冻结静态画刷**（frozen Freezable 无 inheritance-context 跟踪，200 rect 共享零开销），tick 内 `ReferenceEquals` 判断仅在切换帧重赋 Stroke。
  - **逐项描边不截断**：每个当前选中的 stroke/text container 都创建独立虚线描边；整体 bbox 和四角手柄仍保留。性能变化应通过后续虚拟化/采样设计解决，不要悄悄丢选中项的视觉反馈。
  - **泄漏防护三重**：ClearSelection / UpdateSelectionVisuals 空选区分支 / 框选落空自动清空三处显式 Stop+退订；tick 内再兜底 `!IsLoaded`（页控件被移出可视树且未走 ClearSelection 时，防静态 Rendering 事件永久 root 控件）。Start 内先 `-=` 再 `+=` 防重复订阅。
  - 重建时机：拖拽/缩放期间 UpdateSelectionVisuals 每次 mouse-move 整体重建（既有行为，逐项 rect 只是按比例增加），重建时以当前 `_selectionDashOffset`/颜色相位初始化，动画视觉不重置。
  - 纯视觉改动：逐项 rect `IsHitTestVisible=false`，选区命中/移动/缩放逻辑零改动。
- Task 7 决策（Ctrl+点击多选）：
  - **文本优先命中**：TextOverlayCanvas 位于 InkCanvas 之上，同点同时命中时文本视为最顶层视觉（Children 倒序 z 序扫描）；笔迹命中 = `GetBounds().Contains(point) || stroke.HitTest(point, 2)`——bounds 覆盖闭合形状内部点击，2px 直径 Stroke.HitTest 提供墨迹路径附近容差。
  - **v1 限定同页**：Ctrl+点击只在当前选中页内切换/累加；点其他页（EditorPage 委托路径 PreviewMouseDown）先 ClearSelection 旧页再切换新页命中项——点新页空白 = 清空旧选区且无新增（自然行为）。_activeSelectionPage 经 SelectionChanged 事件自动迁移到新页。
  - **仅鼠标**：StylusDown 传 fromStylus:true 绕过 Ctrl 分支（笔+Ctrl 组合罕见，笔触保持经典框选）；e.Handled 由既有包装器（MouseLeftButtonDown/StylusDown）与委托路径统一置位，Core 方法无需事件参数。
  - Ctrl 分支置于手柄/拖拽判定**之前**：Ctrl+点击已选 bbox 内部时切换下层项而非开始拖拽（「已有选区时 Ctrl+点击累加」的必要语义，spec 7.2）。
  - 移除后选区为空走 ClearSelection()（视觉清理+SelectionChanged(false)+停动画），非空走 UpdateSelectionVisuals()（重建 bbox+逐项描边+SelectionChanged(true)）——与框选完成路径行为一致；多选移动/缩放/复制/删除/剪切经核查均已迭代 _selectedStrokes/_selectedTextContainers 双列表，无需改动。
- Task 12.2 决策（位图两层交换）：
  - **预热帧 Opacity=0.001 而非 0**：全透明视觉可能被 WPF 渲染走查整体跳过（不合成位图），预热帧就失去"先解码/合成新位图"的意义；0.1% alpha 视觉不可见且必然走完整合成路径。这是 spec 原案（opacity 0）的健壮性修正。
  - **2 帧预热而非 1 帧**：`BeginInvoke(Render)` 回调在该帧渲染 pass 前执行；2 帧保证覆盖位图异步解码 + GPU 上传的调度窗口。之后 reveal（opacity 1）与主图换源**同帧**（覆盖层全遮盖，换源不可见），再下一帧清覆盖层（主图已完整渲染新位图）。
  - **generation 计数器防连发竞态**：快速缩放/滚动时多条交换链交错，旧回调全部按代际失配 no-op；主图始终持有最后一次**已提交**位图，覆盖层随时可被新一轮 staging 覆写。无定时器、无 Storyboard（不做 120ms 渐隐——立即清即可，主图与覆盖层该帧内容相同）。
  - **布局恒定是前提**：两 Image 同 Grid 槽 + Stretch=Uniform + 页根固定宽高（LoadPdf 由 GetPageSizeInDips 设定），换源只换像素不换几何——任何尺寸抖动都会让覆盖层错位露馅。
- Task 15 决策（仅笔绘制的设备判别）：
  - **不按任务原文的 `TabletDevice.Type != Stylus` 一刀切**：本代码库已明确记录（IsTouchFinger 注释、EditorPage 笔滚动启发式）华为 M-Pencil 等 pen-as-touch 设备报 Touch 型但多按钮，且在全 App 均按"笔"对待（笔滚动、PDF 文本选择均用 IsTouchFinger 判别）。一刀切会让仅笔模式把华为笔也拦掉。故采用 `ShouldBlockNonPenInk(device) = device == null || IsTouchFinger(device)`：纯鼠标（WPF 无鼠标→stylus 提升，故 StylusDevice==null 即真鼠标）与真手指（Touch 型 ≤1 按钮）阻断，真笔与 pen-as-touch 放行——语义即任务本意（防误触：手掌+鼠标），设备判别与全库一致。
  - **三重阻断**（preview AddHandler ×2 + 冒泡 handler 显式守卫 ×2）：InkCanvas 鼠标落墨始于 MouseLeftButtonDown、stylus 落墨（含触摸提升）始于 StylusDown，两个 preview 各拦一头；冒泡侧守卫是 preview 失效时的双保险。自由手写的单指触摸本就被 EditorPage 上游拦为平移（Pen/Highlighter/Eraser 在 `PdfScrollViewer_PreviewTouchDown` 的 e.Handled 列表内），InkCanvas 层的 stylus 拦截主要补 Shape 工具的触摸缺口（Shape 不在上游拦截列表）+ 兜底。
  - **仅作用于墨迹创建**：`IsInkCreationModeActive`（Inking/Shape）——橡皮（鼠标擦除有用）、选区、文本、PDF 文本选择对全部设备照常；且 SetInputMode(None) 下 InkCanvas 不参与命中，preview handler 天然不触发。
- Task 20 决策（激光笔）：
  - **普通 Polyline + Canvas 而非 InkCanvas 动态 Stroke**：激光笔迹必须"永不进入文档"，独立 `LaserInkCanvas` 是最强隔离（InkCanvas.Strokes 通道物理上碰不到）；Polyline + DoubleAnimation 是 WPF 最轻量的自愈视觉，Completed 移除后引用全断、零泄漏。
  - **live 上限 60 条 + 最旧先丢**：渐隐周期 ~1.05s（150ms 延迟 + 900ms 动画），正常使用远达不到 60；上限只防极端快速涂画（动画堆积 + Children 无界增长）。
  - **抬笔前不动画（live 全不透明）**：任务语义"书写后 ~1s 渐隐"——书写过程中轨迹持续可见（讲解指引），抬笔才开始倒计时；若 live 即渐隐，长笔画尾段会先消失、违背"激光笔"心智。
  - **激光不进 ClearAllAnnotations**：该 API 语义是清"注释"（持久内容），激光是 ephemeral 视觉且自愈（≤1.05s 后必然消失），版本恢复等场景无需等它。
  - **触摸允许画激光**（无 pen-only 守卫，任务明示鼠标允许；Laser 也不在 EditorPage 上游触摸平移拦截列表，单指直接画激光——讲解场景手指指点反而合理）。
- Task 21 决策（Shift 约束）：
  - **形状：约束收口在 UpdateShapeDrag/EndShapeDrag 两个输入入口**（_shapeCurrent 存约束后的值），preview 与 commit 自动一致（"一处计算"）；阈值判定仍用原始 position，避免约束后的短笔画被误判为拖拽失败。Rect/Ellipse 的 sign(0) 取 +1（纯垂直/水平拖拽时正方/正圆仍有完整边长）。
  - **自由手写：per-stroke 整笔拉直而非逐点拦截**——WPF InkCanvas 原生收集管线逐点拦截复杂（需接管 EditingMode 或自绘 collection），v1 按任务指示用 StrokeCollected 时刻 Shift 状态整笔替换为首尾两点直线；拉直置于形状识别/墨水模拟**之前**（2 点笔迹天然被两个后处理的点数门跳过——用户显式意图优先于启发式）。替换走 ApplyInkSimulation 同款 `_strokes[index]=replacement` 原位换入，undo 单步覆盖（StrokeAddedAction 直接引用替换后的 Stroke）。
  - **高亮笔同样拉直**（spec 明示画笔/荧光笔都要）：attrs 克隆保 IsHighlighter=true，荧光直线半透明属性不受影响。
- Task 22 决策（直尺吸附，本控件侧）：
  - **吸附目标=尺的上边缘而非设计原文的"过中心线"**：尺身高 56px，若 snap 到中线，用户沿可见边缘画的线会跳到 28px 外的中线位置（视觉错误）；且 24px 容差 < 28px 半高，中线方案下沿边画的笔迹**根本进不了容差门**（互相矛盾）。上边缘方案：边缘外 0~24px 可画可吸附、尺身被尺元素自身遮挡（物理直觉：笔不能穿过尺子）、旋转 180° 自然换用另一条物理边，全方向可用。
  - **delegate 查询式（Func 每次调用实时 TranslatePoint）而非静态/快照段**：尺是 viewport 锚定的、内容会滚动缩放、尺本身可拖可转——任何缓存的线段都会过期；查询式保证 StrokeCollected 时刻的坐标系恒正确。delegate 由 EditorPage 注入（每页一个闭包），本控件只拿到 RootGrid 坐标再自行 TranslatePoint 到 InkCanvas（stylus points 的度量系，防未来层间 offset）。
  - **两遍扫描（先全点判定后投影）而非逐点边判边投**：必须整笔都在容差内才吸附——半截靠尺半截远离的笔迹不应被掰弯；判定用**点到线段（t clamp）**的距离而非点到无限直线，超出尺端的行程计入距离，防止沿尺延长线远端的笔迹被拉回。
  - **投影保留全部采样点与 PressureFactor（不合并为首尾 2 点）**：与 Shift 拉直不同，沿尺画的线要保留笔速/压感信息（墨水模拟照常工作产生粗细变化——更像真笔沿尺画）；t clamp 到 [0,1] 保证线不超出尺两端。投影后笔迹继续走全部后处理：形状识别可能把直线识别成 line 理想形（视觉一致，无害）、墨水模拟照常。
  - **`_currentMode == Inking` gate**：事件本身只在 EditingMode=Ink 时触发（Shape/Laser 是 EditingMode=None + 手动提交，橡皮不收集），gate 是显式化契约的防御（未来若 Inking 模式被更多工具复用，吸附语义仍限定在自由手写）。
- Task 24 决策（笔迹平滑，本控件侧）：
  - **"关"档也做一次原位替换（FitToCurve=false attrs 克隆）而非直接 return**：WPF 的 FitToCurve 是渲染期 Bézier 拟合，InkCanvas 默认属性链（SetInkAttributes 强制 true）下不替换属性的话"关"档看到的仍是平滑曲线——必须显式关掉才是"原始轨迹"；已 false 的笔迹（Shift 拉直线）短路跳过免多余替换。
  - **短笔迹有效窗口钳制 w≤(count-1)/2（对 spec 原文的保护性补充）**：端点 clamp 的滑动均值在窗口 ≥ 笔长-1 时每个输出点都等于全笔均值（质心坍缩），3-9 点短笔画会被缩成点；钳制后短笔画退化为轻度平滑（如 3 点笔 w=1），无坍缩。正常长度笔画（count > 2w+1）行为与 spec 原文完全一致。
  - **平滑不设 IsHighlighter 门（与墨水模拟/形状识别不同）**：平滑的对象是轨迹几何而非墨水语义，荧光笔迹的抖动同样该被抹平；高亮的半透明属性在 attrs 克隆中原样保留。
  - **PressureFactor 取原中心点而非窗口均值**：压感是设备物理信号，均值化会把压力变化抹掉导致粗细变化丢失；位置均值 + 压力原值兼顾平滑与笔锋。
  - **不做 Douglas-Peucker 抽稀（spec 可选项，跳过）**：窗口均值已达成"四档可感知"目标；抽稀会改变点密度影响墨水模拟的速度代理，v1 不值。

## Important Notes / NEVER Change
- **NEVER**：`InkCanvas.Strokes = _strokes` 的自定义集合模式（防 EditingMode 切换清空笔迹）。
- **NEVER**：`SetInkAttributes` 的 `IgnorePressure = !PressureEnabled` 联动（Task 5 后压感开关真实生效；勿回退为硬编码 false）。
- **NEVER**：层级顺序（Ink 在文本层之下、选区/橡皮覆盖层在最上、高亮与文本选择不参与命中）——命中测试语义依赖此顺序。
- `EditingModeInverted = None` + 自定义 `_isStylusInverted` 逻辑勿回退为原生倒置橡皮。

## OpenNotes Completion Pass

- **Status:** in_progress
- Text geometry is persisted by EditorPage/PdfService, not by the PDF bitmap layer. Keep image/ink/selection layer ordering and page DIP coordinates unchanged.

## Wave 1 quality follow-up

- Production tests must exercise `PdfPageControl` on an STA thread and invoke the actual nested undo actions (or a single production-used pure core), not only a parallel fixture or source-string contract.
- Every stroke undo action that removes/re-adds a live stroke must carry `StrokePlacement` identity: stable token, original side, original index and owning page. Cross-page transfer creates an explicit target placement and restores the source placement before shape undo.
- Snapshot points preserve `PressureFactor`; snapshots preserve `DrawingAttributes.IgnorePressure` so freehand undo restores pressure-sensitive rendering while ideal shapes remain uniform.
- `GetStrokes()` must no longer expose the live mutable collection; existing callers must retain read/enumeration compatibility while mutations go through quiet APIs.

## V5 Completion Status

- Tasks 25-27 add text-markup, sticky-note and rectangular-area-highlight overlay behavior while preserving the existing DIP coordinate, selection, movement and deletion contracts.
- `SelectAllAnnotations` covers strokes, text, images and custom overlay containers for Ctrl+A.
- Open threads: no required V5 control implementation remains; Wave5 render-hash coverage confirms the PDF bitmap is unaffected by Neutral/Paper/Slate workspace backdrop selection.

## Hidden Ink code-level status

- 普通 InkCanvas、选择层和 HiddenInkCanvas 彼此隔离：隐藏遮罩不会进入普通笔迹集合，普通选择/擦除逻辑也不会意外修改答案。
- `SetDocumentInputEnabled(bool)` 是 EditorPage 关闭/导航 admission 的输入闸门：禁用页面控件的 WPF 输入但保留渲染，避免排队的 ink/text/selection mutation 在最终快照后进入模型。
- `HiddenInkCreated` 与 `HiddenInkRemoved` 只在用户创建/单个点击移除时触发；拖动擦除使用 `HiddenInksRemoved` 一次性发送整个手势的深拷贝列表。装载和 undo/redo 使用 quiet API，避免重复产生 undo 命令。
- PDF 页的 HiddenInkCanvas 位于选择层之上、橡皮指示器之下；只有 Polyline 路径可命中，页面空白区域仍交给普通编辑/滚动输入。
- Hidden Ink 擦除使用线段-矩形相交（Liang–Barsky clipping）而不是只比较点或轴对齐 bounds，斜向遮罩也能按真实几何命中。

## Change History

- 2026-08-28: Selection gestures capture their originating input route (stylus vs mouse) and all completion paths release both capture kinds under the cancellation guard. The ruler provider now supplies both long edges/body; collected ink starting inside is rejected, crossings clip at first body entry, and parallel strokes snap to the nearer edge before smoothing/history.
- 2026-08-28 review: exact boundary starts are outside the strict interior test, enabling the primary along-edge gesture; a following point inside the body still rejects the gesture.

- 2026-08-26: `QuietStrokeMutation` now fires only after successful quiet add/remove/replace operations used by undo, redo, delete, erase, paste, and cross-page transfer. EditorPage uses it for page-local thumbnail invalidation without dirty/history side effects.
- 2026-08-24: Wave5 review closure keeps the PDF display layer independent from workspace decoration. Laser fade and selection-dash animation now consume `ThemeService.GetAnimationDuration`/`ShouldAnimate`, while `PdfImage` and `PdfImageOverlay` remain bitmap-only hosts. The real STA `PdfPageControl` composite probe wraps a known non-white bitmap and annotation overlay in Neutral/Paper/Slate workspace parents and confirms the page crop is byte-stable; this is complementary to the PDF service hash contract and must not be replaced by tinting the page.
- 2026-08-24: Wave5 review chrome follow-up routes eraser/text-selection/selection-handle visuals through live `ThemeAccentBrush`, `ThemeFocusBrush`, `ThemeSelectionBrush`, and `ThemeSurfaceBrush` resource references. The marching-ants phase may switch between the current accent/focus resources, but no frozen blue/cyan brush captures a palette; PDF bitmap pixels and user annotation colors remain untouched.
- 2026-08-18: 建立镜像文档（Task 0）。
- 2026-08-20: Hidden Ink（Task 49）——新增 HiddenInkCanvas、HiddenInk 输入模式、3 秒点击 reveal、擦除移除事件、定时器清理和独立模型 API；保存/加载由 EditorPage/PdfService 负责。
- 2026-08-20: Hidden Ink 完成回归——拖动擦除改为手势级批量 `HiddenInksRemoved`，使用线段与膨胀矩形相交判定；`FitToCurve` 在 GetStrokeData/AddStroke 间往返；逐项选择描边移除原有 200 项上限。
- 2026-08-18: Task 1——新增 StrokesErased 事件 + StrokesErasedEventArgs（removed/added 净变化 payload，橡皮模式无关）；擦除管线经 ApplyErasedStroke 做手势级累积（同手势碎片再切割自动抵消），EndEraseGesture 在 StylusUp/MouseUp 触发事件；未改动 ClipStrokeByErasers 切割算法与 _strokes 自定义集合模式。
- 2026-08-18: Task 2+5——EraseStrokesAtPoints 增加整笔擦除分支（WholeStrokeEraser=true 时 bounds 相交任一橡皮矩形即 ApplyErasedStroke(stroke, 空列表)，无碎片，undo 自动走 StrokesErasedAction）；新增 WholeStrokeEraser/InkSimulationEnabled 公开属性；InkCanvas_StrokeCollected → ApplyInkSimulation（速度→PressureFactor 后处理，替换 Stroke 并以新对象发 undo 事件）；SetInkAttributes 改为 IgnorePressure=!PressureEnabled。
- 2026-08-18: Task 3 形状工具——XAML 新增 ShapePreviewCanvas 层；CustomInkInputProcessingMode 新增 Shape 值 + ShapeKind 枚举；ShapeMode/CurrentShape/ShapeColor/ShapeStrokeSize 属性；StylusDown/Move/Up 与新增 MouseLeftButtonDown/MouseMove/MouseUp（StylusDevice 守卫）双路径拖拽（CaptureStylus/CaptureMouse）；#region Shape tool（BeginShapeDrag/UpdateShapeDrag/EndShapeDrag/BuildShapeOutline/BuildArrowGeometry/CommitShape）；提交走 InkCanvas.Strokes.Add + InkMutated + StrokeCollectedUndoable 既有管线（undo/选区/保存全自动）；SetInputMode 离开 Shape 清拖拽残留；箭头=2 Stroke 决策见上。
- 2026-08-18: Task 4 涂鸦形状识别——新增 #region Scribble shape recognition（TryRecognizeShape/LooksLikeLine/LooksLikeEllipse/LooksLikeRectangle/DirectionBucket/DirectionRun/ReplaceRecognizedStroke/Dist/PerpendicularDistance + 10 个阈值常量）；ShapeRecognitionEnabled 属性；StrokeRecognized 事件 + StrokeRecognizedEventArgs（original/ideal）；InkCanvas_StrokeCollected 在墨水模拟前挂识别（命中→原位替换+新事件+跳过模拟）；理想形状复用 BuildShapeOutline。决策（矩形先于椭圆、mod-180° 方向桶、纯点 bounds、事件代替 StrokeCollectedUndoable）见上。
- 2026-08-18: Task 6 选区逐项 marching-ants 描边——UpdateSelectionVisuals 保留整体框+手柄，新增 AddPerItemOutline 为每个选中 stroke/text container 画独立虚线 rect（外扩 3px、1.2 粗、无填充、不参与命中、Tag="perItemOutline"，初版上限 200；后续回归移除）；新增共享动画驱动 StartSelectionDashAnimation/StopSelectionDashAnimation/SelectionDashAnimation_Tick（单一 CompositionTarget.Rendering 订阅，15 units/s dash offset + 1.5s 半周期 blue↔cyan 硬切换，冻结静态画刷与冻结共享 DashArray）；ClearSelection/空选区分支/框选落空三处停止退订，tick 内 IsLoaded 兜底。纯视觉，命中/移动/缩放逻辑零改动。附带修复：EditorPage.DetachAllPageControlEvents 补 StrokesErased -= 退订（Task 1 遗留）。决策见上 Task 6 段。
- 2026-08-18: Task 7 Ctrl+点击多选——SelectionOverlayCanvas_MouseLeftButtonDownCore 增加 fromStylus 参数 + Ctrl 分支（Keyboard.Modifiers 含 Ctrl 且非笔触时先于手柄/拖拽判定进入，不启动框选不捕获）；新增 HandleCtrlClickToggle（文本优先倒序 z 序→笔迹倒序，SelectionFilter 三态过滤）/HitTextContainer（ActualWidth/RenderSize fallback bounds 含点）/HitStroke（GetBounds.Contains || HitTest(point,2)）/ToggleStrokeSelection/ToggleTextContainerSelection/RefreshSelectionAfterToggle（空→ClearSelection，非空→UpdateSelectionVisuals）；StylusDown 传 fromStylus:true（笔触不走 Ctrl 分支）；EditorPage.PdfScrollViewer_PreviewMouseDown 委托路径增加跨页 Ctrl 清旧页选区（_activeSelectionPage 经 SelectionChanged 自动迁移）。多选移动/缩放/复制/删除/剪切经核查均已迭代双列表，无需改动。决策见上 Task 7 段。
- 2026-08-18: Task 8.2 粘贴自动全选——新增 `public void SelectItems(IEnumerable<Stroke>, IEnumerable<Grid>)`（ClearSelection 之后）：清空双列表后整批去重填入（null 项跳过），末尾复用 RefreshSelectionAfterToggle（空输入→ClearSelection 发 SelectionChanged(false)，非空→UpdateSelectionVisuals 重建整体框+逐项描边+手柄并发 SelectionChanged(true, bounds)），与框选完成/Ctrl 切换路径完全同构。签名用 Grid 而非任务建议的 FrameworkElement——内部 _selectedTextContainers 即 List<Grid>，免 cast；调用方 PasteSelection 的 pastedContainers 亦为 List<Grid>。
- 2026-08-18: Task 12.2 位图替换防闪——XAML 新增 `PdfImageOverlay`（PdfImage 之上、InkCanvas 之下，同布局槽，默认 Opacity 0 + IsHitTestVisible=false）；`OnPageSourceChanged` 改调新私有方法 `SwapPageSource`：新位图入覆盖层（Opacity 0.001）预热 2 帧 → 同帧 reveal（Opacity 1）+ 主图换源 → 下一帧清覆盖层；`_pageSourceSwapGeneration` 字段做代际守卫（连发时旧链 no-op）；null 直接清两层。初始渲染与缩放重渲染两条路径（EditorPage 侧 RenderPageInitialAsync/ReRenderPagesAsync）均经此 DP 回调，一处修复全覆盖。决策（0.001 预热、2 帧窗口、generation 守卫、布局恒定前提）见上 Task 12.2 段。
- 2026-08-18: Task 15 仅笔绘制（防误触）——新增 `PenOnlyMode` 属性 + `IsInkCreationModeActive`（Inking/Shape 判定）+ `ShouldBlockNonPenInk`（null 或 IsTouchFinger 判定，见决策段为何不用 Type!=Stylus 一刀切）；构造器对 InkCanvas `AddHandler(UIElement.PreviewMouseLeftButtonDownEvent / PreviewStylusDownEvent, …, handledEventsToo:true)` 挂 `InkCanvas_PreviewMouseLeftButtonDown_PenOnly` / `InkCanvas_PreviewStylusDown_PenOnly` 两过滤 handler（置 Handled 抑制 InkCanvas 落墨启动）；`InkCanvas_StylusDown` / `InkCanvas_MouseLeftButtonDown` 的 Shape 分支加同条件早退守卫（双保险）。橡皮/选区/文本/PDF 文本选择不受影响。构建 0 错误 + 15/15 测试通过。
- 2026-08-21: Hidden Ink 输入边界回归——`IsPenOnlyInkCreationMode` 仅将 Inking/Shape 纳入 PenOnly 过滤，Hidden Ink 保留鼠标与真笔输入；新增无硬件回归测试。
- 2026-08-18: Task 19 图片注释——XAML 新增 `ImageOverlayCanvas`（PdfImageOverlay 与 InkCanvas 之间，IsHitTestVisible=false）；新增 `ImageContainerTag`/`_imageContainers`/`_imageDataById`/`ImagesChanged` + `AddImage(bytes, pos, explicitW?, explicitH?)`/`ImageContainers`/`GetImageData`/`SetImageData`/`IsImageContainer`；`RemoveTextContainerQuiet`/`AddTextContainerQuiet` 层级感知（集中式容器增删，image quiet 移除保留 payload 字典）；`ClearAllAnnotations` 清图片层+注册表；`ScaleItemsDirectly` image 分支（缩放 Grid 宽高而非 FontSize）；框选完成两分支 + `HandleCtrlClickToggle` 迭代 ImageOverlayCanvas（文本→图片→笔迹 z 序）；`GetSelectionBounds` ActualWidth=0 时回退显式 Width（粘贴即选首帧 bbox）。决策：图片作为"容器型"条目复用 _selectedTextContainers 选区管线（Grid 同型），移动/删除/跨页/逐项描边零改动即通；跨页字节由 EditorPage 显式 TransferImageData。构建 0 错误 + 21/21 测试通过（含新增 2 个图片往返测试）。
- 2026-08-18: Task 20 激光笔——`CustomInkInputProcessingMode` 增 `Laser`；XAML 新增 `LaserInkCanvas`（最顶层，EraserCanvas 之上，IsHitTestVisible=false）；新增 #region Laser pointer（BeginLaserStroke/UpdateLaserStroke/EndLaserStroke + 常量 LaserColor #FF3B30/粗 3/延迟 150ms/渐隐 900ms/上限 60）；InkCanvas 六个输入 handler（StylusDown/Move/Up + MouseDown/Move/Up）各加 Laser 分支（镜像 Shape 管线，无 pen-only 守卫）；SetInputMode 增 Laser case（命中开 + EditingMode=None + Cross）+ 离开 Laser 清拖拽状态。隔离：零事件、零 Strokes 写入、保存路径不含激光层。构建 0 错误 + 21/21 测试通过。
- 2026-08-18: Task 21 Shift 约束——新增静态 helper `ConstrainShapeEndpoints`（Line/Arrow 45° 吸附重投影；Rect/Ellipse 正方/正圆保方向）+ `IsShiftHeld`；接入 UpdateShapeDrag/EndShapeDrag（preview/commit 一处计算一致）；新增 `StraightenShiftStroke`（InkCanvas_StrokeCollected 最前，Shift 按住时整笔拉直为首尾两点直线，attrs 克隆 + FitToCurve=false，_strokes 原位替换，单步 undo；跳过形状识别/墨水模拟）。构建 0 错误 + 21/21 测试通过。
- 2026-08-18: Task 22 直尺吸附——新增 `GetRulerEdgeInPageCoords`（`Func<(Point A, Point B)?>` delegate，EditorPage LoadPdf 注入）+ `RulerSnapTolerancePx=24` 常量 + `SnapStrokeToRuler`（两遍扫描：全点距线段 <24px 才整笔投影，t clamp 保 PressureFactor，`_strokes` 原位替换）；`InkCanvas_StrokeCollected` 在 Shift 拉直前挂钩（`_currentMode == Inking` gate）。尺本体（视觉/拖动/旋转/吸附几何）全在 EditorPage overlay，见其镜像。构建 0 错误 + 21/21 测试通过。
- 2026-08-23: Wave 1 形状识别撤销——普通 Stroke 由 reference-identity 字典分配 session token；识别事件改为 token/index/immutable snapshots，`TryReplaceStrokeQuiet` 原位替换并在 token 缺失或 side 不符时 no-op，整笔擦除/清理同步移除 metadata；focused shape contract 4/4、Wave 1 integration 31/31、full suite 107/107 通过。专用 shape pointer smoke 脚本尚不存在，故不宣称手动结果。
- 2026-08-23: Wave 1 quality follow-up——placement owner/token/side/index metadata now flows through erase/delete/cross-page actions; snapshots preserve pressure and IgnorePressure; production `StrokeReplacementState` is shared by page operations; `GetStrokes()` returns a defensive legacy-typed copy. Production 5/5 and full suite 113/113 pass; pointer foreground evidence remains open.
- 2026-08-23: Wave 2 plan — switch only new Hidden Ink defaults to `#C7CDD4` while preserving explicit legacy white values and transient reveal semantics; no layer-order, ownership-prefix, or PDF coordinate changes.
- 2026-08-23: Wave 2 implementation — model/control/editor defaults now use `#C7CDD4`; explicit white annotations remain white through load/save, while `/CA 1`, `wna_hidden_`, `/WNARevealMs`, round caps and reveal timers remain unchanged.
- 2026-08-23: Wave 2 final review — `SetDocumentInputEnabled` now participates in the shared editor close admission; page input is disabled before final save/release and restored only after a cancelled/recoverable navigation or close.
- 2026-08-23: Wave 2 transactional follow-up — `RemoveImageData` lets cross-page selection rollback clear a copied image payload from the target page while preserving the source owner.
- 2026-08-18: Task 24 笔迹平滑——新增 `StrokeSmoothingLevel` 属性（默认 2）+ `ApplySmoothing(stroke)`（档 0=FitToCurve=false 原位替换保原始轨迹；档 1-3=滑动窗口均值 w=1/2/4、PressureFactor 取原中心点、短笔迹窗口钳制防质心坍缩）；`InkCanvas_StrokeCollected` 链位插在 Shift 拉直之后、形状识别/墨水模拟之前。设置 UI（pen popup 分段行）在 EditorPage，见其镜像。构建 0 错误 + 21/21 测试通过。
