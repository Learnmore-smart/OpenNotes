using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Caelum.Controls;

/// <summary>
/// Border-based text annotation move handle with a stable UI Automation peer.
/// The visual and pointer event surface remain the same as the existing handle.
/// </summary>
public sealed class TextAnnotationDragHandleBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new TextAnnotationDragHandleAutomationPeer(this);
    }
}

internal sealed class TextAnnotationDragHandleAutomationPeer : FrameworkElementAutomationPeer
{
    public TextAnnotationDragHandleAutomationPeer(TextAnnotationDragHandleBorder owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Thumb;
    }

    protected override string GetClassNameCore()
    {
        return nameof(TextAnnotationDragHandleBorder);
    }

    protected override string GetNameCore()
    {
        return AutomationProperties.GetName((TextAnnotationDragHandleBorder)Owner);
    }
}
