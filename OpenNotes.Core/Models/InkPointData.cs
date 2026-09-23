namespace Caelum.Models;

/// <summary>
/// UI-free stylus point: planar position plus the normalized pressure factor
/// (same convention as WPF <c>StylusPoint.PressureFactor</c>, ~0.0–1.0).
/// Keeping this a value type lets geometry code pass point lists without
/// copying and lets tests compare points by value.
/// </summary>
public readonly record struct InkPointData(double X, double Y, float Pressure = 0.5f);
