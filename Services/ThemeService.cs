using System;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Caelum.Services
{
    /// <summary>
    /// Task 39: centralizes the small set of application chrome brushes that
    /// can be switched at runtime. The PDF page bitmap itself is never tinted.
    /// </summary>
    public static class ThemeService
    {
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
                ["ThemeMarkBrush"] = "#D9A72E"
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
                ["ThemeMarkBrush"] = "#F2C75C"
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
                ["ThemeMarkBrush"] = "#FFFF00"
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

        // These overrides are deliberately only exposed through the test
        // refresh hook below.  They let the STA contract tests exercise the
        // System + OS high-contrast path without changing Windows settings.
        private static bool? SystemHighContrastOverrideForTests { get; set; }

        private static bool? SystemDarkThemeOverrideForTests { get; set; }

        public static bool ShouldAnimate => !ReduceMotion;

        /// <summary>
        /// Returns the one application animation duration.  A zero duration
        /// is a real, interruptible state rather than a token that views may
        /// accidentally ignore when Reduce Motion is enabled.
        /// </summary>
        public static TimeSpan GetAnimationDuration(TimeSpan requested)
        {
            if (!ShouldAnimate || requested <= TimeSpan.Zero)
                return TimeSpan.Zero;

            if (Application.Current?.Resources["ThemeAnimationDuration"] is Duration duration &&
                duration.HasTimeSpan && duration.TimeSpan > TimeSpan.Zero)
                return duration.TimeSpan;

            return requested;
        }

        /// <summary>
        /// Returns the live shadow opacity for code-created popup/chrome
        /// effects.  Reading the resource at creation time keeps these
        /// effects aligned with ReduceTransparency without freezing a brush or
        /// retaining a stale palette value.
        /// </summary>
        public static double GetShadowOpacity()
        {
            if (Application.Current?.Resources["ThemeShadowOpacity"] is double opacity)
                return Math.Clamp(opacity, 0.0, 1.0);
            return ReduceTransparency ? 0.0 : 0.12;
        }

        /// <summary>
        /// Re-evaluates the System theme/accessibility inputs.  Optional
        /// overrides are a deterministic test hook; passing null restores the
        /// real Windows values.  The normal runtime path is still driven by
        /// SystemEvents.UserPreferenceChanged.
        /// </summary>
        public static void RefreshSystemPreferencesForTests(bool? highContrast = null, bool? darkTheme = null)
        {
            SystemHighContrastOverrideForTests = highContrast;
            SystemDarkThemeOverrideForTests = darkTheme;
            if (RequestedTheme == "System" || RequestedTheme == "HighContrast")
                Apply(RequestedTheme, ReduceMotionOverride, ReduceTransparencyOverride, RequestedWorkspaceBackdrop);
        }

        /// <summary>
        /// Unhooks the process-wide Windows preference event.  App calls this
        /// at shutdown and tests can use ResetForTests to avoid static-state
        /// leakage between WPF application instances.
        /// </summary>
        public static void Shutdown()
        {
            if (SystemEventsHooked)
                SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            SystemEventsHooked = false;
        }

        public static void ResetForTests()
        {
            Shutdown();
            SystemHighContrastOverrideForTests = null;
            SystemDarkThemeOverrideForTests = null;
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
            ReduceMotion = IsHighContrast || (reduceMotion ?? !SystemParameters.ClientAreaAnimation);
            ReduceTransparency = IsHighContrast || (reduceTransparency ?? false);

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
                // Use the OS brushes directly so user-selected Windows colors
                // flow through without a hard-coded black/white assumption.
                resources["ThemeWindowBackgroundBrush"] = SystemColors.WindowBrush;
                resources["ThemeSurfaceBrush"] = SystemColors.WindowBrush;
                resources["ThemeSurfaceAltBrush"] = SystemColors.ControlBrush;
                resources["ThemeCanvasBrush"] = SystemColors.WindowBrush;
                resources["ThemeBorderBrush"] = SystemColors.ActiveBorderBrush;
                resources["ThemeForegroundBrush"] = SystemColors.WindowTextBrush;
                resources["ThemeSubtleForegroundBrush"] = SystemColors.GrayTextBrush;
                resources["ThemeControlHoverBrush"] = SystemColors.HighlightBrush;
                resources["ThemeControlPressedBrush"] = SystemColors.HighlightBrush;
                resources["ThemeSelectionBrush"] = SystemColors.HighlightBrush;
                resources["ThemeSelectionForegroundBrush"] = SystemColors.HighlightTextBrush;
                resources["ThemeAccentBrush"] = SystemColors.HotTrackBrush;
                resources["ThemeAccentHoverBrush"] = SystemColors.HighlightTextBrush;
                resources["ThemeAccentPressedBrush"] = SystemColors.HighlightBrush;
                resources["ThemeDisabledForegroundBrush"] = SystemColors.GrayTextBrush;
                resources["ThemeScrollbarTrackBrush"] = SystemColors.WindowBrush;
                resources["ThemeScrollbarThumbBrush"] = SystemColors.HighlightBrush;
                resources["ThemeScrollbarThumbHoverBrush"] = SystemColors.HighlightTextBrush;
                resources["ThemeScrollbarThumbPressedBrush"] = SystemColors.HighlightBrush;
                resources["ThemeSliderTrackBrush"] = SystemColors.ActiveBorderBrush;
                resources["ThemeMenuSeparatorBrush"] = SystemColors.ActiveBorderBrush;
                resources["ThemeDeskBrush"] = SystemColors.WindowBrush;
                resources["ThemePaperBrush"] = SystemColors.WindowBrush;
                resources["ThemePaperAltBrush"] = SystemColors.ControlBrush;
                resources["ThemeInkBrush"] = SystemColors.HotTrackBrush;
                resources["ThemeMarginBrush"] = SystemColors.HighlightBrush;
                resources["ThemeMarkBrush"] = SystemColors.HighlightBrush;
            }

            var workspaceBrush = IsHighContrast
                ? (resources["ThemeCanvasBrush"] as Brush ?? SystemColors.WindowBrush)
                : CreateBrush(GetWorkspaceBackdropColor(CurrentTheme, CurrentWorkspaceBackdrop));
            resources["ThemeWorkspaceBackdropBrush"] = workspaceBrush;
            if (IsHighContrast)
            {
                resources["ThemeDeskBrush"] = workspaceBrush;
                resources["ThemeCanvasBrush"] = workspaceBrush;
            }

            // Stable semantic aliases. Consumers use DynamicResource for these
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
                    ? SystemColors.HighlightBrush
                    : CreateBrush(HighContrastPalette["ThemeSelectionBrush"]))
                : CreateBrush(IsDark ? "#FFFF8A8A" : "#FFB42318");

            // These tokens let custom controls opt into accessibility settings
            // without hard-coding animation or opacity values in every view.
            resources["ThemeAnimationDuration"] = new Duration(
                ReduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(160));
            resources["ThemeSurfaceOpacity"] = ReduceTransparency ? 1.0 : 0.96;
            resources["ThemeShadowOpacity"] = ReduceTransparency ? 0.0 : 0.12;
            resources["ThemePopupAnimation"] = ReduceMotion ? PopupAnimation.None : PopupAnimation.Slide;
            resources["ThemeFocusBrush"] = IsHighContrast
                ? (IsSystemHighContrast()
                    ? SystemColors.HighlightBrush
                    : CreateBrush(HighContrastPalette["ThemeFocusBrush"]))
                : CreateBrush(IsDark ? "#92C7F5" : "#154F86");
        }

        private static void EnsureSystemEventsHooked()
        {
            if (SystemEventsHooked || Application.Current == null)
                return;

            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            SystemEventsHooked = true;
        }

        private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if ((RequestedTheme != "System" && !IsHighContrast) ||
                (e.Category != UserPreferenceCategory.General &&
                 e.Category != UserPreferenceCategory.Accessibility))
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            void Refresh() => Apply(RequestedTheme, ReduceMotionOverride, ReduceTransparencyOverride, RequestedWorkspaceBackdrop);
            if (dispatcher.CheckAccess())
                Refresh();
            else
                dispatcher.BeginInvoke((Action)Refresh, DispatcherPriority.ApplicationIdle);
        }

        private static bool IsSystemDarkTheme()
        {
            if (SystemDarkThemeOverrideForTests.HasValue)
                return SystemDarkThemeOverrideForTests.Value;

            try
            {
                object value = Registry.GetValue(
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
            return SystemHighContrastOverrideForTests ?? SystemParameters.HighContrast;
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
            if (string.Equals(backdrop?.Trim(), "Slate", StringComparison.OrdinalIgnoreCase))
                return "Slate";
            return "Neutral";
        }

        private static string GetWorkspaceBackdropColor(string theme, string backdrop)
        {
            if (string.Equals(theme, "Dark", StringComparison.Ordinal))
            {
                return backdrop switch
                {
                    "Paper" => "#202A35",
                    "Slate" => "#2A3440",
                    _ => "#151D26"
                };
            }

            return backdrop switch
            {
                // Paper is cool and almost white; it is deliberately not
                // cream/yellow and remains distinct from the PDF page layer.
                "Paper" => "#F1F3F5",
                "Slate" => "#D7DBE1",
                _ => "#FFFFFF"
            };
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
    }
}
