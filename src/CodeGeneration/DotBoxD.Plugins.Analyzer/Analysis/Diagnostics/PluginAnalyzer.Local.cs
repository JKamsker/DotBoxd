using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void ValidateLocalMember(
        SymbolAnalysisContext context,
        ISymbol member,
        IMethodSymbol method)
    {
        ValidateLocalMemberCore(context, member, method.IsStatic);
    }

    private static void ValidateLocalMember(
        SymbolAnalysisContext context,
        ISymbol member,
        IPropertySymbol property)
    {
        ValidateLocalMemberCore(context, member, property.IsStatic);
    }

    private static void ValidateLocalMemberCore(SymbolAnalysisContext context, ISymbol member, bool isStatic)
    {
        if (isStatic || !IsDeclaredPluginServerContext(context.Compilation, member.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PluginAnalyzerDiagnostics.LocalContextMemberRule,
                member.Locations.FirstOrDefault(),
                "[NativeOnly] is valid only on instance members of the declared generated plugin server context."));
        }
    }

    private static bool IsDeclaredPluginServerContext(Compilation compilation, INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        foreach (var symbol in compilation.GetSymbolsWithName(static _ => true, SymbolFilter.Type))
        {
            if (symbol is not INamedTypeSymbol server)
            {
                continue;
            }

            if (ServerDeclaresContext(server, type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ServerDeclaresContext(INamedTypeSymbol server, INamedTypeSymbol type)
    {
        foreach (var attribute in server.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.GeneratePluginServerAttribute,
                    StringComparison.Ordinal) &&
                attribute.NamedArguments.Any(argument =>
                    string.Equals(argument.Key, "Context", StringComparison.Ordinal) &&
                    argument.Value.Value is INamedTypeSymbol contextType &&
                    SymbolEqualityComparer.Default.Equals(contextType, type)))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportLocalUseIfInvalid(OperationAnalysisContext context, ISymbol target)
    {
        var model = context.Operation.SemanticModel;
        if (!HasAttribute(target, DotBoxDMetadataNames.NativeOnlyAttribute) ||
            model is null ||
            !IsLocalUseForbidden(context.Operation.Syntax, context.ContainingSymbol, model, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            PluginAnalyzerDiagnostics.LocalContextMemberRule,
            context.Operation.Syntax.GetLocation(),
            "[NativeOnly] context members run natively and cannot be used in lowered hook chains or server-extension bodies."));
    }

    private static bool IsLocalUseForbidden(
        SyntaxNode syntax,
        ISymbol? containingSymbol,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (containingSymbol is IMethodSymbol method &&
            HasAttribute(method, DotBoxDMetadataNames.ServerExtensionMethodAttribute))
        {
            return true;
        }

        foreach (var lambda in syntax.AncestorsAndSelf().OfType<LambdaExpressionSyntax>())
        {
            if (lambda.Parent is ArgumentSyntax argument &&
                argument.Parent is ArgumentListSyntax argumentList &&
                argumentList.Parent is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax member)
            {
                return member.Name.Identifier.ValueText is "Where" or "Select" or "Run" or "Register" &&
                    IsHookChainReceiver(member.Expression, model, cancellationToken);
            }
        }

        return false;
    }

    private static bool IsHookChainReceiver(
        ExpressionSyntax receiver,
        SemanticModel model,
        CancellationToken cancellationToken)
        => model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol receiverType &&
           IsHookChainType(receiverType);

    private static bool IsHookChainType(INamedTypeSymbol type)
        => type.ContainingNamespace.ToDisplayString() switch
        {
            "DotBoxD.Plugins.Runtime" => type.Name is
                "HookPipeline" or
                "SubscriptionPipeline" or
                "RemoteHookPipeline" or
                "RemoteSubscriptionPipeline",
            "DotBoxD.Plugins.Runtime.Hooks" => type.Name is "HookStage" or "RemoteHookStage",
            "DotBoxD.Plugins.Runtime.Subscriptions" => type.Name is "SubscriptionStage" or "RemoteSubscriptionStage",
            _ => false,
        };
}
