using System;
using System.Linq;

namespace ReAgent.ExileAuras;

public sealed partial class ExileAurasModule
{
    private static string CreateTextureKey(ExileAuraRule rule)
    {
        return $"exileauras_icon_{rule.Id}";
    }

    private static string CreateFrameTextureKey(string frameName)
    {
        return $"exileauras_frame_{frameName}";
    }

    private static string Initials(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        var initials = string.Concat(text
            .Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(x => char.ToUpperInvariant(x[0])));
        return string.IsNullOrWhiteSpace(initials) ? "?" : initials;
    }
}
