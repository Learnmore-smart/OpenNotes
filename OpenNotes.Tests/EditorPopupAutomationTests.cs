using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Shape;
using Caelum.Models;
using Caelum.Pages;
using Caelum.Services;
using NUnit.Framework;

namespace Caelum.Tests;

[TestFixture]
public sealed class EditorPopupAutomationTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void DynamicEditorPopupsExposeLocalizedPeersAndSemanticToggleState()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        if (application.Resources["SleekScrollViewer"] == null)
            application.Resources["SleekScrollViewer"] = new Style(typeof(ScrollViewer));
        if (application.Resources["CompactComboBox"] == null)
            application.Resources["CompactComboBox"] = new Style(typeof(ComboBox));
        if (application.Resources["ToolbarFocusVisualStyle"] == null)
            application.Resources["ToolbarFocusVisualStyle"] = new Style(typeof(Control));

        var previousLanguage = LocalizationService.CurrentLanguage;
        try
        {
            LocalizationService.ApplyLanguage(AppLanguage.French);
            var editor = new EditorPage();

            var highlighterPopup = GetField<Popup>(editor, "_highlighterPopup");
            var sizeSlider = FindByAutomationId<Slider>(highlighterPopup.Child, "Editor.Highlighter.Size");
            AssertPeerMetadata(sizeSlider, "Editor.Highlighter.Size", "Taille");
            Assert.That(sizeSlider.MinHeight, Is.GreaterThanOrEqualTo(32));
            Assert.That(sizeSlider.FocusVisualStyle, Is.Not.Null);

            foreach (var id in new[]
            {
                "Editor.Highlighter.Freehand", "Editor.Highlighter.Text",
                "Editor.Highlighter.Underline", "Editor.Highlighter.StrikeOut",
                "Editor.Highlighter.Squiggly", "Editor.Highlighter.Area"
            })
            {
                var mode = FindByAutomationId<ToggleButton>(highlighterPopup.Child, id);
                AssertPeerMetadata(mode, id, null);
                Assert.That(mode.MinWidth, Is.GreaterThanOrEqualTo(32), id);
                Assert.That(mode.MinHeight, Is.GreaterThanOrEqualTo(32), id);
                Assert.That(GetToggleState(mode), Is.EqualTo(ToggleState.Off).Or.EqualTo(ToggleState.On), id);
            }

            var selectionPopup = GetField<Popup>(editor, "_selectionPopup");
            var rectangle = FindByAutomationId<ToggleButton>(selectionPopup.Child, "Editor.Select.Shape.Rectangle");
            var both = FindByAutomationId<ToggleButton>(selectionPopup.Child, "Editor.Select.Filter.Both");
            AssertPeerMetadata(rectangle, "Editor.Select.Shape.Rectangle", null);
            AssertPeerMetadata(both, "Editor.Select.Filter.Both", null);
            Assert.That(GetToggleState(rectangle), Is.EqualTo(ToggleState.On));
            Assert.That(GetToggleState(both), Is.EqualTo(ToggleState.On));
            Assert.That(rectangle.MinWidth, Is.GreaterThanOrEqualTo(32));
            Assert.That(rectangle.MinHeight, Is.GreaterThanOrEqualTo(32));
            Assert.That(both.MinWidth, Is.GreaterThanOrEqualTo(32));
            Assert.That(both.MinHeight, Is.GreaterThanOrEqualTo(32));

            var togglePeer = UIElementAutomationPeer.CreatePeerForElement(both);
            Assert.That(togglePeer?.GetPattern(PatternInterface.Toggle), Is.Not.Null);
            ((IToggleProvider)togglePeer!.GetPattern(PatternInterface.Toggle)!).Toggle();
            Assert.That(GetToggleState(both), Is.EqualTo(ToggleState.On),
                "Mutually-exclusive selection options must remain selected after keyboard/UIA activation.");
        }
        finally
        {
            LocalizationService.ApplyLanguage(previousLanguage);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ToolbarStateColorsFollowLiveThemeResourceChanges()
    {
        EnsureWpfEnvironment();
        var application = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        if (application.Resources["SleekScrollViewer"] == null)
            application.Resources["SleekScrollViewer"] = new Style(typeof(ScrollViewer));
        if (application.Resources["CompactComboBox"] == null)
            application.Resources["CompactComboBox"] = new Style(typeof(ComboBox));
        if (application.Resources["ToolbarFocusVisualStyle"] == null)
            application.Resources["ToolbarFocusVisualStyle"] = new Style(typeof(Control));

        var themeKeys = new[]
        {
            "ThemeAccentBrush", "ThemeForegroundBrush", "ThemeSurfaceAltBrush", "ThemeBorderBrush"
        };
        var originalResources = themeKeys.ToDictionary(
            key => key,
            key => (Exists: application.Resources.Contains(key),
                Value: application.Resources.Contains(key) ? application.Resources[key] : null));
        var previousLanguage = LocalizationService.CurrentLanguage;
        try
        {
            var accent = new SolidColorBrush(Colors.Magenta);
            var foreground = new SolidColorBrush(Colors.LimeGreen);
            var surfaceAlt = new SolidColorBrush(Colors.DarkSlateGray);
            var border = new SolidColorBrush(Colors.Orange);
            application.Resources["ThemeAccentBrush"] = accent;
            application.Resources["ThemeForegroundBrush"] = foreground;
            application.Resources["ThemeSurfaceAltBrush"] = surfaceAlt;
            application.Resources["ThemeBorderBrush"] = border;

            LocalizationService.ApplyLanguage(AppLanguage.English);
            var editor = new EditorPage();
            var toolbar = GetField<Border>(editor, "_inlineTextBoxToolbar");
            var rulerIcon = GetField<ShapePath>(editor, "RulerIcon");
            var setRulerVisible = typeof(EditorPage).GetMethod(
                "SetRulerVisible", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setRulerVisible, Is.Not.Null);
            setRulerVisible!.Invoke(editor, new object[] { true });
            Assert.That(rulerIcon.Stroke, Is.SameAs(accent));
            Assert.That(DependencyPropertyHelper.GetValueSource(
                rulerIcon, ShapePath.StrokeProperty).IsExpression, Is.True);

            setRulerVisible.Invoke(editor, new object[] { false });
            Assert.That(rulerIcon.Stroke, Is.SameAs(foreground));

            var colorIndicator = GetField<Border>(editor, "_colorIndicator");
            Assert.That(colorIndicator.BorderBrush, Is.SameAs(border));
            Assert.That(DependencyPropertyHelper.GetValueSource(
                colorIndicator, Border.BorderBrushProperty).IsExpression, Is.True);
            Assert.That(colorIndicator.Background, Is.TypeOf<SolidColorBrush>());
            Assert.That(((SolidColorBrush)colorIndicator.Background).Color, Is.EqualTo(Colors.Black));

            var fontGroup = Descendants(toolbar)
                .OfType<Border>()
                .FirstOrDefault(candidate =>
                    Math.Abs(candidate.CornerRadius.TopLeft - 10) < 0.001 &&
                    Math.Abs(candidate.Padding.Left - 2) < 0.001 &&
                    Math.Abs(candidate.Padding.Right - 2) < 0.001);
            Assert.That(fontGroup, Is.Not.Null);
            Assert.That(fontGroup!.Background, Is.SameAs(surfaceAlt));
            Assert.That(DependencyPropertyHelper.GetValueSource(
                fontGroup, Border.BackgroundProperty).IsExpression, Is.True);
        }
        finally
        {
            LocalizationService.ApplyLanguage(previousLanguage);
            foreach (var pair in originalResources)
            {
                if (pair.Value.Exists)
                    application.Resources[pair.Key] = pair.Value.Value;
                else
                    application.Resources.Remove(pair.Key);
            }
        }
    }

    private static ToggleState GetToggleState(ToggleButton button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        Assert.That(peer, Is.Not.Null);
        var provider = peer!.GetPattern(PatternInterface.Toggle) as IToggleProvider;
        Assert.That(provider, Is.Not.Null);
        return provider!.ToggleState;
    }

    private static void AssertPeerMetadata(FrameworkElement element, string expectedId, string? expectedName)
    {
        Assert.That(element, Is.Not.Null, expectedId);
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        Assert.That(peer, Is.Not.Null, expectedId);
        Assert.That(peer!.GetAutomationId(), Is.EqualTo(expectedId));
        Assert.That(peer.GetName(), Is.Not.Null.And.Not.Empty);
        Assert.That(peer.GetHelpText(), Is.Not.Null.And.Not.Empty);
        if (!string.IsNullOrWhiteSpace(expectedName))
            Assert.That(peer.GetName(), Does.Contain(expectedName));
    }

    private static T FindByAutomationId<T>(DependencyObject root, string id)
        where T : FrameworkElement
    {
        foreach (var element in Descendants(root))
        {
            if (element is T typed && AutomationProperties.GetAutomationId(typed) == id)
                return typed;
        }

        throw new AssertionException($"Dynamic popup control '{id}' was not found.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root == null)
            yield break;

        yield return root;
        int visualChildren = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < visualChildren; index++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }

        // Popup ScrollViewers may not have a realized visual template until
        // opened. Follow logical content as well so automation metadata stays
        // testable without requiring a foreground desktop window.
        if (visualChildren == 0 && root is ContentControl contentControl && contentControl.Content is DependencyObject content)
        {
            foreach (var child in Descendants(content))
                yield return child;
        }
    }

    private static T GetField<T>(EditorPage editor, string name)
        where T : class
    {
        return typeof(EditorPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) as T
            ?? throw new AssertionException($"EditorPage field '{name}' was not initialized.");
    }

    private static void EnsureWpfEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            Environment.SetEnvironmentVariable("WINDIR", Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows");
    }
}
