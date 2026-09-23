using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Caelum.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Windows.UI.Text; // FontWeight struct; FontWeights static class is Microsoft.UI.Text.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Caelum.Models
{
    /// <summary>
    /// One tab in the WinUI shell — the counterpart of the WPF
    /// <c>Models/AppTab.cs</c>. It owns its live WinUI
    /// <see cref="Microsoft.UI.Xaml.Controls.Frame"/>, title/icon metadata, and
    /// optional document path.
    ///
    /// Design decision (V6 Task 4): this type intentionally lives in the WinUI
    /// project, not in OpenNotes.Core. The plan's "Core minus Frame" split was
    /// dropped — a Frame-free AppTab carries almost nothing (the Frame IS the
    /// payload) and would just be a second type to keep in sync. The WPF twin
    /// keeps its own copy for the same reason.
    ///
    /// The tab template binds the computed chrome properties below so
    /// active/inactive visuals stay declarative. They resolve theme brushes at
    /// get-time from <see cref="Application.Resources"/>; the window calls
    /// <see cref="RefreshVisualState"/> after each palette swap (and whenever
    /// tab-count chrome changes) so stale brushes are never painted.
    /// </summary>
    public class AppTab : INotifyPropertyChanged
    {
        private string _title = "Home";
        private string _icon = "Home";
        private string _filePath;
        private Frame _frame;
        private bool _isActive;
        private bool _isCloseButtonVisible = true;

        public string Id { get; } = Guid.NewGuid().ToString("N");

        public string Title
        {
            get => _title;
            set
            {
                if (_title == value)
                    return;
                _title = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }

        /// <summary>
        /// Lucide icon name (kept identical to WPF so the real icon port can
        /// swap the renderer without touching the model). <see cref="IconGlyph"/>
        /// maps it to a Segoe Fluent/MDL2 glyph for the interim FontIcon.
        /// </summary>
        public string Icon
        {
            get => _icon;
            set
            {
                if (_icon == value)
                    return;
                _icon = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IconGlyph));
            }
        }

        /// <summary>
        /// null for Home tabs, file path for editor tabs.
        /// </summary>
        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public bool IsHome => string.IsNullOrEmpty(_filePath);

        public Frame Frame
        {
            get => _frame;
            set { _frame = value; OnPropertyChanged(); }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;
                _isActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TabBackground));
                OnPropertyChanged(nameof(TabBorderBrush));
                OnPropertyChanged(nameof(TabForeground));
                OnPropertyChanged(nameof(TitleFontWeight));
                OnPropertyChanged(nameof(CloseButtonOpacity));
            }
        }

        /// <summary>
        /// WPF parity: close chrome is hidden while only one tab exists.
        /// </summary>
        public bool IsCloseButtonVisible
        {
            get => _isCloseButtonVisible;
            set
            {
                if (_isCloseButtonVisible == value)
                    return;
                _isCloseButtonVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CloseButtonVisibility));
            }
        }

        // ── Template-bound chrome (computed; re-raised by RefreshVisualState) ──

        /// <summary>WPF truncates tab titles to 20 chars with "..." suffix.</summary>
        public string DisplayTitle =>
            _title != null && _title.Length > 20 ? _title.Substring(0, 17) + "..." : _title;

        /// <summary>Segoe Fluent/MDL2 stand-in for <see cref="Icon"/>.</summary>
        public string IconGlyph => IconGlyphFor(_icon);

        public Brush TabBackground =>
            _isActive ? ResolveBrush("ThemeSurfaceAltBrush", "#F8F9FA") : TransparentBrush;

        public Brush TabBorderBrush =>
            _isActive ? ResolveBrush("ThemeBorderBrush", "#D1D5DB") : TransparentBrush;

        public Brush TabForeground =>
            ResolveBrush(_isActive ? "ThemeForegroundBrush" : "ThemeSubtleForegroundBrush",
                _isActive ? "#1F2937" : "#4B5563");

        public FontWeight TitleFontWeight =>
            _isActive ? FontWeights.Medium : FontWeights.Normal;

        public double CloseButtonOpacity => _isActive ? 1 : 0.72;

        public Visibility CloseButtonVisibility =>
            _isCloseButtonVisible ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Localized close tooltip hook (WPF parity).</summary>
        public string CloseTooltip => LocalizationService.Get("Main.CloseTabTooltip");

        /// <summary>
        /// Re-raises every computed chrome property so bindings re-resolve the
        /// current palette. Called by the window after a theme apply.
        /// </summary>
        public void RefreshVisualState()
        {
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(IconGlyph));
            OnPropertyChanged(nameof(TabBackground));
            OnPropertyChanged(nameof(TabBorderBrush));
            OnPropertyChanged(nameof(TabForeground));
            OnPropertyChanged(nameof(TitleFontWeight));
            OnPropertyChanged(nameof(CloseButtonOpacity));
            OnPropertyChanged(nameof(CloseButtonVisibility));
            OnPropertyChanged(nameof(CloseTooltip));
        }

        internal static string IconGlyphFor(string icon)
        {
            // Interim mapping until the Lucide vector icon port lands.
            switch (icon)
            {
                case "Home": return "\uE80F";
                case "File":
                case "FileText": return "\uE8A5";
                default: return "\uE8A5";
            }
        }

        private static Brush ResolveBrush(string key, string fallbackHex)
        {
            if (Application.Current?.Resources?.TryGetValue(key, out var value) == true &&
                value is Brush brush)
                return brush;
            return WinUiThemeService.CreateBrush(fallbackHex);
        }

        private static readonly Brush TransparentBrush = new SolidColorBrush(Colors.Transparent);

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
