using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private static HostBindingInvocation? ResolveHostBindingInvocation(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is IMethodSymbol method &&
            HostBinding(method, context.SemanticModel.Compilation) is { } binding)
        {
            return new HostBindingInvocation(method, binding);
        }

        return TryResolveGeneratedWorldBinding(invocation, context);
    }

    private static HostBindingInvocation? TryResolveGeneratedWorldBinding(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.ContextWorldType is not INamedTypeSymbol worldType ||
            invocation.Expression is not MemberAccessExpressionSyntax access ||
            !IsGeneratedWorldReceiver(access.Expression))
        {
            return null;
        }

        var candidates = WorldMethods(worldType, access.Name.Identifier.ValueText)
            .Where(method => CanBindHostBindingArguments(invocation.ArgumentList.Arguments, method.Parameters))
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        if (candidates.Length != 1)
        {
            throw new NotSupportedException(
                $"Generated context World call '{access.Name.Identifier.ValueText}' must resolve to one non-overloaded host service method.");
        }

        return HostBinding(candidates[0], context.SemanticModel.Compilation) is { } binding
            ? new HostBindingInvocation(candidates[0], binding)
            : null;
    }

    private static bool IsGeneratedWorldReceiver(ExpressionSyntax expression)
        => expression is IdentifierNameSyntax { Identifier.ValueText: "World" } ||
           expression is MemberAccessExpressionSyntax
           {
               Expression: ThisExpressionSyntax,
               Name.Identifier.ValueText: "World"
           };

    private static IEnumerable<IMethodSymbol> WorldMethods(INamedTypeSymbol worldType, string name)
    {
        foreach (var method in Methods(worldType, name))
        {
            yield return method;
        }

        foreach (var inherited in worldType.AllInterfaces)
        {
            foreach (var method in Methods(inherited, name))
            {
                yield return method;
            }
        }
    }

    private static IEnumerable<IMethodSymbol> Methods(INamedTypeSymbol type, string name)
        => type.GetMembers(name).OfType<IMethodSymbol>().Where(static method =>
            method.MethodKind == MethodKind.Ordinary &&
            !method.IsStatic &&
            !method.IsGenericMethod);

    private readonly record struct HostBindingInvocation(
        IMethodSymbol Method,
        (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync) Binding);
}
