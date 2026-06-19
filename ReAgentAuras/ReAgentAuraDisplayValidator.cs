using System;
using System.Collections.Generic;
using System.Linq;

namespace ReAgent.ReAgentAuras;

internal static class ReAgentAuraDisplayValidator
{
    public static List<ReAgentAuraDisplayRuntime> CreateDisplayStates(ReAgentAuraRule rule, out string error)
    {
        error = "";
        var displays = (rule.Displays ?? [])
            .Select(display => new ReAgentAuraDisplayRuntime(display))
            .ToList();

        var validation = Validate(rule);
        if (!validation.Success)
        {
            error = validation.Error;
        }

        return displays;
    }

    public static ReAgentAuraValidationResult Validate(ReAgentAuraRule rule)
    {
        if ((rule.Displays ?? []).Any(display => string.IsNullOrWhiteSpace(display.Name)))
        {
            return ReAgentAuraValidationResult.Fail("Every display needs a name.");
        }

        var duplicateName = (rule.Displays ?? [])
            .Where(display => !string.IsNullOrWhiteSpace(display.Name))
            .GroupBy(display => display.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(duplicateName)
            ? ReAgentAuraValidationResult.Ok()
            : ReAgentAuraValidationResult.Fail($"Display name '{duplicateName}' is used more than once on this rule.");
    }
}

internal sealed record ReAgentAuraValidationResult(bool Success, string Error)
{
    public static ReAgentAuraValidationResult Ok() => new(true, "");
    public static ReAgentAuraValidationResult Fail(string error) => new(false, error);
}
