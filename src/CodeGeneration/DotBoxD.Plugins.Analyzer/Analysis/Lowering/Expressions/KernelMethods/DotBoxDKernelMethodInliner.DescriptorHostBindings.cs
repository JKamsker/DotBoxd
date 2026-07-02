using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static DescriptorBindingRequirements? HostBindingRequirements(
        INamedTypeSymbol contextType,
        Compilation compilation,
        string bindingId)
    {
        foreach (var iface in ContextWorldInterfaces(contextType, compilation))
        {
            if (WorldHostBindingRequirements(iface, compilation, bindingId) is { } requirements)
            {
                return requirements;
            }
        }

        return null;
    }

    private static DescriptorBindingRequirements? WorldHostBindingRequirements(
        INamedTypeSymbol namedWorld,
        Compilation compilation,
        string bindingId)
    {
        foreach (var method in WorldMethods(namedWorld))
        {
            if (TryHostBinding(method, compilation) is { } binding &&
                string.Equals(binding.BindingId, bindingId, StringComparison.Ordinal))
            {
                return new DescriptorBindingRequirements(
                    binding.Capability,
                    binding.Effects,
                    binding.IsAsync,
                    HostReturnShape(method.ReturnType, bindingId),
                    method.Parameters
                        .Select((parameter, index) => HostParameterShape(parameter.Type, bindingId, index))
                        .ToArray());
            }
        }

        return null;
    }

    private static (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync)? TryHostBinding(
        IMethodSymbol method,
        Compilation compilation)
    {
        try
        {
            return DotBoxDHostBindingExpressionLowerer.HostBinding(method, compilation);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static IEnumerable<INamedTypeSymbol> ContextWorldInterfaces(
        INamedTypeSymbol contextType,
        Compilation compilation)
    {
        INamedTypeSymbol? matchedServer = null;
        foreach (var serverType in TypesInNamespace(contextType.ContainingAssembly.GlobalNamespace))
        {
            if (!GeneratedContextMatches(serverType, contextType, compilation))
            {
                continue;
            }

            if (matchedServer is not null)
            {
                yield break;
            }

            matchedServer = serverType;
        }

        if (matchedServer is null)
        {
            yield break;
        }

        foreach (var iface in matchedServer.AllInterfaces)
        {
            if (HasAttribute(iface, DotBoxDMetadataNames.RpcServiceAttribute, compilation))
            {
                yield return iface;
            }
        }
    }

    private static IEnumerable<IMethodSymbol> WorldMethods(INamedTypeSymbol worldType)
    {
        foreach (var method in worldType.GetMembers().OfType<IMethodSymbol>())
        {
            yield return method;
        }

        foreach (var inherited in worldType.AllInterfaces)
        {
            foreach (var method in inherited.GetMembers().OfType<IMethodSymbol>())
            {
                yield return method;
            }
        }
    }

    private static void AddBindingRequirements(
        ISet<string> capabilities,
        ISet<string> effects,
        DescriptorBindingRequirements requirements)
    {
        if (requirements.Capability is { Length: > 0 } capability)
        {
            capabilities.Add(capability);
        }

        var requiresAsync = requirements.IsAsync ||
            requirements.Effects.Contains(DotBoxDGenerationNames.Effects.Concurrency);
        if (requiresAsync)
        {
            capabilities.Add(DotBoxDGenerationNames.Capabilities.RuntimeAsync);
            effects.Add(DotBoxDGenerationNames.Effects.Concurrency);
        }

        foreach (var effect in requirements.Effects)
        {
            effects.Add(effect);
        }
    }

    private sealed record DescriptorBindingRequirements(
        string? Capability,
        IReadOnlyList<string> Effects,
        bool IsAsync,
        DescriptorShape ReturnShape,
        IReadOnlyList<DescriptorShape> ParameterShapes);
}
