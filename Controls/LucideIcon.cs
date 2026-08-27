using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Caelum.Controls
{
    /// <summary>
    /// Font-independent 24-unit outline icons based on the Lucide visual language.
    /// The owning Button/ToggleButton continues to provide the accessible name.
    /// </summary>
    public sealed class LucideIcon : Shape
    {
        private static readonly IReadOnlyDictionary<string, Geometry> Icons = CreateIcons();
        private static readonly IReadOnlyDictionary<string, string> LegacyKinds = new Dictionary<string, string>
        {
            [((char)0xE14D).ToString()] = "Copy",
            [((char)0xE70B).ToString()] = "FilePlus",
            [((char)0xE70F).ToString()] = "Pencil",
            [((char)0xE710).ToString()] = "Plus",
            [((char)0xE713).ToString()] = "Settings",
            [((char)0xE73E).ToString()] = "Check",
            [((char)0xE749).ToString()] = "Printer",
            [((char)0xE74D).ToString()] = "Trash2",
            [((char)0xE74E).ToString()] = "Save",
            [((char)0xE762).ToString()] = "ListFilter",
            [((char)0xE783).ToString()] = "AlertCircle",
            [((char)0xE7AD).ToString()] = "RotateCw",
            [((char)0xE7C3).ToString()] = "FileText",
            [((char)0xE80F).ToString()] = "Home",
            [((char)0xE838).ToString()] = "FolderOpen",
            [((char)0xE8B7).ToString()] = "Folder",
            [((char)0xE8BB).ToString()] = "X",
            [((char)0xE8C6).ToString()] = "Scissors",
            [((char)0xE8C8).ToString()] = "Copy",
            [((char)0xE8DE).ToString()] = "Move",
            [((char)0xE8E5).ToString()] = "FileText",
            [((char)0xE946).ToString()] = "History",
            [((char)0xEA90).ToString()] = "FileText",
            [((char)0xEDA4).ToString()] = "PenLine",
            [((char)0xEDE1).ToString()] = "Download"
        };

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(string), typeof(LucideIcon),
            new FrameworkPropertyMetadata("Circle", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        static LucideIcon()
        {
            StretchProperty.OverrideMetadata(typeof(LucideIcon), new FrameworkPropertyMetadata(Stretch.Uniform));
            StrokeThicknessProperty.OverrideMetadata(typeof(LucideIcon), new FrameworkPropertyMetadata(1.8));
            StrokeStartLineCapProperty.OverrideMetadata(typeof(LucideIcon), new FrameworkPropertyMetadata(PenLineCap.Round));
            StrokeEndLineCapProperty.OverrideMetadata(typeof(LucideIcon), new FrameworkPropertyMetadata(PenLineCap.Round));
            StrokeLineJoinProperty.OverrideMetadata(typeof(LucideIcon), new FrameworkPropertyMetadata(PenLineJoin.Round));
            FillProperty.OverrideMetadata(typeof(LucideIcon), new FrameworkPropertyMetadata(Brushes.Transparent));
            IsHitTestVisibleProperty.OverrideMetadata(typeof(LucideIcon), new FrameworkPropertyMetadata(false));
            FocusableProperty.OverrideMetadata(typeof(LucideIcon), new FrameworkPropertyMetadata(false));
        }

        public string Kind
        {
            get => (string)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        protected override Geometry DefiningGeometry =>
            !string.IsNullOrWhiteSpace(ResolveKind(Kind)) && Icons.TryGetValue(ResolveKind(Kind), out Geometry geometry)
                ? geometry
                : Icons["Circle"];

        private static string ResolveKind(string kind) =>
            !string.IsNullOrEmpty(kind) && LegacyKinds.TryGetValue(kind, out string resolved) ? resolved : kind;

        private static IReadOnlyDictionary<string, Geometry> CreateIcons()
        {
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Circle"] = "M12,3 A9,9 0 1 1 11.99,3",
                ["Undo2"] = "M9,14 L4,9 L9,4 M4,9 H15 A6,6 0 0 1 15,21 H11",
                ["Redo2"] = "M15,14 L20,9 L15,4 M20,9 H9 A6,6 0 0 0 9,21 H13",
                ["PenLine"] = "M4,16 L14,6 L18,10 L8,20 H4 Z M12,8 L16,12 M13,20 H21",
                ["Highlighter"] = "M3,15 L13,5 L19,11 L9,21 H3 Z M11,7 L17,13 M14,21 H21",
                ["HiddenInkReveal"] = "M4,5 H20 V19 H4 Z M4,9 H20 M8,13 H16 M8,16 H13 M17,12 V16 M14.5,14 H19.5",
                ["StickyNote"] = "M5,3 H19 V15 L14,20 H5 Z M14,20 V15 H19 M8,8 H16 M8,11 H14",
                ["Eraser"] = "M7,19 L3,15 L13,5 A2,2 0 0 1 16,5 L20,9 A2,2 0 0 1 20,12 L13,19 Z M8,10 L15,17 M12,19 H21",
                ["Shapes"] = "M5,4 H12 V11 H5 Z M16,5 A4,4 0 1 1 15.99,5 M4,20 L9,13 L14,20 Z",
                ["Laser"] = "M3,12 H15 M11,8 L15,12 L11,16 M18,12 A2,2 0 1 1 17.99,12",
                ["Ruler"] = "M4,15 L15,4 L20,9 L9,20 Z M11,8 L16,13 M8,11 L10,13 M5,14 L7,16",
                ["MousePointer2"] = "M5,3 L19,13 L12,14 L9,21 Z M12,14 L18,20",
                ["Type"] = "M5,5 H19 M12,5 V19 M8,19 H16",
                ["Save"] = "M5,3 H17 L21,7 V21 H3 V3 Z M7,3 V9 H16 V3 M7,21 V14 H17 V21",
                ["History"] = "M3,12 A9,9 0 1 0 6,5 M3,4 V10 H9 M12,7 V12 L16,14",
                ["Minus"] = "M5,12 H19",
                ["Plus"] = "M5,12 H19 M12,5 V19",
                ["RotateCcw"] = "M4,4 V10 H10 M5,15 A8,8 0 1 0 6,7",
                ["Maximize"] = "M8,3 H3 V8 M16,3 H21 V8 M3,16 V21 H8 M21,16 V21 H16",
                ["PanelLeftClose"] = "M4,3 H20 V21 H4 Z M9,3 V21 M15,9 L12,12 L15,15",
                ["PanelLeftOpen"] = "M4,3 H20 V21 H4 Z M9,3 V21 M12,9 L15,12 L12,15",
                ["Files"] = "M8,2 H18 A2,2 0 0 1 20,4 V18 M6,6 H16 A2,2 0 0 1 18,8 V20 A2,2 0 0 1 16,22 H6 A2,2 0 0 1 4,20 V8 A2,2 0 0 1 6,6 Z M8,11 H14 M8,15 H14",
                ["ListTree"] = "M4,5 H6 M10,5 H20 M4,12 H6 M10,12 H20 M4,19 H6 M10,19 H20",
                ["Bookmark"] = "M6,3 H18 V21 L12,17 L6,21 Z",
                ["ChevronLeft"] = "M15,18 L9,12 L15,6",
                ["ChevronRight"] = "M9,18 L15,12 L9,6",
                ["GripVertical"] = "M9,5 A1,1 0 1 1 8.99,5 M15,5 A1,1 0 1 1 14.99,5 M9,12 A1,1 0 1 1 8.99,12 M15,12 A1,1 0 1 1 14.99,12 M9,19 A1,1 0 1 1 8.99,19 M15,19 A1,1 0 1 1 14.99,19",
                ["ArrowLeft"] = "M19,12 H5 M12,19 L5,12 L12,5",
                ["ArrowRight"] = "M5,12 H19 M12,5 L19,12 L12,19",
                ["Home"] = "M3,11 L12,3 L21,11 M5,10 V21 H19 V10 M9,21 V14 H15 V21",
                ["Search"] = "M4,11 A7,7 0 1 1 18,11 A7,7 0 1 1 4,11 M16,16 L21,21",
                ["ListFilter"] = "M4,6 H20 M7,12 H17 M10,18 H14",
                ["ArrowUpDown"] = "M8,4 V20 M4,8 L8,4 L12,8 M16,20 V4 M12,16 L16,20 L20,16",
                ["Ellipsis"] = "M5,12 A1,1 0 1 1 4.99,12 M12,12 A1,1 0 1 1 11.99,12 M19,12 A1,1 0 1 1 18.99,12",
                ["Square"] = "M5,5 H19 V19 H5 Z",
                ["Restore"] = "M8,8 H20 V20 H8 Z M4,16 V4 H16 V8",
                ["X"] = "M6,6 L18,18 M18,6 L6,18",
                ["Check"] = "M4,12 L9,17 L20,6",
                ["Printer"] = "M6,9 V3 H18 V9 M6,18 H4 A2,2 0 0 1 2,16 V11 A2,2 0 0 1 4,9 H20 A2,2 0 0 1 22,11 V16 A2,2 0 0 1 20,18 H18 M6,14 H18 V21 H6 Z",
                ["Trash2"] = "M4,7 H20 M9,3 H15 L17,7 M7,7 L8,21 H16 L17,7 M10,11 V17 M14,11 V17",
                ["FilePlus"] = "M6,2 H14 L20,8 V22 H6 Z M14,2 V8 H20 M9,15 H17 M13,11 V19",
                ["FileText"] = "M6,2 H14 L20,8 V22 H6 Z M14,2 V8 H20 M9,13 H17 M9,17 H17",
                ["Folder"] = "M3,6 H10 L12,9 H21 V20 H3 Z",
                ["FolderOpen"] = "M3,7 H10 L12,10 H21 L19,20 H3 L1,10 H5 Z",
                ["Pencil"] = "M4,20 L8,19 L19,8 A2,2 0 0 0 16,5 L5,16 Z M14,7 L17,10",
                ["Copy"] = "M8,8 H21 V21 H8 Z M3,3 H16 V8 M3,3 V16 H8",
                ["Scissors"] = "M6,8 A3,3 0 1 1 6,2 A3,3 0 1 1 6,8 M6,22 A3,3 0 1 1 6,16 A3,3 0 1 1 6,22 M8,7 L21,20 M8,17 L21,4",
                ["Image"] = "M3,4 H21 V20 H3 Z M8,10 A2,2 0 1 1 7.99,10 M3,17 L9,11 L14,16 L17,13 L21,17",
                ["AlertCircle"] = "M12,3 A9,9 0 1 1 11.99,3 M12,7 V13 M12,17 A1,1 0 1 1 11.99,17",
                ["Settings"] = "M12,8 A4,4 0 1 1 11.99,8 M19,13.5 L21,15 L19,19 L16.5,18.5 L15,21 H9 L7.5,18.5 L5,19 L3,15 L5,13.5 V10.5 L3,9 L5,5 L7.5,5.5 L9,3 H15 L16.5,5.5 L19,5 L21,9 L19,10.5 Z",
                ["Download"] = "M12,3 V15 M7,10 L12,15 L17,10 M4,20 H20",
                ["RotateCw"] = "M20,4 V10 H14 M19,15 A8,8 0 1 1 18,7",
                ["LoaderCircle"] = "M12,3 A9,9 0 0 1 21,12 M12,21 A9,9 0 0 1 3,12",
                ["Move"] = "M12,3 V21 M8,7 L12,3 L16,7 M8,17 L12,21 L16,17 M3,12 H21 M7,8 L3,12 L7,16 M17,8 L21,12 L17,16"
            };

            var result = new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in data)
            {
                Geometry geometry = Geometry.Parse(entry.Value);
                geometry.Freeze();
                result[entry.Key] = geometry;
            }

            return result;
        }
    }
}
