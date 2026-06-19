using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Nodes;

namespace ReAgent.ReAgentAuras;

public sealed class ReAgentAurasSettings
{
    [Menu("Unlocked")]
    public ToggleNode Unlocked { get; set; } = new(true);

    [Menu("Poll Interval Ms")]
    public RangeNode<int> PollIntervalMs { get; set; } = new(100, 25, 1000);

    [Menu("Enable Extraction")]
    public ToggleNode EnableExtraction { get; set; } = new(true);

    public TextNode ContentGgpkPath { get; set; } = new("");
}
