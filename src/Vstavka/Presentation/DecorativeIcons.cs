using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Vstavka.Presentation;

internal sealed class DecorativeImage : Image
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}

internal sealed class DecorativeGlyph : TextBlock
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
