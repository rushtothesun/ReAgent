using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore2;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using ReAgent.State;

namespace ReAgent.ExileAuras;

internal sealed class ExileAuraConditionCompiler
{
    private delegate T ScriptFunc<T>(RuleState State, Func<string, ExileAuraDisplayRuntime> Display);

    private readonly Dictionary<string, CachedCondition> _conditions = new(StringComparer.Ordinal);

    public ExileAuraEvaluation Evaluate(ExileAuraRule rule, RuleState state)
    {
        var displays = ExileAuraDisplayValidator.CreateDisplayStates(rule, out var displayError);
        if (!string.IsNullOrWhiteSpace(displayError))
        {
            return new ExileAuraEvaluation(false, displayError, displays);
        }

        foreach (var display in displays)
        {
            display.Value = ExileAuraDisplayText.BuildDefaultText(display.Display, rule, state);
        }

        if (string.IsNullOrWhiteSpace(rule.ConditionSource))
        {
            return new ExileAuraEvaluation(false, "", displays);
        }

        var condition = GetCompiledCondition(rule);
        if (!string.IsNullOrWhiteSpace(condition.Error))
        {
            return new ExileAuraEvaluation(false, condition.Error, displays);
        }

        try
        {
            var lookup = displays
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            ExileAuraDisplayRuntime Display(string name)
            {
                if (lookup.TryGetValue(name, out var display))
                {
                    return display;
                }

                throw new KeyNotFoundException($"Display '{name}' was not found on this rule.");
            }

            return new ExileAuraEvaluation(condition.Func?.Invoke(state, Display) == true, "", displays);
        }
        catch (Exception ex)
        {
            return new ExileAuraEvaluation(false, $"Exception while evaluating: {ex.Message}", displays);
        }
    }

    public ExileAuraValidationResult Validate(ExileAuraRule rule)
    {
        var displayValidation = ExileAuraDisplayValidator.Validate(rule);
        if (!displayValidation.Success)
        {
            return displayValidation;
        }

        if (string.IsNullOrWhiteSpace(rule.ConditionSource))
        {
            return ExileAuraValidationResult.Ok();
        }

        var condition = GetCompiledCondition(rule);
        return string.IsNullOrWhiteSpace(condition.Error)
            ? ExileAuraValidationResult.Ok()
            : ExileAuraValidationResult.Fail(condition.Error);
    }

    private CompiledCondition GetCompiledCondition(ExileAuraRule rule)
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
                ScriptCompilerSupport.CreateAssemblyLoadContext("ExileAura"));
            return new CompiledCondition((state, display) => func(state, display), "");
        }
        catch (Exception ex)
        {
            return new CompiledCondition(null, $"Expression compilation failed: {ex.Message}");
        }
    }

    private sealed record CachedCondition(string Source, Lazy<CompiledCondition> Condition);
    private sealed record CompiledCondition(Func<RuleState, Func<string, ExileAuraDisplayRuntime>, bool> Func, string Error);
}

internal sealed record ExileAuraEvaluation(bool Active, string Error, IReadOnlyCollection<ExileAuraDisplayRuntime> Displays);
