using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Caelum.Controls;

/// <summary>
/// Border-based text resize handle with an explicit UI Automation peer.
/// WPF's plain Border has no default peer, so AutomationProperties alone
/// does not make a code-created handle discoverable to desktop UIA clients.
/// </summary>
public sealed class TextResizeHandleBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new TextResizeHandleAutomationPeer(this);
    }
}

internal sealed class TextResizeHandleAutomationPeer : FrameworkElementAutomationPeer
{
    public TextResizeHandleAutomationPeer(TextResizeHandleBorder owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Thumb;
    }

    protected override string GetClassNameCore()
    {
        return nameof(TextResizeHandleBorder);
    }

    protected override string GetNameCore()
    {
        return AutomationProperties.GetName((TextResizeHandleBorder)Owner);
    }
}
