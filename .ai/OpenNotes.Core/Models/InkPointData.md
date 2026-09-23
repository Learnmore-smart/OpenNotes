# OpenNotes.Core/Models/InkPointData.cs
> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

UI-free stylus point for the V6 WinUI 3 migration (Task 2): planar X/Y in page DIP coordinates plus a normalized `Pressure` factor using the same convention as WPF `StylusPoint.PressureFactor` (~0.0–1.0, default 0.5).

## API

- `readonly record struct InkPointData(double X, double Y, float Pressure = 0.5f)` — positional record struct so point lists compare by value and pass without copying.

## Constraints

- Page DIP coordinates, origin top-left — identical convention to `StrokeAnnotation.Points` and WPF `StylusPointCollection`.
- No WPF/WinUI/WinForms dependency; must stay usable from `net8.0` `OpenNotes.Core`.

## Verification

`OpenNotes.Tests/CoreStrokeGeometryTests.cs` exercises the type through the `StrokeGeometry` math (hit-test, split, bounds, smoothing, ruler).
