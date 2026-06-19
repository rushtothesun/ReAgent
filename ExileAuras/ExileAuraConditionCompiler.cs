using System;
using System.Collections.Generic;
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
    private delegate T ScriptFunc<T>(RuleState State);

    private static readonly InteractiveAssemblyLoader Loader;
    private static readonly PortableExecutableReference MetadataReference;

    private readonly Dictionary<string, Lazy<CompiledCondition>> _conditions = new(StringComparer.Ordinal);

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

    public (bool Active, string Error) Evaluate(ExileAuraRule rule, RuleState state)
    {
        if (string.IsNullOrWhiteSpace(rule.ConditionSource))
        {
            return (false, "");
        }

        var key = $"{rule.Id}:{rule.ConditionSource}";
        if (!_conditions.TryGetValue(key, out var lazy))
        {
            lazy = new Lazy<CompiledCondition>(() => Compile(rule.ConditionSource));
            _conditions[key] = lazy;
        }

        var condition = lazy.Value;
        if (!string.IsNullOrWhiteSpace(condition.Error))
        {
            return (false, condition.Error);
        }

        try
        {
            return (condition.Func?.Invoke(state) == true, "");
        }
        catch (Exception ex)
        {
            return (false, $"Exception while evaluating: {ex.Message}");
        }
    }

    private static CompiledCondition Compile(string source)
    {
        try
        {
            var func = DelegateCompiler.CompileDelegate<ScriptFunc<bool>>(source, ScriptOptions, CreateAlc());
            return new CompiledCondition(state => func(state), "");
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

    private sealed record CompiledCondition(Func<RuleState, bool> Func, string Error);
}
