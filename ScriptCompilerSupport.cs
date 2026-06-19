using System;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.Shared.Enums;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace ReAgent;

internal static class ScriptCompilerSupport
{
    public static readonly ScriptOptions ScriptOptions;

    static ScriptCompilerSupport()
    {
        unsafe
        {
            Assembly.GetExecutingAssembly().TryGetRawMetadata(out var blob, out var length);
            var moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
            var assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
            var metadataReference = assemblyMetadata.GetReference();

            ScriptOptions = ScriptOptions.Default
                .AddReferences(
                    typeof(Vector2).Assembly,
                    typeof(GameStat).Assembly,
                    typeof(Core).Assembly,
                    typeof(Keys).Assembly)
                .AddReferences(metadataReference)
                .AddImports(
                    "System.Collections.Generic", "System.Linq", "System.Numerics", "System.Windows.Forms", "System",
                    "ReAgent", "ReAgent.State", "ReAgent.SideEffects", "ReAgent.ExileAuras",
                    "ExileCore2", "ExileCore2.Shared", "ExileCore2.Shared.Enums",
                    "ExileCore2.Shared.Helpers", "ExileCore2.PoEMemory.Components", "ExileCore2.PoEMemory.MemoryObjects",
                    "ExileCore2.PoEMemory", "ExileCore2.PoEMemory.FilesInMemory",
                    "GameOffsets2", "GameOffsets2.Native");
        }
    }

    public static AssemblyLoadContext CreateAssemblyLoadContext(string namePrefix)
    {
        var assemblyLoadContext = new AssemblyLoadContext($"{namePrefix}{Guid.NewGuid():N}", true);
        assemblyLoadContext.Resolving += (_, name) => name.Name == "ReAgent" ? Assembly.GetExecutingAssembly() : null;
        return assemblyLoadContext;
    }
}
