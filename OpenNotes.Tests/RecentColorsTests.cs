using System.Collections.Generic;
using Caelum.Models;
using Caelum.Services;

namespace Caelum.Tests;

/// <summary>
/// Task-14 parity tests for <see cref="RecentColors"/> — the Core home of
/// the WPF <c>RecordRecentColor</c>/<c>TryParseRecentColor</c> helpers that
/// both shells' tool flyouts (pen/highlighter/text) share for G4. The list
/// lives on a transient <see cref="AppSettings"/> clone; persistence is the
/// caller's job, so these tests only pin list mutation + hex parsing.
/// </summary>
[TestFixture]
public sealed class RecentColorsTests
{
    [Test]
    public void Record_InsertsNewestFirst()
    {
        var list = new List<string> { "#0000FF" };
        RecentColors.Record(list, "#FF0000");

        Assert.That(list, Is.EqualTo(new[] { "#FF0000", "#0000FF" }));
    }

    [Test]
    public void Record_DedupesCaseInsensitively()
    {
        var list = new List<string> { "#112233", "#FF0000", "#445566" };
        RecentColors.Record(list, "#ff0000");

        Assert.That(list, Is.EqualTo(new[] { "#ff0000", "#112233", "#445566" }),
            "the existing entry moves to the front in its newly-recorded casing");
    }

    [Test]
    public void Record_CapsAtMaxRecentColors()
    {
        var list = new List<string>();
        for (int i = 0; i < RecentColors.MaxRecentColors + 4; i++)
            RecentColors.Record(list, $"#{i:X2}{i:X2}{i:X2}");

        Assert.That(list.Count, Is.EqualTo(RecentColors.MaxRecentColors));
        Assert.That(list[0], Is.EqualTo($"#{(RecentColors.MaxRecentColors + 3):X2}{(RecentColors.MaxRecentColors + 3):X2}{(RecentColors.MaxRecentColors + 3):X2}"),
            "newest entry stays at the front");
        Assert.That(list, Does.Not.Contain("#000000"),
            "oldest entries are trimmed off the tail");
    }

    [Test]
    public void Record_IsNullAndBlankSafe()
    {
        Assert.DoesNotThrow(() => RecentColors.Record(null, "#FF0000"));

        var list = new List<string> { "#0000FF" };
        RecentColors.Record(list, "");
        RecentColors.Record(list, "   ");
        Assert.That(list, Is.EqualTo(new[] { "#0000FF" }));
    }

    [Test]
    public void TryParse_ReadsRecordedRRGGBB()
    {
        Assert.That(RecentColors.TryParse("#1A2B3C", out byte a, out byte r, out byte g, out byte b), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo((byte)255), "6-digit entries default to opaque");
            Assert.That(r, Is.EqualTo((byte)0x1A));
            Assert.That(g, Is.EqualTo((byte)0x2B));
            Assert.That(b, Is.EqualTo((byte)0x3C));
        });
    }

    [Test]
    public void TryParse_ToleratesHandEditedAARRGGBB()
    {
        Assert.That(RecentColors.TryParse("#80112A3F", out byte a, out byte r, out byte g, out byte b), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo((byte)0x80));
            Assert.That(r, Is.EqualTo((byte)0x11));
            Assert.That(g, Is.EqualTo((byte)0x2A));
            Assert.That(b, Is.EqualTo((byte)0x3F));
        });
    }

    [Test]
    public void TryParse_RejectsMalformedEntriesWithoutThrowing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RecentColors.TryParse(null, out _, out _, out _, out _), Is.False);
            Assert.That(RecentColors.TryParse("", out _, out _, out _, out _), Is.False);
            Assert.That(RecentColors.TryParse("#12345", out _, out _, out _, out _), Is.False);
            Assert.That(RecentColors.TryParse("#1234567", out _, out _, out _, out _), Is.False);
            Assert.That(RecentColors.TryParse("#GGHHII", out _, out _, out _, out _), Is.False);
            Assert.That(RecentColors.TryParse("red", out _, out _, out _, out _), Is.False);
        });
    }

    [Test]
    public void AppSettings_ExposesTheThreeRecentColorLists()
    {
        var settings = new AppSettings();
        Assert.Multiple(() =>
        {
            Assert.That(settings.RecentPenColors, Is.Not.Null.And.Empty);
            Assert.That(settings.RecentHighlighterColors, Is.Not.Null.And.Empty);
            Assert.That(settings.RecentTextColors, Is.Not.Null.And.Empty);
        });
    }
}
