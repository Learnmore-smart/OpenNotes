using System;
using System.Threading;
using System.Threading.Tasks;
using Caelum.Controls;
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
    ///
    /// <para><b>Popup z-order (WPF PopupZOrderHelper — deliberately not
    /// ported).</b> WPF popups are HWND-backed: <c>ContextMenu</c> and
    /// ComboBox dropdowns kept their own HWND in the topmost band after an
    /// Alt-Tab, so <c>PopupZOrderHelper</c> called SetWindowPos(HWND_NOTOPMOST)
    /// on each popup hwnd (pulling it out of the topmost band while staying
    /// above the owner) and added WS_EX_NOACTIVATE so popups never steal
    /// activation. WinUI
    /// <see cref="MenuFlyout"/>/<see cref="Flyout"/>/<see cref="ContentDialog"/>
    /// are XamlRoot-scoped visuals composited inside the app's own swap
    /// chain — they cannot be clipped by sibling HWNDs and offer no HWND to
    /// reorder, so the equivalent of "fixed topmost" is simply showing the
    /// flyout/dialog on the current XamlRoot. No code maps over.</para>
    /// </summary>
    public static class WinUiDialogService
    {
        private static readonly SemaphoreSlim DialogGate = new SemaphoreSlim(1, 1);

        public static async Task ShowInfoAsync(XamlRoot xamlRoot, string title, string content)
        {
            await ShowDialogAsync(xamlRoot, title, content, null, LocalizationService.Get("Common.OK"),
                dangerConfirm: false, iconKind: "Info", iconBrushKey: "ThemeAccentBrush");
        }

        public static async Task ShowErrorAsync(XamlRoot xamlRoot, string title, string content)
        {
            await ShowDialogAsync(xamlRoot, title, content, null, LocalizationService.Get("Common.OK"),
                dangerConfirm: false, iconKind: "AlertCircle", iconBrushKey: "ThemeDangerBrush");
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
            return await ShowDialogAsync(xamlRoot, title, content, cancelButtonText, confirmButtonText, dangerConfirm: true,
                iconKind: "AlertTriangle", iconBrushKey: "ThemeDangerBrush");
        }

        private static async Task<bool?> ShowDialogAsync(
            XamlRoot xamlRoot,
            string title,
            string content,
            string cancelButtonText,
            string okButtonText,
            bool dangerConfirm,
            string iconKind = null,
            string iconBrushKey = null)
        {
            if (xamlRoot == null)
                return null;

            var contentText = new TextBlock
            {
                Text = content ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 20
            };
            if (Application.Current?.Resources?.TryGetValue("ThemeSubtleForegroundBrush", out var fg) == true &&
                fg is Brush subtleBrush)
            {
                contentText.Foreground = subtleBrush;
            }

            // T13-B severity glyph: a 22-DIP Lucide icon beside the body —
            // accent Info / danger AlertCircle / danger AlertTriangle —
            // so error + danger-confirm dialogs read differently from a
            // plain info box at a glance.
            UIElement body = contentText;
            if (!string.IsNullOrEmpty(iconKind))
            {
                var icon = new LucideIcon
                {
                    Kind = iconKind,
                    Width = 22,
                    Height = 22,
                    Stroke = ResolveDialogBrush(iconBrushKey, "#6B7280"),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1, 0, 0)
                };
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(icon);
                Grid.SetColumn(contentText, 1);
                row.Children.Add(contentText);
                body = row;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title ?? string.Empty,
                Content = new ScrollViewer
                {
                    Content = body,
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
        /// Resolves a theme brush for code-built dialog chrome; falls back
        /// to a literal hex when the key is absent (mirrors the pages'
        /// ResolveThemeBrush helper).
        /// </summary>
        private static Brush ResolveDialogBrush(string key, string fallbackHex)
        {
            if (!string.IsNullOrEmpty(key)
                && Application.Current?.Resources?.TryGetValue(key, out var value) == true
                && value is Brush brush)
                return brush;
            return WinUiThemeService.CreateBrush(fallbackHex);
        }

        /// <summary>
        /// Danger confirm chrome: red filled button, matching the WPF
        /// <c>DialogDangerButton</c> role. The full ControlTemplate +
        /// VisualStateManager live in App.xaml's
        /// <c>DialogDangerButtonStyle</c> — a code-only Background setter
        /// would let the stock Button template's state setters repaint the
        /// fill generic gray on PointerOver/Pressed, erasing the warning
        /// color exactly when the user hovers to click. The bare-setter
        /// fallback below only runs if that resource is ever missing, and
        /// still pulls the live <c>ThemeDangerBrush</c>.
        /// </summary>
        private static Style BuildDangerButtonStyle()
        {
            if (Application.Current?.Resources?.TryGetValue("DialogDangerButtonStyle", out var resource) == true
                && resource is Style style)
                return style;

            var fallback = new Style(typeof(Button));
            Brush danger = null;
            if (Application.Current?.Resources?.TryGetValue("ThemeDangerBrush", out var value) == true)
                danger = value as Brush;
            fallback.Setters.Add(new Setter(Control.BackgroundProperty,
                danger ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xB4, 0x23, 0x18))));
            fallback.Setters.Add(new Setter(Control.ForegroundProperty,
                new SolidColorBrush(Microsoft.UI.Colors.White)));
            return fallback;
        }
    }
}
