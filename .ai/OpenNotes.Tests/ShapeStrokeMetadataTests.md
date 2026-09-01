# OpenNotes.Tests/ShapeStrokeMetadataTests.cs
> Last updated: 2026-08-30 | Protection: STANDARD

## Purpose

Guards real-gap dashed geometry and WPF stroke logical-shape metadata round-trips.

## Change History

- 2026-08-31: Added polyline phase-continuity coverage so dashed closed shapes do not restart the dash pattern at every corner.
