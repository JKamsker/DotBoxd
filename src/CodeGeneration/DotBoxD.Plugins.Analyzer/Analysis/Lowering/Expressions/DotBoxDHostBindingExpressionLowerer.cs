using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

/// <summary>
/// Lowers a host-service call the kernel reaches through <c>ctx.Host&lt;T&gt;()</c> or a
/// constructor-injected service field — e.g. <c>_world.GetHealth(e.MonsterId)</c> — into a sandbox
/// <c>CallExpression(bindingId, args)</c>. The called method must carry
/// <c>[HostBinding(bindingId, capability)]</c>; the capability is collected so it lands in the
/// manifest's required capabilities and gates the install (the host registers a matching binding whose
/// <c>RequiredCapability</c> the policy must grant). Arguments are positional and both argument and
/// return types must be supported scalars; anything else fails safe via <see cref="NotSupportedException"/>.
/// </summary>
internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private const string HostCapabilityAttribute = "DotBoxD.Abstractions.HostCapabilityAttribute";

    public static DotBoxDExpressionModel? TryLower(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method ||
            HostBinding(method) is not { } binding)
        {
            return null;
        }

        var (bindingId, capability, effects, isAsync) = binding;
        if (TryLowerPatternCaptureInvocation(
                invocation,
                method,
                binding,
                context,
                lowerExpression) is { } patternCaptureCall)
        {
            return patternCaptureCall;
        }

        if (!method.IsStatic &&
            PolymorphicHandleMetadataReader.TryResolve(method.ContainingType, out _))
        {
            throw new NotSupportedException(
                $"Polymorphic handle binding '{bindingId}' must be called on a pattern-captured subtype.");
        }

        // Host bindings use the same non-nullable marshaller shapes the runtime binding factory accepts.
        var returnType = HostBindingReturnTag(method.ReturnType, bindingId);

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != method.Parameters.Length)
        {
            throw new NotSupportedException(
                $"Host binding '{bindingId}' call must pass {method.Parameters.Length} positional argument(s).");
        }

        var loweredSources = new List<string>(arguments.Count);
        var allocates = IsAllocatingTag(returnType);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon is not null ||
                !arguments[i].RefKindKeyword.IsKind(SyntaxKind.None) ||
                method.Parameters[i].RefKind != RefKind.None)
            {
                throw new NotSupportedException(
                    $"Host binding '{bindingId}' arguments must be positional value arguments.");
            }

            var expected = HostBindingManifestTag(method.Parameters[i].Type, bindingId, $"argument {i}");
            var lowered = lowerExpression(arguments[i].Expression);
            if (!string.Equals(lowered.Type, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Host binding '{bindingId}' argument {i} must lower to {expected}.");
            }

            loweredSources.Add(lowered.Source);
            allocates |= lowered.Allocates;
        }

        AddBindingRequirements(context, capability, effects, isAsync);

        var source =
            $"new {TypeNames.GlobalCallExpression}({LiteralReader.StringLiteral(bindingId)}, " +
            $"[{string.Join(", ", loweredSources)}], null, Span)";
        return new DotBoxDExpressionModel(source, returnType, allocates);
    }

    public static DotBoxDExpressionModel? TryLowerProperty(
        IPropertySymbol property,
        DotBoxDExpressionLoweringContext context)
    {
        if (ExplicitHostBinding(property) is not { } binding)
        {
            return null;
        }

        var returnType = HostBindingManifestTag(property.Type, binding.BindingId, "return");
        AddBindingRequirements(context, binding.Capability, binding.Effects, binding.IsAsync);
        var source =
            $"new {TypeNames.GlobalCallExpression}({LiteralReader.StringLiteral(binding.BindingId)}, " +
            "[], null, Span)";
        return new DotBoxDExpressionModel(source, returnType, IsAllocatingTag(returnType));
    }

    // Must match HostServiceBindingFactory.ReturnAllocates.
    private static bool IsAllocatingTag(string tag)
        => string.Equals(tag, ManifestTypes.String, StringComparison.Ordinal) ||
           string.Equals(tag, ManifestTypes.Guid, StringComparison.Ordinal) ||
           string.Equals(tag, ManifestTypes.List, StringComparison.Ordinal) ||
           string.Equals(tag, ManifestTypes.Map, StringComparison.Ordinal) ||
           string.Equals(tag, ManifestTypes.Record, StringComparison.Ordinal);

    internal static (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync)? HostBinding(
        IMethodSymbol method)
    {
        return ExplicitHostBinding(method) ?? TryAutoHostBinding(method);
    }

    private static (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync)? ExplicitHostBinding(
        ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.HostBindingAttribute,
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
                return (bindingId, capability, EffectNames(attribute.ConstructorArguments[2]), IsAsync(attribute));
            }
        }

        return null;
    }

    private static (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync)? TryAutoHostBinding(
        IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Ordinary ||
            method.IsStatic ||
            method.IsGenericMethod ||
            !HasDotBoxDServiceAttribute(method.ContainingType))
        {
            return null;
        }

        var capability = HostCapability(method);
        return (
            HostBindingRoute(method.ContainingType, method),
            capability,
            AutoEffectNames(method, capability),
            IsTaskLike(method.ReturnType));
    }

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.DotBoxDServiceAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string HostBindingRoute(INamedTypeSymbol type, IMethodSymbol method)
    {
        var ns = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString() + ".";
        return "host." + ns + type.MetadataName + "." + method.Name;
    }

    private static string? HostCapability(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), HostCapabilityAttribute, StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string capability &&
                !string.IsNullOrWhiteSpace(capability))
            {
                return capability;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> AutoEffectNames(IMethodSymbol method, string? capability)
    {
        var effects = new List<string> { DotBoxDGenerationNames.Effects.Cpu };
        var returnType = DotBoxDTypeNameReader.UnwrapTaskLike(method.ReturnType);
        if (ReturnAllocates(returnType))
        {
            effects.Add(DotBoxDGenerationNames.Effects.Alloc);
        }

        effects.Add(IsWriteMethod(method, capability)
            ? DotBoxDGenerationNames.Effects.HostStateWrite
            : DotBoxDGenerationNames.Effects.HostStateRead);
        return effects;
    }

    private static bool IsWriteMethod(IMethodSymbol method, string? capability)
        => capability?.Contains(".write.", StringComparison.Ordinal) == true ||
           method.Name.StartsWith("Kill", StringComparison.Ordinal) ||
           method.Name.StartsWith("Set", StringComparison.Ordinal) ||
           method.Name.StartsWith("Update", StringComparison.Ordinal) ||
           method.Name.StartsWith("Delete", StringComparison.Ordinal) ||
           method.Name.StartsWith("Add", StringComparison.Ordinal) ||
           method.Name.StartsWith("Remove", StringComparison.Ordinal) ||
           method.Name.StartsWith("Move", StringComparison.Ordinal) ||
           method.Name.StartsWith("Teleport", StringComparison.Ordinal);

    // Keep auto-binding Alloc classification in sync with runtime binding registration.
    private static bool ReturnAllocates(ITypeSymbol type)
        => !IsUnitTaskLike(type) && IsAllocatingTag(SandboxTypeSourceEmitter.ManifestTag(type));

    private static bool IsUnitTaskLike(ITypeSymbol type)
        => type is INamedTypeSymbol
        {
            IsGenericType: false,
            Name: "Task" or "ValueTask",
            ContainingNamespace: { } ns
        } &&
        string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal);

    private static bool IsTaskLike(ITypeSymbol type)
        => type is INamedTypeSymbol
        {
            Name: "Task" or "ValueTask",
            ContainingNamespace: { } ns
        } &&
        string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal);

    private static bool IsAsync(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.Key, "IsAsync", StringComparison.Ordinal) &&
                argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return false;
    }

    /// <summary>Single-bit <c>SandboxEffect</c> flag names used as manifest effect tokens.</summary>
    private static IReadOnlyList<string> EffectNames(TypedConstant effects)
    {
        if (effects.Value is null || effects.Type is not INamedTypeSymbol enumType)
        {
            return [];
        }

        var bits = Convert.ToInt64(effects.Value, System.Globalization.CultureInfo.InvariantCulture);
        var names = new List<string>();
        foreach (var member in enumType.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } field && field.ConstantValue is not null)
            {
                var memberBits = Convert.ToInt64(field.ConstantValue, System.Globalization.CultureInfo.InvariantCulture);
                if (memberBits != 0 && (bits & memberBits) == memberBits)
                {
                    names.Add(field.Name);
                }
            }
        }

        return names;
    }

}
