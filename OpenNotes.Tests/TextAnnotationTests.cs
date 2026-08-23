using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Threading;
using Caelum.Controls;
using Caelum.Models;

namespace Caelum.Tests;

[TestFixture]
public sealed class TextAnnotationTests
{
    [Test]
    public void LegacyTextAnnotation_UsesAutomaticDimensions()
    {
        var annotation = new TextAnnotation();

        Assert.That(annotation.Width, Is.EqualTo(0));
        Assert.That(annotation.Height, Is.EqualTo(0));
    }

    [Test]
    public void ResizeFromTopLeft_PreservesOppositeAnchorAndClampsMinimumSize()
    {
        var start = new TextBoxBounds(200, 100, 240, 120);

        var resized = TextAnnotationGeometry.Resize(
            start,
            TextResizeHandle.TopLeft,
            deltaX: 190,
            deltaY: 100);

        Assert.That(resized.X, Is.EqualTo(320).Within(0.001));
        Assert.That(resized.Y, Is.EqualTo(172).Within(0.001));
        Assert.That(resized.Width, Is.EqualTo(TextAnnotationGeometry.MinimumWidth).Within(0.001));
        Assert.That(resized.Height, Is.EqualTo(TextAnnotationGeometry.MinimumHeight).Within(0.001));
    }

    [Test]
    public void ResizeFromRightAndBottom_ChangesOnlyTheRequestedEdges()
    {
        var start = new TextBoxBounds(40, 60, 200, 80);

        var resized = TextAnnotationGeometry.Resize(
            start,
            TextResizeHandle.BottomRight,
            deltaX: 35,
            deltaY: 25);

        Assert.That(resized.X, Is.EqualTo(start.X));
        Assert.That(resized.Y, Is.EqualTo(start.Y));
        Assert.That(resized.Width, Is.EqualTo(235));
        Assert.That(resized.Height, Is.EqualTo(105));
    }

    [Test]
    public void ClampToPage_ConstrainsPositionAndSizeToMeasuredSurface()
    {
        var clamped = TextAnnotationGeometry.ClampToPage(
            new TextBoxBounds(760, 590, 260, 120),
            pageWidth: 900,
            pageHeight: 650);

        Assert.That(clamped.X, Is.EqualTo(760).Within(0.001));
        Assert.That(clamped.Y, Is.EqualTo(590).Within(0.001));
        Assert.That(clamped.Right, Is.EqualTo(900).Within(0.001));
        Assert.That(clamped.Bottom, Is.EqualTo(650).Within(0.001));
        Assert.That(clamped.Width, Is.GreaterThanOrEqualTo(TextAnnotationGeometry.MinimumWidth));
        Assert.That(clamped.Height, Is.GreaterThanOrEqualTo(TextAnnotationGeometry.MinimumHeight));
    }

    [TestCase(TextResizeHandle.TopLeft)]
    [TestCase(TextResizeHandle.Top)]
    [TestCase(TextResizeHandle.TopRight)]
    [TestCase(TextResizeHandle.Left)]
    [TestCase(TextResizeHandle.Right)]
    [TestCase(TextResizeHandle.BottomLeft)]
    [TestCase(TextResizeHandle.Bottom)]
    [TestCase(TextResizeHandle.BottomRight)]
    public void ResizeHandleAutomationId_IsStableAndDirectionSpecific(TextResizeHandle handle)
    {
        Assert.That(
            TextAnnotationGeometry.GetResizeHandleAutomationId(handle),
            Is.EqualTo($"TextResizeHandle.{handle}"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ResizeHandleBorder_ProvidesThumbAutomationPeer()
    {
        string? originalWindir = Environment.GetEnvironmentVariable("WINDIR");
        if (string.IsNullOrWhiteSpace(originalWindir))
        {
            string? systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (!string.IsNullOrWhiteSpace(systemRoot))
                Environment.SetEnvironmentVariable("WINDIR", systemRoot);
        }

        try
        {
            var handle = new TextResizeHandleBorder();
            const string automationId = "TextResizeHandle.BottomRight";
            AutomationProperties.SetAutomationId(handle, automationId);
            AutomationProperties.SetName(handle, "Resize text box");

            var peer = UIElementAutomationPeer.CreatePeerForElement(handle);

            Assert.That(peer, Is.Not.Null);
            Assert.That(peer!.GetAutomationControlType(), Is.EqualTo(AutomationControlType.Thumb));
            Assert.That(peer.GetAutomationId(), Is.EqualTo(automationId));
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(originalWindir))
                Environment.SetEnvironmentVariable("WINDIR", null);
        }
    }
}
