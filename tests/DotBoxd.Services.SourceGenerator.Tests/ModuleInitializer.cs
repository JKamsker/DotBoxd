using System.Runtime.CompilerServices;
using DiffEngine;

namespace DotBoxd.Services.SourceGenerator.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Don't open a diff tool in CI / agent runs.
        DiffRunner.Disabled = true;

        VerifySourceGenerators.Initialize();
        VerifyDiffPlex.Initialize();
    }
}
