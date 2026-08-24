using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Caelum.Models;

/// <summary>
/// Identifies which side of a recognized-stroke replacement is currently in
/// the page collection. Tokens are session-only and never serialized.
/// </summary>
public enum StrokeReplacementSide
{
    Original,
    Ideal
}

/// <summary>
/// A value object for one copied stylus point. Keeping this type independent
/// of WPF makes the replacement contract deterministic in unit tests.
/// </summary>
public readonly record struct StrokeReplacementPoint(double X, double Y, float PressureFactor = 0.5f);

/// <summary>
/// Immutable, WPF-independent stroke payload used by shape replacement undo.
/// It intentionally contains no live <c>System.Windows.Ink.Stroke</c>.
/// </summary>
public sealed class StrokeReplacementSnapshot : IEquatable<StrokeReplacementSnapshot>
{
    private readonly IReadOnlyList<StrokeReplacementPoint> _points;

    public StrokeReplacementSnapshot(
        Guid token,
        StrokeReplacementSide side,
        IEnumerable<StrokeReplacementPoint> points,
        byte r,
        byte g,
        byte b,
        byte a,
        double width,
        double height,
        bool isHighlighter,
        bool fitToCurve,
        bool ignorePressure = true)
    {
        if (token == Guid.Empty)
            throw new ArgumentException("A replacement snapshot requires a non-empty token.", nameof(token));

        Token = token;
        Side = side;
        _points = new ReadOnlyCollection<StrokeReplacementPoint>(
            (points ?? throw new ArgumentNullException(nameof(points))).ToList());
        R = r;
        G = g;
        B = b;
        A = a;
        Width = width;
        Height = height;
        IsHighlighter = isHighlighter;
        FitToCurve = fitToCurve;
        IgnorePressure = ignorePressure;
    }

    public Guid Token { get; }
    public StrokeReplacementSide Side { get; }
    public IReadOnlyList<StrokeReplacementPoint> Points => _points;
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }
    public double Width { get; }
    public double Height { get; }
    public bool IsHighlighter { get; }
    public bool FitToCurve { get; }
    public bool IgnorePressure { get; }

    public StrokeReplacementSnapshot WithSide(StrokeReplacementSide side)
    {
        return new StrokeReplacementSnapshot(
            Token,
            side,
            Points,
            R,
            G,
            B,
            A,
            Width,
            Height,
            IsHighlighter,
            FitToCurve,
            IgnorePressure);
    }

    public StrokeReplacementSnapshot WithIgnorePressure(bool ignorePressure)
    {
        return new StrokeReplacementSnapshot(
            Token,
            Side,
            Points,
            R,
            G,
            B,
            A,
            Width,
            Height,
            IsHighlighter,
            FitToCurve,
            ignorePressure);
    }

    public bool Equals(StrokeReplacementSnapshot other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || Token != other.Token
            || Side != other.Side
            || R != other.R
            || G != other.G
            || B != other.B
            || A != other.A
            || Width != other.Width
            || Height != other.Height
            || IsHighlighter != other.IsHighlighter
            || FitToCurve != other.FitToCurve
            || IgnorePressure != other.IgnorePressure
            || Points.Count != other.Points.Count)
        {
            return false;
        }

        return Points.SequenceEqual(other.Points);
    }

    public override bool Equals(object obj) => Equals(obj as StrokeReplacementSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Token);
        hash.Add(Side);
        hash.Add(R);
        hash.Add(G);
        hash.Add(B);
        hash.Add(A);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(IsHighlighter);
        hash.Add(FitToCurve);
        hash.Add(IgnorePressure);
        foreach (var point in Points)
        {
            hash.Add(point.X);
            hash.Add(point.Y);
            hash.Add(point.PressureFactor);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// A tokenized immutable entry in the deterministic replacement ledger.
/// </summary>
public sealed class StrokeReplacementEntry
{
    public StrokeReplacementEntry(StrokeReplacementSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public Guid Token => Snapshot.Token;
    public StrokeReplacementSide Side => Snapshot.Side;
    public StrokeReplacementSnapshot Snapshot { get; }
}

/// <summary>
/// Pure token/index replacement semantics shared by tests and the page-level
/// quiet operation. A failed lookup is always a no-op and never appends.
/// </summary>
public sealed class StrokeReplacementState
{
    private readonly List<StrokeReplacementEntry> _strokes;

    public StrokeReplacementState(IEnumerable<StrokeReplacementEntry> strokes)
    {
        _strokes = (strokes ?? throw new ArgumentNullException(nameof(strokes))).ToList();
        if (_strokes.Any(entry => entry == null))
            throw new ArgumentException("Stroke entries cannot be null.", nameof(strokes));
        if (_strokes.GroupBy(entry => entry.Token).Any(group => group.Count() > 1))
            throw new ArgumentException("Stroke replacement tokens must be unique.", nameof(strokes));
    }

    public IReadOnlyList<StrokeReplacementEntry> Strokes =>
        new ReadOnlyCollection<StrokeReplacementEntry>(_strokes);

    public int FindIndex(Guid token)
    {
        return token == Guid.Empty
            ? -1
            : _strokes.FindIndex(entry => entry.Token == token);
    }

    public void InsertAt(int index, StrokeReplacementEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));
        if (_strokes.Any(existing => existing.Token == entry.Token))
            throw new ArgumentException("Stroke replacement tokens must be unique.", nameof(entry));

        _strokes.Insert(Math.Max(0, Math.Min(index, _strokes.Count)), entry);
    }

    public bool RemoveAt(int index, out StrokeReplacementEntry removed)
    {
        removed = null;
        if (index < 0 || index >= _strokes.Count)
            return false;

        removed = _strokes[index];
        _strokes.RemoveAt(index);
        return true;
    }

    public bool ReplaceAt(int index, StrokeReplacementEntry entry)
    {
        if (entry == null || index < 0 || index >= _strokes.Count)
            return false;
        if (_strokes.Where((existing, existingIndex) => existingIndex != index)
            .Any(existing => existing.Token == entry.Token))
            return false;

        _strokes[index] = entry;
        return true;
    }

    public bool TryReplaceStrokeQuiet(
        Guid token,
        StrokeReplacementSide expectedSide,
        StrokeReplacementSnapshot replacement,
        out int index)
    {
        index = -1;
        if (token == Guid.Empty || replacement == null || replacement.Token != token)
            return false;

        index = FindIndex(token);
        if (index < 0 || _strokes[index].Side != expectedSide)
        {
            index = -1;
            return false;
        }

        _strokes[index] = new StrokeReplacementEntry(replacement);
        return true;
    }

    public bool RemoveToken(Guid token)
    {
        int index = _strokes.FindIndex(entry => entry.Token == token);
        if (index < 0)
            return false;

        _strokes.RemoveAt(index);
        return true;
    }
}
