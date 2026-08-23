using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Caelum.Models;

namespace Caelum.Services
{
    public static class DialogService
    {
        public static async System.Threading.Tasks.Task ShowInfoAsync(Window owner, string title, string content)
        {
            await ShowDialogAsync(owner, title, content, null, LocalizationService.Get("Common.OK"));
        }

        public static async System.Threading.Tasks.Task ShowErrorAsync(Window owner, string title, string content)
        {
            await ShowDialogAsync(owner, title, content, null, LocalizationService.Get("Common.OK"));
        }

        public static async System.Threading.Tasks.Task<bool?> ShowDialogAsync(
            Window owner,
            string title,
            string content,
            string cancelButtonText = null,
            string okButtonText = null)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 520,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false
            };

            var mainBorder = new Border
            {
                CornerRadius = new CornerRadius(22),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24)
            };
            mainBorder.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceAltBrush");
            mainBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
            mainBorder.SetResourceReference(UIElement.OpacityProperty, "ThemeSurfaceOpacity");

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Padding = new Thickness(0)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            scrollViewer.Content = grid;
            mainBorder.Child = scrollViewer;
            dialog.MouseLeftButtonDown += (s, ev) => { dialog.DragMove(); };

            // Header with title and close button
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStackPanel = new StackPanel();
            var titleLabel = new TextBlock
            {
                Text = title,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold
            };
            titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "ThemeForegroundBrush");
            titleStackPanel.Children.Add(titleLabel);
            Grid.SetColumn(titleStackPanel, 0);
            headerGrid.Children.Add(titleStackPanel);

            // Close button
            var closeIcon = new TextBlock
            {
                Text = "\xE8BB",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11
            };
            closeIcon.SetResourceReference(TextBlock.ForegroundProperty, "ThemeSubtleForegroundBrush");

            var closeButton = new Button
            {
                Width = 34,
                Height = 34,
                Margin = new Thickness(12, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Content = closeIcon
            };
            closeButton.Template = CreateCloseButtonTemplate();
            closeButton.Click += (s, ev) =>
            {
                dialog.DialogResult = false;
            };
            Grid.SetColumn(closeButton, 1);
            headerGrid.Children.Add(closeButton);

            Grid.SetRow(headerGrid, 0);
            grid.Children.Add(headerGrid);

            var contentText = new TextBlock
            {
                Text = content,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 22, 0, 0)
            };
            contentText.SetResourceReference(TextBlock.ForegroundProperty, "ThemeSubtleForegroundBrush");
            Grid.SetRow(contentText, 1);
            grid.Children.Add(contentText);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 16, 0, 0)
            };
            Grid.SetRow(btnPanel, 2);

            bool? result = null;
            Button cancelBtn = null;
            Button okBtn = null;

            if (!string.IsNullOrEmpty(cancelButtonText))
            {
                cancelBtn = new Button
                {
                    Content = cancelButtonText,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsCancel = true
                };
                var secStyle = Application.Current.TryFindResource("DialogSecondaryButton") as Style;
                if (secStyle != null)
                {
                    cancelBtn.Style = secStyle;
                }
                cancelBtn.Click += (s, ev) =>
                {
                    result = false;
                    dialog.DialogResult = false;
                };
                btnPanel.Children.Add(cancelBtn);
            }

            if (!string.IsNullOrEmpty(okButtonText))
            {
                okBtn = new Button
                {
                    Content = okButtonText,
                    IsDefault = true
                };
                var priStyle = Application.Current.TryFindResource("DialogPrimaryButton") as Style;
                if (priStyle != null)
                {
                    okBtn.Style = priStyle;
                }
                okBtn.Click += (s, ev) =>
                {
                    result = true;
                    dialog.DialogResult = true;
                };
                btnPanel.Children.Add(okBtn);
            }

            grid.Children.Add(btnPanel);

            dialog.Content = mainBorder;
            EventHandler languageChanged = (_, __) =>
            {
                dialog.Title = RefreshKnownCatalogText(title);
                titleLabel.Text = RefreshKnownCatalogText(title);
                contentText.Text = RefreshKnownCatalogText(content);
                if (cancelBtn != null)
                    cancelBtn.Content = RefreshKnownCatalogText(cancelButtonText);
                if (okBtn != null)
                    okBtn.Content = RefreshKnownCatalogText(okButtonText);
            };
            LocalizationService.LanguageChanged += languageChanged;
            try
            {
                dialog.ShowDialog();
            }
            finally
            {
                LocalizationService.LanguageChanged -= languageChanged;
            }

            await System.Threading.Tasks.Task.CompletedTask;
            return result;
        }

        internal static ControlTemplate CreateCloseButtonTemplate()
        {
            var closeButtonTemplate = new ControlTemplate(typeof(Button));
            var templateFactory = new FrameworkElementFactory(typeof(Border));
            templateFactory.Name = "Root";
            templateFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            templateFactory.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeFocusBrush"));
            templateFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            templateFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            templateFactory.AppendChild(contentPresenter);
            closeButtonTemplate.VisualTree = templateFactory;

            var closeFocusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            closeFocusTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                new DynamicResourceExtension("ThemeControlHoverBrush"),
                "Root"));
            closeFocusTrigger.Setters.Add(new Setter(
                Border.BorderBrushProperty,
                new DynamicResourceExtension("ThemeFocusBrush"),
                "Root"));
            closeFocusTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Root"));
            closeButtonTemplate.Triggers.Add(closeFocusTrigger);
            return closeButtonTemplate;
        }

        private static string RefreshKnownCatalogText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var catalog = LocalizationService.GetCatalog();
            foreach (var entry in catalog)
            {
                if (string.Equals(value, entry.Value.English, StringComparison.Ordinal) ||
                    string.Equals(value, entry.Value.Chinese, StringComparison.Ordinal) ||
                    string.Equals(value, entry.Value.French, StringComparison.Ordinal))
                {
                    return LocalizationService.CurrentLanguage switch
                    {
                        AppLanguage.Chinese => entry.Value.Chinese,
                        AppLanguage.French => entry.Value.French,
                        _ => entry.Value.English
                    };
                }
            }

            return value;
        }
    }
}
