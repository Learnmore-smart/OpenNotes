using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Caelum.Models;

namespace Caelum.Services
{
    public static class AppSettingsService
    {
        private static readonly object SyncRoot = new object();
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static string _cachedSettingsPath;
        private static AppSettings _cachedSettings;

        private static string GetSettingsPath()
        {
            var folder = ProductInfo.GetDataDirectory();
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "settings.json");
        }

        public static AppSettings Load()
        {
            lock (SyncRoot)
            {
                var path = GetSettingsPath();
                if (_cachedSettings == null
                    || !string.Equals(_cachedSettingsPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _cachedSettings = ReadSettingsCore(path);
                    _cachedSettingsPath = path;
                }
                return Clone(_cachedSettings);
            }
        }

        public static AppSettings Save(AppSettings settings)
        {
            lock (SyncRoot)
            {
                var path = GetSettingsPath();
                _cachedSettings = Sanitize(settings);
                _cachedSettingsPath = path;
                File.WriteAllText(path, JsonSerializer.Serialize(_cachedSettings, SerializerOptions));
                return Clone(_cachedSettings);
            }
        }

        private static AppSettings ReadSettingsCore(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new AppSettings();

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return new AppSettings();

                return Sanitize(JsonSerializer.Deserialize<AppSettings>(json));
            }
            catch
            {
                return new AppSettings();
            }
        }

        private static AppSettings Sanitize(AppSettings settings)
        {
            var source = settings ?? new AppSettings();
            var language = Enum.IsDefined(typeof(AppLanguage), source.Language)
                ? source.Language
                : AppLanguage.English;
            // Task 24: clamp hand-edited out-of-range values into 0..3.
            int smoothing = source.StrokeSmoothing < 0 || source.StrokeSmoothing > 3
                ? 2
                : source.StrokeSmoothing;
            int autoSaveInterval = source.AutoSaveIntervalSeconds == 15
                || source.AutoSaveIntervalSeconds == 30
                || source.AutoSaveIntervalSeconds == 60
                || source.AutoSaveIntervalSeconds == 120
                ? source.AutoSaveIntervalSeconds
                : 60;
            double defaultPenSize = source.DefaultPenSize < 0.5 || source.DefaultPenSize > 24
                || double.IsNaN(source.DefaultPenSize) || double.IsInfinity(source.DefaultPenSize)
                ? 1.5
                : source.DefaultPenSize;
            string defaultPenColor = IsHexColor(source.DefaultPenColorHex)
                ? source.DefaultPenColorHex.ToUpperInvariant()
                : "#000000";
            string theme = NormalizeTheme(source.Theme);
            string workspaceBackdrop = NormalizeWorkspaceBackdrop(source.WorkspaceBackdrop);
            string performanceMode = PdfRenderPolicy.NormalizeMode(source.PerformanceMode);

            return new AppSettings
            {
                Language = language,
                EnablePressure = source.EnablePressure,
                WholeStrokeEraser = source.WholeStrokeEraser,
                InkSimulation = source.InkSimulation,
                ShapeRecognition = source.ShapeRecognition,
                PenOnlyMode = source.PenOnlyMode,
                RecentPenColors = CopyColorList(source.RecentPenColors),
                RecentHighlighterColors = CopyColorList(source.RecentHighlighterColors),
                RecentTextColors = CopyColorList(source.RecentTextColors),
                PenPresets = CopyPenPresets(source.PenPresets),
                StrokeSmoothing = smoothing,
                AutoSaveIntervalSeconds = autoSaveInterval,
                DefaultPenColorHex = defaultPenColor,
                DefaultPenSize = defaultPenSize,
                Theme = theme,
                WorkspaceBackdrop = workspaceBackdrop,
                PerformanceMode = performanceMode
            };
        }

