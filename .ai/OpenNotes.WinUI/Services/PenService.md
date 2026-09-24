# OpenNotes.WinUI/Services/PenService.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

WinUI port of the WPF `WindowsPenService`/`HuaweiPenService` pair (Task 7 Phase A) — one `PenService` per `EditorPage` covers both (the WPF Huawei service is a strict subset — same Win+F19/F20 hotkeys — so one class suffices). HWND subclass + window-scoped hotkeys + pointer-packet capability probing (`Caelum.Services`).

## What it does

- `PenService : IDisposable` — `Initialize(Window)` resolves the HWND via `Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id)`, `RegisterHotKey`s Win+F19 (`HOTKEY_ID_WIN_F19`=9001, `VK_F19`=0x82) and Win+F20 (`HOTKEY_ID_WIN_F20`=9002, `VK_F20`=0x83) with `MOD_WIN|MOD_NOREPEAT`, then `SetWindowSubclass` (`SUBCLASS_ID`=0xCAE1) so `SubclassWndProc` sees `WM_HOTKEY` (0x0312) → `ToolToggleRequested` (raised on the subclass thread — subscribers marshal via `DispatcherQueue`; `EditorPage` maps it to `ToggleEraserMode`). Both `RegisterHotKey` results are checked — failure logs `Marshal.GetLastWin32Error()` and skips subclassing (a second-hotkey failure also unregisters the first, no half-registered state). A failed `SetWindowSubclass` unregisters BOTH hotkeys: WM_HOTKEY only reaches us through the subclass proc, so dead registrations would silently swallow the keys for other apps.
- `ProbePointer(PointerPoint)` — called by `InkSurface` on pen pointer-DOWN (once per gesture is enough for capability accumulation); accumulates `Capabilities` (`PenCapabilities`: `HasPressure`/`HasTilt`/`HasBarrelButton`/`HasEraserTail`/`HasTwist`) from the packet properties and fires `PenDeviceDetected` ONCE, on the first pen packet seen, with a `PenDeviceInfo` (`DeviceId`/`DeviceName`/`PenBrand`/`SupportsPressure`/`SupportsXTilt`/`SupportsYTilt`/`SupportsBarrelButton`/`SupportsSecondaryTip`/`SupportsTwist`/`IsInverted`). Aggregate "first pen seen" model — WinUI exposes no stable per-device identity and no device name, so detected pens are always `PenBrand.Generic`.
- `NoteBarrelButton()` — the surface reports barrel-button packets explicitly.
- `PressureEnabled`/`TiltEnabled` — user-pref projections of `Capabilities`; `InkSurface.EnablePressure` is NOT synced from `PressureEnabled` — `EditorPage.ApplyToolToAllPages` owns it from `AppSettings.EnablePressure` (do not re-introduce a `SetPenService` sync). `SimulateToggle()` raises `ToolToggleRequested` for tests/keyboard.
- Pure helpers (WPF math unchanged): `PressureToWidthMultiplier` (γ=0.7 curve, 0.3–1.8×), `PressureToHighlighterOpacity` (0.3–0.8), `ComputeTiltAngle`, `TiltToWidthMultiplier` (1.0–2.5×). All four are **reserved** — nothing consumes them yet (InkSurface tessellates via `StrokeOutline`); kept as Phase-B width/opacity/tilt hooks.
- `enum PenBrand` (Generic/Surface/Wacom/Huawei/Dell/HP/Lenovo/Samsung/Asus/Acer/NTrig/Synaptics/Elan/XPPen/Huion/Gaomon) — API parity only; brand detection is impossible on WinUI.

## Constraints / NEVER Change

- **No `PointerPointProperties.HasUsage`** — that API is not in the WinUI projection (compile error); capabilities are inferred from concrete packet properties instead (pressure ≠ 0.5 ⇒ real digitizer; non-zero `XTilt`/`YTilt`/`Twist`; `IsBarrelButtonPressed`; `IsEraser`/`IsInverted`).
- `SubclassWndProc` forwards every message to `DefSubclassProc` — missing the forward wedges the window; the `_subclassProc` delegate field must outlive the registration.
- Hotkeys stay window-scoped (the editor window's HWND) — WPF registered per-window too; do not move to a global hook.
- `Dispose()` removes the subclass and unregisters both hotkeys (guarded by `_disposed`); `Initialize` is idempotent (`_isInitialized`).
- WPF keeps `Services/WindowsPenService.cs`/`HuaweiPenService.cs` — this is a parallel implementation, not a shared one (input model differs).

## Open Threads / Resume Context

- Deferred to Phase B: stylus Bluetooth/pairing polish and brand-specific quirks beyond the hotkey; `Capabilities` currently only feeds the pen-detected toast — nothing gates behaviour on it yet.
