using System;
using System.Collections.Generic;
using System.Linq;
using Caelum.Models;
using Caelum.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Caelum
{
    /// <summary>
    /// WinUI port of the WPF <c>SettingsWindow</c> as a <see cref="ContentDialog"/>
    /// (a borderless WPF window has no WinUI counterpart; the shared
    /// <see cref="WinUiDialogService"/> gate serializes this dialog with every
    /// other ContentDialog on the window's XamlRoot).
    ///
    /// Semantics mirror the WPF window exactly:
    /// <list type="bullet">
    /// <item>The dialog stages a clone of the current settings — every control
    /// change live-previews through <see cref="MainWindow.PreviewSettings"/>
    /// (language + theme + editor tool settings) just like the WPF owner-call.
    /// </item>
    /// <item>Save (primary button) captures <see cref="SelectedSettings"/> for
    /// the caller to persist via <c>AppSettingsService.Save</c>.</item>
    /// <item>Cancel, the close button, Esc or dismiss all revert the previewed
    /// values with <see cref="MainWindow.PreviewSettings"/>(original) —
    /// ContentDialogs can close without a button, so the revert lives in
    /// <see cref="ContentDialog.Closed"/> rather than the button handlers.</item>
    /// <item><see cref="LocalizationService.LanguageChanged"/> re-localizes the
    /// dialog live (WPF parity), preserving the current combo indices.</item>
    /// </list>
    ///
    /// Theme brushes baked into code-built labels are re-resolved on
    /// <see cref="WinUiThemeService.ThemeApplied"/> so previewed theme changes
    /// repaint the open dialog like WPF's DynamicResource did.
    /// </summary>
    public sealed class SettingsDialog : ContentDialog
    {
        private readonly AppSettings _originalSettings;
        private readonly List<ThemeBinding> _themeBindings = new();
        private bool _isApplyingLocalization;
        private bool _opened;
        private bool _confirmed;

        private static readonly string[] WorkspaceBackdropValues =
        {
            "Neutral", "Paper", "Mist", "Warm", "Slate", "Midnight"
        };

        private ComboBox _languageComboBox;
        private ComboBox _autoSaveIntervalComboBox;
        private ToggleSwitch _pressureSwitch;
        private ToggleSwitch _penOnlySwitch;
        private ComboBox _smoothingComboBox;
        private ComboBox _themeComboBox;
        private ComboBox _performanceModeComboBox;
        private ComboBox _workspaceBackdropComboBox;
        private Border _workspaceBackdropSwatch;

        private TextBlock _subtitleText;
        private TextBlock _languageLabelText;
        private TextBlock _languageHintText;
        private TextBlock _utilityLabelText;
        private TextBlock _utilityHintText;
        private TextBlock _autoSaveIntervalLabelText;
        private TextBlock _pressureLabelText;
        private TextBlock _penOnlyLabelText;
        private TextBlock _smoothingLabelText;
        private TextBlock _performanceModeLabelText;
        private TextBlock _themeLabelText;
        private TextBlock _workspaceBackdropLabelText;
        private TextBlock _workspaceBackdropHintText;

        private sealed class ThemeBinding
        {
            public ThemeBinding(DependencyObject element, DependencyProperty property, string key, Color fallback)
            {
                Element = element;
                Property = property;
                Key = key;
                Fallback = fallback;
            }

            public DependencyObject Element { get; }
            public DependencyProperty Property { get; }
            public string Key { get; }
            public Color Fallback { get; }
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

        public SettingsDialog(AppSettings currentSettings)
        {
            _originalSettings = CloneSettings(currentSettings);

            AutomationProperties.SetAutomationId(this, "SettingsDialog");
            MinWidth = 560;
            MaxWidth = 680;
            MaxHeight = 760;
            DefaultButton = ContentDialogButton.Primary;

            Content = BuildContent(currentSettings);
            ApplyLocalization();
            RestoreControlState(currentSettings);

            Opened += (_, _) => _opened = true;
            PrimaryButtonClick += (_, _) =>
            {
                SelectedSettings = GetSelectedSettings();
                _confirmed = true;
            };
            LocalizationService.LanguageChanged += OnLanguageChanged;
            WinUiThemeService.ThemeApplied += OnThemeApplied;
            Closed += (_, _) =>
            {
                LocalizationService.LanguageChanged -= OnLanguageChanged;
                WinUiThemeService.ThemeApplied -= OnThemeApplied;
                // WPF CancelButton_Click/CloseButton_Click parity — also covers
                // Esc/light dismiss, which the WPF window could not produce.
                if (!_confirmed)
                    MainWindow.Current?.PreviewSettings(_originalSettings);
            };
        }

        /// <summary>Populated when Save (primary) is pressed; null otherwise.</summary>
        public AppSettings SelectedSettings { get; private set; }

        // ── Control construction ──────────────────────────────────────────

        private FrameworkElement BuildContent(AppSettings currentSettings)
        {
            var content = new StackPanel { Spacing = 6 };

            _subtitleText = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
            BindBrush(_subtitleText, TextBlock.ForegroundProperty, "ThemeSubtleTextBrush",
                Color.FromArgb(255, 0x4B, 0x55, 0x63));
            content.Children.Add(_subtitleText);

            _languageLabelText = SectionLabel();
            _languageHintText = HintText();
            content.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 18, 0, 0),
                Children = { _languageLabelText, _languageHintText }
            });

            _languageComboBox = new ComboBox
            {
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = LocalizationService.GetLanguageOptions(),
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Language",
            };
            AutomationProperties.SetAutomationId(_languageComboBox, "LanguageComboBox");
            _languageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;
            content.Children.Add(_languageComboBox);

            _utilityLabelText = SectionLabel();
            _utilityHintText = HintText();
            content.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 22, 0, 4),
                Children = { _utilityLabelText, _utilityHintText }
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 7; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.Children.Add(grid);

            int row = 0;
            _autoSaveIntervalLabelText = RowLabel(grid, row);
            _autoSaveIntervalComboBox = new ComboBox
            {
                Width = 160,
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
                ItemsSource = new[] { 15, 30, 60, 120 },
            };
            AutomationProperties.SetAutomationId(_autoSaveIntervalComboBox, "AutoSaveIntervalComboBox");
            _autoSaveIntervalComboBox.SelectionChanged += SettingsControl_SelectionChanged;
            Grid.SetRow(_autoSaveIntervalComboBox, row);
            Grid.SetColumn(_autoSaveIntervalComboBox, 1);
            grid.Children.Add(_autoSaveIntervalComboBox);
            row++;

            _pressureLabelText = RowLabel(grid, row);
            _pressureSwitch = new ToggleSwitch
            {
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            AutomationProperties.SetAutomationId(_pressureSwitch, "PressureCheckBox");
            _pressureSwitch.Toggled += SettingsControl_Toggled;
            Grid.SetRow(_pressureSwitch, row);
            Grid.SetColumn(_pressureSwitch, 1);
            grid.Children.Add(_pressureSwitch);
            row++;

            _smoothingLabelText = RowLabel(grid, row);
            _smoothingComboBox = new ComboBox
            {
                Width = 160,
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            AutomationProperties.SetAutomationId(_smoothingComboBox, "SmoothingComboBox");
            _smoothingComboBox.SelectionChanged += SettingsControl_SelectionChanged;
            Grid.SetRow(_smoothingComboBox, row);
            Grid.SetColumn(_smoothingComboBox, 1);
            grid.Children.Add(_smoothingComboBox);
            row++;

            _themeLabelText = RowLabel(grid, row);
            _themeComboBox = new ComboBox
            {
                Width = 160,
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            AutomationProperties.SetAutomationId(_themeComboBox, "ThemeComboBox");
            _themeComboBox.SelectionChanged += SettingsControl_SelectionChanged;
            Grid.SetRow(_themeComboBox, row);
            Grid.SetColumn(_themeComboBox, 1);
            grid.Children.Add(_themeComboBox);
            row++;

            _performanceModeLabelText = RowLabel(grid, row);
            _performanceModeComboBox = new ComboBox
            {
                Width = 160,
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            AutomationProperties.SetAutomationId(_performanceModeComboBox, "PerformanceModeComboBox");
            _performanceModeComboBox.SelectionChanged += SettingsControl_SelectionChanged;
            Grid.SetRow(_performanceModeComboBox, row);
            Grid.SetColumn(_performanceModeComboBox, 1);
            grid.Children.Add(_performanceModeComboBox);
            row++;

            // Workspace backdrop: label + hint stacked (WPF parity); the
            // selected option's preview swatch sits beside the ComboBox —
            // WinUI's selection box cannot host the per-item swatch elements
            // from the dropdown, so the swatch shows the CURRENT pick.
            var workspaceLabels = new StackPanel();
            _workspaceBackdropLabelText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            BindBrush(_workspaceBackdropLabelText, TextBlock.ForegroundProperty, "ThemeTextBrush",
                Color.FromArgb(255, 0x1F, 0x29, 0x37));
            _workspaceBackdropHintText = new TextBlock
            {
                Margin = new Thickness(0, 2, 12, 0),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
            };
            BindBrush(_workspaceBackdropHintText, TextBlock.ForegroundProperty, "ThemeSubtleTextBrush",
                Color.FromArgb(255, 0x4B, 0x55, 0x63));
            workspaceLabels.Children.Add(_workspaceBackdropLabelText);
            workspaceLabels.Children.Add(_workspaceBackdropHintText);
            Grid.SetRow(workspaceLabels, row);
            grid.Children.Add(workspaceLabels);

            var backdropPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 3, 0, 3),
            };
            _workspaceBackdropSwatch = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            BindBrush(_workspaceBackdropSwatch, Border.BorderBrushProperty, "ThemeBorderBrush",
                Color.FromArgb(255, 0xD1, 0xD5, 0xDB));
            backdropPanel.Children.Add(_workspaceBackdropSwatch);
            _workspaceBackdropComboBox = new ComboBox
            {
                Width = 150,
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Value",
            };
            AutomationProperties.SetAutomationId(_workspaceBackdropComboBox, "WorkspaceBackdropComboBox");
            _workspaceBackdropComboBox.SelectionChanged += WorkspaceBackdropComboBox_SelectionChanged;
            backdropPanel.Children.Add(_workspaceBackdropComboBox);
            Grid.SetRow(backdropPanel, row);
            Grid.SetColumn(backdropPanel, 1);
            grid.Children.Add(backdropPanel);
            row++;

            _penOnlyLabelText = RowLabel(grid, row);
            _penOnlySwitch = new ToggleSwitch
            {
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            AutomationProperties.SetAutomationId(_penOnlySwitch, "PenOnlyCheckBox");
            _penOnlySwitch.Toggled += SettingsControl_Toggled;
            Grid.SetRow(_penOnlySwitch, row);
            Grid.SetColumn(_penOnlySwitch, 1);
            grid.Children.Add(_penOnlySwitch);

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
            };
        }

        private TextBlock SectionLabel()
        {
            var label = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            BindBrush(label, TextBlock.ForegroundProperty, "ThemeTextBrush",
                Color.FromArgb(255, 0x1F, 0x29, 0x37));
            return label;
        }

        private TextBlock HintText()
        {
            var hint = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            };
            BindBrush(hint, TextBlock.ForegroundProperty, "ThemeSubtleTextBrush",
                Color.FromArgb(255, 0x4B, 0x55, 0x63));
            return hint;
        }

        private TextBlock RowLabel(Grid grid, int row)
        {
            var label = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 12, 6),
                TextWrapping = TextWrapping.Wrap,
            };
            BindBrush(label, TextBlock.ForegroundProperty, "ThemeTextBrush",
                Color.FromArgb(255, 0x1F, 0x29, 0x37));
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            return label;
        }

        /// <summary>Sets initial control state from the staged settings.</summary>
        private void RestoreControlState(AppSettings settings)
        {
            _isApplyingLocalization = true;
            try
            {
                _languageComboBox.SelectedValue = settings.Language;
                _autoSaveIntervalComboBox.SelectedItem = settings.AutoSaveIntervalSeconds;
                _pressureSwitch.IsOn = settings.EnablePressure;
                _penOnlySwitch.IsOn = settings.PenOnlyMode;
                _smoothingComboBox.SelectedIndex = Math.Max(0, Math.Min(3, settings.StrokeSmoothing));
                _performanceModeComboBox.SelectedIndex = GetPerformanceModeIndex(settings.PerformanceMode);
                _themeComboBox.SelectedIndex = GetThemeIndex(settings.Theme);
                _workspaceBackdropComboBox.SelectedIndex = GetWorkspaceBackdropIndex(settings.WorkspaceBackdrop);
                UpdateWorkspaceBackdropSwatch();
            }
            finally
            {
                _isApplyingLocalization = false;
            }
        }

        // ── Localization (WPF ApplyLocalization parity) ───────────────────

        public void ApplyLocalization()
        {
            var smoothingIndex = _smoothingComboBox.SelectedIndex < 0 ? 2 : _smoothingComboBox.SelectedIndex;
            var performanceModeIndex = _performanceModeComboBox.SelectedIndex < 0 ? 1 : _performanceModeComboBox.SelectedIndex;
            var themeIndex = _themeComboBox.SelectedIndex < 0 ? 0 : _themeComboBox.SelectedIndex;
            var workspaceBackdropIndex = _workspaceBackdropComboBox.SelectedIndex < 0 ? 0 : _workspaceBackdropComboBox.SelectedIndex;

            _isApplyingLocalization = true;
            try
            {
                Title = LocalizationService.Get("Settings.Title");
                _subtitleText.Text = LocalizationService.Get("Settings.Subtitle");
                _languageLabelText.Text = LocalizationService.Get("Settings.LanguageLabel");
                _languageHintText.Text = LocalizationService.Get("Settings.LanguageHint");
                _utilityLabelText.Text = LocalizationService.Get("Settings.UtilityLabel");
                _utilityHintText.Text = LocalizationService.Get("Settings.UtilityHint");
                _autoSaveIntervalLabelText.Text = LocalizationService.Get("Settings.AutoSaveInterval");
                _pressureLabelText.Text = LocalizationService.Get("Settings.Pressure");
                string enabled = LocalizationService.Get("Settings.Enabled");
                _pressureSwitch.OnContent = enabled;
                _pressureSwitch.OffContent = enabled;
                _penOnlyLabelText.Text = LocalizationService.Get("Settings.PenOnly");
                _penOnlySwitch.OnContent = enabled;
                _penOnlySwitch.OffContent = enabled;
                _smoothingLabelText.Text = LocalizationService.Get("Settings.Smoothing");
                _performanceModeLabelText.Text = LocalizationService.Get("Settings.Performance");
                _themeLabelText.Text = LocalizationService.Get("Settings.Theme");
                PrimaryButtonText = LocalizationService.Get("Common.Save");
                CloseButtonText = LocalizationService.Get("Common.Cancel");

                _smoothingComboBox.ItemsSource = new[]
                {
                    LocalizationService.Get("Editor.SmoothingOff"),
                    LocalizationService.Get("Editor.SmoothingLow"),
                    LocalizationService.Get("Editor.SmoothingMid"),
                    LocalizationService.Get("Editor.SmoothingHigh")
                };
                _smoothingComboBox.SelectedIndex = Math.Max(0, Math.Min(3, smoothingIndex));

                _performanceModeComboBox.ItemsSource = new[]
                {
                    LocalizationService.Get("Settings.PerformanceBatterySaver"),
                    LocalizationService.Get("Settings.PerformanceBalanced"),
                    LocalizationService.Get("Settings.PerformanceBestQuality")
                };
                _performanceModeComboBox.SelectedIndex = Math.Max(0, Math.Min(2, performanceModeIndex));

                _themeComboBox.ItemsSource = new[]
                {
                    LocalizationService.Get("Settings.ThemeLight"),
                    LocalizationService.Get("Settings.ThemeDark"),
                    LocalizationService.Get("Settings.ThemeSystem"),
                    LocalizationService.Get("Settings.ThemeHighContrast")
                };
                _themeComboBox.SelectedIndex = Math.Max(0, Math.Min(3, themeIndex));

                _workspaceBackdropLabelText.Text = LocalizationService.Get("Settings.WorkspaceBackdrop");
                _workspaceBackdropHintText.Text = LocalizationService.Get("Settings.WorkspaceBackdropHint");
                AutomationProperties.SetName(_workspaceBackdropComboBox, _workspaceBackdropLabelText.Text);
                AutomationProperties.SetHelpText(_workspaceBackdropComboBox, _workspaceBackdropHintText.Text);
                _workspaceBackdropComboBox.ItemsSource = CreateWorkspaceBackdropOptions();
                _workspaceBackdropComboBox.SelectedIndex = Math.Max(0,
                    Math.Min(WorkspaceBackdropValues.Length - 1, workspaceBackdropIndex));
                UpdateWorkspaceBackdropSwatch();
            }
            finally
            {
                _isApplyingLocalization = false;
            }
        }

        // ── Selection plumbing ────────────────────────────────────────────

        public AppSettings GetSelectedSettings()
        {
            var selectedLanguage = _languageComboBox.SelectedValue is AppLanguage language
                ? language
                : AppLanguage.English;

            int autoSaveInterval = _autoSaveIntervalComboBox.SelectedItem is int interval ? interval : 60;
            int smoothing = Math.Max(0, Math.Min(3,
                _smoothingComboBox.SelectedIndex < 0 ? 2 : _smoothingComboBox.SelectedIndex));

            var selected = CloneSettings(_originalSettings);
            selected.Language = selectedLanguage;
            selected.AutoSaveIntervalSeconds = autoSaveInterval;
            selected.EnablePressure = _pressureSwitch.IsOn;
            selected.PenOnlyMode = _penOnlySwitch.IsOn;
            selected.StrokeSmoothing = smoothing;
            selected.PerformanceMode = GetPerformanceModeValue(_performanceModeComboBox.SelectedIndex);
            selected.Theme = GetThemeValue(_themeComboBox.SelectedIndex);
            selected.WorkspaceBackdrop = GetWorkspaceBackdropValue(_workspaceBackdropComboBox.SelectedIndex);
            return selected;
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_opened || _isApplyingLocalization)
                return;

            var previewSettings = GetSelectedSettings();
            LocalizationService.ApplyLanguage(previewSettings.Language);
            MainWindow.Current?.PreviewSettings(previewSettings);
        }

        private void SettingsControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingLocalization)
                return;
            PreviewCurrentSettings();
        }

        private void SettingsControl_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isApplyingLocalization)
                return;
            PreviewCurrentSettings();
        }

        private void WorkspaceBackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateWorkspaceBackdropSwatch();
            SettingsControl_SelectionChanged(sender, e);
        }

        private void UpdateWorkspaceBackdropSwatch()
        {
            if (_workspaceBackdropSwatch == null)
                return;
            if (_workspaceBackdropComboBox?.SelectedItem is WorkspaceBackdropOption option)
                _workspaceBackdropSwatch.Background = option.PreviewBrush;
        }

        private void PreviewCurrentSettings()
        {
            if (!_opened || _isApplyingLocalization)
                return;
            MainWindow.Current?.PreviewSettings(GetSelectedSettings());
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_opened)
                ApplyLocalization();
        }

        private void OnThemeApplied(object sender, EventArgs e)
        {
            foreach (var binding in _themeBindings)
                binding.Element.SetValue(binding.Property, Res(binding.Key, binding.Fallback));
            // The card swatch border is theme-bound but the swatch fills are
            // literal palette colors — re-stamp both.
            UpdateWorkspaceBackdropSwatch();
        }

        // ── Value/index mapping (direct WPF ports) ────────────────────────

        private static int GetWorkspaceBackdropIndex(string value)
        {
            string normalized = WinUiThemeService.NormalizeWorkspaceBackdrop(value);
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
            string[] labels =
            {
                LocalizationService.Get("Settings.WorkspaceBackdropNeutral"),
                LocalizationService.Get("Settings.WorkspaceBackdropPaper"),
                LocalizationService.Get("Settings.WorkspaceBackdropMist"),
                LocalizationService.Get("Settings.WorkspaceBackdropWarm"),
                LocalizationService.Get("Settings.WorkspaceBackdropSlate"),
                LocalizationService.Get("Settings.WorkspaceBackdropMidnight")
            };
            string[] previewColors = { "#FFFFFF", "#F5F3EE", "#EAF2F6", "#F1E7DA", "#D7DEE7", "#101722" };

            return WorkspaceBackdropValues.Select((value, index) => new WorkspaceBackdropOption(
                value,
                labels[index],
                new SolidColorBrush(ParseHexColor(previewColors[index])))).ToArray();
        }

        /// <summary>Parses "#RRGGBB" into an opaque <see cref="Color"/> (WPF ColorConverter parity).</summary>
        private static Color ParseHexColor(string hex)
        {
            uint value = Convert.ToUInt32((hex ?? "FFFFFF").TrimStart('#'), 16);
            return Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
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

        /// <summary>
        /// Deep-clones an <see cref="AppSettings"/> snapshot (WPF CloneSettings
        /// parity — list-valued members are copied so staged edits never leak
        /// into the persisted model).
        /// </summary>
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
                RecentPenColors = source.RecentPenColors == null ? new List<string>() : new List<string>(source.RecentPenColors),
                RecentHighlighterColors = source.RecentHighlighterColors == null ? new List<string>() : new List<string>(source.RecentHighlighterColors),
                RecentTextColors = source.RecentTextColors == null ? new List<string>() : new List<string>(source.RecentTextColors),
                PenPresets = source.PenPresets == null ? new List<PenPreset>() : source.PenPresets.Where(p => p != null).Select(p => new PenPreset { Tool = p.Tool, ColorHex = p.ColorHex, Size = p.Size }).ToList(),
                StrokeSmoothing = source.StrokeSmoothing,
                AutoSaveIntervalSeconds = source.AutoSaveIntervalSeconds,
                DefaultPenColorHex = source.DefaultPenColorHex,
                DefaultPenSize = source.DefaultPenSize,
                Theme = source.Theme,
                WorkspaceBackdrop = source.WorkspaceBackdrop,
                PerformanceMode = source.PerformanceMode
            };
        }

        // ── Theme-brush plumbing for code-built content ───────────────────

        private void BindBrush(DependencyObject element, DependencyProperty property, string key, Color fallback)
        {
            element.SetValue(property, Res(key, fallback));
            _themeBindings.Add(new ThemeBinding(element, property, key, fallback));
        }

        private static Brush Res(string key, Color fallback)
        {
            if (Application.Current?.Resources is { } resources
                && resources.TryGetValue(key, out var value)
                && value is Brush brush)
                return brush;
            return new SolidColorBrush(fallback);
        }
    }
}
