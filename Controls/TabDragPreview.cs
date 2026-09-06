using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Caelum.Models;

namespace Caelum.Controls
{
    /// <summary>
    /// Browser-like tab chip that follows the cursor for the duration of a tab drag.
    /// </summary>
    internal sealed class TabDragPreview : IDisposable
    {
        private readonly Window _window;
        private bool _disposed;

        public TabDragPreview(AppTab tab)
        {
            ArgumentNullException.ThrowIfNull(tab);

            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(tab.Title) ? "OpenNotes" : tab.Title,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 180,
                Margin = new Thickness(8, 0, 8, 0)
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "ThemeForegroundBrush");

            var chrome = new Border
            {
                Child = title,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 0, 4, 0),
                Height = 32,
                MinWidth = 88,
                SnapsToDevicePixels = true
            };
            chrome.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceAltBrush");
            chrome.SetResourceReference(Border.BorderBrushProperty, "ThemeWindowOutlineBrush");
            chrome.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 4,
                Opacity = 0.28,
                Color = Colors.Black
            };

            _window = new Window
            {
                Content = chrome,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                ShowActivated = false,
                IsHitTestVisible = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                Owner = Application.Current?.MainWindow
            };

            _window.SourceInitialized += (_, _) =>
            {
                var helper = new WindowInteropHelper(_window);
                int exStyle = GetWindowLong(helper.Handle, GwlExStyle);
                SetWindowLong(helper.Handle, GwlExStyle, exStyle | WsExTransparent | WsExToolWindow | WsExNoActivate);
            };

            _window.Show();
            MoveToCursor();
        }

        public void MoveToCursor()
        {
            if (_disposed || !_window.IsVisible)
                return;

            if (!GetCursorPos(out POINT point))
                return;

            var dip = ScreenPixelsToDip(point.X + 14, point.Y + 14);
            _window.Left = dip.X;
            _window.Top = dip.Y;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                _window.Close();
            }
            catch (InvalidOperationException)
            {
                // The drag can end after the dispatcher has started shutting the window down.
            }
        }

        private static Point ScreenPixelsToDip(int x, int y)
        {
            var source = PresentationSource.FromVisual(Application.Current?.MainWindow);
            Matrix transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            return transform.Transform(new Point(x, y));
        }

        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
