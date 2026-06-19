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
            ExileAuraDisplayEffect.ShowTimer => ExileAurasModule.FormatTimerForDisplay(rows.Select(x => (float)x.TimeLeft).Where(ExileAurasModule.IsFiniteTimerForDisplay).DefaultIfEmpty(float.PositiveInfinity).Min()),
            ExileAuraDisplayEffect.ShowCharges => rows.Max(x => x.Charges).ToString(),
            ExileAuraDisplayEffect.ShowInstanceCount => rows.Count.ToString(),
            ExileAuraDisplayEffect.ShowStack => rows.Max(x => x.Stacks).ToString(),
            _ => ""
        };
    }
}
