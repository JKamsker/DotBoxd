using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Scoped service-handle lowering for <see cref="DotBoxDRpcJsonLowerer"/>. A host <c>control</c> hands out a
/// scoped handle via <c>Get(key)</c> whose method bindings have lowered arity <c>scopeArgs + methodArgs</c> —
/// the key captured by <c>Get(key)</c> becomes the leading host-call argument. Both the local-variable form
/// (<c>var h = control.Get(key); h.Method(...)</c>) and the inline form (<c>control.Get(key).Method(...)</c>)
/// capture the same key and lower identically.
/// </summary>
internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string? TryLowerServiceHandleInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member ||
            TryResolveScopeHandle(member.Expression) is not { } handleId ||
            _model.GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol method ||
            DotBoxDHostBindingExpressionLowerer.HostBinding(method) is not { } binding)
        {
            return null;
        }

        AddBindingMetadata(binding);
        var loweredArgs = LowerArgumentsInParameterOrder(
            invocation.ArgumentList.Arguments,
            method.Parameters,
            $"Host binding '{binding.BindingId}'");
        var args = new string[loweredArgs.Length + 1];
        args[0] = handleId;
        loweredArgs.CopyTo(args, 1);

        return Call(binding.BindingId, null, args);
    }

    private bool TryLowerServiceHandleLocal(string localName, ExpressionSyntax value, List<string> output)
    {
        if (value is not InvocationExpressionSyntax invocation ||
            !TryGetServiceHandleAccessor(invocation, out var handleId))
        {
            return false;
        }

        _serviceHandleLocals[localName] = handleId;
        output.Add(SetStatement(localName, handleId));
        return true;
    }

    /// <summary>
    /// Resolves the scope key for a scoped-handle method receiver and threads it as the leading host-call
    /// argument. Both the local-variable form (<c>var h = control.Get(key); h.Method(...)</c>, where
    /// <paramref name="receiver"/> is the handle local) and the inline form
    /// (<c>control.Get(key).Method(...)</c>, where <paramref name="receiver"/> is the <c>Get(key)</c> call)
    /// capture the same key, so the two lower identically. Returns <see langword="null"/> when the receiver is
    /// not a known scoped handle.
    /// </summary>
    private string? TryResolveScopeHandle(ExpressionSyntax receiver)
        => receiver switch
        {
            IdentifierNameSyntax identifier
                when _serviceHandleLocals.TryGetValue(identifier.Identifier.ValueText, out var localHandleId)
                => localHandleId,
            InvocationExpressionSyntax accessor when TryGetServiceHandleAccessor(accessor, out var inlineHandleId)
                => inlineHandleId,
            _ => null
        };

    /// <summary>
    /// Recognizes a scoped-handle accessor call — <c>control.Get(key)</c> whose return type is a
    /// <c>[DotBoxDService]</c> handle — and lowers the captured key (its first argument). Shared by the local
    /// declaration path (<see cref="TryLowerServiceHandleLocal"/>) and the inline receiver path so both capture
    /// the scope key identically.
    /// </summary>
    private bool TryGetServiceHandleAccessor(InvocationExpressionSyntax invocation, out string handleId)
    {
        if (_model.GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol method ||
            !HasDotBoxDServiceAttribute(method.ReturnType) ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            handleId = string.Empty;
            return false;
        }

        handleId = LowerExpression(invocation.ArgumentList.Arguments[0].Expression);
        return true;
    }
}
