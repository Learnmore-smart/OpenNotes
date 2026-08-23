using System;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Windows;
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
                ["ThemeWindowBackgroundBrush"] = "#F3EFE7",
                ["ThemeSurfaceBrush"] = "#FFFDF8",
                ["ThemeSurfaceAltBrush"] = "#F3EFE6",
                ["ThemeCanvasBrush"] = "#D7D3CB",
                ["ThemeBorderBrush"] = "#D2CBC0",
                ["ThemeForegroundBrush"] = "#1E2933",
                ["ThemeSubtleForegroundBrush"] = "#66717B",
                ["ThemeControlHoverBrush"] = "#E6EDF4",
                ["ThemeControlPressedBrush"] = "#D3E0EB",
                ["ThemeSelectionBrush"] = "#DCEAF8",
                ["ThemeSelectionForegroundBrush"] = "#164C86",
                ["ThemeAccentBrush"] = "#1C5D99",
                ["ThemeAccentHoverBrush"] = "#2872AF",
                ["ThemeAccentPressedBrush"] = "#124776",
                ["ThemeDisabledForegroundBrush"] = "#949694",
                ["ThemeScrollbarTrackBrush"] = "#1F52606C",
                ["ThemeScrollbarThumbBrush"] = "#A85C6975",
                ["ThemeScrollbarThumbHoverBrush"] = "#CC394B5A",
                ["ThemeScrollbarThumbPressedBrush"] = "#E8212D38",
                ["ThemeSliderTrackBrush"] = "#221C5D99",
                ["ThemeMenuSeparatorBrush"] = "#1F1E2933",
                ["ThemeDeskBrush"] = "#E6E1D8",
                ["ThemePaperBrush"] = "#FFFDF7",
                ["ThemePaperAltBrush"] = "#F3EFE6",
                ["ThemeInkBrush"] = "#1C5D99",
                ["ThemeMarginBrush"] = "#B94B52",
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

        private static string RequestedTheme { get; set; } = "Light";

        private static bool? ReduceMotionOverride { get; set; }

        private static bool? ReduceTransparencyOverride { get; set; }

        private static bool SystemEventsHooked { get; set; }

        public static void Apply(string theme, bool? reduceMotion = null, bool? reduceTransparency = null)
        {
            string normalizedTheme = NormalizeTheme(theme);
            RequestedTheme = normalizedTheme;
            ReduceMotionOverride = reduceMotion;
            ReduceTransparencyOverride = reduceTransparency;
            IsHighContrast = normalizedTheme == "HighContrast" ||
                (normalizedTheme == "System" && SystemParameters.HighContrast);
            IsDark = !IsHighContrast &&
                (normalizedTheme == "Dark" ||
                 (normalizedTheme == "System" && IsSystemDarkTheme()));
            CurrentTheme = IsHighContrast ? "HighContrast" : (IsDark ? "Dark" : "Light");
            // Respect the system animation preference when the application has
            // not supplied an explicit override. High contrast also defaults to
            // reduced motion so focus and selection changes stay legible.
            ReduceMotion = reduceMotion ?? (!SystemParameters.ClientAreaAnimation || IsHighContrast);
            ReduceTransparency = reduceTransparency ?? IsHighContrast;

            EnsureSystemEventsHooked();

            var resources = Application.Current?.Resources;
            if (resources == null)
                return;

            var palette = IsHighContrast
                ? HighContrastPalette
                : (IsDark ? DarkPalette : LightPalette);
            foreach (var entry in palette)
                resources[entry.Key] = CreateBrush(entry.Value);

            // These tokens let custom controls opt into accessibility settings
            // without hard-coding animation or opacity values in every view.
            resources["ThemeAnimationDuration"] = new Duration(
                ReduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(160));
            resources["ThemeSurfaceOpacity"] = ReduceTransparency ? 1.0 : 0.96;
            resources["ThemeFocusBrush"] = CreateBrush(
                IsHighContrast ? HighContrastPalette["ThemeFocusBrush"] : (IsDark ? "#92C7F5" : "#154F86"));
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
            if (RequestedTheme != "System" ||
                (e.Category != UserPreferenceCategory.General &&
                 e.Category != UserPreferenceCategory.Accessibility))
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            void Refresh() => Apply(RequestedTheme, ReduceMotionOverride, ReduceTransparencyOverride);
            if (dispatcher.CheckAccess())
                Refresh();
            else
                dispatcher.BeginInvoke((Action)Refresh, DispatcherPriority.ApplicationIdle);
        }

        private static bool IsSystemDarkTheme()
        {
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

        private static SolidColorBrush CreateBrush(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
    }
}
