using System.Linq;
using Caelum.Models;
using Caelum.Pages;
using Caelum.Services;

namespace Caelum
{
    public partial class MainWindow
    {
        private string GetHomeTabTitle()
        {
            return LocalizationService.Get("Main.HomeTabTitle");
        }

        public void PreviewSettings(AppSettings settings)
        {
            LocalizationService.ApplyLanguage(settings.Language);
            ThemeService.Apply(settings.Theme);

            foreach (var tab in _tabs.Where(tab => tab?.Frame?.Content is EditorPage))
            {
                if (tab.Frame.Content is EditorPage editor)
                    editor.ApplySettings(settings);
            }
        }

        private void ApplyLocalization()
        {
            Title = ProductInfo.DisplayName;
            if (ProductNameTextBlock != null)
                ProductNameTextBlock.Text = ProductInfo.DisplayName;
            SearchPlaceholder.Text = LocalizationService.Get("Main.SearchPlaceholder");
            SelectButtonLabel.Text = LocalizationService.Get("Main.Select");
            SortByNameMenuItem.Header = LocalizationService.Get("Main.SortByName");
            SortByDateMenuItem.Header = LocalizationService.Get("Main.SortByDate");
            SettingsMenuItem.Header = LocalizationService.Get("Main.Settings");
            AboutMenuItem.Header = LocalizationService.Get("Main.About");
            NewTabButton.ToolTip = LocalizationService.Get("Main.NewTabTooltip");
            RefreshOpenContentLocalization();
            RebuildTabBar();
            RefreshSelectButtonVisualState();
        }

        public void RefreshSelectButtonVisualState()
        {
            if (SelectButton == null)
                return;

            var isActive =
                (ActiveFrame?.Content is HomePage home && home.IsSelectionMode) ||
                (ActiveFrame?.Content is EditorPage editor && editor.IsSelectionMode);

            SelectButton.Tag = isActive;
        }

        private void ApplySettings(AppSettings settings)
        {
            var savedSettings = AppSettingsService.Save(settings);
            PreviewSettings(savedSettings);
        }

        private void RefreshOpenContentLocalization()
        {
            foreach (var tab in _tabs.Where(tab => tab != null))
            {
                if (tab.Frame?.Content is HomePage home)
                {
                    tab.Title = GetHomeTabTitle();
                    if (!home.IsLoaded)
                        home.ApplyLocalization();
                }
                else if (tab.Frame?.Content is EditorPage editor)
                {
                    if (!editor.IsLoaded)
                        editor.ApplyLocalization();
                }
            }
        }

        private void OpenSettingsDialog()
        {
            var originalSettings = AppSettingsService.Load();
            var dialog = new SettingsWindow(originalSettings)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                ApplySettings(dialog.SelectedSettings ?? dialog.GetSelectedSettings());
                ShowToast(LocalizationService.Get("Main.SettingsSaved"), "\uE713");
                return;
            }

            PreviewSettings(originalSettings);
        }
    }
}
