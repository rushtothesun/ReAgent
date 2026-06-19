using System;
using System.Collections.Generic;
using System.Threading;
using ExileCore2;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using ReAgent.State;

namespace ReAgent;

internal sealed class GroupConditionCompiler
{
    private delegate T ScriptFunc<T>(RuleState State);

    private readonly Dictionary<string, Lazy<CompiledCondition>> _conditions = new(StringComparer.Ordinal);

    public GroupConditionEvaluation Evaluate(string source, RuleState state)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new GroupConditionEvaluation(true, "");
        }

        var condition = GetCompiledCondition(source);
        if (!string.IsNullOrWhiteSpace(condition.Error))
        {
            return new GroupConditionEvaluation(false, condition.Error);
        }

        try
        {
            return new GroupConditionEvaluation(condition.Func?.Invoke(state) == true, "");
        }
        catch (Exception ex)
        {
            return new GroupConditionEvaluation(false, $"Exception while evaluating: {ex.Message}");
        }
    }

    private CompiledCondition GetCompiledCondition(string source)
    {
        if (!_conditions.TryGetValue(source, out var lazy))
        {
            lazy = new Lazy<CompiledCondition>(() => Compile(source), LazyThreadSafetyMode.None);
            _conditions[source] = lazy;
        }

        return lazy.Value;
    }

    private static CompiledCondition Compile(string source)
    {
        try
        {
            var func = DelegateCompiler.CompileDelegate<ScriptFunc<bool>>(
                source,
                ScriptCompilerSupport.ScriptOptions,
                ScriptCompilerSupport.CreateAssemblyLoadContext("GroupCondition"));
            return new CompiledCondition(func, "");
        }
        catch (Exception ex)
        {
            return new CompiledCondition(null, $"Expression compilation failed: {ex.Message}");
        }
    }

    private sealed record CompiledCondition(ScriptFunc<bool> Func, string Error);
}

internal sealed record GroupConditionEvaluation(bool Active, string Error);
