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
        private bool _isPointerOver;
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
        /// Lucide icon name (kept identical to WPF). T13-B: the tab template
        /// binds it straight into <c>LucideIcon.Kind</c> — the interim
        /// Segoe-glyph mapper is gone.
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
            }
        }

        /// <summary>
        /// null for Home tabs, file path for editor tabs.
        /// </summary>
        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath == value)
                    return;
                _filePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsHome)); // derived from FilePath
            }
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
                OnPropertyChanged(nameof(CloseButtonHitTestVisible));
                OnPropertyChanged(nameof(AccentBarVisibility));
            }
        }

        /// <summary>
        /// Set by the pill template's PointerEntered/Exited — drives the
        /// hover-reveal close chrome (inactive tabs show their close button
        /// only while hovered; the active tab always shows it).
        /// </summary>
        public bool IsPointerOver
        {
            get => _isPointerOver;
            set
            {
                if (_isPointerOver == value)
                    return;
                _isPointerOver = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CloseButtonOpacity));
                OnPropertyChanged(nameof(CloseButtonHitTestVisible));
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
                OnPropertyChanged(nameof(CloseButtonHitTestVisible));
            }
        }

        // ── Template-bound chrome (computed; re-raised by RefreshVisualState) ──

        /// <summary>WPF truncates tab titles to 20 chars with "..." suffix.</summary>
        public string DisplayTitle =>
            _title != null && _title.Length > 20 ? _title.Substring(0, 17) + "..." : _title;

        /// <summary>
        /// Fluent pill: the active tab gets the elevated surface brush so it
        /// reads as a card floating on the Mica/acrylic chrome band;
        /// inactive pills stay transparent over the band.
        /// </summary>
        public Brush TabBackground =>
            _isActive ? ResolveBrush("ThemeSurfaceBrush", "#FFFFFF") : TransparentBrush;

        public Brush TabBorderBrush =>
            _isActive ? ResolveBrush("ThemeBorderBrush", "#D1D5DB") : TransparentBrush;

        public Brush TabForeground =>
            ResolveBrush(_isActive ? "ThemeForegroundBrush" : "ThemeSubtleForegroundBrush",
                _isActive ? "#1F2937" : "#4B5563");

        public FontWeight TitleFontWeight =>
            _isActive ? FontWeights.Medium : FontWeights.Normal;

        /// <summary>Accent underline under the active pill (Fluent tab cue).</summary>
        public Visibility AccentBarVisibility =>
            _isActive ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Hover-reveal close chrome: always visible on the active tab,
        /// shown on inactive tabs only while the pointer is over the pill.
        /// </summary>
        public double CloseButtonOpacity => _isActive || _isPointerOver ? 1 : 0;

        /// <summary>
        /// An opacity-0 element still hit-tests by default — without this
        /// gate an invisible close button on an inactive, unhovered pill
        /// could swallow a click aimed at selecting the tab.
        /// </summary>
        public bool CloseButtonHitTestVisible =>
            _isCloseButtonVisible && (_isActive || _isPointerOver);

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
            OnPropertyChanged(nameof(TabBackground));
            OnPropertyChanged(nameof(TabBorderBrush));
            OnPropertyChanged(nameof(TabForeground));
            OnPropertyChanged(nameof(TitleFontWeight));
            OnPropertyChanged(nameof(CloseButtonOpacity));
            OnPropertyChanged(nameof(CloseButtonHitTestVisible));
            OnPropertyChanged(nameof(CloseButtonVisibility));
            OnPropertyChanged(nameof(AccentBarVisibility));
            OnPropertyChanged(nameof(CloseTooltip));
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
