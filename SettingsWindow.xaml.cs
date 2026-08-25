using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Caelum.Models;
using Caelum.Services;

namespace Caelum
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _originalSettings;
        private bool _isApplyingLocalization;
        private static readonly string[] WorkspaceBackdropValues =
        {
            "Neutral", "Paper", "Mist", "Warm", "Slate", "Midnight"
        };

        public SettingsWindow(AppSettings currentSettings)
        {
            _originalSettings = CloneSettings(currentSettings);

            InitializeComponent();
            MouseLeftButtonDown += (sender, args) => DragMove();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Closed += (_, __) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            // Dropdown popup must not float above other applications after Alt-Tab (Task 10)
            PopupZOrderHelper.FixComboBoxPopupTopmost(LanguageComboBox);
            PopupZOrderHelper.FixComboBoxPopupTopmost(AutoSaveIntervalComboBox);
            PopupZOrderHelper.FixComboBoxPopupTopmost(SmoothingComboBox);
            PopupZOrderHelper.FixComboBoxPopupTopmost(PerformanceModeComboBox);
            PopupZOrderHelper.FixComboBoxPopupTopmost(ThemeComboBox);
            PopupZOrderHelper.FixComboBoxPopupTopmost(WorkspaceBackdropComboBox);
            LanguageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;

            LanguageComboBox.ItemsSource = LocalizationService.GetLanguageOptions();
            LanguageComboBox.SelectedValue = currentSettings.Language;

            AutoSaveIntervalComboBox.ItemsSource = new[] { 15, 30, 60, 120 };
            AutoSaveIntervalComboBox.SelectedItem = currentSettings.AutoSaveIntervalSeconds;

            PressureCheckBox.IsChecked = currentSettings.EnablePressure;
            PenOnlyCheckBox.IsChecked = currentSettings.PenOnlyMode;
            var smoothingIndex = Math.Max(0, Math.Min(3, currentSettings.StrokeSmoothing));
            var themeIndex = GetThemeIndex(currentSettings.Theme);
            var performanceModeIndex = GetPerformanceModeIndex(currentSettings.PerformanceMode);
            var workspaceBackdropIndex = GetWorkspaceBackdropIndex(currentSettings.WorkspaceBackdrop);
            AutoSaveIntervalComboBox.SelectionChanged += SettingsControl_SelectionChanged;
            PressureCheckBox.Checked += SettingsControl_Changed;
            PressureCheckBox.Unchecked += SettingsControl_Changed;
            PenOnlyCheckBox.Checked += SettingsControl_Changed;
            PenOnlyCheckBox.Unchecked += SettingsControl_Changed;
            SmoothingComboBox.SelectionChanged += SettingsControl_SelectionChanged;
            PerformanceModeComboBox.SelectionChanged += SettingsControl_SelectionChanged;
            ThemeComboBox.SelectionChanged += SettingsControl_SelectionChanged;
            WorkspaceBackdropComboBox.SelectionChanged += SettingsControl_SelectionChanged;

            ApplyLocalization();
            SmoothingComboBox.SelectedIndex = smoothingIndex;
            PerformanceModeComboBox.SelectedIndex = performanceModeIndex;
            ThemeComboBox.SelectedIndex = themeIndex;
            WorkspaceBackdropComboBox.SelectedIndex = workspaceBackdropIndex;
        }

        public AppSettings SelectedSettings { get; private set; }

        public void ApplyLocalization()
        {
            var smoothingIndex = SmoothingComboBox.SelectedIndex < 0 ? 2 : SmoothingComboBox.SelectedIndex;
            var performanceModeIndex = PerformanceModeComboBox.SelectedIndex < 0 ? 1 : PerformanceModeComboBox.SelectedIndex;
            var themeIndex = ThemeComboBox.SelectedIndex < 0 ? 0 : ThemeComboBox.SelectedIndex;
            var workspaceBackdropIndex = WorkspaceBackdropComboBox.SelectedIndex < 0 ? 0 : WorkspaceBackdropComboBox.SelectedIndex;

            _isApplyingLocalization = true;
            try
            {
                TitleTextBlock.Text = LocalizationService.Get("Settings.Title");
                SubtitleTextBlock.Text = LocalizationService.Get("Settings.Subtitle");
                LanguageLabelTextBlock.Text = LocalizationService.Get("Settings.LanguageLabel");
                LanguageHintTextBlock.Text = LocalizationService.Get("Settings.LanguageHint");
                UtilityLabelTextBlock.Text = LocalizationService.Get("Settings.UtilityLabel");
                UtilityHintTextBlock.Text = LocalizationService.Get("Settings.UtilityHint");
                AutoSaveIntervalLabelTextBlock.Text = LocalizationService.Get("Settings.AutoSaveInterval");
                PressureLabelTextBlock.Text = LocalizationService.Get("Settings.Pressure");
                PressureCheckBox.Content = LocalizationService.Get("Settings.Enabled");
                PenOnlyLabelTextBlock.Text = LocalizationService.Get("Settings.PenOnly");
                PenOnlyCheckBox.Content = LocalizationService.Get("Settings.Enabled");
                SmoothingLabelTextBlock.Text = LocalizationService.Get("Settings.Smoothing");
                PerformanceModeLabelTextBlock.Text = LocalizationService.Get("Settings.Performance");
                ThemeLabelTextBlock.Text = LocalizationService.Get("Settings.Theme");
                CancelButton.Content = LocalizationService.Get("Common.Cancel");
                SaveButton.Content = LocalizationService.Get("Common.Save");
                Title = LocalizationService.Get("Settings.Title");

                SmoothingComboBox.ItemsSource = new[]
                {
                    LocalizationService.Get("Editor.SmoothingOff"),
                    LocalizationService.Get("Editor.SmoothingLow"),
                    LocalizationService.Get("Editor.SmoothingMid"),
                    LocalizationService.Get("Editor.SmoothingHigh")
                };
                SmoothingComboBox.SelectedIndex = Math.Max(0, Math.Min(3, smoothingIndex));

                PerformanceModeComboBox.ItemsSource = new[]
                {
                    LocalizationService.Get("Settings.PerformanceBatterySaver"),
                    LocalizationService.Get("Settings.PerformanceBalanced"),
                    LocalizationService.Get("Settings.PerformanceBestQuality")
                };
                PerformanceModeComboBox.SelectedIndex = Math.Max(0, Math.Min(2, performanceModeIndex));

                ThemeComboBox.ItemsSource = new[]
                {
                    LocalizationService.Get("Settings.ThemeLight"),
                    LocalizationService.Get("Settings.ThemeDark"),
                    LocalizationService.Get("Settings.ThemeSystem"),
                    LocalizationService.Get("Settings.ThemeHighContrast")
                };
                ThemeComboBox.SelectedIndex = Math.Max(0, Math.Min(3, themeIndex));

                WorkspaceBackdropLabelTextBlock.Text = LocalizationService.Get("Settings.WorkspaceBackdrop");
                WorkspaceBackdropHintTextBlock.Text = LocalizationService.Get("Settings.WorkspaceBackdropHint");
                AutomationProperties.SetName(WorkspaceBackdropComboBox, WorkspaceBackdropLabelTextBlock.Text);
                AutomationProperties.SetHelpText(WorkspaceBackdropComboBox, WorkspaceBackdropHintTextBlock.Text);
                WorkspaceBackdropComboBox.ItemsSource = CreateWorkspaceBackdropOptions();
                WorkspaceBackdropComboBox.SelectedIndex = Math.Max(0, Math.Min(WorkspaceBackdropValues.Length - 1, workspaceBackdropIndex));
            }
            finally
            {
                _isApplyingLocalization = false;
            }
        }

        public AppSettings GetSelectedSettings()
        {
            var selectedLanguage = LanguageComboBox.SelectedValue is AppLanguage language
                ? language
                : AppLanguage.English;

            int autoSaveInterval = AutoSaveIntervalComboBox.SelectedItem is int interval ? interval : 60;
            int smoothing = Math.Max(0, Math.Min(3, SmoothingComboBox.SelectedIndex < 0 ? 2 : SmoothingComboBox.SelectedIndex));

            var selected = CloneSettings(_originalSettings);
            selected.Language = selectedLanguage;
            selected.AutoSaveIntervalSeconds = autoSaveInterval;
            selected.EnablePressure = PressureCheckBox.IsChecked != false;
            selected.PenOnlyMode = PenOnlyCheckBox.IsChecked == true;
            selected.StrokeSmoothing = smoothing;
            selected.PerformanceMode = GetPerformanceModeValue(PerformanceModeComboBox.SelectedIndex);
            selected.Theme = GetThemeValue(ThemeComboBox.SelectedIndex);
            selected.WorkspaceBackdrop = GetWorkspaceBackdropValue(WorkspaceBackdropComboBox.SelectedIndex);
            return selected;
        }

        private static int GetWorkspaceBackdropIndex(string value)
        {
            string normalized = ThemeService.NormalizeWorkspaceBackdrop(value);
            int index = Array.FindIndex(WorkspaceBackdropValues,
                candidate => string.Equals(candidate, normalized, StringComparison.Ordinal));
            return Math.Max(0, index);
        }

        private static string GetWorkspaceBackdropValue(int index)
        {
            return index >= 0 && index < WorkspaceBackdropValues.Length
                ? WorkspaceBackdropValues[index]
                : "Neutral";
        }

        private static WorkspaceBackdropOption[] CreateWorkspaceBackdropOptions()
        {
            string[] keys =
            {
                "Settings.WorkspaceBackdropNeutral",
                "Settings.WorkspaceBackdropPaper",
                "Settings.WorkspaceBackdropMist",
                "Settings.WorkspaceBackdropWarm",
                "Settings.WorkspaceBackdropSlate",
                "Settings.WorkspaceBackdropMidnight"
            };
            string[] previewColors = { "#FFFFFF", "#F5F3EE", "#EAF2F6", "#F1E7DA", "#D7DEE7", "#101722" };

            return WorkspaceBackdropValues.Select((value, index) => new WorkspaceBackdropOption(
                value,
                LocalizationService.Get(keys[index]),
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(previewColors[index])))).ToArray();
        }

        private static int GetPerformanceModeIndex(string value)
        {
            return PdfRenderPolicy.NormalizeMode(value) switch
            {
                PdfRenderPolicy.BatterySaver => 0,
                PdfRenderPolicy.BestQuality => 2,
                _ => 1
            };
        }

        private static string GetPerformanceModeValue(int index)
        {
            return index switch
            {
                0 => PdfRenderPolicy.BatterySaver,
                2 => PdfRenderPolicy.BestQuality,
                _ => PdfRenderPolicy.Balanced
            };
        }

        private static int GetThemeIndex(string theme)
        {
            if (string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (string.Equals(theme, "HighContrast", StringComparison.OrdinalIgnoreCase)
                || string.Equals(theme, "High Contrast", StringComparison.OrdinalIgnoreCase))
                return 3;
            return 0;
        }

        private static string GetThemeValue(int index)
        {
            return index switch
            {
                1 => "Dark",
                2 => "System",
                3 => "HighContrast",
                _ => "Light"
            };
        }

        private sealed class WorkspaceBackdropOption
        {
            public WorkspaceBackdropOption(string value, string displayName, Brush previewBrush)
            {
                Value = value;
                DisplayName = displayName;
                PreviewBrush = previewBrush;
            }

            public string Value { get; }
            public string DisplayName { get; }
            public Brush PreviewBrush { get; }

            public override string ToString() => DisplayName;
        }

        private static AppSettings CloneSettings(AppSettings source)
        {
            source ??= new AppSettings();

            return new AppSettings
            {
                Language = source.Language,
                EnablePressure = source.EnablePressure,
                WholeStrokeEraser = source.WholeStrokeEraser,
                InkSimulation = source.InkSimulation,
                ShapeRecognition = source.ShapeRecognition,
                PenOnlyMode = source.PenOnlyMode,
                RecentPenColors = source.RecentPenColors == null ? new System.Collections.Generic.List<string>() : new System.Collections.Generic.List<string>(source.RecentPenColors),
                RecentHighlighterColors = source.RecentHighlighterColors == null ? new System.Collections.Generic.List<string>() : new System.Collections.Generic.List<string>(source.RecentHighlighterColors),
                RecentTextColors = source.RecentTextColors == null ? new System.Collections.Generic.List<string>() : new System.Collections.Generic.List<string>(source.RecentTextColors),
                PenPresets = source.PenPresets == null ? new System.Collections.Generic.List<PenPreset>() : source.PenPresets.Where(p => p != null).Select(p => new PenPreset { Tool = p.Tool, ColorHex = p.ColorHex, Size = p.Size }).ToList(),
                StrokeSmoothing = source.StrokeSmoothing,
                AutoSaveIntervalSeconds = source.AutoSaveIntervalSeconds,
                DefaultPenColorHex = source.DefaultPenColorHex,
                DefaultPenSize = source.DefaultPenSize,
                Theme = source.Theme,
                WorkspaceBackdrop = source.WorkspaceBackdrop,
                PerformanceMode = source.PerformanceMode
            };
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            var previewSettings = GetSelectedSettings();
            LocalizationService.ApplyLanguage(previewSettings.Language);

            if (Owner is MainWindow mainWindow)
                mainWindow.PreviewSettings(previewSettings);
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            if (IsLoaded)
                ApplyLocalization();
        }

        private void SettingsControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingLocalization)
                return;
            PreviewCurrentSettings();
        }

        private void SettingsControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingLocalization)
                return;
            PreviewCurrentSettings();
        }

        private void PreviewCurrentSettings()
        {
            if (!IsLoaded)
                return;
            var previewSettings = GetSelectedSettings();
            if (Owner is MainWindow mainWindow)
                mainWindow.PreviewSettings(previewSettings);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedSettings = GetSelectedSettings();
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow mainWindow)
                mainWindow.PreviewSettings(_originalSettings);
            DialogResult = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow mainWindow)
                mainWindow.PreviewSettings(_originalSettings);
            DialogResult = false;
        }
    }
}
