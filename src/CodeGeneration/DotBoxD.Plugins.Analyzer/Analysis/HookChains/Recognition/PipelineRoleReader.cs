using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// Analyzer-side mirror of <c>DotBoxD.Abstractions.PipelineStepRole</c>. The integer values are a wire
/// contract with the public enum (read out of the attribute's constructor argument) — never renumber.
/// </summary>
internal enum PipelineStepRole
{
    Seed = 0,
    Filter = 1,
    Projection = 2,
    Run = 3,
    RunLocal = 4,
    Register = 5,
    RegisterLocal = 6,
}

/// <summary>
/// Resolves the pipeline role of a fluent call and the transport of a receiver type from the
/// <c>[PipelineStep]</c> / <c>[PipelineSurface]</c> attributes the library (and consumers) place on their own
/// methods and types. This is the attribute-driven replacement for hardcoded <c>Where</c>/<c>Select</c>/
/// <c>Run</c>/<c>Register</c>/<c>On</c> method-name recognition; the framework marks its own pipeline surface
/// with these attributes so no method-name literal drives recognition.
/// </summary>
internal static class PipelineRoleReader
{
    /// <summary>The role declared by a <c>[PipelineStep]</c> on <paramref name="method"/>, or <c>null</c>.</summary>
    public static PipelineStepRole? RoleOf(IMethodSymbol? method, Compilation compilation)
    {
        if (method is null)
        {
            return null;
        }

        foreach (var attribute in method.OriginalDefinition.GetAttributes())
        {
            if (IsDotBoxDAttribute(attribute, compilation, DotBoxDGenerationNames.TypeNames.PipelineStepAttribute) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is int value &&
                value is >= (int)PipelineStepRole.Seed and <= (int)PipelineStepRole.RegisterLocal)
            {
                return (PipelineStepRole)value;
            }
        }

        return null;
    }

    /// <summary>The transport declared by a <c>[PipelineSurface]</c> on <paramref name="type"/> (or a base
    /// type), mapped to <see cref="HookChainReceiverKind"/>; <c>null</c> when the type is not a marked surface.</summary>
    public static HookChainReceiverKind? Transport(INamedTypeSymbol? type, Compilation compilation)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.OriginalDefinition.GetAttributes())
            {
                if (IsDotBoxDAttribute(attribute, compilation, DotBoxDGenerationNames.TypeNames.PipelineSurfaceAttribute) &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is int value)
                {
                    // PipelineTransport.Remote == 1, Local == 0.
                    return value == 1 ? HookChainReceiverKind.Remote : HookChainReceiverKind.Local;
                }
            }
        }

        return null;
    }

    private static bool IsDotBoxDAttribute(AttributeData attribute, Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) is { } expected &&
           SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected);
}
