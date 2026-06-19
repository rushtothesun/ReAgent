using System;
using System.Collections.Generic;
using System.Linq;

namespace ReAgent.ReAgentAuras;

internal static class ReAgentAuraFrames
{
    public const string None = "None";

    private static readonly ReAgentAuraFrameDefinition[] Definitions =
    [
        new("buff", "art/textures/interface/2d/2dart/uiimages/ingame/4k/buff.dds", new ReAgentAuraFrameLayout("buff.png", 132f, 132f, 0.625f, 0f, -4f)),
        new("charges", "art/textures/interface/2d/2dart/uiimages/ingame/4k/charges.dds", new ReAgentAuraFrameLayout("charges.png", 132f, 132f, 0.625f, 0f, -4f)),
        new("debuff", "art/textures/interface/2d/2dart/uiimages/ingame/4k/debuff.dds", new ReAgentAuraFrameLayout("debuff.png", 132f, 132f, 0.625f, 0f, -4f)),
        new("minionframe", "art/textures/interface/2d/2dart/uiimages/ingame/4k/minionframe.dds", new ReAgentAuraFrameLayout("minionframe.png", 136f, 132f, 0.625f, 0f, -4f)),
        new("nopausebuffframe", "art/textures/interface/2d/2dart/uiimages/ingame/4k/nopausebuffframe.dds", new ReAgentAuraFrameLayout("nopausebuffframe.png", 132f, 132f, 0.625f, 0f, -4f))
    ];

    public static readonly string[] Options = [None, .. Definitions.Select(frame => frame.Name)];

    public static readonly IReadOnlyCollection<AssetExtractionRequest> ExtractionRequests = Definitions
        .Select(frame => new AssetExtractionRequest(frame.DdsPath, frame.Layout.FileName, (int)frame.Layout.Width, (int)frame.Layout.Height))
        .ToList();

    public static bool TryGetLayout(string frameName, out ReAgentAuraFrameLayout layout)
    {
        var frame = Definitions.FirstOrDefault(definition => string.Equals(definition.Name, frameName, StringComparison.Ordinal));
        layout = frame?.Layout;
        return frame != null;
    }
}

internal sealed record ReAgentAuraFrameDefinition(string Name, string DdsPath, ReAgentAuraFrameLayout Layout);
