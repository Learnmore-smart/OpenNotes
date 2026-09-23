using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Caelum.Services
{
    /// <summary>
    /// Minimal WinUI port of the WPF <c>Services/DialogService.cs</c>, scoped
    /// to exactly what the HomePage/MainWindow slice needs: info/error dialogs
    /// and a danger-styled confirm, built on <see cref="ContentDialog"/> with
    /// an explicit <see cref="XamlRoot"/> (unpackaged WinUI has no implicit
    /// dialog host).
    ///
    /// Semantics mirror the WPF <c>ShowDialogAsync</c> result: confirm →
    /// <c>true</c>, cancel/close → <c>false</c>. The WPF danger-confirm also
    /// returned <c>false</c> for a dismissed dialog, so callers that check
    /// <c>!= true</c> behave identically.
    ///
    /// ContentDialog only allows one open instance per XamlRoot — a process-
    /// wide semaphore serializes calls so a second dialog awaited during the
    /// first never throws <c>InvalidOperationException</c>.
    /// </summary>
    public static class WinUiDialogService
    {
        private static readonly SemaphoreSlim DialogGate = new SemaphoreSlim(1, 1);

        public static async Task ShowInfoAsync(XamlRoot xamlRoot, string title, string content)
        {
            await ShowDialogAsync(xamlRoot, title, content, null, LocalizationService.Get("Common.OK"));
        }

        public static async Task ShowErrorAsync(XamlRoot xamlRoot, string title, string content)
        {
            await ShowDialogAsync(xamlRoot, title, content, null, LocalizationService.Get("Common.OK"));
        }

        public static async Task<bool?> ShowDialogAsync(
            XamlRoot xamlRoot,
            string title,
            string content,
            string cancelButtonText = null,
            string okButtonText = null)
        {
            return await ShowDialogAsync(xamlRoot, title, content, cancelButtonText, okButtonText, dangerConfirm: false);
        }

        public static async Task<bool?> ShowDangerConfirmAsync(
            XamlRoot xamlRoot,
            string title,
            string content,
            string cancelButtonText,
            string confirmButtonText)
        {
            return await ShowDialogAsync(xamlRoot, title, content, cancelButtonText, confirmButtonText, dangerConfirm: true);
        }

        private static async Task<bool?> ShowDialogAsync(
            XamlRoot xamlRoot,
            string title,
            string content,
            string cancelButtonText,
            string okButtonText,
            bool dangerConfirm)
        {
            if (xamlRoot == null)
                return null;

            var contentText = new TextBlock
            {
                Text = content ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14
            };
            if (Application.Current?.Resources?.TryGetValue("ThemeSubtleForegroundBrush", out var fg) == true &&
                fg is Brush subtleBrush)
            {
                contentText.Foreground = subtleBrush;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title ?? string.Empty,
                Content = new ScrollViewer
                {
                    Content = contentText,
                    MaxHeight = 360,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                CloseButtonText = cancelButtonText,
                PrimaryButtonText = okButtonText,
                DefaultButton = string.IsNullOrEmpty(okButtonText)
                    ? ContentDialogButton.Close
                    : ContentDialogButton.Primary
            };

            if (dangerConfirm)
                dialog.PrimaryButtonStyle = BuildDangerButtonStyle();

            await DialogGate.WaitAsync();
            try
            {
                var result = await dialog.ShowAsync();
                // WPF parity: confirm=true, cancel/dismiss=false.
                return result == ContentDialogResult.Primary;
            }
            catch (InvalidOperationException)
            {
                // Defense in depth: a ContentDialog that bypassed the gate was
                // already open on this XamlRoot. Report "not confirmed" rather
                // than crashing an async-void event handler.
                return null;
            }
            finally
            {
                DialogGate.Release();
            }
        }

        /// <summary>
        /// Runs a caller-built <see cref="ContentDialog"/> (e.g. HomePage's
        /// input/template pickers) under the same process-wide
        /// <see cref="DialogGate"/> the service dialogs use, so no two
        /// ContentDialogs are ever open on a XamlRoot at once. A stray
        /// already-open dialog that still trips
        /// <c>InvalidOperationException</c> returns <c>default(T)</c> —
        /// for <see cref="ContentDialogResult"/> that is
        /// <see cref="ContentDialogResult.None"/>, i.e. "dismissed".
        /// </summary>
        internal static async Task<T> RunUnderDialogGateAsync<T>(Func<Task<T>> showAsync)
        {
            await DialogGate.WaitAsync();
            try
            {
                return await showAsync();
            }
            catch (InvalidOperationException)
            {
                return default;
            }
            finally
            {
                DialogGate.Release();
            }
        }

        /// <summary>
        /// Danger confirm chrome: red filled button, matching the WPF
        /// <c>DialogDangerButton</c> role. Built once in code so it can pull
        /// the live <c>ThemeDangerBrush</c>.
        /// </summary>
        private static Style BuildDangerButtonStyle()
        {
            var style = new Style(typeof(Button));
            Brush danger = null;
            if (Application.Current?.Resources?.TryGetValue("ThemeDangerBrush", out var value) == true)
                danger = value as Brush;
            style.Setters.Add(new Setter(Control.BackgroundProperty,
                danger ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xB4, 0x23, 0x18))));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                new SolidColorBrush(Microsoft.UI.Colors.White)));
            return style;
        }
    }
}
