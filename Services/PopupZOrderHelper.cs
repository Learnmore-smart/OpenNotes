using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Caelum.Services
{
    /// <summary>
    /// Fixes WPF popup z-order behaviour (Task 10): transparent popups (tool popups,
    /// ContextMenus, ComboBox dropdowns) are hosted in topmost Win32 windows, so after
    /// Alt-Tab they keep floating above other applications. On open we push the popup
    /// HWND out of the topmost band (SetWindowPos HWND_NOTOPMOST — it stays above its
    /// owner, the main window) and add WS_EX_NOACTIVATE so the popup never steals
    /// Windows-level focus from the main window (fixes the "toolbar buttons need two
    /// clicks" bug).
    /// </summary>
    public static class PopupZOrderHelper
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        // HWND_NOTOPMOST: place above all non-topmost windows, behind topmost ones.
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>
        /// Applies the no-topmost + no-activate Win32 fix to a Popup every time it opens.
        /// (Logic moved from EditorPage.FixPopupTopmost — EditorPage delegates here.)
        /// </summary>
        public static void FixPopupTopmost(Popup popup)
        {
            popup.Opened += (s, e) =>
            {
                ApplyNoTopmost(PresentationSource.FromVisual(popup.Child) as HwndSource);
            };
        }

        /// <summary>
        /// Applies the same fix to a ContextMenu. A ContextMenu renders inside an
        /// internal Popup but is not a Popup itself, so we grab its window handle one
        /// render pass after Opened (Dispatcher BeginInvoke at Render priority), when
        /// the popup HWND has been created.
        /// </summary>
        public static void FixContextMenuTopmost(ContextMenu menu)
        {
            menu.Opened += (s, e) =>
            {
                menu.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    ApplyNoTopmost(PresentationSource.FromVisual(menu) as HwndSource);
                }));
            };
        }

        /// <summary>
        /// Applies the same fix to a ComboBox dropdown (e.g. the ModernComboBox
        /// template's AllowsTransparency Popup). On DropDownOpened, wait one render
        /// pass, locate the template's Popup child and fix its HWND.
        /// </summary>
        public static void FixComboBoxPopupTopmost(ComboBox comboBox)
        {
            comboBox.DropDownOpened += (s, e) =>
            {
                comboBox.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    var popup = FindVisualChild<Popup>(comboBox);
                    if (popup?.Child != null)
                        ApplyNoTopmost(PresentationSource.FromVisual(popup.Child) as HwndSource);
                }));
            };
        }

        private static void ApplyNoTopmost(HwndSource source)
        {
            if (source == null) return;

            // Remove topmost z-order imposed by WPF's transparent popup
            SetWindowPos(source.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            // Add WS_EX_NOACTIVATE so the popup never steals Windows-level focus
            // from the main window. Without this, interacting with a Slider or
            // other focusable control inside the popup causes the popup HWND to
            // become the active window; the first subsequent click on the main
            // window is then swallowed by Windows to re-activate it, making
            // toolbar buttons appear to require two clicks.
            int exStyle = GetWindowLong(source.Handle, GWL_EXSTYLE);
            SetWindowLong(source.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
