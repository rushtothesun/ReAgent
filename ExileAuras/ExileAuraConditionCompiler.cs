using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.Shared.Enums;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using ReAgent.State;

namespace ReAgent.ExileAuras;

internal sealed class ExileAuraConditionCompiler
{
    private delegate T ScriptFunc<T>(RuleState State, Func<string, ExileAuraDisplayRuntime> Display);

    private static readonly InteractiveAssemblyLoader Loader;
    private static readonly PortableExecutableReference MetadataReference;

    private readonly Dictionary<string, CachedCondition> _conditions = new(StringComparer.Ordinal);

    static ExileAuraConditionCompiler()
    {
        unsafe
        {
            Assembly.GetExecutingAssembly().TryGetRawMetadata(out var blob, out var length);
            var moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
            var assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
            MetadataReference = assemblyMetadata.GetReference();
            Loader = new InteractiveAssemblyLoader();
            Loader.RegisterDependency(typeof(ReAgent).Assembly);
        }
    }

    private static ScriptOptions ScriptOptions => ScriptOptions.Default
        .AddReferences(
            typeof(Vector2).Assembly,
            typeof(GameStat).Assembly,
            typeof(Core).Assembly,
            typeof(Keys).Assembly)
        .AddReferences(MetadataReference)
        .AddImports(
            "System.Collections.Generic", "System.Linq", "System.Numerics", "System.Windows.Forms", "System",
            "ReAgent", "ReAgent.State", "ReAgent.SideEffects", "ReAgent.ExileAuras",
            "ExileCore2", "ExileCore2.Shared", "ExileCore2.Shared.Enums",
            "ExileCore2.Shared.Helpers", "ExileCore2.PoEMemory.Components", "ExileCore2.PoEMemory.MemoryObjects",
            "ExileCore2.PoEMemory", "ExileCore2.PoEMemory.FilesInMemory",
            "GameOffsets2", "GameOffsets2.Native");

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
            var func = DelegateCompiler.CompileDelegate<ScriptFunc<bool>>(source, ScriptOptions, CreateAlc());
            return new CompiledCondition((state, display) => func(state, display), "");
        }
        catch (Exception ex)
        {
            return new CompiledCondition(null, $"Expression compilation failed: {ex.Message}");
        }
    }

    private static AssemblyLoadContext CreateAlc()
    {
        var assemblyLoadContext = new AssemblyLoadContext($"ExileAura{Guid.NewGuid():N}", true);
        assemblyLoadContext.Resolving += (_, name) => name.Name == "ReAgent" ? Assembly.GetExecutingAssembly() : null;
        return assemblyLoadContext;
    }

    private sealed record CachedCondition(string Source, Lazy<CompiledCondition> Condition);
    private sealed record CompiledCondition(Func<RuleState, Func<string, ExileAuraDisplayRuntime>, bool> Func, string Error);
}

internal sealed record ExileAuraEvaluation(bool Active, string Error, IReadOnlyCollection<ExileAuraDisplayRuntime> Displays);
