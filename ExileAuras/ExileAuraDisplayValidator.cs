using System;
using System.Collections.Generic;
using System.Linq;

namespace ReAgent.ExileAuras;

internal static class ExileAuraDisplayValidator
{
    public static List<ExileAuraDisplayRuntime> CreateDisplayStates(ExileAuraRule rule, out string error)
    {
        error = "";
        var displays = (rule.Displays ?? [])
            .Select(display => new ExileAuraDisplayRuntime(display))
            .ToList();

        var validation = Validate(rule);
        if (!validation.Success)
        {
            error = validation.Error;
        }

        return displays;
    }

    public static ExileAuraValidationResult Validate(ExileAuraRule rule)
    {
        if ((rule.Displays ?? []).Any(display => string.IsNullOrWhiteSpace(display.Name)))
        {
            return ExileAuraValidationResult.Fail("Every display needs a name.");
        }

        var duplicateName = (rule.Displays ?? [])
            .Where(display => !string.IsNullOrWhiteSpace(display.Name))
            .GroupBy(display => display.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(duplicateName)
            ? ExileAuraValidationResult.Ok()
            : ExileAuraValidationResult.Fail($"Display name '{duplicateName}' is used more than once on this rule.");
    }
}

internal sealed record ExileAuraValidationResult(bool Success, string Error)
{
    public static ExileAuraValidationResult Ok() => new(true, "");
    public static ExileAuraValidationResult Fail(string error) => new(false, error);
}
