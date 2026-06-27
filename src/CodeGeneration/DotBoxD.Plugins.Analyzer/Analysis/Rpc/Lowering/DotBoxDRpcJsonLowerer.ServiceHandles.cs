using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Scoped service-handle lowering for <see cref="DotBoxDRpcJsonLowerer"/>. A host <c>control</c> hands out a
/// scoped handle via <c>Get(key)</c> whose method bindings have lowered arity <c>scopeArgs + methodArgs</c> —
/// the key captured by <c>Get(key)</c> becomes the leading host-call argument. Both the local-variable form
/// (<c>var h = control.Get(key); h.Method(...)</c>) and the inline form (<c>control.Get(key).Method(...)</c>)
/// capture the same key and lower identically. A handle local can also be copied to another local; the alias
/// remains a scoped handle over the original key.
/// </summary>
internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string? TryLowerServiceHandleInvocation(InvocationExpressionSyntax invocation)
    {
        // Confirm the call is a host binding (a pure attribute read) before resolving the scope handle, which
        // lowers the captured key — so that lowering and its side effects run only for a real scoped host call.
        if (invocation.Expression is not MemberAccessExpressionSyntax member ||
            _model.GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol method ||
            DotBoxDHostBindingExpressionLowerer.HostBinding(method, _model.Compilation) is not { } binding ||
            TryResolveScopeHandle(member.Expression) is not { } handleId)
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
            !TryGetServiceHandleAccessor(invocation, out var handleId, output))
        {
            if (TryResolveScopeHandleAlias(value) is not { } aliasHandleId)
            {
                return false;
            }

            handleId = aliasHandleId;
        }

        _serviceHandleLocals[localName] = handleId;
        output.Add(SetStatement(localName, handleId));
        return true;
    }

    private void LowerServiceHandleScopedBlock(BlockSyntax block, List<string> output)
    {
        var previous = new Dictionary<string, string>(_serviceHandleLocals, StringComparer.Ordinal);
        try
        {
            foreach (var inner in block.Statements)
            {
                LowerStatement(inner, output);
            }
        }
        finally
        {
            _serviceHandleLocals.Clear();
            foreach (var local in previous)
            {
                _serviceHandleLocals.Add(local.Key, local.Value);
            }
        }
    }

    /// <summary>
    /// Resolves the scope key for a scoped-handle method receiver and threads it as the leading host-call
    /// argument. Both the local-variable form (<c>var h = control.Get(key); h.Method(...)</c>, where
    /// <paramref name="receiver"/> is the handle local) and the inline form
    /// (<c>control.Get(key).Method(...)</c>, where <paramref name="receiver"/> is the <c>Get(key)</c> call)
    /// capture the same key, so the two lower identically. Parentheses are stripped so
    /// <c>(control.Get(key)).Method(...)</c> behaves the same. Returns <see langword="null"/> when the receiver
    /// is not a known scoped handle.
    /// </summary>
    private string? TryResolveScopeHandle(ExpressionSyntax receiver)
        => receiver switch
        {
            ParenthesizedExpressionSyntax parenthesized => TryResolveScopeHandle(parenthesized.Expression),
            CastExpressionSyntax cast => TryResolveScopeHandle(cast.Expression),
            IdentifierNameSyntax identifier
                when _serviceHandleLocals.TryGetValue(identifier.Identifier.ValueText, out var localHandleId)
                => localHandleId,
            MemberAccessExpressionSyntax member
                when IsThisOrBaseExpression(member.Expression) &&
                     _serviceHandleLocals.TryGetValue(member.Name.Identifier.ValueText, out var memberHandleId)
                => memberHandleId,
            InvocationExpressionSyntax accessor when TryGetServiceHandleAccessor(accessor, out var inlineHandleId)
                => inlineHandleId,
            _ => null
        };

    private string? TryResolveScopeHandleAlias(ExpressionSyntax value)
        => value switch
        {
            ParenthesizedExpressionSyntax parenthesized => TryResolveScopeHandleAlias(parenthesized.Expression),
            CastExpressionSyntax cast => TryResolveScopeHandleAlias(cast.Expression),
            IdentifierNameSyntax identifier
                when _serviceHandleLocals.TryGetValue(identifier.Identifier.ValueText, out var localHandleId)
                => localHandleId,
            MemberAccessExpressionSyntax member
                when IsThisOrBaseExpression(member.Expression) &&
                     _serviceHandleLocals.TryGetValue(member.Name.Identifier.ValueText, out var memberHandleId)
                => memberHandleId,
            _ => null
        };

    /// <summary>
    /// Recognizes a scoped-handle accessor call — <c>control.Get(key)</c> whose return type is a
    /// <c>[DotBoxDService]</c> handle — and lowers the captured key (its first argument). Shared by the local
    /// declaration path (<see cref="TryLowerServiceHandleLocal"/>) and the inline receiver path so both capture
    /// the scope key identically.
    /// </summary>
    private bool TryGetServiceHandleAccessor(
        InvocationExpressionSyntax invocation,
        out string handleId,
        List<string>? output = null)
    {
        if (_model.GetSymbolInfo(invocation, _cancellationToken).Symbol is not IMethodSymbol method ||
            !HasDotBoxDServiceAttribute(method.ReturnType) ||
            IsHookContextHostMarker(method))
        {
            handleId = string.Empty;
            return false;
        }

        if (invocation.ArgumentList.Arguments.Count != 1)
        {
            throw new NotSupportedException(
                $"Scoped service handle accessor '{method.Name}' must pass exactly one scope argument.");
        }

        var scopeArgument = invocation.ArgumentList.Arguments[0];
        if (scopeArgument.RefKindKeyword.ValueText.Length != 0)
        {
            throw new NotSupportedException(
                $"Scoped service handle accessor '{method.Name}' cannot use ref, in, or out arguments.");
        }

        handleId = output is null
            ? LowerExpression(scopeArgument.Expression)
            : LowerExpressionWithPrelude(scopeArgument.Expression, output);
        return true;
    }

    private static bool IsHookContextHostMarker(IMethodSymbol method)
        => string.Equals(method.Name, "Host", StringComparison.Ordinal) &&
           method.Arity == 1 &&
           method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
           "global::DotBoxD.Abstractions.HookContext";

    private static bool IsThisOrBaseExpression(ExpressionSyntax expression)
        => expression is ThisExpressionSyntax or BaseExpressionSyntax;
}
