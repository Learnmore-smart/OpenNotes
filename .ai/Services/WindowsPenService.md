# Services/WindowsPenService.cs
> Last updated: 2026-08-18 | Protection: STANDARD

## Purpose（一句话）
统一触控笔服务：每 MainWindow 一实例，负责 stylus 设备探测与能力聚合、品牌启发式识别、华为 M-Pencil 双击热键（Win+F19/F20），并暴露压感/倾斜偏好开关。

## What It Does（关键机制，含行号引用）
- **设计契约**（类注释行 10-25）：一个 MainWindow 生命周期一实例；EditorPage 订阅事件；PdfPageControl 读 `PenCapabilities` 自适应墨迹渲染。
- **事件**（行 34-40）：`ToolToggleRequested`（笔端请求切换工具——华为双击）；`PenDeviceDetected`（新设备首次发现，UI 可 toast）。
- **状态**（行 47-64）：`Capabilities`（聚合 `PenCapabilities`：HasPressure/HasTilt/HasBarrelButton/HasEraserTail/HasTwist）；`DetectedDevices`（按 `StylusDevice.Id` 键的字典）；`PressureEnabled`/`TiltEnabled`（用户偏好，默认 true）。
- **初始化** `Initialize(Window)`（行 96-119）：取 HWND（SourceInitialized 延迟兜底），`AttachHooks`（行 121-130）注册热键并挂 `WndProc`。
- **设备探测** `ProbeDevice(StylusDevice)`（行 138-153）：首次见到的设备 `BuildDeviceInfo`（行 155-205，遍历 `TabletDevice.SupportedStylusPointProperties` 判定 NormalPressure/XTilt/YTilt/BarrelButton/SecondaryTip/Twist 能力）→ `MergeCapabilities`（行 233-245 聚合 OR）→ 触发 `PenDeviceDetected`。
- **品牌识别** `ClassifyBrand`（行 207-231）：设备名小写包含子串匹配 Surface/Wacom/Huawei(m-pencil|matebook)/Dell/HP/Lenovo/Samsung(s pen)/Asus/Acer/N-trig/Synaptics/Elan/XP-Pen/Huion/Gaomon → `PenBrand` 枚举，否则 Generic。
- **华为热键**（行 75-81 常量 + `WndProc` 行 310-323）：`RegisterHotKey` 注册 `Win+F19`(id 9001) / `Win+F20`(9002)（MOD_WIN|MOD_NOREPEAT）；收到 `WM_HOTKEY(0x0312)` 即触发 `ToolToggleRequested`。非华为设备注册无害。
- **压感/倾斜辅助（死代码）**：
  - `PressureToWidthMultiplier`（**行 257**，γ=0.7 幂曲线，0.3-1.8 倍）；
  - `PressureToHighlighterOpacity`（行 276）、`ComputeTiltAngle`（行 289）；
  - `TiltToWidthMultiplier`（**行 298**，tilt→1.0-2.5 倍书法效果）。
  - **全仓库 grep 确认：这 4 个静态方法只在定义处出现，无任何调用点**（压感实际由 InkCanvas 原生 `IgnorePressure=false` + StylusPoints 处理，未走这些曲线）。
- **模拟** `SimulateToggle()`（行 327）：手动触发 ToolToggleRequested（测试/快捷键用）。
- **释放** `Dispose`（行 343-354）：摘钩 + UnregisterHotKey（try-catch 包裹）。
- **日志**（行 334-339）：`[WindowsPen] HH:mm:ss.fff` 同步写 Console + Debug。
- 配套类型：`PenCapabilities`（行 362）、`PenDeviceInfo`（行 377，含 PressureMin/Max=1024、TiltMin/Max=90 默认值）、`PenBrand` 枚举。

## Public API / 关键成员（表）
| 成员 | 行号 | 说明 |
|---|---|---|
| `Initialize(Window)` | 96 | 挂钩 + 注册热键（幂等） |
| `ProbeDevice(StylusDevice)` | 138 | 探测/缓存设备并聚合能力 |
| `Capabilities` / `DetectedDevices` | 47/52 | 聚合能力 / 设备表 |
| `PressureEnabled` / `TiltEnabled` | 58/64 | 用户偏好开关（默认 true） |
| `ToolToggleRequested` / `PenDeviceDetected` | 34/40 | 事件 |
| `SimulateToggle()` | 327 | 手动触发切换事件 |
| `PressureToWidthMultiplier` 等 4 个 static | 257/276/289/298 | **死代码，未被调用** |
| `Dispose()` | 343 | 注销热键与钩子 |

## Dependencies
- user32.dll P/Invoke（RegisterHotKey/UnregisterHotKey）、HwndSource 钩子。
- 被 MainWindow 持有（每窗口一实例），PdfPageControl 经 `SetPenService`（其行 214）接收并同步 Pressure/TiltEnabled；EditorPage 订阅 ToolToggleRequested。

## Open Threads / Resume Context
V5 spec（`.trae/specs/add-core-note-features`）实施中，Task 0-40。若 Task 涉及"压感曲线可调/倾斜书法笔刷"，死代码 4 方法是现成落点（启用前先验证实际手感的 γ 参数）。

## Agent Decisions / Thoughts
- 压感宽度目前完全交给 WPF InkCanvas 原生（IgnorePressure=false），服务里的曲线函数是"设计了但未接线"的遗留——**勿误以为它们在生效**。
- 品牌识别是字符串启发式，无 WMI/VID-PID 精确匹配；新品牌（如 iPad 侧信道）不会命中任何分支。

## Important Notes / NEVER Change
- 热键 ID 9001/9002 与 WM_HOTKEY 处理（华为双击唯一信号路径）。
- 一个 MainWindow 一个实例的契约（热键注册按 HWND，多实例会重复注册/串扰）。
- 删除死代码前确认 V5 spec 是否计划启用（先查 tasks.md）。

## Change History
- 2026-08-18: 建立镜像文档（Task 0），grep 验证 PressureToWidthMultiplier(257)/TiltToWidthMultiplier(298) 等为未调用死代码。