        private static AppSettings Clone(AppSettings settings)
        {
            return new AppSettings
            {
                Language = settings.Language,
                EnablePressure = settings.EnablePressure,
                WholeStrokeEraser = settings.WholeStrokeEraser,
                InkSimulation = settings.InkSimulation,
                ShapeRecognition = settings.ShapeRecognition,
                PenOnlyMode = settings.PenOnlyMode,
                RecentPenColors = CopyColorList(settings.RecentPenColors),
                RecentHighlighterColors = CopyColorList(settings.RecentHighlighterColors),
                RecentTextColors = CopyColorList(settings.RecentTextColors),
                PenPresets = CopyPenPresets(settings.PenPresets),
                StrokeSmoothing = settings.StrokeSmoothing,
                AutoSaveIntervalSeconds = settings.AutoSaveIntervalSeconds,
                DefaultPenColorHex = settings.DefaultPenColorHex,
                DefaultPenSize = settings.DefaultPenSize,
                Theme = settings.Theme,
                WorkspaceBackdrop = settings.WorkspaceBackdrop,
                PerformanceMode = settings.PerformanceMode
            };
        }

        /// <summary>
        /// Task 14: null-guarded list copy so old settings.json files without
        /// the recent-color fields deserialize to empty lists instead of null.
        /// </summary>
        private static List<string> CopyColorList(List<string> source)
        {
            var result = new List<string>();
            foreach (var value in source ?? new List<string>())
            {
                if (!IsHexColor(value))
                    continue;

                var normalized = value.ToUpperInvariant();
                if (!result.Contains(normalized))
                    result.Add(normalized);
                if (result.Count == 8)
                    break;
            }

            return result;
        }

        private static bool IsHexColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#')
                return false;

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                bool isDigit = c >= '0' && c <= '9';
                bool isUpper = c >= 'A' && c <= 'F';
                bool isLower = c >= 'a' && c <= 'f';
                if (!isDigit && !isUpper && !isLower)
                    return false;
            }

            return true;
        }

        private static string NormalizeTheme(string value)
        {
            if (string.Equals(value?.Trim(), "Dark", StringComparison.OrdinalIgnoreCase))
                return "Dark";
            if (string.Equals(value?.Trim(), "System", StringComparison.OrdinalIgnoreCase))
                return "System";
            if (string.Equals(value?.Trim(), "HighContrast", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value?.Trim(), "High Contrast", StringComparison.OrdinalIgnoreCase))
                return "HighContrast";
            return "Light";
        }

        private static string NormalizeWorkspaceBackdrop(string value)
        {
            if (string.Equals(value?.Trim(), "Paper", StringComparison.OrdinalIgnoreCase))
                return "Paper";
            if (string.Equals(value?.Trim(), "Mist", StringComparison.OrdinalIgnoreCase))
                return "Mist";
            if (string.Equals(value?.Trim(), "Warm", StringComparison.OrdinalIgnoreCase))
                return "Warm";
            if (string.Equals(value?.Trim(), "Slate", StringComparison.OrdinalIgnoreCase))
                return "Slate";
            if (string.Equals(value?.Trim(), "Midnight", StringComparison.OrdinalIgnoreCase))
                return "Midnight";
            return "Neutral";
        }

        /// <summary>
        /// Task 23: null-guarded deep copy of the pen preset slots (each
        /// entry cloned so mutating one settings clone never aliases another).
        /// Old settings.json files without the field deserialize to an empty
        /// list; EditorPage may use in-memory visual fallbacks without writing
        /// those defaults back to the persisted settings.
        /// </summary>
        private static List<PenPreset> CopyPenPresets(List<PenPreset> source)
        {
            if (source == null)
                return new List<PenPreset>();

            var copy = new List<PenPreset>(Math.Min(source.Count, 3));
            foreach (var preset in source)
            {
                if (preset == null)
                    continue;

                bool isPen = string.Equals(preset.Tool, "Pen", StringComparison.OrdinalIgnoreCase);
                bool isHighlighter = string.Equals(preset.Tool, "Highlighter", StringComparison.OrdinalIgnoreCase);
                if ((!isPen && !isHighlighter)
                    || !IsHexColor(preset.ColorHex)
                    || double.IsNaN(preset.Size)
                    || double.IsInfinity(preset.Size)
                    || preset.Size < 0.5
                    || preset.Size > 24)
                {
                    continue;
                }

                copy.Add(new PenPreset
                {
                    Tool = isHighlighter ? "Highlighter" : "Pen",
                    ColorHex = preset.ColorHex.ToUpperInvariant(),
                    Size = preset.Size
                });

                if (copy.Count == 3)
                    break;
            }
            return copy;
        }
    }
}
