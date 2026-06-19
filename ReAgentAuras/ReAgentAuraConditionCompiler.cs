using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore2;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using ReAgent.State;

namespace ReAgent.ReAgentAuras;

internal sealed class ReAgentAuraConditionCompiler
{
    private delegate T ScriptFunc<T>(RuleState State, Func<string, ReAgentAuraDisplayRuntime> Display);

    private readonly Dictionary<string, CachedCondition> _conditions = new(StringComparer.Ordinal);

    public ReAgentAuraEvaluation Evaluate(ReAgentAuraRule rule, RuleState state)
    {
        var displays = ReAgentAuraDisplayValidator.CreateDisplayStates(rule, out var displayError);
        if (!string.IsNullOrWhiteSpace(displayError))
        {
            return new ReAgentAuraEvaluation(false, displayError, displays);
        }

        foreach (var display in displays)
        {
            display.Value = ReAgentAuraDisplayText.BuildDefaultText(display.Display, rule, state);
        }

        if (string.IsNullOrWhiteSpace(rule.ConditionSource))
        {
            return new ReAgentAuraEvaluation(false, "", displays);
        }

        var condition = GetCompiledCondition(rule);
        if (!string.IsNullOrWhiteSpace(condition.Error))
        {
            return new ReAgentAuraEvaluation(false, condition.Error, displays);
        }

        try
        {
            var lookup = displays
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            ReAgentAuraDisplayRuntime Display(string name)
            {
                if (lookup.TryGetValue(name, out var display))
                {
                    return display;
                }

                throw new KeyNotFoundException($"Display '{name}' was not found on this rule.");
            }

            return new ReAgentAuraEvaluation(condition.Func?.Invoke(state, Display) == true, "", displays);
        }
        catch (Exception ex)
        {
            return new ReAgentAuraEvaluation(false, $"Exception while evaluating: {ex.Message}", displays);
        }
    }

    public ReAgentAuraValidationResult Validate(ReAgentAuraRule rule)
    {
        var displayValidation = ReAgentAuraDisplayValidator.Validate(rule);
        if (!displayValidation.Success)
        {
            return displayValidation;
        }

        if (string.IsNullOrWhiteSpace(rule.ConditionSource))
        {
            return ReAgentAuraValidationResult.Ok();
        }

        var condition = GetCompiledCondition(rule);
        return string.IsNullOrWhiteSpace(condition.Error)
            ? ReAgentAuraValidationResult.Ok()
            : ReAgentAuraValidationResult.Fail(condition.Error);
    }

    private CompiledCondition GetCompiledCondition(ReAgentAuraRule rule)
    {
        var source = rule.ConditionSource ?? string.Empty;
        if (_conditions.TryGetValue(rule.Id, out var cached) &&
            string.Equals(cached.Source, source, StringComparison.Ordinal))
        {
            return cached.Condition.Value;
        }

        var next = new CachedCondition(source, new Lazy<CompiledCondition>(() => Compile(source)));
        _conditions[rule.Id] = next;
        return next.Condition.Value;
    }

    private static CompiledCondition Compile(string source)
    {
        try
        {
            var func = DelegateCompiler.CompileDelegate<ScriptFunc<bool>>(
                source,
                ScriptCompilerSupport.ScriptOptions,
                ScriptCompilerSupport.CreateAssemblyLoadContext("ReAgentAura"));
            return new CompiledCondition((state, display) => func(state, display), "");
        }
        catch (Exception ex)
        {
            return new CompiledCondition(null, $"Expression compilation failed: {ex.Message}");
        }
    }

    private sealed record CachedCondition(string Source, Lazy<CompiledCondition> Condition);
    private sealed record CompiledCondition(Func<RuleState, Func<string, ReAgentAuraDisplayRuntime>, bool> Func, string Error);
}

internal sealed record ReAgentAuraEvaluation(bool Active, string Error, IReadOnlyCollection<ReAgentAuraDisplayRuntime> Displays);
