# OpenNotes.WinUI/Services/PenService.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

WinUI port of the WPF `WindowsPenService`/`HuaweiPenService` pair (Task 7 Phase A) — stylus device detection, capability probing and the pen hotkey (Win+F19/F20) glue for the WinUI window's HWND.

## What it does

- `PenDeviceInfo` (Name, `IsHuawei`, `SupportsPressure`, `SupportsBarrel`, `SupportsEraser`, `SupportsInvert`) — detected snapshot.
- `PenService.Current` — `InitializeForWindow(Window)` (subclasses the window HWND via `Microsoft.UI.Win32Interop.GetWindowFromWindowId` + `SetWindowSubclass`; installs `RegisterHotKey` for Win+F19/F20 → `PenHotKeyPressed`), `ProbePointerProperties(PointerPointProperties)` (accumulates capability flags across packets — never assumes one packet exposes everything), `Dispose` (unsubclass + unregister).
- Capability inference is conservative: pressure `≠ 0.5` or `IsInContact` transitions ⇒ pressure; `IsBarrelButtonPressed`/`IsEraser`/`IsInverted` flags ever seen ⇒ the matching capability bit. `IsHuawei` matches the device name when available, else stays false (Huawei toggle still works — the WPF service only used the brand for a hotkey default).

## Constraints / NEVER Change

- **No `PointerPointProperties.HasUsage`** — that API does not exist in the WinUI projection (compile error); probe the concrete properties instead.
- WndProc must forward every message to `DefSubclassProc` — missing the forward wedges the window.
- Hotkeys are window-scoped (`HWND` of the editor window) — WPF registered them per-window too; do not move to a global hook.
- WPF keeps `Services/WindowsPenService.cs`/`HuaweiPenService.cs` — the WinUI class is a parallel implementation, not a shared one (input model differs).

## Open Threads / Resume Context

- Deferred to Phase B: stylus Bluetooth/pairing status polish and any brand-specific quirks beyond the hotkey; `Supports*` bits currently only inform `IsHuawei`-style defaults — nothing gates behaviour on them yet.
