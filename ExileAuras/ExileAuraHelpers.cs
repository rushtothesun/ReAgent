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

    private static bool IsFiniteTimer(float timer)
    {
        return timer > 0f && timer < 9999f && !float.IsInfinity(timer) && !float.IsNaN(timer);
    }

    private static string FormatTimer(float timer)
    {
        return IsFiniteTimer(timer) ? $"{timer:0.0}s" : "";
    }

    internal static bool IsFiniteTimerForDisplay(float timer)
    {
        return IsFiniteTimer(timer);
    }

    internal static string FormatTimerForDisplay(float timer)
    {
        return FormatTimer(timer);
    }
}
