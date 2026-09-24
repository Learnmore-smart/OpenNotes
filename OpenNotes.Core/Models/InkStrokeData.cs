using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Session-only rendering choice mirroring WPF
    /// <c>DrawingAttributes.IgnorePressure</c>: when true the stroke renders
    /// (and erases) at uniform width, the effective pressure being 0.5.
    /// Never serialized — WPF drops it too; per-point pressure survives in
    /// the sidecar either way.
    /// </summary>
    public bool IgnorePressure { get; set; }

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

    /// <summary>Deep copy — the point list is duplicated.</summary>
    public InkStrokeData Clone() => new()
    {
        Points = new List<InkPointData>(Points),
        R = R,
        G = G,
        B = B,
        A = A,
        Size = Size,
        IsHighlighter = IsHighlighter,
        FitToCurve = FitToCurve,
        IgnorePressure = IgnorePressure,
        ShapeGroupId = ShapeGroupId,
        ShapeKind = ShapeKind,
        ShapePartIndex = ShapePartIndex,
        IsDashedShape = IsDashedShape,
    };

    // ------------------------------------------------------------------
    // StrokeAnnotation (sidecar) conversion — the [x,y,p] format.
    //
    // Pressure-format decision (Phase A): each point serializes as
    // [x, y, pressure]. The third element is OPTIONAL on read: historical
    // sidecars and anything the WPF app writes use [x,y] and deserialize
    // with the long-standing default pressure of 0.5 — the same value WPF's
    // StylusPoint and the IgnorePressure rendering path use, so legacy
    // strokes render identically. WPF's reader only indexes elements 0/1,
    // so [x,y,p] stays forward-compatible with the shipping WPF app.
    // ------------------------------------------------------------------

    /// <summary>
    /// Serializes to the sidecar record: each point becomes
    /// <c>[x, y, pressure]</c> (3-element). <see cref="IgnorePressure"/> is a
    /// rendering choice and is not persisted.
    /// </summary>
    public StrokeAnnotation ToAnnotation() => new()
    {
        R = R,
        G = G,
        B = B,
        A = A,
        Size = Size,
        IsHighlighter = IsHighlighter,
        FitToCurve = FitToCurve,
        ShapeGroupId = ShapeGroupId,
        ShapeKind = ShapeKind,
        ShapePartIndex = ShapePartIndex,
        IsDashedShape = IsDashedShape,
        Points = Points
            .Select(p => new double[] { p.X, p.Y, p.Pressure })
            .ToList(),
    };

    /// <summary>
    /// Reads a sidecar record. Points with a third element take it as the
    /// pressure factor (clamped to [0,1]); two-element points keep the
    /// historical 0.5 default. Non-finite coordinates are dropped, matching
    /// the defensive parsing in the WPF loader.
    /// </summary>
    public static InkStrokeData FromAnnotation(StrokeAnnotation annotation)
    {
        if (annotation == null)
            throw new ArgumentNullException(nameof(annotation));

        var points = new List<InkPointData>(annotation.Points?.Count ?? 0);
        if (annotation.Points != null)
        {
            foreach (var pt in annotation.Points)
            {
                if (pt == null || pt.Length < 2
                    || !double.IsFinite(pt[0]) || !double.IsFinite(pt[1]))
                {
                    continue;
                }

                float pressure = 0.5f;
                if (pt.Length >= 3 && double.IsFinite(pt[2]))
                    pressure = (float)Math.Clamp(pt[2], 0.0, 1.0);
                points.Add(new InkPointData(pt[0], pt[1], pressure));
            }
        }

        return new InkStrokeData
        {
            Points = points,
            R = annotation.R,
            G = annotation.G,
            B = annotation.B,
            A = annotation.A,
            Size = annotation.Size,
            IsHighlighter = annotation.IsHighlighter,
            FitToCurve = annotation.FitToCurve,
            IgnorePressure = false,
            ShapeGroupId = annotation.ShapeGroupId ?? string.Empty,
            ShapeKind = annotation.ShapeKind ?? string.Empty,
            ShapePartIndex = annotation.ShapePartIndex,
            IsDashedShape = annotation.IsDashedShape,
        };
    }

    // ------------------------------------------------------------------
    // StrokeReplacementSnapshot conversion — tokenized undo/replacement
    // ledger payloads. Snapshots are session-only and immutable.
    // ------------------------------------------------------------------

    /// <summary>
    /// Captures an immutable snapshot under <paramref name="token"/>/
    /// <paramref name="side"/>. The snapshot stores this stroke's
    /// <see cref="IgnorePressure"/> flag and its logical-shape identity
    /// (group/kind/part/dashed) so a replacement round-trip reproduces the
    /// stroke exactly — without them an erase→undo or recognition swap would
    /// demote a grouped/dashed shape to plain ink on the next save.
    /// </summary>
    public StrokeReplacementSnapshot CaptureSnapshot(Guid token, StrokeReplacementSide side)
        => new(
            token,
            side,
            Points.Select(p => new StrokeReplacementPoint(p.X, p.Y, p.Pressure)),
            R, G, B, A,
            Size, Size,
            IsHighlighter,
            FitToCurve,
            IgnorePressure,
            ShapeGroupId,
            ShapeKind,
            ShapePartIndex,
            IsDashedShape);

    /// <summary>Rebuilds a live stroke from a replacement snapshot.</summary>
    public static InkStrokeData FromSnapshot(StrokeReplacementSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var stroke = new InkStrokeData
        {
            Points = snapshot.Points
                .Select(p => new InkPointData(p.X, p.Y, p.PressureFactor))
                .ToList(),
            R = snapshot.R,
            G = snapshot.G,
            B = snapshot.B,
            A = snapshot.A,
            Size = snapshot.Width,
            IsHighlighter = snapshot.IsHighlighter,
            FitToCurve = snapshot.FitToCurve,
            IgnorePressure = snapshot.IgnorePressure,
        };
        stroke.ApplyShapeIdentity(snapshot.GetShapeIdentity());
        return stroke;
    }
}
