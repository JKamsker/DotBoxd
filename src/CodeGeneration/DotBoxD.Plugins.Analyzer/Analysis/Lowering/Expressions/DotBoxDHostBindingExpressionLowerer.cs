using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using DotBoxD.Shared.HostBindings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

/// <summary>
/// Lowers a host-service call the kernel reaches through <c>ctx.Host&lt;T&gt;()</c> or a
/// constructor-injected service field — e.g. <c>_world.GetHealth(e.MonsterId)</c> — into a sandbox
/// <c>CallExpression(bindingId, args)</c>. The called method must carry
/// <c>[HostBinding(bindingId, capability, effects)]</c> or the auto-binding
/// <c>[HostBinding(capability, effects)]</c>; the capability is collected so it lands in the
/// manifest's required capabilities and gates the install (the host registers a matching binding whose
/// <c>RequiredCapability</c> the policy must grant). Arguments lower in parameter order, with explicit
/// defaults filled in only for marshaller-eligible scalars; reordered named calls fail closed because this
/// expression IR cannot preserve call-site side-effect order with temporaries.
/// </summary>
internal static partial class DotBoxDHostBindingExpressionLowerer
{
    public static DotBoxDExpressionModel? TryLower(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (ResolveHostBindingInvocation(invocation, context) is not { } resolved)
        {
            return null;
        }

        var (method, binding) = resolved;
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

        var allocates = HostBindingMetadataRules.ReturnAllocatesManifestTag(returnType);
        var arguments = LowerHostBindingCallArguments(
            invocation,
            method,
            bindingId,
            context,
            lowerExpression);
        var loweredSources = new List<string>(arguments.Count);
        foreach (var argument in arguments)
        {
            loweredSources.Add(argument.Source);
            allocates |= argument.Allocates;
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
        if (ExplicitHostBinding(property, context.SemanticModel.Compilation) is not { } binding)
        {
            return null;
        }

        var returnType = HostBindingManifestTag(property.Type, binding.BindingId, "return");
        AddBindingRequirements(context, binding.Capability, binding.Effects, binding.IsAsync);
        var source =
            $"new {TypeNames.GlobalCallExpression}({LiteralReader.StringLiteral(binding.BindingId)}, " +
            "[], null, Span)";
        return new DotBoxDExpressionModel(source, returnType, HostBindingMetadataRules.ReturnAllocatesManifestTag(returnType));
    }

    private static bool IsAllocatingTag(string tag)
        => HostBindingMetadataRules.ReturnAllocatesManifestTag(tag);

    internal static (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync)? HostBinding(
        IMethodSymbol method,
        Compilation compilation)
        => ExplicitHostBinding(method, compilation) ?? TryAutoHostBinding(method, compilation);

    private static (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync)? ExplicitHostBinding(
        ISymbol symbol,
        Compilation compilation)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (!IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.HostBindingAttribute) ||
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
        IMethodSymbol method,
        Compilation compilation)
    {
        if (method.MethodKind != MethodKind.Ordinary ||
            method.IsStatic ||
            method.IsGenericMethod ||
            !HasDotBoxDServiceAttribute(method.ContainingType, compilation))
        {
            return null;
        }

        var returnType = DotBoxDTypeNameReader.UnwrapTaskLike(method.ReturnType);
        var binding = AutoHostBinding(method, ReturnAllocates(returnType), compilation);
        if (binding is null)
        {
            throw new NotSupportedException(
                $"Auto host binding '{HostBindingRoute(method.ContainingType, method)}' must declare [HostBinding] with explicit effects.");
        }

        return (
            HostBindingRoute(method.ContainingType, method),
            binding.Value.Capability,
            binding.Value.Effects,
            IsTaskLike(method.ReturnType));
    }

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type, Compilation compilation)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.RpcServiceAttribute))
            {
                return true;
            }
        }

        return false;
    }

    private static string HostBindingRoute(INamedTypeSymbol type, IMethodSymbol method)
    {
        var ns = type.ContainingNamespace.IsGlobalNamespace
            ? null
            : type.ContainingNamespace.ToDisplayString();
        return HostBindingMetadataRules.BindingId(ns, type.MetadataName, method.Name);
    }

    private static (string Capability, IReadOnlyList<string> Effects)? AutoHostBinding(
        IMethodSymbol method,
        bool returnAllocates,
        Compilation compilation)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.HostBindingAttribute) &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[0].Value is string capability &&
                !string.IsNullOrWhiteSpace(capability))
            {
                var effects = AutoHostBindingSandboxEffects(attribute.ConstructorArguments[1], returnAllocates, method);
                return (capability, effects);
            }
        }

        return null;
    }

    private static bool IsDotBoxDAttribute(AttributeData attribute, Compilation compilation, string metadataName)
    {
        if (string.Equals(metadataName, DotBoxDMetadataNames.RpcServiceAttribute, StringComparison.Ordinal))
        {
            return IsAnyDotBoxDAttribute(
                attribute,
                compilation,
                DotBoxDMetadataNames.RpcServiceAttribute,
                DotBoxDMetadataNames.DotBoxDServiceAttribute);
        }

        return IsAnyDotBoxDAttribute(attribute, compilation, metadataName);
    }

    private static bool IsAnyDotBoxDAttribute(
        AttributeData attribute,
        Compilation compilation,
        params string[] metadataNames)
    {
        foreach (var metadataName in metadataNames)
        {
            if (compilation.GetTypeByMetadataName(metadataName) is { } expected &&
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected))
            {
                return true;
            }
        }

        return false;
    }

    // Keep auto-binding Alloc classification in sync with runtime binding registration.
    private static bool ReturnAllocates(ITypeSymbol type)
        => !IsUnitTaskLike(type) &&
           HostBindingMetadataRules.ReturnAllocatesManifestTag(SandboxTypeSourceEmitter.ManifestTag(type));

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
