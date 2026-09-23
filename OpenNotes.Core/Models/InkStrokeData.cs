using System.Collections.Generic;

namespace Caelum.Models;

/// <summary>
/// UI-free in-memory stroke payload mirroring <see cref="StrokeAnnotation"/>
/// semantics 1:1: spine points with pressure, RGBA colour channels, a single
/// uniform stroke size, the highlighter/FitToCurve rendering choices and the
/// logical-shape identity fields. The WinUI ink surface builds this type
/// directly; the WPF layer converts live <c>System.Windows.Ink.Stroke</c>
/// objects through its adapter.
/// </summary>
public sealed class InkStrokeData
{
    /// <summary>Spine points in page DIP coordinates (origin top-left).</summary>
    public List<InkPointData> Points { get; set; } = new();

    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte A { get; set; } = 255;

    /// <summary>Uniform stroke width/height in DIPs, like <see cref="StrokeAnnotation.Size"/>.</summary>
    public double Size { get; set; } = 2.0;

    public bool IsHighlighter { get; set; }

    // Preserve the WPF rendering choice used by shape tools and smoothing.
    // Same default as StrokeAnnotation: FitToCurve=true.
    public bool FitToCurve { get; set; } = true;

    public string ShapeGroupId { get; set; } = string.Empty;
    public string ShapeKind { get; set; } = string.Empty;
    public int ShapePartIndex { get; set; }
    public bool IsDashedShape { get; set; }

    /// <summary>The logical-shape identity carried by this stroke (empty = ordinary ink).</summary>
    public ShapeStrokeIdentity GetShapeIdentity() =>
        new(ShapeGroupId, ShapeKind, ShapePartIndex, IsDashedShape);

    /// <summary>Copies a logical-shape identity onto this stroke's metadata fields.</summary>
    public void ApplyShapeIdentity(ShapeStrokeIdentity identity)
    {
        ShapeGroupId = identity.GroupId ?? string.Empty;
        ShapeKind = identity.Kind ?? string.Empty;
        ShapePartIndex = identity.PartIndex;
        IsDashedShape = identity.IsDashed;
    }
}
