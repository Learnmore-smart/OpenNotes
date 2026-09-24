using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace Caelum.Services
{
    /// <summary>
    /// WinUI port of the WPF <c>WindowsPenService</c>/<c>HuaweiPenService</c>
    /// pair (the WPF Huawei service is a strict subset of the unified
    /// service — same Win+F19/F20 hotkeys — so one class covers both here).
    ///
    /// What carries over:
    ///   • Win+F19/F20 global hotkeys → <see cref="ToolToggleRequested"/>
    ///     (Huawei M-Pencil double-tap, via PC Manager / community daemons).
    ///     The WPF HwndSource hook is replaced by SetWindowSubclass on the
    ///     WinUI window HWND — the same native signal path.
    ///   • <see cref="PenDeviceDetected"/> + aggregate
    ///     <see cref="Capabilities"/>, inferred from pointer packets via
    ///     <see cref="PointerPointProperties.HasUsage"/> — the WinRT analog
    ///     of WPF's TabletDevice.SupportedStylusPointProperties probing.
    ///
    /// Deliberate difference: WinUI's pointer stack exposes no stable
    /// per-device identity (PointerPoint has PointerDeviceType only), so
    /// detection is an aggregate "first pen seen" model rather than a
    /// per-device dictionary. Brand classification by device name is not
    /// possible — WinRT does not surface the device name either.
    /// </summary>
    public sealed class PenService : IDisposable
    {
        // ── Public events ────────────────────────────────────────────────

        /// <summary>
        /// Fired when the pen itself asks for a tool toggle (Huawei
        /// double-tap hotkeys). Raised on the subclass thread — subscribers
        /// must marshal to the UI thread.
        /// </summary>
        public event EventHandler ToolToggleRequested;

        /// <summary>
        /// Fired once when the first pen packet reports capabilities.
        /// </summary>
        public event EventHandler<PenDeviceInfo> PenDeviceDetected;

        // ── Public state ─────────────────────────────────────────────────

        /// <summary>Aggregate capabilities of the detected pen hardware.</summary>
        public PenCapabilities Capabilities { get; } = new PenCapabilities();

        /// <summary>Whether pressure-to-width mapping is enabled (user pref).</summary>
        public bool PressureEnabled { get; set; } = true;

        /// <summary>Whether tilt-to-angle mapping is enabled (user pref).</summary>
        public bool TiltEnabled { get; set; } = true;

        // ── Internal state ───────────────────────────────────────────────

        private IntPtr _hwnd;
        private SubclassProc _subclassProc; // must outlive the subclass registration
        private bool _isInitialized;
        private bool _disposed;
        private bool _deviceSeen;

        // Huawei M-Pencil hotkey support (identical IDs to the WPF service).
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_WIN_F19 = 9001;
        private const int HOTKEY_ID_WIN_F20 = 9002;
        private const uint VK_F19 = 0x82;
        private const uint VK_F20 = 0x83;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;
        private const UIntPtr SUBCLASS_ID = (UIntPtr)0xCAE1;

        // HID usage probing (PointerPointProperties.HasUsage) is NOT exposed
        // by WinUI 3 — capabilities are inferred from the packet properties
        // directly: non-uniform pressure, non-zero tilt/twist, and the
        // barrel/eraser flags.

        private delegate IntPtr SubclassProc(
            IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
            UIntPtr uIdSubclass, UIntPtr dwRefData);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(
            IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(
            IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(
            IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        // ── Initialisation ───────────────────────────────────────────────

        /// <summary>
        /// Attaches to the window's HWND: registers the Huawei hotkeys and
        /// subclasses the WndProc for WM_HOTKEY. Safe to call repeatedly.
        /// </summary>
        public void Initialize(Window window)
        {
            if (_isInitialized || _disposed)
                return;
            if (window == null)
            {
                Log("Initialize called with null window");
                return;
            }

            _hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
            if (_hwnd == IntPtr.Zero)
            {
                Log("Window HWND is zero — hotkeys not registered");
                return;
            }

            bool f19 = RegisterHotKey(_hwnd, HOTKEY_ID_WIN_F19, MOD_WIN | MOD_NOREPEAT, VK_F19);
            if (!f19)
                Log($"RegisterHotKey Win+F19 failed: {Marshal.GetLastWin32Error()}");
            bool f20 = RegisterHotKey(_hwnd, HOTKEY_ID_WIN_F20, MOD_WIN | MOD_NOREPEAT, VK_F20);
            if (!f20)
                Log($"RegisterHotKey Win+F20 failed: {Marshal.GetLastWin32Error()}");

            _subclassProc = SubclassWndProc;
            if (!SetWindowSubclass(_hwnd, _subclassProc, SUBCLASS_ID, UIntPtr.Zero))
            {
                Log($"SetWindowSubclass failed: {Marshal.GetLastWin32Error()}");
                _subclassProc = null;
                // Without the WndProc hook nothing can observe WM_HOTKEY —
                // release both registrations so the hotkeys aren't dead-owned
                // (and the pen-side keys still reach their real consumers).
                UnregisterHotKey(_hwnd, HOTKEY_ID_WIN_F19);
                UnregisterHotKey(_hwnd, HOTKEY_ID_WIN_F20);
            }

            _isInitialized = true;
            Log($"Initialized – HWND=0x{_hwnd:X} hotkeys F19={(f19 ? "ok" : "FAILED")} F20={(f20 ? "ok" : "FAILED")} subclass={_subclassProc != null}");
        }

        // ── Pen packet probing ───────────────────────────────────────────

        /// <summary>
        /// Called by the ink surface on pen input. Merges packet-observable
        /// capabilities into <see cref="Capabilities"/> (a barrel button only
        /// shows when pressed, so probing never stops accumulating) and fires
        /// <see cref="PenDeviceDetected"/> once, on the first pen packet.
        /// </summary>
        public void ProbePointer(PointerPoint point)
        {
            if (point == null || point.PointerDeviceType != PointerDeviceType.Pen)
                return;

            var props = point.Properties;
            var caps = Capabilities;
            try
            {
                // Mouse packets always report Pressure == 0.5; a real
                // digitizer varies — treat any non-uniform reading as proof,
                // plus the pen-only flags as capability evidence.
                caps.HasPressure |= props.Pressure > 0f
                    && Math.Abs(props.Pressure - 0.5f) > 0.001f;
                caps.HasTilt |= props.XTilt != 0f || props.YTilt != 0f;
                caps.HasBarrelButton |= props.IsBarrelButtonPressed;
                caps.HasEraserTail |= props.IsEraser || props.IsInverted;
                caps.HasTwist |= props.Twist != 0f;
            }
            catch (Exception ex)
            {
                Log($"ProbePointer faulted: {ex.Message}");
            }

            if (_deviceSeen)
                return;
            _deviceSeen = true;

            var info = new PenDeviceInfo
            {
                DeviceName = "Windows Pen",
                PenBrand = PenBrand.Generic,
                SupportsPressure = caps.HasPressure,
                SupportsXTilt = caps.HasTilt,
                SupportsYTilt = caps.HasTilt,
                SupportsBarrelButton = caps.HasBarrelButton,
                SupportsTwist = caps.HasTwist,
                IsInverted = props.IsEraser,
            };

            Log($"Pen detected: {info} caps={caps}");
            PenDeviceDetected?.Invoke(this, info);
        }

        /// <summary>Notifies the service that a barrel-button packet was seen.</summary>
        public void NoteBarrelButton() => Capabilities.HasBarrelButton = true;

        // ── Pressure helpers (ported unchanged — pure math) ─────────────
        // Reserved API: nothing calls these in Phase A — WinUI strokes
        // carry per-point pressure and the renderer applies the WPF width
        // law directly. Kept public for the Phase-B feature surface
        // (calligraphy/tilt rendering) so the ported WPF formulas don't
        // have to be re-derived; do not delete on "unused" alone.

        /// <summary>
        /// Maps normalized pressure (0.0–1.0) to a pen width multiplier with
        /// a mild γ=0.7 power curve — same formula as the WPF service.
        /// Reserved for Phase B; no Phase-A caller.
        /// </summary>
        public static double PressureToWidthMultiplier(
            double pressureFactor,
            double minMultiplier = 0.3,
            double maxMultiplier = 1.8)
        {
            double p = Math.Max(0.0, Math.Min(1.0, pressureFactor));
            double curved = Math.Pow(p, 0.7);
            return minMultiplier + curved * (maxMultiplier - minMultiplier);
        }

        /// <summary>Pressure → highlighter opacity multiplier (WPF port). Reserved for Phase B; no Phase-A caller.</summary>
        public static double PressureToHighlighterOpacity(
            double pressureFactor,
            double minOpacity = 0.3,
            double maxOpacity = 0.8)
        {
            double p = Math.Max(0.0, Math.Min(1.0, pressureFactor));
            return minOpacity + p * (maxOpacity - minOpacity);
        }

        /// <summary>Tilt magnitude (degrees, 0 = perpendicular). Reserved for Phase B; no Phase-A caller.</summary>
        public static double ComputeTiltAngle(double xTilt, double yTilt)
            => Math.Sqrt(xTilt * xTilt + yTilt * yTilt);

        /// <summary>Tilt → width multiplier (calligraphy effect). Reserved for Phase B; no Phase-A caller.</summary>
        public static double TiltToWidthMultiplier(
            double tiltAngle,
            double maxTilt = 90.0,
            double minMultiplier = 1.0,
            double maxMultiplier = 2.5)
        {
            double t = Math.Max(0.0, Math.Min(maxTilt, tiltAngle)) / maxTilt;
            return minMultiplier + t * (maxMultiplier - minMultiplier);
        }

        // ── WndProc ──────────────────────────────────────────────────────

        private IntPtr SubclassWndProc(
            IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            UIntPtr uIdSubclass, UIntPtr dwRefData)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (hotkeyId == HOTKEY_ID_WIN_F19 || hotkeyId == HOTKEY_ID_WIN_F20)
                {
                    Log($"Huawei hotkey {(hotkeyId == HOTKEY_ID_WIN_F19 ? "Win+F19" : "Win+F20")} → ToolToggleRequested");
                    ToolToggleRequested?.Invoke(this, EventArgs.Empty);
                    return IntPtr.Zero; // message consumed
                }
            }
            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        // ── Simulate (for testing / keyboard shortcut) ───────────────────

        public void SimulateToggle() => ToolToggleRequested?.Invoke(this, EventArgs.Empty);

        // ── Logging ──────────────────────────────────────────────────────

        private static void Log(string message)
        {
            string line = $"[PenService] {DateTime.Now:HH:mm:ss.fff} {message}";
            System.Diagnostics.Debug.WriteLine(line);
        }

        // ── Cleanup ──────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_hwnd != IntPtr.Zero)
            {
                if (_subclassProc != null)
                {
                    try { RemoveWindowSubclass(_hwnd, _subclassProc, SUBCLASS_ID); } catch { }
                    _subclassProc = null;
                }
                try { UnregisterHotKey(_hwnd, HOTKEY_ID_WIN_F19); } catch { }
                try { UnregisterHotKey(_hwnd, HOTKEY_ID_WIN_F20); } catch { }
            }
        }
    }

    // ── Supporting types (same shape as the WPF service) ─────────────────

    /// <summary>Aggregate capability flags across detected pen input.</summary>
    public class PenCapabilities
    {
        public bool HasPressure { get; set; }
        public bool HasTilt { get; set; }
        public bool HasBarrelButton { get; set; }
        public bool HasEraserTail { get; set; }
        public bool HasTwist { get; set; }

        public override string ToString() =>
            $"Pressure={HasPressure} Tilt={HasTilt} Barrel={HasBarrelButton} Eraser={HasEraserTail} Twist={HasTwist}";
    }

    /// <summary>One detected pen — WinUI variant (packet-inferred caps).</summary>
    public class PenDeviceInfo
    {
        public int DeviceId { get; set; } = -1;
        public string DeviceName { get; set; }
        public PenBrand PenBrand { get; set; }

        public bool SupportsPressure { get; set; }
        public bool SupportsXTilt { get; set; }
        public bool SupportsYTilt { get; set; }
        public bool SupportsBarrelButton { get; set; }
        public bool SupportsSecondaryTip { get; set; }
        public bool SupportsTwist { get; set; }
        public bool IsInverted { get; set; }

        public override string ToString() =>
            $"{PenBrand} \"{DeviceName}\" id={DeviceId} " +
            $"pressure={SupportsPressure} tilt={SupportsXTilt}/{SupportsYTilt} " +
            $"barrel={SupportsBarrelButton} inverted={IsInverted}";
    }

    /// <summary>Known pen brands (brand detection unavailable on WinUI — kept for API parity).</summary>
    public enum PenBrand
    {
        Generic,
        Surface,
        Wacom,
        Huawei,
        Dell,
        HP,
        Lenovo,
        Samsung,
        Asus,
        Acer,
        NTrig,
        Synaptics,
        Elan,
        XPPen,
        Huion,
        Gaomon
    }
}
