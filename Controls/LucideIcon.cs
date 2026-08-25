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
            !string.IsNullOrWhiteSpace(Kind) && Icons.TryGetValue(Kind, out Geometry geometry)
                ? geometry
                : Icons["Circle"];

        private static IReadOnlyDictionary<string, Geometry> CreateIcons()
        {
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Circle"] = "M12,3 A9,9 0 1 1 11.99,3",
                ["Undo2"] = "M9,14 L4,9 L9,4 M4,9 H15 A6,6 0 0 1 15,21 H11",
                ["Redo2"] = "M15,14 L20,9 L15,4 M20,9 H9 A6,6 0 0 0 9,21 H13",
                ["PenLine"] = "M4,16 L14,6 L18,10 L8,20 H4 Z M12,8 L16,12 M13,20 H21",
                ["Highlighter"] = "M3,15 L13,5 L19,11 L9,21 H3 Z M11,7 L17,13 M14,21 H21",
                ["PanelTop"] = "M4,4 H20 V20 H4 Z M4,9 H20 M8,13 H16 M8,16 H14",
                ["StickyNote"] = "M5,3 H19 V15 L14,20 H5 Z M14,20 V15 H19 M8,8 H16 M8,11 H14",
                ["Eraser"] = "M7,19 L3,15 L13,5 A2,2 0 0 1 16,5 L20,9 A2,2 0 0 1 20,12 L13,19 Z M8,10 L15,17 M12,19 H21",
                ["Shapes"] = "M5,4 H12 V11 H5 Z M16,5 A4,4 0 1 1 15.99,5 M4,20 L9,13 L14,20 Z",
                ["WandSparkles"] = "M15,4 L20,9 L9,20 L4,15 Z M13,6 L18,11 M19,3 V6 M17.5,4.5 H20.5 M5,3 V6 M3.5,4.5 H6.5",
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
                ["GripVertical"] = "M9,5 A1,1 0 1 1 8.99,5 M15,5 A1,1 0 1 1 14.99,5 M9,12 A1,1 0 1 1 8.99,12 M15,12 A1,1 0 1 1 14.99,12 M9,19 A1,1 0 1 1 8.99,19 M15,19 A1,1 0 1 1 14.99,19"
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
