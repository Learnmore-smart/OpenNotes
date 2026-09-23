using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Caelum.Controls
{
    // Panels and Borders do not create AutomationPeers in WinUI 3, so any
    // AutomationId placed on them is invisible to UIA clients (unlike WPF,
    // where every element surfaces). The WPF editor shell exposes container
    // AutomationIds that the smoke harness searches for — PagesContainer,
    // Editor.PageJumpGroup, DocumentSidebar, PdfSearchPanel — so these thin
    // subclasses surface a FrameworkElementAutomationPeer without changing
    // layout or rendering behavior.

    /// <summary>StackPanel that exposes itself to UI Automation.</summary>
    public class UiaStackPanel : StackPanel
    {
        protected override AutomationPeer OnCreateAutomationPeer()
            => new FrameworkElementAutomationPeer(this);
    }

    /// <summary>Grid that exposes itself to UI Automation.</summary>
    public class UiaGrid : Grid
    {
        protected override AutomationPeer OnCreateAutomationPeer()
            => new FrameworkElementAutomationPeer(this);
    }

    /// <summary>
    /// ContentControl that exposes itself to UI Automation. Border is sealed
    /// in WinUI 3, so border-styled containers that need a UIA presence wrap
    /// their Border child in this host instead.
    /// </summary>
    public class UiaContentControl : ContentControl
    {
        protected override AutomationPeer OnCreateAutomationPeer()
            => new FrameworkElementAutomationPeer(this);
    }
}
