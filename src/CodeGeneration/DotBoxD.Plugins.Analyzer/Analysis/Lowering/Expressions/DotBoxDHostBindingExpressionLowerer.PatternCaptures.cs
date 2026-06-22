using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private static DotBoxDExpressionModel? TryLowerPatternCaptureInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync) binding,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member ||
            Unwrap(member.Expression) is not IdentifierNameSyntax receiver ||
            !context.TryGetPatternCapture(receiver.Identifier.ValueText, out var capture))
        {
            return null;
        }

        if (method.IsStatic || !IsSubtypeMember(method, capture.Subtype))
        {
            throw new NotSupportedException(
                $"Pattern capture host call '{invocation}' must target an instance [HostBinding] on the captured subtype.");
        }

        var returnType = HostBindingManifestTag(
            DotBoxDTypeNameReader.UnwrapTaskLike(method.ReturnType),
            binding.BindingId,
            "return");

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != method.Parameters.Length)
        {
            throw new NotSupportedException(
                $"Host binding '{binding.BindingId}' call must pass {method.Parameters.Length} positional argument(s).");
        }

        var loweredSources = new List<string>(arguments.Count + 1) { capture.Key.Source };
        var allocates = capture.Key.Allocates || IsAllocatingTag(returnType);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon is not null ||
                !arguments[i].RefKindKeyword.IsKind(SyntaxKind.None) ||
                method.Parameters[i].RefKind != RefKind.None)
            {
                throw new NotSupportedException(
                    $"Host binding '{binding.BindingId}' arguments must be positional value arguments.");
            }

            var expected = HostBindingManifestTag(method.Parameters[i].Type, binding.BindingId, $"argument {i}");
            var lowered = lowerExpression(arguments[i].Expression);
            if (!string.Equals(lowered.Type, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Host binding '{binding.BindingId}' argument {i} must lower to {expected}.");
            }

            loweredSources.Add(lowered.Source);
            allocates |= lowered.Allocates;
        }

        AddBindingRequirements(context, binding.Capability, binding.Effects, binding.IsAsync);
        var source =
            $"new {TypeNames.GlobalCallExpression}({LiteralReader.StringLiteral(binding.BindingId)}, " +
            $"[{string.Join(", ", loweredSources)}], null, Span)";
        return new DotBoxDExpressionModel(source, returnType, allocates);
    }

    private static bool IsSubtypeMember(IMethodSymbol method, INamedTypeSymbol subtype)
    {
        for (var current = subtype; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(method.ContainingType, current))
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized.Expression;
        }

        return current;
    }

    private static void AddBindingRequirements(
        DotBoxDExpressionLoweringContext context,
        string? capability,
        IReadOnlyList<string> effects,
        bool isAsync)
    {
        if (capability is { Length: > 0 } requiredCapability)
        {
            context.Capabilities?.Add(requiredCapability);
        }

        if (isAsync || effects.Contains(DotBoxDGenerationNames.Effects.Concurrency))
        {
            context.Capabilities?.Add(DotBoxDGenerationNames.Capabilities.RuntimeAsync);
        }

        if (context.Effects is not { } effectSink)
        {
            return;
        }

        foreach (var effect in effects)
        {
            effectSink.Add(effect);
        }

        if (isAsync || effects.Contains(DotBoxDGenerationNames.Effects.Concurrency))
        {
            effectSink.Add(DotBoxDGenerationNames.Effects.Concurrency);
        }
    }
}
