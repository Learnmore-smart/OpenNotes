using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Caelum.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Caelum.Models
{
    /// <summary>
    /// One library tile on the WinUI <c>HomePage</c> — the counterpart of the
    /// <c>HomeTile</c> class at the bottom of the WPF
    /// <c>Pages/HomePage.xaml.cs</c>. Holds the same data (add-tile / folder /
    /// file kinds, selection state, drop-target highlight, folder color) and
    /// the same computed folder-tab/body/line brushes — now expressed as
    /// <see cref="Microsoft.UI.Xaml.Media.Brush"/>.
    ///
    /// WinUI has no <c>DataTrigger</c>, so the chrome states the WPF templates
    /// expressed as triggers are computed properties here instead:
    /// <see cref="TileBackground"/>/<see cref="TileBorderBrush"/>/
    /// <see cref="TileBorderThickness"/> cover hover/selected/drop-target
    /// painting, and <see cref="CheckBadgeVisibility"/>/
    /// <see cref="CheckGlyphVisibility"/> cover the selection-mode check
    /// badge. The page pushes <see cref="IsHovered"/> (PointerEntered/Exited)
    /// and <see cref="IsSelectionModeUi"/> (selection-mode toggle); both are
    /// INPC so the template bindings stay declarative.
    /// </summary>
    public sealed class HomeTile : INotifyPropertyChanged
    {
        public string Id { get; private set; } = string.Empty;

        public bool IsAddTile { get; private set; }

        public bool IsFolder { get; private set; }

        public bool IsFile => !IsAddTile && !IsFolder;

        public bool IsNotebook { get; private set; }

        public string ParentFolderId { get; private set; } = string.Empty;

        public string Path { get; private set; } = string.Empty;

        public int SortPriority => IsAddTile ? 0 : IsFolder ? 1 : 2;

        /// <summary>
        /// Stable UIA hook for the smoke scripts:
        /// <c>HomeTile_Add</c> for the add tile, <c>HomeTile_&lt;name&gt;</c>
        /// for files/folders.
        /// </summary>
        public string AutomationId => IsAddTile
            ? "HomeTile_Add"
            : $"HomeTile_{FileName}";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(TileBackground));
                OnPropertyChanged(nameof(TileBorderBrush));
                OnPropertyChanged(nameof(CheckBadgeBackground));
                OnPropertyChanged(nameof(CheckBadgeBorderBrush));
                OnPropertyChanged(nameof(CheckGlyphVisibility));
            }
        }

        private bool _isDropTarget;
        public bool IsDropTarget
        {
            get => _isDropTarget;
            set
            {
                if (_isDropTarget == value)
                    return;
                _isDropTarget = value;
                OnPropertyChanged(nameof(IsDropTarget));
                OnPropertyChanged(nameof(TileBackground));
                OnPropertyChanged(nameof(TileBorderBrush));
                OnPropertyChanged(nameof(TileBorderThickness));
            }
        }

        /// <summary>
        /// Set by the page on PointerEntered/PointerExited (the WinUI stand-in
        /// for the WPF <c>IsMouseOver</c> trigger).
        /// </summary>
        private bool _isHovered;
        public bool IsHovered
        {
            get => _isHovered;
            set
            {
                if (_isHovered == value)
                    return;
                _isHovered = value;
                OnPropertyChanged(nameof(IsHovered));
                OnPropertyChanged(nameof(TileBackground));
                OnPropertyChanged(nameof(TileBorderBrush));
            }
        }

        /// <summary>
        /// Mirrors the page's <c>IsSelectionMode</c> so the file-tile check
        /// badge can bind declaratively (WPF used a
        /// <c>RelativeSource AncestorType=Page</c> DataTrigger).
        /// </summary>
        private bool _isSelectionModeUi;
        public bool IsSelectionModeUi
        {
            get => _isSelectionModeUi;
            set
            {
                if (_isSelectionModeUi == value)
                    return;
                _isSelectionModeUi = value;
                OnPropertyChanged(nameof(IsSelectionModeUi));
                OnPropertyChanged(nameof(CheckBadgeVisibility));
            }
        }

        private string _color = string.Empty;
        public string Color
        {
            get => _color;
            private set
            {
                _color = value ?? string.Empty;
                OnPropertyChanged(nameof(Color));
                OnPropertyChanged(nameof(FolderTabBrush));
                OnPropertyChanged(nameof(FolderBodyBrush));
                OnPropertyChanged(nameof(FolderLineBrush));
                OnPropertyChanged(nameof(FolderLineAltBrush));
            }
        }

        public Brush FolderTabBrush => CreateFolderBrush(0.18, "#FBBF24");
        public Brush FolderBodyBrush => CreateFolderBrush(0.0, "#F59E0B");
        public Brush FolderLineBrush => CreateFolderBrush(0.45, "#FDE68A");
        public Brush FolderLineAltBrush => CreateFolderBrush(0.28, "#FCD34D");

        // ── Computed tile chrome (WPF DataTrigger equivalents) ─────────────

        /// <summary>
        /// Folder: drop-target → selection brush + accent border (WPF
        /// IsDropTarget DataTrigger); hover → hover brush + border (IsMouseOver
        /// trigger). File: selected → selection brush + accent border; hover →
        /// hover brush only. Mirrors the WPF trigger precedence — the
        /// selected/drop-target state wins over hover.
        /// </summary>
        public Brush TileBackground
        {
            get
            {
                if (IsDropTarget || IsSelected)
                    return ResolveBrush("ThemeSelectionBrush", "#DBEAFE");
                if (IsHovered)
                    return ResolveBrush("ThemeControlHoverBrush", "#EEF0F2");
                return TransparentBrush;
            }
        }

        public Brush TileBorderBrush
        {
            get
            {
                if (IsDropTarget || IsSelected)
                    return ResolveBrush("ThemeAccentBrush", "#2563EB");
                if (IsHovered && IsFolder)
                    return ResolveBrush("ThemeBorderBrush", "#D1D5DB");
                return TransparentBrush;
            }
        }

        public Thickness TileBorderThickness => IsDropTarget ? new Thickness(1.5) : new Thickness(1);

        public Visibility CheckBadgeVisibility =>
            IsSelectionModeUi && IsFile ? Visibility.Visible : Visibility.Collapsed;

        public Brush CheckBadgeBackground =>
            IsSelected ? ResolveBrush("ThemeAccentBrush", "#2563EB")
                       : ResolveBrush("ThemeSurfaceAltBrush", "#F8F9FA");

        public Brush CheckBadgeBorderBrush =>
            IsSelected ? ResolveBrush("ThemeAccentBrush", "#2563EB")
                       : ResolveBrush("ThemeBorderBrush", "#D1D5DB");

        public Visibility CheckGlyphVisibility =>
            IsSelected ? Visibility.Visible : Visibility.Collapsed;

        private SolidColorBrush CreateFolderBrush(double lighten, string fallbackHex)
        {
            var color = ParseFolderColor(Color, fallbackHex);
            if (lighten > 0)
                color = MixWithWhite(color, lighten);
            return new SolidColorBrush(color);
        }

        private static Windows.UI.Color ParseFolderColor(string hex, string fallbackHex)
        {
            if (TryParseHexColor(hex, out var color))
                return color;
            return TryParseHexColor(fallbackHex, out var fallback)
                ? fallback
                : Windows.UI.Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B);
        }

        private static bool TryParseHexColor(string hex, out Windows.UI.Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex))
                return false;

            var value = hex.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
                value = value.Substring(1);

            try
            {
                if (value.Length == 6)
                {
                    color = Windows.UI.Color.FromArgb(
                        0xFF,
                        Convert.ToByte(value.Substring(0, 2), 16),
                        Convert.ToByte(value.Substring(2, 2), 16),
                        Convert.ToByte(value.Substring(4, 2), 16));
                    return true;
                }

                if (value.Length == 8)
                {
                    color = Windows.UI.Color.FromArgb(
                        Convert.ToByte(value.Substring(0, 2), 16),
                        Convert.ToByte(value.Substring(2, 2), 16),
                        Convert.ToByte(value.Substring(4, 2), 16),
                        Convert.ToByte(value.Substring(6, 2), 16));
                    return true;
                }
            }
            catch
            {
                // Fall through to the fallback color below.
            }

            return false;
        }

        private static Windows.UI.Color MixWithWhite(Windows.UI.Color color, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Windows.UI.Color.FromArgb(
                color.A,
                (byte)(color.R + (255 - color.R) * amount),
                (byte)(color.G + (255 - color.G) * amount),
                (byte)(color.B + (255 - color.B) * amount));
        }

        private int _pageCount;
        public int PageCount
        {
            get => _pageCount;
            set
            {
                _pageCount = value;
                OnPropertyChanged(nameof(PageCount));
                OnPropertyChanged(nameof(InfoText));
            }
        }

        private int _childCount;
        public int ChildCount
        {
            get => _childCount;
            set
            {
                _childCount = value;
                OnPropertyChanged(nameof(ChildCount));
                OnPropertyChanged(nameof(InfoText));
            }
        }

        private DateTime _lastModified;
        public DateTime LastModified
        {
            get => _lastModified;
            set
            {
                _lastModified = value;
                OnPropertyChanged(nameof(LastModified));
                OnPropertyChanged(nameof(InfoText));
            }
        }

        private string _displayName = string.Empty;

        public string FileName
        {
            get
            {
                if (IsFolder)
                    return _displayName;

                if (string.IsNullOrWhiteSpace(Path))
                    return string.Empty;

                return System.IO.Path.GetFileName(Path);
            }
        }

        public string InfoText
        {
            get
            {
                if (IsAddTile)
                    return string.Empty;

                if (IsFolder)
                    return LocalizationService.Format("Home.Info.Items", ChildCount);

                if (string.IsNullOrWhiteSpace(Path))
                    return string.Empty;

                var parts = new List<string>();
                if (IsNotebook)
                    parts.Add(LocalizationService.Get("Home.Info.Notebook"));
                if (PageCount > 0)
                    parts.Add(LocalizationService.Format("Home.Info.Pages", PageCount));
                if (LastModified != default)
                    parts.Add(LastModified.ToString("d", LocalizationService.CurrentCulture));
                return string.Join(" · ", parts);
            }
        }

        public void SetPath(string newPath)
        {
            Path = newPath ?? string.Empty;
            OnPropertyChanged(nameof(Path));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(InfoText));
            OnPropertyChanged(nameof(AutomationId));
        }

        /// <summary>
        /// Re-raises display + computed-brush properties. Called on language
        /// changes (FileName/InfoText text) and theme applies (theme-resolved
        /// brushes are plain objects — they do not re-resolve on their own).
        /// </summary>
        public void RefreshDisplay()
        {
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(InfoText));
            OnPropertyChanged(nameof(AutomationId));
            OnPropertyChanged(nameof(FolderTabBrush));
            OnPropertyChanged(nameof(FolderBodyBrush));
            OnPropertyChanged(nameof(FolderLineBrush));
            OnPropertyChanged(nameof(FolderLineAltBrush));
            OnPropertyChanged(nameof(TileBackground));
            OnPropertyChanged(nameof(TileBorderBrush));
            OnPropertyChanged(nameof(CheckBadgeBackground));
            OnPropertyChanged(nameof(CheckBadgeBorderBrush));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static Brush ResolveBrush(string key, string fallbackHex)
        {
            if (Application.Current?.Resources?.TryGetValue(key, out var value) == true &&
                value is Brush brush)
                return brush;
            return WinUiThemeService.CreateBrush(fallbackHex);
        }

        private static readonly Brush TransparentBrush = new SolidColorBrush(Colors.Transparent);

        public static HomeTile CreateAddTile()
        {
            return new HomeTile
            {
                IsAddTile = true
            };
        }

        public static HomeTile CreateFileTile(RecentFileEntry entry)
        {
            return new HomeTile
            {
                Id = entry?.Id ?? string.Empty,
                Path = entry?.Path ?? string.Empty,
                ParentFolderId = entry?.ParentFolderId ?? string.Empty,
                IsNotebook = entry?.IsNotebook == true,
                _pageCount = entry?.PageCount ?? 0,
                _lastModified = entry?.LastModifiedUtc?.ToLocalTime() ?? default
            };
        }

        public static HomeTile CreateFolderTile(RecentFileEntry entry, int childCount)
        {
            return new HomeTile
            {
                Id = entry?.Id ?? string.Empty,
                IsFolder = true,
                ParentFolderId = entry?.ParentFolderId ?? string.Empty,
                _displayName = entry?.DisplayName ?? string.Empty,
                _childCount = childCount,
                _lastModified = entry?.LastModifiedUtc?.ToLocalTime() ?? default,
                Color = entry?.Color ?? string.Empty
            };
        }
    }
}
