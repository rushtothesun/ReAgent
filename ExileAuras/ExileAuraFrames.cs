using System;
using System.Collections.Generic;
using System.Linq;

namespace ReAgent.ExileAuras;

internal static class ExileAuraFrames
{
    public const string None = "None";

    private static readonly ExileAuraFrameDefinition[] Definitions =
    [
        new("buff", "art/textures/interface/2d/2dart/uiimages/ingame/4k/buff.dds", new ExileAuraFrameLayout("buff.png", 132f, 132f, 0.625f, 0f, -4f)),
        new("charges", "art/textures/interface/2d/2dart/uiimages/ingame/4k/charges.dds", new ExileAuraFrameLayout("charges.png", 132f, 132f, 0.625f, 0f, -4f)),
        new("debuff", "art/textures/interface/2d/2dart/uiimages/ingame/4k/debuff.dds", new ExileAuraFrameLayout("debuff.png", 132f, 132f, 0.625f, 0f, -4f)),
        new("minionframe", "art/textures/interface/2d/2dart/uiimages/ingame/4k/minionframe.dds", new ExileAuraFrameLayout("minionframe.png", 136f, 132f, 0.625f, 0f, -4f)),
        new("nopausebuffframe", "art/textures/interface/2d/2dart/uiimages/ingame/4k/nopausebuffframe.dds", new ExileAuraFrameLayout("nopausebuffframe.png", 132f, 132f, 0.625f, 0f, -4f))
    ];

    public static readonly string[] Options = [None, .. Definitions.Select(frame => frame.Name)];

    public static readonly IReadOnlyCollection<AssetExtractionRequest> ExtractionRequests = Definitions
        .Select(frame => new AssetExtractionRequest(frame.DdsPath, frame.Layout.FileName, (int)frame.Layout.Width, (int)frame.Layout.Height))
        .ToList();

    public static bool TryGetLayout(string frameName, out ExileAuraFrameLayout layout)
    {
        var frame = Definitions.FirstOrDefault(definition => string.Equals(definition.Name, frameName, StringComparison.Ordinal));
        layout = frame?.Layout;
        return frame != null;
    }
}

internal sealed record ExileAuraFrameDefinition(string Name, string DdsPath, ExileAuraFrameLayout Layout);
