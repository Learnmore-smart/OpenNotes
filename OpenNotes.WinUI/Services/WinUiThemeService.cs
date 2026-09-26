using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Caelum.Services
{
    /// <summary>
    /// V6 WinUI port of the WPF <c>Services/ThemeService.cs</c>. Centralizes the
    /// application chrome palette so it can be switched at runtime. The PDF page
    /// bitmap itself is never tinted.
    ///
    /// Differences from the WPF version, by design:
    /// <list type="bullet">
    /// <item>Brushes are written into <see cref="Application.Resources"/> exactly
    /// like WPF, but the effective Fluent theme is switched through
    /// <see cref="FrameworkElement.RequestedTheme"/> on each registered window's
    /// root content (WinUI has no app-wide runtime <c>RequestedTheme</c> setter).</item>
    /// <item>XAML consumes the keys through <c>{ThemeResource}</c>, which
    /// re-resolves both on theme changes and on in-place resource replacement —
    /// the WinUI equivalent of WPF <c>DynamicResource</c>/<c>SetResourceReference</c>.</item>
    /// <item>System preference inputs come from <see cref="UISettings"/> /
    /// <see cref="AccessibilitySettings"/> (with an HKCU registry fallback for
    /// dark mode) instead of <c>SystemEvents.UserPreferenceChanged</c>.</item>
    /// <item><c>ThemePopupAnimation</c> is not ported — it is a WPF
    /// <c>PopupAnimation</c> enum token with no WinUI counterpart; WinUI flyouts
    /// own their own motion.</item>
    /// </list>
    /// </summary>
    public static class WinUiThemeService
    {
        // ── Palettes: 1:1 copies of the WPF ThemeService palettes. ──────────
        private static readonly IReadOnlyDictionary<string, string> LightPalette =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ThemeWindowBackgroundBrush"] = "#FFFFFF",
                ["ThemeSurfaceBrush"] = "#FFFFFF",
                ["ThemeSurfaceAltBrush"] = "#F8F9FA",
                ["ThemeCanvasBrush"] = "#FFFFFF",
                ["ThemeBorderBrush"] = "#D1D5DB",
                ["ThemeForegroundBrush"] = "#1F2937",
                ["ThemeSubtleForegroundBrush"] = "#4B5563",
                ["ThemeControlHoverBrush"] = "#EEF0F2",
                ["ThemeControlPressedBrush"] = "#E2E5E9",
                ["ThemeSelectionBrush"] = "#DBEAFE",
                ["ThemeSelectionForegroundBrush"] = "#1E40AF",
                ["ThemeAccentBrush"] = "#2563EB",
                ["ThemeAccentHoverBrush"] = "#1D4ED8",
                ["ThemeAccentPressedBrush"] = "#1E40AF",
                ["ThemeDisabledForegroundBrush"] = "#9CA3AF",
                ["ThemeScrollbarTrackBrush"] = "#1F52606C",
                ["ThemeScrollbarThumbBrush"] = "#A85C6975",
                ["ThemeScrollbarThumbHoverBrush"] = "#CC394B5A",
                ["ThemeScrollbarThumbPressedBrush"] = "#E8212D38",
                ["ThemeSliderTrackBrush"] = "#221C5D99",
                ["ThemeMenuSeparatorBrush"] = "#1F1E2933",
                ["ThemeDeskBrush"] = "#FFFFFF",
                ["ThemePaperBrush"] = "#FFFFFF",
                ["ThemePaperAltBrush"] = "#F8F9FA",
                ["ThemeInkBrush"] = "#2563EB",
                ["ThemeMarginBrush"] = "#C2414B",
                ["ThemeMarkBrush"] = "#D9A72E",
                ["ThemeWindowOutlineBrush"] = "#1F2937"
            };

        private static readonly IReadOnlyDictionary<string, string> DarkPalette =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ThemeWindowBackgroundBrush"] = "#0C141D",
                ["ThemeSurfaceBrush"] = "#17212C",
                ["ThemeSurfaceAltBrush"] = "#1D2A37",
                ["ThemeCanvasBrush"] = "#081019",
                ["ThemeBorderBrush"] = "#314151",
                ["ThemeForegroundBrush"] = "#EEF2F4",
                ["ThemeSubtleForegroundBrush"] = "#A9B5BF",
                ["ThemeControlHoverBrush"] = "#223343",
                ["ThemeControlPressedBrush"] = "#2C4054",
                ["ThemeSelectionBrush"] = "#203E5C",
                ["ThemeSelectionForegroundBrush"] = "#E2F0FD",
                ["ThemeAccentBrush"] = "#6EACEA",
                ["ThemeAccentHoverBrush"] = "#8ABEF0",
                ["ThemeAccentPressedBrush"] = "#4A8CCC",
                ["ThemeDisabledForegroundBrush"] = "#6F7B86",
                ["ThemeScrollbarTrackBrush"] = "#3D465666",
                ["ThemeScrollbarThumbBrush"] = "#B88999AA",
                ["ThemeScrollbarThumbHoverBrush"] = "#D8B6C0CA",
                ["ThemeScrollbarThumbPressedBrush"] = "#F0E5EBF0",
                ["ThemeSliderTrackBrush"] = "#526EACEA",
                ["ThemeMenuSeparatorBrush"] = "#3AEEF2F4",
                ["ThemeDeskBrush"] = "#0C141D",
                ["ThemePaperBrush"] = "#17212C",
                ["ThemePaperAltBrush"] = "#1D2A37",
                ["ThemeInkBrush"] = "#6EACEA",
                ["ThemeMarginBrush"] = "#ED7A80",
                ["ThemeMarkBrush"] = "#F2C75C",
                ["ThemeWindowOutlineBrush"] = "#9CA3AF"
            };

        private static readonly IReadOnlyDictionary<string, string> HighContrastPalette =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ThemeWindowBackgroundBrush"] = "#000000",
                ["ThemeSurfaceBrush"] = "#000000",
                ["ThemeSurfaceAltBrush"] = "#1A1A1A",
                ["ThemeCanvasBrush"] = "#000000",
                ["ThemeBorderBrush"] = "#FFFFFF",
                ["ThemeForegroundBrush"] = "#FFFFFF",
                ["ThemeSubtleForegroundBrush"] = "#FFFFFF",
                ["ThemeControlHoverBrush"] = "#333333",
                ["ThemeControlPressedBrush"] = "#4D4D4D",
                ["ThemeSelectionBrush"] = "#FFFF00",
                ["ThemeSelectionForegroundBrush"] = "#000000",
                ["ThemeAccentBrush"] = "#00FFFF",
                ["ThemeAccentHoverBrush"] = "#FFFFFF",
                ["ThemeAccentPressedBrush"] = "#FFFF00",
                ["ThemeDisabledForegroundBrush"] = "#BFBFBF",
                ["ThemeScrollbarTrackBrush"] = "#000000",
                ["ThemeScrollbarThumbBrush"] = "#FFFFFF",
                ["ThemeScrollbarThumbHoverBrush"] = "#FFFF00",
                ["ThemeScrollbarThumbPressedBrush"] = "#00FFFF",
                ["ThemeSliderTrackBrush"] = "#FFFFFF",
                ["ThemeMenuSeparatorBrush"] = "#FFFFFF",
                ["ThemeFocusBrush"] = "#FFFF00",
                ["ThemeDeskBrush"] = "#000000",
                ["ThemePaperBrush"] = "#000000",
                ["ThemePaperAltBrush"] = "#1A1A1A",
                ["ThemeInkBrush"] = "#00FFFF",
                ["ThemeMarginBrush"] = "#FF8080",
                ["ThemeMarkBrush"] = "#FFFF00",
                ["ThemeWindowOutlineBrush"] = "#FFFFFF"
            };

        public static bool IsDark { get; private set; }

        public static bool IsHighContrast { get; private set; }

        public static bool ReduceMotion { get; private set; }

        public static bool ReduceTransparency { get; private set; }

        public static string CurrentTheme { get; private set; } = "Light";

        /// <summary>
        /// Effective editor workspace decoration. High contrast always uses
        /// Neutral/system colors even if the persisted preference says Paper
        /// or Slate.
        /// </summary>
        public static string CurrentWorkspaceBackdrop { get; private set; } = "Neutral";

        private static string RequestedTheme { get; set; } = "Light";

        private static string RequestedWorkspaceBackdrop { get; set; } = "Neutral";

        private static bool? ReduceMotionOverride { get; set; }

        private static bool? ReduceTransparencyOverride { get; set; }

        private static bool SystemEventsHooked { get; set; }

        private static UISettings _uiSettings;
        private static AccessibilitySettings _accessibilitySettings;

        // Registered windows get their root element's RequestedTheme flipped on
        // every Apply — the WinUI stand-in for WPF's app-wide DynamicResource
        // refresh of Fluent control themes.
        private static readonly List<Window> Windows = new List<Window>();

        public static bool ShouldAnimate => !ReduceMotion;

        /// <summary>
        /// Raised after every successful <see cref="Apply"/> so chrome that
        /// resolved brushes into plain values (tab template bindings that go
        /// through <see cref="Models.AppTab"/>'s computed properties) can
        /// re-resolve against the new palette.
        /// </summary>
        public static event EventHandler ThemeApplied;

        /// <summary>
        /// Registers a window so its root element's <c>RequestedTheme</c> tracks
        /// the effective palette. The window removes itself on <c>Closed</c>.
        /// </summary>
        public static void RegisterWindow(Window window)
        {
            if (window == null || Windows.Contains(window))
                return;

            Windows.Add(window);
            window.Closed += OnWindowClosed;
            ApplyRequestedThemeTo(window);
        }

        private static void OnWindowClosed(object sender, WindowEventArgs args)
        {
            if (sender is Window window)
            {
                window.Closed -= OnWindowClosed;
                Windows.Remove(window);
            }
            // Last window gone: unhook OS listeners so a late callback can't
            // resurrect the service or run Apply against a dying process.
            if (Windows.Count == 0)
                Shutdown();
        }

        /// <summary>
        /// Returns the one application animation duration. A zero duration is a
        /// real, interruptible state rather than a token that views may
        /// accidentally ignore when Reduce Motion is enabled.
        /// </summary>
        public static TimeSpan GetAnimationDuration(TimeSpan requested)
        {
            if (!ShouldAnimate || requested <= TimeSpan.Zero)
                return TimeSpan.Zero;

            if (Application.Current?.Resources.TryGetValue("ThemeAnimationDuration", out var value) == true &&
                value is Duration duration &&
                duration.HasTimeSpan && duration.TimeSpan > TimeSpan.Zero)
                return duration.TimeSpan;

            return requested;
        }

        /// <summary>
        /// Returns the live shadow opacity for code-created popup/chrome
        /// effects. Reading the resource at creation time keeps these effects
        /// aligned with ReduceTransparency without freezing a stale palette
        /// value.
        /// </summary>
        public static double GetShadowOpacity()
        {
            if (Application.Current?.Resources.TryGetValue("ThemeShadowOpacity", out var value) == true &&
                value is double opacity)
                return Math.Clamp(opacity, 0.0, 1.0);
            return ReduceTransparency ? 0.0 : 0.12;
        }

        /// <summary>
        /// Re-evaluates the System theme/accessibility inputs and reapplies when
        /// the requested theme derives from them. Mirrors the WPF refresh hook
        /// minus the deterministic test overrides.
        /// </summary>
        public static void RefreshSystemPreferences()
        {
            if (RequestedTheme == "System" || RequestedTheme == "HighContrast")
                Apply(RequestedTheme, ReduceMotionOverride, ReduceTransparencyOverride, RequestedWorkspaceBackdrop);
        }

        /// <summary>
        /// Unhooks the OS preference listeners. Called when the last window
        /// closes / at app shutdown.
        /// </summary>
        public static void Shutdown()
        {
            if (SystemEventsHooked)
            {
                if (_uiSettings != null)
                    _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
                if (_accessibilitySettings != null)
                    _accessibilitySettings.HighContrastChanged -= AccessibilitySettings_HighContrastChanged;
                _uiSettings = null;
                _accessibilitySettings = null;
            }
            SystemEventsHooked = false;
        }

        public static void ResetForTests()
        {
            Shutdown();
            RequestedTheme = "Light";
            RequestedWorkspaceBackdrop = "Neutral";
            ReduceMotionOverride = null;
            ReduceTransparencyOverride = null;
            IsDark = false;
            IsHighContrast = false;
            ReduceMotion = false;
            ReduceTransparency = false;
            CurrentTheme = "Light";
            CurrentWorkspaceBackdrop = "Neutral";
        }

        public static void Apply(
            string theme,
            bool? reduceMotion = null,
            bool? reduceTransparency = null,
            string workspaceBackdrop = null)
        {
            string normalizedTheme = NormalizeTheme(theme);
            RequestedTheme = normalizedTheme;
            RequestedWorkspaceBackdrop = NormalizeWorkspaceBackdrop(workspaceBackdrop);
            ReduceMotionOverride = reduceMotion;
            ReduceTransparencyOverride = reduceTransparency;
            IsHighContrast = normalizedTheme == "HighContrast" ||
                (normalizedTheme == "System" && IsSystemHighContrast());
            IsDark = !IsHighContrast &&
                (normalizedTheme == "Dark" ||
                 (normalizedTheme == "System" && IsSystemDarkTheme()));
            CurrentTheme = IsHighContrast ? "HighContrast" : (IsDark ? "Dark" : "Light");
            CurrentWorkspaceBackdrop = IsHighContrast ? "Neutral" : RequestedWorkspaceBackdrop;
            // Respect the system animation preference when the application has
            // not supplied an explicit override. High contrast also defaults to
            // reduced motion so focus and selection changes stay legible.
            ReduceMotion = IsHighContrast || (reduceMotion ?? !IsSystemAnimationEnabled());
            ReduceTransparency = IsHighContrast || (reduceTransparency ?? !IsSystemTransparencyEnabled());

            EnsureSystemEventsHooked();

            var resources = Application.Current?.Resources;
            if (resources == null)
                return;

            var palette = IsHighContrast
                ? HighContrastPalette
                : (IsDark ? DarkPalette : LightPalette);
            foreach (var entry in palette)
                resources[entry.Key] = CreateBrush(entry.Value);

            if (IsHighContrast && IsSystemHighContrast())
            {
                // High contrast is a system contract, not a decorative theme.
                // The WPF version repaints these keys from SystemColors; WinUI
                // exposes the OS picks through UIColorType, which covers
                // background/foreground/accent/complement only, so this is the
                // closest equivalent mapping.
                Brush windowBrush = CreateSystemBrush(UIColorType.Background, "#000000");
                Brush foregroundBrush = CreateSystemBrush(UIColorType.Foreground, "#FFFFFF");
                Brush accentBrush = CreateSystemBrush(UIColorType.Accent, "#1AEBFF");
                Brush complementBrush = CreateSystemBrush(UIColorType.Complement, "#333333");

                resources["ThemeWindowBackgroundBrush"] = windowBrush;
                resources["ThemeSurfaceBrush"] = windowBrush;
                resources["ThemeSurfaceAltBrush"] = complementBrush;
                resources["ThemeCanvasBrush"] = windowBrush;
                resources["ThemeBorderBrush"] = foregroundBrush;
                resources["ThemeForegroundBrush"] = foregroundBrush;
                resources["ThemeSubtleForegroundBrush"] = foregroundBrush;
                resources["ThemeControlHoverBrush"] = complementBrush;
                resources["ThemeControlPressedBrush"] = accentBrush;
                resources["ThemeSelectionBrush"] = accentBrush;
                resources["ThemeSelectionForegroundBrush"] = windowBrush;
                resources["ThemeAccentBrush"] = accentBrush;
                resources["ThemeAccentHoverBrush"] = foregroundBrush;
                resources["ThemeAccentPressedBrush"] = accentBrush;
                resources["ThemeDisabledForegroundBrush"] = foregroundBrush;
                resources["ThemeScrollbarTrackBrush"] = windowBrush;
                resources["ThemeScrollbarThumbBrush"] = foregroundBrush;
                resources["ThemeScrollbarThumbHoverBrush"] = accentBrush;
                resources["ThemeScrollbarThumbPressedBrush"] = foregroundBrush;
                resources["ThemeSliderTrackBrush"] = foregroundBrush;
                resources["ThemeMenuSeparatorBrush"] = foregroundBrush;
                resources["ThemeDeskBrush"] = windowBrush;
                resources["ThemePaperBrush"] = windowBrush;
                resources["ThemePaperAltBrush"] = complementBrush;
                resources["ThemeInkBrush"] = accentBrush;
                resources["ThemeMarginBrush"] = accentBrush;
                resources["ThemeMarkBrush"] = accentBrush;
                resources["ThemeWindowOutlineBrush"] = foregroundBrush;
            }

            var workspaceBrush = IsHighContrast
                ? (resources["ThemeCanvasBrush"] as Brush ?? CreateBrush("#000000"))
                : CreateBrush(GetWorkspaceBackdropColor(CurrentTheme, CurrentWorkspaceBackdrop));
            resources["ThemeWorkspaceBackdropBrush"] = workspaceBrush;
            if (IsHighContrast)
            {
                resources["ThemeDeskBrush"] = workspaceBrush;
                resources["ThemeCanvasBrush"] = workspaceBrush;
            }

            // Stable semantic aliases. Consumers use {ThemeResource} for these
            // keys; replacing the brush values above therefore refreshes every
            // open shell/editor/settings surface without static brush capture.
            resources["ThemeWindowBrush"] = resources["ThemeWindowBackgroundBrush"];
            resources["ThemeWorkspaceBrush"] = workspaceBrush;
            resources["ThemeSidebarBrush"] = resources["ThemeSurfaceAltBrush"];
            resources["ThemeToolbarBrush"] = resources["ThemePaperBrush"];
            resources["ThemeControlBrush"] = resources["ThemeSurfaceAltBrush"];
            resources["ThemeTextBrush"] = resources["ThemeForegroundBrush"];
            resources["ThemeSubtleTextBrush"] = resources["ThemeSubtleForegroundBrush"];
            resources["ThemeDangerBrush"] = IsHighContrast
                ? (IsSystemHighContrast()
                    ? CreateSystemBrush(UIColorType.Accent, "#FFFF00")
                    : CreateBrush(HighContrastPalette["ThemeSelectionBrush"]))
                : CreateBrush(IsDark ? "#FFFF8A8A" : "#FFB42318");

            // Fluent chrome-band tint (T12-A): the MainWindow chrome row
            // resolves this brush only while a system backdrop (Mica or
            // desktop acrylic) is installed, so it stays translucent enough
            // for the material to read through (~70% opacity). Under high
            // contrast or reduced transparency translucency is wrong — the
            // band falls back to the fully opaque toolbar brush. Windows
            // without a backdrop never resolve this key at all (opaque
            // ThemeToolbarBrush is applied instead).
            resources["ThemeChromeBrush"] = IsHighContrast || ReduceTransparency
                ? resources["ThemeToolbarBrush"]
                : CreateBrush(IsDark ? "#B317212C" : "#B3FFFFFF");

            // These tokens let custom controls opt into accessibility settings
            // without hard-coding animation or opacity values in every view.
            resources["ThemeAnimationDuration"] = new Duration(
                ReduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(160));
            resources["ThemeSurfaceOpacity"] = ReduceTransparency ? 1.0 : 0.96;
            resources["ThemeShadowOpacity"] = ReduceTransparency ? 0.0 : 0.12;
            resources["ThemeFocusBrush"] = IsHighContrast
                ? (IsSystemHighContrast()
                    ? CreateSystemBrush(UIColorType.Accent, "#FFFF00")
                    : CreateBrush(HighContrastPalette["ThemeFocusBrush"]))
                : CreateBrush(IsDark ? "#92C7F5" : "#154F86");

            foreach (var window in Windows)
                ApplyRequestedThemeTo(window);

            ThemeApplied?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Flips the Fluent theme of a window's content root. WinUI has no
        /// runtime application-level RequestedTheme setter, and ElementTheme
        /// cannot express HighContrast — Fluent handles real OS high contrast
        /// itself, so the app's explicit HighContrast falls back to Dark chrome
        /// under the (black) HC palette.
        /// </summary>
        private static void ApplyRequestedThemeTo(Window window)
        {
            if (window?.Content is FrameworkElement root)
            {
                root.RequestedTheme = IsDark || IsHighContrast
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
            }
        }

        private static void EnsureSystemEventsHooked()
        {
            if (SystemEventsHooked || Application.Current == null)
                return;

            // Independent hookups: if the first threw before subscribing, a
            // retry would double-subscribe the second — keep each in its own
            // try/catch and mark done only when both had their chance.
            bool uiHooked = false;
            bool accessibilityHooked = false;
            try
            {
                _uiSettings ??= new UISettings();
                _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
                uiHooked = true;
            }
            catch
            {
                // OS preference listeners are best-effort.
            }
            try
            {
                _accessibilitySettings ??= new AccessibilitySettings();
                _accessibilitySettings.HighContrastChanged += AccessibilitySettings_HighContrastChanged;
                accessibilityHooked = true;
            }
            catch
            {
                // OS preference listeners are best-effort.
            }

            // "Hooked" means "won't be retried" — partial success still counts
            // as hooked for the half that succeeded, and a retry for the
            // failed half would risk a double-subscribe on the working half.
            SystemEventsHooked = uiHooked || accessibilityHooked;
        }

        private static void UiSettings_ColorValuesChanged(UISettings sender, object args)
            => RefreshOnUiThread();

        private static void AccessibilitySettings_HighContrastChanged(AccessibilitySettings sender, object args)
            => RefreshOnUiThread();

        /// <summary>
        /// OS preference callbacks may arrive on a non-UI thread; marshal the
        /// refresh through a registered window's dispatcher.
        /// </summary>
        private static void RefreshOnUiThread()
        {
            if (RequestedTheme != "System" && !IsHighContrast)
                return;
            // No windows left: nothing to refresh, and RefreshSystemPreferences
            // → Apply would mutate Application.Resources off the UI thread.
            if (Windows.Count == 0)
                return;

            var dispatcher = Windows[0].DispatcherQueue;
            if (dispatcher == null || dispatcher.HasThreadAccess)
                RefreshSystemPreferences();
            else
                dispatcher.TryEnqueue(RefreshSystemPreferences);
        }

        private static bool IsSystemDarkTheme()
        {
            try
            {
                var color = (_uiSettings ??= new UISettings()).GetColorValue(UIColorType.Background);
                // Dark mode reports a near-black system background.
                return color.R + color.G + color.B < 128 * 3 / 2;
            }
            catch
            {
                // Fall through to the registry probe the WPF service uses.
            }

            try
            {
                object value = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1);
                return value is int intValue && intValue == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSystemHighContrast()
        {
            try
            {
                return (_accessibilitySettings ??= new AccessibilitySettings()).HighContrast;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSystemAnimationEnabled()
        {
            try
            {
                return (_uiSettings ??= new UISettings()).AnimationsEnabled;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsSystemTransparencyEnabled()
        {
            try
            {
                return (_uiSettings ??= new UISettings()).AdvancedEffectsEnabled;
            }
            catch
            {
                return true;
            }
        }

        private static string NormalizeTheme(string theme)
        {
            if (string.Equals(theme?.Trim(), "Dark", StringComparison.OrdinalIgnoreCase))
                return "Dark";
            if (string.Equals(theme?.Trim(), "HighContrast", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(theme?.Trim(), "High Contrast", StringComparison.OrdinalIgnoreCase))
                return "HighContrast";
            if (string.Equals(theme?.Trim(), "System", StringComparison.OrdinalIgnoreCase))
                return "System";
            return "Light";
        }

        public static string NormalizeWorkspaceBackdrop(string backdrop)
        {
            if (string.Equals(backdrop?.Trim(), "Paper", StringComparison.OrdinalIgnoreCase))
                return "Paper";
            if (string.Equals(backdrop?.Trim(), "Mist", StringComparison.OrdinalIgnoreCase))
                return "Mist";
            if (string.Equals(backdrop?.Trim(), "Warm", StringComparison.OrdinalIgnoreCase))
                return "Warm";
            if (string.Equals(backdrop?.Trim(), "Slate", StringComparison.OrdinalIgnoreCase))
                return "Slate";
            if (string.Equals(backdrop?.Trim(), "Midnight", StringComparison.OrdinalIgnoreCase))
                return "Midnight";
            return "Neutral";
        }

        private static string GetWorkspaceBackdropColor(string theme, string backdrop)
        {
            if (string.Equals(theme, "Dark", StringComparison.Ordinal))
            {
                return backdrop switch
                {
                    "Paper" => "#202A35",
                    "Mist" => "#22303A",
                    "Warm" => "#302B28",
                    "Slate" => "#2A3440",
                    "Midnight" => "#070B10",
                    _ => "#151D26"
                };
            }

            return backdrop switch
            {
                // Paper is cool and almost white; it is deliberately not
                // cream/yellow and remains distinct from the PDF page layer.
                "Paper" => "#F1F3F5",
                "Mist" => "#EAF2F6",
                "Warm" => "#F1E7DA",
                "Slate" => "#D7DBE1",
                "Midnight" => "#101722",
                _ => "#FFFFFF"
            };
        }

        internal static SolidColorBrush CreateBrush(string hex)
        {
            var color = ParseHexColor(hex);
            return new SolidColorBrush(color);
        }

        private static SolidColorBrush CreateSystemBrush(UIColorType colorType, string fallbackHex)
        {
            try
            {
                var color = (_uiSettings ??= new UISettings()).GetColorValue(colorType);
                return new SolidColorBrush(color);
            }
            catch
            {
                return CreateBrush(fallbackHex);
            }
        }

        private static Color ParseHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Colors.Transparent;

            string value = hex.Trim().TrimStart('#');
            if (value.Length == 6)
                value = "FF" + value;
            if (value.Length != 8 ||
                !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out uint argb))
                return Colors.Transparent;

            return Color.FromArgb(
                (byte)(argb >> 24),
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb);
        }
    }
}
