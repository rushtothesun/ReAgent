using System;
using System.Linq;
using ReAgent.State;

namespace ReAgent.ExileAuras;

internal static class ExileAuraDisplayText
{
    public static string BuildDefaultText(ExileAuraDisplay display, ExileAuraRule rule, RuleState state)
    {
        if (string.IsNullOrWhiteSpace(rule.SourceName))
        {
            return "";
        }

        var rows = state.Buffs.AllBuffs
            .Where(buff => string.Equals(buff.Name, rule.SourceName, StringComparison.Ordinal))
            .ToList();

        if (rows.Count == 0)
        {
            return "";
        }

        return display.Effect switch
        {
            ExileAuraDisplayEffect.ShowTimer => FormatTimer(rows.Select(x => (float)x.TimeLeft).Where(IsFiniteTimer).DefaultIfEmpty(float.PositiveInfinity).Min()),
            ExileAuraDisplayEffect.ShowCharges => rows.Max(x => x.Charges).ToString(),
            ExileAuraDisplayEffect.ShowInstanceCount => rows.Count.ToString(),
            ExileAuraDisplayEffect.ShowStack => rows.Max(x => x.Stacks).ToString(),
            _ => ""
        };
    }

    private static bool IsFiniteTimer(float timer)
    {
        return timer > 0f && timer < 9999f && !float.IsInfinity(timer) && !float.IsNaN(timer);
    }

    private static string FormatTimer(float timer)
    {
        return IsFiniteTimer(timer) ? $"{timer:0.0}s" : "";
    }
}
