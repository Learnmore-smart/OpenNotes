using System;

namespace Caelum.Models;

/// <summary>
/// Logical-shape identity shared by all ink parts of one recognized or
/// shape-tool figure. Empty group/kind means ordinary legacy ink.
/// </summary>
public readonly record struct ShapeStrokeIdentity(
    string GroupId,
    string Kind,
    int PartIndex,
    bool IsDashed);

/// <summary>
/// Stable property keys the WPF adapter uses to attach a
/// <see cref="ShapeStrokeIdentity"/> to a live <c>System.Windows.Ink.Stroke</c>
/// via its extended-property bag. Core models store the same fields directly
/// (<see cref="InkStrokeData"/>); the keys must never change so old property
/// data and persisted sidecars keep resolving.
/// </summary>
internal static class ShapeStrokeMetadataKeys
{
    public static readonly Guid GroupId = new("767C2E92-6A10-4D55-9B79-5DCA69089B28");
    public static readonly Guid Kind = new("4F97F528-AB40-4091-8899-326019220E6F");
    public static readonly Guid PartIndex = new("3A1127F7-2EA3-48D8-A5E4-B376AEAC2C87");
    public static readonly Guid IsDashed = new("D3F93EA9-D2D1-476A-BF14-9F267205983A");
}
