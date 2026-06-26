using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncModelFactory
{
    private const string InvokeAsyncMethod = "InvokeAsync";

    public static InvokeAsyncResult? Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        try
        {
            return TryCreate(invocation, context.SemanticModel, cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            return new InvokeAsyncResult(
                null,
                null,
                new PluginKernelDiagnostic(
                    "InvokeAsync call could not be lowered: " + ex.Message,
                    PluginDiagnosticLocation.From(invocation.GetLocation())));
        }
    }

    private static InvokeAsyncResult? TryCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        string receiverType;
        string? serverAccessType;
        INamedTypeSymbol worldType;
        if (IsUnqualifiedInvokeAsyncExpression(invocation.Expression))
        {
            if (BindsToUserInvokeAsync(model, invocation, cancellationToken))
            {
                return null;
            }

            if (TryImplicitGeneratedFacadeSurface(
                    model,
                    invocation,
                    cancellationToken,
                    out receiverType,
                    out serverAccessType,
                    out worldType))
            {
                return CreateForSurface(
                    invocation,
                    model,
                    cancellationToken,
                    receiverType,
                    serverAccessType,
                    worldType);
            }

            if (IsDotBoxDInvokeAsync(model, invocation, cancellationToken))
            {
                throw new NotSupportedException(
                    "implicit InvokeAsync calls are not supported; call InvokeAsync on the generated plugin server receiver.");
            }

            return null;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax access ||
            !string.Equals(access.Name.Identifier.ValueText, InvokeAsyncMethod, StringComparison.Ordinal))
        {
            return null;
        }

        if (!TryServerInvocationSurface(
                model,
                access.Expression,
                cancellationToken,
                out receiverType,
                out serverAccessType,
                out worldType))
        {
            if (IsDotBoxDInvokeAsync(model, invocation, cancellationToken))
            {
                throw new NotSupportedException(
                    "receiver must be a generated plugin server facade or generated server interface, not the erased IPluginServer<TWorld> surface.");
            }

            return null;
        }

        return CreateForSurface(
            invocation,
            model,
            cancellationToken,
            receiverType,
            serverAccessType,
            worldType);
    }

    private static InvokeAsyncResult CreateForSurface(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        string receiverType,
        string? serverAccessType,
        INamedTypeSymbol worldType)
    {
        var shape = model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method
            ? InvokeAsyncCallShape.Create(invocation, method, model, cancellationToken)
            : null;
        shape ??= InvokeAsyncCallShape.Create(invocation, worldType, model, cancellationToken);
        if (shape is null)
        {
            throw new NotSupportedException(
                "lambda must use a supported block body and capture shape.");
        }

        InvokeAsyncGeneratedTypeValidator.Validate(shape, model.Compilation);

        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var lowerer = new DotBoxDRpcJsonLowerer(
            model,
            capabilities,
            effects,
            cancellationToken,
            serverContextParameterName: shape.WorldParameterName,
            serverContextType: shape.WorldType);
        var bodyJson = shape.LowerBody(lowerer, shape.Block);
        effects.Add(DotBoxDGenerationNames.Effects.Cpu);
        if (lowerer.Allocates)
        {
            effects.Add(DotBoxDGenerationNames.Effects.Alloc);
        }

        var id = HookChainIdentity.Compute(invocation);
        var pluginId = "$anon:" + id;
        var packageName = "InvokeAsync_" + id + DotBoxDGenerationNames.PluginPackageSuffix;
        var ns = HookChainIdentity.Namespace(invocation);
        var interception = Interception(
            invocation,
            model,
            receiverType,
            serverAccessType,
            ns,
            packageName,
            pluginId,
            shape,
            cancellationToken);
        if (interception is null)
        {
            throw new NotSupportedException("call site is not interceptable by the C# compiler.");
        }

        var package = EmitPackage(ns, packageName, pluginId, shape, bodyJson, effects, capabilities);
        return new InvokeAsyncResult(package, interception, null);
    }

    private static bool IsUnqualifiedInvokeAsyncExpression(ExpressionSyntax expression)
        => expression is IdentifierNameSyntax { Identifier.ValueText: InvokeAsyncMethod } or
            GenericNameSyntax { Identifier.ValueText: InvokeAsyncMethod };

    private static bool TryImplicitGeneratedFacadeSurface(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        receiverType = string.Empty;
        serverAccessType = null;
        worldType = null!;
        var containingType = model.GetEnclosingSymbol(invocation.SpanStart, cancellationToken)?.ContainingType;
        return containingType is not null &&
               InvokeAsyncReceiverResolver.TryResolveGeneratedFacadeType(
                   containingType,
                   out receiverType,
                   out serverAccessType,
                   out worldType);
    }

    private static bool IsDotBoxDInvokeAsync(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            !string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal))
        {
            return false;
        }

        return IsPluginServerType(method.ContainingType);
    }

    private static bool BindsToUserInvokeAsync(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            method.ContainingType.TypeKind == TypeKind.Error)
        {
            return false;
        }

        return !IsPluginServerType(method.ContainingType);
    }

    private static bool IsPluginServerType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var original = named.OriginalDefinition.ToDisplayString();
        return string.Equals(original, "DotBoxD.Abstractions.IPluginServer<TWorld>", StringComparison.Ordinal);
    }

    private static bool TryServerInvocationSurface(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
        => InvokeAsyncReceiverResolver.TryResolve(
            model,
            receiver,
            cancellationToken,
            out receiverType,
            out serverAccessType,
            out worldType);

}
