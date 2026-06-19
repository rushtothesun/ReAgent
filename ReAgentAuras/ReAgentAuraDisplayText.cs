using System;
using System.Linq;
using ReAgent.State;

namespace ReAgent.ReAgentAuras;

internal static class ReAgentAuraDisplayText
{
    public static string BuildDefaultText(ReAgentAuraDisplay display, ReAgentAuraRule rule, RuleState state)
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
            ReAgentAuraDisplayEffect.ShowTimer => FormatTimer(rows.Select(x => (float)x.TimeLeft).Where(IsFiniteTimer).DefaultIfEmpty(float.PositiveInfinity).Min()),
            ReAgentAuraDisplayEffect.ShowCharges => rows.Max(x => x.Charges).ToString(),
            ReAgentAuraDisplayEffect.ShowInstanceCount => rows.Count.ToString(),
            ReAgentAuraDisplayEffect.ShowStack => rows.Max(x => x.Stacks).ToString(),
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
