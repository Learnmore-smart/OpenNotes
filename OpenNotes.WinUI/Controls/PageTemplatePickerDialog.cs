using System;
using System.Collections.Generic;
using Caelum.Models;
using Caelum.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace Caelum.Controls
{
    /// <summary>
    /// WinUI port of the WPF <c>PageTemplatePickerWindow</c> as a
    /// <see cref="ContentDialog"/>: a 3×3 card grid where each card carries a
    /// mini preview sketch, a localized title and a localized hint.
    ///
    /// Two modes (WPF parity):
    /// <list type="bullet">
    /// <item>Insert-page mode (parameterless ctor): clicking a card selects
    /// that template and closes the dialog immediately (<see cref="IsConfirmed"/>
    /// = true, <see cref="SelectedTemplate"/> set). Cancel/Esc/dismiss close
    /// with <see cref="IsConfirmed"/> = false.</item>
    /// <item>Notebook-creation mode (<see cref="PageTemplatePickerDialog(string)"/>):
    /// clicking a card only selects it; the primary ("Create") button — kept
    /// disabled until a non-empty folder is chosen — confirms. The folder row
    /// (label + path + browse) is only visible in this mode.</item>
    /// </list>
    ///
    /// Callers must set <see cref="ContentDialog.XamlRoot"/> and show the
    /// dialog through <see cref="WinUiDialogService.RunUnderDialogGateAsync{T}"/>
    /// so it serializes with every other ContentDialog on the same root.
    ///
    /// Code-built content cannot use <c>{ThemeResource}</c>, so the theme-
    /// bound brushes are baked at build time and the whole content grid is
    /// rebuilt on <see cref="WinUiThemeService.ThemeApplied"/> (selection and
    /// folder state are field-backed and survive the rebuild). Text is
    /// re-localized on <see cref="LocalizationService.LanguageChanged"/>.
    /// Both subscriptions attach in <see cref="ContentDialog.Opened"/> (a
    /// <see cref="ContentDialog.ShowAsync"/> that throws before opening never
    /// raises Closed and would leak them) and are released in
    /// <see cref="ContentDialog.Closed"/>.
    /// </summary>
    public sealed class PageTemplatePickerDialog : ContentDialog
    {
        /// <summary>
        /// The nine page templates in display order with their localization
        /// keys (WPF PageTemplatePickerWindow card order parity — also the
        /// template table the HomePage notebook picker used before this
        /// dialog replaced it).
        /// </summary>
        internal static readonly (PageInsertTemplate Template, string TitleKey, string HintKey)[] TemplateOptions =
        {
            (PageInsertTemplate.Blank, "Editor.PageTemplateBlank", "Editor.PageTemplateBlankHint"),
            (PageInsertTemplate.Notebook, "Editor.PageTemplateNotebook", "Editor.PageTemplateNotebookHint"),
            (PageInsertTemplate.Lined, "Editor.PageTemplateLined", "Editor.PageTemplateLinedHint"),
            (PageInsertTemplate.Quadrille, "Editor.PageTemplateQuadrille", "Editor.PageTemplateQuadrilleHint"),
            (PageInsertTemplate.Dotted, "PageTemplate.DottedTitle", "PageTemplate.DottedHint"),
            (PageInsertTemplate.Music, "PageTemplate.MusicTitle", "PageTemplate.MusicHint"),
            (PageInsertTemplate.Cornell, "PageTemplate.CornellTitle", "PageTemplate.CornellHint"),
            (PageInsertTemplate.Checklist, "PageTemplate.ChecklistTitle", "PageTemplate.ChecklistHint"),
            (PageInsertTemplate.TwoColumn, "PageTemplate.TwoColumnTitle", "PageTemplate.TwoColumnHint"),
        };

        private readonly bool _notebookCreationMode;
        private readonly Dictionary<PageInsertTemplate, Button> _cards = new();
        private readonly Dictionary<PageInsertTemplate, (TextBlock Title, TextBlock Hint)> _cardTexts = new();
        private bool _opened;

        private TextBlock _subtitleText;
        private TextBlock _pathLabelText;
        private TextBlock _selectedFolderPathText;
        private Grid _pathRow;
        private PageInsertTemplate _selectedTemplate;

        /// <summary>Insert-page mode picker.</summary>
        public PageTemplatePickerDialog()
            : this(notebookCreationMode: false)
        {
        }

        /// <summary>Notebook-creation mode picker.</summary>
        public PageTemplatePickerDialog(string initialFolderPath)
            : this(notebookCreationMode: true, initialFolderPath)
        {
        }

        private PageTemplatePickerDialog(bool notebookCreationMode, string initialFolderPath = null)
        {
            _notebookCreationMode = notebookCreationMode;
            _selectedTemplate = PageInsertTemplate.Blank;
            SelectedFolderPath = initialFolderPath ?? string.Empty;

            AutomationProperties.SetAutomationId(this, "PageTemplatePickerDialog");
            MinWidth = 560;
            MaxWidth = 900;
            MaxHeight = 840;
            DefaultButton = _notebookCreationMode
                ? ContentDialogButton.Primary
                : ContentDialogButton.Close;

            Content = BuildContent();
            ApplyLocalization();
            UpdateCardSelection();
            UpdateFolderPathText();

            PrimaryButtonClick += (_, _) =>
            {
                // Notebook mode: Create confirms the current card + folder.
                IsConfirmed = true;
            };

            // Subscribe to the static change events only once the dialog is
            // actually open: a ShowAsync that throws before opening (missing
            // XamlRoot, a second dialog already up) never raises Closed, so
            // ctor-time subscription would leak the handlers and leave a
            // non-shown dialog answering language/theme changes.
            Opened += (_, _) =>
            {
                if (_opened)
                    return; // defensive re-show guard: never double-subscribe
                _opened = true;
                LocalizationService.LanguageChanged += OnLanguageChanged;
                WinUiThemeService.ThemeApplied += OnThemeApplied;
            };
            Closed += (_, _) =>
            {
                _opened = false;
                LocalizationService.LanguageChanged -= OnLanguageChanged;
                WinUiThemeService.ThemeApplied -= OnThemeApplied;
            };
        }

        /// <summary>The last card selection (also the confirmed template).</summary>
        public PageInsertTemplate SelectedTemplate => _selectedTemplate;

        /// <summary>The chosen notebook folder (notebook-creation mode only).</summary>
        public string SelectedFolderPath { get; private set; }

        /// <summary>
        /// WPF <c>DialogResult == true</c> parity: true when the user picked a
        /// template (insert-mode card click / notebook-mode Create), false for
        /// Cancel/close/dismiss.
        /// </summary>
        public bool IsConfirmed { get; private set; }

        private void SelectTemplate(PageInsertTemplate template)
        {
            _selectedTemplate = template;
            UpdateCardSelection();
            if (!_notebookCreationMode)
            {
                // Insert mode: the WPF picker treats a card click as the
                // dialog result (DialogResult = true) and closes.
                IsConfirmed = true;
                Hide();
            }
        }

        private void UpdateCardSelection()
        {
            foreach (var pair in _cards)
            {
                bool selected = pair.Key == _selectedTemplate;
                pair.Value.Background = selected
                    ? Res("ThemeSelectionBrush", Color.FromArgb(255, 0xDB, 0xEA, 0xFE))
                    : Res("ThemeSurfaceBrush", Colors.White);
                pair.Value.BorderBrush = selected
                    ? Res("ThemeAccentBrush", Color.FromArgb(255, 0x25, 0x63, 0xEB))
                    : Res("ThemeBorderBrush", Color.FromArgb(255, 0xD1, 0xD5, 0xDB));
                pair.Value.BorderThickness = new Thickness(selected ? 1.5 : 1);
            }
        }

        private Grid BuildContent()
        {
            _cards.Clear();
            _cardTexts.Clear();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _subtitleText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Res("ThemeSubtleForegroundBrush", Color.FromArgb(255, 0x4B, 0x55, 0x63)),
            };
            root.Children.Add(_subtitleText);

            var cardGrid = new Grid { MinWidth = 620 };
            for (int c = 0; c < 3; c++)
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < 3; r++)
                cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < TemplateOptions.Length; i++)
            {
                var card = BuildCard(TemplateOptions[i].Template);
                Grid.SetRow(card, i / 3);
                Grid.SetColumn(card, i % 3);
                cardGrid.Children.Add(card);
            }

            var scroll = new ScrollViewer
            {
                Margin = new Thickness(0, 20, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = cardGrid,
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            // Notebook-creation folder row (WPF PathSectionBorder).
            _pathRow = new Grid
            {
                Margin = new Thickness(0, 20, 0, 0),
                Visibility = _notebookCreationMode ? Visibility.Visible : Visibility.Collapsed,
            };
            _pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _pathLabelText = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("ThemeSubtleForegroundBrush", Color.FromArgb(255, 0x4B, 0x55, 0x63)),
            };
            _pathRow.Children.Add(_pathLabelText);

            _selectedFolderPathText = new TextBlock
            {
                Margin = new Thickness(12, 0, 12, 0),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Res("ThemeForegroundBrush", Color.FromArgb(255, 0x1F, 0x29, 0x37)),
            };
            Grid.SetColumn(_selectedFolderPathText, 1);
            _pathRow.Children.Add(_selectedFolderPathText);

            var browseButton = new Button
            {
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Content = new LucideIcon
                {
                    Kind = "FolderOpen",
                    Width = 16,
                    Height = 16,
                    Stroke = Res("ThemeSubtleForegroundBrush", Color.FromArgb(255, 0x4B, 0x55, 0x63)),
                },
            };
            AutomationProperties.SetAutomationId(browseButton, "BrowsePathButton");
            browseButton.Click += BrowsePathButton_Click;
            Grid.SetColumn(browseButton, 2);
            _pathRow.Children.Add(browseButton);

            Grid.SetRow(_pathRow, 2);
            root.Children.Add(_pathRow);
            return root;
        }

        private Button BuildCard(PageInsertTemplate template)
        {
            var titleText = new TextBlock
            {
                Margin = new Thickness(0, 16, 0, 0),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Res("ThemeForegroundBrush", Color.FromArgb(255, 0x1F, 0x29, 0x37)),
            };
            var hintText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Res("ThemeSubtleForegroundBrush", Color.FromArgb(255, 0x4B, 0x55, 0x63)),
            };
            _cardTexts[template] = (titleText, hintText);

            var body = new StackPanel { Margin = new Thickness(18) };
            body.Children.Add(BuildPreview(template));
            body.Children.Add(titleText);
            body.Children.Add(hintText);

            var card = new Button
            {
                Margin = new Thickness(8),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = body,
            };
            // Card ids mirror the WPF x:Names (BlankCard, NotebookCard, …).
            AutomationProperties.SetAutomationId(card, $"{template}Card");
            var option = template;
            card.Click += (_, _) => SelectTemplate(option);
            _cards[template] = card;
            return card;
        }

        private async void BrowsePathButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = MainWindow.Current;
                if (window == null)
                    return;
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    CommitButtonText = LocalizationService.Get("Home.CreateNotebookBrowseFolder"),
                };
                picker.FileTypeFilter.Add("*");
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
                var folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    SelectedFolderPath = folder.Path;
                    UpdateFolderPathText();
                }
            }
            catch (Exception ex)
            {
                // async-void click handler: no App.UnhandledException backstop.
                System.Diagnostics.Debug.WriteLine($"[PageTemplatePicker] Folder pick faulted: {ex}");
            }
        }

        private void UpdateFolderPathText()
        {
            if (_selectedFolderPathText != null)
                _selectedFolderPathText.Text = SelectedFolderPath ?? string.Empty;
            // WPF UpdateFolderPathText: Create stays disabled until a folder
            // exists; insert mode has no primary button to gate.
            if (_notebookCreationMode)
                IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(SelectedFolderPath);
        }

        /// <summary>
        /// Re-localizes every visible string (title/subtitle, card titles +
        /// hints, folder row, dialog buttons) — WPF ApplyLocalization parity.
        /// </summary>
        public void ApplyLocalization()
        {
            // WPF keys (PageTemplatePickerWindow.xaml.cs): the insert mode
            // reuses the InsertPageDialog* pair — the earlier
            // Editor.PageTemplateTitle/Subtitle names never existed in the
            // catalog and Get() threw KeyNotFoundException on open.
            Title = LocalizationService.Get(_notebookCreationMode
                ? "Home.CreateNotebookDialogTitle"
                : "Editor.InsertPageDialogTitle");
            if (_subtitleText != null)
            {
                _subtitleText.Text = LocalizationService.Get(_notebookCreationMode
                    ? "Home.CreateNotebookDialogSubtitle"
                    : "Editor.InsertPageDialogSubtitle");
            }

            foreach (var (template, titleKey, hintKey) in TemplateOptions)
            {
                if (_cardTexts.TryGetValue(template, out var texts))
                {
                    texts.Title.Text = LocalizationService.Get(titleKey);
                    texts.Hint.Text = LocalizationService.Get(hintKey);
                }
            }

            if (_pathLabelText != null)
                _pathLabelText.Text = LocalizationService.Get("Home.CreateNotebookPathLabel");
            PrimaryButtonText = _notebookCreationMode
                ? LocalizationService.Get("Home.CreateNotebookAction")
                : string.Empty;
            CloseButtonText = LocalizationService.Get("Common.Cancel");
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            // SettingsDialog parity: only a live dialog re-localizes.
            if (_opened)
                ApplyLocalization();
        }

        private void OnThemeApplied(object sender, EventArgs e)
        {
            if (!_opened)
                return;
            // Theme brushes are baked into the code-built content — rebuild it
            // with the repainted palette (selection + folder are field-backed).
            Content = BuildContent();
            ApplyLocalization();
            UpdateCardSelection();
            UpdateFolderPathText();
        }

        // ── Preview sketches (WPF card preview parity) ────────────────────

        private FrameworkElement BuildPreview(PageInsertTemplate template)
        {
            var paperBrush = Res("ThemePaperBrush", Colors.White);
            var borderBrush = Res("ThemeBorderBrush", Color.FromArgb(255, 0xD1, 0xD5, 0xDB));
            var accentBrush = Res("ThemeAccentBrush", Color.FromArgb(255, 0x25, 0x63, 0xEB));
            var inkBrush = Res("ThemeInkBrush", Color.FromArgb(255, 0x25, 0x63, 0xEB));
            var marginBrush = Res("ThemeMarginBrush", Color.FromArgb(255, 0xC2, 0x41, 0x4B));
            var selectionBrush = Res("ThemeSelectionBrush", Color.FromArgb(255, 0xDB, 0xEA, 0xFE));

            var inner = new Grid { Margin = new Thickness(18) };
            inner.Children.Add(new Rectangle
            {
                RadiusX = 6,
                RadiusY = 6,
                Fill = paperBrush,
                Stroke = borderBrush,
                StrokeThickness = 1,
            });

            switch (template)
            {
                case PageInsertTemplate.Notebook:
                    inner.Children.Add(MakeLine(28, 0, 28, 124, marginBrush, 1.5));
                    inner.Children.Add(MakeLine(0, 22, 120, 22, inkBrush, 1, 0.32));
                    foreach (var y in new[] { 46.0, 70.0, 94.0 })
                        inner.Children.Add(MakeLine(0, y, 120, y, selectionBrush, 1));
                    break;
                case PageInsertTemplate.Lined:
                    foreach (var y in new[] { 20.0, 44.0, 68.0, 92.0 })
                        inner.Children.Add(MakeLine(0, y, 120, y, borderBrush, 1));
                    break;
                case PageInsertTemplate.Quadrille:
                    inner.Children.Add(MakeLine(24, 0, 24, 124, inkBrush, 1));
                    inner.Children.Add(MakeLine(48, 0, 48, 124, selectionBrush, 1));
                    inner.Children.Add(MakeLine(72, 0, 72, 124, selectionBrush, 1));
                    inner.Children.Add(MakeLine(96, 0, 96, 124, accentBrush, 1));
                    inner.Children.Add(MakeLine(0, 24, 120, 24, accentBrush, 1));
                    inner.Children.Add(MakeLine(0, 48, 120, 48, selectionBrush, 1));
                    inner.Children.Add(MakeLine(0, 72, 120, 72, selectionBrush, 1));
                    inner.Children.Add(MakeLine(0, 96, 120, 96, accentBrush, 1));
                    break;
                case PageInsertTemplate.Dotted:
                {
                    var dots = new Grid { Margin = new Thickness(8) };
                    for (int c = 0; c < 7; c++)
                        dots.ColumnDefinitions.Add(new ColumnDefinition());
                    for (int r = 0; r < 5; r++)
                        dots.RowDefinitions.Add(new RowDefinition());
                    for (int r = 0; r < 5; r++)
                    {
                        for (int c = 0; c < 7; c++)
                        {
                            var dot = new Ellipse { Width = 3, Height = 3, Fill = borderBrush };
                            Grid.SetRow(dot, r);
                            Grid.SetColumn(dot, c);
                            dots.Children.Add(dot);
                        }
                    }
                    inner.Children.Add(dots);
                    break;
                }
                case PageInsertTemplate.Music:
                {
                    var staff = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
                    staff.Children.Add(BuildStaffLines(borderBrush, new Thickness(0, 0, 0, 12)));
                    staff.Children.Add(BuildStaffLines(borderBrush, new Thickness(0)));
                    inner.Children.Add(staff);
                    break;
                }
                case PageInsertTemplate.Cornell:
                    inner.Children.Add(MakeLine(0, 24, 120, 24, borderBrush, 1.2));
                    inner.Children.Add(MakeLine(34, 24, 34, 112, borderBrush, 1.2));
                    inner.Children.Add(MakeLine(0, 112, 120, 112, borderBrush, 1.2));
                    break;
                case PageInsertTemplate.Checklist:
                {
                    var rows = new StackPanel { Margin = new Thickness(16, 16, 12, 12) };
                    for (int i = 0; i < 4; i++)
                    {
                        var row = new Grid { Margin = new Thickness(0, 0, 0, i < 3 ? 12 : 0) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                        row.ColumnDefinitions.Add(new ColumnDefinition());
                        row.Children.Add(new Rectangle
                        {
                            Width = 10,
                            Height = 10,
                            RadiusX = 2,
                            RadiusY = 2,
                            Stroke = accentBrush,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        });
                        var rule = MakeLine(0, 5, 90, 5, borderBrush, 1);
                        Grid.SetColumn(rule, 1);
                        row.Children.Add(rule);
                        rows.Children.Add(row);
                    }
                    inner.Children.Add(rows);
                    break;
                }
                case PageInsertTemplate.TwoColumn:
                    inner.Children.Add(MakeLine(60, 14, 60, 110, accentBrush, 1.2));
                    foreach (var y in new[] { 26.0, 50.0, 74.0 })
                    {
                        inner.Children.Add(MakeLine(12, y, 50, y, borderBrush, 1));
                        inner.Children.Add(MakeLine(70, y, 108, y, borderBrush, 1));
                    }
                    break;
                case PageInsertTemplate.Blank:
                default:
                    break;
            }

            // WPF card preview: 160px rounded well on the surface fill.
            return new Border
            {
                Height = 160,
                CornerRadius = new CornerRadius(16),
                Background = template == PageInsertTemplate.Notebook
                    ? Res("ThemeSurfaceAltBrush", Color.FromArgb(255, 0xF5, 0xF7, 0xFA))
                    : Res("ThemeSurfaceBrush", Colors.White),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Child = inner,
            };
        }

        private static Grid BuildStaffLines(Brush brush, Thickness margin)
        {
            var grid = new Grid { Margin = margin };
            for (int i = 0; i < 5; i++)
                grid.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < 5; i++)
            {
                var rule = new Border
                {
                    Height = 1,
                    Background = brush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(rule, i);
                grid.Children.Add(rule);
            }
            return grid;
        }

        private static Line MakeLine(
            double x1, double y1, double x2, double y2,
            Brush stroke, double thickness, double opacity = 1.0)
        {
            return new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = stroke,
                StrokeThickness = thickness,
                Opacity = opacity,
            };
        }

        /// <summary>
        /// Resolves a theme brush from Application.Resources; falls back to a
        /// literal color when the key is absent (mirrors the pages'
        /// ResolveThemeBrush helper).
        /// </summary>
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
