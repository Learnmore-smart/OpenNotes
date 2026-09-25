# OpenNotes.WinUI/Services/PenService.cs
> Last updated: 2026-09-25 (window-scoped ownership — review fix) | Protection: STANDARD

## Purpose

WinUI port of the WPF `WindowsPenService`/`HuaweiPenService` pair (Task 7 Phase A; the WPF Huawei service is a strict subset — same Win+F19/F20 hotkeys — so one class suffices). HWND subclass + window-scoped hotkeys + pointer-packet capability probing (`Caelum.Services`). **Ownership (2026-09-25 review fix): exactly ONE instance per window — `MainWindow.GetOrCreatePenService()` creates + `Initialize`s it lazily, `MainWindow_Closed` disposes it; `EditorPage` instances pull the shared reference (`GetMainWindow()?.GetOrCreatePenService()`) and never construct/dispose their own.** Per-page construction raced every page onto the same HWND: duplicate `RegisterHotKey` ids fail for tabs 2+, the owning tab's teardown unregistered the pair app-wide, and every subclass proc saw the shared `WM_HOTKEY` so the eraser toggle hit inactive editors.

## What it does

- `PenService : IDisposable` — `Initialize(Window)` resolves the HWND via `Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id)`, `RegisterHotKey`s Win+F19 (`HOTKEY_ID_WIN_F19`=9001, `VK_F19`=0x82) and Win+F20 (`HOTKEY_ID_WIN_F20`=9002, `VK_F20`=0x83) with `MOD_WIN|MOD_NOREPEAT`, then `SetWindowSubclass` (`SUBCLASS_ID`=0xCAE1) so `SubclassWndProc` sees `WM_HOTKEY` (0x0312) → `ToolToggleRequested` (raised on the subclass thread — subscribers marshal via `DispatcherQueue`; `MainWindow` routes it to the ACTIVE tab's `EditorPage.HandlePenToolToggle()` → `ToggleEraserMode`, with `_isHostActive`/`_resourcesReleased` gates pre-enqueue and inside the callback — the WPF `IsActiveEditorPage()` parity). Both `RegisterHotKey` results are checked — a failure logs `Marshal.GetLastWin32Error()` but the other hotkey stays registered (a lone F20 failure leaves F19 live — functional partial state, intentional). A failed `SetWindowSubclass` unregisters BOTH hotkeys: WM_HOTKEY only reaches us through the subclass proc, so dead registrations would silently swallow the keys for other apps.
- `ProbePointer(PointerPoint)` — called by `InkSurface` on pen pointer-DOWN (once per gesture is enough for capability accumulation); accumulates `Capabilities` (`PenCapabilities`: `HasPressure`/`HasTilt`/`HasBarrelButton`/`HasEraserTail`/`HasTwist`) from the packet properties and fires `PenDeviceDetected` ONCE, on the first pen packet seen (routed by `MainWindow` to the active editor's `HandlePenDeviceDetected` toast only — dropped when no editor tab is active, matching the WPF active-page gate), with a `PenDeviceInfo` (`DeviceId`/`DeviceName`/`PenBrand`/`SupportsPressure`/`SupportsXTilt`/`SupportsYTilt`/`SupportsBarrelButton`/`SupportsSecondaryTip`/`SupportsTwist`/`IsInverted`). Aggregate "first pen seen" model — WinUI exposes no stable per-device identity and no device name, so detected pens are always `PenBrand.Generic`.
- `NoteBarrelButton()` — the surface reports barrel-button packets explicitly.
- `PressureEnabled`/`TiltEnabled` — user-pref projections of `Capabilities`; `InkSurface.EnablePressure` is NOT synced from `PressureEnabled` — `EditorPage.ApplyToolToAllPages` owns it from `AppSettings.EnablePressure` (do not re-introduce a `SetPenService` sync). `SimulateToggle()` raises `ToolToggleRequested` for tests/keyboard.
- Pure helpers (WPF math unchanged): `PressureToWidthMultiplier` (γ=0.7 curve, 0.3–1.8×), `PressureToHighlighterOpacity` (0.3–0.8), `ComputeTiltAngle`, `TiltToWidthMultiplier` (1.0–2.5×). All four are **reserved** — nothing consumes them yet (InkSurface tessellates via `StrokeOutline`); kept as Phase-B width/opacity/tilt hooks.
- `enum PenBrand` (Generic/Surface/Wacom/Huawei/Dell/HP/Lenovo/Samsung/Asus/Acer/NTrig/Synaptics/Elan/XPPen/Huion/Gaomon) — API parity only; brand detection is impossible on WinUI.

## Constraints / NEVER Change

- **No `PointerPointProperties.HasUsage`** — that API is not in the WinUI projection (compile error); capabilities are inferred from concrete packet properties instead (pressure ≠ 0.5 ⇒ real digitizer; non-zero `XTilt`/`YTilt`/`Twist`; `IsBarrelButtonPressed`; `IsEraser`/`IsInverted`).
- `SubclassWndProc` forwards every message to `DefSubclassProc` — missing the forward wedges the window; the `_subclassProc` delegate field must outlive the registration.
- Hotkeys stay window-scoped (the editor window's HWND) — WPF registered per-window too; do not move to a global hook.
- `Dispose()` removes the subclass and unregisters both hotkeys (guarded by `_disposed`); `Initialize` is idempotent (`_isInitialized`) — `GetOrCreatePenService` calls it every access so a not-yet-ready HWND retries. **Only `MainWindow` may dispose it.**
- WPF keeps `Services/WindowsPenService.cs`/`HuaweiPenService.cs` — this is a parallel implementation, not a shared one (input model differs).

## Open Threads / Resume Context

- Deferred to Phase B: stylus Bluetooth/pairing polish and brand-specific quirks beyond the hotkey; `Capabilities` currently only feeds the pen-detected toast — nothing gates behaviour on it yet.
