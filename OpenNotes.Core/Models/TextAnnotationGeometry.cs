using System;

namespace Caelum.Models;

public enum TextResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

public readonly record struct TextBoxBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public static class TextAnnotationGeometry
{
    public const double DefaultWidth = 280;
    public const double DefaultHeight = 84;
    public const double MinimumWidth = 120;
    public const double MinimumHeight = 48;
    public const double MoveBorderHitThickness = 8;

    public static string GetResizeHandleAutomationId(TextResizeHandle handle)
    {
        return $"TextResizeHandle.{handle}";
    }

    public static bool IsMoveBorderHit(
        double x,
        double y,
        double width,
        double height,
        double hitThickness = MoveBorderHitThickness)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || width <= 0 ||
            !double.IsFinite(height) || height <= 0 ||
            !double.IsFinite(hitThickness) || hitThickness <= 0 ||
            x < 0 || y < 0 || x > width || y > height)
        {
            return false;
        }

        double band = Math.Min(hitThickness, Math.Min(width, height) / 2);
        return x <= band || x >= width - band || y <= band || y >= height - band;
    }

    public static TextBoxBounds Normalize(TextBoxBounds bounds)
    {
        return bounds with
        {
            Width = ClampDimension(bounds.Width, MinimumWidth),
            Height = ClampDimension(bounds.Height, MinimumHeight),
        };
    }

    public static TextBoxBounds Resize(
        TextBoxBounds start,
        TextResizeHandle handle,
        double deltaX,
        double deltaY)
    {
        start = Normalize(start);

        bool movesLeft = handle is TextResizeHandle.TopLeft
            or TextResizeHandle.Left
            or TextResizeHandle.BottomLeft;
        bool movesRight = handle is TextResizeHandle.TopRight
            or TextResizeHandle.Right
            or TextResizeHandle.BottomRight;
        bool movesTop = handle is TextResizeHandle.TopLeft
            or TextResizeHandle.Top
            or TextResizeHandle.TopRight;
        bool movesBottom = handle is TextResizeHandle.BottomLeft
            or TextResizeHandle.Bottom
            or TextResizeHandle.BottomRight;

        double width = start.Width;
        double height = start.Height;
        double x = start.X;
        double y = start.Y;

        if (movesLeft)
        {
            width = ClampDimension(start.Width - deltaX, MinimumWidth);
            x = start.Right - width;
        }
        else if (movesRight)
        {
            width = ClampDimension(start.Width + deltaX, MinimumWidth);
        }

        if (movesTop)
        {
            height = ClampDimension(start.Height - deltaY, MinimumHeight);
            y = start.Bottom - height;
        }
        else if (movesBottom)
        {
            height = ClampDimension(start.Height + deltaY, MinimumHeight);
        }

        return new TextBoxBounds(x, y, width, height);
    }

    /// <summary>
    /// Keeps a text box inside the page surface while retaining the configured
    /// minimum dimensions. Invalid or not-yet-measured page dimensions leave
    /// the normalized bounds unchanged so resizing still works during layout.
    /// </summary>
    public static TextBoxBounds ClampToPage(TextBoxBounds bounds, double pageWidth, double pageHeight)
    {
        var normalized = Normalize(bounds);
        if (!double.IsFinite(pageWidth) || pageWidth <= 0 ||
            !double.IsFinite(pageHeight) || pageHeight <= 0)
        {
            return normalized;
        }

        double x = Math.Max(0, normalized.X);
        double y = Math.Max(0, normalized.Y);

        if (pageWidth >= MinimumWidth)
        {
            x = Math.Min(x, pageWidth - MinimumWidth);
            double width = Math.Min(normalized.Width, pageWidth - x);
            normalized = normalized with { X = x, Width = Math.Max(MinimumWidth, width) };
        }
        else
        {
            normalized = normalized with { X = 0 };
        }

        if (pageHeight >= MinimumHeight)
        {
            y = Math.Min(y, pageHeight - MinimumHeight);
            double height = Math.Min(normalized.Height, pageHeight - y);
            normalized = normalized with { Y = y, Height = Math.Max(MinimumHeight, height) };
        }
        else
        {
            normalized = normalized with { Y = 0 };
        }

        return normalized;
    }

    private static double ClampDimension(double value, double minimum)
    {
        return double.IsFinite(value) ? Math.Max(minimum, value) : minimum;
    }
}
