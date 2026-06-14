namespace DotBoxd.Plugins.Analyzer;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Lowers a host-service call the kernel reaches through <c>ctx.Host&lt;T&gt;()</c> — e.g.
/// <c>ctx.Host&lt;IGameWorldAccess&gt;().GetHealth(e.MonsterId)</c> — into a sandbox
/// <c>CallExpression(bindingId, args)</c>. The called method must carry
/// <c>[HostBinding(bindingId, capability)]</c>; the capability is collected so it lands in the
/// manifest's required capabilities and gates the install (the host registers a matching binding whose
/// <c>RequiredCapability</c> the policy must grant). Arguments are positional and both argument and
/// return types must be supported scalars; anything else fails safe via <see cref="NotSupportedException"/>.
/// </summary>
internal static class DotBoxdHostBindingExpressionLowerer
{
    public static DotBoxdExpressionModel? TryLower(
        InvocationExpressionSyntax invocation,
        DotBoxdExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method ||
            HostBinding(method) is not { } binding)
        {
            return null;
        }

        var (bindingId, capability, effects) = binding;
        var returnType = DotBoxdTypeNameReader.SandboxTypeName(method.ReturnType);
        if (string.Equals(returnType, DotBoxdGenerationNames.ManifestTypes.Unsupported, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Host binding '{bindingId}' must return a supported scalar type.");
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != method.Parameters.Length)
        {
            throw new NotSupportedException(
                $"Host binding '{bindingId}' call must pass {method.Parameters.Length} positional argument(s).");
        }

        var loweredSources = new List<string>(arguments.Count);
        var allocates = string.Equals(returnType, DotBoxdGenerationNames.ManifestTypes.String, StringComparison.Ordinal);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon is not null)
            {
                throw new NotSupportedException(
                    $"Host binding '{bindingId}' arguments must be positional.");
            }

            var lowered = lowerExpression(arguments[i].Expression);
            var expected = DotBoxdTypeNameReader.SandboxTypeName(method.Parameters[i].Type);
            if (!string.Equals(lowered.Type, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Host binding '{bindingId}' argument {i} must lower to {expected}.");
            }

            loweredSources.Add(lowered.Source);
            allocates |= lowered.Allocates;
        }

        context.Capabilities?.Add(capability);
        if (context.Effects is { } effectSink)
        {
            foreach (var effect in effects)
            {
                effectSink.Add(effect);
            }
        }

        var source =
            $"new global::DotBoxd.Kernels.CallExpression({LiteralReader.StringLiteral(bindingId)}, " +
            $"[{string.Join(", ", loweredSources)}], null, Span)";
        return new DotBoxdExpressionModel(source, returnType, allocates);
    }

    internal static (string BindingId, string Capability, IReadOnlyList<string> Effects)? HostBinding(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxdGenerationNames.Metadata.HostBindingAttribute,
                    StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length != 3)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is string bindingId &&
                attribute.ConstructorArguments[1].Value is string capability &&
                !string.IsNullOrEmpty(bindingId) &&
                !string.IsNullOrEmpty(capability))
            {
                return (bindingId, capability, EffectNames(attribute.ConstructorArguments[2]));
            }
        }

        return null;
    }

    /// <summary>
    /// The single-bit flag names set in a <c>SandboxEffect</c> attribute argument (e.g. "Cpu",
    /// "HostStateRead"). Read from the enum's own members so the names match the manifest's effect
    /// tokens (parsed back via <c>Enum.TryParse&lt;SandboxEffect&gt;</c> at install).
    /// </summary>
    private static IReadOnlyList<string> EffectNames(TypedConstant effects)
    {
        if (effects.Value is null || effects.Type is not INamedTypeSymbol enumType)
        {
            return [];
        }

        var bits = Convert.ToInt64(effects.Value);
        var names = new List<string>();
        foreach (var member in enumType.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } field && field.ConstantValue is not null)
            {
                var memberBits = Convert.ToInt64(field.ConstantValue);
                if (memberBits != 0 && (bits & memberBits) == memberBits)
                {
                    names.Add(field.Name);
                }
            }
        }

        return names;
    }
}
