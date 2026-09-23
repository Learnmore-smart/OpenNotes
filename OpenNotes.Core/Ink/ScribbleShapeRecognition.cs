using System;
using System.Collections.Generic;
using System.Linq;
using Caelum.InkGeometry;
using Caelum.Models;

namespace Caelum.Ink;

/// <summary>
/// The outcome of a successful scribble shape-recognition replacement:
/// the shared stroke token, the index the original occupied, and the two
/// immutable snapshots an <see cref="InkStrokeReplacedAction"/> needs to
/// swap the sides back and forth on undo/redo.
/// </summary>
public sealed class RecognizedStrokeReplacement
{
    public RecognizedStrokeReplacement(
        Guid token,
        int originalIndex,
        StrokeReplacementSnapshot originalSnapshot,
        StrokeReplacementSnapshot idealSnapshot)
    {
        Token = token;
        OriginalIndex = originalIndex;
        OriginalSnapshot = originalSnapshot;
        IdealSnapshot = idealSnapshot;
    }

    public Guid Token { get; }
    public int OriginalIndex { get; }
    public StrokeReplacementSnapshot OriginalSnapshot { get; }
    public StrokeReplacementSnapshot IdealSnapshot { get; }
}

/// <summary>
/// The recognition block of the WPF <c>InkCanvas_StrokeCollected</c>
/// pipeline, ported UI-free so the WinUI ink surface and headless tests
/// run the identical replace: classify the collected spine through
/// <see cref="StrokeGeometry.TryRecognizeShape"/> and, on a hit, add the
/// stroke to the store and immediately swap it for its ideal outline
/// under the same token (Original → Ideal side flip) via
/// <see cref="InkStrokeStore.TryReplaceStrokeQuiet"/> — in place, never
/// appended, same index.
/// The ideal stroke inherits the original's colour/size/highlighter
/// payload with <c>FitToCurve=false</c> and <c>IgnorePressure=true</c>
/// (crisp uniform-width edges, WPF parity); its outline points carry the
/// uniform 0.5 pressure a WPF <c>StylusPoint</c> defaults to.
/// The recognizer emits exactly ONE outline stroke — the multi-part
/// ShapeGroupId/ShapePartIndex strokes are the shape tool's commit format
/// (arrows etc., Phase B), not something scribble recognition produces.
/// </summary>
public static class ScribbleShapeRecognition
{
    /// <summary>
    /// Attempts to recognize <paramref name="stroke"/> as a line /
    /// rectangle / ellipse and to replace it inside <paramref name="store"/>
    /// with the ideal stroke. Returns false without touching the store
    /// when the stroke fails the gates (null, highlighter, fewer than
    /// <see cref="StrokeGeometry.MinRecognizedShapePoints"/> points) or no
    /// shape matches — the caller then finishes the normal add path. When
    /// the in-place replace itself fails after the stroke was added (a
    /// pathological token collision), the added stroke is removed again so
    /// the store looks exactly as if the call never ran.
    /// </summary>
    public static bool TryReplaceWithRecognizedStroke(
        InkStrokeStore store,
        InkStrokeData stroke,
        out RecognizedStrokeReplacement replacement)
    {
        replacement = null;
        if (store == null || stroke == null
            || stroke.IsHighlighter
            || stroke.Points == null
            || stroke.Points.Count < StrokeGeometry.MinRecognizedShapePoints)
        {
            return false;
        }

        var spine = stroke.Points
            .Select(p => new PointD(p.X, p.Y))
            .ToList();
        if (!StrokeGeometry.TryRecognizeShape(spine, out var outline)
            || outline == null || outline.Count == 0)
        {
            return false;
        }

        // The stroke must sit in the store before the replace so the token
        // and the slot exist — the same order WPF uses (InkCanvas inserts
        // the collected stroke before StrokeCollected fires).
        var placement = store.AddStrokeQuiet(stroke);
        var token = placement.Token;
        var originalSnapshot = stroke.CaptureSnapshot(token, StrokeReplacementSide.Original);

        var ideal = stroke.Clone();
        ideal.Points = outline
            .Select(p => new InkPointData(p.X, p.Y, 0.5f))
            .ToList();
        ideal.FitToCurve = false;    // crisp polygon edges like the shape tool
        ideal.IgnorePressure = true; // uniform width, no pressure jitter
        var idealSnapshot = ideal.CaptureSnapshot(token, StrokeReplacementSide.Ideal);

        if (!store.TryReplaceStrokeQuiet(
                token, StrokeReplacementSide.Original, idealSnapshot, out int index))
        {
            store.RemoveStrokeQuiet(stroke);
            return false;
        }

        replacement = new RecognizedStrokeReplacement(
            token, index, originalSnapshot, idealSnapshot);
        return true;
    }
}
